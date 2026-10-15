import { expect, test, type Page } from '@playwright/test';
import { API, apiToken, futureWorkday, login, removeAbsences } from './helpers';

const SLUG = 'mueller-software';

async function switchUser(page: Page, email: string) {
	await page.context().clearCookies();
	await page.evaluate(() => localStorage.clear());
	await login(page, email);
}

/**
 * Organigramm (issue #66): the admin collapses the tree, opens "Backend" with the keyboard, opens its
 * panel and follows "Gruppenkalender" — the team calendar then lists only Backend's (incl. the API
 * sub-group's) absences.
 */
test('the Organigramm expands a group by keyboard and its group calendar shows only that group', async ({ page, request }) => {
	const headers = { Authorization: `Bearer ${await apiToken(request, 'max.mueller@example.com')}` };
	const org = await (await request.get(`${API}/organizations/${SLUG}`, { headers })).json();
	const id = (email: string) => org.members.find((m: { email: string }) => m.email === email).id as number;
	const year = new Date().getFullYear() + 1;
	const day = `${year}${futureWorkday(year).slice(4)}`;

	try {
		for (const email of ['jonas.wolf@example.com', 'lisa.braun@example.com']) {
			const res = await request.post(`${API}/organizations/${SLUG}/absences/admin`, { headers, data: { userId: id(email), date: day, type: 1 } });
			expect(res.ok(), await res.text()).toBeTruthy();
		}

		await login(page, 'anna.schmidt@example.com');
		await page.goto(`/organizations/${SLUG}/members`);
		await page.getByRole('link', { name: 'Organigramm' }).last().click();
		await expect(page).toHaveURL(new RegExp(`/organizations/${SLUG}/chart`));

		const tree = page.getByRole('tree');
		await expect(tree).toBeVisible();
		await page.getByRole('button', { name: 'Alle zuklappen' }).click();
		const backend = tree.getByRole('treeitem', { name: /^Backend,/ });
		await expect(backend).toHaveAttribute('aria-expanded', 'false');
		await expect(tree.getByRole('treeitem', { name: /^API,/ })).toHaveCount(0);

		// Keyboard: Tab into the tree, ↓ to the first group, → opens it, ↓ moves into it
		await tree.getByRole('treeitem').first().focus();
		await page.keyboard.press('ArrowDown');
		await expect(backend).toBeFocused();
		await page.keyboard.press('ArrowRight');
		await expect(backend).toHaveAttribute('aria-expanded', 'true');
		await page.keyboard.press('ArrowDown');
		await expect(tree.getByRole('treeitem', { name: /^API,/ })).toBeFocused();
		await page.keyboard.press('ArrowLeft');
		await expect(backend).toBeFocused();

		// Enter opens the side panel, Esc closes it and gives the focus back
		await page.keyboard.press('Enter');
		const panel = page.getByTestId('oc-panel');
		await expect(panel.getByRole('heading', { name: 'Backend' })).toBeVisible();
		await expect(panel.getByTestId('oc-rules')).toContainText('Kernarbeitszeit');
		await expect(panel.getByRole('link', { name: 'Gruppe verwalten' })).toBeVisible();
		await page.keyboard.press('Escape');
		await expect(panel).toHaveCount(0);
		await expect(backend).toBeFocused();

		await backend.locator('[data-testid="oc-node"]').first().click();
		await panel.getByTestId('oc-group-calendar').click();
		await expect(page).toHaveURL(/absences\?group=\d+/);
		await expect(page.locator('#calendar-group-filter option:checked')).toHaveText(/Backend/);
		await page.getByLabel('Jahr').selectOption(String(year));
		await expect(page.locator('li', { hasText: 'Jonas Wolf' }).first()).toBeVisible();
		await expect(page.locator('li', { hasText: 'Lisa Braun' })).toHaveCount(0);

		// "Gruppe verwalten" lands on the groups page with that group open
		await page.goto(`/organizations/${SLUG}/chart`);
		await tree.getByRole('treeitem', { name: /^Backend,/ }).locator('[data-testid="oc-node"]').first().click();
		await panel.getByRole('link', { name: 'Gruppe verwalten' }).click();
		await expect(page).toHaveURL(/groups\?group=\d+/);
		await expect(page.locator('[data-testid="group-header"][aria-expanded="true"]')).toContainText('Backend');
	} finally {
		await removeAbsences(request, 'jonas.wolf@example.com', day);
		await removeAbsences(request, 'lisa.braun@example.com', day);
	}
});

/** A member with colleagues hidden outside their groups: those are not in the chart at all, the card shows only what is shared. */
test('the Organigramm respects the visibility matrix for members', async ({ page, request }) => {
	const headers = { Authorization: `Bearer ${await apiToken(request, 'max.mueller@example.com')}` };
	const org = await (await request.get(`${API}/organizations/${SLUG}`, { headers })).json();
	const original = org.visibility as { kind: string; inGroups: boolean; outsideGroups: boolean }[];
	const settings = (rows: { kind: string; inGroups: boolean; outsideGroups: boolean }[]) =>
		request.put(`${API}/Organizations/${SLUG}/settings`, { headers, data: { visibility: rows } });

	try {
		const set = await settings([
			{ kind: 'Member', inGroups: true, outsideGroups: false },
			{ kind: 'Email', inGroups: false, outsideGroups: false },
			{ kind: 'Presence', inGroups: false, outsideGroups: false },
			{ kind: 'TrackedTime', inGroups: false, outsideGroups: false }
		]);
		expect(set.ok(), await set.text()).toBeTruthy();

		await login(page, 'tom.weber@example.com');
		await page.goto(`/organizations/${SLUG}/chart`);
		const tree = page.getByRole('tree');
		await expect(tree.getByRole('treeitem', { name: /^Felix Koch/ }).first()).toBeVisible();
		await expect(tree.getByRole('treeitem', { name: /^Lisa Braun/ })).toHaveCount(0);
		await expect(tree.getByRole('treeitem', { name: /^Frontend, 0 Personen/ })).toBeVisible();

		await tree.getByRole('treeitem', { name: /^Felix Koch/ }).first().locator('[data-testid="oc-node"]').first().click();
		const panel = page.getByTestId('oc-panel');
		await expect(panel.getByRole('heading', { name: 'Felix Koch' })).toBeVisible();
		await expect(panel.getByTestId('oc-email')).toHaveCount(0);
		await expect(panel.getByTestId('oc-person-groups')).toContainText('Backend');
		await expect(panel.getByTestId('oc-no-details')).toBeVisible();
		await expect(panel.getByTestId('oc-person-balance')).toHaveCount(0);
	} finally {
		await settings(original);
	}
});

test('on a phone the Organigramm is an indented list without horizontal scrolling, details in a bottom sheet', async ({ page }) => {
	await page.setViewportSize({ width: 390, height: 844 });
	await login(page, 'anna.schmidt@example.com');
	await page.goto(`/organizations/${SLUG}/chart`);
	const tree = page.getByRole('tree');
	await expect(tree.getByRole('treeitem', { name: /^Lisa Braun/ })).toBeVisible();
	expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(390);

	await page.getByRole('searchbox').fill('jana');
	await expect(page.getByText('1 Treffer')).toBeVisible();
	await tree.getByRole('treeitem', { name: /^Jana Meyer/ }).locator('[data-testid="oc-node"]').click();
	const sheet = page.getByRole('dialog', { name: 'Jana Meyer' });
	await expect(sheet).toBeVisible();
	await expect(sheet.getByTestId('oc-person-balance')).toBeVisible();
	await expect(sheet.getByRole('link', { name: 'Abwesenheiten im Teamkalender' })).toBeVisible();
	await page.keyboard.press('Escape');
	await expect(sheet).toBeHidden();
	expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(390);
});

test('switching to English translates the Organigramm', async ({ page }) => {
	await login(page, 'tom.weber@example.com');
	await page.goto(`/organizations/${SLUG}/chart`);
	await expect(page.getByRole('tree', { name: /Organigramm/ })).toBeVisible();
	await page.getByRole('button', { name: 'EN', exact: true }).first().click();
	await expect(page.getByRole('button', { name: 'Collapse all' })).toBeVisible();
	await page.getByRole('button', { name: 'DE', exact: true }).first().click();
	await expect(page.getByRole('button', { name: 'Alle zuklappen' })).toBeVisible();
});
