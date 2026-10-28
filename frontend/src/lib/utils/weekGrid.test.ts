import { afterEach, describe, expect, it } from 'vitest';
import { locale } from '$lib/stores/locale.svelte';
import { appointmentLabel, buildWeekRows, isoWeek, weekDayKeys, weekStart } from './weekGrid';
import { absenceChipClass, absenceChipStyle } from './colors';
import { dateKey } from './dateHelpers';
import type { OrganizationMemberResponse } from '$lib/api';

function member(id: number, firstName: string, groups: { groupId: number; name: string; viaGroupName?: string }[] = []): OrganizationMemberResponse {
	return { id, firstName, lastName: 'X', groups };
}

describe('week grid', () => {
	afterEach(() => locale.set('de'));

	it('finds the Monday, the ISO week and the seven days', () => {
		expect(dateKey(weekStart(new Date(2026, 9, 27)))).toBe('2026-10-26');
		expect(dateKey(weekStart(new Date(2026, 10, 1)))).toBe('2026-10-26'); // Sunday
		expect(isoWeek(new Date(2026, 9, 27))).toBe(44);
		expect(isoWeek(new Date(2027, 0, 1))).toBe(53); // Friday → 2026's week 53
		expect(isoWeek(new Date(2025, 11, 29))).toBe(1); // Monday of 2026's week 1
		expect(isoWeek(new Date(2026, 2, 30))).toBe(14); // after the DST switch
		expect(weekDayKeys(new Date(2026, 9, 29))).toEqual(['2026-10-26', '2026-10-27', '2026-10-28', '2026-10-29', '2026-10-30', '2026-10-31', '2026-11-01']);
	});

	it('labels appointments with short times, the note or the chip word', () => {
		expect(appointmentLabel({ startTime: '10:00', endTime: '12:00', allDay: false, note: 'Arzt' })).toBe('10–12 Arzt');
		expect(appointmentLabel({ startTime: '09:30', endTime: '12:00', allDay: false, note: null })).toBe('09:30–12 Termin');
		expect(appointmentLabel({ startTime: null, endTime: null, allDay: true, note: null })).toBe('Ganztägig Termin');
		locale.set('en');
		expect(appointmentLabel({ startTime: '14:00', endTime: '15:45', allDay: false })).toBe('14–15:45 Appointment');
	});

	it('builds rows: own first, then by group (none last) and name; cells sorted', () => {
		const days = weekDayKeys(new Date(2026, 9, 26));
		const rows = buildWeekRows({
			members: [
				member(1, 'Zoe'),
				member(2, 'Bert', [{ groupId: 10, name: 'Küche' }]),
				member(3, 'Anna', [{ groupId: 10, name: 'Küche' }]),
				member(4, 'Me', [{ groupId: 20, name: 'Bar' }]),
				member(5, 'Carl', [{ groupId: 10, name: 'Küche', viaGroupName: 'Spül' }, { groupId: 11, name: 'Spül' }])
			],
			me: 4,
			days,
			absences: [{ id: 1, userId: 3, date: '2026-10-27', type: 'Vacation', isHalfDay: true }, { id: 2, userId: 99, date: '2026-10-27', type: 'Other' }, { id: 3, userId: 3, date: '2026-12-01', type: 'Other' }],
			appointments: [
				{ id: 1, userId: 4, date: '2026-10-28', startTime: '14:00', endTime: '15:00', allDay: false },
				{ id: 2, userId: 4, date: '2026-10-28', startTime: '08:00', endTime: '09:00', allDay: false },
				{ id: 3, userId: 4, date: '2026-10-28', allDay: true }
			]
		});
		expect(rows.map((r) => r.member.firstName)).toEqual(['Me', 'Anna', 'Bert', 'Carl', 'Zoe']);
		expect(rows.map((r) => r.group)).toEqual(['Bar', 'Küche', 'Küche', 'Spül', null]);
		expect(rows[1].cells['2026-10-27'].absences).toHaveLength(1);
		expect(rows[0].cells['2026-10-28'].appointments.map((a) => a.id)).toEqual([3, 2, 1]);
		expect(rows[0].isMe).toBe(true);

		const kitchen = buildWeekRows({ members: [member(1, 'Zoe'), member(5, 'Carl', [{ groupId: 10, name: 'Küche', viaGroupName: 'Spül' }])], me: 1, days, absences: [], appointments: [], groupId: 10 });
		expect(kitchen.map((r) => r.member.id)).toEqual([5]);
	});

	it('colours absence chips like the month view', () => {
		expect(absenceChipStyle({ customTypeName: 'Schule', customTypeColor: '#123456' })).toContain('#123456');
		expect(absenceChipStyle({ type: 'Vacation', userId: 2 })).toContain('--accent-');
		expect(absenceChipStyle({ type: 'SickDay' })).toBe('');
		expect(absenceChipClass({ type: 'SickDay' })).toContain('bg-error/10');
		expect(absenceChipClass({ type: 'Other' })).toContain('bg-base-content/10');
		expect(absenceChipClass({ type: 'Vacation' })).toBe('');
	});
});
