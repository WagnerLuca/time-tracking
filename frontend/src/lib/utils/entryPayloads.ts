import { t } from '$lib/i18n';
import { formatDateTime } from './formatters';

/** The inline entry form's values (datetime-local strings; pause null = let the pause rules decide). */
export interface EntryFormValues {
	start: string;
	end: string;
	description: string;
	pause: number | null | '';
}

/** Body for POST TimeTracking/manual and POST …/members/{id}/entries. */
export function newEntryPayload(form: EntryFormValues, includePause: boolean) {
	const pause = form.pause === '' || form.pause == null ? null : Math.max(0, Math.round(Number(form.pause)) || 0);
	return {
		startTime: new Date(form.start).toISOString(),
		endTime: new Date(form.end).toISOString(),
		description: form.description.trim() || null,
		...(includePause && pause != null ? { pauseDurationMinutes: pause } : {})
	};
}

/** Default times for an entry added on `day` ('YYYY-MM-DD'): 09:00–17:00. */
export function defaultEntryTimes(day: string): { start: string; end: string } {
	return { start: `${day}T09:00`, end: `${day}T17:00` };
}

/** Translated Working Time Act warnings of a saved entry (`dailyMax`, `restPeriod`); unknown ones are skipped. */
export function warningMessages(warnings: string[] | null | undefined): string[] {
	return (warnings ?? []).flatMap((w) => (w === 'dailyMax' || w === 'restPeriod' ? [t(`entries.warnings.${w}`)] : []));
}

const HISTORY_FIELDS = ['StartTime', 'EndTime', 'PauseDurationMinutes', 'Description', 'OrganizationId', 'IsFailure'] as const;
type HistoryField = (typeof HISTORY_FIELDS)[number];
const isHistoryField = (f: string | null | undefined): f is HistoryField => (HISTORY_FIELDS as readonly string[]).includes(f ?? '');

export function historyFieldLabel(field: string | null | undefined): string {
	return isHistoryField(field) ? t(`entries.history.field.${field}`) : (field ?? '');
}

/** A recorded value (invariant strings from the backend: ISO instants, "true"/"false", numbers) for display. */
export function historyValue(field: string | null | undefined, value: string | null | undefined): string {
	if (value == null || value === '') return t('entries.history.empty_value');
	if (field === 'StartTime' || field === 'EndTime') return formatDateTime(value);
	if (field === 'PauseDurationMinutes') return t('entries.history.minutes', { value });
	if (field === 'IsFailure') return value === 'true' ? t('common.yes') : t('common.no');
	return value;
}

export function historyKindLabel(kind: string | null | undefined): string {
	return kind === 'Created' || kind === 'Updated' || kind === 'Deleted' || kind === 'ApprovedRequest' ? t(`entries.history.kind.${kind}`) : (kind ?? '');
}
