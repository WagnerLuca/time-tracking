# Final measurement — Phases 0–2b done (Phase 3)

Measured 2026-10-19 13:30 with the Phase 0 harness, the full default matrix
(`export DOCKER_API_VERSION=1.41; dotnet test --filter Category=Perf` in `backend/tests/TimeTracking.Api.Tests`,
`PERF_SIZES` unset = `100x1,100x2,100x5,100x10,25x5,50x5,200x5`), alone on the machine, on the working tree with
Phases 0, 1, 2 and 2b (balance checkpoints, vacation carry-over). Same harness and method as `baseline.md` (which was
re-measured on the Phase 0 tree with the corrected harness: fresh database per size, no background jobs, 2026-10-19 09:49),
so the columns compare directly. The whole matrix took 4.5 minutes (seeding included).

p50 ms · SQL commands per call. The baseline has only 100x1 and 100x10; the full final report (all sizes, p95, EXPLAIN)
follows below. `*` = slow call, measured 1 + 5 times instead of 3 + 15.

| endpoint | kind | baseline 100x1 | final 100x1 | baseline 100x10 | final 100x10 | change at 10 y | final 200x5 |
|---|---|---:|---:|---:|---:|---:|---:|
| balance | interactive | 63.3 · 22 | 61.5 · 18 | 82.8 · 24 | 53.3 · 22 | -36 % | 53.5 · 22 |
| team-overview | interactive | 204.3 · 10 | 41.9 · 10 | 1317.8 · 10 | 48.0 · 10 | -96 % | 56.5 · 10 |
| inbox: failures | interactive | 4.0 · 1 | 3.3 · 1 | 3.6 · 1 | 3.6 · 1 | +0 % | 3.0 · 1 |
| inbox: approvals | interactive | 20.8 · 7 | 21.5 · 7 | 19.9 · 7 | 23.0 · 7 | +16 % | 24.3 · 7 |
| inbox: user-notifications | interactive | 4.8 · 1 | 4.3 · 1 | 5.1 · 1 | 4.7 · 1 | -8 % | 3.8 · 1 |
| inbox: my-requests | interactive | 18.2 · 6 | 17.3 · 6 | 16.8 · 5 | 17.9 · 5 | +7 % | 20.1 · 6 |
| inbox: notifications | interactive | 6.0 · 2 | 4.4 · 2 | 4.8 · 2 | 5.0 · 2 | +4 % | 5.0 · 2 |
| TimeTracking week | interactive | 7.3 · 2 | 5.4 · 2 | 5.6 · 2 | 6.0 · 2 | +7 % | 6.0 · 2 |
| member entries week | interactive | 21.7 · 7 | 18.3 · 7 | 18.4 · 7 | 17.9 · 7 | -3 % | 18.8 · 7 |
| compliance month | interactive | 21.9 · 7 | 17.6 · 7 | 20.4 · 7 | 17.5 · 7 | -14 % | 31.6 · 9 |
| absences year | interactive | 24.9 · 7 | 21.4 · 7 | 23.6 · 7 | 30.3 · 7 | +28 % | 30.8 · 7 |
| org detail | interactive | 43.3 · 8 | 43.9 · 10 | 41.9 · 8 | 44.5 · 10 | +6 % | 60.4 · 10 |
| accounting summary month | export | 2200.8 · 903 | 52.5 · 11 | 4997.5 · 907* | 58.0 · 11 | -99 % | 83.7 · 11 |
| payroll CSV month | export | 2200.3 · 903 | 47.2 · 11 | 5706.9 · 907* | 56.4 · 11 | -99 % | 73.5 · 11 |
| accounting vacation year | export | 29.4 · 7 | 32.0 · 9 | 30.2 · 7 | 35.0 · 9 | +16 % | 44.1 · 9 |
| daily CSV month | export | 52.8 · 22 | 46.7 · 18 | 83.2 · 22 | 48.6 · 18 | -42 % | 61.5 · 22 |
| monthly PDF | export | 62.9 · 15 | 49.0 · 11 | 79.3 · 15 | 51.2 · 11 | -35 % | 50.8 · 13 |
| timer start | interactive | 17.8 · 7 | 20.3 · 7 | 17.0 · 7 | 16.5 · 7 | -3 % | 16.9 · 7 |
| timer stop | interactive | 19.3 · 7 | 23.4 · 7 | 17.4 · 7 | 18.0 · 7 | +3 % | 17.1 · 7 |
| manual entry create | interactive | 39.0 · 13 | 41.3 · 13 | 32.9 · 13 | 46.4 · 13 | +41 % | 39.2 · 13 |
| manual entry delete | interactive | 20.7 · 7 | 23.4 · 7 | 22.1 · 7 | 24.8 · 7 | +12 % | 21.4 · 7 |

Reading it:

- **Nothing grows with the years any more**: every endpoint's t(100x10)/t(100x1) is 0.8–1.4x (baseline: team overview 6.4x,
  exports 2.3–2.6x), and the SQL count of no endpoint depends on the history length. The 1-vs-3-years SQL equality is now
  pinned in CI by the PerfSmoke test (`Postgres/BalanceQueryBudgetTests.cs`).
- **Targets met at 100 × 10 years**: the slowest interactive p95 of the whole matrix is 138 ms (balance, 100x1 — a single
  outlier; 75 ms at 100x10), the slowest export p95 153 ms (daily CSV, 100x10) — target 300 ms / 2 s. Baseline: team
  overview p50 1.3 s, payroll / accounting summary 5–5.7 s with 907 SQL commands (N+1) at 10 years.
- The +2 SQL on org detail and accounting vacation come from the vacation carry-over (Phase 2b); time unchanged within noise.
- SQL counts that differ between sizes at the same code (balance 18/22, compliance 7/9, daily CSV 18/22, monthly PDF 11/13)
  are data-dependent, not history-dependent: the core-hours re-evaluation (`CoreHours.OpenViolationsAsync`) only queries when
  the range holds stored violations, which depends on the random history of perf-user1 at that size.
- Single-digit-ms differences on untouched endpoints (inbox, timer, manual entry, absences) are run-to-run noise: each
  p50 is 15 calls on a desktop machine; unchanged code measured up to 2x apart between the phase1 and phase2 runs (accounting vacation 26.8 vs 51.0 ms at 100x1).

## Cold first calls (no checkpoints), 100x10

Measured once more on this tree on a fresh 100x10 history (HTTP, as the app role under RLS, queries compiled beforehand,
`BalanceCheckpoints` emptied and `BalanceWarmedMonth` reset before each call; 3 rounds each, a temporary test deleted afterwards):

| call | cold (computes and stores the history) | next call |
|---|---:|---:|
| balance (one member, 10 years) | 74–93 ms | 44–65 ms |
| team-overview (99 members) | 1662–1771 ms | 41–48 ms |
| payroll CSV month (all 100 members) | 1755–1830 ms | 68–92 ms |
| accounting summary month | 1855–1921 ms | 72–88 ms |
| `BalanceCheckpoints.WarmAsync` (system pre-warm of the org) | 1535–1817 ms | — |

Same picture as `phase2.md` (1.7–1.9 s once, ~12,000 checkpoint rows written): the first full read after the deploy is
taken by the background pre-warm (`BalanceCheckpointWarmupService`), afterwards one row per member and month.

---

# Scaling report — 2026-10-19 13:35

Sizes (employees x years): 100x1, 100x2, 100x5, 100x10, 25x5, 50x5, 200x5. Each call: 3 warm-ups, 15 measured (calls slower than 5000 ms: 1 + 5, marked *), over HTTP (TestServer, no network) as the app role with row-level security on, against PostgreSQL 16 in Docker (Testcontainers). Method: every size gets a fresh database of its own (created empty, migrated by its own app instance, seeded with PerfSeeder by binary COPY, then ANALYZEd), measured, and dropped — sizes never share tables, indexes or caches beyond the Postgres server itself; the background jobs (business-day-end/core hours loop, push dispatch, retention) are not running. Times in ms: p50 and p95 by nearest rank over n measured calls (with n < 20 the p95 is the maximum), SQL = commands EF sent for one call (median). Member = perf-user1 (full history), admin = perf-admin1, owner. Month = the last ended month, week = the current one, year = 2026. Growth = p50 ratio between the shortest and longest history at the same team size.

## Seeded data

| size | seconds | Users | WorkSchedules | Holidays | Groups | TimeEntries | TimeEntryChanges | OrgRequests | AbsenceDays | CoreHoursChecks | TerminalPunches | Notifications |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 100x1 | 7.6 | 100 | 100 | 19 | 20 | 25,365 | 1,383 | 1,191 | 3,480 | 33,260 | 25,150 | 15,583 |
| 100x2 | 10.3 | 100 | 100 | 23 | 20 | 50,627 | 3,061 | 2,276 | 6,746 | 66,214 | 28,678 | 30,138 |
| 100x5 | 18.7 | 100 | 189 | 35 | 20 | 126,597 | 8,130 | 5,526 | 16,561 | 165,188 | 28,852 | 75,739 |
| 100x10 | 36.6 | 100 | 365 | 55 | 20 | 252,667 | 16,191 | 10,943 | 32,912 | 330,086 | 29,274 | 150,996 |
| 25x5 | 5.3 | 25 | 47 | 35 | 20 | 31,751 | 2,057 | 1,383 | 4,136 | 41,337 | 7,538 | 16,075 |
| 50x5 | 9.6 | 50 | 94 | 35 | 20 | 63,605 | 4,120 | 2,781 | 8,318 | 83,134 | 14,570 | 33,772 |
| 200x5 | 37.8 | 200 | 377 | 35 | 20 | 251,693 | 15,979 | 10,971 | 32,861 | 327,936 | 57,450 | 150,919 |

## Summary (p50 ms · SQL commands)

| endpoint | kind | 100x1 | 100x2 | 100x5 | 100x10 | 25x5 | 50x5 | 200x5 | t(100x10)/t(100x1) |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|
| balance | interactive | 61.5 · 18 | 38.2 · 18 | 48.3 · 22 | 53.3 · 22 | 52.9 · 22 | 45.8 · 18 | 53.5 · 22 | 0.9x (SQL 18 → 22) |
| team-overview | interactive | 41.9 · 10 | 37.8 · 10 | 35.1 · 10 | 48.0 · 10 | 31.8 · 10 | 29.0 · 10 | 56.5 · 10 | 1.1x (SQL 10 → 10) |
| inbox: failures | interactive | 3.3 · 1 | 3.7 · 1 | 2.8 · 1 | 3.6 · 1 | 6.8 · 1 | 2.9 · 1 | 3.0 · 1 | 1.1x (SQL 1 → 1) |
| inbox: approvals | interactive | 21.5 · 7 | 23.1 · 7 | 19.3 · 7 | 23.0 · 7 | 27.9 · 7 | 17.5 · 7 | 24.3 · 7 | 1.1x (SQL 7 → 7) |
| inbox: user-notifications | interactive | 4.3 · 1 | 5.2 · 1 | 4.2 · 1 | 4.7 · 1 | 5.5 · 1 | 4.5 · 1 | 3.8 · 1 | 1.1x (SQL 1 → 1) |
| inbox: my-requests | interactive | 17.3 · 6 | 20.0 · 6 | 16.3 · 6 | 17.9 · 5 | 19.1 · 6 | 14.8 · 5 | 20.1 · 6 | 1.0x (SQL 6 → 5) |
| inbox: notifications | interactive | 4.4 · 2 | 4.9 · 2 | 4.5 · 2 | 5.0 · 2 | 4.6 · 2 | 4.0 · 2 | 5.0 · 2 | 1.1x (SQL 2 → 2) |
| TimeTracking week | interactive | 5.4 · 2 | 6.1 · 2 | 5.3 · 2 | 6.0 · 2 | 6.0 · 2 | 6.3 · 2 | 6.0 · 2 | 1.1x (SQL 2 → 2) |
| member entries week | interactive | 18.3 · 7 | 18.7 · 7 | 18.3 · 7 | 17.9 · 7 | 17.4 · 7 | 16.8 · 7 | 18.8 · 7 | 1.0x (SQL 7 → 7) |
| compliance month | interactive | 17.6 · 7 | 22.3 · 9 | 19.5 · 9 | 17.5 · 7 | 22.8 · 9 | 20.4 · 7 | 31.6 · 9 | 1.0x (SQL 7 → 7) |
| absences year | interactive | 21.4 · 7 | 21.6 · 7 | 19.1 · 7 | 30.3 · 7 | 22.4 · 7 | 18.8 · 7 | 30.8 · 7 | 1.4x (SQL 7 → 7) |
| org detail | interactive | 43.9 · 10 | 40.5 · 10 | 35.0 · 10 | 44.5 · 10 | 31.2 · 10 | 29.7 · 10 | 60.4 · 10 | 1.0x (SQL 10 → 10) |
| accounting summary month | export | 52.5 · 11 | 52.7 · 11 | 61.6 · 11 | 58.0 · 11 | 36.0 · 11 | 43.8 · 11 | 83.7 · 11 | 1.1x (SQL 11 → 11) |
| payroll CSV month | export | 47.2 · 11 | 48.5 · 11 | 52.2 · 11 | 56.4 · 11 | 43.2 · 11 | 41.2 · 11 | 73.5 · 11 | 1.2x (SQL 11 → 11) |
| accounting vacation year | export | 32.0 · 9 | 31.4 · 9 | 31.4 · 9 | 35.0 · 9 | 33.0 · 9 | 27.3 · 9 | 44.1 · 9 | 1.1x (SQL 9 → 9) |
| daily CSV month | export | 46.7 · 18 | 55.5 · 22 | 58.0 · 22 | 48.6 · 18 | 55.4 · 22 | 46.8 · 18 | 61.5 · 22 | 1.0x (SQL 18 → 18) |
| monthly PDF | export | 49.0 · 11 | 55.6 · 13 | 57.4 · 13 | 51.2 · 11 | 49.8 · 13 | 50.2 · 11 | 50.8 · 13 | 1.0x (SQL 11 → 11) |
| timer start | interactive | 20.3 · 7 | 18.2 · 7 | 21.0 · 7 | 16.5 · 7 | 18.0 · 7 | 16.5 · 7 | 16.9 · 7 | 0.8x (SQL 7 → 7) |
| timer stop | interactive | 23.4 · 7 | 23.6 · 7 | 20.0 · 7 | 18.0 · 7 | 18.1 · 7 | 25.3 · 7 | 17.1 · 7 | 0.8x (SQL 7 → 7) |
| manual entry create | interactive | 41.3 · 13 | 37.5 · 13 | 38.1 · 13 | 46.4 · 13 | 41.2 · 13 | 36.7 · 13 | 39.2 · 13 | 1.1x (SQL 13 → 13) |
| manual entry delete | interactive | 23.4 · 7 | 21.0 · 7 | 20.3 · 7 | 24.8 · 7 | 22.9 · 7 | 23.8 · 7 | 21.4 · 7 | 1.1x (SQL 7 → 7) |

## Per endpoint

### balance (interactive)

| | 100x1 | 100x2 | 100x5 | 100x10 | 25x5 | 50x5 | 200x5 |
|---|---:|---:|---:|---:|---:|---:|---:|
| p50 ms | 61.5 | 38.2 | 48.3 | 53.3 | 52.9 | 45.8 | 53.5 |
| p95 ms (nearest rank) | 138.1 | 73.5 | 82.3 | 74.9 | 75.9 | 119.5 | 106.3 |
| n | 15 | 15 | 15 | 15 | 15 | 15 | 15 |
| SQL | 18 | 18 | 22 | 22 | 22 | 18 | 22 |

Growth t(100x10)/t(100x1): 0.9x (SQL 18 → 22)

### team-overview (interactive)

| | 100x1 | 100x2 | 100x5 | 100x10 | 25x5 | 50x5 | 200x5 |
|---|---:|---:|---:|---:|---:|---:|---:|
| p50 ms | 41.9 | 37.8 | 35.1 | 48.0 | 31.8 | 29.0 | 56.5 |
| p95 ms (nearest rank) | 58.0 | 59.9 | 37.8 | 90.9 | 47.3 | 35.6 | 75.4 |
| n | 15 | 15 | 15 | 15 | 15 | 15 | 15 |
| SQL | 10 | 10 | 10 | 10 | 10 | 10 | 10 |

Growth t(100x10)/t(100x1): 1.1x (SQL 10 → 10)

### inbox: failures (interactive)

| | 100x1 | 100x2 | 100x5 | 100x10 | 25x5 | 50x5 | 200x5 |
|---|---:|---:|---:|---:|---:|---:|---:|
| p50 ms | 3.3 | 3.7 | 2.8 | 3.6 | 6.8 | 2.9 | 3.0 |
| p95 ms (nearest rank) | 5.3 | 5.9 | 3.1 | 4.3 | 8.0 | 4.1 | 4.2 |
| n | 15 | 15 | 15 | 15 | 15 | 15 | 15 |
| SQL | 1 | 1 | 1 | 1 | 1 | 1 | 1 |

Growth t(100x10)/t(100x1): 1.1x (SQL 1 → 1)

### inbox: approvals (interactive)

| | 100x1 | 100x2 | 100x5 | 100x10 | 25x5 | 50x5 | 200x5 |
|---|---:|---:|---:|---:|---:|---:|---:|
| p50 ms | 21.5 | 23.1 | 19.3 | 23.0 | 27.9 | 17.5 | 24.3 |
| p95 ms (nearest rank) | 24.0 | 28.0 | 22.7 | 24.6 | 77.6 | 21.4 | 25.2 |
| n | 15 | 15 | 15 | 15 | 15 | 15 | 15 |
| SQL | 7 | 7 | 7 | 7 | 7 | 7 | 7 |

Growth t(100x10)/t(100x1): 1.1x (SQL 7 → 7)

### inbox: user-notifications (interactive)

| | 100x1 | 100x2 | 100x5 | 100x10 | 25x5 | 50x5 | 200x5 |
|---|---:|---:|---:|---:|---:|---:|---:|
| p50 ms | 4.3 | 5.2 | 4.2 | 4.7 | 5.5 | 4.5 | 3.8 |
| p95 ms (nearest rank) | 6.3 | 5.6 | 4.5 | 5.3 | 6.1 | 5.9 | 5.1 |
| n | 15 | 15 | 15 | 15 | 15 | 15 | 15 |
| SQL | 1 | 1 | 1 | 1 | 1 | 1 | 1 |

Growth t(100x10)/t(100x1): 1.1x (SQL 1 → 1)

### inbox: my-requests (interactive)

| | 100x1 | 100x2 | 100x5 | 100x10 | 25x5 | 50x5 | 200x5 |
|---|---:|---:|---:|---:|---:|---:|---:|
| p50 ms | 17.3 | 20.0 | 16.3 | 17.9 | 19.1 | 14.8 | 20.1 |
| p95 ms (nearest rank) | 22.4 | 23.7 | 17.0 | 30.5 | 20.4 | 17.5 | 21.8 |
| n | 15 | 15 | 15 | 15 | 15 | 15 | 15 |
| SQL | 6 | 6 | 6 | 5 | 6 | 5 | 6 |

Growth t(100x10)/t(100x1): 1.0x (SQL 6 → 5)

### inbox: notifications (interactive)

| | 100x1 | 100x2 | 100x5 | 100x10 | 25x5 | 50x5 | 200x5 |
|---|---:|---:|---:|---:|---:|---:|---:|
| p50 ms | 4.4 | 4.9 | 4.5 | 5.0 | 4.6 | 4.0 | 5.0 |
| p95 ms (nearest rank) | 5.2 | 8.7 | 5.9 | 5.9 | 5.1 | 4.4 | 5.6 |
| n | 15 | 15 | 15 | 15 | 15 | 15 | 15 |
| SQL | 2 | 2 | 2 | 2 | 2 | 2 | 2 |

Growth t(100x10)/t(100x1): 1.1x (SQL 2 → 2)

### TimeTracking week (interactive)

| | 100x1 | 100x2 | 100x5 | 100x10 | 25x5 | 50x5 | 200x5 |
|---|---:|---:|---:|---:|---:|---:|---:|
| p50 ms | 5.4 | 6.1 | 5.3 | 6.0 | 6.0 | 6.3 | 6.0 |
| p95 ms (nearest rank) | 6.3 | 6.9 | 5.9 | 6.5 | 6.9 | 9.5 | 8.4 |
| n | 15 | 15 | 15 | 15 | 15 | 15 | 15 |
| SQL | 2 | 2 | 2 | 2 | 2 | 2 | 2 |

Growth t(100x10)/t(100x1): 1.1x (SQL 2 → 2)

### member entries week (interactive)

| | 100x1 | 100x2 | 100x5 | 100x10 | 25x5 | 50x5 | 200x5 |
|---|---:|---:|---:|---:|---:|---:|---:|
| p50 ms | 18.3 | 18.7 | 18.3 | 17.9 | 17.4 | 16.8 | 18.8 |
| p95 ms (nearest rank) | 25.2 | 20.5 | 22.4 | 19.0 | 18.9 | 20.3 | 20.9 |
| n | 15 | 15 | 15 | 15 | 15 | 15 | 15 |
| SQL | 7 | 7 | 7 | 7 | 7 | 7 | 7 |

Growth t(100x10)/t(100x1): 1.0x (SQL 7 → 7)

### compliance month (interactive)

| | 100x1 | 100x2 | 100x5 | 100x10 | 25x5 | 50x5 | 200x5 |
|---|---:|---:|---:|---:|---:|---:|---:|
| p50 ms | 17.6 | 22.3 | 19.5 | 17.5 | 22.8 | 20.4 | 31.6 |
| p95 ms (nearest rank) | 21.1 | 27.6 | 22.0 | 20.3 | 26.9 | 25.4 | 43.2 |
| n | 15 | 15 | 15 | 15 | 15 | 15 | 15 |
| SQL | 7 | 9 | 9 | 7 | 9 | 7 | 9 |

Growth t(100x10)/t(100x1): 1.0x (SQL 7 → 7)

### absences year (interactive)

| | 100x1 | 100x2 | 100x5 | 100x10 | 25x5 | 50x5 | 200x5 |
|---|---:|---:|---:|---:|---:|---:|---:|
| p50 ms | 21.4 | 21.6 | 19.1 | 30.3 | 22.4 | 18.8 | 30.8 |
| p95 ms (nearest rank) | 69.3 | 24.3 | 20.9 | 39.6 | 55.3 | 22.7 | 41.9 |
| n | 15 | 15 | 15 | 15 | 15 | 15 | 15 |
| SQL | 7 | 7 | 7 | 7 | 7 | 7 | 7 |

Growth t(100x10)/t(100x1): 1.4x (SQL 7 → 7)

### org detail (interactive)

| | 100x1 | 100x2 | 100x5 | 100x10 | 25x5 | 50x5 | 200x5 |
|---|---:|---:|---:|---:|---:|---:|---:|
| p50 ms | 43.9 | 40.5 | 35.0 | 44.5 | 31.2 | 29.7 | 60.4 |
| p95 ms (nearest rank) | 65.5 | 49.3 | 38.7 | 53.9 | 34.1 | 33.3 | 74.2 |
| n | 15 | 15 | 15 | 15 | 15 | 15 | 15 |
| SQL | 10 | 10 | 10 | 10 | 10 | 10 | 10 |

Growth t(100x10)/t(100x1): 1.0x (SQL 10 → 10)

### accounting summary month (export)

| | 100x1 | 100x2 | 100x5 | 100x10 | 25x5 | 50x5 | 200x5 |
|---|---:|---:|---:|---:|---:|---:|---:|
| p50 ms | 52.5 | 52.7 | 61.6 | 58.0 | 36.0 | 43.8 | 83.7 |
| p95 ms (nearest rank) | 65.9 | 76.5 | 87.7 | 72.8 | 102.4 | 49.1 | 132.9 |
| n | 15 | 15 | 15 | 15 | 15 | 15 | 15 |
| SQL | 11 | 11 | 11 | 11 | 11 | 11 | 11 |

Growth t(100x10)/t(100x1): 1.1x (SQL 11 → 11)

### payroll CSV month (export)

| | 100x1 | 100x2 | 100x5 | 100x10 | 25x5 | 50x5 | 200x5 |
|---|---:|---:|---:|---:|---:|---:|---:|
| p50 ms | 47.2 | 48.5 | 52.2 | 56.4 | 43.2 | 41.2 | 73.5 |
| p95 ms (nearest rank) | 51.8 | 57.7 | 112.2 | 74.9 | 67.2 | 44.7 | 117.2 |
| n | 15 | 15 | 15 | 15 | 15 | 15 | 15 |
| SQL | 11 | 11 | 11 | 11 | 11 | 11 | 11 |

Growth t(100x10)/t(100x1): 1.2x (SQL 11 → 11)

### accounting vacation year (export)

| | 100x1 | 100x2 | 100x5 | 100x10 | 25x5 | 50x5 | 200x5 |
|---|---:|---:|---:|---:|---:|---:|---:|
| p50 ms | 32.0 | 31.4 | 31.4 | 35.0 | 33.0 | 27.3 | 44.1 |
| p95 ms (nearest rank) | 38.0 | 34.0 | 40.0 | 41.8 | 40.1 | 29.2 | 63.4 |
| n | 15 | 15 | 15 | 15 | 15 | 15 | 15 |
| SQL | 9 | 9 | 9 | 9 | 9 | 9 | 9 |

Growth t(100x10)/t(100x1): 1.1x (SQL 9 → 9)

### daily CSV month (export)

| | 100x1 | 100x2 | 100x5 | 100x10 | 25x5 | 50x5 | 200x5 |
|---|---:|---:|---:|---:|---:|---:|---:|
| p50 ms | 46.7 | 55.5 | 58.0 | 48.6 | 55.4 | 46.8 | 61.5 |
| p95 ms (nearest rank) | 85.8 | 64.2 | 66.6 | 152.5 | 68.7 | 54.1 | 116.5 |
| n | 15 | 15 | 15 | 15 | 15 | 15 | 15 |
| SQL | 18 | 22 | 22 | 18 | 22 | 18 | 22 |

Growth t(100x10)/t(100x1): 1.0x (SQL 18 → 18)

### monthly PDF (export)

| | 100x1 | 100x2 | 100x5 | 100x10 | 25x5 | 50x5 | 200x5 |
|---|---:|---:|---:|---:|---:|---:|---:|
| p50 ms | 49.0 | 55.6 | 57.4 | 51.2 | 49.8 | 50.2 | 50.8 |
| p95 ms (nearest rank) | 58.0 | 76.4 | 71.4 | 61.6 | 56.7 | 56.1 | 59.7 |
| n | 15 | 15 | 15 | 15 | 15 | 15 | 15 |
| SQL | 11 | 13 | 13 | 11 | 13 | 11 | 13 |

Growth t(100x10)/t(100x1): 1.0x (SQL 11 → 11)

### timer start (interactive)

| | 100x1 | 100x2 | 100x5 | 100x10 | 25x5 | 50x5 | 200x5 |
|---|---:|---:|---:|---:|---:|---:|---:|
| p50 ms | 20.3 | 18.2 | 21.0 | 16.5 | 18.0 | 16.5 | 16.9 |
| p95 ms (nearest rank) | 30.0 | 22.1 | 27.6 | 21.9 | 20.0 | 20.9 | 18.5 |
| n | 15 | 15 | 15 | 15 | 15 | 15 | 15 |
| SQL | 7 | 7 | 7 | 7 | 7 | 7 | 7 |

Growth t(100x10)/t(100x1): 0.8x (SQL 7 → 7)

### timer stop (interactive)

| | 100x1 | 100x2 | 100x5 | 100x10 | 25x5 | 50x5 | 200x5 |
|---|---:|---:|---:|---:|---:|---:|---:|
| p50 ms | 23.4 | 23.6 | 20.0 | 18.0 | 18.1 | 25.3 | 17.1 |
| p95 ms (nearest rank) | 25.9 | 29.1 | 24.9 | 23.1 | 24.4 | 30.1 | 19.2 |
| n | 15 | 15 | 15 | 15 | 15 | 15 | 15 |
| SQL | 7 | 7 | 7 | 7 | 7 | 7 | 7 |

Growth t(100x10)/t(100x1): 0.8x (SQL 7 → 7)

### manual entry create (interactive)

| | 100x1 | 100x2 | 100x5 | 100x10 | 25x5 | 50x5 | 200x5 |
|---|---:|---:|---:|---:|---:|---:|---:|
| p50 ms | 41.3 | 37.5 | 38.1 | 46.4 | 41.2 | 36.7 | 39.2 |
| p95 ms (nearest rank) | 64.0 | 42.8 | 46.4 | 91.6 | 66.3 | 45.6 | 48.0 |
| n | 15 | 15 | 15 | 15 | 15 | 15 | 15 |
| SQL | 13 | 13 | 13 | 13 | 13 | 13 | 13 |

Growth t(100x10)/t(100x1): 1.1x (SQL 13 → 13)

### manual entry delete (interactive)

| | 100x1 | 100x2 | 100x5 | 100x10 | 25x5 | 50x5 | 200x5 |
|---|---:|---:|---:|---:|---:|---:|---:|
| p50 ms | 23.4 | 21.0 | 20.3 | 24.8 | 22.9 | 23.8 | 21.4 |
| p95 ms (nearest rank) | 31.0 | 25.5 | 22.6 | 45.9 | 28.7 | 29.5 | 22.8 |
| n | 15 | 15 | 15 | 15 | 15 | 15 | 15 |
| SQL | 7 | 7 | 7 | 7 | 7 | 7 | 7 |

Growth t(100x10)/t(100x1): 1.1x (SQL 7 → 7)

## EXPLAIN (ANALYZE, BUFFERS) — the 5 slowest calls at 100x10

The slowest SELECTs (up to 3) of the last measured call, re-run as the app role for the same user (RLS on).

### accounting summary month — p50 58.0 ms, 11 SQL

12.4 ms in the call:

```sql
SELECT u."UserId", u."BalanceVersion", o."BalanceVersion", o."TimeZone", (
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
Sort  (cost=77.55..77.55 rows=1 width=290) (actual time=11.035..11.055 rows=365 loops=1)
  Sort Key: u."Id", w."ValidFrom"
  Sort Method: quicksort  Memory: 93kB
  Buffers: shared hit=6819
  ->  Nested Loop Left Join  (cost=0.67..77.54 rows=1 width=290) (actual time=0.137..10.741 rows=365 loops=1)
        Buffers: shared hit=6819
        ->  Nested Loop  (cost=0.39..10.66 rows=1 width=166) (actual time=0.034..0.197 rows=100 loops=1)
              Buffers: shared hit=202
              ->  Seq Scan on "UserOrganizations" u  (cost=0.25..2.50 rows=1 width=16) (actual time=0.019..0.057 rows=100 loops=1)
                    Filter: (("OrganizationId" = 2) AND ("UserId" = ANY ('{16,17,18,19,20,15,21,30,31,32,33,34,35,36,37,38,39,22,40,41,42,43,44,45,46,47,48,49,23,50,51,52,53,54,55,56,57,58,59,24,60,61,62,63,64,65,66,67,68,69,25,70,71,72,73,74,75,76,77,78,79,26,80,81,82,83,84,85,86,87,88,89,27,90,91,92,93,94,95,96,97,98,99,28,100,101,102,103,104,105,106,107,108,109,29,110,111,112,113,114}'::integer[])))
                    Rows Removed by Filter: 14
                    Buffers: shared hit=2
              ->  Index Scan using "PK_Organizations" on "Organizations" o  (cost=0.14..8.16 rows=1 width=154) (actual time=0.001..0.001 rows=1 loops=100)
                    Index Cond: ("Id" = 2)
                    Buffers: shared hit=200
        ->  Index Scan using "IX_WorkSchedules_UserId_OrganizationId_ValidFrom" on "WorkSchedules" w  (cost=0.28..8.29 rows=1 width=92) (actual time=0.002..0.003 rows=4 loops=100)
              Index Cond: (("UserId" = u."UserId") AND ("OrganizationId" = 2))
              Buffers: shared hit=304
        SubPlan 2
          ->  Limit  (cost=2.60..10.65 rows=1 width=4) (actual time=0.005..0.005 rows=1 loops=365)
                Buffers: shared hit=1097
                ->  Index Scan Backward using "IX_BalanceCheckpoints_OrganizationId_UserId_Month" on "BalanceCheckpoints" b  (cost=2.60..10.65 rows=1 width=4) (actual time=0.005..0.005 rows=1 loops=365)
                      Index Cond: (("OrganizationId" = 2) AND ("UserId" = u."UserId") AND ("Month" < '2026-09-01'::date))
                      Filter: (("Algorithm" = 1) AND ("Version" = u."BalanceVersion") AND ("OrgVersion" = o."BalanceVersion") AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 1)))
                      Buffers: shared hit=1097
                      SubPlan 1
                        ->  Seq Scan on "UserOrganizations" uo  (cost=0.00..2.31 rows=1 width=4) (actual time=0.006..0.033 rows=1 loops=1)
                              Filter: ("IsActive" AND ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer))
                              Rows Removed by Filter: 113
                              Buffers: shared hit=2
        SubPlan 4
          ->  Limit  (cost=2.60..10.65 rows=1 width=12) (actual time=0.005..0.005 rows=1 loops=365)
                Buffers: shared hit=1097
                ->  Index Scan Backward using "IX_BalanceCheckpoints_OrganizationId_UserId_Month" on "BalanceCheckpoints" b0  (cost=2.60..10.65 rows=1 width=12) (actual time=0.004..0.004 rows=1 loops=365)
                      Index Cond: (("OrganizationId" = 2) AND ("UserId" = u."UserId") AND ("Month" < '2026-09-01'::date))
                      Filter: (("Algorithm" = 1) AND ("Version" = u."BalanceVersion") AND ("OrgVersion" = o."BalanceVersion") AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 3)))
                      Buffers: shared hit=1097
                      SubPlan 3
                        ->  Seq Scan on "UserOrganizations" uo_1  (cost=0.00..2.31 rows=1 width=4) (actual time=0.006..0.031 rows=1 loops=1)
                              Filter: ("IsActive" AND ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer))
                              Rows Removed by Filter: 113
                              Buffers: shared hit=2
        SubPlan 6
          ->  Limit  (cost=2.60..10.65 rows=1 width=8) (actual time=0.005..0.005 rows=1 loops=365)
                Buffers: shared hit=1097
                ->  Index Scan Backward using "IX_BalanceCheckpoints_OrganizationId_UserId_Month" on "BalanceCheckpoints" b1  (cost=2.60..10.65 rows=1 width=8) (actual time=0.004..0.004 rows=1 loops=365)
                      Index Cond: (("OrganizationId" = 2) AND ("UserId" = u."UserId") AND ("Month" < '2026-09-01'::date))
                      Filter: (("Algorithm" = 1) AND ("Version" = u."BalanceVersion") AND ("OrgVersion" = o."BalanceVersion") AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 5)))
                      Buffers: shared hit=1097
                      SubPlan 5
                        ->  Seq Scan on "UserOrganizations" uo_2  (cost=0.00..2.31 rows=1 width=4) (actual time=0.005..0.030 rows=1 loops=1)
                              Filter: ("IsActive" AND ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer))
                              Rows Removed by Filter: 113
                              Buffers: shared hit=2
        SubPlan 9
          ->  Result  (cost=10.65..10.66 rows=1 width=4) (actual time=0.005..0.005 rows=1 loops=365)
                Buffers: shared hit=1097
                InitPlan 8 (returns $16)
                  ->  Limit  (cost=2.60..10.65 rows=1 width=4) (actual time=0.005..0.005 rows=1 loops=365)
                        Buffers: shared hit=1097
                        ->  Index Scan Backward using "IX_BalanceCheckpoints_OrganizationId_UserId_Month" on "BalanceCheckpoints" b2  (cost=2.60..10.65 rows=1 width=4) (actual time=0.004..0.004 rows=1 loops=365)
                              Index Cond: (("OrganizationId" = 2) AND ("UserId" = u."UserId") AND ("Month" IS NOT NULL))
                              Filter: (("Algorithm" = 1) AND ("Version" = u."BalanceVersion") AND ("OrgVersion" = o."BalanceVersion") AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 7)))
                              Buffers: shared hit=1097
                              SubPlan 7
                                ->  Seq Scan on "UserOrganizations" uo_3  (cost=0.00..2.31 rows=1 width=4) (actual time=0.006..0.031 rows=1 loops=1)
                                      Filter: ("IsActive" AND ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer))
                                      Rows Removed by Filter: 113
                                      Buffers: shared hit=2
        SubPlan 12
          ->  Result  (cost=5.42..5.43 rows=1 width=8) (actual time=0.005..0.005 rows=1 loops=365)
                Buffers: shared hit=1462
                InitPlan 11 (returns $19)
                  ->  Limit  (cost=2.74..5.42 rows=1 width=8) (actual time=0.005..0.005 rows=1 loops=365)
                        Buffers: shared hit=1462
                        ->  Index Scan using "IX_TimeEntries_UserId_StartTime" on "TimeEntries" t  (cost=2.74..3057.11 rows=1137 width=8) (actual time=0.005..0.005 rows=1 loops=365)
                              Index Cond: (("UserId" = u."UserId") AND ("StartTime" IS NOT NULL))
                              Filter: ((NOT "IsRunning") AND ("OrganizationId" = 2) AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 10)))
                              Buffers: shared hit=1462
                              SubPlan 10
                                ->  Seq Scan on "UserOrganizations" uo_4  (cost=0.00..2.31 rows=1 width=4) (actual time=0.005..0.031 rows=1 loops=1)
                                      Filter: ("IsActive" AND ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer))
                                      Rows Removed by Filter: 113
                                      Buffers: shared hit=2
        SubPlan 14
          ->  Aggregate  (cost=10.50..10.51 rows=1 width=4) (actual time=0.001..0.001 rows=1 loops=365)
                Buffers: shared hit=463
                ->  Index Scan using "IX_TimeEntries_UserId_Failure" on "TimeEntries" t0  (cost=2.46..10.50 rows=1 width=0) (actual time=0.001..0.001 rows=0 loops=365)
                      Index Cond: ("UserId" = u."UserId")
                      Filter: (("StartTime" < '1900-01-01 00:00:00+00'::timestamp with time zone) AND ("OrganizationId" = 2) AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 13)))
                      Rows Removed by Filter: 0
                      Buffers: shared hit=463
                      SubPlan 13
                        ->  Seq Scan on "UserOrganizations" uo_5  (cost=0.00..2.31 rows=1 width=4) (never executed)
                              Filter: ("IsActive" AND ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer))
Planning Time: 1.213 ms
Execution Time: 11.392 ms
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
HashAggregate  (cost=3664.90..3666.90 rows=200 width=4) (actual time=3.983..3.996 rows=100 loops=1)
  Group Key: t."UserId"
  Batches: 1  Memory Usage: 40kB
  Buffers: shared hit=2361
  ->  HashAggregate  (cost=3654.56..3662.83 rows=827 width=4) (actual time=3.947..3.964 rows=100 loops=1)
        Group Key: t."UserId"
        Batches: 1  Memory Usage: 49kB
        Buffers: shared hit=2361
        ->  Append  (cost=2.88..3652.49 rows=827 width=4) (actual time=0.082..3.553 rows=2671 loops=1)
              Buffers: shared hit=2361
              ->  Nested Loop Left Join  (cost=2.88..3254.40 rows=613 width=4) (actual time=0.081..2.927 rows=2265 loops=1)
                    Filter: ((t."OrganizationId" IS NULL) OR o."IsActive")
                    Buffers: shared hit=2280
                    ->  Index Scan using "IX_TimeEntries_OrganizationId_StartTime" on "TimeEntries" t  (cost=2.74..3230.91 rows=1226 width=8) (actual time=0.067..2.416 rows=2265 loops=1)
                          Index Cond: (("OrganizationId" = 2) AND ("StartTime" >= '2026-08-31 22:00:00+00'::timestamp with time zone) AND ("StartTime" < '2026-09-30 22:00:00+00'::timestamp with time zone))
                          Filter: ((NOT "IsRunning") AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 1)))
                          Buffers: shared hit=2278
                          SubPlan 1
                            ->  Seq Scan on "UserOrganizations" uo  (cost=0.00..2.31 rows=1 width=4) (actual time=0.008..0.035 rows=1 loops=1)
                                  Filter: ("IsActive" AND ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer))
                                  Rows Removed by Filter: 113
                                  Buffers: shared hit=2
                    ->  Materialize  (cost=0.14..8.17 rows=1 width=5) (actual time=0.000..0.000 rows=1 loops=2265)
                          Buffers: shared hit=2
                          ->  Index Scan using "PK_Organizations" on "Organizations" o  (cost=0.14..8.16 rows=1 width=5) (actual time=0.009..0.009 rows=1 loops=1)
                                Index Cond: ("Id" = 2)
                                Filter: "IsActive"
                                Buffers: shared hit=2
              ->  Nested Loop  (cost=12.08..393.96 rows=214 width=4) (actual time=0.105..0.452 rows=406 loops=1)
                    Buffers: shared hit=81
                    ->  Index Scan using "PK_Organizations" on "Organizations" o1  (cost=0.14..8.16 rows=1 width=4) (actual time=0.014..0.015 rows=1 loops=1)
                          Index Cond: ("Id" = 2)
                          Filter: ("IsActive" AND "IsActive")
                          Buffers: shared hit=2
                    ->  Bitmap Heap Scan on "AbsenceDays" a  (cost=11.94..383.66 rows=214 width=8) (actual time=0.085..0.398 rows=406 loops=1)
                          Recheck Cond: (("OrganizationId" = 2) AND ("Date" >= '2026-09-01'::date) AND ("Date" <= '2026-09-30'::date))
                          Filter: ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 2))
                          Heap Blocks: exact=75
                          Buffers: shared hit=79
                          ->  Bitmap Index Scan on "IX_AbsenceDays_OrganizationId_Date"  (cost=0.00..9.56 rows=422 width=0) (actual time=0.031..0.031 rows=406 loops=1)
                                Index Cond: (("OrganizationId" = 2) AND ("Date" >= '2026-09-01'::date) AND ("Date" <= '2026-09-30'::date))
                                Buffers: shared hit=2
                          SubPlan 2
                            ->  Seq Scan on "UserOrganizations" uo_1  (cost=0.00..2.31 rows=1 width=4) (actual time=0.007..0.033 rows=1 loops=1)
                                  Filter: ("IsActive" AND ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer))
                                  Rows Removed by Filter: 113
                                  Buffers: shared hit=2
Planning Time: 0.523 ms
Execution Time: 4.161 ms
```

2.0 ms in the call:

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
Nested Loop Left Join  (cost=82.05..3321.76 rows=611 width=26) (actual time=0.235..2.793 rows=2265 loops=1)
  Filter: ((t."OrganizationId" IS NULL) OR o."IsActive")
  Buffers: shared hit=138
  ->  Bitmap Heap Scan on "TimeEntries" t  (cost=81.90..3298.32 rows=1222 width=30) (actual time=0.219..2.179 rows=2265 loops=1)
        Recheck Cond: ((("OrganizationId" = 2) AND ("StartTime" >= '2026-08-31 22:00:00+00'::timestamp with time zone) AND ("StartTime" < '2026-09-30 22:00:00+00'::timestamp with time zone)) OR ("UserId" = ANY ('{}'::integer[])))
        Filter: ((((NOT "IsRunning") AND ("UserId" = ANY ('{15,16,17,18,19,20,21,22,23,24,25,26,27,28,29,30,31,32,33,34,35,36,37,38,39,40,41,42,43,44,45,46,47,48,49,50,51,52,53,54,55,56,57,58,59,60,61,62,63,64,65,66,67,68,69,70,71,72,73,74,75,76,77,78,79,80,81,82,83,84,85,86,87,88,89,90,91,92,93,94,95,96,97,98,99,100,101,102,103,104,105,106,107,108,109,110,111,112,113,114}'::integer[])) AND ("StartTime" >= '2026-08-31 22:00:00+00'::timestamp with time zone) AND ("StartTime" < '2026-09-30 22:00:00+00'::timestamp with time zone)) OR ("IsRunning" AND ("UserId" = ANY ('{}'::integer[])) AND ("StartTime" < '2026-10-19 22:00:00+00'::timestamp with time zone))) AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 1)))
        Heap Blocks: exact=123
        Buffers: shared hit=136
        ->  BitmapOr  (cost=79.34..79.34 rows=2413 width=0) (actual time=0.188..0.188 rows=0 loops=1)
              Buffers: shared hit=11
              ->  Bitmap Index Scan on "IX_TimeEntries_OrganizationId_StartTime"  (cost=0.00..74.58 rows=2413 width=0) (actual time=0.187..0.187 rows=2265 loops=1)
                    Index Cond: (("OrganizationId" = 2) AND ("StartTime" >= '2026-08-31 22:00:00+00'::timestamp with time zone) AND ("StartTime" < '2026-09-30 22:00:00+00'::timestamp with time zone))
                    Buffers: shared hit=11
              ->  Bitmap Index Scan on "IX_TimeEntries_UserId"  (cost=0.00..4.14 rows=1 width=0) (actual time=0.000..0.000 rows=0 loops=1)
                    Index Cond: ("UserId" = ANY ('{}'::integer[]))
        SubPlan 1
          ->  Seq Scan on "UserOrganizations" uo  (cost=0.00..2.31 rows=1 width=4) (actual time=0.008..0.035 rows=1 loops=1)
                Filter: ("IsActive" AND ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer))
                Rows Removed by Filter: 113
                Buffers: shared hit=2
  ->  Materialize  (cost=0.14..8.17 rows=1 width=5) (actual time=0.000..0.000 rows=1 loops=2265)
        Buffers: shared hit=2
        ->  Index Scan using "PK_Organizations" on "Organizations" o  (cost=0.14..8.16 rows=1 width=5) (actual time=0.011..0.011 rows=1 loops=1)
              Index Cond: ("Id" = 2)
              Filter: "IsActive"
              Buffers: shared hit=2
Planning Time: 0.433 ms
Execution Time: 2.997 ms
```

### payroll CSV month — p50 56.4 ms, 11 SQL

15.3 ms in the call:

```sql
SELECT u."UserId", u."BalanceVersion", o."BalanceVersion", o."TimeZone", (
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
Sort  (cost=77.55..77.55 rows=1 width=290) (actual time=10.484..10.503 rows=365 loops=1)
  Sort Key: u."Id", w."ValidFrom"
  Sort Method: quicksort  Memory: 93kB
  Buffers: shared hit=6819
  ->  Nested Loop Left Join  (cost=0.67..77.54 rows=1 width=290) (actual time=0.119..10.213 rows=365 loops=1)
        Buffers: shared hit=6819
        ->  Nested Loop  (cost=0.39..10.66 rows=1 width=166) (actual time=0.027..0.195 rows=100 loops=1)
              Buffers: shared hit=202
              ->  Seq Scan on "UserOrganizations" u  (cost=0.25..2.50 rows=1 width=16) (actual time=0.014..0.049 rows=100 loops=1)
                    Filter: (("OrganizationId" = 2) AND ("UserId" = ANY ('{16,17,18,19,20,15,21,30,31,32,33,34,35,36,37,38,39,22,40,41,42,43,44,45,46,47,48,49,23,50,51,52,53,54,55,56,57,58,59,24,60,61,62,63,64,65,66,67,68,69,25,70,71,72,73,74,75,76,77,78,79,26,80,81,82,83,84,85,86,87,88,89,27,90,91,92,93,94,95,96,97,98,99,28,100,101,102,103,104,105,106,107,108,109,29,110,111,112,113,114}'::integer[])))
                    Rows Removed by Filter: 14
                    Buffers: shared hit=2
              ->  Index Scan using "PK_Organizations" on "Organizations" o  (cost=0.14..8.16 rows=1 width=154) (actual time=0.001..0.001 rows=1 loops=100)
                    Index Cond: ("Id" = 2)
                    Buffers: shared hit=200
        ->  Index Scan using "IX_WorkSchedules_UserId_OrganizationId_ValidFrom" on "WorkSchedules" w  (cost=0.28..8.29 rows=1 width=92) (actual time=0.001..0.003 rows=4 loops=100)
              Index Cond: (("UserId" = u."UserId") AND ("OrganizationId" = 2))
              Buffers: shared hit=304
        SubPlan 2
          ->  Limit  (cost=2.60..10.65 rows=1 width=4) (actual time=0.005..0.005 rows=1 loops=365)
                Buffers: shared hit=1097
                ->  Index Scan Backward using "IX_BalanceCheckpoints_OrganizationId_UserId_Month" on "BalanceCheckpoints" b  (cost=2.60..10.65 rows=1 width=4) (actual time=0.004..0.004 rows=1 loops=365)
                      Index Cond: (("OrganizationId" = 2) AND ("UserId" = u."UserId") AND ("Month" < '2026-09-01'::date))
                      Filter: (("Algorithm" = 1) AND ("Version" = u."BalanceVersion") AND ("OrgVersion" = o."BalanceVersion") AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 1)))
                      Buffers: shared hit=1097
                      SubPlan 1
                        ->  Seq Scan on "UserOrganizations" uo  (cost=0.00..2.31 rows=1 width=4) (actual time=0.006..0.032 rows=1 loops=1)
                              Filter: ("IsActive" AND ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer))
                              Rows Removed by Filter: 113
                              Buffers: shared hit=2
        SubPlan 4
          ->  Limit  (cost=2.60..10.65 rows=1 width=12) (actual time=0.004..0.004 rows=1 loops=365)
                Buffers: shared hit=1097
                ->  Index Scan Backward using "IX_BalanceCheckpoints_OrganizationId_UserId_Month" on "BalanceCheckpoints" b0  (cost=2.60..10.65 rows=1 width=12) (actual time=0.004..0.004 rows=1 loops=365)
                      Index Cond: (("OrganizationId" = 2) AND ("UserId" = u."UserId") AND ("Month" < '2026-09-01'::date))
                      Filter: (("Algorithm" = 1) AND ("Version" = u."BalanceVersion") AND ("OrgVersion" = o."BalanceVersion") AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 3)))
                      Buffers: shared hit=1097
                      SubPlan 3
                        ->  Seq Scan on "UserOrganizations" uo_1  (cost=0.00..2.31 rows=1 width=4) (actual time=0.006..0.030 rows=1 loops=1)
                              Filter: ("IsActive" AND ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer))
                              Rows Removed by Filter: 113
                              Buffers: shared hit=2
        SubPlan 6
          ->  Limit  (cost=2.60..10.65 rows=1 width=8) (actual time=0.004..0.004 rows=1 loops=365)
                Buffers: shared hit=1097
                ->  Index Scan Backward using "IX_BalanceCheckpoints_OrganizationId_UserId_Month" on "BalanceCheckpoints" b1  (cost=2.60..10.65 rows=1 width=8) (actual time=0.004..0.004 rows=1 loops=365)
                      Index Cond: (("OrganizationId" = 2) AND ("UserId" = u."UserId") AND ("Month" < '2026-09-01'::date))
                      Filter: (("Algorithm" = 1) AND ("Version" = u."BalanceVersion") AND ("OrgVersion" = o."BalanceVersion") AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 5)))
                      Buffers: shared hit=1097
                      SubPlan 5
                        ->  Seq Scan on "UserOrganizations" uo_2  (cost=0.00..2.31 rows=1 width=4) (actual time=0.006..0.030 rows=1 loops=1)
                              Filter: ("IsActive" AND ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer))
                              Rows Removed by Filter: 113
                              Buffers: shared hit=2
        SubPlan 9
          ->  Result  (cost=10.65..10.66 rows=1 width=4) (actual time=0.005..0.005 rows=1 loops=365)
                Buffers: shared hit=1097
                InitPlan 8 (returns $16)
                  ->  Limit  (cost=2.60..10.65 rows=1 width=4) (actual time=0.004..0.004 rows=1 loops=365)
                        Buffers: shared hit=1097
                        ->  Index Scan Backward using "IX_BalanceCheckpoints_OrganizationId_UserId_Month" on "BalanceCheckpoints" b2  (cost=2.60..10.65 rows=1 width=4) (actual time=0.004..0.004 rows=1 loops=365)
                              Index Cond: (("OrganizationId" = 2) AND ("UserId" = u."UserId") AND ("Month" IS NOT NULL))
                              Filter: (("Algorithm" = 1) AND ("Version" = u."BalanceVersion") AND ("OrgVersion" = o."BalanceVersion") AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 7)))
                              Buffers: shared hit=1097
                              SubPlan 7
                                ->  Seq Scan on "UserOrganizations" uo_3  (cost=0.00..2.31 rows=1 width=4) (actual time=0.006..0.030 rows=1 loops=1)
                                      Filter: ("IsActive" AND ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer))
                                      Rows Removed by Filter: 113
                                      Buffers: shared hit=2
        SubPlan 12
          ->  Result  (cost=5.42..5.43 rows=1 width=8) (actual time=0.005..0.005 rows=1 loops=365)
                Buffers: shared hit=1462
                InitPlan 11 (returns $19)
                  ->  Limit  (cost=2.74..5.42 rows=1 width=8) (actual time=0.005..0.005 rows=1 loops=365)
                        Buffers: shared hit=1462
                        ->  Index Scan using "IX_TimeEntries_UserId_StartTime" on "TimeEntries" t  (cost=2.74..3057.11 rows=1137 width=8) (actual time=0.004..0.004 rows=1 loops=365)
                              Index Cond: (("UserId" = u."UserId") AND ("StartTime" IS NOT NULL))
                              Filter: ((NOT "IsRunning") AND ("OrganizationId" = 2) AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 10)))
                              Buffers: shared hit=1462
                              SubPlan 10
                                ->  Seq Scan on "UserOrganizations" uo_4  (cost=0.00..2.31 rows=1 width=4) (actual time=0.005..0.030 rows=1 loops=1)
                                      Filter: ("IsActive" AND ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer))
                                      Rows Removed by Filter: 113
                                      Buffers: shared hit=2
        SubPlan 14
          ->  Aggregate  (cost=10.50..10.51 rows=1 width=4) (actual time=0.001..0.001 rows=1 loops=365)
                Buffers: shared hit=463
                ->  Index Scan using "IX_TimeEntries_UserId_Failure" on "TimeEntries" t0  (cost=2.46..10.50 rows=1 width=0) (actual time=0.001..0.001 rows=0 loops=365)
                      Index Cond: ("UserId" = u."UserId")
                      Filter: (("StartTime" < '1900-01-01 00:00:00+00'::timestamp with time zone) AND ("OrganizationId" = 2) AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 13)))
                      Rows Removed by Filter: 0
                      Buffers: shared hit=463
                      SubPlan 13
                        ->  Seq Scan on "UserOrganizations" uo_5  (cost=0.00..2.31 rows=1 width=4) (never executed)
                              Filter: ("IsActive" AND ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer))
Planning Time: 1.050 ms
Execution Time: 10.826 ms
```

6.5 ms in the call:

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
HashAggregate  (cost=3664.90..3666.90 rows=200 width=4) (actual time=4.223..4.236 rows=100 loops=1)
  Group Key: t."UserId"
  Batches: 1  Memory Usage: 40kB
  Buffers: shared hit=2361
  ->  HashAggregate  (cost=3654.56..3662.83 rows=827 width=4) (actual time=4.189..4.205 rows=100 loops=1)
        Group Key: t."UserId"
        Batches: 1  Memory Usage: 49kB
        Buffers: shared hit=2361
        ->  Append  (cost=2.88..3652.49 rows=827 width=4) (actual time=0.083..3.802 rows=2671 loops=1)
              Buffers: shared hit=2361
              ->  Nested Loop Left Join  (cost=2.88..3254.40 rows=613 width=4) (actual time=0.083..3.137 rows=2265 loops=1)
                    Filter: ((t."OrganizationId" IS NULL) OR o."IsActive")
                    Buffers: shared hit=2280
                    ->  Index Scan using "IX_TimeEntries_OrganizationId_StartTime" on "TimeEntries" t  (cost=2.74..3230.91 rows=1226 width=8) (actual time=0.068..2.520 rows=2265 loops=1)
                          Index Cond: (("OrganizationId" = 2) AND ("StartTime" >= '2026-08-31 22:00:00+00'::timestamp with time zone) AND ("StartTime" < '2026-09-30 22:00:00+00'::timestamp with time zone))
                          Filter: ((NOT "IsRunning") AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 1)))
                          Buffers: shared hit=2278
                          SubPlan 1
                            ->  Seq Scan on "UserOrganizations" uo  (cost=0.00..2.31 rows=1 width=4) (actual time=0.008..0.034 rows=1 loops=1)
                                  Filter: ("IsActive" AND ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer))
                                  Rows Removed by Filter: 113
                                  Buffers: shared hit=2
                    ->  Materialize  (cost=0.14..8.17 rows=1 width=5) (actual time=0.000..0.000 rows=1 loops=2265)
                          Buffers: shared hit=2
                          ->  Index Scan using "PK_Organizations" on "Organizations" o  (cost=0.14..8.16 rows=1 width=5) (actual time=0.009..0.009 rows=1 loops=1)
                                Index Cond: ("Id" = 2)
                                Filter: "IsActive"
                                Buffers: shared hit=2
              ->  Nested Loop  (cost=12.08..393.96 rows=214 width=4) (actual time=0.109..0.474 rows=406 loops=1)
                    Buffers: shared hit=81
                    ->  Index Scan using "PK_Organizations" on "Organizations" o1  (cost=0.14..8.16 rows=1 width=4) (actual time=0.018..0.019 rows=1 loops=1)
                          Index Cond: ("Id" = 2)
                          Filter: ("IsActive" AND "IsActive")
                          Buffers: shared hit=2
                    ->  Bitmap Heap Scan on "AbsenceDays" a  (cost=11.94..383.66 rows=214 width=8) (actual time=0.085..0.416 rows=406 loops=1)
                          Recheck Cond: (("OrganizationId" = 2) AND ("Date" >= '2026-09-01'::date) AND ("Date" <= '2026-09-30'::date))
                          Filter: ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 2))
                          Heap Blocks: exact=75
                          Buffers: shared hit=79
                          ->  Bitmap Index Scan on "IX_AbsenceDays_OrganizationId_Date"  (cost=0.00..9.56 rows=422 width=0) (actual time=0.029..0.029 rows=406 loops=1)
                                Index Cond: (("OrganizationId" = 2) AND ("Date" >= '2026-09-01'::date) AND ("Date" <= '2026-09-30'::date))
                                Buffers: shared hit=2
                          SubPlan 2
                            ->  Seq Scan on "UserOrganizations" uo_1  (cost=0.00..2.31 rows=1 width=4) (actual time=0.008..0.034 rows=1 loops=1)
                                  Filter: ("IsActive" AND ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer))
                                  Rows Removed by Filter: 113
                                  Buffers: shared hit=2
Planning Time: 0.533 ms
Execution Time: 4.416 ms
```

3.0 ms in the call:

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
Nested Loop Left Join  (cost=82.05..3321.76 rows=611 width=26) (actual time=0.211..2.736 rows=2265 loops=1)
  Filter: ((t."OrganizationId" IS NULL) OR o."IsActive")
  Buffers: shared hit=138
  ->  Bitmap Heap Scan on "TimeEntries" t  (cost=81.90..3298.32 rows=1222 width=30) (actual time=0.192..2.128 rows=2265 loops=1)
        Recheck Cond: ((("OrganizationId" = 2) AND ("StartTime" >= '2026-08-31 22:00:00+00'::timestamp with time zone) AND ("StartTime" < '2026-09-30 22:00:00+00'::timestamp with time zone)) OR ("UserId" = ANY ('{}'::integer[])))
        Filter: ((((NOT "IsRunning") AND ("UserId" = ANY ('{15,16,17,18,19,20,21,22,23,24,25,26,27,28,29,30,31,32,33,34,35,36,37,38,39,40,41,42,43,44,45,46,47,48,49,50,51,52,53,54,55,56,57,58,59,60,61,62,63,64,65,66,67,68,69,70,71,72,73,74,75,76,77,78,79,80,81,82,83,84,85,86,87,88,89,90,91,92,93,94,95,96,97,98,99,100,101,102,103,104,105,106,107,108,109,110,111,112,113,114}'::integer[])) AND ("StartTime" >= '2026-08-31 22:00:00+00'::timestamp with time zone) AND ("StartTime" < '2026-09-30 22:00:00+00'::timestamp with time zone)) OR ("IsRunning" AND ("UserId" = ANY ('{}'::integer[])) AND ("StartTime" < '2026-10-19 22:00:00+00'::timestamp with time zone))) AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 1)))
        Heap Blocks: exact=123
        Buffers: shared hit=136
        ->  BitmapOr  (cost=79.34..79.34 rows=2413 width=0) (actual time=0.140..0.142 rows=0 loops=1)
              Buffers: shared hit=11
              ->  Bitmap Index Scan on "IX_TimeEntries_OrganizationId_StartTime"  (cost=0.00..74.58 rows=2413 width=0) (actual time=0.139..0.140 rows=2265 loops=1)
                    Index Cond: (("OrganizationId" = 2) AND ("StartTime" >= '2026-08-31 22:00:00+00'::timestamp with time zone) AND ("StartTime" < '2026-09-30 22:00:00+00'::timestamp with time zone))
                    Buffers: shared hit=11
              ->  Bitmap Index Scan on "IX_TimeEntries_UserId"  (cost=0.00..4.14 rows=1 width=0) (actual time=0.001..0.001 rows=0 loops=1)
                    Index Cond: ("UserId" = ANY ('{}'::integer[]))
        SubPlan 1
          ->  Seq Scan on "UserOrganizations" uo  (cost=0.00..2.31 rows=1 width=4) (actual time=0.008..0.034 rows=1 loops=1)
                Filter: ("IsActive" AND ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer))
                Rows Removed by Filter: 113
                Buffers: shared hit=2
  ->  Materialize  (cost=0.14..8.17 rows=1 width=5) (actual time=0.000..0.000 rows=1 loops=2265)
        Buffers: shared hit=2
        ->  Index Scan using "PK_Organizations" on "Organizations" o  (cost=0.14..8.16 rows=1 width=5) (actual time=0.013..0.013 rows=1 loops=1)
              Index Cond: ("Id" = 2)
              Filter: "IsActive"
              Buffers: shared hit=2
Planning Time: 0.407 ms
Execution Time: 2.935 ms
```

### balance — p50 53.3 ms, 22 SQL

3.1 ms in the call:

```sql
SELECT u."UserId", u."BalanceVersion", o."BalanceVersion", o."TimeZone", (
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
Sort  (cost=77.25..77.25 rows=1 width=290) (actual time=0.221..0.224 rows=4 loops=1)
  Sort Key: u."Id", w."ValidFrom"
  Sort Method: quicksort  Memory: 25kB
  Buffers: shared hit=75
  ->  Nested Loop Left Join  (cost=0.42..77.24 rows=1 width=290) (actual time=0.134..0.208 rows=4 loops=1)
        Buffers: shared hit=75
        ->  Nested Loop  (cost=0.14..10.36 rows=1 width=166) (actual time=0.029..0.039 rows=1 loops=1)
              Buffers: shared hit=4
              ->  Seq Scan on "UserOrganizations" u  (cost=0.00..2.19 rows=1 width=16) (actual time=0.013..0.022 rows=1 loops=1)
                    Filter: (("UserId" = ANY ('{21}'::integer[])) AND ("OrganizationId" = 2))
                    Rows Removed by Filter: 113
                    Buffers: shared hit=2
              ->  Index Scan using "PK_Organizations" on "Organizations" o  (cost=0.14..8.16 rows=1 width=154) (actual time=0.015..0.015 rows=1 loops=1)
                    Index Cond: ("Id" = 2)
                    Buffers: shared hit=2
        ->  Index Scan using "IX_WorkSchedules_UserId_OrganizationId_ValidFrom" on "WorkSchedules" w  (cost=0.28..8.29 rows=1 width=92) (actual time=0.012..0.014 rows=4 loops=1)
              Index Cond: (("UserId" = u."UserId") AND ("OrganizationId" = 2))
              Buffers: shared hit=3
        SubPlan 2
          ->  Limit  (cost=2.60..10.65 rows=1 width=4) (actual time=0.007..0.007 rows=1 loops=4)
                Buffers: shared hit=12
                ->  Index Scan Backward using "IX_BalanceCheckpoints_OrganizationId_UserId_Month" on "BalanceCheckpoints" b  (cost=2.60..10.65 rows=1 width=4) (actual time=0.007..0.007 rows=1 loops=4)
                      Index Cond: (("OrganizationId" = 2) AND ("UserId" = u."UserId") AND ("Month" < '2026-10-01'::date))
                      Filter: (("Algorithm" = 1) AND ("Version" = u."BalanceVersion") AND ("OrgVersion" = o."BalanceVersion") AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 1)))
                      Buffers: shared hit=12
                      SubPlan 1
                        ->  Seq Scan on "UserOrganizations" uo  (cost=0.00..2.31 rows=1 width=4) (never executed)
                              Filter: ("IsActive" AND ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer))
        SubPlan 4
          ->  Limit  (cost=2.60..10.65 rows=1 width=12) (actual time=0.004..0.004 rows=1 loops=4)
                Buffers: shared hit=12
                ->  Index Scan Backward using "IX_BalanceCheckpoints_OrganizationId_UserId_Month" on "BalanceCheckpoints" b0  (cost=2.60..10.65 rows=1 width=12) (actual time=0.004..0.004 rows=1 loops=4)
                      Index Cond: (("OrganizationId" = 2) AND ("UserId" = u."UserId") AND ("Month" < '2026-10-01'::date))
                      Filter: (("Algorithm" = 1) AND ("Version" = u."BalanceVersion") AND ("OrgVersion" = o."BalanceVersion") AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 3)))
                      Buffers: shared hit=12
                      SubPlan 3
                        ->  Seq Scan on "UserOrganizations" uo_1  (cost=0.00..2.31 rows=1 width=4) (never executed)
                              Filter: ("IsActive" AND ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer))
        SubPlan 6
          ->  Limit  (cost=2.60..10.65 rows=1 width=8) (actual time=0.004..0.004 rows=1 loops=4)
                Buffers: shared hit=12
                ->  Index Scan Backward using "IX_BalanceCheckpoints_OrganizationId_UserId_Month" on "BalanceCheckpoints" b1  (cost=2.60..10.65 rows=1 width=8) (actual time=0.004..0.004 rows=1 loops=4)
                      Index Cond: (("OrganizationId" = 2) AND ("UserId" = u."UserId") AND ("Month" < '2026-10-01'::date))
                      Filter: (("Algorithm" = 1) AND ("Version" = u."BalanceVersion") AND ("OrgVersion" = o."BalanceVersion") AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 5)))
                      Buffers: shared hit=12
                      SubPlan 5
                        ->  Seq Scan on "UserOrganizations" uo_2  (cost=0.00..2.31 rows=1 width=4) (never executed)
                              Filter: ("IsActive" AND ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer))
        SubPlan 9
          ->  Result  (cost=10.65..10.66 rows=1 width=4) (actual time=0.005..0.005 rows=1 loops=4)
                Buffers: shared hit=12
                InitPlan 8 (returns $16)
                  ->  Limit  (cost=2.60..10.65 rows=1 width=4) (actual time=0.004..0.004 rows=1 loops=4)
                        Buffers: shared hit=12
                        ->  Index Scan Backward using "IX_BalanceCheckpoints_OrganizationId_UserId_Month" on "BalanceCheckpoints" b2  (cost=2.60..10.65 rows=1 width=4) (actual time=0.004..0.004 rows=1 loops=4)
                              Index Cond: (("OrganizationId" = 2) AND ("UserId" = u."UserId") AND ("Month" IS NOT NULL))
                              Filter: (("Algorithm" = 1) AND ("Version" = u."BalanceVersion") AND ("OrgVersion" = o."BalanceVersion") AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 7)))
                              Buffers: shared hit=12
                              SubPlan 7
                                ->  Seq Scan on "UserOrganizations" uo_3  (cost=0.00..2.31 rows=1 width=4) (never executed)
                                      Filter: ("IsActive" AND ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer))
        SubPlan 12
          ->  Result  (cost=5.43..5.44 rows=1 width=8) (actual time=0.009..0.009 rows=1 loops=4)
                Buffers: shared hit=16
                InitPlan 11 (returns $19)
                  ->  Limit  (cost=2.74..5.43 rows=1 width=8) (actual time=0.009..0.009 rows=1 loops=4)
                        Buffers: shared hit=16
                        ->  Index Scan using "IX_TimeEntries_UserId_StartTime" on "TimeEntries" t  (cost=2.74..3057.11 rows=1136 width=8) (actual time=0.008..0.008 rows=1 loops=4)
                              Index Cond: (("UserId" = u."UserId") AND ("StartTime" IS NOT NULL))
                              Filter: ((NOT "IsRunning") AND ("OrganizationId" = 2) AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 10)))
                              Buffers: shared hit=16
                              SubPlan 10
                                ->  Seq Scan on "UserOrganizations" uo_4  (cost=0.00..2.31 rows=1 width=4) (never executed)
                                      Filter: ("IsActive" AND ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer))
        SubPlan 14
          ->  Aggregate  (cost=10.50..10.51 rows=1 width=4) (actual time=0.005..0.005 rows=1 loops=4)
                Buffers: shared hit=4
                ->  Index Scan using "IX_TimeEntries_UserId_Failure" on "TimeEntries" t0  (cost=2.46..10.50 rows=1 width=0) (actual time=0.004..0.004 rows=0 loops=4)
                      Index Cond: ("UserId" = u."UserId")
                      Filter: (("StartTime" < '1900-01-01 00:00:00+00'::timestamp with time zone) AND ("OrganizationId" = 2) AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 13)))
                      Buffers: shared hit=4
                      SubPlan 13
                        ->  Seq Scan on "UserOrganizations" uo_5  (cost=0.00..2.31 rows=1 width=4) (never executed)
                              Filter: ("IsActive" AND ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer))
Planning Time: 1.114 ms
Execution Time: 0.537 ms
```

1.6 ms in the call:

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
Sort  (cost=31.00..31.03 rows=12 width=609) (actual time=0.207..0.212 rows=110 loops=1)
  Sort Key: g."Id"
  Sort Method: quicksort  Memory: 38kB
  Buffers: shared hit=82
  ->  Nested Loop Left Join  (cost=4.50..30.78 rows=12 width=609) (actual time=0.041..0.148 rows=110 loops=1)
        Buffers: shared hit=82
        ->  Nested Loop  (cost=0.29..16.33 rows=1 width=585) (actual time=0.030..0.062 rows=20 loops=1)
              Buffers: shared hit=42
              ->  Index Scan using "IX_Groups_OrganizationId" on "Groups" g  (cost=0.14..8.16 rows=1 width=581) (actual time=0.019..0.029 rows=20 loops=1)
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
Planning Time: 0.234 ms
Execution Time: 0.305 ms
```

1.5 ms in the call:

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
Nested Loop Left Join  (cost=2.88..44.17 rows=4 width=94) (actual time=0.055..0.140 rows=52 loops=1)
  Filter: ((t."OrganizationId" IS NULL) OR o."IsActive")
  Buffers: shared hit=8
  ->  Index Scan using "IX_TimeEntries_UserId_StartTime" on "TimeEntries" t  (cost=2.74..35.91 rows=8 width=94) (actual time=0.035..0.098 rows=52 loops=1)
        Index Cond: (("UserId" = 21) AND ("StartTime" >= '2026-09-30 22:00:00+00'::timestamp with time zone) AND ("StartTime" < '2026-10-31 23:00:00+00'::timestamp with time zone))
        Filter: ((NOT "IsRunning") AND ("OrganizationId" = 2) AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 1)))
        Buffers: shared hit=6
        SubPlan 1
          ->  Seq Scan on "UserOrganizations" uo  (cost=0.00..2.31 rows=1 width=4) (never executed)
                Filter: ("IsActive" AND ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer))
  ->  Materialize  (cost=0.14..8.17 rows=1 width=5) (actual time=0.000..0.000 rows=1 loops=52)
        Buffers: shared hit=2
        ->  Index Scan using "PK_Organizations" on "Organizations" o  (cost=0.14..8.16 rows=1 width=5) (actual time=0.013..0.013 rows=1 loops=1)
              Index Cond: ("Id" = 2)
              Filter: "IsActive"
              Buffers: shared hit=2
Planning Time: 0.365 ms
Execution Time: 0.227 ms
```

### monthly PDF — p50 51.2 ms, 11 SQL

3.5 ms in the call:

```sql
SELECT u."UserId", u."BalanceVersion", o."BalanceVersion", o."TimeZone", (
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
Sort  (cost=77.24..77.25 rows=1 width=290) (actual time=0.461..0.464 rows=4 loops=1)
  Sort Key: u."Id", w."ValidFrom"
  Sort Method: quicksort  Memory: 25kB
  Buffers: shared hit=85
  ->  Nested Loop Left Join  (cost=0.42..77.23 rows=1 width=290) (actual time=0.353..0.448 rows=4 loops=1)
        Buffers: shared hit=85
        ->  Nested Loop  (cost=0.14..10.36 rows=1 width=166) (actual time=0.032..0.042 rows=1 loops=1)
              Buffers: shared hit=4
              ->  Seq Scan on "UserOrganizations" u  (cost=0.00..2.19 rows=1 width=16) (actual time=0.015..0.024 rows=1 loops=1)
                    Filter: (("UserId" = ANY ('{21}'::integer[])) AND ("OrganizationId" = 2))
                    Rows Removed by Filter: 113
                    Buffers: shared hit=2
              ->  Index Scan using "PK_Organizations" on "Organizations" o  (cost=0.14..8.16 rows=1 width=154) (actual time=0.016..0.016 rows=1 loops=1)
                    Index Cond: ("Id" = 2)
                    Buffers: shared hit=2
        ->  Index Scan using "IX_WorkSchedules_UserId_OrganizationId_ValidFrom" on "WorkSchedules" w  (cost=0.28..8.29 rows=1 width=92) (actual time=0.015..0.017 rows=4 loops=1)
              Index Cond: (("UserId" = u."UserId") AND ("OrganizationId" = 2))
              Buffers: shared hit=3
        SubPlan 2
          ->  Limit  (cost=2.60..10.65 rows=1 width=4) (actual time=0.018..0.018 rows=1 loops=4)
                Buffers: shared hit=14
                ->  Index Scan Backward using "IX_BalanceCheckpoints_OrganizationId_UserId_Month" on "BalanceCheckpoints" b  (cost=2.60..10.65 rows=1 width=4) (actual time=0.018..0.018 rows=1 loops=4)
                      Index Cond: (("OrganizationId" = 2) AND ("UserId" = u."UserId") AND ("Month" < '2026-09-01'::date))
                      Filter: (("Algorithm" = 1) AND ("Version" = u."BalanceVersion") AND ("OrgVersion" = o."BalanceVersion") AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 1)))
                      Buffers: shared hit=14
                      SubPlan 1
                        ->  Seq Scan on "UserOrganizations" uo  (cost=0.00..2.31 rows=1 width=4) (actual time=0.007..0.037 rows=1 loops=1)
                              Filter: ("IsActive" AND ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer))
                              Rows Removed by Filter: 113
                              Buffers: shared hit=2
        SubPlan 4
          ->  Limit  (cost=2.60..10.65 rows=1 width=12) (actual time=0.015..0.015 rows=1 loops=4)
                Buffers: shared hit=14
                ->  Index Scan Backward using "IX_BalanceCheckpoints_OrganizationId_UserId_Month" on "BalanceCheckpoints" b0  (cost=2.60..10.65 rows=1 width=12) (actual time=0.015..0.015 rows=1 loops=4)
                      Index Cond: (("OrganizationId" = 2) AND ("UserId" = u."UserId") AND ("Month" < '2026-09-01'::date))
                      Filter: (("Algorithm" = 1) AND ("Version" = u."BalanceVersion") AND ("OrgVersion" = o."BalanceVersion") AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 3)))
                      Buffers: shared hit=14
                      SubPlan 3
                        ->  Seq Scan on "UserOrganizations" uo_1  (cost=0.00..2.31 rows=1 width=4) (actual time=0.009..0.035 rows=1 loops=1)
                              Filter: ("IsActive" AND ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer))
                              Rows Removed by Filter: 113
                              Buffers: shared hit=2
        SubPlan 6
          ->  Limit  (cost=2.60..10.65 rows=1 width=8) (actual time=0.015..0.015 rows=1 loops=4)
                Buffers: shared hit=14
                ->  Index Scan Backward using "IX_BalanceCheckpoints_OrganizationId_UserId_Month" on "BalanceCheckpoints" b1  (cost=2.60..10.65 rows=1 width=8) (actual time=0.015..0.015 rows=1 loops=4)
                      Index Cond: (("OrganizationId" = 2) AND ("UserId" = u."UserId") AND ("Month" < '2026-09-01'::date))
                      Filter: (("Algorithm" = 1) AND ("Version" = u."BalanceVersion") AND ("OrgVersion" = o."BalanceVersion") AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 5)))
                      Buffers: shared hit=14
                      SubPlan 5
                        ->  Seq Scan on "UserOrganizations" uo_2  (cost=0.00..2.31 rows=1 width=4) (actual time=0.005..0.039 rows=1 loops=1)
                              Filter: ("IsActive" AND ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer))
                              Rows Removed by Filter: 113
                              Buffers: shared hit=2
        SubPlan 9
          ->  Result  (cost=10.65..10.66 rows=1 width=4) (actual time=0.016..0.016 rows=1 loops=4)
                Buffers: shared hit=14
                InitPlan 8 (returns $16)
                  ->  Limit  (cost=2.60..10.65 rows=1 width=4) (actual time=0.015..0.015 rows=1 loops=4)
                        Buffers: shared hit=14
                        ->  Index Scan Backward using "IX_BalanceCheckpoints_OrganizationId_UserId_Month" on "BalanceCheckpoints" b2  (cost=2.60..10.65 rows=1 width=4) (actual time=0.014..0.014 rows=1 loops=4)
                              Index Cond: (("OrganizationId" = 2) AND ("UserId" = u."UserId") AND ("Month" IS NOT NULL))
                              Filter: (("Algorithm" = 1) AND ("Version" = u."BalanceVersion") AND ("OrgVersion" = o."BalanceVersion") AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 7)))
                              Buffers: shared hit=14
                              SubPlan 7
                                ->  Seq Scan on "UserOrganizations" uo_3  (cost=0.00..2.31 rows=1 width=4) (actual time=0.006..0.030 rows=1 loops=1)
                                      Filter: ("IsActive" AND ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer))
                                      Rows Removed by Filter: 113
                                      Buffers: shared hit=2
        SubPlan 12
          ->  Result  (cost=5.42..5.43 rows=1 width=8) (actual time=0.022..0.022 rows=1 loops=4)
                Buffers: shared hit=18
                InitPlan 11 (returns $19)
                  ->  Limit  (cost=2.74..5.42 rows=1 width=8) (actual time=0.021..0.021 rows=1 loops=4)
                        Buffers: shared hit=18
                        ->  Index Scan using "IX_TimeEntries_UserId_StartTime" on "TimeEntries" t  (cost=2.74..3057.11 rows=1137 width=8) (actual time=0.020..0.020 rows=1 loops=4)
                              Index Cond: (("UserId" = u."UserId") AND ("StartTime" IS NOT NULL))
                              Filter: ((NOT "IsRunning") AND ("OrganizationId" = 2) AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 10)))
                              Buffers: shared hit=18
                              SubPlan 10
                                ->  Seq Scan on "UserOrganizations" uo_4  (cost=0.00..2.31 rows=1 width=4) (actual time=0.008..0.048 rows=1 loops=1)
                                      Filter: ("IsActive" AND ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer))
                                      Rows Removed by Filter: 113
                                      Buffers: shared hit=2
        SubPlan 14
          ->  Aggregate  (cost=10.50..10.51 rows=1 width=4) (actual time=0.003..0.003 rows=1 loops=4)
                Buffers: shared hit=4
                ->  Index Scan using "IX_TimeEntries_UserId_Failure" on "TimeEntries" t0  (cost=2.46..10.50 rows=1 width=0) (actual time=0.002..0.002 rows=0 loops=4)
                      Index Cond: ("UserId" = u."UserId")
                      Filter: (("StartTime" < '1900-01-01 00:00:00+00'::timestamp with time zone) AND ("OrganizationId" = 2) AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 13)))
                      Buffers: shared hit=4
                      SubPlan 13
                        ->  Seq Scan on "UserOrganizations" uo_5  (cost=0.00..2.31 rows=1 width=4) (never executed)
                              Filter: ("IsActive" AND ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer))
Planning Time: 1.257 ms
Execution Time: 0.772 ms
```

1.9 ms in the call:

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
Sort  (cost=31.00..31.03 rows=12 width=609) (actual time=0.269..0.276 rows=110 loops=1)
  Sort Key: g."Id"
  Sort Method: quicksort  Memory: 38kB
  Buffers: shared hit=82
  ->  Nested Loop Left Join  (cost=4.50..30.78 rows=12 width=609) (actual time=0.091..0.208 rows=110 loops=1)
        Buffers: shared hit=82
        ->  Nested Loop  (cost=0.29..16.33 rows=1 width=585) (actual time=0.081..0.116 rows=20 loops=1)
              Buffers: shared hit=42
              ->  Index Scan using "IX_Groups_OrganizationId" on "Groups" g  (cost=0.14..8.16 rows=1 width=581) (actual time=0.063..0.074 rows=20 loops=1)
                    Index Cond: ("OrganizationId" = 2)
                    Filter: ("IsActive" AND "IsActive")
                    Buffers: shared hit=2
              ->  Index Scan using "PK_Organizations" on "Organizations" o  (cost=0.14..8.16 rows=1 width=4) (actual time=0.001..0.002 rows=1 loops=20)
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
Planning Time: 0.252 ms
Execution Time: 0.394 ms
```

1.8 ms in the call:

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
Limit  (cost=0.28..18.51 rows=1 width=7) (actual time=0.042..0.043 rows=1 loops=1)
  Buffers: shared hit=5
  ->  Nested Loop  (cost=0.28..18.51 rows=1 width=7) (actual time=0.041..0.042 rows=1 loops=1)
        Buffers: shared hit=5
        ->  Nested Loop  (cost=0.14..10.34 rows=1 width=10) (actual time=0.031..0.032 rows=1 loops=1)
              Join Filter: (u."OrganizationId" = o."Id")
              Buffers: shared hit=3
              ->  Seq Scan on "UserOrganizations" u  (cost=0.00..2.17 rows=1 width=13) (actual time=0.011..0.012 rows=1 loops=1)
                    Filter: ("IsActive" AND ("UserId" = 15))
                    Rows Removed by Filter: 14
                    Buffers: shared hit=1
              ->  Index Scan using "IX_Organizations_Slug" on "Organizations" o  (cost=0.14..8.16 rows=1 width=5) (actual time=0.018..0.018 rows=1 loops=1)
                    Index Cond: (("Slug")::text = 'perf-100-10'::text)
                    Buffers: shared hit=2
        ->  Index Scan using "PK_Users" on "Users" u0  (cost=0.14..8.16 rows=1 width=5) (actual time=0.009..0.009 rows=1 loops=1)
              Index Cond: ("Id" = 15)
              Buffers: shared hit=2
Planning Time: 0.195 ms
Execution Time: 0.085 ms
```

### daily CSV month — p50 48.6 ms, 18 SQL

3.1 ms in the call:

```sql
SELECT u."UserId", u."BalanceVersion", o."BalanceVersion", o."TimeZone", (
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
Sort  (cost=77.24..77.25 rows=1 width=290) (actual time=0.399..0.402 rows=4 loops=1)
  Sort Key: u."Id", w."ValidFrom"
  Sort Method: quicksort  Memory: 25kB
  Buffers: shared hit=85
  ->  Nested Loop Left Join  (cost=0.42..77.23 rows=1 width=290) (actual time=0.309..0.385 rows=4 loops=1)
        Buffers: shared hit=85
        ->  Nested Loop  (cost=0.14..10.36 rows=1 width=166) (actual time=0.026..0.036 rows=1 loops=1)
              Buffers: shared hit=4
              ->  Seq Scan on "UserOrganizations" u  (cost=0.00..2.19 rows=1 width=16) (actual time=0.012..0.020 rows=1 loops=1)
                    Filter: (("UserId" = ANY ('{21}'::integer[])) AND ("OrganizationId" = 2))
                    Rows Removed by Filter: 113
                    Buffers: shared hit=2
              ->  Index Scan using "PK_Organizations" on "Organizations" o  (cost=0.14..8.16 rows=1 width=154) (actual time=0.013..0.014 rows=1 loops=1)
                    Index Cond: ("Id" = 2)
                    Buffers: shared hit=2
        ->  Index Scan using "IX_WorkSchedules_UserId_OrganizationId_ValidFrom" on "WorkSchedules" w  (cost=0.28..8.29 rows=1 width=92) (actual time=0.013..0.015 rows=4 loops=1)
              Index Cond: (("UserId" = u."UserId") AND ("OrganizationId" = 2))
              Buffers: shared hit=3
        SubPlan 2
          ->  Limit  (cost=2.60..10.65 rows=1 width=4) (actual time=0.016..0.016 rows=1 loops=4)
                Buffers: shared hit=14
                ->  Index Scan Backward using "IX_BalanceCheckpoints_OrganizationId_UserId_Month" on "BalanceCheckpoints" b  (cost=2.60..10.65 rows=1 width=4) (actual time=0.016..0.016 rows=1 loops=4)
                      Index Cond: (("OrganizationId" = 2) AND ("UserId" = u."UserId") AND ("Month" < '2026-09-01'::date))
                      Filter: (("Algorithm" = 1) AND ("Version" = u."BalanceVersion") AND ("OrgVersion" = o."BalanceVersion") AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 1)))
                      Buffers: shared hit=14
                      SubPlan 1
                        ->  Seq Scan on "UserOrganizations" uo  (cost=0.00..2.31 rows=1 width=4) (actual time=0.006..0.032 rows=1 loops=1)
                              Filter: ("IsActive" AND ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer))
                              Rows Removed by Filter: 113
                              Buffers: shared hit=2
        SubPlan 4
          ->  Limit  (cost=2.60..10.65 rows=1 width=12) (actual time=0.015..0.015 rows=1 loops=4)
                Buffers: shared hit=14
                ->  Index Scan Backward using "IX_BalanceCheckpoints_OrganizationId_UserId_Month" on "BalanceCheckpoints" b0  (cost=2.60..10.65 rows=1 width=12) (actual time=0.015..0.015 rows=1 loops=4)
                      Index Cond: (("OrganizationId" = 2) AND ("UserId" = u."UserId") AND ("Month" < '2026-09-01'::date))
                      Filter: (("Algorithm" = 1) AND ("Version" = u."BalanceVersion") AND ("OrgVersion" = o."BalanceVersion") AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 3)))
                      Buffers: shared hit=14
                      SubPlan 3
                        ->  Seq Scan on "UserOrganizations" uo_1  (cost=0.00..2.31 rows=1 width=4) (actual time=0.006..0.041 rows=1 loops=1)
                              Filter: ("IsActive" AND ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer))
                              Rows Removed by Filter: 113
                              Buffers: shared hit=2
        SubPlan 6
          ->  Limit  (cost=2.60..10.65 rows=1 width=8) (actual time=0.013..0.013 rows=1 loops=4)
                Buffers: shared hit=14
                ->  Index Scan Backward using "IX_BalanceCheckpoints_OrganizationId_UserId_Month" on "BalanceCheckpoints" b1  (cost=2.60..10.65 rows=1 width=8) (actual time=0.013..0.013 rows=1 loops=4)
                      Index Cond: (("OrganizationId" = 2) AND ("UserId" = u."UserId") AND ("Month" < '2026-09-01'::date))
                      Filter: (("Algorithm" = 1) AND ("Version" = u."BalanceVersion") AND ("OrgVersion" = o."BalanceVersion") AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 5)))
                      Buffers: shared hit=14
                      SubPlan 5
                        ->  Seq Scan on "UserOrganizations" uo_2  (cost=0.00..2.31 rows=1 width=4) (actual time=0.006..0.031 rows=1 loops=1)
                              Filter: ("IsActive" AND ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer))
                              Rows Removed by Filter: 113
                              Buffers: shared hit=2
        SubPlan 9
          ->  Result  (cost=10.65..10.66 rows=1 width=4) (actual time=0.013..0.014 rows=1 loops=4)
                Buffers: shared hit=14
                InitPlan 8 (returns $16)
                  ->  Limit  (cost=2.60..10.65 rows=1 width=4) (actual time=0.012..0.012 rows=1 loops=4)
                        Buffers: shared hit=14
                        ->  Index Scan Backward using "IX_BalanceCheckpoints_OrganizationId_UserId_Month" on "BalanceCheckpoints" b2  (cost=2.60..10.65 rows=1 width=4) (actual time=0.012..0.012 rows=1 loops=4)
                              Index Cond: (("OrganizationId" = 2) AND ("UserId" = u."UserId") AND ("Month" IS NOT NULL))
                              Filter: (("Algorithm" = 1) AND ("Version" = u."BalanceVersion") AND ("OrgVersion" = o."BalanceVersion") AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 7)))
                              Buffers: shared hit=14
                              SubPlan 7
                                ->  Seq Scan on "UserOrganizations" uo_3  (cost=0.00..2.31 rows=1 width=4) (actual time=0.005..0.030 rows=1 loops=1)
                                      Filter: ("IsActive" AND ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer))
                                      Rows Removed by Filter: 113
                                      Buffers: shared hit=2
        SubPlan 12
          ->  Result  (cost=5.42..5.43 rows=1 width=8) (actual time=0.017..0.017 rows=1 loops=4)
                Buffers: shared hit=18
                InitPlan 11 (returns $19)
                  ->  Limit  (cost=2.74..5.42 rows=1 width=8) (actual time=0.016..0.016 rows=1 loops=4)
                        Buffers: shared hit=18
                        ->  Index Scan using "IX_TimeEntries_UserId_StartTime" on "TimeEntries" t  (cost=2.74..3057.11 rows=1137 width=8) (actual time=0.016..0.016 rows=1 loops=4)
                              Index Cond: (("UserId" = u."UserId") AND ("StartTime" IS NOT NULL))
                              Filter: ((NOT "IsRunning") AND ("OrganizationId" = 2) AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 10)))
                              Buffers: shared hit=18
                              SubPlan 10
                                ->  Seq Scan on "UserOrganizations" uo_4  (cost=0.00..2.31 rows=1 width=4) (actual time=0.006..0.031 rows=1 loops=1)
                                      Filter: ("IsActive" AND ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer))
                                      Rows Removed by Filter: 113
                                      Buffers: shared hit=2
        SubPlan 14
          ->  Aggregate  (cost=10.50..10.51 rows=1 width=4) (actual time=0.003..0.003 rows=1 loops=4)
                Buffers: shared hit=4
                ->  Index Scan using "IX_TimeEntries_UserId_Failure" on "TimeEntries" t0  (cost=2.46..10.50 rows=1 width=0) (actual time=0.002..0.002 rows=0 loops=4)
                      Index Cond: ("UserId" = u."UserId")
                      Filter: (("StartTime" < '1900-01-01 00:00:00+00'::timestamp with time zone) AND ("OrganizationId" = 2) AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 13)))
                      Buffers: shared hit=4
                      SubPlan 13
                        ->  Seq Scan on "UserOrganizations" uo_5  (cost=0.00..2.31 rows=1 width=4) (never executed)
                              Filter: ("IsActive" AND ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer))
Planning Time: 1.223 ms
Execution Time: 0.718 ms
```

1.8 ms in the call:

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
Nested Loop Left Join  (cost=2.88..65.79 rows=7 width=94) (actual time=0.075..0.105 rows=27 loops=1)
  Filter: ((t."OrganizationId" IS NULL) OR o."IsActive")
  Buffers: shared hit=8
  ->  Index Scan using "IX_TimeEntries_UserId_StartTime" on "TimeEntries" t  (cost=2.74..57.45 rows=14 width=94) (actual time=0.061..0.083 rows=27 loops=1)
        Index Cond: (("UserId" = 21) AND ("StartTime" >= '2026-08-31 22:00:00+00'::timestamp with time zone) AND ("StartTime" < '2026-09-30 22:00:00+00'::timestamp with time zone))
        Filter: ((NOT "IsRunning") AND ("OrganizationId" = 2) AND ((current_setting('app.bypass_rls'::text, true) = 'on'::text) OR ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer) OR (hashed SubPlan 1)))
        Buffers: shared hit=6
        SubPlan 1
          ->  Seq Scan on "UserOrganizations" uo  (cost=0.00..2.31 rows=1 width=4) (actual time=0.007..0.034 rows=1 loops=1)
                Filter: ("IsActive" AND ("UserId" = (NULLIF(current_setting('app.user_id'::text, true), ''::text))::integer))
                Rows Removed by Filter: 113
                Buffers: shared hit=2
  ->  Materialize  (cost=0.14..8.17 rows=1 width=5) (actual time=0.000..0.000 rows=1 loops=27)
        Buffers: shared hit=2
        ->  Index Scan using "PK_Organizations" on "Organizations" o  (cost=0.14..8.16 rows=1 width=5) (actual time=0.009..0.009 rows=1 loops=1)
              Index Cond: ("Id" = 2)
              Filter: "IsActive"
              Buffers: shared hit=2
Planning Time: 0.251 ms
Execution Time: 0.169 ms
```

1.6 ms in the call:

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
Sort  (cost=31.00..31.03 rows=12 width=609) (actual time=0.196..0.202 rows=110 loops=1)
  Sort Key: g."Id"
  Sort Method: quicksort  Memory: 38kB
  Buffers: shared hit=82
  ->  Nested Loop Left Join  (cost=4.50..30.78 rows=12 width=609) (actual time=0.032..0.139 rows=110 loops=1)
        Buffers: shared hit=82
        ->  Nested Loop  (cost=0.29..16.33 rows=1 width=585) (actual time=0.023..0.054 rows=20 loops=1)
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
Planning Time: 0.200 ms
Execution Time: 0.295 ms
```
