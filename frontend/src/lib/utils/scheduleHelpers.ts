/**
 * Canonical schedule helper functions for work schedule target and absence credit calculations.
 *
 * IMPORTANT: This is the single source of truth for getDayTarget() and getAbsenceCredit().
 * Previously there were 3 different implementations with subtle bugs.
 * The canonical version here supports schedule periods (history page behavior)
 * and falls back to the base schedule (dashboard/time page behavior).
 */

import { dateKey } from './dateHelpers';

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
 * 1. Holidays → 0 (no target)
 * 2. Matching schedule period (by date range) → use that period's targets
 * 3. Base schedule → use its targets
 * 4. No schedule → 0
 *
 * Target values are stored in hours in the DB/API; this function returns MINUTES.
 */
export function getDayTarget(
	date: Date,
	schedule: ScheduleLike | null | undefined,
	holidayDates: Map<string, string> | Set<string>,
	periods: SchedulePeriodLike[] = []
): number {
	if (!schedule && periods.length === 0) return 0;
	const key = dateKey(date);

	// Holidays reduce target to 0
	if (holidayDates.has(key)) return 0;

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

/**
 * Get absence credit for a specific day (minutes credited for sick/vacation/other absence days).
 * Returns the full day target if the day has an absence; 0 otherwise.
 */
export function getAbsenceCredit(
	date: Date,
	schedule: ScheduleLike | null | undefined,
	holidayDates: Map<string, string> | Set<string>,
	sickDayDates: Set<string>,
	vacationDates: Set<string>,
	otherAbsenceDates: Set<string>,
	periods: SchedulePeriodLike[] = []
): number {
	const key = dateKey(date);
	if (sickDayDates.has(key) || vacationDates.has(key) || otherAbsenceDates.has(key)) {
		return getDayTarget(date, schedule, holidayDates, periods);
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
	periods: SchedulePeriodLike[] = []
): number {
	let credits = 0;
	const cursor = new Date(from);
	cursor.setHours(0, 0, 0, 0);
	const end = new Date(to);
	end.setHours(23, 59, 59, 999);
	while (cursor <= end) {
		credits += getAbsenceCredit(cursor, schedule, holidayDates, sickDayDates, vacationDates, otherAbsenceDates, periods);
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
	otherAbsenceDates: Set<string>
): 'holiday' | 'sick' | 'vacation' | 'other-absence' | null {
	const key = typeof date === 'string' ? date : dateKey(date);
	if (holidayDates.has(key)) return 'holiday';
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
	otherAbsenceDates: Set<string>
): string {
	const key = typeof date === 'string' ? date : dateKey(date);
	if (holidayDates.has(key)) return holidayDates.get(key) ?? 'Holiday';
	if (sickDayDates.has(key)) return 'Sick Day';
	if (vacationDates.has(key)) return 'Vacation';
	if (otherAbsenceDates.has(key)) return 'Other Absence';
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
	firstEntryDate: Date | null = null
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
		total += getDayTarget(cursor, schedule, holidayDates, periods);
		cursor.setDate(cursor.getDate() + 1);
	}
	return total;
}

/**
 * Get the heat map color for a calendar cell based on worked/target ratio.
 */
export function getHeatColor(minutes: number, target: number): string {
	if (minutes === 0) return 'transparent';
	if (target === 0) return 'rgba(59, 130, 246, 0.3)';
	const ratio = minutes / target;
	if (ratio >= 1) return 'rgba(34, 197, 94, 0.5)';
	if (ratio >= 0.75) return 'rgba(34, 197, 94, 0.3)';
	if (ratio >= 0.5) return 'rgba(250, 204, 21, 0.3)';
	return 'rgba(239, 68, 68, 0.2)';
}
