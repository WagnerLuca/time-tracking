# Phase 2 — overtime balance checkpoints: before / after

Measured 2026-10-19 with the Phase 0 harness (each size in a fresh database, no background jobs, so no pre-warm),
`PERF_SIZES=100x1,100x10 dotnet test --filter Category=Perf`, alone on the machine, on the Phase 2 working tree
(**after**, full report below). **Before** = the Phase 1 numbers (`docs/perf/phase1.md`). p50 ms · SQL commands per
call. The harness makes 3 warm-up calls per endpoint before the 15 measured ones, so the measured calls find the
checkpoints already written by the warm-ups; the cold first calls are measured separately below.

| endpoint | before 100x1 | after 100x1 | before 100x10 | after 100x10 | change at 10 y |
|---|---:|---:|---:|---:|---|
| balance | 61.1 · 20 | 50.8 · 18 | 55.1 · 24 | 48.7 · 22 | −12 %; one query (versions + checkpoint + first entry + schedules) instead of first entry, time zone and schedules |
| team-overview | 164.9 · 10 | 35.8 · 10 | 852.3 · 10 | 39.2 · 10 | **−95 %**, flat over years (only the current month is computed) |
| inbox: failures | 3.9 · 1 | 3.2 · 1 | 2.9 · 1 | 2.8 · 1 | untouched |
| inbox: approvals | 25.3 · 7 | 20.2 · 7 | 18.6 · 7 | 20.8 · 7 | untouched |
| inbox: user-notifications | 5.2 · 1 | 5.4 · 1 | 4.0 · 1 | 3.7 · 1 | untouched |
| inbox: my-requests | 17.6 · 6 | 22.9 · 6 | 14.1 · 5 | 14.9 · 5 | untouched |
| inbox: notifications | 7.7 · 2 | 6.2 · 2 | 4.7 · 2 | 4.3 · 2 | untouched |
| TimeTracking week | 6.8 · 2 | 7.2 · 2 | 5.6 · 2 | 5.0 · 2 | untouched |
| member entries week | 18.5 · 7 | 17.1 · 7 | 17.5 · 7 | 17.8 · 7 | untouched |
| compliance month | 17.9 · 7 | 16.3 · 7 | 15.1 · 7 | 15.3 · 7 | untouched |
| absences year | 20.6 · 7 | 21.3 · 7 | 19.4 · 7 | 17.8 · 7 | untouched |
| org detail | 41.7 · 8 | 44.7 · 8 | 36.3 · 8 | 40.9 · 8 | untouched |
| accounting summary month | 131.9 · 11 | 57.8 · 11 | 866.8 · 11 | 71.1 · 11 | **−92 %**, flat (history from the August checkpoint, September computed) |
| payroll CSV month | 136.9 · 11 | 60.6 · 11 | 895.5 · 11 | 57.2 · 11 | **−94 %**, flat |
| accounting vacation year | 26.8 · 7 | 51.0 · 7 | 27.6 · 7 | 31.1 · 7 | untouched (100x1 value noisy) |
| daily CSV month | 49.6 · 20 | 90.4 · 18 | 56.7 · 20 | 48.7 · 18 | −2 SQL; 100x1 value noisy (single run) |
| monthly PDF | 55.1 · 13 | 63.7 · 11 | 61.9 · 13 | 56.0 · 11 | −2 SQL, flat |
| timer start | 18.2 · 7 | 20.3 · 7 | 15.5 · 7 | 18.9 · 7 | invalidation hook: no extra query (running entries are not a balance input) |
| timer stop | 16.9 · 7 | 24.3 · 7 | 17.0 · 7 | 18.1 · 7 | hook: no extra query (today's month cannot have a checkpoint) |
| manual entry create | 37.3 · 13 | 45.6 · 13 | 30.4 · 13 | 34.2 · 13 | hook: no extra query |
| manual entry delete | 21.4 · 7 | 29.3 · 7 | 18.9 · 7 | 22.3 · 7 | hook: no extra query |

The small single-digit differences on untouched endpoints are run-to-run noise (same code paths as Phase 1).

## Cold first calls (no checkpoints yet), 100x10

Measured once on a fresh 100x10 history (HTTP, as the app role under RLS, query compilation warmed beforehand,
`BalanceCheckpoints` emptied before each call):

| call | cold (computes and stores the history) | next call |
|---|---:|---:|
| balance (one member, 10 years) | 76 ms | 47 ms |
| team-overview (99 members) | 1903 ms | 52 ms |
| payroll CSV month (all 100 members) | 1736 ms | 49 ms |
| `BalanceCheckpoints.WarmAsync` (system pre-warm of the org) | 1742 ms | — |

A cold team overview / payroll costs about twice the Phase 1 time once: the full history is computed as before
(~850 ms) and ~12,000 checkpoint rows (100 members × 120 months) are upserted in chunks of 1000 rows. In production the
first such read after the deploy is taken by the background pre-warm (`BalanceCheckpointWarmupService`, its own
hosted service, once per org-local month; `BalanceWarmedMonth` is null after the migration, so it runs on its first
pass), so users normally never see a cold call; afterwards, each new month adds one row per member, computed by the
pre-warm on the 2nd (a month is stored only once its last day is before yesterday — the one-day margin against writers).

## How it works (short)

- `BalanceCheckpoints` rows: `(OrganizationId, UserId, Month)` unique, `OvertimeDelta` = overtime sum from `FirstDay`
  to the month's last day without the initial overtime, valid iff `Version`/`OrgVersion`/`Algorithm` equal
  `UserOrganization.BalanceVersion`/`Organization.BalanceVersion`/`BalanceCheckpoints.AlgorithmVersion`. RLS like
  TimeEntries (verified: an admin's team-overview computation writes the members' rows under RLS).
- Read: one query per call (`BalanceCheckpoints.LoadAsync`): per membership the versions, the latest valid checkpoint
  before the display range (three scalar subqueries, each a backward probe of the unique index), the latest valid
  month overall, the first finished entry (index probe), the failure count (partial index) and the schedules. Rows
  are built from the day after the checkpoint; the months that end inside them are upserted (never downgrading
  versions; nothing written when they are already valid).
- Exactness: `BuildRows` now keeps the running total as `initial + sum` with the sum continued from the checkpoint, so
  a checkpointed total is bit for bit the full recompute (with a non-zero initial overtime the value differs from the
  old `initial + o1 + o2 + …` association only in the last bits; every existing test is unchanged and green).
- Invalidation (`BalanceInvalidation`, every `SaveChangesAsync`, system work too): a change of an entry/absence/schedule
  (member) or holiday/custom credit mode/time zone (org) touching a month that has ended (last day before today) bumps the
  version in the same transaction (Postgres: `UPDATE … SET "BalanceVersion" = "BalanceVersion" + 1 RETURNING`, so the
  row lock orders concurrent bumps) and carries the valid rows of earlier months to the new version. Changes in the
  running month bump nothing and, in the usual case, cost no query.

## Equivalence check

A temporary test (deleted afterwards) on a PerfSeeder 40×3 history on Postgres compared, acting as an org admin under
RLS, the team balances (`BalancesAsync`), a past month's payroll totals and every member's
`BuildWithRunningTotalAsync` rows against a from-scratch `BuildAsync(firstDay → today)` with exact equality, cold, warm,
after a system edit of an entry 5 months back and a new absence 8 months back (13 of 1280 checkpoints invalidated,
the rest carried), after a new holiday 2 months back (80 invalidated, org-wide) and after an admin edit 19 months back
(19 invalidated, the carry runs under the admin's RLS). All equal to the last bit.

---

# Scaling report — 2026-10-19 11:56

Sizes (employees x years): 100x1, 100x10. Each call: 3 warm-ups, 15 measured (calls slower than 5000 ms: 1 + 5, marked *), over HTTP (TestServer, no network) as the app role with row-level security on, against PostgreSQL 16 in Docker (Testcontainers). Method: every size gets a fresh database of its own (created empty, migrated by its own app instance, seeded with PerfSeeder by binary COPY, then ANALYZEd), measured, and dropped — sizes never share tables, indexes or caches beyond the Postgres server itself; the background jobs (business-day-end/core hours loop, push dispatch, retention) are not running. Times in ms: p50 and p95 by nearest rank over n measured calls (with n < 20 the p95 is the maximum), SQL = commands EF sent for one call (median). Member = perf-user1 (full history), admin = perf-admin1, owner. Month = the last ended month, week = the current one, year = 2026. Growth = p50 ratio between the shortest and longest history at the same team size.

## Seeded data

| size | seconds | Users | WorkSchedules | Holidays | Groups | TimeEntries | TimeEntryChanges | OrgRequests | AbsenceDays | CoreHoursChecks | TerminalPunches | Notifications |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 100x1 | 7.2 | 100 | 100 | 19 | 20 | 25,365 | 1,383 | 1,191 | 3,480 | 33,260 | 25,150 | 15,581 |
| 100x10 | 33.4 | 100 | 365 | 55 | 20 | 252,667 | 16,191 | 10,943 | 32,912 | 330,086 | 29,274 | 150,994 |

## Summary (p50 ms · SQL commands)

| endpoint | kind | 100x1 | 100x10 | t(100x10)/t(100x1) |
|---|---|---:|---:|---:|
| balance | interactive | 50.8 · 18 | 48.7 · 22 | 1.0x (SQL 18 → 22) |
| team-overview | interactive | 35.8 · 10 | 39.2 · 10 | 1.1x (SQL 10 → 10) |
| inbox: failures | interactive | 3.2 · 1 | 2.8 · 1 | 0.9x (SQL 1 → 1) |
| inbox: approvals | interactive | 20.2 · 7 | 20.8 · 7 | 1.0x (SQL 7 → 7) |
| inbox: user-notifications | interactive | 5.4 · 1 | 3.7 · 1 | 0.7x (SQL 1 → 1) |
| inbox: my-requests | interactive | 22.9 · 6 | 14.9 · 5 | 0.6x (SQL 6 → 5) |
| inbox: notifications | interactive | 6.2 · 2 | 4.3 · 2 | 0.7x (SQL 2 → 2) |
| TimeTracking week | interactive | 7.2 · 2 | 5.0 · 2 | 0.7x (SQL 2 → 2) |
| member entries week | interactive | 17.1 · 7 | 17.8 · 7 | 1.0x (SQL 7 → 7) |
| compliance month | interactive | 16.3 · 7 | 15.3 · 7 | 0.9x (SQL 7 → 7) |
| absences year | interactive | 21.3 · 7 | 17.8 · 7 | 0.8x (SQL 7 → 7) |
| org detail | interactive | 44.7 · 8 | 40.9 · 8 | 0.9x (SQL 8 → 8) |
| accounting summary month | export | 57.8 · 11 | 71.1 · 11 | 1.2x (SQL 11 → 11) |
| payroll CSV month | export | 60.6 · 11 | 57.2 · 11 | 0.9x (SQL 11 → 11) |
| accounting vacation year | export | 51.0 · 7 | 31.1 · 7 | 0.6x (SQL 7 → 7) |
| daily CSV month | export | 90.4 · 18 | 48.7 · 18 | 0.5x (SQL 18 → 18) |
| monthly PDF | export | 63.7 · 11 | 56.0 · 11 | 0.9x (SQL 11 → 11) |
| timer start | interactive | 20.3 · 7 | 18.9 · 7 | 0.9x (SQL 7 → 7) |
| timer stop | interactive | 24.3 · 7 | 18.1 · 7 | 0.7x (SQL 7 → 7) |
| manual entry create | interactive | 45.6 · 13 | 34.2 · 13 | 0.7x (SQL 13 → 13) |
| manual entry delete | interactive | 29.3 · 7 | 22.3 · 7 | 0.8x (SQL 7 → 7) |

## Per endpoint

### balance (interactive)

| | 100x1 | 100x10 |
|---|---:|---:|
| p50 ms | 50.8 | 48.7 |
| p95 ms (nearest rank) | 117.3 | 72.8 |
| n | 15 | 15 |
| SQL | 18 | 22 |

Growth t(100x10)/t(100x1): 1.0x (SQL 18 → 22)

### team-overview (interactive)

| | 100x1 | 100x10 |
|---|---:|---:|
| p50 ms | 35.8 | 39.2 |
| p95 ms (nearest rank) | 40.7 | 67.4 |
| n | 15 | 15 |
| SQL | 10 | 10 |

Growth t(100x10)/t(100x1): 1.1x (SQL 10 → 10)

### inbox: failures (interactive)

| | 100x1 | 100x10 |
|---|---:|---:|
| p50 ms | 3.2 | 2.8 |
| p95 ms (nearest rank) | 3.7 | 11.8 |
| n | 15 | 15 |
| SQL | 1 | 1 |

Growth t(100x10)/t(100x1): 0.9x (SQL 1 → 1)

### inbox: approvals (interactive)

| | 100x1 | 100x10 |
|---|---:|---:|
| p50 ms | 20.2 | 20.8 |
| p95 ms (nearest rank) | 23.0 | 22.8 |
| n | 15 | 15 |
| SQL | 7 | 7 |

Growth t(100x10)/t(100x1): 1.0x (SQL 7 → 7)

### inbox: user-notifications (interactive)

| | 100x1 | 100x10 |
|---|---:|---:|
| p50 ms | 5.4 | 3.7 |
| p95 ms (nearest rank) | 7.6 | 4.1 |
| n | 15 | 15 |
| SQL | 1 | 1 |

Growth t(100x10)/t(100x1): 0.7x (SQL 1 → 1)

### inbox: my-requests (interactive)

| | 100x1 | 100x10 |
|---|---:|---:|
| p50 ms | 22.9 | 14.9 |
| p95 ms (nearest rank) | 26.1 | 16.3 |
| n | 15 | 15 |
| SQL | 6 | 5 |

Growth t(100x10)/t(100x1): 0.6x (SQL 6 → 5)

### inbox: notifications (interactive)

| | 100x1 | 100x10 |
|---|---:|---:|
| p50 ms | 6.2 | 4.3 |
| p95 ms (nearest rank) | 7.1 | 6.3 |
| n | 15 | 15 |
| SQL | 2 | 2 |

Growth t(100x10)/t(100x1): 0.7x (SQL 2 → 2)

### TimeTracking week (interactive)

| | 100x1 | 100x10 |
|---|---:|---:|
| p50 ms | 7.2 | 5.0 |
| p95 ms (nearest rank) | 7.9 | 5.4 |
| n | 15 | 15 |
| SQL | 2 | 2 |

Growth t(100x10)/t(100x1): 0.7x (SQL 2 → 2)

### member entries week (interactive)

| | 100x1 | 100x10 |
|---|---:|---:|
| p50 ms | 17.1 | 17.8 |
| p95 ms (nearest rank) | 21.1 | 20.7 |
| n | 15 | 15 |
| SQL | 7 | 7 |

Growth t(100x10)/t(100x1): 1.0x (SQL 7 → 7)

### compliance month (interactive)

| | 100x1 | 100x10 |
|---|---:|---:|
| p50 ms | 16.3 | 15.3 |
| p95 ms (nearest rank) | 21.5 | 18.1 |
| n | 15 | 15 |
| SQL | 7 | 7 |

Growth t(100x10)/t(100x1): 0.9x (SQL 7 → 7)

### absences year (interactive)

| | 100x1 | 100x10 |
|---|---:|---:|
| p50 ms | 21.3 | 17.8 |
| p95 ms (nearest rank) | 24.2 | 19.1 |
| n | 15 | 15 |
| SQL | 7 | 7 |

Growth t(100x10)/t(100x1): 0.8x (SQL 7 → 7)

### org detail (interactive)

| | 100x1 | 100x10 |
|---|---:|---:|
| p50 ms | 44.7 | 40.9 |
| p95 ms (nearest rank) | 46.3 | 129.5 |
| n | 15 | 15 |
| SQL | 8 | 8 |

Growth t(100x10)/t(100x1): 0.9x (SQL 8 → 8)

### accounting summary month (export)

| | 100x1 | 100x10 |
|---|---:|---:|
| p50 ms | 57.8 | 71.1 |
| p95 ms (nearest rank) | 93.5 | 84.1 |
| n | 15 | 15 |
| SQL | 11 | 11 |

Growth t(100x10)/t(100x1): 1.2x (SQL 11 → 11)

### payroll CSV month (export)

| | 100x1 | 100x10 |
|---|---:|---:|
| p50 ms | 60.6 | 57.2 |
| p95 ms (nearest rank) | 92.0 | 65.7 |
| n | 15 | 15 |
| SQL | 11 | 11 |

Growth t(100x10)/t(100x1): 0.9x (SQL 11 → 11)

### accounting vacation year (export)

| | 100x1 | 100x10 |
|---|---:|---:|
| p50 ms | 51.0 | 31.1 |
| p95 ms (nearest rank) | 72.4 | 40.9 |
| n | 15 | 15 |
| SQL | 7 | 7 |

Growth t(100x10)/t(100x1): 0.6x (SQL 7 → 7)

### daily CSV month (export)

| | 100x1 | 100x10 |
|---|---:|---:|
| p50 ms | 90.4 | 48.7 |
| p95 ms (nearest rank) | 110.5 | 57.7 |
| n | 15 | 15 |
| SQL | 18 | 18 |

Growth t(100x10)/t(100x1): 0.5x (SQL 18 → 18)

### monthly PDF (export)

| | 100x1 | 100x10 |
|---|---:|---:|
| p50 ms | 63.7 | 56.0 |
| p95 ms (nearest rank) | 116.6 | 71.9 |
| n | 15 | 15 |
| SQL | 11 | 11 |

Growth t(100x10)/t(100x1): 0.9x (SQL 11 → 11)

### timer start (interactive)

| | 100x1 | 100x10 |
|---|---:|---:|
| p50 ms | 20.3 | 18.9 |
| p95 ms (nearest rank) | 25.9 | 25.3 |
| n | 15 | 15 |
| SQL | 7 | 7 |

Growth t(100x10)/t(100x1): 0.9x (SQL 7 → 7)

### timer stop (interactive)

| | 100x1 | 100x10 |
|---|---:|---:|
| p50 ms | 24.3 | 18.1 |
| p95 ms (nearest rank) | 29.2 | 20.5 |
| n | 15 | 15 |
| SQL | 7 | 7 |

Growth t(100x10)/t(100x1): 0.7x (SQL 7 → 7)

### manual entry create (interactive)

| | 100x1 | 100x10 |
|---|---:|---:|
| p50 ms | 45.6 | 34.2 |
| p95 ms (nearest rank) | 57.1 | 36.7 |
| n | 15 | 15 |
| SQL | 13 | 13 |

Growth t(100x10)/t(100x1): 0.7x (SQL 13 → 13)

### manual entry delete (interactive)

| | 100x1 | 100x10 |
|---|---:|---:|
| p50 ms | 29.3 | 22.3 |
| p95 ms (nearest rank) | 32.4 | 32.0 |
| n | 15 | 15 |
| SQL | 7 | 7 |

Growth t(100x10)/t(100x1): 0.8x (SQL 7 → 7)

## EXPLAIN (ANALYZE, BUFFERS) — the 5 slowest calls at 100x10

The slowest SELECTs (up to 3) of the last measured call, re-run as the app role for the same user (RLS on).

### accounting summary month — p50 71.1 ms, 11 SQL

14.6 ms in the call:

```sql
SELECT u."UserId", u."BalanceVersion", o."BalanceVersion", (
    SELECT b."Month"
    FROM "BalanceCheckpoints" AS b
    WHERE b."OrganizationId" = @orgId AND b."Algorithm" = 1 AND b."Month" < @bound AND b."UserId" = u."UserId" AND b."Version" = u."BalanceVersion" AND b."OrgVersion" = o."BalanceVersion"
    ORDER BY b."Month" DESC
    LIMIT 1), (
    SELECT b0."OvertimeDelta"
    FROM "BalanceCheckpoints" AS b0
    WHERE b0."OrganizationId" = @orgId AND b0."Algorithm" = 1 AND b0."Month" < @bound AND b0."UserId" = u."UserId" AND b0."Version" = u."BalanceVersion" AND b0."OrgVersion" = o."BalanceVersion"
    ORDER BY b0."Month" DESC
    LIMIT 1), (
    SELECT b1."FirstDay"
    FROM "BalanceCheckpoints" AS b1
    WHERE b1."OrganizationId" = @orgId AND b1."Algorithm" = 1 AND b1."Month" < @bound AND b1."UserId" = u."UserId" AND b1."Version" = u."BalanceVersion" AND b1."OrgVersion" = o."BalanceVersion"
    ORDER BY b1."Month" DESC
    LIMIT 1), (
    SELECT max(b2."Month")
    FROM "BalanceCheckpoints" AS b2
    WHERE b2."OrganizationId" = @orgId AND b2."Algorithm" = 1 AND b2."UserId" = u."UserId" AND b2."Version" = u."BalanceVersion" AND b2."OrgVersion" = o."BalanceVersion"), (
    SELECT min(t."StartTime")
    FROM "TimeEntries" AS t
    WHERE t."OrganizationId" = @orgId9 AND t."UserId" = u."UserId" AND NOT (t."IsRunning")), (
    SELECT count(*)::int
    FROM "TimeEntries" AS t0
    WHERE t0."OrganizationId" = @orgId9 AND t0."UserId" = u."UserId" AND t0."IsFailure" AND t0."StartTime" < @failuresBeforeUtc), u."Id", o."Id", w0."Id", w0."CreatedAt", w0."OrganizationId", w0."TargetFri", w0."TargetMon", w0."TargetSat", w0."TargetSun", w0."TargetThu", w0."TargetTue", w0."TargetWed", w0."UserId", w0."ValidFrom", w0."ValidTo", w0."WeeklyWorkHours"
FROM "UserOrganizations" AS u
INNER JOIN "Organizations" AS o ON u."OrganizationId" = o."Id"
LEFT JOIN (
    SELECT w."Id", w."CreatedAt", w."OrganizationId", w."TargetFri", w."TargetMon", w."TargetSat", w."TargetSun", w."TargetThu", w."TargetTue", w."TargetWed", w."UserId", w."ValidFrom", w."ValidTo", w."WeeklyWorkHours"
    FROM "WorkSchedules" AS w
    WHERE w."OrganizationId" = @orgId
) AS w0 ON u."UserId" = w0."UserId"
WHERE u."OrganizationId" = @orgId AND u."UserId" = ANY (@ids)
ORDER BY u."Id", o."Id", w0."ValidFrom"
```

```text
Sort  (cost=144.08..144.09 rows=1 width=144) (actual time=9.918..9.936 rows=365 loops=1)
  Sort Key: u."Id", w."ValidFrom"
  Sort Method: quicksort  Memory: 87kB
  Buffers: shared hit=6820
  ->  Nested Loop Left Join  (cost=4.83..144.07 rows=1 width=144) (actual time=0.207..9.721 rows=365 loops=1)
        Buffers: shared hit=6820
        ->  Nested Loop  (cost=4.56..17.93 rows=1 width=20) (actual time=0.048..0.200 rows=100 loops=1)
              Buffers: shared hit=203
              ->  Bitmap Heap Scan on "UserOrganizations" u  (cost=4.41..9.76 rows=1 width=16) (actual time=0.036..0.076 rows=100 loops=1)
                    Recheck Cond: ("OrganizationId" = 2)
                    Filter: ("UserId" = ANY ('{16,17,18,19,20,15,21,30,31,32,33,34,35,36,37,38,39,22,40,41,42,43,44,45,46,47,48,49,23,50,51,52,53,54,55,56,57,58,59,24,60,61,62,63,64,65,66,67,68,69,25,70,71,72,73,74,75,76,77,78,79,26,80,81,82,83,84,85,86,87,88,89,27,90,91,92,93,94,95,96,97,98,99,28,100,101,102,103,104,105,106,107,108,109,29,110,111,112,113,114}'::integer[]))
                    Heap Blocks: exact=2
                    Buffers: shared hit=3
                    ->  Bitmap Index Scan on "IX_UserOrganizations_OrganizationId_PersonnelNumber"  (cost=0.00..4.16 rows=2 width=0) (actual time=0.021..0.022 rows=100 loops=1)
                          Index Cond: ("OrganizationId" = 2)
                          Buffers: shared hit=1
              ->  Index Scan using "PK_Organizations" on "Organizations" o  (cost=0.14..8.16 rows=1 width=8) (actual time=0.001..0.001 rows=1 loops=100)
                    Index Cond: ("Id" = 2)
                    Buffers: shared hit=200
        ->  Index Scan using "IX_WorkSchedules_UserId_OrganizationId_ValidFrom" on "WorkSchedules" w  (cost=0.28..8.29 rows=1 width=92) (actual time=0.001..0.002 rows=4 loops=100)
              Index Cond: (("UserId" = u."UserId") AND ("OrganizationId" = 2))
              Buffers: shared hit=304
        SubPlan 2
          ->  Limit  (cost=12.48..20.53 rows=1 width=4) (actual time=0.004..0.005 rows=1 loops=365)
                Buffers: shared hit=1097
                ->  Index Scan Backward using "IX_BalanceCheckpoints_OrganizationId_UserId_Month" on "BalanceCheckpoints" b  (cost=12.48..20.53 rows=1 width=4) (actual time=0.004..0.004 rows=1 loops=365)
                      Index Cond: (("OrganizationId" = 2) AND ("UserId" = u."UserId") AND ("Month" < '2026-09-01'::date))
                      Filter: (("Algorithm" = 1) AND ("Version" = u."BalanceVersion") AND ("OrgVersion" = o."BalanceVersion") AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 1)))
                      Buffers: shared hit=1097
                      SubPlan 1
                        ->  Index Scan using "IX_UserOrganizations_UserId_OrganizationId" on "UserOrganizations" uo  (cost=0.16..12.19 rows=1 width=4) (actual time=0.008..0.008 rows=1 loops=1)
                              Index Cond: ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer)
                              Filter: "IsActive"
                              Buffers: shared hit=2
        SubPlan 4
          ->  Limit  (cost=12.48..20.53 rows=1 width=12) (actual time=0.004..0.004 rows=1 loops=365)
                Buffers: shared hit=1097
                ->  Index Scan Backward using "IX_BalanceCheckpoints_OrganizationId_UserId_Month" on "BalanceCheckpoints" b0  (cost=12.48..20.53 rows=1 width=12) (actual time=0.004..0.004 rows=1 loops=365)
                      Index Cond: (("OrganizationId" = 2) AND ("UserId" = u."UserId") AND ("Month" < '2026-09-01'::date))
                      Filter: (("Algorithm" = 1) AND ("Version" = u."BalanceVersion") AND ("OrgVersion" = o."BalanceVersion") AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 3)))
                      Buffers: shared hit=1097
                      SubPlan 3
                        ->  Index Scan using "IX_UserOrganizations_UserId_OrganizationId" on "UserOrganizations" uo_1  (cost=0.16..12.19 rows=1 width=4) (actual time=0.006..0.006 rows=1 loops=1)
                              Index Cond: ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer)
                              Filter: "IsActive"
                              Buffers: shared hit=2
        SubPlan 6
          ->  Limit  (cost=12.48..20.53 rows=1 width=8) (actual time=0.004..0.004 rows=1 loops=365)
                Buffers: shared hit=1097
                ->  Index Scan Backward using "IX_BalanceCheckpoints_OrganizationId_UserId_Month" on "BalanceCheckpoints" b1  (cost=12.48..20.53 rows=1 width=8) (actual time=0.004..0.004 rows=1 loops=365)
                      Index Cond: (("OrganizationId" = 2) AND ("UserId" = u."UserId") AND ("Month" < '2026-09-01'::date))
                      Filter: (("Algorithm" = 1) AND ("Version" = u."BalanceVersion") AND ("OrgVersion" = o."BalanceVersion") AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 5)))
                      Buffers: shared hit=1097
                      SubPlan 5
                        ->  Index Scan using "IX_UserOrganizations_UserId_OrganizationId" on "UserOrganizations" uo_2  (cost=0.16..12.19 rows=1 width=4) (actual time=0.005..0.005 rows=1 loops=1)
                              Index Cond: ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer)
                              Filter: "IsActive"
                              Buffers: shared hit=2
        SubPlan 9
          ->  Result  (cost=20.53..20.54 rows=1 width=4) (actual time=0.004..0.004 rows=1 loops=365)
                Buffers: shared hit=1097
                InitPlan 8 (returns $16)
                  ->  Limit  (cost=12.48..20.53 rows=1 width=4) (actual time=0.004..0.004 rows=1 loops=365)
                        Buffers: shared hit=1097
                        ->  Index Scan Backward using "IX_BalanceCheckpoints_OrganizationId_UserId_Month" on "BalanceCheckpoints" b2  (cost=12.48..20.53 rows=1 width=4) (actual time=0.004..0.004 rows=1 loops=365)
                              Index Cond: (("OrganizationId" = 2) AND ("UserId" = u."UserId") AND ("Month" IS NOT NULL))
                              Filter: (("Algorithm" = 1) AND ("Version" = u."BalanceVersion") AND ("OrgVersion" = o."BalanceVersion") AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 7)))
                              Buffers: shared hit=1097
                              SubPlan 7
                                ->  Index Scan using "IX_UserOrganizations_UserId_OrganizationId" on "UserOrganizations" uo_3  (cost=0.16..12.19 rows=1 width=4) (actual time=0.005..0.005 rows=1 loops=1)
                                      Index Cond: ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer)
                                      Filter: "IsActive"
                                      Buffers: shared hit=2
        SubPlan 12
          ->  Result  (cost=15.30..15.31 rows=1 width=8) (actual time=0.005..0.005 rows=1 loops=365)
                Buffers: shared hit=1462
                InitPlan 11 (returns $19)
                  ->  Limit  (cost=12.62..15.30 rows=1 width=8) (actual time=0.005..0.005 rows=1 loops=365)
                        Buffers: shared hit=1462
                        ->  Index Scan using "IX_TimeEntries_UserId_StartTime" on "TimeEntries" t  (cost=12.62..3066.98 rows=1136 width=8) (actual time=0.004..0.004 rows=1 loops=365)
                              Index Cond: (("UserId" = u."UserId") AND ("StartTime" IS NOT NULL))
                              Filter: ((NOT "IsRunning") AND ("OrganizationId" = 2) AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 10)))
                              Buffers: shared hit=1462
                              SubPlan 10
                                ->  Index Scan using "IX_UserOrganizations_UserId_OrganizationId" on "UserOrganizations" uo_4  (cost=0.16..12.19 rows=1 width=4) (actual time=0.005..0.005 rows=1 loops=1)
                                      Index Cond: ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer)
                                      Filter: "IsActive"
                                      Buffers: shared hit=2
        SubPlan 14
          ->  Aggregate  (cost=20.38..20.39 rows=1 width=4) (actual time=0.001..0.001 rows=1 loops=365)
                Buffers: shared hit=463
                ->  Index Scan using "IX_TimeEntries_UserId_Failure" on "TimeEntries" t0  (cost=12.33..20.37 rows=1 width=0) (actual time=0.001..0.001 rows=0 loops=365)
                      Index Cond: ("UserId" = u."UserId")
                      Filter: (("StartTime" < '1900-01-01 00:00:00+00'::timestamp with time zone) AND ("OrganizationId" = 2) AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 13)))
                      Rows Removed by Filter: 0
                      Buffers: shared hit=463
                      SubPlan 13
                        ->  Index Scan using "IX_UserOrganizations_UserId_OrganizationId" on "UserOrganizations" uo_5  (cost=0.16..12.19 rows=1 width=4) (never executed)
                              Index Cond: ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer)
                              Filter: "IsActive"
Planning Time: 1.418 ms
Execution Time: 10.412 ms
```

5.8 ms in the call:

```sql
SELECT DISTINCT u."UserId"
FROM (
    SELECT t."UserId"
    FROM "TimeEntries" AS t
    LEFT JOIN (
        SELECT o."Id", o."IsActive"
        FROM "Organizations" AS o
        WHERE o."IsActive"
    ) AS o0 ON t."OrganizationId" = o0."Id"
    WHERE (t."OrganizationId" IS NULL OR o0."IsActive") AND t."OrganizationId" = @org_Id AND NOT (t."IsRunning") AND t."StartTime" >= @fromUtc AND t."StartTime" < @toUtc
    UNION
    SELECT a."UserId"
    FROM "AbsenceDays" AS a
    INNER JOIN (
        SELECT o1."Id", o1."IsActive"
        FROM "Organizations" AS o1
        WHERE o1."IsActive"
    ) AS o2 ON a."OrganizationId" = o2."Id"
    WHERE o2."IsActive" AND a."OrganizationId" = @org_Id3 AND a."Date" >= @from AND a."Date" <= @to
) AS u
```

```text
HashAggregate  (cost=3670.47..3672.47 rows=200 width=4) (actual time=3.929..3.941 rows=100 loops=1)
  Group Key: t."UserId"
  Batches: 1  Memory Usage: 40kB
  Buffers: shared hit=2361
  ->  HashAggregate  (cost=3660.24..3668.42 rows=818 width=4) (actual time=3.898..3.913 rows=100 loops=1)
        Group Key: t."UserId"
        Batches: 1  Memory Usage: 49kB
        Buffers: shared hit=2361
        ->  Append  (cost=12.76..3658.20 rows=818 width=4) (actual time=0.057..3.531 rows=2671 loops=1)
              Buffers: shared hit=2361
              ->  Nested Loop Left Join  (cost=12.76..3251.35 rows=610 width=4) (actual time=0.056..2.929 rows=2265 loops=1)
                    Filter: ((t."OrganizationId" IS NULL) OR o."IsActive")
                    Buffers: shared hit=2280
                    ->  Index Scan using "IX_TimeEntries_OrganizationId_StartTime" on "TimeEntries" t  (cost=12.62..3227.95 rows=1219 width=8) (actual time=0.042..2.359 rows=2265 loops=1)
                          Index Cond: (("OrganizationId" = 2) AND ("StartTime" >= '2026-08-31 22:00:00+00'::timestamp with time zone) AND ("StartTime" < '2026-09-30 22:00:00+00'::timestamp with time zone))
                          Filter: ((NOT "IsRunning") AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 1)))
                          Buffers: shared hit=2278
                          SubPlan 1
                            ->  Index Scan using "IX_UserOrganizations_UserId_OrganizationId" on "UserOrganizations" uo  (cost=0.16..12.19 rows=1 width=4) (actual time=0.008..0.009 rows=1 loops=1)
                                  Index Cond: ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer)
                                  Filter: "IsActive"
                                  Buffers: shared hit=2
                    ->  Materialize  (cost=0.14..8.17 rows=1 width=5) (actual time=0.000..0.000 rows=1 loops=2265)
                          Buffers: shared hit=2
                          ->  Index Scan using "PK_Organizations" on "Organizations" o  (cost=0.14..8.16 rows=1 width=5) (actual time=0.009..0.009 rows=1 loops=1)
                                Index Cond: ("Id" = 2)
                                Filter: "IsActive"
                                Buffers: shared hit=2
              ->  Nested Loop  (cost=21.79..402.76 rows=208 width=4) (actual time=0.116..0.423 rows=406 loops=1)
                    Buffers: shared hit=81
                    ->  Index Scan using "PK_Organizations" on "Organizations" o1  (cost=0.14..8.16 rows=1 width=4) (actual time=0.015..0.016 rows=1 loops=1)
                          Index Cond: ("Id" = 2)
                          Filter: ("IsActive" AND "IsActive")
                          Buffers: shared hit=2
                    ->  Bitmap Heap Scan on "AbsenceDays" a  (cost=21.65..392.52 rows=208 width=8) (actual time=0.096..0.373 rows=406 loops=1)
                          Recheck Cond: (("OrganizationId" = 2) AND ("Date" >= '2026-09-01'::date) AND ("Date" <= '2026-09-30'::date))
                          Filter: ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 2))
                          Heap Blocks: exact=75
                          Buffers: shared hit=79
                          ->  Bitmap Index Scan on "IX_AbsenceDays_OrganizationId_Date"  (cost=0.00..9.40 rows=409 width=0) (actual time=0.027..0.027 rows=406 loops=1)
                                Index Cond: (("OrganizationId" = 2) AND ("Date" >= '2026-09-01'::date) AND ("Date" <= '2026-09-30'::date))
                                Buffers: shared hit=2
                          SubPlan 2
                            ->  Index Scan using "IX_UserOrganizations_UserId_OrganizationId" on "UserOrganizations" uo_1  (cost=0.16..12.19 rows=1 width=4) (actual time=0.010..0.011 rows=1 loops=1)
                                  Index Cond: ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer)
                                  Filter: "IsActive"
                                  Buffers: shared hit=2
Planning Time: 0.504 ms
Execution Time: 4.109 ms
```

2.4 ms in the call:

```sql
SELECT a."Id", a."CreatedAt", a."CreatedByUserId", a."CreatedViaRequestId", a."CustomTypeId", a."Date", a."IsHalfDay", a."Note", a."OrganizationId", a."Type", a."UserId", s."Id", s."Color", s."CountsAsVacation", s."CreatedAt", s."CreditMode", s."IsActive", s."Name", s."OrganizationId"
FROM "AbsenceDays" AS a
INNER JOIN (
    SELECT o."Id", o."IsActive"
    FROM "Organizations" AS o
    WHERE o."IsActive"
) AS o0 ON a."OrganizationId" = o0."Id"
LEFT JOIN (
    SELECT a0."Id", a0."Color", a0."CountsAsVacation", a0."CreatedAt", a0."CreditMode", a0."IsActive", a0."Name", a0."OrganizationId"
    FROM "AbsenceTypeDefinitions" AS a0
    INNER JOIN (
        SELECT o1."Id", o1."IsActive"
        FROM "Organizations" AS o1
        WHERE o1."IsActive"
    ) AS o2 ON a0."OrganizationId" = o2."Id"
    WHERE o2."IsActive"
) AS s ON a."CustomTypeId" = s."Id"
WHERE o0."IsActive" AND a."OrganizationId" = @org_Id AND a."UserId" = ANY (@startIds) AND a."Date" >= @from AND a."Date" <= @to
```

```text
Nested Loop  (cost=46.93..430.72 rows=207 width=353) (actual time=0.110..0.633 rows=406 loops=1)
  Buffers: shared hit=83
  ->  Index Scan using "PK_Organizations" on "Organizations" o  (cost=0.14..8.16 rows=1 width=4) (actual time=0.012..0.013 rows=1 loops=1)
        Index Cond: ("Id" = 2)
        Filter: ("IsActive" AND "IsActive")
        Buffers: shared hit=2
  ->  Hash Left Join  (cost=46.79..420.49 rows=207 width=353) (actual time=0.095..0.545 rows=406 loops=1)
        Hash Cond: (a."CustomTypeId" = a0."Id")
        Buffers: shared hit=81
        ->  Bitmap Heap Scan on "AbsenceDays" a  (cost=21.90..394.82 rows=207 width=55) (actual time=0.059..0.428 rows=406 loops=1)
              Recheck Cond: (("OrganizationId" = 2) AND ("Date" >= '2026-09-01'::date) AND ("Date" <= '2026-09-30'::date))
              Filter: (("UserId" = ANY ('{15,16,17,18,19,20,21,22,23,24,25,26,27,28,29,30,31,32,33,34,35,36,37,38,39,40,41,42,43,44,45,46,47,48,49,50,51,52,53,54,55,56,57,58,59,60,61,62,63,64,65,66,67,68,69,70,71,72,73,74,75,76,77,78,79,80,81,82,83,84,85,86,87,88,89,90,91,92,93,94,95,96,97,98,99,100,101,102,103,104,105,106,107,108,109,110,111,112,113,114}'::integer[])) AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 1)))
              Heap Blocks: exact=75
              Buffers: shared hit=79
              ->  Bitmap Index Scan on "IX_AbsenceDays_OrganizationId_Date"  (cost=0.00..9.40 rows=409 width=0) (actual time=0.026..0.026 rows=406 loops=1)
                    Index Cond: (("OrganizationId" = 2) AND ("Date" >= '2026-09-01'::date) AND ("Date" <= '2026-09-30'::date))
                    Buffers: shared hit=2
              SubPlan 1
                ->  Index Scan using "IX_UserOrganizations_UserId_OrganizationId" on "UserOrganizations" uo  (cost=0.16..12.19 rows=1 width=4) (actual time=0.006..0.007 rows=1 loops=1)
                      Index Cond: ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer)
                      Filter: "IsActive"
                      Buffers: shared hit=2
        ->  Hash  (cost=24.09..24.09 rows=64 width=298) (actual time=0.028..0.029 rows=3 loops=1)
              Buckets: 1024  Batches: 1  Memory Usage: 9kB
              Buffers: shared hit=2
              ->  Hash Join  (cost=10.92..24.09 rows=64 width=298) (actual time=0.021..0.023 rows=3 loops=1)
                    Hash Cond: (a0."OrganizationId" = o1."Id")
                    Buffers: shared hit=2
                    ->  Seq Scan on "AbsenceTypeDefinitions" a0  (cost=0.00..12.50 rows=250 width=298) (actual time=0.003..0.004 rows=3 loops=1)
                          Buffers: shared hit=1
                    ->  Hash  (cost=10.70..10.70 rows=18 width=4) (actual time=0.012..0.012 rows=2 loops=1)
                          Buckets: 1024  Batches: 1  Memory Usage: 9kB
                          Buffers: shared hit=1
                          ->  Seq Scan on "Organizations" o1  (cost=0.00..10.70 rows=18 width=4) (actual time=0.006..0.007 rows=2 loops=1)
                                Filter: ("IsActive" AND "IsActive")
                                Buffers: shared hit=1
Planning Time: 0.516 ms
Execution Time: 0.844 ms
```

### payroll CSV month — p50 57.2 ms, 11 SQL

13.4 ms in the call:

```sql
SELECT u."UserId", u."BalanceVersion", o."BalanceVersion", (
    SELECT b."Month"
    FROM "BalanceCheckpoints" AS b
    WHERE b."OrganizationId" = @orgId AND b."Algorithm" = 1 AND b."Month" < @bound AND b."UserId" = u."UserId" AND b."Version" = u."BalanceVersion" AND b."OrgVersion" = o."BalanceVersion"
    ORDER BY b."Month" DESC
    LIMIT 1), (
    SELECT b0."OvertimeDelta"
    FROM "BalanceCheckpoints" AS b0
    WHERE b0."OrganizationId" = @orgId AND b0."Algorithm" = 1 AND b0."Month" < @bound AND b0."UserId" = u."UserId" AND b0."Version" = u."BalanceVersion" AND b0."OrgVersion" = o."BalanceVersion"
    ORDER BY b0."Month" DESC
    LIMIT 1), (
    SELECT b1."FirstDay"
    FROM "BalanceCheckpoints" AS b1
    WHERE b1."OrganizationId" = @orgId AND b1."Algorithm" = 1 AND b1."Month" < @bound AND b1."UserId" = u."UserId" AND b1."Version" = u."BalanceVersion" AND b1."OrgVersion" = o."BalanceVersion"
    ORDER BY b1."Month" DESC
    LIMIT 1), (
    SELECT max(b2."Month")
    FROM "BalanceCheckpoints" AS b2
    WHERE b2."OrganizationId" = @orgId AND b2."Algorithm" = 1 AND b2."UserId" = u."UserId" AND b2."Version" = u."BalanceVersion" AND b2."OrgVersion" = o."BalanceVersion"), (
    SELECT min(t."StartTime")
    FROM "TimeEntries" AS t
    WHERE t."OrganizationId" = @orgId9 AND t."UserId" = u."UserId" AND NOT (t."IsRunning")), (
    SELECT count(*)::int
    FROM "TimeEntries" AS t0
    WHERE t0."OrganizationId" = @orgId9 AND t0."UserId" = u."UserId" AND t0."IsFailure" AND t0."StartTime" < @failuresBeforeUtc), u."Id", o."Id", w0."Id", w0."CreatedAt", w0."OrganizationId", w0."TargetFri", w0."TargetMon", w0."TargetSat", w0."TargetSun", w0."TargetThu", w0."TargetTue", w0."TargetWed", w0."UserId", w0."ValidFrom", w0."ValidTo", w0."WeeklyWorkHours"
FROM "UserOrganizations" AS u
INNER JOIN "Organizations" AS o ON u."OrganizationId" = o."Id"
LEFT JOIN (
    SELECT w."Id", w."CreatedAt", w."OrganizationId", w."TargetFri", w."TargetMon", w."TargetSat", w."TargetSun", w."TargetThu", w."TargetTue", w."TargetWed", w."UserId", w."ValidFrom", w."ValidTo", w."WeeklyWorkHours"
    FROM "WorkSchedules" AS w
    WHERE w."OrganizationId" = @orgId
) AS w0 ON u."UserId" = w0."UserId"
WHERE u."OrganizationId" = @orgId AND u."UserId" = ANY (@ids)
ORDER BY u."Id", o."Id", w0."ValidFrom"
```

```text
Sort  (cost=144.08..144.09 rows=1 width=144) (actual time=10.211..10.229 rows=365 loops=1)
  Sort Key: u."Id", w."ValidFrom"
  Sort Method: quicksort  Memory: 87kB
  Buffers: shared hit=6820
  ->  Nested Loop Left Join  (cost=4.83..144.07 rows=1 width=144) (actual time=0.128..10.000 rows=365 loops=1)
        Buffers: shared hit=6820
        ->  Nested Loop  (cost=4.56..17.93 rows=1 width=20) (actual time=0.038..0.224 rows=100 loops=1)
              Buffers: shared hit=203
              ->  Bitmap Heap Scan on "UserOrganizations" u  (cost=4.41..9.76 rows=1 width=16) (actual time=0.029..0.097 rows=100 loops=1)
                    Recheck Cond: ("OrganizationId" = 2)
                    Filter: ("UserId" = ANY ('{16,17,18,19,20,15,21,30,31,32,33,34,35,36,37,38,39,22,40,41,42,43,44,45,46,47,48,49,23,50,51,52,53,54,55,56,57,58,59,24,60,61,62,63,64,65,66,67,68,69,25,70,71,72,73,74,75,76,77,78,79,26,80,81,82,83,84,85,86,87,88,89,27,90,91,92,93,94,95,96,97,98,99,28,100,101,102,103,104,105,106,107,108,109,29,110,111,112,113,114}'::integer[]))
                    Heap Blocks: exact=2
                    Buffers: shared hit=3
                    ->  Bitmap Index Scan on "IX_UserOrganizations_OrganizationId_PersonnelNumber"  (cost=0.00..4.16 rows=2 width=0) (actual time=0.016..0.017 rows=100 loops=1)
                          Index Cond: ("OrganizationId" = 2)
                          Buffers: shared hit=1
              ->  Index Scan using "PK_Organizations" on "Organizations" o  (cost=0.14..8.16 rows=1 width=8) (actual time=0.001..0.001 rows=1 loops=100)
                    Index Cond: ("Id" = 2)
                    Buffers: shared hit=200
        ->  Index Scan using "IX_WorkSchedules_UserId_OrganizationId_ValidFrom" on "WorkSchedules" w  (cost=0.28..8.29 rows=1 width=92) (actual time=0.001..0.003 rows=4 loops=100)
              Index Cond: (("UserId" = u."UserId") AND ("OrganizationId" = 2))
              Buffers: shared hit=304
        SubPlan 2
          ->  Limit  (cost=12.48..20.53 rows=1 width=4) (actual time=0.005..0.005 rows=1 loops=365)
                Buffers: shared hit=1097
                ->  Index Scan Backward using "IX_BalanceCheckpoints_OrganizationId_UserId_Month" on "BalanceCheckpoints" b  (cost=12.48..20.53 rows=1 width=4) (actual time=0.004..0.004 rows=1 loops=365)
                      Index Cond: (("OrganizationId" = 2) AND ("UserId" = u."UserId") AND ("Month" < '2026-09-01'::date))
                      Filter: (("Algorithm" = 1) AND ("Version" = u."BalanceVersion") AND ("OrgVersion" = o."BalanceVersion") AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 1)))
                      Buffers: shared hit=1097
                      SubPlan 1
                        ->  Index Scan using "IX_UserOrganizations_UserId_OrganizationId" on "UserOrganizations" uo  (cost=0.16..12.19 rows=1 width=4) (actual time=0.006..0.007 rows=1 loops=1)
                              Index Cond: ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer)
                              Filter: "IsActive"
                              Buffers: shared hit=2
        SubPlan 4
          ->  Limit  (cost=12.48..20.53 rows=1 width=12) (actual time=0.004..0.004 rows=1 loops=365)
                Buffers: shared hit=1097
                ->  Index Scan Backward using "IX_BalanceCheckpoints_OrganizationId_UserId_Month" on "BalanceCheckpoints" b0  (cost=12.48..20.53 rows=1 width=12) (actual time=0.004..0.004 rows=1 loops=365)
                      Index Cond: (("OrganizationId" = 2) AND ("UserId" = u."UserId") AND ("Month" < '2026-09-01'::date))
                      Filter: (("Algorithm" = 1) AND ("Version" = u."BalanceVersion") AND ("OrgVersion" = o."BalanceVersion") AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 3)))
                      Buffers: shared hit=1097
                      SubPlan 3
                        ->  Index Scan using "IX_UserOrganizations_UserId_OrganizationId" on "UserOrganizations" uo_1  (cost=0.16..12.19 rows=1 width=4) (actual time=0.005..0.005 rows=1 loops=1)
                              Index Cond: ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer)
                              Filter: "IsActive"
                              Buffers: shared hit=2
        SubPlan 6
          ->  Limit  (cost=12.48..20.53 rows=1 width=8) (actual time=0.004..0.004 rows=1 loops=365)
                Buffers: shared hit=1097
                ->  Index Scan Backward using "IX_BalanceCheckpoints_OrganizationId_UserId_Month" on "BalanceCheckpoints" b1  (cost=12.48..20.53 rows=1 width=8) (actual time=0.004..0.004 rows=1 loops=365)
                      Index Cond: (("OrganizationId" = 2) AND ("UserId" = u."UserId") AND ("Month" < '2026-09-01'::date))
                      Filter: (("Algorithm" = 1) AND ("Version" = u."BalanceVersion") AND ("OrgVersion" = o."BalanceVersion") AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 5)))
                      Buffers: shared hit=1097
                      SubPlan 5
                        ->  Index Scan using "IX_UserOrganizations_UserId_OrganizationId" on "UserOrganizations" uo_2  (cost=0.16..12.19 rows=1 width=4) (actual time=0.005..0.005 rows=1 loops=1)
                              Index Cond: ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer)
                              Filter: "IsActive"
                              Buffers: shared hit=2
        SubPlan 9
          ->  Result  (cost=20.53..20.54 rows=1 width=4) (actual time=0.004..0.005 rows=1 loops=365)
                Buffers: shared hit=1097
                InitPlan 8 (returns $16)
                  ->  Limit  (cost=12.48..20.53 rows=1 width=4) (actual time=0.004..0.004 rows=1 loops=365)
                        Buffers: shared hit=1097
                        ->  Index Scan Backward using "IX_BalanceCheckpoints_OrganizationId_UserId_Month" on "BalanceCheckpoints" b2  (cost=12.48..20.53 rows=1 width=4) (actual time=0.004..0.004 rows=1 loops=365)
                              Index Cond: (("OrganizationId" = 2) AND ("UserId" = u."UserId") AND ("Month" IS NOT NULL))
                              Filter: (("Algorithm" = 1) AND ("Version" = u."BalanceVersion") AND ("OrgVersion" = o."BalanceVersion") AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 7)))
                              Buffers: shared hit=1097
                              SubPlan 7
                                ->  Index Scan using "IX_UserOrganizations_UserId_OrganizationId" on "UserOrganizations" uo_3  (cost=0.16..12.19 rows=1 width=4) (actual time=0.006..0.006 rows=1 loops=1)
                                      Index Cond: ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer)
                                      Filter: "IsActive"
                                      Buffers: shared hit=2
        SubPlan 12
          ->  Result  (cost=15.30..15.31 rows=1 width=8) (actual time=0.005..0.005 rows=1 loops=365)
                Buffers: shared hit=1462
                InitPlan 11 (returns $19)
                  ->  Limit  (cost=12.62..15.30 rows=1 width=8) (actual time=0.005..0.005 rows=1 loops=365)
                        Buffers: shared hit=1462
                        ->  Index Scan using "IX_TimeEntries_UserId_StartTime" on "TimeEntries" t  (cost=12.62..3066.98 rows=1136 width=8) (actual time=0.004..0.004 rows=1 loops=365)
                              Index Cond: (("UserId" = u."UserId") AND ("StartTime" IS NOT NULL))
                              Filter: ((NOT "IsRunning") AND ("OrganizationId" = 2) AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 10)))
                              Buffers: shared hit=1462
                              SubPlan 10
                                ->  Index Scan using "IX_UserOrganizations_UserId_OrganizationId" on "UserOrganizations" uo_4  (cost=0.16..12.19 rows=1 width=4) (actual time=0.005..0.005 rows=1 loops=1)
                                      Index Cond: ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer)
                                      Filter: "IsActive"
                                      Buffers: shared hit=2
        SubPlan 14
          ->  Aggregate  (cost=20.38..20.39 rows=1 width=4) (actual time=0.001..0.001 rows=1 loops=365)
                Buffers: shared hit=463
                ->  Index Scan using "IX_TimeEntries_UserId_Failure" on "TimeEntries" t0  (cost=12.33..20.37 rows=1 width=0) (actual time=0.001..0.001 rows=0 loops=365)
                      Index Cond: ("UserId" = u."UserId")
                      Filter: (("StartTime" < '1900-01-01 00:00:00+00'::timestamp with time zone) AND ("OrganizationId" = 2) AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 13)))
                      Rows Removed by Filter: 0
                      Buffers: shared hit=463
                      SubPlan 13
                        ->  Index Scan using "IX_UserOrganizations_UserId_OrganizationId" on "UserOrganizations" uo_5  (cost=0.16..12.19 rows=1 width=4) (never executed)
                              Index Cond: ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer)
                              Filter: "IsActive"
Planning Time: 1.088 ms
Execution Time: 10.658 ms
```

4.8 ms in the call:

```sql
SELECT DISTINCT u."UserId"
FROM (
    SELECT t."UserId"
    FROM "TimeEntries" AS t
    LEFT JOIN (
        SELECT o."Id", o."IsActive"
        FROM "Organizations" AS o
        WHERE o."IsActive"
    ) AS o0 ON t."OrganizationId" = o0."Id"
    WHERE (t."OrganizationId" IS NULL OR o0."IsActive") AND t."OrganizationId" = @org_Id AND NOT (t."IsRunning") AND t."StartTime" >= @fromUtc AND t."StartTime" < @toUtc
    UNION
    SELECT a."UserId"
    FROM "AbsenceDays" AS a
    INNER JOIN (
        SELECT o1."Id", o1."IsActive"
        FROM "Organizations" AS o1
        WHERE o1."IsActive"
    ) AS o2 ON a."OrganizationId" = o2."Id"
    WHERE o2."IsActive" AND a."OrganizationId" = @org_Id3 AND a."Date" >= @from AND a."Date" <= @to
) AS u
```

```text
HashAggregate  (cost=3670.47..3672.47 rows=200 width=4) (actual time=3.524..3.536 rows=100 loops=1)
  Group Key: t."UserId"
  Batches: 1  Memory Usage: 40kB
  Buffers: shared hit=2361
  ->  HashAggregate  (cost=3660.24..3668.42 rows=818 width=4) (actual time=3.492..3.508 rows=100 loops=1)
        Group Key: t."UserId"
        Batches: 1  Memory Usage: 49kB
        Buffers: shared hit=2361
        ->  Append  (cost=12.76..3658.20 rows=818 width=4) (actual time=0.051..3.145 rows=2671 loops=1)
              Buffers: shared hit=2361
              ->  Nested Loop Left Join  (cost=12.76..3251.35 rows=610 width=4) (actual time=0.050..2.621 rows=2265 loops=1)
                    Filter: ((t."OrganizationId" IS NULL) OR o."IsActive")
                    Buffers: shared hit=2280
                    ->  Index Scan using "IX_TimeEntries_OrganizationId_StartTime" on "TimeEntries" t  (cost=12.62..3227.95 rows=1219 width=8) (actual time=0.038..2.149 rows=2265 loops=1)
                          Index Cond: (("OrganizationId" = 2) AND ("StartTime" >= '2026-08-31 22:00:00+00'::timestamp with time zone) AND ("StartTime" < '2026-09-30 22:00:00+00'::timestamp with time zone))
                          Filter: ((NOT "IsRunning") AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 1)))
                          Buffers: shared hit=2278
                          SubPlan 1
                            ->  Index Scan using "IX_UserOrganizations_UserId_OrganizationId" on "UserOrganizations" uo  (cost=0.16..12.19 rows=1 width=4) (actual time=0.008..0.009 rows=1 loops=1)
                                  Index Cond: ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer)
                                  Filter: "IsActive"
                                  Buffers: shared hit=2
                    ->  Materialize  (cost=0.14..8.17 rows=1 width=5) (actual time=0.000..0.000 rows=1 loops=2265)
                          Buffers: shared hit=2
                          ->  Index Scan using "PK_Organizations" on "Organizations" o  (cost=0.14..8.16 rows=1 width=5) (actual time=0.007..0.007 rows=1 loops=1)
                                Index Cond: ("Id" = 2)
                                Filter: "IsActive"
                                Buffers: shared hit=2
              ->  Nested Loop  (cost=21.79..402.76 rows=208 width=4) (actual time=0.062..0.364 rows=406 loops=1)
                    Buffers: shared hit=81
                    ->  Index Scan using "PK_Organizations" on "Organizations" o1  (cost=0.14..8.16 rows=1 width=4) (actual time=0.010..0.010 rows=1 loops=1)
                          Index Cond: ("Id" = 2)
                          Filter: ("IsActive" AND "IsActive")
                          Buffers: shared hit=2
                    ->  Bitmap Heap Scan on "AbsenceDays" a  (cost=21.65..392.52 rows=208 width=8) (actual time=0.050..0.321 rows=406 loops=1)
                          Recheck Cond: (("OrganizationId" = 2) AND ("Date" >= '2026-09-01'::date) AND ("Date" <= '2026-09-30'::date))
                          Filter: ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 2))
                          Heap Blocks: exact=75
                          Buffers: shared hit=79
                          ->  Bitmap Index Scan on "IX_AbsenceDays_OrganizationId_Date"  (cost=0.00..9.40 rows=409 width=0) (actual time=0.024..0.024 rows=406 loops=1)
                                Index Cond: (("OrganizationId" = 2) AND ("Date" >= '2026-09-01'::date) AND ("Date" <= '2026-09-30'::date))
                                Buffers: shared hit=2
                          SubPlan 2
                            ->  Index Scan using "IX_UserOrganizations_UserId_OrganizationId" on "UserOrganizations" uo_1  (cost=0.16..12.19 rows=1 width=4) (actual time=0.008..0.009 rows=1 loops=1)
                                  Index Cond: ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer)
                                  Filter: "IsActive"
                                  Buffers: shared hit=2
Planning Time: 0.429 ms
Execution Time: 3.698 ms
```

2.5 ms in the call:

```sql
SELECT t."UserId", t."StartTime", t."EndTime", t."IsRunning", t."IsFailure", t."PauseDurationMinutes"
FROM "TimeEntries" AS t
LEFT JOIN (
    SELECT o."Id", o."IsActive"
    FROM "Organizations" AS o
    WHERE o."IsActive"
) AS o0 ON t."OrganizationId" = o0."Id"
WHERE (t."OrganizationId" IS NULL OR o0."IsActive") AND t."OrganizationId" = @org_Id AND ((NOT (t."IsRunning") AND t."UserId" = ANY (@startIds) AND t."StartTime" >= @fromUtc AND t."StartTime" < @toUtc) OR (t."IsRunning" AND t."UserId" = ANY (@runningIds) AND t."StartTime" < @tomorrowUtc))
```

```text
Nested Loop Left Join  (cost=91.73..3326.06 rows=608 width=26) (actual time=0.204..2.517 rows=2265 loops=1)
  Filter: ((t."OrganizationId" IS NULL) OR o."IsActive")
  Buffers: shared hit=138
  ->  Bitmap Heap Scan on "TimeEntries" t  (cost=91.59..3302.71 rows=1215 width=30) (actual time=0.189..1.908 rows=2265 loops=1)
        Recheck Cond: ((("OrganizationId" = 2) AND ("StartTime" >= '2026-08-31 22:00:00+00'::timestamp with time zone) AND ("StartTime" < '2026-09-30 22:00:00+00'::timestamp with time zone)) OR ("UserId" = ANY ('{}'::integer[])))
        Filter: ((((NOT "IsRunning") AND ("UserId" = ANY ('{15,16,17,18,19,20,21,22,23,24,25,26,27,28,29,30,31,32,33,34,35,36,37,38,39,40,41,42,43,44,45,46,47,48,49,50,51,52,53,54,55,56,57,58,59,60,61,62,63,64,65,66,67,68,69,70,71,72,73,74,75,76,77,78,79,80,81,82,83,84,85,86,87,88,89,90,91,92,93,94,95,96,97,98,99,100,101,102,103,104,105,106,107,108,109,110,111,112,113,114}'::integer[])) AND ("StartTime" >= '2026-08-31 22:00:00+00'::timestamp with time zone) AND ("StartTime" < '2026-09-30 22:00:00+00'::timestamp with time zone)) OR ("IsRunning" AND ("UserId" = ANY ('{}'::integer[])) AND ("StartTime" < '2026-10-19 22:00:00+00'::timestamp with time zone))) AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 1)))
        Heap Blocks: exact=123
        Buffers: shared hit=136
        ->  BitmapOr  (cost=79.15..79.15 rows=2399 width=0) (actual time=0.130..0.131 rows=0 loops=1)
              Buffers: shared hit=11
              ->  Bitmap Index Scan on "IX_TimeEntries_OrganizationId_StartTime"  (cost=0.00..74.41 rows=2399 width=0) (actual time=0.130..0.130 rows=2265 loops=1)
                    Index Cond: (("OrganizationId" = 2) AND ("StartTime" >= '2026-08-31 22:00:00+00'::timestamp with time zone) AND ("StartTime" < '2026-09-30 22:00:00+00'::timestamp with time zone))
                    Buffers: shared hit=11
              ->  Bitmap Index Scan on "IX_TimeEntries_UserId"  (cost=0.00..4.13 rows=1 width=0) (actual time=0.000..0.000 rows=0 loops=1)
                    Index Cond: ("UserId" = ANY ('{}'::integer[]))
        SubPlan 1
          ->  Index Scan using "IX_UserOrganizations_UserId_OrganizationId" on "UserOrganizations" uo  (cost=0.16..12.19 rows=1 width=4) (actual time=0.007..0.007 rows=1 loops=1)
                Index Cond: ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer)
                Filter: "IsActive"
                Buffers: shared hit=2
  ->  Materialize  (cost=0.14..8.17 rows=1 width=5) (actual time=0.000..0.000 rows=1 loops=2265)
        Buffers: shared hit=2
        ->  Index Scan using "PK_Organizations" on "Organizations" o  (cost=0.14..8.16 rows=1 width=5) (actual time=0.009..0.009 rows=1 loops=1)
              Index Cond: ("Id" = 2)
              Filter: "IsActive"
              Buffers: shared hit=2
Planning Time: 0.448 ms
Execution Time: 2.682 ms
```

### monthly PDF — p50 56.0 ms, 11 SQL

3.0 ms in the call:

```sql
SELECT u."UserId", u."BalanceVersion", o."BalanceVersion", (
    SELECT b."Month"
    FROM "BalanceCheckpoints" AS b
    WHERE b."OrganizationId" = @orgId AND b."Algorithm" = 1 AND b."Month" < @bound AND b."UserId" = u."UserId" AND b."Version" = u."BalanceVersion" AND b."OrgVersion" = o."BalanceVersion"
    ORDER BY b."Month" DESC
    LIMIT 1), (
    SELECT b0."OvertimeDelta"
    FROM "BalanceCheckpoints" AS b0
    WHERE b0."OrganizationId" = @orgId AND b0."Algorithm" = 1 AND b0."Month" < @bound AND b0."UserId" = u."UserId" AND b0."Version" = u."BalanceVersion" AND b0."OrgVersion" = o."BalanceVersion"
    ORDER BY b0."Month" DESC
    LIMIT 1), (
    SELECT b1."FirstDay"
    FROM "BalanceCheckpoints" AS b1
    WHERE b1."OrganizationId" = @orgId AND b1."Algorithm" = 1 AND b1."Month" < @bound AND b1."UserId" = u."UserId" AND b1."Version" = u."BalanceVersion" AND b1."OrgVersion" = o."BalanceVersion"
    ORDER BY b1."Month" DESC
    LIMIT 1), (
    SELECT max(b2."Month")
    FROM "BalanceCheckpoints" AS b2
    WHERE b2."OrganizationId" = @orgId AND b2."Algorithm" = 1 AND b2."UserId" = u."UserId" AND b2."Version" = u."BalanceVersion" AND b2."OrgVersion" = o."BalanceVersion"), (
    SELECT min(t."StartTime")
    FROM "TimeEntries" AS t
    WHERE t."OrganizationId" = @orgId9 AND t."UserId" = u."UserId" AND NOT (t."IsRunning")), (
    SELECT count(*)::int
    FROM "TimeEntries" AS t0
    WHERE t0."OrganizationId" = @orgId9 AND t0."UserId" = u."UserId" AND t0."IsFailure" AND t0."StartTime" < @failuresBeforeUtc), u."Id", o."Id", w0."Id", w0."CreatedAt", w0."OrganizationId", w0."TargetFri", w0."TargetMon", w0."TargetSat", w0."TargetSun", w0."TargetThu", w0."TargetTue", w0."TargetWed", w0."UserId", w0."ValidFrom", w0."ValidTo", w0."WeeklyWorkHours"
FROM "UserOrganizations" AS u
INNER JOIN "Organizations" AS o ON u."OrganizationId" = o."Id"
LEFT JOIN (
    SELECT w."Id", w."CreatedAt", w."OrganizationId", w."TargetFri", w."TargetMon", w."TargetSat", w."TargetSun", w."TargetThu", w."TargetTue", w."TargetWed", w."UserId", w."ValidFrom", w."ValidTo", w."WeeklyWorkHours"
    FROM "WorkSchedules" AS w
    WHERE w."OrganizationId" = @orgId
) AS w0 ON u."UserId" = w0."UserId"
WHERE u."OrganizationId" = @orgId AND u."UserId" = ANY (@ids)
ORDER BY u."Id", o."Id", w0."ValidFrom"
```

```text
Sort  (cost=142.48..142.49 rows=1 width=144) (actual time=0.276..0.279 rows=4 loops=1)
  Sort Key: u."Id", w."ValidFrom"
  Sort Method: quicksort  Memory: 25kB
  Buffers: shared hit=85
  ->  Nested Loop Left Join  (cost=0.57..142.47 rows=1 width=144) (actual time=0.203..0.266 rows=4 loops=1)
        Buffers: shared hit=85
        ->  Nested Loop  (cost=0.29..16.34 rows=1 width=20) (actual time=0.024..0.025 rows=1 loops=1)
              Buffers: shared hit=4
              ->  Index Scan using "IX_UserOrganizations_UserId_OrganizationId" on "UserOrganizations" u  (cost=0.15..8.17 rows=1 width=16) (actual time=0.014..0.015 rows=1 loops=1)
                    Index Cond: (("UserId" = ANY ('{21}'::integer[])) AND ("OrganizationId" = 2))
                    Buffers: shared hit=2
              ->  Index Scan using "PK_Organizations" on "Organizations" o  (cost=0.14..8.16 rows=1 width=8) (actual time=0.008..0.008 rows=1 loops=1)
                    Index Cond: ("Id" = 2)
                    Buffers: shared hit=2
        ->  Index Scan using "IX_WorkSchedules_UserId_OrganizationId_ValidFrom" on "WorkSchedules" w  (cost=0.28..8.29 rows=1 width=92) (actual time=0.010..0.012 rows=4 loops=1)
              Index Cond: (("UserId" = u."UserId") AND ("OrganizationId" = 2))
              Buffers: shared hit=3
        SubPlan 2
          ->  Limit  (cost=12.48..20.53 rows=1 width=4) (actual time=0.009..0.009 rows=1 loops=4)
                Buffers: shared hit=14
                ->  Index Scan Backward using "IX_BalanceCheckpoints_OrganizationId_UserId_Month" on "BalanceCheckpoints" b  (cost=12.48..20.53 rows=1 width=4) (actual time=0.008..0.008 rows=1 loops=4)
                      Index Cond: (("OrganizationId" = 2) AND ("UserId" = u."UserId") AND ("Month" < '2026-09-01'::date))
                      Filter: (("Algorithm" = 1) AND ("Version" = u."BalanceVersion") AND ("OrgVersion" = o."BalanceVersion") AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 1)))
                      Buffers: shared hit=14
                      SubPlan 1
                        ->  Index Scan using "IX_UserOrganizations_UserId_OrganizationId" on "UserOrganizations" uo  (cost=0.16..12.19 rows=1 width=4) (actual time=0.006..0.007 rows=1 loops=1)
                              Index Cond: ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer)
                              Filter: "IsActive"
                              Buffers: shared hit=2
        SubPlan 4
          ->  Limit  (cost=12.48..20.53 rows=1 width=12) (actual time=0.015..0.015 rows=1 loops=4)
                Buffers: shared hit=14
                ->  Index Scan Backward using "IX_BalanceCheckpoints_OrganizationId_UserId_Month" on "BalanceCheckpoints" b0  (cost=12.48..20.53 rows=1 width=12) (actual time=0.014..0.014 rows=1 loops=4)
                      Index Cond: (("OrganizationId" = 2) AND ("UserId" = u."UserId") AND ("Month" < '2026-09-01'::date))
                      Filter: (("Algorithm" = 1) AND ("Version" = u."BalanceVersion") AND ("OrgVersion" = o."BalanceVersion") AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 3)))
                      Buffers: shared hit=14
                      SubPlan 3
                        ->  Index Scan using "IX_UserOrganizations_UserId_OrganizationId" on "UserOrganizations" uo_1  (cost=0.16..12.19 rows=1 width=4) (actual time=0.007..0.007 rows=1 loops=1)
                              Index Cond: ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer)
                              Filter: "IsActive"
                              Buffers: shared hit=2
        SubPlan 6
          ->  Limit  (cost=12.48..20.53 rows=1 width=8) (actual time=0.010..0.010 rows=1 loops=4)
                Buffers: shared hit=14
                ->  Index Scan Backward using "IX_BalanceCheckpoints_OrganizationId_UserId_Month" on "BalanceCheckpoints" b1  (cost=12.48..20.53 rows=1 width=8) (actual time=0.007..0.007 rows=1 loops=4)
                      Index Cond: (("OrganizationId" = 2) AND ("UserId" = u."UserId") AND ("Month" < '2026-09-01'::date))
                      Filter: (("Algorithm" = 1) AND ("Version" = u."BalanceVersion") AND ("OrgVersion" = o."BalanceVersion") AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 5)))
                      Buffers: shared hit=14
                      SubPlan 5
                        ->  Index Scan using "IX_UserOrganizations_UserId_OrganizationId" on "UserOrganizations" uo_2  (cost=0.16..12.19 rows=1 width=4) (actual time=0.006..0.006 rows=1 loops=1)
                              Index Cond: ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer)
                              Filter: "IsActive"
                              Buffers: shared hit=2
        SubPlan 9
          ->  Result  (cost=20.53..20.54 rows=1 width=4) (actual time=0.007..0.007 rows=1 loops=4)
                Buffers: shared hit=14
                InitPlan 8 (returns $16)
                  ->  Limit  (cost=12.48..20.53 rows=1 width=4) (actual time=0.006..0.006 rows=1 loops=4)
                        Buffers: shared hit=14
                        ->  Index Scan Backward using "IX_BalanceCheckpoints_OrganizationId_UserId_Month" on "BalanceCheckpoints" b2  (cost=12.48..20.53 rows=1 width=4) (actual time=0.006..0.006 rows=1 loops=4)
                              Index Cond: (("OrganizationId" = 2) AND ("UserId" = u."UserId") AND ("Month" IS NOT NULL))
                              Filter: (("Algorithm" = 1) AND ("Version" = u."BalanceVersion") AND ("OrgVersion" = o."BalanceVersion") AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 7)))
                              Buffers: shared hit=14
                              SubPlan 7
                                ->  Index Scan using "IX_UserOrganizations_UserId_OrganizationId" on "UserOrganizations" uo_3  (cost=0.16..12.19 rows=1 width=4) (actual time=0.005..0.005 rows=1 loops=1)
                                      Index Cond: ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer)
                                      Filter: "IsActive"
                                      Buffers: shared hit=2
        SubPlan 12
          ->  Result  (cost=15.30..15.31 rows=1 width=8) (actual time=0.010..0.010 rows=1 loops=4)
                Buffers: shared hit=18
                InitPlan 11 (returns $19)
                  ->  Limit  (cost=12.62..15.30 rows=1 width=8) (actual time=0.009..0.009 rows=1 loops=4)
                        Buffers: shared hit=18
                        ->  Index Scan using "IX_TimeEntries_UserId_StartTime" on "TimeEntries" t  (cost=12.62..3066.98 rows=1136 width=8) (actual time=0.009..0.009 rows=1 loops=4)
                              Index Cond: (("UserId" = u."UserId") AND ("StartTime" IS NOT NULL))
                              Filter: ((NOT "IsRunning") AND ("OrganizationId" = 2) AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 10)))
                              Buffers: shared hit=18
                              SubPlan 10
                                ->  Index Scan using "IX_UserOrganizations_UserId_OrganizationId" on "UserOrganizations" uo_4  (cost=0.16..12.19 rows=1 width=4) (actual time=0.005..0.006 rows=1 loops=1)
                                      Index Cond: ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer)
                                      Filter: "IsActive"
                                      Buffers: shared hit=2
        SubPlan 14
          ->  Aggregate  (cost=20.38..20.39 rows=1 width=4) (actual time=0.002..0.003 rows=1 loops=4)
                Buffers: shared hit=4
                ->  Index Scan using "IX_TimeEntries_UserId_Failure" on "TimeEntries" t0  (cost=12.33..20.37 rows=1 width=0) (actual time=0.002..0.002 rows=0 loops=4)
                      Index Cond: ("UserId" = u."UserId")
                      Filter: (("StartTime" < '1900-01-01 00:00:00+00'::timestamp with time zone) AND ("OrganizationId" = 2) AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 13)))
                      Buffers: shared hit=4
                      SubPlan 13
                        ->  Index Scan using "IX_UserOrganizations_UserId_OrganizationId" on "UserOrganizations" uo_5  (cost=0.16..12.19 rows=1 width=4) (never executed)
                              Index Cond: ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer)
                              Filter: "IsActive"
Planning Time: 1.037 ms
Execution Time: 0.595 ms
```

2.3 ms in the call:

```sql
SELECT g."Id", g."AbsenceSelfServiceMode", g."Color", g."CoreHoursEnd", g."CoreHoursMode", g."CoreHoursStart", g."CoreHoursToleranceMinutes", g."CreatedAt", g."CsvImportMode", g."Description", g."EditPastEntriesMode", g."EditPauseMode", g."GroupAdminCanApproveAbsences", g."GroupAdminCanApproveScheduleChanges", g."GroupAdminCanApproveTimeEdits", g."GroupAdminCanExportData", g."GroupAdminCanManageMembers", g."GroupAdminCanViewAllTimeEntries", g."InitialOvertimeMode", g."IsActive", g."Name", g."OrganizationId", g."ParentGroupId", g."UpdatedAt", g."VisibilityAllowInGroups", g."VisibilityAllowOutsideGroups", g."VisibilityDenyInGroups", g."VisibilityDenyOutsideGroups", g."WorkScheduleChangeMode", o0."Id", g0."Id", g0."GroupId", g0."JoinedAt", g0."Role", g0."UserId"
FROM "Groups" AS g
INNER JOIN (
    SELECT o."Id", o."IsActive"
    FROM "Organizations" AS o
    WHERE o."IsActive"
) AS o0 ON g."OrganizationId" = o0."Id"
LEFT JOIN "GroupMembers" AS g0 ON g."Id" = g0."GroupId"
WHERE g."IsActive" AND o0."IsActive" AND g."OrganizationId" = @orgId AND g."IsActive"
ORDER BY g."Id", o0."Id"
```

```text
Sort  (cost=31.00..31.03 rows=12 width=609) (actual time=0.201..0.206 rows=110 loops=1)
  Sort Key: g."Id"
  Sort Method: quicksort  Memory: 38kB
  Buffers: shared hit=82
  ->  Nested Loop Left Join  (cost=4.50..30.78 rows=12 width=609) (actual time=0.029..0.132 rows=110 loops=1)
        Buffers: shared hit=82
        ->  Nested Loop  (cost=0.29..16.33 rows=1 width=585) (actual time=0.022..0.052 rows=20 loops=1)
              Buffers: shared hit=42
              ->  Index Scan using "IX_Groups_OrganizationId" on "Groups" g  (cost=0.14..8.16 rows=1 width=581) (actual time=0.013..0.022 rows=20 loops=1)
                    Index Cond: ("OrganizationId" = 2)
                    Filter: ("IsActive" AND "IsActive")
                    Buffers: shared hit=2
              ->  Index Scan using "PK_Organizations" on "Organizations" o  (cost=0.14..8.16 rows=1 width=4) (actual time=0.001..0.001 rows=1 loops=20)
                    Index Cond: ("Id" = 2)
                    Filter: ("IsActive" AND "IsActive")
                    Buffers: shared hit=40
        ->  Bitmap Heap Scan on "GroupMembers" g0  (cost=4.21..14.37 rows=8 width=24) (actual time=0.002..0.002 rows=6 loops=20)
              Recheck Cond: (g."Id" = "GroupId")
              Heap Blocks: exact=20
              Buffers: shared hit=40
              ->  Bitmap Index Scan on "IX_GroupMembers_GroupId_UserId"  (cost=0.00..4.21 rows=8 width=0) (actual time=0.001..0.001 rows=6 loops=20)
                    Index Cond: ("GroupId" = g."Id")
                    Buffers: shared hit=20
Planning Time: 0.199 ms
Execution Time: 0.292 ms
```

1.4 ms in the call:

```sql
SELECT u."Role", u."IsAccountant", o0."Require2fa", u0."TwoFactorEnabled"
FROM "UserOrganizations" AS u
INNER JOIN (
    SELECT o."Id", o."Require2fa", o."Slug"
    FROM "Organizations" AS o
    WHERE o."IsActive"
) AS o0 ON u."OrganizationId" = o0."Id"
INNER JOIN "Users" AS u0 ON u."UserId" = u0."Id"
WHERE u."IsActive" AND o0."Slug" = @slug AND u."UserId" = @userId
LIMIT 1
```

```text
Limit  (cost=4.44..25.84 rows=1 width=7) (actual time=0.031..0.032 rows=1 loops=1)
  Buffers: shared hit=6
  ->  Nested Loop  (cost=4.44..25.84 rows=1 width=7) (actual time=0.031..0.031 rows=1 loops=1)
        Buffers: shared hit=6
        ->  Nested Loop  (cost=4.30..17.67 rows=1 width=10) (actual time=0.021..0.021 rows=1 loops=1)
              Join Filter: (u."OrganizationId" = o."Id")
              Buffers: shared hit=4
              ->  Bitmap Heap Scan on "UserOrganizations" u  (cost=4.16..9.50 rows=1 width=13) (actual time=0.008..0.008 rows=1 loops=1)
                    Recheck Cond: ("UserId" = 15)
                    Filter: "IsActive"
                    Heap Blocks: exact=1
                    Buffers: shared hit=2
                    ->  Bitmap Index Scan on "IX_UserOrganizations_UserId_OrganizationId"  (cost=0.00..4.16 rows=2 width=0) (actual time=0.004..0.005 rows=1 loops=1)
                          Index Cond: ("UserId" = 15)
                          Buffers: shared hit=1
              ->  Index Scan using "IX_Organizations_Slug" on "Organizations" o  (cost=0.14..8.16 rows=1 width=5) (actual time=0.012..0.012 rows=1 loops=1)
                    Index Cond: (("Slug")::text = 'perf-100-10'::text)
                    Buffers: shared hit=2
        ->  Index Scan using "PK_Users" on "Users" u0  (cost=0.14..8.16 rows=1 width=5) (actual time=0.009..0.009 rows=1 loops=1)
              Index Cond: ("Id" = 15)
              Buffers: shared hit=2
Planning Time: 0.176 ms
Execution Time: 0.083 ms
```

### daily CSV month — p50 48.7 ms, 18 SQL

3.4 ms in the call:

```sql
SELECT u."UserId", u."BalanceVersion", o."BalanceVersion", (
    SELECT b."Month"
    FROM "BalanceCheckpoints" AS b
    WHERE b."OrganizationId" = @orgId AND b."Algorithm" = 1 AND b."Month" < @bound AND b."UserId" = u."UserId" AND b."Version" = u."BalanceVersion" AND b."OrgVersion" = o."BalanceVersion"
    ORDER BY b."Month" DESC
    LIMIT 1), (
    SELECT b0."OvertimeDelta"
    FROM "BalanceCheckpoints" AS b0
    WHERE b0."OrganizationId" = @orgId AND b0."Algorithm" = 1 AND b0."Month" < @bound AND b0."UserId" = u."UserId" AND b0."Version" = u."BalanceVersion" AND b0."OrgVersion" = o."BalanceVersion"
    ORDER BY b0."Month" DESC
    LIMIT 1), (
    SELECT b1."FirstDay"
    FROM "BalanceCheckpoints" AS b1
    WHERE b1."OrganizationId" = @orgId AND b1."Algorithm" = 1 AND b1."Month" < @bound AND b1."UserId" = u."UserId" AND b1."Version" = u."BalanceVersion" AND b1."OrgVersion" = o."BalanceVersion"
    ORDER BY b1."Month" DESC
    LIMIT 1), (
    SELECT max(b2."Month")
    FROM "BalanceCheckpoints" AS b2
    WHERE b2."OrganizationId" = @orgId AND b2."Algorithm" = 1 AND b2."UserId" = u."UserId" AND b2."Version" = u."BalanceVersion" AND b2."OrgVersion" = o."BalanceVersion"), (
    SELECT min(t."StartTime")
    FROM "TimeEntries" AS t
    WHERE t."OrganizationId" = @orgId9 AND t."UserId" = u."UserId" AND NOT (t."IsRunning")), (
    SELECT count(*)::int
    FROM "TimeEntries" AS t0
    WHERE t0."OrganizationId" = @orgId9 AND t0."UserId" = u."UserId" AND t0."IsFailure" AND t0."StartTime" < @failuresBeforeUtc), u."Id", o."Id", w0."Id", w0."CreatedAt", w0."OrganizationId", w0."TargetFri", w0."TargetMon", w0."TargetSat", w0."TargetSun", w0."TargetThu", w0."TargetTue", w0."TargetWed", w0."UserId", w0."ValidFrom", w0."ValidTo", w0."WeeklyWorkHours"
FROM "UserOrganizations" AS u
INNER JOIN "Organizations" AS o ON u."OrganizationId" = o."Id"
LEFT JOIN (
    SELECT w."Id", w."CreatedAt", w."OrganizationId", w."TargetFri", w."TargetMon", w."TargetSat", w."TargetSun", w."TargetThu", w."TargetTue", w."TargetWed", w."UserId", w."ValidFrom", w."ValidTo", w."WeeklyWorkHours"
    FROM "WorkSchedules" AS w
    WHERE w."OrganizationId" = @orgId
) AS w0 ON u."UserId" = w0."UserId"
WHERE u."OrganizationId" = @orgId AND u."UserId" = ANY (@ids)
ORDER BY u."Id", o."Id", w0."ValidFrom"
```

```text
Sort  (cost=142.48..142.49 rows=1 width=144) (actual time=0.254..0.257 rows=4 loops=1)
  Sort Key: u."Id", w."ValidFrom"
  Sort Method: quicksort  Memory: 25kB
  Buffers: shared hit=85
  ->  Nested Loop Left Join  (cost=0.57..142.47 rows=1 width=144) (actual time=0.181..0.243 rows=4 loops=1)
        Buffers: shared hit=85
        ->  Nested Loop  (cost=0.29..16.34 rows=1 width=20) (actual time=0.024..0.025 rows=1 loops=1)
              Buffers: shared hit=4
              ->  Index Scan using "IX_UserOrganizations_UserId_OrganizationId" on "UserOrganizations" u  (cost=0.15..8.17 rows=1 width=16) (actual time=0.016..0.016 rows=1 loops=1)
                    Index Cond: (("UserId" = ANY ('{21}'::integer[])) AND ("OrganizationId" = 2))
                    Buffers: shared hit=2
              ->  Index Scan using "PK_Organizations" on "Organizations" o  (cost=0.14..8.16 rows=1 width=8) (actual time=0.008..0.008 rows=1 loops=1)
                    Index Cond: ("Id" = 2)
                    Buffers: shared hit=2
        ->  Index Scan using "IX_WorkSchedules_UserId_OrganizationId_ValidFrom" on "WorkSchedules" w  (cost=0.28..8.29 rows=1 width=92) (actual time=0.010..0.012 rows=4 loops=1)
              Index Cond: (("UserId" = u."UserId") AND ("OrganizationId" = 2))
              Buffers: shared hit=3
        SubPlan 2
          ->  Limit  (cost=12.48..20.53 rows=1 width=4) (actual time=0.009..0.009 rows=1 loops=4)
                Buffers: shared hit=14
                ->  Index Scan Backward using "IX_BalanceCheckpoints_OrganizationId_UserId_Month" on "BalanceCheckpoints" b  (cost=12.48..20.53 rows=1 width=4) (actual time=0.008..0.008 rows=1 loops=4)
                      Index Cond: (("OrganizationId" = 2) AND ("UserId" = u."UserId") AND ("Month" < '2026-09-01'::date))
                      Filter: (("Algorithm" = 1) AND ("Version" = u."BalanceVersion") AND ("OrgVersion" = o."BalanceVersion") AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 1)))
                      Buffers: shared hit=14
                      SubPlan 1
                        ->  Index Scan using "IX_UserOrganizations_UserId_OrganizationId" on "UserOrganizations" uo  (cost=0.16..12.19 rows=1 width=4) (actual time=0.006..0.006 rows=1 loops=1)
                              Index Cond: ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer)
                              Filter: "IsActive"
                              Buffers: shared hit=2
        SubPlan 4
          ->  Limit  (cost=12.48..20.53 rows=1 width=12) (actual time=0.006..0.006 rows=1 loops=4)
                Buffers: shared hit=14
                ->  Index Scan Backward using "IX_BalanceCheckpoints_OrganizationId_UserId_Month" on "BalanceCheckpoints" b0  (cost=12.48..20.53 rows=1 width=12) (actual time=0.006..0.006 rows=1 loops=4)
                      Index Cond: (("OrganizationId" = 2) AND ("UserId" = u."UserId") AND ("Month" < '2026-09-01'::date))
                      Filter: (("Algorithm" = 1) AND ("Version" = u."BalanceVersion") AND ("OrgVersion" = o."BalanceVersion") AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 3)))
                      Buffers: shared hit=14
                      SubPlan 3
                        ->  Index Scan using "IX_UserOrganizations_UserId_OrganizationId" on "UserOrganizations" uo_1  (cost=0.16..12.19 rows=1 width=4) (actual time=0.005..0.005 rows=1 loops=1)
                              Index Cond: ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer)
                              Filter: "IsActive"
                              Buffers: shared hit=2
        SubPlan 6
          ->  Limit  (cost=12.48..20.53 rows=1 width=8) (actual time=0.006..0.006 rows=1 loops=4)
                Buffers: shared hit=14
                ->  Index Scan Backward using "IX_BalanceCheckpoints_OrganizationId_UserId_Month" on "BalanceCheckpoints" b1  (cost=12.48..20.53 rows=1 width=8) (actual time=0.006..0.006 rows=1 loops=4)
                      Index Cond: (("OrganizationId" = 2) AND ("UserId" = u."UserId") AND ("Month" < '2026-09-01'::date))
                      Filter: (("Algorithm" = 1) AND ("Version" = u."BalanceVersion") AND ("OrgVersion" = o."BalanceVersion") AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 5)))
                      Buffers: shared hit=14
                      SubPlan 5
                        ->  Index Scan using "IX_UserOrganizations_UserId_OrganizationId" on "UserOrganizations" uo_2  (cost=0.16..12.19 rows=1 width=4) (actual time=0.006..0.006 rows=1 loops=1)
                              Index Cond: ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer)
                              Filter: "IsActive"
                              Buffers: shared hit=2
        SubPlan 9
          ->  Result  (cost=20.53..20.54 rows=1 width=4) (actual time=0.015..0.015 rows=1 loops=4)
                Buffers: shared hit=14
                InitPlan 8 (returns $16)
                  ->  Limit  (cost=12.48..20.53 rows=1 width=4) (actual time=0.014..0.014 rows=1 loops=4)
                        Buffers: shared hit=14
                        ->  Index Scan Backward using "IX_BalanceCheckpoints_OrganizationId_UserId_Month" on "BalanceCheckpoints" b2  (cost=12.48..20.53 rows=1 width=4) (actual time=0.014..0.014 rows=1 loops=4)
                              Index Cond: (("OrganizationId" = 2) AND ("UserId" = u."UserId") AND ("Month" IS NOT NULL))
                              Filter: (("Algorithm" = 1) AND ("Version" = u."BalanceVersion") AND ("OrgVersion" = o."BalanceVersion") AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 7)))
                              Buffers: shared hit=14
                              SubPlan 7
                                ->  Index Scan using "IX_UserOrganizations_UserId_OrganizationId" on "UserOrganizations" uo_3  (cost=0.16..12.19 rows=1 width=4) (actual time=0.007..0.007 rows=1 loops=1)
                                      Index Cond: ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer)
                                      Filter: "IsActive"
                                      Buffers: shared hit=2
        SubPlan 12
          ->  Result  (cost=15.30..15.31 rows=1 width=8) (actual time=0.009..0.009 rows=1 loops=4)
                Buffers: shared hit=18
                InitPlan 11 (returns $19)
                  ->  Limit  (cost=12.62..15.30 rows=1 width=8) (actual time=0.009..0.009 rows=1 loops=4)
                        Buffers: shared hit=18
                        ->  Index Scan using "IX_TimeEntries_UserId_StartTime" on "TimeEntries" t  (cost=12.62..3066.98 rows=1136 width=8) (actual time=0.009..0.009 rows=1 loops=4)
                              Index Cond: (("UserId" = u."UserId") AND ("StartTime" IS NOT NULL))
                              Filter: ((NOT "IsRunning") AND ("OrganizationId" = 2) AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 10)))
                              Buffers: shared hit=18
                              SubPlan 10
                                ->  Index Scan using "IX_UserOrganizations_UserId_OrganizationId" on "UserOrganizations" uo_4  (cost=0.16..12.19 rows=1 width=4) (actual time=0.005..0.006 rows=1 loops=1)
                                      Index Cond: ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer)
                                      Filter: "IsActive"
                                      Buffers: shared hit=2
        SubPlan 14
          ->  Aggregate  (cost=20.38..20.39 rows=1 width=4) (actual time=0.002..0.002 rows=1 loops=4)
                Buffers: shared hit=4
                ->  Index Scan using "IX_TimeEntries_UserId_Failure" on "TimeEntries" t0  (cost=12.33..20.37 rows=1 width=0) (actual time=0.002..0.002 rows=0 loops=4)
                      Index Cond: ("UserId" = u."UserId")
                      Filter: (("StartTime" < '1900-01-01 00:00:00+00'::timestamp with time zone) AND ("OrganizationId" = 2) AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 13)))
                      Buffers: shared hit=4
                      SubPlan 13
                        ->  Index Scan using "IX_UserOrganizations_UserId_OrganizationId" on "UserOrganizations" uo_5  (cost=0.16..12.19 rows=1 width=4) (never executed)
                              Index Cond: ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer)
                              Filter: "IsActive"
Planning Time: 0.951 ms
Execution Time: 0.535 ms
```

2.0 ms in the call:

```sql
SELECT g."Id", g."AbsenceSelfServiceMode", g."Color", g."CoreHoursEnd", g."CoreHoursMode", g."CoreHoursStart", g."CoreHoursToleranceMinutes", g."CreatedAt", g."CsvImportMode", g."Description", g."EditPastEntriesMode", g."EditPauseMode", g."GroupAdminCanApproveAbsences", g."GroupAdminCanApproveScheduleChanges", g."GroupAdminCanApproveTimeEdits", g."GroupAdminCanExportData", g."GroupAdminCanManageMembers", g."GroupAdminCanViewAllTimeEntries", g."InitialOvertimeMode", g."IsActive", g."Name", g."OrganizationId", g."ParentGroupId", g."UpdatedAt", g."VisibilityAllowInGroups", g."VisibilityAllowOutsideGroups", g."VisibilityDenyInGroups", g."VisibilityDenyOutsideGroups", g."WorkScheduleChangeMode", o0."Id", g0."Id", g0."GroupId", g0."JoinedAt", g0."Role", g0."UserId"
FROM "Groups" AS g
INNER JOIN (
    SELECT o."Id", o."IsActive"
    FROM "Organizations" AS o
    WHERE o."IsActive"
) AS o0 ON g."OrganizationId" = o0."Id"
LEFT JOIN "GroupMembers" AS g0 ON g."Id" = g0."GroupId"
WHERE g."IsActive" AND o0."IsActive" AND g."OrganizationId" = @orgId AND g."IsActive"
ORDER BY g."Id", o0."Id"
```

```text
Sort  (cost=31.00..31.03 rows=12 width=609) (actual time=0.192..0.197 rows=110 loops=1)
  Sort Key: g."Id"
  Sort Method: quicksort  Memory: 38kB
  Buffers: shared hit=82
  ->  Nested Loop Left Join  (cost=4.50..30.78 rows=12 width=609) (actual time=0.034..0.137 rows=110 loops=1)
        Buffers: shared hit=82
        ->  Nested Loop  (cost=0.29..16.33 rows=1 width=585) (actual time=0.026..0.056 rows=20 loops=1)
              Buffers: shared hit=42
              ->  Index Scan using "IX_Groups_OrganizationId" on "Groups" g  (cost=0.14..8.16 rows=1 width=581) (actual time=0.014..0.023 rows=20 loops=1)
                    Index Cond: ("OrganizationId" = 2)
                    Filter: ("IsActive" AND "IsActive")
                    Buffers: shared hit=2
              ->  Index Scan using "PK_Organizations" on "Organizations" o  (cost=0.14..8.16 rows=1 width=4) (actual time=0.001..0.001 rows=1 loops=20)
                    Index Cond: ("Id" = 2)
                    Filter: ("IsActive" AND "IsActive")
                    Buffers: shared hit=40
        ->  Bitmap Heap Scan on "GroupMembers" g0  (cost=4.21..14.37 rows=8 width=24) (actual time=0.002..0.002 rows=6 loops=20)
              Recheck Cond: (g."Id" = "GroupId")
              Heap Blocks: exact=20
              Buffers: shared hit=40
              ->  Bitmap Index Scan on "IX_GroupMembers_GroupId_UserId"  (cost=0.00..4.21 rows=8 width=0) (actual time=0.001..0.001 rows=6 loops=20)
                    Index Cond: ("GroupId" = g."Id")
                    Buffers: shared hit=20
Planning Time: 0.202 ms
Execution Time: 0.291 ms
```

1.9 ms in the call:

```sql
SELECT a."Id", a."CreatedAt", a."CreatedByUserId", a."CreatedViaRequestId", a."CustomTypeId", a."Date", a."IsHalfDay", a."Note", a."OrganizationId", a."Type", a."UserId", s."Id", s."Color", s."CountsAsVacation", s."CreatedAt", s."CreditMode", s."IsActive", s."Name", s."OrganizationId"
FROM "AbsenceDays" AS a
INNER JOIN (
    SELECT o."Id", o."IsActive"
    FROM "Organizations" AS o
    WHERE o."IsActive"
) AS o0 ON a."OrganizationId" = o0."Id"
LEFT JOIN (
    SELECT a0."Id", a0."Color", a0."CountsAsVacation", a0."CreatedAt", a0."CreditMode", a0."IsActive", a0."Name", a0."OrganizationId"
    FROM "AbsenceTypeDefinitions" AS a0
    INNER JOIN (
        SELECT o1."Id", o1."IsActive"
        FROM "Organizations" AS o1
        WHERE o1."IsActive"
    ) AS o2 ON a0."OrganizationId" = o2."Id"
    WHERE o2."IsActive"
) AS s ON a."CustomTypeId" = s."Id"
WHERE o0."IsActive" AND a."UserId" = @userId AND a."OrganizationId" = @orgId AND a."Date" >= @dateFrom AND a."Date" <= @dateTo
```

```text
Nested Loop  (cost=12.91..51.19 rows=2 width=353) (actual time=0.030..0.031 rows=0 loops=1)
  Buffers: shared hit=4
  ->  Index Scan using "PK_Organizations" on "Organizations" o  (cost=0.14..8.16 rows=1 width=4) (actual time=0.015..0.016 rows=1 loops=1)
        Index Cond: ("Id" = 2)
        Filter: ("IsActive" AND "IsActive")
        Buffers: shared hit=2
  ->  Nested Loop Left Join  (cost=12.77..43.01 rows=2 width=353) (actual time=0.014..0.014 rows=0 loops=1)
        Buffers: shared hit=2
        ->  Index Scan using "IX_AbsenceDays_UserId_OrganizationId_Date" on "AbsenceDays" a  (cost=12.48..25.91 rows=2 width=55) (actual time=0.013..0.013 rows=0 loops=1)
              Index Cond: (("UserId" = 21) AND ("OrganizationId" = 2) AND ("Date" >= '2026-09-01'::date) AND ("Date" <= '2026-09-30'::date))
              Filter: ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 1))
              Buffers: shared hit=2
              SubPlan 1
                ->  Index Scan using "IX_UserOrganizations_UserId_OrganizationId" on "UserOrganizations" uo  (cost=0.16..12.19 rows=1 width=4) (never executed)
                      Index Cond: ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer)
                      Filter: "IsActive"
        ->  Nested Loop  (cost=0.29..8.54 rows=1 width=298) (never executed)
              ->  Index Scan using "PK_AbsenceTypeDefinitions" on "AbsenceTypeDefinitions" a0  (cost=0.14..8.16 rows=1 width=298) (never executed)
                    Index Cond: ("Id" = a."CustomTypeId")
              ->  Index Scan using "PK_Organizations" on "Organizations" o1  (cost=0.14..0.35 rows=1 width=4) (never executed)
                    Index Cond: ("Id" = a0."OrganizationId")
                    Filter: ("IsActive" AND "IsActive")
Planning Time: 0.436 ms
Execution Time: 0.118 ms
```

### balance — p50 48.7 ms, 22 SQL

3.0 ms in the call:

```sql
SELECT u."UserId", u."BalanceVersion", o."BalanceVersion", (
    SELECT b."Month"
    FROM "BalanceCheckpoints" AS b
    WHERE b."OrganizationId" = @orgId AND b."Algorithm" = 1 AND b."Month" < @bound AND b."UserId" = u."UserId" AND b."Version" = u."BalanceVersion" AND b."OrgVersion" = o."BalanceVersion"
    ORDER BY b."Month" DESC
    LIMIT 1), (
    SELECT b0."OvertimeDelta"
    FROM "BalanceCheckpoints" AS b0
    WHERE b0."OrganizationId" = @orgId AND b0."Algorithm" = 1 AND b0."Month" < @bound AND b0."UserId" = u."UserId" AND b0."Version" = u."BalanceVersion" AND b0."OrgVersion" = o."BalanceVersion"
    ORDER BY b0."Month" DESC
    LIMIT 1), (
    SELECT b1."FirstDay"
    FROM "BalanceCheckpoints" AS b1
    WHERE b1."OrganizationId" = @orgId AND b1."Algorithm" = 1 AND b1."Month" < @bound AND b1."UserId" = u."UserId" AND b1."Version" = u."BalanceVersion" AND b1."OrgVersion" = o."BalanceVersion"
    ORDER BY b1."Month" DESC
    LIMIT 1), (
    SELECT max(b2."Month")
    FROM "BalanceCheckpoints" AS b2
    WHERE b2."OrganizationId" = @orgId AND b2."Algorithm" = 1 AND b2."UserId" = u."UserId" AND b2."Version" = u."BalanceVersion" AND b2."OrgVersion" = o."BalanceVersion"), (
    SELECT min(t."StartTime")
    FROM "TimeEntries" AS t
    WHERE t."OrganizationId" = @orgId9 AND t."UserId" = u."UserId" AND NOT (t."IsRunning")), (
    SELECT count(*)::int
    FROM "TimeEntries" AS t0
    WHERE t0."OrganizationId" = @orgId9 AND t0."UserId" = u."UserId" AND t0."IsFailure" AND t0."StartTime" < @failuresBeforeUtc), u."Id", o."Id", w0."Id", w0."CreatedAt", w0."OrganizationId", w0."TargetFri", w0."TargetMon", w0."TargetSat", w0."TargetSun", w0."TargetThu", w0."TargetTue", w0."TargetWed", w0."UserId", w0."ValidFrom", w0."ValidTo", w0."WeeklyWorkHours"
FROM "UserOrganizations" AS u
INNER JOIN "Organizations" AS o ON u."OrganizationId" = o."Id"
LEFT JOIN (
    SELECT w."Id", w."CreatedAt", w."OrganizationId", w."TargetFri", w."TargetMon", w."TargetSat", w."TargetSun", w."TargetThu", w."TargetTue", w."TargetWed", w."UserId", w."ValidFrom", w."ValidTo", w."WeeklyWorkHours"
    FROM "WorkSchedules" AS w
    WHERE w."OrganizationId" = @orgId
) AS w0 ON u."UserId" = w0."UserId"
WHERE u."OrganizationId" = @orgId AND u."UserId" = ANY (@ids)
ORDER BY u."Id", o."Id", w0."ValidFrom"
```

```text
Sort  (cost=142.48..142.49 rows=1 width=144) (actual time=0.212..0.215 rows=4 loops=1)
  Sort Key: u."Id", w."ValidFrom"
  Sort Method: quicksort  Memory: 25kB
  Buffers: shared hit=75
  ->  Nested Loop Left Join  (cost=0.57..142.47 rows=1 width=144) (actual time=0.143..0.202 rows=4 loops=1)
        Buffers: shared hit=75
        ->  Nested Loop  (cost=0.29..16.34 rows=1 width=20) (actual time=0.037..0.038 rows=1 loops=1)
              Buffers: shared hit=4
              ->  Index Scan using "IX_UserOrganizations_UserId_OrganizationId" on "UserOrganizations" u  (cost=0.15..8.17 rows=1 width=16) (actual time=0.027..0.028 rows=1 loops=1)
                    Index Cond: (("UserId" = ANY ('{21}'::integer[])) AND ("OrganizationId" = 2))
                    Buffers: shared hit=2
              ->  Index Scan using "PK_Organizations" on "Organizations" o  (cost=0.14..8.16 rows=1 width=8) (actual time=0.009..0.009 rows=1 loops=1)
                    Index Cond: ("Id" = 2)
                    Buffers: shared hit=2
        ->  Index Scan using "IX_WorkSchedules_UserId_OrganizationId_ValidFrom" on "WorkSchedules" w  (cost=0.28..8.29 rows=1 width=92) (actual time=0.011..0.013 rows=4 loops=1)
              Index Cond: (("UserId" = u."UserId") AND ("OrganizationId" = 2))
              Buffers: shared hit=3
        SubPlan 2
          ->  Limit  (cost=12.48..20.53 rows=1 width=4) (actual time=0.007..0.007 rows=1 loops=4)
                Buffers: shared hit=12
                ->  Index Scan Backward using "IX_BalanceCheckpoints_OrganizationId_UserId_Month" on "BalanceCheckpoints" b  (cost=12.48..20.53 rows=1 width=4) (actual time=0.006..0.006 rows=1 loops=4)
                      Index Cond: (("OrganizationId" = 2) AND ("UserId" = u."UserId") AND ("Month" < '2026-10-01'::date))
                      Filter: (("Algorithm" = 1) AND ("Version" = u."BalanceVersion") AND ("OrgVersion" = o."BalanceVersion") AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 1)))
                      Buffers: shared hit=12
                      SubPlan 1
                        ->  Index Scan using "IX_UserOrganizations_UserId_OrganizationId" on "UserOrganizations" uo  (cost=0.16..12.19 rows=1 width=4) (never executed)
                              Index Cond: ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer)
                              Filter: "IsActive"
        SubPlan 4
          ->  Limit  (cost=12.48..20.53 rows=1 width=12) (actual time=0.004..0.004 rows=1 loops=4)
                Buffers: shared hit=12
                ->  Index Scan Backward using "IX_BalanceCheckpoints_OrganizationId_UserId_Month" on "BalanceCheckpoints" b0  (cost=12.48..20.53 rows=1 width=12) (actual time=0.004..0.004 rows=1 loops=4)
                      Index Cond: (("OrganizationId" = 2) AND ("UserId" = u."UserId") AND ("Month" < '2026-10-01'::date))
                      Filter: (("Algorithm" = 1) AND ("Version" = u."BalanceVersion") AND ("OrgVersion" = o."BalanceVersion") AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 3)))
                      Buffers: shared hit=12
                      SubPlan 3
                        ->  Index Scan using "IX_UserOrganizations_UserId_OrganizationId" on "UserOrganizations" uo_1  (cost=0.16..12.19 rows=1 width=4) (never executed)
                              Index Cond: ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer)
                              Filter: "IsActive"
        SubPlan 6
          ->  Limit  (cost=12.48..20.53 rows=1 width=8) (actual time=0.008..0.008 rows=1 loops=4)
                Buffers: shared hit=12
                ->  Index Scan Backward using "IX_BalanceCheckpoints_OrganizationId_UserId_Month" on "BalanceCheckpoints" b1  (cost=12.48..20.53 rows=1 width=8) (actual time=0.008..0.008 rows=1 loops=4)
                      Index Cond: (("OrganizationId" = 2) AND ("UserId" = u."UserId") AND ("Month" < '2026-10-01'::date))
                      Filter: (("Algorithm" = 1) AND ("Version" = u."BalanceVersion") AND ("OrgVersion" = o."BalanceVersion") AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 5)))
                      Buffers: shared hit=12
                      SubPlan 5
                        ->  Index Scan using "IX_UserOrganizations_UserId_OrganizationId" on "UserOrganizations" uo_2  (cost=0.16..12.19 rows=1 width=4) (never executed)
                              Index Cond: ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer)
                              Filter: "IsActive"
        SubPlan 9
          ->  Result  (cost=20.53..20.54 rows=1 width=4) (actual time=0.005..0.005 rows=1 loops=4)
                Buffers: shared hit=12
                InitPlan 8 (returns $16)
                  ->  Limit  (cost=12.48..20.53 rows=1 width=4) (actual time=0.004..0.005 rows=1 loops=4)
                        Buffers: shared hit=12
                        ->  Index Scan Backward using "IX_BalanceCheckpoints_OrganizationId_UserId_Month" on "BalanceCheckpoints" b2  (cost=12.48..20.53 rows=1 width=4) (actual time=0.004..0.004 rows=1 loops=4)
                              Index Cond: (("OrganizationId" = 2) AND ("UserId" = u."UserId") AND ("Month" IS NOT NULL))
                              Filter: (("Algorithm" = 1) AND ("Version" = u."BalanceVersion") AND ("OrgVersion" = o."BalanceVersion") AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 7)))
                              Buffers: shared hit=12
                              SubPlan 7
                                ->  Index Scan using "IX_UserOrganizations_UserId_OrganizationId" on "UserOrganizations" uo_3  (cost=0.16..12.19 rows=1 width=4) (never executed)
                                      Index Cond: ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer)
                                      Filter: "IsActive"
        SubPlan 12
          ->  Result  (cost=15.30..15.31 rows=1 width=8) (actual time=0.008..0.008 rows=1 loops=4)
                Buffers: shared hit=16
                InitPlan 11 (returns $19)
                  ->  Limit  (cost=12.62..15.30 rows=1 width=8) (actual time=0.007..0.007 rows=1 loops=4)
                        Buffers: shared hit=16
                        ->  Index Scan using "IX_TimeEntries_UserId_StartTime" on "TimeEntries" t  (cost=12.62..3066.98 rows=1136 width=8) (actual time=0.007..0.007 rows=1 loops=4)
                              Index Cond: (("UserId" = u."UserId") AND ("StartTime" IS NOT NULL))
                              Filter: ((NOT "IsRunning") AND ("OrganizationId" = 2) AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 10)))
                              Buffers: shared hit=16
                              SubPlan 10
                                ->  Index Scan using "IX_UserOrganizations_UserId_OrganizationId" on "UserOrganizations" uo_4  (cost=0.16..12.19 rows=1 width=4) (never executed)
                                      Index Cond: ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer)
                                      Filter: "IsActive"
        SubPlan 14
          ->  Aggregate  (cost=20.38..20.39 rows=1 width=4) (actual time=0.002..0.002 rows=1 loops=4)
                Buffers: shared hit=4
                ->  Index Scan using "IX_TimeEntries_UserId_Failure" on "TimeEntries" t0  (cost=12.33..20.37 rows=1 width=0) (actual time=0.002..0.002 rows=0 loops=4)
                      Index Cond: ("UserId" = u."UserId")
                      Filter: (("StartTime" < '1900-01-01 00:00:00+00'::timestamp with time zone) AND ("OrganizationId" = 2) AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 13)))
                      Buffers: shared hit=4
                      SubPlan 13
                        ->  Index Scan using "IX_UserOrganizations_UserId_OrganizationId" on "UserOrganizations" uo_5  (cost=0.16..12.19 rows=1 width=4) (never executed)
                              Index Cond: ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer)
                              Filter: "IsActive"
Planning Time: 0.990 ms
Execution Time: 0.468 ms
```

1.5 ms in the call:

```sql
SELECT g."Id", g."AbsenceSelfServiceMode", g."Color", g."CoreHoursEnd", g."CoreHoursMode", g."CoreHoursStart", g."CoreHoursToleranceMinutes", g."CreatedAt", g."CsvImportMode", g."Description", g."EditPastEntriesMode", g."EditPauseMode", g."GroupAdminCanApproveAbsences", g."GroupAdminCanApproveScheduleChanges", g."GroupAdminCanApproveTimeEdits", g."GroupAdminCanExportData", g."GroupAdminCanManageMembers", g."GroupAdminCanViewAllTimeEntries", g."InitialOvertimeMode", g."IsActive", g."Name", g."OrganizationId", g."ParentGroupId", g."UpdatedAt", g."VisibilityAllowInGroups", g."VisibilityAllowOutsideGroups", g."VisibilityDenyInGroups", g."VisibilityDenyOutsideGroups", g."WorkScheduleChangeMode", o0."Id", g0."Id", g0."GroupId", g0."JoinedAt", g0."Role", g0."UserId"
FROM "Groups" AS g
INNER JOIN (
    SELECT o."Id", o."IsActive"
    FROM "Organizations" AS o
    WHERE o."IsActive"
) AS o0 ON g."OrganizationId" = o0."Id"
LEFT JOIN "GroupMembers" AS g0 ON g."Id" = g0."GroupId"
WHERE g."IsActive" AND o0."IsActive" AND g."OrganizationId" = @orgId AND g."IsActive"
ORDER BY g."Id", o0."Id"
```

```text
Sort  (cost=31.00..31.03 rows=12 width=609) (actual time=0.193..0.197 rows=110 loops=1)
  Sort Key: g."Id"
  Sort Method: quicksort  Memory: 38kB
  Buffers: shared hit=82
  ->  Nested Loop Left Join  (cost=4.50..30.78 rows=12 width=609) (actual time=0.035..0.138 rows=110 loops=1)
        Buffers: shared hit=82
        ->  Nested Loop  (cost=0.29..16.33 rows=1 width=585) (actual time=0.026..0.056 rows=20 loops=1)
              Buffers: shared hit=42
              ->  Index Scan using "IX_Groups_OrganizationId" on "Groups" g  (cost=0.14..8.16 rows=1 width=581) (actual time=0.017..0.026 rows=20 loops=1)
                    Index Cond: ("OrganizationId" = 2)
                    Filter: ("IsActive" AND "IsActive")
                    Buffers: shared hit=2
              ->  Index Scan using "PK_Organizations" on "Organizations" o  (cost=0.14..8.16 rows=1 width=4) (actual time=0.001..0.001 rows=1 loops=20)
                    Index Cond: ("Id" = 2)
                    Filter: ("IsActive" AND "IsActive")
                    Buffers: shared hit=40
        ->  Bitmap Heap Scan on "GroupMembers" g0  (cost=4.21..14.37 rows=8 width=24) (actual time=0.002..0.002 rows=6 loops=20)
              Recheck Cond: (g."Id" = "GroupId")
              Heap Blocks: exact=20
              Buffers: shared hit=40
              ->  Bitmap Index Scan on "IX_GroupMembers_GroupId_UserId"  (cost=0.00..4.21 rows=8 width=0) (actual time=0.001..0.001 rows=6 loops=20)
                    Index Cond: ("GroupId" = g."Id")
                    Buffers: shared hit=20
Planning Time: 0.215 ms
Execution Time: 0.295 ms
```

1.4 ms in the call:

```sql
SELECT a."Id", a."CreatedAt", a."CreatedByUserId", a."CreatedViaRequestId", a."CustomTypeId", a."Date", a."IsHalfDay", a."Note", a."OrganizationId", a."Type", a."UserId", s."Id", s."Color", s."CountsAsVacation", s."CreatedAt", s."CreditMode", s."IsActive", s."Name", s."OrganizationId"
FROM "AbsenceDays" AS a
INNER JOIN (
    SELECT o."Id", o."IsActive"
    FROM "Organizations" AS o
    WHERE o."IsActive"
) AS o0 ON a."OrganizationId" = o0."Id"
LEFT JOIN (
    SELECT a0."Id", a0."Color", a0."CountsAsVacation", a0."CreatedAt", a0."CreditMode", a0."IsActive", a0."Name", a0."OrganizationId"
    FROM "AbsenceTypeDefinitions" AS a0
    INNER JOIN (
        SELECT o1."Id", o1."IsActive"
        FROM "Organizations" AS o1
        WHERE o1."IsActive"
    ) AS o2 ON a0."OrganizationId" = o2."Id"
    WHERE o2."IsActive"
) AS s ON a."CustomTypeId" = s."Id"
WHERE o0."IsActive" AND a."UserId" = @userId AND a."OrganizationId" = @orgId AND a."Date" >= @dateFrom AND a."Date" <= @dateTo
```

```text
Nested Loop  (cost=12.91..37.25 rows=1 width=353) (actual time=0.016..0.016 rows=0 loops=1)
  Buffers: shared hit=2
  ->  Nested Loop Left Join  (cost=12.77..29.08 rows=1 width=353) (actual time=0.015..0.016 rows=0 loops=1)
        Buffers: shared hit=2
        ->  Index Scan using "IX_AbsenceDays_UserId_OrganizationId_Date" on "AbsenceDays" a  (cost=12.48..20.53 rows=1 width=55) (actual time=0.015..0.015 rows=0 loops=1)
              Index Cond: (("UserId" = 21) AND ("OrganizationId" = 2) AND ("Date" >= '2026-10-01'::date) AND ("Date" <= '2026-10-19'::date))
              Filter: ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 1))
              Buffers: shared hit=2
              SubPlan 1
                ->  Index Scan using "IX_UserOrganizations_UserId_OrganizationId" on "UserOrganizations" uo  (cost=0.16..12.19 rows=1 width=4) (never executed)
                      Index Cond: ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer)
                      Filter: "IsActive"
        ->  Nested Loop  (cost=0.29..8.54 rows=1 width=298) (never executed)
              ->  Index Scan using "PK_AbsenceTypeDefinitions" on "AbsenceTypeDefinitions" a0  (cost=0.14..8.16 rows=1 width=298) (never executed)
                    Index Cond: ("Id" = a."CustomTypeId")
              ->  Index Scan using "PK_Organizations" on "Organizations" o1  (cost=0.14..0.35 rows=1 width=4) (never executed)
                    Index Cond: ("Id" = a0."OrganizationId")
                    Filter: ("IsActive" AND "IsActive")
  ->  Index Scan using "PK_Organizations" on "Organizations" o  (cost=0.14..8.16 rows=1 width=4) (never executed)
        Index Cond: ("Id" = 2)
        Filter: ("IsActive" AND "IsActive")
Planning Time: 0.393 ms
Execution Time: 0.104 ms
```
