import { expect, test, type Page } from '@playwright/test';
import { API, PWA_URL, apiToken, login } from './helpers';

/**
 * The installable app (issue #67) against a production build — the service worker never runs
 * on the dev server. See playwright.config.ts for how the preview is started.
 */
test.use({ baseURL: PWA_URL });

const ADMIN = 'anna.schmidt@example.com';

/** Waits until the service worker is active and controls the page. */
async function controlled(page: Page) {
	await page.evaluate(() => navigator.serviceWorker.ready);
	if (!(await page.evaluate(() => !!navigator.serviceWorker.controller))) await page.reload();
	await expect.poll(() => page.evaluate(() => !!navigator.serviceWorker.controller)).toBe(true);
}

test('pwa: manifest, icons, CSP and the service worker in the production build', async ({ page, request }) => {
	const res = await request.get(`${PWA_URL}/manifest.webmanifest`);
	expect(res.ok()).toBeTruthy();
	expect(res.headers()['content-type']).toContain('application/manifest+json');
	const manifest = await res.json();
	expect(manifest).toMatchObject({ name: 'WL Zeiterfassung', start_url: '/', scope: '/', display: 'standalone', lang: 'de' });
	expect(manifest.icons.map((i: { purpose: string }) => i.purpose)).toEqual(expect.arrayContaining(['any', 'maskable', 'monochrome']));
	expect(manifest.shortcuts.map((s: { url: string }) => s.url)).toEqual(['/time', '/history', '/team']);
	for (const icon of manifest.icons) expect((await request.get(`${PWA_URL}${icon.src}`)).ok(), icon.src).toBeTruthy();

	const csp = (await request.get(`${PWA_URL}/login`)).headers()['content-security-policy'];
	expect(csp).toContain("worker-src 'self'");
	expect(csp).toContain("manifest-src 'self'");

	await page.goto('/login');
	await expect(page.locator('link[rel="manifest"]')).toHaveAttribute('href', '/manifest.webmanifest');
	await expect(page.locator('meta[name="theme-color"]')).toHaveAttribute('content', /^#[0-9A-F]{6}$/i);
	const script = await page.evaluate(async () => (await navigator.serviceWorker.ready).active?.scriptURL);
	expect(script).toMatch(/\/service-worker\.js$/);
	const caches = await page.evaluate(() => window.caches.keys());
	expect(caches.some((k) => k.startsWith('wl-time-'))).toBe(true);
	// The API is never cached
	const cachedApi = await page.evaluate(async () => {
		for (const key of await window.caches.keys())
			if ((await (await window.caches.open(key)).keys()).some((r) => new URL(r.url).pathname.startsWith('/api/'))) return true;
		return false;
	});
	expect(cachedApi).toBe(false);
});

test('pwa: offline banner, locked actions, the shell from the cache and a reload on reconnect', async ({ page, context }) => {
	await login(page, ADMIN);
	await page.goto('/time');
	await controlled(page);
	const timerButton = page.getByRole('button', { name: /^Timer (starten|stoppen)$/ });
	await expect(timerButton).toBeEnabled();

	await context.setOffline(true);
	const banner = page.getByTestId('offline-banner');
	await expect(banner).toContainText('Keine Verbindung – Daten werden geladen, sobald du wieder online bist');
	await expect(timerButton).toBeDisabled();

	// A reload offline still opens the app (shell from the service worker cache)
	await page.reload();
	await expect(banner).toBeVisible();
	await expect(page.getByRole('navigation', { name: 'Menü', exact: true }).first()).toBeVisible();

	await context.setOffline(false);
	await expect(banner).toBeHidden();
	// The page loads again by itself
	await expect(timerButton).toBeEnabled({ timeout: 15_000 });
});

test('pwa: push opt-in asks only after a click, stores categories, and turns off again', async ({ page, request }) => {
	// Mocked Notification permission + PushManager (no real push service in a test browser)
	await page.addInitScript(() => {
		const saved = () => sessionStorage.getItem('e2e-push-sub');
		let permission = sessionStorage.getItem('e2e-push-permission') ?? 'default';
		const w = window as unknown as { __permissionAsked: number };
		w.__permissionAsked = 0;
		Object.defineProperty(Notification, 'permission', { configurable: true, get: () => permission });
		Notification.requestPermission = async () => {
			w.__permissionAsked++;
			permission = 'granted';
			sessionStorage.setItem('e2e-push-permission', permission);
			return 'granted';
		};
		const b64 = (bytes: number[]) => btoa(String.fromCharCode(...bytes)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
		const subscription = (endpoint: string) => ({
			endpoint,
			toJSON: () => ({ endpoint, keys: { p256dh: b64([4, ...Array(64).fill(7)]), auth: b64(Array(16).fill(9)) } }),
			unsubscribe: async () => {
				sessionStorage.removeItem('e2e-push-sub');
				return true;
			}
		});
		PushManager.prototype.subscribe = async function () {
			const endpoint = `https://fcm.googleapis.com/fcm/send/e2e-${Date.now()}`;
			sessionStorage.setItem('e2e-push-sub', endpoint);
			return subscription(endpoint) as unknown as PushSubscription;
		};
		PushManager.prototype.getSubscription = async function () {
			const endpoint = saved();
			return (endpoint ? subscription(endpoint) : null) as unknown as PushSubscription | null;
		};
	});

	await login(page, ADMIN);
	await page.goto('/settings#app');
	await controlled(page);
	const section = page.getByTestId('push-settings');
	const enable = section.getByRole('button', { name: 'Benachrichtigungen aktivieren' });
	await expect(enable).toBeVisible();
	expect(await page.evaluate(() => (window as unknown as { __permissionAsked: number }).__permissionAsked)).toBe(0);

	await enable.click();
	await expect(section.getByText('Dieses Gerät erhält Benachrichtigungen.')).toBeVisible();
	expect(await page.evaluate(() => (window as unknown as { __permissionAsked: number }).__permissionAsked)).toBe(1);
	const coreHours = section.getByRole('checkbox', { name: /Kernzeit/ });
	await expect(coreHours).toBeChecked();
	const saved = page.waitForResponse((r) => r.url().endsWith('/api/v1/Push/settings') && r.request().method() === 'PUT');
	await coreHours.uncheck();
	expect((await saved).ok()).toBeTruthy();

	// Stored per user: still off after a reload, and the device is still registered
	await page.reload();
	await expect(section.getByText('Dieses Gerät erhält Benachrichtigungen.')).toBeVisible();
	await expect(coreHours).not.toBeChecked();
	await coreHours.check();
	await expect(coreHours).toBeChecked();

	await section.getByRole('button', { name: 'Auf diesem Gerät deaktivieren' }).click();
	await expect(enable).toBeVisible();

	const token = await apiToken(request, ADMIN);
	const settings = await (await request.get(`${API}/Push/settings`, { headers: { Authorization: `Bearer ${token}` } })).json();
	expect(settings).toEqual({ reminders: true, requests: true, coreHours: true, organization: true });
});

test('pwa: "App installieren" in the Mehr menu at 390 px explains the manual way', async ({ page }) => {
	await page.setViewportSize({ width: 390, height: 844 });
	await login(page, ADMIN);
	await page.getByRole('button', { name: 'Mehr' }).click();
	await page.getByRole('button', { name: 'App installieren' }).click();
	await expect(page).toHaveURL(/\/settings#app$/);
	await expect(page.getByTestId('install-hint')).toContainText('App installieren');
});
