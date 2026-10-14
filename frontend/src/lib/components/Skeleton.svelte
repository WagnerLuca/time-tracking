<script lang="ts">
	import { t } from '$lib/i18n';

	/** Page-load placeholder: grey blocks in the page's shape instead of a spinner. */
	let { lines = 3, message }: { lines?: number; message?: string } = $props();
</script>

<div class="flex flex-col gap-3 py-2" role="status" aria-live="polite" data-testid="skeleton">
	<span class="sr-only">{message ?? t('common.loading')}</span>
	<div class="skeleton h-7 w-48" aria-hidden="true"></div>
	{#each Array(lines) as _, i (i)}
		<div class="skeleton h-20 w-full" aria-hidden="true"></div>
	{/each}
</div>
