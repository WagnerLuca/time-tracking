<script lang="ts">
	import { auth } from '$lib/stores/auth.svelte';
	import { orgContext } from '$lib/stores/orgContext.svelte';
	import { page } from '$app/stores';
	import { goto } from '$app/navigation';
	import { onMount } from 'svelte';
	import favicon from '$lib/assets/favicon.svg';
	import { requestsApi, organizationsApi } from '$lib/apiClient';
	import type { OrgRequestResponse } from '$lib/api';

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

	onMount(() => {
		auth.init();
		orgContext.init();
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
			const { data } = await requestsApi.apiOrganizationsNotificationsGet();
			pendingCount = data.pendingRequests ?? 0;
			pendingRequests = data.requests ?? [];
		} catch {
			pendingCount = 0;
			pendingRequests = [];
		}
		// Also load user notifications (responses to my requests)
		try {
			const { data } = await requestsApi.apiOrganizationsUserNotificationsGet();
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
			await requestsApi.apiOrganizationsSlugRequestsIdPut(request.organizationSlug, request.id, { accept });
			await loadNotifications();
			if (accept && request.type === 'JoinOrganization') {
				// Reload orgs if needed
				if (auth.user?.id) orgContext.loadOrganizations(auth.user.id);
			}
		} catch (err: any) {
			console.error('Failed to respond to request', err);
		}
	}

	async function dismissUserNotif(requestId: number) {
		try {
			await requestsApi.apiOrganizationsUserNotificationsMarkSeenPost([requestId]);
			userNotifRequests = userNotifRequests.filter(r => r.id !== requestId);
			userNotifCount = userNotifRequests.length;
		} catch {
			// ignore
		}
	}

	async function dismissAllUserNotifs() {
		try {
			await requestsApi.apiOrganizationsUserNotificationsMarkSeenPost(userNotifRequests.map(r => r.id!));
			userNotifRequests = [];
			userNotifCount = 0;
		} catch {
			// ignore
		}
	}

	function formatRequestType(type: string | null | undefined): string {
		if (type === 'JoinOrganization') return 'Join';
		if (type === 'EditPastEntry') return 'Edit Entry';
		if (type === 'EditPause') return 'Edit Pause';
		if (type === 'SetInitialOvertime') return 'Set Overtime';
		return type ?? 'Unknown';
	}

	function formatTimeAgo(dateStr: string | null | undefined): string {
		if (!dateStr) return '';
		const diff = Date.now() - new Date(dateStr).getTime();
		const mins = Math.floor(diff / 60000);
		if (mins < 1) return 'just now';
		if (mins < 60) return `${mins}m ago`;
		const hours = Math.floor(mins / 60);
		if (hours < 24) return `${hours}h ago`;
		const days = Math.floor(hours / 24);
		return `${days}d ago`;
	}

	async function handleLogout() {
		await auth.logout();
		goto('/login');
	}

	function handleOrgChange(e: Event) {
		const value = (e.target as HTMLSelectElement).value;
		orgContext.select(value ? parseInt(value) : null);
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
			<a href="/time" class="nav-link">Timer</a>
			<a href="/organizations" class="nav-link">Organizations</a>
		</div>
		<div class="nav-right">
			{#if orgContext.organizations.length > 0}
				<select class="org-switcher" value={orgContext.selectedOrgId ?? ''} onchange={handleOrgChange}>
					<option value="">Personal</option>
					{#each orgContext.organizations as org}
						<option value={org.organizationId}>{org.name}</option>
					{/each}
				</select>
			{/if}

			<!-- Notification bell -->
			<div class="notif-wrapper">
				<button class="notif-bell" onclick={toggleNotifPanel} title="Notifications">
					<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
						<path d="M18 8A6 6 0 0 0 6 8c0 7-3 9-3 9h18s-3-2-3-9"></path>
						<path d="M13.73 21a2 2 0 0 1-3.46 0"></path>
					</svg>
					{#if totalNotifCount > 0}
						<span class="notif-badge">{totalNotifCount}</span>
					{/if}
				</button>

				{#if showNotifPanel}
					<!-- svelte-ignore a11y_no_static_element_interactions -->
					<div class="notif-backdrop" onclick={closeNotifPanel}></div>
					<div class="notif-panel">
						<div class="notif-header">
							<span class="notif-title">Notifications</span>
							<button class="notif-close" onclick={closeNotifPanel}>&times;</button>
						</div>

						<!-- User notifications (responses to my requests) -->
						{#if userNotifRequests.length > 0}
							<div class="notif-section">
								<div class="notif-section-header">
									<span class="notif-section-title">My Request Updates</span>
									<button class="notif-dismiss-all" onclick={dismissAllUserNotifs}>Dismiss all</button>
								</div>
								<div class="notif-list">
									{#each userNotifRequests as req}
										<div class="notif-item user-notif-item" class:notif-accepted={req.status === 'Accepted'} class:notif-declined={req.status === 'Declined'}>
											<div class="notif-item-info">
												<span class="notif-item-status-badge" class:accepted={req.status === 'Accepted'} class:declined={req.status === 'Declined'}>
													{req.status === 'Accepted' ? '✓ Accepted' : '✗ Declined'}
												</span>
												<span class="notif-item-type">{formatRequestType(req.type)}</span>
												<span class="notif-item-org">{req.organizationName}</span>
												{#if req.respondedByName}
													<span class="notif-item-msg">by {req.respondedByName}</span>
												{/if}
												<span class="notif-item-time">{formatTimeAgo(req.respondedAt)}</span>
											</div>
											<div class="notif-item-actions">
												<button class="notif-dismiss" onclick={() => dismissUserNotif(req.id!)} title="Dismiss">&times;</button>
											</div>
										</div>
									{/each}
								</div>
							</div>
						{/if}

						<!-- Admin pending requests -->
						{#if pendingRequests.length > 0}
							<div class="notif-section">
								<div class="notif-section-header">
									<span class="notif-section-title">Pending Requests</span>
								</div>
								<div class="notif-list">
									{#each pendingRequests as req}
										<div class="notif-item">
											<div class="notif-item-info">
												<span class="notif-item-type">{formatRequestType(req.type)}</span>
												<span class="notif-item-user">{req.userFirstName} {req.userLastName}</span>
												<span class="notif-item-org">{req.organizationName}</span>
												{#if req.message}
													<span class="notif-item-msg">"{req.message}"</span>
												{/if}
												<span class="notif-item-time">{formatTimeAgo(req.createdAt)}</span>
											</div>
											<div class="notif-item-actions">
												<button class="notif-accept" onclick={() => respondToRequest(req, true)} title="Accept">&#10003;</button>
												<button class="notif-decline" onclick={() => respondToRequest(req, false)} title="Decline">&#10007;</button>
											</div>
										</div>
									{/each}
								</div>
							</div>
						{/if}

						{#if pendingRequests.length === 0 && userNotifRequests.length === 0}
							<div class="notif-empty">No notifications</div>
						{/if}
					</div>
				{/if}
			</div>

			<a href="/settings" class="nav-user-link" title="Settings">{auth.user?.firstName} {auth.user?.lastName}</a>
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

	.nav-user-link {
		color: #374151;
		font-size: 0.875rem;
		font-weight: 500;
		text-decoration: none;
		padding: 0.25rem 0.5rem;
		border-radius: 6px;
		transition: background 0.15s, color 0.15s;
	}

	.nav-user-link:hover {
		background: #f3f4f6;
		color: #1a1a2e;
	}

	.org-switcher {
		padding: 0.375rem 0.625rem;
		border: 1px solid #d1d5db;
		border-radius: 8px;
		font-size: 0.8125rem;
		background: white;
		color: #374151;
		max-width: 200px;
		cursor: pointer;
	}

	.org-switcher:focus {
		outline: none;
		border-color: #3b82f6;
		box-shadow: 0 0 0 2px rgba(59, 130, 246, 0.15);
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

	/* Notification bell & panel */
	.notif-wrapper {
		position: relative;
	}

	.notif-bell {
		background: none;
		border: none;
		color: #4b5563;
		cursor: pointer;
		padding: 0.35rem;
		border-radius: 6px;
		position: relative;
		display: flex;
		align-items: center;
		transition: background 0.15s, color 0.15s;
	}

	.notif-bell:hover {
		background: #f3f4f6;
		color: #1a1a2e;
	}

	.notif-badge {
		position: absolute;
		top: -2px;
		right: -4px;
		background: #ef4444;
		color: white;
		font-size: 0.65rem;
		font-weight: 700;
		min-width: 16px;
		height: 16px;
		border-radius: 99px;
		display: flex;
		align-items: center;
		justify-content: center;
		padding: 0 3px;
		line-height: 1;
	}

	.notif-backdrop {
		position: fixed;
		inset: 0;
		z-index: 199;
	}

	.notif-panel {
		position: absolute;
		top: calc(100% + 8px);
		right: 0;
		width: 340px;
		max-height: 420px;
		background: white;
		border: 1px solid #e5e7eb;
		border-radius: 12px;
		box-shadow: 0 8px 30px rgba(0, 0, 0, 0.12);
		z-index: 200;
		overflow: hidden;
		display: flex;
		flex-direction: column;
	}

	.notif-header {
		display: flex;
		align-items: center;
		justify-content: space-between;
		padding: 0.75rem 1rem;
		border-bottom: 1px solid #f3f4f6;
	}

	.notif-title {
		font-weight: 600;
		font-size: 0.9rem;
		color: #1a1a2e;
	}

	.notif-close {
		background: none;
		border: none;
		font-size: 1.25rem;
		color: #9ca3af;
		cursor: pointer;
		padding: 0;
		line-height: 1;
	}

	.notif-close:hover { color: #4b5563; }

	.notif-empty {
		padding: 2rem 1rem;
		text-align: center;
		color: #9ca3af;
		font-size: 0.875rem;
	}

	.notif-list {
		overflow-y: auto;
		max-height: 360px;
	}

	.notif-item {
		display: flex;
		align-items: center;
		justify-content: space-between;
		padding: 0.65rem 1rem;
		border-bottom: 1px solid #f3f4f6;
		gap: 0.5rem;
	}

	.notif-item:last-child { border-bottom: none; }

	.notif-item-info {
		display: flex;
		flex-direction: column;
		gap: 0.15rem;
		flex: 1;
		min-width: 0;
	}

	.notif-item-type {
		font-weight: 600;
		font-size: 0.8rem;
		color: #3b82f6;
	}

	.notif-item-user {
		font-size: 0.85rem;
		font-weight: 500;
		color: #1a1a2e;
	}

	.notif-item-org {
		font-size: 0.75rem;
		color: #6b7280;
	}

	.notif-item-msg {
		font-size: 0.75rem;
		color: #9ca3af;
		font-style: italic;
		overflow: hidden;
		text-overflow: ellipsis;
		white-space: nowrap;
	}

	.notif-item-time {
		font-size: 0.7rem;
		color: #9ca3af;
	}

	.notif-item-actions {
		display: flex;
		gap: 0.35rem;
		flex-shrink: 0;
	}

	.notif-accept, .notif-decline {
		width: 28px;
		height: 28px;
		border: none;
		border-radius: 6px;
		font-size: 0.9rem;
		cursor: pointer;
		display: flex;
		align-items: center;
		justify-content: center;
		transition: background 0.15s;
	}

	.notif-accept {
		background: #dcfce7;
		color: #16a34a;
	}

	.notif-accept:hover { background: #bbf7d0; }

	.notif-decline {
		background: #fef2f2;
		color: #dc2626;
	}

	.notif-decline:hover { background: #fecaca; }

	/* User notification styles */
	.notif-section {
		padding: 0.25rem 0;
	}

	.notif-section-header {
		display: flex;
		align-items: center;
		justify-content: space-between;
		padding: 0.5rem 0.75rem;
	}

	.notif-section-title {
		font-size: 0.6875rem;
		font-weight: 700;
		text-transform: uppercase;
		letter-spacing: 0.05em;
		color: #9ca3af;
	}

	.notif-dismiss-all {
		background: none;
		border: none;
		color: #3b82f6;
		font-size: 0.75rem;
		cursor: pointer;
		padding: 0;
	}

	.notif-dismiss-all:hover {
		text-decoration: underline;
	}

	.notif-item-status-badge {
		font-size: 0.6875rem;
		font-weight: 600;
		padding: 0.0625rem 0.375rem;
		border-radius: 8px;
	}

	.notif-item-status-badge.accepted {
		background: #d1fae5;
		color: #065f46;
	}

	.notif-item-status-badge.declined {
		background: #fee2e2;
		color: #991b1b;
	}

	.user-notif-item {
		border-left: 3px solid transparent;
	}

	.notif-accepted {
		border-left-color: #10b981;
	}

	.notif-declined {
		border-left-color: #ef4444;
	}

	.notif-dismiss {
		background: none;
		border: none;
		color: #9ca3af;
		font-size: 1rem;
		cursor: pointer;
		padding: 0.125rem 0.25rem;
		border-radius: 4px;
		line-height: 1;
	}

	.notif-dismiss:hover {
		color: #6b7280;
		background: #f3f4f6;
	}
</style>

