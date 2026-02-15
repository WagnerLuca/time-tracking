<script lang="ts">
	import { onMount, onDestroy } from 'svelte';
	import { auth } from '$lib/stores/auth.svelte';
	import { orgContext } from '$lib/stores/orgContext.svelte';
	import { apiService } from '$lib/apiService';
	import type { TimeEntryResponse, StartTimeEntryRequest } from '$lib/types';

	let currentEntry = $state<TimeEntryResponse | null>(null);
	let todayMinutes = $state(0);
	let weekMinutes = $state(0);
	let monthMinutes = $state(0);
	let elapsed = $state('00:00:00');
	let loading = $state(true);
	let starting = $state(false);
	let stopping = $state(false);
	let actionError = $state('');
	let timerInterval: ReturnType<typeof setInterval> | null = null;

	onMount(async () => {
		if (!auth.user) return;
		try {
			await Promise.all([loadCurrentTimer(), loadStats()]);
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
			currentEntry = await apiService.get<TimeEntryResponse>('/api/TimeTracking/current');
			if (currentEntry) {
				timerInterval = setInterval(updateElapsed, 1000);
				updateElapsed();
			}
		} catch {
			currentEntry = null;
		}
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

			const [todayEntries, weekEntries, monthEntries] = await Promise.all([
				apiService.get<TimeEntryResponse[]>(
					`/api/TimeTracking?from=${todayStart.toISOString()}&to=${todayEnd.toISOString()}&limit=200`
				),
				apiService.get<TimeEntryResponse[]>(
					`/api/TimeTracking?from=${weekStart.toISOString()}&to=${weekEnd.toISOString()}&limit=200`
				),
				apiService.get<TimeEntryResponse[]>(
					`/api/TimeTracking?from=${monthStart.toISOString()}&to=${monthEnd.toISOString()}&limit=500`
				)
			]);

			const sumMinutes = (entries: TimeEntryResponse[]) =>
				entries.filter(e => !e.isRunning && (e.netDurationMinutes ?? e.durationMinutes))
					.reduce((s, e) => s + (e.netDurationMinutes ?? e.durationMinutes ?? 0), 0);

			todayMinutes = sumMinutes(todayEntries);
			weekMinutes = sumMinutes(weekEntries);
			monthMinutes = sumMinutes(monthEntries);
		} catch {
			todayMinutes = 0;
			weekMinutes = 0;
			monthMinutes = 0;
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
				organizationId: orgContext.selectedOrgId ?? undefined
			};
			currentEntry = await apiService.post<TimeEntryResponse>('/api/TimeTracking/start', payload);
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
			await apiService.post('/api/TimeTracking/stop', {});
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
		return (minutes / 60).toFixed(1) + 'h';
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
		</div>
		<div class="stat-card accent">
			<span class="stat-label">This Week</span>
			<span class="stat-value">{formatHours(weekMinutes)}</span>
		</div>
		<div class="stat-card">
			<span class="stat-label">{getMonthName()}</span>
			<span class="stat-value">{formatHours(monthMinutes)}</span>
		</div>
	</div>

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
