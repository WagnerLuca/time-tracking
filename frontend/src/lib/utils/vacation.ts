/**
 * Vacation balance helpers (backend `Services/Common/VacationBalances.cs` computes the figures; nothing is
 * recomputed here): the carry-over lines under a remaining-days figure and the org's carry-over settings.
 */
import { t, tp } from '$lib/i18n';
import { formatDate, formatNumber } from './formatters';
import { orgDateKey } from './closedPeriods';

/** The current year in the organization's time zone (the backend's `OrgClock`). */
export function orgYear(timeZone?: string | null, now: Date = new Date()): number {
	return Number(orgDateKey(now, timeZone).slice(0, 4));
}

/** Clamps a day to the month's length (29 February allowed), so changing the month never makes the date invalid. */
export function clampExpiryDay(month: number, day: number): number {
	return Math.max(1, Math.min(day, new Date(2024, month, 0).getDate()));
}

/** The carry-over part of a balance, as the org detail member rows / vacation endpoint / accounting rows carry it. */
export interface CarryFigures {
	carriedIn?: number;
	carriedUsed?: number;
	expired?: number;
	expiresOn?: string | null;
	adjustment?: number;
	adjustmentNote?: string | null;
}

/** Carried-over days still open (not used, not lapsed). */
export function openCarry(b: CarryFigures): number {
	return Math.max(0, (b.carriedIn ?? 0) - (b.carriedUsed ?? 0) - (b.expired ?? 0));
}

/**
 * The lines under the remaining days of `year`: "davon 3 Tage aus 2025, verfallen am 31.03.2026" while carried days
 * are open, "2 Tage verfallen" once some lapsed. Empty without carry-over.
 */
export function carryOverLines(b: CarryFigures, year: number): string[] {
	const lines: string[] = [];
	const open = openCarry(b);
	if (open > 0) {
		const params = { days: formatNumber(open), year: year - 1 };
		lines.push(b.expiresOn
			? tp('vacation.carry.fromLastYearUntil', open, { ...params, date: formatDate(b.expiresOn) })
			: tp('vacation.carry.fromLastYear', open, params));
	}
	const expired = b.expired ?? 0;
	if (expired > 0) lines.push(tp('vacation.carry.expired', expired, { days: formatNumber(expired) }));
	return lines;
}

/** "+2" / "−1,5" days. */
export function signedDays(days: number): string {
	return days > 0 ? `+${formatNumber(days)}` : days < 0 ? `−${formatNumber(-days)}` : formatNumber(0);
}

/** "Korrektur +5 Tage (Altsystem)", or null without an adjustment. */
export function adjustmentLine(b: CarryFigures): string | null {
	const days = b.adjustment ?? 0;
	if (!days) return null;
	// The sign is part of {days}; the plural follows the amount ("−1 Tag", "+2 Tage")
	return b.adjustmentNote
		? tp('vacation.carry.adjustmentWithNote', Math.abs(days), { days: signedDays(days), note: b.adjustmentNote })
		: tp('vacation.carry.adjustment', Math.abs(days), { days: signedDays(days) });
}

/** Every line describing where a year's figure comes from (carry-over, lapsed, adjustment). */
export function vacationDetailLines(b: CarryFigures, year: number): string[] {
	const adjustment = adjustmentLine(b);
	return adjustment ? [...carryOverLines(b, year), adjustment] : carryOverLines(b, year);
}

// ── Organization settings ──

export type CarryOverModeName = 'None' | 'UntilDate' | 'Unlimited';
export const CARRY_OVER_MODES: CarryOverModeName[] = ['UntilDate', 'Unlimited', 'None'];

/** The settings request's numeric enum value of a mode name (org detail returns names). */
export function carryOverModeValue(mode: string | null | undefined): 0 | 1 | 2 {
	return mode === 'None' ? 0 : mode === 'Unlimited' ? 2 : 1;
}

/** "MM-DD" → month/day (default 31 March). */
export function parseExpiry(value: string | null | undefined): { month: number; day: number } {
	const m = /^(\d{2})-(\d{2})$/.exec(value ?? '');
	return m ? { month: Number(m[1]), day: Number(m[2]) } : { month: 3, day: 31 };
}

/** A day the month can have (29 February included; the backend clamps it in short years). */
export function isValidExpiry(month: number, day: number): boolean {
	if (!Number.isInteger(month) || !Number.isInteger(day) || month < 1 || month > 12 || day < 1) return false;
	return day <= new Date(2024, month, 0).getDate();
}

/** month/day → "MM-DD". */
export function expiryValue(month: number, day: number): string {
	return `${String(month).padStart(2, '0')}-${String(day).padStart(2, '0')}`;
}

/** "31.03." (de) / "31/03" (en) for a "MM-DD" expiry. */
export function formatExpiry(value: string | null | undefined): string {
	const { month, day } = parseExpiry(value);
	return formatDate(new Date(2024, month - 1, day), { day: '2-digit', month: '2-digit' });
}

/** The org's carry-over rule in one line, e.g. "Übertragen bis Stichtag (31.03.), höchstens 10 Tage, Übertrag ab 2026". */
export function carryOverRuleText(org: {
	vacationCarryOverMode?: string | null;
	vacationCarryOverExpiry?: string | null;
	vacationCarryOverMaxDays?: number | null;
	vacationCarryOverSince?: number;
}): string {
	const mode = (CARRY_OVER_MODES as string[]).includes(org.vacationCarryOverMode ?? '') ? (org.vacationCarryOverMode as CarryOverModeName) : 'UntilDate';
	const label = t(`orgSettings.carryOver.modes.${mode}`);
	if (mode === 'None') return label;
	let rule = mode === 'UntilDate' ? `${label} (${formatExpiry(org.vacationCarryOverExpiry)})` : label;
	if (org.vacationCarryOverMaxDays != null) rule += `, ${t('orgSettings.carryOver.maxSummary', { days: formatNumber(org.vacationCarryOverMaxDays) })}`;
	return t('orgSettings.carryOver.summary', { rule, year: org.vacationCarryOverSince ?? orgYear() });
}

/** The vacation fields of an org detail member row. */
export interface MemberVacationRow {
	vacationDaysPerYear?: number;
	vacationAvailable?: number;
	vacationAdjustment?: number;
	vacationCarriedIn?: number;
	vacationCarriedUsed?: number;
	vacationExpired?: number;
	vacationExpiresOn?: string | null;
}

/** What a member's year offers (entitlement + adjustment + carried in − lapsed); the allowance for older responses. */
export function vacationTotal(m: MemberVacationRow): number {
	return m.vacationAvailable ?? m.vacationDaysPerYear ?? 0;
}

/** A member row's carry-over figures, for {@link vacationDetailLines}. */
export function memberCarryFigures(m: MemberVacationRow): CarryFigures {
	return {
		carriedIn: m.vacationCarriedIn,
		carriedUsed: m.vacationCarriedUsed,
		expired: m.vacationExpired,
		expiresOn: m.vacationExpiresOn,
		adjustment: m.vacationAdjustment
	};
}
