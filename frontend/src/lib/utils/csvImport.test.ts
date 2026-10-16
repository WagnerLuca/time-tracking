import { afterEach, describe, expect, it } from 'vitest';
import { locale } from '$lib/stores/locale.svelte';
import { IMPORT_FORMATS, importFormatLabel, importFormatOptions, importSummary, isImportFormat } from './csvImport';

describe('csvImport', () => {
	afterEach(() => locale.set('de'));

	it('knows the backend format ids', () => {
		expect(IMPORT_FORMATS).toEqual(['auto', 'standard', 'evaluation']);
		expect(isImportFormat('evaluation')).toBe(true);
		expect(isImportFormat('excel')).toBe(false);
		expect(isImportFormat(undefined)).toBe(false);
	});

	it('labels formats in both languages and passes unknown ids through', () => {
		expect(importFormatLabel('standard')).toBe('Standard (Datum, Start, Ende)');
		expect(importFormatLabel('future-format')).toBe('future-format');
		expect(importFormatLabel(null)).toBe('');
		locale.set('en');
		expect(importFormatOptions().map((o) => o.label)).toEqual([
			'Detect automatically',
			'Standard (date, start, end)',
			'Daily evaluation ("Auswertungen" export)'
		]);
	});

	it('summarises a daily evaluation of the member', () => {
		const summary = importSummary({
			format: 'evaluation',
			employeeName: 'Erika Muster',
			employeeMatchesUser: true,
			skippedRowCount: 72,
			skippedVacationDays: 11,
			skippedSickDays: 1,
			skippedOtherAbsenceDays: 0
		});
		expect(summary.notes).toEqual([
			'Gelesen als: Tagesauswertung (Auswertungen-Export)',
			'Daten von Erika Muster',
			'72 Zeilen ohne Arbeitszeit übersprungen (z. B. Wochenende, Feiertag, Abwesenheit).'
		]);
		expect(summary.warnings).toEqual([
			'Abwesenheiten werden nicht importiert – bitte separat eintragen: 11 Urlaubstage, 1 Krankheitstag.'
		]);
	});

	it("warns about someone else's data and stays quiet for a plain file", () => {
		locale.set('en');
		const other = importSummary({ format: 'evaluation', employeeName: 'Max Beispiel', employeeMatchesUser: false, skippedOtherAbsenceDays: 2 });
		expect(other.notes).toEqual(['Read as: Daily evaluation ("Auswertungen" export)']);
		expect(other.warnings).toEqual([
			'The file holds the times of Max Beispiel, not yours. The import always goes into your own account.',
			'Absences are not imported – please add them separately: 2 days of other absence.'
		]);

		expect(importSummary({ format: 'standard', skippedRowCount: 0 })).toEqual({
			notes: ['Read as: Standard (date, start, end)'],
			warnings: []
		});
		expect(importSummary({})).toEqual({ notes: [], warnings: [] });
	});
});
