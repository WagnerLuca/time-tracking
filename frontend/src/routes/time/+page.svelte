<script lang="ts">
	import { onMount, onDestroy } from 'svelte';
	import { orgContext } from '$lib/stores/orgContext.svelte';
	import { auth } from '$lib/stores/auth.svelte';
	import { timeTrackingApi, organizationsApi, workScheduleApi } from '$lib/apiClient';
	import type { TimeEntryResponse, StartTimeEntryRequest, WorkScheduleResponse, OrganizationDetailResponse } from '$lib/api';
	import { formatHoursDecimal, formatDelta, formatDuration, formatTime, formatDateShort, formatWeekLabel, formatHours } from '$lib/utils/formatters';
	import { dateKey, isToday, getWeekRange } from '$lib/utils/dateHelpers';
	import { getDayTarget, getAbsenceCredit, getDayType, getDayTypeLabel } from '$lib/utils/scheduleHelpers';
	import { DAY_NAMES, MAX_ENTRIES_FOR_OVERTIME } from '$lib/utils/constants';
	import { extractErrorMessage } from '$lib/utils/errorHandler';
	import { canEditEntries, canRequestEditEntries, canEditPause, canRequestEditPause } from '$lib/utils/orgRules';
	import { loadDaysOff, emptyDaysOff, type DaysOffData } from '$lib/utils/daysOff';
	import { createEntryEditor } from '$lib/utils/entryEditor.svelte';
	import LoadingSpinner from '$lib/components/LoadingSpinner.svelte';
	import DayTypeLegend from '$lib/components/DayTypeLegend.svelte';
	import EntryEditForm from '$lib/components/EntryEditForm.svelte';
	import RequestModal from '$lib/components/RequestModal.svelte';

	let current = $state<TimeEntryResponse | null>(null);
	let weekEntries = $state<TimeEntryResponse[]>([]);
	let loading = $state(true);
	let actionError = $state('');

	// Work schedule (target hours)
	let workSchedule = $state<WorkScheduleResponse | null>(null);
	// Org detail (for settings like editPauseMode)
	let orgDetail = $state<OrganizationDetailResponse | null>(null);

	// Cumulative overtime
	let cumulativeOvertime = $state(0);
	let hasOvertimeData = $state(false);
	let cumulativeOvertimeWeekEnd = $state(0);
	let hasWeekEndOvertimeData = $state(false);
	// Days off (holidays + absences)
	let daysOff = $state<DaysOffData>(emptyDaysOff());
	let holidayDates = $derived(daysOff.holidayDates);
	let halfDayHolidays = $derived(daysOff.halfDayHolidays);
	let sickDayDates = $derived(daysOff.sickDayDates);
	let vacationDates = $derived(daysOff.vacationDates);
	let otherAbsenceDates = $derived(daysOff.otherAbsenceDates);
	let halfDayAbsences = $derived(daysOff.halfDayAbsences);
	let daysOffSet = $derived(daysOff.daysOffSet);

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
		await loadCumulativeOvertime();
		loading = false;
	});

	// Reload data when organization context changes
	let prevOrgSlug: string | null | undefined = undefined;
	$effect(() => {
		const currentSlug = orgContext.selectedOrgSlug;
		if (currentSlug && currentSlug !== prevOrgSlug) {
			loadWeek();
			loadWorkSchedule().then(() => loadCumulativeOvertime());
		}
		prevOrgSlug = currentSlug;
	});

	onDestroy(() => {
		if (timerInterval) clearInterval(timerInterval);
	});

	function computeDailyTotals(entries: TimeEntryResponse[], range: { start: Date; end: Date }, schedule: WorkScheduleResponse | null) {
		const days = DAY_NAMES;
		const now = new Date();
		now.setHours(23, 59, 59, 999);
		const totals = days.map((name, i) => {
			const date = new Date(range.start);
			date.setDate(range.start.getDate() + i);
			const isPastOrToday = date <= now;
			return { name, date: new Date(date), minutes: 0, targetMinutes: getDayTarget(date, schedule, holidayDates, [], halfDayHolidays), entryCount: 0, isPastOrToday };
		});

		for (const entry of entries) {
			if (entry.isRunning) continue;
			const entryDate = new Date(entry.startTime!);
			const dayIndex = (entryDate.getDay() + 6) % 7; // Mon=0 ... Sun=6
			if (dayIndex >= 0 && dayIndex < 7) {
				// Use net duration (after pause deduction) when available
				totals[dayIndex].minutes += entry.netDurationMinutes ?? entry.durationMinutes ?? 0;
				totals[dayIndex].entryCount++;
			}
		}
		return totals;
	}

	function computeCumulativeUntilDate(entries: TimeEntryResponse[], firstTrackedDate: Date, untilDate: Date): number {
		if (!workSchedule) return 0;

		const rangeEnd = new Date(untilDate);
		rangeEnd.setHours(23, 59, 59, 999);

		let workedMinutes = 0;
		for (const entry of entries) {
			if (!entry.startTime) continue;
			const entryStart = new Date(entry.startTime);
			if (entryStart > rangeEnd) break;
			workedMinutes += entry.netDurationMinutes ?? entry.durationMinutes ?? 0;
		}

		let targetMinutes = 0;
		let absenceCredits = 0;
		const cursor = new Date(firstTrackedDate);
		while (cursor <= rangeEnd) {
			targetMinutes += getDayTarget(cursor, workSchedule, holidayDates, [], halfDayHolidays);
			absenceCredits += getAbsenceCredit(cursor, workSchedule, holidayDates, sickDayDates, vacationDates, otherAbsenceDates, [], halfDayAbsences, halfDayHolidays);
			cursor.setDate(cursor.getDate() + 1);
		}

		const initialOvertime = (workSchedule.initialOvertimeMode !== 'Disabled' && workSchedule.initialOvertimeHours)
			? workSchedule.initialOvertimeHours * 60
			: 0;

		return workedMinutes + absenceCredits - targetMinutes + initialOvertime;
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
		const start = new Date(current.startTime!).getTime();
		const diff = Math.floor((Date.now() - start) / 1000);
		const h = Math.floor(diff / 3600);
		const m = Math.floor((diff % 3600) / 60);
		const s = diff % 60;
		elapsed = `${String(h).padStart(2, '0')}:${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}`;
	}

	async function loadCurrent() {
		try {
			const { data } = await timeTrackingApi.apiV1TimeTrackingCurrentGet();
			current = data;
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
			const orgId = orgContext.selectedOrgId ?? undefined;
			const { data } = await timeTrackingApi.apiV1TimeTrackingGet(orgId, from, to, 200);
			weekEntries = data.items ?? [];
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
			const { data: ws } = await workScheduleApi.apiV1OrganizationsSlugWorkScheduleGet(orgContext.selectedOrgSlug!);
			workSchedule = ws;
		} catch {
			workSchedule = null;
		}
		try {
			const { data: od } = await organizationsApi.apiV1OrganizationsSlugGet(orgContext.selectedOrgSlug!);
			orgDetail = od;
		} catch {
			orgDetail = null;
		}
	}

	async function loadCumulativeOvertime() {
		daysOff = await loadDaysOff(orgContext.selectedOrgSlug, auth.user?.id);

		if (!workSchedule) {
			cumulativeOvertime = 0;
			hasOvertimeData = false;
			cumulativeOvertimeWeekEnd = 0;
			hasWeekEndOvertimeData = false;
			return;
		}
		try {
			const orgId = orgContext.selectedOrgId ?? undefined;
			const { data: allEntriesPage } = await timeTrackingApi.apiV1TimeTrackingGet(orgId, undefined, undefined, MAX_ENTRIES_FOR_OVERTIME);
			const allEntries = allEntriesPage.items ?? [];
			const sorted = [...allEntries]
				.filter(e => !e.isRunning && e.endTime)
				.sort((a, b) => new Date(a.startTime!).getTime() - new Date(b.startTime!).getTime());
			const initialOvertime = (workSchedule.initialOvertimeMode !== 'Disabled' && workSchedule.initialOvertimeHours)
				? workSchedule.initialOvertimeHours * 60
				: 0;

			if (sorted.length > 0) {
				const firstDate = new Date(sorted[0].startTime!);
				firstDate.setHours(0, 0, 0, 0);
				cumulativeOvertime = computeCumulativeUntilDate(sorted, firstDate, new Date());
				cumulativeOvertimeWeekEnd = computeCumulativeUntilDate(sorted, firstDate, weekRange.end);
				hasOvertimeData = true;
				hasWeekEndOvertimeData = true;
			} else {
				cumulativeOvertime = initialOvertime;
				cumulativeOvertimeWeekEnd = initialOvertime;
				hasOvertimeData = initialOvertime !== 0;
				hasWeekEndOvertimeData = true;
			}
		} catch {
			cumulativeOvertime = 0;
			hasOvertimeData = false;
			cumulativeOvertimeWeekEnd = 0;
			hasWeekEndOvertimeData = false;
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
			const { data } = await timeTrackingApi.apiV1TimeTrackingStartPost(payload);
			current = data;
			startTimer();
			await loadWeek();
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
			const payload = { description: note.trim() || undefined };
			await timeTrackingApi.apiV1TimeTrackingStopPost(payload);
			current = null;
			stopTimer();
			note = '';
			loadCumulativeOvertime();
			await loadWeek();
		} catch (err) {
			actionError = extractErrorMessage(err, 'Failed to stop timer.');
		} finally {
			stopping = false;
		}
	}

	async function deleteEntry(id: number) {
		if (!confirm('Delete this time entry?')) return;
		try {
			await timeTrackingApi.apiV1TimeTrackingIdDelete(id);
			weekEntries = weekEntries.filter((e) => e.id !== id);
		} catch (err) {
			editor.actionError = extractErrorMessage(err, 'Failed to delete entry.');
		}
	}

	function changeWeek(dir: number) {
		weekOffset += dir;
		loadWeek();
		loadCumulativeOvertime();
	}

	// Shared entry editing + request logic
	const editor = createEntryEditor({
		getOrgDetail: () => orgDetail,
		getOrgSlug: () => orgContext.selectedOrgSlug,
		onSaved: () => loadWeek(),
		onDeleted: async () => { weekEntries = weekEntries.filter(e => e.id !== editor.editingEntryId); },
		findEntry: (id) => weekEntries.find(e => e.id === id)
	});
</script>

<svelte:head>
	<title>Timer - Time Tracking</title>
</svelte:head>

<div class="max-w-3xl mx-auto p-6">
	{#if loading}
		<LoadingSpinner message="Loading..." size="sm" />
	{:else}
		{#if actionError}
			<div class="alert alert-error mb-4 text-sm">{actionError}
				<button class="btn btn-ghost btn-sm text-error text-xl" onclick={() => (actionError = '')}>&times;</button>
			</div>
		{/if}

		<!-- Timer card -->
		<div class="card bg-base-100 border-2 {current ? 'border-success shadow-[0_0_0_4px] shadow-success/10' : 'border-base-300'} p-8 mb-8 text-center transition-colors">
			<div class="text-6xl font-bold tabular-nums {current ? 'text-success' : 'text-base-content'} mb-2 tracking-wider">{elapsed}</div>

			{#if orgContext.selectedOrg}
				<div class="text-sm text-base-content/70 mb-5">Tracking for <strong>{orgContext.selectedOrg.name}</strong></div>
			{:else}
				<div class="text-sm text-base-content/40 mb-5">Personal (no organization)</div>
			{/if}

			<div class="mb-4">
				{#if current}
					<button class="btn btn-error btn-lg min-w-40 text-lg font-bold" onclick={handleStop} disabled={stopping}>
						{stopping ? 'Stopping...' : 'Stop'}
					</button>
				{:else}
					<button class="btn btn-success btn-lg min-w-40 text-lg font-bold" onclick={handleStart} disabled={starting}>
						{starting ? 'Starting...' : 'Start'}
					</button>
				{/if}
			</div>

			<div class="relative w-full max-w-[360px] mx-auto">
				<input
					type="text"
					bind:value={note}
					placeholder="Optional note..."
					class="input input-bordered input-sm w-full text-center bg-base-200/50 text-base-content/60"
					disabled={stopping}
					onfocus={() => showNoteSuggestions = true}
					onblur={() => setTimeout(() => showNoteSuggestions = false, 200)}
				/>
				{#if showNoteSuggestions && previousDescriptions().length > 0}
					<div class="absolute top-full left-0 right-0 bg-base-100 border border-base-300 rounded-lg shadow-lg z-10 max-h-40 overflow-y-auto mt-0.5">
						{#each previousDescriptions() as desc}
							<button
								class="block w-full py-2 px-3 border-none bg-transparent text-left text-sm text-base-content/70 cursor-pointer hover:bg-base-200 border-t border-t-base-200 first:border-t-0"
								onmousedown={() => { note = desc; showNoteSuggestions = false; }}
							>{desc}</button>
						{/each}
					</div>
				{/if}
			</div>
		</div>

		<!-- Weekly summary -->
		<section class="card bg-base-100 border border-base-300 p-5 mb-8">
			<div class="flex items-center justify-between mb-4">
				<button class="btn btn-ghost btn-sm btn-square text-xl" onclick={() => changeWeek(-1)}>&lsaquo;</button>
				<div class="text-center">
					<span class="block font-semibold text-base text-base-content">{formatWeekLabel(weekRange)}</span>
					<span class="block text-sm text-base-content/60 mt-0.5">
						{formatHoursDecimal(weekTotal)}{#if weekTargetFull > 0} / {formatHoursDecimal(weekTargetFull)}{/if}
						{#if weekTargetSoFar > 0}
							{@const weekDelta = weekTotal - weekTargetSoFar}
							<span class="{weekDelta > 0 ? 'text-success' : weekDelta < 0 ? 'text-error' : ''}">
								({weekDelta > 0 ? '+' : ''}{formatHours(weekDelta)})
							</span>
						{/if}
					</span>
					{#if weekTargetFull > 0}
						{@const pctWeek = Math.min((weekTotal / weekTargetFull) * 100, 100)}
						<div class="h-1 bg-base-200 rounded-full overflow-hidden mt-1.5 w-full max-w-[140px] mx-auto">
							<div class="h-full bg-primary rounded-full transition-all duration-300" style="width: {pctWeek}%"></div>
						</div>
					{/if}
				</div>
				<button class="btn btn-ghost btn-sm btn-square text-xl" onclick={() => changeWeek(1)} disabled={weekOffset >= 0}>&rsaquo;</button>
			</div>

			<!-- Day bars -->
			<div class="flex flex-col gap-2">
				{#each dailyTotals as day}
					{@const maxMins = Math.max(...dailyTotals.map(d => Math.max(d.minutes, d.targetMinutes)), 480)}
					{@const pct = maxMins > 0 ? Math.min((day.minutes / maxMins) * 100, 100) : 0}
					{@const targetPct = maxMins > 0 && day.targetMinutes > 0 ? Math.min((day.targetMinutes / maxMins) * 100, 100) : 0}
					{@const delta = day.isPastOrToday && day.targetMinutes > 0 ? day.minutes - day.targetMinutes : 0}
					{@const dayType = getDayType(day.date, holidayDates, sickDayDates, vacationDates, otherAbsenceDates)}
					<div class="grid grid-cols-[36px_70px_1fr_auto] items-center gap-2 py-1.5 px-2 {isToday(day.date) ? 'font-semibold' : ''} {!day.isPastOrToday ? 'opacity-50' : ''} {dayType === 'holiday' ? 'bg-secondary/10 rounded-md' : dayType === 'sick' ? 'bg-error/10 rounded-md' : dayType === 'vacation' ? 'bg-accent/10 rounded-md' : dayType === 'other-absence' ? 'bg-base-300/30 rounded-md' : ''}">
						<span class="text-sm text-base-content/70">{day.name}</span>
						<span class="text-xs text-base-content/40">{formatDateShort(day.date)}{#if dayType} <span class="w-2 h-2 rounded-full shrink-0 inline-block align-middle ml-1 {dayType === 'holiday' ? 'bg-secondary' : dayType === 'sick' ? 'bg-error' : dayType === 'vacation' ? 'bg-accent' : 'bg-base-content/40'}" title={getDayTypeLabel(day.date, holidayDates, sickDayDates, vacationDates, otherAbsenceDates, halfDayHolidays, halfDayAbsences)}></span>{/if}</span>
						<div class="h-5 bg-base-200 rounded overflow-hidden relative">
							{#if targetPct > 0}
								<div class="absolute top-0 bottom-0 w-0.5 bg-base-content/30 z-[1]" style="left: {targetPct}%"></div>
							{/if}
							<div class="h-full {isToday(day.date) ? 'bg-success' : 'bg-primary'} rounded min-w-0 transition-all duration-300" style="width: {pct}%"></div>
						</div>
						<span class="text-right text-sm text-base-content/70 tabular-nums whitespace-nowrap min-w-[110px]">
							{formatHoursDecimal(day.minutes)}{#if day.targetMinutes > 0}<span class="text-base-content/40 font-normal text-xs"> / {formatHoursDecimal(day.targetMinutes)}</span>{/if}
							{#if delta !== 0}
								<span class="{delta > 0 ? 'text-success' : delta < 0 ? 'text-error' : ''}">
									{delta > 0 ? '+' : ''}{formatHours(delta)}
								</span>
							{/if}
						</span>
					</div>
				{/each}
			</div>

			<DayTypeLegend {holidayDates} {sickDayDates} {vacationDates} {otherAbsenceDates} />
		</section>

		<!-- Cumulative overtime -->
		{#if (hasOvertimeData || hasWeekEndOvertimeData) && workSchedule}
			<div class="card bg-base-100 border-2 p-4 mb-8 {cumulativeOvertime > 0 ? 'border-success/50 bg-success/5' : cumulativeOvertime < 0 ? 'border-error/50 bg-error/5' : 'border-base-300'}">
				<div class="grid grid-cols-1 md:grid-cols-2 gap-4">
					<div class="flex flex-col gap-1">
						<span class="text-xs text-base-content/40 uppercase font-semibold tracking-wider">Cumulative Balance</span>
						<span class="text-xl font-bold tabular-nums {cumulativeOvertime > 0 ? 'text-success' : cumulativeOvertime < 0 ? 'text-error' : ''}">
							{formatDelta(cumulativeOvertime)}
						</span>
						<span class="text-xs text-base-content/40">As of today</span>
					</div>
					{#if hasWeekEndOvertimeData}
						<div class="flex flex-col gap-1 md:border-l md:border-base-300 md:pl-4">
							<span class="text-xs text-base-content/40 uppercase font-semibold tracking-wider">End of Selected Week</span>
							<span class="text-xl font-bold tabular-nums {cumulativeOvertimeWeekEnd > 0 ? 'text-success' : cumulativeOvertimeWeekEnd < 0 ? 'text-error' : ''}">
								{formatDelta(cumulativeOvertimeWeekEnd)}
							</span>
							<span class="text-xs text-base-content/40">{formatWeekLabel(weekRange)}</span>
						</div>
					{/if}
				</div>
			</div>
		{/if}

		<!-- Day entries detail -->
		<section>
			<h2 class="text-lg text-base-content/70 mb-4">Entries this week</h2>

			{#if weekEntries.length === 0}
				<p class="text-base-content/50">No entries this week. Start tracking above!</p>
			{:else}
				<div class="card bg-base-100 border border-base-300 overflow-hidden">
					{#each weekEntries as entry}
						{#if editor.editingEntryId === entry.id}
							<div class="border-b border-base-300">
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
							</div>
						{:else}
							<div class="grid grid-cols-[auto_1fr_auto_auto] items-center gap-4 py-3 px-4 border-b border-base-200 last:border-b-0 {entry.isRunning ? 'bg-success/5' : ''}">
								<div class="text-left">
									<span class="block text-sm text-base-content/70">
										{formatTime(entry.startTime!)}{entry.endTime ? ` – ${formatTime(entry.endTime!)}` : ''}
									</span>
									<span class="text-xs text-base-content/40">{formatDateShort(new Date(entry.startTime!))}</span>
								</div>
								<div class="flex items-center gap-2 flex-wrap">
									{#if entry.organizationName}
										<span class="badge badge-info badge-sm">{entry.organizationName}</span>
									{/if}
									{#if entry.description}
										<span class="text-sm text-base-content/60">{entry.description}</span>
									{/if}
									{#if entry.isRunning}
										<span class="badge badge-success badge-sm uppercase font-semibold">Running</span>
									{/if}
									{#if (entry.pauseDurationMinutes ?? 0) > 0}
										{#if canEditPause(orgDetail) && !entry.isRunning}
											<button class="badge badge-warning badge-sm badge-outline cursor-pointer hover:bg-warning/20" title="Click to edit pause" onclick={() => editor.startEditEntry(entry)}>&#8722;{entry.pauseDurationMinutes}m pause &#9998;</button>
										{:else if canRequestEditPause(orgDetail) && !entry.isRunning}
											<button class="badge badge-warning badge-sm badge-outline border-dashed border-warning cursor-pointer hover:bg-warning/10" title="Request pause edit" onclick={() => editor.startRequest(entry.id!, 'pause')}>&#8722;{entry.pauseDurationMinutes}m pause &#128233;</button>
										{:else}
											<span class="badge badge-warning badge-sm badge-outline">&#8722;{entry.pauseDurationMinutes}m pause</span>
										{/if}
									{:else if canEditPause(orgDetail) && !entry.isRunning}
										<button class="badge badge-ghost badge-sm cursor-pointer hover:badge-warning hover:badge-outline" title="Click to add pause" onclick={() => editor.startEditEntry(entry)}>+pause &#9998;</button>
									{:else if canRequestEditPause(orgDetail) && !entry.isRunning}
										<button class="badge badge-ghost badge-sm cursor-pointer border-dashed border-warning hover:bg-warning/10" title="Request to add pause" onclick={() => editor.startRequest(entry.id!, 'pause')}>+pause &#128233;</button>
									{/if}
								</div>
								<div class="font-semibold text-sm text-base-content/70 min-w-[56px] text-right tabular-nums">
									{#if entry.isRunning}
										{elapsed}
									{:else if (entry.pauseDurationMinutes ?? 0) > 0}
										<span class="block">{formatDuration(entry.netDurationMinutes ?? undefined)}</span>
										<span class="block text-xs text-base-content/40 font-normal">({formatDuration(entry.durationMinutes ?? undefined)})</span>
									{:else}
										{formatDuration(entry.durationMinutes ?? undefined)}
									{/if}
								</div>
								<div class="min-w-[28px] flex gap-0.5">
									{#if !entry.isRunning}
										{#if canEditEntries(orgDetail)}
											<button class="btn btn-ghost btn-xs text-primary opacity-40 hover:opacity-100" title="Edit" onclick={() => editor.startEditEntry(entry)}>&#9998;</button>
										{:else if canRequestEditEntries(orgDetail)}
											<button class="btn btn-ghost btn-xs text-warning opacity-50 hover:opacity-100" title="Request edit" onclick={() => editor.startRequest(entry.id!, 'edit')}>&#128233;</button>
										{/if}
										<button class="btn btn-ghost btn-xs text-error opacity-40 hover:opacity-100" title="Delete" onclick={() => deleteEntry(entry.id!)}>
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

	{#if editor.requestSuccess}
		<div class="alert alert-success mt-4 text-sm">{editor.requestSuccess}</div>
	{/if}

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
</div>
