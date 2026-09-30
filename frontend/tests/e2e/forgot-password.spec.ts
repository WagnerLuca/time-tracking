import { expect, test } from '@playwright/test';

/** Login → "Passwort vergessen?" → neutral confirmation (the dev backend only logs the mail). */
test('forgot password shows the neutral confirmation', async ({ page }) => {
	await page.goto('/login');
	await page.waitForLoadState('networkidle');
	await page.getByRole('link', { name: 'Passwort vergessen?' }).click();
	await expect(page).toHaveURL(/\/forgot-password/);

	await page.locator('input[type="email"]').fill('tom.weber@example.com');
	await page.getByRole('button', { name: 'Link senden' }).click();
	await expect(page.getByText('Falls ein Konto mit dieser Adresse existiert')).toBeVisible();
});
