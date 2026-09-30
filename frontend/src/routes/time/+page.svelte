<script lang="ts">
	import { untrack } from 'svelte';
	import { orgContext } from '$lib/stores/orgContext.svelte';
	import { auth } from '$lib/stores/auth.svelte';
	import { timer } from '$lib/stores/timer.svelte';
	import { toast } from '$lib/stores/toast.svelte';
	import { confirmDialog } from '$lib/stores/confirm.svelte';
	import { timeTrackingApi, organizationsApi, workScheduleApi, groupApi, reportsApi } from '$lib/apiClient';
	import type { TimeEntryResponse, WorkScheduleResponse, OrganizationDetailResponse, EffectivePermissionsResponse } from '$lib/api';
	import { formatHoursDecimal, formatDelta, formatDateShort, formatWeekLabel, formatClock, dayNamesShort } from '$lib/utils/formatters';
	import { isToday, getWeekRange } from '$lib/utils/dateHelpers';
	import { getDayTarget, getDayType, getDayTypeLabel } from '$lib/utils/scheduleHelpers';
	import { WEEKLY_ENTRY_LIMIT } from '$lib/utils/constants';
	import { balanceToMinutes, type BalanceMinutes } from '$lib/utils/balance';
	import { extractErrorMessage } from '$lib/utils/errorHandler';
	import { dayTypeClasses } from '$lib/utils/colors';
	import { canEditEntries, canRequestEditEntries, canEditPause, canRequestEditPause } from '$lib/utils/orgRules';
	import { loadDaysOff, emptyDaysOff, type DaysOffData } from '$lib/utils/daysOff';
	import { createEntryEditor } from '$lib/utils/entryEditor.svelte';
	import LoadingSpinner from '$lib/components/LoadingSpinner.svelte';
	import DayTypeLegend from '$lib/components/DayTypeLegend.svelte';
	import EntryRow from '$lib/components/EntryRow.svelte';
	import RequestModal from '$lib/components/RequestModal.svelte';
	import { t } from '$lib/i18n';

	let weekEntries = $state<TimeEntryResponse[]>([]);
	let loading = $state(true);
	let weekError = $state('');

	let workSchedule = $state<WorkScheduleResponse | null>(null);
	let orgDetail = $state<OrganizationDetailResponse | null>(null);
	/** The caller's effective permissions in the selected org (group overrides applied). */
	let perms = $state<EffectivePermissionsResponse | null>(null);

	/** Balance from the backend (report rules); null without org/schedule or when it failed to load. */
	let balance = $state<BalanceMinutes | null>(null);

	let daysOff = $state<DaysOffData>(emptyDaysOff());
	let holidayDates = $derived(daysOff.holidayDates);
	let halfDayHolidays = $derived(daysOff.halfDayHolidays);
	let sickDayDates = $derived(daysOff.sickDayDates);
	let vacationDates = $derived(daysOff.vacationDates);
	let otherAbsenceDates = $derived(daysOff.otherAbsenceDates);
	let halfDayAbsences = $derived(daysOff.halfDayAbsences);

	// Optional note for the running / next timer
	let note = $state('');
	let pending = $state(false);
	const noteListId = $props.id();

	const previousDescriptions = $derived([
		...new Set(weekEntries.map((e) => e.description?.trim()).filter((d): d is string => !!d))
	]);

	const selectedSlug = $derived(orgContext.selectedOrgSlug);
	/** A timer runs, but for another organization than the selected one. */
	const runsElsewhere = $derived(
		!!timer.current && !!orgContext.selectedOrg && (timer.current.organizationId ?? null) !== orgContext.selectedOrgId
	);

	const entryPerms = $derived({
		canEdit: canEditEntries(perms, orgDetail),
		canRequestEdit: canRequestEditEntries(perms, orgDetail),
		canEditPause: canEditPause(perms, orgDetail),
		canRequestEditPause: canRequestEditPause(perms, orgDetail)
	});

	// Week navigation
	let weekOffset = $state(0);
	const weekRange = $derived(getWeekRange(weekOffset));
	const dailyTotals = $derived(computeDailyTotals(weekEntries, weekRange, workSchedule));
	const weekTotal = $derived(dailyTotals.reduce((s, d) => s + d.minutes, 0));
	const weekTargetSoFar = $derived(dailyTotals.filter((d) => d.isPastOrToday).reduce((s, d) => s + d.targetMinutes, 0));
	const weekTargetFull = $derived(workSchedule?.weeklyWorkHours ? workSchedule.weeklyWorkHours * 60 : dailyTotals.reduce((s, d) => s + d.targetMinutes, 0));
	const maxDayMinutes = $derived(Math.max(...dailyTotals.map((d) => Math.max(d.minutes, d.targetMinutes)), 480));

	// (Re)load everything once the org list is known and whenever the selected org changes
	$effect(() => {
		const slug = selectedSlug;
		if (orgContext.loading) return;
		untrack(() => loadAll(slug));
	});

	// Keep the note field in sync with the running entry
	$effect(() => {
		timer.current?.id;
		untrack(() => (note = timer.current?.description ?? ''));
	});

	async function loadAll(slug: string | null) {
		loading = true;
		await Promise.all([loadWeek(), loadOrgData(slug)]);
		await loadBalance();
		loading = false;
	}

	function computeDailyTotals(entries: TimeEntryResponse[], range: { start: Date; end: Date }, schedule: WorkScheduleResponse | null) {
		const endOfToday = new Date();
		endOfToday.setHours(23, 59, 59, 999);
		const totals = dayNamesShort().map((name, i) => {
			const date = new Date(range.start);
			date.setDate(range.start.getDate() + i);
			return { name, date, minutes: 0, targetMinutes: getDayTarget(date, schedule, holidayDates, [], halfDayHolidays), isPastOrToday: date <= endOfToday };
		});
		for (const entry of entries) {
			if (entry.isRunning || !entry.startTime) continue;
			const dayIndex = (new Date(entry.startTime).getDay() + 6) % 7; // Mon=0 … Sun=6
			// Net duration (after pause deduction) when available
			totals[dayIndex].minutes += entry.netDurationMinutes ?? entry.durationMinutes ?? 0;
		}
		return totals;
	}

	async function loadWeek() {
		weekError = '';
		try {
			const { data } = await timeTrackingApi.apiV1TimeTrackingGet(
				orgContext.selectedOrgId ?? undefined, weekRange.start.toISOString(), weekRange.end.toISOString(), WEEKLY_ENTRY_LIMIT);
			weekEntries = data.items ?? [];
		} catch (err) {
			weekEntries = [];
			weekError = extractErrorMessage(err, t('timer.weekLoadFailed'));
		}
	}

	async function loadOrgData(slug: string | null) {
		if (!slug) {
			workSchedule = null;
			orgDetail = null;
			perms = null;
			daysOff = emptyDaysOff();
			return;
		}
		const [ws, od, ep, off] = await Promise.allSettled([
			workScheduleApi.apiV1OrganizationsSlugWorkScheduleGet(slug),
			organizationsApi.apiV1OrganizationsSlugGet(slug),
			groupApi.apiV1OrganizationsSlugEffectivePermissionsGet(slug),
			loadDaysOff(slug, auth.user?.id)
		]);
		workSchedule = ws.status === 'fulfilled' ? ws.value.data : null;
		orgDetail = od.status === 'fulfilled' ? od.value.data : null;
		// Without effective permissions the org defaults apply (orgRules falls through to orgDetail)
		perms = ep.status === 'fulfilled' ? ep.value.data : null;
		daysOff = off.status === 'fulfilled' ? off.value : emptyDaysOff();
		if (ws.status === 'rejected') toast.error(ws.reason, t('timer.scheduleLoadFailed'));
		if (od.status === 'rejected') toast.error(od.reason, t('timer.orgLoadFailed'));
	}

	async function loadBalance() {
		const slug = selectedSlug;
		const userId = auth.user?.id;
		if (!slug || !userId || !workSchedule?.weeklyWorkHours) {
			balance = null;
			return;
		}
		try {
			const { data } = await reportsApi.apiV1OrganizationsSlugMembersMemberIdBalanceGet(slug, userId);
			if (slug === selectedSlug) balance = balanceToMinutes(data);
		} catch (err) {
			balance = null;
			toast.error(err, t('timer.overtimeFailed'));
		}
	}

	async function runTimerAction(action: () => Promise<unknown>, fallback: string) {
		pending = true;
		try {
			await action();
			await loadWeek();
			loadBalance();
		} catch (err) {
			toast.error(err, fallback);
		} finally {
			pending = false;
		}
	}

	function handleStart() {
		return runTimerAction(
			() => timer.start({ description: note.trim() || undefined, organizationSlug: selectedSlug ?? undefined }),
			t('timer.startFailed')
		);
	}

	function handleStop() {
		return runTimerAction(async () => {
			await timer.stop({ description: note.trim() || undefined });
			note = '';
		}, t('timer.stopFailed'));
	}

	async function handleSwitch() {
		const from = timer.current?.organizationName ?? t('timer.anotherOrg');
		const to = orgContext.selectedOrg?.name ?? '';
		const confirmed = await confirmDialog({
			title: t('timer.switchTitle'),
			message: t('timer.switchMessage', { from, to }),
			confirmLabel: t('timer.switchConfirm')
		});
		if (confirmed) await handleStart(); // the backend stops the running timer first
	}

	function changeWeek(dir: number) {
		weekOffset += dir;
		loadWeek();
	}

	// Shared entry editing + request logic
	const editor = createEntryEditor({
		getOrgDetail: () => orgDetail,
		getPermissions: () => perms,
		getOrgSlug: () => selectedSlug,
		onSaved: async () => { await Promise.all([loadWeek(), loadBalance()]); },
		onDeleted: async () => { await Promise.all([loadWeek(), loadBalance()]); },
		findEntry: (id) => weekEntries.find((e) => e.id === id)
	});

	function deltaClass(delta: number): string {
		return delta > 0 ? 'text-success' : delta < 0 ? 'text-error' : '';
	}
</script>

<svelte:head>
	<title>{t('common.pageTitle', { page: t('timer.pageTitle') })}</title>
</svelte:head>

<div class="max-w-3xl mx-auto">
	<!-- Timer card -->
	<div class="card bg-base-100 border-2 {timer.current ? 'border-success shadow-[0_0_0_4px] shadow-success/10' : 'border-base-300'} p-6 sm:p-8 mb-8 text-center transition-colors">
		<div class="text-5xl sm:text-6xl font-bold tabular-nums {timer.current ? 'text-success' : 'text-base-content'} mb-2 tracking-wider" role="timer" aria-live="off">
			{formatClock(timer.elapsedSeconds)}
		</div>

		{#if timer.current}
			<div class="text-sm text-base-content/70 mb-5">
				{t('timer.runningFor', { org: timer.current.organizationName ?? t('timer.personal') })}
			</div>
		{:else if orgContext.selectedOrg}
			<div class="text-sm text-base-content/70 mb-5">{t('timer.trackingFor', { org: orgContext.selectedOrg.name ?? '' })}</div>
		{:else}
			<div class="text-sm text-base-content/50 mb-5">{t('timer.personal')}</div>
		{/if}

		{#if runsElsewhere}
			<div role="status" class="alert alert-warning text-sm mb-4 text-left max-sm:alert-vertical">
				<span>{t('timer.runsElsewhere', { running: timer.current?.organizationName ?? t('timer.anotherOrg'), selected: orgContext.selectedOrg?.name ?? '' })}</span>
				<button type="button" class="btn btn-sm" onclick={handleSwitch} disabled={pending}>{t('timer.switchTo', { org: orgContext.selectedOrg?.name ?? '' })}</button>
			</div>
		{/if}

		<div class="mb-4">
			{#if timer.current}
				<button type="button" class="btn btn-error btn-lg min-w-40 text-lg font-bold" onclick={handleStop} disabled={pending}>
					{pending ? t('timer.stopping') : t('timer.stop')}
				</button>
			{:else}
				<button type="button" class="btn btn-success btn-lg min-w-40 text-lg font-bold" onclick={handleStart} disabled={pending || !timer.loaded}>
					{pending ? t('timer.starting') : t('timer.start')}
				</button>
			{/if}
		</div>

		<div class="w-full max-w-[360px] mx-auto">
			<label for="{noteListId}-note" class="sr-only">{t('timer.note')}</label>
			<input
				id="{noteListId}-note"
				type="text"
				bind:value={note}
				placeholder={t('timer.note')}
				list={previousDescriptions.length > 0 ? `${noteListId}-suggestions` : undefined}
				autocomplete="off"
				class="input input-bordered input-sm w-full text-center bg-base-200/50"
				disabled={pending}
			/>
			<datalist id="{noteListId}-suggestions">
				{#each previousDescriptions as desc (desc)}
					<option value={desc}></option>
				{/each}
			</datalist>
		</div>
	</div>

	{#if loading}
		<LoadingSpinner size="sm" />
	{:else}
		<!-- Weekly summary -->
		<section class="card bg-base-100 border border-base-300 p-4 sm:p-5 mb-8" aria-label={t('timer.weekSummary')}>
			<div class="flex items-center justify-between mb-4">
				<button type="button" class="btn btn-ghost btn-sm btn-square text-xl" aria-label={t('timer.prevWeek')} title={t('timer.prevWeek')} onclick={() => changeWeek(-1)}>‹</button>
				<div class="text-center">
					<span class="block font-semibold text-base text-base-content">{formatWeekLabel(weekRange)}</span>
					<span class="block text-sm text-base-content/60 mt-0.5">
						{formatHoursDecimal(weekTotal)}{#if weekTargetFull > 0} / {formatHoursDecimal(weekTargetFull)}{/if}
						{#if weekTargetSoFar > 0}
							{@const weekDelta = weekTotal - weekTargetSoFar}
							<span class={deltaClass(weekDelta)}>({formatDelta(weekDelta)})</span>
						{/if}
					</span>
					{#if weekTargetFull > 0}
						<progress class="progress progress-primary h-1 w-36 mt-1.5" value={Math.min(weekTotal, weekTargetFull)} max={weekTargetFull}></progress>
					{/if}
				</div>
				<button type="button" class="btn btn-ghost btn-sm btn-square text-xl" aria-label={t('timer.nextWeek')} title={t('timer.nextWeek')} onclick={() => changeWeek(1)} disabled={weekOffset >= 0}>›</button>
			</div>

			{#if weekError}
				<div role="alert" class="alert alert-error text-sm mb-3 max-sm:alert-vertical">
					<span>{weekError}</span>
					<button type="button" class="btn btn-sm" onclick={loadWeek}>{t('common.retry')}</button>
				</div>
			{/if}

			<!-- Day bars -->
			<div class="flex flex-col gap-1.5">
				{#each dailyTotals as day (day.date.getTime())}
					{@const pct = Math.min((day.minutes / maxDayMinutes) * 100, 100)}
					{@const targetPct = day.targetMinutes > 0 ? Math.min((day.targetMinutes / maxDayMinutes) * 100, 100) : 0}
					{@const delta = day.isPastOrToday && day.targetMinutes > 0 ? day.minutes - day.targetMinutes : 0}
					{@const dayType = getDayType(day.date, holidayDates, sickDayDates, vacationDates, otherAbsenceDates, halfDayHolidays)}
					{@const dt = dayTypeClasses(dayType)}
					<div class="grid grid-cols-[2rem_1fr_auto] sm:grid-cols-[2.25rem_4.5rem_1fr_auto] items-center gap-2 py-1.5 px-2 rounded-md {dt.soft} {isToday(day.date) ? 'font-semibold' : ''} {!day.isPastOrToday ? 'opacity-50' : ''}">
						<span class="text-sm text-base-content/70">{day.name}</span>
						<span class="text-xs text-base-content/40 max-sm:hidden">
							{formatDateShort(day.date)}
							{#if dayType}<span class="w-2 h-2 rounded-full inline-block align-middle ml-1 {dt.dot}" title={getDayTypeLabel(day.date, holidayDates, sickDayDates, vacationDates, otherAbsenceDates, halfDayHolidays, halfDayAbsences)}></span>{/if}
						</span>
						<div class="h-5 bg-base-200 rounded overflow-hidden relative">
							{#if targetPct > 0}
								<div class="absolute top-0 bottom-0 w-0.5 bg-base-content/30 z-[1]" style="left: {targetPct}%"></div>
							{/if}
							<div class="h-full {isToday(day.date) ? 'bg-success' : 'bg-primary'} rounded transition-all duration-300" style="width: {pct}%"></div>
						</div>
						<span class="text-right text-sm text-base-content/70 tabular-nums whitespace-nowrap">
							{formatHoursDecimal(day.minutes)}{#if day.targetMinutes > 0}<span class="text-base-content/40 font-normal text-xs"> / {formatHoursDecimal(day.targetMinutes)}</span>{/if}
							{#if delta !== 0}
								<span class="block sm:inline text-xs {deltaClass(delta)}">{formatDelta(delta)}</span>
							{/if}
						</span>
					</div>
				{/each}
			</div>

			<DayTypeLegend {holidayDates} {sickDayDates} {vacationDates} {otherAbsenceDates} />
		</section>

		<!-- Cumulative overtime -->
		{#if balance}
			<div class="card bg-base-100 border-2 p-4 mb-8 {balance.cumulative > 0 ? 'border-success/50 bg-success/5' : balance.cumulative < 0 ? 'border-error/50 bg-error/5' : 'border-base-300'}">
				<div class="grid grid-cols-1 sm:grid-cols-2 gap-4">
					<div class="flex flex-col gap-1">
						<span class="text-xs text-base-content/50 uppercase font-semibold tracking-wider">{t('timer.cumulativeBalance')}</span>
						<span class="text-xl font-bold tabular-nums {deltaClass(balance.cumulative)}">{formatDelta(balance.cumulative)}</span>
						<span class="text-xs text-base-content/50">{t('timer.asOfToday')}</span>
					</div>
					<div class="flex flex-col gap-1 sm:border-l sm:border-base-300 sm:pl-4">
						<span class="text-xs text-base-content/50 uppercase font-semibold tracking-wider">{t('timer.thisWeek')}</span>
						<span class="text-xl font-bold tabular-nums {deltaClass(balance.week.delta)}">{formatDelta(balance.week.delta)}</span>
						<span class="text-xs text-base-content/50">{t('timer.weekOfTarget', { worked: formatHoursDecimal(balance.week.worked), target: formatHoursDecimal(balance.week.target) })}</span>
					</div>
				</div>
			</div>
		{/if}

		<!-- Entries of the selected week -->
		<section>
			<h2 class="text-lg text-base-content/70 mb-4">{t('timer.entriesThisWeek')}</h2>

			{#if weekEntries.length === 0}
				{#if !weekError}<p class="text-base-content/50">{t('timer.noEntries')}</p>{/if}
			{:else}
				<ul class="card bg-base-100 border border-base-300 overflow-hidden">
					{#each weekEntries as entry (entry.id)}
						<li class="border-b border-base-200 last:border-b-0">
							<EntryRow {entry} {editor} {...entryPerms} showDate showOrg />
						</li>
					{/each}
				</ul>
			{/if}
		</section>
	{/if}

	<RequestModal
		open={!!editor.requestingEntryId}
		type={editor.requestType}
		bind:newStart={editor.requestNewStart}
		bind:newEnd={editor.requestNewEnd}
		bind:newPause={editor.requestNewPause}
		bind:message={editor.requestMessage}
		sending={editor.requestSending}
		error={editor.requestError}
		onsubmit={editor.submitRequest}
		oncancel={editor.cancelRequest}
	/>
</div>
