<script lang="ts">
	/** "Nothing here yet" with the obvious next step, e.g. "Noch keine Einträge — Timer starten". */
	interface Props {
		text: string;
		actionLabel?: string;
		href?: string;
		onaction?: () => void;
	}
	let { text, actionLabel, href, onaction }: Props = $props();
</script>

<div class="flex flex-col items-center gap-3 rounded-box border border-dashed border-base-300 px-4 py-6 text-center text-sm text-base-content/70" data-testid="empty-state">
	<p class="m-0">{text}</p>
	{#if actionLabel && href}
		<a {href} class="btn btn-primary btn-sm">{actionLabel}</a>
	{:else if actionLabel && onaction}
		<button type="button" class="btn btn-primary btn-sm" onclick={onaction}>{actionLabel}</button>
	{/if}
</div>
