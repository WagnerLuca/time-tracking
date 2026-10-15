import { defineConfig, devices } from '@playwright/test';

/**
 * End-to-end smoke test against an already running stack (not part of `npm test`):
 *   backend  → http://localhost:7000 (Development, in-memory DB with seeded users)
 *   frontend → http://localhost:5179 (`npm run dev -- --port 5179`)
 * Run with `npm run test:e2e` (first time: `npx playwright install chromium`).
 * Override the frontend URL with E2E_BASE_URL, the browser with E2E_CHANNEL (e.g. msedge).
 *
 * pwa.spec.ts needs a production build (the service worker never runs in dev): it uses
 * E2E_PWA_URL when set (CI: the preview on :5179), else Playwright builds and starts
 * `vite preview` on :4179 (same /api proxy) for the run and stops it afterwards.
 */
const PWA_URL = process.env.E2E_PWA_URL ?? 'http://localhost:4179';
export default defineConfig({
	testDir: 'tests/e2e',
	timeout: 60_000,
	retries: 0,
	workers: 1,
	reporter: 'list',
	use: {
		baseURL: process.env.E2E_BASE_URL ?? 'http://localhost:5179',
		trace: 'retain-on-failure',
		locale: 'de-DE',
		timezoneId: 'Europe/Berlin'
	},
	webServer: process.env.E2E_PWA_URL
		? undefined
		: { command: 'npm run build && npx vite preview --port 4179 --strictPort', url: PWA_URL, reuseExistingServer: true, timeout: 240_000 },
	// E2E_CHANNEL=msedge|chrome uses an installed browser instead of the downloaded Chromium
	projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'], channel: process.env.E2E_CHANNEL || undefined } }]
});
