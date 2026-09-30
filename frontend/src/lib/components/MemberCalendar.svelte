<script lang="ts">
	import { onMount } from 'svelte';
	import { auth } from '$lib/stores/auth.svelte';
	import { organizationsApi, timeTrackingApi, workScheduleApi, absenceDayApi, groupApi } from '$lib/apiClient';
	import type { TimeEntryResponse, WorkScheduleResponse, OrganizationDetailResponse, AbsenceTypeDefinitionResponse, EffectivePermissionsResponse } from '$lib/api';
	import { formatHours, formatDelta, formatDuration, formatTime, formatDateFull, barWidth } from '$lib/utils/formatters';
	import { dateKey } from '$lib/utils/dateHelpers';
	import { getDayTarget, getAbsenceCredit, getDayType, getDayTypeLabel, getHeatColor, type CustomCreditSets } from '$lib/utils/scheduleHelpers';
	import { canEditEntries, canRequestEditEntries, canEditPause, canRequestEditPause } from '$lib/utils/orgRules';
	import { loadDaysOff as fetchDaysOff, emptyDaysOff, type DaysOffData } from '$lib/utils/daysOff';
	import { createEntryEditor } from '$lib/utils/entryEditor.svelte';
	import LoadingSpinner from '$lib/components/LoadingSpinner.svelte';
	import EntryEditForm from '$lib/components/EntryEditForm.svelte';
	import RequestModal from '$lib/components/RequestModal.svelte';
	import AbsenceDialog from '$lib/components/AbsenceDialog.svelte';

	/**
	 * Shared month/year calendar of a member's tracked time, absences and holidays.
	 * Used by the personal History page (isSelf) and the member detail page.
	 */
	interface Props {
		orgSlug: string;
		orgId?: number | null;
		userId: number;
		/** When true, uses the caller's own endpoints and enables entry editing. */
		isSelf?: boolean;
		/** Optional preloaded org detail (avoids an extra fetch). */
		orgDetail?: OrganizationDetailResponse | null;
	}
	let { orgSlug, orgId = null, userId, isSelf = false, orgDetail: orgDetailProp = null }: Props = $props();

	// View mode
	let viewMode = $state<'month' | 'year'>('month');

	// Navigation
	let currentYear = $state(new Date().getFullYear());
	let currentMonth = $state(new Date().getMonth()); // 0-indexed

	// Data
	let loading = $state(true);
	let entries = $state<TimeEntryResponse[]>([]);
	let workSchedule = $state<WorkScheduleResponse | null>(null);
	let schedulePeriods = $state<WorkScheduleResponse[]>([]);

	// Day-type maps
	let daysOff = $state<DaysOffData>(emptyDaysOff());
	let holidayDates = $derived(daysOff.holidayDates);
	let halfDayHolidays = $derived(daysOff.halfDayHolidays);
	let sickDayDates = $derived(daysOff.sickDayDates);
	let vacationDates = $derived(daysOff.vacationDates);
	let otherAbsenceDates = $derived(daysOff.otherAbsenceDates);
	let halfDayAbsences = $derived(daysOff.halfDayAbsences);
	let customTypeDates = $derived(daysOff.customTypeDates);
	let customCredits = $derived<CustomCreditSets>({ noCredit: daysOff.customNoCreditDates, topUp: daysOff.customTopUpDates });

	// Year view data
	let yearEntries = $state<TimeEntryResponse[]>([]);
	let yearLoading = $state(false);

	// Cumulative balance
	let allTimeEntries = $state<TimeEntryResponse[]>([]);
	let cumulativeBalance = $state(0); // overall cumulative balance in minutes
	let monthlyCumulativeBalances = $state<Map<string, number>>(new Map()); // "YYYY-MM" -> cumulative balance
	let selectedDayKey = $state<string | null>(null);
	let fetchedOrgDetail = $state<OrganizationDetailResponse | null>(null);
	const orgDetail = $derived(orgDetailProp ?? fetchedOrgDetail);

	// Day-click add-absence popup
	let absenceTypes = $state<AbsenceTypeDefinitionResponse[]>([]);
	let effectivePerms = $state<EffectivePermissionsResponse | null>(null);
	let showAbsenceDialog = $state(false);
	let absenceDialogDate = $state('');
	let absenceMessage = $state('');

	let myRole = $derived(orgDetail?.members?.find(m => m.id === auth.user?.id)?.role ?? null);
	let isOrgAdmin = $derived(myRole === 'Owner' || myRole === 'Admin');
	let selfAbsenceMode = $derived(effectivePerms?.absenceSelfServiceMode ?? orgDetail?.absenceSelfServiceMode ?? 'Allowed');
	/** Admins can add for the viewed person; members can add/request for themselves. */
	let canAddAbsence = $derived(isOrgAdmin || (isSelf && selfAbsenceMode !== 'Disabled'));
	let mustRequest = $derived(!isOrgAdmin && isSelf && selfAbsenceMode === 'RequiresApproval');

	// Shared entry editing + request logic (only exposed in the UI when isSelf)
	const editor = createEntryEditor({
		getOrgDetail: () => orgDetail,
		getOrgSlug: () => orgSlug,
		onSaved: async () => { await loadEntries(); await loadCumulativeBalance(); },
		onDeleted: async () => { await loadEntries(); await loadCumulativeBalance(); },
		findEntry: (id) => selectedDayEntries.find(e => e.id === id)
	});

	// Computed
	const monthName = $derived(new Date(currentYear, currentMonth).toLocaleDateString('en-US', { month: 'long' }));
	const calendarDays = $derived(buildCalendarDays(currentYear, currentMonth, entries));
	const monthStats = $derived(computeMonthStats(calendarDays));
	const yearMonths = $derived(viewMode === 'year' ? buildYearMonths(currentYear, yearEntries) : []);

	const currentMonthCumulative = $derived(() => {
		const key = `${currentYear}-${String(currentMonth + 1).padStart(2, '0')}`;
		return monthlyCumulativeBalances.get(key) ?? 0;
	});

	const selectedDay = $derived(
		selectedDayKey
			? (calendarDays.find((day) => day.key === selectedDayKey && day.isCurrentMonth) ?? null)
			: null
	);

	const selectedDayEntries = $derived(
		selectedDay
			? entries
				.filter((entry) => entry.startTime && dateKey(new Date(entry.startTime)) === selectedDay.key)
				.sort((a, b) => new Date(a.startTime!).getTime() - new Date(b.startTime!).getTime())
			: []
	);

	onMount(async () => {
		await loadAll();
		loading = false;
	});

	// ── Data loading (self vs. member endpoints) ─────────────────────────

	async function fetchEntriesRange(from: Date, to: Date, limit: number): Promise<TimeEntryResponse[]> {
		try {
			if (isSelf) {
				const { data } = await timeTrackingApi.apiV1TimeTrackingGet(
					orgId ?? undefined, from.toISOString(), to.toISOString(), limit);
				return data.items ?? [];
			}
			const { data } = await organizationsApi.apiV1OrganizationsSlugMemberEntriesMemberIdGet(
				orgSlug, userId, from.toISOString(), to.toISOString(), limit);
			return data.items ?? [];
		} catch {
			return [];
		}
	}

	async function loadAll() {
		loading = true;
		await Promise.all([
			loadEntries(),
			loadWorkSchedule(),
			loadOrgDetail(),
			loadDaysOff(),
			loadSchedulePeriods(),
			loadAbsenceTypes(),
			loadEffectivePermissions()
		]);
		await loadCumulativeBalance();
		if (viewMode === 'year') await loadYearEntries();
		loading = false;
	}

	async function loadOrgDetail() {
		if (orgDetail) return;
		try {
			const { data } = await organizationsApi.apiV1OrganizationsSlugGet(orgSlug);
			fetchedOrgDetail = data;
		} catch { fetchedOrgDetail = null; }
	}

	async function loadEntries() {
		const from = new Date(currentYear, currentMonth, 1);
		const to = new Date(currentYear, currentMonth + 1, 0, 23, 59, 59, 999);
		entries = await fetchEntriesRange(from, to, 5000);
		ensureSelectedDayForCurrentMonth();
	}

	async function loadYearEntries() {
		yearLoading = true;
		const from = new Date(currentYear, 0, 1);
		const to = new Date(currentYear, 11, 31, 23, 59, 59, 999);
		yearEntries = await fetchEntriesRange(from, to, 50000);
		yearLoading = false;
	}

	async function loadWorkSchedule() {
		try {
			const { data } = isSelf
				? await workScheduleApi.apiV1OrganizationsSlugWorkScheduleGet(orgSlug)
				: await workScheduleApi.apiV1OrganizationsSlugMembersMemberIdWorkScheduleGet(orgSlug, userId);
			workSchedule = data;
		} catch { workSchedule = null; }
	}

	async function loadSchedulePeriods() {
		try {
			const { data } = isSelf
				? await workScheduleApi.apiV1OrganizationsSlugWorkSchedulesGet(orgSlug)
				: await workScheduleApi.apiV1OrganizationsSlugMembersMemberIdWorkSchedulesGet(orgSlug, userId);
			schedulePeriods = data;
		} catch { schedulePeriods = []; }
	}

	async function loadDaysOff() {
		daysOff = await fetchDaysOff(orgSlug, userId);
	}

	async function loadAbsenceTypes() {
		try {
			const { data } = await absenceDayApi.apiV1OrganizationsSlugAbsenceTypesGet(orgSlug);
			absenceTypes = data;
		} catch { absenceTypes = []; }
	}

	async function loadEffectivePermissions() {
		try {
			const { data } = await groupApi.apiV1OrganizationsSlugEffectivePermissionsGet(orgSlug);
			effectivePerms = data;
		} catch { effectivePerms = null; }
	}

	async function onAbsenceSaved(requestMessage?: string) {
		if (requestMessage) {
			absenceMessage = requestMessage;
			setTimeout(() => (absenceMessage = ''), 6000);
			return;
		}
		await Promise.all([loadDaysOff(), loadEntries()]);
		await loadCumulativeBalance();
	}

	async function loadCumulativeBalance() {
		if (!workSchedule) {
			cumulativeBalance = 0;
			monthlyCumulativeBalances = new Map();
			return;
		}
		try {
			const allEntries = await fetchEntriesRange(new Date(2000, 0, 1), new Date(2099, 11, 31), 50000);
			allTimeEntries = allEntries.filter(e => !e.isRunning && e.endTime);

			if (allTimeEntries.length === 0) {
				cumulativeBalance = 0;
				monthlyCumulativeBalances = new Map();
				return;
			}

			// Sort by date
			const sorted = [...allTimeEntries].sort((a, b) => new Date(a.startTime!).getTime() - new Date(b.startTime!).getTime());
			const firstDate = new Date(sorted[0].startTime!);
			firstDate.setHours(0, 0, 0, 0);

			// Group entries by month and by day (per-day needed for top-up credits)
			const monthlyWorked = new Map<string, number>();
			const dailyWorked = new Map<string, number>();
			for (const e of sorted) {
				const d = new Date(e.startTime!);
				const mk = `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`;
				const minutes = e.netDurationMinutes ?? e.durationMinutes ?? 0;
				monthlyWorked.set(mk, (monthlyWorked.get(mk) ?? 0) + minutes);
				const dk = dateKey(d);
				dailyWorked.set(dk, (dailyWorked.get(dk) ?? 0) + minutes);
			}

			// Compute targets and absence credits per month from first entry month to now
			const now = new Date();
			const monthlyTargets = new Map<string, number>();
			const monthlyAbsCredits = new Map<string, number>();
			const cursor = new Date(firstDate.getFullYear(), firstDate.getMonth(), 1);
			while (cursor <= now) {
				const mk = `${cursor.getFullYear()}-${String(cursor.getMonth() + 1).padStart(2, '0')}`;
				const lastDayOfMonth = new Date(cursor.getFullYear(), cursor.getMonth() + 1, 0);
				let target = 0;
				let absCredits = 0;
				const dayCursor = new Date(cursor);
				// Start from actual first entry date in the first month
				if (cursor.getFullYear() === firstDate.getFullYear() && cursor.getMonth() === firstDate.getMonth()) {
					dayCursor.setDate(firstDate.getDate());
				}
				const endDate = now < lastDayOfMonth ? now : lastDayOfMonth;
				while (dayCursor <= endDate) {
					const d = new Date(dayCursor);
					target += getDayTarget(d, workSchedule, holidayDates, schedulePeriods, halfDayHolidays);
					absCredits += getAbsenceCredit(d, workSchedule, holidayDates, sickDayDates, vacationDates, otherAbsenceDates, schedulePeriods, halfDayAbsences, halfDayHolidays, customCredits, dailyWorked.get(dateKey(d)) ?? 0);
					dayCursor.setDate(dayCursor.getDate() + 1);
				}
				monthlyTargets.set(mk, target);
				monthlyAbsCredits.set(mk, absCredits);
				cursor.setMonth(cursor.getMonth() + 1);
			}

			// Build cumulative balances
			const initialOvertime = (workSchedule?.initialOvertimeMode !== 'Disabled' && workSchedule?.initialOvertimeHours) ? workSchedule.initialOvertimeHours * 60 : 0;
			let cumulative = initialOvertime;
			const balances = new Map<string, number>();

			// Get all months from first entry to now
			const monthKeys: string[] = [];
			const mk1 = new Date(firstDate.getFullYear(), firstDate.getMonth(), 1);
			while (mk1 <= now) {
				monthKeys.push(`${mk1.getFullYear()}-${String(mk1.getMonth() + 1).padStart(2, '0')}`);
				mk1.setMonth(mk1.getMonth() + 1);
			}

			for (const mk of monthKeys) {
				const worked = monthlyWorked.get(mk) ?? 0;
				const absCredits = monthlyAbsCredits.get(mk) ?? 0;
				const target = monthlyTargets.get(mk) ?? 0;
				cumulative += worked + absCredits - target;
				balances.set(mk, cumulative);
			}

			cumulativeBalance = cumulative;
			monthlyCumulativeBalances = balances;
		} catch {
			cumulativeBalance = 0;
			monthlyCumulativeBalances = new Map();
		}
	}

	// ── Navigation ────────────────────────────────────────────────────────

	function prevMonth() {
		if (currentMonth === 0) { currentMonth = 11; currentYear--; }
		else currentMonth--;
		loadEntries();
	}
	function nextMonth() {
		if (currentMonth === 11) { currentMonth = 0; currentYear++; }
		else currentMonth++;
		loadEntries();
	}
	function prevYear() { currentYear--; if (viewMode === 'year') loadYearEntries(); else loadEntries(); }
	function nextYear() { currentYear++; if (viewMode === 'year') loadYearEntries(); else loadEntries(); }

	function switchView(mode: 'month' | 'year') {
		viewMode = mode;
		if (mode === 'year' && yearEntries.length === 0) loadYearEntries();
	}

	function ensureSelectedDayForCurrentMonth() {
		const monthPrefix = `${currentYear}-${String(currentMonth + 1).padStart(2, '0')}-`;
		if (selectedDayKey?.startsWith(monthPrefix)) {
			return;
		}

		const today = new Date();
		if (today.getFullYear() === currentYear && today.getMonth() === currentMonth) {
			selectedDayKey = dateKey(today);
			return;
		}

		selectedDayKey = `${monthPrefix}01`;
	}

	function selectDay(day: CalendarDay, openDialog = true) {
		if (!day.isCurrentMonth) return;
		selectedDayKey = day.key;
		editor.cancelEditEntry();
		if (openDialog && canAddAbsence) {
			absenceDialogDate = day.key;
			showAbsenceDialog = true;
		}
	}

	function isPastOrToday(date: Date): boolean {
		const endOfToday = new Date();
		endOfToday.setHours(23, 59, 59, 999);
		return date <= endOfToday;
	}

	// ── Helpers ───────────────────────────────────────────────────────────

	function getYearCumulative(year: number): number {
		// Find the latest month in this year that has cumulative data
		for (let m = 12; m >= 1; m--) {
			const key = `${year}-${String(m).padStart(2, '0')}`;
			const val = monthlyCumulativeBalances.get(key);
			if (val !== undefined) return val;
		}
		return cumulativeBalance;
	}

	interface CalendarDay {
		date: Date;
		key: string;
		dayOfMonth: number;
		isCurrentMonth: boolean;
		isToday: boolean;
		isWeekend: boolean;
		workedMinutes: number;
		creditedMinutes: number;
		targetMinutes: number;
		entryCount: number;
		/** A timer on this day was auto-stopped at the business day end and still needs correcting. */
		hasFailure: boolean;
		dayType: string | null;
		dayTypeLabel: string;
		delta: number;
	}

	function buildCalendarDays(year: number, month: number, entries: TimeEntryResponse[]): CalendarDay[] {
		const firstDay = new Date(year, month, 1);
		const lastDay = new Date(year, month + 1, 0);
		const today = new Date();
		today.setHours(0, 0, 0, 0);

		// Start from Monday before the first day
		const startOffset = (firstDay.getDay() + 6) % 7; // Mon=0
		const calStart = new Date(firstDay);
		calStart.setDate(calStart.getDate() - startOffset);

		// End on Sunday after the last day
		const endOffset = (7 - ((lastDay.getDay() + 6) % 7 + 1)) % 7;
		const calEnd = new Date(lastDay);
		calEnd.setDate(calEnd.getDate() + endOffset);

		// Build entry map
		const entryMap = new Map<string, { minutes: number; count: number; failure: boolean }>();
		for (const entry of entries) {
			if (entry.isRunning || !entry.startTime) continue;
			const d = new Date(entry.startTime);
			const key = dateKey(d);
			const existing = entryMap.get(key) ?? { minutes: 0, count: 0, failure: false };
			existing.minutes += entry.netDurationMinutes ?? entry.durationMinutes ?? 0;
			existing.count++;
			existing.failure ||= !!entry.isFailure;
			entryMap.set(key, existing);
		}

		const days: CalendarDay[] = [];
		const cursor = new Date(calStart);
		while (cursor <= calEnd) {
			const key = dateKey(cursor);
			const isCurrentMonth = cursor.getMonth() === month;
			const isToday = cursor.toDateString() === today.toDateString();
			const isWeekend = cursor.getDay() === 0 || cursor.getDay() === 6;
			const entryData = entryMap.get(key) ?? { minutes: 0, count: 0, failure: false };
			const targetMinutes = isCurrentMonth ? getDayTarget(new Date(cursor), workSchedule, holidayDates, schedulePeriods, halfDayHolidays) : 0;
			const absCredit = isCurrentMonth ? getAbsenceCredit(new Date(cursor), workSchedule, holidayDates, sickDayDates, vacationDates, otherAbsenceDates, schedulePeriods, halfDayAbsences, halfDayHolidays, customCredits, entryData.minutes) : 0;

			days.push({
				date: new Date(cursor),
				key,
				dayOfMonth: cursor.getDate(),
				isCurrentMonth,
				isToday,
				isWeekend,
				workedMinutes: entryData.minutes,
				creditedMinutes: absCredit,
				targetMinutes,
				entryCount: entryData.count,
				hasFailure: entryData.failure,
				dayType: getDayType(key, holidayDates, sickDayDates, vacationDates, otherAbsenceDates, halfDayHolidays),
				dayTypeLabel: getDayTypeLabel(key, holidayDates, sickDayDates, vacationDates, otherAbsenceDates, halfDayHolidays, halfDayAbsences, customTypeDates),
				delta: entryData.minutes + absCredit - targetMinutes
			});

			cursor.setDate(cursor.getDate() + 1);
		}
		return days;
	}

	function computeMonthStats(days: CalendarDay[]) {
		// Only count days up to today — future days haven't happened yet and
		// would otherwise inflate "Missing"/target figures for days not yet elapsed.
		const today = new Date();
		today.setHours(23, 59, 59, 999);
		const currentDays = days.filter(d => d.isCurrentMonth && d.date <= today);
		const totalWorked = currentDays.reduce((s, d) => s + d.workedMinutes, 0);
		const totalCredits = currentDays.reduce((s, d) => s + d.creditedMinutes, 0);
		const totalTarget = currentDays.reduce((s, d) => s + d.targetMinutes, 0);
		const totalDelta = totalWorked + totalCredits - totalTarget;
		const workDays = currentDays.filter(d => d.targetMinutes > 0).length;
		const workedDays = currentDays.filter(d => d.workedMinutes > 0).length;
		const holidays = currentDays.filter(d => d.dayType === 'holiday').length;
		const sickDays = currentDays.filter(d => d.dayType === 'sick').length;
		const vacationDays = currentDays.filter(d => d.dayType === 'vacation').length;
		const avgPerDay = workedDays > 0 ? totalWorked / workedDays : 0;
		return { totalWorked, totalCredits, totalTarget, totalDelta, workDays, workedDays, holidays, sickDays, vacationDays, avgPerDay };
	}

	interface YearMonth {
		month: number;
		name: string;
		workedMinutes: number;
		creditedMinutes: number;
		targetMinutes: number;
		delta: number;
		workDays: number;
		holidays: number;
		sickDays: number;
		vacationDays: number;
		entryCount: number;
	}

	function buildYearMonths(year: number, entries: TimeEntryResponse[]): YearMonth[] {
		// Only count days up to today — future months/days haven't happened yet.
		const today = new Date();
		today.setHours(23, 59, 59, 999);

		// Per-day worked minutes (needed for top-up credits)
		const dailyWorked = new Map<string, number>();
		for (const entry of entries) {
			if (entry.isRunning || !entry.startTime) continue;
			const dk = dateKey(new Date(entry.startTime));
			dailyWorked.set(dk, (dailyWorked.get(dk) ?? 0) + (entry.netDurationMinutes ?? entry.durationMinutes ?? 0));
		}

		const months: YearMonth[] = [];
		for (let m = 0; m < 12; m++) {
			const monthName = new Date(year, m).toLocaleDateString('en-US', { month: 'short' });
			const firstDay = new Date(year, m, 1);
			const lastDay = new Date(year, m + 1, 0);

			// Count work days, holidays, sick days, vacation days
			let workDays = 0, holidays = 0, sickDays = 0, vacationDays = 0, targetMinutes = 0, absenceCredits = 0;
			const cursor = new Date(firstDay);
			while (cursor <= lastDay && cursor <= today) {
				const key = dateKey(cursor);
				const dt = getDayType(key, holidayDates, sickDayDates, vacationDates, otherAbsenceDates, halfDayHolidays);
				if (dt === 'holiday') holidays++;
				else if (dt === 'sick') sickDays++;
				else if (dt === 'vacation') vacationDays++;

				const target = getDayTarget(new Date(cursor), workSchedule, holidayDates, schedulePeriods, halfDayHolidays);
				if (target > 0) workDays++;
				targetMinutes += target;
				absenceCredits += getAbsenceCredit(new Date(cursor), workSchedule, holidayDates, sickDayDates, vacationDates, otherAbsenceDates, schedulePeriods, halfDayAbsences, halfDayHolidays, customCredits, dailyWorked.get(key) ?? 0);

				cursor.setDate(cursor.getDate() + 1);
			}

			// Sum entries for this month (up to today)
			let workedMinutes = 0, entryCount = 0;
			for (const entry of entries) {
				if (entry.isRunning || !entry.startTime) continue;
				const d = new Date(entry.startTime);
				if (d.getMonth() === m && d.getFullYear() === year && d <= today) {
					workedMinutes += entry.netDurationMinutes ?? entry.durationMinutes ?? 0;
					entryCount++;
				}
			}

			months.push({
				month: m,
				name: monthName,
				workedMinutes,
				creditedMinutes: absenceCredits,
				targetMinutes,
				delta: workedMinutes + absenceCredits - targetMinutes,
				workDays,
				holidays,
				sickDays,
				vacationDays,
				entryCount
			});
		}
		return months;
	}
</script>

<div class="member-calendar">
	<div class="flex items-center justify-end mb-4">
		<div class="join">
			<button class="join-item btn btn-sm {viewMode === 'month' ? 'btn-primary' : 'btn-ghost'}" onclick={() => switchView('month')}>Month</button>
			<button class="join-item btn btn-sm {viewMode === 'year' ? 'btn-primary' : 'btn-ghost'}" onclick={() => switchView('year')}>Year</button>
		</div>
	</div>

	{#if absenceMessage}
		<div class="alert alert-success text-sm py-2 px-3 mb-4">{absenceMessage}</div>
	{/if}

	{#if loading}
		<LoadingSpinner message="Loading..." size="sm" />
	{:else if viewMode === 'month'}
		<!-- MONTH VIEW -->
		<div class="month-view">
			<!-- Navigation -->
			<div class="flex items-center justify-center gap-3 mb-5">
				<button class="btn btn-ghost btn-sm btn-square" onclick={prevYear} title="Previous year">&laquo;</button>
				<button class="btn btn-ghost btn-sm btn-square" onclick={prevMonth} title="Previous month">&lsaquo;</button>
				<h2 class="text-lg font-bold min-w-[180px] text-center m-0">{monthName} {currentYear}</h2>
				<button class="btn btn-ghost btn-sm btn-square" onclick={nextMonth} title="Next month">&rsaquo;</button>
				<button class="btn btn-ghost btn-sm btn-square" onclick={nextYear} title="Next year">&raquo;</button>
			</div>

			<!-- Month stats -->
			<div class="grid grid-cols-[repeat(auto-fit,minmax(120px,1fr))] gap-3 mb-4">
				<div class="bg-base-100 border border-base-300 rounded-lg p-3 text-center">
					<span class="block text-xl font-bold text-base-content">{formatHours(monthStats.totalWorked)}</span>
					<span class="text-xs text-base-content/60">Worked</span>
				</div>
				<div class="bg-base-100 border border-base-300 rounded-lg p-3 text-center">
					<span class="block text-xl font-bold text-base-content">{formatHours(monthStats.totalCredits)}</span>
					<span class="text-xs text-base-content/60">Credits</span>
				</div>
				<div class="bg-base-100 border border-base-300 rounded-lg p-3 text-center">
					<span class="block text-xl font-bold text-base-content">{formatHours(monthStats.totalTarget)}</span>
					<span class="text-xs text-base-content/60">Target</span>
				</div>
				<div class="bg-base-100 border border-base-300 rounded-lg p-3 text-center">
					<span class="block text-xl font-bold {monthStats.totalDelta > 0 ? 'text-success' : monthStats.totalDelta < 0 ? 'text-error' : 'text-base-content'}">{formatDelta(monthStats.totalDelta)}</span>
					<span class="text-xs text-base-content/60">Month Balance</span>
				</div>
				<div class="bg-base-100 border border-base-300 rounded-lg p-3 text-center border-l-3 border-l-secondary">
					<span class="block text-xl font-bold {currentMonthCumulative() > 0 ? 'text-success' : currentMonthCumulative() < 0 ? 'text-error' : 'text-base-content'}">{formatDelta(currentMonthCumulative())}</span>
					<span class="text-xs text-base-content/60">Cumulative</span>
				</div>
				<div class="bg-base-100 border border-base-300 rounded-lg p-3 text-center">
					<span class="block text-xl font-bold text-base-content">{formatHours(monthStats.avgPerDay)}</span>
					<span class="text-xs text-base-content/60">Avg / Day</span>
				</div>
			</div>

			<!-- Summary badges -->
			{#if monthStats.workDays > 0 || monthStats.holidays > 0 || monthStats.sickDays > 0 || monthStats.vacationDays > 0}
				<div class="flex flex-wrap gap-2 mb-5">
					<span class="badge badge-ghost badge-sm">{monthStats.workDays} Work Days</span>
					<span class="badge badge-info badge-sm">{monthStats.workedDays} Days Worked</span>
					{#if monthStats.holidays > 0}<span class="badge badge-secondary badge-sm">{monthStats.holidays} Holiday{monthStats.holidays > 1 ? 's' : ''}</span>{/if}
					{#if monthStats.sickDays > 0}<span class="badge badge-error badge-sm">{monthStats.sickDays} Sick Day{monthStats.sickDays > 1 ? 's' : ''}</span>{/if}
					{#if monthStats.vacationDays > 0}<span class="badge badge-accent badge-sm">{monthStats.vacationDays} Vacation Day{monthStats.vacationDays > 1 ? 's' : ''}</span>{/if}
				</div>
			{/if}

			<!-- Calendar grid -->
			<div class="bg-base-100 border border-base-300 rounded-xl overflow-hidden mb-5">
				<div class="grid grid-cols-7 bg-base-200/50 border-b border-base-300">
					{#each ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'] as dow}
						<span class="text-center text-xs font-semibold text-base-content/60 py-2">{dow}</span>
					{/each}
				</div>
				<div class="grid grid-cols-7">
					{#each calendarDays as day}
						<button
							type="button"
							class="min-h-[72px] p-1.5 border-r border-b border-base-200 [&:nth-child(7n)]:border-r-0 flex flex-col gap-0.5 relative transition-colors text-left {!day.isCurrentMonth ? 'opacity-30' : ''} {day.isWeekend ? 'bg-base-200/30' : ''} {day.isToday ? 'outline-2 outline-primary -outline-offset-2 rounded' : ''} {day.key === selectedDayKey ? 'ring-2 ring-primary/40 bg-primary/10' : ''} {day.dayType === 'holiday' ? 'bg-secondary/10' : day.dayType === 'sick' ? 'bg-error/10' : day.dayType === 'vacation' ? 'bg-accent/10' : day.dayType === 'other-absence' ? 'bg-base-content/10' : ''}"
							title={(day.dayTypeLabel || (day.workedMinutes > 0 ? `${formatHours(day.workedMinutes)} worked` : '')) + (canAddAbsence && day.isCurrentMonth ? (mustRequest ? ' — click to request absence' : ' — click to add absence') : '')}
							onclick={() => selectDay(day)}
							disabled={!day.isCurrentMonth}
						>
							<span class="text-xs font-medium {day.isToday ? 'text-primary font-bold' : 'text-base-content/70'}">{day.dayOfMonth}</span>
							{#if day.hasFailure}
								<span class="absolute top-1 right-1 h-2 w-2 rounded-full bg-error" title="Timer not stopped. Needs correction"></span>
							{/if}
							{#if day.isCurrentMonth}
								{#if day.workedMinutes > 0}
									<span class="text-xs font-semibold text-base-content">{formatHours(day.workedMinutes)}</span>
									{#if day.targetMinutes > 0}
										<div class="h-[3px] bg-base-200 rounded-sm overflow-hidden mt-auto">
											<div class="h-full rounded-sm min-w-[2px]" style="width: {barWidth(day.workedMinutes, day.targetMinutes)}%; background: {getHeatColor(day.workedMinutes, day.targetMinutes)}"></div>
										</div>
									{/if}
								{:else if day.dayType}
									<span class="text-xs mt-0.5">{day.dayType === 'holiday' ? '🏖️' : day.dayType === 'sick' ? '🤒' : day.dayType === 'vacation' ? '✈️' : '📋'}</span>
								{:else if day.targetMinutes > 0 && !day.isToday && day.date < new Date()}
									<span class="text-xs text-base-content/20 mt-0.5">—</span>
								{/if}
							{/if}
						</button>
					{/each}
				</div>
			</div>

			<!-- Legend -->
			<div class="flex gap-4 flex-wrap text-xs text-base-content/60 mb-6">
				<span class="flex items-center gap-1.5"><span class="w-2.5 h-2.5 rounded-sm shrink-0 bg-primary/30"></span> Work Done</span>
				{#if holidayDates.size > 0}<span class="flex items-center gap-1.5"><span class="w-2.5 h-2.5 rounded-sm shrink-0 bg-secondary/40"></span> Holiday</span>{/if}
				{#if sickDayDates.size > 0}<span class="flex items-center gap-1.5"><span class="w-2.5 h-2.5 rounded-sm shrink-0 bg-error/40"></span> Sick Day</span>{/if}
				{#if vacationDates.size > 0}<span class="flex items-center gap-1.5"><span class="w-2.5 h-2.5 rounded-sm shrink-0 bg-accent/40"></span> Vacation</span>{/if}
				{#if otherAbsenceDates.size > 0}<span class="flex items-center gap-1.5"><span class="w-2.5 h-2.5 rounded-sm shrink-0 bg-base-content/30"></span> Other Absence</span>{/if}
				<span class="flex items-center gap-1.5"><span class="w-2.5 h-2.5 rounded-sm shrink-0 bg-success/50"></span> Target Met</span>
				<span class="flex items-center gap-1.5"><span class="w-2.5 h-2.5 rounded-sm shrink-0 bg-warning/30"></span> Under Target</span>
			</div>

			<!-- Daily breakdown for month -->
			<div class="card bg-base-100 border border-base-300 p-4">
				<h3 class="text-[0.9375rem] font-bold mb-3 m-0">Daily Breakdown</h3>
				<div class="flex flex-col gap-1.5">
					{#each calendarDays.filter(d => d.isCurrentMonth && (d.workedMinutes > 0 || d.targetMinutes > 0 || d.dayType)) as day}
						{@const dayName = day.date.toLocaleDateString('en-US', { weekday: 'short' })}
						<button
							type="button"
							class="flex items-center gap-2 py-1 px-1.5 rounded text-left transition-colors {day.isToday ? 'font-semibold' : ''} {day.key === selectedDayKey ? 'ring-2 ring-primary/30 bg-primary/10' : ''} {day.dayType === 'holiday' ? 'bg-secondary/5' : day.dayType === 'sick' ? 'bg-error/5' : day.dayType === 'vacation' ? 'bg-accent/5' : day.dayType === 'other-absence' ? 'bg-base-content/5' : ''}"
							onclick={() => selectDay(day, false)}
						>
							<span class="w-7 text-xs text-base-content/60 shrink-0">{dayName}</span>
							<span class="w-5 text-xs text-base-content/40 shrink-0 text-right">{day.dayOfMonth}</span>
							{#if day.dayType}
								<span class="w-2 h-2 rounded-full shrink-0 {day.dayType === 'holiday' ? 'bg-secondary' : day.dayType === 'sick' ? 'bg-error' : day.dayType === 'vacation' ? 'bg-accent' : 'bg-base-content/40'}" title={day.dayTypeLabel}></span>
							{:else}
								<span class="w-2 shrink-0"></span>
							{/if}
							<div class="flex-1 h-1.5 bg-base-200 rounded-full overflow-hidden">
								<div class="h-full {day.workedMinutes > day.targetMinutes && day.targetMinutes > 0 ? 'bg-success' : 'bg-primary'} rounded-full transition-all duration-300" style="width: {barWidth(day.workedMinutes, day.targetMinutes)}%"></div>
							</div>
							<span class="text-xs text-base-content/70 min-w-[36px] text-right">{formatHours(day.workedMinutes)}</span>
							{#if day.targetMinutes > 0}
								<span class="text-xs text-base-content/40">/ {formatHours(day.targetMinutes)}</span>
							{/if}
						</button>
					{/each}
				</div>
			</div>

			{#if selectedDay}
				{@const dayDetails = selectedDay}
				{@const dayEntries = selectedDayEntries}
				{@const showUnderTarget = isPastOrToday(dayDetails.date) && dayDetails.targetMinutes > 0 && dayDetails.delta < 0}
				<div class="card bg-base-100 border border-base-300 p-4 mt-4">
					<div class="flex flex-wrap items-center justify-between gap-2 mb-3">
						<h3 class="text-[0.9375rem] font-bold m-0">Day Details</h3>
						<span class="text-sm text-base-content/70">{formatDateFull(dayDetails.date)}</span>
					</div>

					<div class="flex flex-wrap gap-2 mb-4">
						<span class="badge badge-outline">Worked {formatHours(dayDetails.workedMinutes)}</span>
						{#if dayDetails.creditedMinutes > 0}
							<span class="badge badge-secondary badge-outline">Credits {formatHours(dayDetails.creditedMinutes)}</span>
						{/if}
						<span class="badge badge-outline">Target {formatHours(dayDetails.targetMinutes)}</span>
						<span class={"badge " + (dayDetails.delta > 0 ? 'badge-success' : dayDetails.delta < 0 ? 'badge-error' : 'badge-ghost')}>
							{formatDelta(dayDetails.delta)}
						</span>
						{#if showUnderTarget}
							<span class="badge badge-warning">Under target</span>
						{/if}
						{#if dayDetails.dayType}
							<span class={"badge " + (dayDetails.dayType === 'holiday' ? 'badge-secondary' : dayDetails.dayType === 'sick' ? 'badge-error' : dayDetails.dayType === 'vacation' ? 'badge-accent' : 'badge-ghost')}>
								{dayDetails.dayTypeLabel}
							</span>
						{/if}
					</div>

					{#if isSelf}
						{#if editor.actionError}
							<div class="alert alert-error text-sm mb-3 py-2 px-3">{editor.actionError}</div>
						{/if}
						{#if editor.requestSuccess}
							<div class="alert alert-success text-sm mb-3 py-2 px-3">{editor.requestSuccess}</div>
						{/if}
					{/if}

					{#if dayEntries.length > 0}
						<ul class="flex flex-col gap-2">
							{#each dayEntries as entry}
								<li class="rounded-lg border border-base-300 bg-base-200/40 overflow-hidden">
									{#if isSelf && editor.editingEntryId === entry.id}
										<EntryEditForm
											bind:startTime={editor.editStartTime}
											bind:endTime={editor.editEndTime}
											bind:description={editor.editDescription}
											bind:pause={editor.editPause}
											error={editor.editError}
											saving={editor.editSaving}
											{orgDetail}
											onsave={() => editor.saveEditEntry(entry.id!)}
											oncancel={editor.cancelEditEntry}
										/>
									{:else}
										<!-- Entry display row -->
										<div class="flex items-start justify-between gap-3 px-3 py-2">
											<div class="flex-1">
												<div class="text-sm font-medium text-base-content">
													{formatTime(entry.startTime!)}{entry.endTime ? ` - ${formatTime(entry.endTime)}` : ' - Running'}
												</div>
												{#if entry.description}
													<div class="text-xs text-base-content/60">{entry.description}</div>
												{/if}
												<!-- Pause display -->
												<div class="flex flex-wrap gap-1 mt-1">
													{#if (entry.pauseDurationMinutes ?? 0) > 0}
														{#if isSelf && canEditPause(orgDetail) && !entry.isRunning}
															<button class="badge badge-warning badge-sm badge-outline cursor-pointer hover:bg-warning/20" title="Click to edit pause" onclick={() => editor.startEditEntry(entry)}>&#8722;{entry.pauseDurationMinutes}m pause &#9998;</button>
														{:else if isSelf && canRequestEditPause(orgDetail) && !entry.isRunning}
															<button class="badge badge-warning badge-sm badge-outline border-dashed border-warning cursor-pointer hover:bg-warning/10" title="Request pause edit" onclick={() => editor.startRequest(entry.id!, 'pause')}>&#8722;{entry.pauseDurationMinutes}m pause &#128233;</button>
														{:else}
															<span class="badge badge-warning badge-sm badge-outline">&#8722;{entry.pauseDurationMinutes}m pause</span>
														{/if}
													{:else if isSelf && canEditPause(orgDetail) && !entry.isRunning}
														<button class="badge badge-ghost badge-sm cursor-pointer hover:badge-warning hover:badge-outline" title="Click to add pause" onclick={() => editor.startEditEntry(entry)}>+pause &#9998;</button>
													{:else if isSelf && canRequestEditPause(orgDetail) && !entry.isRunning}
														<button class="badge badge-ghost badge-sm cursor-pointer border-dashed border-warning hover:bg-warning/10" title="Request to add pause" onclick={() => editor.startRequest(entry.id!, 'pause')}>+pause &#128233;</button>
													{/if}
												</div>
											</div>
											<div class="flex items-center gap-1">
												{#if entry.isFailure}
													<span class="badge badge-error badge-sm" title="The timer ran past the business day end and was stopped automatically. It counts as 0 until you correct the end time.">Needs correction</span>
												{:else}
													<span class="badge badge-outline badge-sm">
														{formatDuration(entry.netDurationMinutes ?? entry.durationMinutes ?? undefined)}
													</span>
												{/if}
												<!-- Edit / Delete buttons (own entries only) -->
												{#if isSelf && !entry.isRunning}
													<div class="flex gap-0.5 ml-1">
														{#if canEditEntries(orgDetail)}
															<button class="btn btn-ghost btn-xs text-primary opacity-40 hover:opacity-100" title="Edit" onclick={() => editor.startEditEntry(entry)}>&#9998;</button>
														{:else if canRequestEditEntries(orgDetail)}
															<button class="btn btn-ghost btn-xs text-warning opacity-50 hover:opacity-100" title="Request edit" onclick={() => editor.startRequest(entry.id!, 'edit')}>&#128233;</button>
														{/if}
														<button class="btn btn-ghost btn-xs text-error opacity-40 hover:opacity-100" title="Delete" onclick={() => editor.deleteEntry(entry.id!)}>&times;</button>
													</div>
												{/if}
											</div>
										</div>
									{/if}
								</li>
							{/each}
						</ul>
					{:else}
						<p class="text-sm text-base-content/60 m-0">No tracked entries for this day.</p>
					{/if}
				</div>
			{/if}

			<AbsenceDialog
				open={showAbsenceDialog}
				{orgSlug}
				date={absenceDialogDate}
				fixedUserId={userId}
				isAdmin={isOrgAdmin}
				{mustRequest}
				{absenceTypes}
				onclose={() => (showAbsenceDialog = false)}
				onsaved={onAbsenceSaved}
			/>

			{#if isSelf}
				<RequestModal
					open={!!editor.requestingEntryId}
					type={editor.requestType}
					bind:newStart={editor.requestNewStart}
					bind:newEnd={editor.requestNewEnd}
					bind:newPause={editor.requestNewPause}
					bind:message={editor.requestMessage}
					sending={editor.requestSending}
					onsubmit={editor.submitRequest}
					oncancel={editor.cancelRequest}
				/>
			{/if}
		</div>

	{:else}
		<!-- YEAR VIEW -->
		<div class="year-view">
			<div class="flex items-center justify-center gap-3 mb-5">
				<button class="btn btn-ghost btn-sm btn-square" onclick={prevYear}>&lsaquo;</button>
				<h2 class="text-lg font-bold min-w-[180px] text-center m-0">{currentYear}</h2>
				<button class="btn btn-ghost btn-sm btn-square" onclick={nextYear}>&rsaquo;</button>
			</div>

			{#if yearLoading}
				<LoadingSpinner message="Loading year data..." size="sm" />
			{:else}
				<!-- Year summary -->
				{@const yearTotal = yearMonths.reduce((s, m) => s + m.workedMinutes, 0)}
				{@const yearCredits = yearMonths.reduce((s, m) => s + m.creditedMinutes, 0)}
				{@const yearTarget = yearMonths.reduce((s, m) => s + m.targetMinutes, 0)}
				{@const yearDelta = yearTotal + yearCredits - yearTarget}
				{@const yearHolidays = yearMonths.reduce((s, m) => s + m.holidays, 0)}
				{@const yearSick = yearMonths.reduce((s, m) => s + m.sickDays, 0)}
				{@const yearVacation = yearMonths.reduce((s, m) => s + m.vacationDays, 0)}

				<div class="grid grid-cols-[repeat(auto-fit,minmax(120px,1fr))] gap-3 mb-4">
					<div class="bg-base-100 border border-base-300 rounded-lg p-3 text-center">
						<span class="block text-xl font-bold text-base-content">{formatHours(yearTotal)}</span>
						<span class="text-xs text-base-content/60">Total Worked</span>
					</div>
					<div class="bg-base-100 border border-base-300 rounded-lg p-3 text-center">
						<span class="block text-xl font-bold text-base-content">{formatHours(yearCredits)}</span>
						<span class="text-xs text-base-content/60">Credits</span>
					</div>
					<div class="bg-base-100 border border-base-300 rounded-lg p-3 text-center">
						<span class="block text-xl font-bold text-base-content">{formatHours(yearTarget)}</span>
						<span class="text-xs text-base-content/60">Total Target</span>
					</div>
					<div class="bg-base-100 border border-base-300 rounded-lg p-3 text-center">
						<span class="block text-xl font-bold {yearDelta > 0 ? 'text-success' : yearDelta < 0 ? 'text-error' : 'text-base-content'}">{formatDelta(yearDelta)}</span>
						<span class="text-xs text-base-content/60">Year Balance</span>
					</div>
					<div class="bg-base-100 border border-base-300 rounded-lg p-3 text-center border-l-3 border-l-secondary">
						<span class="block text-xl font-bold {getYearCumulative(currentYear) > 0 ? 'text-success' : getYearCumulative(currentYear) < 0 ? 'text-error' : 'text-base-content'}">{formatDelta(getYearCumulative(currentYear))}</span>
						<span class="text-xs text-base-content/60">Cumulative</span>
					</div>
				</div>

				<div class="flex flex-wrap gap-2 mb-5">
					{#if yearHolidays > 0}<span class="badge badge-secondary badge-sm">{yearHolidays} Holidays</span>{/if}
					{#if yearSick > 0}<span class="badge badge-error badge-sm">{yearSick} Sick Days</span>{/if}
					{#if yearVacation > 0}<span class="badge badge-accent badge-sm">{yearVacation} Vacation Days</span>{/if}
				</div>

				<!-- Monthly cards -->
				<div class="grid grid-cols-[repeat(auto-fill,minmax(200px,1fr))] gap-3">
					{#each yearMonths as mo}
						{@const isCurrent = mo.month === new Date().getMonth() && currentYear === new Date().getFullYear()}
						{@const moKey = `${currentYear}-${String(mo.month + 1).padStart(2, '0')}`}
						{@const moCumulative = monthlyCumulativeBalances.get(moKey)}
						<button class="bg-base-100 border rounded-lg p-3.5 cursor-pointer text-left transition-all w-full hover:border-primary/30 hover:shadow-md {isCurrent ? 'border-primary shadow-[0_0_0_1px] shadow-primary' : 'border-base-300'}" onclick={() => { currentMonth = mo.month; viewMode = 'month'; loadEntries(); }}>
							<div class="flex justify-between items-center mb-2">
								<span class="font-semibold text-sm text-base-content">{mo.name}</span>
								{#if mo.workedMinutes > 0 || mo.creditedMinutes > 0}
									<span class="text-xs font-medium {mo.delta > 0 ? 'text-success' : mo.delta < 0 ? 'text-error' : ''}">{formatDelta(mo.delta)}</span>
								{/if}
							</div>
							<div class="h-1.5 bg-base-200 rounded-full overflow-hidden mb-2">
								<div class="h-full {mo.workedMinutes > mo.targetMinutes && mo.targetMinutes > 0 ? 'bg-success' : 'bg-primary'} rounded-full transition-all duration-300" style="width: {barWidth(mo.workedMinutes, mo.targetMinutes)}%"></div>
							</div>
							<div class="text-sm text-base-content/70 mb-1.5">
								<span class="font-semibold">{formatHours(mo.workedMinutes)}</span>
								{#if mo.targetMinutes > 0}
									<span class="text-base-content/40">/ {formatHours(mo.targetMinutes)}</span>
								{/if}
							</div>
							{#if mo.creditedMinutes > 0}
								<div class="text-xs text-base-content/60 mb-1">+{formatHours(mo.creditedMinutes)} credits</div>
							{/if}
							{#if moCumulative !== undefined}
								<div class="text-xs font-semibold mb-1 {moCumulative > 0 ? 'text-success' : moCumulative < 0 ? 'text-error' : 'text-base-content/60'}">
									Σ {formatDelta(moCumulative)}
								</div>
							{/if}
							<div class="flex gap-1.5 flex-wrap">
								{#if mo.holidays > 0}<span class="text-xs">{mo.holidays}🏖️</span>{/if}
								{#if mo.sickDays > 0}<span class="text-xs">{mo.sickDays}🤒</span>{/if}
								{#if mo.vacationDays > 0}<span class="text-xs">{mo.vacationDays}✈️</span>{/if}
							</div>
						</button>
					{/each}
				</div>
			{/if}
		</div>
	{/if}
</div>
