import { afterAll, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import axios, { AxiosError, type InternalAxiosRequestConfig } from 'axios';

/**
 * apiClient.ts wiring against a fake backend (axios adapter): base URL, bearer token, the refresh
 * interceptor's storage + login redirect, the 2FA redirect and blob error bodies.
 */
const nav = vi.hoisted(() => ({ goto: vi.fn() }));
vi.mock('$app/navigation', () => nav);
vi.mock('$env/dynamic/public', () => ({ env: { PUBLIC_API_BASE_URL: 'https://api.test' } }));

type Reply = { status: number; data?: unknown };
const seen: { url: string; auth: string | undefined }[] = [];
let respond: (config: InternalAxiosRequestConfig) => Reply;

const storage = new Map<string, string>();
const location = { origin: 'https://app.test', pathname: '/history', search: '?date=2026-10-01', href: '' };
let client: typeof import('./apiClient');

beforeAll(async () => {
	vi.stubGlobal('localStorage', {
		getItem: (k: string) => storage.get(k) ?? null,
		setItem: (k: string, v: string) => void storage.set(k, v),
		removeItem: (k: string) => void storage.delete(k)
	});
	vi.stubGlobal('window', { location });
	axios.defaults.adapter = async (config) => {
		seen.push({ url: config.url ?? '', auth: config.headers?.Authorization as string | undefined });
		const { status, data = {} } = respond(config);
		const response = { status, statusText: String(status), data, headers: {}, config };
		if (status >= 400) throw new AxiosError(String(status), 'ERR_BAD_RESPONSE', config, null, response);
		return response;
	};
	client = await import('./apiClient');
});

afterAll(() => {
	vi.unstubAllGlobals();
	axios.defaults.adapter = undefined;
});

beforeEach(() => {
	seen.length = 0;
	storage.clear();
	nav.goto.mockClear();
	location.pathname = '/history';
	location.href = '';
});

/** Protected URLs need `Bearer good`; /Auth/refresh hands out "good" for refresh token "r1". */
function backend(refreshOk = true) {
	respond = (config) => {
		if (config.url?.endsWith('/Auth/refresh')) {
			const body = JSON.parse(config.data as string);
			return refreshOk && body.refreshToken === 'r1' ? { status: 200, data: { accessToken: 'good', refreshToken: 'r2' } } : { status: 401 };
		}
		return config.headers?.Authorization === 'Bearer good' ? { status: 200, data: { id: 7 } } : { status: 401 };
	};
}

describe('apiClient', () => {
	it('offline: refuses at once, reads quietly (retried on reconnect), writes with an error', async () => {
		backend();
		storage.set('authToken', 'good');
		const { network } = await import('$lib/pwa/network.svelte');
		const { toast } = await import('$lib/stores/toast.svelte');
		vi.stubGlobal('navigator', { onLine: false });
		try {
			const read = await client.authApi.apiV1AuthMeGet().catch((e) => e);
			const write = await client.pushApi.apiV1PushSubscriptionsRemovePost({ endpoint: 'x' }).catch((e) => e);
			expect(seen).toEqual([]);
			expect(read).toMatchObject({ quiet: true, response: { data: { code: 'network.offline' } } });
			expect(write).toMatchObject({ quiet: false });
			expect(toast.error(read)).toBe(0); // the offline banner explains it
			expect(toast.error(write)).toBeGreaterThan(0);
			expect(toast.items.at(-1)?.message).toContain('Keine Verbindung');
		} finally {
			vi.stubGlobal('navigator', { onLine: true });
		}
		// The missed read makes the next "online" a reconnect
		const listeners: Record<string, () => void> = {};
		vi.stubGlobal('window', { location, addEventListener: (n: string, f: () => void) => (listeners[n] = f), removeEventListener: () => {} });
		network.listen()();
		expect(network.reconnects).toBe(1);
		vi.stubGlobal('window', { location });
	});

	it('sends the stored bearer token to the configured base URL', async () => {
		backend();
		storage.set('authToken', 'good');
		const { data } = await client.authApi.apiV1AuthMeGet();
		expect(data).toEqual({ id: 7 });
		expect(seen).toEqual([{ url: 'https://api.test/api/v1/Auth/me', auth: 'Bearer good' }]);
	});

	it('refreshes an expired token once, stores the rotated pair and retries', async () => {
		backend();
		storage.set('authToken', 'expired');
		storage.set('refreshToken', 'r1');
		const [a, b] = await Promise.all([client.authApi.apiV1AuthMeGet(), client.organizationsApi.apiV1OrganizationsMineGet()]);
		expect([a.data, b.data]).toEqual([{ id: 7 }, { id: 7 }]);
		expect(seen.filter((s) => s.url.endsWith('/Auth/refresh'))).toHaveLength(1);
		expect([storage.get('authToken'), storage.get('refreshToken')]).toEqual(['good', 'r2']);
	});

	it('ends the session and sends the user to the login with the way back when the refresh fails', async () => {
		backend(false);
		storage.set('authToken', 'expired');
		storage.set('refreshToken', 'r1');
		storage.set('authUser', '{}');
		await expect(client.authApi.apiV1AuthMeGet()).rejects.toBeTruthy();
		expect(storage.size).toBe(0);
		expect(location.href).toBe('/login?next=%2Fhistory%3Fdate%3D2026-10-01');

		// Already on the login page: no redirect loop
		location.pathname = '/login';
		location.href = '';
		storage.set('refreshToken', 'r1');
		await expect(client.authApi.apiV1AuthMeGet()).rejects.toBeTruthy();
		expect(location.href).toBe('');
	});

	it('hands refreshes and forced logouts to the handlers the auth store registers', async () => {
		backend();
		const handlers = { onRefreshed: vi.fn(), onAuthFailure: vi.fn() };
		client.setAuthHandlers(handlers);
		storage.set('authToken', 'expired');
		storage.set('refreshToken', 'r1');
		// The store's handler stores the tokens; this one does not, so the retry is still 401 → failure
		await expect(client.authApi.apiV1AuthMeGet()).rejects.toBeTruthy();
		expect(handlers.onRefreshed).toHaveBeenCalledWith({ accessToken: 'good', refreshToken: 'r2' });
		expect(handlers.onAuthFailure).toHaveBeenCalledTimes(1);
	});

	it('sends a user to the 2FA setup when the organization requires it (except from the settings page)', async () => {
		respond = () => ({ status: 403, data: { title: 'TwoFactorRequired' } });
		await expect(client.organizationsApi.apiV1OrganizationsMineGet()).rejects.toMatchObject({ response: { status: 403 } });
		expect(nav.goto).toHaveBeenCalledWith('/settings#two-factor');

		location.pathname = '/settings';
		await expect(client.organizationsApi.apiV1OrganizationsMineGet()).rejects.toBeTruthy();
		expect(nav.goto).toHaveBeenCalledTimes(1);
	});

	it('asks an admin for a reason when a closed month needs one and sends the request again with it', async () => {
		const { confirmState } = await import('$lib/stores/confirm.svelte');
		const reasons: (string | undefined)[] = [];
		respond = (config) => {
			const reason = config.headers?.['X-Change-Reason'] as string | undefined;
			reasons.push(reason);
			return reason ? { status: 200, data: { id: 9 } } : { status: 409, data: { code: 'period.reasonRequired', month: '2026-09' } };
		};
		const saved = client.timeTrackingApi.apiV1TimeTrackingIdPut(9, {});
		await vi.waitFor(() => expect(confirmState.current?.input).toBeTruthy());
		expect(confirmState.current?.message).toContain('September 2026');
		confirmState.answer(true, '  Lohn & Gehalt ');
		await expect(saved).resolves.toMatchObject({ data: { id: 9 } });
		expect(reasons).toEqual([undefined, encodeURIComponent('Lohn & Gehalt')]);

		// Cancelled: the original error reaches the caller, nothing is sent again
		reasons.length = 0;
		const cancelled = client.timeTrackingApi.apiV1TimeTrackingIdPut(9, {});
		await vi.waitFor(() => expect(confirmState.current).toBeTruthy());
		confirmState.answer(false);
		await expect(cancelled).rejects.toMatchObject({ response: { status: 409 } });
		expect(reasons).toEqual([undefined]);
	});

	it('turns a JSON error body of a blob download back into an object', async () => {
		respond = () => ({ status: 400, data: new Blob([JSON.stringify({ code: 'range.tooLong' })], { type: 'application/problem+json' }) });
		await expect(client.authApi.apiV1AuthMeGet()).rejects.toMatchObject({ response: { data: { code: 'range.tooLong' } } });

		respond = () => ({ status: 400, data: new Blob(['not json'], { type: 'application/json' }) });
		const err = await client.authApi.apiV1AuthMeGet().catch((e) => e);
		expect(err.response.data).toBeInstanceOf(Blob);
	});
});
