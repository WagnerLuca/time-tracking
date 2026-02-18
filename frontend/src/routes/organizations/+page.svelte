<script lang="ts">
	import { onMount } from 'svelte';
	import { auth } from '$lib/stores/auth.svelte';
	import { orgContext } from '$lib/stores/orgContext.svelte';
	import { goto } from '$app/navigation';
	import { organizationsApi, requestsApi } from '$lib/apiClient';
	import type { OrganizationResponse } from '$lib/api';
	import { RequestType } from '$lib/api';

	let organizations = $state<OrganizationResponse[]>([]);
	let myOrgIds = $state<Set<number>>(new Set());
	let pendingJoinSlugs = $state<Set<string>>(new Set());
	let loading = $state(true);
	let error = $state('');
	let joiningSlugs = $state<Set<string>>(new Set());
	let joinMessages = $state<Record<string, { type: 'success' | 'error'; text: string }>>({});

	onMount(async () => {
		try {
			const [orgRes, myOrgsRes] = await Promise.all([
				organizationsApi.apiOrganizationsGet(),
				auth.user?.id
					? organizationsApi.apiOrganizationsUserUserIdGet(auth.user.id)
					: Promise.resolve({ data: [] })
			]);
			organizations = orgRes.data;
			myOrgIds = new Set((myOrgsRes.data ?? []).map((o: any) => o.organizationId));

			// Load pending join requests
			try {
				const { data: myRequests } = await requestsApi.apiOrganizationsMyRequestsGet(0 as RequestType);
				for (const r of myRequests ?? []) {
					if (r.status === 'Pending' && r.organizationSlug) {
						pendingJoinSlugs.add(r.organizationSlug);
					}
				}
				pendingJoinSlugs = new Set(pendingJoinSlugs);
			} catch { /* ignore if endpoint fails */ }
		} catch (err) {
			error = 'Failed to load organizations.';
			console.error(err);
		} finally {
			loading = false;
		}
	});

	async function joinOrg(org: OrganizationResponse) {
		if (!org.slug) return;
		joiningSlugs = new Set([...joiningSlugs, org.slug]);
		try {
			const res = await requestsApi.apiOrganizationsSlugRequestsPost(org.slug, {
				type: 0 as RequestType, // JoinOrganization
				message: ''
			});
			const status = res.data?.status;
			if (status === 'Accepted') {
				joinMessages = { ...joinMessages, [org.slug]: { type: 'success', text: 'Joined!' } };
				myOrgIds = new Set([...myOrgIds, org.id!]);
				// Auto-select the joined org and redirect to dashboard
				if (auth.user?.id) {
					await orgContext.loadOrganizations(auth.user.id);
				}
				orgContext.select(org.id!);
				goto('/');
			} else {
				joinMessages = { ...joinMessages, [org.slug]: { type: 'success', text: 'Request sent! Waiting for admin approval.' } };
				pendingJoinSlugs = new Set([...pendingJoinSlugs, org.slug]);
			}
		} catch (err: any) {
			const msg = err.response?.data?.message || 'Failed to join.';
			joinMessages = { ...joinMessages, [org.slug!]: { type: 'error', text: msg } };
		} finally {
			joiningSlugs = new Set([...joiningSlugs].filter(s => s !== org.slug));
		}
	}
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
				<div class="org-card-wrapper">
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
					<div class="org-card-actions">
						{#if myOrgIds.has(org.id!)}
							<span class="member-badge">Member</span>
						{:else if pendingJoinSlugs.has(org.slug!)}
							<span class="pending-badge">Request Pending</span>
						{:else if org.joinPolicy !== 'Disabled'}
							<button
								class="btn-join"
								onclick={(e) => { e.preventDefault(); joinOrg(org); }}
								disabled={joiningSlugs.has(org.slug!)}
							>
								{joiningSlugs.has(org.slug!) ? 'Joining...' : org.joinPolicy === 'Allowed' ? 'Join' : 'Request to Join'}
							</button>
						{/if}
						{#if joinMessages[org.slug!]}
							<span class="join-msg {joinMessages[org.slug!].type}">{joinMessages[org.slug!].text}</span>
						{/if}
					</div>
				</div>
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

	.org-card-wrapper {
		display: flex;
		flex-direction: column;
		background: white;
		border-radius: 12px;
		border: 1px solid #e5e7eb;
		overflow: hidden;
		transition: box-shadow 0.15s, border-color 0.15s;
	}

	.org-card-wrapper:hover {
		box-shadow: 0 4px 12px rgba(0, 0, 0, 0.06);
		border-color: #3b82f6;
	}

	.org-card-wrapper .org-card {
		background: none;
		border: none;
		border-radius: 0;
	}

	.org-card-wrapper .org-card:hover {
		box-shadow: none;
		border-color: transparent;
	}

	.org-card-actions {
		padding: 0 1.25rem 1rem;
		display: flex;
		align-items: center;
		gap: 0.5rem;
		flex-wrap: wrap;
	}

	.btn-join {
		background: #3b82f6;
		color: white;
		border: none;
		padding: 0.35rem 0.85rem;
		border-radius: 6px;
		font-size: 0.8125rem;
		font-weight: 600;
		cursor: pointer;
		transition: background 0.15s;
	}

	.btn-join:hover { background: #2563eb; }
	.btn-join:disabled { opacity: 0.5; cursor: not-allowed; }

	.member-badge {
		background: #dcfce7;
		color: #16a34a;
		padding: 0.25rem 0.65rem;
		border-radius: 6px;
		font-size: 0.8rem;
		font-weight: 600;
	}

	.pending-badge {
		background: #fef3c7;
		color: #d97706;
		padding: 0.25rem 0.65rem;
		border-radius: 6px;
		font-size: 0.8rem;
		font-weight: 600;
	}

	.join-msg {
		font-size: 0.8rem;
	}

	.join-msg.success { color: #16a34a; }
	.join-msg.error { color: #dc2626; }
</style>
