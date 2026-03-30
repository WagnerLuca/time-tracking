import axios from 'axios';
import { env } from '$env/dynamic/public';
import { Configuration, AbsenceDayApi, AuthApi, HolidayApi, NotificationsApi, OrganizationsApi, PauseRulesApi, RequestsApi, TimeTrackingApi, WorkScheduleApi } from '$lib/api';

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

// Response interceptor: handle 401
axiosInstance.interceptors.response.use(
    (response) => response,
    (error) => {
        if (error.response?.status === 401) {
            localStorage.removeItem('authToken');
            localStorage.removeItem('refreshToken');
            localStorage.removeItem('authUser');
            if (typeof window !== 'undefined' && !window.location.pathname.startsWith('/login')) {
                window.location.href = '/login';
            }
        }
        return Promise.reject(error);
    }
);

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
export const workScheduleApi = new WorkScheduleApi(config, undefined, axiosInstance);

// Type for the polymorphic 2FA-required login response (not in generated types
// because the login endpoint declares LoginResponse as its return type).
export interface TwoFactorRequiredResponse {
	twoFactorToken: string;
	requiresTwoFactor: boolean;
}
