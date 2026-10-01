import { expect, test } from '@playwright/test';
import { apiToken, login, myOrgs, submitLogin } from './helpers';

/**
 * Invitations. The real token only exists in the invitation email, so the accept call is
 * mocked; everything around it (redirect to login, coming back, toast, navigation, error
 * codes) runs against the real app.
 */
test('admin invites by email and revokes the pending invitation', async ({ page }) => {
	const email = `e2e-invite-${Date.now()}@example.com`;
	await login(page, 'max.mueller@example.com');
	await page.goto('/organizations/mueller-software/members');

	await page.getByLabel('E-Mail-Adresse').fill(email);
	await page.getByRole('button', { name: 'Einladen', exact: true }).click();
	const pending = page.getByTestId('pending-invitations');
	await expect(pending.getByText(email)).toBeVisible();

	await page.getByRole('button', { name: `Einladung an ${email} zurückziehen` }).click();
	await page.getByRole('dialog').getByRole('button', { name: 'Zurückziehen' }).click();
	await expect(page.getByText(email)).toHaveCount(0);
});

test('invite link: sign in first, then the invitation is accepted', async ({ page, request }) => {
	const org = (await myOrgs(request, await apiToken(request, 'tom.weber@example.com')))[0];
	let acceptedToken = '';
	await page.route('**/api/v1/invitations/accept', async (route) => {
		acceptedToken = route.request().postDataJSON().token;
		await route.fulfill({
			json: { organizationId: org.organizationId, organizationSlug: org.slug, organizationName: org.name, role: 'Member' }
		});
	});

	await page.goto('/invite?token=abc123');
	await expect(page).toHaveURL(/\/login\?next=/);
	await expect(page.getByText('Du wurdest in eine Organisation eingeladen.', { exact: false })).toBeVisible();
	await submitLogin(page, 'tom.weber@example.com');

	await expect(page).toHaveURL(new RegExp(`/organizations/${org.slug}$`), { timeout: 15_000 });
	await expect(page.getByText(`Willkommen bei ${org.name}!`)).toBeVisible();
	expect(acceptedToken).toBe('abc123');
});

test('invite link for another address explains how to switch accounts', async ({ page }) => {
	await page.route('**/api/v1/invitations/accept', (route) =>
		route.fulfill({
			status: 403,
			json: { title: 'Forbidden', status: 403, code: 'invitation.wrongEmail', detail: 'This invitation was sent to a different email address.' }
		})
	);
	await login(page, 'tom.weber@example.com');
	await page.goto('/invite?token=abc123');
	await expect(page.getByText('Diese Einladung wurde an eine andere E-Mail-Adresse geschickt.', { exact: false })).toBeVisible();
	await expect(page.getByText('Du bist als tom.weber@example.com angemeldet.')).toBeVisible();
	await page.getByRole('button', { name: 'Mit einem anderen Konto anmelden' }).click();
	await expect(page).toHaveURL(/\/login\?next=%2Finvite%3Ftoken%3Dabc123/);
});
