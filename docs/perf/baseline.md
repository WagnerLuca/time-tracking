<!--
Baseline of docs/PERFORMANCE_PLAN.md, re-measured 2026-10-19 with the revised Phase 0 harness: every size in a fresh
database of its own (PerfApiFactory.ForDatabaseAsync), no background jobs during measurement, core-hours rows for every
calendar day. Code = the staged Phase 0 tree only (exported with `git checkout-index -a -f --prefix=…`, no Phase 1
changes), run with `PERF_SIZES=100x1,100x10 dotnet test --filter Category=Perf` (DOCKER_API_VERSION=1.41), alone on
the machine. Replaces the earlier baseline, which was measured on one shared database with the background jobs running.
-->

# Scaling report — 2026-10-19 09:49

Sizes (employees x years): 100x1, 100x10. Each call: 3 warm-ups, 15 measured (calls slower than 5000 ms: 1 + 5, marked *), over HTTP (TestServer, no network) as the app role with row-level security on, against PostgreSQL 16 in Docker (Testcontainers). Method: every size gets a fresh database of its own (created empty, migrated by its own app instance, seeded with PerfSeeder by binary COPY, then ANALYZEd), measured, and dropped — sizes never share tables, indexes or caches beyond the Postgres server itself; the background jobs (business-day-end/core hours loop, push dispatch, retention) are not running. Times in ms: p50 and p95 by nearest rank over n measured calls (with n < 20 the p95 is the maximum), SQL = commands EF sent for one call (median). Member = perf-user1 (full history), admin = perf-admin1, owner. Month = the last ended month, week = the current one, year = 2026. Growth = p50 ratio between the shortest and longest history at the same team size.

## Seeded data

| size | seconds | Users | WorkSchedules | Holidays | Groups | TimeEntries | TimeEntryChanges | OrgRequests | AbsenceDays | CoreHoursChecks | TerminalPunches | Notifications |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 100x1 | 7.0 | 100 | 100 | 19 | 20 | 25,365 | 1,383 | 1,191 | 3,480 | 33,260 | 25,150 | 15,577 |
| 100x10 | 32.1 | 100 | 365 | 55 | 20 | 252,667 | 16,191 | 10,943 | 32,912 | 330,086 | 29,274 | 150,990 |

## Summary (p50 ms · SQL commands)

| endpoint | kind | 100x1 | 100x10 | t(100x10)/t(100x1) |
|---|---|---:|---:|---:|
| balance | interactive | 63.3 · 22 | 82.8 · 24 | 1.3x (SQL 22 → 24) |
| team-overview | interactive | 204.3 · 10 | 1317.8 · 10 | 6.4x (SQL 10 → 10) |
| inbox: failures | interactive | 4.0 · 1 | 3.6 · 1 | 0.9x (SQL 1 → 1) |
| inbox: approvals | interactive | 20.8 · 7 | 19.9 · 7 | 1.0x (SQL 7 → 7) |
| inbox: user-notifications | interactive | 4.8 · 1 | 5.1 · 1 | 1.1x (SQL 1 → 1) |
| inbox: my-requests | interactive | 18.2 · 6 | 16.8 · 5 | 0.9x (SQL 6 → 5) |
| inbox: notifications | interactive | 6.0 · 2 | 4.8 · 2 | 0.8x (SQL 2 → 2) |
| TimeTracking week | interactive | 7.3 · 2 | 5.6 · 2 | 0.8x (SQL 2 → 2) |
| member entries week | interactive | 21.7 · 7 | 18.4 · 7 | 0.8x (SQL 7 → 7) |
| compliance month | interactive | 21.9 · 7 | 20.4 · 7 | 0.9x (SQL 7 → 7) |
| absences year | interactive | 24.9 · 7 | 23.6 · 7 | 0.9x (SQL 7 → 7) |
| org detail | interactive | 43.3 · 8 | 41.9 · 8 | 1.0x (SQL 8 → 8) |
| accounting summary month | export | 2200.8 · 903 | 4997.5 · 907* | 2.3x (SQL 903 → 907) |
| payroll CSV month | export | 2200.3 · 903 | 5706.9 · 907* | 2.6x (SQL 903 → 907) |
| accounting vacation year | export | 29.4 · 7 | 30.2 · 7 | 1.0x (SQL 7 → 7) |
| daily CSV month | export | 52.8 · 22 | 83.2 · 22 | 1.6x (SQL 22 → 22) |
| monthly PDF | export | 62.9 · 15 | 79.3 · 15 | 1.3x (SQL 15 → 15) |
| timer start | interactive | 17.8 · 7 | 17.0 · 7 | 1.0x (SQL 7 → 7) |
| timer stop | interactive | 19.3 · 7 | 17.4 · 7 | 0.9x (SQL 7 → 7) |
| manual entry create | interactive | 39.0 · 13 | 32.9 · 13 | 0.8x (SQL 13 → 13) |
| manual entry delete | interactive | 20.7 · 7 | 22.1 · 7 | 1.1x (SQL 7 → 7) |

## Per endpoint

### balance (interactive)

| | 100x1 | 100x10 |
|---|---:|---:|
| p50 ms | 63.3 | 82.8 |
| p95 ms (nearest rank) | 119.1 | 108.7 |
| n | 15 | 15 |
| SQL | 22 | 24 |

Growth t(100x10)/t(100x1): 1.3x (SQL 22 → 24)

### team-overview (interactive)

| | 100x1 | 100x10 |
|---|---:|---:|
| p50 ms | 204.3 | 1317.8 |
| p95 ms (nearest rank) | 254.8 | 1452.0 |
| n | 15 | 15 |
| SQL | 10 | 10 |

Growth t(100x10)/t(100x1): 6.4x (SQL 10 → 10)

### inbox: failures (interactive)

| | 100x1 | 100x10 |
|---|---:|---:|
| p50 ms | 4.0 | 3.6 |
| p95 ms (nearest rank) | 4.3 | 4.4 |
| n | 15 | 15 |
| SQL | 1 | 1 |

Growth t(100x10)/t(100x1): 0.9x (SQL 1 → 1)

### inbox: approvals (interactive)

| | 100x1 | 100x10 |
|---|---:|---:|
| p50 ms | 20.8 | 19.9 |
| p95 ms (nearest rank) | 23.1 | 22.5 |
| n | 15 | 15 |
| SQL | 7 | 7 |

Growth t(100x10)/t(100x1): 1.0x (SQL 7 → 7)

### inbox: user-notifications (interactive)

| | 100x1 | 100x10 |
|---|---:|---:|
| p50 ms | 4.8 | 5.1 |
| p95 ms (nearest rank) | 5.1 | 7.1 |
| n | 15 | 15 |
| SQL | 1 | 1 |

Growth t(100x10)/t(100x1): 1.1x (SQL 1 → 1)

### inbox: my-requests (interactive)

| | 100x1 | 100x10 |
|---|---:|---:|
| p50 ms | 18.2 | 16.8 |
| p95 ms (nearest rank) | 20.0 | 20.5 |
| n | 15 | 15 |
| SQL | 6 | 5 |

Growth t(100x10)/t(100x1): 0.9x (SQL 6 → 5)

### inbox: notifications (interactive)

| | 100x1 | 100x10 |
|---|---:|---:|
| p50 ms | 6.0 | 4.8 |
| p95 ms (nearest rank) | 6.8 | 5.5 |
| n | 15 | 15 |
| SQL | 2 | 2 |

Growth t(100x10)/t(100x1): 0.8x (SQL 2 → 2)

### TimeTracking week (interactive)

| | 100x1 | 100x10 |
|---|---:|---:|
| p50 ms | 7.3 | 5.6 |
| p95 ms (nearest rank) | 10.1 | 7.7 |
| n | 15 | 15 |
| SQL | 2 | 2 |

Growth t(100x10)/t(100x1): 0.8x (SQL 2 → 2)

### member entries week (interactive)

| | 100x1 | 100x10 |
|---|---:|---:|
| p50 ms | 21.7 | 18.4 |
| p95 ms (nearest rank) | 28.1 | 20.3 |
| n | 15 | 15 |
| SQL | 7 | 7 |

Growth t(100x10)/t(100x1): 0.8x (SQL 7 → 7)

### compliance month (interactive)

| | 100x1 | 100x10 |
|---|---:|---:|
| p50 ms | 21.9 | 20.4 |
| p95 ms (nearest rank) | 24.1 | 37.6 |
| n | 15 | 15 |
| SQL | 7 | 7 |

Growth t(100x10)/t(100x1): 0.9x (SQL 7 → 7)

### absences year (interactive)

| | 100x1 | 100x10 |
|---|---:|---:|
| p50 ms | 24.9 | 23.6 |
| p95 ms (nearest rank) | 30.6 | 27.1 |
| n | 15 | 15 |
| SQL | 7 | 7 |

Growth t(100x10)/t(100x1): 0.9x (SQL 7 → 7)

### org detail (interactive)

| | 100x1 | 100x10 |
|---|---:|---:|
| p50 ms | 43.3 | 41.9 |
| p95 ms (nearest rank) | 52.9 | 48.6 |
| n | 15 | 15 |
| SQL | 8 | 8 |

Growth t(100x10)/t(100x1): 1.0x (SQL 8 → 8)

### accounting summary month (export)

| | 100x1 | 100x10 |
|---|---:|---:|
| p50 ms | 2200.8 | 4997.5 |
| p95 ms (nearest rank) | 2260.4 | 6826.0 |
| n | 15 | 5 |
| SQL | 903 | 907 |

Growth t(100x10)/t(100x1): 2.3x (SQL 903 → 907)

### payroll CSV month (export)

| | 100x1 | 100x10 |
|---|---:|---:|
| p50 ms | 2200.3 | 5706.9 |
| p95 ms (nearest rank) | 2371.6 | 6942.2 |
| n | 15 | 5 |
| SQL | 903 | 907 |

Growth t(100x10)/t(100x1): 2.6x (SQL 903 → 907)

### accounting vacation year (export)

| | 100x1 | 100x10 |
|---|---:|---:|
| p50 ms | 29.4 | 30.2 |
| p95 ms (nearest rank) | 33.8 | 32.4 |
| n | 15 | 15 |
| SQL | 7 | 7 |

Growth t(100x10)/t(100x1): 1.0x (SQL 7 → 7)

### daily CSV month (export)

| | 100x1 | 100x10 |
|---|---:|---:|
| p50 ms | 52.8 | 83.2 |
| p95 ms (nearest rank) | 56.8 | 91.4 |
| n | 15 | 15 |
| SQL | 22 | 22 |

Growth t(100x10)/t(100x1): 1.6x (SQL 22 → 22)

### monthly PDF (export)

| | 100x1 | 100x10 |
|---|---:|---:|
| p50 ms | 62.9 | 79.3 |
| p95 ms (nearest rank) | 75.0 | 91.6 |
| n | 15 | 15 |
| SQL | 15 | 15 |

Growth t(100x10)/t(100x1): 1.3x (SQL 15 → 15)

### timer start (interactive)

| | 100x1 | 100x10 |
|---|---:|---:|
| p50 ms | 17.8 | 17.0 |
| p95 ms (nearest rank) | 24.7 | 21.7 |
| n | 15 | 15 |
| SQL | 7 | 7 |

Growth t(100x10)/t(100x1): 1.0x (SQL 7 → 7)

### timer stop (interactive)

| | 100x1 | 100x10 |
|---|---:|---:|
| p50 ms | 19.3 | 17.4 |
| p95 ms (nearest rank) | 22.4 | 18.7 |
| n | 15 | 15 |
| SQL | 7 | 7 |

Growth t(100x10)/t(100x1): 0.9x (SQL 7 → 7)

### manual entry create (interactive)

| | 100x1 | 100x10 |
|---|---:|---:|
| p50 ms | 39.0 | 32.9 |
| p95 ms (nearest rank) | 47.0 | 35.4 |
| n | 15 | 15 |
| SQL | 13 | 13 |

Growth t(100x10)/t(100x1): 0.8x (SQL 13 → 13)

### manual entry delete (interactive)

| | 100x1 | 100x10 |
|---|---:|---:|
| p50 ms | 20.7 | 22.1 |
| p95 ms (nearest rank) | 28.2 | 24.4 |
| n | 15 | 15 |
| SQL | 7 | 7 |

Growth t(100x10)/t(100x1): 1.1x (SQL 7 → 7)

## EXPLAIN (ANALYZE, BUFFERS) — the 5 slowest calls at 100x10

The slowest SELECTs (up to 3) of the last measured call, re-run as the app role for the same user (RLS on).

### payroll CSV month — p50 5706.9 ms, 907 SQL

7.4 ms in the call:

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
HashAggregate  (cost=3659.01..3661.01 rows=200 width=4) (actual time=3.999..4.012 rows=100 loops=1)
  Group Key: t."UserId"
  Batches: 1  Memory Usage: 40kB
  Buffers: shared hit=2361
  ->  HashAggregate  (cost=3648.65..3656.94 rows=829 width=4) (actual time=3.968..3.983 rows=100 loops=1)
        Group Key: t."UserId"
        Batches: 1  Memory Usage: 49kB
        Buffers: shared hit=2361
        ->  Append  (cost=5.13..3646.58 rows=829 width=4) (actual time=0.151..3.616 rows=2671 loops=1)
              Buffers: shared hit=2361
              ->  Nested Loop Left Join  (cost=5.13..3245.53 rows=610 width=4) (actual time=0.150..2.964 rows=2265 loops=1)
                    Filter: ((t."OrganizationId" IS NULL) OR o."IsActive")
                    Buffers: shared hit=2280
                    ->  Index Scan using "IX_TimeEntries_OrganizationId_StartTime" on "TimeEntries" t  (cost=4.99..3222.11 rows=1220 width=8) (actual time=0.133..2.471 rows=2265 loops=1)
                          Index Cond: (("OrganizationId" = 2) AND ("StartTime" >= '2026-08-31 22:00:00+00'::timestamp with time zone) AND ("StartTime" < '2026-09-30 22:00:00+00'::timestamp with time zone))
                          Filter: ((NOT "IsRunning") AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 1)))
                          Buffers: shared hit=2278
                          SubPlan 1
                            ->  Seq Scan on "UserOrganizations" uo  (cost=0.00..4.56 rows=1 width=4) (actual time=0.011..0.036 rows=1 loops=1)
                                  Filter: ("IsActive" AND ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer))
                                  Rows Removed by Filter: 113
                                  Buffers: shared hit=2
                    ->  Materialize  (cost=0.14..8.17 rows=1 width=5) (actual time=0.000..0.000 rows=1 loops=2265)
                          Buffers: shared hit=2
                          ->  Index Scan using "PK_Organizations" on "Organizations" o  (cost=0.14..8.16 rows=1 width=5) (actual time=0.011..0.011 rows=1 loops=1)
                                Index Cond: ("Id" = 2)
                                Filter: "IsActive"
                                Buffers: shared hit=2
              ->  Nested Loop  (cost=14.45..396.91 rows=219 width=4) (actual time=0.099..0.451 rows=406 loops=1)
                    Buffers: shared hit=81
                    ->  Index Scan using "PK_Organizations" on "Organizations" o1  (cost=0.14..8.16 rows=1 width=4) (actual time=0.014..0.015 rows=1 loops=1)
                          Index Cond: ("Id" = 2)
                          Filter: ("IsActive" AND "IsActive")
                          Buffers: shared hit=2
                    ->  Bitmap Heap Scan on "AbsenceDays" a  (cost=14.31..386.56 rows=219 width=8) (actual time=0.082..0.375 rows=406 loops=1)
                          Recheck Cond: (("OrganizationId" = 2) AND ("Date" >= '2026-09-01'::date) AND ("Date" <= '2026-09-30'::date))
                          Filter: ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 2))
                          Heap Blocks: exact=75
                          Buffers: shared hit=79
                          ->  Bitmap Index Scan on "IX_AbsenceDays_OrganizationId_Date"  (cost=0.00..9.69 rows=432 width=0) (actual time=0.032..0.032 rows=406 loops=1)
                                Index Cond: (("OrganizationId" = 2) AND ("Date" >= '2026-09-01'::date) AND ("Date" <= '2026-09-30'::date))
                                Buffers: shared hit=2
                          SubPlan 2
                            ->  Seq Scan on "UserOrganizations" uo_1  (cost=0.00..4.56 rows=1 width=4) (actual time=0.008..0.031 rows=1 loops=1)
                                  Filter: ("IsActive" AND ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer))
                                  Rows Removed by Filter: 113
                                  Buffers: shared hit=2
Planning Time: 0.518 ms
Execution Time: 4.251 ms
```

6.6 ms in the call:

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
Sort  (cost=3462.42..3464.17 rows=698 width=94) (actual time=3.727..3.836 rows=2802 loops=1)
  Sort Key: t."StartTime"
  Sort Method: quicksort  Memory: 381kB
  Buffers: shared hit=53
  ->  Nested Loop Left Join  (cost=83.97..3429.45 rows=698 width=94) (actual time=0.220..2.894 rows=2802 loops=1)
        Filter: ((t."OrganizationId" IS NULL) OR o."IsActive")
        Buffers: shared hit=53
        ->  Bitmap Heap Scan on "TimeEntries" t  (cost=83.82..3403.83 rows=1397 width=94) (actual time=0.208..2.105 rows=2802 loops=1)
              Recheck Cond: (("UserId" = 68) AND ("StartTime" >= '2016-10-18 22:00:00+00'::timestamp with time zone) AND ("StartTime" < '2026-09-30 22:00:00+00'::timestamp with time zone))
              Filter: ((NOT "IsRunning") AND ("OrganizationId" = 2) AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 1)))
              Heap Blocks: exact=36
              Buffers: shared hit=51
              ->  Bitmap Index Scan on "IX_TimeEntries_UserId_StartTime"  (cost=0.00..78.91 rows=2759 width=0) (actual time=0.153..0.153 rows=2802 loops=1)
                    Index Cond: (("UserId" = 68) AND ("StartTime" >= '2016-10-18 22:00:00+00'::timestamp with time zone) AND ("StartTime" < '2026-09-30 22:00:00+00'::timestamp with time zone))
                    Buffers: shared hit=13
              SubPlan 1
                ->  Seq Scan on "UserOrganizations" uo  (cost=0.00..4.56 rows=1 width=4) (actual time=0.008..0.032 rows=1 loops=1)
                      Filter: ("IsActive" AND ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer))
                      Rows Removed by Filter: 113
                      Buffers: shared hit=2
        ->  Materialize  (cost=0.14..8.17 rows=1 width=5) (actual time=0.000..0.000 rows=1 loops=2802)
              Buffers: shared hit=2
              ->  Index Scan using "PK_Organizations" on "Organizations" o  (cost=0.14..8.16 rows=1 width=5) (actual time=0.008..0.008 rows=1 loops=1)
                    Index Cond: ("Id" = 2)
                    Filter: "IsActive"
                    Buffers: shared hit=2
Planning Time: 0.243 ms
Execution Time: 4.037 ms
```

6.3 ms in the call:

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
Sort  (cost=3448.62..3450.34 rows=688 width=94) (actual time=4.003..4.118 rows=2805 loops=1)
  Sort Key: t."StartTime"
  Sort Method: quicksort  Memory: 381kB
  Buffers: shared hit=53
  ->  Nested Loop Left Join  (cost=83.42..3416.19 rows=688 width=94) (actual time=0.251..3.179 rows=2805 loops=1)
        Filter: ((t."OrganizationId" IS NULL) OR o."IsActive")
        Buffers: shared hit=53
        ->  Bitmap Heap Scan on "TimeEntries" t  (cost=83.28..3390.84 rows=1375 width=94) (actual time=0.237..2.357 rows=2805 loops=1)
              Recheck Cond: (("UserId" = 56) AND ("StartTime" >= '2016-10-18 22:00:00+00'::timestamp with time zone) AND ("StartTime" < '2026-09-30 22:00:00+00'::timestamp with time zone))
              Filter: ((NOT "IsRunning") AND ("OrganizationId" = 2) AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 1)))
              Heap Blocks: exact=36
              Buffers: shared hit=51
              ->  Bitmap Index Scan on "IX_TimeEntries_UserId_StartTime"  (cost=0.00..78.37 rows=2716 width=0) (actual time=0.144..0.144 rows=2805 loops=1)
                    Index Cond: (("UserId" = 56) AND ("StartTime" >= '2016-10-18 22:00:00+00'::timestamp with time zone) AND ("StartTime" < '2026-09-30 22:00:00+00'::timestamp with time zone))
                    Buffers: shared hit=13
              SubPlan 1
                ->  Seq Scan on "UserOrganizations" uo  (cost=0.00..4.56 rows=1 width=4) (actual time=0.039..0.076 rows=1 loops=1)
                      Filter: ("IsActive" AND ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer))
                      Rows Removed by Filter: 113
                      Buffers: shared hit=2
        ->  Materialize  (cost=0.14..8.17 rows=1 width=5) (actual time=0.000..0.000 rows=1 loops=2805)
              Buffers: shared hit=2
              ->  Index Scan using "PK_Organizations" on "Organizations" o  (cost=0.14..8.16 rows=1 width=5) (actual time=0.009..0.009 rows=1 loops=1)
                    Index Cond: ("Id" = 2)
                    Filter: "IsActive"
                    Buffers: shared hit=2
Planning Time: 0.224 ms
Execution Time: 4.320 ms
```

### accounting summary month — p50 4997.5 ms, 907 SQL

10.4 ms in the call:

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
Sort  (cost=3472.76..3474.53 rows=707 width=94) (actual time=4.010..4.144 rows=2806 loops=1)
  Sort Key: t."StartTime"
  Sort Method: quicksort  Memory: 381kB
  Buffers: shared hit=54
  ->  Nested Loop Left Join  (cost=84.38..3439.30 rows=707 width=94) (actual time=0.203..3.140 rows=2806 loops=1)
        Filter: ((t."OrganizationId" IS NULL) OR o."IsActive")
        Buffers: shared hit=54
        ->  Bitmap Heap Scan on "TimeEntries" t  (cost=84.24..3413.46 rows=1414 width=94) (actual time=0.190..2.296 rows=2806 loops=1)
              Recheck Cond: (("UserId" = 112) AND ("StartTime" >= '2016-10-18 22:00:00+00'::timestamp with time zone) AND ("StartTime" < '2026-09-30 22:00:00+00'::timestamp with time zone))
              Filter: ((NOT "IsRunning") AND ("OrganizationId" = 2) AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 1)))
              Heap Blocks: exact=36
              Buffers: shared hit=52
              ->  Bitmap Index Scan on "IX_TimeEntries_UserId_StartTime"  (cost=0.00..79.32 rows=2792 width=0) (actual time=0.144..0.145 rows=2806 loops=1)
                    Index Cond: (("UserId" = 112) AND ("StartTime" >= '2016-10-18 22:00:00+00'::timestamp with time zone) AND ("StartTime" < '2026-09-30 22:00:00+00'::timestamp with time zone))
                    Buffers: shared hit=14
              SubPlan 1
                ->  Seq Scan on "UserOrganizations" uo  (cost=0.00..4.56 rows=1 width=4) (actual time=0.007..0.031 rows=1 loops=1)
                      Filter: ("IsActive" AND ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer))
                      Rows Removed by Filter: 113
                      Buffers: shared hit=2
        ->  Materialize  (cost=0.14..8.17 rows=1 width=5) (actual time=0.000..0.000 rows=1 loops=2806)
              Buffers: shared hit=2
              ->  Index Scan using "PK_Organizations" on "Organizations" o  (cost=0.14..8.16 rows=1 width=5) (actual time=0.009..0.009 rows=1 loops=1)
                    Index Cond: ("Id" = 2)
                    Filter: "IsActive"
                    Buffers: shared hit=2
Planning Time: 0.262 ms
Execution Time: 4.352 ms
```

9.7 ms in the call:

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
Sort  (cost=3499.66..3501.47 rows=726 width=94) (actual time=3.761..3.891 rows=2685 loops=1)
  Sort Key: t."StartTime"
  Sort Method: quicksort  Memory: 369kB
  Buffers: shared hit=51
  ->  Nested Loop Left Join  (cost=89.34..3465.16 rows=726 width=94) (actual time=0.241..2.953 rows=2685 loops=1)
        Filter: ((t."OrganizationId" IS NULL) OR o."IsActive")
        Buffers: shared hit=51
        ->  Bitmap Heap Scan on "TimeEntries" t  (cost=89.20..3438.85 rows=1452 width=94) (actual time=0.227..2.163 rows=2685 loops=1)
              Recheck Cond: (("UserId" = 17) AND ("StartTime" >= '2016-10-18 22:00:00+00'::timestamp with time zone) AND ("StartTime" < '2026-09-30 22:00:00+00'::timestamp with time zone))
              Filter: ((NOT "IsRunning") AND ("OrganizationId" = 2) AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 1)))
              Heap Blocks: exact=34
              Buffers: shared hit=49
              ->  Bitmap Index Scan on "IX_TimeEntries_UserId_StartTime"  (cost=0.00..84.27 rows=2868 width=0) (actual time=0.178..0.178 rows=2685 loops=1)
                    Index Cond: (("UserId" = 17) AND ("StartTime" >= '2016-10-18 22:00:00+00'::timestamp with time zone) AND ("StartTime" < '2026-09-30 22:00:00+00'::timestamp with time zone))
                    Buffers: shared hit=13
              SubPlan 1
                ->  Seq Scan on "UserOrganizations" uo  (cost=0.00..4.56 rows=1 width=4) (actual time=0.008..0.031 rows=1 loops=1)
                      Filter: ("IsActive" AND ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer))
                      Rows Removed by Filter: 113
                      Buffers: shared hit=2
        ->  Materialize  (cost=0.14..8.17 rows=1 width=5) (actual time=0.000..0.000 rows=1 loops=2685)
              Buffers: shared hit=2
              ->  Index Scan using "PK_Organizations" on "Organizations" o  (cost=0.14..8.16 rows=1 width=5) (actual time=0.009..0.009 rows=1 loops=1)
                    Index Cond: ("Id" = 2)
                    Filter: "IsActive"
                    Buffers: shared hit=2
Planning Time: 0.215 ms
Execution Time: 4.095 ms
```

9.6 ms in the call:

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
Sort  (cost=3440.45..3442.16 rows=681 width=94) (actual time=4.569..4.680 rows=2790 loops=1)
  Sort Key: t."StartTime"
  Sort Method: quicksort  Memory: 380kB
  Buffers: shared hit=53
  ->  Nested Loop Left Join  (cost=83.11..3408.41 rows=681 width=94) (actual time=0.276..3.624 rows=2790 loops=1)
        Filter: ((t."OrganizationId" IS NULL) OR o."IsActive")
        Buffers: shared hit=53
        ->  Bitmap Heap Scan on "TimeEntries" t  (cost=82.97..3383.22 rows=1362 width=94) (actual time=0.258..2.680 rows=2790 loops=1)
              Recheck Cond: (("UserId" = 34) AND ("StartTime" >= '2016-10-18 22:00:00+00'::timestamp with time zone) AND ("StartTime" < '2026-09-30 22:00:00+00'::timestamp with time zone))
              Filter: ((NOT "IsRunning") AND ("OrganizationId" = 2) AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 1)))
              Heap Blocks: exact=36
              Buffers: shared hit=51
              ->  Bitmap Index Scan on "IX_TimeEntries_UserId_StartTime"  (cost=0.00..78.06 rows=2691 width=0) (actual time=0.174..0.175 rows=2790 loops=1)
                    Index Cond: (("UserId" = 34) AND ("StartTime" >= '2016-10-18 22:00:00+00'::timestamp with time zone) AND ("StartTime" < '2026-09-30 22:00:00+00'::timestamp with time zone))
                    Buffers: shared hit=13
              SubPlan 1
                ->  Seq Scan on "UserOrganizations" uo  (cost=0.00..4.56 rows=1 width=4) (actual time=0.013..0.059 rows=1 loops=1)
                      Filter: ("IsActive" AND ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer))
                      Rows Removed by Filter: 113
                      Buffers: shared hit=2
        ->  Materialize  (cost=0.14..8.17 rows=1 width=5) (actual time=0.000..0.000 rows=1 loops=2790)
              Buffers: shared hit=2
              ->  Index Scan using "PK_Organizations" on "Organizations" o  (cost=0.14..8.16 rows=1 width=5) (actual time=0.013..0.013 rows=1 loops=1)
                    Index Cond: ("Id" = 2)
                    Filter: "IsActive"
                    Buffers: shared hit=2
Planning Time: 0.341 ms
Execution Time: 4.928 ms
```

### team-overview — p50 1317.8 ms, 10 SQL

2.4 ms in the call:

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
Sort  (cost=39.25..40.13 rows=349 width=92) (actual time=0.308..0.323 rows=361 loops=1)
  Sort Key: w."ValidFrom"
  Sort Method: quicksort  Memory: 70kB
  Buffers: shared hit=8
  ->  Nested Loop  (cost=0.39..24.51 rows=349 width=92) (actual time=0.028..0.188 rows=361 loops=1)
        Buffers: shared hit=8
        ->  Index Scan using "PK_Organizations" on "Organizations" o  (cost=0.14..8.16 rows=1 width=4) (actual time=0.015..0.015 rows=1 loops=1)
              Index Cond: ("Id" = 2)
              Filter: ("IsActive" AND "IsActive")
              Buffers: shared hit=2
        ->  Seq Scan on "WorkSchedules" w  (cost=0.25..12.86 rows=349 width=92) (actual time=0.012..0.119 rows=361 loops=1)
              Filter: (("OrganizationId" = 2) AND ("UserId" = ANY ('{15,17,18,19,20,21,22,23,24,25,26,27,28,29,30,31,32,33,34,35,36,37,38,39,40,41,42,43,44,45,46,47,48,49,50,51,52,53,54,55,56,57,58,59,60,61,62,63,64,65,66,67,68,69,70,71,72,73,74,75,76,77,78,79,80,81,82,83,84,85,86,87,88,89,90,91,92,93,94,95,96,97,98,99,100,101,102,103,104,105,106,107,108,109,110,111,112,113,114}'::integer[])))
              Rows Removed by Filter: 17
              Buffers: shared hit=6
Planning Time: 0.219 ms
Execution Time: 0.381 ms
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
WHERE o0."IsActive" AND a."OrganizationId" = @org_Id AND a."UserId" = ANY (@userIds) AND a."Date" >= @from AND a."Date" <= @today
```

```text
Nested Loop  (cost=29.85..2004.28 rows=16358 width=352) (actual time=0.102..33.584 rows=32375 loops=1)
  Buffers: shared hit=344
  ->  Index Scan using "PK_Organizations" on "Organizations" o  (cost=0.14..8.16 rows=1 width=4) (actual time=0.011..0.013 rows=1 loops=1)
        Index Cond: ("Id" = 2)
        Filter: ("IsActive" AND "IsActive")
        Buffers: shared hit=2
  ->  Hash Left Join  (cost=29.71..1832.54 rows=16358 width=352) (actual time=0.090..27.898 rows=32375 loops=1)
        Hash Cond: (a."CustomTypeId" = a0."Id")
        Buffers: shared hit=342
        ->  Seq Scan on "AbsenceDays" a  (cost=4.81..1746.29 rows=16358 width=54) (actual time=0.061..21.296 rows=32375 loops=1)
              Filter: (("Date" >= '2016-10-19'::date) AND ("Date" <= '2026-10-19'::date) AND ("OrganizationId" = 2) AND ("UserId" = ANY ('{15,17,18,19,20,21,22,23,24,25,26,27,28,29,30,31,32,33,34,35,36,37,38,39,40,41,42,43,44,45,46,47,48,49,50,51,52,53,54,55,56,57,58,59,60,61,62,63,64,65,66,67,68,69,70,71,72,73,74,75,76,77,78,79,80,81,82,83,84,85,86,87,88,89,90,91,92,93,94,95,96,97,98,99,100,101,102,103,104,105,106,107,108,109,110,111,112,113,114}'::integer[])) AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 1)))
              Rows Removed by Filter: 648
              Buffers: shared hit=340
              SubPlan 1
                ->  Seq Scan on "UserOrganizations" uo  (cost=0.00..4.56 rows=1 width=4) (actual time=0.007..0.033 rows=1 loops=1)
                      Filter: ("IsActive" AND ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer))
                      Rows Removed by Filter: 113
                      Buffers: shared hit=2
        ->  Hash  (cost=24.09..24.09 rows=64 width=298) (actual time=0.021..0.023 rows=3 loops=1)
              Buckets: 1024  Batches: 1  Memory Usage: 9kB
              Buffers: shared hit=2
              ->  Hash Join  (cost=10.92..24.09 rows=64 width=298) (actual time=0.016..0.018 rows=3 loops=1)
                    Hash Cond: (a0."OrganizationId" = o1."Id")
                    Buffers: shared hit=2
                    ->  Seq Scan on "AbsenceTypeDefinitions" a0  (cost=0.00..12.50 rows=250 width=298) (actual time=0.003..0.003 rows=3 loops=1)
                          Buffers: shared hit=1
                    ->  Hash  (cost=10.70..10.70 rows=18 width=4) (actual time=0.006..0.006 rows=2 loops=1)
                          Buckets: 1024  Batches: 1  Memory Usage: 9kB
                          Buffers: shared hit=1
                          ->  Seq Scan on "Organizations" o1  (cost=0.00..10.70 rows=18 width=4) (actual time=0.001..0.002 rows=2 loops=1)
                                Filter: ("IsActive" AND "IsActive")
                                Buffers: shared hit=1
Planning Time: 0.408 ms
Execution Time: 34.628 ms
```

1.8 ms in the call:

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
Nested Loop  (cost=5.13..444.08 rows=1 width=63) (actual time=0.239..0.240 rows=0 loops=1)
  Buffers: shared hit=297
  ->  Index Scan using "IX_CoreHoursChecks_OrganizationId_UserId_Date" on "CoreHoursChecks" c  (cost=4.99..435.91 rows=1 width=63) (actual time=0.238..0.238 rows=0 loops=1)
        Index Cond: (("OrganizationId" = 2) AND ("UserId" = ANY ('{15,17,18,19,20,21,22,23,24,25,26,27,28,29,30,31,32,33,34,35,36,37,38,39,40,41,42,43,44,45,46,47,48,49,50,51,52,53,54,55,56,57,58,59,60,61,62,63,64,65,66,67,68,69,70,71,72,73,74,75,76,77,78,79,80,81,82,83,84,85,86,87,88,89,90,91,92,93,94,95,96,97,98,99,100,101,102,103,104,105,106,107,108,109,110,111,112,113,114}'::integer[])) AND ("Date" >= '2026-10-19'::date) AND ("Date" <= '2026-10-19'::date))
        Filter: (("Issues" <> 0) AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 1)))
        Buffers: shared hit=297
        SubPlan 1
          ->  Seq Scan on "UserOrganizations" uo  (cost=0.00..4.56 rows=1 width=4) (never executed)
                Filter: ("IsActive" AND ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer))
  ->  Index Scan using "PK_Organizations" on "Organizations" o  (cost=0.14..8.16 rows=1 width=4) (never executed)
        Index Cond: ("Id" = 2)
        Filter: ("IsActive" AND "IsActive")
Planning Time: 0.278 ms
Execution Time: 0.287 ms
```

### daily CSV month — p50 83.2 ms, 22 SQL

4.9 ms in the call:

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
Sort  (cost=3557.25..3559.21 rows=782 width=94) (actual time=5.687..5.841 rows=2811 loops=1)
  Sort Key: t."StartTime"
  Sort Method: quicksort  Memory: 382kB
  Buffers: shared hit=54
  ->  Nested Loop Left Join  (cost=92.11..3519.67 rows=782 width=94) (actual time=0.358..4.407 rows=2811 loops=1)
        Filter: ((t."OrganizationId" IS NULL) OR o."IsActive")
        Buffers: shared hit=54
        ->  Bitmap Heap Scan on "TimeEntries" t  (cost=91.97..3491.97 rows=1563 width=94) (actual time=0.337..3.308 rows=2811 loops=1)
              Recheck Cond: (("UserId" = 21) AND ("StartTime" >= '2016-10-18 22:00:00+00'::timestamp with time zone) AND ("StartTime" < '2026-09-30 22:00:00+00'::timestamp with time zone))
              Filter: ((NOT "IsRunning") AND ("OrganizationId" = 2) AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 1)))
              Heap Blocks: exact=36
              Buffers: shared hit=52
              ->  Bitmap Index Scan on "IX_TimeEntries_UserId_StartTime"  (cost=0.00..87.01 rows=3087 width=0) (actual time=0.244..0.245 rows=2811 loops=1)
                    Index Cond: (("UserId" = 21) AND ("StartTime" >= '2016-10-18 22:00:00+00'::timestamp with time zone) AND ("StartTime" < '2026-09-30 22:00:00+00'::timestamp with time zone))
                    Buffers: shared hit=14
              SubPlan 1
                ->  Seq Scan on "UserOrganizations" uo  (cost=0.00..4.56 rows=1 width=4) (actual time=0.014..0.060 rows=1 loops=1)
                      Filter: ("IsActive" AND ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer))
                      Rows Removed by Filter: 113
                      Buffers: shared hit=2
        ->  Materialize  (cost=0.14..8.17 rows=1 width=5) (actual time=0.000..0.000 rows=1 loops=2811)
              Buffers: shared hit=2
              ->  Index Scan using "PK_Organizations" on "Organizations" o  (cost=0.14..8.16 rows=1 width=5) (actual time=0.013..0.013 rows=1 loops=1)
                    Index Cond: ("Id" = 2)
                    Filter: "IsActive"
                    Buffers: shared hit=2
Planning Time: 0.208 ms
Execution Time: 6.133 ms
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
WHERE o0."IsActive" AND a."UserId" = @userId AND a."OrganizationId" = @orgId AND a."Date" >= @dateFrom AND a."Date" <= @dateTo
```

```text
Nested Loop  (cost=43.29..420.00 rows=182 width=352) (actual time=0.118..0.510 rows=361 loops=1)
  Buffers: shared hit=14
  ->  Index Scan using "PK_Organizations" on "Organizations" o  (cost=0.14..8.16 rows=1 width=4) (actual time=0.012..0.014 rows=1 loops=1)
        Index Cond: ("Id" = 2)
        Filter: ("IsActive" AND "IsActive")
        Buffers: shared hit=2
  ->  Hash Left Join  (cost=43.15..410.02 rows=182 width=352) (actual time=0.104..0.432 rows=361 loops=1)
        Hash Cond: (a."CustomTypeId" = a0."Id")
        Buffers: shared hit=12
        ->  Bitmap Heap Scan on "AbsenceDays" a  (cost=18.26..384.44 rows=182 width=54) (actual time=0.075..0.331 rows=361 loops=1)
              Recheck Cond: (("UserId" = 21) AND ("OrganizationId" = 2) AND ("Date" >= '2016-10-19'::date) AND ("Date" <= '2026-09-30'::date))
              Filter: ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 1))
              Heap Blocks: exact=4
              Buffers: shared hit=10
              ->  Bitmap Index Scan on "IX_AbsenceDays_UserId_OrganizationId_Date"  (cost=0.00..13.64 rows=357 width=0) (actual time=0.031..0.031 rows=361 loops=1)
                    Index Cond: (("UserId" = 21) AND ("OrganizationId" = 2) AND ("Date" >= '2016-10-19'::date) AND ("Date" <= '2026-09-30'::date))
                    Buffers: shared hit=4
              SubPlan 1
                ->  Seq Scan on "UserOrganizations" uo  (cost=0.00..4.56 rows=1 width=4) (actual time=0.008..0.032 rows=1 loops=1)
                      Filter: ("IsActive" AND ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer))
                      Rows Removed by Filter: 113
                      Buffers: shared hit=2
        ->  Hash  (cost=24.09..24.09 rows=64 width=298) (actual time=0.020..0.021 rows=3 loops=1)
              Buckets: 1024  Batches: 1  Memory Usage: 9kB
              Buffers: shared hit=2
              ->  Hash Join  (cost=10.92..24.09 rows=64 width=298) (actual time=0.015..0.017 rows=3 loops=1)
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
Planning Time: 0.373 ms
Execution Time: 0.665 ms
```

2.4 ms in the call:

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
Nested Loop  (cost=5.01..251.85 rows=31 width=63) (actual time=0.075..0.766 rows=71 loops=1)
  Buffers: shared hit=54
  ->  Index Scan using "PK_Organizations" on "Organizations" o  (cost=0.14..8.16 rows=1 width=4) (actual time=0.010..0.011 rows=1 loops=1)
        Index Cond: ("Id" = 2)
        Filter: ("IsActive" AND "IsActive")
        Buffers: shared hit=2
  ->  Index Scan using "IX_CoreHoursChecks_UserId" on "CoreHoursChecks" c  (cost=4.87..243.38 rows=31 width=63) (actual time=0.063..0.742 rows=71 loops=1)
        Index Cond: ("UserId" = ANY ('{21}'::integer[]))
        Filter: (("Date" >= '2016-10-19'::date) AND ("Date" <= '2026-09-30'::date) AND ("Issues" <> 0) AND ("OrganizationId" = 2) AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 1)))
        Rows Removed by Filter: 3581
        Buffers: shared hit=52
        SubPlan 1
          ->  Seq Scan on "UserOrganizations" uo  (cost=0.00..4.56 rows=1 width=4) (actual time=0.013..0.037 rows=1 loops=1)
                Filter: ("IsActive" AND ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer))
                Rows Removed by Filter: 113
                Buffers: shared hit=2
Planning Time: 0.224 ms
Execution Time: 0.821 ms
```

### balance — p50 82.8 ms, 24 SQL

5.1 ms in the call:

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
Sort  (cost=3560.52..3562.49 rows=786 width=94) (actual time=3.444..3.564 rows=2863 loops=1)
  Sort Key: t."StartTime"
  Sort Method: quicksort  Memory: 387kB
  Buffers: shared hit=53
  ->  Nested Loop Left Join  (cost=92.30..3522.72 rows=786 width=94) (actual time=0.169..2.603 rows=2863 loops=1)
        Filter: ((t."OrganizationId" IS NULL) OR o."IsActive")
        Buffers: shared hit=53
        ->  Bitmap Heap Scan on "TimeEntries" t  (cost=92.16..3494.92 rows=1571 width=94) (actual time=0.156..1.792 rows=2863 loops=1)
              Recheck Cond: (("UserId" = 21) AND ("StartTime" >= '2016-10-18 22:00:00+00'::timestamp with time zone) AND ("StartTime" < '2026-10-19 22:00:00+00'::timestamp with time zone))
              Filter: ((NOT "IsRunning") AND ("OrganizationId" = 2) AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 1)))
              Heap Blocks: exact=37
              Buffers: shared hit=51
              ->  Bitmap Index Scan on "IX_TimeEntries_UserId_StartTime"  (cost=0.00..87.20 rows=3102 width=0) (actual time=0.144..0.145 rows=2866 loops=1)
                    Index Cond: (("UserId" = 21) AND ("StartTime" >= '2016-10-18 22:00:00+00'::timestamp with time zone) AND ("StartTime" < '2026-10-19 22:00:00+00'::timestamp with time zone))
                    Buffers: shared hit=14
              SubPlan 1
                ->  Seq Scan on "UserOrganizations" uo  (cost=0.00..4.56 rows=1 width=4) (never executed)
                      Filter: ("IsActive" AND ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer))
        ->  Materialize  (cost=0.14..8.17 rows=1 width=5) (actual time=0.000..0.000 rows=1 loops=2863)
              Buffers: shared hit=2
              ->  Index Scan using "PK_Organizations" on "Organizations" o  (cost=0.14..8.16 rows=1 width=5) (actual time=0.008..0.008 rows=1 loops=1)
                    Index Cond: ("Id" = 2)
                    Filter: "IsActive"
                    Buffers: shared hit=2
Planning Time: 0.210 ms
Execution Time: 3.761 ms
```

2.8 ms in the call:

```sql
SELECT count(*)::int
FROM "TimeEntries" AS t
LEFT JOIN (
    SELECT o."Id", o."IsActive"
    FROM "Organizations" AS o
    WHERE o."IsActive"
) AS o0 ON t."OrganizationId" = o0."Id"
WHERE (t."OrganizationId" IS NULL OR o0."IsActive") AND t."UserId" = @targetUserId AND t."OrganizationId" = @context_Org_Id AND t."IsFailure"
```

```text
Aggregate  (cost=3471.68..3471.70 rows=1 width=4) (actual time=0.426..0.427 rows=1 loops=1)
  Buffers: shared hit=51
  ->  Nested Loop Left Join  (cost=76.40..3471.68 rows=1 width=0) (actual time=0.424..0.424 rows=0 loops=1)
        Filter: ((t."OrganizationId" IS NULL) OR o."IsActive")
        Buffers: shared hit=51
        ->  Bitmap Heap Scan on "TimeEntries" t  (cost=76.25..3463.51 rows=1 width=4) (actual time=0.423..0.424 rows=0 loops=1)
              Recheck Cond: ("UserId" = 21)
              Filter: ("IsFailure" AND ("OrganizationId" = 2) AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 1)))
              Rows Removed by Filter: 2863
              Heap Blocks: exact=37
              Buffers: shared hit=51
              ->  Bitmap Index Scan on "IX_TimeEntries_UserId_StartTime"  (cost=0.00..71.69 rows=3102 width=0) (actual time=0.127..0.128 rows=2866 loops=1)
                    Index Cond: ("UserId" = 21)
                    Buffers: shared hit=14
              SubPlan 1
                ->  Seq Scan on "UserOrganizations" uo  (cost=0.00..4.56 rows=1 width=4) (never executed)
                      Filter: ("IsActive" AND ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer))
        ->  Index Scan using "PK_Organizations" on "Organizations" o  (cost=0.14..8.16 rows=1 width=5) (never executed)
              Index Cond: ("Id" = 2)
              Filter: "IsActive"
Planning Time: 0.185 ms
Execution Time: 0.497 ms
```

2.5 ms in the call:

```sql
SELECT t."UserId", t."StartTime", t."EndTime", t."IsRunning"
FROM "TimeEntries" AS t
LEFT JOIN (
    SELECT o."Id", o."IsActive"
    FROM "Organizations" AS o
    WHERE o."IsActive"
) AS o0 ON t."OrganizationId" = o0."Id"
WHERE (t."OrganizationId" IS NULL OR o0."IsActive") AND t."UserId" = ANY (@userIds) AND t."OrganizationId" = @orgId AND NOT (t."IsFailure") AND t."StartTime" < @toUtc AND (t."IsRunning" OR t."EndTime" > @fromUtc)
```

```text
Nested Loop Left Join  (cost=84.15..3491.16 rows=4 width=21) (actual time=0.557..0.597 rows=52 loops=1)
  Filter: ((t."OrganizationId" IS NULL) OR o."IsActive")
  Buffers: shared hit=53
  ->  Bitmap Heap Scan on "TimeEntries" t  (cost=84.01..3482.90 rows=8 width=25) (actual time=0.538..0.567 rows=52 loops=1)
        Recheck Cond: (("UserId" = ANY ('{21}'::integer[])) AND ("StartTime" < '2026-11-01 23:00:00+00'::timestamp with time zone))
        Filter: ((NOT "IsFailure") AND ("IsRunning" OR ("EndTime" > '2026-09-30 22:00:00+00'::timestamp with time zone)) AND ("OrganizationId" = 2) AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 1)))
        Rows Removed by Filter: 2811
        Heap Blocks: exact=37
        Buffers: shared hit=51
        ->  Bitmap Index Scan on "IX_TimeEntries_UserId_StartTime"  (cost=0.00..79.44 rows=3102 width=0) (actual time=0.127..0.127 rows=2866 loops=1)
              Index Cond: (("UserId" = ANY ('{21}'::integer[])) AND ("StartTime" < '2026-11-01 23:00:00+00'::timestamp with time zone))
              Buffers: shared hit=14
        SubPlan 1
          ->  Seq Scan on "UserOrganizations" uo  (cost=0.00..4.56 rows=1 width=4) (never executed)
                Filter: ("IsActive" AND ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer))
  ->  Materialize  (cost=0.14..8.17 rows=1 width=5) (actual time=0.000..0.000 rows=1 loops=52)
        Buffers: shared hit=2
        ->  Index Scan using "PK_Organizations" on "Organizations" o  (cost=0.14..8.16 rows=1 width=5) (actual time=0.012..0.012 rows=1 loops=1)
              Index Cond: ("Id" = 2)
              Filter: "IsActive"
              Buffers: shared hit=2
Planning Time: 0.198 ms
Execution Time: 0.715 ms
```
