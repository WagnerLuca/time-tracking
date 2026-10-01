/** Strings for time entry rows, the inline editor and edit requests. `en` is typed against `de`. */
export const de = {
	start: 'Beginn',
	end: 'Ende',
	note: 'Notiz',
	notePlaceholder: 'Notiz (optional)',
	pauseMinutes: 'Pause (min)',
	endAfterStart: 'Das Ende muss nach dem Beginn liegen.',
	running: 'Läuft',
	needsCorrection: 'Korrektur nötig',
	needsCorrectionHint: 'Der Timer lief über das Ende des Arbeitstags und wurde automatisch gestoppt. Er zählt als 0, bis die Endzeit korrigiert ist.',
	pauseBadge: '−{minutes} min Pause',
	addPauseBadge: '+ Pause',
	editPause: 'Pause bearbeiten',
	requestPauseEdit: 'Pausenänderung beantragen',
	addPause: 'Pause hinzufügen',
	requestPause: 'Pause beantragen',
	editEntry: 'Zeiteintrag bearbeiten',
	requestEdit: 'Änderung beantragen',
	deleteEntry: 'Zeiteintrag löschen',
	deleteTitle: 'Zeiteintrag löschen?',
	deleteMessage: 'Der Zeiteintrag wird endgültig entfernt.',
	saved: 'Zeiteintrag gespeichert.',
	deleted: 'Zeiteintrag gelöscht.',
	saveFailed: 'Zeiteintrag konnte nicht gespeichert werden.',
	deleteFailed: 'Zeiteintrag konnte nicht gelöscht werden.',
	pauseAuto: 'automatisch',
	pauseAutoHint: 'Leer lassen: die Pausenregeln der Organisation gelten.',
	manual: {
		add: 'Eintrag nachtragen',
		addAdmin: 'Eintrag hinzufügen',
		title: 'Vergessenen Eintrag für {date} nachtragen',
		titleAdmin: 'Eintrag für {date} hinzufügen',
		needsApprovalHint: 'In dieser Organisation muss ein nachgetragener Eintrag genehmigt werden. Er wird als Antrag gesendet.',
		created: 'Zeiteintrag nachgetragen.',
		requested: 'Antrag gesendet. Der Eintrag erscheint, sobald ein Admin ihn genehmigt.',
		failed: 'Der Eintrag konnte nicht nachgetragen werden.',
		endRequired: 'Bitte ein Ende angeben.',
		disabled: 'Nachtragen ist in dieser Organisation deaktiviert.',
		overlap: 'Der Eintrag überschneidet sich mit einem vorhandenen Eintrag.'
	},
	warnings: {
		dailyMax: 'Arbeitszeitgesetz: An diesem Tag wurde mehr als 10 Stunden gearbeitet.',
		restPeriod: 'Arbeitszeitgesetz: Vor diesem Eintrag lagen weniger als 11 Stunden Ruhezeit.'
	},
	history: {
		title: 'Verlauf',
		loading: 'Verlauf wird geladen…',
		empty: 'Noch keine Änderungen aufgezeichnet.',
		failed: 'Der Verlauf konnte nicht geladen werden.',
		by: '{name}',
		system: 'System',
		kind: {
			Created: 'Erstellt',
			Updated: 'Geändert',
			Deleted: 'Gelöscht',
			ApprovedRequest: 'Per Antrag übernommen'
		},
		field: {
			StartTime: 'Beginn',
			EndTime: 'Ende',
			PauseDurationMinutes: 'Pause',
			Description: 'Notiz',
			OrganizationId: 'Organisation',
			IsFailure: 'Korrektur nötig'
		},
		empty_value: 'leer',
		minutes: '{value} min'
	},
	request: {
		titleEdit: 'Änderung des Zeiteintrags beantragen',
		titlePause: 'Pausenänderung beantragen',
		intro: 'Neue Werte eintragen. Ein Admin prüft und übernimmt sie.',
		newStart: 'Neuer Beginn',
		newEnd: 'Neues Ende',
		newPause: 'Neue Pause (Minuten)',
		message: 'Nachricht an den Admin (optional)',
		submit: 'Antrag absenden',
		submitted: 'Antrag gesendet. Ein Admin prüft ihn.',
		submitFailed: 'Antrag konnte nicht gesendet werden.'
	}
};

export const en: typeof de = {
	start: 'Start',
	end: 'End',
	note: 'Note',
	notePlaceholder: 'Note (optional)',
	pauseMinutes: 'Pause (min)',
	endAfterStart: 'End must be after start.',
	running: 'Running',
	needsCorrection: 'Needs correction',
	needsCorrectionHint: 'The timer ran past the business day end and was stopped automatically. It counts as 0 until the end time is corrected.',
	pauseBadge: '−{minutes} min pause',
	addPauseBadge: '+ Pause',
	editPause: 'Edit pause',
	requestPauseEdit: 'Request pause change',
	addPause: 'Add pause',
	requestPause: 'Request pause',
	editEntry: 'Edit time entry',
	requestEdit: 'Request change',
	deleteEntry: 'Delete time entry',
	deleteTitle: 'Delete time entry?',
	deleteMessage: 'The time entry is removed permanently.',
	saved: 'Time entry saved.',
	deleted: 'Time entry deleted.',
	saveFailed: 'Could not save the time entry.',
	deleteFailed: 'Could not delete the time entry.',
	pauseAuto: 'automatic',
	pauseAutoHint: "Leave empty: the organization's pause rules apply.",
	manual: {
		add: 'Add missed entry',
		addAdmin: 'Add entry',
		title: 'Add a forgotten entry for {date}',
		titleAdmin: 'Add an entry for {date}',
		needsApprovalHint: 'In this organization an added entry needs approval. It is sent as a request.',
		created: 'Time entry added.',
		requested: 'Request sent. The entry appears once an admin approves it.',
		failed: 'The entry could not be added.',
		endRequired: 'Please enter an end time.',
		disabled: 'Adding past entries is disabled in this organization.',
		overlap: 'The entry overlaps an existing entry.'
	},
	warnings: {
		dailyMax: 'Working Time Act: more than 10 hours were worked on this day.',
		restPeriod: 'Working Time Act: less than 11 hours of rest before this entry.'
	},
	history: {
		title: 'History',
		loading: 'Loading history…',
		empty: 'No changes recorded yet.',
		failed: 'Could not load the history.',
		by: '{name}',
		system: 'System',
		kind: {
			Created: 'Created',
			Updated: 'Changed',
			Deleted: 'Deleted',
			ApprovedRequest: 'Applied from a request'
		},
		field: {
			StartTime: 'Start',
			EndTime: 'End',
			PauseDurationMinutes: 'Pause',
			Description: 'Note',
			OrganizationId: 'Organization',
			IsFailure: 'Needs correction'
		},
		empty_value: 'empty',
		minutes: '{value} min'
	},
	request: {
		titleEdit: 'Request time entry change',
		titlePause: 'Request pause change',
		intro: 'Enter the new values. An admin will review and apply them.',
		newStart: 'New start',
		newEnd: 'New end',
		newPause: 'New pause (minutes)',
		message: 'Message for the admin (optional)',
		submit: 'Submit request',
		submitted: 'Request submitted. An admin will review it.',
		submitFailed: 'Could not submit the request.'
	}
};
