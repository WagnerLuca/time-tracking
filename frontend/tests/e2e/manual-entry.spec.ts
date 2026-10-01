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
