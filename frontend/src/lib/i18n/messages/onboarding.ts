/** Dashboard first-steps checklists. */
export const de = {
	memberTitle: 'Erste Schritte',
	adminTitle: 'Organisation einrichten',
	progress: '{done} von {total} erledigt',
	dismiss: 'Checkliste ausblenden',
	done: 'erledigt',
	steps: {
		schedule: { label: 'Arbeitszeitmodell einrichten', hint: 'Deine Soll-Stunden – ohne sie gibt es keinen Saldo.' },
		timer: { label: 'Timer starten', hint: 'Ein Klick beim Arbeitsbeginn, einer am Ende.' },
		teamCalendar: { label: 'Teamkalender ansehen', hint: 'Wer ist diese Woche abwesend?' },
		invite: { label: 'Mitglieder einladen', hint: 'Per E-Mail – die Person tritt mit einem Klick bei.' },
		rules: { label: 'Regeln & Sichtbarkeit prüfen', hint: 'Was Mitglieder selbst dürfen und über andere sehen.' },
		holidays: { label: 'Feiertage importieren', hint: 'Für dein Bundesland, damit Feiertage nicht als Fehltage zählen.' }
	}
};

export const en: typeof de = {
	memberTitle: 'First steps',
	adminTitle: 'Set up your organization',
	progress: '{done} of {total} done',
	dismiss: 'Hide checklist',
	done: 'done',
	steps: {
		schedule: { label: 'Set up your work schedule', hint: 'Your target hours – without them there is no balance.' },
		timer: { label: 'Start the timer', hint: 'One click when you start work, one when you stop.' },
		teamCalendar: { label: 'Look at the team calendar', hint: 'Who is away this week?' },
		invite: { label: 'Invite members', hint: 'By email – they join with one click.' },
		rules: { label: 'Check rules & visibility', hint: 'What members may do themselves and see about others.' },
		holidays: { label: 'Import holidays', hint: 'For your region, so holidays never count as missing days.' }
	}
};
