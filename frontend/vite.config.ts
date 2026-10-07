import { sveltekit } from '@sveltejs/kit/vite';
import tailwindcss from '@tailwindcss/vite';
import { createRequire } from 'node:module';
import { fileURLToPath } from 'node:url';
import { defineConfig } from 'vite';

// @wagnerluca/ui is an optional dependency: it only installs with a GitHub Packages
// token. Without it, app.css's `@import "@wagnerluca/ui/tokens.css"` gets the bundled
// copy instead, so the app always builds and looks the same (`npm run tokens:sync`).
const designTokens = (() => {
	try {
		return createRequire(import.meta.url).resolve('@wagnerluca/ui/tokens.css');
	} catch {
		return fileURLToPath(new URL('./src/lib/theme/wl-tokens.fallback.css', import.meta.url));
	}
})();
const usingPackage = !designTokens.endsWith('wl-tokens.fallback.css');
console.log(`[theme] design tokens from ${usingPackage ? '@wagnerluca/ui' : 'the bundled fallback'}`);

export default defineConfig({
	plugins: [tailwindcss(), sveltekit()],
	resolve: { alias: { '@wagnerluca/ui/tokens.css': designTokens } },
	// 5173 is usually taken by other projects; the e2e config expects 5179. /api goes to the
	// backend so the browser stays same-origin, exactly like behind Caddy in production.
	server: { port: 5179, strictPort: true, proxy: { '/api': 'http://localhost:7000' } },
	preview: { proxy: { '/api': 'http://localhost:7000' } }
});
