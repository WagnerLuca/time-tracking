import { authApi } from '$lib/apiClient';
import type { LoginRequest, LoginResponse, RegisterRequest, UserInfo } from '$lib/api';

const TOKEN_KEY = 'authToken';
const REFRESH_TOKEN_KEY = 'refreshToken';
const USER_KEY = 'authUser';

function createAuthStore() {
	let user = $state<UserInfo | null>(null);
	let token = $state<string | null>(null);
	let loading = $state(true);

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
	}

	function setAuth(loginResponse: LoginResponse) {
		token = loginResponse.accessToken!;
		user = loginResponse.user!;
		localStorage.setItem(TOKEN_KEY, loginResponse.accessToken!);
		localStorage.setItem(REFRESH_TOKEN_KEY, loginResponse.refreshToken!);
		localStorage.setItem(USER_KEY, JSON.stringify(loginResponse.user));
	}

	function clear() {
		token = null;
		user = null;
		localStorage.removeItem(TOKEN_KEY);
		localStorage.removeItem(REFRESH_TOKEN_KEY);
		localStorage.removeItem(USER_KEY);
	}

	async function login(request: LoginRequest): Promise<void> {
		const { data } = await authApi.apiAuthLoginPost(request);
		setAuth(data);
	}

	async function register(request: RegisterRequest): Promise<void> {
		const { data } = await authApi.apiAuthRegisterPost(request);
		setAuth(data);
	}

	async function logout(): Promise<void> {
		try {
			const refreshToken = localStorage.getItem(REFRESH_TOKEN_KEY);
			if (refreshToken) {
				await authApi.apiAuthLogoutPost({ refreshToken });
			}
		} catch {
			// Ignore logout errors
		} finally {
			clear();
		}
	}

	async function fetchCurrentUser(): Promise<void> {
		try {
			const { data: userInfo } = await authApi.apiAuthMeGet();
			user = userInfo;
			localStorage.setItem(USER_KEY, JSON.stringify(userInfo));
		} catch {
			clear();
		}
	}

	return {
		get user() { return user; },
		get token() { return token; },
		get loading() { return loading; },
		get isAuthenticated() { return !!token && !!user; },
		init,
		login,
		register,
		logout,
		fetchCurrentUser
	};
}

export const auth = createAuthStore();
