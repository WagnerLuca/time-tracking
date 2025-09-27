# Time Tracking Frontend

A SvelteKit application that consumes the ASP.NET Core Web API.

## 🚀 Getting Started

### Prerequisites
- Node.js (v18 or higher)
- .NET 9.0 SDK
- Your backend API running

### Installation

1. Install dependencies:
```bash
npm install
```

2. Start the development server:
```bash
npm run dev
```

The application will be available at `http://localhost:5173`

## 🔧 API Integration

### Method 1: Manual API Calls (Currently Implemented)

The project includes a manual API service (`src/lib/apiService.ts`) that handles:
- HTTP client configuration with Axios
- Request/response interceptors
- Error handling
- Authentication token management

### Method 2: Auto-generated API Client (Swagger Codegen)

Generate a TypeScript client from your API's Swagger specification:

1. **Start your .NET API**:
```bash
cd ../backend
dotnet run
```

2. **Generate the API client**:
```bash
npm run generate-api
```

This will:
- Fetch the Swagger specification from your running API
- Generate TypeScript interfaces and API client classes
- Place the generated code in `src/lib/api/`

3. **Use the generated client**:
```typescript
import { WeatherForecastApi, Configuration } from '$lib/api';

// Create API configuration
const config = new Configuration({
    basePath: 'https://localhost:7000'
});

// Create API instance
const weatherApi = new WeatherForecastApi(config);

// Use the API
const forecast = await weatherApi.weatherForecastGet();
```

### API Configuration

Update the API base URL in `src/lib/apiService.ts`:
```typescript
const API_BASE_URL = 'https://localhost:7000'; // Update this to match your API
```

## 📁 Project Structure

```
src/
├── lib/
│   ├── apiService.ts       # Manual API service
│   └── api/                # Generated API client (after running generate-api)
├── routes/
│   ├── +layout.svelte      # Global layout
│   └── +page.svelte        # Home page with API demo
└── app.html                # HTML template
```

## 🛠 Available Scripts

- `npm run dev` - Start development server
- `npm run build` - Build for production  
- `npm run preview` - Preview production build
- `npm run generate-api` - Generate API client from Swagger
- `npm run check` - Run TypeScript type checking

## 🔗 API Connection

The frontend is configured to connect to your .NET Web API. Make sure:

1. Your API is running (usually on `https://localhost:7000` or `http://localhost:5000`)
2. CORS is configured in your API to allow requests from the frontend
3. The API URL in `apiService.ts` matches your running API

## 🎯 Next Steps

1. **Add CORS support** to your .NET API for the frontend origin
2. **Implement authentication** if needed
3. **Create time tracking components** (timers, projects, reports)
4. **Add more API endpoints** as your backend grows
5. **Use the generated API client** for type-safe API calls

## 🐛 Troubleshooting

### API Connection Issues
- Check if the API is running: `cd ../backend && dotnet run`
- Verify the API URL in `src/lib/apiService.ts`
- Check browser developer tools for CORS errors

### Swagger Generation Issues
- Ensure the API is running before generating the client
- Check if Swagger UI is accessible at `http://localhost:5000/swagger`
- Verify the Swagger JSON endpoint: `http://localhost:5000/swagger/v1/swagger.json`
