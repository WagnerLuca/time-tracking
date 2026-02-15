<script lang="ts">
	import { onMount } from 'svelte';
	import { auth } from '$lib/stores/auth.svelte';
	import { apiService } from '$lib/apiService';
	import type { UserOrganizationResponse } from '$lib/types';

	let myOrgs = $state<UserOrganizationResponse[]>([]);
	let loading = $state(true);
	let error = $state('');

	onMount(async () => {
		if (!auth.user) return;
		try {
			myOrgs = await apiService.get<UserOrganizationResponse[]>(
				`/api/Organizations/user/${auth.user.id}`
			);
		} catch (err) {
			error = 'Failed to load your organizations.';
			console.error(err);
		} finally {
			loading = false;
		}
	});
</script>

<svelte:head>
	<title>Dashboard - Time Tracking</title>
</svelte:head>

<div class="dashboard">
	<h1>Welcome, {auth.user?.firstName}!</h1>

	<section class="section">
		<div class="section-header">
			<h2>Your Organizations</h2>
			<a href="/organizations/new" class="btn-primary-sm">+ New Organization</a>
		</div>

		{#if loading}
			<p class="muted">Loading...</p>
		{:else if error}
			<div class="error-msg">{error}</div>
		{:else if myOrgs.length === 0}
			<div class="empty-state">
				<p>You're not a member of any organizations yet.</p>
				<a href="/organizations/new" class="btn-primary-sm">Create your first organization</a>
			</div>
		{:else}
			<div class="org-grid">
				{#each myOrgs as org}
					<a href="/organizations/{org.organizationId}" class="org-card">
						<div class="org-name">{org.name}</div>
						{#if org.description}
							<div class="org-desc">{org.description}</div>
						{/if}
						<div class="org-meta">
							<span class="role-badge role-{org.role.toLowerCase()}">{org.role}</span>
							<span class="member-count">{org.memberCount} member{org.memberCount !== 1 ? 's' : ''}</span>
						</div>
					</a>
				{/each}
			</div>
		{/if}
	</section>
</div>

<style>
	.dashboard h1 {
		margin: 0 0 2rem;
		font-size: 1.75rem;
		color: #1a1a2e;
	}

	.section-header {
		display: flex;
		align-items: center;
		justify-content: space-between;
		margin-bottom: 1.25rem;
	}

	.section-header h2 {
		margin: 0;
		font-size: 1.25rem;
		color: #374151;
	}

	.btn-primary-sm {
		background: #3b82f6;
		color: white;
		padding: 0.5rem 1rem;
		border-radius: 8px;
		text-decoration: none;
		font-size: 0.875rem;
		font-weight: 600;
		border: none;
		cursor: pointer;
		transition: background 0.15s;
		display: inline-block;
	}

	.btn-primary-sm:hover {
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

	.empty-state .btn-primary-sm {
		margin-top: 1rem;
	}

	.org-grid {
		display: grid;
		grid-template-columns: repeat(auto-fill, minmax(300px, 1fr));
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
		margin-bottom: 0.375rem;
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

	.org-meta {
		display: flex;
		align-items: center;
		gap: 0.75rem;
	}

	.role-badge {
		font-size: 0.75rem;
		font-weight: 600;
		padding: 0.125rem 0.5rem;
		border-radius: 999px;
		text-transform: uppercase;
		letter-spacing: 0.025em;
	}

	.role-owner {
		background: #fef3c7;
		color: #92400e;
	}

	.role-admin {
		background: #dbeafe;
		color: #1e40af;
	}

	.role-member {
		background: #f3f4f6;
		color: #4b5563;
	}

	.member-count {
		font-size: 0.8125rem;
		color: #9ca3af;
	}
</style>
