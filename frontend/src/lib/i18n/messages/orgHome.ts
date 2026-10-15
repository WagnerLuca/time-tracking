/** Strings for the org home ("my schedule") page. Add keys to both objects; `en` is typed against `de`. */
export const de = {
	fallbackName: 'Organisation',
	notFound: 'Organisation nicht gefunden.',
	loadFailed: 'Die Organisation konnte nicht geladen werden.',
	reloadFailed: 'Die Organisation konnte nicht neu geladen werden.',
	scheduleFailed: 'Dein Arbeitszeitmodell konnte nicht geladen werden.',
	permissionsFailed: 'Deine Berechtigungen konnten nicht geladen werden.',
	absenceTypesFailed: 'Die Abwesenheitsarten konnten nicht geladen werden.',
	markReadFailed: 'Die Benachrichtigung konnte nicht als gelesen markiert werden.',
	intro: 'Dein Arbeitszeitmodell, deine Überstunden, dein Urlaub und deine Abwesenheiten in dieser Organisation.',
	visibilityWarning: 'Die Admins dieser Organisation können deine erfassten Arbeitszeiten sehen.',
	rulesChangedBefore: 'Die Regeln der Organisation wurden geändert. Details unter',
	rulesChangedLink: 'Einstellungen',
	rulesChangedAfter: '.',
	myAbsences: 'Meine Abwesenheiten',
	edit: {
		name: 'Name',
		slug: 'Kürzel (URL)',
		description: 'Beschreibung',
		website: 'Website',
		nameRequired: 'Bitte einen Namen eingeben.',
		slugRequired: 'Bitte ein Kürzel eingeben.',
		slugInvalid: 'Nur Kleinbuchstaben, Ziffern und Bindestriche verwenden.',
		saved: 'Organisation gespeichert.',
		saveFailed: 'Die Organisation konnte nicht gespeichert werden.'
	},
	general: {
		title: 'Allgemein',
		intro: 'Name, Kürzel, Beschreibung, Website und Zeitbezug der Organisation. Nur Admins sehen diesen Bereich.',
		profile: 'Profil',
		time: 'Zeit',
		timeIntro: 'Zeitzone und Ende des Arbeitstags gelten für alle Tage, Berichte und den automatischen Timer-Stopp.',
		danger: 'Gefahrenzone',
		deleteIntro: 'Löscht die Organisation. Alle Mitglieder verlieren sofort den Zugang; das lässt sich nicht rückgängig machen.',
		ownerOnly: 'Nur die Inhaberin oder der Inhaber kann die Organisation löschen.',
		typeName: 'Zur Bestätigung den Namen „{name}“ eintippen'
	},
	delete: {
		confirm: 'Organisation löschen',
		deleted: 'Organisation gelöscht.',
		failed: 'Die Organisation konnte nicht gelöscht werden.'
	}
};

export const en: typeof de = {
	fallbackName: 'Organization',
	notFound: 'Organization not found.',
	loadFailed: 'Could not load the organization.',
	reloadFailed: 'Could not reload the organization.',
	scheduleFailed: 'Could not load your work schedule.',
	permissionsFailed: 'Could not load your permissions.',
	absenceTypesFailed: 'Could not load the absence types.',
	markReadFailed: 'Could not mark the notification as read.',
	intro: 'Your work schedule, overtime, vacation and absences in this organization.',
	visibilityWarning: 'The admins of this organization can see your tracked working hours.',
	rulesChangedBefore: 'The organization rules have changed. See',
	rulesChangedLink: 'Settings',
	rulesChangedAfter: ' for details.',
	myAbsences: 'My absences',
	edit: {
		name: 'Name',
		slug: 'Slug (URL)',
		description: 'Description',
		website: 'Website',
		nameRequired: 'Enter a name.',
		slugRequired: 'Enter a slug.',
		slugInvalid: 'Use lowercase letters, digits and hyphens only.',
		saved: 'Organization saved.',
		saveFailed: 'Could not save the organization.'
	},
	general: {
		title: 'General',
		intro: "The organization's name, slug, description, website and time reference. Only admins see this section.",
		profile: 'Profile',
		time: 'Time',
		timeIntro: 'Time zone and business day end apply to every day, report and the automatic timer stop.',
		danger: 'Danger zone',
		deleteIntro: 'Deletes the organization. Every member loses access at once; this cannot be undone.',
		ownerOnly: 'Only the owner can delete the organization.',
		typeName: 'Type the name "{name}" to confirm'
	},
	delete: {
		confirm: 'Delete organization',
		deleted: 'Organization deleted.',
		failed: 'Could not delete the organization.'
	}
};
