import { describe, expect, it } from 'vitest';
import { closedMonthSet, entryLock, formatMonth, isClosedDay, lastClosableMonth, monthKey, monthsBetween } from './closedPeriods';

describe('closed periods', () => {
	it('matches days against closed months', () => {
		const closed = closedMonthSet(['2026-08-01', '2026-09-01']);
		expect(monthKey('2026-09-30')).toBe('2026-09');
		expect(isClosedDay('2026-09-30', closed)).toBe(true);
		expect(isClosedDay('2026-10-01', closed)).toBe(false);
		expect(isClosedDay(null, closed)).toBe(false);
		expect(closedMonthSet(undefined).size).toBe(0);
	});

	it('names a month and lists ranges across years', () => {
		expect(formatMonth('2026-09')).toMatch(/2026/);
		expect(monthsBetween('2025-11', '2026-02')).toEqual(['2025-11', '2025-12', '2026-01', '2026-02']);
		expect(monthsBetween('2026-03', '2026-02')).toEqual([]);
		expect(lastClosableMonth(new Date(2026, 0, 15))).toBe('2025-12');
		expect(lastClosableMonth(new Date(2026, 9, 1))).toBe('2026-09');
	});

	it('locks entries of closed months for non-admins of that organization only', () => {
		const org = { id: 3, closedMonths: ['2026-09-01'] };
		const entry = { startTime: '2026-09-14T06:00:00Z', organizationId: 3 };
		expect(entryLock(entry, org, false)).toBe('Abgeschlossen (September 2026) – nur Admins können hier ändern');
		expect(entryLock(entry, org, true)).toBe('');
		expect(entryLock({ ...entry, organizationId: 4 }, org, false)).toBe('');
		expect(entryLock({ ...entry, startTime: '2026-10-01T06:00:00Z' }, org, false)).toBe('');
		expect(entryLock(entry, null, false)).toBe('');
	});
});
