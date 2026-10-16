<script lang="ts">
	import { untrack } from 'svelte';
	import { closedPeriodsApi } from '$lib/apiClient';
	import type { AccountingMonthStatus, ClosedPeriodsResponse } from '$lib/api';
	import { invalidateOrgDetail } from '$lib/stores/orgDetail';
	import { confirmDialog, promptDialog } from '$lib/stores/confirm.svelte';
	import { toast } from '$lib/stores/toast.svelte';
	import { extractErrorMessage } from '$lib/utils/errorHandler';
	import { formatDateTime } from '$lib/utils/formatters';
	import { formatMonth, lastClosableMonth, monthKey, monthsBetween } from '$lib/utils/closedPeriods';
	import { closeWarnings } from '$lib/utils/accounting';
	import { t } from '$lib/i18n';

	/**
	 * Auswertungen → Monatsabschluss (issue #57): close a range, reopen one with a reason, the audit log.
	 * Admins and accountants. `statuses` (the year's months from the accounting API) adds a warning when a
	 * month to close still has failure entries or pending requests.
	 */
	let { orgSlug, timeZone, statuses = [], onchanged }: {
		orgSlug: string;
		timeZone?: string | null;
		statuses?: AccountingMonthStatus[];
		onchanged?: () => void;
	} = $props();

	const latest = lastClosableMonth(new Date(), untrack(() => timeZone));
	let data = $state<ClosedPeriodsResponse | null>(null);
	let loadError = $state('');
	let from = $state(latest);
	let to = $state(latest);
	let note = $state('');
	let busy = $state(false);
	const range = $derived(from && to ? monthsBetween(from, to) : []);
	const closedMonths = $derived(data?.months ?? []);
	const events = $derived(data?.events ?? []);

	$effect(() => {
		const s = orgSlug;
		untrack(() => load(s));
	});

	async function load(s: string) {
		loadError = '';
		try {
			data = (await closedPeriodsApi.apiV1OrganizationsSlugClosedPeriodsGet(s)).data;
		} catch (err) {
			loadError = extractErrorMessage(err);
		}
	}

	async function close(e: Event) {
		e.preventDefault();
		if (range.length === 0) return;
		const warnings = range.flatMap((m) => {
			const issues = closeWarnings(statuses.find((s) => monthKey(s.month ?? '') === m));
			return issues.length ? [`${formatMonth(m)}: ${issues.join(', ')}`] : [];
		});
		const message = t('closedPeriods.closeMessage', { months: range.map(formatMonth).join(', ') });
		const ok = await confirmDialog({
			title: t('closedPeriods.closeTitle'),
			message: warnings.length ? `${message} ${t('closedPeriods.openIssues', { issues: warnings.join('; ') })}` : message,
			confirmLabel: warnings.length ? t('accounting.closing.closeAnyway') : t('closedPeriods.close'),
			danger: warnings.length > 0
		});
		if (!ok) return;
		busy = true;
		try {
			data = (await closedPeriodsApi.apiV1OrganizationsSlugClosedPeriodsPost(orgSlug, {
				from: `${from}-01`, to: `${to}-01`, reason: note.trim() || undefined
			})).data;
			note = '';
			invalidateOrgDetail(orgSlug);
			toast.success(t('closedPeriods.closedOk'));
			onchanged?.();
		} catch (err) {
			toast.error(err);
		} finally {
			busy = false;
		}
	}

	async function reopen(month: string) {
		const reason = await promptDialog({
			title: t('closedPeriods.reopenTitle', { month: formatMonth(month) }),
			message: t('closedPeriods.reopenMessage'),
			confirmLabel: t('closedPeriods.reopen'),
			input: { label: t('closedPeriods.reopenLabel'), maxLength: 500 }
		});
		if (!reason) return;
		busy = true;
		try {
			data = (await closedPeriodsApi.apiV1OrganizationsSlugClosedPeriodsReopenPost(orgSlug, { month, reason })).data;
			invalidateOrgDetail(orgSlug);
			toast.success(t('closedPeriods.reopened', { month: formatMonth(month) }));
			onchanged?.();
		} catch (err) {
			toast.error(err);
		} finally {
			busy = false;
		}
	}

	/** "TimeEntry:2,AbsenceDay:1" → "Zeiteinträge: 2 · Abwesenheiten: 1". */
	function summary(text: string | null | undefined): string {
		return (text ?? '').split(',').filter(Boolean).map((part) => {
			const [kind, count] = part.split(':');
			return kind === 'TimeEntry' || kind === 'AbsenceDay' || kind === 'WorkSchedule'
				? t(`closedPeriods.summary.${kind}`, { count })
				: part;
		}).join(' · ');
	}
</script>

<section class="card bg-base-100 border border-base-300 p-4 sm:p-5 flex flex-col gap-4 mt-8 max-w-2xl" aria-labelledby="closed-periods" data-testid="closed-periods">
	<div>
		<h2 id="closed-periods" class="text-base font-semibold m-0">{t('closedPeriods.title')}</h2>
		<p class="text-sm text-base-content/70 mt-1 mb-0">{t('closedPeriods.intro')}</p>
		<p class="text-xs text-base-content/70 mt-1 mb-0">{t('closedPeriods.rules')}</p>
	</div>

	{#if loadError}
		<div class="alert alert-error text-sm" role="alert">{loadError}</div>
	{:else if data}
		<div>
			<h3 class="text-sm font-semibold m-0 mb-2">{t('closedPeriods.closed')}</h3>
			{#if closedMonths.length === 0}
				<p class="text-sm text-base-content/70 m-0">{t('closedPeriods.none')}</p>
			{:else}
				<ul class="flex flex-wrap gap-2 m-0 p-0 list-none">
					{#each [...closedMonths].reverse() as month (month)}
						<li class="flex items-center gap-1 rounded-full border border-base-300 pl-2.5 pr-1 py-0.5 text-sm">
							<svg class="w-3.5 h-3.5 text-base-content/70" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true"><rect x="5" y="11" width="14" height="10" rx="2" /><path d="M8 11V7a4 4 0 0 1 8 0v4" /></svg>
							<span>{formatMonth(month)}</span>
							<button type="button" class="btn btn-ghost btn-xs" disabled={busy} onclick={() => reopen(month)}
								aria-label={`${t('closedPeriods.reopen')}: ${formatMonth(month)}`}>{t('closedPeriods.reopen')}</button>
						</li>
					{/each}
				</ul>
			{/if}
		</div>

		<form class="grid grid-cols-2 sm:grid-cols-[auto_auto_1fr_auto] gap-3 items-end" onsubmit={close} novalidate>
			<label class="min-w-0">
				<span class="label text-sm font-medium text-base-content/80">{t('closedPeriods.from')}</span>
				<input type="month" class="input w-full" max={to || latest} bind:value={from} required disabled={busy} data-testid="close-from" />
			</label>
			<label class="min-w-0">
				<span class="label text-sm font-medium text-base-content/80">{t('closedPeriods.to')}</span>
				<input type="month" class="input w-full" min={from} max={latest} bind:value={to} required disabled={busy} data-testid="close-to" />
			</label>
			<label class="col-span-2 sm:col-span-1 min-w-0">
				<span class="label text-sm font-medium text-base-content/80">{t('closedPeriods.note')}</span>
				<input type="text" class="input w-full" maxlength="500" placeholder={t('closedPeriods.notePlaceholder')} bind:value={note} disabled={busy} />
			</label>
			<button type="submit" class="btn btn-primary col-span-2 sm:col-span-1" disabled={busy || range.length === 0 || monthKey(to) > latest}>{t('closedPeriods.close')}</button>
		</form>

		<div>
			<h3 class="text-sm font-semibold m-0 mb-2">{t('closedPeriods.audit')}</h3>
			{#if events.length === 0}
				<p class="text-sm text-base-content/70 m-0">{t('closedPeriods.auditEmpty')}</p>
			{:else}
				<ul class="m-0 p-0 list-none flex flex-col divide-y divide-base-300 text-sm" data-testid="closed-periods-log">
					{#each events as event (event.id)}
						<li class="py-2 flex flex-col gap-0.5">
							<div class="flex flex-wrap gap-x-2">
								<span class="font-medium">{formatMonth(event.month ?? '')}</span>
								<span>{t(`closedPeriods.kind.${event.kind as 'Closed' | 'Reopened' | 'AdminChange'}`)}</span>
								{#if event.userName}<span class="text-base-content/70">{t('closedPeriods.by', { name: event.userName })}</span>{/if}
								<span class="text-base-content/70">· {formatDateTime(event.at ?? '')}</span>
							</div>
							{#if event.kind === 'AdminChange'}
								<div class="text-base-content/70">
									{summary(event.summary)} {event.memberName ? t('closedPeriods.forMember', { name: event.memberName }) : t('closedPeriods.several')}
								</div>
							{/if}
							{#if event.reason}<div class="text-base-content/80">{t('closedPeriods.reason', { reason: event.reason })}</div>{/if}
						</li>
					{/each}
				</ul>
			{/if}
		</div>
	{/if}
</section>
