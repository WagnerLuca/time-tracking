import { sveltekit } from '@sveltejs/kit/vite';
import tailwindcss from '@tailwindcss/vite';
import { defineConfig } from 'vite';

export default defineConfig({
	plugins: [tailwindcss(), sveltekit()],
	// 5173 is usually taken by other projects; the backend's dev CORS and the e2e config expect 5179
	server: { port: 5179, strictPort: true }
});