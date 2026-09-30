<script lang="ts">
	import { onMount } from 'svelte';
	import { auth } from '$lib/stores/auth.svelte';
	import { orgContext } from '$lib/stores/orgContext.svelte';
	import { toast } from '$lib/stores/toast.svelte';
	import { goto } from '$app/navigation';
	import { organizationsApi, requestsApi } from '$lib/apiClient';
	import type { OrganizationResponse, UserOrganizationResponse } from '$lib/api';
	import { RequestType } from '$lib/api';
	import { extractErrorMessage } from '$lib/utils/errorHandler';
	import { roleLabel } from '$lib/utils/formatters';

	let organizations = $state<OrganizationResponse[]>([]);
	let myOrgs = $state<UserOrganizationResponse[]>([]);
	let myOrgIds = $derived(new Set(myOrgs.map((o) => o.organizationId)));
	let otherOrgs = $derived(organizations.filter((o) => !myOrgIds.has(o.id)));
	let pendingJoinSlugs = $state<Set<string>>(new Set());
	let loading = $state(true);
	let error = $state('');
	let joiningSlug = $state<string | null>(null);

	// Join by slug (for organizations that are not listed or when the user got the slug from an admin)
	let joinSlug = $state('');
	let joinSlugTouched = $state(false);
	const SLUG_PATTERN = /^[a-z0-9]+(?:-[a-z0-9]+)*$/;
	let joinSlugValue = $derived(joinSlug.trim().toLowerCase().replace(/^.*\/organizations\//, '').replace(/\/.*$/, ''));
	let joinSlugInvalid = $derived(joinSlugTouched && !SLUG_PATTERN.test(joinSlugValue));

	async function load() {
		loading = true;
		error = '';
		try {
			const [orgRes, myOrgsRes] = await Promise.all([
				organizationsApi.apiV1OrganizationsGet(),
				organizationsApi.apiV1OrganizationsMineGet()
			]);
			organizations = orgRes.data.items ?? [];
			myOrgs = myOrgsRes.data ?? [];
		} catch (err) {
			error = extractErrorMessage(err, 'Could not load organizations.');
		} finally {
			loading = false;
		}

		try {
			const { data: myRequestsPage } = await requestsApi.apiV1OrganizationsMyRequestsGet(RequestType.NUMBER_0);
			pendingJoinSlugs = new Set(
				(myRequestsPage.items ?? []).filter((r) => r.status === 'Pending' && r.organizationSlug).map((r) => r.organizationSlug!)
			);
		} catch (err) {
			toast.error(err, 'Could not load your pending join requests.');
		}
	}

	onMount(load);

	function openOrg(org: UserOrganizationResponse) {
		orgContext.select(org.organizationId!);
		goto(`/organizations/${org.slug}`);
	}

	async function join(slug: string) {
		joiningSlug = slug;
		try {
			const res = await requestsApi.apiV1OrganizationsSlugRequestsPost(slug, {
				type: RequestType.NUMBER_0, // JoinOrganization
				message: ''
			});
			if (res.data?.status === 'Accepted') {
				toast.success('Joined the organization.');
				if (auth.user?.id) await orgContext.loadOrganizations(auth.user.id);
				const joined = orgContext.organizations.find((o) => o.slug === slug);
				if (joined) orgContext.select(joined.organizationId!);
				goto('/');
			} else {
				toast.info('Request sent. An admin has to approve it.');
				pendingJoinSlugs = new Set([...pendingJoinSlugs, slug]);
				joinSlug = '';
				joinSlugTouched = false;
			}
		} catch (err) {
			toast.error(err, 'Could not join the organization.');
		} finally {
			joiningSlug = null;
		}
	}

	function joinBySlug(e: Event) {
		e.preventDefault();
		joinSlugTouched = true;
		if (!SLUG_PATTERN.test(joinSlugValue)) return;
		if (myOrgs.some((o) => o.slug === joinSlugValue)) {
			toast.info('You are already a member of this organization.');
			return;
		}
		join(joinSlugValue);
	}
</script>

<svelte:head>
	<title>Organizations - Time Tracking</title>
</svelte:head>

<div class="page space-y-8">
	<div class="flex flex-wrap items-center justify-between gap-3">
		<h1 class="text-2xl font-bold">Organizations</h1>
		{#if !loading && myOrgs.length > 0}
			<a href="/organizations/new" class="btn btn-primary btn-sm">+ New organization</a>
		{/if}
	</div>

	{#if loading}
		<div class="flex items-center gap-2 text-base-content/60">
			<span class="loading loading-spinner loading-sm"></span> Loading organizations…
		</div>
	{:else if error}
		<div class="alert alert-error">
			<span>{error}</span>
			<button type="button" class="btn btn-sm" onclick={load}>Try again</button>
		</div>
	{:else}
		{#if myOrgs.length === 0}
			<!-- First-time experience -->
			<section class="card bg-base-100 border border-base-300" aria-labelledby="welcomeHeading">
				<div class="card-body gap-4">
					<div>
						<h2 id="welcomeHeading" class="text-lg font-semibold">Get started with an organization</h2>
						<p class="text-sm text-base-content/60 mt-1">
							Time entries, work schedules, absences and reports belong to an organization — your company, team or
							club. Create your own or join an existing one.
						</p>
					</div>
					<div class="grid grid-cols-1 md:grid-cols-2 gap-4">
						<div class="rounded-xl border border-base-300 p-4 flex flex-col gap-3">
							<h3 class="font-semibold">Create an organization</h3>
							<p class="text-sm text-base-content/60 flex-1">You become its owner and can invite members and configure rules.</p>
							<a href="/organizations/new" class="btn btn-primary btn-sm self-start">Create organization</a>
						</div>
						{@render joinForm()}
					</div>
				</div>
			</section>
		{:else}
			<section class="space-y-3" aria-labelledby="myOrgsHeading">
				<h2 id="myOrgsHeading" class="text-lg font-semibold">My organizations</h2>
				<div class="grid grid-cols-1 sm:grid-cols-[repeat(auto-fill,minmax(280px,1fr))] gap-4">
					{#each myOrgs as org (org.organizationId)}
						<button
							type="button"
							class="text-left bg-base-100 rounded-xl border p-5 transition-all hover:shadow-md hover:border-primary {org.organizationId === orgContext.selectedOrgId ? 'border-primary' : 'border-base-300'}"
							onclick={() => openOrg(org)}
						>
							<div class="flex items-start justify-between gap-2">
								<div class="min-w-0">
									<div class="font-semibold text-base truncate">{org.name}</div>
									<div class="text-base-content/60 text-xs truncate">/{org.slug}</div>
								</div>
								<span class="badge badge-sm badge-ghost shrink-0">{roleLabel(org.role)}</span>
							</div>
							{#if org.description}
								<div class="text-base-content/60 text-sm mt-2 line-clamp-2">{org.description}</div>
							{/if}
							<div class="text-xs text-base-content/60 mt-3 pt-2 border-t border-base-200">
								{org.memberCount} member{org.memberCount !== 1 ? 's' : ''}
								{#if org.organizationId === orgContext.selectedOrgId}
									· <span class="text-primary font-medium">Selected</span>
								{/if}
							</div>
						</button>
					{/each}
				</div>
			</section>
			<section class="max-w-md">
				{@render joinForm()}
			</section>
		{/if}

		{#if otherOrgs.length > 0}
			<section class="space-y-3" aria-labelledby="directoryHeading">
				<h2 id="directoryHeading" class="text-lg font-semibold">Other organizations</h2>
				<div class="grid grid-cols-1 sm:grid-cols-[repeat(auto-fill,minmax(280px,1fr))] gap-4">
					{#each otherOrgs as org (org.id)}
						<div class="flex flex-col bg-base-100 rounded-xl border border-base-300 p-5 gap-2">
							<div class="min-w-0">
								<div class="font-semibold text-base truncate">{org.name}</div>
								<div class="text-base-content/60 text-xs truncate">/{org.slug}</div>
							</div>
							{#if org.description}
								<div class="text-base-content/60 text-sm line-clamp-2">{org.description}</div>
							{/if}
							<div class="flex flex-wrap items-center justify-between gap-2 text-xs text-base-content/60 mt-auto pt-2 border-t border-base-200">
								<span>{org.memberCount} member{org.memberCount !== 1 ? 's' : ''}</span>
								{#if pendingJoinSlugs.has(org.slug!)}
									<span class="badge badge-warning badge-sm">Request pending</span>
								{:else if org.joinPolicy !== 'Disabled'}
									<button
										type="button"
										class="btn btn-primary btn-xs"
										onclick={() => join(org.slug!)}
										disabled={joiningSlug !== null}
									>
										{joiningSlug === org.slug ? 'Joining…' : org.joinPolicy === 'Allowed' ? 'Join' : 'Request to join'}
									</button>
								{:else}
									<span>Invite only</span>
								{/if}
							</div>
						</div>
					{/each}
				</div>
			</section>
		{/if}
	{/if}
</div>

{#snippet joinForm()}
	<form class="rounded-xl border border-base-300 bg-base-100 p-4 flex flex-col gap-3" onsubmit={joinBySlug} novalidate>
		<h3 class="font-semibold">Join an organization</h3>
		<p class="text-sm text-base-content/60">Enter the organization's ID (the part after /organizations/ in its link). Depending on its settings an admin may have to approve.</p>
		<div class="flex flex-col sm:flex-row gap-2">
			<label for="joinSlug" class="sr-only">Organization ID</label>
			<input
				id="joinSlug"
				type="text"
				class="input input-bordered input-sm w-full"
				class:input-error={joinSlugInvalid}
				placeholder="my-company"
				autocomplete="off"
				autocapitalize="none"
				spellcheck="false"
				bind:value={joinSlug}
				onblur={() => (joinSlugTouched = joinSlug.trim() !== '')}
				aria-invalid={joinSlugInvalid}
				aria-describedby={joinSlugInvalid ? 'joinSlugError' : undefined}
				disabled={joiningSlug !== null}
			/>
			<button type="submit" class="btn btn-sm shrink-0" disabled={joiningSlug !== null || !joinSlug.trim()}>
				{joiningSlug !== null && joiningSlug === joinSlugValue ? 'Joining…' : 'Join'}
			</button>
		</div>
		{#if joinSlugInvalid}
			<p id="joinSlugError" class="text-xs text-error">Only lowercase letters, numbers and hyphens.</p>
		{/if}
	</form>
{/snippet}
