import { expect, test } from '@playwright/test';
import { API, apiToken, login } from './helpers';

/**
 * History: add a forgotten entry for the selected day (Tom's org allows editing past
 * entries, so it is created directly), see it in the day list with its history, delete it.
 */
test('history: add a missed entry for the selected day', async ({ page }) => {
	const note = `E2E nachgetragen ${Date.now()}`;
	// A random early-morning slot so reruns never overlap an earlier entry
	const minute = Math.floor(Math.random() * 50);
	const hour = 1 + Math.floor(Math.random() * 4);
	const pad = (n: number) => String(n).padStart(2, '0');

	await login(page, 'tom.weber@example.com');
	await page.goto('/history');
	// Last month is always in the past
	await page.getByRole('button', { name: 'Vorheriger Monat' }).click();
	const day = await page.evaluate(() => {
		const d = new Date();
		const prev = new Date(d.getFullYear(), d.getMonth() - 1, 15);
		return `${prev.getFullYear()}-${String(prev.getMonth() + 1).padStart(2, '0')}-15`;
	});
	await page.locator(`[data-day="${day}"]`).click();
	const details = page.getByTestId('day-details');
	await details.getByRole('button', { name: 'Eintrag nachtragen' }).click();

	const form = details.getByRole('form');
	await form.getByLabel('Beginn').fill(`${day}T${pad(hour)}:${pad(minute)}`);
	await form.getByLabel('Ende').fill(`${day}T${pad(hour)}:${pad(minute + 9)}`);
	await form.getByLabel('Notiz').fill(note);
	await form.getByRole('button', { name: 'Speichern' }).click();

	await expect(page.getByText('Zeiteintrag nachgetragen.')).toBeVisible();
	const row = details.locator('li', { hasText: note });
	await expect(row).toBeVisible();

	// The editor shows the entry's history: created by Tom
	await row.getByRole('button', { name: 'Zeiteintrag bearbeiten' }).click();
	await details.getByText('Verlauf').click();
	await expect(details.getByText('Erstellt', { exact: false }).first()).toBeVisible();
	await details.getByRole('button', { name: 'Abbrechen' }).click();

	// Clean up
	await details.locator('li', { hasText: note }).getByRole('button', { name: 'Zeiteintrag löschen' }).click();
	await page.getByRole('dialog').getByRole('button', { name: 'Löschen' }).click();
	await expect(details.getByText(note)).toHaveCount(0);
});

test('member detail: the owner adds, corrects and deletes an entry of a member', async ({ page, request }) => {
	const note = `E2E Korrektur ${Date.now()}`;
	const minute = Math.floor(Math.random() * 50);
	const pad = (n: number) => String(n).padStart(2, '0');
	const token = await apiToken(request, 'max.mueller@example.com');
	const org = await (await request.get(`${API}/organizations/mueller-software`, { headers: { Authorization: `Bearer ${token}` } })).json();
	const tom = org.members.find((m: { email: string }) => m.email === 'tom.weber@example.com');

	await login(page, 'max.mueller@example.com');
	await page.goto(`/organizations/mueller-software/members/${tom.id}`);
	await page.getByRole('button', { name: 'Vorheriger Monat' }).click();
	const day = await page.evaluate(() => {
		const d = new Date();
		const prev = new Date(d.getFullYear(), d.getMonth() - 1, 14);
		return `${prev.getFullYear()}-${String(prev.getMonth() + 1).padStart(2, '0')}-14`;
	});
	await page.locator(`[data-day="${day}"]`).click();
	const details = page.getByTestId('day-details');
	await details.getByRole('button', { name: 'Eintrag hinzufügen' }).click();
	const form = details.getByRole('form');
	await form.getByLabel('Beginn').fill(`${day}T05:${pad(minute)}`);
	await form.getByLabel('Ende').fill(`${day}T05:${pad(minute + 5)}`);
	await form.getByLabel('Notiz').fill(note);
	await form.getByRole('button', { name: 'Speichern' }).click();
	const row = details.locator('li', { hasText: note });
	await expect(row).toBeVisible();

	await row.getByRole('button', { name: 'Zeiteintrag bearbeiten' }).click();
	await details.getByLabel('Notiz').fill(`${note} korrigiert`);
	await details.getByRole('button', { name: 'Speichern' }).click();
	await expect(page.getByText('Zeiteintrag gespeichert.')).toBeVisible();
	await expect(details.getByText(`${note} korrigiert`)).toBeVisible();

	await details.locator('li', { hasText: note }).getByRole('button', { name: 'Zeiteintrag löschen' }).click();
	await page.getByRole('dialog').getByRole('button', { name: 'Löschen' }).click();
	await expect(details.getByText(note)).toHaveCount(0);
});

/**
 * #52: a group admin sees Tom's entries (view-all right) but gets the correction controls only once
 * the group may approve time edits — the page follows the backend's `canCorrectEntries`.
 */
test('member detail: a group admin corrects entries only with the time-edit right', async ({ page, request }) => {
	const SLUG = 'mueller-software';
	const headers = { Authorization: `Bearer ${await apiToken(request, 'max.mueller@example.com')}` };
	const org = await (await request.get(`${API}/organizations/${SLUG}`, { headers })).json();
	const userId = (email: string): number => org.members.find((m: { email: string }) => m.email === email).id;
	const group = await (await request.post(`${API}/organizations/${SLUG}/groups`, { headers, data: { name: `E2E Korrektur ${Date.now()}` } })).json();
	const perms = (data: object) => request.put(`${API}/organizations/${SLUG}/groups/${group.id}/permissions`, { headers, data });
	try {
		expect((await perms({ groupAdminCanViewAllTimeEntries: true })).ok()).toBeTruthy();
		for (const [uid, role] of [[userId('tom.weber@example.com'), 'Member'], [userId('niklas.hoffmann@example.com'), 'Admin']] as const) {
			const added = await request.post(`${API}/organizations/${SLUG}/groups/${group.id}/members`, { headers, data: { userId: uid, role } });
			expect(added.ok(), await added.text()).toBeTruthy();
		}

		await login(page, 'niklas.hoffmann@example.com');
		const openDay = async () => {
			await page.goto(`/organizations/${SLUG}/members/${userId('tom.weber@example.com')}`);
			await page.getByRole('button', { name: 'Vorheriger Monat' }).click();
			await page.locator('[data-day$="-14"]').first().click();
			return page.getByTestId('day-details');
		};
		const details = await openDay();
		await expect(details).toBeVisible();
		await expect(details.getByRole('button', { name: 'Eintrag hinzufügen' })).toHaveCount(0);

		expect((await perms({ groupAdminCanApproveTimeEdits: true })).ok()).toBeTruthy();
		await expect((await openDay()).getByRole('button', { name: 'Eintrag hinzufügen' })).toBeVisible();
	} finally {
		await request.delete(`${API}/organizations/${SLUG}/groups/${group.id}`, { headers });
	}
});

/** #54: the owner sets a member's personnel number on the member page and removes it again. */
test('member detail: the owner sets and clears a personnel number', async ({ page, request }) => {
	const headers = { Authorization: `Bearer ${await apiToken(request, 'max.mueller@example.com')}` };
	const org = await (await request.get(`${API}/organizations/mueller-software`, { headers })).json();
	const tom = org.members.find((m: { email: string }) => m.email === 'tom.weber@example.com');
	const number = `E2E-${Date.now()}`;

	await login(page, 'max.mueller@example.com');
	await page.goto(`/organizations/mueller-software/members/${tom.id}`);
	const card = page.locator('section', { has: page.getByRole('heading', { name: 'Personalnummer' }) });
	await card.getByRole('button', { name: 'Bearbeiten' }).click();
	await card.getByLabel('Personalnummer').fill(number);
	await card.getByRole('button', { name: 'Speichern' }).click();
	await expect(card.getByText(number)).toBeVisible();

	await card.getByRole('button', { name: 'Bearbeiten' }).click();
	await card.getByLabel('Personalnummer').fill('');
	await card.getByRole('button', { name: 'Speichern' }).click();
	await expect(card.getByText('Keine Personalnummer hinterlegt.')).toBeVisible();
});
