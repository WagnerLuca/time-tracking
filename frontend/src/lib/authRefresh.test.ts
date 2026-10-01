import { describe, expect, it, vi } from 'vitest';
import axios, { AxiosError, type AxiosAdapter, type InternalAxiosRequestConfig } from 'axios';
import { installRefreshInterceptor, type RefreshDeps } from './authRefresh';

/**
 * A fake backend behind a custom axios adapter: protected URLs answer 401 unless
 * the request carries `Bearer <validToken>`.
 */
function setup(options: { refreshFails?: boolean } = {}) {
	let validToken = 'fresh-token';
	let storedAccess = 'expired-token';
	let storedRefresh: string | null = 'refresh-1';
	const seen: Array<{ url: string; auth: string | undefined }> = [];

	const adapter: AxiosAdapter = async (config: InternalAxiosRequestConfig) => {
		const auth = config.headers?.Authorization as string | undefined;
		seen.push({ url: config.url ?? '', auth });
		await new Promise((r) => setTimeout(r, 5));
		const ok = config.url?.includes('/auth/login') || auth === `Bearer ${validToken}`;
		if (!ok) {
			const response = { status: 401, statusText: 'Unauthorized', data: {}, headers: {}, config };
			throw new AxiosError('401', 'ERR_BAD_REQUEST', config, null, response);
		}
		return { status: 200, statusText: 'OK', data: { url: config.url }, headers: {}, config };
	};

	const instance = axios.create({ adapter });
	// Mirrors apiClient.ts: the request interceptor always reads the current token
	instance.interceptors.request.use((config) => {
		config.headers.Authorization = `Bearer ${storedAccess}`;
		return config;
	});

	const deps: RefreshDeps = {
		getRefreshToken: () => storedRefresh,
		getAccessToken: () => storedAccess,
		refresh: vi.fn(async (rt: string) => {
			await new Promise((r) => setTimeout(r, 20));
			if (options.refreshFails || rt !== 'refresh-1') throw new Error('refresh rejected');
			return { accessToken: validToken, refreshToken: 'refresh-2' };
		}),
		onRefreshed: vi.fn((tokens) => {
			storedAccess = tokens.accessToken;
			storedRefresh = tokens.refreshToken;
		}),
		onAuthFailure: vi.fn(() => {
			storedAccess = '';
			storedRefresh = null;
		})
	};
	installRefreshInterceptor(instance, deps);

	return {
		instance,
		deps,
		seen,
		get storedRefresh() { return storedRefresh; },
		rotateServerToken(t: string) { validToken = t; }
	};
}

describe('installRefreshInterceptor', () => {
	it('two concurrent 401s share exactly one refresh and are both retried', async () => {
		const s = setup();
		const [a, b] = await Promise.all([s.instance.get('/api/v1/time-tracking/current'), s.instance.get('/api/v1/organizations/mine')]);
		expect(a.data.url).toBe('/api/v1/time-tracking/current');
		expect(b.data.url).toBe('/api/v1/organizations/mine');
		expect(s.deps.refresh).toHaveBeenCalledTimes(1);
		expect(s.deps.refresh).toHaveBeenCalledWith('refresh-1');
		expect(s.deps.onRefreshed).toHaveBeenCalledTimes(1);
		expect(s.deps.onAuthFailure).not.toHaveBeenCalled();
		expect(s.storedRefresh).toBe('refresh-2');
		// 2 failed originals + 2 retries with the new token
		expect(s.seen).toHaveLength(4);
		expect(s.seen.filter((r) => r.auth === 'Bearer fresh-token')).toHaveLength(2);
	});

	it('a failed refresh clears auth and rejects both requests', async () => {
		const s = setup({ refreshFails: true });
		const results = await Promise.allSettled([s.instance.get('/api/v1/a'), s.instance.get('/api/v1/b')]);
		expect(results.map((r) => r.status)).toEqual(['rejected', 'rejected']);
		expect(s.deps.refresh).toHaveBeenCalledTimes(1);
		expect(s.deps.onAuthFailure).toHaveBeenCalled();
		expect(s.storedRefresh).toBeNull();
	});

	it('a rejected refresh sent through the same instance (real /Auth/refresh casing) ends the session instead of hanging', async () => {
		const s = setup();
		s.rotateServerToken('never-valid');
		// Like apiClient.ts: the refresh call itself goes through the intercepted instance
		vi.mocked(s.deps.refresh).mockImplementationOnce(async (rt) => {
			const { data } = await s.instance.post('/api/v1/Auth/refresh', { refreshToken: rt });
			return data;
		});
		const outcome = await Promise.race([
			s.instance.get('/api/v1/TimeTracking/current').then(() => 'resolved', () => 'rejected'),
			new Promise((r) => setTimeout(() => r('hung'), 1000))
		]);
		expect(outcome).toBe('rejected');
		expect(s.deps.onAuthFailure).toHaveBeenCalledTimes(1);
	});

	it('does not try to refresh for the auth endpoints themselves (e.g. wrong password)', async () => {
		const s = setup();
		s.rotateServerToken('never-valid');
		await expect(s.instance.post('/api/v1/auth/refresh', {})).rejects.toBeInstanceOf(AxiosError);
		expect(s.deps.refresh).not.toHaveBeenCalled();
		expect(s.deps.onAuthFailure).not.toHaveBeenCalled();
	});

	it('retries only once: a retried request that is still 401 ends the session', async () => {
		const s = setup();
		// the refresh "succeeds" but hands out a token the server rejects too
		vi.mocked(s.deps.refresh).mockResolvedValueOnce({ accessToken: 'bogus', refreshToken: 'refresh-2' });
		await expect(s.instance.get('/api/v1/time-tracking/current')).rejects.toBeInstanceOf(AxiosError);
		expect(s.deps.refresh).toHaveBeenCalledTimes(1);
		expect(s.deps.onAuthFailure).toHaveBeenCalledTimes(1);
	});

	it('a later 401 after a completed refresh starts a new refresh', async () => {
		const s = setup();
		await s.instance.get('/api/v1/x');
		expect(s.deps.refresh).toHaveBeenCalledTimes(1);
		// the stored refresh token is now 'refresh-2', which the fake server rejects
		s.rotateServerToken('rotated-again');
		await expect(s.instance.get('/api/v1/y')).rejects.toBeInstanceOf(AxiosError);
		expect(s.deps.refresh).toHaveBeenCalledTimes(2);
		expect(s.deps.onAuthFailure).toHaveBeenCalledTimes(1);
	});

	it('passes non-401 errors through untouched', async () => {
		const instance = axios.create({
			adapter: async (config) => {
				throw new AxiosError('500', 'ERR_BAD_RESPONSE', config, null, { status: 500, statusText: 'x', data: {}, headers: {}, config });
			}
		});
		const deps: RefreshDeps = { getRefreshToken: () => 'r', getAccessToken: () => 'a', refresh: vi.fn(), onRefreshed: vi.fn(), onAuthFailure: vi.fn() };
		installRefreshInterceptor(instance, deps);
		await expect(instance.get('/api/v1/z')).rejects.toMatchObject({ response: { status: 500 } });
		expect(deps.refresh).not.toHaveBeenCalled();
	});

	it('two tabs sharing storage and a lock refresh only once (no reuse detection)', async () => {
		// One localStorage, one server that accepts each refresh token exactly once
		const storage = { access: 'expired', refresh: 'refresh-1' };
		let valid = 'access-1';
		const used = new Set<string>();
		const refresh = vi.fn(async (rt: string) => {
			await new Promise((r) => setTimeout(r, 20));
			if (used.has(rt)) throw new Error('reuse detected');
			used.add(rt);
			valid = `access-${used.size}`;
			return { accessToken: valid, refreshToken: `refresh-${used.size + 1}` };
		});
		// Promise-chain mutex standing in for navigator.locks (shared by both "tabs")
		let tail: Promise<unknown> = Promise.resolve();
		const lock = <T>(fn: () => Promise<T>): Promise<T> => {
			const run = tail.then(fn, fn);
			tail = run.catch(() => {});
			return run;
		};
		const tab = () => {
			const instance = axios.create({
				adapter: async (config) => {
					await new Promise((r) => setTimeout(r, 5));
					if (config.headers?.Authorization !== `Bearer ${valid}`) {
						throw new AxiosError('401', 'ERR_BAD_REQUEST', config, null, { status: 401, statusText: 'x', data: {}, headers: {}, config });
					}
					return { status: 200, statusText: 'OK', data: {}, headers: {}, config };
				}
			});
			instance.interceptors.request.use((config) => {
				config.headers.Authorization = `Bearer ${storage.access}`;
				return config;
			});
			const onAuthFailure = vi.fn();
			installRefreshInterceptor(instance, {
				getRefreshToken: () => storage.refresh,
				getAccessToken: () => storage.access,
				refresh,
				onRefreshed: (t) => { storage.access = t.accessToken; storage.refresh = t.refreshToken; },
				onAuthFailure,
				lock
			});
			return { instance, onAuthFailure };
		};
		const [a, b] = [tab(), tab()];
		const results = await Promise.allSettled([a.instance.get('/api/v1/x'), b.instance.get('/api/v1/y')]);
		expect(results.map((r) => r.status)).toEqual(['fulfilled', 'fulfilled']);
		expect(refresh).toHaveBeenCalledTimes(1);
		expect(a.onAuthFailure).not.toHaveBeenCalled();
		expect(b.onAuthFailure).not.toHaveBeenCalled();
	});

	it('a logout in another tab while waiting for the lock ends the session without refreshing', async () => {
		const s = setup();
		const lock = async <T>(fn: () => Promise<T>) => {
			// the other tab cleared the tokens before we got the lock
			s.deps.onAuthFailure();
			vi.mocked(s.deps.onAuthFailure).mockClear();
			return fn();
		};
		const instance = axios.create({
			adapter: async (config) => {
				throw new AxiosError('401', 'ERR_BAD_REQUEST', config, null, { status: 401, statusText: 'x', data: {}, headers: {}, config });
			}
		});
		installRefreshInterceptor(instance, { ...s.deps, lock });
		await expect(instance.get('/api/v1/x')).rejects.toBeInstanceOf(AxiosError);
		expect(s.deps.refresh).not.toHaveBeenCalled();
		expect(s.deps.onAuthFailure).toHaveBeenCalledTimes(1);
	});
});
