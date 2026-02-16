<script lang="ts">
	import { onMount } from 'svelte';
	import { apiService } from '$lib/apiService';
	import type { OrganizationResponse } from '$lib/types';

	let organizations = $state<OrganizationResponse[]>([]);
	let loading = $state(true);
	let error = $state('');

	onMount(async () => {
		try {
			organizations = await apiService.get<OrganizationResponse[]>('/api/Organizations');
		} catch (err) {
			error = 'Failed to load organizations.';
			console.error(err);
		} finally {
			loading = false;
		}
	});
</script>

<svelte:head>
	<title>Organizations - Time Tracking</title>
</svelte:head>

<div class="page">
	<div class="page-header">
		<h1>Organizations</h1>
		<a href="/organizations/new" class="btn-primary">+ New Organization</a>
	</div>

	{#if loading}
		<p class="muted">Loading organizations...</p>
	{:else if error}
		<div class="error-msg">{error}</div>
	{:else if organizations.length === 0}
		<div class="empty-state">
			<p>No organizations found.</p>
			<a href="/organizations/new" class="btn-primary">Create the first one</a>
		</div>
	{:else}
		<div class="org-grid">
			{#each organizations as org}
				<a href="/organizations/{org.slug}" class="org-card">
					<div class="org-name">{org.name}</div>
					<div class="org-slug">/{org.slug}</div>
					{#if org.description}
						<div class="org-desc">{org.description}</div>
					{/if}
					<div class="org-footer">
						<span>{org.memberCount} member{org.memberCount !== 1 ? 's' : ''}</span>
						{#if org.website}
							<span class="org-website">{org.website}</span>
						{/if}
					</div>
				</a>
			{/each}
		</div>
	{/if}
</div>

<style>
	.page-header {
		display: flex;
		align-items: center;
		justify-content: space-between;
		margin-bottom: 1.5rem;
	}

	.page-header h1 {
		margin: 0;
		font-size: 1.75rem;
		color: #1a1a2e;
	}

	.btn-primary {
		background: #3b82f6;
		color: white;
		padding: 0.5rem 1.25rem;
		border-radius: 8px;
		text-decoration: none;
		font-size: 0.9375rem;
		font-weight: 600;
		border: none;
		cursor: pointer;
		transition: background 0.15s;
	}

	.btn-primary:hover {
		background: #2563eb;
	}

	.muted {
		color: #9ca3af;
	}

	.error-msg {
		color: #dc2626;
		background: #fef2f2;
		padding: 0.75rem 1rem;
		border-radius: 8px;
		border-left: 3px solid #dc2626;
	}

	.empty-state {
		text-align: center;
		padding: 3rem 1rem;
		background: white;
		border-radius: 12px;
		color: #6b7280;
	}

	.empty-state .btn-primary {
		margin-top: 1rem;
		display: inline-block;
	}

	.org-grid {
		display: grid;
		grid-template-columns: repeat(auto-fill, minmax(320px, 1fr));
		gap: 1rem;
	}

	.org-card {
		background: white;
		border-radius: 12px;
		padding: 1.25rem;
		text-decoration: none;
		color: inherit;
		border: 1px solid #e5e7eb;
		transition: box-shadow 0.15s, border-color 0.15s;
		display: block;
	}

	.org-card:hover {
		box-shadow: 0 4px 12px rgba(0, 0, 0, 0.06);
		border-color: #3b82f6;
	}

	.org-name {
		font-weight: 600;
		font-size: 1.0625rem;
		color: #1a1a2e;
	}

	.org-slug {
		color: #9ca3af;
		font-size: 0.8125rem;
		margin-bottom: 0.5rem;
	}

	.org-desc {
		color: #6b7280;
		font-size: 0.875rem;
		margin-bottom: 0.75rem;
		display: -webkit-box;
		-webkit-line-clamp: 2;
		-webkit-box-orient: vertical;
		overflow: hidden;
	}

	.org-footer {
		display: flex;
		justify-content: space-between;
		font-size: 0.8125rem;
		color: #9ca3af;
		margin-top: 0.5rem;
		padding-top: 0.5rem;
		border-top: 1px solid #f3f4f6;
	}

	.org-website {
		color: #3b82f6;
	}
</style>
