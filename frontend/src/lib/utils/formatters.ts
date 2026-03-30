/**
 * Canonical formatting functions used across all pages.
 * Each function exists here ONLY — no local copies in page files.
 */

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
	if (minutes == null || minutes === 0) return '0h';
	const sign = minutes < 0 ? '-' : '';
	const abs = Math.abs(minutes);
	const h = Math.floor(abs / 60);
	const m = Math.round(abs % 60);
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
	if (minutes === 0) return '±0h';
	const sign = minutes > 0 ? '+' : '';
	return sign + formatHours(minutes);
}

/**
 * Format minutes as a decimal hours string (e.g., "1.5h").
 * Used where compact numeric display is preferred over h/m format.
 *
 * @example formatHoursDecimal(0) → '-'
 * @example formatHoursDecimal(90) → '1.5h'
 * @example formatHoursDecimal(-90) → '-1.5h'
 */
export function formatHoursDecimal(minutes: number | undefined | null): string {
	if (minutes == null || minutes === 0) return '-';
	return (minutes / 60).toFixed(1) + 'h';
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
	const h = Math.floor(minutes / 60);
	const m = Math.round(minutes % 60);
	if (h > 0) return `${h}h ${m}m`;
	return `${m}m`;
}

/**
 * Format an ISO timestamp as local time (HH:MM).
 *
 * @example formatTime('2026-01-05T14:30:00Z') → '14:30' (locale-dependent)
 */
export function formatTime(iso: string): string {
	return new Date(iso).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
}

/**
 * Format a Date object as a short date string (e.g., "Jan 5").
 */
export function formatDateShort(date: Date | string): string {
	const d = typeof date === 'string' ? new Date(date) : date;
	return d.toLocaleDateString([], { month: 'short', day: 'numeric' });
}

/**
 * Format a date as a full date string (e.g., "Jan 5, 2026").
 */
export function formatDateFull(date: Date | string): string {
	const d = typeof date === 'string' ? new Date(date) : date;
	return d.toLocaleDateString([], { month: 'short', day: 'numeric', year: 'numeric' });
}

/**
 * Format a week range as "Mon D – Mon D".
 *
 * @example formatWeekLabel({ start: new Date(2026,0,5), end: new Date(2026,0,11) }) → 'Jan 5 – Jan 11'
 */
export function formatWeekLabel(range: { start: Date; end: Date }): string {
	const opts: Intl.DateTimeFormatOptions = { month: 'short', day: 'numeric' };
	return `${range.start.toLocaleDateString([], opts)} – ${range.end.toLocaleDateString([], opts)}`;
}

/**
 * Format a relative time string (e.g., "5m ago", "2h ago").
 */
export function formatTimeAgo(dateStr: string | null | undefined): string {
	if (!dateStr) return '';
	const diff = Date.now() - new Date(dateStr).getTime();
	const mins = Math.floor(diff / 60000);
	if (mins < 1) return 'just now';
	if (mins < 60) return `${mins}m ago`;
	const hours = Math.floor(mins / 60);
	if (hours < 24) return `${hours}h ago`;
	const days = Math.floor(hours / 24);
	return `${days}d ago`;
}

/**
 * Format a request type enum value as a short label.
 */
export function formatRequestType(type: string | null | undefined): string {
	switch (type) {
		case 'JoinOrganization': return 'Join';
		case 'EditPastEntry': return 'Edit Entry';
		case 'EditPause': return 'Edit Pause';
		case 'SetInitialOvertime': return 'Set Overtime';
		case 'WorkScheduleChange': return 'Schedule';
		case 'CsvImport': return 'CSV Import';
		default: return type ?? 'Unknown';
	}
}

/**
 * Format a request type enum value as a full descriptive label.
 */
export function formatRequestTypeFull(type: string | null | undefined): string {
	switch (type) {
		case 'JoinOrganization': return 'Join Organization';
		case 'EditPastEntry': return 'Edit Past Time Entry';
		case 'EditPause': return 'Edit Pause Duration';
		case 'SetInitialOvertime': return 'Set Initial Overtime';
		case 'WorkScheduleChange': return 'Change Work Schedule';
		case 'CsvImport': return 'Import CSV Time Entries';
		default: return type ?? 'Unknown';
	}
}

/**
 * Format an absence type as a human-readable label.
 */
export function absenceTypeLabel(type: string | null | undefined): string {
	if (type === 'SickDay' || type === '0') return 'Sick Day';
	if (type === 'Vacation' || type === '1') return 'Vacation';
	if (type === 'Other' || type === 'OtherAbsence' || type === '2') return 'Other';
	return type ?? 'Unknown';
}

/**
 * Get the CSS badge class for an absence type.
 */
export function absenceTypeBadge(type: string | null | undefined): string {
	if (type === 'SickDay' || type === '0') return 'badge-sick';
	if (type === 'Vacation' || type === '1') return 'badge-vacation';
	return 'badge-other';
}

/**
 * Get the status badge CSS class for a request status.
 */
export function statusBadgeClass(status: string | null | undefined): string {
	switch (status) {
		case 'Accepted': return 'status-accepted';
		case 'Declined': return 'status-declined';
		case 'Pending': return 'status-pending';
		default: return '';
	}
}

/**
 * Parse request data JSON into display parts.
 * Returns an array of strings for layout-level display, or a single joined string for org page.
 */
export function parseRequestData(type: string | null | undefined, data: string | null | undefined): string[] {
	if (!data) return [];
	try {
		if (type === 'EditPastEntry') {
			const obj = JSON.parse(data);
			const parts: string[] = [];
			if (obj.startTime) parts.push(`Start: ${new Date(obj.startTime).toLocaleString()}`);
			if (obj.endTime) parts.push(`End: ${new Date(obj.endTime).toLocaleString()}`);
			if (obj.description !== undefined) parts.push(`Note: ${obj.description}`);
			return parts;
		} else if (type === 'EditPause') {
			return [`Pause: ${data} min`];
		} else if (type === 'SetInitialOvertime') {
			return [`Overtime: ${data}h`];
		}
	} catch { /* fallback */ }
	return data ? [data] : [];
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

/**
 * Get the name of the current month.
 */
export function getMonthName(): string {
	return new Date().toLocaleDateString('en-US', { month: 'long' });
}
