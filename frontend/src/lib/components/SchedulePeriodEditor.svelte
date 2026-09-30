<script lang="ts">
	import { untrack } from 'svelte';
	import { workScheduleApi } from '$lib/apiClient';
	import type { CreateWorkScheduleRequest, WorkScheduleResponse } from '$lib/api';
	import { dateKey } from '$lib/utils/dateHelpers';
	import { dayNamesShort, formatDateFull, formatNumber } from '$lib/utils/formatters';
	import { extractErrorMessage } from '$lib/utils/errorHandler';
	import { confirmDialog } from '$lib/stores/confirm.svelte';
	import { toast } from '$lib/stores/toast.svelte';

	/**
	 * Schedule periods (Arbeitszeit-Zeiträume) of one person in one org: list with
	 * the active one marked, add/replace form, delete. Loads its own data.
	 *
	 * Endpoints: `memberId` set → the admin member endpoints; unset → the caller's
	 * own ("self") endpoints. The page decides `editable` (admin rights, or the
	 * effective workScheduleChangeMode === 'Allowed'); `mode` only explains why a
	 * read-only list cannot be changed.
	 */
	interface Props {
		orgSlug: string;
		memberId?: number | null;
		editable: boolean;
		/** workScheduleChangeMode, for the read-only explanation (self pages). */
		mode?: string | null;
		/** After a period was added, replaced or deleted (e.g. reload the active schedule). */
		onchanged?: () => void;
		class?: string;
	}
	let { orgSlug, memberId = null, editable, mode = null, onchanged, class: className = '' }: Props = $props();

	const MAX_WEEKLY = 80;
	const MAX_DAILY = 24;

	let periods = $state<WorkScheduleResponse[]>([]);
	let loading = $state(true);
	let loadError = $state('');

	let showForm = $state(false);
	let from = $state('');
	let to = $state('');
	let weekly = $state<number | null>(null);
	let distribute = $state(true);
	let days = $state<(number | null)[]>([0, 0, 0, 0, 0]);
	let submitted = $state(false);
	let saving = $state(false);
	let saveError = $state('');
	let deletingId = $state<number | null>(null);

	const today = dateKey(new Date());

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
				? await workScheduleApi.apiV1OrganizationsSlugMembersMemberIdWorkSchedulesGet(orgSlug, memberId)
				: await workScheduleApi.apiV1OrganizationsSlugWorkSchedulesGet(orgSlug);
			periods = [...(data as WorkScheduleResponse[])].sort((a, b) => (b.validFrom ?? '').localeCompare(a.validFrom ?? ''));
		} catch (err) {
			loadError = extractErrorMessage(err, 'Could not load schedule periods.');
		} finally {
			loading = false;
		}
	}

	function isActive(p: WorkScheduleResponse): boolean {
		return !!p.validFrom && p.validFrom <= today && (!p.validTo || p.validTo >= today);
	}

	function outOfRange(v: number | null | undefined, max: number): boolean {
		return v != null && (!Number.isFinite(v) || v < 0 || v > max);
	}

	let fromError = $derived(submitted && !from ? 'Start date is required.' : '');
	let toError = $derived(submitted && !!to && !!from && to < from ? 'The end must be on or after the start.' : '');
	let weeklyError = $derived(submitted && outOfRange(weekly, MAX_WEEKLY) ? `Enter 0–${MAX_WEEKLY} hours.` : '');
	let daysError = $derived(submitted && !distribute && days.some((d) => outOfRange(d, MAX_DAILY)) ? `Daily targets must be 0–${MAX_DAILY} hours.` : '');
	let replacing = $derived(periods.find((p) => p.validFrom === from) ?? null);

	function resetForm() {
		from = '';
		to = '';
		weekly = null;
		distribute = true;
		days = [0, 0, 0, 0, 0];
		submitted = false;
		saveError = '';
	}

	function toggleForm() {
		showForm = !showForm;
		if (!showForm) resetForm();
	}

	async function save(e: Event) {
		e.preventDefault();
		submitted = true;
		if (fromError || toError || weeklyError || daysError) return;
		saving = true;
		saveError = '';
		const [mon, tue, wed, thu, fri] = days.map((d) => d ?? 0);
		const payload: CreateWorkScheduleRequest = {
			validFrom: from,
			validTo: to || undefined,
			weeklyWorkHours: weekly ?? undefined,
			distributeEvenly: distribute,
			targetMon: distribute ? undefined : mon,
			targetTue: distribute ? undefined : tue,
			targetWed: distribute ? undefined : wed,
			targetThu: distribute ? undefined : thu,
			targetFri: distribute ? undefined : fri
		};
		const existingId = replacing?.id;
		try {
			if (memberId) {
				if (existingId) await workScheduleApi.apiV1OrganizationsSlugMembersMemberIdWorkSchedulesIdPut(orgSlug, memberId, existingId, payload);
				else await workScheduleApi.apiV1OrganizationsSlugMembersMemberIdWorkSchedulesPost(orgSlug, memberId, payload);
			} else {
				if (existingId) await workScheduleApi.apiV1OrganizationsSlugWorkSchedulesIdPut(orgSlug, existingId, payload);
				else await workScheduleApi.apiV1OrganizationsSlugWorkSchedulesPost(orgSlug, payload);
			}
			toast.success(existingId ? 'Schedule period updated.' : 'Schedule period added.');
			showForm = false;
			resetForm();
			await load();
			onchanged?.();
		} catch (err) {
			saveError = extractErrorMessage(err, 'Could not save the schedule period.');
		} finally {
			saving = false;
		}
	}

	async function remove(p: WorkScheduleResponse) {
		if (!p.id) return;
		const ok = await confirmDialog({
			title: 'Delete schedule period?',
			message: `${formatDateFull(p.validFrom!)} → ${p.validTo ? formatDateFull(p.validTo) : 'ongoing'}`,
			confirmLabel: 'Delete',
			danger: true
		});
		if (!ok) return;
		deletingId = p.id;
		try {
			if (memberId) await workScheduleApi.apiV1OrganizationsSlugMembersMemberIdWorkSchedulesIdDelete(orgSlug, memberId, p.id);
			else await workScheduleApi.apiV1OrganizationsSlugWorkSchedulesIdDelete(orgSlug, p.id);
			toast.success('Schedule period deleted.');
			await load();
			onchanged?.();
		} catch (err) {
			toast.error(err, 'Could not delete the schedule period.');
		} finally {
			deletingId = null;
		}
	}
</script>

<section class="card bg-base-100 border border-base-300 p-5 {className}">
	<div class="flex items-center justify-between gap-2 mb-3 flex-wrap">
		<h2 class="text-base font-bold text-base-content m-0">Schedule periods</h2>
		{#if editable && !loading && !loadError}
			<button type="button" class="btn btn-sm {showForm ? 'btn-ghost' : 'btn-primary'}" onclick={toggleForm} aria-expanded={showForm}>
				{showForm ? 'Cancel' : '+ Add period'}
			</button>
		{/if}
	</div>

	{#if !editable && mode === 'RequiresApproval'}
		<p class="text-sm text-base-content/60 mb-3">Changing schedule periods needs an admin's approval. Ask an admin of your organization to adjust them.</p>
	{:else if !editable && mode === 'Disabled'}
		<p class="text-sm text-base-content/60 mb-3">Schedule periods are managed by the admins of this organization.</p>
	{/if}

	{#if showForm && editable}
		<form onsubmit={save} novalidate class="flex flex-col gap-3 p-3 mb-4 rounded-lg border border-base-300 bg-base-200/40">
			{#if saveError}
				<div class="alert alert-error text-sm py-2">{saveError}</div>
			{/if}
			<div class="grid grid-cols-1 sm:grid-cols-2 gap-3">
				<div>
					<label for="sp-from-{memberId ?? 'self'}" class="block text-sm font-medium text-base-content/80 mb-1">Valid from</label>
					<input id="sp-from-{memberId ?? 'self'}" type="date" class="input input-bordered input-sm w-full" class:input-error={fromError} aria-invalid={!!fromError} bind:value={from} disabled={saving} />
					{#if fromError}<p class="text-error text-xs mt-1">{fromError}</p>{/if}
				</div>
				<div>
					<label for="sp-to-{memberId ?? 'self'}" class="block text-sm font-medium text-base-content/80 mb-1">Valid to <span class="text-base-content/40">(optional)</span></label>
					<input id="sp-to-{memberId ?? 'self'}" type="date" class="input input-bordered input-sm w-full" class:input-error={toError} aria-invalid={!!toError} min={from || undefined} bind:value={to} disabled={saving} />
					{#if toError}<p class="text-error text-xs mt-1">{toError}</p>{/if}
				</div>
			</div>
			<div>
				<label for="sp-weekly-{memberId ?? 'self'}" class="block text-sm font-medium text-base-content/80 mb-1">Weekly hours</label>
				<input id="sp-weekly-{memberId ?? 'self'}" type="number" step="0.5" min="0" max={MAX_WEEKLY} class="input input-bordered input-sm w-32" class:input-error={weeklyError} aria-invalid={!!weeklyError} bind:value={weekly} disabled={saving} />
				{#if weeklyError}<p class="text-error text-xs mt-1">{weeklyError}</p>{/if}
			</div>
			<label class="flex items-center gap-2 text-sm text-base-content/80 cursor-pointer w-fit">
				<input type="checkbox" class="checkbox checkbox-primary checkbox-sm" bind:checked={distribute} disabled={saving} />
				Distribute evenly (Mon–Fri)
			</label>
			{#if !distribute}
				<fieldset>
					<legend class="text-sm font-medium text-base-content/80 mb-1">Daily targets (hours)</legend>
					<div class="grid grid-cols-5 gap-2 max-w-sm">
						{#each dayNamesShort().slice(0, 5) as dayName, i}
							<div class="flex flex-col items-center gap-1">
								<label for="sp-day{i}-{memberId ?? 'self'}" class="text-xs font-semibold text-base-content/60">{dayName}</label>
								<input id="sp-day{i}-{memberId ?? 'self'}" type="number" step="0.5" min="0" max={MAX_DAILY} class="input input-bordered input-xs w-full text-center" bind:value={days[i]} disabled={saving} />
							</div>
						{/each}
					</div>
					{#if daysError}<p class="text-error text-xs mt-1">{daysError}</p>{/if}
				</fieldset>
			{/if}
			{#if replacing}
				<p class="text-xs text-warning m-0">A period starting on this date exists already — saving replaces it.</p>
			{/if}
			<div>
				<button type="submit" class="btn btn-primary btn-sm" disabled={saving}>
					{saving ? 'Saving…' : replacing ? 'Replace period' : 'Add period'}
				</button>
			</div>
		</form>
	{/if}

	{#if loading}
		<p class="text-sm text-base-content/50 flex items-center gap-2"><span class="loading loading-spinner loading-xs"></span> Loading…</p>
	{:else if loadError}
		<div class="alert alert-error text-sm py-2">
			<span>{loadError}</span>
			<button type="button" class="btn btn-ghost btn-xs" onclick={load}>Try again</button>
		</div>
	{:else if periods.length === 0}
		<p class="text-sm text-base-content/50">No schedule periods yet.</p>
	{:else}
		<ul class="flex flex-col gap-1.5 list-none p-0 m-0">
			{#each periods as p (p.id)}
				{@const active = isActive(p)}
				<li class="flex items-center gap-x-3 gap-y-1 flex-wrap py-2 px-3 rounded-md border text-sm {active ? 'border-success/40 bg-success/5' : 'border-base-200 bg-base-200/30'}">
					<span class="font-medium text-base-content/70">
						{formatDateFull(p.validFrom!)} <span class="text-base-content/40" aria-hidden="true">→</span>
						{p.validTo ? formatDateFull(p.validTo) : 'ongoing'}
					</span>
					{#if active}<span class="badge badge-success badge-xs font-semibold">Active</span>{/if}
					<span class="font-semibold text-base-content ml-auto whitespace-nowrap">
						{p.weeklyWorkHours != null ? `${formatNumber(p.weeklyWorkHours)} h/week` : '—'}
					</span>
					{#if editable}
						<button type="button" class="btn btn-ghost btn-xs text-base-content/40 hover:text-error" aria-label="Delete schedule period" title="Delete schedule period" onclick={() => remove(p)} disabled={deletingId === p.id}>
							{#if deletingId === p.id}<span class="loading loading-spinner loading-xs"></span>{:else}✕{/if}
						</button>
					{/if}
				</li>
			{/each}
		</ul>
	{/if}
</section>
