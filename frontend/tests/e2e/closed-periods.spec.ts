import { expect, test } from '@playwright/test';
import { API, apiToken, login } from './helpers';

/**
 * #57: the admin closes last month in Allgemein; Tom sees his entry there locked (no edit) and the
 * API refuses him; the admin corrects it with a reason and the entry's history shows it.
 * Last month is reopened and the entry deleted afterwards, so the spec stays re-runnable.
 */
test('closed periods: admin closes last month, member is locked out, admin edits with a reason', async ({ page, browser, request }) => {
	const SLUG = 'mueller-software';
	const headers = { Authorization: `Bearer ${await apiToken(request, 'anna.schmidt@example.com')}` };
	const tomHeaders = { Authorization: `Bearer ${await apiToken(request, 'tom.weber@example.com')}` };
	const org = await (await request.get(`${API}/organizations/${SLUG}`, { headers })).json();
	const tomId: number = org.members.find((m: { email: string }) => m.email === 'tom.weber@example.com').id;

	const now = new Date();
	const prev = new Date(now.getFullYear(), now.getMonth() - 1, 13);
	const month = `${prev.getFullYear()}-${String(prev.getMonth() + 1).padStart(2, '0')}`;
	const day = `${month}-13`;
	const minute = String(Math.floor(Math.random() * 50)).padStart(2, '0');
	const note = `E2E abgeschlossen ${Date.now()}`;
	const created = await request.post(`${API}/organizations/${SLUG}/members/${tomId}/entries`, {
		headers, data: { startTime: `${day}T02:${minute}:00Z`, endTime: `${day}T02:${String(Number(minute) + 6).padStart(2, '0')}:00Z`, description: note }
	});
	expect(created.ok(), await created.text()).toBeTruthy();
	const entryId: number = (await created.json()).id;

	try {
		// Anna closes last month (the form defaults to it)
		await login(page, 'anna.schmidt@example.com');
		await page.goto(`/organizations/${SLUG}/accounting?tab=closing`);
		const panel = page.getByTestId('closed-periods');
		await expect(panel.getByTestId('close-to')).toHaveValue(month);
		await panel.getByRole('button', { name: 'Abschließen' }).click();
		await page.getByRole('dialog').getByRole('button', { name: 'Abschließen' }).click();
		await expect(page.getByText('Zeitraum abgeschlossen.')).toBeVisible();
		await expect(panel.getByRole('button', { name: /^Wieder öffnen: / })).toHaveCount(1);
		await expect(panel.getByTestId('closed-periods-log')).toContainText('abgeschlossen');

		// Tom: the day is marked, his entry has no edit controls, and the API refuses him
		const tomContext = await browser.newContext();
		const tom = await tomContext.newPage();
		await login(tom, 'tom.weber@example.com');
		await tom.goto(`/history?date=${day}`);
		const tomDetails = tom.getByTestId('day-details');
		await expect(tomDetails.getByTestId('closed-day-notice')).toContainText('nur noch durch Admins');
		const tomRow = tomDetails.locator('li', { hasText: note });
		await expect(tomRow.getByTestId('closed-lock')).toHaveCount(1);
		await expect(tomRow.getByRole('button', { name: 'Zeiteintrag bearbeiten' })).toHaveCount(0);
		await expect(tom.locator(`[data-day="${day}"]`).getByTestId('closed-lock')).toHaveCount(1);
		const refused = await request.put(`${API}/TimeTracking/${entryId}`, { headers: tomHeaders, data: { description: 'nope' } });
		expect(refused.status()).toBe(409);
		expect((await refused.json()).code).toBe('period.closed');
		await tomContext.close();

		// Anna corrects it on Tom's page: the save asks for a reason, the history shows it
		await page.goto(`/organizations/${SLUG}/members/${tomId}`);
		await page.getByRole('button', { name: 'Vorheriger Monat' }).click();
		await page.locator(`[data-day="${day}"]`).click();
		const details = page.getByTestId('day-details');
		await expect(details.getByTestId('closed-day-notice')).toContainText('fragen nach einem Grund');
		const row = details.locator('li', { hasText: note });
		await row.getByRole('button', { name: 'Zeiteintrag bearbeiten' }).click();
		await details.getByLabel('Notiz').fill(`${note} korrigiert`);
		await details.getByRole('button', { name: 'Speichern' }).click();
		const prompt = page.getByRole('dialog');
		await expect(prompt).toContainText('ist abgeschlossen');
		await prompt.getByTestId('prompt-input').fill('Nachtrag nach Lohnabrechnung');
		await prompt.getByRole('button', { name: 'Mit Grund speichern' }).click();
		await expect(page.getByText('Zeiteintrag gespeichert.')).toBeVisible();

		await details.locator('li', { hasText: `${note} korrigiert` }).getByRole('button', { name: 'Zeiteintrag bearbeiten' }).click();
		await details.getByText('Verlauf', { exact: true }).click();
		const flagged = details.getByTestId('history-closed');
		await expect(flagged).toContainText('im abgeschlossenen Zeitraum');
		await expect(flagged).toContainText('Grund: Nachtrag nach Lohnabrechnung');
		await details.getByRole('button', { name: 'Abbrechen' }).click();

		// The log lists the admin change with its reason
		await page.goto(`/organizations/${SLUG}/accounting?tab=closing`);
		await expect(page.getByTestId('closed-periods-log').locator('li').first()).toContainText('Admin-Änderung');
		await expect(page.getByTestId('closed-periods-log').locator('li').first()).toContainText('Nachtrag nach Lohnabrechnung');
	} finally {
		await request.post(`${API}/organizations/${SLUG}/closed-periods/reopen`, { headers, data: { month: `${month}-01`, reason: 'E2E aufräumen' } });
		await request.delete(`${API}/organizations/${SLUG}/members/${tomId}/entries/${entryId}`, { headers });
	}
});
