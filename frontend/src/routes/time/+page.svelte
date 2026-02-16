<script lang="ts">
	import { onMount, onDestroy } from 'svelte';
	import { orgContext } from '$lib/stores/orgContext.svelte';
	import { apiService } from '$lib/apiService';
	import type { TimeEntryResponse, StartTimeEntryRequest, UpdateTimeEntryRequest, WorkScheduleResponse, OrganizationDetailResponse } from '$lib/types';

	let current = $state<TimeEntryResponse | null>(null);
	let weekEntries = $state<TimeEntryResponse[]>([]);
	let loading = $state(true);
	let actionError = $state('');

	// Work schedule (target hours)
	let workSchedule = $state<WorkScheduleResponse | null>(null);
	// Org detail (for settings like allowEditPause)
	let orgDetail = $state<OrganizationDetailResponse | null>(null);

	// Timer display
	let elapsed = $state('00:00:00');
	let timerInterval: ReturnType<typeof setInterval> | null = null;

	// Optional note (de-emphasized)
	let note = $state('');
	let starting = $state(false);
	let stopping = $state(false);
	let showNoteSuggestions = $state(false);

	// Previous descriptions for quick pick
	const previousDescriptions = $derived(() => {
		const descs = weekEntries
			.map(e => e.description)
			.filter((d): d is string => !!d && d.trim().length > 0);
		return [...new Set(descs)];
	});

	// Week navigation
	let weekOffset = $state(0);

	const weekRange = $derived(getWeekRange(weekOffset));
	const dailyTotals = $derived(computeDailyTotals(weekEntries, weekRange, workSchedule));
	const weekTotal = $derived(dailyTotals.reduce((s, d) => s + d.minutes, 0));
	const weekTargetSoFar = $derived(dailyTotals.filter(d => d.isPastOrToday).reduce((s, d) => s + d.targetMinutes, 0));
	const weekTargetFull = $derived(workSchedule?.weeklyWorkHours ? workSchedule.weeklyWorkHours * 60 : dailyTotals.reduce((s, d) => s + d.targetMinutes, 0));

	onMount(async () => {
		await Promise.all([loadCurrent(), loadWeek(), loadWorkSchedule()]);
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

	function computeDailyTotals(entries: TimeEntryResponse[], range: { start: Date; end: Date }, schedule: WorkScheduleResponse | null) {
		const days = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];
		const targets = schedule
			? [schedule.targetMon, schedule.targetTue, schedule.targetWed, schedule.targetThu, schedule.targetFri, 0, 0]
			: [0, 0, 0, 0, 0, 0, 0];
		const now = new Date();
		now.setHours(23, 59, 59, 999);
		const totals = days.map((name, i) => {
			const date = new Date(range.start);
			date.setDate(range.start.getDate() + i);
			const isPastOrToday = date <= now;
			return { name, date: new Date(date), minutes: 0, targetMinutes: targets[i] * 60, entryCount: 0, isPastOrToday };
		});

		for (const entry of entries) {
			if (entry.isRunning) continue;
			const entryDate = new Date(entry.startTime);
			const dayIndex = (entryDate.getDay() + 6) % 7; // Mon=0 ... Sun=6
			if (dayIndex >= 0 && dayIndex < 7) {
				// Use net duration (after pause deduction) when available
				totals[dayIndex].minutes += entry.netDurationMinutes ?? entry.durationMinutes ?? 0;
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

	async function loadWorkSchedule() {
		if (!orgContext.selectedOrgSlug) {
			workSchedule = null;
			orgDetail = null;
			return;
		}
		try {
			workSchedule = await apiService.get<WorkScheduleResponse>(
				`/api/Organizations/${orgContext.selectedOrgSlug}/work-schedule`
			);
		} catch {
			workSchedule = null;
		}
		try {
			orgDetail = await apiService.get<OrganizationDetailResponse>(
				`/api/Organizations/${orgContext.selectedOrgSlug}`
			);
		} catch {
			orgDetail = null;
		}
	}

	async function handleStart() {
		actionError = '';
		starting = true;
		try {
			const payload: StartTimeEntryRequest = {
				description: note.trim() || undefined,
				organizationSlug: orgContext.selectedOrgSlug ?? undefined
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

	// Edit entry
	let editingEntryId = $state<number | null>(null);
	let editStartTime = $state('');
	let editEndTime = $state('');
	let editDescription = $state('');
	let editPause = $state<number>(0);
	let editError = $state('');
	let editSaving = $state(false);

	function toLocalDateTimeInput(iso: string): string {
		const d = new Date(iso);
		const pad = (n: number) => String(n).padStart(2, '0');
		return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
	}

	function startEditEntry(entry: TimeEntryResponse) {
		editingEntryId = entry.id;
		editStartTime = toLocalDateTimeInput(entry.startTime);
		editEndTime = entry.endTime ? toLocalDateTimeInput(entry.endTime) : '';
		editDescription = entry.description ?? '';
		editPause = entry.pauseDurationMinutes;
		editError = '';
	}

	function cancelEditEntry() {
		editingEntryId = null;
		editError = '';
	}

	async function saveEditEntry(entryId: number) {
		editError = '';
		editSaving = true;
		try {
			const payload: UpdateTimeEntryRequest = {};
			if (editStartTime) payload.startTime = new Date(editStartTime).toISOString();
			if (editEndTime) payload.endTime = new Date(editEndTime).toISOString();
			payload.description = editDescription.trim() || undefined;
			if (orgDetail?.allowEditPause) {
				payload.pauseDurationMinutes = Math.max(0, Number(editPause) || 0);
			}
			await apiService.put(`/api/TimeTracking/${entryId}`, payload);
			await loadWeek();
			editingEntryId = null;
		} catch (err: any) {
			editError = err.response?.data?.message || err.response?.data || 'Failed to update entry.';
		} finally {
			editSaving = false;
		}
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

			<div class="note-wrapper">
				<input
					type="text"
					bind:value={note}
					placeholder="Optional note..."
					class="note-input"
					disabled={stopping}
					onfocus={() => showNoteSuggestions = true}
					onblur={() => setTimeout(() => showNoteSuggestions = false, 200)}
				/>
				{#if showNoteSuggestions && previousDescriptions().length > 0}
					<div class="note-suggestions">
						{#each previousDescriptions() as desc}
							<button
								class="note-suggestion"
								onmousedown={() => { note = desc; showNoteSuggestions = false; }}
							>{desc}</button>
						{/each}
					</div>
				{/if}
			</div>
		</div>

		<!-- Weekly summary -->
		<section class="week-section">
			<div class="week-header">
				<button class="week-nav" onclick={() => changeWeek(-1)}>&lsaquo;</button>
				<div class="week-title">
					<span class="week-label">{formatWeekLabel(weekRange)}</span>
					<span class="week-total">
						{formatHours(weekTotal)}{#if weekTargetFull > 0} / {formatHours(weekTargetFull)}{/if}
						{#if weekTargetSoFar > 0}
							{@const weekDelta = weekTotal - weekTargetSoFar}
							<span class="week-delta" class:positive={weekDelta > 0} class:negative={weekDelta < 0}>
								({weekDelta > 0 ? '+' : ''}{formatHours(weekDelta)})
							</span>
						{/if}
					</span>
					{#if weekTargetFull > 0}
						{@const pctWeek = Math.min((weekTotal / weekTargetFull) * 100, 100)}
						<div class="week-progress-track">
							<div class="week-progress-fill" style="width: {pctWeek}%"></div>
						</div>
					{/if}
				</div>
				<button class="week-nav" onclick={() => changeWeek(1)} disabled={weekOffset >= 0}>&rsaquo;</button>
			</div>

			<!-- Day bars -->
			<div class="day-grid">
				{#each dailyTotals as day}
					{@const maxMins = Math.max(...dailyTotals.map(d => Math.max(d.minutes, d.targetMinutes)), 480)}
					{@const pct = maxMins > 0 ? Math.min((day.minutes / maxMins) * 100, 100) : 0}
					{@const targetPct = maxMins > 0 && day.targetMinutes > 0 ? Math.min((day.targetMinutes / maxMins) * 100, 100) : 0}
					{@const delta = day.isPastOrToday && day.targetMinutes > 0 ? day.minutes - day.targetMinutes : 0}
					<div class="day-row" class:today={isToday(day.date)} class:future={!day.isPastOrToday}>
						<span class="day-name">{day.name}</span>
						<span class="day-date">{formatDateShort(day.date)}</span>
						<div class="day-bar-track">
							{#if targetPct > 0}
								<div class="day-bar-target" style="left: {targetPct}%"></div>
							{/if}
							<div class="day-bar-fill" style="width: {pct}%"></div>
						</div>
						<span class="day-hours">
							{formatHours(day.minutes)}{#if day.targetMinutes > 0}<span class="day-target-label"> / {formatHours(day.targetMinutes)}</span>{/if}
							{#if delta !== 0}
								<span class="day-delta" class:positive={delta > 0} class:negative={delta < 0}>
									{delta > 0 ? '+' : ''}{formatHours(delta)}
								</span>
							{/if}
						</span>
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
						{#if editingEntryId === entry.id}
							<!-- Inline edit form -->
							<div class="entry-edit-row">
								{#if editError}
									<div class="edit-error">{editError}</div>
								{/if}
								<div class="edit-fields">
									<div class="edit-field">
										<!-- svelte-ignore a11y_label_has_associated_control -->
										<label>Start</label>
										<input type="datetime-local" bind:value={editStartTime} disabled={editSaving} />
									</div>
									<div class="edit-field">
										<!-- svelte-ignore a11y_label_has_associated_control -->
										<label>End</label>
										<input type="datetime-local" bind:value={editEndTime} disabled={editSaving} />
									</div>
									<div class="edit-field edit-field-desc">
										<!-- svelte-ignore a11y_label_has_associated_control -->
										<label>Note</label>
										<input type="text" bind:value={editDescription} placeholder="Optional note" disabled={editSaving} />
									</div>
									{#if orgDetail?.allowEditPause}
										<div class="edit-field edit-field-pause">
											<!-- svelte-ignore a11y_label_has_associated_control -->
											<label>Pause (min)</label>
											<input type="number" min="0" bind:value={editPause} disabled={editSaving} />
										</div>
									{/if}
								</div>
								<div class="edit-actions">
									<button class="btn-save-sm" onclick={() => saveEditEntry(entry.id)} disabled={editSaving}>
										{editSaving ? 'Saving...' : 'Save'}
									</button>
									<button class="btn-cancel-sm" onclick={cancelEditEntry}>Cancel</button>
								</div>
							</div>
						{:else}
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
									{#if entry.pauseDurationMinutes > 0}
										{#if orgDetail?.allowEditPause && !entry.isRunning}
											<button class="pause-badge pause-badge-edit" title="Click to edit pause" onclick={() => startEditEntry(entry)}>-{entry.pauseDurationMinutes}m pause &#9998;</button>
										{:else}
											<span class="pause-badge">-{entry.pauseDurationMinutes}m pause</span>
										{/if}
									{/if}
								</div>
								<div class="entry-dur">
									{#if entry.isRunning}
										{elapsed}
									{:else if entry.pauseDurationMinutes > 0}
										<span class="net-dur">{formatDuration(entry.netDurationMinutes ?? undefined)}</span>
										<span class="gross-dur">({formatDuration(entry.durationMinutes)})</span>
									{:else}
										{formatDuration(entry.durationMinutes)}
									{/if}
								</div>
								<div class="entry-actions">
									{#if !entry.isRunning}
										<button class="btn-icon-edit" title="Edit" onclick={() => startEditEntry(entry)}>&#9998;</button>
										<button class="btn-icon-danger" title="Delete" onclick={() => deleteEntry(entry.id)}>
											&times;
										</button>
									{/if}
								</div>
							</div>
						{/if}
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

	.note-wrapper {
		position: relative;
		width: 100%;
		max-width: 360px;
		margin: 0 auto;
	}

	.note-wrapper .note-input {
		max-width: none;
		margin: 0;
	}

	.note-suggestions {
		position: absolute;
		top: 100%;
		left: 0;
		right: 0;
		background: white;
		border: 1px solid #e5e7eb;
		border-radius: 8px;
		box-shadow: 0 4px 12px rgba(0, 0, 0, 0.1);
		z-index: 10;
		max-height: 160px;
		overflow-y: auto;
		margin-top: 2px;
	}

	.note-suggestion {
		display: block;
		width: 100%;
		padding: 0.5rem 0.75rem;
		border: none;
		background: none;
		text-align: left;
		font-size: 0.8125rem;
		color: #374151;
		cursor: pointer;
		transition: background 0.1s;
	}

	.note-suggestion:hover {
		background: #f3f4f6;
	}

	.note-suggestion + .note-suggestion {
		border-top: 1px solid #f3f4f6;
	}

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
		grid-template-columns: 36px 52px 1fr auto;
		align-items: center;
		gap: 0.5rem;
		padding: 0.25rem 0;
	}

	.day-row.today {
		font-weight: 600;
	}

	.day-row.future {
		opacity: 0.5;
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
		position: relative;
	}

	.day-bar-fill {
		height: 100%;
		background: #3b82f6;
		border-radius: 4px;
		min-width: 0;
		transition: width 0.3s ease;
	}

	.day-bar-target {
		position: absolute;
		top: 0;
		bottom: 0;
		width: 2px;
		background: #9ca3af;
		z-index: 1;
		opacity: 0.7;
	}

	.today .day-bar-fill {
		background: #22c55e;
	}

	.day-hours {
		text-align: right;
		font-size: 0.8125rem;
		color: #374151;
		font-variant-numeric: tabular-nums;
		white-space: nowrap;
	}

	.day-target-label {
		color: #9ca3af;
		font-weight: 400;
		font-size: 0.6875rem;
	}

	.day-delta {
		display: inline-block;
		font-size: 0.6875rem;
		font-weight: 500;
		margin-left: 0.25rem;
	}

	.day-delta.positive { color: #16a34a; }
	.day-delta.negative { color: #dc2626; }

	.week-delta {
		font-size: 0.75rem;
		font-weight: 500;
		margin-left: 0.25rem;
	}

	.week-delta.positive { color: #16a34a; }
	.week-delta.negative { color: #dc2626; }

	/* Week progress */
	.week-progress-track {
		height: 4px;
		background: #e5e7eb;
		border-radius: 2px;
		overflow: hidden;
		margin-top: 0.375rem;
		width: 100%;
		max-width: 140px;
		margin-left: auto;
		margin-right: auto;
	}

	.week-progress-fill {
		height: 100%;
		background: #3b82f6;
		border-radius: 2px;
		transition: width 0.3s ease;
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

	/* Edit entry inline */
	.entry-edit-row {
		padding: 1rem;
		background: #f9fafb;
		border-bottom: 1px solid #e5e7eb;
	}

	.edit-error {
		background: #fef2f2;
		color: #dc2626;
		padding: 0.5rem 0.75rem;
		border-radius: 6px;
		margin-bottom: 0.75rem;
		font-size: 0.8125rem;
		border-left: 3px solid #dc2626;
	}

	.edit-fields {
		display: flex;
		gap: 0.75rem;
		flex-wrap: wrap;
		margin-bottom: 0.75rem;
	}

	.edit-field {
		display: flex;
		flex-direction: column;
		gap: 0.25rem;
	}

	.edit-field label {
		font-size: 0.75rem;
		font-weight: 600;
		color: #6b7280;
		text-transform: uppercase;
		letter-spacing: 0.025em;
	}

	.edit-field input {
		padding: 0.5rem 0.625rem;
		border: 1px solid #d1d5db;
		border-radius: 6px;
		font-size: 0.8125rem;
		font-family: inherit;
	}

	.edit-field input:focus {
		outline: none;
		border-color: #3b82f6;
		box-shadow: 0 0 0 2px rgba(59, 130, 246, 0.1);
	}

	.edit-field-desc {
		flex: 1;
		min-width: 150px;
	}

	.edit-field-desc input {
		width: 100%;
	}

	.edit-actions {
		display: flex;
		gap: 0.5rem;
	}

	.btn-save-sm {
		background: #3b82f6;
		color: white;
		padding: 0.375rem 0.875rem;
		border-radius: 6px;
		font-size: 0.8125rem;
		font-weight: 600;
		border: none;
		cursor: pointer;
		transition: background 0.15s;
	}

	.btn-save-sm:hover:not(:disabled) { background: #2563eb; }
	.btn-save-sm:disabled { opacity: 0.6; cursor: not-allowed; }

	.btn-cancel-sm {
		background: white;
		color: #4b5563;
		padding: 0.375rem 0.875rem;
		border-radius: 6px;
		font-size: 0.8125rem;
		font-weight: 500;
		border: 1px solid #d1d5db;
		cursor: pointer;
		transition: background 0.15s;
	}

	.btn-cancel-sm:hover { background: #f9fafb; }

	/* Pause badge */
	.pause-badge {
		font-size: 0.6875rem;
		background: #fff7ed;
		color: #c2410c;
		padding: 0.0625rem 0.5rem;
		border-radius: 999px;
		font-weight: 500;
	}

	.pause-badge-edit {
		border: 1px solid #fed7aa;
		cursor: pointer;
		transition: background 0.15s, border-color 0.15s;
	}

	.pause-badge-edit:hover {
		background: #fed7aa;
		border-color: #fb923c;
	}

	.edit-field-pause input {
		width: 5rem;
	}

	.net-dur {
		display: block;
	}

	.gross-dur {
		display: block;
		font-size: 0.6875rem;
		color: #9ca3af;
		font-weight: 400;
	}

	/* Edit icon */
	.btn-icon-edit {
		background: none;
		border: none;
		color: #3b82f6;
		font-size: 0.9375rem;
		cursor: pointer;
		padding: 0.125rem 0.375rem;
		border-radius: 4px;
		line-height: 1;
		opacity: 0.4;
		transition: opacity 0.15s;
	}

	.btn-icon-edit:hover {
		opacity: 1;
		background: #eff6ff;
	}
</style>
