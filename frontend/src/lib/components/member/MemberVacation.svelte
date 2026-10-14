<script lang="ts">
	import { organizationsApi } from '$lib/apiClient';
	import type { OrganizationMemberResponse } from '$lib/api';
	import { formatNumber } from '$lib/utils/formatters';
	import { extractErrorMessage } from '$lib/utils/errorHandler';
	import { toast } from '$lib/stores/toast.svelte';
	import { t } from '$lib/i18n';

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
	let daysError = $derived(submitted && (days == null || !Number.isFinite(days) || days < 0 || days > 365) ? t('memberDetail.vacation.range') : '');

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
			toast.success(t('memberDetail.vacation.saved'));
			editing = false;
			onchanged?.();
		} catch (err) {
			saveError = extractErrorMessage(err, t('memberDetail.vacation.saveFailed'));
		} finally {
			saving = false;
		}
	}
</script>

<section class="card bg-base-100 border border-base-300 p-5 {className}">
	<div class="flex items-center justify-between gap-2 mb-3">
		<h2 class="text-base font-bold text-base-content m-0">{t('memberDetail.vacation.title')}</h2>
		{#if canEdit && !editing}
			<button type="button" class="btn btn-ghost btn-sm" onclick={startEdit}>{t('common.edit')}</button>
		{/if}
	</div>

	{#if editing}
		<form onsubmit={save} novalidate class="flex flex-col gap-2">
			{#if saveError}<div class="alert alert-error text-sm py-2">{saveError}</div>{/if}
			<label for="vac-days-{member.id}" class="text-sm font-medium text-base-content/80">{t('memberDetail.vacation.daysPerYear')}</label>
			<div class="flex items-center gap-2 flex-wrap">
				<input id="vac-days-{member.id}" type="number" step="0.5" min="0" max="365" class="input input-sm w-28" class:input-error={daysError} aria-invalid={!!daysError} bind:value={days} disabled={saving} />
				<button type="submit" class="btn btn-primary btn-sm" disabled={saving}>{saving ? t('common.saving') : t('common.save')}</button>
				<button type="button" class="btn btn-ghost btn-sm" onclick={() => (editing = false)} disabled={saving}>{t('common.cancel')}</button>
			</div>
			{#if daysError}<p class="text-error text-xs m-0">{daysError}</p>{/if}
		</form>
	{:else if allowed > 0}
		<div class="flex gap-x-6 gap-y-2 mb-3 flex-wrap">
			<div><span class="text-2xl font-bold text-base-content">{formatNumber(remaining)}</span> <span class="text-sm text-base-content/70">{t('memberDetail.vacation.remaining')}</span></div>
			<div><span class="text-lg font-semibold text-base-content/70">{formatNumber(taken)}</span> <span class="text-sm text-base-content/70">{t('memberDetail.vacation.taken')}</span></div>
			<div><span class="text-lg font-semibold text-base-content/70">{formatNumber(planned)}</span> <span class="text-sm text-base-content/70">{t('memberDetail.vacation.planned')}</span></div>
			<div><span class="text-lg font-semibold text-base-content/70">{formatNumber(allowed)}</span> <span class="text-sm text-base-content/70">{t('memberDetail.vacation.total')}</span></div>
		</div>
		<div class="w-full bg-base-200 rounded-full h-2.5 overflow-hidden flex" role="img" aria-label={t('memberDetail.vacation.barLabel', { taken: formatNumber(taken), planned: formatNumber(planned), total: formatNumber(allowed) })}>
			<div class="h-2.5 transition-all {barClass}" style="width: {takenPct}%"></div>
			<div class="h-2.5 transition-all {barClass} opacity-40" style="width: {plannedPct}%"></div>
		</div>
		<div class="flex gap-x-4 gap-y-1 mt-2 text-xs text-base-content/70 flex-wrap">
			<span class="flex items-center gap-1"><span class="w-2.5 h-2.5 rounded-sm {barClass}"></span> {t('memberDetail.vacation.legendTaken')}</span>
			<span class="flex items-center gap-1"><span class="w-2.5 h-2.5 rounded-sm {barClass} opacity-40"></span> {t('memberDetail.vacation.legendPlanned')}</span>
		</div>
	{:else}
		<p class="text-sm text-base-content/70 m-0">{t('memberDetail.vacation.none')}{canEdit ? '' : ` ${t('memberDetail.vacation.askAdmin')}`}</p>
	{/if}
</section>
