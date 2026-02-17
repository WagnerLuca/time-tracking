import axios from 'axios';
import { Configuration, AuthApi, OrganizationsApi, TimeTrackingApi, UsersApi } from '$lib/api';

// Automatically detect base URL based on environment
const getApiBaseUrl = (): string => {
    if (typeof window !== 'undefined' && window.location.hostname !== 'localhost') {
        return 'http://backend:7000';
    }
    return 'http://localhost:7000';
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
export const organizationsApi = new OrganizationsApi(config, undefined, axiosInstance);
export const timeTrackingApi = new TimeTrackingApi(config, undefined, axiosInstance);
export const usersApi = new UsersApi(config, undefined, axiosInstance);
