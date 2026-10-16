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
		personnelNumberTaken: 'Diese Personalnummer hat bereits ein anderes Mitglied.',
		userNotMember: 'Diese Person ist kein Mitglied der Organisation.',
		memberNotFound: 'Mitglied nicht gefunden.',
		twoFactorRequired: 'Diese Organisation verlangt die Zwei-Faktor-Authentifizierung. Richte sie in den Einstellungen ein, um fortzufahren.',
		adminRequired: 'Das dürfen nur Admins der Organisation.',
		ownerRequired: 'Das darf nur der Inhaber der Organisation.',
		accountantRequired: 'Das dürfen nur die Buchhaltung und Admins der Organisation.',
		slugRequired: 'Die Organisation fehlt.',
		featureDisabled: 'Diese Funktion ist in der Organisation deaktiviert.',
		featureRequiresApproval: 'Das muss in dieser Organisation genehmigt werden. Bitte stelle eine Anfrage.',
		slugTaken: 'Diese Kurzbezeichnung ist bereits vergeben.',
		ownerRoleFixed: 'Die Rolle des Inhabers lässt sich nicht ändern. Übertrage zuerst die Inhaberschaft.',
		ownerCannotLeave: 'Der Inhaber kann die Organisation nicht verlassen. Lösche sie oder übertrage die Inhaberschaft.',
		enableOwnTwoFactorFirst: 'Aktiviere zuerst die Zwei-Faktor-Authentifizierung für dein eigenes Konto.',
		unknownTimeZone: 'Unbekannte Zeitzone.',
		coreHoursOrder: 'Die Kernarbeitszeit braucht eine gültige Start- und Endzeit, die sich unterscheiden (ein Ende vor dem Beginn reicht über Mitternacht).',
		coreHoursBackfillDays: 'Es lassen sich 1 bis 31 vergangene Tage prüfen.'
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
		payrollAdminOnly: 'Nur Admins und die Buchhaltung können die Lohnabrechnung exportieren.',
		hoursHiddenFromAdmins: 'In dieser Organisation sehen Admins die Arbeitszeiten der Mitglieder nicht (Einstellungen). Die Rolle Buchhaltung sieht sie.'
	},
	import: {
		disabled: 'Der CSV-Import ist in dieser Organisation deaktiviert.',
		requiresApproval: 'Der CSV-Import muss genehmigt werden. Bitte stelle eine Anfrage.',
		empty: 'Die CSV-Datei braucht eine Kopfzeile und mindestens eine Datenzeile.',
		missingColumns: 'Die CSV-Datei braucht die Spalten Datum, Start und Ende.',
		tooManyRows: 'Ein Import darf höchstens 1000 Zeilen enthalten.',
		unknownFormat: 'Unbekanntes Import-Format.',
		formatMismatch: 'Die Datei hat nicht die Spalten des gewählten Formats.',
		severalEmployees: 'Die Datei enthält Daten mehrerer Personen. Bitte pro Datei nur eine Person importieren.'
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
		notNeeded: 'Das kannst du direkt erledigen, ohne Anfrage.',
		notWithdrawable: 'Nur offene Anfragen lassen sich zurückziehen – diese wurde schon entschieden oder zurückgezogen.'
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
		coreHoursTolerance: 'Die Toleranz muss zwischen 0 und 120 Minuten liegen.',
		alreadyMember: 'Diese Person ist bereits in der Gruppe.',
		memberNotFound: 'Mitglied nicht in der Gruppe gefunden.',
		parentNotFound: 'Übergeordnete Gruppe nicht gefunden.',
		parentOrTopLevel: 'Wähle entweder eine übergeordnete Gruppe oder die oberste Ebene.',
		cycle: 'Eine Gruppe kann nicht in sich selbst oder eine ihrer Untergruppen verschoben werden.',
		tooDeep: 'Gruppen können höchstens 3 Ebenen tief verschachtelt werden.',
		hasSubGroups: 'Diese Gruppe hat Untergruppen. Wähle, ob sie eine Ebene nach oben rücken oder mit gelöscht werden.',
		unknownCapability: 'Unbekannte Gruppenadmin-Berechtigung.'
	},
	visibility: {
		unknownKind: 'Unbekannte Art von Information.'
	},
	apiKey: {
		notFound: 'API-Schlüssel nicht gefunden.'
	},
	terminal: {
		invalidKey: 'Der API-Schlüssel fehlt oder ist ungültig.',
		unknownCard: 'Unbekannte Karte.',
		cardBlocked: 'Diese Karte ist gesperrt.',
		tooManyUnknown: 'Zu viele unbekannte Karten gescannt. Bitte später erneut versuchen.',
		scannedAtOutOfRange: 'Die Scan-Zeit liegt außerhalb des Offline-Fensters der Organisation oder mehr als 2 Minuten in der Zukunft.',
		doubleScanRange: 'Das Doppel-Scan-Fenster muss zwischen 5 und 600 Sekunden liegen.',
		offlineRange: 'Das Offline-Fenster muss zwischen 1 und 168 Stunden liegen.',
		unknownLimitRange: 'Das Limit für unbekannte Karten muss zwischen 5 und 200 Scans pro 10 Minuten liegen.',
		outOfOrder: 'Dieser Scan ist älter als der letzte Stempel des Mitglieds.',
		timerElsewhere: 'Ein Timer läuft außerhalb dieser Organisation. Bitte zuerst in der App stoppen.',
		cardTaken: 'Diese Karte ist bereits registriert.',
		cardRequired: 'Gib eine Karten-ID ein oder wähle eine gescannte Karte.',
		cardNotFound: 'Karte nicht gefunden.',
		scanNotFound: 'Scan nicht gefunden.'
	},
	period: {
		closed: 'Der Zeitraum {month} ist abgeschlossen. Änderungen darin können nur Admins der Organisation vornehmen.',
		reasonRequired: 'Der Zeitraum {month} ist abgeschlossen. Gib einen Grund für die Änderung an.',
		notEnded: 'Nur Monate, die schon vorbei sind, lassen sich abschließen.',
		tooManyMonths: 'Schließe höchstens 24 Monate auf einmal ab.',
		notClosed: 'Dieser Monat ist nicht abgeschlossen.'
	},
	push: {
		notConfigured: 'Push-Benachrichtigungen sind auf diesem Server nicht eingerichtet.',
		unsupportedService: 'Dieser Push-Dienst wird nicht unterstützt.',
		invalidSubscription: 'Die Anmeldung für Benachrichtigungen ist ungültig.',
		otherAccount: 'Dieses Gerät erhält bereits Benachrichtigungen für ein anderes Konto.'
	},
	network: {
		offline: 'Keine Verbindung. Versuche es erneut, sobald du wieder online bist.'
	},
	accounting: {
		invalidYear: 'Das Jahr muss zwischen 2000 und 2100 liegen.',
		ownerOnly: 'Die Rolle Buchhaltung von Admins oder dir selbst kann nur der Inhaber ändern.'
	}
};
