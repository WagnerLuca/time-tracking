import { sveltekit } from '@sveltejs/kit/vite';
import { defineConfig } from 'vitest/config';

// Unit tests only (pure TS + .svelte.ts rune modules); no DOM environment needed.
// TZ is pinned so date-key / DST tests behave the same on every machine and in CI.
export default defineConfig({
	plugins: [sveltekit()],
	test: {
		include: ['src/**/*.test.ts'],
		env: { TZ: 'Europe/Berlin' },
		coverage: {
			provider: 'v8',
			reporter: ['text', 'text-summary', 'json-summary', 'lcov', 'html'],
			reportsDirectory: 'coverage',
			// Only .ts (incl. .svelte.ts rune stores): v8 reports untested .svelte
			// components as a single 100 % line, which would inflate the totals.
			// The generated API client and the plain message dictionaries are excluded.
			include: ['src/lib/**/*.ts'],
			exclude: ['src/lib/api/**', 'src/lib/i18n/messages/**', '**/*.test.ts', '**/*.d.ts'],
			// ~2 points below the measured totals: raise them when coverage goes up, never lower them.
			thresholds: { lines: 56, statements: 56, functions: 93, branches: 94 }
		}
	}
});
