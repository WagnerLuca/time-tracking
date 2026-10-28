<script lang="ts">
	import { organizationsApi } from '$lib/apiClient';
	import type { OrganizationMemberResponse } from '$lib/api';
	import { formatNumber } from '$lib/utils/formatters';
	import { extractErrorMessage } from '$lib/utils/errorHandler';
	import { toast } from '$lib/stores/toast.svelte';
	import { t } from '$lib/i18n';
	import { memberCarryFigures, orgYear, vacationDetailLines, vacationTotal } from '$lib/utils/vacation';
	import VacationYearEditor from './VacationYearEditor.svelte';

	/**
	 * Vacation of one member this year: remaining / taken / planned / available (entitlement + adjustment + carried-over
	 * days − lapsed ones), with the carry-over lines; admins edit the yearly days and each year's entitlement/adjustment.
	 */
	interface Props {
		orgSlug: string;
		/** The org's time zone: "this year" is the org-local one, like the backend's. */
		timeZone?: string | null;
		member: OrganizationMemberResponse;
		canEdit: boolean;
		onchanged?: () => void;
		class?: string;
	}
	let { orgSlug, timeZone = null, member, canEdit, onchanged, class: className = '' }: Props = $props();

	let editing = $state(false);
	let days = $state<number | null>(0);
	let submitted = $state(false);
	let saving = $state(false);
	let saveError = $state('');
	let yearsOpen = $state(false);

	let allowed = $derived(vacationTotal(member));
	let year = $derived(orgYear(timeZone));
	let detailLines = $derived(vacationDetailLines(memberCarryFigures(member), year));
	let taken = $derived(member.vacationDaysTaken ?? 0);
	let planned = $derived(member.vacationDaysPlanned ?? 0);
	let remaining = $derived(member.vacationDaysRemaining ?? 0);
	let usedRatio = $derived(allowed > 0 ? (member.vacationDaysUsed ?? taken + planned) / allowed : 0);
	let takenPct = $derived(allowed > 0 ? Math.min(100, (taken / allowed) * 100) : 0);
	let plannedPct = $derived(allowed > 0 ? Math.min(100 - takenPct, (planned / allowed) * 100) : 0);
	let barClass = $derived(usedRatio > 0.9 ? 'bg-error' : usedRatio > 0.7 ? 'bg-warning' : 'bg-success');
	let daysError = $derived(submitted && (days == null || !Number.isFinite(days) || days < 0 || days > 365) ? t('memberDetail.vacation.range') : '');

	function startEdit() {
		// "Tage pro Jahr" = this year's entitlement (later years follow it)
		days = member.vacationEntitlement ?? member.vacationDaysPerYear ?? 0;
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
			<div class="flex gap-1">
				<button type="button" class="btn btn-ghost btn-sm" onclick={() => (yearsOpen = true)}>{t('vacation.year.open')}</button>
				<button type="button" class="btn btn-ghost btn-sm" onclick={startEdit}>{t('common.edit')}</button>
			</div>
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
		{#each detailLines as line (line)}
			<p class="text-xs text-base-content/70 m-0 mb-2" data-testid="vacation-carry-line">{line}</p>
		{/each}
		<div class="w-full bg-base-200 rounded-full h-2.5 overflow-hidden flex" role="img" aria-label={t('memberDetail.vacation.barLabel', { taken: formatNumber(taken), planned: formatNumber(planned), total: formatNumber(allowed) })}>
			<div class="h-2.5 transition-[width] duration-300 ease-out {barClass}" style="width: {takenPct}%"></div>
			<div class="h-2.5 transition-[width] duration-300 ease-out {barClass} opacity-40" style="width: {plannedPct}%"></div>
		</div>
		<div class="flex gap-x-4 gap-y-1 mt-2 text-xs text-base-content/70 flex-wrap">
			<span class="flex items-center gap-1"><span class="w-2.5 h-2.5 rounded-sm {barClass}"></span> {t('memberDetail.vacation.legendTaken')}</span>
			<span class="flex items-center gap-1"><span class="w-2.5 h-2.5 rounded-sm {barClass} opacity-40"></span> {t('memberDetail.vacation.legendPlanned')}</span>
		</div>
	{:else}
		{#each detailLines as line (line)}
			<p class="text-xs text-base-content/70 m-0 mb-2" data-testid="vacation-carry-line">{line}</p>
		{/each}
		<p class="text-sm text-base-content/70 m-0">{t('memberDetail.vacation.none')}{canEdit ? '' : ` ${t('memberDetail.vacation.askAdmin')}`}</p>
	{/if}
</section>

{#if canEdit}
	<VacationYearEditor open={yearsOpen} onclose={() => (yearsOpen = false)} {orgSlug} {timeZone} memberId={member.id!} memberName={`${member.firstName} ${member.lastName}`} onsaved={onchanged} />
{/if}
