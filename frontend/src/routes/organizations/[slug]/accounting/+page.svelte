<script lang="ts">
	import { untrack } from 'svelte';
	import { page } from '$app/state';
	import { goto } from '$app/navigation';
	import { accountingApi, closedPeriodsApi } from '$lib/apiClient';
	import type { AccountingMonthsResponse, AccountingSummaryResponse, OrganizationDetailResponse, VacationOverviewResponse } from '$lib/api';
	import { orgContext } from '$lib/stores/orgContext.svelte';
	import { getOrgDetail, invalidateOrgDetail } from '$lib/stores/orgDetail';
	import { locale } from '$lib/stores/locale.svelte';
	import { confirmDialog } from '$lib/stores/confirm.svelte';
	import { toast } from '$lib/stores/toast.svelte';
	import { extractErrorMessage } from '$lib/utils/errorHandler';
	import { formatDate, formatNumber, getMonthName } from '$lib/utils/formatters';
	import { formatMonth } from '$lib/utils/closedPeriods';
	import { downloadBlob } from '$lib/utils/download';
	import { canUseAccounting, seesAllHours } from '$lib/utils/orgRules';
	import { closeWarnings, previousMonth, summaryTotals, tabFromQuery, validYear, ACCOUNTING_TABS, type AccountingTab } from '$lib/utils/accounting';
	import ClosedPeriodsPanel from '$lib/components/ClosedPeriodsPanel.svelte';
	import EmptyState from '$lib/components/EmptyState.svelte';
	import { t } from '$lib/i18n';

	/** "Auswertungen" (docs/ACCOUNTING_ROLE_PLAN.md): owners, admins and accountants. */
	const orgSlug = $derived(page.params.slug ?? '');
	const membership = $derived(orgContext.organizations.find((o) => o.slug === orgSlug) ?? null);
	const allowed = $derived(canUseAccounting(membership));
	const tab = $derived(tabFromQuery(page.url.searchParams.get('tab')));

	const start = previousMonth();
	let year = $state(start.year);
	let month = $state(start.month);
	let org = $state<OrganizationDetailResponse | null>(null);
	const hoursVisible = $derived(seesAllHours(membership, org));

	let summary = $state<AccountingSummaryResponse | null>(null);
	let months = $state<AccountingMonthsResponse | null>(null);
	let vacation = $state<VacationOverviewResponse | null>(null);
	let loading = $state(false);
	let loadError = $state('');
	let busy = $state(false);

	const totals = $derived(summaryTotals(summary?.members ?? []));
	const monthLabel = $derived(formatMonth(`${year}-${String(month).padStart(2, '0')}`));

	$effect(() => {
		const s = orgSlug;
		if (!s) return;
		untrack(async () => {
			try {
				org = await getOrgDetail(s);
			} catch {
				org = null;
			}
		});
	});

	// (Re)load the open tab whenever org, tab or period change
	$effect(() => {
		const key = { s: orgSlug, tab, year, month, ok: allowed };
		if (!key.s || !key.ok || !validYear(key.year)) return;
		untrack(() => load(key.tab));
	});

	async function load(which: AccountingTab) {
		loading = true;
		loadError = '';
		try {
			if (which === 'reports') summary = (await accountingApi.apiV1OrganizationsSlugAccountingSummaryGet(orgSlug, year, month)).data;
			else if (which === 'closing') months = (await accountingApi.apiV1OrganizationsSlugAccountingMonthsGet(orgSlug, year)).data;
			else vacation = (await accountingApi.apiV1OrganizationsSlugAccountingVacationGet(orgSlug, year)).data;
		} catch (err) {
			if (which === 'reports') summary = null;
			loadError = extractErrorMessage(err, t('accounting.loadFailed'));
		} finally {
			loading = false;
		}
	}

	function selectTab(next: AccountingTab) {
		goto(`?tab=${next}`, { replaceState: true, noScroll: true, keepFocus: true });
	}

	async function download(kind: 'csv' | 'pdf') {
		busy = true;
		try {
			const name = `accounting-${orgSlug}-${year}-${String(month).padStart(2, '0')}.${kind}`;
			if (kind === 'csv') {
				const res = await accountingApi.apiV1OrganizationsSlugAccountingSummaryCsvGet(orgSlug, year, month, locale.current, { responseType: 'blob' });
				downloadBlob(res.data as unknown as BlobPart, 'text/csv', name);
			} else {
				const res = await accountingApi.apiV1OrganizationsSlugAccountingSummaryPdfGet(orgSlug, year, month, locale.current, { responseType: 'blob' });
				downloadBlob(res.data as unknown as BlobPart, 'application/pdf', name);
			}
		} catch (err) {
			toast.error(err, t('accounting.reports.downloadFailed'));
		} finally {
			busy = false;
		}
	}

	async function memberReport(memberId: number, name: string) {
		busy = true;
		try {
			const res = await accountingApi.apiV1OrganizationsSlugAccountingMembersMemberIdReportGet(orgSlug, memberId, year, month, locale.current, { responseType: 'blob' });
			downloadBlob(res.data as unknown as BlobPart, 'application/pdf', `time-report-${name.replace(/\s+/g, '-')}-${year}-${String(month).padStart(2, '0')}.pdf`);
		} catch (err) {
			toast.error(err, t('accounting.reports.downloadFailed'));
		} finally {
			busy = false;
		}
	}

	async function closeMonth(first: string, failureEntryCount?: number, pendingRequestCount?: number) {
		const name = formatMonth(first);
		const issues = closeWarnings({ failureEntryCount, pendingRequestCount });
		const ok = await confirmDialog({
			title: t('accounting.closing.closeTitle', { month: name }),
			message: issues.length
				? t('accounting.closing.closeWarning', { month: name, issues: issues.join(', ') })
				: t('accounting.closing.closeMessage'),
			confirmLabel: issues.length ? t('accounting.closing.closeAnyway') : t('accounting.closing.close'),
			danger: issues.length > 0
		});
		if (!ok) return;
		busy = true;
		try {
			await closedPeriodsApi.apiV1OrganizationsSlugClosedPeriodsPost(orgSlug, { from: first, to: first });
			invalidateOrgDetail(orgSlug);
			toast.success(t('accounting.closing.closedOk', { month: name }));
			await load('closing');
		} catch (err) {
			toast.error(err);
		} finally {
			busy = false;
		}
	}

	const hours = (h: number | undefined) => formatNumber(h ?? 0, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
	const signed = (h: number | undefined) => ((h ?? 0) > 0 ? '+' : '') + hours(h);
	const days = (d: number | undefined) => formatNumber(d ?? 0, { maximumFractionDigits: 1 });
</script>

<svelte:head><title>{t('accounting.title')}</title></svelte:head>

<div class="max-w-6xl mx-auto px-4 sm:px-6 py-4">
	<h1 class="text-2xl font-bold m-0">{t('accounting.title')}</h1>
	<p class="text-sm text-base-content/70 mt-1">{membership?.name ?? orgSlug} · {t('accounting.intro')}</p>

	{#if !allowed}
		<div class="alert alert-error text-sm mt-4" role="alert">{t('errors.org.accountantRequired')}</div>
	{:else}
		<nav class="overflow-x-auto -mx-4 px-4 sm:mx-0 sm:px-0 mt-4" aria-label={t('accounting.tabs.label')}>
			<div class="tabs flex-nowrap w-max min-w-full" role="tablist">
				{#each ACCOUNTING_TABS as name (name)}
					<button type="button" role="tab" class="tab whitespace-nowrap min-h-11 px-2.5" class:tab-active={tab === name}
						aria-selected={tab === name} onclick={() => selectTab(name)} data-testid="accounting-tab-{name}">{t(`accounting.tabs.${name}`)}</button>
				{/each}
			</div>
		</nav>

		<div class="flex flex-wrap items-end gap-3 mt-4">
			{#if tab === 'reports'}
				<label>
					<span class="label text-sm font-medium text-base-content/80">{t('accounting.month')}</span>
					<select class="select select-sm" bind:value={month}>
						{#each Array.from({ length: 12 }, (_, i) => i + 1) as m (m)}
							<option value={m}>{getMonthName(new Date(2000, m - 1))}</option>
						{/each}
					</select>
				</label>
			{/if}
			<label>
				<span class="label text-sm font-medium text-base-content/80">{t('accounting.year')}</span>
				<input type="number" class="input input-sm w-24" class:input-error={!validYear(year)} aria-invalid={!validYear(year)} min="2000" max="2100" bind:value={year} />
			</label>
			{#if loading}<span class="loading loading-spinner loading-sm mb-1" aria-label={t('common.loading')}></span>{/if}
		</div>

		{#if loadError}
			<div class="alert alert-error text-sm mt-4" role="alert">{loadError}</div>
		{/if}

		{#if tab === 'reports'}
			{#if !hoursVisible && org}
				<div class="alert alert-info text-sm mt-4">{t('accounting.hoursHidden')}</div>
			{/if}
			{#if summary}
				<section class="card bg-base-100 border border-base-300 mt-4" aria-labelledby="acct-summary" data-testid="accounting-summary">
					<div class="card-body p-4 sm:p-5 gap-3">
						<div class="flex flex-wrap items-center justify-between gap-2">
							<h2 id="acct-summary" class="text-base font-semibold m-0">
								{t('accounting.reports.heading', { month: monthLabel })}
								<span class="badge badge-sm ml-1 {summary.isClosed ? 'badge-success' : 'badge-ghost'}">{summary.isClosed ? t('accounting.reports.closed') : t('accounting.reports.open')}</span>
							</h2>
							<div class="flex gap-2">
								<button type="button" class="btn btn-primary btn-sm" disabled={busy} onclick={() => download('csv')}>{t('accounting.reports.csv')}</button>
								<button type="button" class="btn btn-sm" disabled={busy} onclick={() => download('pdf')}>{t('accounting.reports.pdf')}</button>
							</div>
						</div>
						{#if (summary.members ?? []).length === 0}
							<EmptyState text={t('accounting.empty')} />
						{:else}
							<div class="overflow-x-auto">
								<table class="table table-sm w-full text-sm">
									<thead>
										<tr>
											<th>{t('accounting.reports.personnelNo')}</th>
											<th>{t('accounting.reports.name')}</th>
											<th class="text-right">{t('accounting.reports.target')}</th>
											<th class="text-right">{t('accounting.reports.worked')}</th>
											<th class="text-right">{t('accounting.reports.overtime')}</th>
											<th class="text-right">{t('accounting.reports.balance')}</th>
											<th class="text-right">{t('accounting.reports.vacation')}</th>
											<th class="text-right">{t('accounting.reports.sick')}</th>
											<th class="text-right">{t('accounting.reports.other')}</th>
											<th class="text-right">{t('accounting.reports.corrections')}</th>
											<th><span class="sr-only">PDF</span></th>
										</tr>
									</thead>
									<tbody>
										{#each summary.members ?? [] as row (row.userId)}
											<tr>
												<td class="tabular-nums">{row.personnelNumber ?? ''}</td>
												<td>
													{row.name}
													{#if row.isFormerMember}<span class="badge badge-ghost badge-sm ml-1">{t('accounting.reports.former')}</span>{/if}
												</td>
												<td class="text-right tabular-nums">{hours(row.targetHours)}</td>
												<td class="text-right tabular-nums">{hours(row.workedHours)}</td>
												<td class="text-right tabular-nums" class:text-error={(row.overtimeHours ?? 0) < 0} class:text-success={(row.overtimeHours ?? 0) > 0}>{signed(row.overtimeHours)}</td>
												<td class="text-right tabular-nums">{signed(row.overtimeBalance)}</td>
												<td class="text-right tabular-nums">{days(row.vacationDays)}</td>
												<td class="text-right tabular-nums">{days(row.sickDays)}</td>
												<td class="text-right tabular-nums">{days(row.otherAbsenceDays)}</td>
												<td class="text-right tabular-nums">
													{#if row.failureEntryCount}<span class="badge badge-error badge-sm">{row.failureEntryCount}</span>{:else}0{/if}
												</td>
												<td>
													<button type="button" class="btn btn-ghost btn-xs" disabled={busy} onclick={() => memberReport(row.userId!, row.name ?? '')}
														aria-label={t('accounting.reports.memberPdf', { name: row.name ?? '' })} title={t('accounting.reports.memberPdf', { name: row.name ?? '' })}>PDF</button>
												</td>
											</tr>
										{/each}
									</tbody>
									<tfoot>
										<tr class="font-semibold">
											<td colspan="2">{t('accounting.reports.total')}</td>
											<td class="text-right tabular-nums">{hours(totals.targetHours)}</td>
											<td class="text-right tabular-nums">{hours(totals.workedHours)}</td>
											<td class="text-right tabular-nums">{signed(totals.overtimeHours)}</td>
											<td></td>
											<td class="text-right tabular-nums">{days(totals.vacationDays)}</td>
											<td class="text-right tabular-nums">{days(totals.sickDays)}</td>
											<td class="text-right tabular-nums">{days(totals.otherAbsenceDays)}</td>
											<td class="text-right tabular-nums">{totals.failureEntryCount}</td>
											<td></td>
										</tr>
									</tfoot>
								</table>
							</div>
							<p class="text-xs text-base-content/70 m-0">{t('accounting.reports.correctionsHint')}</p>
						{/if}
					</div>
				</section>
			{/if}
		{:else if tab === 'closing'}
			<section class="card bg-base-100 border border-base-300 mt-4" aria-labelledby="acct-closing" data-testid="accounting-months">
				<div class="card-body p-4 sm:p-5 gap-3">
					<h2 id="acct-closing" class="text-base font-semibold m-0">{t('accounting.closing.heading', { year })}</h2>
					<p class="text-sm text-base-content/70 m-0">{t('accounting.closing.intro')}</p>
					{#if months}
						<ul class="m-0 p-0 list-none flex flex-col divide-y divide-base-300 text-sm">
							{#each months.months ?? [] as m (m.month)}
								{@const issues = closeWarnings(m)}
								<li class="py-2 flex flex-wrap items-center gap-x-3 gap-y-1">
									<span class="font-medium w-40">{formatMonth(m.month ?? '')}</span>
									<span class="flex-1 min-w-48 text-base-content/80">
										{#if m.isClosed}
											<span class="badge badge-success badge-sm mr-1">{t('accounting.reports.closed')}</span>
											{m.closedByName
												? t('accounting.closing.closedBy', { name: m.closedByName, date: formatDate(m.closedAt ?? '') })
												: t('accounting.closing.closedAt', { date: formatDate(m.closedAt ?? '') })}
										{:else if !m.hasEnded}
											<span class="text-base-content/70">{t('accounting.closing.notEnded')}</span>
										{:else if issues.length}
											<span class="badge badge-warning badge-sm mr-1">{t('accounting.closing.openState')}</span>{issues.join(' · ')}
										{:else}
											<span class="badge badge-ghost badge-sm mr-1">{t('accounting.closing.openState')}</span>{t('accounting.closing.ready')}
										{/if}
									</span>
									{#if m.hasEnded && !m.isClosed}
										<button type="button" class="btn btn-primary btn-xs" disabled={busy}
											onclick={() => closeMonth(m.month!, m.failureEntryCount, m.pendingRequestCount)}
											aria-label={`${t('accounting.closing.close')}: ${formatMonth(m.month ?? '')}`}>{t('accounting.closing.close')}</button>
									{/if}
								</li>
							{/each}
						</ul>
					{/if}
				</div>
			</section>
			{#key months}
				<ClosedPeriodsPanel {orgSlug} timeZone={org?.timeZone} statuses={months?.months ?? []} onchanged={() => load('closing')} />
			{/key}
		{:else}
			<section class="card bg-base-100 border border-base-300 mt-4" aria-labelledby="acct-vacation" data-testid="accounting-vacation">
				<div class="card-body p-4 sm:p-5 gap-3">
					<h2 id="acct-vacation" class="text-base font-semibold m-0">{t('accounting.vacation.heading', { year })}</h2>
					{#if vacation}
						{#if (vacation.members ?? []).length === 0}
							<EmptyState text={t('accounting.empty')} />
						{:else}
							<div class="overflow-x-auto">
								<table class="table table-sm w-full text-sm">
									<thead>
										<tr>
											<th>{t('accounting.reports.personnelNo')}</th>
											<th>{t('accounting.reports.name')}</th>
											<th class="text-right">{t('accounting.vacation.entitlement')}</th>
											<th class="text-right">{t('accounting.vacation.taken')}</th>
											<th class="text-right">{t('accounting.vacation.planned')}</th>
											<th class="text-right">{t('accounting.vacation.remaining')}</th>
											<th class="text-right">{t('accounting.vacation.sick')}</th>
											<th class="text-right">{t('accounting.vacation.other')}</th>
										</tr>
									</thead>
									<tbody>
										{#each vacation.members ?? [] as row (row.userId)}
											<tr>
												<td class="tabular-nums">{row.personnelNumber ?? ''}</td>
												<td>{row.name}</td>
												<td class="text-right tabular-nums">{days(row.entitlement)}</td>
												<td class="text-right tabular-nums">{days(row.taken)}</td>
												<td class="text-right tabular-nums">{days(row.planned)}</td>
												<td class="text-right tabular-nums font-semibold">{days(row.remaining)}</td>
												<td class="text-right tabular-nums">{days(row.sickDays)}</td>
												<td class="text-right tabular-nums">{days(row.otherAbsenceDays)}</td>
											</tr>
										{/each}
									</tbody>
								</table>
							</div>
						{/if}
						<p class="text-xs text-base-content/70 m-0">{t('accounting.vacation.hint')}</p>
					{/if}
				</div>
			</section>
		{/if}
	{/if}
</div>
