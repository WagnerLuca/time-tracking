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
	inheritFromYes: 'Erben von {source} (ja)',
	inheritFromNo: 'Erben von {source} (nein)',
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
		Groups: 'Gruppen (inkl. Gruppenleitung)',
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
