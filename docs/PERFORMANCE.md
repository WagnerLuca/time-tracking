# Performance

How the app stays fast with long histories (target: 100 members × 10 years — ≈ 250k time entries, 330k core-hours
checks — interactive calls p95 < 300 ms, exports p95 < 2 s), how to measure it, and the rules that keep it that way.
History of the work: `docs/PERFORMANCE_PLAN.md` (phases 0–3), measurements in `docs/perf/` (`baseline.md`, `phase1.md`,
`phase2.md`, `final.md`, `k6.md`).

## Tools

| what | where | run |
|---|---|---|
| History generator | `backend/src/Data/PerfSeeder.cs` | `dotnet run -- perf-seed --employees 100 --years 10 [--core-hours off] [--password …]` (`backend/`, Postgres via `ConnectionStrings__DefaultConnection`/`DATABASE_URL`; `--drop --org-slug perf-…` removes it). Docker: `docker compose … run --rm --no-deps backend perf-seed …`. Outside Development/Testing only with `--force`, and only next to `perf-…`/demo orgs. |
| Scaling report | `backend/tests/TimeTracking.Api.Tests/Perf/ScalingTests.cs` (`Category=Perf`) | `dotnet test --filter Category=Perf` in `backend/tests/TimeTracking.Api.Tests` (Docker; on old Docker Desktop `export DOCKER_API_VERSION=1.41`). `PERF_SIZES` (default `100x1,100x2,100x5,100x10,25x5,50x5,200x5`, employees x years), `PERF_WARMUP` (3), `PERF_RUNS` (15). Report: `TestResults/perf/scaling-<stamp>.md` + `.csv` + progress `.log`. ≈ 5 min for the default matrix. CI: Actions → "Backend scaling tests" (`perf.yml`, manual, input `sizes`), artifact `perf-report` (14 days), summary table on the run page. |
| Query budgets (PerfSmoke) | `Postgres/BalanceQueryBudgetTests.cs` (`Category=PerfSmoke`) | part of the normal `dotnet test` / CI run (~15 s). |
| Load test | `loadtest/` (k6) | see `loadtest/README.md`; k6 via `docker run grafana/k6`. The production compose file needs Docker Compose ≥ 2.23 (inline `configs`); `docs/perf/k6.md` describes the stand-in used on an older Docker Desktop. |

### What the scaling report measures
Every size is seeded into a fresh database of its own (PerfSeeder, binary COPY, `ANALYZE`), the app runs with no
background jobs, and each endpoint is called over HTTP (TestServer) as the non-superuser app role with row-level security
on: 3 warm-ups, 15 measured calls → p50/p95 and the number of SQL commands EF sent for one call (`QueryCounter`, per async
flow). Endpoints: balance, team overview, the five inbox calls, entries of a week (own / member), compliance month,
absences year, org detail, accounting summary month, payroll CSV month, accounting vacation year, daily CSV month, monthly
PDF, timer start/stop, manual entry create/delete. The report adds the growth ratio t(100x10)/t(100x1) and
`EXPLAIN (ANALYZE, BUFFERS)` of the slowest statements of the 5 slowest calls at the longest history. Measure alone on the
machine; single-digit-ms differences between runs are noise.

### What the PerfSmoke test guards
Two PerfSeeder organizations (10 people × 1 year and × 3 years, core hours off), after warm-up: for balance, team overview,
payroll, inbox failures/approvals, accounting summary + vacation, daily CSV, monthly PDF, org detail, timer start, timer
stop and manual entry the SQL count is **the same for 1 and 3 years** and **≤ a fixed ceiling** (`Ceilings` in the test).
Plus: the warm balance loads no entries older than ~2 months, and a manual entry / timer stop in the current month is one
SQL command (the invalidation hook adds none). Lower a ceiling when a request gets cheaper; raising one needs a reason that
does not grow with the history. SQL counts can differ by data (e.g. the core-hours re-evaluation queries only when the range
holds stored violations), never by years.

## Results

p50 ms · SQL commands, scaling harness (full tables: `docs/perf/final.md`).

| endpoint | baseline 100x10 | final 100x10 | final 200x5 |
|---|---:|---:|---:|
| balance | 82.8 · 24 | 53.3 · 22 | 53.5 · 22 |
| team overview | 1317.8 · 10 | 48.0 · 10 | 56.5 · 10 |
| accounting summary month | 4997.5 · 907 | 58.0 · 11 | 83.7 · 11 |
| payroll CSV month | 5706.9 · 907 | 56.4 · 11 | 73.5 · 11 |
| daily CSV month | 83.2 · 22 | 48.6 · 18 | 61.5 · 22 |
| monthly PDF | 79.3 · 15 | 51.2 · 11 | 50.8 · 13 |
| inbox, timer, entries, org detail | 4–44 | 4–46 (unchanged) | 3–60 |

No endpoint grows with the years any more (t(100x10)/t(100x1) 0.8–1.4x; baseline: team overview 6.4x, exports 2.3–2.6x).
A cold balance read (no checkpoints, 100 × 10 years) costs ~1.8 s once for the whole team; the background pre-warm takes it.

k6 (production image, Postgres, RLS, 100 users × 10 years, all five scenarios, 15 min, everything on one desktop;
`docs/perf/k6.md`): 12,086 requests, 0 failed; interactive p95 21.6 ms (threshold 300 ms), exports p95 233 ms (threshold
2 s) — all thresholds pass.

## What was changed (short)

- **Phase 1**: overlap check bounded by the 24 h entry limit; team overview loads running entries + grouped failure counts
  only; core-hours re-evaluation indexed by day and only for the display range; balance-only callers skip the summary
  strings; payroll N+1 removed; indexes (`PerformanceIndexes`); retention via `ExecuteDelete`; import preview loads only
  the file's date range.
- **Phase 2**: overtime balance checkpoints (below). Every "balance as of a day" goes through `BalanceCheckpoints`.
- **Phase 2b**: vacation carry-over (`VacationYear` rows + org rules in `Services/Common/VacationBalances.cs`; rules in
  `docs/PERFORMANCE_PLAN.md` "Phase 2b"): carried days are used first, lapse at the org's expiry date (default 31.03.)
  unless the mode is Unlimited, optionally capped; a settings change applies to all years from `VacationCarryOverSince`.

## Balance checkpoints — invariants

`BalanceCheckpoints` rows: `(OrganizationId, UserId, Month)` unique, `OvertimeDelta` = the overtime sum from the member's
first tracked day to the month's last day, **without** the initial overtime. RLS like TimeEntries.

1. **Validity**: a row counts only while its `Version`, `OrgVersion` and `Algorithm` equal `UserOrganization.BalanceVersion`,
   `Organization.BalanceVersion` and `BalanceCheckpoints.AlgorithmVersion`. Invalid rows are ignored (and removed by the
   retention job). Versions are read before the source data and written with the rows, so a computation racing an edit
   only ever stores rows that are already invalid.
2. **What bumps**: `BalanceInvalidation` runs in every `TimeTrackingDbContext.SaveChangesAsync` (system work too) and bumps in
   the same transaction when a change touches a month that has **ended**:
   - per member: `TimeEntry` (start, end, pause, failure flag, running, org, user — also a pure timer stop, also the
     auto-stop), `AbsenceDay`, `WorkSchedule` (old and new owner/org);
   - per organization: `Holiday`, a custom absence type's credit mode (or its deletion), `Organization.TimeZone`.
   Before bumping it carries the still valid rows of earlier months to the new version, so only months from the earliest
   touched one on are recomputed. A change in the running month bumps nothing and costs no query.
3. **One-day margin**: a reader stores month M only when `LastDay(M) < today − 1` (from the 2nd of the next month), a writer
   bumps for months with `LastDay(M) < today` — so readers and writers whose org-local "today" differ by a day can never
   leave a stale valid row (`BalanceCheckpoints.Storable`).
4. **`AlgorithmVersion`**: bump it whenever the day rules of `DailyReportAggregator.BuildRows` change (targets, credits,
   pauses, what counts — e.g. #76). Every stored checkpoint then becomes invalid and is recomputed on the next read / by the
   pre-warm. Forgetting it leaves old sums in every balance.
5. **Never bypass SaveChanges on balance inputs**: no `ExecuteUpdate`/`ExecuteDelete`/raw SQL/COPY on TimeEntries,
   AbsenceDays, WorkSchedules, Holidays, AbsenceTypeDefinitions or `Organizations.TimeZone`
   (`BalanceInputWriteGuardTests` reads `backend/src` and fails otherwise; the allow-list names each exception). The
   closed-period guard needs the same.
6. **Exactness**: checkpointed balances equal the full recompute to the last bit (`BalanceCheckpointPropertyTests` +
   `BalanceCheckpointPropertyPostgresTests`, `TeamOverviewPostgresTests`).
7. **Pre-warm**: `BalanceCheckpointWarmupService` computes every org's checkpoints once per org-local month (on the 2nd,
   `Organization.BalanceWarmedMonth`), so the first read after a deploy or a month change is not a cold one.

## Known limits / follow-ups

- **No injectable clock**: the month-margin end-to-end checks (`BalanceCheckpointMarginTests`) shift "today" with the org's
  time zone and only run when some UTC offset is on a month boundary (around every month change); on other days they are a
  no-op. The rule itself is pinned date-independently. An `IClock`/`TimeProvider` would make them deterministic.
- **InMemory and concurrency**: InMemory has no unique index, row locks or transactions — two concurrent first computations
  may both add a month there (the write tolerates duplicates; InMemory saves are also skipped while the context has unsaved changes). Concurrency is only proven on Postgres
  (`BalanceCheckpointConcurrencyPostgresTests`); keep such tests under `Postgres/`.
- **Rejoining members and vacation**: the carry-over chain starts at `max(VacationCarryOverSince, year of JoinedAt)`;
  `VacationYear` rows of an earlier membership stay. Whether a rejoined member's earlier leftover should count again is
  not decided.
- **Who edits vacation years**: `PUT members/{id}/vacation/{year}` is org admins only, like the allowance. The plan asked
  whether group admins with absence-approval capability should get it too — open.
- k6 ran against a local single-machine stack (see `docs/perf/k6.md`); repeat it on the test host before relying on
  absolute numbers.
