<script lang="ts">
	import { orgContext } from '$lib/stores/orgContext.svelte';
	import { exportImportApi, organizationsApi, requestsApi } from '$lib/apiClient';
	import type {
		ExportRequest,
		OrganizationDetailResponse,
		ImportPreviewRow,
		ImportEntryRequest
	} from '$lib/api';
	import { RequestType } from '$lib/api';
	import { extractErrorMessage } from '$lib/utils/errorHandler';
	import { csvImportEnabled, canImportCsv, isAllowed, isDisabled, isRequiresApproval } from '$lib/utils/orgRules';

	// ── Tab state ──
	let activeTab = $state<'export' | 'import'>('export');

	// ── Org detail (for import rule check) ──
	let orgDetail = $state<OrganizationDetailResponse | null>(null);

	$effect(() => {
		const slug = orgContext.selectedOrgSlug;
		if (slug) loadOrgDetail(slug);
		else orgDetail = null;
	});

	async function loadOrgDetail(slug: string) {
		try {
			const { data } = await organizationsApi.apiV1OrganizationsSlugGet(slug);
			orgDetail = data;
		} catch {
			orgDetail = null;
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
	let exportSuccess = $state('');

	// Column selection
	const entryColumns = [
		{ key: 'Date', label: 'Date', default: true },
		{ key: 'DayOfWeek', label: 'Day of Week', default: true },
		{ key: 'StartTime', label: 'Start Time', default: true },
		{ key: 'EndTime', label: 'End Time', default: true },
		{ key: 'Duration', label: 'Duration (h)', default: true },
		{ key: 'Pause', label: 'Pause (min)', default: true },
		{ key: 'NetDuration', label: 'Net Duration (h)', default: true },
		{ key: 'Description', label: 'Description', default: true },
		{ key: 'Organization', label: 'Organization', default: false }
	];

	const dailyColumns = [
		{ key: 'Date', label: 'Date', default: true },
		{ key: 'DayOfWeek', label: 'Day of Week', default: true },
		{ key: 'TargetHours', label: 'Target Hours', default: true },
		{ key: 'WorkedHours', label: 'Worked Hours', default: true },
		{ key: 'Pause', label: 'Pause (min)', default: true },
		{ key: 'NetWorkedHours', label: 'Net Worked (h)', default: true },
		{ key: 'Overtime', label: 'Overtime', default: true },
		{ key: 'CumulativeOvertime', label: 'Cumul. Overtime', default: true },
		{ key: 'Status', label: 'Status', default: true },
		{ key: 'HolidayName', label: 'Holiday Name', default: false },
		{ key: 'AbsenceType', label: 'Absence Type', default: false },
		{ key: 'AbsenceNote', label: 'Absence Note', default: false },
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
					from: start.toISOString().split('T')[0],
					to: end.toISOString().split('T')[0]
				};
			}
			case 'month':
				return {
					from: `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-01`,
					to: new Date(now.getFullYear(), now.getMonth() + 1, 0).toISOString().split('T')[0]
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

	async function doExport() {
		exporting = true;
		exportError = '';
		exportSuccess = '';

		try {
			const { from, to } = computeDateRange();
			const columns =
				exportType === 'entries'
					? [...selectedEntryColumns]
					: [...selectedDailyColumns];
			const orgSlug =
				exportScope === 'org' ? (orgContext.selectedOrgSlug ?? undefined) : undefined;

			if (exportType === 'daily' && !orgSlug) {
				exportError = 'Daily report requires an organization. Please select one.';
				return;
			}

			const request: ExportRequest = {
				type: exportType,
				organizationSlug: orgSlug,
				from: from ? new Date(from).toISOString() : undefined,
				to: to ? new Date(to).toISOString() : undefined,
				columns
			};

			const response = await exportImportApi.apiV1ExportImportExportPost(request, {
				responseType: 'blob'
			});

			// Download the file
			const blob = new Blob([response.data as unknown as BlobPart], { type: 'text/csv' });
			const url = URL.createObjectURL(blob);
			const a = document.createElement('a');
			a.href = url;
			const label = exportType === 'daily' ? `daily-report-${orgSlug}` : 'time-entries';
			a.download = `${label}-${new Date().toISOString().split('T')[0]}.csv`;
			a.click();
			URL.revokeObjectURL(url);

			exportSuccess = 'CSV file downloaded successfully!';
		} catch (err) {
			exportError = extractErrorMessage(err, 'Failed to export.');
		} finally {
			exporting = false;
		}
	}

	// ── Import state ──
	let importFile = $state<File | null>(null);
	let importPreviewing = $state(false);
	let importConfirming = $state(false);
	let importError = $state('');
	let importSuccess = $state('');
	let previewRows = $state<ImportPreviewRow[]>([]);
	let previewWarnings = $state<string[]>([]);
	let previewDuplicateCount = $state(0);
	let selectedRows = $state<Set<number>>(new Set());
	let showPreview = $state(false);

	function onFileChange(e: Event) {
		const target = e.target as HTMLInputElement;
		importFile = target.files?.[0] ?? null;
		showPreview = false;
		previewRows = [];
		importError = '';
		importSuccess = '';
	}

	async function doPreview() {
		if (!importFile || !orgContext.selectedOrgSlug) return;

		importPreviewing = true;
		importError = '';
		importSuccess = '';

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
			importError = extractErrorMessage(err, 'Failed to preview CSV.');
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
		importSuccess = '';

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

			importSuccess = `Imported ${data.importedCount} entries.` +
				(data.skippedCount ? ` Skipped ${data.skippedCount}.` : '');

			if (data.errors && data.errors.length > 0) {
				importError = data.errors.join('; ');
			}

			showPreview = false;
			previewRows = [];
			importFile = null;
		} catch (err) {
			importError = extractErrorMessage(err, 'Failed to import.');
		} finally {
			importConfirming = false;
		}
	}

	// Derived helpers
	let availableColumns = $derived(exportType === 'entries' ? entryColumns : dailyColumns);
	let activeColumnSet = $derived(
		exportType === 'entries' ? selectedEntryColumns : selectedDailyColumns
	);
	let canImport = $derived(orgContext.selectedOrg && csvImportEnabled(orgDetail));
	let importAllowed = $derived(orgContext.selectedOrg && canImportCsv(orgDetail));
	let importNeedsApproval = $derived(orgContext.selectedOrg && isRequiresApproval(orgDetail?.csvImportMode));
	let importDisabledReason = $derived(
		!orgContext.selectedOrg
			? 'Select an organization first'
			: isDisabled(orgDetail?.csvImportMode)
				? 'CSV import is disabled for this organization'
				: !isAllowed(orgDetail?.csvImportMode)
					? 'CSV import requires admin approval in this organization'
					: ''
	);

	// ── Import request approval state ──
	let requestMessage = $state('');
	let requestSending = $state(false);
	let requestSuccess = $state('');
	let showRequestModal = $state(false);

	function openRequestModal() {
		requestMessage = '';
		requestSuccess = '';
		showRequestModal = true;
	}

	async function submitImportRequest() {
		if (!orgContext.selectedOrgSlug) return;
		requestSending = true;
		try {
			const summary = previewRows.length > 0
				? `${selectedRows.size} entries from CSV (${previewRows.length} total parsed)`
				: 'CSV import';
			const requestData = JSON.stringify({
				entryCount: selectedRows.size,
				fileName: importFile?.name ?? 'unknown'
			});
			await requestsApi.apiV1OrganizationsSlugRequestsPost(orgContext.selectedOrgSlug, {
				type: RequestType.NUMBER_5,
				message: requestMessage || `Request to import ${summary}`,
				requestData
			});
			requestSuccess = 'Import request submitted! An admin will review it.';
			showRequestModal = false;
			setTimeout(() => (requestSuccess = ''), 5000);
		} catch (err) {
			importError = extractErrorMessage(err, 'Failed to submit request.');
		} finally {
			requestSending = false;
		}
	}
</script>

<svelte:head>
	<title>Export & Import | Time Tracking</title>
</svelte:head>

<div class="space-y-6">
	<h1 class="text-2xl font-bold">Export & Import</h1>

	<!-- Tab buttons -->
	<div class="flex gap-1 rounded-lg border border-base-300 bg-base-200 p-1 w-fit">
		<button
			class="rounded-md px-4 py-1.5 text-sm font-medium transition-colors {activeTab === 'export'
				? 'bg-base-100 text-base-content shadow-sm'
				: 'text-base-content/60 hover:text-base-content'}"
			onclick={() => (activeTab = 'export')}
		>
			Export
		</button>
		<button
			class="rounded-md px-4 py-1.5 text-sm font-medium transition-colors {activeTab === 'import'
				? 'bg-base-100 text-base-content shadow-sm'
				: 'text-base-content/60 hover:text-base-content'}"
			onclick={() => (activeTab = 'import')}
		>
			Import
		</button>
	</div>

	<!-- ═══════════ EXPORT TAB ═══════════ -->
	{#if activeTab === 'export'}
		<div class="space-y-5">
			<!-- Export Type -->
			<div class="card bg-base-100 border border-base-300 shadow-sm">
				<div class="card-body p-5 space-y-4">
					<h2 class="text-base font-semibold">Export Type</h2>
					<div class="flex gap-3">
						<label class="flex items-center gap-2 cursor-pointer">
							<input
								type="radio"
								name="exportType"
								class="radio radio-sm radio-primary"
								value="entries"
								checked={exportType === 'entries'}
								onchange={() => (exportType = 'entries')}
							/>
							<span class="text-sm">Individual Entries</span>
						</label>
						<label class="flex items-center gap-2 cursor-pointer">
							<input
								type="radio"
								name="exportType"
								class="radio radio-sm radio-primary"
								value="daily"
								checked={exportType === 'daily'}
								onchange={() => (exportType = 'daily')}
							/>
							<span class="text-sm">Daily Summary</span>
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
							<label class="label text-xs font-medium text-base-content/60" for="exportScope">Organization Scope</label>
							<select
								id="exportScope"
								class="select select-bordered select-sm w-full"
								bind:value={exportScope}
								disabled={exportType === 'daily'}
							>
								<option value="org">Current Organization ({orgContext.selectedOrg?.name ?? 'none'})</option>
								<option value="all">All Organizations</option>
							</select>
							{#if exportType === 'daily'}
								<p class="text-xs text-base-content/40 mt-1">Daily summary always uses current org.</p>
							{/if}
						</div>

						<!-- Time Range -->
						<div>
							<label class="label text-xs font-medium text-base-content/60" for="exportRange">Time Range</label>
							<select
								id="exportRange"
								class="select select-bordered select-sm w-full"
								bind:value={exportRange}
							>
								<option value="week">This Week</option>
								<option value="month">This Month</option>
								<option value="year">This Year</option>
								<option value="all">All Time</option>
								<option value="custom">Custom Range</option>
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
									bind:value={customFrom}
								/>
							</div>
							<div>
								<label class="label text-xs font-medium text-base-content/60" for="customTo">To</label>
								<input
									id="customTo"
									type="date"
									class="input input-bordered input-sm w-full"
									bind:value={customTo}
								/>
							</div>
						</div>
					{/if}
				</div>
			</div>

			<!-- Column Selection -->
			<div class="card bg-base-100 border border-base-300 shadow-sm">
				<div class="card-body p-5 space-y-3">
					<div class="flex items-center justify-between">
						<h2 class="text-base font-semibold">Columns</h2>
						<div class="flex gap-2">
							<button
								class="btn btn-ghost btn-xs"
								onclick={() => {
									if (exportType === 'entries')
										selectedEntryColumns = new Set(entryColumns.map((c) => c.key));
									else selectedDailyColumns = new Set(dailyColumns.map((c) => c.key));
								}}>Select All</button
							>
							<button
								class="btn btn-ghost btn-xs"
								onclick={() => {
									if (exportType === 'entries') selectedEntryColumns = new Set();
									else selectedDailyColumns = new Set();
								}}>Deselect All</button
							>
						</div>
					</div>
					<div class="flex flex-wrap gap-2">
						{#each availableColumns as col}
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
				</div>
			</div>

			<!-- Export button -->
			{#if exportError}
				<div class="alert alert-error text-sm">{exportError}</div>
			{/if}
			{#if exportSuccess}
				<div class="alert alert-success text-sm">{exportSuccess}</div>
			{/if}
			<button
				class="btn btn-primary"
				onclick={doExport}
				disabled={exporting || activeColumnSet.size === 0}
			>
				{#if exporting}
					<span class="loading loading-spinner loading-sm"></span>
					Exporting...
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
				<div class="alert alert-warning text-sm">
					Please select an organization to import time entries.
				</div>
			{:else if !canImport}
				<div class="alert alert-warning text-sm">
					{importDisabledReason}
				</div>
			{:else}
				<!-- Import info -->
				<div class="card bg-base-100 border border-base-300 shadow-sm">
					<div class="card-body p-5 space-y-3">
						<h2 class="text-base font-semibold">Import CSV File</h2>
						<p class="text-sm text-base-content/60">
							Upload a CSV file with time entries. The file must contain <strong>Date</strong>,
							<strong>Start Time</strong>, and <strong>End Time</strong> columns.
							Optional columns: <em>Pause (min)</em>, <em>Description</em>.
						</p>

						{#if !importAllowed}
							<div class="alert alert-info text-sm py-2">
								CSV import requires admin approval. You can preview your file but importing is not available.
							</div>
						{/if}

						<!-- File picker -->
						<div class="flex flex-col sm:flex-row items-start sm:items-end gap-3">
							<label class="flex flex-col gap-1.5 cursor-pointer w-full sm:w-auto">
								<span class="text-xs font-medium text-base-content/60">CSV File</span>
								<div class="flex items-center gap-3 rounded-lg border-2 border-dashed border-base-300 hover:border-primary/50 px-4 py-3 transition-colors {importFile ? 'border-primary/40 bg-primary/5' : ''}">
									<svg class="w-5 h-5 text-base-content/40 shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
										<path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M7 16a4 4 0 01-.88-7.903A5 5 0 1115.9 6L16 6a5 5 0 011 9.9M15 13l-3-3m0 0l-3 3m3-3v12"></path>
									</svg>
									<span class="text-sm {importFile ? 'text-primary font-medium' : 'text-base-content/50'}">
										{importFile ? importFile.name : 'Choose a .csv file...'}
									</span>
									<input
										type="file"
										accept=".csv,text/csv"
										class="hidden"
										onchange={onFileChange}
									/>
								</div>
							</label>
							<button
								class="btn btn-primary btn-sm"
								onclick={doPreview}
								disabled={!importFile || importPreviewing}
							>
								{#if importPreviewing}
									<span class="loading loading-spinner loading-xs"></span>
									Parsing...
								{:else}
									Preview File
								{/if}
							</button>
						</div>
					</div>
				</div>

				<!-- Preview results -->
				{#if showPreview}
					<div class="card bg-base-100 border border-base-300 shadow-sm">
						<div class="card-body p-5 space-y-3">
							<div class="flex items-center justify-between">
								<h2 class="text-base font-semibold">
									Preview ({previewRows.length} rows)
								</h2>
								<div class="flex items-center gap-3 text-sm">
									{#if previewDuplicateCount > 0}
										<span class="badge badge-warning badge-sm">{previewDuplicateCount} duplicates</span>
									{/if}
									<span class="text-base-content/50">{selectedRows.size} selected</span>
									<button class="btn btn-ghost btn-xs" onclick={selectAll}>All</button>
									<button class="btn btn-ghost btn-xs" onclick={deselectAll}>None</button>
								</div>
							</div>

							{#if previewWarnings.length > 0}
								<div class="alert alert-warning text-xs py-2">
									{#each previewWarnings as w}
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
													checked={selectedRows.size === previewRows.length}
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
										{#each previewRows as row}
											<tr
												class="{row.isDuplicate
													? 'bg-warning/10'
													: row.warning
														? 'bg-error/10'
														: ''}"
											>
												<td>
													<input
														type="checkbox"
														class="checkbox checkbox-xs"
														checked={selectedRows.has(row.rowNumber!)}
														onchange={() => toggleRow(row.rowNumber!)}
													/>
												</td>
												<td class="font-mono text-xs">{row.date}</td>
												<td class="font-mono text-xs">{row.startTime}</td>
												<td class="font-mono text-xs">{row.endTime}</td>
												<td class="text-xs">{row.pauseMinutes} min</td>
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

								<div class="flex items-center gap-3 pt-2 border-t border-base-200">
									{#if importAllowed}
										<button
											class="btn btn-primary"
											onclick={doImport}
											disabled={importConfirming || selectedRows.size === 0}
										>
											{#if importConfirming}
												<span class="loading loading-spinner loading-sm"></span>
												Importing...
											{:else}
												Import {selectedRows.size} Entries
											{/if}
										</button>
									{:else if importNeedsApproval}
										<button
											class="btn btn-warning"
											onclick={openRequestModal}
											disabled={selectedRows.size === 0}
										>
											Request Import Approval
										</button>
										<span class="text-xs text-base-content/50">Admin must approve before entries are imported</span>
									{/if}
									<button
										class="btn btn-ghost btn-sm"
										onclick={() => {
											showPreview = false;
											previewRows = [];
										}}
									>
										Cancel
									</button>
								</div>
						</div>
					</div>
				{/if}

				<!-- Import results -->
				{#if importError}
					<div class="alert alert-error text-sm">{importError}</div>
				{/if}
				{#if importSuccess}
					<div class="alert alert-success text-sm">{importSuccess}</div>
				{/if}
				{#if requestSuccess}
					<div class="alert alert-success text-sm">{requestMessage}</div>
				{/if}
			{/if}
		</div>
	{/if}
</div>

<!-- Request Approval Modal -->
{#if showRequestModal}
	<div class="fixed inset-0 bg-black/50 z-50 flex items-center justify-center p-4">
		<div class="card bg-base-100 shadow-xl w-full max-w-md">
			<div class="card-body">
				<h3 class="card-title text-lg">Request CSV Import Approval</h3>
				<p class="text-sm text-base-content/70">
					Your organization requires admin approval for CSV imports.
					Submit a request to import {selectedRows.size} entries.
				</p>
				<div class="form-control">
					<label class="label" for="request-message">
						<span class="label-text">Message (optional)</span>
					</label>
					<textarea
						id="request-message"
						class="textarea textarea-bordered h-24"
						placeholder="Add a note for the admin..."
						bind:value={requestMessage}
					></textarea>
				</div>
				{#if importError}
					<div class="alert alert-error text-sm">{importError}</div>
				{/if}
				<div class="card-actions justify-end mt-2">
					<button
						class="btn btn-ghost"
						onclick={() => (showRequestModal = false)}
						disabled={requestSending}
					>
						Cancel
					</button>
					<button
						class="btn btn-primary"
						onclick={submitImportRequest}
						disabled={requestSending}
					>
						{#if requestSending}
							<span class="loading loading-spinner loading-sm"></span>
							Sending...
						{:else}
							Submit Request
						{/if}
					</button>
				</div>
			</div>
		</div>
	</div>
{/if}
