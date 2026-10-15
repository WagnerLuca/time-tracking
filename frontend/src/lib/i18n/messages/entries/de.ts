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
	source: {
		terminal: 'Terminal',
		terminalNamed: 'Terminal {name}',
		terminalRoute: 'Gestartet an {start}, gestoppt an {stop}',
		manual: 'Nachgetragen',
		import: 'Importiert',
		admin: 'Vom Admin erfasst'
	},
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
