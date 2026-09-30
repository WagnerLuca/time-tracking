<script lang="ts">
	import { organizationsApi } from '$lib/apiClient';
	import type { OrganizationMemberResponse } from '$lib/api';
	import { formatNumber } from '$lib/utils/formatters';
	import { extractErrorMessage } from '$lib/utils/errorHandler';
	import { toast } from '$lib/stores/toast.svelte';

	/** Vacation allowance of one member: remaining / taken / planned / total; admins edit the yearly days. */
	interface Props {
		orgSlug: string;
		member: OrganizationMemberResponse;
		canEdit: boolean;
		onchanged?: () => void;
		class?: string;
	}
	let { orgSlug, member, canEdit, onchanged, class: className = '' }: Props = $props();

	let editing = $state(false);
	let days = $state<number | null>(0);
	let submitted = $state(false);
	let saving = $state(false);
	let saveError = $state('');

	let allowed = $derived(member.vacationDaysPerYear ?? 0);
	let taken = $derived(member.vacationDaysTaken ?? 0);
	let planned = $derived(member.vacationDaysPlanned ?? 0);
	let remaining = $derived(member.vacationDaysRemaining ?? 0);
	let usedRatio = $derived(allowed > 0 ? (member.vacationDaysUsed ?? taken + planned) / allowed : 0);
	let takenPct = $derived(allowed > 0 ? Math.min(100, (taken / allowed) * 100) : 0);
	let plannedPct = $derived(allowed > 0 ? Math.min(100 - takenPct, (planned / allowed) * 100) : 0);
	let barClass = $derived(usedRatio > 0.9 ? 'bg-error' : usedRatio > 0.7 ? 'bg-warning' : 'bg-success');
	let daysError = $derived(submitted && (days == null || !Number.isFinite(days) || days < 0 || days > 365) ? 'Enter 0–365 days.' : '');

	function startEdit() {
		days = allowed;
		submitted = false;
		saveError = '';
		editing = true;
	}

	async function save(e: Event) {
		e.preventDefault();
		submitted = true;
		if (daysError || days == null) return;
		saving = true;
		saveError = '';
		try {
			await organizationsApi.apiV1OrganizationsSlugMembersMemberIdVacationDaysPut(orgSlug, member.id!, { days });
			toast.success('Vacation days saved.');
			editing = false;
			onchanged?.();
		} catch (err) {
			saveError = extractErrorMessage(err, 'Could not save the vacation days.');
		} finally {
			saving = false;
		}
	}
</script>

<section class="card bg-base-100 border border-base-300 p-5 {className}">
	<div class="flex items-center justify-between gap-2 mb-3">
		<h2 class="text-base font-bold text-base-content m-0">Vacation days</h2>
		{#if canEdit && !editing}
			<button type="button" class="btn btn-ghost btn-sm" onclick={startEdit}>Edit</button>
		{/if}
	</div>

	{#if editing}
		<form onsubmit={save} novalidate class="flex flex-col gap-2">
			{#if saveError}<div class="alert alert-error text-sm py-2">{saveError}</div>{/if}
			<label for="vac-days-{member.id}" class="text-sm font-medium text-base-content/80">Days per year</label>
			<div class="flex items-center gap-2 flex-wrap">
				<input id="vac-days-{member.id}" type="number" step="0.5" min="0" max="365" class="input input-bordered input-sm w-28" class:input-error={daysError} aria-invalid={!!daysError} bind:value={days} disabled={saving} />
				<button type="submit" class="btn btn-primary btn-sm" disabled={saving}>{saving ? 'Saving…' : 'Save'}</button>
				<button type="button" class="btn btn-ghost btn-sm" onclick={() => (editing = false)} disabled={saving}>Cancel</button>
			</div>
			{#if daysError}<p class="text-error text-xs m-0">{daysError}</p>{/if}
		</form>
	{:else if allowed > 0}
		<div class="flex gap-x-6 gap-y-2 mb-3 flex-wrap">
			<div><span class="text-2xl font-bold text-base-content">{formatNumber(remaining)}</span> <span class="text-sm text-base-content/50">remaining</span></div>
			<div><span class="text-lg font-semibold text-base-content/70">{formatNumber(taken)}</span> <span class="text-sm text-base-content/50">taken</span></div>
			<div><span class="text-lg font-semibold text-base-content/70">{formatNumber(planned)}</span> <span class="text-sm text-base-content/50">planned</span></div>
			<div><span class="text-lg font-semibold text-base-content/70">{formatNumber(allowed)}</span> <span class="text-sm text-base-content/50">total</span></div>
		</div>
		<div class="w-full bg-base-200 rounded-full h-2.5 overflow-hidden flex" role="img" aria-label="{formatNumber(taken)} taken, {formatNumber(planned)} planned of {formatNumber(allowed)} days">
			<div class="h-2.5 transition-all {barClass}" style="width: {takenPct}%"></div>
			<div class="h-2.5 transition-all {barClass} opacity-40" style="width: {plannedPct}%"></div>
		</div>
		<div class="flex gap-x-4 gap-y-1 mt-2 text-xs text-base-content/50 flex-wrap">
			<span class="flex items-center gap-1"><span class="w-2.5 h-2.5 rounded-sm {barClass}"></span> Taken</span>
			<span class="flex items-center gap-1"><span class="w-2.5 h-2.5 rounded-sm {barClass} opacity-40"></span> Planned for the rest of the year</span>
		</div>
	{:else}
		<p class="text-sm text-base-content/50 m-0">No vacation allowance set.{canEdit ? '' : ' Ask an admin to set it.'}</p>
	{/if}
</section>
