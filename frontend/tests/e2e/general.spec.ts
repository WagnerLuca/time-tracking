import { expect, test } from '@playwright/test';
import { API, apiToken, login } from './helpers';

/** "Allgemein": the owner edits a fresh organization's profile and time settings, then deletes it with the typed name. */
test('owner edits and deletes an organization in the general section', async ({ page, request }) => {
	const stamp = Date.now();
	const name = `E2E Allgemein ${stamp}`;
	const slug = `e2e-general-${stamp}`;
	const token = await apiToken(request, 'max.mueller@example.com');
	const headers = { Authorization: `Bearer ${token}` };
	expect((await request.post(`${API}/Organizations`, { headers, data: { name, slug } })).ok()).toBeTruthy();

	await login(page, 'max.mueller@example.com');
	await page.goto(`/organizations/${slug}`);
	// The old edit/delete buttons are gone; "Allgemein" is the first section tab
	await expect(page.getByRole('button', { name: 'Löschen' })).toHaveCount(0);
	const tabs = page.getByRole('navigation', { name: 'Organisation' }).getByRole('link');
	await expect(tabs.first()).toHaveText('Allgemein');
	await tabs.first().click();
	await expect(page).toHaveURL(new RegExp(`/organizations/${slug}/general$`));

	await page.getByLabel('Beschreibung').fill('Mit Beschreibung');
	await page.getByLabel('Ende des Arbeitstags').fill('04:00');
	await page.getByRole('button', { name: 'Änderungen speichern' }).click();
	await expect(page.getByText('Organisation gespeichert.')).toBeVisible();
	const org = await (await request.get(`${API}/Organizations/${slug}`, { headers })).json();
	expect(org.description).toBe('Mit Beschreibung');
	expect(org.businessDayEnd).toBe('04:00');

	const danger = page.getByTestId('danger-zone');
	const remove = danger.getByRole('button', { name: 'Organisation löschen' });
	await expect(remove).toBeDisabled();
	await danger.getByLabel(/Zur Bestätigung/).fill(name);
	await remove.click();
	await expect(page).toHaveURL(/\/organizations$/);
	expect((await request.get(`${API}/Organizations/${slug}`, { headers })).status()).toBe(404);
});

test('members do not see the general section', async ({ page }) => {
	await login(page, 'tom.weber@example.com');
	await page.goto('/organizations/mueller-software/general');
	await expect(page).toHaveURL(/\/organizations\/mueller-software$/);
	await expect(page.getByRole('navigation', { name: 'Organisation' }).getByRole('link', { name: 'Allgemein' })).toHaveCount(0);
});
