import { expect, type APIRequestContext, type Page } from '@playwright/test';

export const API = process.env.E2E_API_URL ?? 'http://localhost:7000/api/v1';
export const PASSWORD = 'Password123';

/** Fill and submit the login form (the page must already show it). */
export async function submitLogin(page: Page, email: string, password = PASSWORD) {
	// client-only app: wait until Svelte has mounted before typing
	await page.waitForLoadState('networkidle');
	await page.locator('input[type="email"]').fill(email);
	await page.locator('input[type="password"]').fill(password);
	await page.locator('form button[type="submit"]').click();
	await expect(page).not.toHaveURL(/\/login/, { timeout: 15_000 });
}

interface Session {
	accessToken: string;
	refreshToken: string;
	user: unknown;
}

// One API login per user and test run: the backend allows 10 logins per minute and IP
// (AuthStrict), which a UI login in every test would exceed.
const sessions = new Map<string, Session>();

async function session(request: APIRequestContext, email: string): Promise<Session> {
	const cached = sessions.get(email);
	if (cached) return cached;
	for (let attempt = 0; ; attempt++) {
		const res = await request.post(`${API}/Auth/login`, { data: { email, password: PASSWORD } });
		if (res.status() === 429 && attempt < 2) {
			await new Promise((r) => setTimeout(r, 61_000)); // next rate-limit window
			continue;
		}
		expect(res.ok(), await res.text()).toBeTruthy();
		const data = (await res.json()) as Session;
		sessions.set(email, data);
		return data;
	}
}

/** Signed in as `email` without the login form (the session is put into localStorage like a real login). */
export async function login(page: Page, email: string) {
	const s = await session(page.request, email);
	await page.goto('/login');
	await page.evaluate((s) => {
		localStorage.setItem('authToken', s.accessToken);
		localStorage.setItem('refreshToken', s.refreshToken);
		localStorage.setItem('authUser', JSON.stringify(s.user));
	}, s);
	await page.goto('/');
	await expect(page).not.toHaveURL(/\/login/);
	if (page.url().includes('/select-org')) await page.locator('main button').first().click();
}

/** A bearer token straight from the API, for test setup. */
export async function apiToken(request: APIRequestContext, email: string): Promise<string> {
	return (await session(request, email)).accessToken;
}

/** The organizations the user belongs to (slug, id, role). */
export async function myOrgs(request: APIRequestContext, token: string) {
	const res = await request.get(`${API}/Organizations/mine`, { headers: { Authorization: `Bearer ${token}` } });
	expect(res.ok()).toBeTruthy();
	return (await res.json()) as { organizationId: number; slug: string; name: string; role: string }[];
}

/**
 * A weekday (yyyy-MM-dd) from `firstYear` on that differs per run: Feb/Mar/Jul/Aug/Sep only, so it is
 * never one of the seeded recurring holidays.
 */
export function futureWorkday(firstYear: number, stamp = Date.now()): string {
	const n = Math.floor(stamp / 1000);
	const d = new Date(firstYear + (n % 4), [1, 2, 6, 7, 8][Math.floor(n / 4) % 5], 2 + (Math.floor(n / 20) % 24));
	while (d.getDay() === 0 || d.getDay() === 6) d.setDate(d.getDate() + 1);
	return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

/** Removes every absence of `email` on `date` in the seeded org (as the owner, who may delete any). */
export async function removeAbsences(request: APIRequestContext, email: string, date: string) {
	const headers = { Authorization: `Bearer ${await apiToken(request, 'max.mueller@example.com')}` };
	const org = await (await request.get(`${API}/organizations/mueller-software`, { headers })).json();
	const user = org.members.find((m: { email: string }) => m.email === email);
	const res = await request.get(`${API}/organizations/mueller-software/absences?userId=${user.id}&from=${date}&to=${date}`, { headers });
	expect(res.ok(), await res.text()).toBeTruthy();
	for (const a of (await res.json()).items as { id: number }[]) {
		const del = await request.delete(`${API}/organizations/mueller-software/absences/${a.id}`, { headers });
		expect(del.ok(), await del.text()).toBeTruthy();
	}
}
