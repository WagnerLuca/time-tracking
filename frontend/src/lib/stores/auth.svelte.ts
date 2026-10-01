import { authApi, setAuthHandlers } from '$lib/apiClient';
import { orgContext } from '$lib/stores/orgContext.svelte';
import type { TwoFactorRequiredResponse } from '$lib/apiClient';
import type { LoginRequest, LoginResponse, RegisterRequest, UserInfo } from '$lib/api';

const TOKEN_KEY = 'authToken';
const REFRESH_TOKEN_KEY = 'refreshToken';
const USER_KEY = 'authUser';

function createAuthStore() {
	let user = $state<UserInfo | null>(null);
	let token = $state<string | null>(null);
	let loading = $state(true);
	let twoFactorRequired = $state(false);
	let twoFactorToken = $state<string | null>(null);

	// Initialize from localStorage
	function init() {
		if (typeof window === 'undefined') {
			loading = false;
			return;
		}
		const savedToken = localStorage.getItem(TOKEN_KEY);
		const savedUser = localStorage.getItem(USER_KEY);
		if (savedToken && savedUser) {
			token = savedToken;
			try {
				user = JSON.parse(savedUser);
			} catch {
				clear();
			}
		}
		loading = false;
		window.addEventListener('storage', syncFromOtherTab);
	}

	/** Another tab logged in, logged out or rotated the tokens (localStorage is shared). */
	function syncFromOtherTab(e: StorageEvent) {
		if (e.storageArea !== localStorage || (e.key !== null && e.key !== TOKEN_KEY && e.key !== USER_KEY)) return;
		const savedToken = localStorage.getItem(TOKEN_KEY);
		const savedUser = localStorage.getItem(USER_KEY);
		if (!savedToken) {
			// Logged out elsewhere: the root layout now redirects this tab to /login
			if (token) {
				token = null;
				user = null;
				orgContext.select(null);
			}
			return;
		}
		if (!savedUser) return; // login in progress in the other tab; the user key follows
		let next: UserInfo;
		try {
			next = JSON.parse(savedUser);
		} catch {
			return;
		}
		// A different account signed in elsewhere: drop everything loaded for the old one
		if (user && user.id !== next.id) {
			window.location.reload();
			return;
		}
		token = savedToken;
		user = next;
	}

	function setAuth(loginResponse: LoginResponse) {
		token = loginResponse.accessToken!;
		user = loginResponse.user!;
		localStorage.setItem(TOKEN_KEY, loginResponse.accessToken!);
		localStorage.setItem(REFRESH_TOKEN_KEY, loginResponse.refreshToken!);
		localStorage.setItem(USER_KEY, JSON.stringify(loginResponse.user));
	}

	/** Store tokens rotated by the refresh interceptor (apiClient.ts). */
	function setTokens(accessToken: string, refreshToken: string) {
		token = accessToken;
		localStorage.setItem(TOKEN_KEY, accessToken);
		localStorage.setItem(REFRESH_TOKEN_KEY, refreshToken);
	}

	function clear() {
		token = null;
		user = null;
		localStorage.removeItem(TOKEN_KEY);
		localStorage.removeItem(REFRESH_TOKEN_KEY);
		localStorage.removeItem(USER_KEY);
	}

	async function login(request: LoginRequest): Promise<'success' | '2fa'> {
		const { data } = await authApi.apiV1AuthLoginPost(request);
		// Check if the response is a TwoFactorRequiredResponse
		const maybeTwo = data as unknown as TwoFactorRequiredResponse;
		if (maybeTwo.requiresTwoFactor && maybeTwo.twoFactorToken) {
			twoFactorRequired = true;
			twoFactorToken = maybeTwo.twoFactorToken;
			return '2fa';
		}
		setAuth(data);
		return 'success';
	}

	async function verifyTwoFactor(code: string): Promise<void> {
		if (!twoFactorToken) throw new Error('No 2FA token available');
		const { data } = await authApi.apiV1Auth2faVerifyPost({ twoFactorToken, code });
		twoFactorRequired = false;
		twoFactorToken = null;
		setAuth(data);
	}

	function clearTwoFactor() {
		twoFactorRequired = false;
		twoFactorToken = null;
	}

	async function register(request: RegisterRequest): Promise<void> {
		const { data } = await authApi.apiV1AuthRegisterPost(request);
		setAuth(data);
	}

	async function logout(): Promise<void> {
		try {
			const refreshToken = localStorage.getItem(REFRESH_TOKEN_KEY);
			if (refreshToken) {
				await authApi.apiV1AuthLogoutPost({ refreshToken });
			}
		} catch {
			// Ignore logout errors
		} finally {
			clear();
			// The next account on this browser must not land in the previous one's org
			orgContext.select(null);
		}
	}

	async function fetchCurrentUser(): Promise<void> {
		try {
			const { data: userInfo } = await authApi.apiV1AuthMeGet();
			user = userInfo;
			localStorage.setItem(USER_KEY, JSON.stringify(userInfo));
		} catch {
			clear();
		}
	}

	setAuthHandlers({
		onRefreshed: (tokens) => setTokens(tokens.accessToken, tokens.refreshToken),
		onAuthFailure: clear
	});

	return {
		get user() { return user; },
		get token() { return token; },
		get loading() { return loading; },
		get isAuthenticated() { return !!token && !!user; },
		get twoFactorRequired() { return twoFactorRequired; },
		init,
		clear,
		login,
		verifyTwoFactor,
		clearTwoFactor,
		register,
		logout,
		fetchCurrentUser
	};
}

export const auth = createAuthStore();
