/** Strings for the member visibility matrix (org settings, group permissions, member detail). Add keys to both objects; `en` is typed against `de`. */
export const de = {
	title: 'Sichtbarkeit unter Mitgliedern',
	intro:
		'Was normale Mitglieder über andere sehen – getrennt für Personen, mit denen sie mindestens eine Gruppe teilen, und für alle anderen. Admins sehen alles, jede Person sieht sich selbst, Gruppen-Admins behalten ihre Gruppenrechte.',
	groupIntro:
		'Abweichungen für die Mitglieder dieser Gruppe: Was andere über sie sehen. Gehört eine Person zu mehreren Gruppen, gewinnt die großzügigste Abweichung; ohne Abweichung gilt die Organisation.',
	needsMember: 'Nur sichtbar, wenn die Person selbst sichtbar ist.',
	kind: 'Information',
	inGroups: 'In meinen Gruppen',
	outsideGroups: 'Außerhalb',
	cellLabel: '{kind} – {column}',
	inheritYes: 'Erben (ja)',
	inheritNo: 'Erben (nein)',
	allow: 'Ja',
	deny: 'Nein',
	visible: 'sichtbar',
	hidden: 'verborgen',
	saved: 'Sichtbarkeit gespeichert.',
	saveFailed: 'Sichtbarkeit konnte nicht gespeichert werden.',
	previewGroup: 'Vorschau für',
	previewNoGroup: 'ein Mitglied ohne Gruppe',
	preview: {
		member: 'Ein Mitglied',
		ofGroup: 'Ein Mitglied von „{group}“',
		sentence: '{who} sieht über Kolleg:innen der eigenen Gruppen: {inGroups}; über alle anderen: {outside}.',
		nothing: 'nichts',
		othersNote: '(Personen außerhalb mit eigener Gruppen-Abweichung können mehr oder weniger zeigen.)'
	},
	self: {
		title: 'Was andere über dich sehen',
		titleOther: 'Was andere über dieses Mitglied sehen',
		inGroups: 'Gemeinsame Gruppen',
		outside: 'Alle anderen'
	},
	kinds: {
		Member: 'Person (Name, Bild)',
		Email: 'E-Mail-Adresse',
		Groups: 'Gruppen und Rollen',
		Presence: 'Anwesenheit (Timer läuft)',
		Vacation: 'Urlaub',
		SickDays: 'Krankheitstage',
		OtherAbsences: 'Sonstige Abwesenheiten',
		AbsenceNotes: 'Notizen zu Abwesenheiten',
		VacationBalance: 'Resturlaub',
		WorkSchedule: 'Arbeitszeitmodell (Sollstunden)',
		TrackedTime: 'Erfasste Zeiten und Saldo'
	},
	short: {
		Member: 'Name',
		Email: 'E-Mail',
		Groups: 'Gruppen',
		Presence: 'Anwesenheit',
		Vacation: 'Urlaub',
		SickDays: 'Krankheitstage',
		OtherAbsences: 'sonstige Abwesenheiten',
		AbsenceNotes: 'Notizen',
		VacationBalance: 'Resturlaub',
		WorkSchedule: 'Sollstunden',
		TrackedTime: 'Zeiten und Saldo'
	},
	present: 'Anwesend',
	absent: 'Nicht eingestempelt'
};

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
		Groups: 'Groups and roles',
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
