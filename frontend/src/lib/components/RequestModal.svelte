<script lang="ts">
	import Modal from './Modal.svelte';

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

<Modal {open} onclose={oncancel} title={type === 'edit' ? 'Request entry edit' : 'Request pause edit'}>
	<form id="{id}-form" onsubmit={submit} novalidate>
		<p class="text-sm text-base-content/60 mb-4">Enter the new values. An admin will review and apply them.</p>

		{#if error}
			<div role="alert" class="alert alert-error text-sm mb-3 py-2 px-3">{error}</div>
		{/if}

		{#if type === 'edit'}
			<div class="flex flex-col gap-3 mb-3">
				<div class="flex flex-col gap-1">
					<label for="{id}-start" class="text-xs font-semibold text-base-content/70">New start time</label>
					<input id="{id}-start" class="input input-bordered input-sm w-full" type="datetime-local" bind:value={newStart} disabled={sending} />
				</div>
				<div class="flex flex-col gap-1">
					<label for="{id}-end" class="text-xs font-semibold text-base-content/70">New end time</label>
					<input id="{id}-end" class="input input-bordered input-sm w-full {endBeforeStart ? 'input-error' : ''}" type="datetime-local" bind:value={newEnd} min={newStart || undefined} disabled={sending} aria-invalid={endBeforeStart} />
					{#if endBeforeStart}
						<span class="text-xs text-error">End must be after start.</span>
					{/if}
				</div>
			</div>
		{:else}
			<div class="flex flex-col gap-1 mb-3">
				<label for="{id}-pause" class="text-xs font-semibold text-base-content/70">New pause (minutes)</label>
				<input id="{id}-pause" class="input input-bordered input-sm w-full {pauseInvalid ? 'input-error' : ''}" type="number" min="0" step="1" bind:value={newPause} disabled={sending} aria-invalid={pauseInvalid} />
			</div>
		{/if}

		<label for="{id}-message" class="text-xs font-semibold text-base-content/70">Message for the admin (optional)</label>
		<textarea
			id="{id}-message"
			class="textarea textarea-bordered w-full text-sm mt-1"
			bind:value={message}
			rows="2"
			disabled={sending}
		></textarea>
	</form>

	{#snippet actions()}
		<button type="button" class="btn btn-ghost btn-sm" onclick={oncancel} disabled={sending}>Cancel</button>
		<button type="submit" form="{id}-form" class="btn btn-primary btn-sm" disabled={sending || !valid}>
			{sending ? 'Sending…' : 'Submit request'}
		</button>
	{/snippet}
</Modal>
