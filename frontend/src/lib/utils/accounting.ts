import { t } from '$lib/i18n';

/** Helpers of the accounting panel ("Auswertungen", docs/ACCOUNTING_ROLE_PLAN.md). Figures come from the API — these only sum and label. */

type SummaryRow = {
	targetHours?: number;
	workedHours?: number;
	overtimeHours?: number;
	vacationDays?: number;
	sickDays?: number;
	otherAbsenceDays?: number;
	failureEntryCount?: number;
};

/** Column sums of the monthly summary (hours rounded to 2 decimals like the API). */
export function summaryTotals(rows: readonly SummaryRow[]) {
	const sum = (pick: (r: SummaryRow) => number | undefined) => Math.round(rows.reduce((acc, r) => acc + (pick(r) ?? 0), 0) * 100) / 100;
	return {
		targetHours: sum((r) => r.targetHours),
		workedHours: sum((r) => r.workedHours),
		overtimeHours: sum((r) => r.overtimeHours),
		vacationDays: sum((r) => r.vacationDays),
		sickDays: sum((r) => r.sickDays),
		otherAbsenceDays: sum((r) => r.otherAbsenceDays),
		failureEntryCount: sum((r) => r.failureEntryCount)
	};
}

type MonthStatus = { failureEntryCount?: number; pendingRequestCount?: number };

/** What still needs attention before closing a month, as translated phrases (empty = ready). */
export function closeWarnings(status: MonthStatus | null | undefined): string[] {
	const out: string[] = [];
	if (status?.failureEntryCount) out.push(t('accounting.closing.failures', { count: status.failureEntryCount }));
	if (status?.pendingRequestCount) out.push(t('accounting.closing.pending', { count: status.pendingRequestCount }));
	return out;
}

/** The month before the given date's month, as { year, month } (the usual month to report on and close). */
export function previousMonth(now: Date = new Date()): { year: number; month: number } {
	const m = now.getMonth(); // 0-based → previous month 1-based
	return m === 0 ? { year: now.getFullYear() - 1, month: 12 } : { year: now.getFullYear(), month: m };
}

export const ACCOUNTING_TABS = ['reports', 'closing', 'vacation'] as const;
export type AccountingTab = (typeof ACCOUNTING_TABS)[number];

/** The tab named in the URL (`?tab=`), defaulting to the reports. */
export function tabFromQuery(value: string | null | undefined): AccountingTab {
	return (ACCOUNTING_TABS as readonly string[]).includes(value ?? '') ? (value as AccountingTab) : 'reports';
}

/** A year typed into the year field the API accepts. */
export function validYear(year: number): boolean {
	return Number.isInteger(year) && year >= 2000 && year <= 2100;
}
