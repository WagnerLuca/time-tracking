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
 * Kept free of SvelteKit imports so it can be unit-tested with a mock adapter.
 */
export interface RefreshedTokens {
	accessToken: string;
	refreshToken: string;
}

export interface RefreshDeps {
	getRefreshToken: () => string | null;
	refresh: (refreshToken: string) => Promise<RefreshedTokens>;
	onRefreshed: (tokens: RefreshedTokens) => void;
	onAuthFailure: () => void;
}

/** Endpoints whose 401 means "wrong credentials", not "expired access token". */
const AUTH_ENDPOINT = /\/auth\/(login|register|refresh|logout|2fa\/verify)(\?|$)/;

type RetriableConfig = InternalAxiosRequestConfig & { _retried?: boolean };

export function installRefreshInterceptor(instance: AxiosInstance, deps: RefreshDeps): void {
	let inflight: Promise<string> | null = null;

	function refreshOnce(): Promise<string> {
		inflight ??= (async () => {
			const refreshToken = deps.getRefreshToken();
			if (!refreshToken) throw new Error('No refresh token');
			const tokens = await deps.refresh(refreshToken);
			deps.onRefreshed(tokens);
			return tokens.accessToken;
		})().finally(() => {
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
