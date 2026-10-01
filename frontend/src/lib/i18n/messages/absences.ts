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

export const en: typeof de = {
	title: 'Absences',
	notFound: 'Organization not found.',
	loadOrgFailed: 'Could not load the organization.',
	loadPermsFailed: 'Could not load your permissions.',
	loadTypesFailed: 'Could not load absence types.',
	refreshFailed: 'Could not refresh vacation balances.',
	loadHolidaysFailed: 'Could not load holidays.',
	loadFailed: 'Could not load absences.',
	intro: {
		admin: 'Calendar overview of absences and vacation days across all members. Right-click (or long-press) a day to add an absence.',
		request: 'Calendar overview of absences visible to you. Right-click (or long-press) a day to request an absence – a group or org admin approves it.',
		self: 'Calendar overview of absences visible to you. Right-click (or long-press) a day to add your own absence.',
		readOnly: 'Calendar overview of absences visible to you.'
	},
	deleteTitle: 'Delete absence?',
	deleteLabel: 'Delete absence',
	deleted: 'Absence deleted.',
	deleteFailed: 'Could not delete the absence.',
	saved: 'Absence saved.',
	prevMonth: 'Previous month',
	nextMonth: 'Next month',
	today: 'Today',
	show: 'Show',
	allMembers: 'All members',
	clearFilter: 'Clear',
	youSuffix: ' (you)',
	requestAbsence: 'Request absence',
	addAbsence: 'Add absence',
	emptyMonth: 'No absences or holidays this month.',
	emptyDay: 'Nobody is absent on this day.',
	halfDay: 'Half day',
	days: { one: '1 day', other: '{count} days' },
	legend: {
		vacationHint: 'Vacation colours vary by person'
	},
	balance: {
		title: 'Vacation days balance',
		taken: 'Taken',
		planned: 'Planned',
		none: 'No members have vacation days configured.',
		noneSelected: 'The selected member has no vacation days configured.',
		barTitle: '{taken} days taken, {planned} days planned',
		used: '{taken} of {total} days taken',
		plannedSuffix: ' · {planned} planned',
		left: '{remaining} left'
	},
	list: {
		title: 'Absence list',
		year: 'Year',
		member: 'Member',
		type: 'Type',
		allTypes: 'All types',
		empty: 'No absences found for {year}.',
		halfDay: '½ day',
		entries: { one: '1 entry', other: '{count} entries' },
		totalDays: { one: '(1 day total)', other: '({count} days total)' }
	},
	dialog: {
		existing: 'Absences on this day',
		requestDivider: 'Request absence',
		addDivider: 'Add new absence',
		requestHint: 'This will be sent as a request and needs approval by your group admin or an organization admin.',
		member: 'Member',
		selectMember: 'Select member…',
		memberRequired: 'Select a member.',
		from: 'From',
		to: 'To (optional)',
		toBeforeFrom: 'The end date must not be before the start date.',
		type: 'Type',
		note: 'Note (optional)',
		notePlaceholder: "e.g. Doctor's appointment",
		halfDay: 'Half day',
		workdaysHint: 'Only working days of the work schedule count (Mon–Fri without one), holidays do not.',
		sendRequest: 'Send request',
		add: 'Add absence',
		requestSent: {
			one: 'Absence request for 1 day sent for approval.',
			other: 'Absence request for {count} calendar days sent for approval; only your working days count.'
		},
		requestFailed: 'Could not send the request.',
		addFailed: 'Could not add the absence.',
		credit: {
			FillToTarget: 'Hours are filled up to the expected daily target.',
			FullCredit: 'The full daily target is credited.',
			None: 'Hours are kept as tracked – no credit.'
		}
	}
};
