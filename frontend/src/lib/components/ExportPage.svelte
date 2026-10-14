<script lang="ts">
	import { getOrgDetail } from '$lib/stores/orgDetail';
	import { untrack } from 'svelte';
	import { auth } from '$lib/stores/auth.svelte';
	import { orgContext } from '$lib/stores/orgContext.svelte';
	import { toast } from '$lib/stores/toast.svelte';
	import { exportImportApi, groupApi, requestsApi, reportsApi } from '$lib/apiClient';
	import type {
		EffectivePermissionsResponse,
		ExportRequest,
		OrganizationDetailResponse,
		ImportPreviewRow,
		ImportEntryRequest
	} from '$lib/api';
	import { RequestType } from '$lib/api';
	import { extractErrorMessage } from '$lib/utils/errorHandler';
	import { dateKey } from '$lib/utils/dateHelpers';
	import { csvImportEnabled, canImportCsv, isOrgAdmin, isRequiresApproval, ruleMode } from '$lib/utils/orgRules';
	import { formatDate, getMonthName } from '$lib/utils/formatters';
	import { t, tp } from '$lib/i18n';
	import type * as ExportMessages from '$lib/i18n/messages/export';
	import Modal from '$lib/components/Modal.svelte';
	import { locale } from '$lib/stores/locale.svelte';
	import { downloadBlob } from '$lib/utils/download';

	// ── Tab state ──
	let activeTab = $state<'export' | 'import' | 'report'>('export');

	// ── Org detail (member list) + effective permissions (CSV import rule incl. group overrides) ──
	let orgDetail = $state<OrganizationDetailResponse | null>(null);
	let perms = $state<EffectivePermissionsResponse | null>(null);
	let orgLoading = $state(false);
	let orgLoadError = $state('');

	// ── Target member (export/report for someone else) ──
	let targetUserId = $state<number | null>(null); // null = self
	let myMember = $derived(orgDetail?.members?.find((m) => m.id === auth.user?.id) ?? null);
	let isAnyGroupAdmin = $derived((myMember?.groups ?? []).some((g) => g.isGroupAdmin));
	/** Whether the UI should offer a member picker at all. The backend is the
	 * final authority — a group admin without the specific export capability
	 * for a given member's group will still get a clear 403 if they try. */
	let canExportOthers = $derived(isOrgAdmin(orgContext.selectedOrg?.role) || isAnyGroupAdmin);
	let exportableMembers = $derived((orgDetail?.members ?? []).filter((m) => m.id !== auth.user?.id));

	$effect(() => {
		const slug = orgContext.selectedOrgSlug;
		untrack(() => {
			targetUserId = null;
			if (slug) loadOrgDetail(slug);
			else {
				orgDetail = null;
				perms = null;
			}
		});
	});

	async function loadOrgDetail(slug: string) {
		orgLoading = true;
		orgLoadError = '';
		try {
			const [detail, effective] = await Promise.all([
				getOrgDetail(slug).then((data) => ({ data })),
				groupApi.apiV1OrganizationsSlugEffectivePermissionsGet(slug)
			]);
			if (slug !== orgContext.selectedOrgSlug) return; // org switched meanwhile
			orgDetail = detail.data;
			perms = effective.data;
		} catch (err) {
			orgDetail = null;
			perms = null;
			orgLoadError = extractErrorMessage(err, t('export.loadOrgFailed'));
		} finally {
			orgLoading = false;
		}
	}

	// ── Export state ──
	let exportType = $state<'entries' | 'daily'>('entries');
	let exportScope = $state<'org' | 'all'>('org');
	let exportRange = $state<'week' | 'month' | 'year' | 'all' | 'custom'>('month');
	let customFrom = $state('');
	let customTo = $state('');
	let exporting = $state(false);
	let exportError = $state('');

	// Column selection: `key` is what the backend expects, `label` the key under export.columns
	type ColumnLabel = Exclude<keyof typeof ExportMessages.de.columns, 'title' | 'selectAll' | 'deselectAll' | 'none'>;
	interface Column {
		key: string;
		label: ColumnLabel;
		default: boolean;
	}
	const entryColumns: Column[] = [
		{ key: 'Date', label: 'Date', default: true },
		{ key: 'DayOfWeek', label: 'DayOfWeek', default: true },
		{ key: 'StartTime', label: 'StartTime', default: true },
		{ key: 'EndTime', label: 'EndTime', default: true },
		{ key: 'Duration', label: 'Duration', default: true },
		{ key: 'Pause', label: 'Pause', default: true },
		{ key: 'NetDuration', label: 'NetDuration', default: true },
		{ key: 'Description', label: 'Description', default: true },
		{ key: 'Organization', label: 'Organization', default: false }
	];

	const dailyColumns: Column[] = [
		{ key: 'Date', label: 'Date', default: true },
		{ key: 'DayOfWeek', label: 'DayOfWeek', default: true },
		{ key: 'TargetHours', label: 'TargetHours', default: true },
		{ key: 'WorkedHours', label: 'WorkedHours', default: true },
		{ key: 'Pause', label: 'Pause', default: true },
		{ key: 'NetWorkedHours', label: 'NetWorkedHours', default: true },
		{ key: 'Overtime', label: 'Overtime', default: true },
		{ key: 'CumulativeOvertime', label: 'CumulativeOvertime', default: true },
		{ key: 'Status', label: 'Status', default: true },
		{ key: 'HolidayName', label: 'HolidayName', default: false },
		{ key: 'AbsenceType', label: 'AbsenceType', default: false },
		{ key: 'AbsenceNote', label: 'AbsenceNote', default: false },
		{ key: 'Description', label: 'Entries', default: true },
		{ key: 'Organization', label: 'Organization', default: false }
	];

	let selectedEntryColumns = $state<Set<string>>(
		new Set(entryColumns.filter((c) => c.default).map((c) => c.key))
	);
	let selectedDailyColumns = $state<Set<string>>(
		new Set(dailyColumns.filter((c) => c.default).map((c) => c.key))
	);

	function toggleColumn(set: Set<string>, key: string) {
		const next = new Set(set);
		if (next.has(key)) next.delete(key);
		else next.add(key);
		return next;
	}

	function computeDateRange(): { from?: string; to?: string } {
		const now = new Date();
		switch (exportRange) {
			case 'week': {
				const day = now.getDay();
				const diff = day === 0 ? 6 : day - 1; // Monday start
				const start = new Date(now);
				start.setDate(now.getDate() - diff);
				const end = new Date(start);
				end.setDate(start.getDate() + 6);
				return {
					from: dateKey(start),
					to: dateKey(end)
				};
			}
			case 'month':
				return {
					from: `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-01`,
					to: dateKey(new Date(now.getFullYear(), now.getMonth() + 1, 0))
				};
			case 'year':
				return {
					from: `${now.getFullYear()}-01-01`,
					to: `${now.getFullYear()}-12-31`
				};
			case 'custom':
				return { from: customFrom || undefined, to: customTo || undefined };
			case 'all':
			default:
				return {};
		}
	}

	/** Exporting for someone else or the daily summary always needs an org (permission is org-scoped). */
	let forceOrgScope = $derived(exportType === 'daily' || targetUserId !== null);
	let customRangeInvalid = $derived(exportRange === 'custom' && !!customFrom && !!customTo && customFrom > customTo);
	let exportBlockedReason = $derived(
		forceOrgScope && !orgContext.selectedOrgSlug
			? targetUserId !== null
				? t('export.blocked.member')
				: t('export.blocked.daily')
			: ''
	);

	// Without an org only "all organizations" is possible
	$effect(() => {
		if (!orgContext.selectedOrgSlug) exportScope = 'all';
	});

	async function doExport() {
		if (customRangeInvalid || exportBlockedReason) return;
		exporting = true;
		exportError = '';

		try {
			const { from, to } = computeDateRange();
			const columns =
				exportType === 'entries'
					? [...selectedEntryColumns]
					: [...selectedDailyColumns];
			const orgSlug =
				(forceOrgScope || exportScope === 'org') ? (orgContext.selectedOrgSlug ?? undefined) : undefined;

			const request: ExportRequest = {
				type: exportType,
				organizationSlug: orgSlug,
				targetUserId: targetUserId ?? undefined,
				from: from ? new Date(from).toISOString() : undefined,
				to: to ? new Date(to).toISOString() : undefined,
				columns,
				lang: locale.current
			};

			const response = await exportImportApi.apiV1ExportImportExportPost(request, {
				responseType: 'blob'
			});

			const targetSuffix = targetUserId !== null ? `-member${targetUserId}` : '';
			const label = exportType === 'daily' ? `daily-report-${orgSlug}${targetSuffix}` : `time-entries${targetSuffix}`;
			downloadBlob(response.data as unknown as BlobPart, 'text/csv', `${label}-${dateKey(new Date())}.csv`);
			toast.success(t('export.downloaded'));
		} catch (err) {
			exportError = extractErrorMessage(err, t('export.failed'));
		} finally {
			exporting = false;
		}
	}

	// ── Import state ──
	let importFile = $state<File | null>(null);
	let fileError = $state('');
	let importPreviewing = $state(false);
	let importConfirming = $state(false);
	let importError = $state('');
	let previewRows = $state<ImportPreviewRow[]>([]);
	let previewWarnings = $state<string[]>([]);
	let previewDuplicateCount = $state(0);
	let selectedRows = $state<Set<number>>(new Set());
	let showPreview = $state(false);
	let fileInput = $state<HTMLInputElement>();

	function onFileChange(e: Event) {
		const target = e.target as HTMLInputElement;
		const file = target.files?.[0] ?? null;
		resetPreview();
		importError = '';
		fileError = '';
		if (file && !file.name.toLowerCase().endsWith('.csv')) {
			fileError = t('export.import.wrongType');
			importFile = null;
		} else if (file && file.size === 0) {
			fileError = t('export.import.empty');
			importFile = null;
		} else {
			importFile = file;
		}
	}

	function resetPreview() {
		showPreview = false;
		previewRows = [];
		previewWarnings = [];
		previewDuplicateCount = 0;
		selectedRows = new Set();
	}

	function resetImport() {
		resetPreview();
		importFile = null;
		if (fileInput) fileInput.value = '';
	}

	async function doPreview() {
		if (!importFile || !orgContext.selectedOrgSlug) return;

		importPreviewing = true;
		importError = '';

		try {
			const { data } = await exportImportApi.apiV1ExportImportImportPreviewPost(
				orgContext.selectedOrgSlug,
				importFile
			);

			previewRows = data.rows ?? [];
			previewWarnings = data.warnings ?? [];
			previewDuplicateCount = data.duplicateCount ?? 0;

			// Select all non-duplicate, non-warning rows by default
			selectedRows = new Set(
				previewRows
					.filter((r) => !r.isDuplicate && !r.warning)
					.map((r) => r.rowNumber!)
			);
			showPreview = true;
		} catch (err) {
			importError = extractErrorMessage(err, t('export.import.previewFailed'));
		} finally {
			importPreviewing = false;
		}
	}

	function toggleRow(rowNum: number) {
		const next = new Set(selectedRows);
		if (next.has(rowNum)) next.delete(rowNum);
		else next.add(rowNum);
		selectedRows = next;
	}

	function selectAll() {
		selectedRows = new Set(previewRows.map((r) => r.rowNumber!));
	}

	function deselectAll() {
		selectedRows = new Set();
	}

	async function doImport() {
		if (!orgContext.selectedOrgSlug || selectedRows.size === 0) return;

		importConfirming = true;
		importError = '';

		try {
			const entries: ImportEntryRequest[] = previewRows
				.filter((r) => selectedRows.has(r.rowNumber!))
				.map((r) => ({
					date: r.date!,
					startTime: r.startTime!,
					endTime: r.endTime!,
					pauseMinutes: r.pauseMinutes ?? 0,
					description: r.description ?? undefined
				}));

			const { data } = await exportImportApi.apiV1ExportImportImportConfirmPost(
				orgContext.selectedOrgSlug,
				entries
			);

			toast.success(
				tp('export.import.imported', data.importedCount ?? 0) +
					(data.skippedCount ? ' ' + t('export.import.skipped', { count: data.skippedCount }) : '')
			);

			if (data.errors && data.errors.length > 0) {
				importError = data.errors.join('; ');
			}

			resetImport();
		} catch (err) {
			importError = extractErrorMessage(err, t('export.import.failed'));
		} finally {
			importConfirming = false;
		}
	}

	// ── PDF Report state ──
	const now = new Date();
	let reportMonth = $state(now.getMonth() + 1); // 1-12
	let reportMonthYear = $state(now.getFullYear());
	let reportYear = $state(now.getFullYear());
	let monthlyReportDownloading = $state(false);
	let yearlyReportDownloading = $state(false);
	let reportError = $state('');

	function validYear(y: number) {
		return Number.isInteger(y) && y >= 2000 && y <= 2100;
	}


	async function downloadMonthlyReport() {
		const orgSlug = orgContext.selectedOrgSlug;
		const memberId = targetUserId ?? auth.user?.id;
		if (!orgSlug || !memberId || !validYear(reportMonthYear)) return;

		monthlyReportDownloading = true;
		reportError = '';
		try {
			const response = await reportsApi.apiV1OrganizationsSlugMembersMemberIdReportsMonthlyGet(
				orgSlug, memberId, reportMonthYear, reportMonth, locale.current, { responseType: 'blob' }
			);
			downloadBlob(response.data as unknown as BlobPart, 'application/pdf', `time-report-${orgSlug}-${reportMonthYear}-${String(reportMonth).padStart(2, '0')}.pdf`);
			toast.success(t('export.report.downloaded'));
		} catch (e) {
			reportError = extractErrorMessage(e, t('export.report.monthlyFailed'));
		} finally {
			monthlyReportDownloading = false;
		}
	}

	async function downloadYearlyReport() {
		const orgSlug = orgContext.selectedOrgSlug;
		const memberId = targetUserId ?? auth.user?.id;
		if (!orgSlug || !memberId || !validYear(reportYear)) return;

		yearlyReportDownloading = true;
		reportError = '';
		try {
			const response = await reportsApi.apiV1OrganizationsSlugMembersMemberIdReportsYearlyGet(
				orgSlug, memberId, reportYear, locale.current, { responseType: 'blob' }
			);
			downloadBlob(response.data as unknown as BlobPart, 'application/pdf', `time-report-${orgSlug}-${reportYear}.pdf`);
			toast.success(t('export.report.downloaded'));
		} catch (e) {
			reportError = extractErrorMessage(e, t('export.report.yearlyFailed'));
		} finally {
			yearlyReportDownloading = false;
		}
	}

	// ── Payroll CSV (org admins, while admins may see members' hours) ──
	let payrollMonth = $state(now.getMonth() + 1);
	let payrollYear = $state(now.getFullYear());
	let payrollDownloading = $state(false);
	let payrollError = $state('');
	let canPayroll = $derived(isOrgAdmin(orgContext.selectedOrg?.role) && !!orgDetail?.memberTimeEntryVisibility);

	async function downloadPayroll() {
		const orgSlug = orgContext.selectedOrgSlug;
		if (!orgSlug || !validYear(payrollYear)) return;
		payrollDownloading = true;
		payrollError = '';
		try {
			const from = dateKey(new Date(payrollYear, payrollMonth - 1, 1));
			const to = dateKey(new Date(payrollYear, payrollMonth, 0));
			const response = await exportImportApi.apiV1ExportImportPayrollPost({ orgSlug, from, to, lang: locale.current }, { responseType: 'blob' });
			downloadBlob(response.data as unknown as BlobPart, 'text/csv', `payroll-${orgSlug}-${from.slice(0, 7)}.csv`);
			toast.success(t('export.downloaded'));
		} catch (e) {
			payrollError = extractErrorMessage(e, t('export.payroll.failed'));
		} finally {
			payrollDownloading = false;
		}
	}

	// Derived helpers
	let availableColumns = $derived(exportType === 'entries' ? entryColumns : dailyColumns);
	let activeColumnSet = $derived(
		exportType === 'entries' ? selectedEntryColumns : selectedDailyColumns
	);
	// Effective permissions first, so a group override beats the org default
	let canImport = $derived(csvImportEnabled(perms, orgDetail));
	let importAllowed = $derived(canImportCsv(perms, orgDetail));
	let importNeedsApproval = $derived(isRequiresApproval(ruleMode('csvImportMode', perms, orgDetail)));

	// ── Import request approval state ──
	let requestMessage = $state('');
	let requestSending = $state(false);
	let requestError = $state('');
	let showRequestModal = $state(false);

	function openRequestModal() {
		requestMessage = '';
		requestError = '';
		showRequestModal = true;
	}

	async function submitImportRequest() {
		if (!orgContext.selectedOrgSlug) return;
		requestSending = true;
		requestError = '';
		try {
			const entries = previewRows
				.filter((r) => selectedRows.has(r.rowNumber!))
				.map((r) => ({
					date: r.date,
					startTime: r.startTime,
					endTime: r.endTime,
					pauseMinutes: r.pauseMinutes ?? 0,
					description: r.description ?? null
				}));
			const requestData = JSON.stringify({
				entries,
				fileName: importFile?.name ?? 'unknown'
			});
			await requestsApi.apiV1OrganizationsSlugRequestsPost(orgContext.selectedOrgSlug, {
				type: RequestType.NUMBER_5,
				message: requestMessage.trim() || t('export.request.defaultMessage', { count: entries.length, total: previewRows.length }),
				requestData
			});
			toast.success(t('export.request.submitted'));
			showRequestModal = false;
			resetImport();
		} catch (err) {
			requestError = extractErrorMessage(err, t('export.request.failed'));
		} finally {
			requestSending = false;
		}
	}
</script>

<svelte:head>
	<title>{t('common.pageTitle', { page: t('export.title') })}</title>
</svelte:head>

{#snippet noOrgState(text: string)}
	<div class="card bg-base-100 border border-base-300 shadow-sm">
		<div class="card-body p-5 items-start gap-3">
			<h2 class="text-base font-semibold">{t('export.noOrg.title')}</h2>
			<p class="text-sm text-base-content/70">{text}</p>
			<div class="flex flex-wrap gap-2">
				{#if orgContext.organizations.length > 0}
					<a href="/select-org" class="btn btn-primary btn-sm">{t('export.noOrg.select')}</a>
				{:else}
					<a href="/organizations" class="btn btn-primary btn-sm">{t('export.noOrg.createOrJoin')}</a>
				{/if}
			</div>
		</div>
	</div>
{/snippet}

<div class="space-y-6">
	<h1 class="text-2xl font-bold">{t('export.title')}</h1>

	<!-- Tab buttons -->
	<div class="flex gap-1 rounded-lg border border-base-300 bg-base-200 p-1 w-fit max-w-full overflow-x-auto" role="group" aria-label={t('export.tabsLabel')}>
		{#each ['export', 'import', 'report'] as const as tab (tab)}
			<button
				type="button"
				class="rounded-md px-4 py-1.5 text-sm font-medium whitespace-nowrap transition-colors {activeTab === tab
					? 'bg-base-100 text-base-content shadow-sm'
					: 'text-base-content/70 hover:text-base-content'}"
				aria-pressed={activeTab === tab}
				onclick={() => (activeTab = tab)}
			>
				{t(`export.tabs.${tab}`)}
			</button>
		{/each}
	</div>

	{#if orgLoadError}
		<div class="alert alert-error text-sm">
			<span>{orgLoadError}</span>
			{#if orgContext.selectedOrgSlug}
				<button type="button" class="btn btn-sm" onclick={() => loadOrgDetail(orgContext.selectedOrgSlug!)}>{t('common.retry')}</button>
			{/if}
		</div>
	{/if}

	<!-- Shared "exporting for" member picker (visible on Export and PDF Report tabs) -->
	{#snippet memberSelector()}
		{#if canExportOthers}
			<div class="card bg-base-100 border border-base-300 shadow-sm">
				<div class="card-body p-5 space-y-2">
					<label for="targetMember" class="text-base font-semibold">{t('export.member.label')}</label>
					<select id="targetMember" class="select select-sm w-full sm:w-72" bind:value={targetUserId}>
						<option value={null}>{t('export.member.myself')}</option>
						{#each exportableMembers as m (m.id)}
							<option value={m.id}>{m.firstName} {m.lastName}</option>
						{/each}
					</select>
					{#if targetUserId !== null}
						<p class="text-xs text-base-content/70">{t('export.member.scopeHint', { org: orgContext.selectedOrg?.name ?? '' })}</p>
					{/if}
				</div>
			</div>
		{/if}
	{/snippet}

	<!-- ═══════════ EXPORT TAB ═══════════ -->
	{#if activeTab === 'export'}
		<div class="space-y-5">
			{#if !orgContext.selectedOrg}
				<div class="alert alert-info text-sm">
					<span>{t('export.noOrg.exportHint')} <a href="/select-org" class="link">{t('export.noOrg.select')}</a></span>
				</div>
			{/if}

			{@render memberSelector()}

			<!-- Export Type -->
			<div class="card bg-base-100 border border-base-300 shadow-sm">
				<div class="card-body p-5 space-y-4">
					<h2 id="exportTypeLabel" class="text-base font-semibold">{t('export.type.label')}</h2>
					<div class="flex flex-wrap gap-x-4 gap-y-2" role="radiogroup" aria-labelledby="exportTypeLabel">
						<label class="flex items-center gap-2 cursor-pointer">
							<input type="radio" name="exportType" class="radio radio-sm radio-primary" value="entries" bind:group={exportType} />
							<span class="text-sm">{t('export.type.entries')}</span>
						</label>
						<label class="flex items-center gap-2 cursor-pointer">
							<input type="radio" name="exportType" class="radio radio-sm radio-primary" value="daily" bind:group={exportType} />
							<span class="text-sm">{t('export.type.daily')}</span>
						</label>
					</div>
				</div>
			</div>

			<!-- Scope & Time Range -->
			<div class="card bg-base-100 border border-base-300 shadow-sm">
				<div class="card-body p-5 space-y-4">
					<h2 class="text-base font-semibold">{t('export.filters.title')}</h2>

					<div class="grid grid-cols-1 sm:grid-cols-2 gap-4">
						<!-- Scope -->
						<div>
							<label class="label text-xs font-medium text-base-content/70" for="exportScope">{t('export.filters.scope')}</label>
							<select
								id="exportScope"
								class="select select-sm w-full"
								bind:value={exportScope}
								disabled={forceOrgScope || !orgContext.selectedOrg}
							>
								{#if orgContext.selectedOrg}
									<option value="org">{t('export.filters.scopeCurrent', { name: orgContext.selectedOrg.name ?? '' })}</option>
								{/if}
								<option value="all">{t('export.filters.scopeAll')}</option>
							</select>
							{#if exportType === 'daily'}
								<p class="text-xs text-base-content/70 mt-1">{t('export.filters.dailyHint')}</p>
							{:else if targetUserId !== null}
								<p class="text-xs text-base-content/70 mt-1">{t('export.filters.memberHint')}</p>
							{/if}
						</div>

						<!-- Time Range -->
						<div>
							<label class="label text-xs font-medium text-base-content/70" for="exportRange">{t('export.filters.range')}</label>
							<select id="exportRange" class="select select-sm w-full" bind:value={exportRange}>
								{#each ['week', 'month', 'year', 'all', 'custom'] as const as r (r)}
									<option value={r}>{t(`export.filters.ranges.${r}`)}</option>
								{/each}
							</select>
						</div>
					</div>

					{#if exportRange === 'custom'}
						<div class="grid grid-cols-1 sm:grid-cols-2 gap-4">
							<div>
								<label class="label text-xs font-medium text-base-content/70" for="customFrom">{t('export.filters.from')}</label>
								<input
									id="customFrom"
									type="date"
									class="input input-sm w-full"
									class:input-error={customRangeInvalid}
									max={customTo || undefined}
									bind:value={customFrom}
								/>
							</div>
							<div>
								<label class="label text-xs font-medium text-base-content/70" for="customTo">{t('export.filters.to')}</label>
								<input
									id="customTo"
									type="date"
									class="input input-sm w-full"
									class:input-error={customRangeInvalid}
									min={customFrom || undefined}
									aria-invalid={customRangeInvalid}
									aria-describedby={customRangeInvalid ? 'customRangeError' : undefined}
									bind:value={customTo}
								/>
							</div>
						</div>
						{#if customRangeInvalid}
							<p id="customRangeError" class="text-xs text-error">{t('export.filters.rangeInvalid')}</p>
						{/if}
					{/if}
				</div>
			</div>

			<!-- Column Selection -->
			<div class="card bg-base-100 border border-base-300 shadow-sm">
				<div class="card-body p-5 space-y-3">
					<div class="flex flex-wrap items-center justify-between gap-2">
						<h2 class="text-base font-semibold">{t('export.columns.title')}</h2>
						<div class="flex gap-2">
							<button
								type="button"
								class="btn btn-ghost btn-xs"
								onclick={() => {
									if (exportType === 'entries')
										selectedEntryColumns = new Set(entryColumns.map((c) => c.key));
									else selectedDailyColumns = new Set(dailyColumns.map((c) => c.key));
								}}>{t('export.columns.selectAll')}</button
							>
							<button
								type="button"
								class="btn btn-ghost btn-xs"
								onclick={() => {
									if (exportType === 'entries') selectedEntryColumns = new Set();
									else selectedDailyColumns = new Set();
								}}>{t('export.columns.deselectAll')}</button
							>
						</div>
					</div>
					<div class="flex flex-wrap gap-2">
						{#each availableColumns as col (col.key)}
							<label
								class="flex items-center gap-1.5 rounded-lg border px-3 py-1.5 text-sm cursor-pointer transition-colors
								{activeColumnSet.has(col.key) ? 'border-primary bg-primary/10 text-primary' : 'border-base-300 text-base-content/70 hover:border-base-content/30'}"
							>
								<input
									type="checkbox"
									class="checkbox checkbox-xs checkbox-primary"
									checked={activeColumnSet.has(col.key)}
									onchange={() => {
										if (exportType === 'entries')
											selectedEntryColumns = toggleColumn(selectedEntryColumns, col.key);
										else
											selectedDailyColumns = toggleColumn(selectedDailyColumns, col.key);
									}}
								/>
								{t(`export.columns.${col.label}`)}
							</label>
						{/each}
					</div>
					{#if activeColumnSet.size === 0}
						<p class="text-xs text-error">{t('export.columns.none')}</p>
					{/if}
				</div>
			</div>

			<!-- Export button -->
			{#if exportError}
				<div class="alert alert-error text-sm">{exportError}</div>
			{/if}
			{#if exportBlockedReason}
				<div class="alert alert-warning text-sm">
					<span>{exportBlockedReason} <a href="/select-org" class="link">{t('export.noOrg.select')}</a></span>
				</div>
			{/if}
			<button
				type="button"
				class="btn btn-primary w-full sm:w-auto"
				onclick={doExport}
				disabled={exporting || activeColumnSet.size === 0 || customRangeInvalid || !!exportBlockedReason}
			>
				{#if exporting}
					<span class="loading loading-spinner loading-sm"></span>
					{t('export.exporting')}
				{:else}
					{t('export.download')}
				{/if}
			</button>

			{#if canPayroll}
				<div class="card bg-base-100 border border-base-300 shadow-sm">
					<div class="card-body p-5 space-y-3">
						<h2 class="text-base font-semibold">{t('export.payroll.title')}</h2>
						<p class="text-sm text-base-content/70">{t('export.payroll.text', { org: orgContext.selectedOrg?.name ?? '' })}</p>
						<div class="flex items-center gap-2 flex-wrap">
							<select class="select select-sm" bind:value={payrollMonth} aria-label={t('export.payroll.month')}>
								{#each Array.from({ length: 12 }, (_, i) => i + 1) as m (m)}
									<option value={m}>{getMonthName(new Date(2000, m - 1))}</option>
								{/each}
							</select>
							<input type="number" class="input input-sm w-24" class:input-error={!validYear(payrollYear)} bind:value={payrollYear} min="2000" max="2100" aria-label={t('export.payroll.year')} aria-invalid={!validYear(payrollYear)} />
							<button type="button" class="btn btn-primary btn-sm" onclick={downloadPayroll} disabled={payrollDownloading || !validYear(payrollYear)}>
								{#if payrollDownloading}
									<span class="loading loading-spinner loading-xs"></span> {t('export.exporting')}
								{:else}
									{t('export.download')}
								{/if}
							</button>
						</div>
						{#if payrollError}<div class="alert alert-error text-sm">{payrollError}</div>{/if}
					</div>
				</div>
			{/if}
		</div>
	{/if}

	<!-- ═══════════ IMPORT TAB ═══════════ -->
	{#if activeTab === 'import'}
		<div class="space-y-5">
			{#if !orgContext.selectedOrg}
				{@render noOrgState(t('export.noOrg.importText'))}
			{:else if orgLoading && !perms}
				<div class="flex items-center gap-2 text-sm text-base-content/70">
					<span class="loading loading-spinner loading-sm"></span> {t('common.loading')}
				</div>
			{:else if !canImport}
				<div class="alert alert-warning text-sm">{t('export.import.disabled')}</div>
			{:else}
				<!-- Import info -->
				<div class="card bg-base-100 border border-base-300 shadow-sm">
					<div class="card-body p-5 space-y-3">
						<h2 class="text-base font-semibold">{t('export.import.title')}</h2>
						<!-- Column names stay English: they are the CSV headers the backend parser matches -->
						<p class="text-sm text-base-content/70">
							{t('export.import.description', { required: 'Date, Start Time, End Time', optional: 'Pause (min), Description' })}
						</p>

						{#if !importAllowed}
							<div class="alert alert-info text-sm py-2">
								{t('export.import.needsApproval')}
							</div>
						{/if}

						<!-- File picker -->
						<div class="flex flex-col sm:flex-row items-stretch sm:items-end gap-3">
							<label class="flex flex-col gap-1.5 cursor-pointer w-full sm:w-auto min-w-0">
								<span class="text-xs font-medium text-base-content/70">{t('export.import.file')}</span>
								<div class="flex items-center gap-3 rounded-lg border-2 border-dashed px-4 py-3 transition-colors focus-within:border-primary {fileError ? 'border-error' : importFile ? 'border-primary/40 bg-primary/5' : 'border-base-300 hover:border-primary/50'}">
									<svg class="w-5 h-5 text-base-content/70 shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24" aria-hidden="true">
										<path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M7 16a4 4 0 01-.88-7.903A5 5 0 1115.9 6L16 6a5 5 0 011 9.9M15 13l-3-3m0 0l-3 3m3-3v12"></path>
									</svg>
									<span class="text-sm truncate {importFile ? 'text-primary font-medium' : 'text-base-content/70'}">
										{importFile ? importFile.name : t('export.import.choose')}
									</span>
									<input
										bind:this={fileInput}
										type="file"
										accept=".csv,text/csv"
										class="sr-only"
										aria-invalid={!!fileError}
										aria-describedby={fileError ? 'fileError' : undefined}
										onchange={onFileChange}
										disabled={importPreviewing || importConfirming}
									/>
								</div>
							</label>
							<button
								type="button"
								class="btn btn-primary btn-sm"
								onclick={doPreview}
								disabled={!importFile || importPreviewing || importConfirming}
							>
								{#if importPreviewing}
									<span class="loading loading-spinner loading-xs"></span>
									{t('export.import.reading')}
								{:else}
									{t('export.import.preview')}
								{/if}
							</button>
						</div>
						{#if fileError}
							<p id="fileError" class="text-xs text-error">{fileError}</p>
						{/if}
					</div>
				</div>

				<!-- Preview results -->
				{#if showPreview}
					<div class="card bg-base-100 border border-base-300 shadow-sm">
						<div class="card-body p-5 space-y-3">
							<div class="flex flex-wrap items-center justify-between gap-2">
								<h2 class="text-base font-semibold">
									{tp('export.import.previewTitle', previewRows.length)}
								</h2>
								<div class="flex flex-wrap items-center gap-3 text-sm">
									{#if previewDuplicateCount > 0}
										<span class="badge badge-warning badge-sm">{tp('export.import.duplicates', previewDuplicateCount)}</span>
									{/if}
									<span class="text-base-content/70">{t('export.import.selected', { count: selectedRows.size })}</span>
									<button type="button" class="btn btn-ghost btn-xs" onclick={selectAll}>{t('export.import.all')}</button>
									<button type="button" class="btn btn-ghost btn-xs" onclick={deselectAll}>{t('export.import.none')}</button>
								</div>
							</div>

							{#if previewWarnings.length > 0}
								<div class="alert alert-warning text-xs py-2 flex-col items-start">
									{#each previewWarnings as w, i (i)}
										<p>{w}</p>
									{/each}
								</div>
							{/if}

							<div class="overflow-x-auto max-h-96 overflow-y-auto">
								<table class="table table-xs table-zebra w-full">
									<thead class="sticky top-0 bg-base-200 z-10">
										<tr>
											<th class="w-8">
												<input
													type="checkbox"
													class="checkbox checkbox-xs"
													aria-label={t('export.import.selectAllRows')}
													checked={previewRows.length > 0 && selectedRows.size === previewRows.length}
													onchange={() =>
														selectedRows.size === previewRows.length
															? deselectAll()
															: selectAll()}
												/>
											</th>
											<th>{t('export.import.col.date')}</th>
											<th>{t('export.import.col.start')}</th>
											<th>{t('export.import.col.end')}</th>
											<th>{t('export.import.col.pause')}</th>
											<th>{t('export.import.col.description')}</th>
											<th>{t('export.import.col.status')}</th>
										</tr>
									</thead>
									<tbody>
										{#each previewRows as row (row.rowNumber)}
											<tr class={row.isDuplicate ? 'bg-warning/10' : row.warning ? 'bg-error/10' : ''}>
												<td>
													<input
														type="checkbox"
														class="checkbox checkbox-xs"
														aria-label={t('export.import.selectRow', { row: row.rowNumber ?? '' })}
														checked={selectedRows.has(row.rowNumber!)}
														onchange={() => toggleRow(row.rowNumber!)}
													/>
												</td>
												<td class="font-mono text-xs whitespace-nowrap">{row.date ? formatDate(row.date) : '—'}</td>
												<td class="font-mono text-xs">{row.startTime}</td>
												<td class="font-mono text-xs">{row.endTime}</td>
												<td class="text-xs whitespace-nowrap">{t('export.import.minutes', { value: row.pauseMinutes ?? 0 })}</td>
												<td class="text-xs max-w-32 truncate">{row.description || '—'}</td>
												<td>
													{#if row.isDuplicate}
														<span class="badge badge-warning badge-xs">{t('export.import.status.duplicate')}</span>
													{:else if row.warning}
														<span class="badge badge-error badge-xs" title={row.warning}>{t('export.import.status.error')}</span>
													{:else}
														<span class="badge badge-success badge-xs">{t('export.import.status.ok')}</span>
													{/if}
												</td>
											</tr>
										{/each}
									</tbody>
								</table>
							</div>

							<div class="flex flex-wrap items-center gap-3 pt-2 border-t border-base-200">
								{#if importAllowed}
									<button
										type="button"
										class="btn btn-primary"
										onclick={doImport}
										disabled={importConfirming || selectedRows.size === 0}
									>
										{#if importConfirming}
											<span class="loading loading-spinner loading-sm"></span>
											{t('export.import.importing')}
										{:else}
											{tp('export.import.importCount', selectedRows.size)}
										{/if}
									</button>
								{:else if importNeedsApproval}
									<button
										type="button"
										class="btn btn-warning"
										onclick={openRequestModal}
										disabled={selectedRows.size === 0}
									>
										{t('export.import.requestApproval')}
									</button>
									<span class="text-xs text-base-content/70">{t('export.import.approvalHint')}</span>
								{/if}
								<button type="button" class="btn btn-ghost btn-sm" onclick={resetPreview} disabled={importConfirming}>
									{t('common.cancel')}
								</button>
							</div>
						</div>
					</div>
				{/if}

				{#if importError}
					<div class="alert alert-error text-sm">{importError}</div>
				{/if}
			{/if}
		</div>
	{/if}

	<!-- ═══════════ PDF REPORT TAB ═══════════ -->
	{#if activeTab === 'report'}
		<div class="space-y-5">
			{#if !orgContext.selectedOrg}
				{@render noOrgState(t('export.noOrg.reportText'))}
			{:else}
				{@render memberSelector()}

				<div class="card bg-base-100 border border-base-300 shadow-sm">
					<div class="card-body p-5 space-y-3">
						<h2 class="text-base font-semibold">{t('export.report.monthlyTitle')}</h2>
						<p class="text-sm text-base-content/70">{t('export.report.monthlyText')}</p>
						<div class="flex items-center gap-2 flex-wrap">
							<select class="select select-sm" bind:value={reportMonth} aria-label={t('export.report.month')}>
								{#each Array.from({ length: 12 }, (_, i) => i + 1) as m (m)}
									<option value={m}>{getMonthName(new Date(2000, m - 1))}</option>
								{/each}
							</select>
							<input type="number" class="input input-sm w-24" class:input-error={!validYear(reportMonthYear)} bind:value={reportMonthYear} min="2000" max="2100" aria-label={t('export.report.year')} aria-invalid={!validYear(reportMonthYear)} />
							<button type="button" class="btn btn-primary btn-sm" onclick={downloadMonthlyReport} disabled={monthlyReportDownloading || !validYear(reportMonthYear)}>
								{#if monthlyReportDownloading}
									<span class="loading loading-spinner loading-xs"></span> {t('export.report.generating')}
								{:else}
									{t('export.report.download')}
								{/if}
							</button>
						</div>
					</div>
				</div>

				<div class="card bg-base-100 border border-base-300 shadow-sm">
					<div class="card-body p-5 space-y-3">
						<h2 class="text-base font-semibold">{t('export.report.yearlyTitle')}</h2>
						<p class="text-sm text-base-content/70">{t('export.report.yearlyText')}</p>
						<div class="flex items-center gap-2 flex-wrap">
							<input type="number" class="input input-sm w-24" class:input-error={!validYear(reportYear)} bind:value={reportYear} min="2000" max="2100" aria-label={t('export.report.year')} aria-invalid={!validYear(reportYear)} />
							<button type="button" class="btn btn-primary btn-sm" onclick={downloadYearlyReport} disabled={yearlyReportDownloading || !validYear(reportYear)}>
								{#if yearlyReportDownloading}
									<span class="loading loading-spinner loading-xs"></span> {t('export.report.generating')}
								{:else}
									{t('export.report.download')}
								{/if}
							</button>
						</div>
					</div>
				</div>

				{#if reportError}
					<div class="alert alert-error text-sm">{reportError}</div>
				{/if}
			{/if}
		</div>
	{/if}
</div>

<!-- Request Approval Modal -->
<Modal open={showRequestModal} onclose={() => { if (!requestSending) showRequestModal = false; }} title={t('export.request.title')}>
	<p class="text-sm text-base-content/70 mb-3">{tp('export.request.text', selectedRows.size)}</p>
	<label class="label" for="request-message">
		<span>{t('export.request.message')} ({t('common.optional')})</span>
	</label>
	<textarea
		id="request-message"
		class="textarea h-24 w-full"
		placeholder={t('export.request.placeholder')}
		bind:value={requestMessage}
		disabled={requestSending}
	></textarea>
	{#if requestError}
		<div class="alert alert-error text-sm mt-3">{requestError}</div>
	{/if}
	{#snippet actions()}
		<button type="button" class="btn btn-ghost" onclick={() => (showRequestModal = false)} disabled={requestSending}>
			{t('common.cancel')}
		</button>
		<button type="button" class="btn btn-primary" onclick={submitImportRequest} disabled={requestSending}>
			{#if requestSending}
				<span class="loading loading-spinner loading-sm"></span>
				{t('common.sending')}
			{:else}
				{t('export.request.submit')}
			{/if}
		</button>
	{/snippet}
</Modal>
