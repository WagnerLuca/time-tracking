<script lang="ts">
	import { onMount } from 'svelte';

	let iframeElement: HTMLIFrameElement;

	onMount(() => {
	});
</script>

<svelte:head>
	<title>API Documentation</title>
</svelte:head>

<div class="docs-container">
	<h1>API Documentation</h1>
	<iframe
		bind:this={iframeElement}
		src="/api-docs/index.html"
		title="API Documentation"
		class="docs-iframe"
	></iframe>
</div>

<style>
	.docs-container {
		width: 100%;
		height: 100vh;
		display: flex;
		flex-direction: column;
		padding: 1rem;
		box-sizing: border-box;
	}

	h1 {
		margin: 0 0 1rem 0;
	}

	.docs-iframe {
		flex: 1;
		width: 100%;
		border: 1px solid #ddd;
		border-radius: 4px;
	}
</style>
