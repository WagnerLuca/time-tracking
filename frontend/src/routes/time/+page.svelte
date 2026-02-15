<script lang="ts">
	import { onMount, onDestroy } from 'svelte';
	import { orgContext } from '$lib/stores/orgContext.svelte';
	import { apiService } from '$lib/apiService';
	import type { TimeEntryResponse, StartTimeEntryRequest } from '$lib/types';

	let current = $state<TimeEntryResponse | null>(null);
	let weekEntries = $state<TimeEntryResponse[]>([]);
	let loading = $state(true);
	let actionError = $state('');

	// Timer display
	let elapsed = $state('00:00:00');
	let timerInterval: ReturnType<typeof setInterval> | null = null;

	// Optional note (de-emphasized)
	let note = $state('');
	let starting = $state(false);
	let stopping = $state(false);

	// Week navigation
	let weekOffset = $state(0);

	const weekRange = $derived(getWeekRange(weekOffset));
	const dailyTotals = $derived(computeDailyTotals(weekEntries, weekRange));
	const weekTotal = $derived(dailyTotals.reduce((s, d) => s + d.minutes, 0));

	onMount(async () => {
		await Promise.all([loadCurrent(), loadWeek()]);
		loading = false;
	});

	onDestroy(() => {
		if (timerInterval) clearInterval(timerInterval);
	});

	function getWeekRange(offset: number) {
		const now = new Date();
		const start = new Date(now);
		const dayOfWeek = now.getDay() || 7; // Sunday = 7 (end of Mon-Sun week)
		start.setDate(now.getDate() - dayOfWeek + 1 + offset * 7); // Monday
		start.setHours(0, 0, 0, 0);
		const end = new Date(start);
		end.setDate(start.getDate() + 6);
		end.setHours(23, 59, 59, 999);
		return { start, end };
	}

	function computeDailyTotals(entries: TimeEntryResponse[], range: { start: Date; end: Date }) {
		const days = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];
		const totals = days.map((name, i) => {
			const date = new Date(range.start);
			date.setDate(range.start.getDate() + i);
			return { name, date: new Date(date), minutes: 0, entryCount: 0 };
		});

		for (const entry of entries) {
			if (entry.isRunning) continue;
			const entryDate = new Date(entry.startTime);
			const dayIndex = (entryDate.getDay() + 6) % 7; // Mon=0 ... Sun=6
			if (dayIndex >= 0 && dayIndex < 7) {
				totals[dayIndex].minutes += entry.durationMinutes ?? 0;
				totals[dayIndex].entryCount++;
			}
		}
		return totals;
	}

	function startTimer() {
		if (timerInterval) clearInterval(timerInterval);
		timerInterval = setInterval(updateElapsed, 1000);
		updateElapsed();
	}

	function stopTimer() {
		if (timerInterval) {
			clearInterval(timerInterval);
			timerInterval = null;
		}
		elapsed = '00:00:00';
	}

	function updateElapsed() {
		if (!current) return;
		const start = new Date(current.startTime).getTime();
		const diff = Math.floor((Date.now() - start) / 1000);
		const h = Math.floor(diff / 3600);
		const m = Math.floor((diff % 3600) / 60);
		const s = diff % 60;
		elapsed = `${String(h).padStart(2, '0')}:${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}`;
	}

	async function loadCurrent() {
		try {
			current = await apiService.get<TimeEntryResponse>('/api/TimeTracking/current');
			if (current) {
				note = current.description ?? '';
				startTimer();
			}
		} catch {
			current = null;
		}
	}

	async function loadWeek() {
		try {
			const from = weekRange.start.toISOString();
			const to = weekRange.end.toISOString();
			weekEntries = await apiService.get<TimeEntryResponse[]>(
				`/api/TimeTracking?from=${from}&to=${to}&limit=200`
			);
		} catch {
			weekEntries = [];
		}
	}

	async function handleStart() {
		actionError = '';
		starting = true;
		try {
			const payload: StartTimeEntryRequest = {
				description: note.trim() || undefined,
				organizationId: orgContext.selectedOrgId ?? undefined
			};
			current = await apiService.post<TimeEntryResponse>('/api/TimeTracking/start', payload);
			startTimer();
			await loadWeek();
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
			const payload = { description: note.trim() || undefined };
			await apiService.post('/api/TimeTracking/stop', payload);
			current = null;
			stopTimer();
			note = '';
			await loadWeek();
		} catch (err: any) {
			actionError = err.response?.data?.message || 'Failed to stop timer.';
		} finally {
			stopping = false;
		}
	}

	async function deleteEntry(id: number) {
		if (!confirm('Delete this time entry?')) return;
		try {
			await apiService.delete(`/api/TimeTracking/${id}`);
			weekEntries = weekEntries.filter((e) => e.id !== id);
		} catch (err: any) {
			actionError = err.response?.data?.message || 'Failed to delete entry.';
		}
	}

	function changeWeek(dir: number) {
		weekOffset += dir;
		loadWeek();
	}

	function formatDuration(minutes?: number): string {
		if (minutes == null || minutes === 0) return '-';
		const h = Math.floor(minutes / 60);
		const m = Math.round(minutes % 60);
		if (h > 0) return `${h}h ${m}m`;
		return `${m}m`;
	}

	function formatHours(minutes: number): string {
		if (minutes === 0) return '-';
		return (minutes / 60).toFixed(1) + 'h';
	}

	function formatTime(iso: string): string {
		return new Date(iso).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
	}

	function formatDateShort(date: Date): string {
		return date.toLocaleDateString([], { month: 'short', day: 'numeric' });
	}

	function formatWeekLabel(range: { start: Date; end: Date }): string {
		const opts: Intl.DateTimeFormatOptions = { month: 'short', day: 'numeric' };
		return `${range.start.toLocaleDateString([], opts)} – ${range.end.toLocaleDateString([], opts)}`;
	}

	function isToday(date: Date): boolean {
		const now = new Date();
		return date.getFullYear() === now.getFullYear() &&
			date.getMonth() === now.getMonth() &&
			date.getDate() === now.getDate();
	}
</script>

<svelte:head>
	<title>Timer - Time Tracking</title>
</svelte:head>

<div class="page">
	{#if loading}
		<p class="muted">Loading...</p>
	{:else}
		{#if actionError}
			<div class="error-banner">{actionError}
				<button class="dismiss" onclick={() => (actionError = '')}>&times;</button>
			</div>
		{/if}

		<!-- Timer card -->
		<div class="timer-card" class:running={!!current}>
			<div class="timer-display">{elapsed}</div>

			{#if orgContext.selectedOrg}
				<div class="timer-org-label">Tracking for <strong>{orgContext.selectedOrg.name}</strong></div>
			{:else}
				<div class="timer-org-label muted-label">Personal (no organization)</div>
			{/if}

			<div class="timer-actions">
				{#if current}
					<button class="btn-stop" onclick={handleStop} disabled={stopping}>
						{stopping ? 'Stopping...' : 'Stop'}
					</button>
				{:else}
					<button class="btn-start" onclick={handleStart} disabled={starting}>
						{starting ? 'Starting...' : 'Start'}
					</button>
				{/if}
			</div>

			<input
				type="text"
				bind:value={note}
				placeholder="Optional note..."
				class="note-input"
				disabled={stopping}
			/>
		</div>

		<!-- Weekly summary -->
		<section class="week-section">
			<div class="week-header">
				<button class="week-nav" onclick={() => changeWeek(-1)}>&lsaquo;</button>
				<div class="week-title">
					<span class="week-label">{formatWeekLabel(weekRange)}</span>
					<span class="week-total">{formatHours(weekTotal)}</span>
				</div>
				<button class="week-nav" onclick={() => changeWeek(1)} disabled={weekOffset >= 0}>&rsaquo;</button>
			</div>

			<!-- Day bars -->
			<div class="day-grid">
				{#each dailyTotals as day}
					{@const maxMins = Math.max(...dailyTotals.map(d => d.minutes), 480)}
					{@const pct = maxMins > 0 ? Math.min((day.minutes / maxMins) * 100, 100) : 0}
					<div class="day-row" class:today={isToday(day.date)}>
						<span class="day-name">{day.name}</span>
						<span class="day-date">{formatDateShort(day.date)}</span>
						<div class="day-bar-track">
							<div class="day-bar-fill" style="width: {pct}%"></div>
						</div>
						<span class="day-hours">{formatHours(day.minutes)}</span>
					</div>
				{/each}
			</div>
		</section>

		<!-- Day entries detail -->
		<section class="entries-section">
			<h2>Entries this week</h2>

			{#if weekEntries.length === 0}
				<p class="muted">No entries this week. Start tracking above!</p>
			{:else}
				<div class="entries-list">
					{#each weekEntries as entry}
						<div class="entry-row" class:is-running={entry.isRunning}>
							<div class="entry-time">
								<span class="entry-time-range">
									{formatTime(entry.startTime)}{entry.endTime ? ` – ${formatTime(entry.endTime)}` : ''}
								</span>
								<span class="entry-date">{formatDateShort(new Date(entry.startTime))}</span>
							</div>
							<div class="entry-middle">
								{#if entry.organizationName}
									<span class="entry-org-tag">{entry.organizationName}</span>
								{/if}
								{#if entry.description}
									<span class="entry-note">{entry.description}</span>
								{/if}
								{#if entry.isRunning}
									<span class="running-badge">Running</span>
								{/if}
							</div>
							<div class="entry-dur">
								{entry.isRunning ? elapsed : formatDuration(entry.durationMinutes)}
							</div>
							<div class="entry-actions">
								{#if !entry.isRunning}
									<button class="btn-icon-danger" title="Delete" onclick={() => deleteEntry(entry.id)}>
										&times;
									</button>
								{/if}
							</div>
						</div>
					{/each}
				</div>
			{/if}
		</section>
	{/if}
</div>

<style>
	.muted { color: #9ca3af; }

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
		border-radius: 16px;
		padding: 2rem;
		border: 2px solid #e5e7eb;
		margin-bottom: 2rem;
		text-align: center;
		transition: border-color 0.3s;
	}

	.timer-card.running {
		border-color: #22c55e;
		box-shadow: 0 0 0 4px rgba(34, 197, 94, 0.1);
	}

	.timer-display {
		font-size: 3.5rem;
		font-weight: 700;
		font-variant-numeric: tabular-nums;
		color: #1a1a2e;
		margin-bottom: 0.5rem;
		letter-spacing: 0.05em;
	}

	.running .timer-display {
		color: #16a34a;
	}

	.timer-org-label {
		font-size: 0.875rem;
		color: #374151;
		margin-bottom: 1.25rem;
	}

	.muted-label {
		color: #9ca3af;
	}

	.timer-actions {
		margin-bottom: 1rem;
	}

	.note-input {
		display: block;
		width: 100%;
		max-width: 360px;
		margin: 0 auto;
		padding: 0.5rem 0.75rem;
		border: 1px solid #e5e7eb;
		border-radius: 8px;
		font-size: 0.8125rem;
		color: #6b7280;
		text-align: center;
		background: #fafafa;
	}

	.note-input:focus {
		outline: none;
		border-color: #d1d5db;
		background: white;
	}

	.note-input::placeholder { color: #d1d5db; }

	.btn-start {
		padding: 0.875rem 3rem;
		background: #22c55e;
		color: white;
		border: none;
		border-radius: 12px;
		font-size: 1.125rem;
		font-weight: 700;
		cursor: pointer;
		transition: background 0.15s;
		min-width: 160px;
	}

	.btn-start:hover:not(:disabled) { background: #16a34a; }
	.btn-start:disabled { opacity: 0.6; cursor: not-allowed; }

	.btn-stop {
		padding: 0.875rem 3rem;
		background: #ef4444;
		color: white;
		border: none;
		border-radius: 12px;
		font-size: 1.125rem;
		font-weight: 700;
		cursor: pointer;
		transition: background 0.15s;
		min-width: 160px;
	}

	.btn-stop:hover:not(:disabled) { background: #dc2626; }
	.btn-stop:disabled { opacity: 0.6; cursor: not-allowed; }

	/* Weekly summary */
	.week-section {
		background: white;
		border-radius: 12px;
		padding: 1.25rem 1.5rem;
		border: 1px solid #e5e7eb;
		margin-bottom: 2rem;
	}

	.week-header {
		display: flex;
		align-items: center;
		justify-content: space-between;
		margin-bottom: 1rem;
	}

	.week-nav {
		background: none;
		border: 1px solid #e5e7eb;
		width: 32px;
		height: 32px;
		border-radius: 8px;
		font-size: 1.25rem;
		cursor: pointer;
		color: #374151;
		display: flex;
		align-items: center;
		justify-content: center;
		transition: background 0.15s;
	}

	.week-nav:hover:not(:disabled) { background: #f3f4f6; }
	.week-nav:disabled { opacity: 0.3; cursor: not-allowed; }

	.week-title {
		text-align: center;
	}

	.week-label {
		display: block;
		font-weight: 600;
		font-size: 0.9375rem;
		color: #1a1a2e;
	}

	.week-total {
		display: block;
		font-size: 0.8125rem;
		color: #6b7280;
		margin-top: 0.125rem;
	}

	.day-grid {
		display: flex;
		flex-direction: column;
		gap: 0.5rem;
	}

	.day-row {
		display: grid;
		grid-template-columns: 36px 64px 1fr 56px;
		align-items: center;
		gap: 0.5rem;
		padding: 0.25rem 0;
	}

	.day-row.today {
		font-weight: 600;
	}

	.day-name {
		font-size: 0.8125rem;
		color: #374151;
	}

	.day-date {
		font-size: 0.75rem;
		color: #9ca3af;
	}

	.day-bar-track {
		height: 20px;
		background: #f3f4f6;
		border-radius: 4px;
		overflow: hidden;
	}

	.day-bar-fill {
		height: 100%;
		background: #3b82f6;
		border-radius: 4px;
		min-width: 0;
		transition: width 0.3s ease;
	}

	.today .day-bar-fill {
		background: #22c55e;
	}

	.day-hours {
		text-align: right;
		font-size: 0.8125rem;
		color: #374151;
		font-variant-numeric: tabular-nums;
	}

	/* Entries list */
	.entries-section h2 {
		margin: 0 0 1rem;
		font-size: 1.125rem;
		color: #374151;
	}

	.entries-list {
		background: white;
		border: 1px solid #e5e7eb;
		border-radius: 12px;
		overflow: hidden;
	}

	.entry-row {
		display: grid;
		grid-template-columns: auto 1fr auto auto;
		align-items: center;
		gap: 1rem;
		padding: 0.75rem 1rem;
		border-bottom: 1px solid #f3f4f6;
	}

	.entry-row:last-child { border-bottom: none; }

	.entry-row.is-running {
		background: #f0fdf4;
	}

	.entry-time {
		text-align: left;
	}

	.entry-time-range {
		display: block;
		font-size: 0.8125rem;
		color: #374151;
	}

	.entry-date {
		font-size: 0.6875rem;
		color: #9ca3af;
	}

	.entry-middle {
		display: flex;
		align-items: center;
		gap: 0.5rem;
		flex-wrap: wrap;
	}

	.entry-org-tag {
		font-size: 0.6875rem;
		background: #eff6ff;
		color: #2563eb;
		padding: 0.0625rem 0.5rem;
		border-radius: 999px;
		font-weight: 500;
	}

	.entry-note {
		font-size: 0.8125rem;
		color: #6b7280;
	}

	.running-badge {
		font-size: 0.6875rem;
		background: #dcfce7;
		color: #16a34a;
		padding: 0.125rem 0.5rem;
		border-radius: 999px;
		font-weight: 600;
		text-transform: uppercase;
	}

	.entry-dur {
		font-weight: 600;
		font-size: 0.875rem;
		color: #374151;
		min-width: 56px;
		text-align: right;
		font-variant-numeric: tabular-nums;
	}

	.entry-actions {
		min-width: 28px;
	}

	.btn-icon-danger {
		background: none;
		border: none;
		color: #dc2626;
		font-size: 1.125rem;
		cursor: pointer;
		padding: 0.125rem 0.375rem;
		border-radius: 4px;
		line-height: 1;
		opacity: 0.4;
		transition: opacity 0.15s;
	}

	.btn-icon-danger:hover {
		opacity: 1;
		background: #fef2f2;
	}
</style>
