import { expect, type APIRequestContext, type Page } from '@playwright/test';

export const API = process.env.E2E_API_URL ?? 'http://localhost:7000/api/v1';
export const PASSWORD = 'Password123';

/** Fill and submit the login form (the page must already show it). */
export async function submitLogin(page: Page, email: string) {
	// client-only app: wait until Svelte has mounted before typing
	await page.waitForLoadState('networkidle');
	await page.locator('input[type="email"]').fill(email);
	await page.locator('input[type="password"]').fill(PASSWORD);
	await page.locator('form button[type="submit"]').click();
	await expect(page).not.toHaveURL(/\/login/, { timeout: 15_000 });
}

/** Log in through the UI; a user with several orgs and none selected picks the first. */
export async function login(page: Page, email: string) {
	await page.goto('/login');
	await submitLogin(page, email);
	if (page.url().includes('/select-org')) await page.locator('main button').first().click();
}

/** A bearer token straight from the API, for test setup. */
export async function apiToken(request: APIRequestContext, email: string): Promise<string> {
	const res = await request.post(`${API}/Auth/login`, { data: { email, password: PASSWORD } });
	expect(res.ok()).toBeTruthy();
	return (await res.json()).accessToken;
}

/** The organizations the user belongs to (slug, id, role). */
export async function myOrgs(request: APIRequestContext, token: string) {
	const res = await request.get(`${API}/Organizations/mine`, { headers: { Authorization: `Bearer ${token}` } });
	expect(res.ok()).toBeTruthy();
	return (await res.json()) as { organizationId: number; slug: string; name: string; role: string }[];
}
