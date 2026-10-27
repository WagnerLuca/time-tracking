/**
 * One place for the app's colour decisions. Everything here resolves to the
 * theme colours in app.css or to @wagnerluca/ui's accent tokens, so it flips
 * correctly in dark mode — no hardcoded Tailwind palette classes or hex values
 * in components. Colours the user picks (group / custom absence type) stay user
 * data; they are only ever used as a tint via color-mix, so text keeps the
 * theme's base-content colour and stays readable on either background.
 */

/** Design-system accents usable as distinguishable series colours (tokens.css). */
export const ACCENTS = ['school', 'berry', 'teal', 'game', 'future', 'time', 'mustard', 'slate'] as const;
export type Accent = (typeof ACCENTS)[number];

/** Group colour presets = the accents' mid tones (readable as dot / border / tint in both themes). */
export const GROUP_COLOR_PRESETS = ['#6B8CAE', '#B87373', '#5A9188', '#C98A3F', '#9B8AA6', '#7FA98E', '#C4A44A', '#7B8B9E'];
export const DEFAULT_GROUP_COLOR = '#7B8B9E';

const SAFE_COLOR = /^#[0-9a-f]{3,8}$/i;

/** A user-supplied colour, or the default when it is missing or not a plain hex value. */
export function safeColor(color: string | null | undefined, fallback = DEFAULT_GROUP_COLOR): string {
	return color && SAFE_COLOR.test(color) ? color : fallback;
}

/** Inline style for a tag/chip tinted with a (user) colour; text colour is left to the theme. */
export function tintStyle(color: string | null | undefined, strength = 15): string {
	const c = safeColor(color);
	return `border-color: color-mix(in srgb, ${c} 45%, transparent); background: color-mix(in srgb, ${c} ${strength}%, transparent);`;
}

/** Stable accent for the n-th person in a team view (cycles through ACCENTS). */
export function personAccent(index: number): Accent {
	return ACCENTS[((index % ACCENTS.length) + ACCENTS.length) % ACCENTS.length];
}

/** Inline style for a per-person bar/chip in team calendars (soft fill, accent border, theme text). */
export function personChipStyle(index: number): string {
	const a = personAccent(index);
	return `background: var(--accent-${a}-soft); border-color: var(--accent-${a}); color: var(--color-text);`;
}

/** Inline style for a per-person dot. */
export function personDotStyle(index: number): string {
	return `background: var(--accent-${personAccent(index)});`;
}

export type DayType = 'holiday' | 'sick' | 'vacation' | 'other-absence';

export interface DayTypeClasses {
	/** Small solid dot / legend swatch. */
	dot: string;
	/** Soft row / cell background. */
	soft: string;
	/** Badge colour modifier (lib/theme/components.css). */
	badge: string;
	/** Emoji marker used in compact calendar cells. */
	icon: string;
}

/** Colours per day type — shared by dashboard, timer, calendars and legends. */
export const DAY_TYPE_CLASSES: Record<DayType, DayTypeClasses> = {
	holiday: { dot: 'bg-secondary', soft: 'bg-secondary/10', badge: 'badge-secondary', icon: '🏖️' },
	sick: { dot: 'bg-error', soft: 'bg-error/10', badge: 'badge-error', icon: '🤒' },
	vacation: { dot: 'bg-info', soft: 'bg-info/10', badge: 'badge-info', icon: '✈️' },
	'other-absence': { dot: 'bg-base-content/40', soft: 'bg-base-content/10', badge: 'badge-ghost', icon: '📋' }
};

/** Classes for a day type, or empty strings for a normal day. */
export function dayTypeClasses(dayType: string | null | undefined): DayTypeClasses {
	return DAY_TYPE_CLASSES[dayType as DayType] ?? { dot: '', soft: '', badge: 'badge-ghost', icon: '' };
}

/** Classes for an absence type enum value ('SickDay' | 'Vacation' | 'Other', or their numeric strings). */
export function absenceTypeClasses(type: string | null | undefined): DayTypeClasses {
	if (type === 'SickDay' || type === '0') return DAY_TYPE_CLASSES.sick;
	if (type === 'Vacation' || type === '1') return DAY_TYPE_CLASSES.vacation;
	return DAY_TYPE_CLASSES['other-absence'];
}

interface AbsenceLike {
	userId?: number | null;
	type?: string | null;
	customTypeName?: string | null;
	customTypeColor?: string | null;
}

/**
 * Team calendar chip colours: vacation gets a per-person accent (tells people apart),
 * custom types their own colour, sick/other the shared day-type colours (`absenceChipClass`).
 */
export function absenceChipStyle(a: AbsenceLike): string {
	if (a.customTypeName) return tintStyle(a.customTypeColor, 25);
	if (a.type === 'Vacation') return personChipStyle(a.userId ?? 0);
	return '';
}

export function absenceChipClass(a: AbsenceLike): string {
	if (a.customTypeName || a.type === 'Vacation') return '';
	return `${dayTypeClasses(a.type === 'SickDay' ? 'sick' : 'other-absence').soft} border-base-content/20`;
}

/** Heat colour for a calendar cell based on the worked/target ratio (theme tokens, dark-mode aware). */
export function heatColor(minutes: number, target: number): string {
	if (minutes === 0) return 'transparent';
	if (target === 0) return 'color-mix(in srgb, var(--color-info) 35%, transparent)';
	const ratio = minutes / target;
	if (ratio >= 1) return 'color-mix(in srgb, var(--color-success) 70%, transparent)';
	if (ratio >= 0.75) return 'color-mix(in srgb, var(--color-success) 40%, transparent)';
	if (ratio >= 0.5) return 'color-mix(in srgb, var(--color-warning) 45%, transparent)';
	return 'color-mix(in srgb, var(--color-error) 35%, transparent)';
}
