<script lang="ts">
	import { onMount, onDestroy } from 'svelte';
	import { auth } from '$lib/stores/auth.svelte';
	import { orgContext } from '$lib/stores/orgContext.svelte';
	import { timeTrackingApi, organizationsApi, workScheduleApi, holidayApi, absenceDayApi } from '$lib/apiClient';
	import type { TimeEntryResponse, StartTimeEntryRequest, WorkScheduleResponse } from '$lib/api';

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
	let prevOrgId: number | null | undefined = undefined;

	// Weekly breakdown data
	let weekDays = $state<Array<{label: string, date: Date, worked: number, target: number}>>([]);
	// Monthly week breakdown
	let monthWeeks = $state<Array<{label: string, worked: number, target: number}>>([]);
	// First entry date
	let firstEntryDate = $state<Date | null>(null);
	// Days off (holidays + absences) — date strings like "2025-01-01"
	let daysOffSet = $state<Set<string>>(new Set());
	// Separate day-type maps for color coding
	let holidayDates = $state<Map<string, string>>(new Map()); // date -> name
	let sickDayDates = $state<Set<string>>(new Set());
	let vacationDates = $state<Set<string>>(new Set());
	let otherAbsenceDates = $state<Set<string>>(new Set());

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

	// Reload when org changes
	$effect(() => {
		const currentOrgId = orgContext.selectedOrgId;
		if (prevOrgId !== undefined && prevOrgId !== currentOrgId) {
			loadWorkSchedule().then(() => loadStats());
		}
		prevOrgId = currentOrgId;
	});

	onDestroy(() => {
		if (timerInterval) clearInterval(timerInterval);
	});

	async function loadCurrentTimer() {
		try {
			const { data } = await timeTrackingApi.apiTimeTrackingCurrentGet();
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
			const { data } = await workScheduleApi.apiOrganizationsSlugWorkScheduleGet(orgContext.selectedOrgSlug!);
			workSchedule = data;
		} catch {
			workSchedule = null;
		}
	}

	function getDayTarget(date: Date): number {
		if (!workSchedule) return 0;
		const dayOfWeek = date.getDay();
		// Check if this date is a holiday or absence day
		const dateStr = `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
		if (daysOffSet.has(dateStr)) return 0;
		const targets: Record<number, number> = {
			1: workSchedule.targetMon ?? 0,
			2: workSchedule.targetTue ?? 0,
			3: workSchedule.targetWed ?? 0,
			4: workSchedule.targetThu ?? 0,
			5: workSchedule.targetFri ?? 0,
		};
		return (targets[dayOfWeek] ?? 0) * 60; // convert hours to minutes
	}

	function getWeekTargetMinutes(): number {
		if (!workSchedule) return 0;
		const now = new Date();
		const dayOfWeek = now.getDay() || 7;
		const weekStart = new Date(now);
		weekStart.setDate(now.getDate() - dayOfWeek + 1);
		weekStart.setHours(0, 0, 0, 0);
		const todayEnd = new Date(now);
		todayEnd.setHours(23, 59, 59, 999);
		return getTargetSinceFirstEntry(weekStart, todayEnd);
	}

	function getMonthTargetMinutes(year: number, month: number): number {
		if (!workSchedule) return 0;
		const monthStart = new Date(year, month, 1);
		const now = new Date();
		const isCurrentMonth = now.getFullYear() === year && now.getMonth() === month;
		const monthEnd = isCurrentMonth ? new Date(now) : new Date(year, month + 1, 0);
		monthEnd.setHours(23, 59, 59, 999);
		return getTargetSinceFirstEntry(monthStart, monthEnd);
	}

	function getTargetSinceFirstEntry(rangeStart: Date, rangeEnd: Date): number {
		if (!workSchedule) return 0;
		const effectiveStart = firstEntryDate && firstEntryDate > rangeStart ? firstEntryDate : rangeStart;
		if (effectiveStart > rangeEnd) return 0;

		let total = 0;
		const cursor = new Date(effectiveStart);
		cursor.setHours(0, 0, 0, 0);
		while (cursor <= rangeEnd) {
			total += getDayTarget(cursor);
			cursor.setDate(cursor.getDate() + 1);
		}
		return total;
	}

	async function loadStats() {
		try {
			// Load holidays + absences for days-off calculation and color coding
			if (orgContext.selectedOrgSlug) {
				try {
					const [holRes, absRes] = await Promise.all([
						holidayApi.apiOrganizationsSlugHolidaysGet(orgContext.selectedOrgSlug),
						absenceDayApi.apiOrganizationsSlugAbsencesGet(orgContext.selectedOrgSlug)
					]);
					const offDates = new Set<string>();
					const holidays = new Map<string, string>();
					const sick = new Set<string>();
					const vacation = new Set<string>();
					const otherAbs = new Set<string>();
					for (const h of holRes.data) {
						if (h.date) {
							offDates.add(h.date);
							holidays.set(h.date, h.name ?? 'Holiday');
						}
					}
					for (const a of absRes.data) {
						if (a.date) {
							offDates.add(a.date);
							if (a.type === 'SickDay') sick.add(a.date);
							else if (a.type === 'Vacation') vacation.add(a.date);
							else otherAbs.add(a.date);
						}
					}
					daysOffSet = offDates;
					holidayDates = holidays;
					sickDayDates = sick;
					vacationDates = vacation;
					otherAbsenceDates = otherAbs;
				} catch {
					daysOffSet = new Set();
					holidayDates = new Map();
					sickDayDates = new Set();
					vacationDates = new Set();
					otherAbsenceDates = new Set();
				}
			} else {
				daysOffSet = new Set();
				holidayDates = new Map();
				sickDayDates = new Set();
				vacationDates = new Set();
				otherAbsenceDates = new Set();
			}

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
				timeTrackingApi.apiTimeTrackingGet(orgId, todayStart.toISOString(), todayEnd.toISOString(), 200),
				timeTrackingApi.apiTimeTrackingGet(orgId, weekStart.toISOString(), weekEnd.toISOString(), 200),
				timeTrackingApi.apiTimeTrackingGet(orgId, monthStart.toISOString(), monthEnd.toISOString(), 500),
				timeTrackingApi.apiTimeTrackingGet(orgId, undefined, undefined, 10000)
			]);
			const todayEntries = todayRes.data;
			const weekEntries = weekRes.data;
			const monthEntries = monthRes.data;
			const allEntries = allRes.data;

			const sumMinutes = (entries: TimeEntryResponse[]) =>
				entries.filter(e => !e.isRunning && (e.netDurationMinutes ?? e.durationMinutes))
					.reduce((s, e) => s + (e.netDurationMinutes ?? e.durationMinutes ?? 0), 0);

			// Find first entry date (for target calculations)
			const sorted = [...allEntries].filter(e => !e.isRunning && e.endTime)
				.sort((a, b) => new Date(a.startTime!).getTime() - new Date(b.startTime!).getTime());
			if (sorted.length > 0) {
				firstEntryDate = new Date(sorted[0].startTime!);
				firstEntryDate.setHours(0, 0, 0, 0);
			} else {
				firstEntryDate = null;
			}

			todayMinutes = sumMinutes(todayEntries);
			weekMinutes = sumMinutes(weekEntries);
			monthMinutes = sumMinutes(monthEntries);

			// Compute targets — only since first tracked entry
			todayTarget = getDayTarget(now);
			weekTarget = getWeekTargetMinutes();
			monthTarget = getMonthTargetMinutes(now.getFullYear(), now.getMonth());

			// Build weekly breakdown (Mon-Sun)
			const dayNames = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];
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
				tempWeekDays.push({ label: dayNames[i], date: d, worked: sumMinutes(dayEntries), target: getDayTarget(d) });
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
					target: getTargetSinceFirstEntry(wkS, wkEnd)
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
					totalTargetMins += getDayTarget(cursor);
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
			const { data } = await timeTrackingApi.apiTimeTrackingStartPost(payload);
			currentEntry = data;
			timerInterval = setInterval(updateElapsed, 1000);
			updateElapsed();
		} catch (err: any) {
			actionError = err.response?.data?.message || 'Failed to start timer.';
		} finally {
			starting = false;
		}
	}

	async function handleStop() {
		actionError = '';
		stopping = true;
		try {
			await timeTrackingApi.apiTimeTrackingStopPost({});
			if (timerInterval) { clearInterval(timerInterval); timerInterval = null; }
			currentEntry = null;
			elapsed = '00:00:00';
			runningMinutes = 0;
			await loadStats();
		} catch (err: any) {
			actionError = err.response?.data?.message || 'Failed to stop timer.';
		} finally {
			stopping = false;
		}
	}

	function formatHours(minutes: number): string {
		if (minutes === 0) return '0h';
		const sign = minutes < 0 ? '-' : '';
		return sign + (Math.abs(minutes) / 60).toFixed(1) + 'h';
	}

	function formatDelta(minutes: number): string {
		if (minutes === 0) return '±0h';
		const sign = minutes > 0 ? '+' : '';
		return sign + (minutes / 60).toFixed(1) + 'h';
	}

	function getMonthName(): string {
		return new Date().toLocaleDateString([], { month: 'long' });
	}

	function isToday(date: Date): boolean {
		const now = new Date();
		return date.getDate() === now.getDate() && date.getMonth() === now.getMonth() && date.getFullYear() === now.getFullYear();
	}

	function isFuture(date: Date): boolean {
		const now = new Date();
		now.setHours(0, 0, 0, 0);
		const d = new Date(date);
		d.setHours(0, 0, 0, 0);
		return d > now;
	}

	function barWidth(worked: number, target: number): number {
		if (target <= 0) return worked > 0 ? 100 : 0;
		return Math.min(100, (worked / target) * 100);
	}

	function dateKey(d: Date): string {
		return d.toISOString().slice(0, 10);
	}

	function getDayType(d: Date): 'holiday' | 'sick' | 'vacation' | 'other-absence' | null {
		const key = dateKey(d);
		if (holidayDates.has(key)) return 'holiday';
		if (sickDayDates.has(key)) return 'sick';
		if (vacationDates.has(key)) return 'vacation';
		if (otherAbsenceDates.has(key)) return 'other-absence';
		return null;
	}

	function getDayTypeLabel(d: Date): string {
		const key = dateKey(d);
		if (holidayDates.has(key)) return holidayDates.get(key)!;
		if (sickDayDates.has(key)) return 'Sick Day';
		if (vacationDates.has(key)) return 'Vacation';
		if (otherAbsenceDates.has(key)) return 'Absence';
		return '';
	}
</script>

<svelte:head>
	<title>Dashboard - Time Tracking</title>
</svelte:head>

<div class="dashboard">
	{#if loading}
		<div class="loading-state">
			<div class="spinner"></div>
			<p>Loading dashboard...</p>
		</div>
	{:else}
	<!-- Header with org context -->
	<div class="dash-header">
		<div>
			<h1>Welcome, {auth.user?.firstName}!</h1>
			{#if orgContext.selectedOrg}
				<p class="org-context">{orgContext.selectedOrg.name}</p>
			{:else}
				<p class="org-context personal">Personal</p>
			{/if}
		</div>
		{#if orgContext.selectedOrg}
			<a href="/organizations/{orgContext.selectedOrgSlug}" class="org-link" title="View organization">
				<span class="org-link-icon">&#9881;</span> Manage
			</a>
		{/if}
	</div>

	{#if actionError}
		<div class="error-banner">{actionError}
			<button class="dismiss" onclick={() => (actionError = '')}>&times;</button>
		</div>
	{/if}

	<!-- Timer control -->
	<div class="timer-card" class:running={!!currentEntry}>
		{#if currentEntry}
			<div class="timer-row">
				<div class="timer-info">
					<span class="timer-pulse"></span>
					<span class="timer-label">{currentEntry.organizationName || 'Personal'}</span>
				</div>
				<span class="timer-clock">{elapsed}</span>
				<button class="btn-stop" onclick={handleStop} disabled={stopping}>
					{stopping ? 'Stopping...' : 'Stop'}
				</button>
			</div>
		{:else}
			<div class="timer-row">
				<span class="timer-label idle">No timer running</span>
				<button class="btn-start" onclick={handleStart} disabled={starting}>
					{starting ? 'Starting...' : 'Start'}
				</button>
			</div>
		{/if}
	</div>

	<!-- Stats cards -->
	<div class="stats-row">
		<div class="stat-card">
			<span class="stat-label">Today</span>
			<span class="stat-value">{formatHours(todayMinutes + runningMinutes)}</span>
			{#if todayTarget > 0}
				<span class="stat-target">/ {formatHours(todayTarget)}</span>
				{@const d = todayMinutes + runningMinutes - todayTarget}
				<span class="stat-delta" class:positive={d > 0} class:negative={d < 0}>{formatDelta(d)}</span>
			{/if}
		</div>
		<div class="stat-card accent">
			<span class="stat-label">This Week</span>
			<span class="stat-value">{formatHours(weekMinutes + runningMinutes)}</span>
			{#if weekTarget > 0}
				<span class="stat-target">/ {formatHours(weekTarget)}</span>
				{@const d = weekMinutes + runningMinutes - weekTarget}
				<span class="stat-delta" class:positive={d > 0} class:negative={d < 0}>{formatDelta(d)}</span>
			{/if}
		</div>
		<div class="stat-card">
			<span class="stat-label">{getMonthName()}</span>
			<span class="stat-value">{formatHours(monthMinutes + runningMinutes)}</span>
			{#if monthTarget > 0}
				<span class="stat-target">/ {formatHours(monthTarget)}</span>
				{@const d = monthMinutes + runningMinutes - monthTarget}
				<span class="stat-delta" class:positive={d > 0} class:negative={d < 0}>{formatDelta(d)}</span>
			{/if}
		</div>
	</div>

	<!-- Cumulative overtime -->
	{#if workSchedule}
		<div class="overtime-card" class:positive={cumulativeOvertime + runningMinutes > 0} class:negative={cumulativeOvertime + runningMinutes < 0}>
			<span class="overtime-label">Cumulative Balance</span>
			<span class="overtime-value">{formatDelta(cumulativeOvertime + runningMinutes)}</span>
			<span class="overtime-hint">Since first tracked entry{firstEntryDate ? ` (${firstEntryDate.toLocaleDateString()})` : ''}</span>
		</div>
	{/if}

	<!-- Weekly Breakdown -->
	{#if weekDays.length > 0}
		<div class="breakdown-card">
			<h2 class="breakdown-title">This Week</h2>
			<div class="breakdown-rows">
				{#each weekDays as day}
					{@const dayType = getDayType(day.date)}
					<div class="breakdown-row" class:today={isToday(day.date)} class:future={isFuture(day.date)} class:day-holiday={dayType === 'holiday'} class:day-sick={dayType === 'sick'} class:day-vacation={dayType === 'vacation'} class:day-other={dayType === 'other-absence'}>
						<span class="breakdown-day">{day.label}</span>
						{#if dayType}
							<span class="day-type-dot day-type-{dayType}" title={getDayTypeLabel(day.date)}></span>
						{/if}
						<div class="breakdown-bar-track">
							<div class="breakdown-bar-fill" class:over={day.worked > day.target && day.target > 0} style="width: {barWidth(isToday(day.date) ? day.worked + runningMinutes : day.worked, day.target)}%"></div>
						</div>
						<span class="breakdown-hours">{formatHours(isToday(day.date) ? day.worked + runningMinutes : day.worked)}</span>
						{#if day.target > 0}
							<span class="breakdown-target">/ {formatHours(day.target)}</span>
						{/if}
					</div>
				{/each}
			</div>
			<div class="breakdown-footer">
				<span>Total: {formatHours(weekMinutes + runningMinutes)}</span>
				{#if weekTarget > 0}
					<span class="breakdown-footer-delta" class:positive={weekMinutes + runningMinutes - weekTarget > 0} class:negative={weekMinutes + runningMinutes - weekTarget < 0}>
						{formatDelta(weekMinutes + runningMinutes - weekTarget)}
					</span>
				{/if}
			</div>
		</div>
	{/if}

	<!-- Day Type Legend -->
	{#if holidayDates.size > 0 || sickDayDates.size > 0 || vacationDates.size > 0 || otherAbsenceDates.size > 0}
		<div class="day-type-legend">
			{#if holidayDates.size > 0}<span class="legend-item"><span class="day-type-dot day-type-holiday"></span> Holiday</span>{/if}
			{#if sickDayDates.size > 0}<span class="legend-item"><span class="day-type-dot day-type-sick"></span> Sick Day</span>{/if}
			{#if vacationDates.size > 0}<span class="legend-item"><span class="day-type-dot day-type-vacation"></span> Vacation</span>{/if}
			{#if otherAbsenceDates.size > 0}<span class="legend-item"><span class="day-type-dot day-type-other"></span> Other Absence</span>{/if}
		</div>
	{/if}

	<!-- Monthly Overview -->
	{#if monthWeeks.length > 0}
		<div class="breakdown-card">
			<h2 class="breakdown-title">{getMonthName()} Overview</h2>
			<div class="breakdown-rows">
				{#each monthWeeks as wk}
					<div class="breakdown-row">
						<span class="breakdown-day month-range">{wk.label}</span>
						<div class="breakdown-bar-track">
							<div class="breakdown-bar-fill month-bar" class:over={wk.worked > wk.target && wk.target > 0} style="width: {barWidth(wk.worked, wk.target)}%"></div>
						</div>
						<span class="breakdown-hours">{formatHours(wk.worked)}</span>
						{#if wk.target > 0}
							<span class="breakdown-target">/ {formatHours(wk.target)}</span>
						{/if}
					</div>
				{/each}
			</div>
			<div class="breakdown-footer">
				<span>Total: {formatHours(monthMinutes + runningMinutes)}</span>
				{#if monthTarget > 0}
					<span class="breakdown-footer-delta" class:positive={monthMinutes + runningMinutes - monthTarget > 0} class:negative={monthMinutes + runningMinutes - monthTarget < 0}>
						{formatDelta(monthMinutes + runningMinutes - monthTarget)}
					</span>
				{/if}
			</div>
		</div>
	{/if}

	<!-- Quick links -->
	<h2 class="section-title">Quick Links</h2>
	<div class="quick-links">
		<a href="/time" class="quick-link">
			<span class="ql-icon">&#9201;</span>
			<span>Weekly View</span>
		</a>
		<a href="/history" class="quick-link">
			<span class="ql-icon">&#128197;</span>
			<span>History</span>
		</a>
		{#if orgContext.selectedOrg}
			<a href="/organizations/{orgContext.selectedOrgSlug}" class="quick-link">
				<span class="ql-icon">&#128101;</span>
				<span>Organization</span>
			</a>
		{/if}
		<a href="/settings" class="quick-link">
			<span class="ql-icon">&#9881;</span>
			<span>Settings</span>
		</a>
	</div>
	{/if}
</div>

<style>
	.dashboard h1 {
		margin: 0;
		

	.section-title {
		font-size: 1rem;
		color: #374151;
		margin: 1.5rem 0 0.75rem;
		font-weight: 600;
	}font-size: 1.75rem;
		color: #1a1a2e;
	}

	.dash-header {
		display: flex;
		align-items: flex-start;
		justify-content: space-between;
		margin-bottom: 1.5rem;
	}

	.org-context {
		margin: 0.25rem 0 0;
		font-size: 0.9375rem;
		color: #3b82f6;
		font-weight: 500;
	}

	.org-context.personal {
		color: #9ca3af;
	}

	.org-link {
		display: inline-flex;
		align-items: center;
		gap: 0.375rem;
		background: #eff6ff;
		color: #2563eb;
		border: 1px solid #bfdbfe;
		border-radius: 8px;
		padding: 0.5rem 1rem;
		font-size: 0.8125rem;
		font-weight: 500;
		text-decoration: none;
		transition: all 0.15s;
	}

	.org-link:hover {
		background: #dbeafe;
		border-color: #93c5fd;
	}

	.org-link-icon {
		font-size: 0.875rem;
	}

	.error-banner {
		background: #fef2f2;
		color: #dc2626;
		padding: 0.75rem 1rem;
		border-radius: 8px;
		margin-bottom: 1rem;
		font-size: 0.875rem;
		border-left: 3px solid #dc2626;
		display: flex;
		justify-content: space-between;
		align-items: center;
	}

	.dismiss {
		background: none;
		border: none;
		color: #dc2626;
		font-size: 1.25rem;
		cursor: pointer;
	}

	/* Timer card */
	.timer-card {
		background: white;
		border: 2px solid #e5e7eb;
		border-radius: 14px;
		padding: 1.25rem 1.5rem;
		margin-bottom: 1.5rem;
		transition: border-color 0.3s;
	}

	.timer-card.running {
		border-color: #22c55e;
		background: #f0fdf4;
	}

	.timer-row {
		display: flex;
		align-items: center;
		gap: 1rem;
	}

	.timer-info {
		display: flex;
		align-items: center;
		gap: 0.625rem;
		flex: 1;
	}

	.timer-pulse {
		width: 10px;
		height: 10px;
		background: #22c55e;
		border-radius: 50%;
		animation: pulse 1.5s infinite;
	}

	@keyframes pulse {
		0%, 100% { opacity: 1; }
		50% { opacity: 0.4; }
	}

	.timer-label {
		font-weight: 600;
		color: #1a1a2e;
		font-size: 1rem;
	}

	.timer-label.idle {
		color: #9ca3af;
		font-weight: 400;
		flex: 1;
	}

	.timer-clock {
		font-weight: 700;
		font-size: 1.5rem;
		font-variant-numeric: tabular-nums;
		color: #16a34a;
		letter-spacing: 0.03em;
	}

	.btn-start {
		padding: 0.625rem 2rem;
		background: #22c55e;
		color: white;
		border: none;
		border-radius: 10px;
		font-size: 1rem;
		font-weight: 700;
		cursor: pointer;
		transition: background 0.15s;
	}

	.btn-start:hover:not(:disabled) { background: #16a34a; }
	.btn-start:disabled { opacity: 0.6; cursor: not-allowed; }

	.btn-stop {
		padding: 0.625rem 2rem;
		background: #ef4444;
		color: white;
		border: none;
		border-radius: 10px;
		font-size: 1rem;
		font-weight: 700;
		cursor: pointer;
		transition: background 0.15s;
	}

	.btn-stop:hover:not(:disabled) { background: #dc2626; }
	.btn-stop:disabled { opacity: 0.6; cursor: not-allowed; }

	/* Stats */
	.stats-row {
		display: grid;
		grid-template-columns: repeat(3, 1fr);
		gap: 1rem;
		margin-bottom: 1.5rem;
	}

	.stat-card {
		background: white;
		border: 1px solid #e5e7eb;
		border-radius: 12px;
		padding: 1.25rem;
		text-align: center;
	}

	.stat-card.accent {
		border-color: #3b82f6;
		background: #eff6ff;
	}

	.stat-label {
		display: block;
		font-size: 0.75rem;
		color: #9ca3af;
		text-transform: uppercase;
		font-weight: 600;
		letter-spacing: 0.05em;
		margin-bottom: 0.375rem;
	}

	.stat-card.accent .stat-label {
		color: #3b82f6;
	}

	.stat-value {
		display: block;
		font-size: 1.5rem;
		font-weight: 700;
		color: #1a1a2e;
		font-variant-numeric: tabular-nums;
	}

	.stat-target {
		display: block;
		font-size: 0.75rem;
		color: #9ca3af;
		margin-top: 0.125rem;
	}

	.stat-delta {
		display: block;
		font-size: 0.8125rem;
		font-weight: 600;
		margin-top: 0.25rem;
	}

	.stat-delta.positive { color: #16a34a; }
	.stat-delta.negative { color: #dc2626; }

	/* Cumulative overtime card */
	.overtime-card {
		background: white;
		border: 2px solid #e5e7eb;
		border-radius: 12px;
		padding: 1.25rem;
		text-align: center;
		margin-bottom: 1.5rem;
	}

	.overtime-card.positive {
		border-color: #86efac;
		background: #f0fdf4;
	}

	.overtime-card.negative {
		border-color: #fca5a5;
		background: #fef2f2;
	}

	.overtime-label {
		display: block;
		font-size: 0.75rem;
		color: #9ca3af;
		text-transform: uppercase;
		font-weight: 600;
		letter-spacing: 0.05em;
		margin-bottom: 0.375rem;
	}

	.overtime-value {
		display: block;
		font-size: 2rem;
		font-weight: 700;
		font-variant-numeric: tabular-nums;
	}

	.overtime-card.positive .overtime-value { color: #16a34a; }
	.overtime-card.negative .overtime-value { color: #dc2626; }

	.overtime-hint {
		display: block;
		font-size: 0.6875rem;
		color: #9ca3af;
		margin-top: 0.25rem;
	}

	/* Quick links */
	.quick-links {
		display: grid;
		grid-template-columns: repeat(auto-fit, minmax(160px, 1fr));
		gap: 1rem;
	}

	.quick-link {
		display: flex;
		align-items: center;
		gap: 0.75rem;
		background: white;
		border: 1px solid #e5e7eb;
		border-radius: 12px;
		padding: 1rem 1.25rem;
		text-decoration: none;
		color: #374151;
		font-weight: 500;
		font-size: 0.9375rem;
		transition: border-color 0.15s, box-shadow 0.15s;
	}

	.quick-link:hover {
		border-color: #3b82f6;
		box-shadow: 0 2px 8px rgba(59, 130, 246, 0.08);
	}

	.ql-icon {
		font-size: 1.125rem;
	}

	/* Breakdown cards */
	.breakdown-card {
		background: white;
		border: 1px solid #e5e7eb;
		border-radius: 12px;
		padding: 1.25rem;
		margin-bottom: 1.5rem;
	}

	.breakdown-title {
		margin: 0 0 1rem;
		font-size: 1rem;
		font-weight: 600;
		color: #1a1a2e;
	}

	.breakdown-rows {
		display: flex;
		flex-direction: column;
		gap: 0.5rem;
	}

	.breakdown-row {
		display: flex;
		align-items: center;
		gap: 0.625rem;
	}

	.breakdown-row.today {
		font-weight: 600;
	}

	.breakdown-row.future {
		opacity: 0.4;
	}

	.breakdown-row.day-holiday { background: rgba(139, 92, 246, 0.08); border-radius: 4px; padding: 2px 4px; margin: -2px -4px; }
	.breakdown-row.day-sick { background: rgba(239, 68, 68, 0.08); border-radius: 4px; padding: 2px 4px; margin: -2px -4px; }
	.breakdown-row.day-vacation { background: rgba(16, 185, 129, 0.08); border-radius: 4px; padding: 2px 4px; margin: -2px -4px; }
	.breakdown-row.day-other { background: rgba(156, 163, 175, 0.12); border-radius: 4px; padding: 2px 4px; margin: -2px -4px; }

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

	.day-type-legend {
		display: flex;
		gap: 1rem;
		flex-wrap: wrap;
		font-size: 0.75rem;
		color: #6b7280;
		margin-bottom: 1rem;
	}
	.legend-item {
		display: flex;
		align-items: center;
		gap: 0.35rem;
	}

	.breakdown-day {
		width: 32px;
		font-size: 0.8125rem;
		color: #6b7280;
		flex-shrink: 0;
	}

	.breakdown-day.month-range {
		width: 50px;
	}

	.breakdown-bar-track {
		flex: 1;
		height: 8px;
		background: #f3f4f6;
		border-radius: 4px;
		overflow: hidden;
	}

	.breakdown-bar-fill {
		height: 100%;
		background: #3b82f6;
		border-radius: 4px;
		transition: width 0.3s ease;
	}

	.breakdown-bar-fill.over {
		background: #16a34a;
	}

	.breakdown-bar-fill.month-bar {
		background: #8b5cf6;
	}

	.breakdown-bar-fill.month-bar.over {
		background: #16a34a;
	}

	.breakdown-hours {
		font-size: 0.8125rem;
		font-weight: 600;
		color: #1a1a2e;
		width: 40px;
		text-align: right;
		font-variant-numeric: tabular-nums;
	}

	.breakdown-target {
		font-size: 0.75rem;
		color: #9ca3af;
		width: 40px;
		font-variant-numeric: tabular-nums;
	}

	.breakdown-footer {
		display: flex;
		justify-content: space-between;
		align-items: center;
		margin-top: 0.75rem;
		padding-top: 0.75rem;
		border-top: 1px solid #f3f4f6;
		font-size: 0.875rem;
		color: #374151;
		font-weight: 600;
	}

	.breakdown-footer-delta {
		font-weight: 600;
	}

	.breakdown-footer-delta.positive { color: #16a34a; }

	/* Loading state */
	.loading-state {
		display: flex;
		flex-direction: column;
		align-items: center;
		justify-content: center;
		padding: 4rem 2rem;
		gap: 1rem;
		color: #6b7280;
	}

	.spinner {
		width: 2rem;
		height: 2rem;
		border: 3px solid #e5e7eb;
		border-top-color: #3b82f6;
		border-radius: 50%;
		animation: spin 0.8s linear infinite;
	}

	@keyframes spin {
		to { transform: rotate(360deg); }
	}

	/* Responsive */
	@media (max-width: 640px) {
		.dashboard {
			padding: 1rem;
		}

		.dashboard h1 {
			font-size: 1.35rem;
		}

		.stats-row {
			grid-template-columns: 1fr !important;
			gap: 0.75rem;
		}

		.timer-row {
			flex-wrap: wrap;
			gap: 0.5rem;
		}

		.timer-clock {
			font-size: 1.5rem;
		}

		.quick-links {
			grid-template-columns: 1fr !important;
		}

		.breakdown-row {
			font-size: 0.8rem;
		}
	}
	.breakdown-footer-delta.negative { color: #dc2626; }
</style>
