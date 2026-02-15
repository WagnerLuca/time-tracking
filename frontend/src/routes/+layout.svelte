<script lang="ts">
	import { auth } from '$lib/stores/auth.svelte';
	import { page } from '$app/stores';
	import { goto } from '$app/navigation';
	import { onMount } from 'svelte';
	import favicon from '$lib/assets/favicon.svg';

	let { children } = $props();

	const publicRoutes = ['/login', '/register', '/health'];

	onMount(() => {
		auth.init();
	});

	$effect(() => {
		if (!auth.loading && !auth.isAuthenticated) {
			const path = window.location.pathname;
			if (!publicRoutes.some((r) => path.startsWith(r))) {
				goto('/login');
			}
		}
	});

	async function handleLogout() {
		await auth.logout();
		goto('/login');
	}
</script>

<svelte:head>
	<link rel="icon" href={favicon} />
</svelte:head>

{#if auth.loading}
	<div class="loading-screen">
		<p>Loading...</p>
	</div>
{:else if auth.isAuthenticated}
	<nav class="top-nav">
		<div class="nav-left">
			<a href="/" class="nav-brand">Time Tracking</a>
			<a href="/organizations" class="nav-link">Organizations</a>
		</div>
		<div class="nav-right">
			<span class="nav-user">{auth.user?.firstName} {auth.user?.lastName}</span>
			<button class="btn-logout" onclick={handleLogout}>Sign Out</button>
		</div>
	</nav>
	<main class="app-main">
		{@render children?.()}
	</main>
{:else}
	{@render children?.()}
{/if}

<style>
	:global(body) {
		margin: 0;
		padding: 0;
		font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
		background: #f0f2f5;
		color: #1a1a2e;
	}

	.loading-screen {
		min-height: 100vh;
		display: flex;
		align-items: center;
		justify-content: center;
		color: #6b7280;
	}

	.top-nav {
		background: white;
		border-bottom: 1px solid #e5e7eb;
		padding: 0 1.5rem;
		height: 56px;
		display: flex;
		align-items: center;
		justify-content: space-between;
		position: sticky;
		top: 0;
		z-index: 100;
	}

	.nav-left {
		display: flex;
		align-items: center;
		gap: 1.5rem;
	}

	.nav-brand {
		font-weight: 700;
		font-size: 1.125rem;
		color: #3b82f6;
		text-decoration: none;
	}

	.nav-link {
		color: #4b5563;
		text-decoration: none;
		font-size: 0.9375rem;
		font-weight: 500;
		padding: 0.25rem 0;
		border-bottom: 2px solid transparent;
		transition: color 0.15s, border-color 0.15s;
	}

	.nav-link:hover {
		color: #1a1a2e;
		border-bottom-color: #3b82f6;
	}

	.nav-right {
		display: flex;
		align-items: center;
		gap: 1rem;
	}

	.nav-user {
		color: #6b7280;
		font-size: 0.875rem;
	}

	.btn-logout {
		background: none;
		border: 1px solid #d1d5db;
		color: #4b5563;
		padding: 0.375rem 0.75rem;
		border-radius: 6px;
		font-size: 0.8125rem;
		cursor: pointer;
		transition: background 0.15s, color 0.15s;
	}

	.btn-logout:hover {
		background: #f3f4f6;
		color: #1a1a2e;
	}

	.app-main {
		max-width: 1100px;
		margin: 0 auto;
		padding: 2rem 1.5rem;
	}
</style>

