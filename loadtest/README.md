# Load tests (k6)

Phase 0.3 of `docs/PERFORMANCE_PLAN.md`: a day in an organization of 100 people with a long history, against a
real deployment (Postgres, row-level security, rate limits). The scaling tests in
`backend/tests/TimeTracking.Api.Tests/Perf/` measure single calls; this measures them under concurrent load.

| file | |
|---|---|
| `scenarios.js` | the scenarios and thresholds |
| `lib/auth.js` | logins (once per user in `setup()`, paced for the login rate limit), request tags |

## Scenarios

| scenario | what | load |
|---|---|---|
| `morning_rush` | users start their timer; every second one by badge (`POST terminal/punch` with an API key and cards created in `setup()`), the others with `POST TimeTracking/start` | ramping arrival rate, ≈ 100 starts in 15 min |
| `polling` | every user polls the inbox (`organizations/notifications`, `organizations/user-notifications`, `organizations/my-requests`, `Notifications?unreadOnly=true`, `TimeTracking/failures`) every 60 s; balance once on "page load" | `USERS` VUs |
| `polling_team` | admins poll `organizations/{slug}/team-overview` every 60 s | 10 VUs (owner + admins, repeated) |
| `browsing` | dashboard / time / history page loads with the calls the frontend makes (`Organizations/mine`, `TimeTracking/current`, org detail, work schedule(s), balance, entries of the week / month, compliance, absence types, effective permissions, absences) | `BROWSE_RATE` page views per minute |
| `month_end` | 2 accounting users (owner, first admin): accounting summary, payroll CSV, vacation overview, monthly PDFs of 10 members | once, starting after 5 min |

Requests are tagged `kind:interactive` or `kind:export`. Thresholds: interactive p95 < 300 ms, exports p95 < 2 s,
failed requests < 0.1 % (setup/teardown calls are `kind:setup` and not counted).

## Run

Never against production data. Use the test instance (`docker-compose.test.yml` layered on the production file, see
its header) or a throw-away Postgres.

1. Seed an organization. The image's entrypoint is the API, so `perf-seed` runs in a one-off backend container,
   migrates, seeds and exits:

   ```sh
   docker compose -f docker-compose.production.yml -f docker-compose.test.yml up -d
   docker compose -f docker-compose.production.yml -f docker-compose.test.yml run --rm --no-deps backend \
     perf-seed --employees 100 --years 10 --core-hours off --force
   ```

   - It prints the target host/database first. Without `--force` it runs only in Development/Testing **and** only on
     a database whose organizations are all `perf-…` or the demo org (`mueller-software`); the test instance runs as
     Production, so `--force` is needed there. It refuses an existing slug.
   - The password of all seeded logins is random and **printed once** (`Password (shown once): …`); pass it to k6 as
     `PASSWORD`. `--password …` sets a fixed one (≥ 8 characters).
   - `--core-hours off` on a shared instance: with core hours on (the default, used by the scaling tests) the
     background job checks the perf members every minute and sends their notices.
   - Other options: `--seed N` (deterministic history), `--org-slug perf-…` (default `perf-<employees>-<years>`),
     `--end-date yyyy-MM-dd`. Logins: `perf-owner@<slug>.test`, `perf-admin1..5@…`, `perf-user1..n@…`.
   - Remove it again (hard delete of the organization, its `perf-*` users and all their rows; `perf-…` slugs only):

     ```sh
     docker compose -f docker-compose.production.yml -f docker-compose.test.yml run --rm --no-deps backend \
       perf-seed --drop --org-slug perf-100-10 --force
     ```

   Locally against any Postgres (`backend/`): `dotnet run -- perf-seed --employees 100 --years 10` with
   `ConnectionStrings__DefaultConnection` (or `DATABASE_URL`) set; run the API with `UseInMemoryDatabase=false`.

2. Limits of the instance under test:
   - **Logins**: 10 per minute and IP outside Development. `setup()` logs ~100 users in with pauses (`LOGINS_PER_MINUTE`,
     default 9 → ~12 min of setup). Faster: raise the limit on the test instance (`RateLimiting__AuthStrictPerMinute=500`;
     Development has 200) and pass `-e LOGINS_PER_MINUTE=150`.
   - **Access tokens** are not refreshed (refresh tokens rotate with reuse detection; VUs sharing a user would end each
     other's sessions). They must outlive setup + `DURATION`: the default is 15 min — set
     `JwtSettings__AccessTokenExpiryMinutes=60` on the test instance (Development has 60).
   - `General` allows 300 calls per user and minute — enough for these scenarios.

3. k6 is not installed locally; use the Docker image (mount the folder, the script imports `./lib/auth.js`):

   ```sh
   # Linux, the test instance's backend on 127.0.0.1:7100 (docker-compose.test.yml)
   docker run --rm -i --network host -v "$PWD/loadtest:/scripts" \
     -e BASE_URL=http://localhost:7100 -e ORG_SLUG=perf-100-10 -e PASSWORD='<printed password>' grafana/k6 run /scripts/scenarios.js

   # Docker Desktop (Windows/macOS): the host is host.docker.internal
   docker run --rm -i -v "$PWD/loadtest:/scripts" \
     -e BASE_URL=http://host.docker.internal:7100 -e ORG_SLUG=perf-100-10 -e PASSWORD='<printed password>' grafana/k6 run /scripts/scenarios.js

   # through the host's Caddy: BASE_URL=https://<test host>
   ```

   Environment: `PASSWORD` (required), `BASE_URL` (default `http://localhost:7000`), `ORG_SLUG` (`perf-100-10`),
   `USERS` (100), `DURATION` (`15m`), `BROWSE_RATE` (30), `SCENARIOS` (comma list, default all:
   `morning_rush,polling,polling_team,browsing,month_end`), `LOGINS_PER_MINUTE` (9). A quick smoke run:
   `-e SCENARIOS=polling,browsing -e USERS=10 -e DURATION=2m -e LOGINS_PER_MINUTE=150`.

`teardown()` stops every timer it may have started and deletes the run's terminal key, so the test can run again
against the same organization (cards stay; a second run reuses them). Afterwards drop the organization (step 1).
