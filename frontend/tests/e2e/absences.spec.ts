import { expect, test, type Page } from '@playwright/test';
import { futureWorkday, login, removeAbsences } from './helpers';

async function switchUser(page: Page, email: string) {
	await page.context().clearCookies();
	await page.evaluate(() => localStorage.clear());
	await login(page, email);
}

/**
 * Absence approval: Tom (his org requires approval for absences) requests a vacation day in his
 * calendar, the owner accepts it in the org settings, and the day then shows as vacation for Tom.
 */
test('member requests an absence, the owner approves it, the calendar shows it', async ({ page, request }) => {
	const note = `E2E Urlaub ${Date.now()}`;
	const date = futureWorkday(2031);
	await removeAbsences(request, 'tom.weber@example.com', date); // a leftover of an aborted run

	try {
		await login(page, 'tom.weber@example.com');
		await page.goto(`/history?date=${date}`);
		const details = page.getByTestId('day-details');
		await expect(page.locator(`[data-day="${date}"]`)).toHaveAttribute('aria-pressed', 'true');
		await details.getByRole('button', { name: 'Abwesenheit beantragen' }).click();

		const dialog = page.getByRole('dialog');
		await expect(dialog.getByLabel('Von')).toHaveValue(date);
		await dialog.getByLabel('Art').selectOption({ label: 'Urlaub' });
		await dialog.getByLabel('Notiz (optional)').fill(note);
		await dialog.getByRole('button', { name: 'Antrag senden' }).click();
		await expect(page.getByText('Abwesenheitsantrag für 1 Tag zur Genehmigung gesendet.')).toBeVisible();
		await expect(details.getByText('Urlaub', { exact: true })).toHaveCount(0); // only requested so far

		await switchUser(page, 'max.mueller@example.com');
		await page.goto('/inbox');
		const item = page.getByTestId('inbox-approve').getByTestId('inbox-item').filter({ hasText: note });
		await item.getByRole('button', { name: 'Annehmen' }).click();
		await expect(page.getByText('Antrag von Tom Weber angenommen.')).toBeVisible();
		await expect(item).toHaveCount(0);

		await switchUser(page, 'tom.weber@example.com');
		await page.goto(`/history?date=${date}`);
		await expect(page.getByTestId('day-details').getByText('Urlaub', { exact: true })).toBeVisible();
	} finally {
		await removeAbsences(request, 'tom.weber@example.com', date);
	}
});
