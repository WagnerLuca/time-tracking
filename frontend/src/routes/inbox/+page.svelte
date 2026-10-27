<script lang="ts">
	import { untrack } from 'svelte';
	import { auth } from '$lib/stores/auth.svelte';
	import { orgContext } from '$lib/stores/orgContext.svelte';
	import { inbox } from '$lib/stores/inbox.svelte';
	import { toast } from '$lib/stores/toast.svelte';
	import { confirmDialog } from '$lib/stores/confirm.svelte';
	import { invalidateOrgDetail } from '$lib/stores/orgDetail';
	import { requestsApi } from '$lib/apiClient';
	import type { NotificationResponse, OrgRequestResponse } from '$lib/api';
	import { coreHoursNotice, notificationTarget, vacationExpiryNotice } from '$lib/utils/notifications';
	import { dateKey } from '$lib/utils/dateHelpers';
	import { formatDate, formatDateTime, formatNumber, formatRequestStatus, formatRequestType, formatTimeAgo, parseRequestData, statusBadgeClass } from '$lib/utils/formatters';
	import { t } from '$lib/i18n';
	import DeclineForm from '$lib/components/DeclineForm.svelte';
	import EmptyState from '$lib/components/EmptyState.svelte';
	import Skeleton from '$lib/components/Skeleton.svelte';
	import LockIcon from '$lib/components/LockIcon.svelte';
	import { formatMonth } from '$lib/utils/closedPeriods';
	import { isOrgAdmin } from '$lib/utils/orgRules';

	let respondingId = $state<number | null>(null);
	let decliningId = $state<number | null>(null);
	let withdrawingId = $state<number | null>(null);

	async function withdraw(req: OrgRequestResponse) {
		const type = formatRequestType(req.type);
		const ok = await confirmDialog({
			title: t('inbox.own.confirmTitle'),
			message: t('inbox.own.confirmMessage', { type }),
			confirmLabel: t('inbox.own.withdraw'),
			danger: true
		});
		if (!ok) return;
		withdrawingId = req.id ?? null;
		try {
			await inbox.withdraw(req);
			toast.success(t('inbox.own.withdrawn'));
		} catch (err) {
			toast.error(err, t('inbox.own.withdrawFailed'));
			await inbox.load(); // e.g. decided meanwhile: show the current state
		} finally {
			withdrawingId = null;
		}
	}

	// Decided requests of the selected org (only approvers get them; 403 = not an approver)
	let history = $state<OrgRequestResponse[]>([]);
	$effect(() => {
		const slug = orgContext.selectedOrgSlug;
		untrack(() => loadHistory(slug));
	});
	async function loadHistory(slug: string | null) {
		history = [];
		if (!slug) return;
		try {
			const { data } = await requestsApi.apiV1OrganizationsSlugRequestsGet(slug, undefined, undefined, 20);
			if (slug === orgContext.selectedOrgSlug) history = (data.items ?? []).filter((r) => r.status !== 'Pending');
		} catch {
			// Not an approver in this org: no history section
		}
	}

	$effect(() => {
		if (auth.isAuthenticated) untrack(() => inbox.load());
	});

	const nothing = $derived(inbox.loaded && inbox.count === 0 && inbox.ownOpen.length === 0);

	async function respond(req: OrgRequestResponse, accept: boolean, reason?: string) {
		if (!req.organizationSlug || !req.id) return;
		const name = `${req.userFirstName ?? ''} ${req.userLastName ?? ''}`.trim();
		respondingId = req.id;
		try {
			await requestsApi.apiV1OrganizationsSlugRequestsIdPut(req.organizationSlug, req.id, { accept, reason });
			invalidateOrgDetail(req.organizationSlug); // e.g. vacation counts, new members
			toast.success(accept ? t('inbox.approve.accepted', { name }) : t('inbox.approve.declined', { name }));
			decliningId = null;
			await Promise.all([inbox.load(), loadHistory(orgContext.selectedOrgSlug)]);
			if (accept && req.type === 'JoinOrganization' && auth.user?.id) orgContext.loadOrganizations(auth.user.id);
		} catch (err) {
			toast.error(err, t('inbox.approve.failed'));
		} finally {
			respondingId = null;
		}
	}

	function noticeTitle(n: NotificationResponse): string {
		switch (n.type) {
			case 'FailureEntry': return t('nav.notifications.failureTitle');
			case 'TimerReminder': return t('nav.notifications.reminderTitle');
			case 'SettingsChanged': return t('nav.notifications.settingsTitle');
			case 'CoreHoursViolation': return t('nav.notifications.coreHoursTitle');
			case 'VacationExpiry': return t('nav.notifications.vacationExpiryTitle');
			default: return n.title ?? '';
		}
	}

	function noticeText(n: NotificationResponse, date: string | null): string {
		if (n.type === 'FailureEntry') return date ? t('nav.notifications.failureStarted', { date: formatDate(date) }) : t('nav.notifications.failureCorrect');
		if (n.type === 'TimerReminder') return t('nav.notifications.reminderText');
		if (n.type === 'CoreHoursViolation') {
			const { date: day, name, late } = coreHoursNotice(n, auth.user?.id);
			const d = day ? formatDate(day) : '';
			const text = name ? t('nav.notifications.coreHoursOther', { name, date: d }) : t('nav.notifications.coreHoursOwn', { date: d });
			return late ? `${text} ${t('nav.notifications.coreHoursLate')}` : text;
		}
		if (n.type === 'VacationExpiry') {
			const v = vacationExpiryNotice(n.message);
			if (v) return t('nav.notifications.vacationExpiryText', { left: formatNumber(v.left), lapsing: formatNumber(v.lapsing), date: formatDate(v.date) });
		}
		return n.message ?? '';
	}

	function noticeTarget(n: NotificationResponse) {
		if (n.type === 'SettingsChanged') {
			const slug = orgContext.organizations.find((o) => o.organizationId === n.organizationId)?.slug;
			return { href: slug ? `/organizations/${slug}/settings` : null, date: null };
		}
		return notificationTarget(n, inbox.failures, auth.user?.id, (id) => orgContext.organizations.find((o) => o.organizationId === id)?.slug);
	}

	async function markDone(n: NotificationResponse) {
		try {
			if (n.id != null) await inbox.markRead([n.id]);
		} catch (err) {
			toast.error(err, t('inbox.notices.failed'));
		}
	}

	async function dismissDecisions(ids: number[]) {
		try {
			await inbox.dismissDecisions(ids);
		} catch (err) {
			toast.error(err, t('inbox.decisions.failed'));
		}
	}
</script>

<svelte:head>
	<title>{t('common.pageTitle', { page: t('inbox.title') })}</title>
</svelte:head>

{#snippet requestDetails(req: OrgRequestResponse)}
	{@const lines = parseRequestData(req.type, req.requestData)}
	{#if lines.length > 0}
		<div class="mt-1 text-sm">
			{#each lines as line, i (i)}<p class="m-0">{line}</p>{/each}
		</div>
	{/if}
	{#if req.message}<p class="m-0 mt-1 text-sm italic text-base-content/70 break-words">„{req.message}“</p>{/if}
{/snippet}

<div class="max-w-3xl mx-auto flex flex-col gap-6">
	<header>
		<h1 class="text-2xl font-bold m-0">{t('inbox.title')}</h1>
		<p class="text-sm text-base-content/70 mt-1 mb-0">{t('inbox.intro')}</p>
	</header>

	{#if !inbox.loaded}
		<Skeleton lines={2} />
	{:else if nothing}
		<EmptyState text={t('inbox.allDone')} actionLabel={t('inbox.toDashboard')} href="/" />
	{/if}

	{#if inbox.toApprove.length > 0}
		<section aria-labelledby="inbox-approve" data-testid="inbox-approve">
			<h2 id="inbox-approve" class="text-base font-semibold mb-2">{t('inbox.approve.title')} <span class="badge badge-error badge-sm">{inbox.toApprove.length}</span></h2>
			<ul class="flex flex-col gap-3 m-0 p-0 list-none">
				{#each inbox.toApprove as req (req.id)}
					{@const closedLead = !!req.closedMonth && !isOrgAdmin(orgContext.organizations.find((o) => o.organizationId === req.organizationId)?.role)}
					<li class="card bg-base-100 border border-base-300 p-4" data-testid="inbox-item">
						<div class="flex flex-wrap items-baseline justify-between gap-x-3 gap-y-1">
							<span class="font-semibold">{req.userFirstName} {req.userLastName} · <span class="text-primary">{formatRequestType(req.type)}</span></span>
							<span class="text-xs text-base-content/70">{formatTimeAgo(req.createdAt)}</span>
						</div>
						<p class="m-0 text-xs text-base-content/70">{req.organizationName}{#if req.groupName} · {t('inbox.approve.via', { group: req.groupName })}{/if}</p>
						{@render requestDetails(req)}
						{#if req.closedMonth}
							<p class="m-0 mt-2 text-xs flex items-center gap-1 {closedLead ? 'text-warning' : 'text-base-content/70'}" data-testid="request-closed-hint">
								<LockIcon title={formatMonth(req.closedMonth)} />
								{t(closedLead ? 'closedPeriods.requestHintLead' : 'closedPeriods.requestHint', { month: formatMonth(req.closedMonth) })}
							</p>
						{/if}
						<div class="mt-3 flex flex-wrap gap-2 border-t border-base-200 pt-3">
							{#if decliningId === req.id}
								<DeclineForm busy={respondingId === req.id} onconfirm={(reason) => respond(req, false, reason)} oncancel={() => (decliningId = null)} />
							{:else}
								<button type="button" class="btn btn-success btn-sm max-sm:flex-1" disabled={respondingId === req.id || closedLead} onclick={() => respond(req, true)}>{t('inbox.approve.accept')}</button>
								<button type="button" class="btn btn-outline btn-error btn-sm max-sm:flex-1" disabled={respondingId === req.id} onclick={() => (decliningId = req.id ?? null)}>{t('inbox.approve.decline')}</button>
							{/if}
						</div>
					</li>
				{/each}
			</ul>
		</section>
	{/if}

	{#if inbox.failures.length > 0}
		<section aria-labelledby="inbox-fix" data-testid="inbox-fix">
			<h2 id="inbox-fix" class="text-base font-semibold mb-2">{t('inbox.fix.title')}</h2>
			<ul class="flex flex-col gap-2 m-0 p-0 list-none">
				{#each inbox.failures as f (f.id)}
					{@const day = f.startTime ? dateKey(new Date(f.startTime)) : null}
					<li class="card flex-row flex-wrap items-center justify-between gap-3 border border-error/40 bg-error/5 p-4">
						<div class="min-w-0">
							<p class="m-0 font-semibold text-error">{t('inbox.fix.failure', { date: f.startTime ? formatDateTime(f.startTime) : '' })}</p>
							<p class="m-0 text-xs text-base-content/70">{f.description ? `${f.description} · ` : ''}{t('inbox.fix.failureHint')}</p>
						</div>
						<a class="btn btn-sm btn-primary max-sm:w-full" href={day ? `/history?date=${day}&entry=${f.id}` : '/history'}>{t('inbox.fix.correct')}</a>
					</li>
				{/each}
			</ul>
		</section>
	{/if}

	{#if inbox.notices.length > 0}
		<section aria-labelledby="inbox-notices" data-testid="inbox-notices">
			<h2 id="inbox-notices" class="text-base font-semibold mb-2">{t('inbox.notices.title')}</h2>
			<ul class="flex flex-col gap-2 m-0 p-0 list-none">
				{#each inbox.notices as n (n.id)}
					{@const target = noticeTarget(n)}
					<li class="card flex-row flex-wrap items-center justify-between gap-3 border border-base-300 bg-base-100 p-4 border-l-4 {n.type === 'CoreHoursViolation' || n.type === 'TimerReminder' ? 'border-l-warning' : 'border-l-info'}">
						<div class="min-w-0 flex-1">
							<p class="m-0 font-semibold">{noticeTitle(n)}</p>
							<p class="m-0 text-sm text-base-content/70 break-words">{noticeText(n, target.date)}</p>
							<p class="m-0 text-xs text-base-content/70">{formatTimeAgo(n.createdAt)}</p>
						</div>
						<div class="flex gap-2 max-sm:w-full">
							{#if target.href}<a class="btn btn-sm btn-outline max-sm:flex-1" href={target.href} onclick={() => markDone(n)}>{t('inbox.notices.open')}</a>{/if}
							<button type="button" class="btn btn-sm btn-ghost max-sm:flex-1" aria-label={t('inbox.notices.doneLabel', { title: noticeTitle(n) })} onclick={() => markDone(n)}>{t('inbox.notices.done')}</button>
						</div>
					</li>
				{/each}
			</ul>
		</section>
	{/if}

	{#if inbox.decisions.length > 0}
		<section aria-labelledby="inbox-decisions" data-testid="inbox-decisions">
			<div class="mb-2 flex flex-wrap items-center justify-between gap-2">
				<h2 id="inbox-decisions" class="text-base font-semibold m-0">{t('inbox.decisions.title')}</h2>
				{#if inbox.decisions.length > 1}
					<button type="button" class="btn btn-ghost btn-sm" onclick={() => dismissDecisions(inbox.decisions.map((r) => r.id!))}>{t('inbox.decisions.dismissAll')}</button>
				{/if}
			</div>
			<ul class="flex flex-col gap-2 m-0 p-0 list-none">
				{#each inbox.decisions as req (req.id)}
					<li class="card flex-row flex-wrap items-start justify-between gap-3 border border-base-300 bg-base-100 p-4 border-l-4 {req.status === 'Accepted' ? 'border-l-success' : 'border-l-error'}">
						<div class="min-w-0 flex-1">
							<p class="m-0 flex flex-wrap items-center gap-2">
								<span class="badge badge-sm {statusBadgeClass(req.status)}">{formatRequestStatus(req.status)}</span>
								<span class="font-semibold">{formatRequestType(req.type)}</span>
								<span class="text-xs text-base-content/70">{req.organizationName} · {formatTimeAgo(req.respondedAt)}</span>
							</p>
							{@render requestDetails(req)}
							{#if req.respondedByName}<p class="m-0 mt-1 text-xs text-base-content/70">{t('inbox.decisions.by', { status: formatRequestStatus(req.status), name: req.respondedByName })}</p>{/if}
							{#if req.responseReason}<p class="m-0 mt-1 text-sm break-words">{t('common.reason', { reason: req.responseReason })}</p>{/if}
						</div>
						<button type="button" class="btn btn-sm btn-ghost max-sm:w-full" onclick={() => dismissDecisions([req.id!])}>{t('inbox.decisions.dismiss')}</button>
					</li>
				{/each}
			</ul>
		</section>
	{/if}

	{#if inbox.ownOpen.length > 0}
		<section aria-labelledby="inbox-own" data-testid="inbox-own">
			<h2 id="inbox-own" class="text-base font-semibold mb-2">{t('inbox.own.title')}</h2>
			<ul class="flex flex-col gap-2 m-0 p-0 list-none">
				{#each inbox.ownOpen as req (req.id)}
					<li class="card flex-row flex-wrap items-start justify-between gap-3 border border-base-300 bg-base-100 p-4" data-testid="inbox-own-item">
						<div class="min-w-0 flex-1">
							<p class="m-0 flex flex-wrap items-center gap-2">
								<span class="badge badge-sm badge-warning">{formatRequestStatus(req.status)}</span>
								<span class="font-semibold">{formatRequestType(req.type)}</span>
								<span class="text-xs text-base-content/70">{req.organizationName}</span>
							</p>
							{@render requestDetails(req)}
							<p class="m-0 mt-1 text-xs text-base-content/70">{t('inbox.own.waiting', { time: formatTimeAgo(req.createdAt) })}</p>
						</div>
						<button
							type="button"
							class="btn btn-outline btn-error btn-sm max-sm:w-full"
							aria-label={t('inbox.own.withdrawLabel', { type: formatRequestType(req.type) })}
							disabled={withdrawingId === req.id}
							onclick={() => withdraw(req)}
						>{withdrawingId === req.id ? t('inbox.own.withdrawing') : t('inbox.own.withdraw')}</button>
					</li>
				{/each}
			</ul>
		</section>
	{/if}

	{#if history.length > 0}
		<details class="card border border-base-300 bg-base-100 p-4" data-testid="inbox-history">
			<summary class="cursor-pointer font-semibold min-h-11 flex items-center">{t('inbox.history.title')} ({history.length})</summary>
			<p class="text-sm text-base-content/70 mt-1">{t('inbox.history.intro', { org: orgContext.selectedOrg?.name ?? '' })}</p>
			<ul class="flex flex-col gap-2 m-0 mt-2 p-0 list-none">
				{#each history as req (req.id)}
					<li class="border-t border-base-200 pt-2 text-sm">
						<p class="m-0 flex flex-wrap items-center gap-2">
							<span class="badge badge-sm {statusBadgeClass(req.status)}">{formatRequestStatus(req.status)}</span>
							<strong>{req.userFirstName} {req.userLastName}</strong> · {formatRequestType(req.type)}
						</p>
						{@render requestDetails(req)}
						<p class="m-0 text-xs text-base-content/70">{t('orgSettings.requests.respondedBy', { status: formatRequestStatus(req.status), name: req.respondedByName ?? t('common.unknown'), time: formatTimeAgo(req.respondedAt) })}</p>
						{#if req.responseReason}<p class="m-0 text-xs">{t('common.reason', { reason: req.responseReason })}</p>{/if}
					</li>
				{/each}
			</ul>
		</details>
	{/if}
</div>
