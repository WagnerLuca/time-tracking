<script lang="ts">
	import { untrack } from 'svelte';
	import { holidayApi, requestsApi, workScheduleApi } from '$lib/apiClient';
	import { RequestType, type WorkSchedulePlanResponse, type WorkScheduleResponse } from '$lib/api';
	import { dateKey } from '$lib/utils/dateHelpers';
	import { dayNamesShort, formatDateFull, formatNumber, holidayRegionLabel } from '$lib/utils/formatters';
	import { extractErrorMessage, getErrorStatus } from '$lib/utils/errorHandler';
	import { formErrors, formFrom, MAX_DAILY, MAX_WEEKLY, targetsPayload, weekTargets, weekTotal, type ScheduleForm } from '$lib/utils/schedulePlan';
	import { confirmDialog } from '$lib/stores/confirm.svelte';
	import { toast } from '$lib/stores/toast.svelte';
	import { t } from '$lib/i18n';

	/**
	 * A member's work schedule (issue #82): the current one (Mo–So, weekly total), an "Ab <Datum>" card for the future one
	 * (cancel), "Ändern ab …" (a new schedule from a day; replaces a future one after asking) and the collapsed, read-only
	 * history. Admins also "Aktuelles korrigieren" (not when the current one reaches into a closed month).
	 *
	 * `memberId` set → the admin/member endpoints (changing needs `isAdmin`); unset → the caller's own, where `mode`
	 * (workScheduleChangeMode) decides: Allowed changes directly, RequiresApproval sends a request, Disabled is read-only.
	 */
	interface Props {
		orgSlug: string;
		memberId?: number | null;
		/** The caller is an org admin (member page): change from any open day, correct, cancel. */
		isAdmin?: boolean;
		mode?: string | null;
		onchanged?: () => void;
		class?: string;
	}
	let { orgSlug, memberId = null, isAdmin = false, mode = null, onchanged, class: className = '' }: Props = $props();

	let plan = $state<WorkSchedulePlanResponse | null>(null);
	let loading = $state(true);
	let loadError = $state('');
	/** 'change' | 'correct' while the form is open. */
	let formMode = $state<'change' | 'correct' | null>(null);
	let form = $state<ScheduleForm>(formFrom(null, ''));
	let submitted = $state(false);
	let saving = $state(false);
	let saveError = $state('');

	const today = dateKey(new Date());
	const idSuffix = $derived(memberId ?? 'self');
	const adminView = $derived(!!memberId && isAdmin);
	const canChange = $derived(adminView || (!memberId && (mode === 'Allowed' || mode === 'RequiresApproval')));
	const viaRequest = $derived(!memberId && mode === 'RequiresApproval');
	const canCancel = $derived(adminView || (!memberId && mode === 'Allowed'));
	const errors = $derived(submitted ? formErrors(form, formMode === 'change' && !adminView ? today : null) : {});

	// Contract terms only admins set: the Arbeitsort's state (public holidays) and vacation days per year. A new schedule
	// keeps the current one's unless changed here; a member's own change never touches them.
	let region = $state('');
	let vacationDays = $state<number | null>(null);
	let presets = $state<{ key?: string | null; name?: string | null }[]>([]);
	const vacationDaysError = $derived(submitted && vacationDays != null && (!Number.isFinite(vacationDays) || vacationDays < 0 || vacationDays > 365));
	$effect(() => {
		if (adminView && formMode && presets.length === 0)
			holidayApi.apiV1OrganizationsHolidayPresetsGet().then((r) => (presets = r.data)).catch(() => {});
	});

	$effect(() => {
		if (!orgSlug) return;
		void memberId;
		untrack(() => load());
	});

	async function load() {
		loading = true;
		loadError = '';
		try {
			const { data } = memberId
				? await workScheduleApi.apiV1OrganizationsSlugMembersMemberIdWorkSchedulePlanGet(orgSlug, memberId)
				: await workScheduleApi.apiV1OrganizationsSlugWorkSchedulePlanGet(orgSlug);
			plan = data;
		} catch (err) {
			if (getErrorStatus(err) !== 404) loadError = extractErrorMessage(err, t('schedule.plan.loadFailed'));
		} finally {
			loading = false;
		}
	}

	function open(kind: 'change' | 'correct') {
		formMode = kind;
		form = formFrom(plan?.current ?? null, kind === 'correct' ? (plan?.current?.validFrom ?? today) : today);
		region = plan?.current?.holidayRegion ?? '';
		vacationDays = plan?.current?.vacationDaysPerYear ?? null;
		submitted = false;
		saveError = '';
	}

	function close() {
		formMode = null;
	}

	async function save(e: Event) {
		e.preventDefault();
		submitted = true;
		if (Object.keys(errors).length > 0 || vacationDaysError) return;
		const replaces = formMode === 'change' && !!plan?.future;
		if (replaces && !viaRequest) {
			const ok = await confirmDialog({
				title: t('schedule.plan.replaceTitle'),
				message: t('schedule.plan.replaceMessage', { date: formatDateFull(plan!.future!.validFrom!) }),
				confirmLabel: t('schedule.plan.replaceConfirm')
			});
			if (!ok) return;
		}
		saving = true;
		saveError = '';
		const targets = { ...targetsPayload(form), ...(adminView ? { holidayRegion: region, vacationDaysPerYear: vacationDays ?? undefined } : {}) };
		try {
			if (viaRequest) {
				await requestsApi.apiV1OrganizationsSlugRequestsPost(orgSlug, {
					type: RequestType.NUMBER_4, // WorkScheduleChange
					requestData: JSON.stringify({ validFrom: form.from, ...targets })
				});
				toast.success(t('schedule.plan.requested'));
			} else if (formMode === 'correct') {
				plan = (await workScheduleApi.apiV1OrganizationsSlugMembersMemberIdWorkScheduleCurrentPut(orgSlug, memberId!, { validFrom: form.from, ...targets })).data;
				toast.success(t('schedule.plan.corrected'));
			} else {
				const body = { validFrom: form.from, replaceFuture: replaces, ...targets };
				plan = (memberId
					? await workScheduleApi.apiV1OrganizationsSlugMembersMemberIdWorkScheduleChangePost(orgSlug, memberId, body)
					: await workScheduleApi.apiV1OrganizationsSlugWorkScheduleChangePost(orgSlug, body)).data;
				toast.success(form.from > today ? t('schedule.plan.planned', { date: formatDateFull(form.from) }) : t('schedule.plan.changed'));
			}
			formMode = null;
			onchanged?.();
		} catch (err) {
			saveError = extractErrorMessage(err, t('schedule.plan.saveFailed'));
		} finally {
			saving = false;
		}
	}

	async function cancelFuture() {
		if (!plan?.future) return;
		const ok = await confirmDialog({
			title: t('schedule.plan.cancelTitle'),
			message: t('schedule.plan.cancelMessage', { date: formatDateFull(plan.future.validFrom!) }),
			confirmLabel: t('schedule.plan.cancelConfirm'),
			danger: true
		});
		if (!ok) return;
		try {
			plan = (memberId
				? await workScheduleApi.apiV1OrganizationsSlugMembersMemberIdWorkScheduleFutureDelete(orgSlug, memberId)
				: await workScheduleApi.apiV1OrganizationsSlugWorkScheduleFutureDelete(orgSlug)).data;
			toast.success(t('schedule.plan.cancelled'));
			onchanged?.();
		} catch (err) {
			toast.error(err, t('schedule.plan.saveFailed'));
		}
	}

	function hours(v: number | null | undefined): string {
		return t('common.hoursShort', { value: formatNumber(v ?? 0) });
	}
</script>

{#snippet week(s: WorkScheduleResponse, testid: string)}
	<div class="grid grid-cols-4 sm:grid-cols-8 gap-2" data-testid={testid}>
		{#each dayNamesShort() as day, i}
			<div class="flex flex-col items-center bg-base-200/40 p-2 rounded-lg border border-base-300">
				<span class="text-xs text-base-content/70 font-medium">{day}</span>
				<span class="text-base font-bold text-base-content tabular-nums">{hours(weekTargets(s)[i])}</span>
			</div>
		{/each}
		<div class="flex flex-col items-center bg-primary/10 p-2 rounded-lg border border-primary/30">
			<span class="text-xs text-base-content/70 font-medium">{t('schedule.plan.week')}</span>
			<span class="text-base font-bold text-base-content tabular-nums">{hours(weekTotal(s))}</span>
		</div>
	</div>
{/snippet}

<section class="card bg-base-100 border border-base-300 p-5 {className}" data-testid="schedule-plan">
	<div class="flex items-center justify-between gap-2 mb-3 flex-wrap">
		<h2 class="text-base font-bold text-base-content m-0">{t('schedule.plan.title')}</h2>
		{#if !loading && !loadError && !formMode}
			<div class="flex gap-2 flex-wrap">
				{#if adminView && plan?.current}
					<button type="button" class="btn btn-sm btn-ghost" onclick={() => open('correct')} disabled={plan.currentInClosedMonth}
						title={plan.currentInClosedMonth ? t('schedule.plan.correctClosed') : undefined}>{t('schedule.plan.correct')}</button>
				{/if}
				{#if canChange}
					<button type="button" class="btn btn-sm btn-primary" onclick={() => open('change')}>{t('schedule.plan.change')}</button>
				{/if}
			</div>
		{/if}
	</div>

	{#if !memberId && mode === 'RequiresApproval'}
		<p class="text-sm text-base-content/70 mb-3">{t('schedule.plan.approvalNote')}</p>
	{:else if !memberId && mode === 'Disabled'}
		<p class="text-sm text-base-content/70 mb-3">{t('schedule.plan.disabledNote')}</p>
	{/if}

	{#if loading}
		<p class="text-sm text-base-content/70 flex items-center gap-2"><span class="loading loading-spinner loading-xs"></span> {t('common.loading')}</p>
	{:else if loadError}
		<div class="alert alert-error text-sm py-2">
			<span>{loadError}</span>
			<button type="button" class="btn btn-ghost btn-xs" onclick={load}>{t('common.retry')}</button>
		</div>
	{:else}
		{#if formMode}
			<form onsubmit={save} novalidate class="flex flex-col gap-3 p-3 mb-4 rounded-lg border border-base-300 bg-base-200/40" data-testid="schedule-form">
				<h3 class="text-sm font-semibold m-0">{formMode === 'correct' ? t('schedule.plan.correctTitle') : t('schedule.plan.changeTitle')}</h3>
				{#if formMode === 'correct'}
					<p class="text-xs text-base-content/70 m-0">{t('schedule.plan.correctHint')}</p>
				{:else if plan?.future}
					<p class="text-xs text-warning m-0">{t('schedule.plan.replaceHint', { date: formatDateFull(plan.future.validFrom!) })}</p>
				{/if}
				{#if saveError}
					<div class="alert alert-error text-sm py-2">{saveError}</div>
				{/if}
				<div>
					<label for="plan-from-{idSuffix}" class="block text-sm font-medium text-base-content/80 mb-1">{formMode === 'correct' ? t('schedule.plan.since') : t('schedule.plan.from')}</label>
					<input id="plan-from-{idSuffix}" type="date" class="input input-sm w-full sm:w-48" class:input-error={errors.from} aria-invalid={!!errors.from}
						min={formMode === 'change' && !adminView ? today : undefined} max={formMode === 'correct' ? today : undefined} bind:value={form.from} disabled={saving} />
					{#if errors.from}<p class="text-error text-xs mt-1">{t(errors.from as 'schedule.plan.fromRequired')}</p>{/if}
				</div>
				{#if form.distribute}
				<div>
					<label for="plan-weekly-{idSuffix}" class="block text-sm font-medium text-base-content/80 mb-1">{t('schedule.plan.weeklyHours')}</label>
					<input id="plan-weekly-{idSuffix}" type="number" step="0.5" min="0" max={MAX_WEEKLY} class="input input-sm w-32" class:input-error={errors.weekly} aria-invalid={!!errors.weekly} bind:value={form.weekly} disabled={saving} />
					{#if errors.weekly}<p class="text-error text-xs mt-1">{t(errors.weekly as 'schedule.plan.weeklyRange', { max: MAX_WEEKLY })}</p>{/if}
				</div>
				{/if}
				<label class="flex items-center gap-2 text-sm text-base-content/80 cursor-pointer w-fit">
					<input type="checkbox" class="checkbox checkbox-primary checkbox-sm" bind:checked={form.distribute} disabled={saving} />
					{t('schedule.plan.distribute')}
				</label>
				{#if !form.distribute}
					<fieldset>
						<legend class="text-sm font-medium text-base-content/80 mb-1">{t('schedule.plan.dailyTargets')}</legend>
						<div class="grid grid-cols-4 sm:grid-cols-7 gap-2 max-w-md">
							{#each dayNamesShort() as dayName, i}
								<div class="flex flex-col items-center gap-1">
									<label for="plan-day{i}-{idSuffix}" class="text-xs font-semibold text-base-content/70">{dayName}</label>
									<input id="plan-day{i}-{idSuffix}" type="number" step="0.5" min="0" max={MAX_DAILY} class="input input-xs w-full text-center" bind:value={form.days[i]} disabled={saving} />
								</div>
							{/each}
						</div>
						{#if errors.days}<p class="text-error text-xs mt-1">{t('schedule.plan.dailyRange', { max: MAX_DAILY })}</p>{/if}
					</fieldset>
				{/if}
				{#if adminView}
						<div class="grid grid-cols-1 sm:grid-cols-2 gap-3">
							<div>
								<label for="plan-region-{idSuffix}" class="block text-sm font-medium text-base-content/80 mb-1">{t('schedule.plan.region')}</label>
								<select id="plan-region-{idSuffix}" class="select select-sm w-full" bind:value={region} disabled={saving} aria-describedby="plan-region-hint-{idSuffix}">
									<option value="">{t('schedule.plan.regionDefault')}</option>
									{#each presets as p (p.key)}<option value={p.key}>{holidayRegionLabel(p.key ?? '', p.name ?? '')}</option>{/each}
								</select>
								<p id="plan-region-hint-{idSuffix}" class="text-xs text-base-content/70 mt-1 m-0">{t('schedule.plan.regionHint')}</p>
							</div>
							<div>
								<label for="plan-vac-{idSuffix}" class="block text-sm font-medium text-base-content/80 mb-1">{t('schedule.plan.vacationDays')} <span class="text-base-content/70">({t('common.optional')})</span></label>
								<input id="plan-vac-{idSuffix}" type="number" step="0.5" min="0" max="365" class="input input-sm w-32" class:input-error={vacationDaysError}
									bind:value={vacationDays} disabled={saving} aria-describedby="plan-vac-hint-{idSuffix}" />
								<p id="plan-vac-hint-{idSuffix}" class="text-xs mt-1 m-0 {vacationDaysError ? 'text-error' : 'text-base-content/70'}">
									{vacationDaysError ? t('schedule.plan.vacationDaysRange') : t('schedule.plan.vacationDaysHint')}</p>
							</div>
						</div>
					{/if}
				<div class="flex gap-2 flex-wrap">
					<button type="submit" class="btn btn-primary btn-sm" disabled={saving}>
						{saving ? t('common.saving') : viaRequest ? t('schedule.plan.submitRequest') : formMode === 'correct' ? t('schedule.plan.submitCorrect') : t('schedule.plan.submitChange')}
					</button>
					<button type="button" class="btn btn-ghost btn-sm" onclick={close} disabled={saving}>{t('common.cancel')}</button>
				</div>
			</form>
		{/if}

		{#if plan?.current}
			<p class="text-xs text-base-content/70 m-0 mb-2" data-testid="schedule-current-since">{t('schedule.plan.currentSince', { date: formatDateFull(plan.current.validFrom!) })}</p>
			{@render week(plan.current, 'schedule-current')}
			{#if plan.current.holidayRegion || plan.current.vacationDaysPerYear != null}
				<p class="text-xs text-base-content/70 mt-2 mb-0 flex gap-3 flex-wrap">
					{#if plan.current.holidayRegion}<span>{t('schedule.plan.region')}: {holidayRegionLabel(plan.current.holidayRegion)}</span>{/if}
					{#if plan.current.vacationDaysPerYear != null}<span>{t('schedule.plan.vacationDaysValue', { days: formatNumber(plan.current.vacationDaysPerYear) })}</span>{/if}
				</p>
			{/if}
			{#if adminView && plan.currentInClosedMonth}
				<p class="text-xs text-base-content/70 mt-2 mb-0">{t('schedule.plan.correctClosed')}</p>
			{/if}
		{:else}
			<p class="text-sm text-base-content/70 m-0">{t('schedule.plan.none')}</p>
		{/if}

		{#if plan?.future}
			<div class="mt-4 p-3 rounded-lg border border-info/40 bg-info/5" data-testid="schedule-future">
				<div class="flex items-center justify-between gap-2 mb-2 flex-wrap">
					<h3 class="text-sm font-semibold m-0">{t('schedule.plan.futureFrom', { date: formatDateFull(plan.future.validFrom!) })}</h3>
					{#if canCancel}
						<button type="button" class="btn btn-ghost btn-xs" onclick={cancelFuture}>{t('schedule.plan.cancelFuture')}</button>
					{/if}
				</div>
				{@render week(plan.future, 'schedule-future-week')}
			</div>
		{/if}

		{#if plan?.history && plan.history.length > 0}
			<details class="mt-4" data-testid="schedule-history">
				<summary class="text-sm font-medium cursor-pointer text-base-content/80">{t('schedule.plan.history', { count: plan.history.length })}</summary>
				<ul class="flex flex-col gap-1.5 list-none p-0 mt-2 mb-0">
					{#each plan.history as p (p.id)}
						<li class="flex items-center gap-x-3 gap-y-1 flex-wrap py-2 px-3 rounded-md border border-base-200 bg-base-200/30 text-sm">
							<span class="text-base-content/70">{formatDateFull(p.validFrom!)} → {p.validTo ? formatDateFull(p.validTo) : t('common.ongoing')}</span>
							<span class="font-semibold text-base-content ml-auto whitespace-nowrap">{t('common.perWeek', { value: formatNumber(weekTotal(p)) })}</span>
						</li>
					{/each}
				</ul>
			</details>
		{/if}
	{/if}
</section>
