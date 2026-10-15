<script lang="ts">
	import Modal from './Modal.svelte';
	import { confirmState } from '$lib/stores/confirm.svelte';
	import { t } from '$lib/i18n';

	// Mounted once in the root layout; shows the dialog requested via confirmDialog() / promptDialog().
	const current = $derived(confirmState.current);
	let text = $state('');
	$effect(() => {
		if (current) text = '';
	});
	const blocked = $derived(!!current?.input && !text.trim());
</script>

<Modal open={!!current} title={current?.title} onclose={() => confirmState.answer(false)}>
	{#if current?.message}
		<p class="text-sm text-base-content/70 m-0">{current.message}</p>
	{/if}
	{#if current?.input}
		<label class="block mt-3">
			<span class="text-sm font-medium">{current.input.label}</span>
			<!-- svelte-ignore a11y_autofocus -->
			<textarea class="textarea w-full mt-1" rows="2" maxlength={current.input.maxLength ?? 500} bind:value={text} autofocus
				data-testid="prompt-input"></textarea>
		</label>
	{/if}
	{#snippet actions()}
		<button type="button" class="btn btn-ghost" onclick={() => confirmState.answer(false)}>
			{current?.cancelLabel ?? t('common.cancel')}
		</button>
		<!-- svelte-ignore a11y_autofocus -->
		<button type="button" class="btn {current?.danger ? 'btn-error' : 'btn-primary'}" autofocus={!current?.input} disabled={blocked}
			onclick={() => confirmState.answer(true, text)}>
			{current?.confirmLabel ?? t('common.confirm')}
		</button>
	{/snippet}
</Modal>
