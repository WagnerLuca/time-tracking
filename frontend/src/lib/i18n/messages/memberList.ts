/** Strings for the member list page. Add keys to both objects; `en` is typed against `de`. */
export const de = {
	pageTitle: 'Mitglieder',
	notFound: 'Organisation nicht gefunden.',
	loadFailed: 'Die Organisation konnte nicht geladen werden.',
	weekFailed: 'Die Stunden dieser Woche konnten nicht geladen werden.',
	introAdmin: 'Öffne ein Mitglied für Details, Arbeitszeitmodell und Abwesenheiten.',
	introMember: 'Öffne ein Mitglied, um sein Profil zu sehen.',
	heading: 'Mitglieder ({count})',
	headingFiltered: 'Mitglieder ({count} von {total})',
	filterLabel: 'Nach Gruppe filtern',
	allGroups: 'Alle Gruppen',
	noGroup: 'Ohne Gruppe',
	you: 'Du',
	vacationTitle: 'Urlaub: {used} von {total} Tagen verbraucht',
	vacationLeft: { one: '{days} Urlaubstag übrig', other: '{days} Urlaubstage übrig' },
	trackedTitle: 'Diese Woche erfasst',
	thisWeek: '{hours} diese Woche',
	entries: { one: '{count} Eintrag', other: '{count} Einträge' },
	weeklyTargetTitle: 'Wochen-Soll'
};

export const en: typeof de = {
	pageTitle: 'Members',
	notFound: 'Organization not found.',
	loadFailed: 'Could not load the organization.',
	weekFailed: "Could not load this week's hours.",
	introAdmin: 'Open a member for details, schedule editing and absence history.',
	introMember: 'Open a member to see their profile.',
	heading: 'Members ({count})',
	headingFiltered: 'Members ({count} of {total})',
	filterLabel: 'Filter by group',
	allGroups: 'All groups',
	noGroup: 'No group',
	you: 'You',
	vacationTitle: 'Vacation: {used} of {total} days used',
	vacationLeft: { one: '{days} vacation day left', other: '{days} vacation days left' },
	trackedTitle: 'Tracked this week',
	thisWeek: '{hours} this week',
	entries: { one: '{count} entry', other: '{count} entries' },
	weeklyTargetTitle: 'Weekly target'
};
