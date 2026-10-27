import { holidayApi, absenceDayApi } from '$lib/apiClient';
import { toast } from '$lib/stores/toast.svelte';
import { t } from '$lib/i18n';
import { dateKey } from '$lib/utils/dateHelpers';
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
 * Holidays and absences between `from` and `to` (inclusive, local days). The holidays are the member's own (the server
 * resolves their region and groups: `GET holidays/days`); absences are paged through, so the API's 200-item cap never
 * hides any. A day can hold two half-day absences (e.g. half vacation + half sick): it is then in both sets.
 */
export async function loadDaysOff(orgSlug: string | null | undefined, userId: number | undefined, from: Date, to: Date): Promise<DaysOffData> {
	if (!orgSlug) return emptyDaysOff();

	try {
		const fromKey = dateKey(from);
		const toKey = dateKey(to);
		const [holRes, absences] = await Promise.all([
			holidayApi.apiV1OrganizationsSlugHolidaysDaysGet(orgSlug, fromKey, toKey, userId),
			fetchAllPages((limit, offset) =>
				absenceDayApi.apiV1OrganizationsSlugAbsencesGet(orgSlug, userId, fromKey, toKey, limit, offset).then((r) => r.data))
		]);

		const result = emptyDaysOff();
		const {
			daysOffSet, holidayDates, halfDayHolidays, sickDayDates, vacationDates,
			otherAbsenceDates, halfDayAbsences, customTypeDates, customNoCreditDates, customTopUpDates
		} = result;

		for (const h of holRes.data.days ?? []) {
			if (!h.date) continue;
			daysOffSet.add(h.date);
			holidayDates.set(h.date, h.name ?? ''); // '' → getDayTypeLabel() shows the localized "Holiday"
			if (h.isHalfDay) halfDayHolidays.add(h.date);
		}

		const halvesSeen = new Set<string>();
		for (const a of absences) {
			if (!a.date) continue;
			daysOffSet.add(a.date);
			if (a.type === 'SickDay') sickDayDates.add(a.date);
			else if (a.type === 'Vacation') vacationDates.add(a.date);
			else otherAbsenceDates.add(a.date);
			// Two half-day absences (e.g. half vacation + half sick) make a whole day off
			if (a.isHalfDay) {
				if (halvesSeen.has(a.date)) halfDayAbsences.delete(a.date);
				else halfDayAbsences.add(a.date);
				halvesSeen.add(a.date);
			}

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
