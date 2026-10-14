<script lang="ts">
	import { timeTrackingApi } from '$lib/apiClient';
	import type { TimeEntryChangeResponse } from '$lib/api';
	import { extractErrorMessage } from '$lib/utils/errorHandler';
	import { formatDateTime } from '$lib/utils/formatters';
	import { historyFieldLabel, historyKindLabel, historyValue } from '$lib/utils/entryPayloads';
	import { t } from '$lib/i18n';

	/** Collapsible change history of one time entry ("Verlauf"); loaded the first time it is opened. */
	let { entryId }: { entryId: number } = $props();

	let changes = $state<TimeEntryChangeResponse[] | null>(null);
	let error = $state('');
	let loading = $state(false);

	async function ontoggle(e: Event) {
		if (!(e.currentTarget as HTMLDetailsElement).open || changes || loading) return;
		loading = true;
		error = '';
		try {
			const { data } = await timeTrackingApi.apiV1TimeTrackingIdHistoryGet(entryId);
			changes = [...data].reverse(); // newest first
		} catch (err) {
			error = extractErrorMessage(err, t('entries.history.failed'));
		} finally {
			loading = false;
		}
	}
</script>

<details class="px-3 pb-3 bg-base-200/50 text-sm" {ontoggle}>
	<summary class="cursor-pointer text-xs font-semibold text-base-content/70 uppercase tracking-wide py-1">{t('entries.history.title')}</summary>
	{#if loading}
		<p class="text-xs text-base-content/70 flex items-center gap-2 m-0 mt-1"><span class="loading loading-spinner loading-xs"></span>{t('entries.history.loading')}</p>
	{:else if error}
		<p class="text-xs text-error m-0 mt-1" role="alert">{error}</p>
	{:else if changes && changes.length === 0}
		<p class="text-xs text-base-content/70 m-0 mt-1">{t('entries.history.empty')}</p>
	{:else if changes}
		<ol class="list-none p-0 m-0 mt-1 flex flex-col gap-2">
			{#each changes as c (c.id)}
				<li class="border-l-2 border-base-300 pl-2">
					<div class="text-xs text-base-content/70">
						<span class="font-semibold">{historyKindLabel(c.kind)}</span>
						· {c.changedAt ? formatDateTime(c.changedAt) : ''}
						· {c.changedByName ?? t('entries.history.system')}
					</div>
					{#if c.changes && c.changes.length > 0}
						<ul class="list-none p-0 m-0 text-xs text-base-content/70">
							{#each c.changes as f, i (i)}
								<li class="break-words">
									{historyFieldLabel(f.field)}:
									{#if c.kind === 'Updated' || c.kind === 'ApprovedRequest'}
										<span class="line-through">{historyValue(f.field, f.oldValue)}</span> → {historyValue(f.field, f.newValue)}
									{:else}
										{historyValue(f.field, f.newValue ?? f.oldValue)}
									{/if}
								</li>
							{/each}
						</ul>
					{/if}
				</li>
			{/each}
		</ol>
	{/if}
</details>
