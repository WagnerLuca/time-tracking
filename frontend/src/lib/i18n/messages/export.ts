/** Strings for the export area. Add keys to both objects; `en` is typed against `de`. */
export const de = {
	title: 'Export & Import',
	tabs: { export: 'Export', import: 'Import', report: 'PDF-Bericht' },
	tabsLabel: 'Bereich',
	loadOrgFailed: 'Die Organisation konnte nicht geladen werden.',
	noOrg: {
		title: 'Keine Organisation ausgewählt',
		select: 'Organisation auswählen',
		createOrJoin: 'Organisation erstellen oder beitreten',
		importText: 'Zeiteinträge werden immer in eine Organisation importiert. Wähle zuerst eine aus.',
		reportText: 'PDF-Berichte werden pro Organisation erstellt. Wähle zuerst eine aus.',
		exportHint: 'Keine Organisation ausgewählt – exportiert werden nur deine eigenen Zeiteinträge aus allen Organisationen. Für die Tagesübersicht eine Organisation auswählen.'
	},
	member: {
		label: 'Exportieren für',
		myself: 'Mich selbst',
		scopeHint:
			'Beschränkt auf {org}. Dafür braucht es die Export-Berechtigung für dieses Mitglied – Admins der Organisation haben sie immer, Gruppen-Admins nur, wenn in einer gemeinsamen Gruppe der Datenexport erlaubt ist.'
	},
	type: {
		label: 'Exportart',
		entries: 'Einzelne Zeiteinträge',
		daily: 'Tagesübersicht'
	},
	filters: {
		title: 'Filter',
		scope: 'Organisation',
		scopeCurrent: 'Aktuelle Organisation ({name})',
		scopeAll: 'Alle Organisationen',
		dailyHint: 'Die Tagesübersicht verwendet immer die aktuelle Organisation.',
		memberHint: 'Der Export für ein anderes Mitglied verwendet immer die aktuelle Organisation.',
		range: 'Zeitraum',
		ranges: {
			week: 'Diese Woche',
			month: 'Dieser Monat',
			year: 'Dieses Jahr',
			all: 'Gesamter Zeitraum',
			custom: 'Eigener Zeitraum'
		},
		from: 'Von',
		to: 'Bis',
		rangeInvalid: 'Das Startdatum darf nicht nach dem Enddatum liegen.'
	},
	columns: {
		title: 'Spalten',
		selectAll: 'Alle auswählen',
		deselectAll: 'Alle abwählen',
		none: 'Mindestens eine Spalte auswählen.',
		Date: 'Datum',
		DayOfWeek: 'Wochentag',
		StartTime: 'Startzeit',
		EndTime: 'Endzeit',
		Duration: 'Dauer (h)',
		Pause: 'Pause (min)',
		NetDuration: 'Nettodauer (h)',
		Description: 'Beschreibung',
		Organization: 'Organisation',
		TargetHours: 'Soll (h)',
		WorkedHours: 'Gearbeitet (h)',
		NetWorkedHours: 'Netto gearbeitet (h)',
		Overtime: 'Überstunden',
		CumulativeOvertime: 'Überstunden kumuliert',
		Status: 'Status',
		HolidayName: 'Feiertag',
		AbsenceType: 'Abwesenheitsart',
		AbsenceNote: 'Abwesenheitsnotiz',
		Entries: 'Zeiteinträge'
	},
	blocked: {
		member: 'Der Export für ein anderes Mitglied braucht eine Organisation.',
		daily: 'Die Tagesübersicht braucht eine Organisation.'
	},
	exporting: 'Wird exportiert…',
	download: 'CSV herunterladen',
	downloaded: 'CSV-Datei heruntergeladen.',
	failed: 'Der Export ist fehlgeschlagen.',
	payroll: {
		title: 'Lohnabrechnung (alle Mitglieder)',
		text: 'Eine CSV-Datei für die Lohnbuchhaltung: pro aktivem Mitglied von {org} Soll, Ist, Überstunden, Saldo, Abwesenheiten und offene Korrekturen eines Monats. Deutsches Format (Semikolon, Dezimalkomma).',
		month: 'Monat der Lohnabrechnung',
		year: 'Jahr der Lohnabrechnung',
		failed: 'Die Lohnabrechnung konnte nicht exportiert werden.'
	},
	import: {
		disabled: 'Der CSV-Import ist für dich in dieser Organisation deaktiviert.',
		title: 'CSV-Datei importieren',
		description: 'Lade eine CSV-Datei mit Zeiteinträgen hoch. Pflichtspalten: {required}. Optionale Spalten: {optional}.',
		needsApproval: 'Der CSV-Import muss von einem Admin genehmigt werden. Datei zuerst prüfen, dann die Genehmigung für die ausgewählten Zeilen beantragen.',
		file: 'CSV-Datei',
		choose: '.csv-Datei auswählen…',
		wrongType: 'Bitte eine .csv-Datei auswählen.',
		empty: 'Die Datei ist leer.',
		reading: 'Wird gelesen…',
		preview: 'Datei prüfen',
		previewFailed: 'Die CSV-Datei konnte nicht gelesen werden.',
		previewTitle: { one: 'Vorschau ({count} Zeile)', other: 'Vorschau ({count} Zeilen)' },
		duplicates: { one: '{count} Duplikat', other: '{count} Duplikate' },
		selected: '{count} ausgewählt',
		all: 'Alle',
		none: 'Keine',
		selectAllRows: 'Alle Zeilen auswählen',
		selectRow: 'Zeile {row} auswählen',
		col: {
			date: 'Datum',
			start: 'Start',
			end: 'Ende',
			pause: 'Pause',
			description: 'Beschreibung',
			status: 'Status'
		},
		minutes: '{value} min',
		status: { duplicate: 'Duplikat', error: 'Fehler', ok: 'OK' },
		importing: 'Wird importiert…',
		importCount: { one: '{count} Zeiteintrag importieren', other: '{count} Zeiteinträge importieren' },
		imported: { one: '{count} Zeiteintrag importiert.', other: '{count} Zeiteinträge importiert.' },
		skipped: '{count} übersprungen.',
		failed: 'Der Import ist fehlgeschlagen.',
		requestApproval: 'Import-Genehmigung beantragen',
		approvalHint: 'Ein Admin muss den Import genehmigen, bevor die Zeiteinträge übernommen werden.'
	},
	request: {
		title: 'CSV-Import-Genehmigung beantragen',
		text: { one: 'Diese Organisation verlangt eine Genehmigung für CSV-Importe. Antrag für {count} Zeiteintrag stellen.', other: 'Diese Organisation verlangt eine Genehmigung für CSV-Importe. Antrag für {count} Zeiteinträge stellen.' },
		message: 'Nachricht',
		placeholder: 'Notiz für den Admin…',
		defaultMessage: 'Antrag auf Import von {count} Zeiteinträgen aus CSV ({total} gelesen)',
		submit: 'Antrag stellen',
		submitted: 'Import-Antrag gestellt. Ein Admin prüft ihn.',
		failed: 'Der Antrag konnte nicht gestellt werden.'
	},
	report: {
		monthlyTitle: 'Monatsbericht',
		monthlyText: 'Arbeitszeit, Soll, Überstunden und Status Tag für Tag für einen Kalendermonat.',
		yearlyTitle: 'Jahresbericht',
		yearlyText: 'Monatsübersicht für ein Kalenderjahr mit laufendem kumuliertem Überstunden-Saldo.',
		month: 'Monat des Berichts',
		year: 'Jahr des Berichts',
		generating: 'Wird erstellt…',
		download: 'PDF herunterladen',
		downloaded: 'PDF-Bericht heruntergeladen.',
		monthlyFailed: 'Der Monatsbericht konnte nicht erstellt werden.',
		yearlyFailed: 'Der Jahresbericht konnte nicht erstellt werden.'
	}
};

export const en: typeof de = {
	title: 'Export & import',
	tabs: { export: 'Export', import: 'Import', report: 'PDF report' },
	tabsLabel: 'Section',
	loadOrgFailed: 'Could not load the organization.',
	noOrg: {
		title: 'No organization selected',
		select: 'Select organization',
		createOrJoin: 'Create or join an organization',
		importText: 'Time entries are always imported into an organization. Select one first.',
		reportText: 'PDF reports are generated per organization. Select one first.',
		exportHint: 'No organization selected – only your own time entries across all organizations can be exported. Select an organization for the daily summary.'
	},
	member: {
		label: 'Exporting for',
		myself: 'Myself',
		scopeHint:
			'Scoped to {org}. Requires export permission for this member – organization admins always have it, group admins only if data export is enabled for a shared group.'
	},
	type: {
		label: 'Export type',
		entries: 'Individual time entries',
		daily: 'Daily summary'
	},
	filters: {
		title: 'Filters',
		scope: 'Organization',
		scopeCurrent: 'Current organization ({name})',
		scopeAll: 'All organizations',
		dailyHint: 'The daily summary always uses the current organization.',
		memberHint: 'Exporting for another member always uses the current organization.',
		range: 'Time range',
		ranges: {
			week: 'This week',
			month: 'This month',
			year: 'This year',
			all: 'All time',
			custom: 'Custom range'
		},
		from: 'From',
		to: 'To',
		rangeInvalid: 'The start date must not be after the end date.'
	},
	columns: {
		title: 'Columns',
		selectAll: 'Select all',
		deselectAll: 'Deselect all',
		none: 'Select at least one column.',
		Date: 'Date',
		DayOfWeek: 'Day of week',
		StartTime: 'Start time',
		EndTime: 'End time',
		Duration: 'Duration (h)',
		Pause: 'Pause (min)',
		NetDuration: 'Net duration (h)',
		Description: 'Description',
		Organization: 'Organization',
		TargetHours: 'Target (h)',
		WorkedHours: 'Worked (h)',
		NetWorkedHours: 'Net worked (h)',
		Overtime: 'Overtime',
		CumulativeOvertime: 'Cumulative overtime',
		Status: 'Status',
		HolidayName: 'Holiday',
		AbsenceType: 'Absence type',
		AbsenceNote: 'Absence note',
		Entries: 'Time entries'
	},
	blocked: {
		member: 'Exporting for another member requires an organization.',
		daily: 'The daily summary requires an organization.'
	},
	exporting: 'Exporting…',
	download: 'Download CSV',
	downloaded: 'CSV file downloaded.',
	failed: 'Export failed.',
	payroll: {
		title: 'Payroll (all members)',
		text: 'One CSV file for payroll: target, worked, overtime, balance, absences and open corrections of a month for every active member of {org}. German format (semicolon, decimal comma).',
		month: 'Payroll month',
		year: 'Payroll year',
		failed: 'The payroll export failed.'
	},
	import: {
		disabled: 'CSV import is disabled for you in this organization.',
		title: 'Import CSV file',
		description: 'Upload a CSV file with time entries. Required columns: {required}. Optional columns: {optional}.',
		needsApproval: 'CSV import requires admin approval. Preview your file first, then request approval for the selected rows.',
		file: 'CSV file',
		choose: 'Choose a .csv file…',
		wrongType: 'Please choose a .csv file.',
		empty: 'The file is empty.',
		reading: 'Reading…',
		preview: 'Preview file',
		previewFailed: 'Could not read the CSV file.',
		previewTitle: { one: 'Preview ({count} row)', other: 'Preview ({count} rows)' },
		duplicates: { one: '{count} duplicate', other: '{count} duplicates' },
		selected: '{count} selected',
		all: 'All',
		none: 'None',
		selectAllRows: 'Select all rows',
		selectRow: 'Select row {row}',
		col: {
			date: 'Date',
			start: 'Start',
			end: 'End',
			pause: 'Pause',
			description: 'Description',
			status: 'Status'
		},
		minutes: '{value} min',
		status: { duplicate: 'Duplicate', error: 'Error', ok: 'OK' },
		importing: 'Importing…',
		importCount: { one: 'Import {count} time entry', other: 'Import {count} time entries' },
		imported: { one: 'Imported {count} time entry.', other: 'Imported {count} time entries.' },
		skipped: 'Skipped {count}.',
		failed: 'Import failed.',
		requestApproval: 'Request import approval',
		approvalHint: 'An admin must approve before the time entries are imported.'
	},
	request: {
		title: 'Request CSV import approval',
		text: { one: 'This organization requires approval for CSV imports. Submit a request to import {count} time entry.', other: 'This organization requires approval for CSV imports. Submit a request to import {count} time entries.' },
		message: 'Message',
		placeholder: 'Add a note for the admin…',
		defaultMessage: 'Request to import {count} time entries from CSV ({total} parsed)',
		submit: 'Submit request',
		submitted: 'Import request submitted. An admin will review it.',
		failed: 'Could not submit the request.'
	},
	report: {
		monthlyTitle: 'Monthly report',
		monthlyText: 'Day-by-day working hours, target, overtime and status for one calendar month.',
		yearlyTitle: 'Yearly report',
		yearlyText: 'Month-by-month summary for one calendar year, with the running cumulative overtime balance.',
		month: 'Report month',
		year: 'Report year',
		generating: 'Generating…',
		download: 'Download PDF',
		downloaded: 'PDF report downloaded.',
		monthlyFailed: 'Could not generate the monthly report.',
		yearlyFailed: 'Could not generate the yearly report.'
	}
};
