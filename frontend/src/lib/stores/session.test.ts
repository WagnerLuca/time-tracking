import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { t } from '$lib/i18n';

/**
 * The stores that talk to the API (auth, org context, timer, org detail cache), against a mocked
 * `$lib/apiClient`. Every test imports fresh store singletons (`vi.resetModules`).
 */
const api = vi.hoisted(() => ({
	authApi: {
		apiV1AuthLoginPost: vi.fn(),
		apiV1Auth2faVerifyPost: vi.fn(),
		apiV1AuthRegisterPost: vi.fn(),
		apiV1AuthLogoutPost: vi.fn(),
		apiV1AuthMeGet: vi.fn()
	},
	organizationsApi: {
		apiV1OrganizationsMineGet: vi.fn(),
		apiV1OrganizationsSlugGet: vi.fn(),
		apiV1OrganizationsSlugSettingsPut: vi.fn(),
		apiV1OrganizationsSlugPut: vi.fn()
	},
	timeTrackingApi: { apiV1TimeTrackingCurrentGet: vi.fn(), apiV1TimeTrackingStartPost: vi.fn(), apiV1TimeTrackingStopPost: vi.fn() },
	setAuthHandlers: vi.fn()
}));
vi.mock('$lib/apiClient', () => api);

/** A Map-backed localStorage stand-in. */
function memoryStorage(initial: Record<string, string> = {}) {
	const map = new Map(Object.entries(initial));
	return {
		getItem: (k: string) => map.get(k) ?? null,
		setItem: (k: string, v: string) => void map.set(k, v),
		removeItem: (k: string) => void map.delete(k),
		map
	};
}

const USER = { id: 7, email: 'tom@example.com', firstName: 'Tom' };
const TOKENS = { accessToken: 'access-1', refreshToken: 'refresh-1', expiresAt: '2026-10-15T12:00:00Z', user: USER };

let storage: ReturnType<typeof memoryStorage>;
let listeners: Record<string, (e: unknown) => void>;
let reload: ReturnType<typeof vi.fn>;

function stubBrowser(initial: Record<string, string> = {}) {
	storage = memoryStorage(initial);
	listeners = {};
	reload = vi.fn();
	vi.stubGlobal('localStorage', storage);
	vi.stubGlobal('window', {
		addEventListener: (name: string, fn: (e: unknown) => void) => (listeners[name] = fn),
		location: { reload }
	});
}

const httpError = (status: number) => Object.assign(new Error(`HTTP ${status}`), { response: { status, data: {} } });

beforeEach(() => {
	vi.resetModules();
	vi.clearAllMocks();
	stubBrowser();
});

afterEach(() => {
	vi.unstubAllGlobals();
	vi.useRealTimers();
});

describe('auth store', () => {
	const load = async () => (await import('./auth.svelte')).auth;

	it('restores a saved session and drops a corrupt one', async () => {
		stubBrowser({ authToken: 'saved', authUser: JSON.stringify(USER) });
		const auth = await load();
		expect(auth.loading).toBe(true);
		auth.init();
		expect([auth.loading, auth.isAuthenticated, auth.token, auth.user?.id]).toEqual([false, true, 'saved', 7]);

		vi.resetModules();
		stubBrowser({ authToken: 'saved', authUser: '{not json', refreshToken: 'r' });
		const broken = await load();
		broken.init();
		expect(broken.isAuthenticated).toBe(false);
		expect(storage.map.size).toBe(0);
	});

	it('does nothing but stop loading without a window (SSR)', async () => {
		vi.stubGlobal('window', undefined);
		const auth = await load();
		auth.init();
		expect([auth.loading, auth.isAuthenticated]).toEqual([false, false]);
	});

	it('logs in, persisting the tokens and the user', async () => {
		api.authApi.apiV1AuthLoginPost.mockResolvedValue({ data: TOKENS });
		const auth = await load();
		expect(await auth.login({ email: 'tom@example.com', password: 'x' })).toBe('success');
		expect([auth.isAuthenticated, auth.token, auth.twoFactorRequired]).toEqual([true, 'access-1', false]);
		expect(Object.fromEntries(storage.map)).toEqual({ authToken: 'access-1', refreshToken: 'refresh-1', authUser: JSON.stringify(USER) });
	});

	it('asks for the TOTP code first when 2FA is on, then signs in with it', async () => {
		api.authApi.apiV1AuthLoginPost.mockResolvedValue({ data: { requiresTwoFactor: true, twoFactorToken: '2fa-token' } });
		api.authApi.apiV1Auth2faVerifyPost.mockResolvedValue({ data: TOKENS });
		const auth = await load();

		await expect(auth.verifyTwoFactor('123456')).rejects.toThrow('No 2FA token');
		expect(await auth.login({ email: 'tom@example.com', password: 'x' })).toBe('2fa');
		expect([auth.twoFactorRequired, auth.isAuthenticated, storage.map.size]).toEqual([true, false, 0]);

		await auth.verifyTwoFactor('123456');
		expect(api.authApi.apiV1Auth2faVerifyPost).toHaveBeenCalledWith({ twoFactorToken: '2fa-token', code: '123456' });
		expect([auth.twoFactorRequired, auth.isAuthenticated]).toEqual([false, true]);

		// Going back to the password step forgets the challenge
		await auth.login({ email: 'tom@example.com', password: 'x' });
		auth.clearTwoFactor();
		expect(auth.twoFactorRequired).toBe(false);
		await expect(auth.verifyTwoFactor('1')).rejects.toThrow();
	});

	it('registers straight into a session', async () => {
		api.authApi.apiV1AuthRegisterPost.mockResolvedValue({ data: TOKENS });
		const auth = await load();
		await auth.register({ email: 'tom@example.com', password: 'x', firstName: 'Tom', lastName: 'W' });
		expect(auth.isAuthenticated).toBe(true);
	});

	it('logs out: revokes the refresh token, clears everything and the selected org — even when the call fails', async () => {
		stubBrowser({ authToken: 'a', refreshToken: 'r', authUser: JSON.stringify(USER), selectedOrgId: '3' });
		api.authApi.apiV1AuthLogoutPost.mockRejectedValue(httpError(500));
		const auth = await load();
		auth.init();
		await auth.logout();
		expect(api.authApi.apiV1AuthLogoutPost).toHaveBeenCalledWith({ refreshToken: 'r' });
		expect([auth.isAuthenticated, storage.map.size]).toEqual([false, 0]);

		// Without a refresh token there is nothing to revoke
		await auth.logout();
		expect(api.authApi.apiV1AuthLogoutPost).toHaveBeenCalledTimes(1);
	});

	it('reloads the current user, and signs out when that fails', async () => {
		stubBrowser({ authToken: 'a', authUser: JSON.stringify(USER) });
		api.authApi.apiV1AuthMeGet.mockResolvedValueOnce({ data: { ...USER, firstName: 'Thomas' } }).mockRejectedValueOnce(httpError(401));
		const auth = await load();
		auth.init();
		await auth.fetchCurrentUser();
		expect(auth.user?.firstName).toBe('Thomas');
		expect(JSON.parse(storage.map.get('authUser')!).firstName).toBe('Thomas');
		await auth.fetchCurrentUser();
		expect(auth.isAuthenticated).toBe(false);
	});

	it('wires the refresh interceptor: rotated tokens are stored, a failed refresh ends the session', async () => {
		stubBrowser({ authToken: 'a', refreshToken: 'r', authUser: JSON.stringify(USER) });
		const auth = await load();
		auth.init();
		const handlers = api.setAuthHandlers.mock.calls.at(-1)![0];

		handlers.onRefreshed({ accessToken: 'a2', refreshToken: 'r2' });
		expect([auth.token, storage.map.get('authToken'), storage.map.get('refreshToken')]).toEqual(['a2', 'a2', 'r2']);
		handlers.onAuthFailure();
		expect([auth.isAuthenticated, storage.map.size]).toEqual([false, 0]);
	});

	it('follows logins, logouts and token rotations of other tabs', async () => {
		stubBrowser({ authToken: 'a', authUser: JSON.stringify(USER) });
		const auth = await load();
		auth.init();
		const storageEvent = (key: string | null) => listeners.storage({ storageArea: storage, key });

		storage.map.set('authToken', 'rotated');
		storageEvent('authToken');
		expect(auth.token).toBe('rotated');

		storageEvent('selectedOrgId');            // not ours
		storageEvent('wl-theme');
		expect(auth.token).toBe('rotated');

		storage.map.delete('authUser');           // another tab is mid-login: wait for the user key
		storageEvent('authToken');
		expect(auth.isAuthenticated).toBe(true);

		storage.map.set('authUser', '{broken');
		storageEvent('authUser');
		expect(auth.user?.id).toBe(7);

		storage.map.set('authUser', JSON.stringify({ ...USER, id: 8 }));   // someone else signed in
		storageEvent('authUser');
		expect(reload).toHaveBeenCalledTimes(1);

		storage.map.delete('authToken');          // signed out elsewhere
		storageEvent(null);
		expect(auth.isAuthenticated).toBe(false);
	});
});

describe('organization context', () => {
	const load = async () => (await import('./orgContext.svelte')).orgContext;
	const orgs = [
		{ organizationId: 1, slug: 'one', name: 'One' },
		{ organizationId: 2, slug: 'two', name: 'Two' }
	];

	it('restores the saved choice while it is still one of the user\'s organizations', async () => {
		stubBrowser({ selectedOrgId: '2' });
		api.organizationsApi.apiV1OrganizationsMineGet.mockResolvedValue({ data: orgs });
		const ctx = await load();
		ctx.init();
		expect(ctx.selectedOrgId).toBe(2);
		await ctx.loadOrganizations(7);
		expect([ctx.loading, ctx.selectedOrgSlug, ctx.selectedOrg?.name]).toEqual([false, 'two', 'Two']);
	});

	it('drops a stale choice and auto-selects a single organization', async () => {
		stubBrowser({ selectedOrgId: '99' });
		api.organizationsApi.apiV1OrganizationsMineGet.mockResolvedValueOnce({ data: orgs }).mockResolvedValueOnce({ data: [orgs[0]] });
		const ctx = await load();
		ctx.init();
		await ctx.loadOrganizations(7);
		expect([ctx.selectedOrgId, storage.map.has('selectedOrgId')]).toEqual([null, false]);
		await ctx.loadOrganizations(7);
		expect([ctx.selectedOrgId, storage.map.get('selectedOrgId')]).toEqual([1, '1']);
	});

	it('switches and persists the organization, and survives a failed load', async () => {
		api.organizationsApi.apiV1OrganizationsMineGet.mockResolvedValueOnce({ data: orgs }).mockRejectedValueOnce(httpError(500));
		const ctx = await load();
		await ctx.loadOrganizations(7);
		expect(ctx.selectedOrg).toBeNull();
		ctx.select(2);
		expect([ctx.selectedOrgSlug, storage.map.get('selectedOrgId')]).toEqual(['two', '2']);
		ctx.select(null);
		expect([ctx.selectedOrgSlug, storage.map.has('selectedOrgId')]).toEqual([null, false]);
		await ctx.loadOrganizations(7);
		expect([ctx.organizations, ctx.loading]).toEqual([[], false]);
	});

	it('reads nothing without a window', async () => {
		vi.stubGlobal('window', undefined);
		const ctx = await load();
		ctx.init();
		expect(ctx.selectedOrgId).toBeNull();
	});
});

describe('timer store', () => {
	const load = async () => {
		const { timer } = await import('./timer.svelte');
		const { toast } = await import('./toast.svelte');
		return { timer, toast };
	};
	const entry = (startTime: string) => ({ id: 5, startTime, isRunning: true });

	it('loads the running entry and ticks every second until stopped', async () => {
		vi.useFakeTimers();
		vi.setSystemTime(new Date('2026-10-15T10:00:00Z'));
		api.timeTrackingApi.apiV1TimeTrackingCurrentGet.mockResolvedValue({ data: entry('2026-10-15T09:00:00Z') });
		api.timeTrackingApi.apiV1TimeTrackingStopPost.mockResolvedValueOnce({ data: { id: 5 } }).mockResolvedValueOnce({ data: { id: 5, warnings: ['dailyMax'] } });
		const { timer, toast } = await load();

		expect(timer.loaded).toBe(false);
		await timer.refresh();
		expect([timer.loaded, timer.running, timer.elapsedSeconds, timer.elapsedMinutes]).toEqual([true, true, 3600, 60]);
		vi.advanceTimersByTime(3000);
		expect(timer.elapsedSeconds).toBe(3603);

		await timer.stop();
		expect([timer.running, timer.elapsedSeconds]).toEqual([false, 0]);
		expect(vi.getTimerCount()).toBe(0);   // the ticker is gone
		// The Working Time Act warning of a stopped entry is shown right away
		await timer.stop();
		expect(toast.items.map((i) => [i.kind, i.message])).toContainEqual(['warning', t('entries.warnings.dailyMax')]);
	});

	it('treats an empty answer as "nothing running" and keeps the state when loading fails', async () => {
		api.timeTrackingApi.apiV1TimeTrackingCurrentGet.mockResolvedValueOnce({ data: '' }).mockRejectedValueOnce(httpError(500));
		const { timer } = await load();
		await timer.refresh();
		expect([timer.loaded, timer.current]).toEqual([true, null]);
		await expect(timer.refresh()).rejects.toThrow();
		expect(timer.loaded).toBe(true);
	});

	it('starts a timer; a 409 shows the one started elsewhere instead', async () => {
		vi.useFakeTimers();
		api.timeTrackingApi.apiV1TimeTrackingStartPost
			.mockResolvedValueOnce({ data: entry(new Date().toISOString()) })
			.mockRejectedValueOnce(httpError(409))
			.mockRejectedValueOnce(httpError(500));
		api.timeTrackingApi.apiV1TimeTrackingCurrentGet.mockResolvedValue({ data: { id: 6, startTime: new Date().toISOString() } });
		const { timer } = await load();

		expect((await timer.start({ organizationId: 1 })).id).toBe(5);
		expect(timer.running).toBe(true);
		expect(vi.getTimerCount()).toBe(1);

		await expect(timer.start({ organizationId: 1 })).rejects.toThrow(t('timer.alreadyRunning'));
		expect(timer.current?.id).toBe(6);
		expect(vi.getTimerCount()).toBe(1);   // still one ticker
		await expect(timer.start({ organizationId: 1 })).rejects.toMatchObject({ response: { status: 500 } });

		timer.reset();
		expect([timer.running, timer.loaded, vi.getTimerCount()]).toEqual([false, false, 0]);
	});
});

describe('organization detail cache', () => {
	const load = async () => {
		const auth = (await import('./auth.svelte')).auth;
		return { ...(await import('./orgDetail')), auth };
	};

	it('serves cached details per user and slug for a minute, refetches on demand and after invalidation', async () => {
		vi.useFakeTimers();
		let n = 0;
		api.organizationsApi.apiV1OrganizationsSlugGet.mockImplementation(async (slug: string) => ({ data: { slug, id: ++n } }));
		const { getOrgDetail, invalidateOrgDetail } = await load();

		expect(await getOrgDetail('a')).toEqual({ slug: 'a', id: 1 });
		expect(await getOrgDetail('a')).toEqual({ slug: 'a', id: 1 });
		expect(await getOrgDetail('a', { fresh: true })).toEqual({ slug: 'a', id: 2 });
		vi.advanceTimersByTime(60_001);
		expect((await getOrgDetail('a')).id).toBe(3);

		await getOrgDetail('b');
		invalidateOrgDetail('a');
		expect((await getOrgDetail('a')).id).toBe(5);
		expect((await getOrgDetail('b')).id).toBe(4);
		invalidateOrgDetail();
		expect((await getOrgDetail('b')).id).toBe(6);
	});

	it('drops the cached details once the time settings are saved, even when the profile save fails', async () => {
		let n = 0;
		api.organizationsApi.apiV1OrganizationsSlugGet.mockImplementation(async (slug: string) => ({ data: { slug, id: ++n } }));
		api.organizationsApi.apiV1OrganizationsSlugPut.mockRejectedValueOnce(httpError(409));
		const { getOrgDetail, saveOrgGeneral } = await load();
		const current = { timeZone: 'Europe/Berlin', businessDayEnd: '00:00' };

		await getOrgDetail('a');
		await expect(saveOrgGeneral('a', current, { timeZone: 'America/New_York', businessDayEnd: '00:00' }, { name: 'A' })).rejects.toThrow();
		expect(api.organizationsApi.apiV1OrganizationsSlugSettingsPut).toHaveBeenCalledWith('a', { timeZone: 'America/New_York', businessDayEnd: '00:00' });
		expect((await getOrgDetail('a')).id).toBe(2);

		// Unchanged time settings are not sent again
		await saveOrgGeneral('a', current, current, { name: 'A' });
		expect(api.organizationsApi.apiV1OrganizationsSlugSettingsPut).toHaveBeenCalledTimes(1);
		expect((await getOrgDetail('a')).id).toBe(3);
	});

	it('never caches a failure', async () => {
		api.organizationsApi.apiV1OrganizationsSlugGet.mockRejectedValueOnce(httpError(500)).mockResolvedValueOnce({ data: { slug: 'a' } });
		const { getOrgDetail } = await load();
		await expect(getOrgDetail('a')).rejects.toThrow();
		expect(await getOrgDetail('a')).toEqual({ slug: 'a' });
	});
});
