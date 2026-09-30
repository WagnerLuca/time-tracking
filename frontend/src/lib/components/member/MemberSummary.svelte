<script lang="ts">
	import type { MemberTimeOverviewResponse, OrganizationMemberResponse } from '$lib/api';
	import { formatDelta, formatHoursDecimal, formatNumber } from '$lib/utils/formatters';
	import type { BalanceMinutes } from '$lib/utils/balance';
	import { t } from '$lib/i18n';

	/** All-time figures for a member: cumulative overtime, net tracked time, entries, initial overtime, vacation left. */
	interface Props {
		member: OrganizationMemberResponse;
		overview: MemberTimeOverviewResponse | null;
		initialOvertimeHours: number;
		/** From the balance endpoint (null when not visible to the caller). */
		balance: BalanceMinutes | null;
		loading: boolean;
	}
	let { member, overview, initialOvertimeHours, balance, loading }: Props = $props();
</script>

<section class="card bg-base-100 border border-base-300 p-5 mb-5">
	<h2 class="text-base font-bold text-base-content m-0 mb-3">{t('memberDetail.summary.title')}</h2>
	{#if loading}
		<p class="text-sm text-base-content/50 flex items-center gap-2"><span class="loading loading-spinner loading-xs"></span> {t('memberDetail.summary.calculating')}</p>
	{:else if overview}
		<div class="grid grid-cols-2 sm:grid-cols-[repeat(auto-fit,minmax(120px,1fr))] gap-3">
			{#if balance}
				<div class="rounded-lg p-3 text-center border-2 {balance.cumulative >= 0 ? 'border-success/40 bg-success/5' : 'border-error/40 bg-error/5'}">
					<span class="block text-xl font-bold {balance.cumulative >= 0 ? 'text-success' : 'text-error'}">{formatDelta(balance.cumulative)}</span>
					<span class="block text-xs text-base-content/50 mt-0.5">{t('memberDetail.summary.cumulative')}</span>
				</div>
			{/if}
			<div class="bg-base-200/50 border border-base-200 rounded-lg p-3 text-center">
				<span class="block text-xl font-bold text-base-content">{formatHoursDecimal(overview.netTrackedMinutes)}</span>
				<span class="block text-xs text-base-content/50 mt-0.5">{t('memberDetail.summary.netTracked')}</span>
			</div>
			<div class="bg-base-200/50 border border-base-200 rounded-lg p-3 text-center">
				<span class="block text-xl font-bold text-base-content">{formatNumber(overview.entryCount ?? 0)}</span>
				<span class="block text-xs text-base-content/50 mt-0.5">{t('memberDetail.summary.entries')}</span>
			</div>
			<div class="bg-base-200/50 border border-base-200 rounded-lg p-3 text-center">
				<span class="block text-xl font-bold text-base-content">{t('common.hoursShort', { value: formatNumber(initialOvertimeHours, { signDisplay: 'exceptZero', maximumFractionDigits: 2 }) })}</span>
				<span class="block text-xs text-base-content/50 mt-0.5">{t('memberDetail.summary.initialOvertime')}</span>
			</div>
			{#if (member.vacationDaysPerYear ?? 0) > 0}
				<div class="bg-base-200/50 border border-base-200 rounded-lg p-3 text-center">
					<span class="block text-xl font-bold text-info">{formatNumber(member.vacationDaysRemaining ?? 0)}/{formatNumber(member.vacationDaysPerYear ?? 0)}</span>
					<span class="block text-xs text-base-content/50 mt-0.5">{t('memberDetail.summary.vacationLeft')}</span>
				</div>
			{/if}
		</div>
	{:else}
		<p class="text-sm text-base-content/50 m-0">{t('memberDetail.summary.none')}</p>
	{/if}
</section>
