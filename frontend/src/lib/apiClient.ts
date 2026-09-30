import axios from 'axios';
import { env } from '$env/dynamic/public';
import { goto } from '$app/navigation';
import { isTwoFactorRequired } from '$lib/utils/errorHandler';
import { installRefreshInterceptor, type RefreshedTokens } from '$lib/authRefresh';
import { Configuration, AbsenceDayApi, ApiKeysApi, AuthApi, ExportImportApi, GroupApi, HolidayApi, NotificationsApi, OrganizationsApi, PauseRulesApi, ReportsApi, RequestsApi, TimeTrackingApi, WorkScheduleApi } from '$lib/api';

// Detect base URL: browser uses current hostname, SSR uses Docker service name
const getApiBaseUrl = (): string => {
    const configuredUrl = env.PUBLIC_API_BASE_URL?.trim();
    if (configuredUrl) {
        return configuredUrl;
    }

    if (typeof window !== 'undefined') {
        // Browser: same host, backend on port 7000
        return `${window.location.protocol}//${window.location.hostname}:7000`;
    }
    // SSR: Docker internal or fallback
    return 'http://backend:7000';
};

// Shared axios instance with interceptors
const axiosInstance = axios.create({
    timeout: 10000,
    headers: { 'Content-Type': 'application/json' },
});

// Request interceptor: inject Bearer token
axiosInstance.interceptors.request.use(
    (config) => {
        const token = localStorage.getItem('authToken');
        if (token) {
            config.headers.Authorization = `Bearer ${token}`;
        }
        return config;
    },
    (error) => Promise.reject(error)
);

// Response interceptor: on 401 refresh the access token once (shared by concurrent
// requests) and retry; only a failed refresh ends the session. See authRefresh.ts.
export interface AuthHandlers {
    onRefreshed: (tokens: RefreshedTokens) => void;
    onAuthFailure: () => void;
}

let authHandlers: AuthHandlers = {
    onRefreshed: (tokens) => {
        localStorage.setItem('authToken', tokens.accessToken);
        localStorage.setItem('refreshToken', tokens.refreshToken);
    },
    onAuthFailure: () => {
        localStorage.removeItem('authToken');
        localStorage.removeItem('refreshToken');
        localStorage.removeItem('authUser');
    }
};

/** Lets the auth store keep its reactive state in sync with refreshes and forced logouts. */
export function setAuthHandlers(handlers: AuthHandlers): void {
    authHandlers = handlers;
}

installRefreshInterceptor(axiosInstance, {
    getRefreshToken: () => localStorage.getItem('refreshToken'),
    refresh: async (refreshToken) => {
        const { data } = await authApi.apiV1AuthRefreshPost({ refreshToken });
        return { accessToken: data.accessToken!, refreshToken: data.refreshToken! };
    },
    onRefreshed: (tokens) => authHandlers.onRefreshed(tokens),
    onAuthFailure: () => {
        authHandlers.onAuthFailure();
        if (typeof window !== 'undefined' && !window.location.pathname.startsWith('/login')) {
            const next = window.location.pathname + window.location.search;
            window.location.href = `/login?next=${encodeURIComponent(next)}`;
        }
    }
});

// An org that requires 2FA answers 403 "TwoFactorRequired" until the user sets it up:
// send them to the 2FA section (the error still reaches the caller, localized by extractErrorMessage).
axiosInstance.interceptors.response.use(undefined, (error) => {
    if (isTwoFactorRequired(error) && typeof window !== 'undefined' && window.location.pathname !== '/settings') {
        goto('/settings#two-factor');
    }
    return Promise.reject(error);
});

// SDK Configuration — accessToken is a function so it reads fresh token on every request
const config = new Configuration({
    basePath: getApiBaseUrl(),
    accessToken: () => localStorage.getItem('authToken') ?? '',
});

// Export typed API instances
export const authApi = new AuthApi(config, undefined, axiosInstance);
export const absenceDayApi = new AbsenceDayApi(config, undefined, axiosInstance);
export const holidayApi = new HolidayApi(config, undefined, axiosInstance);
export const notificationsApi = new NotificationsApi(config, undefined, axiosInstance);
export const organizationsApi = new OrganizationsApi(config, undefined, axiosInstance);
export const pauseRulesApi = new PauseRulesApi(config, undefined, axiosInstance);
export const requestsApi = new RequestsApi(config, undefined, axiosInstance);
export const timeTrackingApi = new TimeTrackingApi(config, undefined, axiosInstance);
export const exportImportApi = new ExportImportApi(config, undefined, axiosInstance);
export const workScheduleApi = new WorkScheduleApi(config, undefined, axiosInstance);
export const groupApi = new GroupApi(config, undefined, axiosInstance);
export const reportsApi = new ReportsApi(config, undefined, axiosInstance);
export const apiKeysApi = new ApiKeysApi(config, undefined, axiosInstance);

// Type for the polymorphic 2FA-required login response (not in generated types
// because the login endpoint declares LoginResponse as its return type).
export interface TwoFactorRequiredResponse {
	twoFactorToken: string;
	requiresTwoFactor: boolean;
}
