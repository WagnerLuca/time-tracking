<script lang="ts">
	import { auth } from '$lib/stores/auth.svelte';
	import { orgContext } from '$lib/stores/orgContext.svelte';
	import { goto } from '$app/navigation';

	function selectOrg(orgId: number) {
		orgContext.select(orgId);
		goto('/');
	}

	function continuePersonal() {
		orgContext.select(null);
		goto('/');
	}

	// If already selected, redirect to dashboard
	$effect(() => {
		if (!orgContext.loading && orgContext.selectedOrgId) {
			goto('/');
		}
	});
</script>

<svelte:head>
	<title>Select Organization - Time Tracking</title>
</svelte:head>

<div class="select-org-page">
	<div class="select-org-card">
		<h1>Choose Organization</h1>
		<p class="subtitle">Select which organization you'd like to work with</p>

		{#if orgContext.loading}
			<p class="loading">Loading organizations...</p>
		{:else if orgContext.organizations.length === 0}
			<p class="empty">You're not a member of any organization yet.</p>
			<button class="btn-primary" onclick={continuePersonal}>Continue as Personal</button>
		{:else}
			<div class="org-list">
				{#each orgContext.organizations as org}
					<button class="org-option" onclick={() => selectOrg(org.organizationId!)}>
						<div class="org-option-info">
							<span class="org-option-name">{org.name}</span>
							<span class="org-option-role">{org.role}</span>
						</div>
						<span class="org-option-arrow">→</span>
					</button>
				{/each}
			</div>
			<button class="btn-personal" onclick={continuePersonal}>
				Continue without organization
			</button>
		{/if}
	</div>
</div>

<style>
	.select-org-page {
		min-height: 80vh;
		display: flex;
		align-items: center;
		justify-content: center;
		padding: 2rem;
	}

	.select-org-card {
		background: white;
		border-radius: 16px;
		padding: 2.5rem;
		width: 100%;
		max-width: 480px;
		box-shadow: 0 4px 20px rgba(0, 0, 0, 0.08);
		text-align: center;
	}

	h1 {
		margin: 0 0 0.5rem;
		font-size: 1.5rem;
		color: #1a1a2e;
	}

	.subtitle {
		color: #6b7280;
		font-size: 0.9375rem;
		margin: 0 0 2rem;
	}

	.loading, .empty {
		color: #9ca3af;
		font-size: 0.9375rem;
		margin: 1rem 0;
	}

	.org-list {
		display: flex;
		flex-direction: column;
		gap: 0.5rem;
		margin-bottom: 1.5rem;
	}

	.org-option {
		display: flex;
		align-items: center;
		justify-content: space-between;
		padding: 1rem 1.25rem;
		background: #f9fafb;
		border: 2px solid #e5e7eb;
		border-radius: 12px;
		cursor: pointer;
		transition: all 0.15s;
		text-align: left;
		width: 100%;
	}

	.org-option:hover {
		border-color: #3b82f6;
		background: #eff6ff;
	}

	.org-option-info {
		display: flex;
		flex-direction: column;
		gap: 0.125rem;
	}

	.org-option-name {
		font-weight: 600;
		color: #1a1a2e;
		font-size: 1rem;
	}

	.org-option-role {
		font-size: 0.75rem;
		color: #6b7280;
		text-transform: uppercase;
		font-weight: 500;
		letter-spacing: 0.05em;
	}

	.org-option-arrow {
		font-size: 1.25rem;
		color: #9ca3af;
		transition: color 0.15s;
	}

	.org-option:hover .org-option-arrow {
		color: #3b82f6;
	}

	.btn-primary {
		padding: 0.75rem 2rem;
		background: #3b82f6;
		color: white;
		border: none;
		border-radius: 10px;
		font-size: 1rem;
		font-weight: 600;
		cursor: pointer;
		transition: background 0.15s;
	}

	.btn-primary:hover {
		background: #2563eb;
	}

	.btn-personal {
		background: none;
		border: none;
		color: #6b7280;
		font-size: 0.875rem;
		cursor: pointer;
		text-decoration: underline;
		transition: color 0.15s;
	}

	.btn-personal:hover {
		color: #374151;
	}
</style>
