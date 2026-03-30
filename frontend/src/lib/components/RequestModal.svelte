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
		onsubmit: () => void;
		oncancel: () => void;
	} = $props();
</script>

<Modal {open} onclose={oncancel}>
	<h3 class="text-lg font-bold mb-1">{type === 'edit' ? 'Request Entry Edit' : 'Request Pause Edit'}</h3>
	<p class="text-sm text-base-content/60 mb-4">Specify the new values. An admin will review and apply them.</p>

	{#if type === 'edit'}
		<div class="flex flex-col gap-3 mb-3">
			<div>
				<!-- svelte-ignore a11y_label_has_associated_control -->
				<label class="text-xs font-semibold text-base-content/70 mb-1">New Start Time</label>
				<input class="input input-bordered input-sm w-full" type="datetime-local" bind:value={newStart} disabled={sending} />
			</div>
			<div>
				<!-- svelte-ignore a11y_label_has_associated_control -->
				<label class="text-xs font-semibold text-base-content/70 mb-1">New End Time</label>
				<input class="input input-bordered input-sm w-full" type="datetime-local" bind:value={newEnd} disabled={sending} />
			</div>
		</div>
	{:else}
		<div class="flex flex-col gap-3 mb-3">
			<div>
				<!-- svelte-ignore a11y_label_has_associated_control -->
				<label class="text-xs font-semibold text-base-content/70 mb-1">New Pause Duration (minutes)</label>
				<input class="input input-bordered input-sm w-full" type="number" min="0" bind:value={newPause} disabled={sending} />
			</div>
		</div>
	{/if}

	<textarea
		class="textarea textarea-bordered w-full text-sm mb-4"
		bind:value={message}
		placeholder="Optional message for the admin..."
		rows="2"
		disabled={sending}
	></textarea>
	<div class="flex gap-2">
		<button class="btn btn-primary btn-sm" onclick={onsubmit} disabled={sending}>
			{sending ? 'Sending...' : 'Submit Request'}
		</button>
		<button class="btn btn-ghost btn-sm" onclick={oncancel}>Cancel</button>
	</div>
</Modal>
