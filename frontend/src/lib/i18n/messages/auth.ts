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

export const en: typeof de = {
	email: 'Email',
	emailPlaceholder: 'you@example.com',
	password: 'Password',
	inviteHint: 'You were invited to an organization. Sign in with the invited email address or create an account with it – the invitation is accepted afterwards.',
	login: {
		pageTitle: 'Sign in',
		title: 'Sign in',
		subtitle: 'Welcome back to Time Tracking',
		submit: 'Sign in',
		submitting: 'Signing in…',
		failed: 'Sign-in failed. Please check your email and password.',
		noAccount: "Don't have an account?",
		register: 'Create one',
		forgot: 'Forgot password?'
	},
	twoFactor: {
		title: 'Two-factor authentication',
		subtitle: 'Enter the 6-digit code from your authenticator app',
		code: 'Verification code',
		backupHint: 'You can also use a backup code.',
		verify: 'Verify',
		verifying: 'Verifying…',
		failed: 'Invalid verification code. Please try again.',
		back: 'Back to sign in'
	},
	forgot: {
		pageTitle: 'Forgot password',
		title: 'Forgot your password?',
		subtitle: "Enter your email address and we'll send you a reset link.",
		submit: 'Send link',
		submitting: 'Sending…',
		sent: 'If an account exists for this address, we have sent you a reset link. Please check your inbox.',
		failed: 'The request could not be sent. Please try again later.',
		back: 'Back to sign in'
	},
	reset: {
		pageTitle: 'New password',
		title: 'Set a new password',
		subtitle: 'Choose a new password for your account.',
		newPassword: 'New password',
		submit: 'Save password',
		submitting: 'Saving…',
		done: 'Your password has been changed. You can sign in now.',
		failed: 'The password could not be reset. The link may have expired.',
		missingToken: 'This link is incomplete. Please request a new one.',
		requestNew: 'Request a new link',
		toLogin: 'Go to sign in'
	},
	verify: {
		pageTitle: 'Confirm email',
		verifying: 'Confirming your email address…',
		success: 'Your email address is confirmed.',
		failed: 'The email address could not be confirmed. The link may have expired or was already used.',
		missingToken: 'This confirmation link is incomplete.',
		toApp: 'Go to the app',
		toLogin: 'Go to sign in'
	},
	register: {
		pageTitle: 'Register',
		title: 'Create account',
		subtitle: 'Get started with Time Tracking',
		firstName: 'First name',
		firstNamePlaceholder: 'Jane',
		lastName: 'Last name',
		lastNamePlaceholder: 'Doe',
		passwordHint: 'At least {min} characters, with upper- and lowercase letters, a digit and a special character.',
		confirmPassword: 'Confirm password',
		mismatch: 'Passwords do not match.',
		submit: 'Create account',
		submitting: 'Creating account…',
		failed: 'Registration failed. Please try again.',
		haveAccount: 'Already have an account?',
		login: 'Sign in',
		verifySent: 'Account created. We have sent you an email – please confirm your address using the link in it.'
	}
};
