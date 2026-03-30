<script lang="ts">
	let { message = 'Loading...', size = 'md' }: { message?: string; size?: 'sm' | 'md' | 'lg' } = $props();
</script>

<div class="flex flex-col items-center justify-center py-12 gap-3 text-base-content/60">
	<span class="loading loading-spinner loading-{size}"></span>
	<p class="m-0">{message}</p>
</div>
