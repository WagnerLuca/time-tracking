import { expect, test } from '@playwright/test';
import { API, apiToken, login } from './helpers';

/**
 * Decline with a reason: Tom requests an absence (his org requires approval), the owner
 * declines it with a reason in the inbox, and Tom sees that reason with the decision in his inbox.
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
	await page.getByRole('link', { name: /Benachrichtigungen öffnen/ }).click();
	await expect(page).toHaveURL(/\/inbox$/);
	const item = page.getByTestId('inbox-approve').getByTestId('inbox-item').filter({ hasText: note });
	await item.getByRole('button', { name: 'Ablehnen' }).click();
	await item.getByLabel('Begründung (optional)').fill(reason);
	await item.getByRole('button', { name: 'Ablehnen', exact: true }).click();
	await expect(page.getByText('Antrag von Tom Weber abgelehnt.')).toBeVisible();
	await expect(item).toHaveCount(0);

	// Tom: the decision with its reason, once, under "Antworten auf meine Anträge"
	await page.context().clearCookies();
	await page.evaluate(() => localStorage.clear());
	await login(page, 'tom.weber@example.com');
	await page.getByRole('link', { name: /Benachrichtigungen öffnen/ }).click();
	await expect(page.getByTestId('inbox-decisions').getByText(`Begründung: ${reason}`)).toHaveCount(1);
	await expect(page.getByText(`Begründung: ${reason}`)).toHaveCount(1);
});

/** Withdraw: Tom takes back an open absence request in his inbox; the owner no longer has it to approve. */
test('member withdraws an own open request in the inbox', async ({ page, request }) => {
	const stamp = Date.now();
	const note = `E2E Zurückziehen ${stamp}`;
	const day = new Date(2031, 0, 1 + (stamp % 300));
	const date = `${day.getFullYear()}-${String(day.getMonth() + 1).padStart(2, '0')}-${String(day.getDate()).padStart(2, '0')}`;

	const token = await apiToken(request, 'tom.weber@example.com');
	const created = await request.post(`${API}/organizations/mueller-software/requests`, {
		headers: { Authorization: `Bearer ${token}` },
		data: { type: 6, message: note, requestData: JSON.stringify({ dates: [date], type: 1, isHalfDay: false, note }) }
	});
	expect(created.ok(), await created.text()).toBeTruthy();
	const id = (await created.json()).id as number;

	await login(page, 'tom.weber@example.com');
	await page.goto('/inbox');
	const item = page.getByTestId('inbox-own-item').filter({ hasText: note });
	await item.getByRole('button', { name: /zurückziehen/i }).click();
	const dialog = page.getByRole('dialog');
	await expect(dialog.getByText('Antrag zurückziehen?')).toBeVisible();
	await dialog.getByRole('button', { name: 'Zurückziehen' }).click();
	await expect(page.getByText('Antrag zurückgezogen.')).toBeVisible();
	await expect(item).toHaveCount(0);

	// Approvers: no longer pending, listed as withdrawn
	const owner = await apiToken(request, 'max.mueller@example.com');
	const pending = await request.get(`${API}/organizations/notifications`, { headers: { Authorization: `Bearer ${owner}` } });
	expect(((await pending.json()).requests as { id: number }[]).some((r) => r.id === id)).toBeFalsy();
	const all = await request.get(`${API}/organizations/mueller-software/requests?limit=200`, { headers: { Authorization: `Bearer ${owner}` } });
	expect(((await all.json()).items as { id: number; status: string }[]).find((r) => r.id === id)?.status).toBe('Withdrawn');
});
