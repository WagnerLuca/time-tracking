<script lang="ts">
	import { onMount, onDestroy } from 'svelte';
	import { auth } from '$lib/stores/auth.svelte';
	import { orgContext } from '$lib/stores/orgContext.svelte';
	import { timeTrackingApi, organizationsApi } from '$lib/apiClient';
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
	let loading = $state(true);
	let starting = $state(false);
	let stopping = $state(false);
	let actionError = $state('');
	let timerInterval: ReturnType<typeof setInterval> | null = null;

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
			const { data } = await organizationsApi.apiOrganizationsSlugWorkScheduleGet(orgContext.selectedOrgSlug!);
			workSchedule = data;
		} catch {
			workSchedule = null;
		}
	}

	function getDayTarget(date: Date): number {
		if (!workSchedule) return 0;
		const dayOfWeek = date.getDay();
		const targets: Record<number, number> = {
			1: workSchedule.targetMon,
			2: workSchedule.targetTue,
			3: workSchedule.targetWed,
			4: workSchedule.targetThu,
			5: workSchedule.targetFri,
		};
		return (targets[dayOfWeek] ?? 0) * 60; // convert hours to minutes
	}

	function getWeekTargetMinutes(): number {
		if (!workSchedule) return 0;
		// Only count target minutes for days up to and including today
		const now = new Date();
		const dayOfWeek = now.getDay() || 7;
		const weekStart = new Date(now);
		weekStart.setDate(now.getDate() - dayOfWeek + 1);
		weekStart.setHours(0, 0, 0, 0);

		let total = 0;
		const cursor = new Date(weekStart);
		const todayEnd = new Date(now);
		todayEnd.setHours(23, 59, 59, 999);
		while (cursor <= todayEnd) {
			total += getDayTarget(cursor);
			cursor.setDate(cursor.getDate() + 1);
		}
		return total;
	}

	function getMonthTargetMinutes(year: number, month: number): number {
		if (!workSchedule) return 0;
		// Only count target minutes for days up to and including today
		const now = new Date();
		let total = 0;
		const daysInMonth = new Date(year, month + 1, 0).getDate();
		const todayDate = now.getDate();
		const isCurrentMonth = now.getFullYear() === year && now.getMonth() === month;
		const maxDay = isCurrentMonth ? todayDate : daysInMonth;
		for (let d = 1; d <= maxDay; d++) {
			total += getDayTarget(new Date(year, month, d));
		}
		return total;
	}

	async function loadStats() {
		try {
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

			// Fetch all time entries for cumulative overtime
			const [todayRes, weekRes, monthRes, allRes] = await Promise.all([
				timeTrackingApi.apiTimeTrackingGet(undefined, todayStart.toISOString(), todayEnd.toISOString(), 200),
				timeTrackingApi.apiTimeTrackingGet(undefined, weekStart.toISOString(), weekEnd.toISOString(), 200),
				timeTrackingApi.apiTimeTrackingGet(undefined, monthStart.toISOString(), monthEnd.toISOString(), 500),
				timeTrackingApi.apiTimeTrackingGet(undefined, undefined, undefined, 10000)
			]);
			const todayEntries = todayRes.data;
			const weekEntries = weekRes.data;
			const monthEntries = monthRes.data;
			const allEntries = allRes.data;

			const sumMinutes = (entries: TimeEntryResponse[]) =>
				entries.filter(e => !e.isRunning && (e.netDurationMinutes ?? e.durationMinutes))
					.reduce((s, e) => s + (e.netDurationMinutes ?? e.durationMinutes ?? 0), 0);

			todayMinutes = sumMinutes(todayEntries);
			weekMinutes = sumMinutes(weekEntries);
			monthMinutes = sumMinutes(monthEntries);

			// Compute targets
			todayTarget = getDayTarget(now);
			weekTarget = getWeekTargetMinutes();
			monthTarget = getMonthTargetMinutes(now.getFullYear(), now.getMonth());

			// Compute cumulative overtime since first entry
			if (workSchedule && allEntries.length > 0) {
				const sorted = [...allEntries].filter(e => !e.isRunning && e.endTime).sort((a, b) => new Date(a.startTime).getTime() - new Date(b.startTime).getTime());
				if (sorted.length > 0) {
					const firstDate = new Date(sorted[0].startTime);
					firstDate.setHours(0, 0, 0, 0);
					const today = new Date(now);
					today.setHours(23, 59, 59, 999);

					// Sum actual worked minutes
					const totalWorked = sumMinutes(sorted);

					// Sum target minutes for each day from first entry through today
					let totalTargetMins = 0;
					const cursor = new Date(firstDate);
					while (cursor <= today) {
						totalTargetMins += getDayTarget(cursor);
						cursor.setDate(cursor.getDate() + 1);
					}

					cumulativeOvertime = totalWorked - totalTargetMins;
				}
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
		if (!currentEntry) return;
		const start = new Date(currentEntry.startTime).getTime();
		const diff = Math.floor((Date.now() - start) / 1000);
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
</script>

<svelte:head>
	<title>Dashboard - Time Tracking</title>
</svelte:head>

<div class="dashboard">
	<h1>Welcome, {auth.user?.firstName}!</h1>

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
			<span class="stat-value">{formatHours(todayMinutes)}</span>
			{#if todayTarget > 0}
				<span class="stat-target">/ {formatHours(todayTarget)}</span>
				{@const d = todayMinutes - todayTarget}
				<span class="stat-delta" class:positive={d > 0} class:negative={d < 0}>{formatDelta(d)}</span>
			{/if}
		</div>
		<div class="stat-card accent">
			<span class="stat-label">This Week</span>
			<span class="stat-value">{formatHours(weekMinutes)}</span>
			{#if weekTarget > 0}
				<span class="stat-target">/ {formatHours(weekTarget)}</span>
				{@const d = weekMinutes - weekTarget}
				<span class="stat-delta" class:positive={d > 0} class:negative={d < 0}>{formatDelta(d)}</span>
			{/if}
		</div>
		<div class="stat-card">
			<span class="stat-label">{getMonthName()}</span>
			<span class="stat-value">{formatHours(monthMinutes)}</span>
			{#if monthTarget > 0}
				<span class="stat-target">/ {formatHours(monthTarget)}</span>
				{@const d = monthMinutes - monthTarget}
				<span class="stat-delta" class:positive={d > 0} class:negative={d < 0}>{formatDelta(d)}</span>
			{/if}
		</div>
	</div>

	<!-- Cumulative overtime -->
	{#if workSchedule}
		<div class="overtime-card" class:positive={cumulativeOvertime > 0} class:negative={cumulativeOvertime < 0}>
			<span class="overtime-label">Cumulative Balance</span>
			<span class="overtime-value">{formatDelta(cumulativeOvertime)}</span>
			<span class="overtime-hint">Since first tracked entry</span>
		</div>
	{/if}

	<!-- Quick links -->
	<div class="quick-links">
		<a href="/time" class="quick-link">
			<span class="ql-icon">&#9201;</span>
			<span>Weekly View</span>
		</a>
		<a href="/organizations" class="quick-link">
			<span class="ql-icon">&#128101;</span>
			<span>Organizations</span>
		</a>
		<a href="/settings" class="quick-link">
			<span class="ql-icon">&#9881;</span>
			<span>Settings</span>
		</a>
	</div>
</div>

<style>
	.dashboard h1 {
		margin: 0 0 1.5rem;
		font-size: 1.75rem;
		color: #1a1a2e;
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
</style>
