import { holidayApi, absenceDayApi } from '$lib/apiClient';

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

export async function loadDaysOff(orgSlug: string | null | undefined, userId?: number): Promise<DaysOffData> {
	if (!orgSlug) return emptyDaysOff();

	try {
		const [holRes, absRes] = await Promise.all([
			holidayApi.apiV1OrganizationsSlugHolidaysGet(orgSlug),
			absenceDayApi.apiV1OrganizationsSlugAbsencesGet(orgSlug, userId, undefined, undefined, 200)
		]);

		const result = emptyDaysOff();
		const {
			daysOffSet, holidayDates, halfDayHolidays, sickDayDates, vacationDates,
			otherAbsenceDates, halfDayAbsences, customTypeDates, customNoCreditDates, customTopUpDates
		} = result;

		for (const h of holRes.data) {
			if (h.date) {
				daysOffSet.add(h.date);
				holidayDates.set(h.date, h.name ?? 'Holiday');
				if (h.isHalfDay) halfDayHolidays.add(h.date);
			}
		}

		const absences = absRes.data.items ?? [];
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
	} catch {
		return emptyDaysOff();
	}
}
