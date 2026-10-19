# Phase 1 — quick wins: before / after

Measured 2026-10-19 with the revised Phase 0 harness (each size in a fresh database, no background jobs),
`PERF_SIZES=100x1,100x10 dotnet test --filter Category=Perf`, alone on the machine, once on the staged Phase 0 tree
(**before** = `docs/perf/baseline.md`) and once on the Phase 1 working tree (**after**, full report below). p50 ms · SQL
commands per call. One earlier Phase 1 run was discarded: its 100x10 half was disturbed (timer/manual-entry calls 3–4x
slower on code paths Phase 1 does not touch) and a second one stalled for an hour while the machine paused (expired
token → 401); the run below is the third, undisturbed one.

| endpoint | before 100x1 | after 100x1 | before 100x10 | after 100x10 | change at 10 y |
|---|---:|---:|---:|---:|---|
| balance | 63.3 · 22 | 61.1 · 20 | 82.8 · 24 | 55.1 · 24 | −33 % (no core-hours re-evaluation / entry summaries for history days) |
| team-overview | 204.3 · 10 | 164.9 · 10 | 1317.8 · 10 | 852.3 · 10 | −35 %; still reads the whole history for the balances (Phase 2) |
| inbox: failures | 4.0 · 1 | 3.9 · 1 | 3.6 · 1 | 2.9 · 1 | partial index `IX_TimeEntries_UserId_Failure` |
| inbox: approvals | 20.8 · 7 | 25.3 · 7 | 19.9 · 7 | 18.6 · 7 | flat |
| inbox: user-notifications | 4.8 · 1 | 5.2 · 1 | 5.1 · 1 | 4.0 · 1 | flat |
| inbox: my-requests | 18.2 · 6 | 17.6 · 6 | 16.8 · 5 | 14.1 · 5 | flat |
| inbox: notifications | 6.0 · 2 | 7.7 · 2 | 4.8 · 2 | 4.7 · 2 | flat |
| TimeTracking week | 7.3 · 2 | 6.8 · 2 | 5.6 · 2 | 5.6 · 2 | flat |
| member entries week | 21.7 · 7 | 18.5 · 7 | 18.4 · 7 | 17.5 · 7 | flat |
| compliance month | 21.9 · 7 | 17.9 · 7 | 20.4 · 7 | 15.1 · 7 | covered-entries query bounded by entry length |
| absences year | 24.9 · 7 | 20.6 · 7 | 23.6 · 7 | 19.4 · 7 | flat |
| org detail | 43.3 · 8 | 41.7 · 8 | 41.9 · 8 | 36.3 · 8 | flat |
| accounting summary month | 2200.8 · 903 | 131.9 · 11 | 4997.5 · 907 | 866.8 · 11 | −83 %, SQL no longer grows with members |
| payroll CSV month | 2200.3 · 903 | 136.9 · 11 | 5706.9 · 907 | 895.5 · 11 | −84 %, SQL no longer grows with members |
| accounting vacation year | 29.4 · 7 | 26.8 · 7 | 30.2 · 7 | 27.6 · 7 | flat |
| daily CSV month | 52.8 · 22 | 49.6 · 20 | 83.2 · 22 | 56.7 · 20 | −32 %, now flat over years |
| monthly PDF | 62.9 · 15 | 55.1 · 13 | 79.3 · 15 | 61.9 · 13 | −22 %, now flat over years |
| timer start | 17.8 · 7 | 18.2 · 7 | 17.0 · 7 | 15.5 · 7 | flat |
| timer stop | 19.3 · 7 | 16.9 · 7 | 17.4 · 7 | 17.0 · 7 | flat |
| manual entry create | 39.0 · 13 | 37.3 · 13 | 32.9 · 13 | 30.4 · 13 | overlap check seeks a 7-day index range |
| manual entry delete | 20.7 · 7 | 21.4 · 7 | 22.1 · 7 | 18.9 · 7 | flat |

## What is left (Phase 2)

Team overview and payroll/accounting summary still compute every member's balance from the first entry: one query
reading all finished entries of the team (≈250k rows at 100x10, ~650 ms to read and materialise over the Docker
network on this machine) plus the day loop (~900 ms for 365k member days measured in isolation). Their SQL count is
constant now; the time grows with years until the balance checkpoints of Phase 2 bound it.

## EXPLAIN findings (100x5, as the app role with RLS)

- Team history read (`DailyReportAggregator.TotalsAsync`): a sequential scan — correct, the query reads every row of
  the organization (the seed has one large org). A covering index `(OrganizationId, UserId, StartTime) INCLUDE
  (EndTime, IsRunning, IsFailure, PauseDurationMinutes)` was tried: the planner still chose the sequential scan and the
  query was not faster (92 → 107 ms), so it was not added. Bounding the read is Phase 2's job.
- Failure counts / inbox failures: `IX_TimeEntries_UserId_Failure` (index scan, 0.2 ms).
- Presence: the partial unique `IX_TimeEntries_UserId` (running timers).
- Overlap check and core-hours covered entries: BitmapOr of `IX_TimeEntries_UserId_StartTime` (bounded start range)
  and the running-timer index — a handful of rows instead of the whole history.
- Pending requests / request lists: `IX_OrgRequests_OrganizationId_Status`, `IX_OrgRequests_OrganizationId_CreatedAt`
  (backward scan, LIMIT 50).
- Retention: `IX_TerminalPunches_ReceivedAt` used. Notifications chose a sequential scan because 69 % of the seeded
  notifications were due (the seed writes years of read notifications); with a normal daily backlog the
  `(IsRead, CreatedAt)` index applies. RefreshTokens was empty in the seed (plan not meaningful).
- `TimeEntryChanges (UserId)` already existed as the foreign-key index, so it is not in the migration.

---

# Scaling report — 2026-10-19 11:09

Sizes (employees x years): 100x1, 100x10. Each call: 3 warm-ups, 15 measured (calls slower than 5000 ms: 1 + 5, marked *), over HTTP (TestServer, no network) as the app role with row-level security on, against PostgreSQL 16 in Docker (Testcontainers). Method: every size gets a fresh database of its own (created empty, migrated by its own app instance, seeded with PerfSeeder by binary COPY, then ANALYZEd), measured, and dropped — sizes never share tables, indexes or caches beyond the Postgres server itself; the background jobs (business-day-end/core hours loop, push dispatch, retention) are not running. Times in ms: p50 and p95 by nearest rank over n measured calls (with n < 20 the p95 is the maximum), SQL = commands EF sent for one call (median). Member = perf-user1 (full history), admin = perf-admin1, owner. Month = the last ended month, week = the current one, year = 2026. Growth = p50 ratio between the shortest and longest history at the same team size.

## Seeded data

| size | seconds | Users | WorkSchedules | Holidays | Groups | TimeEntries | TimeEntryChanges | OrgRequests | AbsenceDays | CoreHoursChecks | TerminalPunches | Notifications |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 100x1 | 7.1 | 100 | 100 | 19 | 20 | 25,365 | 1,383 | 1,191 | 3,480 | 33,260 | 25,150 | 15,581 |
| 100x10 | 30.2 | 100 | 365 | 55 | 20 | 252,667 | 16,191 | 10,943 | 32,912 | 330,086 | 29,274 | 150,993 |

## Summary (p50 ms · SQL commands)

| endpoint | kind | 100x1 | 100x10 | t(100x10)/t(100x1) |
|---|---|---:|---:|---:|
| balance | interactive | 61.1 · 20 | 55.1 · 24 | 0.9x (SQL 20 → 24) |
| team-overview | interactive | 164.9 · 10 | 852.3 · 10 | 5.2x (SQL 10 → 10) |
| inbox: failures | interactive | 3.9 · 1 | 2.9 · 1 | 0.7x (SQL 1 → 1) |
| inbox: approvals | interactive | 25.3 · 7 | 18.6 · 7 | 0.7x (SQL 7 → 7) |
| inbox: user-notifications | interactive | 5.2 · 1 | 4.0 · 1 | 0.8x (SQL 1 → 1) |
| inbox: my-requests | interactive | 17.6 · 6 | 14.1 · 5 | 0.8x (SQL 6 → 5) |
| inbox: notifications | interactive | 7.7 · 2 | 4.7 · 2 | 0.6x (SQL 2 → 2) |
| TimeTracking week | interactive | 6.8 · 2 | 5.6 · 2 | 0.8x (SQL 2 → 2) |
| member entries week | interactive | 18.5 · 7 | 17.5 · 7 | 0.9x (SQL 7 → 7) |
| compliance month | interactive | 17.9 · 7 | 15.1 · 7 | 0.8x (SQL 7 → 7) |
| absences year | interactive | 20.6 · 7 | 19.4 · 7 | 0.9x (SQL 7 → 7) |
| org detail | interactive | 41.7 · 8 | 36.3 · 8 | 0.9x (SQL 8 → 8) |
| accounting summary month | export | 131.9 · 11 | 866.8 · 11 | 6.6x (SQL 11 → 11) |
| payroll CSV month | export | 136.9 · 11 | 895.5 · 11 | 6.5x (SQL 11 → 11) |
| accounting vacation year | export | 26.8 · 7 | 27.6 · 7 | 1.0x (SQL 7 → 7) |
| daily CSV month | export | 49.6 · 20 | 56.7 · 20 | 1.1x (SQL 20 → 20) |
| monthly PDF | export | 55.1 · 13 | 61.9 · 13 | 1.1x (SQL 13 → 13) |
| timer start | interactive | 18.2 · 7 | 15.5 · 7 | 0.8x (SQL 7 → 7) |
| timer stop | interactive | 16.9 · 7 | 17.0 · 7 | 1.0x (SQL 7 → 7) |
| manual entry create | interactive | 37.3 · 13 | 30.4 · 13 | 0.8x (SQL 13 → 13) |
| manual entry delete | interactive | 21.4 · 7 | 18.9 · 7 | 0.9x (SQL 7 → 7) |

## Per endpoint

### balance (interactive)

| | 100x1 | 100x10 |
|---|---:|---:|
| p50 ms | 61.1 | 55.1 |
| p95 ms (nearest rank) | 104.6 | 82.1 |
| n | 15 | 15 |
| SQL | 20 | 24 |

Growth t(100x10)/t(100x1): 0.9x (SQL 20 → 24)

### team-overview (interactive)

| | 100x1 | 100x10 |
|---|---:|---:|
| p50 ms | 164.9 | 852.3 |
| p95 ms (nearest rank) | 227.5 | 916.3 |
| n | 15 | 15 |
| SQL | 10 | 10 |

Growth t(100x10)/t(100x1): 5.2x (SQL 10 → 10)

### inbox: failures (interactive)

| | 100x1 | 100x10 |
|---|---:|---:|
| p50 ms | 3.9 | 2.9 |
| p95 ms (nearest rank) | 4.4 | 9.7 |
| n | 15 | 15 |
| SQL | 1 | 1 |

Growth t(100x10)/t(100x1): 0.7x (SQL 1 → 1)

### inbox: approvals (interactive)

| | 100x1 | 100x10 |
|---|---:|---:|
| p50 ms | 25.3 | 18.6 |
| p95 ms (nearest rank) | 31.9 | 22.0 |
| n | 15 | 15 |
| SQL | 7 | 7 |

Growth t(100x10)/t(100x1): 0.7x (SQL 7 → 7)

### inbox: user-notifications (interactive)

| | 100x1 | 100x10 |
|---|---:|---:|
| p50 ms | 5.2 | 4.0 |
| p95 ms (nearest rank) | 7.4 | 13.2 |
| n | 15 | 15 |
| SQL | 1 | 1 |

Growth t(100x10)/t(100x1): 0.8x (SQL 1 → 1)

### inbox: my-requests (interactive)

| | 100x1 | 100x10 |
|---|---:|---:|
| p50 ms | 17.6 | 14.1 |
| p95 ms (nearest rank) | 20.6 | 15.2 |
| n | 15 | 15 |
| SQL | 6 | 5 |

Growth t(100x10)/t(100x1): 0.8x (SQL 6 → 5)

### inbox: notifications (interactive)

| | 100x1 | 100x10 |
|---|---:|---:|
| p50 ms | 7.7 | 4.7 |
| p95 ms (nearest rank) | 10.6 | 6.9 |
| n | 15 | 15 |
| SQL | 2 | 2 |

Growth t(100x10)/t(100x1): 0.6x (SQL 2 → 2)

### TimeTracking week (interactive)

| | 100x1 | 100x10 |
|---|---:|---:|
| p50 ms | 6.8 | 5.6 |
| p95 ms (nearest rank) | 8.2 | 7.6 |
| n | 15 | 15 |
| SQL | 2 | 2 |

Growth t(100x10)/t(100x1): 0.8x (SQL 2 → 2)

### member entries week (interactive)

| | 100x1 | 100x10 |
|---|---:|---:|
| p50 ms | 18.5 | 17.5 |
| p95 ms (nearest rank) | 23.9 | 20.8 |
| n | 15 | 15 |
| SQL | 7 | 7 |

Growth t(100x10)/t(100x1): 0.9x (SQL 7 → 7)

### compliance month (interactive)

| | 100x1 | 100x10 |
|---|---:|---:|
| p50 ms | 17.9 | 15.1 |
| p95 ms (nearest rank) | 21.0 | 18.5 |
| n | 15 | 15 |
| SQL | 7 | 7 |

Growth t(100x10)/t(100x1): 0.8x (SQL 7 → 7)

### absences year (interactive)

| | 100x1 | 100x10 |
|---|---:|---:|
| p50 ms | 20.6 | 19.4 |
| p95 ms (nearest rank) | 26.4 | 22.0 |
| n | 15 | 15 |
| SQL | 7 | 7 |

Growth t(100x10)/t(100x1): 0.9x (SQL 7 → 7)

### org detail (interactive)

| | 100x1 | 100x10 |
|---|---:|---:|
| p50 ms | 41.7 | 36.3 |
| p95 ms (nearest rank) | 45.0 | 40.1 |
| n | 15 | 15 |
| SQL | 8 | 8 |

Growth t(100x10)/t(100x1): 0.9x (SQL 8 → 8)

### accounting summary month (export)

| | 100x1 | 100x10 |
|---|---:|---:|
| p50 ms | 131.9 | 866.8 |
| p95 ms (nearest rank) | 159.7 | 952.7 |
| n | 15 | 15 |
| SQL | 11 | 11 |

Growth t(100x10)/t(100x1): 6.6x (SQL 11 → 11)

### payroll CSV month (export)

| | 100x1 | 100x10 |
|---|---:|---:|
| p50 ms | 136.9 | 895.5 |
| p95 ms (nearest rank) | 183.5 | 1016.2 |
| n | 15 | 15 |
| SQL | 11 | 11 |

Growth t(100x10)/t(100x1): 6.5x (SQL 11 → 11)

### accounting vacation year (export)

| | 100x1 | 100x10 |
|---|---:|---:|
| p50 ms | 26.8 | 27.6 |
| p95 ms (nearest rank) | 30.4 | 32.6 |
| n | 15 | 15 |
| SQL | 7 | 7 |

Growth t(100x10)/t(100x1): 1.0x (SQL 7 → 7)

### daily CSV month (export)

| | 100x1 | 100x10 |
|---|---:|---:|
| p50 ms | 49.6 | 56.7 |
| p95 ms (nearest rank) | 67.0 | 61.1 |
| n | 15 | 15 |
| SQL | 20 | 20 |

Growth t(100x10)/t(100x1): 1.1x (SQL 20 → 20)

### monthly PDF (export)

| | 100x1 | 100x10 |
|---|---:|---:|
| p50 ms | 55.1 | 61.9 |
| p95 ms (nearest rank) | 72.8 | 104.9 |
| n | 15 | 15 |
| SQL | 13 | 13 |

Growth t(100x10)/t(100x1): 1.1x (SQL 13 → 13)

### timer start (interactive)

| | 100x1 | 100x10 |
|---|---:|---:|
| p50 ms | 18.2 | 15.5 |
| p95 ms (nearest rank) | 21.5 | 22.0 |
| n | 15 | 15 |
| SQL | 7 | 7 |

Growth t(100x10)/t(100x1): 0.8x (SQL 7 → 7)

### timer stop (interactive)

| | 100x1 | 100x10 |
|---|---:|---:|
| p50 ms | 16.9 | 17.0 |
| p95 ms (nearest rank) | 18.4 | 24.7 |
| n | 15 | 15 |
| SQL | 7 | 7 |

Growth t(100x10)/t(100x1): 1.0x (SQL 7 → 7)

### manual entry create (interactive)

| | 100x1 | 100x10 |
|---|---:|---:|
| p50 ms | 37.3 | 30.4 |
| p95 ms (nearest rank) | 54.5 | 33.5 |
| n | 15 | 15 |
| SQL | 13 | 13 |

Growth t(100x10)/t(100x1): 0.8x (SQL 13 → 13)

### manual entry delete (interactive)

| | 100x1 | 100x10 |
|---|---:|---:|
| p50 ms | 21.4 | 18.9 |
| p95 ms (nearest rank) | 28.7 | 23.1 |
| n | 15 | 15 |
| SQL | 7 | 7 |

Growth t(100x10)/t(100x1): 0.9x (SQL 7 → 7)

## EXPLAIN (ANALYZE, BUFFERS) — the 5 slowest calls at 100x10

The slowest SELECTs (up to 3) of the last measured call, re-run as the app role for the same user (RLS on).

### payroll CSV month — p50 895.5 ms, 11 SQL

4.9 ms in the call:

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
WHERE o0."IsActive" AND a."OrganizationId" = @org_Id AND a."UserId" = ANY (@userIds) AND a."Date" >= @from AND a."Date" <= @to
```

```text
Nested Loop  (cost=29.85..2005.86 rows=16473 width=352) (actual time=0.078..29.940 rows=32561 loops=1)
  Buffers: shared hit=344
  ->  Index Scan using "PK_Organizations" on "Organizations" o  (cost=0.14..8.16 rows=1 width=4) (actual time=0.020..0.024 rows=1 loops=1)
        Index Cond: ("Id" = 2)
        Filter: ("IsActive" AND "IsActive")
        Buffers: shared hit=2
  ->  Hash Left Join  (cost=29.71..1832.97 rows=16473 width=352) (actual time=0.056..24.937 rows=32561 loops=1)
        Hash Cond: (a."CustomTypeId" = a0."Id")
        Buffers: shared hit=342
        ->  Seq Scan on "AbsenceDays" a  (cost=4.82..1746.30 rows=16473 width=54) (actual time=0.027..19.274 rows=32561 loops=1)
              Filter: (("Date" >= '2016-10-19'::date) AND ("Date" <= '2026-09-30'::date) AND ("OrganizationId" = 2) AND ("UserId" = ANY ('{16,17,18,19,20,15,21,30,31,32,33,34,35,36,37,38,39,22,40,41,42,43,44,45,46,47,48,49,23,50,51,52,53,54,55,56,57,58,59,24,60,61,62,63,64,65,66,67,68,69,25,70,71,72,73,74,75,76,77,78,79,26,80,81,82,83,84,85,86,87,88,89,27,90,91,92,93,94,95,96,97,98,99,28,100,101,102,103,104,105,106,107,108,109,29,110,111,112,113,114}'::integer[])) AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 1)))
              Rows Removed by Filter: 462
              Buffers: shared hit=340
              SubPlan 1
                ->  Seq Scan on "UserOrganizations" uo  (cost=0.00..4.56 rows=1 width=4) (actual time=0.008..0.029 rows=1 loops=1)
                      Filter: ("IsActive" AND ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer))
                      Rows Removed by Filter: 113
                      Buffers: shared hit=2
        ->  Hash  (cost=24.09..24.09 rows=64 width=298) (actual time=0.022..0.025 rows=3 loops=1)
              Buckets: 1024  Batches: 1  Memory Usage: 9kB
              Buffers: shared hit=2
              ->  Hash Join  (cost=10.92..24.09 rows=64 width=298) (actual time=0.017..0.020 rows=3 loops=1)
                    Hash Cond: (a0."OrganizationId" = o1."Id")
                    Buffers: shared hit=2
                    ->  Seq Scan on "AbsenceTypeDefinitions" a0  (cost=0.00..12.50 rows=250 width=298) (actual time=0.003..0.003 rows=3 loops=1)
                          Buffers: shared hit=1
                    ->  Hash  (cost=10.70..10.70 rows=18 width=4) (actual time=0.007..0.007 rows=2 loops=1)
                          Buckets: 1024  Batches: 1  Memory Usage: 9kB
                          Buffers: shared hit=1
                          ->  Seq Scan on "Organizations" o1  (cost=0.00..10.70 rows=18 width=4) (actual time=0.001..0.002 rows=2 loops=1)
                                Filter: ("IsActive" AND "IsActive")
                                Buffers: shared hit=1
Planning Time: 0.463 ms
Execution Time: 31.057 ms
```

3.8 ms in the call:

```sql
SELECT w."Id", w."CreatedAt", w."OrganizationId", w."TargetFri", w."TargetMon", w."TargetSat", w."TargetSun", w."TargetThu", w."TargetTue", w."TargetWed", w."UserId", w."ValidFrom", w."ValidTo", w."WeeklyWorkHours"
FROM "WorkSchedules" AS w
INNER JOIN (
    SELECT o."Id", o."IsActive"
    FROM "Organizations" AS o
    WHERE o."IsActive"
) AS o0 ON w."OrganizationId" = o0."Id"
WHERE o0."IsActive" AND w."OrganizationId" = @org_Id AND w."UserId" = ANY (@userIds)
ORDER BY w."ValidFrom"
```

```text
Sort  (cost=39.43..40.31 rows=352 width=92) (actual time=0.309..0.322 rows=365 loops=1)
  Sort Key: w."ValidFrom"
  Sort Method: quicksort  Memory: 70kB
  Buffers: shared hit=8
  ->  Nested Loop  (cost=0.39..24.54 rows=352 width=92) (actual time=0.031..0.174 rows=365 loops=1)
        Buffers: shared hit=8
        ->  Index Scan using "PK_Organizations" on "Organizations" o  (cost=0.14..8.16 rows=1 width=4) (actual time=0.017..0.018 rows=1 loops=1)
              Index Cond: ("Id" = 2)
              Filter: ("IsActive" AND "IsActive")
              Buffers: shared hit=2
        ->  Seq Scan on "WorkSchedules" w  (cost=0.25..12.87 rows=352 width=92) (actual time=0.013..0.108 rows=365 loops=1)
              Filter: (("OrganizationId" = 2) AND ("UserId" = ANY ('{16,17,18,19,20,15,21,30,31,32,33,34,35,36,37,38,39,22,40,41,42,43,44,45,46,47,48,49,23,50,51,52,53,54,55,56,57,58,59,24,60,61,62,63,64,65,66,67,68,69,25,70,71,72,73,74,75,76,77,78,79,26,80,81,82,83,84,85,86,87,88,89,27,90,91,92,93,94,95,96,97,98,99,28,100,101,102,103,104,105,106,107,108,109,29,110,111,112,113,114}'::integer[])))
              Rows Removed by Filter: 13
              Buffers: shared hit=6
Planning Time: 0.194 ms
Execution Time: 0.372 ms
```

3.7 ms in the call:

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
HashAggregate  (cost=3796.10..3798.10 rows=200 width=4) (actual time=3.308..3.319 rows=100 loops=1)
  Group Key: t."UserId"
  Batches: 1  Memory Usage: 40kB
  Buffers: shared hit=2361
  ->  HashAggregate  (cost=3785.48..3793.98 rows=850 width=4) (actual time=3.281..3.294 rows=100 loops=1)
        Group Key: t."UserId"
        Batches: 1  Memory Usage: 49kB
        Buffers: shared hit=2361
        ->  Append  (cost=5.13..3783.35 rows=850 width=4) (actual time=0.069..2.952 rows=2671 loops=1)
              Buffers: shared hit=2361
              ->  Nested Loop Left Join  (cost=5.13..3384.72 rows=646 width=4) (actual time=0.068..2.407 rows=2265 loops=1)
                    Filter: ((t."OrganizationId" IS NULL) OR o."IsActive")
                    Buffers: shared hit=2280
                    ->  Index Scan using "IX_TimeEntries_OrganizationId_StartTime" on "TimeEntries" t  (cost=4.99..3360.42 rows=1291 width=8) (actual time=0.057..1.999 rows=2265 loops=1)
                          Index Cond: (("OrganizationId" = 2) AND ("StartTime" >= '2026-08-31 22:00:00+00'::timestamp with time zone) AND ("StartTime" < '2026-09-30 22:00:00+00'::timestamp with time zone))
                          Filter: ((NOT "IsRunning") AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 1)))
                          Buffers: shared hit=2278
                          SubPlan 1
                            ->  Seq Scan on "UserOrganizations" uo  (cost=0.00..4.56 rows=1 width=4) (actual time=0.008..0.029 rows=1 loops=1)
                                  Filter: ("IsActive" AND ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer))
                                  Rows Removed by Filter: 113
                                  Buffers: shared hit=2
                    ->  Materialize  (cost=0.14..8.17 rows=1 width=5) (actual time=0.000..0.000 rows=1 loops=2265)
                          Buffers: shared hit=2
                          ->  Index Scan using "PK_Organizations" on "Organizations" o  (cost=0.14..8.16 rows=1 width=5) (actual time=0.007..0.007 rows=1 loops=1)
                                Index Cond: ("Id" = 2)
                                Filter: "IsActive"
                                Buffers: shared hit=2
              ->  Nested Loop  (cost=14.06..394.38 rows=204 width=4) (actual time=0.077..0.375 rows=406 loops=1)
                    Buffers: shared hit=81
                    ->  Index Scan using "PK_Organizations" on "Organizations" o1  (cost=0.14..8.16 rows=1 width=4) (actual time=0.010..0.010 rows=1 loops=1)
                          Index Cond: ("Id" = 2)
                          Filter: ("IsActive" AND "IsActive")
                          Buffers: shared hit=2
                    ->  Bitmap Heap Scan on "AbsenceDays" a  (cost=13.92..384.18 rows=204 width=8) (actual time=0.065..0.314 rows=406 loops=1)
                          Recheck Cond: (("OrganizationId" = 2) AND ("Date" >= '2026-09-01'::date) AND ("Date" <= '2026-09-30'::date))
                          Filter: ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 2))
                          Heap Blocks: exact=75
                          Buffers: shared hit=79
                          ->  Bitmap Index Scan on "IX_AbsenceDays_OrganizationId_Date"  (cost=0.00..9.30 rows=401 width=0) (actual time=0.024..0.024 rows=406 loops=1)
                                Index Cond: (("OrganizationId" = 2) AND ("Date" >= '2026-09-01'::date) AND ("Date" <= '2026-09-30'::date))
                                Buffers: shared hit=2
                          SubPlan 2
                            ->  Seq Scan on "UserOrganizations" uo_1  (cost=0.00..4.56 rows=1 width=4) (actual time=0.006..0.027 rows=1 loops=1)
                                  Filter: ("IsActive" AND ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer))
                                  Rows Removed by Filter: 113
                                  Buffers: shared hit=2
Planning Time: 0.422 ms
Execution Time: 3.483 ms
```

### accounting summary month — p50 866.8 ms, 11 SQL

3.9 ms in the call:

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
HashAggregate  (cost=3796.10..3798.10 rows=200 width=4) (actual time=3.397..3.408 rows=100 loops=1)
  Group Key: t."UserId"
  Batches: 1  Memory Usage: 40kB
  Buffers: shared hit=2361
  ->  HashAggregate  (cost=3785.48..3793.98 rows=850 width=4) (actual time=3.337..3.355 rows=100 loops=1)
        Group Key: t."UserId"
        Batches: 1  Memory Usage: 49kB
        Buffers: shared hit=2361
        ->  Append  (cost=5.13..3783.35 rows=850 width=4) (actual time=0.100..3.041 rows=2671 loops=1)
              Buffers: shared hit=2361
              ->  Nested Loop Left Join  (cost=5.13..3384.72 rows=646 width=4) (actual time=0.100..2.541 rows=2265 loops=1)
                    Filter: ((t."OrganizationId" IS NULL) OR o."IsActive")
                    Buffers: shared hit=2280
                    ->  Index Scan using "IX_TimeEntries_OrganizationId_StartTime" on "TimeEntries" t  (cost=4.99..3360.42 rows=1291 width=8) (actual time=0.088..2.127 rows=2265 loops=1)
                          Index Cond: (("OrganizationId" = 2) AND ("StartTime" >= '2026-08-31 22:00:00+00'::timestamp with time zone) AND ("StartTime" < '2026-09-30 22:00:00+00'::timestamp with time zone))
                          Filter: ((NOT "IsRunning") AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 1)))
                          Buffers: shared hit=2278
                          SubPlan 1
                            ->  Seq Scan on "UserOrganizations" uo  (cost=0.00..4.56 rows=1 width=4) (actual time=0.006..0.064 rows=1 loops=1)
                                  Filter: ("IsActive" AND ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer))
                                  Rows Removed by Filter: 113
                                  Buffers: shared hit=2
                    ->  Materialize  (cost=0.14..8.17 rows=1 width=5) (actual time=0.000..0.000 rows=1 loops=2265)
                          Buffers: shared hit=2
                          ->  Index Scan using "PK_Organizations" on "Organizations" o  (cost=0.14..8.16 rows=1 width=5) (actual time=0.008..0.008 rows=1 loops=1)
                                Index Cond: ("Id" = 2)
                                Filter: "IsActive"
                                Buffers: shared hit=2
              ->  Nested Loop  (cost=14.06..394.38 rows=204 width=4) (actual time=0.086..0.361 rows=406 loops=1)
                    Buffers: shared hit=81
                    ->  Index Scan using "PK_Organizations" on "Organizations" o1  (cost=0.14..8.16 rows=1 width=4) (actual time=0.013..0.014 rows=1 loops=1)
                          Index Cond: ("Id" = 2)
                          Filter: ("IsActive" AND "IsActive")
                          Buffers: shared hit=2
                    ->  Bitmap Heap Scan on "AbsenceDays" a  (cost=13.92..384.18 rows=204 width=8) (actual time=0.070..0.317 rows=406 loops=1)
                          Recheck Cond: (("OrganizationId" = 2) AND ("Date" >= '2026-09-01'::date) AND ("Date" <= '2026-09-30'::date))
                          Filter: ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 2))
                          Heap Blocks: exact=75
                          Buffers: shared hit=79
                          ->  Bitmap Index Scan on "IX_AbsenceDays_OrganizationId_Date"  (cost=0.00..9.30 rows=401 width=0) (actual time=0.021..0.021 rows=406 loops=1)
                                Index Cond: (("OrganizationId" = 2) AND ("Date" >= '2026-09-01'::date) AND ("Date" <= '2026-09-30'::date))
                                Buffers: shared hit=2
                          SubPlan 2
                            ->  Seq Scan on "UserOrganizations" uo_1  (cost=0.00..4.56 rows=1 width=4) (actual time=0.006..0.026 rows=1 loops=1)
                                  Filter: ("IsActive" AND ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer))
                                  Rows Removed by Filter: 113
                                  Buffers: shared hit=2
Planning Time: 0.356 ms
Execution Time: 3.542 ms
```

1.8 ms in the call:

```sql
SELECT w."Id", w."CreatedAt", w."OrganizationId", w."TargetFri", w."TargetMon", w."TargetSat", w."TargetSun", w."TargetThu", w."TargetTue", w."TargetWed", w."UserId", w."ValidFrom", w."ValidTo", w."WeeklyWorkHours"
FROM "WorkSchedules" AS w
INNER JOIN (
    SELECT o."Id", o."IsActive"
    FROM "Organizations" AS o
    WHERE o."IsActive"
) AS o0 ON w."OrganizationId" = o0."Id"
WHERE o0."IsActive" AND w."OrganizationId" = @org_Id AND w."UserId" = ANY (@userIds)
ORDER BY w."ValidFrom"
```

```text
Sort  (cost=39.43..40.31 rows=352 width=92) (actual time=0.264..0.276 rows=365 loops=1)
  Sort Key: w."ValidFrom"
  Sort Method: quicksort  Memory: 70kB
  Buffers: shared hit=8
  ->  Nested Loop  (cost=0.39..24.54 rows=352 width=92) (actual time=0.021..0.160 rows=365 loops=1)
        Buffers: shared hit=8
        ->  Index Scan using "PK_Organizations" on "Organizations" o  (cost=0.14..8.16 rows=1 width=4) (actual time=0.010..0.011 rows=1 loops=1)
              Index Cond: ("Id" = 2)
              Filter: ("IsActive" AND "IsActive")
              Buffers: shared hit=2
        ->  Seq Scan on "WorkSchedules" w  (cost=0.25..12.87 rows=352 width=92) (actual time=0.010..0.101 rows=365 loops=1)
              Filter: (("OrganizationId" = 2) AND ("UserId" = ANY ('{16,17,18,19,20,15,21,30,31,32,33,34,35,36,37,38,39,22,40,41,42,43,44,45,46,47,48,49,23,50,51,52,53,54,55,56,57,58,59,24,60,61,62,63,64,65,66,67,68,69,25,70,71,72,73,74,75,76,77,78,79,26,80,81,82,83,84,85,86,87,88,89,27,90,91,92,93,94,95,96,97,98,99,28,100,101,102,103,104,105,106,107,108,109,29,110,111,112,113,114}'::integer[])))
              Rows Removed by Filter: 13
              Buffers: shared hit=6
Planning Time: 0.199 ms
Execution Time: 0.321 ms
```

1.8 ms in the call:

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
WHERE o0."IsActive" AND a."OrganizationId" = @org_Id AND a."UserId" = ANY (@userIds) AND a."Date" >= @from AND a."Date" <= @to
```

```text
Nested Loop  (cost=29.85..2005.86 rows=16473 width=352) (actual time=0.056..29.187 rows=32561 loops=1)
  Buffers: shared hit=344
  ->  Index Scan using "PK_Organizations" on "Organizations" o  (cost=0.14..8.16 rows=1 width=4) (actual time=0.011..0.015 rows=1 loops=1)
        Index Cond: ("Id" = 2)
        Filter: ("IsActive" AND "IsActive")
        Buffers: shared hit=2
  ->  Hash Left Join  (cost=29.71..1832.97 rows=16473 width=352) (actual time=0.044..24.315 rows=32561 loops=1)
        Hash Cond: (a."CustomTypeId" = a0."Id")
        Buffers: shared hit=342
        ->  Seq Scan on "AbsenceDays" a  (cost=4.82..1746.30 rows=16473 width=54) (actual time=0.022..18.656 rows=32561 loops=1)
              Filter: (("Date" >= '2016-10-19'::date) AND ("Date" <= '2026-09-30'::date) AND ("OrganizationId" = 2) AND ("UserId" = ANY ('{16,17,18,19,20,15,21,30,31,32,33,34,35,36,37,38,39,22,40,41,42,43,44,45,46,47,48,49,23,50,51,52,53,54,55,56,57,58,59,24,60,61,62,63,64,65,66,67,68,69,25,70,71,72,73,74,75,76,77,78,79,26,80,81,82,83,84,85,86,87,88,89,27,90,91,92,93,94,95,96,97,98,99,28,100,101,102,103,104,105,106,107,108,109,29,110,111,112,113,114}'::integer[])) AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 1)))
              Rows Removed by Filter: 462
              Buffers: shared hit=340
              SubPlan 1
                ->  Seq Scan on "UserOrganizations" uo  (cost=0.00..4.56 rows=1 width=4) (actual time=0.006..0.047 rows=1 loops=1)
                      Filter: ("IsActive" AND ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer))
                      Rows Removed by Filter: 113
                      Buffers: shared hit=2
        ->  Hash  (cost=24.09..24.09 rows=64 width=298) (actual time=0.019..0.021 rows=3 loops=1)
              Buckets: 1024  Batches: 1  Memory Usage: 9kB
              Buffers: shared hit=2
              ->  Hash Join  (cost=10.92..24.09 rows=64 width=298) (actual time=0.014..0.017 rows=3 loops=1)
                    Hash Cond: (a0."OrganizationId" = o1."Id")
                    Buffers: shared hit=2
                    ->  Seq Scan on "AbsenceTypeDefinitions" a0  (cost=0.00..12.50 rows=250 width=298) (actual time=0.002..0.002 rows=3 loops=1)
                          Buffers: shared hit=1
                    ->  Hash  (cost=10.70..10.70 rows=18 width=4) (actual time=0.006..0.007 rows=2 loops=1)
                          Buckets: 1024  Batches: 1  Memory Usage: 9kB
                          Buffers: shared hit=1
                          ->  Seq Scan on "Organizations" o1  (cost=0.00..10.70 rows=18 width=4) (actual time=0.001..0.002 rows=2 loops=1)
                                Filter: ("IsActive" AND "IsActive")
                                Buffers: shared hit=1
Planning Time: 0.405 ms
Execution Time: 30.226 ms
```

### team-overview — p50 852.3 ms, 10 SQL

2.0 ms in the call:

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
WHERE o0."IsActive" AND a."OrganizationId" = @org_Id AND a."UserId" = ANY (@userIds) AND a."Date" >= @from AND a."Date" <= @to
```

```text
Nested Loop  (cost=29.85..2004.14 rows=16348 width=352) (actual time=0.103..29.032 rows=32375 loops=1)
  Buffers: shared hit=344
  ->  Index Scan using "PK_Organizations" on "Organizations" o  (cost=0.14..8.16 rows=1 width=4) (actual time=0.018..0.021 rows=1 loops=1)
        Index Cond: ("Id" = 2)
        Filter: ("IsActive" AND "IsActive")
        Buffers: shared hit=2
  ->  Hash Left Join  (cost=29.71..1832.50 rows=16348 width=352) (actual time=0.084..24.101 rows=32375 loops=1)
        Hash Cond: (a."CustomTypeId" = a0."Id")
        Buffers: shared hit=342
        ->  Seq Scan on "AbsenceDays" a  (cost=4.81..1746.29 rows=16348 width=54) (actual time=0.055..18.564 rows=32375 loops=1)
              Filter: (("Date" >= '2016-10-19'::date) AND ("Date" <= '2026-10-19'::date) AND ("OrganizationId" = 2) AND ("UserId" = ANY ('{15,17,18,19,20,21,22,23,24,25,26,27,28,29,30,31,32,33,34,35,36,37,38,39,40,41,42,43,44,45,46,47,48,49,50,51,52,53,54,55,56,57,58,59,60,61,62,63,64,65,66,67,68,69,70,71,72,73,74,75,76,77,78,79,80,81,82,83,84,85,86,87,88,89,90,91,92,93,94,95,96,97,98,99,100,101,102,103,104,105,106,107,108,109,110,111,112,113,114}'::integer[])) AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 1)))
              Rows Removed by Filter: 648
              Buffers: shared hit=340
              SubPlan 1
                ->  Seq Scan on "UserOrganizations" uo  (cost=0.00..4.56 rows=1 width=4) (actual time=0.007..0.027 rows=1 loops=1)
                      Filter: ("IsActive" AND ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer))
                      Rows Removed by Filter: 113
                      Buffers: shared hit=2
        ->  Hash  (cost=24.09..24.09 rows=64 width=298) (actual time=0.023..0.026 rows=3 loops=1)
              Buckets: 1024  Batches: 1  Memory Usage: 9kB
              Buffers: shared hit=2
              ->  Hash Join  (cost=10.92..24.09 rows=64 width=298) (actual time=0.017..0.021 rows=3 loops=1)
                    Hash Cond: (a0."OrganizationId" = o1."Id")
                    Buffers: shared hit=2
                    ->  Seq Scan on "AbsenceTypeDefinitions" a0  (cost=0.00..12.50 rows=250 width=298) (actual time=0.002..0.002 rows=3 loops=1)
                          Buffers: shared hit=1
                    ->  Hash  (cost=10.70..10.70 rows=18 width=4) (actual time=0.006..0.008 rows=2 loops=1)
                          Buckets: 1024  Batches: 1  Memory Usage: 9kB
                          Buffers: shared hit=1
                          ->  Seq Scan on "Organizations" o1  (cost=0.00..10.70 rows=18 width=4) (actual time=0.001..0.002 rows=2 loops=1)
                                Filter: ("IsActive" AND "IsActive")
                                Buffers: shared hit=1
Planning Time: 0.440 ms
Execution Time: 30.053 ms
```

1.8 ms in the call:

```sql
SELECT w."Id", w."CreatedAt", w."OrganizationId", w."TargetFri", w."TargetMon", w."TargetSat", w."TargetSun", w."TargetThu", w."TargetTue", w."TargetWed", w."UserId", w."ValidFrom", w."ValidTo", w."WeeklyWorkHours"
FROM "WorkSchedules" AS w
INNER JOIN (
    SELECT o."Id", o."IsActive"
    FROM "Organizations" AS o
    WHERE o."IsActive"
) AS o0 ON w."OrganizationId" = o0."Id"
WHERE o0."IsActive" AND w."OrganizationId" = @org_Id AND w."UserId" = ANY (@userIds)
ORDER BY w."ValidFrom"
```

```text
Sort  (cost=39.25..40.13 rows=349 width=92) (actual time=0.287..0.299 rows=361 loops=1)
  Sort Key: w."ValidFrom"
  Sort Method: quicksort  Memory: 70kB
  Buffers: shared hit=8
  ->  Nested Loop  (cost=0.39..24.51 rows=349 width=92) (actual time=0.031..0.174 rows=361 loops=1)
        Buffers: shared hit=8
        ->  Index Scan using "PK_Organizations" on "Organizations" o  (cost=0.14..8.16 rows=1 width=4) (actual time=0.018..0.019 rows=1 loops=1)
              Index Cond: ("Id" = 2)
              Filter: ("IsActive" AND "IsActive")
              Buffers: shared hit=2
        ->  Seq Scan on "WorkSchedules" w  (cost=0.25..12.86 rows=349 width=92) (actual time=0.013..0.107 rows=361 loops=1)
              Filter: (("OrganizationId" = 2) AND ("UserId" = ANY ('{15,17,18,19,20,21,22,23,24,25,26,27,28,29,30,31,32,33,34,35,36,37,38,39,40,41,42,43,44,45,46,47,48,49,50,51,52,53,54,55,56,57,58,59,60,61,62,63,64,65,66,67,68,69,70,71,72,73,74,75,76,77,78,79,80,81,82,83,84,85,86,87,88,89,90,91,92,93,94,95,96,97,98,99,100,101,102,103,104,105,106,107,108,109,110,111,112,113,114}'::integer[])))
              Rows Removed by Filter: 17
              Buffers: shared hit=6
Planning Time: 0.199 ms
Execution Time: 0.358 ms
```

1.7 ms in the call:

```sql
SELECT c."Id", c."Backfilled", c."CheckedAt", c."CheckedLate", c."Date", c."HalfDay", c."Issues", c."MissingMinutes", c."NotifiedAt", c."OrganizationId", c."ToleranceMinutes", c."UserId", c."WindowEnd", c."WindowStart"
FROM "CoreHoursChecks" AS c
INNER JOIN (
    SELECT o."Id", o."IsActive"
    FROM "Organizations" AS o
    WHERE o."IsActive"
) AS o0 ON c."OrganizationId" = o0."Id"
WHERE o0."IsActive" AND c."UserId" = ANY (@userIds) AND c."OrganizationId" = @orgId AND c."Date" >= @from AND c."Date" <= @to AND c."Issues" <> 0
```

```text
Nested Loop  (cost=5.13..444.08 rows=1 width=63) (actual time=0.236..0.237 rows=0 loops=1)
  Buffers: shared hit=297
  ->  Index Scan using "IX_CoreHoursChecks_OrganizationId_UserId_Date" on "CoreHoursChecks" c  (cost=4.99..435.91 rows=1 width=63) (actual time=0.236..0.236 rows=0 loops=1)
        Index Cond: (("OrganizationId" = 2) AND ("UserId" = ANY ('{15,17,18,19,20,21,22,23,24,25,26,27,28,29,30,31,32,33,34,35,36,37,38,39,40,41,42,43,44,45,46,47,48,49,50,51,52,53,54,55,56,57,58,59,60,61,62,63,64,65,66,67,68,69,70,71,72,73,74,75,76,77,78,79,80,81,82,83,84,85,86,87,88,89,90,91,92,93,94,95,96,97,98,99,100,101,102,103,104,105,106,107,108,109,110,111,112,113,114}'::integer[])) AND ("Date" >= '2026-10-19'::date) AND ("Date" <= '2026-10-19'::date))
        Filter: (("Issues" <> 0) AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 1)))
        Buffers: shared hit=297
        SubPlan 1
          ->  Seq Scan on "UserOrganizations" uo  (cost=0.00..4.56 rows=1 width=4) (never executed)
                Filter: ("IsActive" AND ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer))
  ->  Index Scan using "PK_Organizations" on "Organizations" o  (cost=0.14..8.16 rows=1 width=4) (never executed)
        Index Cond: ("Id" = 2)
        Filter: ("IsActive" AND "IsActive")
Planning Time: 0.271 ms
Execution Time: 0.281 ms
```

### monthly PDF — p50 61.9 ms, 13 SQL

4.4 ms in the call:

```sql
SELECT t."Id", t."CreatedAt", t."DayEndReminderSentAt", t."Description", t."EndTime", t."IsFailure", t."IsRunning", t."OrganizationId", t."PauseDurationMinutes", t."PauseIsManual", t."Source", t."StartTime", t."UpdatedAt", t."UserId"
FROM "TimeEntries" AS t
LEFT JOIN (
    SELECT o."Id", o."IsActive"
    FROM "Organizations" AS o
    WHERE o."IsActive"
) AS o0 ON t."OrganizationId" = o0."Id"
WHERE (t."OrganizationId" IS NULL OR o0."IsActive") AND t."UserId" = @userId AND t."OrganizationId" = @orgId AND NOT (t."IsRunning") AND t."StartTime" >= @rangeFromUtc AND t."StartTime" < @rangeToUtc
ORDER BY t."StartTime"
```

```text
Sort  (cost=3467.51..3469.26 rows=702 width=95) (actual time=3.401..3.496 rows=2811 loops=1)
  Sort Key: t."StartTime"
  Sort Method: quicksort  Memory: 382kB
  Buffers: shared hit=54
  ->  Nested Loop Left Join  (cost=84.16..3434.32 rows=702 width=95) (actual time=0.208..2.617 rows=2811 loops=1)
        Filter: ((t."OrganizationId" IS NULL) OR o."IsActive")
        Buffers: shared hit=54
        ->  Bitmap Heap Scan on "TimeEntries" t  (cost=84.01..3408.60 rows=1405 width=95) (actual time=0.195..1.943 rows=2811 loops=1)
              Recheck Cond: (("UserId" = 21) AND ("StartTime" >= '2016-10-18 22:00:00+00'::timestamp with time zone) AND ("StartTime" < '2026-09-30 22:00:00+00'::timestamp with time zone))
              Filter: ((NOT "IsRunning") AND ("OrganizationId" = 2) AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 1)))
              Heap Blocks: exact=36
              Buffers: shared hit=52
              ->  Bitmap Index Scan on "IX_TimeEntries_UserId_StartTime"  (cost=0.00..79.10 rows=2774 width=0) (actual time=0.149..0.149 rows=2811 loops=1)
                    Index Cond: (("UserId" = 21) AND ("StartTime" >= '2016-10-18 22:00:00+00'::timestamp with time zone) AND ("StartTime" < '2026-09-30 22:00:00+00'::timestamp with time zone))
                    Buffers: shared hit=14
              SubPlan 1
                ->  Seq Scan on "UserOrganizations" uo  (cost=0.00..4.56 rows=1 width=4) (actual time=0.008..0.029 rows=1 loops=1)
                      Filter: ("IsActive" AND ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer))
                      Rows Removed by Filter: 113
                      Buffers: shared hit=2
        ->  Materialize  (cost=0.14..8.17 rows=1 width=5) (actual time=0.000..0.000 rows=1 loops=2811)
              Buffers: shared hit=2
              ->  Index Scan using "PK_Organizations" on "Organizations" o  (cost=0.14..8.16 rows=1 width=5) (actual time=0.010..0.010 rows=1 loops=1)
                    Index Cond: ("Id" = 2)
                    Filter: "IsActive"
                    Buffers: shared hit=2
Planning Time: 0.286 ms
Execution Time: 3.672 ms
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
Sort  (cost=18.96..18.96 rows=1 width=609) (actual time=0.303..0.308 rows=110 loops=1)
  Sort Key: g."Id"
  Sort Method: quicksort  Memory: 38kB
  Buffers: shared hit=223
  ->  Nested Loop  (cost=8.32..18.95 rows=1 width=609) (actual time=0.062..0.255 rows=110 loops=1)
        Buffers: shared hit=223
        ->  Hash Right Join  (cost=8.17..10.78 rows=1 width=605) (actual time=0.052..0.118 rows=110 loops=1)
              Hash Cond: (g0."GroupId" = g."Id")
              Buffers: shared hit=3
              ->  Seq Scan on "GroupMembers" g0  (cost=0.00..2.26 rows=126 width=24) (actual time=0.004..0.011 rows=126 loops=1)
                    Buffers: shared hit=1
              ->  Hash  (cost=8.16..8.16 rows=1 width=581) (actual time=0.037..0.037 rows=20 loops=1)
                    Buckets: 1024  Batches: 1  Memory Usage: 10kB
                    Buffers: shared hit=2
                    ->  Index Scan using "IX_Groups_OrganizationId" on "Groups" g  (cost=0.14..8.16 rows=1 width=581) (actual time=0.015..0.023 rows=20 loops=1)
                          Index Cond: ("OrganizationId" = 2)
                          Filter: ("IsActive" AND "IsActive")
                          Buffers: shared hit=2
        ->  Index Scan using "PK_Organizations" on "Organizations" o  (cost=0.14..8.16 rows=1 width=4) (actual time=0.001..0.001 rows=1 loops=110)
              Index Cond: ("Id" = 2)
              Filter: ("IsActive" AND "IsActive")
              Buffers: shared hit=220
Planning Time: 0.193 ms
Execution Time: 0.375 ms
```

1.8 ms in the call:

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
Nested Loop  (cost=43.32..420.37 rows=182 width=352) (actual time=0.109..0.467 rows=361 loops=1)
  Buffers: shared hit=14
  ->  Index Scan using "PK_Organizations" on "Organizations" o  (cost=0.14..8.16 rows=1 width=4) (actual time=0.012..0.013 rows=1 loops=1)
        Index Cond: ("Id" = 2)
        Filter: ("IsActive" AND "IsActive")
        Buffers: shared hit=2
  ->  Hash Left Join  (cost=43.18..410.39 rows=182 width=352) (actual time=0.095..0.366 rows=361 loops=1)
        Hash Cond: (a."CustomTypeId" = a0."Id")
        Buffers: shared hit=12
        ->  Bitmap Heap Scan on "AbsenceDays" a  (cost=18.29..384.80 rows=182 width=54) (actual time=0.069..0.277 rows=361 loops=1)
              Recheck Cond: (("UserId" = 21) AND ("OrganizationId" = 2) AND ("Date" >= '2016-10-19'::date) AND ("Date" <= '2026-09-30'::date))
              Filter: ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 1))
              Heap Blocks: exact=4
              Buffers: shared hit=10
              ->  Bitmap Index Scan on "IX_AbsenceDays_UserId_OrganizationId_Date"  (cost=0.00..13.67 rows=359 width=0) (actual time=0.030..0.030 rows=361 loops=1)
                    Index Cond: (("UserId" = 21) AND ("OrganizationId" = 2) AND ("Date" >= '2016-10-19'::date) AND ("Date" <= '2026-09-30'::date))
                    Buffers: shared hit=4
              SubPlan 1
                ->  Seq Scan on "UserOrganizations" uo  (cost=0.00..4.56 rows=1 width=4) (actual time=0.007..0.027 rows=1 loops=1)
                      Filter: ("IsActive" AND ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer))
                      Rows Removed by Filter: 113
                      Buffers: shared hit=2
        ->  Hash  (cost=24.09..24.09 rows=64 width=298) (actual time=0.020..0.020 rows=3 loops=1)
              Buckets: 1024  Batches: 1  Memory Usage: 9kB
              Buffers: shared hit=2
              ->  Hash Join  (cost=10.92..24.09 rows=64 width=298) (actual time=0.015..0.016 rows=3 loops=1)
                    Hash Cond: (a0."OrganizationId" = o1."Id")
                    Buffers: shared hit=2
                    ->  Seq Scan on "AbsenceTypeDefinitions" a0  (cost=0.00..12.50 rows=250 width=298) (actual time=0.002..0.002 rows=3 loops=1)
                          Buffers: shared hit=1
                    ->  Hash  (cost=10.70..10.70 rows=18 width=4) (actual time=0.005..0.005 rows=2 loops=1)
                          Buckets: 1024  Batches: 1  Memory Usage: 9kB
                          Buffers: shared hit=1
                          ->  Seq Scan on "Organizations" o1  (cost=0.00..10.70 rows=18 width=4) (actual time=0.001..0.001 rows=2 loops=1)
                                Filter: ("IsActive" AND "IsActive")
                                Buffers: shared hit=1
Planning Time: 0.355 ms
Execution Time: 0.598 ms
```

### daily CSV month — p50 56.7 ms, 20 SQL

4.0 ms in the call:

```sql
SELECT t."Id", t."CreatedAt", t."DayEndReminderSentAt", t."Description", t."EndTime", t."IsFailure", t."IsRunning", t."OrganizationId", t."PauseDurationMinutes", t."PauseIsManual", t."Source", t."StartTime", t."UpdatedAt", t."UserId"
FROM "TimeEntries" AS t
LEFT JOIN (
    SELECT o."Id", o."IsActive"
    FROM "Organizations" AS o
    WHERE o."IsActive"
) AS o0 ON t."OrganizationId" = o0."Id"
WHERE (t."OrganizationId" IS NULL OR o0."IsActive") AND t."UserId" = @userId AND t."OrganizationId" = @orgId AND NOT (t."IsRunning") AND t."StartTime" >= @rangeFromUtc AND t."StartTime" < @rangeToUtc
ORDER BY t."StartTime"
```

```text
Sort  (cost=3467.51..3469.26 rows=702 width=95) (actual time=3.964..4.092 rows=2811 loops=1)
  Sort Key: t."StartTime"
  Sort Method: quicksort  Memory: 382kB
  Buffers: shared hit=54
  ->  Nested Loop Left Join  (cost=84.16..3434.32 rows=702 width=95) (actual time=0.184..3.075 rows=2811 loops=1)
        Filter: ((t."OrganizationId" IS NULL) OR o."IsActive")
        Buffers: shared hit=54
        ->  Bitmap Heap Scan on "TimeEntries" t  (cost=84.01..3408.60 rows=1405 width=95) (actual time=0.173..2.275 rows=2811 loops=1)
              Recheck Cond: (("UserId" = 21) AND ("StartTime" >= '2016-10-18 22:00:00+00'::timestamp with time zone) AND ("StartTime" < '2026-09-30 22:00:00+00'::timestamp with time zone))
              Filter: ((NOT "IsRunning") AND ("OrganizationId" = 2) AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 1)))
              Heap Blocks: exact=36
              Buffers: shared hit=52
              ->  Bitmap Index Scan on "IX_TimeEntries_UserId_StartTime"  (cost=0.00..79.10 rows=2774 width=0) (actual time=0.128..0.129 rows=2811 loops=1)
                    Index Cond: (("UserId" = 21) AND ("StartTime" >= '2016-10-18 22:00:00+00'::timestamp with time zone) AND ("StartTime" < '2026-09-30 22:00:00+00'::timestamp with time zone))
                    Buffers: shared hit=14
              SubPlan 1
                ->  Seq Scan on "UserOrganizations" uo  (cost=0.00..4.56 rows=1 width=4) (actual time=0.007..0.028 rows=1 loops=1)
                      Filter: ("IsActive" AND ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer))
                      Rows Removed by Filter: 113
                      Buffers: shared hit=2
        ->  Materialize  (cost=0.14..8.17 rows=1 width=5) (actual time=0.000..0.000 rows=1 loops=2811)
              Buffers: shared hit=2
              ->  Index Scan using "PK_Organizations" on "Organizations" o  (cost=0.14..8.16 rows=1 width=5) (actual time=0.008..0.008 rows=1 loops=1)
                    Index Cond: ("Id" = 2)
                    Filter: "IsActive"
                    Buffers: shared hit=2
Planning Time: 0.276 ms
Execution Time: 4.279 ms
```

1.7 ms in the call:

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
Sort  (cost=18.96..18.96 rows=1 width=609) (actual time=0.254..0.259 rows=110 loops=1)
  Sort Key: g."Id"
  Sort Method: quicksort  Memory: 38kB
  Buffers: shared hit=223
  ->  Nested Loop  (cost=8.32..18.95 rows=1 width=609) (actual time=0.059..0.206 rows=110 loops=1)
        Buffers: shared hit=223
        ->  Hash Right Join  (cost=8.17..10.78 rows=1 width=605) (actual time=0.051..0.102 rows=110 loops=1)
              Hash Cond: (g0."GroupId" = g."Id")
              Buffers: shared hit=3
              ->  Seq Scan on "GroupMembers" g0  (cost=0.00..2.26 rows=126 width=24) (actual time=0.004..0.012 rows=126 loops=1)
                    Buffers: shared hit=1
              ->  Hash  (cost=8.16..8.16 rows=1 width=581) (actual time=0.036..0.037 rows=20 loops=1)
                    Buckets: 1024  Batches: 1  Memory Usage: 10kB
                    Buffers: shared hit=2
                    ->  Index Scan using "IX_Groups_OrganizationId" on "Groups" g  (cost=0.14..8.16 rows=1 width=581) (actual time=0.017..0.024 rows=20 loops=1)
                          Index Cond: ("OrganizationId" = 2)
                          Filter: ("IsActive" AND "IsActive")
                          Buffers: shared hit=2
        ->  Index Scan using "PK_Organizations" on "Organizations" o  (cost=0.14..8.16 rows=1 width=4) (actual time=0.001..0.001 rows=1 loops=110)
              Index Cond: ("Id" = 2)
              Filter: ("IsActive" AND "IsActive")
              Buffers: shared hit=220
Planning Time: 0.253 ms
Execution Time: 0.331 ms
```

1.7 ms in the call:

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
Nested Loop  (cost=43.32..420.37 rows=182 width=352) (actual time=0.143..0.444 rows=361 loops=1)
  Buffers: shared hit=14
  ->  Index Scan using "PK_Organizations" on "Organizations" o  (cost=0.14..8.16 rows=1 width=4) (actual time=0.013..0.014 rows=1 loops=1)
        Index Cond: ("Id" = 2)
        Filter: ("IsActive" AND "IsActive")
        Buffers: shared hit=2
  ->  Hash Left Join  (cost=43.18..410.39 rows=182 width=352) (actual time=0.128..0.373 rows=361 loops=1)
        Hash Cond: (a."CustomTypeId" = a0."Id")
        Buffers: shared hit=12
        ->  Bitmap Heap Scan on "AbsenceDays" a  (cost=18.29..384.80 rows=182 width=54) (actual time=0.099..0.280 rows=361 loops=1)
              Recheck Cond: (("UserId" = 21) AND ("OrganizationId" = 2) AND ("Date" >= '2016-10-19'::date) AND ("Date" <= '2026-09-30'::date))
              Filter: ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 1))
              Heap Blocks: exact=4
              Buffers: shared hit=10
              ->  Bitmap Index Scan on "IX_AbsenceDays_UserId_OrganizationId_Date"  (cost=0.00..13.67 rows=359 width=0) (actual time=0.027..0.028 rows=361 loops=1)
                    Index Cond: (("UserId" = 21) AND ("OrganizationId" = 2) AND ("Date" >= '2016-10-19'::date) AND ("Date" <= '2026-09-30'::date))
                    Buffers: shared hit=4
              SubPlan 1
                ->  Seq Scan on "UserOrganizations" uo  (cost=0.00..4.56 rows=1 width=4) (actual time=0.008..0.028 rows=1 loops=1)
                      Filter: ("IsActive" AND ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer))
                      Rows Removed by Filter: 113
                      Buffers: shared hit=2
        ->  Hash  (cost=24.09..24.09 rows=64 width=298) (actual time=0.020..0.021 rows=3 loops=1)
              Buckets: 1024  Batches: 1  Memory Usage: 9kB
              Buffers: shared hit=2
              ->  Hash Join  (cost=10.92..24.09 rows=64 width=298) (actual time=0.016..0.017 rows=3 loops=1)
                    Hash Cond: (a0."OrganizationId" = o1."Id")
                    Buffers: shared hit=2
                    ->  Seq Scan on "AbsenceTypeDefinitions" a0  (cost=0.00..12.50 rows=250 width=298) (actual time=0.002..0.002 rows=3 loops=1)
                          Buffers: shared hit=1
                    ->  Hash  (cost=10.70..10.70 rows=18 width=4) (actual time=0.006..0.006 rows=2 loops=1)
                          Buckets: 1024  Batches: 1  Memory Usage: 9kB
                          Buffers: shared hit=1
                          ->  Seq Scan on "Organizations" o1  (cost=0.00..10.70 rows=18 width=4) (actual time=0.002..0.002 rows=2 loops=1)
                                Filter: ("IsActive" AND "IsActive")
                                Buffers: shared hit=1
Planning Time: 0.314 ms
Execution Time: 0.551 ms
```
