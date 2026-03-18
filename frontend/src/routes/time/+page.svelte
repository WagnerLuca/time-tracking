<script lang="ts">
	import { onMount, onDestroy } from 'svelte';
	import { orgContext } from '$lib/stores/orgContext.svelte';
	import { auth } from '$lib/stores/auth.svelte';
	import { timeTrackingApi, organizationsApi, workScheduleApi, requestsApi, holidayApi, absenceDayApi } from '$lib/apiClient';
	import type { TimeEntryResponse, StartTimeEntryRequest, UpdateTimeEntryRequest, WorkScheduleResponse, OrganizationDetailResponse } from '$lib/api';
	import { RequestType } from '$lib/api';
	import { formatHoursDecimal, formatDelta, formatDuration, formatTime, formatDateShort, formatWeekLabel, formatHours } from '$lib/utils/formatters';
	import { dateKey, isToday, getWeekRange, toLocalDateTimeInput } from '$lib/utils/dateHelpers';
	import { getDayTarget, getAbsenceCredit, getDayType, getDayTypeLabel } from '$lib/utils/scheduleHelpers';
	import { DAY_NAMES, MAX_ENTRIES_FOR_OVERTIME } from '$lib/utils/constants';
	import { extractErrorMessage } from '$lib/utils/errorHandler';

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
	let daysOffSet = $state<Set<string>>(new Set());
	// Day-type tracking for color-coded display
	let holidayDates = $state<Map<string, string>>(new Map()); // date -> name
	let sickDayDates = $state<Set<string>>(new Set());
	let vacationDates = $state<Set<string>>(new Set());
	let otherAbsenceDates = $state<Set<string>>(new Set());

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
			return { name, date: new Date(date), minutes: 0, targetMinutes: getDayTarget(date, schedule, holidayDates), entryCount: 0, isPastOrToday };
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
			targetMinutes += getDayTarget(cursor, workSchedule, holidayDates);
			absenceCredits += getAbsenceCredit(cursor, workSchedule, holidayDates, sickDayDates, vacationDates, otherAbsenceDates);
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
		// Load holidays + absences for days-off calculation
		if (orgContext.selectedOrgSlug) {
			try {
				const [holRes, absRes] = await Promise.all([
					holidayApi.apiV1OrganizationsSlugHolidaysGet(orgContext.selectedOrgSlug),
					absenceDayApi.apiV1OrganizationsSlugAbsencesGet(orgContext.selectedOrgSlug, auth.user?.id)
				]);
				const offDates = new Set<string>();
				const hDates = new Map<string, string>();
				const sDates = new Set<string>();
				const vDates = new Set<string>();
				const oDates = new Set<string>();
				for (const h of holRes.data) {
					if (h.date) {
						offDates.add(h.date);
						hDates.set(h.date, h.name ?? 'Holiday');
					}
				}
				const absences = absRes.data.items ?? [];
				for (const a of absences) {
					if (a.date) {
						offDates.add(a.date);
						if (a.type === 'SickDay') sDates.add(a.date);
						else if (a.type === 'Vacation') vDates.add(a.date);
						else oDates.add(a.date);
					}
				}
				daysOffSet = offDates;
				holidayDates = hDates;
				sickDayDates = sDates;
				vacationDates = vDates;
				otherAbsenceDates = oDates;
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
			actionError = extractErrorMessage(err, 'Failed to delete entry.');
		}
	}

	function changeWeek(dir: number) {
		weekOffset += dir;
		loadWeek();
		loadCumulativeOvertime();
	}

	// Edit entry
	let editingEntryId = $state<number | null>(null);
	let editStartTime = $state('');
	let editEndTime = $state('');
	let editDescription = $state('');
	let editPause = $state<number>(0);
	let editError = $state('');
	let editSaving = $state(false);

	function startEditEntry(entry: TimeEntryResponse) {
		editingEntryId = entry.id ?? null;
		editStartTime = toLocalDateTimeInput(entry.startTime!);
		editEndTime = entry.endTime ? toLocalDateTimeInput(entry.endTime) : '';
		editDescription = entry.description ?? '';
		editPause = entry.pauseDurationMinutes ?? 0;
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
			if (orgDetail?.editPauseMode === 'Allowed') {
				payload.pauseDurationMinutes = Math.max(0, Number(editPause) || 0);
			}
			await timeTrackingApi.apiV1TimeTrackingIdPut(entryId, payload);
			await loadWeek();
			editingEntryId = null;
		} catch (err) {
			editError = extractErrorMessage(err, 'Failed to update entry.');
		} finally {
			editSaving = false;
		}
	}

	// Request functionality for RequiresApproval modes
	let requestingEntryId = $state<number | null>(null);
	let requestType = $state<'edit' | 'pause' | null>(null);
	let requestMessage = $state('');
	let requestSending = $state(false);
	let requestSuccess = $state('');
	// Values for the request
	let requestNewStart = $state('');
	let requestNewEnd = $state('');
	let requestNewPause = $state(0);

	function startRequest(entryId: number, type: 'edit' | 'pause') {
		requestingEntryId = entryId;
		requestType = type;
		requestMessage = '';
		requestSuccess = '';
		// Pre-fill with current values
		const entry = weekEntries.find((e: TimeEntryResponse) => e.id === entryId);
		if (entry) {
			if (type === 'edit') {
				requestNewStart = entry.startTime ? toLocalDateTimeInput(entry.startTime) : '';
				requestNewEnd = entry.endTime ? toLocalDateTimeInput(entry.endTime) : '';
			} else {
				requestNewPause = entry.pauseDurationMinutes ?? 0;
			}
		}
	}

	function cancelRequest() {
		requestingEntryId = null;
		requestType = null;
		requestMessage = '';
	}

	async function submitRequest() {
		if (!requestingEntryId || !requestType || !orgContext.selectedOrgSlug) return;
		requestSending = true;
		try {
			const rType = requestType === 'edit' ? 1 as RequestType : 2 as RequestType;
			let requestData: string | undefined;
			if (requestType === 'edit') {
				const data: Record<string, string> = {};
				if (requestNewStart) data.startTime = new Date(requestNewStart).toISOString();
				if (requestNewEnd) data.endTime = new Date(requestNewEnd).toISOString();
				requestData = JSON.stringify(data);
			} else {
				requestData = String(Math.max(0, requestNewPause));
			}
			await requestsApi.apiV1OrganizationsSlugRequestsPost(orgContext.selectedOrgSlug, {
				type: rType,
				relatedEntityId: requestingEntryId,
				requestData,
				message: requestMessage || undefined
			});
			requestSuccess = 'Request submitted! An admin will review it.';
			requestingEntryId = null;
			requestType = null;
			requestMessage = '';
			setTimeout(() => (requestSuccess = ''), 4000);
		} catch (err) {
			actionError = extractErrorMessage(err, 'Failed to submit request.');
		} finally {
			requestSending = false;
		}
	}
</script>

<svelte:head>
	<title>Timer - Time Tracking</title>
</svelte:head>

<div class="max-w-3xl mx-auto p-6">
	{#if loading}
		<p class="text-base-content/50">Loading...</p>
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
						<span class="text-xs text-base-content/40">{formatDateShort(day.date)}{#if dayType} <span class="w-2 h-2 rounded-full shrink-0 inline-block align-middle ml-1 {dayType === 'holiday' ? 'bg-secondary' : dayType === 'sick' ? 'bg-error' : dayType === 'vacation' ? 'bg-accent' : 'bg-base-content/40'}" title={getDayTypeLabel(day.date, holidayDates, sickDayDates, vacationDates, otherAbsenceDates)}></span>{/if}</span>
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

			<!-- Day Type Legend -->
			{#if holidayDates.size > 0 || sickDayDates.size > 0 || vacationDates.size > 0 || otherAbsenceDates.size > 0}
				<div class="flex gap-4 flex-wrap text-xs text-base-content/60 mt-3">
					{#if holidayDates.size > 0}<span class="flex items-center gap-1.5"><span class="w-2 h-2 rounded-full shrink-0 inline-block bg-secondary"></span> Holiday</span>{/if}
					{#if sickDayDates.size > 0}<span class="flex items-center gap-1.5"><span class="w-2 h-2 rounded-full shrink-0 inline-block bg-error"></span> Sick Day</span>{/if}
					{#if vacationDates.size > 0}<span class="flex items-center gap-1.5"><span class="w-2 h-2 rounded-full shrink-0 inline-block bg-accent"></span> Vacation</span>{/if}
					{#if otherAbsenceDates.size > 0}<span class="flex items-center gap-1.5"><span class="w-2 h-2 rounded-full shrink-0 inline-block bg-base-content/40"></span> Other Absence</span>{/if}
				</div>
			{/if}
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
						{#if editingEntryId === entry.id}
							<!-- Inline edit form -->
							<div class="p-4 bg-base-200/50 border-b border-base-300 overflow-hidden">
								{#if editError}
									<div class="alert alert-error text-sm mb-3 py-2 px-3">{editError}</div>
								{/if}
								<div class="flex gap-3 flex-wrap mb-3 max-w-full overflow-hidden">
									<div class="flex flex-col gap-1">
										<!-- svelte-ignore a11y_label_has_associated_control -->
										<label class="text-xs font-semibold text-base-content/60 uppercase tracking-wide">Start</label>
										<input class="input input-bordered input-sm" type="datetime-local" bind:value={editStartTime} disabled={editSaving} />
									</div>
									<div class="flex flex-col gap-1">
										<!-- svelte-ignore a11y_label_has_associated_control -->
										<label class="text-xs font-semibold text-base-content/60 uppercase tracking-wide">End</label>
										<input class="input input-bordered input-sm" type="datetime-local" bind:value={editEndTime} disabled={editSaving} />
									</div>
									<div class="flex flex-col gap-1 flex-1 min-w-[150px]">
										<!-- svelte-ignore a11y_label_has_associated_control -->
										<label class="text-xs font-semibold text-base-content/60 uppercase tracking-wide">Note</label>
										<input class="input input-bordered input-sm w-full" type="text" bind:value={editDescription} placeholder="Optional note" disabled={editSaving} />
									</div>
									{#if orgDetail?.editPauseMode === 'Allowed'}
										<div class="flex flex-col gap-1">
											<!-- svelte-ignore a11y_label_has_associated_control -->
											<label class="text-xs font-semibold text-base-content/60 uppercase tracking-wide">Pause (min)</label>
											<input class="input input-bordered input-sm w-20" type="number" min="0" bind:value={editPause} disabled={editSaving} />
										</div>
									{/if}
								</div>
								<div class="flex gap-2">
									<button class="btn btn-primary btn-sm" onclick={() => saveEditEntry(entry.id!)} disabled={editSaving}>
										{editSaving ? 'Saving...' : 'Save'}
									</button>
									<button class="btn btn-ghost btn-sm" onclick={cancelEditEntry}>Cancel</button>
								</div>
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
										{#if orgDetail?.editPauseMode === 'Allowed' && !entry.isRunning}
											<button class="badge badge-warning badge-sm badge-outline cursor-pointer hover:bg-warning/20" title="Click to edit pause" onclick={() => startEditEntry(entry)}>&#8722;{entry.pauseDurationMinutes}m pause &#9998;</button>
										{:else if orgDetail?.editPauseMode === 'RequiresApproval' && !entry.isRunning}
											<button class="badge badge-warning badge-sm badge-outline border-dashed border-warning cursor-pointer hover:bg-warning/10" title="Request pause edit" onclick={() => startRequest(entry.id!, 'pause')}>&#8722;{entry.pauseDurationMinutes}m pause &#128233;</button>
										{:else}
											<span class="badge badge-warning badge-sm badge-outline">&#8722;{entry.pauseDurationMinutes}m pause</span>
										{/if}
									{:else if orgDetail?.editPauseMode === 'Allowed' && !entry.isRunning}
										<button class="badge badge-ghost badge-sm cursor-pointer hover:badge-warning hover:badge-outline" title="Click to add pause" onclick={() => startEditEntry(entry)}>+pause &#9998;</button>
									{:else if orgDetail?.editPauseMode === 'RequiresApproval' && !entry.isRunning}
										<button class="badge badge-ghost badge-sm cursor-pointer border-dashed border-warning hover:bg-warning/10" title="Request to add pause" onclick={() => startRequest(entry.id!, 'pause')}>+pause &#128233;</button>
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
										{#if orgDetail?.editPastEntriesMode === 'Allowed'}
											<button class="btn btn-ghost btn-xs text-primary opacity-40 hover:opacity-100" title="Edit" onclick={() => startEditEntry(entry)}>&#9998;</button>
										{:else if orgDetail?.editPastEntriesMode === 'RequiresApproval'}
											<button class="btn btn-ghost btn-xs text-warning opacity-50 hover:opacity-100" title="Request edit" onclick={() => startRequest(entry.id!, 'edit')}>&#128233;</button>
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

	{#if requestSuccess}
		<div class="alert alert-success mt-4 text-sm">{requestSuccess}</div>
	{/if}

	{#if requestingEntryId}
		<!-- svelte-ignore a11y_no_static_element_interactions -->
		<!-- svelte-ignore a11y_click_events_have_key_events -->
		<div class="fixed inset-0 bg-black/30 z-[100]" onclick={cancelRequest}></div>
		<div class="fixed top-1/2 left-1/2 -translate-x-1/2 -translate-y-1/2 bg-base-100 rounded-xl p-6 w-[90%] max-w-[420px] shadow-2xl z-[101]">
			<h3 class="text-lg font-bold mb-1">{requestType === 'edit' ? 'Request Entry Edit' : 'Request Pause Edit'}</h3>
			<p class="text-sm text-base-content/60 mb-4">Specify the new values. An admin will review and apply them.</p>

			{#if requestType === 'edit'}
				<div class="flex flex-col gap-3 mb-3">
					<div>
						<!-- svelte-ignore a11y_label_has_associated_control -->
						<label class="text-xs font-semibold text-base-content/70 mb-1">New Start Time</label>
						<input class="input input-bordered input-sm w-full" type="datetime-local" bind:value={requestNewStart} disabled={requestSending} />
					</div>
					<div>
						<!-- svelte-ignore a11y_label_has_associated_control -->
						<label class="text-xs font-semibold text-base-content/70 mb-1">New End Time</label>
						<input class="input input-bordered input-sm w-full" type="datetime-local" bind:value={requestNewEnd} disabled={requestSending} />
					</div>
				</div>
			{:else}
				<div class="flex flex-col gap-3 mb-3">
					<div>
						<!-- svelte-ignore a11y_label_has_associated_control -->
						<label class="text-xs font-semibold text-base-content/70 mb-1">New Pause Duration (minutes)</label>
						<input class="input input-bordered input-sm w-full" type="number" min="0" bind:value={requestNewPause} disabled={requestSending} />
					</div>
				</div>
			{/if}

			<textarea
				class="textarea textarea-bordered w-full text-sm mb-4"
				bind:value={requestMessage}
				placeholder="Optional message for the admin..."
				rows="2"
				disabled={requestSending}
			></textarea>
			<div class="flex gap-2">
				<button class="btn btn-primary btn-sm" onclick={submitRequest} disabled={requestSending}>
					{requestSending ? 'Sending...' : 'Submit Request'}
				</button>
				<button class="btn btn-ghost btn-sm" onclick={cancelRequest}>Cancel</button>
			</div>
		</div>
	{/if}
</div>
