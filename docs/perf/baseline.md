# Scaling report — 2026-10-18 22:13

Sizes (employees x years): 100x1, 100x2, 100x5, 100x10, 25x5, 50x5, 200x5. Each call: 3 warm-ups, 15 measured (calls slower than 5000 ms: 1 + 5, marked *), over HTTP (TestServer, no network) as the app role with row-level security on, against PostgreSQL 16 in Docker (Testcontainers). Times in ms (p50 / p95 nearest rank), SQL = commands EF sent for one call (median). Member = perf-user1 (full history), admin = perf-admin1, owner. Month = the last ended month, week = the current one, year = 2026. Growth = p50 ratio between the shortest and longest history at the same team size.

## Seeded data

| size | seconds | Users | WorkSchedules | Holidays | Groups | TimeEntries | TimeEntryChanges | OrgRequests | AbsenceDays | CoreHoursChecks | TerminalPunches | Notifications |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 100x1 | 9.0 | 100 | 100 | 19 | 20 | 25,429 | 1,359 | 1,192 | 3,435 | 23,702 | 25,228 | 10,049 |
| 100x2 | 11.0 | 100 | 100 | 23 | 20 | 50,679 | 3,035 | 2,276 | 6,749 | 47,265 | 28,726 | 20,064 |
| 100x5 | 18.2 | 100 | 189 | 35 | 20 | 126,751 | 8,105 | 5,526 | 16,551 | 118,046 | 28,970 | 50,117 |
| 100x10 | 36.9 | 100 | 365 | 55 | 20 | 252,659 | 16,206 | 10,944 | 32,884 | 235,810 | 29,418 | 100,081 |
| 25x5 | 5.1 | 25 | 47 | 35 | 20 | 31,851 | 2,037 | 1,383 | 4,126 | 29,540 | 7,602 | 12,535 |
| 50x5 | 10.9 | 50 | 94 | 35 | 20 | 63,749 | 4,110 | 2,781 | 8,324 | 59,408 | 14,614 | 25,229 |
| 200x5 | 39.9 | 200 | 377 | 35 | 20 | 251,979 | 16,008 | 10,971 | 32,836 | 234,348 | 57,766 | 99,379 |

## Summary (p50 ms · SQL commands)

| endpoint | kind | 100x1 | 100x2 | 100x5 | 100x10 | 25x5 | 50x5 | 200x5 | t(100x10)/t(100x1) |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|
| balance | interactive | 80.0 · 22 | 77.1 · 22 | 76.2 · 24 | 115.8 · 22 | 75.1 · 22 | 92.4 · 22 | 77.1 · 24 | 1.4x (SQL 22 → 22) |
| team-overview | interactive | 438.6 · 10 | 471.8 · 10 | 1005.1 · 10 | 2196.3 · 10 | 291.3 · 10 | 581.5 · 10 | 1183.9 · 10 | 5.0x (SQL 10 → 10) |
| inbox: failures | interactive | 5.5 · 1 | 5.0 · 1 | 4.1 · 1 | 4.3 · 1 | 3.9 · 1 | 3.4 · 1 | 4.6 · 1 | 0.8x (SQL 1 → 1) |
| inbox: approvals | interactive | 28.3 · 7 | 31.0 · 7 | 22.4 · 7 | 23.8 · 7 | 19.1 · 7 | 20.7 · 7 | 70.3 · 7 | 0.8x (SQL 7 → 7) |
| inbox: user-notifications | interactive | 6.2 · 1 | 7.4 · 1 | 5.2 · 1 | 5.8 · 1 | 5.2 · 1 | 5.3 · 1 | 7.4 · 1 | 0.9x (SQL 1 → 1) |
| inbox: my-requests | interactive | 21.5 · 6 | 22.6 · 6 | 20.1 · 6 | 19.1 · 5 | 18.7 · 6 | 19.0 · 6 | 31.7 · 6 | 0.9x (SQL 6 → 5) |
| inbox: notifications | interactive | 7.9 · 2 | 6.4 · 2 | 6.4 · 2 | 6.7 · 2 | 6.3 · 2 | 5.3 · 2 | 6.6 · 2 | 0.9x (SQL 2 → 2) |
| TimeTracking week | interactive | 11.5 · 3 | 12.6 · 3 | 10.5 · 3 | 13.1 · 3 | 11.5 · 3 | 8.5 · 3 | 8.7 · 3 | 1.1x (SQL 3 → 3) |
| member entries week | interactive | 28.4 · 8 | 28.3 · 8 | 21.3 · 8 | 25.7 · 8 | 20.7 · 8 | 19.7 · 8 | 39.2 · 8 | 0.9x (SQL 8 → 8) |
| compliance month | interactive | 30.0 · 9 | 22.0 · 7 | 23.6 · 9 | 26.5 · 7 | 18.2 · 7 | 21.4 · 7 | 33.6 · 9 | 0.9x (SQL 9 → 7) |
| absences year | interactive | 31.2 · 7 | 28.5 · 7 | 22.2 · 7 | 44.8 · 7 | 22.6 · 7 | 23.6 · 7 | 33.6 · 7 | 1.4x (SQL 7 → 7) |
| org detail | interactive | 49.9 · 8 | 45.3 · 8 | 38.5 · 8 | 81.0 · 8 | 35.8 · 8 | 35.5 · 8 | 78.6 · 8 | 1.6x (SQL 8 → 8) |
| accounting summary month | export | 2921.7 · 899 | 3591.6 · 905 | 4270.5 · 905 | 8259.8 · 907* | 1204.6 · 232 | 2217.1 · 457 | 8221.4 · 1805* | 2.8x (SQL 899 → 907) |
| payroll CSV month | export | 2795.6 · 899 | 3210.8 · 905 | 4756.0 · 905 | 7983.3 · 907* | 1339.7 · 232 | 1960.3 · 457 | 7308.2 · 1805* | 2.9x (SQL 899 → 907) |
| accounting vacation year | export | 39.1 · 7 | 36.9 · 7 | 39.3 · 7 | 43.9 · 7 | 26.8 · 7 | 24.6 · 7 | 45.4 · 7 | 1.1x (SQL 7 → 7) |
| daily CSV month | export | 87.3 · 24 | 77.6 · 22 | 94.2 · 24 | 127.8 · 22 | 103.7 · 22 | 77.8 · 22 | 81.9 · 24 | 1.5x (SQL 24 → 22) |
| monthly PDF | export | 121.1 · 15 | 95.9 · 15 | 102.0 · 15 | 153.5 · 15 | 113.6 · 15 | 97.7 · 15 | 67.9 · 15 | 1.3x (SQL 15 → 15) |
| timer start | interactive | 22.5 · 7 | 20.8 · 7 | 21.3 · 7 | 23.6 · 7 | 22.7 · 7 | 29.5 · 7 | 17.8 · 7 | 1.1x (SQL 7 → 7) |
| timer stop | interactive | 25.5 · 7 | 21.1 · 7 | 20.8 · 7 | 27.1 · 7 | 23.3 · 7 | 26.9 · 7 | 17.6 · 7 | 1.1x (SQL 7 → 7) |
| manual entry create | interactive | 49.4 · 13 | 39.9 · 13 | 44.0 · 13 | 51.8 · 13 | 44.8 · 13 | 35.6 · 13 | 36.2 · 13 | 1.0x (SQL 13 → 13) |
| manual entry delete | interactive | 25.6 · 7 | 23.7 · 7 | 24.9 · 7 | 24.8 · 7 | 24.7 · 7 | 29.4 · 7 | 22.9 · 7 | 1.0x (SQL 7 → 7) |

## Per endpoint

### balance (interactive)

| | 100x1 | 100x2 | 100x5 | 100x10 | 25x5 | 50x5 | 200x5 |
|---|---:|---:|---:|---:|---:|---:|---:|
| p50 ms | 80.0 | 77.1 | 76.2 | 115.8 | 75.1 | 92.4 | 77.1 |
| p95 ms | 97.1 | 93.2 | 82.9 | 144.3 | 84.1 | 109.9 | 109.7 |
| SQL | 22 | 22 | 24 | 22 | 22 | 22 | 24 |

Growth t(100x10)/t(100x1): 1.4x (SQL 22 → 22)

### team-overview (interactive)

| | 100x1 | 100x2 | 100x5 | 100x10 | 25x5 | 50x5 | 200x5 |
|---|---:|---:|---:|---:|---:|---:|---:|
| p50 ms | 438.6 | 471.8 | 1005.1 | 2196.3 | 291.3 | 581.5 | 1183.9 |
| p95 ms | 602.0 | 554.4 | 1173.2 | 2294.1 | 336.7 | 723.8 | 1348.5 |
| SQL | 10 | 10 | 10 | 10 | 10 | 10 | 10 |

Growth t(100x10)/t(100x1): 5.0x (SQL 10 → 10)

### inbox: failures (interactive)

| | 100x1 | 100x2 | 100x5 | 100x10 | 25x5 | 50x5 | 200x5 |
|---|---:|---:|---:|---:|---:|---:|---:|
| p50 ms | 5.5 | 5.0 | 4.1 | 4.3 | 3.9 | 3.4 | 4.6 |
| p95 ms | 14.5 | 10.5 | 7.9 | 4.9 | 4.8 | 3.9 | 6.4 |
| SQL | 1 | 1 | 1 | 1 | 1 | 1 | 1 |

Growth t(100x10)/t(100x1): 0.8x (SQL 1 → 1)

### inbox: approvals (interactive)

| | 100x1 | 100x2 | 100x5 | 100x10 | 25x5 | 50x5 | 200x5 |
|---|---:|---:|---:|---:|---:|---:|---:|
| p50 ms | 28.3 | 31.0 | 22.4 | 23.8 | 19.1 | 20.7 | 70.3 |
| p95 ms | 32.4 | 38.3 | 31.6 | 26.2 | 22.6 | 24.3 | 718.9 |
| SQL | 7 | 7 | 7 | 7 | 7 | 7 | 7 |

Growth t(100x10)/t(100x1): 0.8x (SQL 7 → 7)

### inbox: user-notifications (interactive)

| | 100x1 | 100x2 | 100x5 | 100x10 | 25x5 | 50x5 | 200x5 |
|---|---:|---:|---:|---:|---:|---:|---:|
| p50 ms | 6.2 | 7.4 | 5.2 | 5.8 | 5.2 | 5.3 | 7.4 |
| p95 ms | 6.9 | 9.0 | 5.7 | 7.9 | 8.6 | 6.2 | 10.3 |
| SQL | 1 | 1 | 1 | 1 | 1 | 1 | 1 |

Growth t(100x10)/t(100x1): 0.9x (SQL 1 → 1)

### inbox: my-requests (interactive)

| | 100x1 | 100x2 | 100x5 | 100x10 | 25x5 | 50x5 | 200x5 |
|---|---:|---:|---:|---:|---:|---:|---:|
| p50 ms | 21.5 | 22.6 | 20.1 | 19.1 | 18.7 | 19.0 | 31.7 |
| p95 ms | 27.8 | 30.8 | 27.8 | 31.4 | 22.9 | 21.8 | 83.9 |
| SQL | 6 | 6 | 6 | 5 | 6 | 6 | 6 |

Growth t(100x10)/t(100x1): 0.9x (SQL 6 → 5)

### inbox: notifications (interactive)

| | 100x1 | 100x2 | 100x5 | 100x10 | 25x5 | 50x5 | 200x5 |
|---|---:|---:|---:|---:|---:|---:|---:|
| p50 ms | 7.9 | 6.4 | 6.4 | 6.7 | 6.3 | 5.3 | 6.6 |
| p95 ms | 13.4 | 11.1 | 11.8 | 12.8 | 8.4 | 6.6 | 7.1 |
| SQL | 2 | 2 | 2 | 2 | 2 | 2 | 2 |

Growth t(100x10)/t(100x1): 0.9x (SQL 2 → 2)

### TimeTracking week (interactive)

| | 100x1 | 100x2 | 100x5 | 100x10 | 25x5 | 50x5 | 200x5 |
|---|---:|---:|---:|---:|---:|---:|---:|
| p50 ms | 11.5 | 12.6 | 10.5 | 13.1 | 11.5 | 8.5 | 8.7 |
| p95 ms | 19.1 | 14.9 | 14.7 | 17.5 | 45.1 | 10.0 | 11.0 |
| SQL | 3 | 3 | 3 | 3 | 3 | 3 | 3 |

Growth t(100x10)/t(100x1): 1.1x (SQL 3 → 3)

### member entries week (interactive)

| | 100x1 | 100x2 | 100x5 | 100x10 | 25x5 | 50x5 | 200x5 |
|---|---:|---:|---:|---:|---:|---:|---:|
| p50 ms | 28.4 | 28.3 | 21.3 | 25.7 | 20.7 | 19.7 | 39.2 |
| p95 ms | 31.1 | 32.3 | 26.5 | 30.2 | 35.8 | 22.3 | 48.1 |
| SQL | 8 | 8 | 8 | 8 | 8 | 8 | 8 |

Growth t(100x10)/t(100x1): 0.9x (SQL 8 → 8)

### compliance month (interactive)

| | 100x1 | 100x2 | 100x5 | 100x10 | 25x5 | 50x5 | 200x5 |
|---|---:|---:|---:|---:|---:|---:|---:|
| p50 ms | 30.0 | 22.0 | 23.6 | 26.5 | 18.2 | 21.4 | 33.6 |
| p95 ms | 39.2 | 31.6 | 27.3 | 32.1 | 31.2 | 67.6 | 46.9 |
| SQL | 9 | 7 | 9 | 7 | 7 | 7 | 9 |

Growth t(100x10)/t(100x1): 0.9x (SQL 9 → 7)

### absences year (interactive)

| | 100x1 | 100x2 | 100x5 | 100x10 | 25x5 | 50x5 | 200x5 |
|---|---:|---:|---:|---:|---:|---:|---:|
| p50 ms | 31.2 | 28.5 | 22.2 | 44.8 | 22.6 | 23.6 | 33.6 |
| p95 ms | 51.9 | 31.9 | 30.0 | 51.9 | 35.1 | 25.3 | 43.8 |
| SQL | 7 | 7 | 7 | 7 | 7 | 7 | 7 |

Growth t(100x10)/t(100x1): 1.4x (SQL 7 → 7)

### org detail (interactive)

| | 100x1 | 100x2 | 100x5 | 100x10 | 25x5 | 50x5 | 200x5 |
|---|---:|---:|---:|---:|---:|---:|---:|
| p50 ms | 49.9 | 45.3 | 38.5 | 81.0 | 35.8 | 35.5 | 78.6 |
| p95 ms | 57.5 | 51.1 | 48.3 | 105.3 | 44.0 | 45.3 | 97.4 |
| SQL | 8 | 8 | 8 | 8 | 8 | 8 | 8 |

Growth t(100x10)/t(100x1): 1.6x (SQL 8 → 8)

### accounting summary month (export)

| | 100x1 | 100x2 | 100x5 | 100x10 | 25x5 | 50x5 | 200x5 |
|---|---:|---:|---:|---:|---:|---:|---:|
| p50 ms | 2921.7 | 3591.6 | 4270.5 | 8259.8 | 1204.6 | 2217.1 | 8221.4 |
| p95 ms | 3536.8 | 4364.3 | 5616.8 | 9903.4 | 1398.1 | 3364.8 | 10703.8 |
| SQL | 899 | 905 | 905 | 907 | 232 | 457 | 1805 |

Growth t(100x10)/t(100x1): 2.8x (SQL 899 → 907)

### payroll CSV month (export)

| | 100x1 | 100x2 | 100x5 | 100x10 | 25x5 | 50x5 | 200x5 |
|---|---:|---:|---:|---:|---:|---:|---:|
| p50 ms | 2795.6 | 3210.8 | 4756.0 | 7983.3 | 1339.7 | 1960.3 | 7308.2 |
| p95 ms | 3606.6 | 4023.9 | 5002.2 | 8895.8 | 1613.1 | 2633.5 | 7926.7 |
| SQL | 899 | 905 | 905 | 907 | 232 | 457 | 1805 |

Growth t(100x10)/t(100x1): 2.9x (SQL 899 → 907)

### accounting vacation year (export)

| | 100x1 | 100x2 | 100x5 | 100x10 | 25x5 | 50x5 | 200x5 |
|---|---:|---:|---:|---:|---:|---:|---:|
| p50 ms | 39.1 | 36.9 | 39.3 | 43.9 | 26.8 | 24.6 | 45.4 |
| p95 ms | 46.1 | 236.9 | 44.0 | 52.0 | 38.7 | 29.1 | 49.0 |
| SQL | 7 | 7 | 7 | 7 | 7 | 7 | 7 |

Growth t(100x10)/t(100x1): 1.1x (SQL 7 → 7)

### daily CSV month (export)

| | 100x1 | 100x2 | 100x5 | 100x10 | 25x5 | 50x5 | 200x5 |
|---|---:|---:|---:|---:|---:|---:|---:|
| p50 ms | 87.3 | 77.6 | 94.2 | 127.8 | 103.7 | 77.8 | 81.9 |
| p95 ms | 105.6 | 103.4 | 127.3 | 174.7 | 203.7 | 141.1 | 108.6 |
| SQL | 24 | 22 | 24 | 22 | 22 | 22 | 24 |

Growth t(100x10)/t(100x1): 1.5x (SQL 24 → 22)

### monthly PDF (export)

| | 100x1 | 100x2 | 100x5 | 100x10 | 25x5 | 50x5 | 200x5 |
|---|---:|---:|---:|---:|---:|---:|---:|
| p50 ms | 121.1 | 95.9 | 102.0 | 153.5 | 113.6 | 97.7 | 67.9 |
| p95 ms | 148.8 | 116.1 | 114.3 | 178.8 | 137.0 | 134.0 | 93.1 |
| SQL | 15 | 15 | 15 | 15 | 15 | 15 | 15 |

Growth t(100x10)/t(100x1): 1.3x (SQL 15 → 15)

### timer start (interactive)

| | 100x1 | 100x2 | 100x5 | 100x10 | 25x5 | 50x5 | 200x5 |
|---|---:|---:|---:|---:|---:|---:|---:|
| p50 ms | 22.5 | 20.8 | 21.3 | 23.6 | 22.7 | 29.5 | 17.8 |
| p95 ms | 33.6 | 25.7 | 26.0 | 27.1 | 27.1 | 34.9 | 25.5 |
| SQL | 7 | 7 | 7 | 7 | 7 | 7 | 7 |

Growth t(100x10)/t(100x1): 1.1x (SQL 7 → 7)

### timer stop (interactive)

| | 100x1 | 100x2 | 100x5 | 100x10 | 25x5 | 50x5 | 200x5 |
|---|---:|---:|---:|---:|---:|---:|---:|
| p50 ms | 25.5 | 21.1 | 20.8 | 27.1 | 23.3 | 26.9 | 17.6 |
| p95 ms | 28.7 | 26.5 | 24.5 | 32.4 | 25.9 | 40.8 | 20.7 |
| SQL | 7 | 7 | 7 | 7 | 7 | 7 | 7 |

Growth t(100x10)/t(100x1): 1.1x (SQL 7 → 7)

### manual entry create (interactive)

| | 100x1 | 100x2 | 100x5 | 100x10 | 25x5 | 50x5 | 200x5 |
|---|---:|---:|---:|---:|---:|---:|---:|
| p50 ms | 49.4 | 39.9 | 44.0 | 51.8 | 44.8 | 35.6 | 36.2 |
| p95 ms | 56.7 | 51.1 | 50.2 | 66.1 | 50.4 | 40.6 | 49.6 |
| SQL | 13 | 13 | 13 | 13 | 13 | 13 | 13 |

Growth t(100x10)/t(100x1): 1.0x (SQL 13 → 13)

### manual entry delete (interactive)

| | 100x1 | 100x2 | 100x5 | 100x10 | 25x5 | 50x5 | 200x5 |
|---|---:|---:|---:|---:|---:|---:|---:|
| p50 ms | 25.6 | 23.7 | 24.9 | 24.8 | 24.7 | 29.4 | 22.9 |
| p95 ms | 29.5 | 30.3 | 27.0 | 34.6 | 30.6 | 41.6 | 29.9 |
| SQL | 7 | 7 | 7 | 7 | 7 | 7 | 7 |

Growth t(100x10)/t(100x1): 1.0x (SQL 7 → 7)

## EXPLAIN (ANALYZE, BUFFERS) — the 5 slowest calls at 100x10

The slowest SELECTs (up to 3) of the last measured call, re-run as the app role for the same user (RLS on).

### accounting summary month — p50 8259.8 ms, 907 SQL

24.1 ms in the call:

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
Nested Loop  (cost=8.87..157.35 rows=8 width=63) (actual time=0.211..0.840 rows=82 loops=1)
  Buffers: shared hit=9 read=35
  ->  Index Scan using "PK_Organizations" on "Organizations" o  (cost=0.14..8.16 rows=1 width=4) (actual time=0.031..0.032 rows=1 loops=1)
        Index Cond: ("Id" = 5)
        Filter: ("IsActive" AND "IsActive")
        Buffers: shared hit=2
  ->  Index Scan using "IX_CoreHoursChecks_UserId" on "CoreHoursChecks" c  (cost=8.73..149.11 rows=8 width=63) (actual time=0.178..0.790 rows=82 loops=1)
        Index Cond: ("UserId" = ANY ('{349}'::integer[]))
        Filter: (("Date" >= '2016-10-18'::date) AND ("Date" <= '2026-09-30'::date) AND ("Issues" <> 0) AND ("OrganizationId" = 5) AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 1)))
        Rows Removed by Filter: 2529
        Buffers: shared hit=7 read=35
        SubPlan 1
          ->  Index Scan using "IX_UserOrganizations_UserId_OrganizationId" on "UserOrganizations" uo  (cost=0.29..8.30 rows=1 width=4) (actual time=0.020..0.021 rows=1 loops=1)
                Index Cond: ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer)
                Filter: "IsActive"
                Buffers: shared hit=3
Planning Time: 0.397 ms
Execution Time: 0.953 ms
```

22.0 ms in the call:

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
Nested Loop Left Join  (cost=8.87..3513.00 rows=156 width=21) (actual time=0.134..2.757 rows=2114 loops=1)
  Filter: ((t."OrganizationId" IS NULL) OR o."IsActive")
  Buffers: shared hit=6 read=37
  ->  Index Scan using "IX_TimeEntries_UserId_StartTime" on "TimeEntries" t  (cost=8.73..3500.94 rows=312 width=25) (actual time=0.112..2.204 rows=2114 loops=1)
        Index Cond: (("UserId" = ANY ('{348}'::integer[])) AND ("StartTime" < '2026-10-01 22:00:00+00'::timestamp with time zone))
        Filter: ((NOT "IsFailure") AND ("IsRunning" OR ("EndTime" > '2019-03-13 23:00:00+00'::timestamp with time zone)) AND ("OrganizationId" = 5) AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 1)))
        Buffers: shared hit=4 read=37
        SubPlan 1
          ->  Index Scan using "IX_UserOrganizations_UserId_OrganizationId" on "UserOrganizations" uo  (cost=0.29..8.30 rows=1 width=4) (actual time=0.020..0.022 rows=1 loops=1)
                Index Cond: ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer)
                Filter: "IsActive"
                Buffers: shared hit=3
  ->  Materialize  (cost=0.14..8.17 rows=1 width=5) (actual time=0.000..0.000 rows=1 loops=2114)
        Buffers: shared hit=2
        ->  Index Scan using "PK_Organizations" on "Organizations" o  (cost=0.14..8.16 rows=1 width=5) (actual time=0.011..0.011 rows=1 loops=1)
              Index Cond: ("Id" = 5)
              Filter: "IsActive"
              Buffers: shared hit=2
Planning Time: 0.430 ms
Execution Time: 2.930 ms
```

19.7 ms in the call:

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
Nested Loop Left Join  (cost=8.87..3821.78 rows=184 width=94) (actual time=0.078..4.496 rows=2821 loops=1)
  Filter: ((t."OrganizationId" IS NULL) OR o."IsActive")
  Buffers: shared hit=9 read=45
  ->  Index Scan using "IX_TimeEntries_UserId_StartTime" on "TimeEntries" t  (cost=8.73..3809.02 rows=368 width=94) (actual time=0.063..3.554 rows=2821 loops=1)
        Index Cond: (("UserId" = 349) AND ("StartTime" >= '2016-10-17 22:00:00+00'::timestamp with time zone) AND ("StartTime" < '2026-09-30 22:00:00+00'::timestamp with time zone))
        Filter: ((NOT "IsRunning") AND ("OrganizationId" = 5) AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 1)))
        Buffers: shared hit=7 read=45
        SubPlan 1
          ->  Index Scan using "IX_UserOrganizations_UserId_OrganizationId" on "UserOrganizations" uo  (cost=0.29..8.30 rows=1 width=4) (actual time=0.016..0.016 rows=1 loops=1)
                Index Cond: ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer)
                Filter: "IsActive"
                Buffers: shared hit=3
  ->  Materialize  (cost=0.14..8.17 rows=1 width=5) (actual time=0.000..0.000 rows=1 loops=2821)
        Buffers: shared hit=2
        ->  Index Scan using "PK_Organizations" on "Organizations" o  (cost=0.14..8.16 rows=1 width=5) (actual time=0.010..0.011 rows=1 loops=1)
              Index Cond: ("Id" = 5)
              Filter: "IsActive"
              Buffers: shared hit=2
Planning Time: 0.301 ms
Execution Time: 4.721 ms
```

### payroll CSV month — p50 7983.3 ms, 907 SQL

13.4 ms in the call:

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
Nested Loop  (cost=33.76..230.85 rows=53 width=353) (actual time=0.209..0.962 rows=358 loops=1)
  Buffers: shared hit=8 read=7
  ->  Index Scan using "PK_Organizations" on "Organizations" o  (cost=0.14..8.16 rows=1 width=4) (actual time=0.027..0.030 rows=1 loops=1)
        Index Cond: ("Id" = 5)
        Filter: ("IsActive" AND "IsActive")
        Buffers: shared hit=2
  ->  Hash Left Join  (cost=33.61..222.16 rows=53 width=353) (actual time=0.179..0.829 rows=358 loops=1)
        Hash Cond: (a."CustomTypeId" = a0."Id")
        Buffers: shared hit=6 read=7
        ->  Index Scan using "IX_AbsenceDays_UserId_OrganizationId_Date" on "AbsenceDays" a  (cost=8.72..197.06 rows=53 width=55) (actual time=0.124..0.676 rows=358 loops=1)
              Index Cond: (("UserId" = 395) AND ("OrganizationId" = 5) AND ("Date" >= '2016-10-18'::date) AND ("Date" <= '2026-09-30'::date))
              Filter: ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 1))
              Buffers: shared hit=4 read=7
              SubPlan 1
                ->  Index Scan using "IX_UserOrganizations_UserId_OrganizationId" on "UserOrganizations" uo  (cost=0.29..8.30 rows=1 width=4) (actual time=0.022..0.024 rows=1 loops=1)
                      Index Cond: ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer)
                      Filter: "IsActive"
                      Buffers: shared hit=3
        ->  Hash  (cost=24.09..24.09 rows=64 width=298) (actual time=0.043..0.045 rows=3 loops=1)
              Buckets: 1024  Batches: 1  Memory Usage: 9kB
              Buffers: shared hit=2
              ->  Hash Join  (cost=10.92..24.09 rows=64 width=298) (actual time=0.034..0.038 rows=3 loops=1)
                    Hash Cond: (a0."OrganizationId" = o1."Id")
                    Buffers: shared hit=2
                    ->  Seq Scan on "AbsenceTypeDefinitions" a0  (cost=0.00..12.50 rows=250 width=298) (actual time=0.006..0.007 rows=3 loops=1)
                          Buffers: shared hit=1
                    ->  Hash  (cost=10.70..10.70 rows=18 width=4) (actual time=0.013..0.014 rows=8 loops=1)
                          Buckets: 1024  Batches: 1  Memory Usage: 9kB
                          Buffers: shared hit=1
                          ->  Seq Scan on "Organizations" o1  (cost=0.00..10.70 rows=18 width=4) (actual time=0.004..0.006 rows=8 loops=1)
                                Filter: ("IsActive" AND "IsActive")
                                Buffers: shared hit=1
Planning Time: 0.598 ms
Execution Time: 1.146 ms
```

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
Nested Loop Left Join  (cost=8.87..2581.19 rows=120 width=94) (actual time=0.166..3.565 rows=1633 loops=1)
  Filter: ((t."OrganizationId" IS NULL) OR o."IsActive")
  Buffers: shared hit=6 read=30
  ->  Index Scan using "IX_TimeEntries_UserId_StartTime" on "TimeEntries" t  (cost=8.73..2570.04 rows=239 width=94) (actual time=0.127..2.675 rows=1633 loops=1)
        Index Cond: (("UserId" = 369) AND ("StartTime" >= '2020-10-19 22:00:00+00'::timestamp with time zone) AND ("StartTime" < '2026-09-30 22:00:00+00'::timestamp with time zone))
        Filter: ((NOT "IsRunning") AND ("OrganizationId" = 5) AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 1)))
        Buffers: shared hit=4 read=30
        SubPlan 1
          ->  Index Scan using "IX_UserOrganizations_UserId_OrganizationId" on "UserOrganizations" uo  (cost=0.29..8.30 rows=1 width=4) (actual time=0.062..0.064 rows=1 loops=1)
                Index Cond: ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer)
                Filter: "IsActive"
                Buffers: shared hit=3
  ->  Materialize  (cost=0.14..8.17 rows=1 width=5) (actual time=0.000..0.000 rows=1 loops=1633)
        Buffers: shared hit=2
        ->  Index Scan using "PK_Organizations" on "Organizations" o  (cost=0.14..8.16 rows=1 width=5) (actual time=0.031..0.032 rows=1 loops=1)
              Index Cond: ("Id" = 5)
              Filter: "IsActive"
              Buffers: shared hit=2
Planning Time: 0.273 ms
Execution Time: 3.760 ms
```

8.6 ms in the call:

```sql
SELECT min(t."StartTime")
FROM "TimeEntries" AS t
LEFT JOIN (
    SELECT o."Id", o."IsActive"
    FROM "Organizations" AS o
    WHERE o."IsActive"
) AS o0 ON t."OrganizationId" = o0."Id"
WHERE (t."OrganizationId" IS NULL OR o0."IsActive") AND t."UserId" = @member_UserId AND t."OrganizationId" = @orgId AND NOT (t."IsRunning")
```

```text
Aggregate  (cost=3539.20..3539.21 rows=1 width=8) (actual time=3.894..3.896 rows=1 loops=1)
  Buffers: shared hit=6 read=47
  ->  Nested Loop Left Join  (cost=8.87..3538.78 rows=170 width=8) (actual time=0.083..3.689 rows=2811 loops=1)
        Filter: ((t."OrganizationId" IS NULL) OR o."IsActive")
        Buffers: shared hit=6 read=47
        ->  Index Scan using "IX_TimeEntries_UserId_StartTime" on "TimeEntries" t  (cost=8.73..3526.38 rows=339 width=12) (actual time=0.069..2.978 rows=2811 loops=1)
              Index Cond: ("UserId" = 387)
              Filter: ((NOT "IsRunning") AND ("OrganizationId" = 5) AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 1)))
              Buffers: shared hit=4 read=47
              SubPlan 1
                ->  Index Scan using "IX_UserOrganizations_UserId_OrganizationId" on "UserOrganizations" uo  (cost=0.29..8.30 rows=1 width=4) (actual time=0.013..0.014 rows=1 loops=1)
                      Index Cond: ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer)
                      Filter: "IsActive"
                      Buffers: shared hit=3
        ->  Materialize  (cost=0.14..8.17 rows=1 width=5) (actual time=0.000..0.000 rows=1 loops=2811)
              Buffers: shared hit=2
              ->  Index Scan using "PK_Organizations" on "Organizations" o  (cost=0.14..8.16 rows=1 width=5) (actual time=0.008..0.009 rows=1 loops=1)
                    Index Cond: ("Id" = 5)
                    Filter: "IsActive"
                    Buffers: shared hit=2
Planning Time: 0.281 ms
Execution Time: 4.013 ms
```

### team-overview — p50 2196.3 ms, 10 SQL

10.8 ms in the call:

```sql
SELECT t."Id", t."UserId", t."OrganizationId", t."StartTime", t."EndTime", t."IsRunning", t."IsFailure", t."PauseDurationMinutes"
FROM "TimeEntries" AS t
LEFT JOIN (
    SELECT o."Id", o."IsActive"
    FROM "Organizations" AS o
    WHERE o."IsActive"
) AS o0 ON t."OrganizationId" = o0."Id"
WHERE (t."OrganizationId" IS NULL OR o0."IsActive") AND t."OrganizationId" = @orgId AND t."UserId" = ANY (@userIds) AND t."StartTime" < @beforeUtc
```

```text
Nested Loop Left Join  (cost=6987.35..25648.60 rows=19889 width=34) (actual time=19.534..178.638 rows=249879 loops=1)
  Filter: ((t."OrganizationId" IS NULL) OR o."IsActive")
  Buffers: shared hit=412 read=3928 written=1150
  ->  Gather  (cost=6987.21..25143.21 rows=39778 width=34) (actual time=19.455..92.389 rows=249879 loops=1)
        Workers Planned: 2
        Workers Launched: 2
        Buffers: shared hit=410 read=3928 written=1150
        ->  Parallel Bitmap Heap Scan on "TimeEntries" t  (cost=5987.21..20165.41 rows=16574 width=34) (actual time=14.702..107.231 rows=83293 loops=3)
              Recheck Cond: (("UserId" = ANY ('{315,317,318,319,320,321,322,323,324,325,326,327,328,329,330,331,332,333,334,335,336,337,338,339,340,341,342,343,344,345,346,347,348,349,350,351,352,353,354,355,356,357,358,359,360,361,362,363,364,365,366,367,368,369,370,371,372,373,374,375,376,377,378,379,380,381,382,383,384,385,386,387,388,389,390,391,392,393,394,395,396,397,398,399,400,401,402,403,404,405,406,407,408,409,410,411,412,413,414}'::integer[])) AND ("StartTime" < '2026-10-18 22:00:00+00'::timestamp with time zone))
              Filter: (("OrganizationId" = 5) AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 1)))
              Heap Blocks: exact=450
              Buffers: shared hit=410 read=3928 written=1150
              ->  Bitmap Index Scan on "IX_TimeEntries_UserId_StartTime"  (cost=0.00..5968.71 rows=251052 width=0) (actual time=18.375..18.375 rows=249881 loops=1)
                    Index Cond: (("UserId" = ANY ('{315,317,318,319,320,321,322,323,324,325,326,327,328,329,330,331,332,333,334,335,336,337,338,339,340,341,342,343,344,345,346,347,348,349,350,351,352,353,354,355,356,357,358,359,360,361,362,363,364,365,366,367,368,369,370,371,372,373,374,375,376,377,378,379,380,381,382,383,384,385,386,387,388,389,390,391,392,393,394,395,396,397,398,399,400,401,402,403,404,405,406,407,408,409,410,411,412,413,414}'::integer[])) AND ("StartTime" < '2026-10-18 22:00:00+00'::timestamp with time zone))
                    Buffers: shared hit=314 read=925
              SubPlan 1
                ->  Index Scan using "IX_UserOrganizations_UserId_OrganizationId" on "UserOrganizations" uo  (cost=0.29..8.30 rows=1 width=4) (actual time=0.054..0.054 rows=1 loops=3)
                      Index Cond: ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer)
                      Filter: "IsActive"
                      Buffers: shared hit=11
  ->  Materialize  (cost=0.14..8.17 rows=1 width=5) (actual time=0.000..0.000 rows=1 loops=249879)
        Buffers: shared hit=2
        ->  Index Scan using "PK_Organizations" on "Organizations" o  (cost=0.14..8.16 rows=1 width=5) (actual time=0.069..0.069 rows=1 loops=1)
              Index Cond: ("Id" = 5)
              Filter: "IsActive"
              Buffers: shared hit=2
Planning Time: 0.551 ms
Execution Time: 189.200 ms
```

6.1 ms in the call:

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
Nested Loop  (cost=582.16..2141.67 rows=5033 width=353) (actual time=2.809..34.935 rows=32377 loops=1)
  Buffers: shared hit=289 read=455 written=215
  ->  Index Scan using "PK_Organizations" on "Organizations" o  (cost=0.14..8.16 rows=1 width=4) (actual time=0.023..0.026 rows=1 loops=1)
        Index Cond: ("Id" = 5)
        Filter: ("IsActive" AND "IsActive")
        Buffers: shared hit=2
  ->  Hash Left Join  (cost=582.02..2083.18 rows=5033 width=353) (actual time=2.780..29.652 rows=32377 loops=1)
        Hash Cond: (a."CustomTypeId" = a0."Id")
        Buffers: shared hit=287 read=455 written=215
        ->  Bitmap Heap Scan on "AbsenceDays" a  (cost=557.13..2039.41 rows=5033 width=55) (actual time=2.723..23.770 rows=32377 loops=1)
              Recheck Cond: (("UserId" = ANY ('{315,317,318,319,320,321,322,323,324,325,326,327,328,329,330,331,332,333,334,335,336,337,338,339,340,341,342,343,344,345,346,347,348,349,350,351,352,353,354,355,356,357,358,359,360,361,362,363,364,365,366,367,368,369,370,371,372,373,374,375,376,377,378,379,380,381,382,383,384,385,386,387,388,389,390,391,392,393,394,395,396,397,398,399,400,401,402,403,404,405,406,407,408,409,410,411,412,413,414}'::integer[])) AND ("OrganizationId" = 5) AND ("Date" >= '2016-10-18'::date) AND ("Date" <= '2026-10-18'::date))
              Filter: ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 1))
              Heap Blocks: exact=327
              Buffers: shared hit=285 read=455 written=215
              ->  Bitmap Index Scan on "IX_AbsenceDays_UserId_OrganizationId_Date"  (cost=0.00..547.32 rows=9983 width=0) (actual time=2.644..2.644 rows=32377 loops=1)
                    Index Cond: (("UserId" = ANY ('{315,317,318,319,320,321,322,323,324,325,326,327,328,329,330,331,332,333,334,335,336,337,338,339,340,341,342,343,344,345,346,347,348,349,350,351,352,353,354,355,356,357,358,359,360,361,362,363,364,365,366,367,368,369,370,371,372,373,374,375,376,377,378,379,380,381,382,383,384,385,386,387,388,389,390,391,392,393,394,395,396,397,398,399,400,401,402,403,404,405,406,407,408,409,410,411,412,413,414}'::integer[])) AND ("OrganizationId" = 5) AND ("Date" >= '2016-10-18'::date) AND ("Date" <= '2026-10-18'::date))
                    Buffers: shared hit=282 read=128 written=82
              SubPlan 1
                ->  Index Scan using "IX_UserOrganizations_UserId_OrganizationId" on "UserOrganizations" uo  (cost=0.29..8.30 rows=1 width=4) (actual time=0.016..0.016 rows=1 loops=1)
                      Index Cond: ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer)
                      Filter: "IsActive"
                      Buffers: shared hit=3
        ->  Hash  (cost=24.09..24.09 rows=64 width=298) (actual time=0.045..0.048 rows=3 loops=1)
              Buckets: 1024  Batches: 1  Memory Usage: 9kB
              Buffers: shared hit=2
              ->  Hash Join  (cost=10.92..24.09 rows=64 width=298) (actual time=0.039..0.043 rows=3 loops=1)
                    Hash Cond: (a0."OrganizationId" = o1."Id")
                    Buffers: shared hit=2
                    ->  Seq Scan on "AbsenceTypeDefinitions" a0  (cost=0.00..12.50 rows=250 width=298) (actual time=0.019..0.019 rows=3 loops=1)
                          Buffers: shared hit=1
                    ->  Hash  (cost=10.70..10.70 rows=18 width=4) (actual time=0.010..0.011 rows=8 loops=1)
                          Buckets: 1024  Batches: 1  Memory Usage: 9kB
                          Buffers: shared hit=1
                          ->  Seq Scan on "Organizations" o1  (cost=0.00..10.70 rows=18 width=4) (actual time=0.003..0.004 rows=8 loops=1)
                                Filter: ("IsActive" AND "IsActive")
                                Buffers: shared hit=1
Planning Time: 0.559 ms
Execution Time: 36.071 ms
```

3.0 ms in the call:

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
Sort  (cost=31.98..32.29 rows=122 width=174) (actual time=0.331..0.337 rows=110 loops=1)
  Sort Key: g."Id"
  Sort Method: quicksort  Memory: 38kB
  Buffers: shared hit=7 read=2 written=2
  ->  Nested Loop  (cost=3.94..27.75 rows=122 width=174) (actual time=0.159..0.279 rows=110 loops=1)
        Buffers: shared hit=7 read=2 written=2
        ->  Index Scan using "PK_Organizations" on "Organizations" o  (cost=0.14..8.16 rows=1 width=4) (actual time=0.012..0.013 rows=1 loops=1)
              Index Cond: ("Id" = 5)
              Filter: ("IsActive" AND "IsActive")
              Buffers: shared hit=2
        ->  Hash Right Join  (cost=3.80..18.37 rows=122 width=170) (actual time=0.145..0.235 rows=110 loops=1)
              Hash Cond: (g0."GroupId" = g."Id")
              Buffers: shared hit=5 read=2 written=2
              ->  Seq Scan on "GroupMembers" g0  (cost=0.00..12.54 rows=754 width=24) (actual time=0.002..0.100 rows=754 loops=1)
                    Buffers: shared hit=3 read=2 written=2
              ->  Hash  (cost=3.55..3.55 rows=20 width=146) (actual time=0.036..0.038 rows=20 loops=1)
                    Buckets: 1024  Batches: 1  Memory Usage: 10kB
                    Buffers: shared hit=2
                    ->  Seq Scan on "Groups" g  (cost=0.00..3.55 rows=20 width=146) (actual time=0.011..0.024 rows=20 loops=1)
                          Filter: ("IsActive" AND "IsActive" AND ("OrganizationId" = 5))
                          Rows Removed by Filter: 124
                          Buffers: shared hit=2
Planning:
  Buffers: shared hit=7
Planning Time: 0.321 ms
Execution Time: 0.413 ms
```

### monthly PDF — p50 153.5 ms, 15 SQL

3.2 ms in the call:

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
Sort  (cost=31.98..32.29 rows=122 width=174) (actual time=0.279..0.284 rows=110 loops=1)
  Sort Key: g."Id"
  Sort Method: quicksort  Memory: 38kB
  Buffers: shared hit=9
  ->  Nested Loop  (cost=3.94..27.75 rows=122 width=174) (actual time=0.101..0.227 rows=110 loops=1)
        Buffers: shared hit=9
        ->  Index Scan using "PK_Organizations" on "Organizations" o  (cost=0.14..8.16 rows=1 width=4) (actual time=0.010..0.011 rows=1 loops=1)
              Index Cond: ("Id" = 5)
              Filter: ("IsActive" AND "IsActive")
              Buffers: shared hit=2
        ->  Hash Right Join  (cost=3.80..18.37 rows=122 width=170) (actual time=0.090..0.185 rows=110 loops=1)
              Hash Cond: (g0."GroupId" = g."Id")
              Buffers: shared hit=7
              ->  Seq Scan on "GroupMembers" g0  (cost=0.00..12.54 rows=754 width=24) (actual time=0.004..0.050 rows=754 loops=1)
                    Buffers: shared hit=5
              ->  Hash  (cost=3.55..3.55 rows=20 width=146) (actual time=0.036..0.037 rows=20 loops=1)
                    Buckets: 1024  Batches: 1  Memory Usage: 10kB
                    Buffers: shared hit=2
                    ->  Seq Scan on "Groups" g  (cost=0.00..3.55 rows=20 width=146) (actual time=0.010..0.025 rows=20 loops=1)
                          Filter: ("IsActive" AND "IsActive" AND ("OrganizationId" = 5))
                          Rows Removed by Filter: 124
                          Buffers: shared hit=2
Planning:
  Buffers: shared hit=7
Planning Time: 0.278 ms
Execution Time: 0.363 ms
```

2.9 ms in the call:

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
Nested Loop  (cost=33.76..219.99 rows=50 width=353) (actual time=0.140..0.667 rows=362 loops=1)
  Buffers: shared hit=16
  ->  Index Scan using "PK_Organizations" on "Organizations" o  (cost=0.14..8.16 rows=1 width=4) (actual time=0.028..0.029 rows=1 loops=1)
        Index Cond: ("Id" = 5)
        Filter: ("IsActive" AND "IsActive")
        Buffers: shared hit=2
  ->  Hash Left Join  (cost=33.61..211.33 rows=50 width=353) (actual time=0.109..0.564 rows=362 loops=1)
        Hash Cond: (a."CustomTypeId" = a0."Id")
        Buffers: shared hit=14
        ->  Index Scan using "IX_AbsenceDays_UserId_OrganizationId_Date" on "AbsenceDays" a  (cost=8.72..186.24 rows=50 width=55) (actual time=0.065..0.397 rows=362 loops=1)
              Index Cond: (("UserId" = 321) AND ("OrganizationId" = 5) AND ("Date" >= '2016-10-18'::date) AND ("Date" <= '2026-09-30'::date))
              Filter: ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 1))
              Buffers: shared hit=12
              SubPlan 1
                ->  Index Scan using "IX_UserOrganizations_UserId_OrganizationId" on "UserOrganizations" uo  (cost=0.29..8.30 rows=1 width=4) (actual time=0.016..0.017 rows=1 loops=1)
                      Index Cond: ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer)
                      Filter: "IsActive"
                      Buffers: shared hit=3
        ->  Hash  (cost=24.09..24.09 rows=64 width=298) (actual time=0.039..0.040 rows=3 loops=1)
              Buckets: 1024  Batches: 1  Memory Usage: 9kB
              Buffers: shared hit=2
              ->  Hash Join  (cost=10.92..24.09 rows=64 width=298) (actual time=0.029..0.032 rows=3 loops=1)
                    Hash Cond: (a0."OrganizationId" = o1."Id")
                    Buffers: shared hit=2
                    ->  Seq Scan on "AbsenceTypeDefinitions" a0  (cost=0.00..12.50 rows=250 width=298) (actual time=0.006..0.006 rows=3 loops=1)
                          Buffers: shared hit=1
                    ->  Hash  (cost=10.70..10.70 rows=18 width=4) (actual time=0.011..0.012 rows=8 loops=1)
                          Buckets: 1024  Batches: 1  Memory Usage: 9kB
                          Buffers: shared hit=1
                          ->  Seq Scan on "Organizations" o1  (cost=0.00..10.70 rows=18 width=4) (actual time=0.003..0.005 rows=8 loops=1)
                                Filter: ("IsActive" AND "IsActive")
                                Buffers: shared hit=1
Planning Time: 0.491 ms
Execution Time: 0.822 ms
```

2.6 ms in the call:

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
Limit  (cost=0.69..24.77 rows=1 width=7) (actual time=0.036..0.037 rows=1 loops=1)
  Buffers: shared hit=8
  ->  Nested Loop  (cost=0.69..24.77 rows=1 width=7) (actual time=0.035..0.036 rows=1 loops=1)
        Buffers: shared hit=8
        ->  Nested Loop  (cost=0.42..16.46 rows=1 width=10) (actual time=0.025..0.025 rows=1 loops=1)
              Join Filter: (u."OrganizationId" = o."Id")
              Buffers: shared hit=5
              ->  Index Scan using "IX_UserOrganizations_UserId_OrganizationId" on "UserOrganizations" u  (cost=0.28..8.29 rows=1 width=13) (actual time=0.015..0.015 rows=1 loops=1)
                    Index Cond: ("UserId" = 315)
                    Filter: "IsActive"
                    Buffers: shared hit=3
              ->  Index Scan using "IX_Organizations_Slug" on "Organizations" o  (cost=0.14..8.16 rows=1 width=5) (actual time=0.009..0.009 rows=1 loops=1)
                    Index Cond: (("Slug")::text = 'perf-100-10'::text)
                    Buffers: shared hit=2
        ->  Index Scan using "PK_Users" on "Users" u0  (cost=0.28..8.29 rows=1 width=5) (actual time=0.010..0.010 rows=1 loops=1)
              Index Cond: ("Id" = 315)
              Buffers: shared hit=3
Planning Time: 0.152 ms
Execution Time: 0.079 ms
```

### daily CSV month — p50 127.8 ms, 22 SQL

3.9 ms in the call:

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
Sort  (cost=31.98..32.29 rows=122 width=174) (actual time=0.294..0.300 rows=110 loops=1)
  Sort Key: g."Id"
  Sort Method: quicksort  Memory: 38kB
  Buffers: shared hit=9
  ->  Nested Loop  (cost=3.94..27.75 rows=122 width=174) (actual time=0.111..0.234 rows=110 loops=1)
        Buffers: shared hit=9
        ->  Index Scan using "PK_Organizations" on "Organizations" o  (cost=0.14..8.16 rows=1 width=4) (actual time=0.013..0.014 rows=1 loops=1)
              Index Cond: ("Id" = 5)
              Filter: ("IsActive" AND "IsActive")
              Buffers: shared hit=2
        ->  Hash Right Join  (cost=3.80..18.37 rows=122 width=170) (actual time=0.095..0.189 rows=110 loops=1)
              Hash Cond: (g0."GroupId" = g."Id")
              Buffers: shared hit=7
              ->  Seq Scan on "GroupMembers" g0  (cost=0.00..12.54 rows=754 width=24) (actual time=0.004..0.046 rows=754 loops=1)
                    Buffers: shared hit=5
              ->  Hash  (cost=3.55..3.55 rows=20 width=146) (actual time=0.045..0.046 rows=20 loops=1)
                    Buckets: 1024  Batches: 1  Memory Usage: 10kB
                    Buffers: shared hit=2
                    ->  Seq Scan on "Groups" g  (cost=0.00..3.55 rows=20 width=146) (actual time=0.015..0.030 rows=20 loops=1)
                          Filter: ("IsActive" AND "IsActive" AND ("OrganizationId" = 5))
                          Rows Removed by Filter: 124
                          Buffers: shared hit=2
Planning:
  Buffers: shared hit=7
Planning Time: 0.392 ms
Execution Time: 0.387 ms
```

2.8 ms in the call:

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
Nested Loop  (cost=33.76..219.99 rows=50 width=353) (actual time=0.086..0.508 rows=362 loops=1)
  Buffers: shared hit=16
  ->  Index Scan using "PK_Organizations" on "Organizations" o  (cost=0.14..8.16 rows=1 width=4) (actual time=0.012..0.013 rows=1 loops=1)
        Index Cond: ("Id" = 5)
        Filter: ("IsActive" AND "IsActive")
        Buffers: shared hit=2
  ->  Hash Left Join  (cost=33.61..211.33 rows=50 width=353) (actual time=0.073..0.423 rows=362 loops=1)
        Hash Cond: (a."CustomTypeId" = a0."Id")
        Buffers: shared hit=14
        ->  Index Scan using "IX_AbsenceDays_UserId_OrganizationId_Date" on "AbsenceDays" a  (cost=8.72..186.24 rows=50 width=55) (actual time=0.040..0.322 rows=362 loops=1)
              Index Cond: (("UserId" = 321) AND ("OrganizationId" = 5) AND ("Date" >= '2016-10-18'::date) AND ("Date" <= '2026-09-30'::date))
              Filter: ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 1))
              Buffers: shared hit=12
              SubPlan 1
                ->  Index Scan using "IX_UserOrganizations_UserId_OrganizationId" on "UserOrganizations" uo  (cost=0.29..8.30 rows=1 width=4) (actual time=0.010..0.011 rows=1 loops=1)
                      Index Cond: ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer)
                      Filter: "IsActive"
                      Buffers: shared hit=3
        ->  Hash  (cost=24.09..24.09 rows=64 width=298) (actual time=0.025..0.026 rows=3 loops=1)
              Buckets: 1024  Batches: 1  Memory Usage: 9kB
              Buffers: shared hit=2
              ->  Hash Join  (cost=10.92..24.09 rows=64 width=298) (actual time=0.019..0.021 rows=3 loops=1)
                    Hash Cond: (a0."OrganizationId" = o1."Id")
                    Buffers: shared hit=2
                    ->  Seq Scan on "AbsenceTypeDefinitions" a0  (cost=0.00..12.50 rows=250 width=298) (actual time=0.004..0.004 rows=3 loops=1)
                          Buffers: shared hit=1
                    ->  Hash  (cost=10.70..10.70 rows=18 width=4) (actual time=0.007..0.008 rows=8 loops=1)
                          Buckets: 1024  Batches: 1  Memory Usage: 9kB
                          Buffers: shared hit=1
                          ->  Seq Scan on "Organizations" o1  (cost=0.00..10.70 rows=18 width=4) (actual time=0.002..0.003 rows=8 loops=1)
                                Filter: ("IsActive" AND "IsActive")
                                Buffers: shared hit=1
Planning Time: 0.334 ms
Execution Time: 0.673 ms
```

2.6 ms in the call:

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
Nested Loop  (cost=8.87..53.71 rows=1 width=63) (actual time=0.126..0.126 rows=0 loops=1)
  Buffers: shared hit=2 read=4 written=3
  ->  Index Scan using "IX_CoreHoursChecks_OrganizationId_UserId_Date" on "CoreHoursChecks" c  (cost=8.73..45.54 rows=1 width=63) (actual time=0.125..0.126 rows=0 loops=1)
        Index Cond: (("OrganizationId" = 5) AND ("UserId" = ANY ('{321}'::integer[])) AND ("Date" >= '2026-09-01'::date) AND ("Date" <= '2026-09-30'::date))
        Filter: (("Issues" <> 0) AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 1)))
        Rows Removed by Filter: 22
        Buffers: shared hit=2 read=4 written=3
        SubPlan 1
          ->  Index Scan using "IX_UserOrganizations_UserId_OrganizationId" on "UserOrganizations" uo  (cost=0.29..8.30 rows=1 width=4) (never executed)
                Index Cond: ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer)
                Filter: "IsActive"
  ->  Index Scan using "PK_Organizations" on "Organizations" o  (cost=0.14..8.16 rows=1 width=4) (never executed)
        Index Cond: ("Id" = 5)
        Filter: ("IsActive" AND "IsActive")
Planning Time: 0.236 ms
Execution Time: 0.181 ms
```
