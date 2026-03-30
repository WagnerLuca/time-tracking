<script lang="ts">
	import '../app.css';
	import { auth } from '$lib/stores/auth.svelte';
	import { orgContext } from '$lib/stores/orgContext.svelte';
	import { page } from '$app/stores';
	import { goto } from '$app/navigation';
	import { onMount } from 'svelte';
	import favicon from '$lib/assets/favicon.svg';
	import { requestsApi, organizationsApi } from '$lib/apiClient';
	import type { OrgRequestResponse } from '$lib/api';
	import { parseRequestData, formatRequestTypeFull, formatRequestType, formatTimeAgo } from '$lib/utils/formatters';
	import { theme } from '$lib/stores/theme.svelte';

	let { children } = $props();

	const publicRoutes = ['/login', '/register', '/health'];

	// Notification state
	let pendingCount = $state(0);
	let pendingRequests = $state<OrgRequestResponse[]>([]);
	let showNotifPanel = $state(false);
	let notifLoading = $state(false);

	// User notification state (responses to my requests)
	let userNotifCount = $state(0);
	let userNotifRequests = $state<OrgRequestResponse[]>([]);
	let totalNotifCount = $derived(pendingCount + userNotifCount);

	// 2FA enforcement: check if selected org requires 2FA but user hasn't set it up
	let requires2faSetup = $derived(() => {
		if (!auth.isAuthenticated || !auth.user) return false;
		if (auth.user.twoFactorEnabled) return false;
		const selectedOrg = orgContext.selectedOrg;
		if (!selectedOrg) return false;
		return selectedOrg.require2fa === true;
	});

	onMount(() => {
		auth.init();
		orgContext.init();
		theme.load();
	});

	$effect(() => {
		if (!auth.loading && !auth.isAuthenticated) {
			const path = window.location.pathname;
			if (!publicRoutes.some((r) => path.startsWith(r))) {
				goto('/login');
			}
		}
	});

	// Load orgs when user becomes authenticated
	$effect(() => {
		if (auth.isAuthenticated && auth.user) {
			orgContext.loadOrganizations(auth.user.id);
			loadNotifications();
		}
	});

	async function loadNotifications() {
		try {
			const { data } = await requestsApi.apiV1OrganizationsNotificationsGet();
			pendingCount = data.pendingRequests ?? 0;
			pendingRequests = data.requests ?? [];
		} catch {
			pendingCount = 0;
			pendingRequests = [];
		}
		// Also load user notifications (responses to my requests)
		try {
			const { data } = await requestsApi.apiV1OrganizationsUserNotificationsGet();
			userNotifCount = data.count ?? 0;
			userNotifRequests = data.requests ?? [];
		} catch {
			userNotifCount = 0;
			userNotifRequests = [];
		}
	}

	function toggleNotifPanel() {
		showNotifPanel = !showNotifPanel;
	}

	function closeNotifPanel() {
		showNotifPanel = false;
	}

	async function respondToRequest(request: OrgRequestResponse, accept: boolean) {
		if (!request.organizationSlug || !request.id) return;
		try {
			await requestsApi.apiV1OrganizationsSlugRequestsIdPut(request.organizationSlug, request.id, { accept });
			await loadNotifications();
			if (accept && request.type === 'JoinOrganization') {
				// Reload orgs if needed
				if (auth.user?.id) orgContext.loadOrganizations(auth.user.id);
			}
		} catch (err) {
			console.error('Failed to respond to request', err);
		}
	}

	async function dismissUserNotif(requestId: number) {
		try {
			await requestsApi.apiV1OrganizationsUserNotificationsMarkSeenPost([requestId]);
			userNotifRequests = userNotifRequests.filter(r => r.id !== requestId);
			userNotifCount = userNotifRequests.length;
		} catch {
			// ignore
		}
	}

	async function dismissAllUserNotifs() {
		try {
			await requestsApi.apiV1OrganizationsUserNotificationsMarkSeenPost(userNotifRequests.map(r => r.id!));
			userNotifRequests = [];
			userNotifCount = 0;
		} catch {
			// ignore
		}
	}

	// Notification detail popup
	let detailReq = $state<OrgRequestResponse | null>(null);

	function openNotifDetail(req: OrgRequestResponse) {
		detailReq = req;
	}

	function closeNotifDetail() {
		detailReq = null;
	}

	function handleOverlayKeydown(event: KeyboardEvent, close: () => void) {
		if (event.key === 'Escape' || event.key === 'Enter' || event.key === ' ') {
			event.preventDefault();
			close();
		}
	}

	function navLinkClass(path: string) {
		const current = $page.url.pathname;
		const active = current === path || current.startsWith(`${path}/`);
		return `rounded-lg px-3 py-1.5 text-sm font-medium no-underline transition-colors ${active ? 'bg-base-200 text-base-content' : 'text-base-content/70 hover:bg-base-200/60 hover:text-base-content'}`;
	}

	async function handleLogout() {
		await auth.logout();
		goto('/login');
	}
</script>

<svelte:head>
	<link rel="icon" href={favicon} />
</svelte:head>

{#if auth.loading}
	<div class="min-h-screen flex items-center justify-center text-base-content/60">
		<p>Loading...</p>
	</div>
{:else if auth.isAuthenticated}
	<nav class="sticky top-0 z-50 border-b border-base-300 bg-base-100/95 shadow-sm backdrop-blur">
		<div class="mx-auto flex w-full max-w-6xl flex-wrap items-center gap-3 px-4 py-3">
			<div class="flex min-w-0 items-center gap-3 pr-2">
				<a href="/" class="text-lg font-bold text-primary no-underline">Time Tracking</a>
				{#if orgContext.selectedOrg}
					<span class="max-w-44 truncate rounded-md bg-base-200 px-2.5 py-1 text-xs font-medium text-base-content/60">{orgContext.selectedOrg.name}</span>
				{/if}
			</div>

			<div class="flex flex-1 flex-wrap items-center gap-1 md:gap-2">
				<a href="/time" class={navLinkClass('/time')}>Timer</a>
				<a href="/history" class={navLinkClass('/history')}>History</a>
				{#if orgContext.selectedOrg}
					<a href="/organizations/{orgContext.selectedOrgSlug}" class={navLinkClass(`/organizations/${orgContext.selectedOrgSlug}`)}>Organization</a>
				{/if}
			</div>

			<div class="ml-auto flex items-center gap-2 sm:gap-3">
			<!-- Notification bell -->
			<div class="relative">
				<button class="btn btn-ghost btn-sm btn-square" onclick={toggleNotifPanel} title="Notifications">
					<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
						<path d="M18 8A6 6 0 0 0 6 8c0 7-3 9-3 9h18s-3-2-3-9"></path>
						<path d="M13.73 21a2 2 0 0 1-3.46 0"></path>
					</svg>
					{#if totalNotifCount > 0}
						<span class="badge badge-error badge-xs absolute -top-1 -right-1">{totalNotifCount}</span>
					{/if}
				</button>

				{#if showNotifPanel}
					<!-- svelte-ignore a11y_no_static_element_interactions -->
					<div class="fixed inset-0 z-[199]" onclick={closeNotifPanel} onkeydown={(e) => handleOverlayKeydown(e, closeNotifPanel)} tabindex="-1" role="button" aria-label="Close notifications"></div>
					<div class="absolute top-full mt-2 right-0 w-80 max-h-[420px] bg-base-100 border border-base-300 rounded-xl shadow-xl z-[200] overflow-hidden flex flex-col">
						<div class="flex items-center justify-between px-4 py-3 border-b border-base-200">
							<span class="font-semibold text-sm">Notifications</span>
							<button class="btn btn-ghost btn-xs btn-square" onclick={closeNotifPanel}>&times;</button>
						</div>

						<!-- User notifications (responses to my requests) -->
						{#if userNotifRequests.length > 0}
							<div class="py-1">
								<div class="flex items-center justify-between px-3 py-2">
									<span class="text-xs font-bold uppercase tracking-wider text-base-content/40">My Request Updates</span>
									<button class="btn btn-ghost btn-xs text-primary" onclick={dismissAllUserNotifs}>Dismiss all</button>
								</div>
								<div class="overflow-y-auto max-h-[360px]">
									{#each userNotifRequests as req}
										<button class="flex items-center justify-between px-4 py-2.5 border-b border-base-200 w-full bg-transparent cursor-pointer text-left hover:bg-base-200/50 transition-colors border-l-4 {req.status === 'Accepted' ? 'border-l-success' : req.status === 'Declined' ? 'border-l-error' : 'border-l-transparent'}" onclick={() => openNotifDetail(req)}>
											<div class="flex flex-col gap-0.5 flex-1 min-w-0">
												<span class="{req.status === 'Accepted' ? 'badge badge-success badge-xs' : 'badge badge-error badge-xs'}">
													{req.status === 'Accepted' ? 'Accepted' : 'Declined'}
												</span>
												<span class="font-semibold text-xs text-primary">{formatRequestType(req.type)}</span>
												<span class="text-xs text-base-content/50">{req.organizationName}</span>
												{#if req.respondedByName}
													<span class="text-xs text-base-content/40 italic truncate">by {req.respondedByName}</span>
												{/if}
												<span class="text-[0.7rem] text-base-content/40">{formatTimeAgo(req.respondedAt)}</span>
											</div>
											<span class="text-base-content/40 text-lg flex-shrink-0">›</span>
										</button>
									{/each}
								</div>
							</div>
						{/if}

						<!-- Admin pending requests -->
						{#if pendingRequests.length > 0}
							<div class="py-1">
								<div class="flex items-center justify-between px-3 py-2">
									<span class="text-xs font-bold uppercase tracking-wider text-base-content/40">Pending Requests</span>
								</div>
								<div class="overflow-y-auto max-h-[360px]">
									{#each pendingRequests as req}
										<button class="flex items-center justify-between px-4 py-2.5 border-b border-base-200 w-full bg-transparent cursor-pointer text-left hover:bg-base-200/50 transition-colors border-l-4 border-l-transparent" onclick={() => openNotifDetail(req)}>
											<div class="flex flex-col gap-0.5 flex-1 min-w-0">
												<span class="font-semibold text-xs text-primary">{formatRequestType(req.type)}</span>
												<span class="text-sm font-medium">{req.userFirstName} {req.userLastName}</span>
												<span class="text-xs text-base-content/50">{req.organizationName}</span>
												{#if req.message}
													<span class="text-xs text-base-content/40 italic truncate">"{req.message}"</span>
												{/if}
												<span class="text-[0.7rem] text-base-content/40">{formatTimeAgo(req.createdAt)}</span>
											</div>
											<span class="text-base-content/40 text-lg flex-shrink-0">›</span>
										</button>
									{/each}
								</div>
							</div>
						{/if}

						{#if pendingRequests.length === 0 && userNotifRequests.length === 0}
							<div class="py-8 px-4 text-center text-base-content/40 text-sm">No notifications</div>
						{/if}
					</div>
				{/if}
			</div>

			<a href="/settings" class="rounded-md px-2 py-1 text-sm font-medium text-base-content no-underline transition-colors hover:bg-base-200" title="Settings">{auth.user?.firstName} {auth.user?.lastName}</a>

			<button class="btn btn-ghost btn-sm" onclick={handleLogout}>Sign Out</button>
		</div>
		</div>
	</nav>

	<!-- Notification detail popup -->
	{#if detailReq}
		<!-- svelte-ignore a11y_no_static_element_interactions -->
		<div class="fixed inset-0 bg-black/35 z-[299]" onclick={closeNotifDetail} onkeydown={(e) => handleOverlayKeydown(e, closeNotifDetail)} tabindex="-1" role="button" aria-label="Close notification details"></div>
		<div class="fixed top-1/2 left-1/2 -translate-x-1/2 -translate-y-1/2 w-[400px] max-w-[92vw] max-h-[80vh] bg-base-100 rounded-2xl shadow-2xl z-[300] flex flex-col overflow-hidden">
			<div class="flex items-center justify-between px-5 py-4 border-b border-base-200">
				<h3 class="text-base font-bold">{formatRequestTypeFull(detailReq.type)}</h3>
				<button class="btn btn-ghost btn-xs btn-square" onclick={closeNotifDetail}>&times;</button>
			</div>
			<div class="px-5 py-4 overflow-y-auto flex-1">
				{#if detailReq.status}
					<div class="flex justify-between items-baseline py-1.5 border-b border-base-200/50 last:border-b-0">
						<span class="text-xs text-base-content/50 font-medium shrink-0 mr-3">Status</span>
						<span class="text-sm font-medium text-right">
							<span class="{detailReq.status === 'Accepted' ? 'badge badge-success badge-xs' : detailReq.status === 'Declined' ? 'badge badge-error badge-xs' : 'badge badge-warning badge-xs'}">
								{detailReq.status === 'Accepted' ? 'Accepted' : detailReq.status === 'Declined' ? 'Declined' : 'Pending'}
							</span>
						</span>
					</div>
				{/if}

				{#if detailReq.userFirstName || detailReq.userLastName}
					<div class="flex justify-between items-baseline py-1.5 border-b border-base-200/50 last:border-b-0">
						<span class="text-xs text-base-content/50 font-medium shrink-0 mr-3">User</span>
						<span class="text-sm font-medium text-right">{detailReq.userFirstName} {detailReq.userLastName}</span>
					</div>
				{/if}

				{#if detailReq.organizationName}
					<div class="flex justify-between items-baseline py-1.5 border-b border-base-200/50 last:border-b-0">
						<span class="text-xs text-base-content/50 font-medium shrink-0 mr-3">Organization</span>
						<span class="text-sm font-medium text-right">{detailReq.organizationName}</span>
					</div>
				{/if}

				{#if detailReq.createdAt}
					<div class="flex justify-between items-baseline py-1.5 border-b border-base-200/50 last:border-b-0">
						<span class="text-xs text-base-content/50 font-medium shrink-0 mr-3">Created</span>
						<span class="text-sm font-medium text-right">{new Date(detailReq.createdAt).toLocaleString()}</span>
					</div>
				{/if}

				{#if detailReq.respondedAt}
					<div class="flex justify-between items-baseline py-1.5 border-b border-base-200/50 last:border-b-0">
						<span class="text-xs text-base-content/50 font-medium shrink-0 mr-3">Responded</span>
						<span class="text-sm font-medium text-right">{new Date(detailReq.respondedAt).toLocaleString()}</span>
					</div>
				{/if}

				{#if detailReq.respondedByName}
					<div class="flex justify-between items-baseline py-1.5 border-b border-base-200/50 last:border-b-0">
						<span class="text-xs text-base-content/50 font-medium shrink-0 mr-3">Responded by</span>
						<span class="text-sm font-medium text-right">{detailReq.respondedByName}</span>
					</div>
				{/if}

				{#if detailReq.message}
					<div class="flex justify-between items-baseline py-1.5 border-b border-base-200/50 last:border-b-0">
						<span class="text-xs text-base-content/50 font-medium shrink-0 mr-3">Message</span>
						<span class="text-sm font-medium text-right italic text-base-content/50">"{detailReq.message}"</span>
					</div>
				{/if}

				{#if parseRequestData(detailReq.type, detailReq.requestData).length > 0}
					<div class="mt-3 pt-3 border-t border-base-300">
						<span class="block text-xs font-bold uppercase tracking-wider text-base-content/40 mb-2">Request Details</span>
						{#each parseRequestData(detailReq.type, detailReq.requestData) as line}
							<div class="text-sm font-medium py-1">{line}</div>
						{/each}
					</div>
				{/if}
			</div>

			<!-- Action buttons for pending admin requests -->
			{#if detailReq.status === 'Pending' && pendingRequests.some(r => r.id === detailReq?.id)}
				<div class="flex gap-2 px-5 py-3 border-t border-base-200">
					<button class="btn btn-success flex-1" onclick={() => { respondToRequest(detailReq!, true); closeNotifDetail(); }}>Accept</button>
					<button class="btn btn-error flex-1" onclick={() => { respondToRequest(detailReq!, false); closeNotifDetail(); }}>Decline</button>
				</div>
			{/if}

			<!-- Dismiss button for user notifications -->
			{#if detailReq.status !== 'Pending' && userNotifRequests.some(r => r.id === detailReq?.id)}
				<div class="flex gap-2 px-5 py-3 border-t border-base-200">
					<button class="btn btn-ghost flex-1" onclick={() => { dismissUserNotif(detailReq!.id!); closeNotifDetail(); }}>Dismiss Notification</button>
				</div>
			{/if}
		</div>
	{/if}

	<main class="max-w-5xl mx-auto px-6 py-8">
		{#if requires2faSetup() && !$page.url.pathname.startsWith('/settings')}
			<div class="flex items-center justify-center min-h-[60vh]">
				<div class="card bg-base-100 border border-warning shadow-lg w-full max-w-md">
					<div class="card-body text-center">
						<div class="text-4xl mb-3">🔐</div>
						<h2 class="card-title justify-center text-lg">Two-Factor Authentication Required</h2>
						<p class="text-sm text-base-content/60 mt-2">
							Your organization requires two-factor authentication. Please set up 2FA in your account settings to continue.
						</p>
						<div class="mt-4">
							<a href="/settings" class="btn btn-primary">Go to Settings</a>
						</div>
					</div>
				</div>
			</div>
		{:else}
			{@render children?.()}
		{/if}
	</main>
{:else}
	{@render children?.()}
{/if}

