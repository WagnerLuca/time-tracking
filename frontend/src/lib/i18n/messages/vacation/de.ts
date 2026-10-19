/** Vacation balance strings: carry-over lines (member card, dashboard, member list) and the admin's per-year editor. Add keys to both objects; `en` is typed against `de`. */
export const de = {
	carry: {
		fromLastYear: { one: 'davon {days} Tag aus {year}', other: 'davon {days} Tage aus {year}' },
		fromLastYearUntil: { one: 'davon {days} Tag aus {year}, verfällt am {date}', other: 'davon {days} Tage aus {year}, verfallen am {date}' },
		expired: { one: '{days} Tag verfallen', other: '{days} Tage verfallen' },
		adjustment: 'Korrektur {days} Tage',
		adjustmentWithNote: 'Korrektur {days} Tage ({note})'
	},
	year: {
		open: 'Jahre bearbeiten',
		title: 'Urlaub von {name}',
		year: 'Jahr',
		entitlement: 'Anspruch',
		entitlementHint: 'Tage in diesem Jahr. Ein Jahr ohne eigenen Wert übernimmt den letzten früheren, sonst die Tage pro Jahr.',
		inherited: 'Kein eigener Wert: gilt wie das Vorjahr bzw. „Tage pro Jahr“ ({days} Tage). Leer lassen ändert nichts.',
		own: 'Eigener Wert dieses Jahres.',
		clear: 'Eigenen Wert entfernen',
		cleared: 'Der Anspruch {year} gilt wieder wie das Vorjahr.',
		adjustment: 'Korrektur (+/− Tage)',
		adjustmentHint: 'Z. B. Resturlaub aus dem bisherigen System oder eine Gutschrift. Negativ zieht Tage ab.',
		note: 'Notiz zur Korrektur',
		notePlaceholder: 'z. B. Übernahme aus Altsystem',
		balance: 'Stand {year}',
		carriedIn: 'Übertrag',
		expired: 'Verfallen',
		used: 'Genommen + geplant',
		remaining: 'Übrig',
		available: 'Verfügbar',
		updated: 'Zuletzt geändert {date} von {name}',
		saved: 'Urlaub {year} gespeichert.',
		saveFailed: 'Der Urlaub konnte nicht gespeichert werden.',
		loadFailed: 'Der Urlaub konnte nicht geladen werden.',
		entitlementRange: 'Bitte 0–365 Tage eingeben.',
		adjustmentRange: 'Bitte −365 bis 365 Tage eingeben.',
		yearRange: 'Bitte ein Jahr zwischen 2000 und 2100 eingeben.',
		noteTooLong: 'Höchstens 500 Zeichen.'
	}
};
