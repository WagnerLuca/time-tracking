<script lang="ts">
	import Modal from './Modal.svelte';
	import { t } from '$lib/i18n';

	let {
		open = false,
		type = null,
		newStart = $bindable(''),
		newEnd = $bindable(''),
		newPause = $bindable(0),
		message = $bindable(''),
		sending = false,
		error = '',
		onsubmit,
		oncancel
	}: {
		open: boolean;
		type: 'edit' | 'pause' | null;
		newStart: string;
		newEnd: string;
		newPause: number;
		message: string;
		sending: boolean;
		/** Submit error, shown inline so the entered values stay. */
		error?: string;
		onsubmit: () => void;
		oncancel: () => void;
	} = $props();

	const id = $props.id();

	const endBeforeStart = $derived(type === 'edit' && !!newStart && !!newEnd && new Date(newEnd) <= new Date(newStart));
	const pauseInvalid = $derived(type === 'pause' && !(Number(newPause) >= 0));
	const valid = $derived(!endBeforeStart && !pauseInvalid);

	function submit(e: SubmitEvent) {
		e.preventDefault();
		if (valid && !sending) onsubmit();
	}
</script>

<Modal {open} onclose={oncancel} title={type === 'edit' ? t('entries.request.titleEdit') : t('entries.request.titlePause')}>
	<form id="{id}-form" onsubmit={submit} novalidate>
		<p class="text-sm text-base-content/60 mb-4">{t('entries.request.intro')}</p>

		{#if error}
			<div role="alert" class="alert alert-error text-sm mb-3 py-2 px-3">{error}</div>
		{/if}

		{#if type === 'edit'}
			<div class="flex flex-col gap-3 mb-3">
				<div class="flex flex-col gap-1">
					<label for="{id}-start" class="text-xs font-semibold text-base-content/70">{t('entries.request.newStart')}</label>
					<input id="{id}-start" class="input input-sm w-full" type="datetime-local" bind:value={newStart} disabled={sending} />
				</div>
				<div class="flex flex-col gap-1">
					<label for="{id}-end" class="text-xs font-semibold text-base-content/70">{t('entries.request.newEnd')}</label>
					<input id="{id}-end" class="input input-sm w-full {endBeforeStart ? 'input-error' : ''}" type="datetime-local" bind:value={newEnd} min={newStart || undefined} disabled={sending} aria-invalid={endBeforeStart} />
					{#if endBeforeStart}
						<span class="text-xs text-error">{t('entries.endAfterStart')}</span>
					{/if}
				</div>
			</div>
		{:else}
			<div class="flex flex-col gap-1 mb-3">
				<label for="{id}-pause" class="text-xs font-semibold text-base-content/70">{t('entries.request.newPause')}</label>
				<input id="{id}-pause" class="input input-sm w-full {pauseInvalid ? 'input-error' : ''}" type="number" min="0" step="1" bind:value={newPause} disabled={sending} aria-invalid={pauseInvalid} />
			</div>
		{/if}

		<label for="{id}-message" class="text-xs font-semibold text-base-content/70">{t('entries.request.message')}</label>
		<textarea
			id="{id}-message"
			class="textarea w-full text-sm mt-1"
			bind:value={message}
			rows="2"
			disabled={sending}
		></textarea>
	</form>

	{#snippet actions()}
		<button type="button" class="btn btn-ghost btn-sm" onclick={oncancel} disabled={sending}>{t('common.cancel')}</button>
		<button type="submit" form="{id}-form" class="btn btn-primary btn-sm" disabled={sending || !valid}>
			{sending ? t('common.sending') : t('entries.request.submit')}
		</button>
	{/snippet}
</Modal>
