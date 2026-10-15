/** Strings for the auth area. Add keys to both objects; `en` is typed against `de`. */
export const de = {
	email: 'E-Mail',
	emailPlaceholder: 'du@beispiel.de',
	password: 'Passwort',
	inviteHint: 'Du wurdest in eine Organisation eingeladen. Melde dich mit der eingeladenen E-Mail-Adresse an oder erstelle damit ein Konto – danach wird die Einladung angenommen.',
	login: {
		pageTitle: 'Anmelden',
		title: 'Anmelden',
		subtitle: 'Willkommen zurück bei der Zeiterfassung',
		submit: 'Anmelden',
		submitting: 'Wird angemeldet…',
		failed: 'Anmeldung fehlgeschlagen. Bitte E-Mail und Passwort prüfen.',
		noAccount: 'Noch kein Konto?',
		register: 'Konto erstellen',
		forgot: 'Passwort vergessen?'
	},
	twoFactor: {
		title: 'Zwei-Faktor-Authentifizierung',
		subtitle: 'Den 6-stelligen Code aus der Authenticator-App eingeben',
		code: 'Bestätigungscode',
		backupHint: 'Alternativ einen Backup-Code verwenden.',
		verify: 'Bestätigen',
		verifying: 'Wird geprüft…',
		failed: 'Ungültiger Code. Bitte erneut versuchen.',
		back: 'Zurück zur Anmeldung'
	},
	forgot: {
		pageTitle: 'Passwort vergessen',
		title: 'Passwort vergessen?',
		subtitle: 'Gib deine E-Mail-Adresse ein. Wir senden dir einen Link zum Zurücksetzen.',
		submit: 'Link senden',
		submitting: 'Wird gesendet…',
		sent: 'Falls ein Konto mit dieser Adresse existiert, haben wir dir einen Link zum Zurücksetzen gesendet. Bitte prüfe dein Postfach.',
		failed: 'Die Anfrage konnte nicht gesendet werden. Bitte später erneut versuchen.',
		back: 'Zurück zur Anmeldung'
	},
	reset: {
		pageTitle: 'Neues Passwort',
		title: 'Neues Passwort festlegen',
		subtitle: 'Wähle ein neues Passwort für dein Konto.',
		newPassword: 'Neues Passwort',
		submit: 'Passwort speichern',
		submitting: 'Wird gespeichert…',
		done: 'Dein Passwort wurde geändert. Du kannst dich jetzt anmelden.',
		failed: 'Das Passwort konnte nicht zurückgesetzt werden. Der Link ist evtl. abgelaufen.',
		missingToken: 'Dieser Link ist unvollständig. Bitte fordere einen neuen an.',
		requestNew: 'Neuen Link anfordern',
		toLogin: 'Zur Anmeldung'
	},
	verify: {
		pageTitle: 'E-Mail bestätigen',
		verifying: 'E-Mail-Adresse wird bestätigt…',
		success: 'Deine E-Mail-Adresse ist bestätigt.',
		failed: 'Die E-Mail-Adresse konnte nicht bestätigt werden. Der Link ist evtl. abgelaufen oder wurde schon benutzt.',
		missingToken: 'Dieser Bestätigungslink ist unvollständig.',
		toApp: 'Zur App',
		toLogin: 'Zur Anmeldung'
	},
	register: {
		pageTitle: 'Registrieren',
		title: 'Konto erstellen',
		subtitle: 'Loslegen mit der Zeiterfassung',
		firstName: 'Vorname',
		firstNamePlaceholder: 'Max',
		lastName: 'Nachname',
		lastNamePlaceholder: 'Mustermann',
		passwordHint: 'Mindestens {min} Zeichen, mit Groß- und Kleinbuchstaben, einer Ziffer und einem Sonderzeichen.',
		confirmPassword: 'Passwort bestätigen',
		mismatch: 'Die Passwörter stimmen nicht überein.',
		submit: 'Konto erstellen',
		submitting: 'Konto wird erstellt…',
		failed: 'Registrierung fehlgeschlagen. Bitte erneut versuchen.',
		haveAccount: 'Schon ein Konto?',
		login: 'Anmelden',
		verifySent: 'Konto erstellt. Wir haben dir eine E-Mail geschickt – bitte bestätige deine Adresse über den Link darin.'
	}
};
