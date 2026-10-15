import type { de } from './de';

export const en: typeof de = {
	title: 'Visibility among members',
	intro:
		'What regular members see about others – separately for people they share at least one group with and for everyone else. Admins see everything, everyone sees themselves, group admins keep their group rights.',
	groupIntro:
		"Overrides for this group's members: what others see about them. For a person in several groups the most permissive override wins; without an override the organization's setting applies.",
	needsMember: 'Only visible when the person is visible.',
	kind: 'Information',
	inGroups: 'In my groups',
	outsideGroups: 'Outside',
	cellLabel: '{kind} – {column}',
	inheritYes: 'Inherit (yes)',
	inheritNo: 'Inherit (no)',
	inheritFromYes: 'Inherit from {source} (yes)',
	inheritFromNo: 'Inherit from {source} (no)',
	allow: 'Yes',
	deny: 'No',
	visible: 'visible',
	hidden: 'hidden',
	saved: 'Visibility saved.',
	saveFailed: 'Could not save the visibility.',
	previewGroup: 'Preview for',
	previewNoGroup: 'a member without a group',
	preview: {
		member: 'A member',
		ofGroup: 'A member of “{group}”',
		sentence: '{who} sees about colleagues in their own groups: {inGroups}; about everyone else: {outside}.',
		nothing: 'nothing',
		othersNote: '(People outside whose own group overrides this may show more or less.)'
	},
	self: {
		title: 'What others see about you',
		titleOther: 'What others see about this member',
		inGroups: 'Shared groups',
		outside: 'Everyone else'
	},
	kinds: {
		Member: 'Person (name, picture)',
		Email: 'Email address',
		Groups: 'Groups (incl. group leads)',
		Presence: 'Presence (timer running)',
		Vacation: 'Vacation',
		SickDays: 'Sick days',
		OtherAbsences: 'Other absences',
		AbsenceNotes: 'Notes on absences',
		VacationBalance: 'Remaining vacation',
		WorkSchedule: 'Work schedule (target hours)',
		TrackedTime: 'Tracked times and balance'
	},
	short: {
		Member: 'name',
		Email: 'email',
		Groups: 'groups',
		Presence: 'presence',
		Vacation: 'vacation',
		SickDays: 'sick days',
		OtherAbsences: 'other absences',
		AbsenceNotes: 'notes',
		VacationBalance: 'remaining vacation',
		WorkSchedule: 'target hours',
		TrackedTime: 'times and balance'
	},
	present: 'Present',
	absent: 'Not clocked in'
};
