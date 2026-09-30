/**
 * Canonical schedule helper functions for work schedule target and absence credit calculations.
 *
 * IMPORTANT: This is the single source of truth for getDayTarget() and getAbsenceCredit().
 * Previously there were 3 different implementations with subtle bugs.
 * The canonical version here supports schedule periods (history page behavior)
 * and falls back to the base schedule (dashboard/time page behavior).
 */

import { dateKey } from './dateHelpers';
import { t } from '$lib/i18n';

/** Minimal schedule shape needed by helpers (matches WorkScheduleResponse fields). */
export interface ScheduleLike {
	targetMon?: number | null;
	targetTue?: number | null;
	targetWed?: number | null;
	targetThu?: number | null;
	targetFri?: number | null;
	weeklyWorkHours?: number | null;
	initialOvertimeHours?: number | null;
	initialOvertimeMode?: string | null;
}

/** Minimal schedule period shape (matches WorkScheduleResponse with date range). */
export interface SchedulePeriodLike extends ScheduleLike {
	validFrom?: string | null;
	validTo?: string | null;
}

/**
 * Get the target minutes for a specific day, considering holidays and schedule periods.
 *
 * Priority order:
 * 1. Full-day holidays → 0 (no target)
 * 2. Half-day holidays → 50% of normal target
 * 3. Matching schedule period (by date range) → use that period's targets
 * 4. Base schedule → use its targets
 * 5. No schedule → 0
 *
 * Target values are stored in hours in the DB/API; this function returns MINUTES.
 */
export function getDayTarget(
	date: Date,
	schedule: ScheduleLike | null | undefined,
	holidayDates: Map<string, string> | Set<string>,
	periods: SchedulePeriodLike[] = [],
	halfDayHolidays?: Set<string>
): number {
	if (!schedule && periods.length === 0) return 0;
	const key = dateKey(date);

	// Holidays reduce target
	if (holidayDates.has(key)) {
		if (halfDayHolidays?.has(key)) {
			// Half-day holiday: return 50% of normal target
			return getFullDayTarget(date, schedule, periods) * 0.5;
		}
		return 0;
	}

	// Weekend → 0
	const dow = date.getDay();
	if (dow === 0 || dow === 6) return 0;

	// Check schedule periods first (most specific wins)
	for (const p of periods) {
		if (p.validFrom && p.validFrom <= key && (!p.validTo || p.validTo >= key)) {
			return getTargetForDow(dow, p);
		}
	}

	// Fall back to base schedule
	if (!schedule) return 0;
	return getTargetForDow(dow, schedule);
}

/**
 * Get the full (unreduced) day target, ignoring holidays. Used internally for half-day calculations.
 */
function getFullDayTarget(date: Date, schedule: ScheduleLike | null | undefined, periods: SchedulePeriodLike[]): number {
	const dow = date.getDay();
	if (dow === 0 || dow === 6) return 0;
	const key = dateKey(date);
	for (const p of periods) {
		if (p.validFrom && p.validFrom <= key && (!p.validTo || p.validTo >= key)) {
			return getTargetForDow(dow, p);
		}
	}
	if (!schedule) return 0;
	return getTargetForDow(dow, schedule);
}

/**
 * Look up the target hours for a day-of-week from a schedule, and convert to minutes.
 */
function getTargetForDow(dow: number, schedule: ScheduleLike): number {
	const targets: Record<number, number> = {
		1: (schedule.targetMon ?? 0) * 60,
		2: (schedule.targetTue ?? 0) * 60,
		3: (schedule.targetWed ?? 0) * 60,
		4: (schedule.targetThu ?? 0) * 60,
		5: (schedule.targetFri ?? 0) * 60,
	};
	return targets[dow] ?? 0;
}

/** Custom-absence-type credit behaviour per date (from org-defined types). */
export interface CustomCreditSets {
	/** Dates whose custom type credits nothing (keep hours as is). */
	noCredit: Set<string>;
	/** Dates whose custom type tops hours up to the day target (e.g. Business Trip). */
	topUp: Set<string>;
}

/**
 * Get absence credit for a specific day (minutes credited for sick/vacation/other absence days).
 * Returns the full day target if the day has a full-day absence; 50% for half-day absences; 0 otherwise.
 *
 * Custom absence types can change this via `customCredits`:
 * - noCredit dates → 0 (informational marker only)
 * - topUp dates → max(0, target - workedMinutes), so the day nets exactly the target
 */
export function getAbsenceCredit(
	date: Date,
	schedule: ScheduleLike | null | undefined,
	holidayDates: Map<string, string> | Set<string>,
	sickDayDates: Set<string>,
	vacationDates: Set<string>,
	otherAbsenceDates: Set<string>,
	periods: SchedulePeriodLike[] = [],
	halfDayAbsences?: Set<string>,
	halfDayHolidays?: Set<string>,
	customCredits?: CustomCreditSets,
	workedMinutes: number = 0
): number {
	const key = dateKey(date);
	if (sickDayDates.has(key) || vacationDates.has(key) || otherAbsenceDates.has(key)) {
		if (customCredits?.noCredit.has(key)) return 0;
		if (customCredits?.topUp.has(key)) {
			const target = getDayTarget(date, schedule, holidayDates, periods, halfDayHolidays);
			return Math.max(0, target - workedMinutes);
		}
		if (halfDayAbsences?.has(key)) {
			// Use full (unreduced) target so half-day absences on half-holidays
			// give the correct credit (e.g. 4h on an 8h day, not 2h).
			return getFullDayTarget(date, schedule, periods) * 0.5;
		}
		return getDayTarget(date, schedule, holidayDates, periods, halfDayHolidays);
	}
	return 0;
}

/**
 * Sum absence credits over a date range.
 */
export function absenceCreditsForRange(
	from: Date,
	to: Date,
	schedule: ScheduleLike | null | undefined,
	holidayDates: Map<string, string> | Set<string>,
	sickDayDates: Set<string>,
	vacationDates: Set<string>,
	otherAbsenceDates: Set<string>,
	periods: SchedulePeriodLike[] = [],
	halfDayAbsences?: Set<string>,
	halfDayHolidays?: Set<string>
): number {
	let credits = 0;
	const cursor = new Date(from);
	cursor.setHours(0, 0, 0, 0);
	const end = new Date(to);
	end.setHours(23, 59, 59, 999);
	while (cursor <= end) {
		credits += getAbsenceCredit(cursor, schedule, holidayDates, sickDayDates, vacationDates, otherAbsenceDates, periods, halfDayAbsences, halfDayHolidays);
		cursor.setDate(cursor.getDate() + 1);
	}
	return credits;
}

/**
 * Get the day type for a date based on holidays and absence sets.
 */
export function getDayType(
	date: Date | string,
	holidayDates: Map<string, string> | Set<string>,
	sickDayDates: Set<string>,
	vacationDates: Set<string>,
	otherAbsenceDates: Set<string>,
	halfDayHolidays?: Set<string>
): 'holiday' | 'sick' | 'vacation' | 'other-absence' | null {
	const key = typeof date === 'string' ? date : dateKey(date);
	if (holidayDates.has(key)) {
		// On half-day holidays, prioritize the absence type so it shows in stats/history
		if (halfDayHolidays?.has(key)) {
			if (sickDayDates.has(key)) return 'sick';
			if (vacationDates.has(key)) return 'vacation';
			if (otherAbsenceDates.has(key)) return 'other-absence';
		}
		return 'holiday';
	}
	if (sickDayDates.has(key)) return 'sick';
	if (vacationDates.has(key)) return 'vacation';
	if (otherAbsenceDates.has(key)) return 'other-absence';
	return null;
}

/**
 * Get the label for a day type.
 */
export function getDayTypeLabel(
	date: Date | string,
	holidayDates: Map<string, string>,
	sickDayDates: Set<string>,
	vacationDates: Set<string>,
	otherAbsenceDates: Set<string>,
	halfDayHolidays?: Set<string>,
	halfDayAbsences?: Set<string>,
	customTypeDates?: Map<string, { name: string }>
): string {
	const key = typeof date === 'string' ? date : dateKey(date);
	const otherLabel = customTypeDates?.get(key)?.name ?? t('format.dayType.otherAbsence');
	const halfDay = ' ' + t('format.dayType.halfDay');
	if (holidayDates.has(key)) {
		const name = holidayDates.get(key) || t('format.dayType.holiday');
		if (halfDayHolidays?.has(key)) {
			// Show combined label when half-holiday + absence
			const halfSuffix = halfDayAbsences?.has(key) ? halfDay : '';
			let absLabel = '';
			if (sickDayDates.has(key)) absLabel = t('format.dayType.sick') + halfSuffix;
			else if (vacationDates.has(key)) absLabel = t('format.dayType.vacation') + halfSuffix;
			else if (otherAbsenceDates.has(key)) absLabel = otherLabel + halfSuffix;
			return absLabel ? `${name}${halfDay} / ${absLabel}` : `${name}${halfDay}`;
		}
		return name;
	}
	const halfSuffix = halfDayAbsences?.has(key) ? halfDay : '';
	if (sickDayDates.has(key)) return t('format.dayType.sick') + halfSuffix;
	if (vacationDates.has(key)) return t('format.dayType.vacation') + halfSuffix;
	if (otherAbsenceDates.has(key)) return otherLabel + halfSuffix;
	return '';
}

/**
 * Sum the target minutes over a date range, optionally clipping to the first entry date.
 * Used by the dashboard for computing week/month targets since first tracked entry.
 */
export function getTargetForRange(
	rangeStart: Date,
	rangeEnd: Date,
	schedule: ScheduleLike | null | undefined,
	holidayDates: Map<string, string> | Set<string>,
	periods: SchedulePeriodLike[],
	firstEntryDate: Date | null = null,
	halfDayHolidays?: Set<string>
): number {
	if (!schedule && periods.length === 0) return 0;
	const effectiveStart = firstEntryDate && firstEntryDate > rangeStart ? firstEntryDate : rangeStart;
	if (effectiveStart > rangeEnd) return 0;

	let total = 0;
	const cursor = new Date(effectiveStart);
	cursor.setHours(0, 0, 0, 0);
	const end = new Date(rangeEnd);
	end.setHours(23, 59, 59, 999);
	while (cursor <= end) {
		total += getDayTarget(cursor, schedule, holidayDates, periods, halfDayHolidays);
		cursor.setDate(cursor.getDate() + 1);
	}
	return total;
}

