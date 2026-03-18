/**
 * Theme store — persists the selected DaisyUI theme to localStorage
 * and applies it to the <html> element via data-theme attribute.
 */

const STORAGE_KEY = 'time-tracking-theme';

const ALL_THEMES = [
	'light', 'dark', 'cupcake', 'bumblebee', 'emerald', 'corporate',
	'synthwave', 'retro', 'cyberpunk', 'valentine', 'halloween', 'garden',
	'forest', 'aqua', 'lofi', 'pastel', 'fantasy', 'wireframe', 'black',
	'luxury', 'dracula', 'cmyk', 'autumn', 'business', 'acid', 'lemonade',
	'night', 'coffee', 'winter', 'dim', 'nord', 'sunset', 'caramellatte',
	'abyss', 'silk'
] as const;

export type ThemeName = (typeof ALL_THEMES)[number];

const LEGACY_THEME_ALIASES: Record<string, ThemeName> = {
	caramelatte: 'caramellatte'
};

function normalizeTheme(rawTheme: string | null): ThemeName | null {
	if (!rawTheme) return null;
	if (ALL_THEMES.includes(rawTheme as ThemeName)) {
		return rawTheme as ThemeName;
	}
	return LEGACY_THEME_ALIASES[rawTheme] ?? null;
}

function createThemeStore() {
	let current = $state<ThemeName>('light');

	function load() {
		if (typeof window === 'undefined') return;
		const saved = localStorage.getItem(STORAGE_KEY);
		const normalized = normalizeTheme(saved);
		if (normalized) {
			current = normalized;
			if (saved !== normalized) {
				localStorage.setItem(STORAGE_KEY, normalized);
			}
		}
		apply();
	}

	function apply() {
		if (typeof document === 'undefined') return;
		document.documentElement.setAttribute('data-theme', current);
	}

	function set(theme: ThemeName) {
		current = theme;
		localStorage.setItem(STORAGE_KEY, theme);
		apply();
	}

	return {
		get current() { return current; },
		get themes() { return ALL_THEMES; },
		load,
		set
	};
}

export const theme = createThemeStore();
