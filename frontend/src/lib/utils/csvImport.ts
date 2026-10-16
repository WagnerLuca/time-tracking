/**
 * CSV import formats (mirrors the backend's `CsvImportFormats`) and the summary lines shown above
 * the import preview: which format was read, whose data it is, and what was deliberately left out.
 */
import { t, tp } from '$lib/i18n';
import type { ImportPreviewResponse } from '$lib/api';

/** Values of the preview's `format` query parameter; `auto` detects the format from the header row. */
export const IMPORT_FORMATS = ['auto', 'standard', 'evaluation'] as const;
export type ImportFormat = (typeof IMPORT_FORMATS)[number];

export function isImportFormat(value: unknown): value is ImportFormat {
	return typeof value === 'string' && (IMPORT_FORMATS as readonly string[]).includes(value);
}

/** Label of a format id; unknown ids (a newer backend) are shown as they are. */
export function importFormatLabel(format: string | null | undefined): string {
	return isImportFormat(format) ? t(`export.import.formats.${format}`) : (format ?? '');
}

export function importFormatOptions(): { value: ImportFormat; label: string }[] {
	return IMPORT_FORMATS.map((value) => ({ value, label: importFormatLabel(value) }));
}

export interface ImportSummary {
	/** Neutral facts (format, person, skipped rows). */
	notes: string[];
	/** Things the member should act on (someone else's data, absences to add by hand). */
	warnings: string[];
}

type PreviewSummaryFields = Partial<Pick<
	ImportPreviewResponse,
	| 'format'
	| 'employeeName'
	| 'employeeMatchesUser'
	| 'skippedRowCount'
	| 'skippedVacationDays'
	| 'skippedSickDays'
	| 'skippedOtherAbsenceDays'
	>
>;

export function importSummary(preview: PreviewSummaryFields): ImportSummary {
	const notes: string[] = [];
	const warnings: string[] = [];

	if (preview.format) notes.push(t('export.import.summary.format', { format: importFormatLabel(preview.format) }));

	if (preview.employeeName) {
		if (preview.employeeMatchesUser === false)
			warnings.push(t('export.import.summary.employeeMismatch', { name: preview.employeeName }));
		else notes.push(t('export.import.summary.employee', { name: preview.employeeName }));
	}

	const skipped = preview.skippedRowCount ?? 0;
	if (skipped > 0) notes.push(tp('export.import.summary.skippedRows', skipped));

	const absences: string[] = [];
	const vacation = preview.skippedVacationDays ?? 0;
	const sick = preview.skippedSickDays ?? 0;
	const other = preview.skippedOtherAbsenceDays ?? 0;
	if (vacation > 0) absences.push(tp('export.import.summary.vacationDays', vacation));
	if (sick > 0) absences.push(tp('export.import.summary.sickDays', sick));
	if (other > 0) absences.push(tp('export.import.summary.otherAbsenceDays', other));
	if (absences.length > 0) warnings.push(t('export.import.summary.absences', { list: absences.join(', ') }));

	return { notes, warnings };
}
