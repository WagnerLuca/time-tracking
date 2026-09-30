# Time Tracking — Frontend

SvelteKit (Svelte 5 runes, TypeScript) with Tailwind CSS 4 + DaisyUI 5, styled with the
Wagner Luca design system (`@wagnerluca/ui` tokens). German UI by default, English via the
toggle in the nav or in Settings.

**Architecture, conventions and gotchas live in [`../CLAUDE.md`](../CLAUDE.md)** — read that
first. This file only lists the commands.

## Setup

`@wagnerluca/ui` is installed from GitHub Packages, which requires a token even for reads:

```bash
export NODE_AUTH_TOKEN=<classic PAT with read:packages>
npm install
```

## Commands

| Command | What it does |
|---|---|
| `npm run dev -- --port 5179` | Dev server with HMR on http://localhost:5179 (the backend runs on :7000; 5173 is used by another project) |
| `npm run check` | svelte-check / TypeScript — must report 0 errors and 0 warnings |
| `npm test` | Unit tests (Vitest; TZ pinned to Europe/Berlin) |
| `npm run test:e2e` | Playwright smoke test against the running dev servers (frontend :5179, backend :7000 with seeded users); first run `npx playwright install chromium` |
| `npm run build` / `npm run preview` | Production build (adapter-node) / preview it |
| `npm run generate-api` | Regenerate `src/lib/api/` from the backend's Swagger (backend must run on :7000) — never hand-edit that folder |

Seeded dev logins (in-memory backend): `max.mueller@example.com` (Owner),
`anna.schmidt@example.com` (Admin), `tom.weber@example.com` (Member) — password `Password123`.
