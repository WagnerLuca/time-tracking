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
		waiting: 'Gestellt {time} · wartet auf Entscheidung',
		withdraw: 'Zurückziehen',
		withdrawLabel: 'Antrag „{type}“ zurückziehen',
		withdrawing: 'Wird zurückgezogen…',
		confirmTitle: 'Antrag zurückziehen?',
		confirmMessage: 'Der Antrag „{type}“ wird nicht mehr entschieden und erscheint bei dir und den Freigebenden als „zurückgezogen“. Das lässt sich nicht rückgängig machen – du kannst ihn aber neu stellen.',
		withdrawn: 'Antrag zurückgezogen.',
		withdrawFailed: 'Antrag konnte nicht zurückgezogen werden.'
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
