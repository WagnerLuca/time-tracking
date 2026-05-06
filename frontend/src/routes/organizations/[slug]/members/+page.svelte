<script lang="ts">
	import { onMount } from 'svelte';
	import { page } from '$app/stores';
	import { auth } from '$lib/stores/auth.svelte';
	import { organizationsApi } from '$lib/apiClient';
	import { extractErrorMessage, getErrorStatus } from '$lib/utils/errorHandler';
	import type {
		OrganizationDetailResponse,
		AddMemberRequest,
		MemberTimeOverviewResponse,
		OrganizationRole
	} from '$lib/api';

	let org = $state<OrganizationDetailResponse | null>(null);
	let loading = $state(true);
	let error = $state('');

	let myRole = $derived(
		org?.members?.find((m) => m.id === auth.user?.id)?.role ?? null
	);
	let canEdit = $derived(myRole === 'Owner' || myRole === 'Admin');
	let isOwner = $derived(myRole === 'Owner');

	// Add member
	let showAddMember = $state(false);
	let newMemberRole = $state<OrganizationRole>(0 as OrganizationRole);
	let addMemberError = $state('');
	let addingMember = $state(false);

	// User search for add member dropdown
	let allUsers = $state<Array<{id: number, email: string, firstName: string, lastName: string}>>([]);
	let selectedUserId = $state<number | null>(null);
	let usersLoaded = $state(false);

	// Team overview data (admin)
	let teamOverview = $state<MemberTimeOverviewResponse[]>([]);
	let teamOverviewLoading = $state(false);
	let teamOverviewLoaded = $state(false);

	let orgSlug = $state('');

	onMount(() => {
		orgSlug = $page.params.slug ?? '';
		loadOrg();
	});

	async function loadOrg() {
		loading = true;
		error = '';
		try {
			const { data } = await organizationsApi.apiV1OrganizationsSlugGet(orgSlug);
			org = data;
			loadUsersForDropdown();
			loadTeamOverview();
		} catch (err) {
			if (getErrorStatus(err) === 404) { error = 'Organization not found.'; }
			else { error = 'Failed to load organization.'; }
		} finally {
			loading = false;
		}
	}

	async function reloadOrg() {
		try {
			const { data } = await organizationsApi.apiV1OrganizationsSlugGet(orgSlug);
			org = data;
		} catch {}
	}

	async function loadTeamOverview() {
		if (teamOverviewLoaded || !canEdit || !org?.memberTimeEntryVisibility) return;
		teamOverviewLoading = true;
		try {
			const now = new Date();
			const dayOfWeek = now.getDay() || 7;
			const weekStart = new Date(now);
			weekStart.setDate(now.getDate() - dayOfWeek + 1);
			weekStart.setHours(0, 0, 0, 0);
			const weekEnd = new Date(weekStart);
			weekEnd.setDate(weekStart.getDate() + 6);
			weekEnd.setHours(23, 59, 59, 999);
			const { data } = await organizationsApi.apiV1OrganizationsSlugTimeOverviewGet(orgSlug, weekStart.toISOString(), weekEnd.toISOString());
			teamOverview = data;
			teamOverviewLoaded = true;
		} catch {
			teamOverview = [];
		} finally {
			teamOverviewLoading = false;
		}
	}

	function getMemberOverview(memberId: number): MemberTimeOverviewResponse | null {
		return teamOverview.find(m => m.userId === memberId) ?? null;
	}

	function formatMinutesToHours(minutes?: number | null): string {
		if (!minutes) return '0h';
		const h = Math.floor(Math.abs(minutes) / 60);
		const m = Math.abs(minutes) % 60;
		const sign = minutes < 0 ? '-' : '';
		return m > 0 ? `${sign}${h}h ${m}m` : `${sign}${h}h`;
	}

	async function loadUsersForDropdown() {
		if (usersLoaded) return;
		try {
			const { data: orgData } = await organizationsApi.apiV1OrganizationsSlugGet(orgSlug);
			const users = orgData?.members ?? [];
			allUsers = users.map((u: any) => ({
				id: u.id,
				email: u.email,
				firstName: u.firstName,
				lastName: u.lastName
			}));
			usersLoaded = true;
		} catch {
			allUsers = [];
		}
	}

	function getAvailableUsers() {
		const memberIds = new Set((org?.members ?? []).map(m => m.id));
		return allUsers.filter(u => !memberIds.has(u.id));
	}

	async function addMember(e: Event) {
		e.preventDefault();
		if (!selectedUserId) { addMemberError = 'Select a user.'; return; }
		addMemberError = '';
		addingMember = true;
		try {
			const payload: AddMemberRequest = { userId: selectedUserId, role: newMemberRole };
			await organizationsApi.apiV1OrganizationsSlugMembersPost(orgSlug, payload);
			await reloadOrg();
			usersLoaded = false;
			await loadUsersForDropdown();
			showAddMember = false;
			selectedUserId = null;
			newMemberRole = 0;
		} catch (err) {
			addMemberError = extractErrorMessage(err, 'Failed to add member.');
		} finally {
			addingMember = false;
		}
	}
</script>

<svelte:head>
	<title>Members - {org?.name ?? 'Organization'} - Time Tracking</title>
</svelte:head>

<div class="max-w-5xl mx-auto px-6 pb-6">
	{#if loading}
		<div class="flex items-center gap-3 justify-center py-12 text-base-content/40"><span class="loading loading-spinner loading-sm"></span><span>Loading...</span></div>
	{:else if error}
		<div class="alert alert-error">{error}</div>
	{:else if org}
		<div class="pt-2">
			<p class="text-base-content/50 text-sm mt-2 mb-5 leading-relaxed">Your team at a glance. {#if canEdit}Click a member for details, schedule editing, and absence history.{:else}Click a member to see their profile.{/if}</p>

			<!-- Members -->
			<section class="mt-4">
				<div class="flex items-center justify-between mb-4">
					<h2 class="text-xl text-base-content/70">Members ({(org.members ?? []).length})</h2>
					{#if canEdit}
						<button class="btn btn-primary btn-sm" onclick={() => { showAddMember = !showAddMember; if (showAddMember) loadUsersForDropdown(); }}>
							{showAddMember ? 'Cancel' : '+ Add Member'}
						</button>
					{/if}
				</div>

				{#if showAddMember && canEdit}
					<div class="bg-base-200/50 p-4 rounded-lg mb-4 border border-base-300">
						{#if addMemberError}
							<div class="alert alert-error mb-4 text-sm">{addMemberError}</div>
						{/if}
						<form onsubmit={addMember} class="flex gap-2 items-center flex-wrap">
							<select bind:value={selectedUserId} disabled={addingMember} class="select select-bordered select-sm flex-1 min-w-[200px]">
								<option value={null}>Select a user...</option>
								{#each getAvailableUsers() as user}
									<option value={user.id}>{user.firstName} {user.lastName} ({user.email})</option>
								{/each}
							</select>
							<select bind:value={newMemberRole} disabled={addingMember} class="select select-bordered select-sm">
								<option value={0}>Member</option>
								<option value={1}>Admin</option>
								{#if isOwner}<option value={2}>Owner</option>{/if}
							</select>
							<button type="submit" class="btn btn-primary btn-sm" disabled={addingMember}>
								{addingMember ? 'Adding...' : 'Add'}
							</button>
						</form>
					</div>
				{/if}

				<div class="flex flex-col gap-2">
					{#each (org.members ?? []) as member}
						{@const overview = getMemberOverview(member.id!)}
						<a
							class="group flex items-center gap-4 p-3.5 bg-base-100 border border-base-300 rounded-xl cursor-pointer transition-all hover:border-primary/30 hover:shadow-md hover:-translate-y-px no-underline text-base-content"
							href="/organizations/{orgSlug}/members/{member.id}"
							title="View {member.firstName}'s details"
						>
							<div class="w-10 h-10 rounded-full bg-gradient-to-br from-primary to-secondary text-primary-content flex items-center justify-center text-sm font-semibold shrink-0">
								{(member.firstName?.[0] ?? '').toUpperCase()}{(member.lastName?.[0] ?? '').toUpperCase()}
							</div>
							<div class="flex-1 min-w-0">
								<div class="font-semibold text-base-content text-[0.9375rem] flex items-center gap-1.5">
									{member.firstName} {member.lastName}
									{#if member.id === auth.user?.id}
										<span class="badge badge-info badge-xs font-semibold">You</span>
									{/if}
								</div>
								<div class="text-sm text-base-content/40 mt-0.5">{member.email}</div>
								{#if canEdit && org.memberTimeEntryVisibility && overview}
									<div class="flex gap-3 mt-1.5 flex-wrap">
										<span class="flex items-center gap-1 text-xs text-base-content/50" title="Tracked this week">
											<svg viewBox="0 0 16 16" fill="currentColor" class="w-3.5 h-3.5 opacity-60"><path d="M8 3.5a.5.5 0 00-1 0V8a.5.5 0 00.252.434l3.5 2a.5.5 0 00.496-.868L8 7.71V3.5z"/><path d="M8 16A8 8 0 108 0a8 8 0 000 16zm7-8A7 7 0 111 8a7 7 0 0114 0z"/></svg>
											{formatMinutesToHours(overview.netTrackedMinutes)}
										</span>
										<span class="flex items-center gap-1 text-xs text-base-content/50" title="Entries this week">
											<svg viewBox="0 0 16 16" fill="currentColor" class="w-3.5 h-3.5 opacity-60"><path d="M5 3.5h6A1.5 1.5 0 0112.5 5v6a1.5 1.5 0 01-1.5 1.5H5A1.5 1.5 0 013.5 11V5A1.5 1.5 0 015 3.5z"/></svg>
											{overview.entryCount ?? 0} entries
										</span>
										{#if overview.weeklyWorkHours}
											<span class="flex items-center gap-1 text-xs text-base-content/50" title="Weekly target">
												<svg viewBox="0 0 16 16" fill="currentColor" class="w-3.5 h-3.5 opacity-60"><path d="M8 15A7 7 0 118 1a7 7 0 010 14zm0 1A8 8 0 108 0a8 8 0 000 16z"/><path d="M10.97 4.97a.235.235 0 00-.02.022L7.477 9.417 5.384 7.323a.75.75 0 00-1.06 1.06L6.97 11.03a.75.75 0 001.079-.02l3.992-4.99a.75.75 0 00-1.071-1.05z"/></svg>
												{overview.weeklyWorkHours}h/w
											</span>
										{/if}
									</div>
								{/if}
							</div>
							<div class="flex items-center gap-2 shrink-0">
								{#if (member.vacationDaysPerYear ?? 0) > 0}
									<span class="badge badge-sm badge-outline" title="Vacation: {member.vacationDaysUsed ?? 0}/{member.vacationDaysPerYear} used">
										🏖 {member.vacationDaysRemaining ?? member.vacationDaysPerYear}d left
									</span>
								{/if}
								<span class="badge badge-sm uppercase tracking-wide {(member.role?.toLowerCase() ?? 'member') === 'owner' ? 'badge-warning' : (member.role?.toLowerCase() ?? 'member') === 'admin' ? 'badge-info' : 'badge-ghost'}">{member.role}</span>
								<svg class="w-5 h-5 text-base-content/20 group-hover:text-primary transition-colors" viewBox="0 0 20 20" fill="currentColor"><path fill-rule="evenodd" d="M7.21 14.77a.75.75 0 01.02-1.06L11.168 10 7.23 6.29a.75.75 0 111.04-1.08l4.5 4.25a.75.75 0 010 1.08l-4.5 4.25a.75.75 0 01-1.06-.02z" clip-rule="evenodd"/></svg>
							</div>
						</a>
					{/each}
				</div>
			</section>
		</div>
	{/if}
</div>
