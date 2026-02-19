<script lang="ts">
	import { onMount } from 'svelte';
	import { orgContext } from '$lib/stores/orgContext.svelte';
	import { timeTrackingApi, workScheduleApi, holidayApi, absenceDayApi, workSchedulePeriodApi } from '$lib/apiClient';
	import type { TimeEntryResponse, WorkScheduleResponse, WorkSchedulePeriodResponse } from '$lib/api';

	// View mode
	let viewMode = $state<'month' | 'year'>('month');

	// Navigation
	let currentYear = $state(new Date().getFullYear());
	let currentMonth = $state(new Date().getMonth()); // 0-indexed

	// Data
	let loading = $state(true);
	let entries = $state<TimeEntryResponse[]>([]);
	let workSchedule = $state<WorkScheduleResponse | null>(null);
	let schedulePeriods = $state<WorkSchedulePeriodResponse[]>([]);

	// Day-type maps
	let holidayDates = $state<Map<string, string>>(new Map());
	let sickDayDates = $state<Set<string>>(new Set());
	let vacationDates = $state<Set<string>>(new Set());
	let otherAbsenceDates = $state<Set<string>>(new Set());
	let daysOffSet = $state<Set<string>>(new Set());

	// Year view data
	let yearEntries = $state<TimeEntryResponse[]>([]);
	let yearLoading = $state(false);

	// Computed
	const monthName = $derived(new Date(currentYear, currentMonth).toLocaleDateString('en-US', { month: 'long' }));

	// Calendar grid for month view
	const calendarDays = $derived(buildCalendarDays(currentYear, currentMonth, entries));
	const monthStats = $derived(computeMonthStats(calendarDays));

	// Year overview
	const yearMonths = $derived(viewMode === 'year' ? buildYearMonths(currentYear, yearEntries) : []);

	let prevOrgId: number | null | undefined = undefined;

	onMount(async () => {
		await loadAll();
		loading = false;
	});

	$effect(() => {
		const currentOrgId = orgContext.selectedOrgId;
		if (prevOrgId !== undefined && prevOrgId !== currentOrgId) {
			loadAll();
		}
		prevOrgId = currentOrgId;
	});

	async function loadAll() {
		loading = true;
		await Promise.all([
			loadEntries(),
			loadWorkSchedule(),
			loadDaysOff(),
			loadSchedulePeriods()
		]);
		if (viewMode === 'year') await loadYearEntries();
		loading = false;
	}

	async function loadEntries() {
		try {
			const from = new Date(currentYear, currentMonth, 1);
			const to = new Date(currentYear, currentMonth + 1, 0, 23, 59, 59, 999);
			const orgId = orgContext.selectedOrgId ?? undefined;
			const { data } = await timeTrackingApi.apiTimeTrackingGet(orgId, from.toISOString(), to.toISOString(), 5000);
			entries = data;
		} catch { entries = []; }
	}

	async function loadYearEntries() {
		yearLoading = true;
		try {
			const from = new Date(currentYear, 0, 1);
			const to = new Date(currentYear, 11, 31, 23, 59, 59, 999);
			const orgId = orgContext.selectedOrgId ?? undefined;
			const { data } = await timeTrackingApi.apiTimeTrackingGet(orgId, from.toISOString(), to.toISOString(), 50000);
			yearEntries = data;
		} catch { yearEntries = []; }
		yearLoading = false;
	}

	async function loadWorkSchedule() {
		if (!orgContext.selectedOrgSlug) { workSchedule = null; return; }
		try {
			const { data } = await workScheduleApi.apiOrganizationsSlugWorkScheduleGet(orgContext.selectedOrgSlug);
			workSchedule = data;
		} catch { workSchedule = null; }
	}

	async function loadSchedulePeriods() {
		if (!orgContext.selectedOrgSlug) { schedulePeriods = []; return; }
		try {
			const { data } = await workSchedulePeriodApi.apiOrganizationsSlugSchedulePeriodsGet(orgContext.selectedOrgSlug);
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
				holidayApi.apiOrganizationsSlugHolidaysGet(orgContext.selectedOrgSlug),
				absenceDayApi.apiOrganizationsSlugAbsencesGet(orgContext.selectedOrgSlug)
			]);
			const off = new Set<string>();
			const hDates = new Map<string, string>();
			const sDates = new Set<string>();
			const vDates = new Set<string>();
			const oDates = new Set<string>();
			for (const h of holRes.data) {
				if (h.date) { off.add(h.date); hDates.set(h.date, h.name ?? 'Holiday'); }
			}
			for (const a of absRes.data) {
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

	// Helpers
	function dateKey(d: Date): string {
		return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
	}

	function getDayType(key: string): string | null {
		if (holidayDates.has(key)) return 'holiday';
		if (sickDayDates.has(key)) return 'sick';
		if (vacationDates.has(key)) return 'vacation';
		if (otherAbsenceDates.has(key)) return 'other-absence';
		return null;
	}

	function getDayTypeLabel(key: string): string {
		if (holidayDates.has(key)) return holidayDates.get(key) ?? 'Holiday';
		if (sickDayDates.has(key)) return 'Sick Day';
		if (vacationDates.has(key)) return 'Vacation';
		if (otherAbsenceDates.has(key)) return 'Other Absence';
		return '';
	}

	function getDayTarget(date: Date): number {
		if (!workSchedule) return 0;
		const key = dateKey(date);
		if (daysOffSet.has(key)) return 0;
		// Check schedule periods first
		const dateOnly = key;
		for (const p of schedulePeriods) {
			if (p.validFrom && p.validFrom <= dateOnly && (!p.validTo || p.validTo >= dateOnly)) {
				const dow = date.getDay();
				const targets: Record<number, number> = {
					1: (p.targetMon ?? 0) * 60, 2: (p.targetTue ?? 0) * 60, 3: (p.targetWed ?? 0) * 60,
					4: (p.targetThu ?? 0) * 60, 5: (p.targetFri ?? 0) * 60
				};
				return targets[dow] ?? 0;
			}
		}
		const dow = date.getDay();
		const targets: Record<number, number> = {
			1: (workSchedule.targetMon ?? 0) * 60, 2: (workSchedule.targetTue ?? 0) * 60, 3: (workSchedule.targetWed ?? 0) * 60,
			4: (workSchedule.targetThu ?? 0) * 60, 5: (workSchedule.targetFri ?? 0) * 60
		};
		return targets[dow] ?? 0;
	}

	function formatHours(minutes: number): string {
		if (minutes === 0) return '0h';
		const h = Math.floor(Math.abs(minutes) / 60);
		const m = Math.round(Math.abs(minutes) % 60);
		const sign = minutes < 0 ? '-' : '';
		if (m === 0) return `${sign}${h}h`;
		if (h === 0) return `${sign}${m}m`;
		return `${sign}${h}h ${m}m`;
	}

	function formatDelta(minutes: number): string {
		const sign = minutes >= 0 ? '+' : '';
		return sign + formatHours(minutes);
	}

	interface CalendarDay {
		date: Date;
		key: string;
		dayOfMonth: number;
		isCurrentMonth: boolean;
		isToday: boolean;
		isWeekend: boolean;
		workedMinutes: number;
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
			const targetMinutes = isCurrentMonth ? getDayTarget(new Date(cursor)) : 0;

			days.push({
				date: new Date(cursor),
				key,
				dayOfMonth: cursor.getDate(),
				isCurrentMonth,
				isToday,
				isWeekend,
				workedMinutes: entryData.minutes,
				targetMinutes,
				entryCount: entryData.count,
				dayType: getDayType(key),
				dayTypeLabel: getDayTypeLabel(key),
				delta: entryData.minutes - targetMinutes
			});

			cursor.setDate(cursor.getDate() + 1);
		}
		return days;
	}

	function computeMonthStats(days: CalendarDay[]) {
		const currentDays = days.filter(d => d.isCurrentMonth);
		const totalWorked = currentDays.reduce((s, d) => s + d.workedMinutes, 0);
		const totalTarget = currentDays.reduce((s, d) => s + d.targetMinutes, 0);
		const totalDelta = totalWorked - totalTarget;
		const workDays = currentDays.filter(d => d.targetMinutes > 0).length;
		const workedDays = currentDays.filter(d => d.workedMinutes > 0).length;
		const holidays = currentDays.filter(d => d.dayType === 'holiday').length;
		const sickDays = currentDays.filter(d => d.dayType === 'sick').length;
		const vacationDays = currentDays.filter(d => d.dayType === 'vacation').length;
		const avgPerDay = workedDays > 0 ? totalWorked / workedDays : 0;
		return { totalWorked, totalTarget, totalDelta, workDays, workedDays, holidays, sickDays, vacationDays, avgPerDay };
	}

	interface YearMonth {
		month: number;
		name: string;
		workedMinutes: number;
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
			let workDays = 0, holidays = 0, sickDays = 0, vacationDays = 0, targetMinutes = 0;
			const cursor = new Date(firstDay);
			while (cursor <= lastDay) {
				const key = dateKey(cursor);
				const dt = getDayType(key);
				if (dt === 'holiday') holidays++;
				else if (dt === 'sick') sickDays++;
				else if (dt === 'vacation') vacationDays++;

				const target = getDayTarget(new Date(cursor));
				if (target > 0) workDays++;
				targetMinutes += target;

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
				targetMinutes,
				delta: workedMinutes - targetMinutes,
				workDays,
				holidays,
				sickDays,
				vacationDays,
				entryCount
			});
		}
		return months;
	}

	function getHeatColor(minutes: number, target: number): string {
		if (minutes === 0) return 'transparent';
		if (target === 0) return 'rgba(59, 130, 246, 0.3)';
		const ratio = minutes / target;
		if (ratio >= 1) return 'rgba(34, 197, 94, 0.5)';
		if (ratio >= 0.75) return 'rgba(34, 197, 94, 0.3)';
		if (ratio >= 0.5) return 'rgba(250, 204, 21, 0.3)';
		return 'rgba(239, 68, 68, 0.2)';
	}

	function barWidth(worked: number, target: number): number {
		if (target <= 0) return worked > 0 ? 100 : 0;
		return Math.min((worked / target) * 100, 100);
	}
</script>

<div class="history-page">
	<div class="history-header">
		<h1>History</h1>
		<div class="view-toggle">
			<button class="toggle-btn" class:active={viewMode === 'month'} onclick={() => switchView('month')}>Month</button>
			<button class="toggle-btn" class:active={viewMode === 'year'} onclick={() => switchView('year')}>Year</button>
		</div>
	</div>

	{#if loading}
		<div class="loading-state"><div class="spinner"></div><span>Loading...</span></div>
	{:else if viewMode === 'month'}
		<!-- MONTH VIEW -->
		<div class="month-view">
			<!-- Navigation -->
			<div class="nav-row">
				<button class="nav-btn" onclick={prevYear} title="Previous year">&laquo;</button>
				<button class="nav-btn" onclick={prevMonth} title="Previous month">&lsaquo;</button>
				<h2 class="nav-title">{monthName} {currentYear}</h2>
				<button class="nav-btn" onclick={nextMonth} title="Next month">&rsaquo;</button>
				<button class="nav-btn" onclick={nextYear} title="Next year">&raquo;</button>
			</div>

			<!-- Month stats -->
			<div class="stats-row">
				<div class="stat-card">
					<span class="stat-value">{formatHours(monthStats.totalWorked)}</span>
					<span class="stat-label">Worked</span>
				</div>
				<div class="stat-card">
					<span class="stat-value">{formatHours(monthStats.totalTarget)}</span>
					<span class="stat-label">Target</span>
				</div>
				<div class="stat-card">
					<span class="stat-value" class:positive={monthStats.totalDelta > 0} class:negative={monthStats.totalDelta < 0}>{formatDelta(monthStats.totalDelta)}</span>
					<span class="stat-label">Balance</span>
				</div>
				<div class="stat-card">
					<span class="stat-value">{formatHours(monthStats.avgPerDay)}</span>
					<span class="stat-label">Avg / Day</span>
				</div>
			</div>

			<!-- Summary badges -->
			{#if monthStats.workDays > 0 || monthStats.holidays > 0 || monthStats.sickDays > 0 || monthStats.vacationDays > 0}
				<div class="summary-badges">
					<span class="badge badge-work">{monthStats.workDays} Work Days</span>
					<span class="badge badge-worked">{monthStats.workedDays} Days Worked</span>
					{#if monthStats.holidays > 0}<span class="badge badge-holiday">{monthStats.holidays} Holiday{monthStats.holidays > 1 ? 's' : ''}</span>{/if}
					{#if monthStats.sickDays > 0}<span class="badge badge-sick">{monthStats.sickDays} Sick Day{monthStats.sickDays > 1 ? 's' : ''}</span>{/if}
					{#if monthStats.vacationDays > 0}<span class="badge badge-vacation">{monthStats.vacationDays} Vacation Day{monthStats.vacationDays > 1 ? 's' : ''}</span>{/if}
				</div>
			{/if}

			<!-- Calendar grid -->
			<div class="calendar">
				<div class="calendar-header">
					<span>Mon</span><span>Tue</span><span>Wed</span><span>Thu</span><span>Fri</span><span>Sat</span><span>Sun</span>
				</div>
				<div class="calendar-grid">
					{#each calendarDays as day}
						<div
							class="calendar-cell"
							class:other-month={!day.isCurrentMonth}
							class:today={day.isToday}
							class:weekend={day.isWeekend}
							class:has-entries={day.workedMinutes > 0}
							class:day-holiday={day.dayType === 'holiday'}
							class:day-sick={day.dayType === 'sick'}
							class:day-vacation={day.dayType === 'vacation'}
							class:day-other={day.dayType === 'other-absence'}
							title={day.dayTypeLabel || (day.workedMinutes > 0 ? `${formatHours(day.workedMinutes)} worked` : '')}
						>
							<span class="cell-day">{day.dayOfMonth}</span>
							{#if day.isCurrentMonth}
								{#if day.workedMinutes > 0}
									<span class="cell-hours">{formatHours(day.workedMinutes)}</span>
									{#if day.targetMinutes > 0}
										<div class="cell-bar">
											<div class="cell-bar-fill" style="width: {barWidth(day.workedMinutes, day.targetMinutes)}%; background: {getHeatColor(day.workedMinutes, day.targetMinutes)}"></div>
										</div>
									{/if}
								{:else if day.dayType}
									<span class="cell-type-label">{day.dayType === 'holiday' ? '🏖️' : day.dayType === 'sick' ? '🤒' : day.dayType === 'vacation' ? '✈️' : '📋'}</span>
								{:else if day.targetMinutes > 0 && !day.isToday && day.date < new Date()}
									<span class="cell-missed">—</span>
								{/if}
							{/if}
						</div>
					{/each}
				</div>
			</div>

			<!-- Legend -->
			<div class="day-type-legend">
				<span class="legend-item"><span class="legend-dot legend-worked"></span> Work Done</span>
				{#if holidayDates.size > 0}<span class="legend-item"><span class="legend-dot legend-holiday"></span> Holiday</span>{/if}
				{#if sickDayDates.size > 0}<span class="legend-item"><span class="legend-dot legend-sick"></span> Sick Day</span>{/if}
				{#if vacationDates.size > 0}<span class="legend-item"><span class="legend-dot legend-vacation"></span> Vacation</span>{/if}
				{#if otherAbsenceDates.size > 0}<span class="legend-item"><span class="legend-dot legend-other"></span> Other Absence</span>{/if}
				<span class="legend-item"><span class="legend-dot legend-target-met"></span> Target Met</span>
				<span class="legend-item"><span class="legend-dot legend-under"></span> Under Target</span>
			</div>

			<!-- Daily breakdown for month -->
			<div class="month-breakdown">
				<h3>Daily Breakdown</h3>
				<div class="breakdown-rows">
					{#each calendarDays.filter(d => d.isCurrentMonth && (d.workedMinutes > 0 || d.targetMinutes > 0 || d.dayType)) as day}
						{@const dayName = day.date.toLocaleDateString('en-US', { weekday: 'short' })}
						<div class="breakdown-row" class:today={day.isToday} class:day-holiday={day.dayType === 'holiday'} class:day-sick={day.dayType === 'sick'} class:day-vacation={day.dayType === 'vacation'} class:day-other={day.dayType === 'other-absence'}>
							<span class="bk-day">{dayName}</span>
							<span class="bk-date">{day.dayOfMonth}</span>
							{#if day.dayType}
								<span class="day-type-dot day-type-{day.dayType}" title={day.dayTypeLabel}></span>
							{:else}
								<span class="day-type-dot-placeholder"></span>
							{/if}
							<div class="bk-bar-track">
								<div class="bk-bar-fill" class:over={day.workedMinutes > day.targetMinutes && day.targetMinutes > 0} style="width: {barWidth(day.workedMinutes, day.targetMinutes)}%"></div>
							</div>
							<span class="bk-hours">{formatHours(day.workedMinutes)}</span>
							{#if day.targetMinutes > 0}
								<span class="bk-target">/ {formatHours(day.targetMinutes)}</span>
							{/if}
						</div>
					{/each}
				</div>
			</div>
		</div>

	{:else}
		<!-- YEAR VIEW -->
		<div class="year-view">
			<div class="nav-row">
				<button class="nav-btn" onclick={prevYear}>&lsaquo;</button>
				<h2 class="nav-title">{currentYear}</h2>
				<button class="nav-btn" onclick={nextYear}>&rsaquo;</button>
			</div>

			{#if yearLoading}
				<div class="loading-state"><div class="spinner"></div><span>Loading year data...</span></div>
			{:else}
				<!-- Year summary -->
				{@const yearTotal = yearMonths.reduce((s, m) => s + m.workedMinutes, 0)}
				{@const yearTarget = yearMonths.reduce((s, m) => s + m.targetMinutes, 0)}
				{@const yearDelta = yearTotal - yearTarget}
				{@const yearHolidays = yearMonths.reduce((s, m) => s + m.holidays, 0)}
				{@const yearSick = yearMonths.reduce((s, m) => s + m.sickDays, 0)}
				{@const yearVacation = yearMonths.reduce((s, m) => s + m.vacationDays, 0)}

				<div class="stats-row">
					<div class="stat-card">
						<span class="stat-value">{formatHours(yearTotal)}</span>
						<span class="stat-label">Total Worked</span>
					</div>
					<div class="stat-card">
						<span class="stat-value">{formatHours(yearTarget)}</span>
						<span class="stat-label">Total Target</span>
					</div>
					<div class="stat-card">
						<span class="stat-value" class:positive={yearDelta > 0} class:negative={yearDelta < 0}>{formatDelta(yearDelta)}</span>
						<span class="stat-label">Year Balance</span>
					</div>
				</div>

				<div class="summary-badges">
					{#if yearHolidays > 0}<span class="badge badge-holiday">{yearHolidays} Holidays</span>{/if}
					{#if yearSick > 0}<span class="badge badge-sick">{yearSick} Sick Days</span>{/if}
					{#if yearVacation > 0}<span class="badge badge-vacation">{yearVacation} Vacation Days</span>{/if}
				</div>

				<!-- Monthly cards -->
				<div class="year-grid">
					{#each yearMonths as mo}
						{@const isCurrent = mo.month === new Date().getMonth() && currentYear === new Date().getFullYear()}
						<button class="month-card" class:current={isCurrent} class:has-data={mo.workedMinutes > 0} onclick={() => { currentMonth = mo.month; viewMode = 'month'; loadEntries(); }}>
							<div class="mc-header">
								<span class="mc-name">{mo.name}</span>
								{#if mo.workedMinutes > 0}
									<span class="mc-delta" class:positive={mo.delta > 0} class:negative={mo.delta < 0}>{formatDelta(mo.delta)}</span>
								{/if}
							</div>
							<div class="mc-bar-track">
								<div class="mc-bar-fill" class:over={mo.workedMinutes > mo.targetMinutes && mo.targetMinutes > 0} style="width: {barWidth(mo.workedMinutes, mo.targetMinutes)}%"></div>
							</div>
							<div class="mc-stats">
								<span class="mc-worked">{formatHours(mo.workedMinutes)}</span>
								{#if mo.targetMinutes > 0}
									<span class="mc-target">/ {formatHours(mo.targetMinutes)}</span>
								{/if}
							</div>
							<div class="mc-badges">
								{#if mo.holidays > 0}<span class="mc-badge mc-badge-holiday">{mo.holidays}🏖️</span>{/if}
								{#if mo.sickDays > 0}<span class="mc-badge mc-badge-sick">{mo.sickDays}🤒</span>{/if}
								{#if mo.vacationDays > 0}<span class="mc-badge mc-badge-vacation">{mo.vacationDays}✈️</span>{/if}
							</div>
						</button>
					{/each}
				</div>
			{/if}
		</div>
	{/if}
</div>

<style>
	.history-page {
		max-width: 960px;
		margin: 0 auto;
		padding: 1.5rem 1rem;
	}

	.history-header {
		display: flex;
		align-items: center;
		justify-content: space-between;
		margin-bottom: 1.5rem;
	}
	.history-header h1 { margin: 0; font-size: 1.5rem; }

	.view-toggle {
		display: flex;
		border: 1px solid #e5e7eb;
		border-radius: 8px;
		overflow: hidden;
	}
	.toggle-btn {
		padding: 0.4rem 1rem;
		border: none;
		background: #fff;
		cursor: pointer;
		font-size: 0.875rem;
		color: #6b7280;
		transition: all 0.15s;
	}
	.toggle-btn:hover { background: #f9fafb; }
	.toggle-btn.active { background: #3b82f6; color: #fff; }

	.loading-state { display: flex; align-items: center; gap: 0.75rem; justify-content: center; padding: 3rem 1rem; color: #6b7280; }
	.spinner { width: 24px; height: 24px; border: 3px solid #e5e7eb; border-top-color: #3b82f6; border-radius: 50%; animation: spin 0.6s linear infinite; }
	@keyframes spin { to { transform: rotate(360deg); } }

	/* Navigation */
	.nav-row {
		display: flex;
		align-items: center;
		justify-content: center;
		gap: 0.75rem;
		margin-bottom: 1.25rem;
	}
	.nav-btn {
		width: 32px;
		height: 32px;
		border: 1px solid #e5e7eb;
		border-radius: 6px;
		background: #fff;
		cursor: pointer;
		font-size: 1rem;
		color: #374151;
		display: flex;
		align-items: center;
		justify-content: center;
		transition: all 0.15s;
	}
	.nav-btn:hover { background: #f3f4f6; }
	.nav-title { margin: 0; font-size: 1.125rem; min-width: 180px; text-align: center; }

	/* Stats */
	.stats-row {
		display: grid;
		grid-template-columns: repeat(auto-fit, minmax(120px, 1fr));
		gap: 0.75rem;
		margin-bottom: 1rem;
	}
	.stat-card {
		background: #fff;
		border: 1px solid #e5e7eb;
		border-radius: 10px;
		padding: 0.75rem;
		text-align: center;
	}
	.stat-value { display: block; font-size: 1.25rem; font-weight: 700; color: #111827; }
	.stat-value.positive { color: #16a34a; }
	.stat-value.negative { color: #dc2626; }
	.stat-label { font-size: 0.75rem; color: #6b7280; }

	/* Summary badges */
	.summary-badges {
		display: flex;
		flex-wrap: wrap;
		gap: 0.5rem;
		margin-bottom: 1.25rem;
	}
	.badge {
		font-size: 0.75rem;
		padding: 0.2rem 0.6rem;
		border-radius: 999px;
		font-weight: 500;
	}
	.badge-work { background: #f3f4f6; color: #374151; }
	.badge-worked { background: #dbeafe; color: #1e40af; }
	.badge-holiday { background: #ede9fe; color: #6d28d9; }
	.badge-sick { background: #fee2e2; color: #b91c1c; }
	.badge-vacation { background: #d1fae5; color: #065f46; }

	/* Calendar */
	.calendar {
		background: #fff;
		border: 1px solid #e5e7eb;
		border-radius: 12px;
		overflow: hidden;
		margin-bottom: 1.25rem;
	}
	.calendar-header {
		display: grid;
		grid-template-columns: repeat(7, 1fr);
		background: #f9fafb;
		border-bottom: 1px solid #e5e7eb;
	}
	.calendar-header span {
		text-align: center;
		font-size: 0.75rem;
		font-weight: 600;
		color: #6b7280;
		padding: 0.5rem;
	}
	.calendar-grid {
		display: grid;
		grid-template-columns: repeat(7, 1fr);
	}
	.calendar-cell {
		min-height: 72px;
		padding: 0.35rem;
		border-right: 1px solid #f3f4f6;
		border-bottom: 1px solid #f3f4f6;
		display: flex;
		flex-direction: column;
		gap: 2px;
		position: relative;
		transition: background 0.15s;
	}
	.calendar-cell:nth-child(7n) { border-right: none; }
	.calendar-cell.other-month { opacity: 0.3; }
	.calendar-cell.weekend { background: #fafafa; }
	.calendar-cell.today { outline: 2px solid #3b82f6; outline-offset: -2px; border-radius: 4px; }
	.calendar-cell.day-holiday { background: rgba(139, 92, 246, 0.08); }
	.calendar-cell.day-sick { background: rgba(239, 68, 68, 0.08); }
	.calendar-cell.day-vacation { background: rgba(16, 185, 129, 0.08); }
	.calendar-cell.day-other { background: rgba(156, 163, 175, 0.12); }

	.cell-day {
		font-size: 0.75rem;
		font-weight: 500;
		color: #374151;
	}
	.today .cell-day { color: #3b82f6; font-weight: 700; }
	.cell-hours {
		font-size: 0.6875rem;
		font-weight: 600;
		color: #111827;
	}
	.cell-bar {
		height: 3px;
		background: #f3f4f6;
		border-radius: 2px;
		overflow: hidden;
		margin-top: auto;
	}
	.cell-bar-fill {
		height: 100%;
		border-radius: 2px;
		min-width: 2px;
	}
	.cell-type-label {
		font-size: 0.75rem;
		margin-top: 2px;
	}
	.cell-missed {
		font-size: 0.75rem;
		color: #d1d5db;
		margin-top: 2px;
	}

	/* Legend */
	.day-type-legend {
		display: flex;
		gap: 1rem;
		flex-wrap: wrap;
		font-size: 0.75rem;
		color: #6b7280;
		margin-bottom: 1.5rem;
	}
	.legend-item {
		display: flex;
		align-items: center;
		gap: 0.35rem;
	}
	.legend-dot {
		width: 10px;
		height: 10px;
		border-radius: 3px;
		flex-shrink: 0;
	}
	.legend-worked { background: rgba(59, 130, 246, 0.3); }
	.legend-holiday { background: rgba(139, 92, 246, 0.4); }
	.legend-sick { background: rgba(239, 68, 68, 0.4); }
	.legend-vacation { background: rgba(16, 185, 129, 0.4); }
	.legend-other { background: rgba(156, 163, 175, 0.4); }
	.legend-target-met { background: rgba(34, 197, 94, 0.5); }
	.legend-under { background: rgba(250, 204, 21, 0.3); }

	.day-type-dot {
		width: 8px;
		height: 8px;
		border-radius: 50%;
		flex-shrink: 0;
	}
	.day-type-dot.day-type-holiday { background: #8b5cf6; }
	.day-type-dot.day-type-sick { background: #ef4444; }
	.day-type-dot.day-type-vacation { background: #10b981; }
	.day-type-dot.day-type-other { background: #9ca3af; }
	.day-type-dot-placeholder { width: 8px; flex-shrink: 0; }

	/* Daily breakdown */
	.month-breakdown {
		background: #fff;
		border: 1px solid #e5e7eb;
		border-radius: 12px;
		padding: 1rem;
	}
	.month-breakdown h3 {
		margin: 0 0 0.75rem 0;
		font-size: 0.9375rem;
	}
	.breakdown-rows {
		display: flex;
		flex-direction: column;
		gap: 0.375rem;
	}
	.breakdown-row {
		display: flex;
		align-items: center;
		gap: 0.5rem;
		padding: 0.25rem 0.375rem;
		border-radius: 4px;
	}
	.breakdown-row.today { font-weight: 600; }
	.breakdown-row.day-holiday { background: rgba(139, 92, 246, 0.06); }
	.breakdown-row.day-sick { background: rgba(239, 68, 68, 0.06); }
	.breakdown-row.day-vacation { background: rgba(16, 185, 129, 0.06); }
	.breakdown-row.day-other { background: rgba(156, 163, 175, 0.08); }

	.bk-day { width: 28px; font-size: 0.75rem; color: #6b7280; flex-shrink: 0; }
	.bk-date { width: 20px; font-size: 0.75rem; color: #9ca3af; flex-shrink: 0; text-align: right; }
	.bk-bar-track { flex: 1; height: 6px; background: #f3f4f6; border-radius: 3px; overflow: hidden; }
	.bk-bar-fill { height: 100%; background: #3b82f6; border-radius: 3px; transition: width 0.3s; }
	.bk-bar-fill.over { background: #22c55e; }
	.bk-hours { font-size: 0.75rem; color: #374151; min-width: 36px; text-align: right; }
	.bk-target { font-size: 0.6875rem; color: #9ca3af; }

	/* Year view */
	.year-grid {
		display: grid;
		grid-template-columns: repeat(auto-fill, minmax(200px, 1fr));
		gap: 0.75rem;
	}
	.month-card {
		background: #fff;
		border: 1px solid #e5e7eb;
		border-radius: 10px;
		padding: 0.875rem;
		cursor: pointer;
		text-align: left;
		transition: all 0.15s;
		width: 100%;
	}
	.month-card:hover { border-color: #93c5fd; box-shadow: 0 2px 8px rgba(59, 130, 246, 0.1); }
	.month-card.current { border-color: #3b82f6; box-shadow: 0 0 0 1px #3b82f6; }
	.month-card.has-data { }

	.mc-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 0.5rem; }
	.mc-name { font-weight: 600; font-size: 0.875rem; color: #111827; }
	.mc-delta { font-size: 0.75rem; font-weight: 500; }
	.mc-delta.positive { color: #16a34a; }
	.mc-delta.negative { color: #dc2626; }

	.mc-bar-track { height: 6px; background: #f3f4f6; border-radius: 3px; overflow: hidden; margin-bottom: 0.5rem; }
	.mc-bar-fill { height: 100%; background: #3b82f6; border-radius: 3px; transition: width 0.3s; }
	.mc-bar-fill.over { background: #22c55e; }

	.mc-stats { font-size: 0.8125rem; color: #374151; margin-bottom: 0.375rem; }
	.mc-worked { font-weight: 600; }
	.mc-target { color: #9ca3af; }

	.mc-badges { display: flex; gap: 0.375rem; flex-wrap: wrap; }
	.mc-badge { font-size: 0.6875rem; }

	/* Responsive */
	@media (max-width: 640px) {
		.stats-row { grid-template-columns: repeat(2, 1fr); }
		.calendar-cell { min-height: 52px; padding: 0.25rem; }
		.cell-day { font-size: 0.6875rem; }
		.cell-hours { font-size: 0.625rem; }
		.year-grid { grid-template-columns: repeat(2, 1fr); }
		.nav-title { font-size: 0.9375rem; min-width: 140px; }
		.history-header { flex-direction: column; gap: 0.75rem; align-items: flex-start; }
	}

	.positive { color: #16a34a; }
	.negative { color: #dc2626; }
</style>
