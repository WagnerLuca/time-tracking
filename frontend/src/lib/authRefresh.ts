import type { AxiosError, AxiosInstance, InternalAxiosRequestConfig } from 'axios';

/**
 * Single-flight refresh-token handling for an axios instance.
 *
 * On a 401 (except from the auth endpoints themselves) the stored refresh token
 * is exchanged once — concurrent 401s all wait on the same refresh — and every
 * failed request is retried a single time with the new access token. Only when
 * the refresh itself fails (or a retried request is still 401) is `onAuthFailure`
 * called, which clears the session and sends the user to /login.
 *
 * Across tabs the refresh runs under a Web Lock: two tabs exchanging the same refresh
 * token would trip the backend's reuse detection and revoke the whole session. Inside the
 * lock the stored refresh token is read again — if another tab rotated it meanwhile, its
 * new access token is used instead of refreshing a second time.
 *
 * Kept free of SvelteKit imports so it can be unit-tested with a mock adapter.
 */
export interface RefreshedTokens {
	accessToken: string;
	refreshToken: string;
}

export interface RefreshDeps {
	getRefreshToken: () => string | null;
	getAccessToken: () => string | null;
	refresh: (refreshToken: string) => Promise<RefreshedTokens>;
	onRefreshed: (tokens: RefreshedTokens) => void;
	onAuthFailure: () => void;
	/** Cross-tab mutual exclusion; defaults to navigator.locks, or none where unsupported. */
	lock?: <T>(fn: () => Promise<T>) => Promise<T>;
}

const LOCK_NAME = 'tt-auth-refresh';

function webLock<T>(fn: () => Promise<T>): Promise<T> {
	const locks = typeof navigator !== 'undefined' ? navigator.locks : undefined;
	// ponytail: without Web Locks (very old browsers) two tabs can still race; the loser is logged out
	return locks ? (locks.request(LOCK_NAME, fn) as Promise<T>) : fn();
}

/**
 * Endpoints whose 401 means "wrong credentials", not "expired access token". Case-insensitive:
 * the generated client calls `/api/v1/Auth/refresh`, and a refresh 401 that slipped through
 * would wait on the very refresh that is running — a deadlock that left the app loading forever.
 */
const AUTH_ENDPOINT = /\/auth\/(login|register|refresh|logout|2fa\/verify)(\?|$)/i;

type RetriableConfig = InternalAxiosRequestConfig & { _retried?: boolean };

export function installRefreshInterceptor(instance: AxiosInstance, deps: RefreshDeps): void {
	let inflight: Promise<string> | null = null;

	function refreshOnce(): Promise<string> {
		const seen = deps.getRefreshToken();
		const lock = deps.lock ?? webLock;
		inflight ??= lock(async () => {
			const refreshToken = deps.getRefreshToken();
			if (!refreshToken) throw new Error('No refresh token');
			const accessToken = deps.getAccessToken();
			// Another tab refreshed while we waited for the lock: use its tokens
			if (refreshToken !== seen && accessToken) return accessToken;
			const tokens = await deps.refresh(refreshToken);
			deps.onRefreshed(tokens);
			return tokens.accessToken;
		}).finally(() => {
			inflight = null;
		});
		return inflight;
	}

	instance.interceptors.response.use(
		(response) => response,
		async (error: AxiosError) => {
			const config = error.config as RetriableConfig | undefined;
			if (error.response?.status !== 401 || !config || AUTH_ENDPOINT.test(config.url ?? '')) {
				return Promise.reject(error);
			}
			if (config._retried) {
				deps.onAuthFailure();
				return Promise.reject(error);
			}
			config._retried = true;
			let accessToken: string;
			try {
				accessToken = await refreshOnce();
			} catch {
				deps.onAuthFailure();
				return Promise.reject(error);
			}
			config.headers.Authorization = `Bearer ${accessToken}`;
			return instance(config);
		}
	);
}
