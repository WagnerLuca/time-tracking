<script lang="ts">
	/** Small padlock for closed periods; the title is the tooltip and the screen-reader text. */
	let { title, class: className = '', muted = true }: { title: string; class?: string; muted?: boolean } = $props();
</script>

<span class="inline-flex {muted ? 'text-base-content/60' : ''} {className}" {title} data-testid="closed-lock">
	<svg class="w-3 h-3" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" aria-hidden="true"><rect x="5" y="11" width="14" height="10" rx="2" /><path d="M8 11V7a4 4 0 0 1 8 0v4" /></svg>
	<span class="sr-only">{title}</span>
</span>
