/**
 * Translations of the backend's stable error codes (ProblemDetails `code`, see
 * backend `Services/Common/ErrorCodes.cs`). `extractErrorMessage` looks a code up
 * here and falls back to the server's English `detail` for unknown codes.
 * Codes whose server message has variable parts ({orgs}, {feature}, {reason} …)
 * get a generic sentence here — the detail with the specifics is still logged server-side.
 */
export const de = {
	common: {
		forbidden: 'Dafür fehlt dir die Berechtigung.',
		unauthorized: 'Bitte melde dich an.',
		badRequest: 'Die Anfrage ist ungültig.',
		notFound: 'Nicht gefunden.',
		conflict: 'Das kollidiert mit vorhandenen Daten.',
		unexpected: 'Ein unerwarteter Fehler ist aufgetreten.'
	},
	org: {
		notFound: 'Organisation nicht gefunden.',
		notMember: 'Du bist kein Mitglied dieser Organisation.',
		userNotMember: 'Diese Person ist kein Mitglied der Organisation.',
		memberNotFound: 'Mitglied nicht gefunden.',
		twoFactorRequired: 'Diese Organisation verlangt die Zwei-Faktor-Authentifizierung. Richte sie in den Einstellungen ein, um fortzufahren.',
		adminRequired: 'Das dürfen nur Admins der Organisation.',
		ownerRequired: 'Das darf nur der Inhaber der Organisation.',
		slugRequired: 'Die Organisation fehlt.',
		featureDisabled: 'Diese Funktion ist in der Organisation deaktiviert.',
		featureRequiresApproval: 'Das muss in dieser Organisation genehmigt werden. Bitte stelle eine Anfrage.',
		slugTaken: 'Diese Kurzbezeichnung ist bereits vergeben.',
		ownerRoleFixed: 'Die Rolle des Inhabers lässt sich nicht ändern. Übertrage zuerst die Inhaberschaft.',
		ownerCannotLeave: 'Der Inhaber kann die Organisation nicht verlassen. Lösche sie oder übertrage die Inhaberschaft.',
		enableOwnTwoFactorFirst: 'Aktiviere zuerst die Zwei-Faktor-Authentifizierung für dein eigenes Konto.',
		unknownTimeZone: 'Unbekannte Zeitzone.',
		coreHoursOrder: 'Die Kernarbeitszeit muss eine gültige Start- und Endzeit haben, der Beginn vor dem Ende.'
	},
	user: {
		notFound: 'Benutzer nicht gefunden.'
	},
	auth: {
		invalidCredentials: 'E-Mail-Adresse oder Passwort ist falsch.',
		accountDeactivated: 'Dieses Konto wurde deaktiviert.',
		sessionEnded: 'Deine Sitzung ist beendet. Bitte melde dich erneut an.',
		invalidRefreshToken: 'Deine Sitzung ist abgelaufen. Bitte melde dich erneut an.',
		emailInUse: 'Diese E-Mail-Adresse wird bereits verwendet.',
		wrongCurrentPassword: 'Das aktuelle Passwort ist falsch.',
		wrongPassword: 'Das Passwort ist falsch.',
		currentPasswordRequired: 'Gib dein aktuelles Passwort ein, um die E-Mail-Adresse zu ändern.',
		passwordTooShort: 'Das Passwort muss mindestens 8 Zeichen lang sein.',
		passwordNeedsUppercase: 'Das Passwort braucht mindestens einen Großbuchstaben.',
		passwordNeedsLowercase: 'Das Passwort braucht mindestens einen Kleinbuchstaben.',
		passwordNeedsDigit: 'Das Passwort braucht mindestens eine Ziffer.',
		passwordNeedsSpecial: 'Das Passwort braucht mindestens ein Sonderzeichen.',
		mailCooldown: 'Es wurde gerade erst eine E-Mail gesendet. Bitte warte ein paar Minuten.',
		invalidLink: 'Dieser Link ist ungültig oder abgelaufen.',
		emailAlreadyConfirmed: 'Deine E-Mail-Adresse ist bereits bestätigt.',
		twoFactorTokenExpired: 'Die Anmeldung ist abgelaufen. Bitte melde dich erneut an.',
		twoFactorLocked: 'Zu viele falsche Codes. Versuche es in 15 Minuten erneut.',
		twoFactorInvalidCode: 'Der Code ist ungültig.',
		twoFactorAlreadyEnabled: 'Die Zwei-Faktor-Authentifizierung ist bereits aktiv.',
		twoFactorSetupRequired: 'Starte zuerst die Einrichtung der Zwei-Faktor-Authentifizierung.',
		twoFactorNotEnabled: 'Die Zwei-Faktor-Authentifizierung ist nicht aktiv.'
	},
	account: {
		ownsOrganizations: 'Du bist noch Inhaber von Organisationen. Übertrage oder lösche sie zuerst.',
		twoFactorRequiredByOrgs: 'Organisationen, in denen du Mitglied bist, verlangen die Zwei-Faktor-Authentifizierung. Verlasse sie zuerst oder bitte einen Admin, die Pflicht aufzuheben.'
	},
	entry: {
		startTooEarly: 'Die Startzeit muss im Jahr 2000 oder später liegen.',
		startInFuture: 'Die Startzeit darf nicht in der Zukunft liegen.',
		endBeforeStart: 'Das Ende muss nach dem Start liegen.',
		tooLong: 'Ein Eintrag darf höchstens 24 Stunden lang sein.',
		notFound: 'Eintrag nicht gefunden.',
		running: 'Ein laufender Eintrag kann nicht bearbeitet werden. Stoppe ihn zuerst.',
		editDisabled: 'Das Bearbeiten vergangener Einträge ist in dieser Organisation deaktiviert.',
		editRequiresApproval: 'Das Bearbeiten vergangener Einträge muss genehmigt werden. Bitte stelle eine Anfrage.',
		overlap: 'Der Eintrag überschneidet sich mit einem bestehenden Eintrag.',
		manualInFuture: 'Ein nachgetragener Eintrag muss in der Vergangenheit liegen.',
		addDisabled: 'Das Nachtragen von Einträgen ist in dieser Organisation deaktiviert.'
	},
	timer: {
		alreadyRunning: 'Es läuft bereits ein Timer.',
		notRunning: 'Es läuft kein Timer.'
	},
	range: {
		tooEarly: 'Daten müssen im Jahr 2000 oder später liegen.',
		endBeforeStart: 'Das Enddatum darf nicht vor dem Startdatum liegen.',
		tooLong: 'Ein Zeitraum darf höchstens zwei Jahre umfassen.'
	},
	report: {
		invalidMonth: 'Der Monat muss zwischen 1 und 12 liegen.'
	},
	export: {
		forbidden: 'Du darfst die Daten dieses Mitglieds nicht exportieren.',
		orgRequired: 'Für diesen Export muss eine Organisation gewählt sein.',
		payrollAdminOnly: 'Nur Admins können die Lohnabrechnung exportieren.'
	},
	import: {
		disabled: 'Der CSV-Import ist in dieser Organisation deaktiviert.',
		requiresApproval: 'Der CSV-Import muss genehmigt werden. Bitte stelle eine Anfrage.',
		empty: 'Die CSV-Datei braucht eine Kopfzeile und mindestens eine Datenzeile.',
		missingColumns: 'Die CSV-Datei braucht die Spalten Datum, Start und Ende.',
		tooManyRows: 'Ein Import darf höchstens 1000 Zeilen enthalten.'
	},
	request: {
		duplicate: 'Eine gleiche Anfrage ist bereits offen.',
		alreadyResponded: 'Diese Anfrage wurde bereits beantwortet.',
		notFound: 'Anfrage nicht gefunden.',
		cannotRespond: 'Du darfst diese Anfrage nicht beantworten.',
		cannotApply: 'Die Anfrage lässt sich nicht mehr anwenden.',
		alreadyMember: 'Du bist bereits Mitglied dieser Organisation.',
		joinClosed: 'Diese Organisation nimmt keine Beitrittsanfragen an. Bitte einen Admin um eine Einladung.',
		featureDisabled: 'Diese Funktion ist in der Organisation deaktiviert.',
		notNeeded: 'Das kannst du direkt erledigen, ohne Anfrage.'
	},
	absence: {
		notFound: 'Abwesenheit nicht gefunden.',
		adminOwned: 'Diese Abwesenheit wurde von einem Admin eingetragen oder genehmigt. Bitte einen Admin, sie zu entfernen.',
		exists: 'An diesem Tag gibt es bereits eine Abwesenheit.',
		invalidRange: 'Wähle einen Zeitraum von höchstens einem Jahr mit mindestens einem Arbeitstag.',
		disabled: 'Abwesenheiten eintragen ist in dieser Organisation deaktiviert. Frage einen Admin.',
		requiresApproval: 'Abwesenheiten müssen in dieser Organisation genehmigt werden. Bitte stelle eine Anfrage.',
		unknownType: 'Unbekannte oder inaktive Abwesenheitsart.'
	},
	absenceType: {
		nameTaken: 'Eine Abwesenheitsart mit diesem Namen gibt es bereits.',
		notFound: 'Abwesenheitsart nicht gefunden.'
	},
	invitation: {
		invalidRole: 'Einladen kannst du als Mitglied oder Admin; die Inhaberschaft wird separat übertragen.',
		ownerOnly: 'Nur der Inhaber kann Admins einladen.',
		alreadyMember: 'Diese Person ist bereits Mitglied.',
		notFound: 'Einladung nicht gefunden.',
		invalid: 'Diese Einladung ist ungültig oder abgelaufen.',
		wrongEmail: 'Diese Einladung wurde an eine andere E-Mail-Adresse geschickt. Melde dich mit dieser Adresse an, um sie anzunehmen.'
	},
	schedule: {
		notFound: 'Arbeitszeitmodell nicht gefunden.',
		overlap: 'Dieser Zeitraum überschneidet sich mit einem bestehenden Arbeitszeitmodell. Es darf immer nur eines gelten.'
	},
	overtime: {
		initialDisabled: 'Anfangsüberstunden sind in dieser Organisation deaktiviert.',
		initialRequiresApproval: 'Anfangsüberstunden müssen genehmigt werden. Bitte stelle eine Anfrage.'
	},
	holiday: {
		exists: 'An diesem Tag gibt es bereits einen Feiertag.',
		notFound: 'Feiertag nicht gefunden.'
	},
	pauseRule: {
		exists: 'Eine Pausenregel mit dieser Schwelle gibt es bereits.',
		notFound: 'Pausenregel nicht gefunden.'
	},
	group: {
		notFound: 'Gruppe nicht gefunden.',
		alreadyMember: 'Diese Person ist bereits in der Gruppe.',
		memberNotFound: 'Mitglied nicht in der Gruppe gefunden.'
	},
	apiKey: {
		notFound: 'API-Schlüssel nicht gefunden.'
	},
	terminal: {
		invalidKey: 'Der API-Schlüssel fehlt oder ist ungültig.',
		unknownCard: 'Unbekannte Karte.',
		cardBlocked: 'Diese Karte ist gesperrt.',
		tooManyUnknown: 'Zu viele unbekannte Karten gescannt. Bitte später erneut versuchen.',
		scannedAtOutOfRange: 'Die Scan-Zeit muss zwischen vor 72 Stunden und in 2 Minuten liegen.',
		outOfOrder: 'Dieser Scan ist älter als der letzte Stempel des Mitglieds.',
		timerElsewhere: 'Ein Timer läuft außerhalb dieser Organisation. Bitte zuerst in der App stoppen.',
		cardTaken: 'Diese Karte ist bereits registriert.',
		cardRequired: 'Gib eine Karten-ID ein oder wähle eine gescannte Karte.',
		cardNotFound: 'Karte nicht gefunden.',
		scanNotFound: 'Scan nicht gefunden.'
	}
};

export const en: typeof de = {
	common: {
		forbidden: 'You do not have permission to do that.',
		unauthorized: 'Please sign in.',
		badRequest: 'The request is invalid.',
		notFound: 'Not found.',
		conflict: 'That conflicts with existing data.',
		unexpected: 'An unexpected error occurred.'
	},
	org: {
		notFound: 'Organization not found.',
		notMember: 'You are not a member of this organization.',
		userNotMember: 'This person is not a member of the organization.',
		memberNotFound: 'Member not found.',
		twoFactorRequired: 'This organization requires two-factor authentication. Set it up in your settings to continue.',
		adminRequired: 'Only organization admins can do that.',
		ownerRequired: 'Only the organization owner can do that.',
		slugRequired: 'The organization is missing.',
		featureDisabled: 'This feature is disabled in the organization.',
		featureRequiresApproval: 'This needs approval in this organization. Please submit a request.',
		slugTaken: 'This short name is already taken.',
		ownerRoleFixed: "The owner's role cannot be changed. Transfer ownership first.",
		ownerCannotLeave: 'The owner cannot leave. Delete the organization or transfer ownership first.',
		enableOwnTwoFactorFirst: 'Enable two-factor authentication for your own account first.',
		unknownTimeZone: 'Unknown time zone.',
		coreHoursOrder: 'Core working hours need a valid start and end time, with the start before the end.'
	},
	user: {
		notFound: 'User not found.'
	},
	auth: {
		invalidCredentials: 'Invalid email or password.',
		accountDeactivated: 'This account has been deactivated.',
		sessionEnded: 'Your session has ended. Please sign in again.',
		invalidRefreshToken: 'Your session has expired. Please sign in again.',
		emailInUse: 'This email address is already in use.',
		wrongCurrentPassword: 'The current password is incorrect.',
		wrongPassword: 'The password is incorrect.',
		currentPasswordRequired: 'Enter your current password to change your email address.',
		passwordTooShort: 'The password must be at least 8 characters long.',
		passwordNeedsUppercase: 'The password needs at least one uppercase letter.',
		passwordNeedsLowercase: 'The password needs at least one lowercase letter.',
		passwordNeedsDigit: 'The password needs at least one digit.',
		passwordNeedsSpecial: 'The password needs at least one special character.',
		mailCooldown: 'An email was just sent. Please wait a few minutes.',
		invalidLink: 'This link is invalid or has expired.',
		emailAlreadyConfirmed: 'Your email address is already confirmed.',
		twoFactorTokenExpired: 'The sign-in has expired. Please sign in again.',
		twoFactorLocked: 'Too many wrong codes. Try again in 15 minutes.',
		twoFactorInvalidCode: 'The code is invalid.',
		twoFactorAlreadyEnabled: 'Two-factor authentication is already enabled.',
		twoFactorSetupRequired: 'Start the two-factor setup first.',
		twoFactorNotEnabled: 'Two-factor authentication is not enabled.'
	},
	account: {
		ownsOrganizations: 'You still own organizations. Transfer or delete them first.',
		twoFactorRequiredByOrgs: 'Organizations you belong to require two-factor authentication. Leave them first, or ask an admin to turn the requirement off.'
	},
	entry: {
		startTooEarly: 'The start time must be in the year 2000 or later.',
		startInFuture: 'The start time cannot be in the future.',
		endBeforeStart: 'The end must be after the start.',
		tooLong: 'An entry can be at most 24 hours long.',
		notFound: 'Entry not found.',
		running: 'A running entry cannot be edited. Stop it first.',
		editDisabled: 'Editing past entries is disabled in this organization.',
		editRequiresApproval: 'Editing past entries needs approval. Please submit a request.',
		overlap: 'The entry overlaps an existing entry.',
		manualInFuture: 'An added entry must lie in the past.',
		addDisabled: 'Adding past entries is disabled in this organization.'
	},
	timer: {
		alreadyRunning: 'A timer is already running.',
		notRunning: 'No timer is running.'
	},
	range: {
		tooEarly: 'Dates must be in the year 2000 or later.',
		endBeforeStart: 'The end date must not be before the start date.',
		tooLong: 'A range can cover at most two years.'
	},
	report: {
		invalidMonth: 'The month must be between 1 and 12.'
	},
	export: {
		forbidden: "You may not export this member's data.",
		orgRequired: 'Select an organization for this export.',
		payrollAdminOnly: 'Only admins can export payroll data.'
	},
	import: {
		disabled: 'CSV import is disabled in this organization.',
		requiresApproval: 'CSV import needs approval. Please submit a request.',
		empty: 'The CSV file needs a header row and at least one data row.',
		missingColumns: 'The CSV file needs the columns Date, Start Time and End Time.',
		tooManyRows: 'An import can hold at most 1000 rows.'
	},
	request: {
		duplicate: 'An identical request is already pending.',
		alreadyResponded: 'This request has already been answered.',
		notFound: 'Request not found.',
		cannotRespond: 'You may not answer this request.',
		cannotApply: 'The request can no longer be applied.',
		alreadyMember: 'You are already a member of this organization.',
		joinClosed: 'This organization does not accept join requests. Ask an admin for an invitation.',
		featureDisabled: 'This feature is disabled in the organization.',
		notNeeded: 'You can do this directly, without a request.'
	},
	absence: {
		notFound: 'Absence not found.',
		adminOwned: 'This absence was entered or approved by an admin. Ask an admin to remove it.',
		exists: 'An absence already exists on this date.',
		invalidRange: 'Choose a range of at most one year with at least one working day.',
		disabled: 'Adding absences is disabled in this organization. Ask an admin.',
		requiresApproval: 'Absences need approval in this organization. Please submit a request.',
		unknownType: 'Unknown or inactive absence type.'
	},
	absenceType: {
		nameTaken: 'An absence type with this name already exists.',
		notFound: 'Absence type not found.'
	},
	invitation: {
		invalidRole: 'Invite as member or admin; ownership is transferred separately.',
		ownerOnly: 'Only the owner can invite admins.',
		alreadyMember: 'This person is already a member.',
		notFound: 'Invitation not found.',
		invalid: 'This invitation is invalid or has expired.',
		wrongEmail: 'This invitation was sent to a different email address. Sign in with that address to accept it.'
	},
	schedule: {
		notFound: 'Work schedule not found.',
		overlap: 'This period overlaps an existing work schedule. Only one may apply at a time.'
	},
	overtime: {
		initialDisabled: 'Initial overtime is disabled in this organization.',
		initialRequiresApproval: 'Initial overtime needs approval. Please submit a request.'
	},
	holiday: {
		exists: 'A holiday already exists on this date.',
		notFound: 'Holiday not found.'
	},
	pauseRule: {
		exists: 'A pause rule with this threshold already exists.',
		notFound: 'Pause rule not found.'
	},
	group: {
		notFound: 'Group not found.',
		alreadyMember: 'This person is already in the group.',
		memberNotFound: 'Member not found in the group.'
	},
	apiKey: {
		notFound: 'API key not found.'
	},
	terminal: {
		invalidKey: 'The API key is missing or invalid.',
		unknownCard: 'Unknown card.',
		cardBlocked: 'This card is blocked.',
		tooManyUnknown: 'Too many unknown cards were scanned. Try again later.',
		scannedAtOutOfRange: 'The scan time must lie between 72 hours ago and 2 minutes from now.',
		outOfOrder: "This scan is older than the member's last punch.",
		timerElsewhere: 'A timer is running outside this organization. Stop it in the app first.',
		cardTaken: 'This card is already registered.',
		cardRequired: 'Enter a card ID or pick a scanned card.',
		cardNotFound: 'Card not found.',
		scanNotFound: 'Scan not found.'
	}
};
