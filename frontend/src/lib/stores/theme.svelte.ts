/**
 * Theme store — light/dark, mirroring @wagnerluca/ui's useTheme(): a `.dark`
 * class on <html> flips the design tokens, persisted under the shared
 * `wl-theme` key so the choice follows the visitor across Wagner Luca apps.
 * app.html applies it before first paint.
 */

const STORAGE_KEY = 'wl-theme';
/** Status bar / title bar colour: the nav's surface (--color-surface), also in app.html and the manifest. */
export const THEME_COLORS = { light: '#FFFFFF', dark: '#201F23' };

function createThemeStore() {
	let isDark = $state(false);

	function apply() {
		document.documentElement.classList.toggle('dark', isDark);
		document.documentElement.classList.toggle('light', !isDark);
		document.querySelector?.('meta[name="theme-color"]')?.setAttribute('content', isDark ? THEME_COLORS.dark : THEME_COLORS.light);
	}

	function load() {
		if (typeof window === 'undefined') return;
		let saved: string | null = null;
		try { saved = localStorage.getItem(STORAGE_KEY); } catch { /* storage blocked */ }
		isDark = saved ? saved === 'dark' : window.matchMedia('(prefers-color-scheme: dark)').matches;
		apply();
	}

	function set(dark: boolean) {
		isDark = dark;
		try { localStorage.setItem(STORAGE_KEY, dark ? 'dark' : 'light'); } catch { /* storage blocked */ }
		apply();
	}

	return {
		get isDark() { return isDark; },
		load,
		set
	};
}

export const theme = createThemeStore();
