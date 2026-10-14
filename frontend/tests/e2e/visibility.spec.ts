import { expect, test, type Page } from '@playwright/test';
import { API, apiToken, futureWorkday, login, removeAbsences } from './helpers';

const SLUG = 'mueller-software';

async function switchUser(page: Page, email: string) {
	await page.context().clearCookies();
	await page.evaluate(() => localStorage.clear());
	await login(page, email);
}

/**
 * Member visibility matrix (issue #60): with colleagues hidden outside one's own groups, Tom (group
 * "Backend") sees Felix (Backend) but not Lisa (Frontend); with vacations visible in own groups —
 * switched on by the owner in the settings matrix — he sees Felix's vacation but not his sick day.
 */
test('a member sees only people of their own groups, and colleagues’ vacations but not their sick days', async ({ page, request }) => {
	const headers = { Authorization: `Bearer ${await apiToken(request, 'max.mueller@example.com')}` };
	const org = await (await request.get(`${API}/organizations/${SLUG}`, { headers })).json();
	const original = org.visibility as { kind: string; inGroups: boolean; outsideGroups: boolean }[];
	const id = (email: string) => org.members.find((m: { email: string }) => m.email === email).id as number;

	const year = new Date().getFullYear() + 1; // in the absence list's year picker
	const day = futureWorkday(year);
	const vacation = `${year}${day.slice(4)}`;
	const sick = `${year}${day.slice(4, 8)}${String(Number(day.slice(8)) + 1).padStart(2, '0')}`;
	const settings = (rows: { kind: string; inGroups: boolean; outsideGroups: boolean }[]) =>
		request.put(`${API}/Organizations/${SLUG}/settings`, { headers, data: { visibility: rows } });

	try {
		const set = await settings([
			{ kind: 'Member', inGroups: true, outsideGroups: false },
			{ kind: 'Vacation', inGroups: false, outsideGroups: false },
			{ kind: 'SickDays', inGroups: false, outsideGroups: false }
		]);
		expect(set.ok(), await set.text()).toBeTruthy();
		for (const [email, date, type] of [
			['felix.koch@example.com', vacation, 1],
			['felix.koch@example.com', sick, 0],
			['lisa.braun@example.com', vacation, 1]
		] as const) {
			const res = await request.post(`${API}/organizations/${SLUG}/absences/admin`, { headers, data: { userId: id(email), date, type } });
			expect(res.ok(), await res.text()).toBeTruthy();
		}

		// Owner: vacations visible in own groups, switched on in the matrix; the preview follows
		await login(page, 'max.mueller@example.com');
		await page.goto(`/organizations/${SLUG}/settings`);
		const matrix = page.getByTestId('visibility-settings');
		const vacationInGroups = matrix.getByRole('switch', { name: 'Urlaub – In meinen Gruppen', exact: true });
		await expect(vacationInGroups).not.toBeChecked();
		await vacationInGroups.click();
		await expect(vacationInGroups).toBeChecked();
		await expect(matrix.getByTestId('visibility-preview')).toContainText('eigenen Gruppen: Name, E-Mail, Gruppen, Urlaub');
		await expect(matrix.getByTestId('visibility-preview')).toContainText('über alle anderen: nichts');

		// Tom: only the Backend group's people
		await switchUser(page, 'tom.weber@example.com');
		await page.goto(`/organizations/${SLUG}/members`);
		await expect(page.getByText('Felix Koch')).toBeVisible();
		await expect(page.getByText('Lisa Braun')).toHaveCount(0);
		await expect(page.getByText('Anna Schmidt')).toHaveCount(0);

		// … and of Felix the vacation, not the sick day; nothing of Lisa
		await page.goto(`/organizations/${SLUG}/absences`);
		await page.getByLabel('Jahr').selectOption(String(year));
		const items = page.locator('li', { hasText: 'Felix' });
		await expect(items.filter({ hasText: 'Urlaub' }).first()).toBeVisible();
		await expect(items.filter({ hasText: 'Krankheitstag' })).toHaveCount(0);
		await expect(page.locator('li', { hasText: 'Lisa' })).toHaveCount(0);
	} finally {
		await settings(original);
		await removeAbsences(request, 'felix.koch@example.com', vacation);
		await removeAbsences(request, 'felix.koch@example.com', sick);
		await removeAbsences(request, 'lisa.braun@example.com', vacation);
	}
});
