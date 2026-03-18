<script lang="ts">
	import { onMount } from 'svelte';
	import { orgContext } from '$lib/stores/orgContext.svelte';
	import { auth } from '$lib/stores/auth.svelte';
	import { timeTrackingApi, workScheduleApi, holidayApi, absenceDayApi } from '$lib/apiClient';
	import type { TimeEntryResponse, WorkScheduleResponse } from '$lib/api';
	import { formatHours, formatDelta, formatDuration, formatTime, formatDateFull, barWidth } from '$lib/utils/formatters';
	import { dateKey } from '$lib/utils/dateHelpers';
	import { getDayTarget, getAbsenceCredit, getDayType, getDayTypeLabel, getHeatColor } from '$lib/utils/scheduleHelpers';

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
	let holidayDates = $state<Map<string, string>>(new Map());
	let sickDayDates = $state<Set<string>>(new Set());
	let vacationDates = $state<Set<string>>(new Set());
	let otherAbsenceDates = $state<Set<string>>(new Set());
	let daysOffSet = $state<Set<string>>(new Set());

	// Year view data
	let yearEntries = $state<TimeEntryResponse[]>([]);
	let yearLoading = $state(false);

	// Cumulative balance
	let allTimeEntries = $state<TimeEntryResponse[]>([]);
	let cumulativeBalance = $state(0); // overall cumulative balance in minutes
	let monthlyCumulativeBalances = $state<Map<string, number>>(new Map()); // "YYYY-MM" -> cumulative balance
	let selectedDayKey = $state<string | null>(null);

	// Computed
	const monthName = $derived(new Date(currentYear, currentMonth).toLocaleDateString('en-US', { month: 'long' }));

	// Calendar grid for month view
	const calendarDays = $derived(buildCalendarDays(currentYear, currentMonth, entries));
	const monthStats = $derived(computeMonthStats(calendarDays));

	// Year overview
	const yearMonths = $derived(viewMode === 'year' ? buildYearMonths(currentYear, yearEntries) : []);

	// Current month's cumulative balance
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

	let prevOrgSlug: string | null | undefined = undefined;

	onMount(async () => {
		if (orgContext.selectedOrgSlug) {
			await loadAll();
		}
		loading = false;
	});

	$effect(() => {
		const currentSlug = orgContext.selectedOrgSlug;
		if (currentSlug && currentSlug !== prevOrgSlug) {
			loadAll();
		}
		prevOrgSlug = currentSlug;
	});

	async function loadAll() {
		loading = true;
		await Promise.all([
			loadEntries(),
			loadWorkSchedule(),
			loadDaysOff(),
			loadSchedulePeriods()
		]);
		await loadCumulativeBalance();
		if (viewMode === 'year') await loadYearEntries();
		loading = false;
	}

	async function loadEntries() {
		try {
			const from = new Date(currentYear, currentMonth, 1);
			const to = new Date(currentYear, currentMonth + 1, 0, 23, 59, 59, 999);
			const orgId = orgContext.selectedOrgId ?? undefined;
			const { data } = await timeTrackingApi.apiV1TimeTrackingGet(orgId, from.toISOString(), to.toISOString(), 5000);
			entries = data.items ?? [];
		} catch {
			entries = [];
		} finally {
			ensureSelectedDayForCurrentMonth();
		}
	}

	async function loadYearEntries() {
		yearLoading = true;
		try {
			const from = new Date(currentYear, 0, 1);
			const to = new Date(currentYear, 11, 31, 23, 59, 59, 999);
			const orgId = orgContext.selectedOrgId ?? undefined;
			const { data } = await timeTrackingApi.apiV1TimeTrackingGet(orgId, from.toISOString(), to.toISOString(), 50000);
			yearEntries = data.items ?? [];
		} catch { yearEntries = []; }
		yearLoading = false;
	}

	async function loadWorkSchedule() {
		if (!orgContext.selectedOrgSlug) { workSchedule = null; return; }
		try {
			const { data } = await workScheduleApi.apiV1OrganizationsSlugWorkScheduleGet(orgContext.selectedOrgSlug);
			workSchedule = data;
		} catch { workSchedule = null; }
	}

	async function loadSchedulePeriods() {
		if (!orgContext.selectedOrgSlug) { schedulePeriods = []; return; }
		try {
			const { data } = await workScheduleApi.apiV1OrganizationsSlugWorkSchedulesGet(orgContext.selectedOrgSlug);
			schedulePeriods = data;
		} catch { schedulePeriods = []; }
	}

	async function loadDaysOff() {
		if (!orgContext.selectedOrgSlug) {
			holidayDates = new Map(); sickDayDates = new Set(); vacationDates = new Set(); otherAbsenceDates = new Set(); daysOffSet = new Set();
			return;
		}
		try {
			const [holRes, absRes] = await Promise.all([
				holidayApi.apiV1OrganizationsSlugHolidaysGet(orgContext.selectedOrgSlug),
				absenceDayApi.apiV1OrganizationsSlugAbsencesGet(orgContext.selectedOrgSlug, auth.user?.id)
			]);
			const off = new Set<string>();
			const hDates = new Map<string, string>();
			const sDates = new Set<string>();
			const vDates = new Set<string>();
			const oDates = new Set<string>();
			for (const h of holRes.data) {
				if (h.date) { off.add(h.date); hDates.set(h.date, h.name ?? 'Holiday'); }
			}
			const absences = absRes.data.items ?? [];
			for (const a of absences) {
				if (a.date) {
					off.add(a.date);
					if (a.type === 'SickDay') sDates.add(a.date);
					else if (a.type === 'Vacation') vDates.add(a.date);
					else oDates.add(a.date);
				}
			}
			daysOffSet = off;
			holidayDates = hDates;
			sickDayDates = sDates;
			vacationDates = vDates;
			otherAbsenceDates = oDates;
		} catch {
			holidayDates = new Map(); sickDayDates = new Set(); vacationDates = new Set(); otherAbsenceDates = new Set(); daysOffSet = new Set();
		}
	}

	async function loadCumulativeBalance() {
		if (!orgContext.selectedOrgId || !workSchedule) {
			cumulativeBalance = 0;
			monthlyCumulativeBalances = new Map();
			return;
		}
		try {
			const orgId = orgContext.selectedOrgId;
			const { data: allEntriesPage } = await timeTrackingApi.apiV1TimeTrackingGet(orgId, undefined, undefined, 50000);
			const allEntries = allEntriesPage.items ?? [];
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

			// Group entries by month
			const monthlyWorked = new Map<string, number>();
			for (const e of sorted) {
				const d = new Date(e.startTime!);
				const mk = `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`;
				monthlyWorked.set(mk, (monthlyWorked.get(mk) ?? 0) + (e.netDurationMinutes ?? e.durationMinutes ?? 0));
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
					target += getDayTarget(d, workSchedule, holidayDates, schedulePeriods);
					absCredits += getAbsenceCredit(d, workSchedule, holidayDates, sickDayDates, vacationDates, otherAbsenceDates, schedulePeriods);
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

	// Navigate month
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

	function selectDay(day: CalendarDay) {
		if (!day.isCurrentMonth) return;
		selectedDayKey = day.key;
	}

	function isPastOrToday(date: Date): boolean {
		const endOfToday = new Date();
		endOfToday.setHours(23, 59, 59, 999);
		return date <= endOfToday;
	}

	// Helpers

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
		const entryMap = new Map<string, { minutes: number; count: number }>();
		for (const entry of entries) {
			if (entry.isRunning || !entry.startTime) continue;
			const d = new Date(entry.startTime);
			const key = dateKey(d);
			const existing = entryMap.get(key) ?? { minutes: 0, count: 0 };
			existing.minutes += entry.netDurationMinutes ?? entry.durationMinutes ?? 0;
			existing.count++;
			entryMap.set(key, existing);
		}

		const days: CalendarDay[] = [];
		const cursor = new Date(calStart);
		while (cursor <= calEnd) {
			const key = dateKey(cursor);
			const isCurrentMonth = cursor.getMonth() === month;
			const isToday = cursor.toDateString() === today.toDateString();
			const isWeekend = cursor.getDay() === 0 || cursor.getDay() === 6;
			const entryData = entryMap.get(key) ?? { minutes: 0, count: 0 };
			const targetMinutes = isCurrentMonth ? getDayTarget(new Date(cursor), workSchedule, holidayDates, schedulePeriods) : 0;
			const absCredit = isCurrentMonth ? getAbsenceCredit(new Date(cursor), workSchedule, holidayDates, sickDayDates, vacationDates, otherAbsenceDates, schedulePeriods) : 0;

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
				dayType: getDayType(key, holidayDates, sickDayDates, vacationDates, otherAbsenceDates),
				dayTypeLabel: getDayTypeLabel(key, holidayDates, sickDayDates, vacationDates, otherAbsenceDates),
				delta: entryData.minutes + absCredit - targetMinutes
			});

			cursor.setDate(cursor.getDate() + 1);
		}
		return days;
	}

	function computeMonthStats(days: CalendarDay[]) {
		const currentDays = days.filter(d => d.isCurrentMonth);
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
		const months: YearMonth[] = [];
		for (let m = 0; m < 12; m++) {
			const monthName = new Date(year, m).toLocaleDateString('en-US', { month: 'short' });
			const firstDay = new Date(year, m, 1);
			const lastDay = new Date(year, m + 1, 0);

			// Count work days, holidays, sick days, vacation days
			let workDays = 0, holidays = 0, sickDays = 0, vacationDays = 0, targetMinutes = 0, absenceCredits = 0;
			const cursor = new Date(firstDay);
			while (cursor <= lastDay) {
				const key = dateKey(cursor);
				const dt = getDayType(key, holidayDates, sickDayDates, vacationDates, otherAbsenceDates);
				if (dt === 'holiday') holidays++;
				else if (dt === 'sick') sickDays++;
				else if (dt === 'vacation') vacationDays++;

				const target = getDayTarget(new Date(cursor), workSchedule, holidayDates, schedulePeriods);
				if (target > 0) workDays++;
				targetMinutes += target;
				absenceCredits += getAbsenceCredit(new Date(cursor), workSchedule, holidayDates, sickDayDates, vacationDates, otherAbsenceDates, schedulePeriods);

				cursor.setDate(cursor.getDate() + 1);
			}

			// Sum entries for this month
			let workedMinutes = 0, entryCount = 0;
			for (const entry of entries) {
				if (entry.isRunning || !entry.startTime) continue;
				const d = new Date(entry.startTime);
				if (d.getMonth() === m && d.getFullYear() === year) {
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

<div class="max-w-4xl mx-auto py-6 px-4">
	<div class="flex items-center justify-between mb-6">
		<h1 class="text-2xl font-bold text-base-content m-0">History</h1>
		<div class="join">
			<button class="join-item btn btn-sm {viewMode === 'month' ? 'btn-primary' : 'btn-ghost'}" onclick={() => switchView('month')}>Month</button>
			<button class="join-item btn btn-sm {viewMode === 'year' ? 'btn-primary' : 'btn-ghost'}" onclick={() => switchView('year')}>Year</button>
		</div>
	</div>

	{#if loading}
		<div class="flex items-center gap-3 justify-center py-12 text-base-content/50"><span class="loading loading-spinner loading-sm"></span><span>Loading...</span></div>
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
					<span class="text-center text-xs font-semibold text-base-content/60 py-2">Mon</span>
					<span class="text-center text-xs font-semibold text-base-content/60 py-2">Tue</span>
					<span class="text-center text-xs font-semibold text-base-content/60 py-2">Wed</span>
					<span class="text-center text-xs font-semibold text-base-content/60 py-2">Thu</span>
					<span class="text-center text-xs font-semibold text-base-content/60 py-2">Fri</span>
					<span class="text-center text-xs font-semibold text-base-content/60 py-2">Sat</span>
					<span class="text-center text-xs font-semibold text-base-content/60 py-2">Sun</span>
				</div>
				<div class="grid grid-cols-7">
					{#each calendarDays as day}
						<button
							type="button"
							class="min-h-[72px] p-1.5 border-r border-b border-base-200 [&:nth-child(7n)]:border-r-0 flex flex-col gap-0.5 relative transition-colors text-left {!day.isCurrentMonth ? 'opacity-30' : ''} {day.isWeekend ? 'bg-base-200/30' : ''} {day.isToday ? 'outline-2 outline-primary -outline-offset-2 rounded' : ''} {day.key === selectedDayKey ? 'ring-2 ring-primary/40 bg-primary/10' : ''} {day.dayType === 'holiday' ? 'bg-secondary/10' : day.dayType === 'sick' ? 'bg-error/10' : day.dayType === 'vacation' ? 'bg-accent/10' : day.dayType === 'other-absence' ? 'bg-base-content/10' : ''}"
							title={day.dayTypeLabel || (day.workedMinutes > 0 ? `${formatHours(day.workedMinutes)} worked` : '')}
							onclick={() => selectDay(day)}
							disabled={!day.isCurrentMonth}
						>
							<span class="text-xs font-medium {day.isToday ? 'text-primary font-bold' : 'text-base-content/70'}">{day.dayOfMonth}</span>
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
							onclick={() => selectDay(day)}
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

					{#if dayEntries.length > 0}
						<ul class="flex flex-col gap-2">
							{#each dayEntries as entry}
								<li class="flex items-start justify-between gap-3 rounded-lg border border-base-300 bg-base-200/40 px-3 py-2">
									<div>
										<div class="text-sm font-medium text-base-content">
											{formatTime(entry.startTime!)}{entry.endTime ? ` - ${formatTime(entry.endTime)}` : ' - Running'}
										</div>
										{#if entry.description}
											<div class="text-xs text-base-content/60">{entry.description}</div>
										{/if}
									</div>
									<span class="badge badge-outline badge-sm">
										{formatDuration(entry.netDurationMinutes ?? entry.durationMinutes ?? undefined)}
									</span>
								</li>
							{/each}
						</ul>
					{:else}
						<p class="text-sm text-base-content/60 m-0">No tracked entries for this day.</p>
					{/if}
				</div>
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
				<div class="flex items-center gap-3 justify-center py-12 text-base-content/50"><span class="loading loading-spinner loading-sm"></span><span>Loading year data...</span></div>
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
