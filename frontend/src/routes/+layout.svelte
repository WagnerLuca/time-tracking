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
	import { scale } from 'svelte/transition';
	import { prefersReducedMotion } from 'svelte/motion';
	import { authApi } from '$lib/apiClient';
	import { inbox } from '$lib/stores/inbox.svelte';
	import { getOrgDetail } from '$lib/stores/orgDetail';
	import { canUseAccounting, isOrgAdmin } from '$lib/utils/orgRules';
	import { formatClock, roleLabel } from '$lib/utils/formatters';
	import { dateKey } from '$lib/utils/dateHelpers';
	import { t, tp } from '$lib/i18n';
	import ConfirmDialog from '$lib/components/ConfirmDialog.svelte';
	import Toaster from '$lib/components/Toaster.svelte';
	import NavIcon from '$lib/components/NavIcon.svelte';
	import { dev } from '$app/environment';
	import { network } from '$lib/pwa/network.svelte';
	import { serviceWorker } from '$lib/pwa/serviceWorker.svelte';
	import { install } from '$lib/pwa/install.svelte';
	import { disablePush, syncPush } from '$lib/pwa/push';

	let { children } = $props();

	const publicRoutes = ['/login', '/register', '/forgot-password', '/reset-password', '/verify-email', '/health'];
	const isPublicRoute = $derived(publicRoutes.some((r) => page.url.pathname.startsWith(r)));

	// Which nav popup is open (one at a time); Esc or a click outside closes it
	let openMenu = $state<'org' | 'user' | 'more' | null>(null);
	function toggleMenu(menu: NonNullable<typeof openMenu>) {
		openMenu = openMenu === menu ? null : menu;
	}

	// Pending work (bell count, inbox page, failure banner) lives in the inbox store
	const failures = $derived(inbox.failures);
	const firstFailure = $derived(
		[...failures].sort((a, b) => (a.startTime ?? '').localeCompare(b.startTime ?? ''))[0] ?? null
	);
	/** Deep link into History: opens the affected day and the entry editor for that entry. */
	const failureLink = $derived(
		firstFailure?.startTime ? `/history?date=${dateKey(new Date(firstFailure.startTime))}&entry=${firstFailure.id}` : '/history'
	);

	// Re-check after every navigation (throttled) so the hints disappear once entries are corrected,
	// and the timer indicator picks up timers started elsewhere (e.g. an RFID terminal)
	afterNavigate(() => {
		openMenu = null;
		if (auth.isAuthenticated) {
			inbox.refresh();
			timer.refresh().catch(() => {});
		}
	});

	// The inbox also refreshes every minute while the tab is visible
	onMount(() => {
		const interval = setInterval(() => {
			if (document.visibilityState === 'visible' && auth.isAuthenticated && network.online) inbox.load();
		}, 60_000);
		const onVisible = () => { if (document.visibilityState === 'visible' && auth.isAuthenticated) inbox.refresh(); };
		document.addEventListener('visibilitychange', onVisible);
		return () => {
			clearInterval(interval);
			document.removeEventListener('visibilitychange', onVisible);
		};
	});

	/** Approvers (org admins, group admins) get "Anfragen" in the main navigation. */
	let isGroupAdmin = $state(false);
	$effect(() => {
		const slug = orgContext.selectedOrgSlug;
		const userId = auth.user?.id;
		isGroupAdmin = false;
		if (!slug || !userId) return;
		getOrgDetail(slug)
			.then((d) => { if (slug === orgContext.selectedOrgSlug) isGroupAdmin = !!d.members?.find((m) => m.id === userId)?.groups?.some((g) => g.isGroupAdmin); })
			.catch(() => {});
	});
	const canApprove = $derived(isOrgAdmin(orgContext.selectedOrg?.role) || isGroupAdmin || inbox.toApprove.length > 0);

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
		// PWA (issue #67): the service worker only in production builds / preview
		if (!dev) serviceWorker.register((path) => goto(path)).catch(() => {});
		const stopNetwork = network.listen();
		const stopInstall = install.listen();
		return () => {
			stopNetwork();
			stopInstall();
		};
	});

	// Keep this device's push subscription in step with the account and language (no prompt here)
	$effect(() => {
		const userId = auth.user?.id;
		const lang = locale.current;
		if (auth.isAuthenticated && userId != null) syncPush(userId, lang).catch(() => {});
	});

	// Back online after a read failed: refresh the shared stores (the page remounts via {#key})
	$effect(() => {
		if (network.reconnects > 0 && auth.isAuthenticated && auth.user?.id != null) {
			orgContext.loadOrganizations(auth.user.id);
			inbox.refresh();
			timer.refresh().catch(() => {});
		}
	});

	async function installApp() {
		openMenu = null;
		if (!(await install.prompt())) goto('/settings#app');
	}

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
			inbox.load();
			timer.refresh().catch(() => {});
		}
	});

	function matches(path: string) {
		const current = page.url.pathname;
		return path === '/' ? current === '/' : current === path || current.startsWith(`${path}/`);
	}

	/** Active link: the most specific match, so the team calendar does not also light up "Organisation". */
	function isActive(path: string) {
		return matches(path) && !navLinks.some((l) => l.href.length > path.length && l.href.startsWith(path) && matches(l.href));
	}

	function navLinkClass(path: string) {
		return `whitespace-nowrap rounded-lg px-2 py-1.5 text-sm xl:px-3 font-medium no-underline transition-colors ${isActive(path) ? 'bg-base-200 text-base-content' : 'text-base-content/70 hover:bg-base-200/60 hover:text-base-content'}`;
	}

	/** Everyday destinations first; administration sits behind the one "Organisation" entry. */
	const navLinks = $derived([
		{ href: '/', label: t('nav.home'), icon: 'home' },
		{ href: '/time', label: t('nav.timer'), icon: 'clock' },
		{ href: '/history', label: t('nav.history'), icon: 'list' },
		...(orgContext.selectedOrgSlug ? [{ href: `/organizations/${orgContext.selectedOrgSlug}/absences`, label: t('nav.teamCalendar'), icon: 'calendar' }] : []),
		...(canApprove ? [{ href: '/inbox', label: t('nav.requests'), icon: 'inbox' }] : []),
		...(orgContext.selectedOrgSlug && canUseAccounting(orgContext.selectedOrg)
			? [{ href: `/organizations/${orgContext.selectedOrgSlug}/accounting`, label: t('accounting.title'), icon: 'chart' }]
			: []),
		{ href: orgContext.selectedOrgSlug ? `/organizations/${orgContext.selectedOrgSlug}` : '/organizations', label: t('nav.organization'), icon: 'org' }
	]);
	/** The phone's bottom bar: the four everyday places + "Mehr" (everything else). */
	const bottomLinks = $derived(navLinks.filter((l) => ['home', 'clock', 'list', 'calendar'].includes(l.icon)));
	const moreLinks = $derived(navLinks.filter((l) => !bottomLinks.includes(l)));

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
		// This device stops receiving the account's notifications
		await disablePush().catch(() => {});
		await auth.logout();
		timer.reset();
		inbox.reset();
		goto('/login');
	}
</script>

<svelte:window onkeydown={(e) => { if (e.key === 'Escape' && openMenu) openMenu = null; }} />

{#snippet pwaBanners()}
	{#if !network.online}
		<div role="status" class="alert alert-warning mb-6 max-sm:alert-vertical text-sm" data-testid="offline-banner">
			<span><strong>{t('pwa.offline.title')}</strong> {t('pwa.offline.hint')}</span>
		</div>
	{/if}
	{#if serviceWorker.updateAvailable}
		<div role="status" class="alert alert-info mb-6 text-sm">
			<span>{t('pwa.update.available')}</span>
			<button type="button" class="btn btn-sm" onclick={() => serviceWorker.applyUpdate()}>{t('pwa.update.reload')}</button>
		</div>
	{/if}
{/snippet}

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
	<div class="min-h-screen flex items-center justify-center text-base-content/70">
		<span class="loading loading-spinner loading-md" aria-label={t('common.loading')}></span>
	</div>
{:else if auth.isAuthenticated}
	{#if openMenu}
		<!-- Transparent click-catcher: closes the open popup when clicking anywhere else -->
		<button type="button" class="fixed inset-0 z-40 cursor-default" tabindex="-1" aria-label={t('common.close')} onclick={() => (openMenu = null)}></button>
	{/if}

	<a href="#main" class="sr-only focus:not-sr-only focus:fixed focus:left-4 focus:top-2 focus:z-[60] btn btn-primary btn-sm">{t('nav.skipToContent')}</a>

	<nav class="sticky top-0 z-50 border-b border-base-300 bg-base-100/95 pt-[env(safe-area-inset-top)] shadow-sm backdrop-blur" aria-label={t('nav.menu')}>
		<div class="mx-auto flex w-full max-w-6xl items-center gap-2 py-2 pl-[max(1rem,env(safe-area-inset-left))] pr-[max(1rem,env(safe-area-inset-right))] sm:gap-3">
			<!-- Port of @wagnerluca/ui's LogoLockup (accent="time") -->
			<a href="/" class="flex shrink-0 items-center justify-center gap-2.5 text-base-content no-underline min-h-11 min-w-11" aria-label={t('nav.brandHome')}>
				<svg width="28" height="28" viewBox="0 0 100 100" fill="none" aria-hidden="true">
					<rect x="68" y="4" width="20" height="10" rx="5" style="fill: var(--accent-time)" />
					<polyline points="6,26 24,88 42,30 60,88 78,26 78,88 96,88" stroke="currentColor" stroke-width="9" stroke-linecap="round" stroke-linejoin="round" />
				</svg>
				<!-- Only between sm and lg, where the links are in the bottom bar: the nav is max-w-6xl at every width, so from lg the links need the room -->
				<span class="font-display text-lg font-bold tracking-tighter max-sm:hidden lg:hidden"><span class="opacity-60">WL</span> {t('nav.brand')}</span>
			</a>

			<!-- Org switcher: never squeezed by the links; only a name longer than 11rem truncates -->
			<div class="relative z-50 shrink-0 max-w-44">
				<button
					type="button"
					class="flex min-h-11 max-w-full items-center gap-1 truncate rounded-md bg-base-200 px-2.5 text-xs font-medium text-base-content/80 hover:bg-base-300 lg:min-h-8"
					aria-haspopup="menu"
					aria-expanded={openMenu === 'org'}
					aria-label={orgContext.selectedOrg ? t('nav.org.current', { name: orgContext.selectedOrg.name ?? '' }) : t('nav.org.switch')}
					title={orgContext.selectedOrg?.name ?? undefined}
					onclick={() => toggleMenu('org')}
				>
					<span class="truncate">{orgContext.selectedOrg?.name ?? t('nav.org.none')}</span>
					<svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" aria-hidden="true"><path d="m6 9 6 6 6-6" /></svg>
				</button>
				{#if openMenu === 'org'}
					<ul class="menu popover origin-top-left absolute left-0 top-full mt-2 w-64 rounded-box border border-base-300 bg-base-100 p-2 shadow-xl" role="menu">
						<li class="menu-title">{t('nav.org.switch')}</li>
						{#each orgContext.organizations as org (org.organizationId)}
							<li role="none">
								<button type="button" role="menuitemradio" aria-checked={org.organizationId === orgContext.selectedOrgId} class={org.organizationId === orgContext.selectedOrgId ? 'menu-active' : ''} onclick={() => switchOrg(org.organizationId!, org.slug)}>
									<span class="flex-1 truncate">{org.name}</span>
									<span class="text-xs text-base-content/70">{roleLabel(org.role)}</span>
								</button>
							</li>
						{/each}
						<li role="none"><a role="menuitem" href="/organizations">{orgContext.organizations.length ? t('nav.org.manage') : t('nav.org.create')}</a></li>
					</ul>
				{/if}
			</div>

			<div class="hidden flex-1 items-center gap-1 lg:flex xl:gap-2">
				<!-- "Anfragen" only from xl: between lg and xl there is no room next to a running timer, and the bell opens the same page -->
				{#each navLinks as link (link.href)}
					<a href={link.href} class="{navLinkClass(link.href)} {link.icon === 'inbox' ? 'max-xl:hidden' : ''}" aria-current={isActive(link.href) ? 'page' : undefined}>
						{link.label}{#if link.icon === 'inbox' && inbox.toApprove.length > 0}<span class="badge badge-error badge-xs ml-1.5">{inbox.toApprove.length}</span>{/if}
					</a>
				{/each}
			</div>

			<div class="ml-auto flex items-center gap-1.5 sm:gap-2">
				<!-- Running timer indicator (fades/scales in via .timer-pill, out in 150 ms) -->
				{#if timer.current}
					<a href="/time" out:scale={{ start: prefersReducedMotion.current ? 1 : 0.95, duration: 150 }} class="timer-pill flex min-h-11 items-center gap-1.5 rounded-full bg-success/15 px-2.5 text-xs font-semibold tabular-nums text-success no-underline hover:bg-success/25 lg:min-h-8" title={t('nav.timerRunning', { elapsed: formatClock(timer.elapsedSeconds) })} aria-label={t('nav.timerRunning', { elapsed: formatClock(timer.elapsedSeconds) })}>
						<span class="h-2 w-2 rounded-full bg-success animate-pulse motion-reduce:animate-none" aria-hidden="true"></span>
						<span aria-hidden="true">{formatClock(timer.elapsedSeconds)}</span>
					</a>
				{/if}

				<!-- Bell: everything pending is on the inbox page -->
				<a href="/inbox" class="btn btn-ghost btn-sm btn-square relative max-lg:size-11" aria-current={isActive('/inbox') ? 'page' : undefined} aria-label={inbox.count > 0 ? t('nav.notifications.openCount', { count: inbox.count }) : t('nav.notifications.open')} title={t('nav.notifications.title')}>
					<NavIcon name="bell" />
					{#if inbox.count > 0}
						<span class="badge badge-error badge-xs absolute -top-1 -right-1" aria-hidden="true">{inbox.count}</span>
					{/if}
				</a>

				<!-- From lg the links need the room: the toggle moves into the user menu (below lg it is also in "Mehr") -->
				<div class="max-sm:hidden lg:hidden">{@render languageToggle()}</div>

				<!-- User menu -->
				<div class="relative z-50 max-lg:hidden">
					<button type="button" class="btn btn-ghost btn-sm btn-square" aria-haspopup="menu" aria-expanded={openMenu === 'user'} aria-label={t('nav.userMenu')} title={`${auth.user?.firstName ?? ''} ${auth.user?.lastName ?? ''}`} onclick={() => toggleMenu('user')}>
						<!-- An icon, not the name: the bar is max-w-6xl at every width and the links, timer and org name come first -->
						<NavIcon name="user" />
					</button>
					{#if openMenu === 'user'}
						<ul class="menu popover origin-top-right absolute right-0 top-full mt-2 w-56 rounded-box border border-base-300 bg-base-100 p-2 shadow-xl" role="menu">
							<li class="menu-title truncate">{auth.user?.firstName} {auth.user?.lastName}<br /><span class="font-normal">{auth.user?.email}</span></li>
							<li role="none"><a role="menuitem" href="/settings" class={isActive('/settings') ? 'menu-active' : ''}>{t('nav.settings')}</a></li>
							<li role="none"><div class="flex items-center justify-between">{t('nav.language')} {@render languageToggle()}</div></li>
							<li role="none"><button type="button" role="menuitem" onclick={handleLogout}>{t('nav.signOut')}</button></li>
						</ul>
					{/if}
				</div>
			</div>
		</div>
	</nav>

	<!-- Phone: bottom navigation within thumb reach; "Mehr" opens the rest right above it -->
	{#if openMenu === 'more'}
		<div id="more-menu" class="popover origin-bottom fixed inset-x-0 bottom-[calc(4rem+env(safe-area-inset-bottom))] z-50 border-t border-base-300 bg-base-100 px-4 py-2 shadow-xl lg:hidden">
			<p class="m-0 px-3 pt-1 text-xs text-base-content/70 truncate">{auth.user?.firstName} {auth.user?.lastName} · {auth.user?.email}</p>
			<ul class="menu w-full">
				{#each moreLinks as link (link.href)}
					<li>
						<a href={link.href} class="min-h-11 {isActive(link.href) ? 'menu-active' : ''}" aria-current={isActive(link.href) ? 'page' : undefined}>
							<NavIcon name={link.icon} /><span class="flex-1">{link.label}</span>
							{#if link.icon === 'inbox' && inbox.toApprove.length > 0}<span class="badge badge-error badge-xs">{inbox.toApprove.length}</span>{/if}
						</a>
					</li>
				{/each}
				<li><a href="/settings" class="min-h-11 {isActive('/settings') ? 'menu-active' : ''}" aria-current={isActive('/settings') ? 'page' : undefined}><NavIcon name="settings" />{t('nav.settings')}</a></li>
				{#if !install.installed}
					<li><button type="button" class="min-h-11" onclick={installApp}><NavIcon name="download" />{t('pwa.install.button')}</button></li>
				{/if}
				<li><div class="flex min-h-11 items-center justify-between">{t('nav.language')} {@render languageToggle()}</div></li>
				<li><button type="button" class="min-h-11" onclick={handleLogout}><NavIcon name="logout" />{t('nav.signOut')}</button></li>
			</ul>
		</div>
	{/if}
	<nav class="fixed inset-x-0 bottom-0 z-50 border-t border-base-300 bg-base-100/95 pb-[env(safe-area-inset-bottom)] backdrop-blur lg:hidden" aria-label={t('nav.bottom')}>
		<ul class="mx-auto grid max-w-lg grid-cols-5">
			{#each bottomLinks as link (link.href)}
				<li>
					<a href={link.href} class="flex h-16 flex-col items-center justify-center gap-0.5 text-[11px] font-medium no-underline {isActive(link.href) ? 'text-primary' : 'text-base-content/75'}" aria-current={isActive(link.href) ? 'page' : undefined}>
						<NavIcon name={link.icon} />
						<span class="max-w-full truncate px-1">{link.icon === 'calendar' ? t('nav.teamShort') : link.label}</span>
					</a>
				</li>
			{/each}
			<li class="col-start-5">
				<button type="button" class="relative flex h-16 w-full flex-col items-center justify-center gap-0.5 text-[11px] font-medium {openMenu === 'more' ? 'text-primary' : 'text-base-content/75'}" aria-expanded={openMenu === 'more'} aria-controls="more-menu" onclick={() => toggleMenu('more')}>
					<NavIcon name="more" />
					<span>{t('nav.more')}</span>
					{#if inbox.toApprove.length > 0}<span class="badge badge-error badge-xs absolute top-2 right-[calc(50%-1.25rem)]" aria-hidden="true">{inbox.toApprove.length}</span>{/if}
				</button>
			</li>
		</ul>
	</nav>

	<main id="main" tabindex="-1" class="max-w-5xl mx-auto px-4 pt-6 pb-[calc(6rem+env(safe-area-inset-bottom))] sm:px-6 sm:pt-8 lg:pb-8 focus:outline-none">
		{@render pwaBanners()}
		{#if requires2faSetup && !page.url.pathname.startsWith('/settings')}
			<div class="flex items-center justify-center min-h-[60vh]">
				<div class="card bg-base-100 border border-warning shadow-lg w-full max-w-md">
					<div class="card-body text-center">
						<div class="text-4xl mb-3" aria-hidden="true">🔐</div>
						<h2 class="card-title justify-center text-lg">{t('nav.twoFactor.title')}</h2>
						<p class="text-sm text-base-content/70 mt-2">{t('nav.twoFactor.text')}</p>
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
			{#if failures.length > 0 && !isActive('/inbox')}
				<div role="alert" class="alert alert-error mb-6 max-sm:alert-vertical">
					<span>
						<strong>{tp('nav.failure.title', failures.length)}</strong>
						{tp('nav.failure.text', failures.length)}
					</span>
					<a href={failureLink} class="btn btn-sm shrink-0 whitespace-nowrap">{t('nav.failure.correct')}</a>
				</div>
			{/if}
			<!-- Offline every control that needs the server is locked; a read that failed offline
			     remounts the page on reconnect so it loads again (lib/pwa/network.svelte.ts) -->
			<fieldset class="contents" disabled={!network.online}>
				{#key network.reconnects}
					{@render children?.()}
				{/key}
			</fieldset>
		{/if}
	</main>
{:else if isPublicRoute}
	<div class="mx-auto max-w-md px-4 pt-4 empty:hidden">{@render pwaBanners()}</div>
	{@render children?.()}
{:else}
	<!-- Protected page while signed out: render nothing (no API calls) until the redirect to /login lands -->
	<div class="min-h-screen flex items-center justify-center text-base-content/70">
		<span class="loading loading-spinner loading-md"></span>
	</div>
{/if}

<ConfirmDialog />
<Toaster />
