<script lang="ts">
	import { toast } from '$lib/stores/toast.svelte';
	import { t } from '$lib/i18n';

	const alertClass: Record<string, string> = {
		success: 'alert-success',
		error: 'alert-error',
		info: 'alert-info',
		warning: 'alert-warning'
	};
</script>

<div class="toast toast-end toast-bottom z-[1000] max-w-[calc(100vw-1rem)]" role="status" aria-live="polite">
	{#each toast.items as item (item.id)}
		<div class="alert {alertClass[item.kind]} shadow-lg text-sm py-2 pr-2 max-w-sm whitespace-normal" role={item.kind === 'error' ? 'alert' : undefined}>
			<span class="flex-1">{item.message}</span>
			<button type="button" class="btn btn-ghost btn-xs btn-circle" aria-label={t('common.dismiss')} onclick={() => toast.dismiss(item.id)}>✕</button>
		</div>
	{/each}
</div>
