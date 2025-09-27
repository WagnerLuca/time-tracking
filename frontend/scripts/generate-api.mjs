#!/usr/bin/env node

/**
 * API Client Generation Script
 * 
 * This script generates TypeScript API clients from your .NET Web API's Swagger specification.
 * 
 * Usage:
 * 1. Make sure your .NET API is running (dotnet run)
 * 2. Run: npm run generate-api
 * 
 * The generated client will be available in src/lib/api/
 */

import { execSync } from 'child_process';
import { existsSync, mkdirSync } from 'fs';
import path from 'path';

const API_BASE_URL = process.env.API_BASE_URL || 'http://localhost:7000';
const SWAGGER_URL = `${API_BASE_URL}/swagger/v1/swagger.json`;
const OUTPUT_DIR = 'src/lib/api';

console.log('🚀 Generating API client...');
console.log(`📡 Swagger URL: ${SWAGGER_URL}`);
console.log(`📁 Output Directory: ${OUTPUT_DIR}`);

try {
    // Create output directory if it doesn't exist
    if (!existsSync(OUTPUT_DIR)) {
        mkdirSync(OUTPUT_DIR, { recursive: true });
    }

    // Generate the API client
    const command = `npx @openapitools/openapi-generator-cli generate -i ${SWAGGER_URL} -g typescript-axios -o ${OUTPUT_DIR} --additional-properties=supportsES6=true,withInterfaces=true,modelPropertyNaming=camelCase`;
    
    console.log('⏳ Running OpenAPI Generator...');
    execSync(command, { stdio: 'inherit' });
    
    console.log('✅ API client generated successfully!');
    console.log(`📦 Client available at: ${OUTPUT_DIR}`);
    console.log('');
    console.log('Usage example:');
    console.log('```typescript');
    console.log('import { WeatherForecastApi } from "$lib/api";');
    console.log('');
    console.log('const api = new WeatherForecastApi();');
    console.log('const forecast = await api.weatherForecastGet();');
    console.log('```');
    
} catch (error) {
    console.error('❌ Error generating API client:');
    console.error(error.message);
    
    console.log('');
    console.log('💡 Troubleshooting:');
    console.log('1. Make sure your .NET API is running: cd backend && dotnet run');
    console.log('2. Check if Swagger is accessible at:', SWAGGER_URL);
    console.log('3. Verify the API URL in the script matches your running API');
    
    process.exit(1);
}