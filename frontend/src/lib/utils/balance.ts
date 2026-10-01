import type { BalanceResponse, PeriodBalance } from '$lib/api';
import { parseDateKey } from './dateHelpers';

/** A period of the balance endpoint in minutes (the unit every formatter here takes). */
export interface PeriodMinutes {
	worked: number;
	target: number;
	/** worked − target, as computed by the backend (holidays/absences credited, failure entries excluded). */
	delta: number;
}

export interface BalanceMinutes {
	/** Running overtime since the first tracked entry, including the initial overtime. */
	cumulative: number;
	initial: number;
	today: PeriodMinutes;
	week: PeriodMinutes;
	month: PeriodMinutes;
	/** Mon–Sun of the current week; `counted` is false for days still to come. */
	weekDays: (PeriodMinutes & { date: Date; counted: boolean })[];
	/** The current month in Monday-based weeks, clipped to the month. */
	monthWeeks: (PeriodMinutes & { from: Date; to: Date })[];
	failureEntryCount: number;
}

const minutes = (hours: number | undefined) => Math.round((hours ?? 0) * 60);

function period(p: PeriodBalance | undefined): PeriodMinutes {
	return { worked: minutes(p?.workedHours), target: minutes(p?.targetHours), delta: minutes(p?.overtimeHours) };
}

/** Maps GET …/members/{id}/balance (hours) to display minutes. */
export function balanceToMinutes(b: BalanceResponse): BalanceMinutes {
	return {
		cumulative: minutes(b.cumulativeOvertimeHours),
		initial: minutes(b.initialOvertimeHours),
		today: period(b.today),
		week: period(b.week),
		month: period(b.month),
		weekDays: (b.weekDays ?? []).map((d) => ({ ...period(d), date: parseDateKey(d.date!), counted: d.isCounted ?? false })),
		monthWeeks: (b.monthWeeks ?? []).map((w) => ({ ...period(w), from: parseDateKey(w.from!), to: parseDateKey(w.to!) })),
		failureEntryCount: b.failureEntryCount ?? 0
	};
}
