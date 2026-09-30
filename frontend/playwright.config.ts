import { defineConfig, devices } from '@playwright/test';

/**
 * End-to-end smoke test against an already running stack (not part of `npm test`):
 *   backend  → http://localhost:7000 (Development, in-memory DB with seeded users)
 *   frontend → http://localhost:5179 (`npm run dev -- --port 5179`)
 * Run with `npm run test:e2e` (first time: `npx playwright install chromium`).
 * Override the frontend URL with E2E_BASE_URL.
 */
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
	projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }]
});
