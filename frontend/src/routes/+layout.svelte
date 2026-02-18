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

	// Notification detail popup
	let detailReq = $state<OrgRequestResponse | null>(null);

	function openNotifDetail(req: OrgRequestResponse) {
		detailReq = req;
	}

	function closeNotifDetail() {
		detailReq = null;
	}

	function parseRequestData(type: string | null | undefined, data: string | null | undefined): string[] {
		if (!data) return [];
		try {
			if (type === 'EditPastEntry') {
				const obj = JSON.parse(data);
				const parts: string[] = [];
				if (obj.startTime) parts.push(`New Start: ${new Date(obj.startTime).toLocaleString()}`);
				if (obj.endTime) parts.push(`New End: ${new Date(obj.endTime).toLocaleString()}`);
				if (obj.description !== undefined) parts.push(`Note: ${obj.description}`);
				return parts;
			} else if (type === 'EditPause') {
				return [`New Pause: ${data} minutes`];
			} else if (type === 'SetInitialOvertime') {
				return [`Overtime: ${data}h`];
			}
		} catch { /* fallback */ }
		return data ? [data] : [];
	}

	function formatRequestTypeFull(type: string | null | undefined): string {
		if (type === 'JoinOrganization') return 'Join Organization';
		if (type === 'EditPastEntry') return 'Edit Past Time Entry';
		if (type === 'EditPause') return 'Edit Pause Duration';
		if (type === 'SetInitialOvertime') return 'Set Initial Overtime';
		return type ?? 'Unknown';
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
			{#if orgContext.selectedOrg}
				<span class="nav-org-name">{orgContext.selectedOrg.name}</span>
			{/if}
			<a href="/time" class="nav-link">Timer</a>
			{#if orgContext.selectedOrg}
				<a href="/organizations/{orgContext.selectedOrgSlug}" class="nav-link">Organization</a>
			{/if}
		</div>
		<div class="nav-right">
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
										<button class="notif-item-btn user-notif-item" class:notif-accepted={req.status === 'Accepted'} class:notif-declined={req.status === 'Declined'} onclick={() => openNotifDetail(req)}>
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
											<span class="notif-item-expand">›</span>
										</button>
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
										<button class="notif-item-btn" onclick={() => openNotifDetail(req)}>
											<div class="notif-item-info">
												<span class="notif-item-type">{formatRequestType(req.type)}</span>
												<span class="notif-item-user">{req.userFirstName} {req.userLastName}</span>
												<span class="notif-item-org">{req.organizationName}</span>
												{#if req.message}
													<span class="notif-item-msg">"{req.message}"</span>
												{/if}
												<span class="notif-item-time">{formatTimeAgo(req.createdAt)}</span>
											</div>
											<span class="notif-item-expand">›</span>
										</button>
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

	<!-- Notification detail popup -->
	{#if detailReq}
		<!-- svelte-ignore a11y_no_static_element_interactions -->
		<div class="detail-backdrop" onclick={closeNotifDetail}></div>
		<div class="detail-popup">
			<div class="detail-popup-header">
				<h3 class="detail-popup-title">{formatRequestTypeFull(detailReq.type)}</h3>
				<button class="notif-close" onclick={closeNotifDetail}>&times;</button>
			</div>
			<div class="detail-popup-body">
				{#if detailReq.status}
					<div class="detail-row">
						<span class="detail-label">Status</span>
						<span class="detail-value">
							<span class="notif-item-status-badge" class:accepted={detailReq.status === 'Accepted'} class:declined={detailReq.status === 'Declined'} class:pending={detailReq.status === 'Pending'}>
								{detailReq.status === 'Accepted' ? '✓ Accepted' : detailReq.status === 'Declined' ? '✗ Declined' : '⏳ Pending'}
							</span>
						</span>
					</div>
				{/if}

				{#if detailReq.userFirstName || detailReq.userLastName}
					<div class="detail-row">
						<span class="detail-label">User</span>
						<span class="detail-value">{detailReq.userFirstName} {detailReq.userLastName}</span>
					</div>
				{/if}

				{#if detailReq.organizationName}
					<div class="detail-row">
						<span class="detail-label">Organization</span>
						<span class="detail-value">{detailReq.organizationName}</span>
					</div>
				{/if}

				{#if detailReq.createdAt}
					<div class="detail-row">
						<span class="detail-label">Created</span>
						<span class="detail-value">{new Date(detailReq.createdAt).toLocaleString()}</span>
					</div>
				{/if}

				{#if detailReq.respondedAt}
					<div class="detail-row">
						<span class="detail-label">Responded</span>
						<span class="detail-value">{new Date(detailReq.respondedAt).toLocaleString()}</span>
					</div>
				{/if}

				{#if detailReq.respondedByName}
					<div class="detail-row">
						<span class="detail-label">Responded by</span>
						<span class="detail-value">{detailReq.respondedByName}</span>
					</div>
				{/if}

				{#if detailReq.message}
					<div class="detail-row">
						<span class="detail-label">Message</span>
						<span class="detail-value detail-value-msg">"{detailReq.message}"</span>
					</div>
				{/if}

				{#if parseRequestData(detailReq.type, detailReq.requestData).length > 0}
					<div class="detail-section">
						<span class="detail-section-title">Request Details</span>
						{#each parseRequestData(detailReq.type, detailReq.requestData) as line}
							<div class="detail-data-row">{line}</div>
						{/each}
					</div>
				{/if}
			</div>

			<!-- Action buttons for pending admin requests -->
			{#if detailReq.status === 'Pending' && pendingRequests.some(r => r.id === detailReq?.id)}
				<div class="detail-popup-actions">
					<button class="detail-accept" onclick={() => { respondToRequest(detailReq!, true); closeNotifDetail(); }}>Accept</button>
					<button class="detail-decline" onclick={() => { respondToRequest(detailReq!, false); closeNotifDetail(); }}>Decline</button>
				</div>
			{/if}

			<!-- Dismiss button for user notifications -->
			{#if detailReq.status !== 'Pending' && userNotifRequests.some(r => r.id === detailReq?.id)}
				<div class="detail-popup-actions">
					<button class="detail-dismiss-btn" onclick={() => { dismissUserNotif(detailReq!.id!); closeNotifDetail(); }}>Dismiss Notification</button>
				</div>
			{/if}
		</div>
	{/if}

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

	.nav-org-name {
		color: #6b7280;
		font-size: 0.8125rem;
		font-weight: 500;
		padding: 0.25rem 0.625rem;
		background: #f3f4f6;
		border-radius: 6px;
		white-space: nowrap;
		max-width: 160px;
		overflow: hidden;
		text-overflow: ellipsis;
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

	/* Clickable notification items */
	.notif-item-btn {
		display: flex;
		align-items: center;
		justify-content: space-between;
		padding: 0.65rem 1rem;
		border-bottom: 1px solid #f3f4f6;
		gap: 0.5rem;
		width: 100%;
		background: none;
		border-left: 3px solid transparent;
		border-top: none;
		border-right: none;
		cursor: pointer;
		text-align: left;
		font-family: inherit;
		transition: background 0.15s;
	}

	.notif-item-btn:hover {
		background: #f8fafc;
	}

	.notif-item-btn:last-child {
		border-bottom: none;
	}

	.notif-item-expand {
		color: #9ca3af;
		font-size: 1.1rem;
		flex-shrink: 0;
		transition: color 0.15s;
	}

	.notif-item-btn:hover .notif-item-expand {
		color: #3b82f6;
	}

	.notif-item-status-badge.pending {
		background: #fef3c7;
		color: #92400e;
	}

	/* Detail popup */
	.detail-backdrop {
		position: fixed;
		inset: 0;
		background: rgba(0, 0, 0, 0.35);
		z-index: 299;
	}

	.detail-popup {
		position: fixed;
		top: 50%;
		left: 50%;
		transform: translate(-50%, -50%);
		width: 400px;
		max-width: 92vw;
		max-height: 80vh;
		background: white;
		border-radius: 14px;
		box-shadow: 0 16px 48px rgba(0, 0, 0, 0.18);
		z-index: 300;
		display: flex;
		flex-direction: column;
		overflow: hidden;
	}

	.detail-popup-header {
		display: flex;
		align-items: center;
		justify-content: space-between;
		padding: 1rem 1.25rem;
		border-bottom: 1px solid #f3f4f6;
	}

	.detail-popup-title {
		margin: 0;
		font-size: 1rem;
		font-weight: 700;
		color: #1a1a2e;
	}

	.detail-popup-body {
		padding: 1rem 1.25rem;
		overflow-y: auto;
		flex: 1;
	}

	.detail-row {
		display: flex;
		justify-content: space-between;
		align-items: baseline;
		padding: 0.4rem 0;
		border-bottom: 1px solid #f9fafb;
	}

	.detail-row:last-child {
		border-bottom: none;
	}

	.detail-label {
		font-size: 0.8rem;
		color: #6b7280;
		font-weight: 500;
		flex-shrink: 0;
		margin-right: 0.75rem;
	}

	.detail-value {
		font-size: 0.85rem;
		color: #1a1a2e;
		font-weight: 500;
		text-align: right;
	}

	.detail-value-msg {
		font-style: italic;
		color: #6b7280;
	}

	.detail-section {
		margin-top: 0.75rem;
		padding-top: 0.75rem;
		border-top: 1px solid #e5e7eb;
	}

	.detail-section-title {
		display: block;
		font-size: 0.6875rem;
		font-weight: 700;
		text-transform: uppercase;
		letter-spacing: 0.05em;
		color: #9ca3af;
		margin-bottom: 0.5rem;
	}

	.detail-data-row {
		font-size: 0.85rem;
		color: #1a1a2e;
		padding: 0.3rem 0;
		font-weight: 500;
	}

	.detail-popup-actions {
		display: flex;
		gap: 0.5rem;
		padding: 0.75rem 1.25rem;
		border-top: 1px solid #f3f4f6;
	}

	.detail-accept {
		flex: 1;
		padding: 0.5rem;
		border: none;
		border-radius: 8px;
		background: #10b981;
		color: white;
		font-weight: 600;
		font-size: 0.85rem;
		cursor: pointer;
		transition: background 0.15s;
	}

	.detail-accept:hover { background: #059669; }

	.detail-decline {
		flex: 1;
		padding: 0.5rem;
		border: none;
		border-radius: 8px;
		background: #ef4444;
		color: white;
		font-weight: 600;
		font-size: 0.85rem;
		cursor: pointer;
		transition: background 0.15s;
	}

	.detail-decline:hover { background: #dc2626; }

	.detail-dismiss-btn {
		flex: 1;
		padding: 0.5rem;
		border: 1px solid #d1d5db;
		border-radius: 8px;
		background: white;
		color: #4b5563;
		font-weight: 500;
		font-size: 0.85rem;
		cursor: pointer;
		transition: background 0.15s;
	}

	.detail-dismiss-btn:hover { background: #f3f4f6; }
</style>

