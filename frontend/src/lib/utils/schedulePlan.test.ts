import { describe, expect, it } from 'vitest';
import { formErrors, formFrom, targetsPayload, weekTargets, weekTotal } from './schedulePlan';

describe('work schedule plan', () => {
	const fullTime = { weeklyWorkHours: 40, targetMon: 8, targetTue: 8, targetWed: 8, targetThu: 8, targetFri: 8, targetSat: 0, targetSun: 0 };
	const uneven = { weeklyWorkHours: 30, targetMon: 8, targetTue: 8, targetWed: 8, targetThu: 6, targetFri: 0, targetSat: 0, targetSun: 0 };

	it('reads a week and its total', () => {
		expect(weekTargets(fullTime)).toEqual([8, 8, 8, 8, 8, 0, 0]);
		expect(weekTargets(null)).toEqual([0, 0, 0, 0, 0, 0, 0]);
		expect(weekTotal(uneven)).toBe(30);
		expect(weekTotal({ targetMon: 7.7, targetTue: 7.7, targetWed: 7.7, targetThu: 7.7, targetFri: 7.7 })).toBe(38.5);
	});

	it('starts the form from the current values', () => {
		expect(formFrom(fullTime, '2026-11-01')).toEqual({ from: '2026-11-01', weekly: 40, distribute: true, days: [8, 8, 8, 8, 8, 0, 0] });
		expect(formFrom(uneven, '2026-11-01').distribute).toBe(false);
		expect(formFrom({ ...fullTime, targetSat: 4, weeklyWorkHours: null }, 'x')).toMatchObject({ distribute: false, weekly: 44 });
		expect(formFrom(null, '2026-11-01')).toEqual({ from: '2026-11-01', weekly: null, distribute: true, days: [0, 0, 0, 0, 0, 0, 0] });
	});

	it('checks the start day and the hours', () => {
		const ok = formFrom(fullTime, '2026-11-01');
		expect(formErrors(ok, '2026-10-27')).toEqual({});
		expect(formErrors({ ...ok, from: '' }).from).toBe('schedule.plan.fromRequired');
		expect(formErrors({ ...ok, from: '2026-10-01' }, '2026-10-27').from).toBe('schedule.plan.fromBeforeToday');
		expect(formErrors({ ...ok, from: '2026-10-01' }, null)).toEqual({});
		expect(formErrors({ ...ok, weekly: 81 }).weekly).toBe('schedule.plan.weeklyRange');
		expect(formErrors({ ...ok, weekly: null }).weekly).toBe('schedule.plan.weeklyRequired');
		expect(formErrors({ ...ok, distribute: false, weekly: 99, days: [25, 0, 0, 0, 0, 0, 0] })).toEqual({ days: 'schedule.plan.dailyRange' });
		expect(formErrors({ ...ok, distribute: false, weekly: null, days: [25, 0, 0, 0, 0, 0, 0] }).days).toBe('schedule.plan.dailyRange');
	});

	it('sends a weekly total or the days', () => {
		expect(targetsPayload(formFrom(fullTime, 'x'))).toEqual({ weeklyWorkHours: 40, distributeEvenly: true });
		expect(targetsPayload({ from: 'x', weekly: null, distribute: false, days: [8, 8, 8, 6, null, 0, 0] })).toEqual({
			weeklyWorkHours: 30, distributeEvenly: false,
			targetMon: 8, targetTue: 8, targetWed: 8, targetThu: 6, targetFri: 0, targetSat: 0, targetSun: 0
		});
	});
});
