/** Strings for the team time overview page. Add keys to both objects; `en` is typed against `de`. */
export const de = {
	pageTitle: 'Team-Zeitübersicht',
	title: 'Team-Zeitübersicht',
	back: 'Zurück zu {org}',
	fallbackOrg: 'Organisation',
	orgFailed: 'Die Organisation konnte nicht geladen werden.',
	noPermission: 'Du darfst die Zeiteinträge nicht sehen. Ein Admin oder eine Gruppeneinstellung muss die Sichtbarkeit freigeben.',
	loadFailed: 'Die Zeitübersicht konnte nicht geladen werden.',
	entriesFailed: 'Die Zeiteinträge konnten nicht geladen werden.',
	prevWeek: 'Vorherige Woche',
	nextWeek: 'Nächste Woche',
	noMembers: 'Keine Mitglieder gefunden.',
	noEntries: 'Keine Einträge in dieser Woche.',
	running: 'Läuft',
	pause: '−{minutes} Min. Pause',
	toFix: '{count} zu korrigieren',
	toFixTitle: 'Timer, die am Ende des Arbeitstags automatisch gestoppt wurden. Sie zählen erst, wenn das Mitglied sie korrigiert.',
	cols: {
		member: 'Mitglied',
		role: 'Rolle',
		target: 'Soll',
		tracked: 'Erfasst',
		net: 'Netto',
		entries: 'Einträge',
		status: 'Status'
	}
};

export const en: typeof de = {
	pageTitle: 'Team time overview',
	title: 'Team time overview',
	back: 'Back to {org}',
	fallbackOrg: 'Organization',
	orgFailed: 'Could not load the organization.',
	noPermission: 'You may not view time entries. An admin or a group setting must grant visibility.',
	loadFailed: 'Could not load the time overview.',
	entriesFailed: 'Could not load the time entries.',
	prevWeek: 'Previous week',
	nextWeek: 'Next week',
	noMembers: 'No members found.',
	noEntries: 'No entries this week.',
	running: 'Running',
	pause: '−{minutes} min pause',
	toFix: '{count} to fix',
	toFixTitle: 'Timers stopped automatically at the end of the business day. Not counted until the member corrects them.',
	cols: {
		member: 'Member',
		role: 'Role',
		target: 'Target',
		tracked: 'Tracked',
		net: 'Net',
		entries: 'Entries',
		status: 'Status'
	}
};
