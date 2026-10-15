<script lang="ts">
	import { t } from '$lib/i18n';

	/**
	 * "Auch die letzten N Tage prüfen": offered while core hours are not on yet. The caller sends
	 * `coreHoursBackfillDays: days` with the switch-on when `enabled` (the backend then flags those
	 * past days in the background, without notifications).
	 */
	let { enabled = $bindable(false), days = $bindable(7), disabled = false }: { enabled?: boolean; days?: number; disabled?: boolean } = $props();
</script>

<div class="flex items-center gap-2 flex-wrap text-sm mt-2" data-testid="core-hours-backfill">
	<label class="flex items-center gap-2 cursor-pointer min-h-11 sm:min-h-0">
		<input type="checkbox" class="checkbox checkbox-sm" bind:checked={enabled} {disabled} />
		<span>{t('orgSettings.coreHours.backfill')}</span>
	</label>
	<input type="number" class="input input-sm w-16 text-center" min="1" max="31" step="1" bind:value={days}
		aria-label={t('orgSettings.coreHours.backfillDays')} disabled={disabled || !enabled} />
	<span>{t('orgSettings.coreHours.backfillDays')}</span>
	<span class="text-xs text-base-content/70 basis-full">{t('orgSettings.coreHours.backfillHint')}</span>
</div>
