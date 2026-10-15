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
