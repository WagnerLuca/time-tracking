/** Shared labels produced by the helpers in utils/ (formatters, scheduleHelpers, orgRules). */
export const de = {
	requestType: {
		JoinOrganization: 'Beitritt',
		EditPastEntry: 'Eintrag ändern',
		EditPause: 'Pause ändern',
		SetInitialOvertime: 'Überstunden',
		WorkScheduleChange: 'Arbeitszeit',
		CsvImport: 'CSV-Import',
		RequestAbsence: 'Abwesenheit',
		AddManualEntry: 'Eintrag nachtragen'
	},
	requestTypeFull: {
		JoinOrganization: 'Organisation beitreten',
		EditPastEntry: 'Vergangenen Zeiteintrag ändern',
		EditPause: 'Pausendauer ändern',
		SetInitialOvertime: 'Anfangsüberstunden festlegen',
		WorkScheduleChange: 'Arbeitszeitmodell ändern',
		CsvImport: 'Zeiteinträge per CSV importieren',
		RequestAbsence: 'Abwesenheit / Urlaub beantragen',
		AddManualEntry: 'Vergessenen Zeiteintrag nachtragen'
	},
	requestStatus: {
		Accepted: 'Angenommen',
		Declined: 'Abgelehnt',
		Pending: 'Offen',
		Withdrawn: 'Zurückgezogen'
	},
	requestData: {
		start: 'Beginn: {value}',
		end: 'Ende: {value}',
		note: 'Notiz: {value}',
		pause: 'Pause: {value} Min.',
		overtime: 'Überstunden: {value} h',
		date: 'Datum: {value}',
		range: '{count} Tage: {from} → {to}',
		type: 'Art: {value}',
		customType: 'Eigene Abwesenheitsart',
		halfDaySuffix: ' (½ Tag)'
	},
	absenceType: {
		SickDay: 'Krankheitstag',
		Vacation: 'Urlaub',
		Other: 'Sonstiges'
	},
	dayType: {
		holiday: 'Feiertag',
		sick: 'Krankheitstag',
		vacation: 'Urlaub',
		otherAbsence: 'Sonstige Abwesenheit',
		halfDay: '(halber Tag)'
	},
	timeAgo: {
		justNow: 'gerade eben'
	},
	role: {
		Owner: 'Inhaber',
		Admin: 'Admin',
		Member: 'Mitglied'
	},
	ruleMode: {
		Disabled: 'Deaktiviert',
		RequiresApproval: 'Genehmigung erforderlich',
		Allowed: 'Erlaubt'
	},
	joinPolicy: {
		Disabled: 'Nur durch Admins',
		RequiresApproval: 'Genehmigung erforderlich',
		Allowed: 'Offen'
	},
	options: {
		disabled: 'Aus',
		disabledHint: 'Funktion ist für Mitglieder ausgeschaltet',
		approval: 'Genehmigung',
		approvalHint: 'Mitglieder stellen einen Antrag, ein Admin genehmigt',
		allowed: 'Erlaubt',
		allowedHint: 'Mitglieder dürfen das selbst',
		adminOnly: 'Nur Admins',
		adminOnlyHint: 'Nur Admins können Mitglieder hinzufügen',
		joinApprovalHint: 'Beitrittsanfragen brauchen eine Admin-Genehmigung',
		open: 'Offen',
		openHint: 'Jede Person kann direkt beitreten',
		inherit: 'Erben',
		inheritHint: 'Wert der übergeordneten Gruppe bzw. der Organisation verwenden'
	},
	on: 'An',
	off: 'Aus',
	rules: {
		editPastEntriesMode: {
			label: 'Eigene Einträge nachträglich ändern',
			description: 'Ob Mitglieder Beginn und Ende eines fertigen Eintrags selbst korrigieren – direkt, per Antrag oder gar nicht.'
		},
		editPauseMode: {
			label: 'Pause selbst ändern',
			description: 'Ob Mitglieder die automatisch abgezogene Pause eines Eintrags überschreiben dürfen.'
		},
		initialOvertimeMode: {
			label: 'Überstunden-Übertrag',
			description: 'Ob Mitglieder ihre Überstunden aus der Zeit vor der Zeiterfassung selbst eintragen.'
		},
		joinPolicy: {
			label: 'Beitreten',
			description: 'Wie neue Personen in die Organisation kommen – zusätzlich zu Einladungen.'
		},
		workScheduleChangeMode: {
			label: 'Eigenes Arbeitszeitmodell ändern',
			description: 'Ob Mitglieder ihre Soll-Stunden (Arbeitszeit-Zeiträume) selbst anlegen und ändern.'
		},
		csvImportMode: {
			label: 'Einträge aus CSV importieren',
			description: 'Ob Mitglieder Zeiteinträge aus einer CSV-Datei hochladen dürfen.'
		},
		absenceSelfServiceMode: {
			label: 'Urlaub & Abwesenheiten eintragen',
			description: 'Ob Mitglieder Urlaub und andere Abwesenheiten direkt eintragen oder beantragen – oder nur Admins sie eintragen.'
		},
		autoPauseEnabled: {
			label: 'Pause automatisch abziehen',
			description: 'Zieht nach den Pausenregeln automatisch Pause von langen Arbeitstagen ab (ArbZG §4).'
		},
		memberTimeEntryVisibility: {
			label: 'Admins sehen Arbeitszeiten',
			description: 'Admins sehen die erfassten Zeiten aller Mitglieder; Mitglieder bekommen einen Hinweis darauf.'
		},
		require2fa: {
			label: 'Zwei-Faktor-Anmeldung verlangen',
			description: 'Jedes Mitglied muss 2FA einrichten, bevor es die App nutzen kann.'
		},
	}
};
