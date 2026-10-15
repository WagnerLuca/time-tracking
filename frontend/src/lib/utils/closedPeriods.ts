import { formatDate } from './formatters';
import { dateKey } from './dateHelpers';
import { t } from '$lib/i18n';

/**
 * Closed months (issue #57). The API speaks first days ('YYYY-MM-DD', org detail `closedMonths`);
 * the UI compares months as 'YYYY-MM'. The backend guard is the authority — these only decide
 * what to lock and mark in the UI.
 */

/** 'YYYY-MM' of a 'YYYY-MM-DD' key (or of a 'YYYY-MM' month). */
export const monthKey = (day: string): string => day.slice(0, 7);

export function closedMonthSet(months: readonly string[] | null | undefined): Set<string> {
	return new Set((months ?? []).map(monthKey));
}

export function isClosedDay(day: string | null | undefined, closed: Set<string>): boolean {
	return !!day && closed.has(monthKey(day));
}

/** "September 2026" / "September 2026" for '2026-09' or '2026-09-01'. */
export function formatMonth(month: string): string {
	const [y, m] = month.split('-').map(Number);
	return formatDate(new Date(y, m - 1, 1), { month: 'long', year: 'numeric' });
}

/** 'YYYY-MM-DD' of an instant in the organization's time zone (the browser's without one or for an unknown zone). */
export function orgDateKey(instant: Date, timeZone?: string | null): string {
	if (!timeZone) return dateKey(instant);
	try {
		const parts = new Intl.DateTimeFormat('en', { timeZone, year: 'numeric', month: '2-digit', day: '2-digit' }).formatToParts(instant);
		const part = (type: string) => parts.find((p) => p.type === type)?.value;
		return `${part('year')}-${part('month')}-${part('day')}`;
	} catch {
		return dateKey(instant);
	}
}

/** The latest month that may be closed: the one before the current month in the organization's time zone. */
export function lastClosableMonth(now: Date = new Date(), timeZone?: string | null): string {
	const [y, m] = orgDateKey(now, timeZone).split('-').map(Number);
	return m === 1 ? `${y - 1}-12` : `${y}-${String(m - 1).padStart(2, '0')}`;
}

/** The months from..to ('YYYY-MM', inclusive); empty when to is before from. */
export function monthsBetween(from: string, to: string): string[] {
	const out: string[] = [];
	let [y, m] = from.split('-').map(Number);
	const [ty, tm] = to.split('-').map(Number);
	while (y < ty || (y === ty && m <= tm)) {
		out.push(`${y}-${String(m).padStart(2, '0')}`);
		if (++m > 12) { m = 1; y++; }
	}
	return out;
}

/** Tooltip of a locked day: "Abgeschlossen (September 2026) – nur Admins können hier ändern". */
export function lockText(day: string): string {
	return t('closedPeriods.locked', { month: formatMonth(day) });
}

/**
 * Why the caller may not change this entry, or '' when they may: its start day (in the organization's
 * time zone, like the backend guard) lies in a closed month of the organization and the caller is no
 * org admin (admins change it with a reason).
 */
export function entryLock(
	entry: { startTime?: string | null; organizationId?: number | null },
	org: { id?: number; closedMonths?: string[] | null; timeZone?: string | null } | null | undefined,
	isAdmin: boolean
): string {
	if (isAdmin || !org || !entry.startTime || entry.organizationId !== org.id) return '';
	const day = orgDateKey(new Date(entry.startTime), org.timeZone);
	return isClosedDay(day, closedMonthSet(org.closedMonths)) ? lockText(day) : '';
}
