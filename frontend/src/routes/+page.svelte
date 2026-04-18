<script lang="ts">
	import { onMount, onDestroy } from 'svelte';
	import { auth } from '$lib/stores/auth.svelte';
	import { orgContext } from '$lib/stores/orgContext.svelte';
	import { timeTrackingApi, organizationsApi, workScheduleApi } from '$lib/apiClient';
	import type { TimeEntryResponse, StartTimeEntryRequest, WorkScheduleResponse } from '$lib/api';
	import { formatHours, formatDelta, barWidth, getMonthName } from '$lib/utils/formatters';
	import { isToday, isFuture, sumMinutes, dateKey } from '$lib/utils/dateHelpers';
	import { getDayTarget, getAbsenceCredit, absenceCreditsForRange, getDayType, getDayTypeLabel, getTargetForRange } from '$lib/utils/scheduleHelpers';
	import { DAY_NAMES, MAX_ENTRIES_FOR_OVERTIME, WEEKLY_ENTRY_LIMIT, MONTHLY_ENTRY_LIMIT } from '$lib/utils/constants';
	import { extractErrorMessage } from '$lib/utils/errorHandler';
	import { loadDaysOff, emptyDaysOff, type DaysOffData } from '$lib/utils/daysOff';
	import LoadingSpinner from '$lib/components/LoadingSpinner.svelte';
	import DayTypeLegend from '$lib/components/DayTypeLegend.svelte';

	let currentEntry = $state<TimeEntryResponse | null>(null);
	let todayMinutes = $state(0);
	let weekMinutes = $state(0);
	let monthMinutes = $state(0);
	let todayTarget = $state(0);
	let weekTarget = $state(0);
	let monthTarget = $state(0);
	let cumulativeOvertime = $state(0);
	let workSchedule = $state<WorkScheduleResponse | null>(null);
	let elapsed = $state('00:00:00');
	let runningMinutes = $state(0);
	let loading = $state(true);
	let starting = $state(false);
	let stopping = $state(false);
	let actionError = $state('');
	let timerInterval: ReturnType<typeof setInterval> | null = null;

	// Weekly breakdown data
	let weekDays = $state<Array<{label: string, date: Date, worked: number, target: number}>>([]);
	// Monthly week breakdown
	let monthWeeks = $state<Array<{label: string, worked: number, target: number}>>([]);
	// First entry date
	let firstEntryDate = $state<Date | null>(null);
	// Days off (holidays + absences)
	let daysOff = $state<DaysOffData>(emptyDaysOff());
	// Convenience accessors
	let holidayDates = $derived(daysOff.holidayDates);
	let halfDayHolidays = $derived(daysOff.halfDayHolidays);
	let sickDayDates = $derived(daysOff.sickDayDates);
	let vacationDates = $derived(daysOff.vacationDates);
	let otherAbsenceDates = $derived(daysOff.otherAbsenceDates);
	let halfDayAbsences = $derived(daysOff.halfDayAbsences);
	let daysOffSet = $derived(daysOff.daysOffSet);

	// Week-scoped sets for legend (only show legend items for day types visible this week)
	let weekKeys = $derived(weekDays.map(d => dateKey(d.date)));
	let weekHolidayDates = $derived(new Map([...holidayDates].filter(([k]) => weekKeys.includes(k))));
	let weekSickDates = $derived(new Set([...sickDayDates].filter(k => weekKeys.includes(k))));
	let weekVacationDates = $derived(new Set([...vacationDates].filter(k => weekKeys.includes(k))));
	let weekOtherAbsenceDates = $derived(new Set([...otherAbsenceDates].filter(k => weekKeys.includes(k))));

	onMount(async () => {
		if (!auth.user) return;
		try {
			await Promise.all([loadCurrentTimer(), loadWorkSchedule()]);
			await loadStats(); // depends on workSchedule
		} catch (err) {
			console.error(err);
		} finally {
			loading = false;
		}
	});

	// Reload when org changes (track slug since it resolves after organizations load)
	let prevOrgSlug: string | null | undefined = undefined;
	$effect(() => {
		const currentSlug = orgContext.selectedOrgSlug;
		if (currentSlug && currentSlug !== prevOrgSlug) {
			loadWorkSchedule().then(() => loadStats());
		}
		prevOrgSlug = currentSlug;
	});

	onDestroy(() => {
		if (timerInterval) clearInterval(timerInterval);
	});

	async function loadCurrentTimer() {
		try {
			const { data } = await timeTrackingApi.apiV1TimeTrackingCurrentGet();
			currentEntry = data;
			if (currentEntry) {
				timerInterval = setInterval(updateElapsed, 1000);
				updateElapsed();
			}
		} catch {
			currentEntry = null;
		}
	}

	async function loadWorkSchedule() {
		if (!orgContext.selectedOrgSlug) {
			workSchedule = null;
			return;
		}
		try {
			const { data } = await workScheduleApi.apiV1OrganizationsSlugWorkScheduleGet(orgContext.selectedOrgSlug!);
			workSchedule = data;
		} catch {
			workSchedule = null;
		}
	}

	async function loadStats() {
		try {
			daysOff = await loadDaysOff(orgContext.selectedOrgSlug, auth.user?.id);

			const now = new Date();

			// Today
			const todayStart = new Date(now);
			todayStart.setHours(0, 0, 0, 0);
			const todayEnd = new Date(now);
			todayEnd.setHours(23, 59, 59, 999);

			// This week (Mon-Sun)
			const dayOfWeek = now.getDay() || 7;
			const weekStart = new Date(now);
			weekStart.setDate(now.getDate() - dayOfWeek + 1);
			weekStart.setHours(0, 0, 0, 0);
			const weekEnd = new Date(weekStart);
			weekEnd.setDate(weekStart.getDate() + 6);
			weekEnd.setHours(23, 59, 59, 999);

			// This month
			const monthStart = new Date(now.getFullYear(), now.getMonth(), 1);
			const monthEnd = new Date(now.getFullYear(), now.getMonth() + 1, 0, 23, 59, 59, 999);

			const orgId = orgContext.selectedOrgId ?? undefined;
			const [todayRes, weekRes, monthRes, allRes] = await Promise.all([
				timeTrackingApi.apiV1TimeTrackingGet(orgId, todayStart.toISOString(), todayEnd.toISOString(), WEEKLY_ENTRY_LIMIT),
				timeTrackingApi.apiV1TimeTrackingGet(orgId, weekStart.toISOString(), weekEnd.toISOString(), WEEKLY_ENTRY_LIMIT),
				timeTrackingApi.apiV1TimeTrackingGet(orgId, monthStart.toISOString(), monthEnd.toISOString(), MONTHLY_ENTRY_LIMIT),
				timeTrackingApi.apiV1TimeTrackingGet(orgId, undefined, undefined, MAX_ENTRIES_FOR_OVERTIME)
			]);
			const todayEntries = todayRes.data.items ?? [];
			const weekEntries = weekRes.data.items ?? [];
			const monthEntries = monthRes.data.items ?? [];
			const allEntries = allRes.data.items ?? [];

			// Find first entry date (for target calculations)
			const sorted = [...allEntries].filter(e => !e.isRunning && e.endTime)
				.sort((a, b) => new Date(a.startTime!).getTime() - new Date(b.startTime!).getTime());
			if (sorted.length > 0) {
				firstEntryDate = new Date(sorted[0].startTime!);
				firstEntryDate.setHours(0, 0, 0, 0);
			} else {
				firstEntryDate = null;
			}

			todayMinutes = sumMinutes(todayEntries) + getAbsenceCredit(now, workSchedule, holidayDates, sickDayDates, vacationDates, otherAbsenceDates, [], halfDayAbsences, halfDayHolidays);
			weekMinutes = sumMinutes(weekEntries) + absenceCreditsForRange(weekStart, weekEnd, workSchedule, holidayDates, sickDayDates, vacationDates, otherAbsenceDates, [], halfDayAbsences, halfDayHolidays);
			monthMinutes = sumMinutes(monthEntries) + absenceCreditsForRange(monthStart, monthEnd, workSchedule, holidayDates, sickDayDates, vacationDates, otherAbsenceDates, [], halfDayAbsences, halfDayHolidays);

			// Compute targets — only since first tracked entry
			todayTarget = getDayTarget(now, workSchedule, holidayDates, [], halfDayHolidays);
			weekTarget = getTargetForRange(weekStart, todayEnd, workSchedule, holidayDates, [], firstEntryDate, halfDayHolidays);
			monthTarget = getTargetForRange(monthStart, todayEnd, workSchedule, holidayDates, [], firstEntryDate, halfDayHolidays);

			// Build weekly breakdown (Mon-Sun)
			const dayNames = DAY_NAMES;
			const tempWeekDays: typeof weekDays = [];
			for (let i = 0; i < 7; i++) {
				const d = new Date(weekStart);
				d.setDate(weekStart.getDate() + i);
				const dStart = new Date(d); dStart.setHours(0, 0, 0, 0);
				const dEnd = new Date(d); dEnd.setHours(23, 59, 59, 999);
				const dayEntries = weekEntries.filter(e => {
					const t = new Date(e.startTime!);
					return t >= dStart && t <= dEnd;
				});
				tempWeekDays.push({ label: dayNames[i], date: d, worked: sumMinutes(dayEntries) + getAbsenceCredit(d, workSchedule, holidayDates, sickDayDates, vacationDates, otherAbsenceDates, [], halfDayAbsences, halfDayHolidays), target: getDayTarget(d, workSchedule, holidayDates, [], halfDayHolidays) });
			}
			weekDays = tempWeekDays;

			// Build monthly week breakdown
			const tempMonthWeeks: typeof monthWeeks = [];
			let wkStart = new Date(monthStart);
			while (wkStart <= monthEnd) {
				let wkEnd = new Date(wkStart);
				const daysUntilSunday = (7 - wkStart.getDay()) % 7;
				wkEnd.setDate(wkStart.getDate() + (daysUntilSunday || 7) - (wkStart.getDay() === 0 ? 7 : 0));
				if (wkStart.getDay() === 1) {
					wkEnd.setDate(wkStart.getDate() + 6);
				} else if (wkStart.getDay() === 0) {
					wkEnd = new Date(wkStart);
				} else {
					wkEnd.setDate(wkStart.getDate() + (7 - wkStart.getDay()));
				}
				wkEnd.setHours(23, 59, 59, 999);
				if (wkEnd > monthEnd) wkEnd = new Date(monthEnd);
				const wkS = new Date(wkStart); wkS.setHours(0, 0, 0, 0);
				const wkEntries = monthEntries.filter(e => {
					const t = new Date(e.startTime!);
					return t >= wkS && t <= wkEnd;
				});
				tempMonthWeeks.push({
					label: `${wkStart.getDate()}–${wkEnd.getDate()}`,
					worked: sumMinutes(wkEntries),
					target: getTargetForRange(wkS, wkEnd, workSchedule, holidayDates, [], firstEntryDate, halfDayHolidays)
				});
				wkStart = new Date(wkEnd);
				wkStart.setDate(wkStart.getDate() + 1);
				wkStart.setHours(0, 0, 0, 0);
			}
			monthWeeks = tempMonthWeeks;

			// Compute cumulative overtime since first entry
			if (workSchedule && sorted.length > 0) {
				const fDate = new Date(sorted[0].startTime!);
				fDate.setHours(0, 0, 0, 0);
				const today2 = new Date(now);
				today2.setHours(23, 59, 59, 999);
				const totalWorked = sumMinutes(sorted);
				let totalTargetMins = 0;
				const cursor = new Date(fDate);
				while (cursor <= today2) {
					totalTargetMins += getDayTarget(cursor, workSchedule, holidayDates, [], halfDayHolidays);
					cursor.setDate(cursor.getDate() + 1);
				}
				const initialOvertimeMins = (workSchedule?.initialOvertimeMode !== 'Disabled' && workSchedule?.initialOvertimeHours) ? workSchedule.initialOvertimeHours * 60 : 0;
				cumulativeOvertime = totalWorked - totalTargetMins + initialOvertimeMins;
			} else {
				cumulativeOvertime = 0;
			}
		} catch {
			todayMinutes = 0;
			weekMinutes = 0;
			monthMinutes = 0;
			cumulativeOvertime = 0;
		}
	}

	function updateElapsed() {
		if (!currentEntry) { runningMinutes = 0; return; }
		const start = new Date(currentEntry.startTime!).getTime();
		const diff = Math.floor((Date.now() - start) / 1000);
		runningMinutes = diff / 60;
		const h = Math.floor(diff / 3600);
		const m = Math.floor((diff % 3600) / 60);
		const s = diff % 60;
		elapsed = `${String(h).padStart(2, '0')}:${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}`;
	}

	async function handleStart() {
		actionError = '';
		starting = true;
		try {
			const payload: StartTimeEntryRequest = {
				organizationSlug: orgContext.selectedOrgSlug ?? undefined
			};
			const { data } = await timeTrackingApi.apiV1TimeTrackingStartPost(payload);
			currentEntry = data;
			timerInterval = setInterval(updateElapsed, 1000);
			updateElapsed();
		} catch (err) {
			actionError = extractErrorMessage(err, 'Failed to start timer.');
		} finally {
			starting = false;
		}
	}

	async function handleStop() {
		actionError = '';
		stopping = true;
		try {
			await timeTrackingApi.apiV1TimeTrackingStopPost({});
			if (timerInterval) { clearInterval(timerInterval); timerInterval = null; }
			currentEntry = null;
			elapsed = '00:00:00';
			runningMinutes = 0;
			await loadStats();
		} catch (err) {
			actionError = extractErrorMessage(err, 'Failed to stop timer.');
		} finally {
			stopping = false;
		}
	}

</script>

<svelte:head>
	<title>Dashboard - Time Tracking</title>
</svelte:head>

<div>
	{#if loading}
		<LoadingSpinner message="Loading dashboard..." />
	{:else}
	<!-- Header with org context -->
	<div class="flex items-start justify-between mb-6">
		<div>
			<h1 class="text-2xl font-bold text-base-content">Welcome, {auth.user?.firstName}!</h1>
			{#if orgContext.selectedOrg}
				<p class="text-sm font-medium text-primary mt-1">{orgContext.selectedOrg.name}</p>
			{:else}
				<p class="text-sm font-medium text-base-content/40 mt-1">Personal</p>
			{/if}
		</div>
		{#if orgContext.selectedOrg}
			<a href="/organizations/{orgContext.selectedOrgSlug}" class="btn btn-ghost btn-sm text-primary" title="View organization">
				<span>&#9881;</span> Manage
			</a>
		{/if}
	</div>

	{#if actionError}
		<div class="alert alert-error mb-4">{actionError}
			<button class="btn btn-ghost btn-sm btn-square" onclick={() => (actionError = '')}>&times;</button>
		</div>
	{/if}

	<!-- Timer control -->
	<div class="card p-5 mb-6 border-2 {currentEntry ? 'border-success bg-success/5' : 'border-base-300 bg-base-100'}">
		{#if currentEntry}
			<div class="flex items-center gap-4">
				<div class="flex items-center gap-2.5 flex-1">
					<span class="w-2.5 h-2.5 bg-success rounded-full animate-pulse"></span>
					<span class="font-semibold text-base-content">{currentEntry.organizationName || 'Personal'}</span>
				</div>
				<span class="font-bold text-2xl tabular-nums text-success tracking-wide">{elapsed}</span>
				<button class="btn btn-error btn-lg" onclick={handleStop} disabled={stopping}>
					{stopping ? 'Stopping...' : 'Stop'}
				</button>
			</div>
		{:else}
			<div class="flex items-center gap-4">
				<span class="text-base-content/40 font-normal flex-1">No timer running</span>
				<button class="btn btn-success btn-lg" onclick={handleStart} disabled={starting}>
					{starting ? 'Starting...' : 'Start'}
				</button>
			</div>
		{/if}
	</div>

	<!-- Stats cards -->
	<div class="grid grid-cols-3 gap-4 mb-6 max-sm:grid-cols-1">
		<div class="card bg-base-100 border border-base-300 p-5 text-center">
			<span class="block text-xs text-base-content/40 uppercase font-semibold tracking-wider mb-1">Today</span>
			<span class="block text-2xl font-bold text-base-content tabular-nums">{formatHours(todayMinutes + runningMinutes)}</span>
			{#if todayTarget > 0}
				<span class="block text-xs text-base-content/40 mt-0.5">/ {formatHours(todayTarget)}</span>
				{@const d = todayMinutes + runningMinutes - todayTarget}
				<span class="block text-sm font-semibold mt-1 {d > 0 ? 'text-success' : d < 0 ? 'text-error' : ''}">{formatDelta(d)}</span>
			{/if}
		</div>
		<div class="card border-primary bg-primary/5 p-5 text-center">
			<span class="block text-xs text-base-content/40 uppercase font-semibold tracking-wider mb-1">This Week</span>
			<span class="block text-2xl font-bold text-base-content tabular-nums">{formatHours(weekMinutes + runningMinutes)}</span>
			{#if weekTarget > 0}
				<span class="block text-xs text-base-content/40 mt-0.5">/ {formatHours(weekTarget)}</span>
				{@const d = weekMinutes + runningMinutes - weekTarget}
				<span class="block text-sm font-semibold mt-1 {d > 0 ? 'text-success' : d < 0 ? 'text-error' : ''}">{formatDelta(d)}</span>
			{/if}
		</div>
		<div class="card bg-base-100 border border-base-300 p-5 text-center">
			<span class="block text-xs text-base-content/40 uppercase font-semibold tracking-wider mb-1">{getMonthName()}</span>
			<span class="block text-2xl font-bold text-base-content tabular-nums">{formatHours(monthMinutes + runningMinutes)}</span>
			{#if monthTarget > 0}
				<span class="block text-xs text-base-content/40 mt-0.5">/ {formatHours(monthTarget)}</span>
				{@const d = monthMinutes + runningMinutes - monthTarget}
				<span class="block text-sm font-semibold mt-1 {d > 0 ? 'text-success' : d < 0 ? 'text-error' : ''}">{formatDelta(d)}</span>
			{/if}
		</div>
	</div>

	<!-- Cumulative overtime -->
	{#if workSchedule}
		<div class="card p-5 text-center mb-6 border-2 {cumulativeOvertime + runningMinutes > 0 ? 'border-success/50 bg-success/5' : cumulativeOvertime + runningMinutes < 0 ? 'border-error/50 bg-error/5' : 'border-base-300 bg-base-100'}">
			<span class="block text-xs text-base-content/40 uppercase font-semibold tracking-wider mb-1">Cumulative Balance</span>
			<span class="block text-3xl font-bold tabular-nums {cumulativeOvertime + runningMinutes > 0 ? 'text-success' : cumulativeOvertime + runningMinutes < 0 ? 'text-error' : ''}">{formatDelta(cumulativeOvertime + runningMinutes)}</span>
			<span class="block text-xs text-base-content/40 mt-1">Since first tracked entry{firstEntryDate ? ` (${firstEntryDate.toLocaleDateString()})` : ''}</span>
		</div>
	{/if}

	<!-- Weekly Breakdown -->
	{#if weekDays.length > 0}
		<a href="/time" class="card bg-base-100 border border-base-300 p-5 mb-6 no-underline text-base-content hover:border-primary/40 hover:shadow-md transition-all">
			<div class="flex items-center justify-between mb-4">
				<h2 class="text-base font-semibold text-base-content m-0">This Week</h2>
				<span class="text-xs text-primary font-semibold uppercase tracking-wide">Open Timer</span>
			</div>
			<div class="flex flex-col gap-2">
				{#each weekDays as day}
					{@const dayType = getDayType(day.date, holidayDates, sickDayDates, vacationDates, otherAbsenceDates, halfDayHolidays)}
					<div class="flex items-center gap-2.5 {isToday(day.date) ? 'font-semibold' : ''} {isFuture(day.date) ? 'opacity-40' : ''} {dayType === 'holiday' ? 'bg-secondary/10 rounded px-1 -mx-1' : dayType === 'sick' ? 'bg-error/10 rounded px-1 -mx-1' : dayType === 'vacation' ? 'bg-success/10 rounded px-1 -mx-1' : dayType === 'other-absence' ? 'bg-base-content/10 rounded px-1 -mx-1' : ''}">
						<span class="w-8 text-sm text-base-content/50 shrink-0">{day.label}</span>
						{#if dayType}
							<span class="w-2 h-2 rounded-full shrink-0 {dayType === 'holiday' ? 'bg-secondary' : dayType === 'sick' ? 'bg-error' : dayType === 'vacation' ? 'bg-success' : 'bg-base-content/40'}" title={getDayTypeLabel(day.date, holidayDates, sickDayDates, vacationDates, otherAbsenceDates, halfDayHolidays, halfDayAbsences)}></span>
						{/if}
						<div class="flex-1 h-2 bg-base-200 rounded-full overflow-hidden">
							<div class="h-full rounded-full transition-all duration-300 {day.worked > day.target && day.target > 0 ? 'bg-success' : 'bg-primary'}" style="width: {barWidth(isToday(day.date) ? day.worked + runningMinutes : day.worked, day.target)}%"></div>
						</div>
						<span class="text-sm font-semibold text-base-content w-10 text-right tabular-nums">{formatHours(isToday(day.date) ? day.worked + runningMinutes : day.worked)}</span>
						{#if day.target > 0}
							<span class="text-xs text-base-content/40 w-10 tabular-nums">/ {formatHours(day.target)}</span>
						{/if}
					</div>
				{/each}
			</div>
			<div class="flex justify-between items-center mt-3 pt-3 border-t border-base-200 text-sm text-base-content font-semibold">
				<span>Total: {formatHours(weekMinutes + runningMinutes)}</span>
				{#if weekTarget > 0}
					<span class="{weekMinutes + runningMinutes - weekTarget > 0 ? 'text-success' : weekMinutes + runningMinutes - weekTarget < 0 ? 'text-error' : ''} font-semibold">
						{formatDelta(weekMinutes + runningMinutes - weekTarget)}
					</span>
				{/if}
			</div>
		</a>
	{/if}

	<DayTypeLegend holidayDates={weekHolidayDates} sickDayDates={weekSickDates} vacationDates={weekVacationDates} otherAbsenceDates={weekOtherAbsenceDates} />

	<!-- Monthly Overview -->
	{#if monthWeeks.length > 0}
		<a href="/history" class="card bg-base-100 border border-base-300 p-5 mb-6 no-underline text-base-content hover:border-primary/40 hover:shadow-md transition-all">
			<div class="flex items-center justify-between mb-4">
				<h2 class="text-base font-semibold text-base-content m-0">{getMonthName()} Overview</h2>
				<span class="text-xs text-primary font-semibold uppercase tracking-wide">Open History</span>
			</div>
			<div class="flex flex-col gap-2">
				{#each monthWeeks as wk}
					<div class="flex items-center gap-2.5">
						<span class="w-12 text-sm text-base-content/50 shrink-0">{wk.label}</span>
						<div class="flex-1 h-2 bg-base-200 rounded-full overflow-hidden">
							<div class="h-full rounded-full transition-all duration-300 {wk.worked > wk.target && wk.target > 0 ? 'bg-success' : 'bg-secondary'}" style="width: {barWidth(wk.worked, wk.target)}%"></div>
						</div>
						<span class="text-sm font-semibold text-base-content w-10 text-right tabular-nums">{formatHours(wk.worked)}</span>
						{#if wk.target > 0}
							<span class="text-xs text-base-content/40 w-10 tabular-nums">/ {formatHours(wk.target)}</span>
						{/if}
					</div>
				{/each}
			</div>
			<div class="flex justify-between items-center mt-3 pt-3 border-t border-base-200 text-sm text-base-content font-semibold">
				<span>Total: {formatHours(monthMinutes + runningMinutes)}</span>
				{#if monthTarget > 0}
					<span class="{monthMinutes + runningMinutes - monthTarget > 0 ? 'text-success' : monthMinutes + runningMinutes - monthTarget < 0 ? 'text-error' : ''} font-semibold">
						{formatDelta(monthMinutes + runningMinutes - monthTarget)}
					</span>
				{/if}
			</div>
		</a>
	{/if}

	<!-- Quick links -->
	<h2 class="text-base font-semibold text-base-content mt-6 mb-3">Quick Links</h2>
	<div class="grid grid-cols-[repeat(auto-fit,minmax(160px,1fr))] gap-4">
		<a href="/time" class="flex items-center gap-3 card bg-base-100 border border-base-300 p-4 no-underline text-base-content font-medium hover:border-primary hover:shadow-md transition-all">
			<span class="text-lg">&#9201;</span>
			<span>Weekly View</span>
		</a>
		<a href="/history" class="flex items-center gap-3 card bg-base-100 border border-base-300 p-4 no-underline text-base-content font-medium hover:border-primary hover:shadow-md transition-all">
			<span class="text-lg">&#128197;</span>
			<span>History</span>
		</a>
		{#if orgContext.selectedOrg}
			<a href="/organizations/{orgContext.selectedOrgSlug}" class="flex items-center gap-3 card bg-base-100 border border-base-300 p-4 no-underline text-base-content font-medium hover:border-primary hover:shadow-md transition-all">
				<span class="text-lg">&#128101;</span>
				<span>Organization</span>
			</a>
		{/if}
		<a href="/settings" class="flex items-center gap-3 card bg-base-100 border border-base-300 p-4 no-underline text-base-content font-medium hover:border-primary hover:shadow-md transition-all">
			<span class="text-lg">&#9881;</span>
			<span>Settings</span>
		</a>
	</div>
	{/if}
</div>

