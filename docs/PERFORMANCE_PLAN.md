# Performance at 10 years × 100 employees — implementation plan

Status: planned 2026-10-18. Target: an organization with 100 members using the app for 10 years
(≈ 290k time entries, 40k absence days, 220k core-hours checks, tens of thousands of edit-history
rows/requests/notifications) still answers interactive calls in p95 < 300 ms and exports in < 2 s.

The data volume is small for Postgres. The problem is code that **recomputes the whole history per
request** — cost grows linearly with years of use, and some of it is polled every 60 s.

Decisions (2026-10-18): k6 for load tests; balance checkpoints filled lazily on read + pre-warmed on the
1st; no new retention limits; vacation carry-over statutory by default (expiry 31.03., configurable),
optional carry-over cap at org level only (no group overrides).

Work order: Phase 0 → 1 → 2 → 2b → 3. Each phase: implement → review + tests (separate agents) →
fix → full test run (`dotnet test`, frontend `npm run check` + `npm test` where touched).

---

## Phase 0 — measurement harness and baseline

### 0.1 History generator (`backend/src/Data/PerfSeeder.cs`)
- `PerfSeeder.SeedAsync(NpgsqlConnection conn, PerfSeedOptions o)` — `Employees` (100), `Years` (10),
  `Seed` (deterministic `Random`), `OrgSlug` (`perf-<employees>-<years>`), `EndDate` (default today, org-local).
- Writes with **Npgsql binary COPY** (`BeginBinaryImport`) for TimeEntries, AbsenceDays, TimeEntryChanges,
  CoreHoursChecks, TerminalPunches, Notifications, OrgRequests; users/org/memberships/groups/schedules/holidays via
  plain inserts. Must run as a role that bypasses RLS (superuser connection in tests; `app.bypass_rls = on` otherwise).
- Realistic shape per member: joins at a staggered date inside the period (≈ 20 % join later); Mon–Fri
  schedule 8 h (≈ 15 % part-time 6 h; one schedule change every ~3 years); per working day 1–2 entries
  (start 07:00–09:30 ± jitter, 7–9.5 h, pause 30/45 per pause rules, a lunch split on ~30 % of days);
  ~28 vacation days/year (blocks of 1–10 days, ~10 % half days), ~8 sick days/year, ~1 % failure entries
  (most corrected); German holidays as recurring org holidays; a 3-level group tree (2 depts × 3 teams × 2 sub-teams);
  ~5 % of entries edited (TimeEntryChanges rows); one CoreHoursCheck per member and working day (~3 % violations);
  terminal punches for ~50 % of members (last 400 days only, like retention); 1 request/member/month (mostly decided);
  notifications ~2/member/week (read except last 2 weeks). 2 open running timers.
- CLI: `dotnet TimeTracking.Api.dll perf-seed --employees 100 --years 10 [--seed 1] [--org-slug …]`, wired like
  `generate-vapid-keys` in `Program.cs`. Refuses to run unless `ASPNETCORE_ENVIRONMENT` is Development/Testing or
  `--force` is given, and refuses if the slug exists. Uses `ConnectionStrings:DefaultConnection` (Postgres only).
  Creates one owner `perf-owner@<slug>.test`, admins `perf-admin{1..5}@…`, members `perf-user{n}@…`, password `Password123`.

### 0.2 Scaling tests (`backend/tests/TimeTracking.Api.Tests/Perf/`)
- `[Trait("Category", "Perf")]`, `[Collection(PostgresCollection.Name)]`; excluded from the default run by a
  `--filter "Category!=Perf"` in CI and documented: `dotnet test --filter Category=Perf`.
- Shared `Infrastructure/QueryCounter.cs` (`DbCommandInterceptor` counting reader + non-query + scalar commands,
  per-async-flow via `AsyncLocal` scope) — replaces the three copy-pasted `CountingInterceptor`s.
- `ScalingTests`: for history sizes (years 1, 2, 5, 10 at 100 employees; employees 25/50/100/200 at 5 years),
  seed one org each (once per size, reused), then call **over HTTP as the `tt_app` role** (RLS on) with a member,
  an admin and the owner: balance, team-overview, inbox calls (`TimeTracking/failures`, `organizations/notifications`,
  `user-notifications`, `my-requests`, notifications), `TimeTracking` week, member entries week, compliance month,
  absences year, org detail, accounting summary month, payroll CSV month, accounting vacation year, daily CSV month,
  monthly PDF, timer start/stop, manual entry create/delete. 3 warm-ups, 15 measured calls → p50/p95, SQL command count.
- Output: `TestResults/perf/scaling-<timestamp>.md` (+ `.csv`): one table per endpoint, columns per size, and
  the growth ratio t(10y)/t(1y). For the 5 slowest calls at 10 y, `EXPLAIN (ANALYZE, BUFFERS)` of their SQL
  (captured from the counter) appended to the report.
- Assertions only in Phase 3 (Phase 0 just records the baseline). Baseline report copied to `docs/perf/baseline.md`.

### 0.3 k6 load test (`loadtest/`)
- `loadtest/README.md`, `loadtest/lib/auth.js` (login once per VU user, reuse token; respects the 10 logins/min
  limit by logging in during `setup()` for all users with pauses, or using the Development 200/min limit),
  `loadtest/scenarios.js` with scenarios (k6 `scenarios` executors):
  - `morning_rush`: 100 users start a timer within 15 min (ramping-arrival-rate), 50 of them via `POST terminal/punch`
    with an API key + card created in setup.
  - `polling`: 100 users × inbox (5 calls) every 60 s; 10 admins × team-overview every 60 s; balance on each "page load".
  - `browsing`: users open dashboard/time/history (the call sets the frontend makes, see Phase-0 inventory in the plan).
  - `month_end`: 2 accountants load summary, payroll CSV, vacation overview, monthly PDFs for 10 members.
- Thresholds: `http_req_duration{kind:interactive}` p95 < 300 ms, `{kind:export}` p95 < 2000 ms, `http_req_failed` < 0.1 %.
- Run: `docker compose -f docker-compose.production.yml up -d` (+ `perf-seed` via `run --rm backend perf-seed …`),
  then `docker run --rm -i --network host grafana/k6 run - < loadtest/scenarios.js` (`BASE_URL`, `ORG_SLUG` env).
  k6 is not installed locally; the Docker image is the documented path.

---

## Phase 1 — quick wins (no new tables)

1. **`EntryTimes.OverlapsAsync`** (`Services/Common/EntryTimes.cs:52`): entries are ≤ 24 h (`EntryTimes.Validate`), so
   `(e.StartTime < endUtc && e.StartTime > startUtc.AddHours(-24) && e.EndTime > startUtc) || (e.IsRunning && e.StartTime < endUtc)`.
   Check imported/legacy entries cannot exceed 24 h (validation on every path incl. import + terminal failure close); if any path
   allows longer, use that path's max.
2. **Team overview** (`OrganizationService.GetTeamOverviewAsync` ≈ :623–686): stop loading all entries. Presence from running
   entries only; failure counts via `GROUP BY UserId` in SQL; balances from `BalancesAsync` (made cheap in Phase 2 —
   in Phase 1 keep the call but give it a projection-only query; do not change its result).
3. **`CoreHours.OpenViolationsAsync`** (`Services/Common/CoreHours.cs:138–170` and the `:395` overload): index covered
   entries by local day once (`ILookup`), evaluate each violation against that day's entries only → O(violations + entries).
4. **Skip core hours for history**: `BuildWithRunningTotalAsync` / payroll `HoursAsync` evaluate `OpenViolationsAsync` only
   for the display range, not from the first entry (the flag is discarded for earlier days anyway). Add a parameter to
   `BuildAsync` (`coreHoursFrom`) rather than duplicating the method.
5. **`BuildRows` summaries**: an `includeSummaries` flag; balance-only callers (`BalancesAsync`, balance endpoint's
   history part, payroll history part) skip the `entrySummary` string building.
6. **Indexes** (one migration `PerformanceIndexes`):
   - `TimeEntries (UserId) WHERE "IsFailure"` (partial) — `GetFailuresAsync`, failure counts.
   - `OrgRequests (OrganizationId, Status)` and `OrgRequests (OrganizationId, CreatedAt)` — pending lists, request lists.
   - `TimeEntryChanges (UserId)` — `me/export`.
   - `Notifications (IsRead, CreatedAt)` — retention delete.
   - `RefreshTokens (ExpiresAt)`, `TerminalPunches (ReceivedAt)` — retention deletes.
   Verify with `EXPLAIN` in the Phase-0 harness that each is used.
7. **Retention** (`RefreshTokenCleanupService`): `ExecuteDeleteAsync` instead of load + `RemoveRange` (these tables have no
   SaveChanges hooks; check none of them are in `ClosedPeriods.Collect`/`TimeEntryHistory`). InMemory supports `ExecuteDelete`
   since EF 7? — it does **not**; keep a fallback branch for `Database.IsInMemory()`.
8. **Import preview** (`TimeEntryExportService` ≈ :259): load existing entries only within the file's min/max date ± 1 day.
9. **Payroll N+1** (`PayrollExportService.HoursAsync`): replace the per-member loop with one batched load (entries of all
   members in the period + history part via `BalancesAsync`-style batch) — finished properly with checkpoints in Phase 2;
   in Phase 1 only remove the per-member `MinAsync` (one grouped query for first entries).

Acceptance: all existing tests green; Phase-0 scaling report re-run shows team-overview, inbox and overlap calls no longer
growing with years; numbers recorded in `docs/perf/phase1.md`.

---

## Phase 2 — overtime balance checkpoints

### Model
`BalanceCheckpoint` (`Models/BalanceCheckpoint.cs`), table `BalanceCheckpoints`:
| column | meaning |
|---|---|
| `Id` | PK |
| `OrganizationId`, `UserId` | FKs (cascade with org; user deletion anonymizes — rows stay, like TimeEntries) |
| `Month` | `DateOnly`, first day of an org-local month that has **ended** (last day < org-local today) |
| `FirstDay` | the member's first tracked day the chain started from |
| `OvertimeDelta` | cumulative overtime from `FirstDay` to the month's last day, **excluding** `InitialOvertimeHours` |
| `Version` | `UserOrganization.BalanceVersion` at compute time |
| `OrgVersion` | `Organization.BalanceVersion` at compute time |
| `Algorithm` | `BalanceCheckpoints.AlgorithmVersion` constant at compute time |
| `ComputedAt` | UTC |
Unique `(OrganizationId, UserId, Month)`. RLS: same policy as TimeEntries (migration adds `ENABLE`/`FORCE` + policy,
copy the expression from `EnableRowLevelSecurity`). New columns `UserOrganization.BalanceVersion int` and
`Organization.BalanceVersion int` (default 0).

A row is **valid** iff `Version`, `OrgVersion` and `Algorithm` all match the current values. Invalid rows are ignored
(and deleted opportunistically). This makes concurrent compute-vs-edit races safe: a computation that read the old
version writes rows that are already invalid.

### Service `Services/Common/BalanceCheckpoints.cs`
- `public const int AlgorithmVersion = 1;` — bump whenever `BuildRows`/credit/pause rules logic changes (#76-like changes).
- `Task<Dictionary<int, double>> BalancesAsync(db, Organization org, IReadOnlyDictionary<int,double> initialByUser, DateOnly asOf)`
  — the one entry point for "balance as of a day" for one or many members:
  1. Load valid checkpoints: latest valid row per user with `Month` end < `asOf`'s month (one query, `DISTINCT ON` or
     group + max; ≤ 1 row per user).
  2. Load first-day per user without a checkpoint (one grouped `MIN(StartTime)` query).
  3. Load entries/absences/holidays/schedules for `[min(start), asOf]` for all users in one query each, run `BuildRows`
     per user from (checkpoint month end + 1 | first day) → `asOf`, `includeSummaries: false`, no core hours.
  4. For users whose tail spans ≥ 1 ended month without a checkpoint: write the missing month rows from the rows computed
     in step 3 (cumulative at each month end), with the versions read in step 1 (read them in the same query as the
     memberships). Insert with `ON CONFLICT (OrganizationId, UserId, Month) DO UPDATE … WHERE excluded is newer` on Postgres
     (raw SQL via `ExecuteSqlInterpolated` or a `DbUpdateException` catch-and-ignore); InMemory: plain add, ignore duplicates.
     Writes are outside the closed-period guard (not a guarded entity) and must not create history rows.
  - First load of a 10-year member computes the whole history once (as today) and persists ~120 rows; afterwards the tail
    is ≤ 1 month + the current one.
- `DailyReportAggregator.BuildWithRunningTotalAsync`, `DailyReportAggregator.BalancesAsync`, `PayrollExportService.HoursAsync`
  (batched for all members), `PdfReportService` (monthly/yearly/balance), daily CSV, accounting member report all go through
  it: they need (a) the balance at the day before the display range (start value) and (b) rows for the display range only.
  Add `BalanceCheckpoints.StartValueAsync(…, dateFrom)` = balance as of `dateFrom − 1` (+ first day), then
  `BuildAsync(dateFrom, dateTo, startValue)` with the running total continuing from it. **The rows' `CumulativeOvertime`
  must equal today's output exactly** (existing tests + the property test below guard this). Edge: display range before
  the first day → rows start at first day as today.

### Invalidation (`TimeTrackingDbContext.SaveChangesAsync`)
A new `BalanceInvalidation.Collect(db)` runs on **every** save (system work too — the business-day-end auto-stop, imports,
seeding; unlike the closed-period guard), before `base.SaveChangesAsync`, and its effects are part of the same save:
- `TimeEntry` added/modified/deleted (any property affecting the balance: StartTime, EndTime, PauseDurationMinutes,
  IsFailure, IsRunning, OrganizationId, UserId) — **including** a pure stop of a running timer (do not reuse
  `ClosedPeriods.Collect`'s skip). Affected: (org, user) of old and new values.
- `AbsenceDay` added/modified/deleted → (org, user).
- `WorkSchedule` added/modified/deleted → (org, user).
- `Holiday` added/modified/deleted → whole org. `AbsenceTypeDefinition.CreditMode` changed → whole org.
  `Organization.TimeZone` changed → whole org.
- Effect: for each affected (org, user) `UserOrganization.BalanceVersion += 1`; for each affected org
  `Organization.BalanceVersion += 1`. Done by tracking the `UserOrganization`/`Organization` entities (load them in one
  query each if not tracked) so it is in the same transaction. Then delete the now-invalid checkpoint rows
  (`ExecuteDelete` after the save on Postgres, or leave them — they are ignored; a cleanup in `RefreshTokenCleanupService`
  removes invalid rows every 6 h).
- Simplification over "from month X onward": bumping the version invalidates all of that member's checkpoints. Recompute of
  10 years on the next read costs what one balance call costs today, once. **Optimization (do it):** when the earliest touched
  day is in month X, before bumping, *copy* still-valid rows with `Month < X` to the new version (one `UPDATE … SET Version = new
  WHERE Month < X AND Version = old`) — so a normal edit in the current month invalidates nothing. For holiday/time-zone/credit
  changes, same with the earliest affected day (recurring holiday = all).
- Never `ExecuteUpdate`/raw SQL on the source tables (already a project rule) — add a test asserting that a raw change bypass
  is not used in services (grep-based test like `ErrorCodeTests`).

### Pre-warm
`FailureEntryService`'s minute loop: once per org per month (on the org-local 1st, after `BusinessDayEnd`), compute
balances of all active members via `BalancesAsync` (system work). Store `Organization.BalanceWarmedMonth` to run once.

### Correctness net
- Property test (InMemory + one Postgres variant): random sequence of 200 operations over 2 years for 3 members (entries
  incl. cross-midnight and running→stop across month end, absences incl. custom credit types, schedules, holidays incl.
  recurring, time-zone change, initial overtime change, closed months with admin reason) — after each op compare
  `BalancesAsync` vs a from-scratch `BuildAsync(firstDay → today)` (exact to 1e-9).
- `TeamOverviewPostgresTests` keeps comparing against the full recompute.
- Query-count tests: balance endpoint ≤ 10 queries and loads ≤ 31 days of entries after warm-up, independent of years.

---

## Phase 2b — vacation carry-over

Today: one `UserOrganization.VacationDaysPerYear`, `VacationBalances.UsageAsync` counts the selected calendar year only;
leftover days vanish; changing the allowance rewrites past years.

### Model
- `Organization`: `VacationCarryOverMode` enum (`None = 0`, `UntilDate = 1`, `Unlimited = 2`; default `UntilDate` for
  new orgs), `VacationCarryOverExpiryMonth` (3) / `VacationCarryOverExpiryDay` (31), `VacationCarryOverMaxDays double?`
  (null = no cap), `VacationCarryOverSince int` (first year whose leftover carries; migration sets 2026 for existing orgs
  so no past year's figures change, new orgs: creation year).
- `VacationYear` table (`OrganizationId, UserId, Year` unique): `Entitlement double`, `Adjustment double` (+/− days by an
  admin, e.g. opening balance from a previous system), `AdjustmentNote string? (≤ 500)`, `UpdatedAt`, `UpdatedByUserId`.
  Source data only — no cached computation (the chain needs ~30 vacation rows/member/year in one query; measured in the
  Phase-0 harness; add caching only if the numbers say so). RLS like AbsenceDays.
- Allowance changes (`PUT …members/{id}` vacation days, org `DefaultVacationDays` for new members) write the
  **current year's** `VacationYear` row (and `UserOrganization.VacationDaysPerYear` stays as "allowance from now on" for
  years without a row). Years without a row use: the latest earlier row's entitlement, else `VacationDaysPerYear`.
  Migration: no rows created (past years fall back to the current value, exactly today's behaviour).

### Rules (`Services/Common/VacationBalances.cs`)
For year Y (Y ≥ `CarryOverSince + 1` gets carry-in; earlier years have carry-in 0):
- `used(Y)` = vacation days in Y as today (working days, holidays excluded, half days 0.5; taken ≤ today, planned > today).
- `carryIn(Y)` = `carryOut(Y−1)`.
- Mode `UntilDate`: expiry `E = Y-MM-DD` (clamped to month length). Days used in Y on or before `E` consume `carryIn` first.
  `carryUsed = min(carryIn, usedOnOrBefore(E))`; `expired = today > E ? carryIn − carryUsed : 0`.
  Mode `Unlimited`: carried days never expire, used first. Mode `None`: `carryIn = 0`.
- `available(Y) = entitlement(Y) + adjustment(Y) + carryIn(Y) − expired(Y)`; `remaining(Y) = max(0, available − used)`
  (as today: planned counts as used).
- `carryOut(Y)` = leftover at year end = `max(0, entitlement + adjustment + carryIn − expiredAtYearEnd − used(Y))`, capped by
  `MaxDays` when set (cap applies to what moves into Y+1). Uses the full year's usage (planned included).
- `UsageAsync` grows a `Balance` record: `Entitlement, Adjustment, CarriedIn, CarriedUsed, Expired, ExpiresOn, Taken, Planned, Remaining`.
  One method `YearsAsync(db, org, memberIds, year, today)` loads vacation absences for `[CarryOverSince, Y]`, holidays,
  schedules, `VacationYear` rows in 4 queries for many members.
- Settings change (mode/expiry/cap/since): takes effect for all years ≥ since immediately (it is a rule, not data); the
  closed-period guard does not apply (vacation figures are not part of closed months' hours). Document this.

### API
- Org settings (`PUT …/settings`, org detail): `vacationCarryOverMode`, `vacationCarryOverExpiry` ("MM-DD"),
  `vacationCarryOverMaxDays`, `vacationCarryOverSince`; validation codes `org.carryOverExpiry`, `org.carryOverMaxDays`
  (0–365), `org.carryOverSince` (2000..current year) — added to `ErrorCodes.cs` + frontend `errors.ts`.
- `GET organizations/{slug}/members/{id}/vacation?year=` — the member's balance for a year (visibility: `VacationBalance`
  via `MemberVisibility`, like the member list's figures).
- `PUT organizations/{slug}/members/{id}/vacation/{year}` `{ entitlement?, adjustment?, note? }` — admins (and group admins
  with absence-approval capability for their members, like other vacation edits — check what the member-list allowance
  edit uses today and mirror it).
- Org detail member rows + accounting vacation overview (`AccountingService` ≈ :133–176) gain `carriedIn`, `expired`,
  `expiresOn`, `adjustment` fields; the dashboard's "Resturlaub" uses `remaining` (now including carry-over).
- `GET Auth/me/export` includes the member's `VacationYear` rows.

### Frontend
- `npm run generate-api` after the backend changes.
- Org settings (Regeln & Sichtbarkeit → Abwesenheiten/Urlaub topic): mode select, expiry date (day/month), optional cap,
  "gilt ab Jahr". i18n `orgSettings` de/en.
- `MemberVacation.svelte` / member detail: "davon x Tage aus {year−1}, verfallen am {date}" / "x Tage verfallen";
  admin edit of entitlement + adjustment (+ note) per year.
- Accounting vacation tab: columns Übertrag, Verfallen, Anpassung.
- Dashboard key figure tooltip includes carry-over. Unit tests for any new `utils/` helper.

---

## Phase 3 — guards and documentation
- CI (`.github/workflows/backend*.yml`): default `dotnet test --filter "Category!=Perf"`; a `PerfSmoke` subset
  (`[Trait("Category","PerfSmoke")]`, part of the normal run): 20 members × 1 year vs × 3 years → assert ratio of SQL commands
  equal and rows loaded for balance/team-overview/payroll/inbox ≤ 1.2×; fixed query budgets per endpoint.
- `workflow_dispatch` workflow `perf.yml`: full scaling tests (Category=Perf), uploads `TestResults/perf` as artifact.
- `docs/PERFORMANCE.md` (force-added): harness usage, k6 usage, baseline vs after table, the checkpoint invariants
  (what bumps versions, `AlgorithmVersion` rule), carry-over rules. CLAUDE.md: short bullets for checkpoints, carry-over,
  perf commands.
