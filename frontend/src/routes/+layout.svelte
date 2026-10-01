<script lang="ts">
	import '../app.css';
	import { auth } from '$lib/stores/auth.svelte';
	import { orgContext } from '$lib/stores/orgContext.svelte';
	import { timer } from '$lib/stores/timer.svelte';
	import { theme } from '$lib/stores/theme.svelte';
	import { locale, type Locale } from '$lib/stores/locale.svelte';
	import { toast } from '$lib/stores/toast.svelte';
	import { page } from '$app/state';
	import { goto, afterNavigate } from '$app/navigation';
	import { onMount } from 'svelte';
	import favicon from '$lib/assets/favicon.svg';
	import { authApi, requestsApi, timeTrackingApi, notificationsApi } from '$lib/apiClient';
	import type { NotificationResponse, OrgRequestResponse, TimeEntryResponse } from '$lib/api';
	import { bellNotifications, decisionNotificationIds, notificationTarget } from '$lib/utils/notifications';
	import { invalidateOrgDetail } from '$lib/stores/orgDetail';
	import { parseRequestData, formatRequestTypeFull, formatRequestType, formatRequestStatus, formatTimeAgo, formatDate, formatDateTime, formatClock, statusBadgeClass, roleLabel } from '$lib/utils/formatters';
	import { dateKey } from '$lib/utils/dateHelpers';
	import { t, tp } from '$lib/i18n';
	import Modal from '$lib/components/Modal.svelte';
	import ConfirmDialog from '$lib/components/ConfirmDialog.svelte';
	import Toaster from '$lib/components/Toaster.svelte';
	import DeclineForm from '$lib/components/DeclineForm.svelte';

	let { children } = $props();

	const publicRoutes = ['/login', '/register', '/forgot-password', '/reset-password', '/verify-email', '/health'];
	const isPublicRoute = $derived(publicRoutes.some((r) => page.url.pathname.startsWith(r)));

	// Which nav popup is open (one at a time); Esc or a click outside closes it
	let openMenu = $state<'notifications' | 'org' | 'user' | 'mobile' | null>(null);
	function toggleMenu(menu: NonNullable<typeof openMenu>) {
		openMenu = openMenu === menu ? null : menu;
	}

	// Notification state
	let pendingRequests = $state<OrgRequestResponse[]>([]);
	let userNotifRequests = $state<OrgRequestResponse[]>([]);
	/** Unread notifications (the Notification table), incl. request decisions, which the bell shows as requests. */
	let notifications = $state<NotificationResponse[]>([]);
	let bellNotifs = $derived(bellNotifications(notifications));
	let totalNotifCount = $derived(pendingRequests.length + userNotifRequests.length + bellNotifs.length);
	let detailReq = $state<OrgRequestResponse | null>(null);
	let responding = $state(false);
	/** The decline step (optional reason) of the request in the detail modal. */
	let declining = $state(false);

	/** Title of a notification in the UI language; unknown types keep the server's (English) title. */
	function notifTitle(n: NotificationResponse): string {
		switch (n.type) {
			case 'FailureEntry': return t('nav.notifications.failureTitle');
			case 'TimerReminder': return t('nav.notifications.reminderTitle');
			case 'SettingsChanged': return t('nav.notifications.settingsTitle');
			default: return n.title ?? '';
		}
	}

	// Failure entries: timers auto-stopped at the business day end that still need a real end time
	let failures = $state<TimeEntryResponse[]>([]);
	const firstFailure = $derived(
		[...failures].sort((a, b) => (a.startTime ?? '').localeCompare(b.startTime ?? ''))[0] ?? null
	);
	/** Deep link into History: opens the affected day and the entry editor for that entry. */
	const failureLink = $derived(
		firstFailure?.startTime ? `/history?date=${dateKey(new Date(firstFailure.startTime))}&entry=${firstFailure.id}` : '/history'
	);

	async function loadFailures() {
		try {
			const { data } = await timeTrackingApi.apiV1TimeTrackingFailuresGet();
			failures = data;
		} catch {
			failures = [];
		}
	}

	// Re-check after every navigation so the hint disappears once entries are corrected,
	// and the timer indicator picks up timers started elsewhere (e.g. an RFID terminal)
	afterNavigate(() => {
		openMenu = null;
		if (auth.isAuthenticated) {
			loadFailures();
			timer.refresh().catch(() => {});
			refreshNotifications();
		}
	});

	// The bell refreshes on navigation (throttled) and every minute while the tab is visible
	const NOTIF_THROTTLE_MS = 30_000;
	let notifsLoadedAt = 0;
	function refreshNotifications() {
		if (!auth.isAuthenticated || Date.now() - notifsLoadedAt < NOTIF_THROTTLE_MS) return;
		loadNotifications();
	}
	onMount(() => {
		const interval = setInterval(() => {
			if (document.visibilityState === 'visible' && auth.isAuthenticated) loadNotifications();
		}, 60_000);
		const onVisible = () => { if (document.visibilityState === 'visible') refreshNotifications(); };
		document.addEventListener('visibilitychange', onVisible);
		return () => {
			clearInterval(interval);
			document.removeEventListener('visibilitychange', onVisible);
		};
	});

	// Unconfirmed email: a dismissible hint with a resend button (undefined = older cached user → no hint)
	let emailHintDismissed = $state(false);
	let resendingEmail = $state(false);
	const showEmailHint = $derived(auth.user?.emailConfirmed === false && !emailHintDismissed);

	async function resendVerification() {
		resendingEmail = true;
		try {
			await authApi.apiV1AuthVerifyEmailResendPost();
			toast.success(t('common.emailVerify.resent'));
		} catch (err) {
			toast.error(err, t('common.emailVerify.resendFailed'));
		} finally {
			resendingEmail = false;
		}
	}

	// 2FA enforcement: the selected org requires 2FA but the user hasn't set it up
	let requires2faSetup = $derived.by(() => {
		if (!auth.isAuthenticated || !auth.user) return false;
		if (auth.user.twoFactorEnabled) return false;
		return orgContext.selectedOrg?.require2fa === true;
	});

	onMount(() => {
		auth.init();
		orgContext.init();
		theme.load();
		document.documentElement.lang = locale.current;
	});

	$effect(() => {
		if (!auth.loading && !auth.isAuthenticated && !isPublicRoute) {
			const next = page.url.pathname + page.url.search;
			goto(next === '/' ? '/login' : `/login?next=${encodeURIComponent(next)}`, { replaceState: true });
		}
	});

	// Load orgs, notifications, failures and the running timer when the user becomes authenticated
	$effect(() => {
		if (auth.isAuthenticated && auth.user) {
			orgContext.loadOrganizations(auth.user.id!);
			loadNotifications();
			loadFailures();
			timer.refresh().catch(() => {});
		}
	});

	async function loadNotifications() {
		notifsLoadedAt = Date.now();
		const [adminRes, userRes, notifRes] = await Promise.allSettled([
			requestsApi.apiV1OrganizationsNotificationsGet(),
			requestsApi.apiV1OrganizationsUserNotificationsGet(),
			notificationsApi.apiV1NotificationsGet(true, 50)
		]);
		pendingRequests = adminRes.status === 'fulfilled' ? (adminRes.value.data.requests ?? []) : [];
		userNotifRequests = userRes.status === 'fulfilled' ? (userRes.value.data.requests ?? []) : [];
		notifications = notifRes.status === 'fulfilled' ? (notifRes.value.data.items ?? []).filter((n) => !n.isRead) : [];
	}

	async function markNotificationsRead(ids: number[]) {
		notifications = notifications.filter((x) => x.id == null || !ids.includes(x.id));
		await Promise.all(ids.map((id) => notificationsApi.apiV1NotificationsIdReadPut(id)));
	}

	/** Opens where the notification leads (e.g. History at the stopped entry) and marks it read. */
	async function openNotif(n: NotificationResponse) {
		const { href } = notificationTarget(n, failures);
		if (href) {
			openMenu = null;
			goto(href);
		}
		try {
			if (n.id != null) await markNotificationsRead([n.id]);
		} catch (err) {
			toast.error(err, t('nav.notifications.dismissFailed'));
		}
	}

	async function respondToRequest(request: OrgRequestResponse, accept: boolean, reason?: string) {
		if (!request.organizationSlug || !request.id) return;
		responding = true;
		try {
			await requestsApi.apiV1OrganizationsSlugRequestsIdPut(request.organizationSlug, request.id, { accept, reason });
			invalidateOrgDetail(request.organizationSlug); // e.g. vacation counts, new members
			toast.success(accept ? t('nav.notifications.accepted') : t('nav.notifications.declined'));
			detailReq = null;
			declining = false;
			await loadNotifications();
			if (accept && request.type === 'JoinOrganization' && auth.user?.id) {
				orgContext.loadOrganizations(auth.user.id);
			}
		} catch (err) {
			toast.error(err, t('nav.notifications.respondFailed'));
		} finally {
			responding = false;
		}
	}

	async function dismissUserNotifs(ids: number[]) {
		try {
			await requestsApi.apiV1OrganizationsUserNotificationsMarkSeenPost(ids);
			userNotifRequests = userNotifRequests.filter((r) => !ids.includes(r.id!));
			detailReq = null;
			// The matching "request accepted/declined" notifications are the same news: read them too
			const decisionIds = decisionNotificationIds(notifications, ids);
			if (decisionIds.length > 0) await markNotificationsRead(decisionIds);
		} catch (err) {
			toast.error(err, t('nav.notifications.dismissFailed'));
		}
	}

	function openNotifDetail(req: OrgRequestResponse) {
		openMenu = null;
		declining = false;
		detailReq = req;
	}

	function isActive(path: string) {
		const current = page.url.pathname;
		return path === '/' ? current === '/' : current === path || current.startsWith(`${path}/`);
	}

	function navLinkClass(path: string) {
		return `rounded-lg px-3 py-1.5 text-sm font-medium no-underline transition-colors ${isActive(path) ? 'bg-base-200 text-base-content' : 'text-base-content/70 hover:bg-base-200/60 hover:text-base-content'}`;
	}

	const navLinks = $derived([
		{ href: '/time', label: t('nav.timer') },
		{ href: '/history', label: t('nav.history') },
		{ href: '/export', label: t('nav.export') },
		...(orgContext.selectedOrgSlug ? [{ href: `/organizations/${orgContext.selectedOrgSlug}`, label: t('nav.organization') }] : [])
	]);

	/** Switch org context; on an org page, stay on the same sub-page of the new org. */
	function switchOrg(orgId: number, slug: string | null | undefined) {
		const oldSlug = orgContext.selectedOrgSlug;
		orgContext.select(orgId);
		openMenu = null;
		const path = page.url.pathname;
		if (slug && oldSlug && path.startsWith(`/organizations/${oldSlug}`)) {
			// members/[id] of the old org makes no sense in the new one — go to that section's list
			const rest = path.slice(`/organizations/${oldSlug}`.length).replace(/^\/members\/\d+.*/, '/members');
			goto(`/organizations/${slug}${rest}`);
		}
	}

	function setLocale(l: Locale) {
		locale.set(l);
	}

	async function handleLogout() {
		openMenu = null;
		await auth.logout();
		timer.reset();
		goto('/login');
	}
</script>

<svelte:head>
	<link rel="icon" href={favicon} />
</svelte:head>

<svelte:window onkeydown={(e) => { if (e.key === 'Escape' && openMenu) openMenu = null; }} />

{#snippet languageToggle()}
	<div class="join" role="group" aria-label={t('nav.switchLanguage')}>
		{#each ['de', 'en'] as l (l)}
			<button
				type="button"
				class="join-item btn btn-xs {locale.current === l ? 'btn-neutral' : 'btn-ghost'}"
				aria-pressed={locale.current === l}
				lang={l}
				onclick={() => setLocale(l as Locale)}
			>{l.toUpperCase()}</button>
		{/each}
	</div>
{/snippet}

{#if auth.loading}
	<div class="min-h-screen flex items-center justify-center text-base-content/60">
		<span class="loading loading-spinner loading-md" aria-label={t('common.loading')}></span>
	</div>
{:else if auth.isAuthenticated}
	{#if openMenu}
		<!-- Transparent click-catcher: closes the open popup when clicking anywhere else -->
		<button type="button" class="fixed inset-0 z-40 cursor-default" tabindex="-1" aria-label={t('common.close')} onclick={() => (openMenu = null)}></button>
	{/if}

	<nav class="sticky top-0 z-50 border-b border-base-300 bg-base-100/95 shadow-sm backdrop-blur" aria-label={t('nav.menu')}>
		<div class="mx-auto flex w-full max-w-6xl items-center gap-2 px-4 py-2.5 sm:gap-3">
			<!-- Mobile menu button -->
			<button
				type="button"
				class="btn btn-ghost btn-sm btn-square md:hidden"
				aria-label={t('nav.openMenu')}
				aria-expanded={openMenu === 'mobile'}
				aria-controls="mobile-menu"
				onclick={() => toggleMenu('mobile')}
			>
				<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" aria-hidden="true"><path d="M4 6h16M4 12h16M4 18h16" /></svg>
			</button>

			<!-- Port of @wagnerluca/ui's LogoLockup (accent="time") -->
			<a href="/" class="flex shrink-0 items-center gap-2.5 text-base-content no-underline" aria-label={t('nav.home')}>
				<svg width="28" height="28" viewBox="0 0 100 100" fill="none" aria-hidden="true">
					<rect x="68" y="4" width="20" height="10" rx="5" style="fill: var(--accent-time)" />
					<polyline points="6,26 24,88 42,30 60,88 78,26 78,88 96,88" stroke="currentColor" stroke-width="9" stroke-linecap="round" stroke-linejoin="round" />
				</svg>
				<span class="font-display text-lg font-bold tracking-tighter max-sm:hidden"><span class="opacity-60">WL</span> {t('nav.brand')}</span>
			</a>

			<!-- Org switcher -->
			<div class="relative z-50 min-w-0">
				<button
					type="button"
					class="flex max-w-44 items-center gap-1 truncate rounded-md bg-base-200 px-2.5 py-1 text-xs font-medium text-base-content/70 hover:bg-base-300"
					aria-haspopup="menu"
					aria-expanded={openMenu === 'org'}
					aria-label={orgContext.selectedOrg ? t('nav.org.current', { name: orgContext.selectedOrg.name ?? '' }) : t('nav.org.switch')}
					onclick={() => toggleMenu('org')}
				>
					<span class="truncate">{orgContext.selectedOrg?.name ?? t('nav.org.none')}</span>
					<svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" aria-hidden="true"><path d="m6 9 6 6 6-6" /></svg>
				</button>
				{#if openMenu === 'org'}
					<ul class="menu absolute left-0 top-full mt-2 w-64 rounded-box border border-base-300 bg-base-100 p-2 shadow-xl" role="menu">
						<li class="menu-title">{t('nav.org.switch')}</li>
						{#each orgContext.organizations as org (org.organizationId)}
							<li role="none">
								<button type="button" role="menuitemradio" aria-checked={org.organizationId === orgContext.selectedOrgId} class={org.organizationId === orgContext.selectedOrgId ? 'menu-active' : ''} onclick={() => switchOrg(org.organizationId!, org.slug)}>
									<span class="flex-1 truncate">{org.name}</span>
									<span class="text-xs text-base-content/50">{roleLabel(org.role)}</span>
								</button>
							</li>
						{/each}
						<li role="none"><a role="menuitem" href="/organizations">{orgContext.organizations.length ? t('nav.org.manage') : t('nav.org.create')}</a></li>
					</ul>
				{/if}
			</div>

			<div class="hidden flex-1 items-center gap-1 md:flex lg:gap-2">
				{#each navLinks as link (link.href)}
					<a href={link.href} class={navLinkClass(link.href)} aria-current={isActive(link.href) ? 'page' : undefined}>{link.label}</a>
				{/each}
			</div>

			<div class="ml-auto flex items-center gap-1.5 sm:gap-2">
				<!-- Running timer indicator -->
				{#if timer.current}
					<a href="/time" class="flex items-center gap-1.5 rounded-full bg-success/15 px-2.5 py-1 text-xs font-semibold tabular-nums text-success no-underline hover:bg-success/25" title={t('nav.timerRunning', { elapsed: formatClock(timer.elapsedSeconds) })} aria-label={t('nav.timerRunning', { elapsed: formatClock(timer.elapsedSeconds) })}>
						<span class="h-2 w-2 rounded-full bg-success animate-pulse" aria-hidden="true"></span>
						<span aria-hidden="true">{formatClock(timer.elapsedSeconds)}</span>
					</a>
				{/if}

				<!-- Notification bell -->
				<div class="relative z-50">
					<button type="button" class="btn btn-ghost btn-sm btn-square relative" onclick={() => toggleMenu('notifications')} aria-expanded={openMenu === 'notifications'} aria-label={totalNotifCount > 0 ? t('nav.notifications.openCount', { count: totalNotifCount }) : t('nav.notifications.open')}>
						<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
							<path d="M18 8A6 6 0 0 0 6 8c0 7-3 9-3 9h18s-3-2-3-9"></path>
							<path d="M13.73 21a2 2 0 0 1-3.46 0"></path>
						</svg>
						{#if totalNotifCount > 0}
							<span class="badge badge-error badge-xs absolute -top-1 -right-1" aria-hidden="true">{totalNotifCount}</span>
						{/if}
					</button>

					{#if openMenu === 'notifications'}
						<div class="absolute top-full mt-2 right-0 w-80 max-w-[calc(100vw-2rem)] max-h-[420px] bg-base-100 border border-base-300 rounded-xl shadow-xl overflow-hidden flex flex-col">
							<div class="flex items-center justify-between px-4 py-3 border-b border-base-200">
								<span class="font-semibold text-sm">{t('nav.notifications.title')}</span>
								<button type="button" class="btn btn-ghost btn-xs btn-square" aria-label={t('common.close')} onclick={() => (openMenu = null)}>&times;</button>
							</div>

							<div class="overflow-y-auto">
								<!-- Notifications: timers stopped at the business day end, reminders, settings changes -->
								{#if bellNotifs.length > 0}
									<div class="py-1">
										<div class="px-3 py-2">
											<span class="text-xs font-bold uppercase tracking-wider text-base-content/40">{t('nav.notifications.alerts')}</span>
										</div>
										{#each bellNotifs as n (n.id)}
											{@const target = notificationTarget(n, failures)}
											<button type="button" class="flex items-center justify-between px-4 py-2.5 border-b border-base-200 w-full bg-transparent cursor-pointer text-left hover:bg-base-200/50 transition-colors border-l-4 {n.type === 'FailureEntry' ? 'border-l-error' : n.type === 'TimerReminder' ? 'border-l-warning' : 'border-l-info'}" onclick={() => openNotif(n)}>
												<div class="flex flex-col gap-0.5 flex-1 min-w-0">
													<span class="font-semibold text-xs {n.type === 'FailureEntry' ? 'text-error' : 'text-base-content'}">{notifTitle(n)}</span>
													<span class="text-xs text-base-content/60 break-words">
														{#if n.type === 'FailureEntry'}
															{target.date ? t('nav.notifications.failureStarted', { date: formatDate(target.date) }) : t('nav.notifications.failureCorrect')}
														{:else if n.type === 'TimerReminder'}
															{t('nav.notifications.reminderText')}
														{:else}
															{n.message}
														{/if}
													</span>
													<span class="text-[0.7rem] text-base-content/40">{formatTimeAgo(n.createdAt)}</span>
												</div>
												<span class="text-base-content/40 text-lg flex-shrink-0" aria-hidden="true">{target.href ? '›' : '✓'}</span>
											</button>
										{/each}
									</div>
								{/if}

								<!-- User notifications (responses to my requests) -->
								{#if userNotifRequests.length > 0}
									<div class="py-1">
										<div class="flex items-center justify-between px-3 py-2">
											<span class="text-xs font-bold uppercase tracking-wider text-base-content/40">{t('nav.notifications.myUpdates')}</span>
											<button type="button" class="btn btn-ghost btn-xs text-primary" onclick={() => dismissUserNotifs(userNotifRequests.map((r) => r.id!))}>{t('nav.notifications.dismissAll')}</button>
										</div>
										{#each userNotifRequests as req (req.id)}
											<button type="button" class="flex items-center justify-between px-4 py-2.5 border-b border-base-200 w-full bg-transparent cursor-pointer text-left hover:bg-base-200/50 transition-colors border-l-4 {req.status === 'Accepted' ? 'border-l-success' : req.status === 'Declined' ? 'border-l-error' : 'border-l-transparent'}" onclick={() => openNotifDetail(req)}>
												<div class="flex flex-col gap-0.5 flex-1 min-w-0">
													<span class="badge badge-xs {statusBadgeClass(req.status)}">{formatRequestStatus(req.status)}</span>
													<span class="font-semibold text-xs text-primary">{formatRequestType(req.type)}</span>
													<span class="text-xs text-base-content/50">{req.organizationName}</span>
													{#if req.respondedByName}
														<span class="text-xs text-base-content/40 italic truncate">{t('nav.notifications.by', { name: req.respondedByName })}</span>
													{/if}
													{#if req.responseReason}
														<span class="text-xs text-base-content/60 truncate">{t('common.reason', { reason: req.responseReason })}</span>
													{/if}
													<span class="text-[0.7rem] text-base-content/40">{formatTimeAgo(req.respondedAt)}</span>
												</div>
												<span class="text-base-content/40 text-lg flex-shrink-0" aria-hidden="true">›</span>
											</button>
										{/each}
									</div>
								{/if}

								<!-- Admin pending requests -->
								{#if pendingRequests.length > 0}
									<div class="py-1">
										<div class="px-3 py-2">
											<span class="text-xs font-bold uppercase tracking-wider text-base-content/40">{t('nav.notifications.pending')}</span>
										</div>
										{#each pendingRequests as req (req.id)}
											<button type="button" class="flex items-center justify-between px-4 py-2.5 border-b border-base-200 w-full bg-transparent cursor-pointer text-left hover:bg-base-200/50 transition-colors" onclick={() => openNotifDetail(req)}>
												<div class="flex flex-col gap-0.5 flex-1 min-w-0">
													<span class="font-semibold text-xs text-primary">{formatRequestType(req.type)}</span>
													<span class="text-sm font-medium">{req.userFirstName} {req.userLastName}</span>
													<span class="text-xs text-base-content/50">{req.organizationName}</span>
													{#if req.groupName}
														<span class="text-xs text-base-content/50">{t('nav.notifications.via', { group: req.groupName })}</span>
													{/if}
													{#if req.message}
														<span class="text-xs text-base-content/40 italic truncate">"{req.message}"</span>
													{/if}
													<span class="text-[0.7rem] text-base-content/40">{formatTimeAgo(req.createdAt)}</span>
												</div>
												<span class="text-base-content/40 text-lg flex-shrink-0" aria-hidden="true">›</span>
											</button>
										{/each}
									</div>
								{/if}

								{#if totalNotifCount === 0}
									<div class="py-8 px-4 text-center text-base-content/40 text-sm">{t('nav.notifications.empty')}</div>
								{/if}
							</div>
						</div>
					{/if}
				</div>

				<div class="max-sm:hidden">{@render languageToggle()}</div>

				<!-- User menu -->
				<div class="relative z-50">
					<button type="button" class="btn btn-ghost btn-sm max-w-40 truncate font-medium" aria-haspopup="menu" aria-expanded={openMenu === 'user'} aria-label={t('nav.userMenu')} onclick={() => toggleMenu('user')}>
						<span class="truncate max-sm:hidden">{auth.user?.firstName} {auth.user?.lastName}</span>
						<span class="sm:hidden avatar avatar-placeholder"><span class="w-7 rounded-full bg-base-200 text-xs">{(auth.user?.firstName?.[0] ?? '') + (auth.user?.lastName?.[0] ?? '')}</span></span>
					</button>
					{#if openMenu === 'user'}
						<ul class="menu absolute right-0 top-full mt-2 w-56 rounded-box border border-base-300 bg-base-100 p-2 shadow-xl" role="menu">
							<li class="menu-title truncate">{auth.user?.email}</li>
							<li role="none"><a role="menuitem" href="/settings" class={isActive('/settings') ? 'menu-active' : ''}>{t('nav.settings')}</a></li>
							<li role="none" class="sm:hidden"><div class="flex items-center justify-between">{t('nav.language')} {@render languageToggle()}</div></li>
							<li role="none"><button type="button" role="menuitem" onclick={handleLogout}>{t('nav.signOut')}</button></li>
						</ul>
					{/if}
				</div>
			</div>
		</div>

		<!-- Mobile menu -->
		{#if openMenu === 'mobile'}
			<ul id="mobile-menu" class="menu relative z-50 w-full border-t border-base-300 bg-base-100 px-4 py-2 md:hidden">
				<li><a href="/" class={isActive('/') ? 'menu-active' : ''} aria-current={isActive('/') ? 'page' : undefined}>{t('nav.home')}</a></li>
				{#each navLinks as link (link.href)}
					<li><a href={link.href} class={isActive(link.href) ? 'menu-active' : ''} aria-current={isActive(link.href) ? 'page' : undefined}>{link.label}</a></li>
				{/each}
				<li><a href="/settings" class={isActive('/settings') ? 'menu-active' : ''}>{t('nav.settings')}</a></li>
			</ul>
		{/if}
	</nav>

	<!-- Notification detail -->
	<Modal open={!!detailReq} title={detailReq ? formatRequestTypeFull(detailReq.type) : ''} onclose={() => { detailReq = null; declining = false; }}>
		{#if detailReq}
			{#snippet row(label: string, value: string, italic = false)}
				<div class="flex justify-between items-baseline gap-3 py-1.5 border-b border-base-200/50 last:border-b-0">
					<span class="text-xs text-base-content/50 font-medium shrink-0">{label}</span>
					<span class="text-sm font-medium text-right {italic ? 'italic text-base-content/50' : ''}">{value}</span>
				</div>
			{/snippet}
			<dl class="m-0">
				{#if detailReq.status}
					<div class="flex justify-between items-baseline gap-3 py-1.5 border-b border-base-200/50">
						<span class="text-xs text-base-content/50 font-medium shrink-0">{t('nav.notifications.status')}</span>
						<span class="badge badge-sm {statusBadgeClass(detailReq.status)}">{formatRequestStatus(detailReq.status)}</span>
					</div>
				{/if}
				{#if detailReq.userFirstName || detailReq.userLastName}
					{@render row(t('nav.notifications.user'), `${detailReq.userFirstName ?? ''} ${detailReq.userLastName ?? ''}`)}
				{/if}
				{#if detailReq.organizationName}{@render row(t('nav.notifications.organization'), detailReq.organizationName)}{/if}
				{#if detailReq.groupName}{@render row(t('nav.notifications.routedVia'), detailReq.groupName)}{/if}
				{#if detailReq.createdAt}{@render row(t('nav.notifications.created'), formatDateTime(detailReq.createdAt))}{/if}
				{#if detailReq.respondedAt}{@render row(t('nav.notifications.responded'), formatDateTime(detailReq.respondedAt))}{/if}
				{#if detailReq.respondedByName}{@render row(t('nav.notifications.respondedBy'), detailReq.respondedByName)}{/if}
				{#if detailReq.message}{@render row(t('nav.notifications.message'), `"${detailReq.message}"`, true)}{/if}
				{#if detailReq.responseReason}{@render row(t('common.declineReason'), detailReq.responseReason)}{/if}
			</dl>

			{@const dataLines = parseRequestData(detailReq.type, detailReq.requestData)}
			{#if dataLines.length > 0}
				<div class="mt-3 pt-3 border-t border-base-300">
					<span class="block text-xs font-bold uppercase tracking-wider text-base-content/40 mb-2">{t('nav.notifications.details')}</span>
					{#each dataLines as line, i (i)}
						<div class="text-sm font-medium py-1">{line}</div>
					{/each}
				</div>
			{/if}
		{/if}

		{#snippet actions()}
			{#if detailReq?.status === 'Pending' && pendingRequests.some((r) => r.id === detailReq?.id) && declining}
				<DeclineForm busy={responding} onconfirm={(reason) => respondToRequest(detailReq!, false, reason)} oncancel={() => (declining = false)} />
			{:else if detailReq?.status === 'Pending' && pendingRequests.some((r) => r.id === detailReq?.id)}
				<button type="button" class="btn btn-error btn-outline" disabled={responding} onclick={() => (declining = true)}>{t('nav.notifications.decline')}</button>
				<button type="button" class="btn btn-success" disabled={responding} onclick={() => respondToRequest(detailReq!, true)}>{t('nav.notifications.accept')}</button>
			{:else if detailReq && userNotifRequests.some((r) => r.id === detailReq?.id)}
				<button type="button" class="btn btn-ghost" onclick={() => dismissUserNotifs([detailReq!.id!])}>{t('nav.notifications.dismiss')}</button>
			{:else}
				<button type="button" class="btn btn-ghost" onclick={() => (detailReq = null)}>{t('common.close')}</button>
			{/if}
		{/snippet}
	</Modal>

	<main class="max-w-5xl mx-auto px-4 py-6 sm:px-6 sm:py-8">
		{#if requires2faSetup && !page.url.pathname.startsWith('/settings')}
			<div class="flex items-center justify-center min-h-[60vh]">
				<div class="card bg-base-100 border border-warning shadow-lg w-full max-w-md">
					<div class="card-body text-center">
						<div class="text-4xl mb-3" aria-hidden="true">🔐</div>
						<h2 class="card-title justify-center text-lg">{t('nav.twoFactor.title')}</h2>
						<p class="text-sm text-base-content/60 mt-2">{t('nav.twoFactor.text')}</p>
						<div class="mt-4">
							<a href="/settings" class="btn btn-primary">{t('nav.twoFactor.goToSettings')}</a>
						</div>
					</div>
				</div>
			</div>
		{:else}
			{#if showEmailHint}
				<div role="status" class="alert alert-info mb-6 max-sm:alert-vertical text-sm">
					<span>{t('common.emailVerify.notConfirmed')}</span>
					<div class="flex gap-2">
						<button type="button" class="btn btn-sm" onclick={resendVerification} disabled={resendingEmail}>
							{resendingEmail ? t('common.emailVerify.resending') : t('common.emailVerify.resend')}
						</button>
						<button type="button" class="btn btn-sm btn-ghost btn-square" onclick={() => (emailHintDismissed = true)} aria-label={t('common.emailVerify.dismiss')} title={t('common.emailVerify.dismiss')}>✕</button>
					</div>
				</div>
			{/if}
			{#if failures.length > 0}
				<div role="alert" class="alert alert-error mb-6 max-sm:alert-vertical">
					<span>
						<strong>{tp('nav.failure.title', failures.length)}</strong>
						{tp('nav.failure.text', failures.length)}
					</span>
					<a href={failureLink} class="btn btn-sm">{t('nav.failure.correct')}</a>
				</div>
			{/if}
			{@render children?.()}
		{/if}
	</main>
{:else if isPublicRoute}
	{@render children?.()}
{:else}
	<!-- Protected page while signed out: render nothing (no API calls) until the redirect to /login lands -->
	<div class="min-h-screen flex items-center justify-center text-base-content/60">
		<span class="loading loading-spinner loading-md"></span>
	</div>
{/if}

<ConfirmDialog />
<Toaster />
