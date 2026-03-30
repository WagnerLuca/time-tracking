<script lang="ts">
	import type { OrganizationDetailResponse } from '$lib/api';
	import { canEditPause } from '$lib/utils/orgRules';

	let {
		startTime = $bindable(''),
		endTime = $bindable(''),
		description = $bindable(''),
		pause = $bindable(0),
		error = '',
		saving = false,
		orgDetail = null,
		onsave,
		oncancel
	}: {
		startTime: string;
		endTime: string;
		description: string;
		pause: number;
		error: string;
		saving: boolean;
		orgDetail: OrganizationDetailResponse | null;
		onsave: () => void;
		oncancel: () => void;
	} = $props();
</script>

<div class="p-3 bg-base-200/50">
	{#if error}
		<div class="alert alert-error text-sm mb-3 py-2 px-3">{error}</div>
	{/if}
	<div class="flex gap-3 flex-wrap mb-3 max-w-full overflow-hidden">
		<div class="flex flex-col gap-1">
			<!-- svelte-ignore a11y_label_has_associated_control -->
			<label class="text-xs font-semibold text-base-content/60 uppercase tracking-wide">Start</label>
			<input class="input input-bordered input-sm" type="datetime-local" bind:value={startTime} disabled={saving} />
		</div>
		<div class="flex flex-col gap-1">
			<!-- svelte-ignore a11y_label_has_associated_control -->
			<label class="text-xs font-semibold text-base-content/60 uppercase tracking-wide">End</label>
			<input class="input input-bordered input-sm" type="datetime-local" bind:value={endTime} disabled={saving} />
		</div>
		<div class="flex flex-col gap-1 flex-1 min-w-[150px]">
			<!-- svelte-ignore a11y_label_has_associated_control -->
			<label class="text-xs font-semibold text-base-content/60 uppercase tracking-wide">Note</label>
			<input class="input input-bordered input-sm w-full" type="text" bind:value={description} placeholder="Optional note" disabled={saving} />
		</div>
		{#if canEditPause(orgDetail)}
			<div class="flex flex-col gap-1">
				<!-- svelte-ignore a11y_label_has_associated_control -->
				<label class="text-xs font-semibold text-base-content/60 uppercase tracking-wide">Pause (min)</label>
				<input class="input input-bordered input-sm w-20" type="number" min="0" bind:value={pause} disabled={saving} />
			</div>
		{/if}
	</div>
	<div class="flex gap-2">
		<button class="btn btn-primary btn-sm" onclick={onsave} disabled={saving}>
			{saving ? 'Saving...' : 'Save'}
		</button>
		<button class="btn btn-ghost btn-sm" onclick={oncancel}>Cancel</button>
	</div>
</div>
