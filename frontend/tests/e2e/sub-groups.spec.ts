import { expect, test, type Page } from '@playwright/test';
import { API, apiToken, futureWorkday, login, myOrgs } from './helpers';

const SLUG = 'mueller-software';

/** The ModeSelector row of one setting on the open group's "Berechtigungen" tab. */
function permRow(page: Page, label: string) {
	return page
		.locator('div')
		.filter({ has: page.getByText(label, { exact: true }) })
		.filter({ has: page.getByRole('button', { name: 'Erben' }) })
		.last();
}

/**
 * Sub-groups (#59): the owner builds department → team in the tree and sets two rules on the
 * department (past entries need approval, its admins approve time edits). Anna, in the team,
 * inherits them: her forgotten-day entry becomes a request, routed past the admin-less team to
 * the department, whose admin (Tom, a plain member) approves it from the bell.
 * Anna's seeded group only approves absences, so it never takes this request. (Anna and Tom are
 * already signed in by other specs: no extra login against the 10-per-minute auth limit.)
 */
test('a department rule reaches its team, and the department admin approves a team member', async ({ page, request }) => {
	const stamp = Date.now();
	const dept = `E2E Abteilung ${stamp}`;
	const team = `E2E Team ${stamp}`;
	const description = `E2E Nachtrag ${stamp}`;
	const day = futureWorkday(2001, stamp);   // a past day, different per run
	const headers = { Authorization: `Bearer ${await apiToken(request, 'max.mueller@example.com')}` };
	const annaHeaders = { Authorization: `Bearer ${await apiToken(request, 'anna.schmidt@example.com')}` };
	const org = await (await request.get(`${API}/organizations/${SLUG}`, { headers })).json();
	const userId = (email: string): number => org.members.find((m: { email: string }) => m.email === email).id;
	const [annaId, tomId] = [userId('anna.schmidt@example.com'), userId('tom.weber@example.com')];
	const groupId = async (name: string) =>
		((await (await request.get(`${API}/organizations/${SLUG}/groups`, { headers })).json()) as { id: number; name: string }[])
			.find((g) => g.name === name)?.id;
	const header = (name: string) => page.getByTestId('group-header').filter({ hasText: name });
	const annaRule = async () =>
		(await (await request.get(`${API}/organizations/${SLUG}/effective-permissions`, { headers: annaHeaders })).json()).editPastEntriesMode;

	try {
		// Owner: department, then a team below it via "+ Untergruppe" (parent preselected)
		await login(page, 'max.mueller@example.com');
		await page.goto(`/organizations/${SLUG}/groups`);
		await page.getByRole('button', { name: '+ Gruppe erstellen' }).click();
		await page.getByLabel('Name').fill(dept);
		await page.getByRole('button', { name: 'Erstellen', exact: true }).click();
		await header(dept).click();
		await page.getByRole('button', { name: '+ Untergruppe' }).click();
		await expect(page.getByLabel('Übergeordnete Gruppe')).toHaveValue(String(await groupId(dept)));
		await page.getByLabel('Name').fill(team);
		await page.getByRole('button', { name: 'Erstellen', exact: true }).click();
		const teamNode = page.locator('[data-testid="group-node"][data-depth="2"]', { hasText: team });
		await expect(teamNode).toBeVisible();
		await page.getByRole('button', { name: `${dept} zuklappen` }).click();
		await expect(teamNode).toHaveCount(0);
		await page.getByRole('button', { name: `${dept} aufklappen` }).click();
		await expect(teamNode).toBeVisible();

		const deptId = await groupId(dept);
		const teamId = await groupId(team);
		for (const [id, uid, role] of [[deptId, tomId, 'Admin'], [teamId, annaId, 'Member']] as const) {
			const added = await request.post(`${API}/organizations/${SLUG}/groups/${id}/members`, { headers, data: { userId: uid, role } });
			expect(added.ok(), await added.text()).toBeTruthy();
		}
		expect(await annaRule()).toBe('Allowed');

		// Department rules: past entries need approval, its admins approve time edits
		await page.goto(`/organizations/${SLUG}/groups`);
		await header(dept).click();
		await page.getByRole('tab', { name: 'Berechtigungen' }).click();
		const pastRow = permRow(page, 'Vergangene Einträge ändern');
		await pastRow.getByRole('button', { name: 'Genehmigung' }).click();
		await expect(pastRow.getByRole('button', { name: 'Genehmigung' })).toHaveAttribute('aria-pressed', 'true');
		const approveRow = permRow(page, 'Zeitänderungen genehmigen');
		await approveRow.getByRole('button', { name: 'An' }).click();
		await expect(approveRow.getByRole('button', { name: 'An' })).toHaveAttribute('aria-pressed', 'true');

		// The team shows where its values come from
		await header(team).click();
		await page.getByRole('tab', { name: 'Berechtigungen' }).click();
		await expect(permRow(page, 'Vergangene Einträge ändern').getByText(`geerbt von ${dept}: Genehmigung erforderlich`)).toBeVisible();
		await expect(permRow(page, 'Zeitänderungen genehmigen').getByText(`geerbt von ${dept}: An`)).toBeVisible();

		// Anna is affected: her forgotten day becomes a request, and it goes to the department
		expect(await annaRule()).toBe('RequiresApproval');
		const orgId = (await myOrgs(request, await apiToken(request, 'anna.schmidt@example.com'))).find((o) => o.slug === SLUG)!.organizationId;
		const manual = await request.post(`${API}/TimeTracking/manual`, {
			headers: annaHeaders,
			data: { organizationId: orgId, startTime: `${day}T07:00:00Z`, endTime: `${day}T11:00:00Z`, description }
		});
		expect(manual.ok(), await manual.text()).toBeTruthy();
		const req = (await manual.json()).request;
		expect(req.groupName).toBe(dept);

		// The department admin approves it from the bell
		await page.context().clearCookies();
		await page.evaluate(() => localStorage.clear());
		await login(page, 'tom.weber@example.com');
		await page.getByRole('button', { name: /Benachrichtigungen öffnen/ }).click();
		await page.getByRole('button', { name: /Anna Schmidt/ }).filter({ hasText: `über ${dept}` }).click();
		await page.getByRole('dialog').getByRole('button', { name: 'Annehmen' }).click();
		await expect(page.getByRole('dialog')).toHaveCount(0);
		const mine = await (await request.get(`${API}/organizations/my-requests`, { headers: annaHeaders })).json();
		expect(mine.items.find((r: { id: number }) => r.id === req.id).status).toBe('Accepted');
	} finally {
		const deptId = await groupId(dept);
		if (deptId) await request.delete(`${API}/organizations/${SLUG}/groups/${deptId}?subGroups=1`, { headers });
		const entries = await (await request.get(`${API}/Organizations/${SLUG}/member-entries/${annaId}?from=${day}T00:00:00Z&to=${day}T23:59:59Z`, { headers })).json();
		for (const e of (entries.items ?? []) as { id: number; description?: string }[])
			if (e.description === description) await request.delete(`${API}/organizations/${SLUG}/members/${annaId}/entries/${e.id}`, { headers });
	}
});
