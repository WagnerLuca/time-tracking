<script lang="ts">
	import type { Snippet } from 'svelte';

	let {
		open = false,
		onclose,
		children,
		maxWidth = '420px'
	}: {
		open: boolean;
		onclose: () => void;
		children: Snippet;
		maxWidth?: string;
	} = $props();
</script>

{#if open}
	<!-- svelte-ignore a11y_no_static_element_interactions -->
	<!-- svelte-ignore a11y_click_events_have_key_events -->
	<div class="fixed inset-0 bg-black/30 z-[100]" onclick={onclose}></div>
	<div
		class="fixed top-1/2 left-1/2 -translate-x-1/2 -translate-y-1/2 bg-base-100 rounded-xl p-6 w-[90%] shadow-2xl z-[101]"
		style="max-width: {maxWidth}"
	>
		{@render children()}
	</div>
{/if}
