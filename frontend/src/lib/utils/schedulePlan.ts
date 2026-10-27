/**
 * The work schedule model (issue #82): the current schedule, at most one future one ("Ab 01.12.2026") and a read-only
 * history. These helpers back `WorkSchedulePlan.svelte`: the form's defaults and checks and the payload it sends — the
 * backend decides (closing the period before, replacing the future one, closed months).
 */

export const MAX_WEEKLY = 80;
export const MAX_DAILY = 24;

/** The fields a schedule carries (matches WorkScheduleResponse). */
export interface ScheduleValues {
	weeklyWorkHours?: number | null;
	targetMon?: number;
	targetTue?: number;
	targetWed?: number;
	targetThu?: number;
	targetFri?: number;
	targetSat?: number;
	targetSun?: number;
}

/** The form: weekly hours, "gleichmäßig verteilen (Mo–Fr)" and Mon … Sun. */
export interface ScheduleForm {
	from: string;
	weekly: number | null;
	distribute: boolean;
	days: (number | null)[];
}

/** Mon … Sun of a schedule. */
export function weekTargets(s: ScheduleValues | null | undefined): number[] {
	return s ? [s.targetMon, s.targetTue, s.targetWed, s.targetThu, s.targetFri, s.targetSat, s.targetSun].map((v) => v ?? 0) : [0, 0, 0, 0, 0, 0, 0];
}

/** The sum of Mon … Sun. */
export function weekTotal(s: ScheduleValues | null | undefined): number {
	return Math.round(weekTargets(s).reduce((a, b) => a + b, 0) * 100) / 100;
}

/**
 * The form starts from the given schedule's values (the current one): evenly distributed when Mon–Fri are equal and the
 * weekend is free, else per day.
 */
export function formFrom(s: ScheduleValues | null | undefined, from: string): ScheduleForm {
	const days = weekTargets(s);
	const even = days.slice(0, 5).every((d) => d === days[0]) && days[5] === 0 && days[6] === 0;
	const weekly = s?.weeklyWorkHours ?? (s ? weekTotal(s) : null);
	return { from, weekly: weekly || null, distribute: !s || even, days };
}

const outOfRange = (v: number | null | undefined, max: number) => v != null && (!Number.isFinite(v) || v < 0 || v > max);

/** The form's problems as i18n keys (empty: valid). `earliest`: the first allowed start day (members: today). */
export function formErrors(f: ScheduleForm, earliest?: string | null): { from?: string; weekly?: string; days?: string } {
	const errors: { from?: string; weekly?: string; days?: string } = {};
	if (!f.from) errors.from = 'schedule.plan.fromRequired';
	else if (earliest && f.from < earliest) errors.from = 'schedule.plan.fromBeforeToday';
	if (f.distribute && outOfRange(f.weekly, MAX_WEEKLY)) errors.weekly = 'schedule.plan.weeklyRange';
	if (!f.distribute && f.days.some((d) => outOfRange(d, MAX_DAILY))) errors.days = 'schedule.plan.dailyRange';
	if (f.distribute && !f.weekly && !errors.weekly) errors.weekly = 'schedule.plan.weeklyRequired';
	return errors;
}

/** The targets of a change/correction/request (per day: the weekly total is their sum). */
export function targetsPayload(f: ScheduleForm) {
	const [mon, tue, wed, thu, fri, sat, sun] = f.days.map((d) => d ?? 0);
	return f.distribute
		? { weeklyWorkHours: f.weekly ?? undefined, distributeEvenly: true }
		: {
				weeklyWorkHours: Math.round((mon + tue + wed + thu + fri + sat + sun) * 100) / 100,
				distributeEvenly: false,
				targetMon: mon, targetTue: tue, targetWed: wed, targetThu: thu, targetFri: fri, targetSat: sat, targetSun: sun
			};
}
