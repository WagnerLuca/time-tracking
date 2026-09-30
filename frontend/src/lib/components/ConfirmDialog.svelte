<script lang="ts">
	import Modal from './Modal.svelte';
	import { confirmState } from '$lib/stores/confirm.svelte';
	import { t } from '$lib/i18n';

	// Mounted once in the root layout; shows the dialog requested via confirmDialog().
	const current = $derived(confirmState.current);
</script>

<Modal open={!!current} title={current?.title} onclose={() => confirmState.answer(false)}>
	{#if current?.message}
		<p class="text-sm text-base-content/70 m-0">{current.message}</p>
	{/if}
	{#snippet actions()}
		<button type="button" class="btn btn-ghost" onclick={() => confirmState.answer(false)}>
			{current?.cancelLabel ?? t('common.cancel')}
		</button>
		<!-- svelte-ignore a11y_autofocus -->
		<button type="button" class="btn {current?.danger ? 'btn-error' : 'btn-primary'}" autofocus onclick={() => confirmState.answer(true)}>
			{current?.confirmLabel ?? t('common.confirm')}
		</button>
	{/snippet}
</Modal>
