<script lang="ts">
	let {
		startTime = $bindable(''),
		endTime = $bindable(''),
		description = $bindable(''),
		pause = $bindable(0),
		error = '',
		saving = false,
		canEditPause = false,
		onsave,
		oncancel
	}: {
		startTime: string;
		endTime: string;
		description: string;
		pause: number;
		error: string;
		saving: boolean;
		/** Show the pause field (the caller resolves the effective permission). */
		canEditPause: boolean;
		onsave: () => void;
		oncancel: () => void;
	} = $props();

	const id = $props.id();

	const startMissing = $derived(!startTime);
	const endBeforeStart = $derived(!!startTime && !!endTime && new Date(endTime) <= new Date(startTime));
	const pauseInvalid = $derived(canEditPause && (Number(pause) < 0 || !Number.isFinite(Number(pause))));
	const valid = $derived(!startMissing && !endBeforeStart && !pauseInvalid);

	function submit(e: SubmitEvent) {
		e.preventDefault();
		if (valid && !saving) onsave();
	}
</script>

<form class="p-3 bg-base-200/50" onsubmit={submit} novalidate>
	{#if error}
		<div role="alert" class="alert alert-error text-sm mb-3 py-2 px-3">{error}</div>
	{/if}
	<div class="flex gap-3 flex-wrap mb-3 max-w-full">
		<div class="flex flex-col gap-1 max-sm:w-full">
			<label for="{id}-start" class="text-xs font-semibold text-base-content/60 uppercase tracking-wide">Start</label>
			<input id="{id}-start" class="input input-bordered input-sm max-sm:w-full {startMissing ? 'input-error' : ''}" type="datetime-local" bind:value={startTime} disabled={saving} required aria-invalid={startMissing} />
		</div>
		<div class="flex flex-col gap-1 max-sm:w-full">
			<label for="{id}-end" class="text-xs font-semibold text-base-content/60 uppercase tracking-wide">End</label>
			<input id="{id}-end" class="input input-bordered input-sm max-sm:w-full {endBeforeStart ? 'input-error' : ''}" type="datetime-local" bind:value={endTime} min={startTime || undefined} disabled={saving} aria-invalid={endBeforeStart} aria-describedby={endBeforeStart ? `${id}-end-error` : undefined} />
			{#if endBeforeStart}
				<span id="{id}-end-error" class="text-xs text-error">End must be after start.</span>
			{/if}
		</div>
		<div class="flex flex-col gap-1 flex-1 min-w-[150px]">
			<label for="{id}-note" class="text-xs font-semibold text-base-content/60 uppercase tracking-wide">Note</label>
			<input id="{id}-note" class="input input-bordered input-sm w-full" type="text" bind:value={description} placeholder="Optional note" disabled={saving} />
		</div>
		{#if canEditPause}
			<div class="flex flex-col gap-1">
				<label for="{id}-pause" class="text-xs font-semibold text-base-content/60 uppercase tracking-wide">Pause (min)</label>
				<input id="{id}-pause" class="input input-bordered input-sm w-24 {pauseInvalid ? 'input-error' : ''}" type="number" min="0" step="1" bind:value={pause} disabled={saving} aria-invalid={pauseInvalid} />
			</div>
		{/if}
	</div>
	<div class="flex gap-2">
		<button type="submit" class="btn btn-primary btn-sm" disabled={saving || !valid}>
			{saving ? 'Saving…' : 'Save'}
		</button>
		<button type="button" class="btn btn-ghost btn-sm" onclick={oncancel} disabled={saving}>Cancel</button>
	</div>
</form>
