/** Strings for the auth area. Add keys to both objects; `en` is typed against `de`. */
export const de = {
	email: 'E-Mail',
	emailPlaceholder: 'du@beispiel.de',
	password: 'Passwort',
	login: {
		pageTitle: 'Anmelden',
		title: 'Anmelden',
		subtitle: 'Willkommen zurück bei der Zeiterfassung',
		submit: 'Anmelden',
		submitting: 'Wird angemeldet…',
		failed: 'Anmeldung fehlgeschlagen. Bitte E-Mail und Passwort prüfen.',
		noAccount: 'Noch kein Konto?',
		register: 'Konto erstellen'
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
	register: {
		pageTitle: 'Registrieren',
		title: 'Konto erstellen',
		subtitle: 'Loslegen mit der Zeiterfassung',
		firstName: 'Vorname',
		firstNamePlaceholder: 'Max',
		lastName: 'Nachname',
		lastNamePlaceholder: 'Mustermann',
		passwordHint: 'Mindestens {min} Zeichen.',
		confirmPassword: 'Passwort bestätigen',
		mismatch: 'Die Passwörter stimmen nicht überein.',
		submit: 'Konto erstellen',
		submitting: 'Konto wird erstellt…',
		failed: 'Registrierung fehlgeschlagen. Bitte erneut versuchen.',
		haveAccount: 'Schon ein Konto?',
		login: 'Anmelden'
	}
};

export const en: typeof de = {
	email: 'Email',
	emailPlaceholder: 'you@example.com',
	password: 'Password',
	login: {
		pageTitle: 'Sign in',
		title: 'Sign in',
		subtitle: 'Welcome back to Time Tracking',
		submit: 'Sign in',
		submitting: 'Signing in…',
		failed: 'Sign-in failed. Please check your email and password.',
		noAccount: "Don't have an account?",
		register: 'Create one'
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
	register: {
		pageTitle: 'Register',
		title: 'Create account',
		subtitle: 'Get started with Time Tracking',
		firstName: 'First name',
		firstNamePlaceholder: 'Jane',
		lastName: 'Last name',
		lastNamePlaceholder: 'Doe',
		passwordHint: 'At least {min} characters.',
		confirmPassword: 'Confirm password',
		mismatch: 'Passwords do not match.',
		submit: 'Create account',
		submitting: 'Creating account…',
		failed: 'Registration failed. Please try again.',
		haveAccount: 'Already have an account?',
		login: 'Sign in'
	}
};
