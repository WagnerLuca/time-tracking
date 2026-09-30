import { expect, test } from '@playwright/test';

/**
 * Smoke test against the running dev stack (see playwright.config.ts):
 * log in as the seeded member, start and stop a timer, find the entry in History.
 */
test('login, start + stop timer, entry shows up in history', async ({ page }) => {
	const note = `E2E ${Date.now()}`;

	await page.goto('/login');
	// client-only app: wait until Svelte has mounted before typing
	await page.waitForLoadState('networkidle');
	await page.locator('input[type="email"]').fill('tom.weber@example.com');
	await page.locator('input[type="password"]').fill('Password123');
	await page.locator('form button[type="submit"]').click();
	await expect(page).not.toHaveURL(/\/login/, { timeout: 15_000 });

	// Tom may belong to several orgs: pick the first one if asked
	if (page.url().includes('/select-org')) {
		await page.locator('main button').first().click();
	}

	await page.goto('/time');
	const stop = page.getByRole('button', { name: 'Timer stoppen' });
	const start = page.getByRole('button', { name: 'Timer starten' });
	// A timer left running (e.g. by an earlier run) is stopped first
	if (await stop.isVisible().catch(() => false)) {
		await stop.click();
		await expect(start).toBeVisible();
	}

	await page.getByPlaceholder('Notiz (optional)').fill(note);
	await start.click();
	await expect(stop).toBeVisible();
	await page.waitForTimeout(1500);
	await stop.click();
	await expect(start).toBeVisible();

	await page.goto('/history');
	await expect(page.getByText(note).first()).toBeVisible({ timeout: 15_000 });
});
