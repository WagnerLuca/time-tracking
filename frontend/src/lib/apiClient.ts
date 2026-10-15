import axios from 'axios';
import { env } from '$env/dynamic/public';
import { goto } from '$app/navigation';
import { getErrorCode, getErrorMonth, isTwoFactorRequired } from '$lib/utils/errorHandler';
import { promptDialog } from '$lib/stores/confirm.svelte';
import { formatMonth } from '$lib/utils/closedPeriods';
import { t } from '$lib/i18n';
import { installRefreshInterceptor, type RefreshedTokens } from '$lib/authRefresh';
import { network, offlineError } from '$lib/pwa/network.svelte';
import { Configuration, AbsenceDayApi, ApiKeysApi, AuthApi, ClosedPeriodsApi, ExportImportApi, GroupApi, HolidayApi, InvitationsApi, MemberEntriesApi, NotificationsApi, OrganizationsApi, PauseRulesApi, PushApi, ReportsApi, RequestsApi, TimeTrackingApi, WorkScheduleApi } from '$lib/api';

// Detect base URL: browser uses current hostname, SSR uses Docker service name
const getApiBaseUrl = (): string => {
    const configuredUrl = env.PUBLIC_API_BASE_URL?.trim();
    if (configuredUrl) {
        return configuredUrl;
    }

    if (typeof window !== 'undefined') {
        // Browser: same origin — Caddy routes /api/* in production, the Vite proxy in dev
        return window.location.origin;
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

// Offline (issue #67): fail at once instead of after the timeout. A read is retried when the
// connection is back (the layout remounts the page); a write is never queued or replayed.
axiosInstance.interceptors.request.use((config) => {
    if (typeof navigator === 'undefined' || navigator.onLine !== false) return config;
    const read = (config.method ?? 'get').toLowerCase() === 'get';
    if (read) network.markMissed();
    return Promise.reject(offlineError(read));
});

// A failed `responseType: 'blob'` download (CSV/PDF/JSON export) carries its ProblemDetails as a
// Blob: parse it back to JSON so extractErrorMessage can read `code`/`detail` like everywhere else.
axiosInstance.interceptors.response.use(undefined, async (error) => {
    const data = error?.response?.data;
    if (typeof Blob !== 'undefined' && data instanceof Blob && data.type.includes('json')) {
        try {
            error.response.data = JSON.parse(await data.text());
        } catch {
            // leave the blob; the caller falls back to its own message
        }
    }
    return Promise.reject(error);
});

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
    getAccessToken: () => localStorage.getItem('authToken'),
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

// A closed month (issue #57): an org admin's change needs a reason. Ask for it once and send the
// same request again with it (X-Change-Reason, URL-encoded so any text fits a header). This covers
// every write path at once — entries, corrections, absences, schedules, imports, approvals.
axiosInstance.interceptors.response.use(undefined, async (error) => {
    const config = error?.config;
    if (getErrorCode(error) !== 'period.reasonRequired' || !config || config.headers?.['X-Change-Reason']) {
        return Promise.reject(error);
    }
    const month = getErrorMonth(error);
    const reason = await promptDialog({
        title: t('closedPeriods.reasonTitle'),
        message: t('closedPeriods.reasonMessage', { month: month ? formatMonth(month) : '' }),
        confirmLabel: t('closedPeriods.reasonConfirm'),
        input: { label: t('closedPeriods.reasonLabel'), maxLength: 500 }
    });
    if (!reason) return Promise.reject(error);
    config.headers['X-Change-Reason'] = encodeURIComponent(reason);
    return axiosInstance(config);
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
export const invitationsApi = new InvitationsApi(config, undefined, axiosInstance);
export const memberEntriesApi = new MemberEntriesApi(config, undefined, axiosInstance);
export const closedPeriodsApi = new ClosedPeriodsApi(config, undefined, axiosInstance);
export const pushApi = new PushApi(config, undefined, axiosInstance);
