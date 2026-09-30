<script lang="ts">
	import { untrack } from 'svelte';
	import { auth } from '$lib/stores/auth.svelte';
	import { orgContext } from '$lib/stores/orgContext.svelte';
	import { toast } from '$lib/stores/toast.svelte';
	import { exportImportApi, groupApi, organizationsApi, requestsApi, reportsApi } from '$lib/apiClient';
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
	import Modal from '$lib/components/Modal.svelte';

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
				organizationsApi.apiV1OrganizationsSlugGet(slug),
				groupApi.apiV1OrganizationsSlugEffectivePermissionsGet(slug)
			]);
			if (slug !== orgContext.selectedOrgSlug) return; // org switched meanwhile
			orgDetail = detail.data;
			perms = effective.data;
		} catch (err) {
			orgDetail = null;
			perms = null;
			orgLoadError = extractErrorMessage(err, 'Could not load the organization.');
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

	// Column selection
	const entryColumns = [
		{ key: 'Date', label: 'Date', default: true },
		{ key: 'DayOfWeek', label: 'Day of week', default: true },
		{ key: 'StartTime', label: 'Start time', default: true },
		{ key: 'EndTime', label: 'End time', default: true },
		{ key: 'Duration', label: 'Duration (h)', default: true },
		{ key: 'Pause', label: 'Pause (min)', default: true },
		{ key: 'NetDuration', label: 'Net duration (h)', default: true },
		{ key: 'Description', label: 'Description', default: true },
		{ key: 'Organization', label: 'Organization', default: false }
	];

	const dailyColumns = [
		{ key: 'Date', label: 'Date', default: true },
		{ key: 'DayOfWeek', label: 'Day of week', default: true },
		{ key: 'TargetHours', label: 'Target hours', default: true },
		{ key: 'WorkedHours', label: 'Worked hours', default: true },
		{ key: 'Pause', label: 'Pause (min)', default: true },
		{ key: 'NetWorkedHours', label: 'Net worked (h)', default: true },
		{ key: 'Overtime', label: 'Overtime', default: true },
		{ key: 'CumulativeOvertime', label: 'Cumulative overtime', default: true },
		{ key: 'Status', label: 'Status', default: true },
		{ key: 'HolidayName', label: 'Holiday name', default: false },
		{ key: 'AbsenceType', label: 'Absence type', default: false },
		{ key: 'AbsenceNote', label: 'Absence note', default: false },
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
				? 'Exporting for another member requires an organization.'
				: 'The daily summary requires an organization.'
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
				columns
			};

			const response = await exportImportApi.apiV1ExportImportExportPost(request, {
				responseType: 'blob'
			});

			const targetSuffix = targetUserId !== null ? `-member${targetUserId}` : '';
			const label = exportType === 'daily' ? `daily-report-${orgSlug}${targetSuffix}` : `time-entries${targetSuffix}`;
			downloadBlob(response.data as unknown as BlobPart, 'text/csv', `${label}-${dateKey(new Date())}.csv`);
			toast.success('CSV file downloaded.');
		} catch (err) {
			exportError = extractErrorMessage(err, 'Export failed.');
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
			fileError = 'Please choose a .csv file.';
			importFile = null;
		} else if (file && file.size === 0) {
			fileError = 'The file is empty.';
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
			importError = extractErrorMessage(err, 'Could not read the CSV file.');
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
				`Imported ${data.importedCount ?? 0} entries.` + (data.skippedCount ? ` Skipped ${data.skippedCount}.` : '')
			);

			if (data.errors && data.errors.length > 0) {
				importError = data.errors.join('; ');
			}

			resetImport();
		} catch (err) {
			importError = extractErrorMessage(err, 'Import failed.');
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

	function downloadBlob(data: BlobPart, type: string, filename: string) {
		const blob = new Blob([data], { type });
		const url = URL.createObjectURL(blob);
		const a = document.createElement('a');
		a.href = url;
		a.download = filename;
		a.click();
		URL.revokeObjectURL(url);
	}

	async function downloadMonthlyReport() {
		const orgSlug = orgContext.selectedOrgSlug;
		const memberId = targetUserId ?? auth.user?.id;
		if (!orgSlug || !memberId || !validYear(reportMonthYear)) return;

		monthlyReportDownloading = true;
		reportError = '';
		try {
			const response = await reportsApi.apiV1OrganizationsSlugMembersMemberIdReportsMonthlyGet(
				orgSlug, memberId, reportMonthYear, reportMonth, { responseType: 'blob' }
			);
			downloadBlob(response.data as unknown as BlobPart, 'application/pdf', `time-report-${orgSlug}-${reportMonthYear}-${String(reportMonth).padStart(2, '0')}.pdf`);
			toast.success('PDF report downloaded.');
		} catch (e) {
			reportError = extractErrorMessage(e, 'Could not generate the monthly report.');
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
				orgSlug, memberId, reportYear, { responseType: 'blob' }
			);
			downloadBlob(response.data as unknown as BlobPart, 'application/pdf', `time-report-${orgSlug}-${reportYear}.pdf`);
			toast.success('PDF report downloaded.');
		} catch (e) {
			reportError = extractErrorMessage(e, 'Could not generate the yearly report.');
		} finally {
			yearlyReportDownloading = false;
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
				message: requestMessage.trim() || `Request to import ${entries.length} entries from CSV (${previewRows.length} parsed)`,
				requestData
			});
			toast.success('Import request submitted. An admin will review it.');
			showRequestModal = false;
			resetImport();
		} catch (err) {
			requestError = extractErrorMessage(err, 'Could not submit the request.');
		} finally {
			requestSending = false;
		}
	}
</script>

<svelte:head>
	<title>Export & Import | Time Tracking</title>
</svelte:head>

{#snippet noOrgState(text: string)}
	<div class="card bg-base-100 border border-base-300 shadow-sm">
		<div class="card-body p-5 items-start gap-3">
			<h2 class="text-base font-semibold">No organization selected</h2>
			<p class="text-sm text-base-content/60">{text}</p>
			<div class="flex flex-wrap gap-2">
				{#if orgContext.organizations.length > 0}
					<a href="/select-org" class="btn btn-primary btn-sm">Select organization</a>
				{:else}
					<a href="/organizations" class="btn btn-primary btn-sm">Create or join an organization</a>
				{/if}
			</div>
		</div>
	</div>
{/snippet}

<div class="space-y-6">
	<h1 class="text-2xl font-bold">Export & Import</h1>

	<!-- Tab buttons -->
	<div class="flex gap-1 rounded-lg border border-base-300 bg-base-200 p-1 w-fit max-w-full overflow-x-auto">
		{#each [['export', 'Export'], ['import', 'Import'], ['report', 'PDF report']] as const as [tab, label] (tab)}
			<button
				type="button"
				class="rounded-md px-4 py-1.5 text-sm font-medium whitespace-nowrap transition-colors {activeTab === tab
					? 'bg-base-100 text-base-content shadow-sm'
					: 'text-base-content/60 hover:text-base-content'}"
				aria-pressed={activeTab === tab}
				onclick={() => (activeTab = tab)}
			>
				{label}
			</button>
		{/each}
	</div>

	{#if orgLoadError}
		<div class="alert alert-error text-sm">
			<span>{orgLoadError}</span>
			{#if orgContext.selectedOrgSlug}
				<button type="button" class="btn btn-sm" onclick={() => loadOrgDetail(orgContext.selectedOrgSlug!)}>Try again</button>
			{/if}
		</div>
	{/if}

	<!-- Shared "exporting for" member picker (visible on Export and PDF Report tabs) -->
	{#snippet memberSelector()}
		{#if canExportOthers}
			<div class="card bg-base-100 border border-base-300 shadow-sm">
				<div class="card-body p-5 space-y-2">
					<label for="targetMember" class="text-base font-semibold">Exporting for</label>
					<select id="targetMember" class="select select-bordered select-sm w-full sm:w-72" bind:value={targetUserId}>
						<option value={null}>Myself</option>
						{#each exportableMembers as m (m.id)}
							<option value={m.id}>{m.firstName} {m.lastName}</option>
						{/each}
					</select>
					{#if targetUserId !== null}
						<p class="text-xs text-base-content/60">
							Scoped to {orgContext.selectedOrg?.name}. Requires export permission for this member — org admins
							always have it; group admins need the export-data capability enabled for a shared group.
						</p>
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
					<span>No organization selected — only your entries across all organizations can be exported. <a href="/select-org" class="link">Select an organization</a> for the daily summary.</span>
				</div>
			{/if}

			{@render memberSelector()}

			<!-- Export Type -->
			<div class="card bg-base-100 border border-base-300 shadow-sm">
				<div class="card-body p-5 space-y-4">
					<h2 id="exportTypeLabel" class="text-base font-semibold">Export type</h2>
					<div class="flex flex-wrap gap-x-4 gap-y-2" role="radiogroup" aria-labelledby="exportTypeLabel">
						<label class="flex items-center gap-2 cursor-pointer">
							<input type="radio" name="exportType" class="radio radio-sm radio-primary" value="entries" bind:group={exportType} />
							<span class="text-sm">Individual entries</span>
						</label>
						<label class="flex items-center gap-2 cursor-pointer">
							<input type="radio" name="exportType" class="radio radio-sm radio-primary" value="daily" bind:group={exportType} />
							<span class="text-sm">Daily summary</span>
						</label>
					</div>
				</div>
			</div>

			<!-- Scope & Time Range -->
			<div class="card bg-base-100 border border-base-300 shadow-sm">
				<div class="card-body p-5 space-y-4">
					<h2 class="text-base font-semibold">Filters</h2>

					<div class="grid grid-cols-1 sm:grid-cols-2 gap-4">
						<!-- Scope -->
						<div>
							<label class="label text-xs font-medium text-base-content/60" for="exportScope">Organization scope</label>
							<select
								id="exportScope"
								class="select select-bordered select-sm w-full"
								bind:value={exportScope}
								disabled={forceOrgScope || !orgContext.selectedOrg}
							>
								{#if orgContext.selectedOrg}
									<option value="org">Current organization ({orgContext.selectedOrg.name})</option>
								{/if}
								<option value="all">All organizations</option>
							</select>
							{#if exportType === 'daily'}
								<p class="text-xs text-base-content/60 mt-1">The daily summary always uses the current organization.</p>
							{:else if targetUserId !== null}
								<p class="text-xs text-base-content/60 mt-1">Exporting for another member always uses the current organization.</p>
							{/if}
						</div>

						<!-- Time Range -->
						<div>
							<label class="label text-xs font-medium text-base-content/60" for="exportRange">Time range</label>
							<select id="exportRange" class="select select-bordered select-sm w-full" bind:value={exportRange}>
								<option value="week">This week</option>
								<option value="month">This month</option>
								<option value="year">This year</option>
								<option value="all">All time</option>
								<option value="custom">Custom range</option>
							</select>
						</div>
					</div>

					{#if exportRange === 'custom'}
						<div class="grid grid-cols-1 sm:grid-cols-2 gap-4">
							<div>
								<label class="label text-xs font-medium text-base-content/60" for="customFrom">From</label>
								<input
									id="customFrom"
									type="date"
									class="input input-bordered input-sm w-full"
									class:input-error={customRangeInvalid}
									max={customTo || undefined}
									bind:value={customFrom}
								/>
							</div>
							<div>
								<label class="label text-xs font-medium text-base-content/60" for="customTo">To</label>
								<input
									id="customTo"
									type="date"
									class="input input-bordered input-sm w-full"
									class:input-error={customRangeInvalid}
									min={customFrom || undefined}
									aria-invalid={customRangeInvalid}
									aria-describedby={customRangeInvalid ? 'customRangeError' : undefined}
									bind:value={customTo}
								/>
							</div>
						</div>
						{#if customRangeInvalid}
							<p id="customRangeError" class="text-xs text-error">The start date must not be after the end date.</p>
						{/if}
					{/if}
				</div>
			</div>

			<!-- Column Selection -->
			<div class="card bg-base-100 border border-base-300 shadow-sm">
				<div class="card-body p-5 space-y-3">
					<div class="flex flex-wrap items-center justify-between gap-2">
						<h2 class="text-base font-semibold">Columns</h2>
						<div class="flex gap-2">
							<button
								type="button"
								class="btn btn-ghost btn-xs"
								onclick={() => {
									if (exportType === 'entries')
										selectedEntryColumns = new Set(entryColumns.map((c) => c.key));
									else selectedDailyColumns = new Set(dailyColumns.map((c) => c.key));
								}}>Select all</button
							>
							<button
								type="button"
								class="btn btn-ghost btn-xs"
								onclick={() => {
									if (exportType === 'entries') selectedEntryColumns = new Set();
									else selectedDailyColumns = new Set();
								}}>Deselect all</button
							>
						</div>
					</div>
					<div class="flex flex-wrap gap-2">
						{#each availableColumns as col (col.key)}
							<label
								class="flex items-center gap-1.5 rounded-lg border px-3 py-1.5 text-sm cursor-pointer transition-colors
								{activeColumnSet.has(col.key) ? 'border-primary bg-primary/10 text-primary' : 'border-base-300 text-base-content/60 hover:border-base-content/30'}"
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
								{col.label}
							</label>
						{/each}
					</div>
					{#if activeColumnSet.size === 0}
						<p class="text-xs text-error">Select at least one column.</p>
					{/if}
				</div>
			</div>

			<!-- Export button -->
			{#if exportError}
				<div class="alert alert-error text-sm">{exportError}</div>
			{/if}
			{#if exportBlockedReason}
				<div class="alert alert-warning text-sm">
					<span>{exportBlockedReason} <a href="/select-org" class="link">Select an organization</a></span>
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
					Exporting…
				{:else}
					Download CSV
				{/if}
			</button>
		</div>
	{/if}

	<!-- ═══════════ IMPORT TAB ═══════════ -->
	{#if activeTab === 'import'}
		<div class="space-y-5">
			{#if !orgContext.selectedOrg}
				{@render noOrgState('Time entries are always imported into an organization. Select one first.')}
			{:else if orgLoading && !perms}
				<div class="flex items-center gap-2 text-sm text-base-content/60">
					<span class="loading loading-spinner loading-sm"></span> Loading…
				</div>
			{:else if !canImport}
				<div class="alert alert-warning text-sm">CSV import is disabled for you in this organization.</div>
			{:else}
				<!-- Import info -->
				<div class="card bg-base-100 border border-base-300 shadow-sm">
					<div class="card-body p-5 space-y-3">
						<h2 class="text-base font-semibold">Import CSV file</h2>
						<p class="text-sm text-base-content/60">
							Upload a CSV file with time entries. The file must contain <strong>Date</strong>,
							<strong>Start Time</strong>, and <strong>End Time</strong> columns.
							Optional columns: <em>Pause (min)</em>, <em>Description</em>.
						</p>

						{#if !importAllowed}
							<div class="alert alert-info text-sm py-2">
								CSV import requires admin approval. Preview your file, then request approval for the selected rows.
							</div>
						{/if}

						<!-- File picker -->
						<div class="flex flex-col sm:flex-row items-stretch sm:items-end gap-3">
							<label class="flex flex-col gap-1.5 cursor-pointer w-full sm:w-auto min-w-0">
								<span class="text-xs font-medium text-base-content/60">CSV file</span>
								<div class="flex items-center gap-3 rounded-lg border-2 border-dashed px-4 py-3 transition-colors focus-within:border-primary {fileError ? 'border-error' : importFile ? 'border-primary/40 bg-primary/5' : 'border-base-300 hover:border-primary/50'}">
									<svg class="w-5 h-5 text-base-content/40 shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24" aria-hidden="true">
										<path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M7 16a4 4 0 01-.88-7.903A5 5 0 1115.9 6L16 6a5 5 0 011 9.9M15 13l-3-3m0 0l-3 3m3-3v12"></path>
									</svg>
									<span class="text-sm truncate {importFile ? 'text-primary font-medium' : 'text-base-content/60'}">
										{importFile ? importFile.name : 'Choose a .csv file…'}
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
									Reading…
								{:else}
									Preview file
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
									Preview ({previewRows.length} rows)
								</h2>
								<div class="flex flex-wrap items-center gap-3 text-sm">
									{#if previewDuplicateCount > 0}
										<span class="badge badge-warning badge-sm">{previewDuplicateCount} duplicates</span>
									{/if}
									<span class="text-base-content/60">{selectedRows.size} selected</span>
									<button type="button" class="btn btn-ghost btn-xs" onclick={selectAll}>All</button>
									<button type="button" class="btn btn-ghost btn-xs" onclick={deselectAll}>None</button>
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
													aria-label="Select all rows"
													checked={previewRows.length > 0 && selectedRows.size === previewRows.length}
													onchange={() =>
														selectedRows.size === previewRows.length
															? deselectAll()
															: selectAll()}
												/>
											</th>
											<th>Date</th>
											<th>Start</th>
											<th>End</th>
											<th>Pause</th>
											<th>Description</th>
											<th>Status</th>
										</tr>
									</thead>
									<tbody>
										{#each previewRows as row (row.rowNumber)}
											<tr class={row.isDuplicate ? 'bg-warning/10' : row.warning ? 'bg-error/10' : ''}>
												<td>
													<input
														type="checkbox"
														class="checkbox checkbox-xs"
														aria-label="Select row {row.rowNumber}"
														checked={selectedRows.has(row.rowNumber!)}
														onchange={() => toggleRow(row.rowNumber!)}
													/>
												</td>
												<td class="font-mono text-xs whitespace-nowrap">{row.date}</td>
												<td class="font-mono text-xs">{row.startTime}</td>
												<td class="font-mono text-xs">{row.endTime}</td>
												<td class="text-xs whitespace-nowrap">{row.pauseMinutes} min</td>
												<td class="text-xs max-w-32 truncate">{row.description || '—'}</td>
												<td>
													{#if row.isDuplicate}
														<span class="badge badge-warning badge-xs">Duplicate</span>
													{:else if row.warning}
														<span class="badge badge-error badge-xs" title={row.warning}>Error</span>
													{:else}
														<span class="badge badge-success badge-xs">OK</span>
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
											Importing…
										{:else}
											Import {selectedRows.size} entries
										{/if}
									</button>
								{:else if importNeedsApproval}
									<button
										type="button"
										class="btn btn-warning"
										onclick={openRequestModal}
										disabled={selectedRows.size === 0}
									>
										Request import approval
									</button>
									<span class="text-xs text-base-content/60">An admin must approve before the entries are imported.</span>
								{/if}
								<button type="button" class="btn btn-ghost btn-sm" onclick={resetPreview} disabled={importConfirming}>
									Cancel
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
				{@render noOrgState('PDF reports are generated per organization. Select one first.')}
			{:else}
				{@render memberSelector()}

				<div class="card bg-base-100 border border-base-300 shadow-sm">
					<div class="card-body p-5 space-y-3">
						<h2 class="text-base font-semibold">Monthly report</h2>
						<p class="text-sm text-base-content/60">
							Day-by-day working hours, target, overtime and status for one calendar month.
						</p>
						<div class="flex items-center gap-2 flex-wrap">
							<select class="select select-bordered select-sm" bind:value={reportMonth} aria-label="Report month">
								{#each Array.from({ length: 12 }, (_, i) => i + 1) as m (m)}
									<option value={m}>{new Date(2000, m - 1).toLocaleDateString('en-US', { month: 'long' })}</option>
								{/each}
							</select>
							<input type="number" class="input input-bordered input-sm w-24" class:input-error={!validYear(reportMonthYear)} bind:value={reportMonthYear} min="2000" max="2100" aria-label="Report year" aria-invalid={!validYear(reportMonthYear)} />
							<button type="button" class="btn btn-primary btn-sm" onclick={downloadMonthlyReport} disabled={monthlyReportDownloading || !validYear(reportMonthYear)}>
								{#if monthlyReportDownloading}
									<span class="loading loading-spinner loading-xs"></span> Generating…
								{:else}
									Download PDF
								{/if}
							</button>
						</div>
					</div>
				</div>

				<div class="card bg-base-100 border border-base-300 shadow-sm">
					<div class="card-body p-5 space-y-3">
						<h2 class="text-base font-semibold">Yearly report</h2>
						<p class="text-sm text-base-content/60">
							Month-by-month summary for one calendar year, with running overall cumulative overtime.
						</p>
						<div class="flex items-center gap-2 flex-wrap">
							<input type="number" class="input input-bordered input-sm w-24" class:input-error={!validYear(reportYear)} bind:value={reportYear} min="2000" max="2100" aria-label="Report year" aria-invalid={!validYear(reportYear)} />
							<button type="button" class="btn btn-primary btn-sm" onclick={downloadYearlyReport} disabled={yearlyReportDownloading || !validYear(reportYear)}>
								{#if yearlyReportDownloading}
									<span class="loading loading-spinner loading-xs"></span> Generating…
								{:else}
									Download PDF
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
<Modal open={showRequestModal} onclose={() => { if (!requestSending) showRequestModal = false; }} title="Request CSV import approval">
	<p class="text-sm text-base-content/70 mb-3">
		This organization requires admin approval for CSV imports. Submit a request to import {selectedRows.size} entries.
	</p>
	<label class="label" for="request-message">
		<span class="label-text">Message (optional)</span>
	</label>
	<textarea
		id="request-message"
		class="textarea textarea-bordered h-24 w-full"
		placeholder="Add a note for the admin…"
		bind:value={requestMessage}
		disabled={requestSending}
	></textarea>
	{#if requestError}
		<div class="alert alert-error text-sm mt-3">{requestError}</div>
	{/if}
	{#snippet actions()}
		<button type="button" class="btn btn-ghost" onclick={() => (showRequestModal = false)} disabled={requestSending}>
			Cancel
		</button>
		<button type="button" class="btn btn-primary" onclick={submitImportRequest} disabled={requestSending}>
			{#if requestSending}
				<span class="loading loading-spinner loading-sm"></span>
				Sending…
			{:else}
				Submit request
			{/if}
		</button>
	{/snippet}
</Modal>
