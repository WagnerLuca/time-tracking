import { expect, test, type APIRequestContext } from '@playwright/test';
import { API, apiToken, futureWorkday, login } from './helpers';

const SLUG = 'mueller-software';

/** Deletes Tom's own schedule periods starting on `validFrom` (left over from an aborted run). */
async function removePeriods(request: APIRequestContext, validFrom: string) {
	const headers = { Authorization: `Bearer ${await apiToken(request, 'tom.weber@example.com')}` };
	const res = await request.get(`${API}/organizations/${SLUG}/work-schedules`, { headers });
	expect(res.ok(), await res.text()).toBeTruthy();
	for (const p of (await res.json()) as { id: number; validFrom: string }[]) {
		if (p.validFrom === validFrom) await request.delete(`${API}/organizations/${SLUG}/work-schedules/${p.id}`, { headers });
	}
}

/**
 * Settings → Organisation: Tom (his org allows schedule changes) adds a schedule period, sees it
 * listed with its weekly hours, and deletes it again. The period is a closed one long before his
 * current schedule: a later one would auto-close the open-ended current period, and deleting it
 * again does not reopen that (nor can an update clear a validTo), so Tom would lose his schedule.
 */
test('settings: a member adds and deletes a work schedule period', async ({ page, request }) => {
	const stamp = Date.now();
	const validFrom = futureWorkday(2001, stamp);
	const hours = 12 + (stamp % 20) / 2; // 12 … 21.5, step 0.5
	const hoursText = `${new Intl.NumberFormat('de-DE').format(hours)} h/Woche`;
	const dateText = new Date(`${validFrom}T00:00:00`).toLocaleDateString('de-DE', { month: 'short', day: 'numeric', year: 'numeric' });
	await removePeriods(request, validFrom);

	try {
		await login(page, 'tom.weber@example.com');
		await page.goto('/settings');
		await page.getByRole('tab', { name: 'Organisation' }).click();
		await page.getByRole('button', { name: '+ Zeitraum hinzufügen' }).click();
		await page.getByLabel('Gültig ab').fill(validFrom);
		await page.getByLabel('Gültig bis').fill(validFrom);
		await page.getByLabel('Wochenstunden').fill(String(hours));
		await page.getByRole('button', { name: 'Zeitraum hinzufügen', exact: true }).click();
		await expect(page.getByText('Arbeitszeit-Zeitraum hinzugefügt.')).toBeVisible();

		const period = page.locator('li', { hasText: dateText });
		await expect(period).toHaveCount(1);
		await expect(period).toContainText(hoursText);

		await period.getByRole('button', { name: 'Zeitraum löschen' }).click();
		await page.getByRole('dialog').getByRole('button', { name: 'Löschen' }).click();
		await expect(page.getByText('Arbeitszeit-Zeitraum gelöscht.')).toBeVisible();
		await expect(page.locator('li', { hasText: dateText })).toHaveCount(0);
	} finally {
		await removePeriods(request, validFrom);
	}
});
