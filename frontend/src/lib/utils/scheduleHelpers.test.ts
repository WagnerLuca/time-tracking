import { afterEach, describe, expect, it } from 'vitest';
import { locale } from '$lib/stores/locale.svelte';
import { getAbsenceCredit, getDayTarget, getDayType, getDayTypeLabel, type ScheduleLike } from './scheduleHelpers';

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

	it('uses Saturday/Sunday targets when the schedule has them', () => {
		const weekend = { ...schedule, targetSat: 4, targetSun: 2 };
		expect(getDayTarget(sat, weekend, new Map())).toBe(240);
		expect(getDayTarget(sun, weekend, new Map())).toBe(120);
		expect(getDayTarget(sat, weekend, new Map([['2026-03-07', 'Feiertag']]))).toBe(0);
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

describe('getDayType', () => {
	it('prefers the absence on half-day holidays', () => {
		const key = '2026-03-04';
		expect(getDayType(key, new Map([[key, 'x']]), none, new Set([key]), none)).toBe('holiday');
		expect(getDayType(key, new Map([[key, 'x']]), none, new Set([key]), none, new Set([key]))).toBe('vacation');
		expect(getDayType(key, new Map(), new Set([key]), none, none)).toBe('sick');
		expect(getDayType(key, new Map(), none, none, none)).toBeNull();
	});
});

describe('schedule periods with half days', () => {
	const period = { validFrom: '2026-03-01', validTo: '2026-03-31', targetWed: 6 };

	it('a half-day holiday halves the matching period target, not the base schedule', () => {
		const key = '2026-03-04';
		expect(getDayTarget(wed, schedule, new Map([[key, 'Fasching']]), [period], new Set([key]))).toBe(180);
		// period without base schedule
		expect(getDayTarget(wed, null, new Map([[key, 'Fasching']]), [period], new Set([key]))).toBe(180);
	});

	it('a half-day absence credits half of the period target', () => {
		const key = '2026-03-04';
		expect(getAbsenceCredit(wed, schedule, new Map(), none, new Set([key]), none, [period], new Set([key]))).toBe(180);
	});

	it('a half-day absence outside every period and without a base schedule credits nothing', () => {
		const key = '2026-04-01';
		expect(getAbsenceCredit(new Date(2026, 3, 1), null, new Map(), none, new Set([key]), none, [period], new Set([key]))).toBe(0);
	});

	it('a period without validFrom never matches', () => {
		expect(getDayTarget(wed, schedule, new Map(), [{ validFrom: null, targetWed: 2 }])).toBe(480);
	});

	it('other absences credit like vacation; a day without absence gets nothing even if top-up is set', () => {
		const key = '2026-03-02';
		expect(getAbsenceCredit(mon, schedule, new Map(), none, none, new Set([key]))).toBe(480);
		expect(getAbsenceCredit(mon, schedule, new Map(), none, none, none, [], undefined, undefined, { noCredit: new Set(), topUp: new Set([key]) })).toBe(0);
	});
});

describe('getDayType: remaining cases', () => {
	const key = '2026-03-04';
	const hol = new Map([[key, 'x']]);

	it('half-day holiday + sick / other absence, or no absence at all', () => {
		expect(getDayType(key, hol, new Set([key]), none, none, new Set([key]))).toBe('sick');
		expect(getDayType(key, hol, none, none, new Set([key]), new Set([key]))).toBe('other-absence');
		expect(getDayType(key, hol, none, none, none, new Set([key]))).toBe('holiday');
	});

	it('plain absences by priority (sick > vacation > other), Date or key', () => {
		expect(getDayType(wed, new Map(), new Set([key]), new Set([key]), none)).toBe('sick');
		expect(getDayType(wed, new Map(), none, new Set([key]), new Set([key]))).toBe('vacation');
		expect(getDayType(wed, new Map(), none, none, new Set([key]))).toBe('other-absence');
	});
});

describe('getDayTypeLabel', () => {
	afterEach(() => locale.set('de'));
	const key = '2026-03-04';
	const one = new Set([key]);

	it('is empty for a normal day', () => {
		expect(getDayTypeLabel(key, new Map(), none, none, none)).toBe('');
	});

	it('names the holiday, falling back to a generic label', () => {
		expect(getDayTypeLabel(wed, new Map([[key, 'Tag der Arbeit']]), one, none, none)).toBe('Tag der Arbeit');
		expect(getDayTypeLabel(key, new Map([[key, '']]), none, none, none)).toBe('Feiertag');
	});

	it('absences, with a half-day suffix', () => {
		expect(getDayTypeLabel(key, new Map(), one, none, none)).toBe('Krankheitstag');
		expect(getDayTypeLabel(key, new Map(), none, one, none, undefined, one)).toBe('Urlaub (halber Tag)');
		expect(getDayTypeLabel(key, new Map(), none, none, one)).toBe('Sonstige Abwesenheit');
	});

	it('custom absence types show their own name', () => {
		const custom = new Map([[key, { name: 'Dienstreise' }]]);
		expect(getDayTypeLabel(key, new Map(), none, none, one, undefined, one, custom)).toBe('Dienstreise (halber Tag)');
	});

	it('half-day holiday alone, and combined with each absence', () => {
		const hol = new Map([[key, 'Heiligabend']]);
		expect(getDayTypeLabel(key, hol, none, none, none, one)).toBe('Heiligabend (halber Tag)');
		expect(getDayTypeLabel(key, hol, one, none, none, one)).toBe('Heiligabend (halber Tag) / Krankheitstag');
		expect(getDayTypeLabel(key, hol, none, one, none, one, one)).toBe('Heiligabend (halber Tag) / Urlaub (halber Tag)');
		const custom = new Map([[key, { name: 'Dienstreise' }]]);
		expect(getDayTypeLabel(key, hol, none, none, one, one, undefined, custom)).toBe('Heiligabend (halber Tag) / Dienstreise');
	});

	it('follows the UI language', () => {
		locale.set('en');
		expect(getDayTypeLabel(key, new Map([[key, '']]), none, none, none, one)).toBe('Holiday (half day)');
		expect(getDayTypeLabel(key, new Map(), one, none, none, undefined, one)).toBe('Sick day (half day)');
	});
});
