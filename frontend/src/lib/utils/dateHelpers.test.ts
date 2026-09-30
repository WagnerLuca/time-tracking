import { describe, expect, it, vi, afterEach } from 'vitest';
import { dateKey, getWeekRange, isFuture, isToday, sumMinutes, toLocalDateTimeInput } from './dateHelpers';

describe('dateHelpers (TZ=Europe/Berlin)', () => {
	afterEach(() => vi.useRealTimers());

	it('runs in Europe/Berlin', () => {
		// January is CET (UTC+1), July is CEST (UTC+2)
		expect(new Date(2026, 0, 15).getTimezoneOffset()).toBe(-60);
		expect(new Date(2026, 6, 15).getTimezoneOffset()).toBe(-120);
	});

	it('dateKey uses the local calendar day, not UTC', () => {
		expect(dateKey(new Date(2026, 0, 5))).toBe('2026-01-05');
		// 00:30 local on Feb 1 is still Jan 31 in UTC — toISOString() would say 2026-01-31
		const justAfterMidnight = new Date(2026, 1, 1, 0, 30);
		expect(justAfterMidnight.toISOString().slice(0, 10)).toBe('2026-01-31');
		expect(dateKey(justAfterMidnight)).toBe('2026-02-01');
	});

	it('dateKey handles month and year ends', () => {
		expect(dateKey(new Date(2026, 0, 31, 23, 59))).toBe('2026-01-31');
		expect(dateKey(new Date(2026, 1, 28))).toBe('2026-02-28');
		expect(dateKey(new Date(2028, 1, 29))).toBe('2028-02-29');
		expect(dateKey(new Date(2026, 11, 31, 23, 59, 59))).toBe('2026-12-31');
		// last day of month via day 0 of next month (used by the export month range)
		expect(dateKey(new Date(2026, 3, 0))).toBe('2026-03-31');
	});

	it('dateKey is stable across DST switches', () => {
		// 2026-03-29 02:00 → 03:00 (spring forward), 2026-10-25 03:00 → 02:00 (fall back)
		expect(dateKey(new Date(2026, 2, 29, 0, 0))).toBe('2026-03-29');
		expect(dateKey(new Date(2026, 2, 29, 23, 59))).toBe('2026-03-29');
		expect(dateKey(new Date(2026, 9, 25, 0, 0))).toBe('2026-10-25');
		expect(dateKey(new Date(2026, 9, 25, 23, 59))).toBe('2026-10-25');
	});

	it('iterating days with setDate crosses DST without skipping or repeating', () => {
		const keys: string[] = [];
		const cursor = new Date(2026, 2, 27);
		for (let i = 0; i < 4; i++) {
			keys.push(dateKey(cursor));
			cursor.setDate(cursor.getDate() + 1);
		}
		expect(keys).toEqual(['2026-03-27', '2026-03-28', '2026-03-29', '2026-03-30']);
	});

	it('getWeekRange returns Monday 00:00 to Sunday 23:59:59.999, also for the DST week', () => {
		vi.useFakeTimers();
		vi.setSystemTime(new Date(2026, 2, 26, 12)); // Thursday before spring-forward Sunday
		const { start, end } = getWeekRange(0);
		expect(dateKey(start)).toBe('2026-03-23');
		expect(start.getHours()).toBe(0);
		expect(dateKey(end)).toBe('2026-03-29');
		expect([end.getHours(), end.getMinutes(), end.getSeconds(), end.getMilliseconds()]).toEqual([23, 59, 59, 999]);

		const prev = getWeekRange(-1);
		expect(dateKey(prev.start)).toBe('2026-03-16');
		expect(dateKey(prev.end)).toBe('2026-03-22');
	});

	it('getWeekRange treats Sunday as the last day of the week', () => {
		vi.useFakeTimers();
		vi.setSystemTime(new Date(2026, 9, 25, 20)); // Sunday, fall-back day
		const { start, end } = getWeekRange(0);
		expect(dateKey(start)).toBe('2026-10-19');
		expect(dateKey(end)).toBe('2026-10-25');
	});

	it('isToday / isFuture compare calendar days', () => {
		vi.useFakeTimers();
		vi.setSystemTime(new Date(2026, 4, 10, 8));
		expect(isToday(new Date(2026, 4, 10, 23, 59))).toBe(true);
		expect(isToday(new Date(2026, 4, 11, 0, 0))).toBe(false);
		expect(isFuture(new Date(2026, 4, 10, 23, 59))).toBe(false);
		expect(isFuture(new Date(2026, 4, 11, 0, 1))).toBe(true);
	});

	it('toLocalDateTimeInput renders local wall-clock time', () => {
		expect(toLocalDateTimeInput('2026-07-01T06:05:00Z')).toBe('2026-07-01T08:05');
		expect(toLocalDateTimeInput('2026-01-01T06:05:00Z')).toBe('2026-01-01T07:05');
	});

	it('sumMinutes prefers net duration and skips running entries', () => {
		expect(
			sumMinutes([
				{ netDurationMinutes: 450, durationMinutes: 480 },
				{ durationMinutes: 30 },
				{ isRunning: true, durationMinutes: 99 },
				{ netDurationMinutes: null, durationMinutes: null }
			])
		).toBe(480);
	});
});
