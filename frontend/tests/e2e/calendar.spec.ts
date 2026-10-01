import { expect, test } from '@playwright/test';
import { login } from './helpers';

/**
 * History calendar: left click selects a day and lists its entries below the
 * calendar; right click opens the absence dialog for that day.
 */
test('history: left click shows the day entries, right click opens the dialog', async ({ page }) => {
	const note = `E2E Kalender ${Date.now()}`;

	await login(page, 'tom.weber@example.com');

	// An entry for today
	await page.goto('/time');
	const stop = page.getByRole('button', { name: 'Timer stoppen' });
	const start = page.getByRole('button', { name: 'Timer starten' });
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
	const [today, otherDay] = await page.evaluate(() => {
		const key = (d: Date) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
		const now = new Date();
		const other = new Date(now.getFullYear(), now.getMonth(), now.getDate() === 1 ? 2 : 1);
		return [key(now), key(other)];
	});
	const details = page.getByTestId('day-details');

	// Another day first: today's entry is not listed
	await page.locator(`[data-day="${otherDay}"]`).click();
	await expect(details).toBeVisible();
	await expect(details.getByText(note)).toHaveCount(0);
	await expect(page.getByRole('dialog')).toHaveCount(0);

	// Left click on today: selected, its entries below the calendar, no dialog
	const todayCell = page.locator(`[data-day="${today}"]`);
	await todayCell.click();
	await expect(todayCell).toHaveAttribute('aria-pressed', 'true');
	await expect(details.getByText(note)).toBeVisible();
	await expect(page.getByRole('dialog')).toHaveCount(0);

	// Right click: the day dialog
	await todayCell.click({ button: 'right' });
	await expect(page.getByRole('dialog')).toBeVisible();
});
