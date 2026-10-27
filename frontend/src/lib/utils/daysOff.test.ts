import { beforeEach, describe, expect, it, vi } from 'vitest';

const api = vi.hoisted(() => ({
	days: vi.fn(),
	absences: vi.fn()
}));
vi.mock('$lib/apiClient', () => ({
	holidayApi: { apiV1OrganizationsSlugHolidaysDaysGet: api.days },
	absenceDayApi: { apiV1OrganizationsSlugAbsencesGet: api.absences }
}));

import { loadDaysOff } from './daysOff';

describe('loadDaysOff', () => {
	beforeEach(() => {
		api.days.mockResolvedValue({ data: { days: [
			{ date: '2026-12-24', name: 'Heiligabend', isHalfDay: true, source: 'Company' },
			{ date: '2026-12-25', name: '1. Weihnachtstag', isHalfDay: false, source: 'Public' }
		] } });
		api.absences.mockResolvedValue({ data: { items: [
			{ date: '2026-12-21', type: 'Vacation', isHalfDay: true },
			{ date: '2026-12-21', type: 'SickDay', isHalfDay: true },
			{ date: '2026-12-22', type: 'Vacation', isHalfDay: true }
		], total: 3 } });
	});

	it('takes the member’s own holidays from the server', async () => {
		const d = await loadDaysOff('acme', 7, new Date(2026, 11, 1), new Date(2026, 11, 31));
		expect(api.days).toHaveBeenCalledWith('acme', '2026-12-01', '2026-12-31', 7);
		expect([...d.holidayDates.keys()]).toEqual(['2026-12-24', '2026-12-25']);
		expect([...d.halfDayHolidays]).toEqual(['2026-12-24']);
	});

	it('counts two half-day absences on one day as a whole day off', async () => {
		const d = await loadDaysOff('acme', 7, new Date(2026, 11, 1), new Date(2026, 11, 31));
		expect(d.vacationDates.has('2026-12-21') && d.sickDayDates.has('2026-12-21')).toBe(true);
		expect([...d.halfDayAbsences]).toEqual(['2026-12-22']);
	});
});
