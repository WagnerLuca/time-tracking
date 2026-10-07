import adapter from '@sveltejs/adapter-node';
import { vitePreprocess } from '@sveltejs/vite-plugin-svelte';

/** @type {import('@sveltejs/kit').Config} */
const config = {
	// Consult https://svelte.dev/docs/kit/integrations
	// for more information about preprocessors
	preprocess: vitePreprocess(),

	kit: {
		// Node.js adapter for SSR capabilities
		adapter: adapter({
			out: 'build'
		}),
		// Nonces/hashes for SvelteKit's own inline scripts and the pre-paint script in app.html
		csp: {
			mode: 'auto',
			directives: {
				'default-src': ['self'],
				'script-src': ['self'],
				// Svelte transitions and style="" attributes need inline styles
				'style-src': ['self', 'unsafe-inline'],
				'img-src': ['self', 'data:'],
				'font-src': ['self'],
				// The API is always same-origin: Caddy routes /api/* in production, the Vite
				// proxy in dev (vite.config.ts). Never make this depend on the mode — a
				// `vite build` rewrites .svelte-kit/generated, which the dev server shares.
				'connect-src': ['self'],
				'object-src': ['none'],
				'base-uri': ['self'],
				'form-action': ['self'],
				'frame-ancestors': ['none']
			}
		}
	}
};

export default config;
