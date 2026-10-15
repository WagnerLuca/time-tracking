/** Strings for the absences area (team calendar, add-absence dialog). Add keys to both objects; `en` is typed against `de`. */
export const de = {
	title: 'Abwesenheiten',
	notFound: 'Organisation nicht gefunden.',
	loadOrgFailed: 'Organisation konnte nicht geladen werden.',
	loadPermsFailed: 'Deine Berechtigungen konnten nicht geladen werden.',
	loadTypesFailed: 'Abwesenheitsarten konnten nicht geladen werden.',
	refreshFailed: 'Urlaubsanspruch konnte nicht aktualisiert werden.',
	loadHolidaysFailed: 'Feiertage konnten nicht geladen werden.',
	loadFailed: 'Abwesenheiten konnten nicht geladen werden.',
	intro: {
		admin: 'Kalenderübersicht der Abwesenheiten und Urlaubstage aller Mitglieder. Rechtsklick auf einen Tag (oder lange drücken), um eine Abwesenheit einzutragen.',
		request: 'Kalenderübersicht der für dich sichtbaren Abwesenheiten. Rechtsklick auf einen Tag (oder lange drücken), um eine Abwesenheit zu beantragen – ein Gruppen- oder Organisations-Admin genehmigt sie.',
		self: 'Kalenderübersicht der für dich sichtbaren Abwesenheiten. Rechtsklick auf einen Tag (oder lange drücken), um eine eigene Abwesenheit einzutragen.',
		readOnly: 'Kalenderübersicht der für dich sichtbaren Abwesenheiten.'
	},
	noOthers: 'Deine Organisation zeigt dir keine Abwesenheiten anderer – du siehst hier nur deine eigenen.',
	deleteTitle: 'Abwesenheit löschen?',
	deleteLabel: 'Abwesenheit löschen',
	deleted: 'Abwesenheit gelöscht.',
	deleteFailed: 'Abwesenheit konnte nicht gelöscht werden.',
	saved: 'Abwesenheit gespeichert.',
	prevMonth: 'Vorheriger Monat',
	nextMonth: 'Nächster Monat',
	today: 'Heute',
	show: 'Anzeigen',
	allMembers: 'Alle Mitglieder',
	clearFilter: 'Zurücksetzen',
	youSuffix: ' (du)',
	requestAbsence: 'Abwesenheit beantragen',
	addAbsence: 'Abwesenheit eintragen',
	emptyMonth: 'Keine Abwesenheiten oder Feiertage in diesem Monat.',
	emptyDay: 'An diesem Tag ist niemand abwesend.',
	halfDay: 'Halber Tag',
	days: { one: '1 Tag', other: '{count} Tage' },
	legend: {
		vacationHint: 'Urlaubsfarben je Person'
	},
	balance: {
		title: 'Urlaubsanspruch',
		taken: 'Genommen',
		planned: 'Geplant',
		none: 'Für keine Mitglieder sind Urlaubstage hinterlegt.',
		noneSelected: 'Für das ausgewählte Mitglied sind keine Urlaubstage hinterlegt.',
		barTitle: '{taken} Tage genommen, {planned} Tage geplant',
		used: '{taken} von {total} Tagen genommen',
		plannedSuffix: ' · {planned} geplant',
		left: 'noch {remaining}'
	},
	list: {
		title: 'Abwesenheitsliste',
		year: 'Jahr',
		member: 'Mitglied',
		type: 'Art',
		allTypes: 'Alle Arten',
		empty: 'Keine Abwesenheiten in {year}.',
		halfDay: '½ Tag',
		entries: { one: '1 Eintrag', other: '{count} Einträge' },
		totalDays: { one: '(1 Tag insgesamt)', other: '({count} Tage insgesamt)' }
	},
	dialog: {
		existing: 'Abwesenheiten an diesem Tag',
		requestDivider: 'Abwesenheit beantragen',
		addDivider: 'Neue Abwesenheit eintragen',
		requestHint: 'Wird als Antrag gesendet und muss von deinem Gruppen-Admin oder einem Organisations-Admin genehmigt werden.',
		member: 'Mitglied',
		selectMember: 'Mitglied auswählen…',
		memberRequired: 'Bitte ein Mitglied auswählen.',
		from: 'Von',
		to: 'Bis (optional)',
		toBeforeFrom: 'Das Enddatum darf nicht vor dem Startdatum liegen.',
		type: 'Art',
		note: 'Notiz (optional)',
		notePlaceholder: 'z. B. Arzttermin',
		halfDay: 'Halber Tag',
		workdaysHint: 'Gezählt werden nur Arbeitstage laut Arbeitszeitplan (ohne Plan Mo–Fr), Feiertage nicht.',
		sendRequest: 'Antrag senden',
		add: 'Abwesenheit eintragen',
		requestSent: {
			one: 'Abwesenheitsantrag für 1 Tag zur Genehmigung gesendet.',
			other: 'Abwesenheitsantrag für {count} Kalendertage zur Genehmigung gesendet; es zählen nur deine Arbeitstage.'
		},
		requestFailed: 'Antrag konnte nicht gesendet werden.',
		addFailed: 'Abwesenheit konnte nicht eingetragen werden.',
		credit: {
			FillToTarget: 'Die Stunden werden bis zum Tagessoll aufgefüllt.',
			FullCredit: 'Das volle Tagessoll wird gutgeschrieben.',
			None: 'Die Stunden bleiben wie erfasst – keine Gutschrift.'
		}
	}
};
