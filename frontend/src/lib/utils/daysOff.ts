import { holidayApi, absenceDayApi } from '$lib/apiClient';
import { toast } from '$lib/stores/toast.svelte';
import { t } from '$lib/i18n';
import { dateKey, expandHolidays } from '$lib/utils/dateHelpers';
import { fetchAllPages } from '$lib/utils/pagination';

export interface DaysOffData {
	daysOffSet: Set<string>;
	holidayDates: Map<string, string>;
	halfDayHolidays: Set<string>;
	sickDayDates: Set<string>;
	vacationDates: Set<string>;
	otherAbsenceDates: Set<string>;
	halfDayAbsences: Set<string>;
	/** Custom absence type per date: name/color/creditMode of the org-defined type. */
	customTypeDates: Map<string, { name: string; color?: string | null; creditMode: string }>;
	/** Dates whose custom type credits nothing (keep hours as is). */
	customNoCreditDates: Set<string>;
	/** Dates whose custom type tops hours up to the day target (e.g. Business Trip). */
	customTopUpDates: Set<string>;
}

export function emptyDaysOff(): DaysOffData {
	return {
		daysOffSet: new Set(),
		holidayDates: new Map(),
		halfDayHolidays: new Set(),
		sickDayDates: new Set(),
		vacationDates: new Set(),
		otherAbsenceDates: new Set(),
		halfDayAbsences: new Set(),
		customTypeDates: new Map(),
		customNoCreditDates: new Set(),
		customTopUpDates: new Set()
	};
}

/**
 * Holidays and absences between `from` and `to` (inclusive, local days). Recurring
 * holidays are expanded into every year of the range; absences are paged through,
 * so the API's 200-item cap never hides any.
 */
export async function loadDaysOff(orgSlug: string | null | undefined, userId: number | undefined, from: Date, to: Date): Promise<DaysOffData> {
	if (!orgSlug) return emptyDaysOff();

	try {
		const fromKey = dateKey(from);
		const toKey = dateKey(to);
		const [holRes, absences] = await Promise.all([
			holidayApi.apiV1OrganizationsSlugHolidaysGet(orgSlug),
			fetchAllPages((limit, offset) =>
				absenceDayApi.apiV1OrganizationsSlugAbsencesGet(orgSlug, userId, fromKey, toKey, limit, offset).then((r) => r.data))
		]);

		const result = emptyDaysOff();
		const {
			daysOffSet, holidayDates, halfDayHolidays, sickDayDates, vacationDates,
			otherAbsenceDates, halfDayAbsences, customTypeDates, customNoCreditDates, customTopUpDates
		} = result;

		for (const [date, list] of expandHolidays(holRes.data, from.getFullYear(), to.getFullYear())) {
			if (date < fromKey || date > toKey) continue;
			daysOffSet.add(date);
			holidayDates.set(date, list[0].name ?? ''); // '' → getDayTypeLabel() shows the localized "Holiday"
			if (list.every((h) => h.isHalfDay)) halfDayHolidays.add(date);
		}

		for (const a of absences) {
			if (!a.date) continue;
			daysOffSet.add(a.date);
			if (a.type === 'SickDay') sickDayDates.add(a.date);
			else if (a.type === 'Vacation') vacationDates.add(a.date);
			else otherAbsenceDates.add(a.date);
			if (a.isHalfDay) halfDayAbsences.add(a.date);

			if (a.customTypeName) {
				customTypeDates.set(a.date, {
					name: a.customTypeName,
					color: a.customTypeColor,
					creditMode: a.creditMode ?? 'FullCredit'
				});
				if (a.creditMode === 'None') customNoCreditDates.add(a.date);
				else if (a.creditMode === 'FillToTarget') customTopUpDates.add(a.date);
			}
		}

		return result;
	} catch (err) {
		// Keep the page usable, but say that holidays/absences are missing from the numbers
		toast.error(err, t('common.loadFailed'));
		return emptyDaysOff();
	}
}
