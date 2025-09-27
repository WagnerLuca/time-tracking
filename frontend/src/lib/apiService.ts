import axios, { type AxiosInstance } from 'axios';

// Base API configuration
const API_BASE_URL = 'http://localhost:7000'; // Adjust this to match your API URL

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
        // Clear token and redirect to login
        localStorage.removeItem('authToken');
        // You can add navigation logic here
    }

    // Weather API methods (example from your current API)
    async getWeatherForecast() {
        try {
            const response = await this.api.get('/WeatherForecast');
            return response.data;
        } catch (error) {
            console.error('Error fetching weather forecast:', error);
            throw error;
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