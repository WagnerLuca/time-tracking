/**
 * Canonical formatting functions used across all pages.
 * Each function exists here ONLY — no local copies in page files.
 *
 * Everything user-visible goes through the current UI locale (de-DE / en-GB):
 * never call toLocaleString()/toLocaleDateString() with a hardcoded or default
 * locale in a component — use formatDate()/formatDateTime()/formatNumber() here.
 */
import { locale } from '$lib/stores/locale.svelte';
import { t, type MessageKey } from '$lib/i18n';
import { de } from '$lib/i18n/de';
import { lookup } from '$lib/i18n/translate';

// ── Locale-aware primitives ──

function toDate(date: Date | string): Date {
	return typeof date === 'string' ? new Date(date.length === 10 ? date + 'T00:00:00' : date) : date;
}

/** Format a date (Date, ISO timestamp or 'YYYY-MM-DD') with Intl options in the UI locale. */
export function formatDate(date: Date | string, options: Intl.DateTimeFormatOptions = { day: '2-digit', month: '2-digit', year: 'numeric' }): string {
	return toDate(date).toLocaleDateString(locale.intl, options);
}

/** Date and time, e.g. "05.01.2026, 14:30" (de) / "05/01/2026, 14:30" (en). */
export function formatDateTime(date: Date | string): string {
	return toDate(date).toLocaleString(locale.intl, { dateStyle: 'medium', timeStyle: 'short' });
}

/** A number in the UI locale, e.g. 7.5 → "7,5" (de). */
export function formatNumber(value: number, options?: Intl.NumberFormatOptions): string {
	return new Intl.NumberFormat(locale.intl, options).format(value);
}

/** Mon…Sun short weekday names in the UI locale (index 0 = Monday). */
export function dayNamesShort(): string[] {
	// 2024-01-01 was a Monday
	return Array.from({ length: 7 }, (_, i) => new Date(2024, 0, 1 + i).toLocaleDateString(locale.intl, { weekday: 'short' }));
}

// ── Durations ──

/**
 * Format minutes as a human-readable hours string.
 * Uses "Xh Ym" format for mixed values, "Xh" or "Ym" when one component is 0.
 * Handles negative values with a "-" prefix.
 *
 * @example formatHours(0) → '0h'
 * @example formatHours(90) → '1h 30m'
 * @example formatHours(60) → '1h'
 * @example formatHours(30) → '30m'
 * @example formatHours(-90) → '-1h 30m'
 */
export function formatHours(minutes: number | undefined | null): string {
	if (minutes == null || Math.round(minutes) === 0) return '0h';
	const sign = minutes < 0 ? '-' : '';
	const abs = Math.round(Math.abs(minutes));
	const h = Math.floor(abs / 60);
	const m = abs % 60;
	if (m === 0) return `${sign}${h}h`;
	if (h === 0) return `${sign}${m}m`;
	return `${sign}${h}h ${m}m`;
}

/**
 * Format minutes as a signed delta string (for overtime display).
 * Positive → "+Xh Ym", negative → "-Xh Ym", zero → "±0h".
 *
 * @example formatDelta(0) → '±0h'
 * @example formatDelta(90) → '+1h 30m'
 * @example formatDelta(-90) → '-1h 30m'
 */
export function formatDelta(minutes: number): string {
	if (Math.round(minutes) === 0) return '±0h';
	const sign = minutes > 0 ? '+' : '';
	return sign + formatHours(minutes);
}

/**
 * Format minutes as a decimal hours string in the UI locale (e.g. "1,5h" / "1.5h").
 * Used where compact numeric display is preferred over h/m format.
 *
 * @example formatHoursDecimal(0) → '-'
 * @example formatHoursDecimal(90) → '1,5h' (de)
 * @example formatHoursDecimal(-90) → '-1,5h' (de)
 */
export function formatHoursDecimal(minutes: number | undefined | null): string {
	if (minutes == null || minutes === 0) return '-';
	return formatNumber(minutes / 60, { minimumFractionDigits: 1, maximumFractionDigits: 1 }) + 'h';
}

/**
 * Format a duration in minutes as "Xh Ym". Returns '-' for null/zero.
 *
 * @example formatDuration(null) → '-'
 * @example formatDuration(0) → '-'
 * @example formatDuration(62) → '1h 2m'
 * @example formatDuration(30) → '30m'
 */
export function formatDuration(minutes: number | undefined | null): string {
	if (minutes == null || minutes === 0) return '-';
	const total = Math.round(minutes);
	const h = Math.floor(total / 60);
	const m = total % 60;
	if (h > 0) return `${h}h ${m}m`;
	return `${m}m`;
}

/** Seconds as a running clock, "HH:MM:SS". */
export function formatClock(totalSeconds: number): string {
	const s = Math.max(0, Math.floor(totalSeconds));
	const pad = (n: number) => String(n).padStart(2, '0');
	return `${pad(Math.floor(s / 3600))}:${pad(Math.floor((s % 3600) / 60))}:${pad(s % 60)}`;
}

// ── Dates & times ──

/**
 * Format an ISO timestamp as local time (HH:MM, 24h in both locales).
 *
 * @example formatTime('2026-01-05T14:30:00Z') → '15:30' (Europe/Berlin)
 */
export function formatTime(iso: string): string {
	return new Date(iso).toLocaleTimeString(locale.intl, { hour: '2-digit', minute: '2-digit' });
}

/** A short date, e.g. "5. Jan." (de) / "5 Jan" (en). */
export function formatDateShort(date: Date | string): string {
	return formatDate(date, { month: 'short', day: 'numeric' });
}

/** A full date, e.g. "5. Jan. 2026" (de) / "5 Jan 2026" (en). */
export function formatDateFull(date: Date | string): string {
	return formatDate(date, { month: 'short', day: 'numeric', year: 'numeric' });
}

/** A week range, e.g. "5. Jan. – 11. Jan.". */
export function formatWeekLabel(range: { start: Date; end: Date }): string {
	return `${formatDateShort(range.start)} – ${formatDateShort(range.end)}`;
}

/** A relative time, e.g. "vor 5 Minuten" / "5 minutes ago". */
export function formatTimeAgo(dateStr: string | null | undefined): string {
	if (!dateStr) return '';
	const mins = Math.floor((Date.now() - new Date(dateStr).getTime()) / 60000);
	if (mins < 1) return t('format.timeAgo.justNow');
	const rtf = new Intl.RelativeTimeFormat(locale.intl, { numeric: 'always', style: 'short' });
	if (mins < 60) return rtf.format(-mins, 'minute');
	const hours = Math.floor(mins / 60);
	if (hours < 24) return rtf.format(-hours, 'hour');
	return rtf.format(-Math.floor(hours / 24), 'day');
}

/** The name of a month (current month by default), e.g. "Januar". */
export function getMonthName(date: Date = new Date()): string {
	return formatDate(date, { month: 'long' });
}

// ── Domain labels ──

const REQUEST_TYPES = ['JoinOrganization', 'EditPastEntry', 'EditPause', 'SetInitialOvertime', 'WorkScheduleChange', 'CsvImport', 'RequestAbsence', 'AddManualEntry'] as const;

function isRequestType(type: string | null | undefined): type is (typeof REQUEST_TYPES)[number] {
	return (REQUEST_TYPES as readonly string[]).includes(type ?? '');
}

/** Format a request type enum value as a short label. */
export function formatRequestType(type: string | null | undefined): string {
	return isRequestType(type) ? t(`format.requestType.${type}`) : (type ?? t('common.unknown'));
}

/** Format a request type enum value as a full descriptive label. */
export function formatRequestTypeFull(type: string | null | undefined): string {
	return isRequestType(type) ? t(`format.requestTypeFull.${type}`) : (type ?? t('common.unknown'));
}

/** Label for a request status (Accepted / Declined / Pending). */
export function formatRequestStatus(status: string | null | undefined): string {
	if (status === 'Accepted' || status === 'Declined' || status === 'Pending' || status === 'Withdrawn') return t(`format.requestStatus.${status}`);
	return status ?? t('common.unknown');
}

/** Normalise the absence type enum (name or numeric string) to its name. */
export function normalizeAbsenceType(type: string | null | undefined): 'SickDay' | 'Vacation' | 'Other' | null {
	if (type === 'SickDay' || type === '0') return 'SickDay';
	if (type === 'Vacation' || type === '1') return 'Vacation';
	if (type === 'Other' || type === 'OtherAbsence' || type === '2') return 'Other';
	return null;
}

/** Format an absence type as a human-readable label. */
export function absenceTypeLabel(type: string | null | undefined): string {
	const normalized = normalizeAbsenceType(type);
	return normalized ? t(`format.absenceType.${normalized}`) : (type ?? t('common.unknown'));
}

/** Name of a holiday preset region by its stable key ("DE-BY"); unknown keys fall back to the server's (German) name. */
export function holidayRegionLabel(key: string | null | undefined, fallback?: string | null): string {
	const k = `holidays.regions.${key}`;
	return key && typeof lookup(de, k) === 'string' ? t(k as MessageKey) : (fallback ?? key ?? '');
}

/** Label for an organization role (Owner / Admin / Member). */
export function roleLabel(role: string | null | undefined): string {
	if (role === 'Owner' || role === 'Admin' || role === 'Member') return t(`format.role.${role}`);
	return role ?? '';
}

/**
 * Get the status badge CSS class for a request status.
 */
export function statusBadgeClass(status: string | null | undefined): string {
	switch (status) {
		case 'Accepted': return 'badge-success';
		case 'Declined': return 'badge-error';
		case 'Pending': return 'badge-warning';
		default: return 'badge-ghost';
	}
}

/**
 * Parse request data JSON into display lines.
 */
export function parseRequestData(type: string | null | undefined, data: string | null | undefined): string[] {
	if (!data) return [];
	try {
		if (type === 'EditPastEntry' || type === 'AddManualEntry') {
			// camelCase from the frontend (edit requests), PascalCase from the backend (manual entries)
			const raw = JSON.parse(data) as Record<string, unknown>;
			const obj = Object.fromEntries(Object.entries(raw).map(([k, v]) => [k.charAt(0).toLowerCase() + k.slice(1), v]));
			const parts: string[] = [];
			if (obj.startTime) parts.push(t('format.requestData.start', { value: formatDateTime(String(obj.startTime)) }));
			if (obj.endTime) parts.push(t('format.requestData.end', { value: formatDateTime(String(obj.endTime)) }));
			if (obj.pauseDurationMinutes != null) parts.push(t('format.requestData.pause', { value: String(obj.pauseDurationMinutes) }));
			if (obj.description != null) parts.push(t('format.requestData.note', { value: String(obj.description) }));
			return parts;
		} else if (type === 'EditPause') {
			return [t('format.requestData.pause', { value: data })];
		} else if (type === 'SetInitialOvertime') {
			return [t('format.requestData.overtime', { value: formatNumber(Number(data)) })];
		} else if (type === 'RequestAbsence') {
			const obj = JSON.parse(data);
			const parts: string[] = [];
			const dates: string[] = obj.dates ?? [];
			if (dates.length === 1) parts.push(t('format.requestData.date', { value: formatDate(dates[0]) }));
			else if (dates.length > 1) parts.push(t('format.requestData.range', { count: dates.length, from: formatDate(dates[0]), to: formatDate(dates[dates.length - 1]) }));
			const typeLabel = obj.customTypeId ? t('format.requestData.customType') : absenceTypeLabel(String(obj.type ?? 1));
			parts.push(t('format.requestData.type', { value: typeLabel + (obj.isHalfDay ? t('format.requestData.halfDaySuffix') : '') }));
			if (obj.note) parts.push(t('format.requestData.note', { value: obj.note }));
			return parts;
		}
	} catch { /* not JSON — show raw below */ }
	return [data];
}

/**
 * Get initials from a first and last name.
 */
export function getInitials(first: string | null | undefined, last: string | null | undefined): string {
	return ((first?.[0] ?? '') + (last?.[0] ?? '')).toUpperCase() || '?';
}

/**
 * Calculate bar width percentage for a worked/target display.
 */
export function barWidth(worked: number, target: number): number {
	if (target <= 0) return worked > 0 ? 100 : 0;
	return Math.min(100, (worked / target) * 100);
}
