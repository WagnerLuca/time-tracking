/** /inbox: everything that waits for the user in one place. */
export const de = {
	title: 'Anfragen & Hinweise',
	intro: 'Alles, was auf dich wartet: Anträge zum Entscheiden, Einträge zum Korrigieren und Antworten auf deine Anträge.',
	allDone: 'Alles erledigt – nichts wartet auf dich.',
	toDashboard: 'Zum Dashboard',
	approve: {
		title: 'Zu entscheiden',
		empty: 'Keine offenen Anträge von deinem Team.',
		via: 'über {group}',
		accept: 'Annehmen',
		decline: 'Ablehnen',
		accepted: 'Antrag von {name} angenommen.',
		declined: 'Antrag von {name} abgelehnt.',
		failed: 'Antrag konnte nicht beantwortet werden.'
	},
	fix: {
		title: 'Zu korrigieren',
		failure: 'Timer vom {date} wurde nicht gestoppt',
		failureHint: 'Zählt erst, wenn die Endzeit stimmt.',
		correct: 'Endzeit korrigieren'
	},
	notices: {
		title: 'Hinweise',
		open: 'Ansehen',
		done: 'Erledigt',
		doneLabel: 'Hinweis „{title}“ als erledigt markieren',
		failed: 'Hinweis konnte nicht ausgeblendet werden.'
	},
	own: {
		title: 'Meine offenen Anträge',
		waiting: 'Gestellt {time} · wartet auf Entscheidung'
	},
	decisions: {
		title: 'Antworten auf meine Anträge',
		by: '{status} von {name}',
		dismiss: 'Gelesen',
		dismissAll: 'Alle als gelesen markieren',
		failed: 'Konnte nicht als gelesen markiert werden.'
	},
	history: {
		title: 'Entschiedene Anträge',
		intro: 'Die letzten Anträge in {org}, die du entscheiden darfst.'
	}
};

export const en: typeof de = {
	title: 'Requests & notices',
	intro: 'Everything waiting for you: requests to decide, entries to correct and answers to your requests.',
	allDone: 'All done – nothing is waiting for you.',
	toDashboard: 'Go to the dashboard',
	approve: {
		title: 'To decide',
		empty: 'No open requests from your team.',
		via: 'via {group}',
		accept: 'Accept',
		decline: 'Decline',
		accepted: 'Request from {name} accepted.',
		declined: 'Request from {name} declined.',
		failed: 'Could not respond to the request.'
	},
	fix: {
		title: 'To correct',
		failure: 'Timer from {date} was not stopped',
		failureHint: 'It only counts once the end time is right.',
		correct: 'Correct end time'
	},
	notices: {
		title: 'Notices',
		open: 'View',
		done: 'Done',
		doneLabel: 'Mark notice “{title}” as done',
		failed: 'Could not dismiss the notice.'
	},
	own: {
		title: 'My open requests',
		waiting: 'Sent {time} · waiting for a decision'
	},
	decisions: {
		title: 'Answers to my requests',
		by: '{status} by {name}',
		dismiss: 'Mark read',
		dismissAll: 'Mark all as read',
		failed: 'Could not mark as read.'
	},
	history: {
		title: 'Decided requests',
		intro: 'The latest requests in {org} that you may decide.'
	}
};
