<script lang="ts">
	import { t } from '$lib/i18n';

	let {
		startTime = $bindable(''),
		endTime = $bindable(''),
		description = $bindable(''),
		pause = $bindable(0),
		error = '',
		saving = false,
		canEditPause = false,
		requireEnd = false,
		title = '',
		hint = '',
		onsave,
		oncancel
	}: {
		startTime: string;
		endTime: string;
		description: string;
		/** null = empty field: the organization's pause rules decide (new entries). */
		pause: number | null;
		error: string;
		saving: boolean;
		/** Show the pause field (the caller resolves the effective permission). */
		canEditPause: boolean;
		/** New entries need an end time. */
		requireEnd?: boolean;
		/** Heading above the fields (new entries). */
		title?: string;
		/** Info line above the fields (e.g. "needs approval"). */
		hint?: string;
		onsave: () => void;
		oncancel: () => void;
	} = $props();

	const id = $props.id();

	const startMissing = $derived(!startTime);
	const endMissing = $derived(requireEnd && !endTime);
	const endBeforeStart = $derived(!!startTime && !!endTime && new Date(endTime) <= new Date(startTime));
	const pauseInvalid = $derived(canEditPause && pause != null && (Number(pause) < 0 || !Number.isFinite(Number(pause))));
	const valid = $derived(!startMissing && !endMissing && !endBeforeStart && !pauseInvalid);

	function submit(e: SubmitEvent) {
		e.preventDefault();
		if (valid && !saving) onsave();
	}
</script>

<form class="p-3 bg-base-200/50" onsubmit={submit} novalidate aria-label={title || undefined}>
	{#if title}<h4 class="text-sm font-semibold m-0 mb-2">{title}</h4>{/if}
	{#if hint}<p class="text-xs text-base-content/60 m-0 mb-2">{hint}</p>{/if}
	{#if error}
		<div role="alert" class="alert alert-error text-sm mb-3 py-2 px-3">{error}</div>
	{/if}
	<div class="flex gap-3 flex-wrap mb-3 max-w-full">
		<div class="flex flex-col gap-1 max-sm:w-full">
			<label for="{id}-start" class="text-xs font-semibold text-base-content/60 uppercase tracking-wide">{t('entries.start')}</label>
			<input id="{id}-start" class="input input-bordered input-sm max-sm:w-full {startMissing ? 'input-error' : ''}" type="datetime-local" bind:value={startTime} disabled={saving} required aria-invalid={startMissing} />
		</div>
		<div class="flex flex-col gap-1 max-sm:w-full">
			<label for="{id}-end" class="text-xs font-semibold text-base-content/60 uppercase tracking-wide">{t('entries.end')}</label>
			<input id="{id}-end" class="input input-bordered input-sm max-sm:w-full {endBeforeStart || endMissing ? 'input-error' : ''}" type="datetime-local" bind:value={endTime} min={startTime || undefined} disabled={saving} required={requireEnd} aria-invalid={endBeforeStart || endMissing} aria-describedby={endBeforeStart ? `${id}-end-error` : undefined} />
			{#if endBeforeStart}
				<span id="{id}-end-error" class="text-xs text-error">{t('entries.endAfterStart')}</span>
			{/if}
		</div>
		<div class="flex flex-col gap-1 flex-1 min-w-[150px]">
			<label for="{id}-note" class="text-xs font-semibold text-base-content/60 uppercase tracking-wide">{t('entries.note')}</label>
			<input id="{id}-note" class="input input-bordered input-sm w-full" type="text" bind:value={description} placeholder={t('entries.notePlaceholder')} disabled={saving} />
		</div>
		{#if canEditPause}
			<div class="flex flex-col gap-1">
				<label for="{id}-pause" class="text-xs font-semibold text-base-content/60 uppercase tracking-wide">{t('entries.pauseMinutes')}</label>
				<input id="{id}-pause" class="input input-bordered input-sm w-28 {pauseInvalid ? 'input-error' : ''}" type="number" min="0" step="1" bind:value={pause} placeholder={t('entries.pauseAuto')} title={pause == null ? t('entries.pauseAutoHint') : undefined} disabled={saving} aria-invalid={pauseInvalid} />
			</div>
		{/if}
	</div>
	<div class="flex gap-2">
		<button type="submit" class="btn btn-primary btn-sm" disabled={saving || !valid}>
			{saving ? t('common.saving') : t('common.save')}
		</button>
		<button type="button" class="btn btn-ghost btn-sm" onclick={oncancel} disabled={saving}>{t('common.cancel')}</button>
	</div>
</form>
