import { describe, expect, it } from 'vitest';
import { getAbsenceCredit, getDayTarget, getDayType, getTargetForRange, type ScheduleLike } from './scheduleHelpers';

// 40h week, 8h Mon–Fri (targets are hours in the API, the helpers return minutes)
const schedule: ScheduleLike = { targetMon: 8, targetTue: 8, targetWed: 8, targetThu: 8, targetFri: 8, weeklyWorkHours: 40 };
const none = new Set<string>();

// Week of Mon 2026-03-02 … Sun 2026-03-08
const mon = new Date(2026, 2, 2);
const wed = new Date(2026, 2, 4);
const fri = new Date(2026, 2, 6);
const sat = new Date(2026, 2, 7);
const sun = new Date(2026, 2, 8);

describe('getDayTarget', () => {
	it('returns the weekday target in minutes and 0 on weekends', () => {
		expect(getDayTarget(mon, schedule, new Map())).toBe(480);
		expect(getDayTarget(sat, schedule, new Map())).toBe(0);
		expect(getDayTarget(sun, schedule, new Map())).toBe(0);
	});

	it('returns 0 without any schedule', () => {
		expect(getDayTarget(mon, null, new Map())).toBe(0);
	});

	it('full holidays have no target, half holidays half of it', () => {
		const holidays = new Map([['2026-03-04', 'Feiertag']]);
		expect(getDayTarget(wed, schedule, holidays)).toBe(0);
		expect(getDayTarget(wed, schedule, holidays, [], new Set(['2026-03-04']))).toBe(240);
	});

	it('a matching schedule period wins over the base schedule', () => {
		const periods = [{ validFrom: '2026-03-04', validTo: null, targetWed: 6, targetFri: 4 }];
		expect(getDayTarget(mon, schedule, new Map(), periods)).toBe(480); // before the period
		expect(getDayTarget(wed, schedule, new Map(), periods)).toBe(360);
		expect(getDayTarget(fri, schedule, new Map(), periods)).toBe(240);
	});

	it('respects the period end date', () => {
		const periods = [{ validFrom: '2026-03-01', validTo: '2026-03-04', targetMon: 4, targetWed: 4, targetFri: 4 }];
		expect(getDayTarget(wed, schedule, new Map(), periods)).toBe(240);
		expect(getDayTarget(fri, schedule, new Map(), periods)).toBe(480); // after validTo → base schedule
	});

	it('works with periods only (no base schedule)', () => {
		const periods = [{ validFrom: '2026-01-01', targetMon: 5 }];
		expect(getDayTarget(mon, null, new Map(), periods)).toBe(300);
	});
});

describe('getAbsenceCredit', () => {
	it('credits the full day target for a full-day absence and nothing otherwise', () => {
		expect(getAbsenceCredit(mon, schedule, new Map(), new Set(['2026-03-02']), none, none)).toBe(480);
		expect(getAbsenceCredit(mon, schedule, new Map(), none, new Set(['2026-03-02']), none)).toBe(480);
		expect(getAbsenceCredit(mon, schedule, new Map(), none, none, none)).toBe(0);
	});

	it('credits half the target for a half-day absence', () => {
		const vacation = new Set(['2026-03-02']);
		expect(getAbsenceCredit(mon, schedule, new Map(), none, vacation, none, [], new Set(['2026-03-02']))).toBe(240);
	});

	it('credits nothing on a weekend or full holiday', () => {
		expect(getAbsenceCredit(sat, schedule, new Map(), new Set(['2026-03-07']), none, none)).toBe(0);
		const holidays = new Map([['2026-03-04', 'Feiertag']]);
		expect(getAbsenceCredit(wed, schedule, holidays, none, new Set(['2026-03-04']), none)).toBe(0);
	});

	it('half-day absence on a half-day holiday uses the unreduced target (4h of 8h, not 2h)', () => {
		const key = '2026-03-04';
		const holidays = new Map([[key, 'Heiligabend']]);
		const credit = getAbsenceCredit(wed, schedule, holidays, none, new Set([key]), none, [], new Set([key]), new Set([key]));
		expect(credit).toBe(240);
	});

	it('full-day absence on a half-day holiday credits the remaining half', () => {
		const key = '2026-03-04';
		const holidays = new Map([[key, 'Silvester']]);
		expect(getAbsenceCredit(wed, schedule, holidays, none, new Set([key]), none, [], undefined, new Set([key]))).toBe(240);
	});

	it('custom types: no-credit gives 0, top-up fills to the target', () => {
		const key = '2026-03-02';
		const other = new Set([key]);
		expect(getAbsenceCredit(mon, schedule, new Map(), none, none, other, [], undefined, undefined, { noCredit: new Set([key]), topUp: new Set() })).toBe(0);
		expect(getAbsenceCredit(mon, schedule, new Map(), none, none, other, [], undefined, undefined, { noCredit: new Set(), topUp: new Set([key]) }, 300)).toBe(180);
		// worked more than the target → no negative credit
		expect(getAbsenceCredit(mon, schedule, new Map(), none, none, other, [], undefined, undefined, { noCredit: new Set(), topUp: new Set([key]) }, 600)).toBe(0);
	});
});

describe('getTargetForRange', () => {
	it('sums weekday targets, including both ends', () => {
		expect(getTargetForRange(mon, sun, schedule, new Map(), [])).toBe(2400);
		expect(getTargetForRange(mon, mon, schedule, new Map(), [])).toBe(480);
	});

	it('skips holidays and halves half-day holidays', () => {
		const holidays = new Map([['2026-03-04', 'Feiertag'], ['2026-03-06', 'Halber Feiertag']]);
		expect(getTargetForRange(mon, sun, schedule, holidays, [], null, new Set(['2026-03-06']))).toBe(480 * 3 + 240);
	});

	it('clips the range to the first tracked entry', () => {
		expect(getTargetForRange(mon, sun, schedule, new Map(), [], wed)).toBe(480 * 3);
		expect(getTargetForRange(mon, wed, schedule, new Map(), [], new Date(2026, 2, 10))).toBe(0);
	});

	it('crosses the DST switch without losing a day', () => {
		// Mon 2026-03-23 … Fri 2026-03-27, clocks change on Sun 2026-03-29; range to Tue 2026-03-31
		expect(getTargetForRange(new Date(2026, 2, 23), new Date(2026, 2, 31), schedule, new Map(), [])).toBe(480 * 7);
	});

	it('returns 0 without a schedule', () => {
		expect(getTargetForRange(mon, sun, null, new Map(), [])).toBe(0);
	});
});

describe('getDayType', () => {
	it('prefers the absence on half-day holidays', () => {
		const key = '2026-03-04';
		expect(getDayType(key, new Map([[key, 'x']]), none, new Set([key]), none)).toBe('holiday');
		expect(getDayType(key, new Map([[key, 'x']]), none, new Set([key]), none, new Set([key]))).toBe('vacation');
		expect(getDayType(key, new Map(), new Set([key]), none, none)).toBe('sick');
		expect(getDayType(key, new Map(), none, none, none)).toBeNull();
	});
});
