/** Root layout: navigation, notifications, banners. */
export const de = {
	brand: 'Zeiterfassung',
	home: 'Startseite',
	timer: 'Timer',
	history: 'Verlauf',
	export: 'Export',
	organization: 'Organisation',
	settings: 'Einstellungen',
	signOut: 'Abmelden',
	menu: 'Menü',
	openMenu: 'Menü öffnen',
	userMenu: 'Benutzermenü',
	language: 'Sprache',
	switchLanguage: 'Sprache wechseln',
	timerRunning: 'Timer läuft seit {elapsed} – zum Timer',
	org: {
		switch: 'Organisation wechseln',
		current: 'Aktuelle Organisation: {name}',
		none: 'Keine Organisation',
		manage: 'Organisationen verwalten',
		create: 'Organisation erstellen oder beitreten'
	},
	notifications: {
		title: 'Benachrichtigungen',
		open: 'Benachrichtigungen öffnen',
		openCount: 'Benachrichtigungen öffnen ({count} neu)',
		myUpdates: 'Meine Anträge',
		failures: 'Automatisch gestoppte Timer',
		failureTitle: 'Timer automatisch gestoppt',
		failureStarted: 'Gestartet {date} – Endzeit korrigieren',
		failureCorrect: 'Endzeit korrigieren',
		dismissAll: 'Alle ausblenden',
		pending: 'Offene Anträge',
		empty: 'Keine Benachrichtigungen',
		by: 'von {name}',
		via: 'über {group}',
		status: 'Status',
		user: 'Person',
		organization: 'Organisation',
		routedVia: 'Weitergeleitet über Gruppe',
		created: 'Gestellt',
		responded: 'Beantwortet',
		respondedBy: 'Beantwortet von',
		message: 'Nachricht',
		details: 'Details',
		accept: 'Annehmen',
		decline: 'Ablehnen',
		dismiss: 'Benachrichtigung ausblenden',
		accepted: 'Antrag angenommen.',
		declined: 'Antrag abgelehnt.',
		respondFailed: 'Antrag konnte nicht beantwortet werden.',
		dismissFailed: 'Benachrichtigung konnte nicht ausgeblendet werden.',
		loadFailed: 'Benachrichtigungen konnten nicht geladen werden.'
	},
	twoFactor: {
		title: 'Zwei-Faktor-Authentifizierung erforderlich',
		text: 'Diese Organisation verlangt Zwei-Faktor-Authentifizierung. Zum Fortfahren 2FA in den Kontoeinstellungen einrichten.',
		goToSettings: 'Zu den Einstellungen'
	},
	failure: {
		title: {
			one: '1 Timer wurde nicht gestoppt',
			other: '{count} Timer wurden nicht gestoppt'
		},
		text: {
			one: 'und am Ende des Arbeitstags automatisch beendet. Er zählt erst, wenn die Endzeit korrigiert ist.',
			other: 'und am Ende des Arbeitstags automatisch beendet. Sie zählen erst, wenn die Endzeiten korrigiert sind.'
		},
		correct: 'Jetzt korrigieren'
	}
};

export const en: typeof de = {
	brand: 'Time Tracking',
	home: 'Home',
	timer: 'Timer',
	history: 'History',
	export: 'Export',
	organization: 'Organization',
	settings: 'Settings',
	signOut: 'Sign out',
	menu: 'Menu',
	openMenu: 'Open menu',
	userMenu: 'User menu',
	language: 'Language',
	switchLanguage: 'Switch language',
	timerRunning: 'Timer running for {elapsed} – open timer',
	org: {
		switch: 'Switch organization',
		current: 'Current organization: {name}',
		none: 'No organization',
		manage: 'Manage organizations',
		create: 'Create or join an organization'
	},
	notifications: {
		title: 'Notifications',
		open: 'Open notifications',
		openCount: 'Open notifications ({count} new)',
		myUpdates: 'My request updates',
		failures: 'Auto-stopped timers',
		failureTitle: 'Timer stopped automatically',
		failureStarted: 'Started {date} – correct its end time',
		failureCorrect: 'Correct its end time',
		dismissAll: 'Dismiss all',
		pending: 'Pending requests',
		empty: 'No notifications',
		by: 'by {name}',
		via: 'via {group}',
		status: 'Status',
		user: 'User',
		organization: 'Organization',
		routedVia: 'Routed via group',
		created: 'Created',
		responded: 'Responded',
		respondedBy: 'Responded by',
		message: 'Message',
		details: 'Request details',
		accept: 'Accept',
		decline: 'Decline',
		dismiss: 'Dismiss notification',
		accepted: 'Request accepted.',
		declined: 'Request declined.',
		respondFailed: 'Could not respond to the request.',
		dismissFailed: 'Could not dismiss the notification.',
		loadFailed: 'Could not load notifications.'
	},
	twoFactor: {
		title: 'Two-factor authentication required',
		text: 'This organization requires two-factor authentication. Set up 2FA in your account settings to continue.',
		goToSettings: 'Go to settings'
	},
	failure: {
		title: {
			one: '1 timer was not stopped',
			other: '{count} timers were not stopped'
		},
		text: {
			one: 'and ended automatically at the end of the business day. It does not count toward your hours until you correct the end time.',
			other: 'and ended automatically at the end of the business day. They do not count toward your hours until you correct the end times.'
		},
		correct: 'Correct now'
	}
};
