/** Strings for the shared schedule editors (SchedulePeriodEditor, InitialOvertimeEditor, active schedule). Add keys to both objects; `en` is typed against `de`. */
export const de = {
	periods: {
		title: 'Arbeitszeit-Zeiträume',
		add: '+ Zeitraum hinzufügen',
		approvalNote: 'Arbeitszeit-Zeiträume zu ändern braucht eine Genehmigung. Wende dich an einen Admin deiner Organisation.',
		disabledNote: 'Die Arbeitszeit-Zeiträume werden von den Admins dieser Organisation verwaltet.',
		validFrom: 'Gültig ab',
		validTo: 'Gültig bis',
		weeklyHours: 'Wochenstunden',
		distribute: 'Gleichmäßig verteilen (Mo–Fr)',
		dailyTargets: 'Soll pro Tag (Stunden)',
		fromRequired: 'Bitte ein Startdatum wählen.',
		toBeforeFrom: 'Das Ende darf nicht vor dem Start liegen.',
		weeklyRange: 'Bitte 0–{max} Stunden eingeben.',
		dailyRange: 'Das Soll pro Tag muss zwischen 0 und {max} Stunden liegen.',
		replaceHint: 'Ab diesem Datum gibt es schon einen Zeitraum – Speichern ersetzt ihn.',
		submitAdd: 'Zeitraum hinzufügen',
		submitReplace: 'Zeitraum ersetzen',
		empty: 'Noch keine Arbeitszeit-Zeiträume.',
		active: 'Aktiv',
		delete: 'Zeitraum löschen',
		deleteTitle: 'Arbeitszeit-Zeitraum löschen?',
		added: 'Arbeitszeit-Zeitraum hinzugefügt.',
		updated: 'Arbeitszeit-Zeitraum aktualisiert.',
		deleted: 'Arbeitszeit-Zeitraum gelöscht.',
		loadFailed: 'Die Arbeitszeit-Zeiträume konnten nicht geladen werden.',
		saveFailed: 'Der Arbeitszeit-Zeitraum konnte nicht gespeichert werden.',
		deleteFailed: 'Der Arbeitszeit-Zeitraum konnte nicht gelöscht werden.'
	},
	overtime: {
		title: 'Anfangsüberstunden',
		description: 'Überstunden-Übertrag aus der Zeit vor der Zeiterfassung.',
		hoursLabel: 'Stunden (negativ bei Minusstunden)',
		invalid: 'Bitte eine Stundenzahl eingeben.',
		range: 'Bitte einen Wert zwischen -{max} und {max} eingeben.',
		approvalNote: 'Diesen Wert zu ändern braucht eine Genehmigung. Wende dich an einen Admin deiner Organisation.',
		disabledNote: 'Dieser Wert wird von den Admins dieser Organisation verwaltet.',
		saved: 'Anfangsüberstunden gespeichert.',
		saveFailed: 'Die Anfangsüberstunden konnten nicht gespeichert werden.'
	},
	active: {
		title: 'Aktives Arbeitszeitmodell',
		weeklyHours: 'Wochenstunden',
		fromPeriods: 'Die Werte stammen aus den Arbeitszeit-Zeiträumen.',
		none: 'Kein Arbeitszeitmodell hinterlegt.'
	}
};

export const en: typeof de = {
	periods: {
		title: 'Schedule periods',
		add: '+ Add period',
		approvalNote: "Changing schedule periods needs approval. Ask an admin of your organization.",
		disabledNote: 'Schedule periods are managed by the admins of this organization.',
		validFrom: 'Valid from',
		validTo: 'Valid to',
		weeklyHours: 'Weekly hours',
		distribute: 'Distribute evenly (Mon–Fri)',
		dailyTargets: 'Daily targets (hours)',
		fromRequired: 'Choose a start date.',
		toBeforeFrom: 'The end must be on or after the start.',
		weeklyRange: 'Enter 0–{max} hours.',
		dailyRange: 'Daily targets must be between 0 and {max} hours.',
		replaceHint: 'A period starting on this date exists already — saving replaces it.',
		submitAdd: 'Add period',
		submitReplace: 'Replace period',
		empty: 'No schedule periods yet.',
		active: 'Active',
		delete: 'Delete period',
		deleteTitle: 'Delete schedule period?',
		added: 'Schedule period added.',
		updated: 'Schedule period updated.',
		deleted: 'Schedule period deleted.',
		loadFailed: 'Could not load the schedule periods.',
		saveFailed: 'Could not save the schedule period.',
		deleteFailed: 'Could not delete the schedule period.'
	},
	overtime: {
		title: 'Initial overtime',
		description: 'Carry-over overtime from before time tracking started.',
		hoursLabel: 'Hours (negative for a deficit)',
		invalid: 'Enter a number of hours.',
		range: 'Enter a value between -{max} and {max}.',
		approvalNote: 'Changing this value needs approval. Ask an admin of your organization.',
		disabledNote: 'This value is managed by the admins of this organization.',
		saved: 'Initial overtime saved.',
		saveFailed: 'Could not save the initial overtime.'
	},
	active: {
		title: 'Active schedule',
		weeklyHours: 'Weekly hours',
		fromPeriods: 'These values come from the schedule periods.',
		none: 'No schedule configured.'
	}
};
