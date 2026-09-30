# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

Full-stack multi-organization time tracking app. ASP.NET Core Web API backend (`backend/`) + SvelteKit frontend (`frontend/`). Domain covers time entries, work schedules, absences (vacation/sick), holidays, approval requests, notifications, and organization/group-scoped permissions.

- **Backend**: ASP.NET Core, **.NET 10** (`net10.0`), C#. (README says 9.0 — outdated; trust the `.csproj`.)
- **Frontend**: SvelteKit + **Svelte 5 (runes)**, TypeScript, SSR via `adapter-node`, Tailwind CSS 4 + DaisyUI 5, styled with the Wagner Luca design system: `src/app.css` defines a single DaisyUI theme (`wagnerluca`) whose colours are `var()`s onto `@wagnerluca/ui/tokens.css` (primary = `time` accent). Light/dark is the `.dark` class on `<html>` under the shared `wl-theme` key (`theme.svelte.ts` + the pre-paint script in `app.html`), not DaisyUI `data-theme`. `@wagnerluca/ui` comes from GitHub Packages — `NODE_AUTH_TOKEN` (read:packages) for `npm install`, BuildKit secret in Docker, `GHP_READ_TOKEN` repo secret in CI.
- **DB**: PostgreSQL 16 in production; **in-memory EF Core provider in local dev** (no Postgres needed to run the backend).

## Commands

### Backend (`cd backend`)
- Run with hot reload: `dotnet watch run` (or `dotnet run`) → http://localhost:7000, Swagger at `/swagger`
- Build: `dotnet build`
- Uses in-memory DB by default in Development (`UseInMemoryDatabase: true` in `appsettings.Development.json`), auto-seeded on startup.

### Backend tests (`cd backend/tests/TimeTracking.Api.Tests`)
- All tests: `dotnet test`
- Single test/class: `dotnet test --filter "FullyQualifiedName~AuthControllerTests"` or `--filter "DisplayName~specific test name"`
- Tests are **integration tests** using `WebApplicationFactory` (`Infrastructure/TimeTrackingApiFactory.cs`), xUnit + FluentAssertions. They run with `ASPNETCORE_ENVIRONMENT=Testing` (rate limiting disabled in that env).

### Frontend (`cd frontend`)
- Dev server: `npm run dev -- --port 5179` → http://localhost:5179 (HMR; 5173 is taken by another project)
- Type-check: `npm run check` (svelte-check; there is no separate lint step) — keep it at 0 errors **and 0 warnings**
- Unit tests: `npm test` (Vitest, `src/**/*.test.ts`, TZ pinned to Europe/Berlin in `vitest.config.ts`; runs in CI after the check). Pure unit tests, no DOM.
- E2E smoke: `npm run test:e2e` (Playwright, `tests/e2e/`, not part of `npm test`) against the *running* dev servers (frontend :5179, backend :7000 with seeded users); first run `npx playwright install chromium`
- Build: `npm run build`; preview: `npm run preview`
- **Regenerate API client**: `npm run generate-api` — requires the backend running on :7000; regenerates `src/lib/api/` from live Swagger.

### Full launch
- `start-app.bat` (Windows) opens backend + frontend in separate terminals.
- `docker-compose up -d` runs backend (in-memory DB) + Seq log server (:5341). Note: the dev compose file does **not** define a Postgres `db` service despite README references.

## Backend architecture

Layered: **Controller → Service → EF Core `TimeTrackingDbContext`**. Business logic lives in services, never controllers.

- **`ServiceResult` / `ServiceResult<T>`** (`Services/Common/ServiceResult.cs`) is the service→controller contract. Services return `ServiceResult.Ok(data)` or `ServiceResult.NotFound/BadRequest/Forbidden/Conflict(msg)` — they never touch HTTP types. Controllers extend **`OrganizationBaseController`** and call `ToResponse(result)` / `ToCreatedResponse(result)`, which map `ServiceErrorType` → HTTP status + RFC 7807 Problem Details. When adding an endpoint, follow this pattern rather than returning `IActionResult` directly.
- **Authorization is org-scoped via `[RequireOrgRole(...)]`** (`Filters/RequireOrgRoleAttribute.cs`). It reads the `{slug}` route param, checks the caller's `OrganizationRole` (Member ≤ Admin ≤ Owner), and caches the lookup for 2 minutes. Org-scoped routes are `api/v{version}/organizations/{slug}/...`.
- **Failure entries** (`Services/TimeTracking/FailureEntryService.cs`): a hosted service stops any timer still running at its organization's `BusinessDayEnd` (local time in `Organization.TimeZone`, default 00:00 Europe/Berlin) and sets `TimeEntry.IsFailure`. A failure entry counts as **0** everywhere: `TimeEntryResponse` returns null durations (so every client-side sum skips it), `DailyReportAggregator` (daily CSV, PDF, cumulative overtime) excludes it and marks the day "Needs Correction", and the admin overview reports `FailureEntryCount`. Setting an end time (direct edit or approved edit request) clears the flag. Tests call the static `CloseOverdueEntriesAsync(db, nowUtc)` directly.
- **Terminal API keys** (`ApiKeysController`, `Services/ApiKeys/`): org-scoped keys for unattended devices (RFID scanner). `POST /api/v1/terminal/punch` is `[AllowAnonymous]` and checks the `X-Api-Key` header itself (SHA-256 hash lookup); it can only toggle the timer of the member whose `UserOrganization.BadgeId` matches. Keys are *not* an authentication scheme — they never yield a user principal.
- **`ActiveUserFilter`** is a global filter rejecting requests from deactivated users on every authenticated call.
- **Auth**: JWT bearer + refresh tokens (`TokenService`), BCrypt password hashing, TOTP 2FA (`TotpService`, Otp.NET). `RefreshTokenCleanupService` is a hosted background service. Get current user id in controllers via `GetCurrentUserId()`.
- **Permissions & visibility model** (`Models/Organization.cs`): `RuleMode` (`Disabled`/`RequiresApproval`/`Allowed`) gates actions like editing past entries, pause edits, CSV import, schedule changes. `VisibilityMode` (`Private`/`AdminOnly`/`AllMembers`) gates who sees vacation/sick/time-entry data. Both are set at the **organization** level and can be **overridden per `Group`** (`Models/Group.cs`, null = inherit). Groups also carry granular "GroupAdmin can…" capability flags. This group-aware effective-permission resolution is the subject of the current `feature/groups` work — see `docs/GROUPS_FEATURE_PLAN.md`.
- **DbContext** (`Data/TimeTrackingDbContext.cs`): entity config branches on `Database.IsInMemory()` (e.g. `HasDefaultValueSql("CURRENT_TIMESTAMP")` only applies for Postgres). Soft deletes use `IsActive` global query filters. `DbSeeder` seeds demo data; `DesignTimeDbContextFactory` forces the Npgsql provider for `dotnet ef` even when dev config is in-memory.
- **API versioning**: URL-segment (`Asp.Versioning`), default v1. **Logging**: Serilog → Console + Seq (:5341) + rolling file (`logs/`). **Rate limiting**: fixed-window policies `AuthStrict`/`AuthModerate`/`General`.

### EF Core migrations
Migrations are **auto-applied on startup** (`context.Database.Migrate()` for Postgres; `EnsureCreated()` for in-memory). You only need to *create* them: `dotnet ef migrations add DescriptiveName` (from `backend/`). See `backend/EF_COMMANDS.md`.

## Frontend architecture

- **Generated API client** (`src/lib/api/`) is produced by openapi-generator (typescript-axios) — **do not hand-edit**; change the backend and re-run `npm run generate-api`.
- **`src/lib/apiClient.ts`** wires the generated `*Api` classes to a shared axios instance: injects the `Bearer` token from `localStorage`; on a `401` (except from the auth endpoints) `authRefresh.ts` exchanges the refresh token **once** — concurrent 401s share that single refresh — stores the rotated tokens via the auth store and retries each request once. Only a failed refresh (or a retry that is still 401) clears auth and redirects to `/login?next=<path>`; login sends the user back to `next`. Import the typed singletons (`authApi`, `timeTrackingApi`, `groupApi`, …) from here, not from `$lib/api` directly. API base URL comes from `PUBLIC_API_BASE_URL`, falling back to `window.location.hostname:7000`.
- **Client-only rendering**: `src/routes/+layout.ts` sets `ssr = false` (tokens live in localStorage). The root layout renders protected pages only once authenticated (otherwise a spinner while redirecting), so pages never fire API calls signed out.
- **i18n** (`src/lib/i18n/`): German is the default, English via the DE/EN toggle (nav + `/settings`); the choice is stored under the shared `wl-locale` key (`stores/locale.svelte.ts`, also applied to `<html lang>` by the pre-paint script in `app.html`). Components call `t('ns.key', params?)` / `tp('ns.key', count)` — never hardcode either language. Strings live in `messages/<namespace>.ts`, `de` and `en` side by side with `en: typeof de` (a missing English key is a type error); keys are typed dotted paths; runtime fallback is de → key. `i18n.test.ts` also fails on any literal `t('…')` key that doesn't exist. Dates/numbers go through the locale-aware helpers in `utils/formatters.ts` (`formatDate`, `formatDateTime`, `formatNumber`, `dayNamesShort`, … → de-DE / en-GB); never `toLocaleDateString('en-US')` or `toISOString().split('T')[0]` (use `dateKey()` for local YYYY-MM-DD keys).
- **State** uses Svelte 5 runes in `.svelte.ts` files (`src/lib/stores/`): `auth.svelte.ts` (login/2FA/tokens, persisted to localStorage), `orgContext.svelte.ts`, `theme.svelte.ts`, `locale.svelte.ts`, `timer.svelte.ts` (the running timer shared by nav pill, dashboard and timer page), `toast.svelte.ts` (`toast.success/error(err, fallback)` — use it instead of swallowing errors), `confirm.svelte.ts` (`await confirmDialog({...})` — never native `confirm()`). Stores expose `get`-only accessors over internal `$state`.
- **UI building blocks** (`src/lib/components/`): `Modal.svelte` (native `<dialog>`: focus trap, Esc, labelled title) for every overlay; `ConfirmDialog`/`Toaster` are mounted once in the root layout. Colours come from `utils/colors.ts` (day-type/absence classes, per-person accents, group colour presets, `tintStyle` for user-picked colours) — no hardcoded Tailwind palette classes or hex values, so dark mode works.
- **Permissions**: `utils/orgRules.ts` mirrors backend rules client-side; `isOrgAdmin(role)` for Owner/Admin checks, and the `can…(...sources)` helpers take `(effectivePermissions, orgDetail)` so group overrides (`groupApi…EffectivePermissionsGet`) win over org defaults.
- **Routes** (`src/routes/`) are file-based; org features live under `organizations/[slug]/` (absences, groups, holidays, members, settings, time-overview) and (re)load in an `$effect` keyed on `page.params.slug` (use `page` from `$app/state`, not `$app/stores`). `/history?date=YYYY-MM-DD&entry=<id>` opens that day and the entry editor (the failure-entry banner links there). Shared UI in `src/lib/components/`, helpers in `src/lib/utils/`.

## Conventions & gotchas

- The frontend `README.md` is stale (references a nonexistent `apiService.ts` and "weather" demo). Trust `apiClient.ts` and this file instead.
- Seeded dev logins (in-memory DB): `max.mueller@example.com` (Owner), `anna.schmidt@example.com` (Admin), `tom.weber@example.com` (Member) — all password `Password123`.
- CI (`.github/workflows/`) splits backend and frontend by changed paths; backend job builds/tests from `backend/tests/TimeTracking.Api.Tests` and publishes Docker images to GHCR on non-PR pushes.
- `docs/` holds design/plan documents (auth guide, groups feature plan, improvement plans) — useful background but not always current.
