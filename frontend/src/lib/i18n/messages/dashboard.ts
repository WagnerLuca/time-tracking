/** Strings for the dashboard (/). `en` is typed against `de`. */
export const de = {
	pageTitle: 'Übersicht',
	greeting: 'Hallo, {name}',
	timer: 'Timer',
	running: 'Timer läuft',
	notRunning: 'Kein Timer aktiv',
	forOrg: 'für {org}',
	startsFor: 'Startet für {org}',
	startsWithoutOrg: 'Startet ohne Organisation',
	noteAndWeek: 'Notiz & Woche',
	noOrg: {
		title: 'Noch keine Organisation',
		text: 'Soll, Überstunden, Abwesenheiten und Berichte gibt es innerhalb einer Organisation – eine eigene erstellen oder einer bestehenden beitreten.',
		create: 'Organisation erstellen',
		join: 'Organisation beitreten'
	},
	selectOrg: 'Oben im Menü eine Organisation auswählen, um Soll und Saldo zu sehen.',
	noSchedule: {
		title: 'Kein Arbeitszeitmodell hinterlegt.',
		text: 'Soll und Überstunden brauchen ein Arbeitszeitmodell mit deinen Wochenstunden.',
		action: 'Arbeitszeitmodell einrichten'
	},
	statsFailed: 'Statistiken konnten nicht geladen werden.',
	today: 'Heute',
	todayProgress: 'Fortschritt heute',
	weekBalance: 'Saldo Woche',
	weekOfTarget: '{worked} von {target} bisher',
	workedThisWeek: 'diese Woche gearbeitet',
	cumulativeBalance: 'Saldo gesamt',
	since: 'Seit {date}',
	initialOvertime: 'Übernommene Überstunden',
	needsSchedule: 'Braucht ein Arbeitszeitmodell',
	thisWeek: 'Diese Woche',
	openTimer: 'Timer öffnen',
	openHistory: 'Verlauf öffnen',
	total: 'Gesamt: {value}',
	weekRange: '{from}.–{to}.'
};

export const en: typeof de = {
	pageTitle: 'Dashboard',
	greeting: 'Hello, {name}',
	timer: 'Timer',
	running: 'Timer running',
	notRunning: 'No timer running',
	forOrg: 'for {org}',
	startsFor: 'Starts for {org}',
	startsWithoutOrg: 'Starts without organization',
	noteAndWeek: 'Note & week',
	noOrg: {
		title: 'No organization yet',
		text: 'Targets, overtime, absences and reports work inside an organization – create your own or join an existing one.',
		create: 'Create organization',
		join: 'Join organization'
	},
	selectOrg: 'Select an organization in the menu above to see targets and balances.',
	noSchedule: {
		title: 'No work schedule set.',
		text: 'Targets and overtime need a work schedule with your weekly hours.',
		action: 'Set up work schedule'
	},
	statsFailed: 'Could not load your statistics.',
	today: 'Today',
	todayProgress: 'Today’s progress',
	weekBalance: 'Week balance',
	weekOfTarget: '{worked} of {target} so far',
	workedThisWeek: 'worked this week',
	cumulativeBalance: 'Cumulative balance',
	since: 'Since {date}',
	initialOvertime: 'Initial overtime',
	needsSchedule: 'Needs a work schedule',
	thisWeek: 'This week',
	openTimer: 'Open timer',
	openHistory: 'Open history',
	total: 'Total: {value}',
	weekRange: '{from}–{to}'
};
