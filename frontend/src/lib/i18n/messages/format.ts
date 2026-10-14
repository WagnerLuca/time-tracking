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
		Pending: 'Offen'
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
		inheritHint: 'Einstellung der Organisation verwenden'
	},
	rules: {
		editPastEntriesMode: {
			label: 'Vergangene Einträge ändern',
			description: 'Legt fest, ob Mitglieder Beginn und Ende abgeschlossener Zeiteinträge ändern dürfen.'
		},
		editPauseMode: {
			label: 'Pausendauer ändern',
			description: 'Legt fest, ob Mitglieder die automatisch abgezogene Pause überschreiben dürfen.'
		},
		initialOvertimeMode: {
			label: 'Anfangsüberstunden',
			description: 'Legt fest, ob Mitglieder ihren Überstunden-Startsaldo selbst festlegen dürfen.'
		},
		joinPolicy: {
			label: 'Beitrittsregel',
			description: 'Legt fest, wie neue Mitglieder der Organisation beitreten können.'
		},
		workScheduleChangeMode: {
			label: 'Arbeitszeit-Zeiträume',
			description: 'Legt fest, ob Mitglieder eigene Arbeitszeit-Zeiträume anlegen und ändern dürfen.'
		},
		csvImportMode: {
			label: 'CSV-Import',
			description: 'Legt fest, ob Mitglieder Zeiteinträge aus CSV-Dateien importieren dürfen.'
		},
		absenceSelfServiceMode: {
			label: 'Abwesenheiten & Urlaub',
			description: 'Legt fest, ob Mitglieder eigene Abwesenheiten direkt eintragen, beantragen müssen (Genehmigung durch Gruppen- oder Organisations-Admins) oder nur Admins sie eintragen.'
		},
		autoPauseEnabled: {
			label: 'Automatische Pause',
			description: 'Zieht Pausenzeiten anhand konfigurierbarer Regeln automatisch von der erfassten Zeit ab.'
		},
		memberTimeEntryVisibility: {
			label: 'Arbeitszeiten für Admins sichtbar',
			description: 'Wenn aktiv, können Admins die erfassten Arbeitszeiten der Mitglieder sehen. Mitglieder werden darauf hingewiesen.'
		},
		require2fa: {
			label: 'Zwei-Faktor-Authentifizierung verlangen',
			description: 'Wenn aktiv, müssen alle Mitglieder 2FA einrichten, bevor sie die Anwendung nutzen können.'
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
		Pending: 'Pending'
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
		inheritHint: 'Use the organization default'
	},
	rules: {
		editPastEntriesMode: {
			label: 'Edit past entries',
			description: 'Control whether members can edit start/end times of completed time entries.'
		},
		editPauseMode: {
			label: 'Edit pause duration',
			description: 'Control whether members can override auto-deducted break time.'
		},
		initialOvertimeMode: {
			label: 'Initial overtime',
			description: 'Control whether members can set their own initial overtime balance.'
		},
		joinPolicy: {
			label: 'Join policy',
			description: 'Control how new members can join the organization.'
		},
		workScheduleChangeMode: {
			label: 'Schedule periods',
			description: 'Control whether members can create/modify their own schedule periods.'
		},
		csvImportMode: {
			label: 'CSV import',
			description: 'Control whether members can import time entries from CSV files.'
		},
		absenceSelfServiceMode: {
			label: 'Absences & vacation',
			description: 'Control whether members can add their own absences directly, must request approval (group/org admins), or only admins can add them.'
		},
		autoPauseEnabled: {
			label: 'Auto-pause tracking',
			description: 'Automatically deduct break time from tracked hours based on configurable rules.'
		},
		memberTimeEntryVisibility: {
			label: 'Member time visibility',
			description: "When enabled, admins can view members' tracked working hours. Members will see a notification about this."
		},
		require2fa: {
			label: 'Require two-factor authentication',
			description: 'When enabled, all members must set up 2FA before they can use the application.'
		},
	}
};
