import { expect, test } from '@playwright/test';
import { API, apiToken, login } from './helpers';

/**
 * Decline with a reason: Tom requests an absence (his org requires approval), the owner
 * declines it with a reason, and Tom sees that reason with the decision in the bell.
 */
test('admin declines a request with a reason the member then sees', async ({ page, request }) => {
	const stamp = Date.now();
	const note = `E2E Antrag ${stamp}`;
	const reason = `Zu kurzfristig ${stamp}`;
	// A date far ahead, different per run, so the request is never a duplicate
	const day = new Date(2030, 0, 1 + (stamp % 300));
	const date = `${day.getFullYear()}-${String(day.getMonth() + 1).padStart(2, '0')}-${String(day.getDate()).padStart(2, '0')}`;

	const token = await apiToken(request, 'tom.weber@example.com');
	const created = await request.post(`${API}/organizations/mueller-software/requests`, {
		headers: { Authorization: `Bearer ${token}` },
		data: { type: 6, message: note, requestData: JSON.stringify({ dates: [date], type: 1, isHalfDay: false, note }) }
	});
	expect(created.ok(), await created.text()).toBeTruthy();

	await login(page, 'max.mueller@example.com');
	await page.goto('/organizations/mueller-software/settings');
	const item = page.locator('li', { hasText: note }).filter({ has: page.getByRole('button', { name: 'Ablehnen' }) });
	await item.getByRole('button', { name: 'Ablehnen' }).click();
	await item.getByLabel('Begründung (optional)').fill(reason);
	await item.getByRole('button', { name: 'Ablehnen', exact: true }).click();
	await expect(page.locator('li', { hasText: note }).getByText(`Begründung: ${reason}`)).toBeVisible();

	// Tom: the decision with its reason, once, in "Meine Anträge"
	await page.context().clearCookies();
	await page.evaluate(() => localStorage.clear());
	await login(page, 'tom.weber@example.com');
	await page.getByRole('button', { name: /Benachrichtigungen öffnen/ }).click();
	await expect(page.getByText(`Begründung: ${reason}`)).toHaveCount(1);
});
