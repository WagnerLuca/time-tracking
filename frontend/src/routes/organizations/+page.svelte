<script lang="ts">
	import { onMount } from 'svelte';
	import { auth } from '$lib/stores/auth.svelte';
	import { orgContext } from '$lib/stores/orgContext.svelte';
	import { goto } from '$app/navigation';
	import { organizationsApi, requestsApi } from '$lib/apiClient';
	import type { OrganizationResponse } from '$lib/api';
	import { RequestType } from '$lib/api';
	import { extractErrorMessage } from '$lib/utils/errorHandler';

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
				organizationsApi.apiV1OrganizationsGet(),
				auth.user
					? organizationsApi.apiV1OrganizationsMineGet()
					: Promise.resolve({ data: [] as any[] })
			]);
			organizations = orgRes.data.items ?? [];
			myOrgIds = new Set((myOrgsRes.data ?? []).map((o: any) => o.organizationId));

			// Load pending join requests
			try {
				const { data: myRequestsPage } = await requestsApi.apiV1OrganizationsMyRequestsGet(0 as RequestType);
				for (const r of myRequestsPage.items ?? []) {
					if (r.status === 'Pending' && r.organizationSlug) {
						pendingJoinSlugs.add(r.organizationSlug);
					}
				}
				pendingJoinSlugs = new Set(pendingJoinSlugs);
			} catch { /* ignore if endpoint fails */ }
		} catch (err) {
			error = extractErrorMessage(err, 'Failed to load organizations.');
			console.error(err);
		} finally {
			loading = false;
		}
	});

	async function joinOrg(org: OrganizationResponse) {
		if (!org.slug) return;
		joiningSlugs = new Set([...joiningSlugs, org.slug]);
		try {
			const res = await requestsApi.apiV1OrganizationsSlugRequestsPost(org.slug, {
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
		} catch (err) {
			const msg = extractErrorMessage(err, 'Failed to join.');
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
	<div class="flex items-center justify-between mb-6">
		<h1 class="text-2xl font-bold">Organizations</h1>
		<a href="/organizations/new" class="btn btn-primary btn-sm">+ New Organization</a>
	</div>

	{#if loading}
		<p class="text-base-content/50">Loading organizations...</p>
	{:else if error}
		<div class="alert alert-error">{error}</div>
	{:else if organizations.length === 0}
		<div class="text-center py-12 px-4 bg-base-100 rounded-xl text-base-content/60">
			<p>No organizations found.</p>
			<a href="/organizations/new" class="btn btn-primary mt-4 inline-block">Create the first one</a>
		</div>
	{:else}
		<div class="grid grid-cols-[repeat(auto-fill,minmax(320px,1fr))] gap-4">
			{#each organizations as org}
				<div class="flex flex-col bg-base-100 rounded-xl border border-base-300 overflow-hidden transition-all hover:shadow-md hover:border-primary">
					<a href="/organizations/{org.slug}" class="block p-5 no-underline text-inherit">
						<div class="font-semibold text-base">{org.name}</div>
						<div class="text-base-content/50 text-xs mb-2">/{org.slug}</div>
						{#if org.description}
							<div class="text-base-content/60 text-sm mb-3 line-clamp-2">{org.description}</div>
						{/if}
						<div class="flex justify-between text-xs text-base-content/50 mt-2 pt-2 border-t border-base-200">
							<span>{org.memberCount} member{org.memberCount !== 1 ? 's' : ''}</span>
							{#if org.website}
								<span class="text-primary">{org.website}</span>
							{/if}
						</div>
					</a>
					<div class="px-5 pb-4 flex items-center gap-2 flex-wrap">
						{#if myOrgIds.has(org.id!)}
							<span class="badge badge-success">Member</span>
						{:else if pendingJoinSlugs.has(org.slug!)}
							<span class="badge badge-warning">Request Pending</span>
						{:else if org.joinPolicy !== 'Disabled'}
							<button
								class="btn btn-primary btn-xs"
								onclick={(e) => { e.preventDefault(); joinOrg(org); }}
								disabled={joiningSlugs.has(org.slug!)}
							>
								{joiningSlugs.has(org.slug!) ? 'Joining...' : org.joinPolicy === 'Allowed' ? 'Join' : 'Request to Join'}
							</button>
						{/if}
						{#if joinMessages[org.slug!]}
							<span class={"text-xs " + (joinMessages[org.slug!].type === 'success' ? "text-success" : "text-error")}>{joinMessages[org.slug!].text}</span>
						{/if}
					</div>
				</div>
			{/each}
		</div>
	{/if}
</div>

