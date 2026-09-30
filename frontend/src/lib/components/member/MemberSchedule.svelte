<script lang="ts">
	import type { WorkScheduleResponse } from '$lib/api';
	import { dayNamesShort, formatNumber } from '$lib/utils/formatters';
	import { t } from '$lib/i18n';

	/** Read-only summary of the currently active work schedule (values come from the schedule periods). */
	interface Props {
		schedule: WorkScheduleResponse | null;
		loading?: boolean;
		error?: string;
		class?: string;
	}
	let { schedule, loading = false, error = '', class: className = '' }: Props = $props();

	let targets = $derived(schedule ? [schedule.targetMon, schedule.targetTue, schedule.targetWed, schedule.targetThu, schedule.targetFri] : []);

	function hours(v: number | null | undefined): string {
		return v != null ? t('common.hoursShort', { value: formatNumber(v) }) : '—';
	}
</script>

<section class="card bg-base-100 border border-base-300 p-5 {className}">
	<h2 class="text-base font-bold text-base-content m-0 mb-3">{t('schedule.active.title')}</h2>
	{#if loading}
		<p class="text-sm text-base-content/50 flex items-center gap-2"><span class="loading loading-spinner loading-xs"></span> {t('common.loading')}</p>
	{:else if error}
		<div class="alert alert-error text-sm py-2">{error}</div>
	{:else if schedule}
		<div class="grid grid-cols-3 sm:grid-cols-6 gap-2">
			<div class="flex flex-col items-center bg-base-200/40 p-2 rounded-lg border border-base-300">
				<span class="text-xs text-base-content/60 font-medium">{t('schedule.active.weeklyHours')}</span>
				<span class="text-lg font-bold text-base-content">{hours(schedule.weeklyWorkHours)}</span>
			</div>
			{#each dayNamesShort().slice(0, 5) as day, i}
				<div class="flex flex-col items-center bg-base-200/40 p-2 rounded-lg border border-base-300">
					<span class="text-xs text-base-content/60 font-medium">{day}</span>
					<span class="text-lg font-bold text-base-content">{hours(targets[i] ?? 0)}</span>
				</div>
			{/each}
		</div>
		<p class="text-xs text-base-content/50 mt-3 mb-0">{t('schedule.active.fromPeriods')}</p>
	{:else}
		<p class="text-sm text-base-content/50 m-0">{t('schedule.active.none')}</p>
	{/if}
</section>
