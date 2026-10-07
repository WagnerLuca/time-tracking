# Time Tracking

Multi-organization working-time tracking: timer and entries, work schedules, pause rules,
holidays and absences (vacation, sick days, custom types), approval requests, groups with
their own permissions, CSV/PDF reports, RFID terminals, and automatic "failure entries" for
timers that were never stopped. German and English UI.

| Part | Stack |
|---|---|
| Backend | ASP.NET Core / **.NET 10**, EF Core, PostgreSQL 16+ (row-level security) |
| Frontend | SvelteKit, Svelte 5, Tailwind CSS 4 + DaisyUI 5, Wagner Luca design tokens (`@wagnerluca/ui`) |
| Ops | Docker images on GHCR, GitHub Actions, Serilog → Seq |

Architecture and conventions for contributors are in [CLAUDE.md](CLAUDE.md).

## Development

Prerequisites: .NET 10 SDK, Node.js 20+, Docker (only for the Postgres test suite and for
production-like runs).

```bash
# Backend — in-memory database with demo data, http://localhost:7000 (Swagger at /swagger)
cd backend
dotnet run

# Frontend — http://localhost:5179
cd frontend
export NODE_AUTH_TOKEN=<GitHub PAT with read:packages>   # optional: @wagnerluca/ui from GitHub Packages, else bundled tokens
npm install
npm run dev
```

Demo logins (password `Password123`): `max.mueller@example.com` (owner),
`anna.schmidt@example.com` (admin), `tom.weber@example.com` (member; has a forgotten timer
that shows up as a failure entry).

In development, emails (verification, password reset) are not sent but written to the
backend log, including their link.

## Tests

```bash
cd backend/tests/TimeTracking.Api.Tests && dotnet test   # integration tests; the Postgres suite needs Docker
cd frontend && npm run check && npm test                 # type check + Vitest unit tests
cd frontend && npm run test:e2e                          # Playwright smoke test against the running dev servers
```

The backend suite runs most tests on EF InMemory and a `Postgres` collection against a real
PostgreSQL container (Testcontainers) — that is where unique indexes, transactions, UTC
handling and row-level security are verified.

## Production

`docker-compose.production.yml` runs Postgres, Seq, the backend and the frontend from the GHCR
images. Copy [.env.example](.env.example) to `.env`, fill in the secrets, then:

```bash
docker compose -f docker-compose.production.yml up -d
```

Things to know:

- **Database role.** The backend connects as `APP_DB_USER`, a normal role that owns the
  database — never the Postgres superuser, because superusers bypass row-level security. The
  role is created automatically on a new data volume; for an existing database see the
  comment in `.env.example`.
- **Secrets.** The backend refuses to start outside Development with the placeholder or a
  JWT secret shorter than 32 bytes.
- **Migrations** are applied on startup; a failed migration stops the backend.
- **Email.** Configure `Email__Smtp__Host`, `Email__Smtp__Port`, `Email__Smtp__Username`,
  `Email__Smtp__Password`, `Email__From` and `Email__FrontendBaseUrl` (used for links in mails).
- **CORS.** List the frontend origin(s) in `Cors__AllowedOrigins__0`, `…__1`.
- **Reverse proxy.** Set `ReverseProxy__TrustForwardedHeaders=true` only when the backend sits
  behind a proxy that sets `X-Forwarded-For`; rate limits are per client IP.
- **Swagger** is off outside Development unless `Swagger__Enabled=true`.
- **Seq** (logs) is bound to `127.0.0.1` because it runs without authentication; reach it
  through an SSH tunnel.
- **Health:** `GET /health` returns 503 when the database is unreachable.

## Security model (short)

- JWT access tokens + rotating refresh tokens, stored only as SHA-256 hashes; replaying a
  rotated token revokes all of the user's sessions.
- Optional TOTP 2FA with hashed one-time backup codes and replay protection; organizations can
  require 2FA (enforced by the API).
- Organization data is authorized in the services **and** isolated in Postgres with row-level
  security on time entries and absences, so one organization's times can never leak into
  another's, even through a bug.
- Deleting an account anonymizes it (working-time records stay with the organization).
- RFID terminals use organization API keys that can only punch members in and out by badge.

## License

See [LICENSE](LICENSE).
