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

export const en: typeof de = {
	requestType: {
		JoinOrganization: 'Join',
		EditPastEntry: 'Edit entry',
		EditPause: 'Edit pause',
		SetInitialOvertime: 'Overtime',
		WorkScheduleChange: 'Schedule',
		CsvImport: 'CSV import',
		RequestAbsence: 'Absence',
		AddManualEntry: 'Add entry'
	},
	requestTypeFull: {
		JoinOrganization: 'Join organization',
		EditPastEntry: 'Edit past time entry',
		EditPause: 'Edit pause duration',
		SetInitialOvertime: 'Set initial overtime',
		WorkScheduleChange: 'Change work schedule',
		CsvImport: 'Import time entries from CSV',
		RequestAbsence: 'Absence / vacation request',
		AddManualEntry: 'Add a forgotten time entry'
	},
	requestStatus: {
		Accepted: 'Accepted',
		Declined: 'Declined',
		Pending: 'Pending',
		Withdrawn: 'Withdrawn'
	},
	requestData: {
		start: 'Start: {value}',
		end: 'End: {value}',
		note: 'Note: {value}',
		pause: 'Pause: {value} min',
		overtime: 'Overtime: {value} h',
		date: 'Date: {value}',
		range: '{count} days: {from} → {to}',
		type: 'Type: {value}',
		customType: 'Custom absence type',
		halfDaySuffix: ' (½ day)'
	},
	absenceType: {
		SickDay: 'Sick day',
		Vacation: 'Vacation',
		Other: 'Other'
	},
	dayType: {
		holiday: 'Holiday',
		sick: 'Sick day',
		vacation: 'Vacation',
		otherAbsence: 'Other absence',
		halfDay: '(half day)'
	},
	timeAgo: {
		justNow: 'just now'
	},
	role: {
		Owner: 'Owner',
		Admin: 'Admin',
		Member: 'Member'
	},
	ruleMode: {
		Disabled: 'Disabled',
		RequiresApproval: 'Requires approval',
		Allowed: 'Allowed'
	},
	joinPolicy: {
		Disabled: 'Admins only',
		RequiresApproval: 'Requires approval',
		Allowed: 'Open'
	},
	options: {
		disabled: 'Off',
		disabledHint: 'Feature is off for members',
		approval: 'Approval',
		approvalHint: 'Members must request; an admin approves',
		allowed: 'Allowed',
		allowedHint: 'Members can do this themselves',
		adminOnly: 'Admins only',
		adminOnlyHint: 'Only admins can add members',
		joinApprovalHint: 'Join requests need admin approval',
		open: 'Open',
		openHint: 'Anyone can join freely',
		inherit: 'Inherit',
		inheritHint: 'Use the parent group’s or the organization’s value'
	},
	on: 'On',
	off: 'Off',
	rules: {
		editPastEntriesMode: {
			label: 'Change own past entries',
			description: 'Whether members correct start and end of a finished entry themselves – directly, by request or not at all.'
		},
		editPauseMode: {
			label: 'Change own pause',
			description: 'Whether members may override the automatically deducted pause of an entry.'
		},
		initialOvertimeMode: {
			label: 'Overtime carry-over',
			description: 'Whether members enter their overtime from before time tracking themselves.'
		},
		joinPolicy: {
			label: 'Joining',
			description: 'How new people get into the organization – in addition to invitations.'
		},
		workScheduleChangeMode: {
			label: 'Change own work schedule',
			description: 'Whether members create and change their target hours (schedule periods) themselves.'
		},
		csvImportMode: {
			label: 'Import entries from CSV',
			description: 'Whether members may upload time entries from a CSV file.'
		},
		absenceSelfServiceMode: {
			label: 'Enter vacation & absences',
			description: 'Whether members enter vacation and other absences directly or request them – or only admins enter them.'
		},
		autoPauseEnabled: {
			label: 'Deduct pause automatically',
			description: 'Deducts pause from long working days by the pause rules (German Working Hours Act §4).'
		},
		memberTimeEntryVisibility: {
			label: 'Admins see working times',
			description: 'Admins see every member’s tracked time; members are told so.'
		},
		require2fa: {
			label: 'Require two-factor sign-in',
			description: 'Every member must set up 2FA before using the app.'
		},
	}
};
