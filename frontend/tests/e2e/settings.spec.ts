import { expect, test } from '@playwright/test';
import { API } from './helpers';

/** Changing the password ends every session: the app signs out cleanly and asks for the new password. */
test('settings: a password change signs out and the new password works', async ({ page, request }) => {
	const email = `e2e-pw-${Date.now()}@example.com`;
	const oldPassword = 'Altes-Passwort-2026';
	const newPassword = 'Neues-Passwort-2026';
	const res = await request.post(`${API}/Auth/register`, { data: { email, password: oldPassword, firstName: 'E2E', lastName: 'Passwort' } });
	expect(res.ok(), await res.text()).toBeTruthy();

	const signIn = async (password: string) => {
		await page.waitForLoadState('networkidle');
		await page.locator('input[type="email"]').fill(email);
		await page.locator('input[type="password"]').fill(password);
		await page.locator('form button[type="submit"]').click();
		await expect(page).not.toHaveURL(/\/login/, { timeout: 15_000 });
	};
	// Signed in with the registration's session (saves a login against the auth rate limit)
	const reg = await res.json();
	await page.goto('/login');
	await page.evaluate((r) => {
		localStorage.setItem('authToken', r.accessToken);
		localStorage.setItem('refreshToken', r.refreshToken);
		localStorage.setItem('authUser', JSON.stringify(r.user));
	}, reg);
	await page.goto('/settings');
	await page.getByLabel('Aktuelles Passwort').first().fill(oldPassword);
	await page.getByLabel('Neues Passwort', { exact: true }).fill(newPassword);
	await page.getByLabel('Neues Passwort wiederholen').fill(newPassword);
	await page.getByRole('button', { name: 'Passwort ändern' }).click();

	await expect(page).toHaveURL(/\/login/);
	await expect(page.getByText('Passwort geändert.', { exact: false })).toBeVisible();

	await signIn(newPassword);
});
