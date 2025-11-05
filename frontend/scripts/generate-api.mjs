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
import { existsSync, mkdirSync, writeFileSync, readdirSync, renameSync, rmSync, cpSync } from 'fs';
import path from 'path';

const API_BASE_URL = process.env.API_BASE_URL || 'http://localhost:7000';
const SWAGGER_URL = `${API_BASE_URL}/swagger/v1/swagger.json`;
const OUTPUT_DIR = 'src/lib/api';
const DOCS_MD_DIR = 'docs';
const DOCS_HTML_DIR = 'static/api-docs';

console.log('🚀 Generating API client...');
console.log(`📡 Swagger URL: ${SWAGGER_URL}`);
console.log(`📁 Output Directory: ${OUTPUT_DIR}`);

try {
    // Create output directories if they don't exist
    if (!existsSync(OUTPUT_DIR)) {
        mkdirSync(OUTPUT_DIR, { recursive: true });
    }
    if (!existsSync(DOCS_MD_DIR)) {
        mkdirSync(DOCS_MD_DIR, { recursive: true });
    }
    if (!existsSync(DOCS_HTML_DIR)) {
        mkdirSync(DOCS_HTML_DIR, { recursive: true });
    }

    // Generate the API client
    const generateTypescriptCommand = `npx @openapitools/openapi-generator-cli generate -i ${SWAGGER_URL} -g typescript-axios -o ${OUTPUT_DIR} --additional-properties=supportsES6=true,withInterfaces=true,modelPropertyNaming=camelCase`;
    
    console.log('⏳ Running OpenAPI Generator for TypeScript client...');
    execSync(generateTypescriptCommand, { stdio: 'inherit' });

    // Generate HTML documentation with dynamic-html (better styling)
    const generateHtmlCommand = `npx @openapitools/openapi-generator-cli generate -i ${SWAGGER_URL} -g html2 -o ${DOCS_HTML_DIR}`;
    
    console.log('⏳ Generating HTML documentation...');
    execSync(generateHtmlCommand, { stdio: 'inherit' });
    
    
    console.log(`📄 HTML documentation moved to: ${DOCS_HTML_DIR}/index.html`);
    console.log(`🌐 Access in app at: /docs route`);
    
    // Move docs folder to project root and organize
    const docsSourceDir = path.join(OUTPUT_DIR, 'docs');
    const docsTargetDir = 'docs';
    const docsApiDir = path.join(docsTargetDir, 'api');
    const docsModelsDir = path.join(docsTargetDir, 'models');
    
    if (existsSync(docsSourceDir)) {
        // Remove old docs if exists
        if (existsSync(docsTargetDir)) {
            rmSync(docsTargetDir, { recursive: true, force: true });
        }
        
        // Create target directories
        mkdirSync(docsApiDir, { recursive: true });
        mkdirSync(docsModelsDir, { recursive: true });
        
        // Read all doc files
        const docFiles = readdirSync(docsSourceDir).filter(f => f.endsWith('.md'));
        
        // Separate and move API and Model docs
        const apiDocs = [];
        const modelDocs = [];
        
        docFiles.forEach(file => {
            const sourcePath = path.join(docsSourceDir, file);
            if (file.includes('Api.md')) {
                const targetPath = path.join(docsApiDir, file);
                renameSync(sourcePath, targetPath);
                apiDocs.push(file);
            } else {
                const targetPath = path.join(docsModelsDir, file);
                renameSync(sourcePath, targetPath);
                modelDocs.push(file);
            }
        });
        
        // Remove the now-empty source docs directory
        rmSync(docsSourceDir, { recursive: true, force: true });
        
        console.log(`📚 Organized docs:`);
        console.log(`   - API endpoints: ${docsApiDir} (${apiDocs.length} files)`);
        console.log(`   - Models: ${docsModelsDir} (${modelDocs.length} files)`);
        
        // Generate README.md based on available docs
        let readmeContent = `# Time Tracking API Client

Auto-generated TypeScript API client for the Time Tracking backend.

## Documentation

`;
        
        // Add API Endpoints section
        if (apiDocs.length > 0) {
            readmeContent += `### API Endpoints\n`;
            apiDocs.forEach(file => {
                const name = file.replace('.md', '');
                readmeContent += `- [${name}](api/${file}) - ${name.replace('Api', '')} endpoints\n`;
            });
            readmeContent += '\n';
        }
        
        // Add Models section
        if (modelDocs.length > 0) {
            readmeContent += `### Models\n`;
            modelDocs.forEach(file => {
                const name = file.replace('.md', '');
                readmeContent += `- [${name}](models/${file}) - ${name} model\n`;
            });
            readmeContent += '\n';
        }
        
        readmeContent += `## Usage

\`\`\`typescript
import { UsersApi, Configuration } from '$lib/api';

// Create configuration (optional)
const configuration = new Configuration({
    basePath: 'http://localhost:7000'
});

// Create API instance
const usersApi = new UsersApi(configuration);

// Make API calls
const users = await usersApi.apiUsersGet();
\`\`\`

## Documentation Formats

- **Markdown**: Browse the [API](docs/api/) and [Models](docs/models/) folders
- **HTML**: Launch application with \`npm run dev\` and navigate to \`/docs\` route

## Generated

This client was auto-generated using OpenAPI Generator.
To regenerate: \`npm run generate-api\` 
`;

        writeFileSync('./docs/README.md', readmeContent, 'utf8');
        console.log('📖 Generated README.md with documentation links');
    }
    
    console.log('✅ API client generated successfully!');
    console.log(`📦 Client available at: ${OUTPUT_DIR}`);
    console.log(`📖 Documentation: ./docs/`);
    console.log('');
    console.log('Usage example:');
    console.log('```typescript');
    console.log('import { UsersApi } from "$lib/api";');
    console.log('');
    console.log('const api = new UsersApi();');
    console.log('const users = await api.apiUsersGet();');
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