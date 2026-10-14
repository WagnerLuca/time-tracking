import { expect, test, type Page } from '@playwright/test';
import { API, apiToken, futureWorkday, login } from './helpers';

const SLUG = 'mueller-software';

async function switchUser(page: Page, email: string) {
	await page.context().clearCookies();
	await page.evaluate(() => localStorage.clear());
	await login(page, email);
}

/** Team overview (issue #61): Anna (admin) sees Tom's card with his total balance and, once his timer runs, the present dot. */
test('an admin sees a team card with the balance and the present dot of a member whose timer runs', async ({ page, request }) => {
	const tom = { Authorization: `Bearer ${await apiToken(request, 'tom.weber@example.com')}` };
	const org = await (await request.get(`${API}/organizations/${SLUG}`, { headers: tom })).json();
	await request.post(`${API}/TimeTracking/stop`, { headers: tom, data: {} }); // a leftover timer from an earlier run
	const start = await request.post(`${API}/TimeTracking/start`, { headers: tom, data: { organizationId: org.id } });
	expect(start.ok(), await start.text()).toBeTruthy();

	try {
		await login(page, 'anna.schmidt@example.com');
		const card = page.getByTestId('team-card').filter({ hasText: 'Tom Weber' });
		await expect(card).toBeVisible();
		await expect(card).toHaveAttribute('href', /\/organizations\/mueller-software\/members\/\d+$/);
		await expect(card.getByText('Saldo gesamt')).toBeVisible();
		await expect(card.getByTestId('team-balance')).toHaveText(/^(±0h|[+-]?(\d+h)? ?(\d+m)?)$/);
		await expect(card.getByRole('img', { name: 'Anwesend' })).toBeVisible();
		await expect(card.getByText(/^seit \d{2}:\d{2}$/)).toBeVisible();
		// Present people come first
		await expect(page.getByTestId('team-card').first()).toContainText('Tom Weber');

		await card.focus();
		await page.keyboard.press('Enter');
		await expect(page).toHaveURL(/\/organizations\/mueller-software\/members\/\d+$/);
	} finally {
		await request.post(`${API}/TimeTracking/stop`, { headers: tom, data: {} });
	}
});

/**
 * Team calendar shortcut: Tom opens it from the main navigation. With no absence kinds visible he is told
 * so; with vacations visible in own groups he sees Felix's (Backend, like him), never Lisa's (Frontend).
 */
test('a member opens the team calendar from the navigation and sees only what the settings allow', async ({ page, request }) => {
	const headers = { Authorization: `Bearer ${await apiToken(request, 'max.mueller@example.com')}` };
	const org = await (await request.get(`${API}/organizations/${SLUG}`, { headers })).json();
	const original = org.visibility as { kind: string; inGroups: boolean; outsideGroups: boolean }[];
	const id = (email: string) => org.members.find((m: { email: string }) => m.email === email).id as number;
	const settings = (rows: { kind: string; inGroups: boolean; outsideGroups: boolean }[]) =>
		request.put(`${API}/Organizations/${SLUG}/settings`, { headers, data: { visibility: rows } });

	const year = new Date().getFullYear(); // the absence list shows the current year
	const date = `${year}${futureWorkday(year).slice(4)}`;
	const created: number[] = [];

	try {
		const hidden = await settings([
			{ kind: 'Member', inGroups: true, outsideGroups: false },
			{ kind: 'Vacation', inGroups: false, outsideGroups: false },
			{ kind: 'SickDays', inGroups: false, outsideGroups: false },
			{ kind: 'OtherAbsences', inGroups: false, outsideGroups: false }
		]);
		expect(hidden.ok(), await hidden.text()).toBeTruthy();
		for (const email of ['felix.koch@example.com', 'lisa.braun@example.com']) {
			const userId = id(email);
			const existing = await request.get(`${API}/organizations/${SLUG}/absences?userId=${userId}&from=${date}&to=${date}`, { headers });
			for (const a of (await existing.json()).items as { id: number }[]) await request.delete(`${API}/organizations/${SLUG}/absences/${a.id}`, { headers });
			const res = await request.post(`${API}/organizations/${SLUG}/absences/admin`, { headers, data: { userId, date, type: 1 } });
			expect(res.ok(), await res.text()).toBeTruthy();
			created.push(((await res.json()) as { id: number }).id);
		}

		await login(page, 'tom.weber@example.com');
		await page.getByRole('navigation').getByRole('link', { name: 'Teamkalender' }).click();
		await expect(page).toHaveURL(/\/organizations\/mueller-software\/absences$/);
		await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
		await expect(page.getByText('Deine Organisation zeigt dir keine Abwesenheiten anderer')).toBeVisible();
		await expect(page.getByText('Felix Koch')).toHaveCount(0);

		const vacations = await settings([{ kind: 'Vacation', inGroups: true, outsideGroups: false }]);
		expect(vacations.ok(), await vacations.text()).toBeTruthy();
		await switchUser(page, 'tom.weber@example.com');
		await page.getByRole('navigation').getByRole('link', { name: 'Teamkalender' }).click();
		await expect(page.getByRole('listitem').filter({ hasText: 'Felix Koch' }).first()).toBeVisible();
		await expect(page.getByText('Lisa Braun')).toHaveCount(0);
		await expect(page.getByText('Deine Organisation zeigt dir keine Abwesenheiten anderer')).toHaveCount(0);
	} finally {
		for (const a of created) await request.delete(`${API}/organizations/${SLUG}/absences/${a}`, { headers });
		const restore = await settings(original);
		expect(restore.ok(), await restore.text()).toBeTruthy();
	}
});
