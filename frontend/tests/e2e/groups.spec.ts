import { expect, test, type Page } from '@playwright/test';
import { API, apiToken, futureWorkday, login, removeAbsences } from './helpers';

const SLUG = 'mueller-software';

async function switchUser(page: Page, email: string) {
	await page.context().clearCookies();
	await page.evaluate(() => localStorage.clear());
	await login(page, email);
}

/** The ModeSelector row of one setting on the open group's "Berechtigungen" tab. */
function permRow(page: Page, label: string) {
	return page
		.locator('div')
		.filter({ has: page.getByText(label, { exact: true }) })
		.filter({ has: page.getByRole('button', { name: 'Erben' }) })
		.last();
}

/**
 * Group override: the org requires approval for absences; a group overriding that to "Erlaubt"
 * lets Tom enter one directly. Back to "Erben", the override is cleared (null) again.
 */
test('a group override lets a member enter absences directly, "Erben" clears it', async ({ page, request }) => {
	const stamp = Date.now();
	const groupName = `E2E Gruppe ${stamp}`;
	const date = futureWorkday(2036, stamp);
	const headers = { Authorization: `Bearer ${await apiToken(request, 'max.mueller@example.com')}` };
	const org = await (await request.get(`${API}/organizations/${SLUG}`, { headers })).json();
	const tom = org.members.find((m: { email: string }) => m.email === 'tom.weber@example.com');

	const created = await request.post(`${API}/organizations/${SLUG}/groups`, { headers, data: { name: groupName, color: '#6366f1' } });
	expect(created.ok(), await created.text()).toBeTruthy();
	const groupId: number = (await created.json()).id;
	try {
		const added = await request.post(`${API}/organizations/${SLUG}/groups/${groupId}/members`, { headers, data: { userId: tom.id } });
		expect(added.ok(), await added.text()).toBeTruthy();

		// Owner: absences → "Erlaubt" for the group
		await login(page, 'max.mueller@example.com');
		await page.goto(`/organizations/${SLUG}/groups`);
		await page.getByRole('button', { name: groupName }).click();
		await page.getByRole('tab', { name: 'Berechtigungen' }).click();
		const row = permRow(page, 'Abwesenheiten & Urlaub');
		await expect(row.getByRole('button', { name: 'Erben' })).toHaveAttribute('aria-pressed', 'true');
		await row.getByRole('button', { name: 'Erlaubt' }).click();
		await expect(row.getByRole('button', { name: 'Erlaubt' })).toHaveAttribute('aria-pressed', 'true');

		// Tom: no request any more, the absence is entered directly
		await switchUser(page, 'tom.weber@example.com');
		await page.goto(`/history?date=${date}`);
		const details = page.getByTestId('day-details');
		await expect(page.locator(`[data-day="${date}"]`)).toHaveAttribute('aria-pressed', 'true');
		await expect(details.getByRole('button', { name: 'Abwesenheit beantragen' })).toHaveCount(0);
		await details.getByRole('button', { name: 'Abwesenheit eintragen' }).click();
		const dialog = page.getByRole('dialog');
		await expect(dialog.getByText('Wird als Antrag gesendet', { exact: false })).toHaveCount(0);
		await dialog.getByRole('button', { name: 'Abwesenheit eintragen' }).click();
		await expect(dialog).toHaveCount(0);
		await expect(details.getByText('Urlaub', { exact: true })).toBeVisible();

		// Owner: back to "Erben" — the override is cleared
		await switchUser(page, 'max.mueller@example.com');
		await page.goto(`/organizations/${SLUG}/groups`);
		await page.getByRole('button', { name: groupName }).click();
		await page.getByRole('tab', { name: 'Berechtigungen' }).click();
		await row.getByRole('button', { name: 'Erben' }).click();
		await expect(row.getByRole('button', { name: 'Erben' })).toHaveAttribute('aria-pressed', 'true');
		const perms = await request.get(`${API}/organizations/${SLUG}/groups/${groupId}/permissions`, { headers });
		expect(perms.ok(), await perms.text()).toBeTruthy();
		expect((await perms.json()).absenceSelfServiceMode).toBeNull();

		// Tom has to request again
		await switchUser(page, 'tom.weber@example.com');
		await page.goto(`/history?date=${date}`);
		await expect(page.getByTestId('day-details').getByRole('button', { name: 'Abwesenheit beantragen' })).toBeVisible();
	} finally {
		await request.delete(`${API}/organizations/${SLUG}/groups/${groupId}`, { headers });
		await removeAbsences(request, 'tom.weber@example.com', date);
	}
});
