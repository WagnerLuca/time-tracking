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
