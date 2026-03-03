/**
 * Canonical date helper functions used across all pages.
 */

/**
 * Format a Date as 'YYYY-MM-DD' string (used as map keys and for API calls).
 *
 * @example dateKey(new Date(2026, 0, 5)) → '2026-01-05'
 */
export function dateKey(d: Date): string {
	return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

/**
 * Check if a date is today.
 */
export function isToday(date: Date): boolean {
	const now = new Date();
	return date.getFullYear() === now.getFullYear() &&
		date.getMonth() === now.getMonth() &&
		date.getDate() === now.getDate();
}

/**
 * Check if a date is in the future (after today).
 */
export function isFuture(date: Date): boolean {
	const now = new Date();
	now.setHours(0, 0, 0, 0);
	const d = new Date(date);
	d.setHours(0, 0, 0, 0);
	return d > now;
}

/**
 * Get the Monday-to-Sunday date range for a given week offset from the current week.
 *
 * @param offset 0 = current week, -1 = last week, 1 = next week
 * @returns { start: Date (Monday 00:00), end: Date (Sunday 23:59:59.999) }
 */
export function getWeekRange(offset: number): { start: Date; end: Date } {
	const now = new Date();
	const start = new Date(now);
	const dayOfWeek = now.getDay() || 7; // Sunday = 7 (Mon-Sun week)
	start.setDate(now.getDate() - dayOfWeek + 1 + offset * 7); // Monday
	start.setHours(0, 0, 0, 0);
	const end = new Date(start);
	end.setDate(start.getDate() + 6);
	end.setHours(23, 59, 59, 999);
	return { start, end };
}

/**
 * Convert an ISO datetime string to a datetime-local input value (YYYY-MM-DDTHH:MM).
 */
export function toLocalDateTimeInput(iso: string): string {
	const d = new Date(iso);
	const pad = (n: number) => String(n).padStart(2, '0');
	return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

/**
 * Get today's date as a YYYY-MM-DD string.
 */
export function todayKey(): string {
	return dateKey(new Date());
}

/**
 * Sum net/gross minutes from an array of time entries.
 * Skips running entries.
 */
export function sumMinutes(entries: Array<{ isRunning?: boolean; netDurationMinutes?: number | null; durationMinutes?: number | null }>): number {
	return entries
		.filter(e => !e.isRunning && (e.netDurationMinutes ?? e.durationMinutes))
		.reduce((s, e) => s + (e.netDurationMinutes ?? e.durationMinutes ?? 0), 0);
}
