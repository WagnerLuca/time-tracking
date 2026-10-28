import { t } from '$lib/i18n';
import { dateKey } from './dateHelpers';
import type { AbsenceDayResponse, AppointmentResponse, OrganizationMemberResponse } from '$lib/api';

/** Monday (local midnight) of the week containing `d`. */
export function weekStart(d: Date): Date {
	return new Date(d.getFullYear(), d.getMonth(), d.getDate() - ((d.getDay() + 6) % 7));
}

/** ISO 8601 week number: the week with the year's first Thursday is week 1. */
export function isoWeek(d: Date): number {
	const thursday = new Date(d.getFullYear(), d.getMonth(), d.getDate() + 3 - ((d.getDay() + 6) % 7));
	const dayOfYear = Math.round((thursday.getTime() - new Date(thursday.getFullYear(), 0, 1).getTime()) / 86400000);
	return Math.floor(dayOfYear / 7) + 1;
}

/** The seven day keys ('YYYY-MM-DD') Monday to Sunday of the week containing `d`. */
export function weekDayKeys(d: Date): string[] {
	const monday = weekStart(d);
	return Array.from({ length: 7 }, (_, i) => dateKey(new Date(monday.getFullYear(), monday.getMonth(), monday.getDate() + i)));
}

/** "10:00" → "10", "10:30" stays. */
function shortTime(time: string): string {
	return time.endsWith(':00') ? String(Number(time.slice(0, 2))) : time;
}

/** "10–12", or "Ganztägig". */
export function appointmentTimes(a: AppointmentResponse): string {
	if (a.allDay || !a.startTime || !a.endTime) return t('appointments.allDay');
	return `${shortTime(a.startTime)}–${shortTime(a.endTime)}`;
}

/** Chip text: "10–12 Arzt" with a visible note, else "10–12 Termin". */
export function appointmentLabel(a: AppointmentResponse): string {
	return `${appointmentTimes(a)} ${a.note || t('appointments.chip')}`;
}

export interface WeekCell {
	absences: AbsenceDayResponse[];
	appointments: AppointmentResponse[];
}

export interface WeekRow {
	member: OrganizationMemberResponse;
	isMe: boolean;
	/** The member's first direct group (else the first inherited one), null without a group. */
	group: string | null;
	cells: Record<string, WeekCell>;
}

function groupName(m: OrganizationMemberResponse): string | null {
	const groups = m.groups ?? [];
	return (groups.find((g) => !g.viaGroupName) ?? groups[0])?.name ?? null;
}

/**
 * One row per member for the week view: own row first, then by group (members without a group
 * last) and name. `groupId` keeps only members of that group or its sub-groups (inherited
 * memberships list the ancestor groups too). Appointments in a cell: all-day first, then by start.
 */
export function buildWeekRows(input: {
	members: OrganizationMemberResponse[];
	me: number | null | undefined;
	days: string[];
	absences: AbsenceDayResponse[];
	appointments: AppointmentResponse[];
	groupId?: number | null;
}): WeekRow[] {
	const { members, me, days, absences, appointments, groupId } = input;
	const name = (m: OrganizationMemberResponse) => `${m.firstName ?? ''} ${m.lastName ?? ''}`.trim();
	const rows = members
		.filter((m) => !groupId || (m.groups ?? []).some((g) => g.groupId === groupId))
		.map((member): WeekRow => ({
			member,
			isMe: member.id === me,
			group: groupName(member),
			cells: Object.fromEntries(days.map((d) => [d, { absences: [], appointments: [] }]))
		}));
	const byUser = new Map(rows.map((r) => [r.member.id, r]));
	for (const a of absences) byUser.get(a.userId)?.cells[a.date ?? '']?.absences.push(a);
	for (const a of appointments) byUser.get(a.userId)?.cells[a.date ?? '']?.appointments.push(a);
	const startOf = (a: AppointmentResponse) => (a.allDay || !a.startTime ? '' : a.startTime);
	for (const r of rows) for (const c of Object.values(r.cells)) c.appointments.sort((a, b) => startOf(a).localeCompare(startOf(b)));
	return rows.sort((a, b) =>
		Number(b.isMe) - Number(a.isMe) ||
		Number(a.group === null) - Number(b.group === null) ||
		(a.group ?? '').localeCompare(b.group ?? '') ||
		name(a.member).localeCompare(name(b.member)));
}
