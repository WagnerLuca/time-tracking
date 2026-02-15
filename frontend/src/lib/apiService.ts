import axios, { type AxiosInstance } from 'axios';

// Base API configuration - automatically detects environment
const getApiBaseUrl = (): string => {
    // Check if we're running in a container (Docker environment)
    if (typeof window !== 'undefined' && window.location.hostname !== 'localhost') {
        // In Docker, use the service name from docker-compose
        return 'http://backend:7000';
    }
    // Default to localhost for local development
    return 'http://localhost:7000';
};

const API_BASE_URL = getApiBaseUrl();

class ApiService {
    private api: AxiosInstance;

    constructor() {
        this.api = axios.create({
            baseURL: API_BASE_URL,
            timeout: 10000,
            headers: {
                'Content-Type': 'application/json',
            },
        });

        // Request interceptor
        this.api.interceptors.request.use(
            (config) => {
                // Add auth token if available
                const token = this.getAuthToken();
                if (token) {
                    config.headers.Authorization = `Bearer ${token}`;
                }
                return config;
            },
            (error) => {
                return Promise.reject(error);
            }
        );

        // Response interceptor
        this.api.interceptors.response.use(
            (response) => response,
            (error) => {
                // Handle common error responses
                if (error.response?.status === 401) {
                    // Handle unauthorized access
                    this.handleUnauthorized();
                }
                return Promise.reject(error);
            }
        );
    }

    private getAuthToken(): string | null {
        // Get token from localStorage, cookies, or store
        return localStorage.getItem('authToken');
    }

    private handleUnauthorized(): void {
        // Clear auth state and redirect to login
        localStorage.removeItem('authToken');
        localStorage.removeItem('refreshToken');
        localStorage.removeItem('authUser');
        if (typeof window !== 'undefined' && !window.location.pathname.startsWith('/login')) {
            window.location.href = '/login';
        }
    }

    // Generic HTTP methods
    async get<T>(url: string): Promise<T> {
        const response = await this.api.get<T>(url);
        return response.data;
    }

    async post<T>(url: string, data?: any): Promise<T> {
        const response = await this.api.post<T>(url, data);
        return response.data;
    }

    async put<T>(url: string, data?: any): Promise<T> {
        const response = await this.api.put<T>(url, data);
        return response.data;
    }

    async delete<T>(url: string): Promise<T> {
        const response = await this.api.delete<T>(url);
        return response.data;
    }
}

// Export singleton instance
export const apiService = new ApiService();
export default apiService;