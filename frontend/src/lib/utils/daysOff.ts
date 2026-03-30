import { holidayApi, absenceDayApi } from '$lib/apiClient';

export interface DaysOffData {
	daysOffSet: Set<string>;
	holidayDates: Map<string, string>;
	sickDayDates: Set<string>;
	vacationDates: Set<string>;
	otherAbsenceDates: Set<string>;
}

const EMPTY: DaysOffData = {
	daysOffSet: new Set(),
	holidayDates: new Map(),
	sickDayDates: new Set(),
	vacationDates: new Set(),
	otherAbsenceDates: new Set()
};

export function emptyDaysOff(): DaysOffData {
	return {
		daysOffSet: new Set(),
		holidayDates: new Map(),
		sickDayDates: new Set(),
		vacationDates: new Set(),
		otherAbsenceDates: new Set()
	};
}

export async function loadDaysOff(orgSlug: string | null | undefined, userId?: number): Promise<DaysOffData> {
	if (!orgSlug) return emptyDaysOff();

	try {
		const [holRes, absRes] = await Promise.all([
			holidayApi.apiV1OrganizationsSlugHolidaysGet(orgSlug),
			absenceDayApi.apiV1OrganizationsSlugAbsencesGet(orgSlug, userId)
		]);

		const daysOffSet = new Set<string>();
		const holidayDates = new Map<string, string>();
		const sickDayDates = new Set<string>();
		const vacationDates = new Set<string>();
		const otherAbsenceDates = new Set<string>();

		for (const h of holRes.data) {
			if (h.date) {
				daysOffSet.add(h.date);
				holidayDates.set(h.date, h.name ?? 'Holiday');
			}
		}

		const absences = absRes.data.items ?? [];
		for (const a of absences) {
			if (a.date) {
				daysOffSet.add(a.date);
				if (a.type === 'SickDay') sickDayDates.add(a.date);
				else if (a.type === 'Vacation') vacationDates.add(a.date);
				else otherAbsenceDates.add(a.date);
			}
		}

		return { daysOffSet, holidayDates, sickDayDates, vacationDates, otherAbsenceDates };
	} catch {
		return emptyDaysOff();
	}
}
