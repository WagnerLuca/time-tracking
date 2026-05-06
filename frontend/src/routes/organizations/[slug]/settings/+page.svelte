<script lang="ts">
	import { onMount } from 'svelte';
	import { page } from '$app/stores';
	import { auth } from '$lib/stores/auth.svelte';
	import { organizationsApi, pauseRulesApi, requestsApi } from '$lib/apiClient';
	import { formatRequestType, formatTimeAgo, statusBadgeClass, parseRequestData } from '$lib/utils/formatters';
	import { extractErrorMessage, getErrorStatus } from '$lib/utils/errorHandler';
	import {
		parseRuleMode, ruleModeLabel, joinPolicyLabel, ruleModeButtonClass,
		ruleSettings, toggleSettings,
		visibilitySettings, parseVisibilityMode, visibilityModeLabel, visibilityModeButtonClass
	} from '$lib/utils/orgRules';
	import type {
		OrganizationDetailResponse,
		UpdateOrganizationSettingsRequest,
		PauseRuleResponse,
		CreatePauseRuleRequest,
		RuleMode,
		VisibilityMode,
		OrgRequestResponse,
	} from '$lib/api';

	let org = $state<OrganizationDetailResponse | null>(null);
	let loading = $state(true);
	let error = $state('');
	let actionError = $state('');

	let myRole = $derived(
		org?.members?.find((m) => m.id === auth.user?.id)?.role ?? null
	);
	let canEdit = $derived(myRole === 'Owner' || myRole === 'Admin');
	let isOwner = $derived(myRole === 'Owner');

	// Settings
	let settingsSaving = $state(false);
	let settingsError = $state('');

	// Pause Rules
	let showAddRule = $state(false);
	let newRuleMinHours = $state(6);
	let newRulePauseMinutes = $state(30);
	let addRuleError = $state('');
	let addingRule = $state(false);
	let editingRuleId = $state<number | null>(null);
	let editRuleMinHours = $state(0);
	let editRulePauseMinutes = $state(0);
	let editRuleError = $state('');
	let editingRuleSaving = $state(false);

	// Request history
	let requestHistory = $state<OrgRequestResponse[]>([]);
	let requestHistoryLoading = $state(false);
	let requestHistoryLoaded = $state(false);
	let requestHistoryFilter = $state<string>('all');

	let orgSlug = $state('');

	onMount(() => {
		orgSlug = $page.params.slug ?? '';
		loadOrg();
	});

	async function loadOrg() {
		loading = true;
		error = '';
		try {
			const { data } = await organizationsApi.apiV1OrganizationsSlugGet(orgSlug);
			org = data;
			loadRequestHistory();
		} catch (err) {
			if (getErrorStatus(err) === 404) { error = 'Organization not found.'; }
			else { error = 'Failed to load organization.'; }
		} finally {
			loading = false;
		}
	}

	async function reloadOrg() {
		try {
			const { data } = await organizationsApi.apiV1OrganizationsSlugGet(orgSlug);
			org = data;
		} catch {}
	}

	async function loadRequestHistory() {
		if (requestHistoryLoaded) return;
		requestHistoryLoading = true;
		try {
			const { data } = await requestsApi.apiV1OrganizationsSlugRequestsGet(orgSlug);
			requestHistory = data.items ?? [];
			requestHistoryLoaded = true;
		} catch {
			requestHistory = [];
		} finally {
			requestHistoryLoading = false;
		}
	}

	function filteredRequests(): OrgRequestResponse[] {
		if (requestHistoryFilter === 'all') return requestHistory;
		return requestHistory.filter(r => r.status === requestHistoryFilter);
	}

	async function cycleSetting(key: string) {
		if (!org) return;
		settingsSaving = true;
		settingsError = '';
		try {
			const current = parseRuleMode((org as Record<string, any>)[key]);
			const next = ((current + 1) % 3) as RuleMode;
			await organizationsApi.apiV1OrganizationsSlugSettingsPut(orgSlug, { [key]: next } as UpdateOrganizationSettingsRequest);
			await reloadOrg();
		} catch (err) {
			settingsError = extractErrorMessage(err, 'Failed to update setting.');
		} finally {
			settingsSaving = false;
		}
	}

	async function cycleVisibilitySetting(key: string) {
		if (!org) return;
		settingsSaving = true;
		settingsError = '';
		try {
			const current = parseVisibilityMode((org as Record<string, any>)[key]);
			const next = ((current + 1) % 3) as VisibilityMode;
			await organizationsApi.apiV1OrganizationsSlugSettingsPut(orgSlug, { [key]: next } as UpdateOrganizationSettingsRequest);
			await reloadOrg();
		} catch (err) {
			settingsError = extractErrorMessage(err, 'Failed to update setting.');
		} finally {
			settingsSaving = false;
		}
	}

	async function toggleSetting(key: string) {
		if (!org) return;
		settingsSaving = true;
		settingsError = '';
		try {
			const current = (org as Record<string, any>)[key];
			await organizationsApi.apiV1OrganizationsSlugSettingsPut(orgSlug, { [key]: !current } as UpdateOrganizationSettingsRequest);
			await reloadOrg();
		} catch (err) {
			settingsError = extractErrorMessage(err, 'Failed to update setting.');
		} finally {
			settingsSaving = false;
		}
	}

	function getPauseRules(): PauseRuleResponse[] {
		return org?.pauseRules ? [...org.pauseRules].sort((a, b) => (a.minHours ?? 0) - (b.minHours ?? 0)) : [];
	}

	async function addPauseRule(e: Event) {
		e.preventDefault();
		addRuleError = '';
		addingRule = true;
		try {
			const payload: CreatePauseRuleRequest = {
				minHours: newRuleMinHours,
				pauseMinutes: newRulePauseMinutes
			};
			await pauseRulesApi.apiV1OrganizationsSlugPauseRulesPost(orgSlug, payload);
			await reloadOrg();
			showAddRule = false;
			newRuleMinHours = 6;
			newRulePauseMinutes = 30;
		} catch (err) {
			addRuleError = extractErrorMessage(err, 'Failed to add rule.');
		} finally {
			addingRule = false;
		}
	}

	function startEditRule(rule: PauseRuleResponse) {
		editingRuleId = rule.id ?? null;
		editRuleMinHours = rule.minHours ?? 0;
		editRulePauseMinutes = rule.pauseMinutes ?? 0;
		editRuleError = '';
	}

	function cancelEditRule() {
		editingRuleId = null;
	}

	async function saveEditRule(ruleId: number) {
		editRuleError = '';
		editingRuleSaving = true;
		try {
			await pauseRulesApi.apiV1OrganizationsSlugPauseRulesRuleIdPut(orgSlug, ruleId, {
				minHours: editRuleMinHours,
				pauseMinutes: editRulePauseMinutes
			});
			await reloadOrg();
			editingRuleId = null;
		} catch (err) {
			editRuleError = extractErrorMessage(err, 'Failed to update rule.');
		} finally {
			editingRuleSaving = false;
		}
	}

	async function deleteRule(ruleId: number) {
		if (!confirm('Delete this pause rule?')) return;
		try {
			await pauseRulesApi.apiV1OrganizationsSlugPauseRulesRuleIdDelete(orgSlug, ruleId);
			await reloadOrg();
		} catch (err) {
			settingsError = extractErrorMessage(err, 'Failed to delete rule.');
		}
	}

</script>

<svelte:head>
	<title>{org ? `Settings - ${org.name}` : 'Settings'} - Time Tracking</title>
</svelte:head>

<div class="max-w-5xl mx-auto px-6 pb-6">
	<div class="flex items-center gap-2 mb-6">
		<a href="/organizations/{orgSlug}" class="text-base-content/60 no-underline text-sm hover:text-primary">&larr; {org?.name ?? 'Organization'}</a>
	</div>

	{#if loading}
		<div class="flex items-center gap-3 justify-center py-12 text-base-content/40"><span class="loading loading-spinner loading-sm"></span><span>Loading...</span></div>
	{:else if error}
		<div class="alert alert-error">{error}</div>
	{:else if org}
		<h1 class="text-2xl font-bold text-base-content mb-1">{canEdit ? 'Settings' : 'Rules'}</h1>
		<p class="text-base-content/50 text-sm mb-6 leading-relaxed">
			{canEdit ? 'Organization-wide rules, permissions, and approval workflows.' : 'Current organization rules and policies. Only admins can change these settings.'}
		</p>

		{#if actionError}
			<div class="alert alert-error mb-4 text-sm">{actionError}</div>
		{/if}

		{#if canEdit}
			<!-- ==================== Admin Settings ==================== -->
			<section>
				<h2 class="text-xl text-base-content/70 mb-4">Organization Settings</h2>
				{#if settingsError}
					<div class="alert alert-error mb-4 text-sm">{settingsError}</div>
				{/if}

				<div class="bg-base-100 border border-base-300 rounded-xl overflow-hidden">
					<!-- Toggle settings (booleans) -->
					{#each toggleSettings as ts}
						{@const value = (org as Record<string, any>)[ts.key]}
						<div class="flex items-center justify-between p-4 border-b border-base-200 last:border-b-0">
							<div class="setting-info">
								<div class="font-semibold text-base-content mb-0.5">{ts.label}</div>
								<div class="text-sm text-base-content/60 max-w-[400px]">{ts.description}</div>
							</div>
							<button
								class="relative w-12 h-[26px] {value ? 'bg-primary' : 'bg-base-300'} rounded-full border-none cursor-pointer transition-colors shrink-0 p-0"
								onclick={() => toggleSetting(ts.key)}
								disabled={settingsSaving}
								aria-label="Toggle {ts.label}"
							>
								<span class="absolute top-[3px] {value ? 'translate-x-[22px]' : 'translate-x-0'} left-[3px] w-5 h-5 bg-base-100 rounded-full transition-transform shadow-sm"></span>
							</button>
						</div>
					{/each}

					<!-- Rule mode settings (tri-state cycle) -->
					{#each ruleSettings as rs}
						{@const mode = (org as Record<string, any>)[rs.key] as string | null}
						{@const label = rs.labelFn ? rs.labelFn(mode) : ruleModeLabel(mode)}
						<div class="flex items-center justify-between p-4 border-b border-base-200 last:border-b-0">
							<div class="setting-info">
								<div class="font-semibold text-base-content mb-0.5">{rs.label}</div>
								<div class="text-sm text-base-content/60 max-w-[400px]">{rs.description}</div>
							</div>
							<button
								class="btn btn-sm min-w-[130px] font-semibold whitespace-nowrap {ruleModeButtonClass(mode)}"
								onclick={() => cycleSetting(rs.key)}
								disabled={settingsSaving}
							>
								{label}
							</button>
						</div>
					{/each}

					<!-- Visibility settings -->
					{#each visibilitySettings as vs}
						{@const mode = (org as Record<string, any>)[vs.key] as string | null}
						<div class="flex items-center justify-between p-4 border-b border-base-200 last:border-b-0">
							<div class="setting-info">
								<div class="font-semibold text-base-content mb-0.5">{vs.label}</div>
								<div class="text-sm text-base-content/60 max-w-[400px]">{vs.description}</div>
							</div>
							<button
								class="btn btn-sm min-w-[130px] font-semibold whitespace-nowrap {visibilityModeButtonClass(mode)}"
								onclick={() => cycleVisibilitySetting(vs.key)}
								disabled={settingsSaving}
							>
								{visibilityModeLabel(mode)}
							</button>
						</div>
					{/each}
				</div>

				<!-- Default Vacation Days -->
				<div class="bg-base-200/50 rounded-lg p-4 mt-4 border border-base-300">
					<div class="flex items-center justify-between">
						<div class="setting-info">
							<div class="font-semibold text-base-content mb-0.5">Default Vacation Days</div>
							<div class="text-sm text-base-content/60 max-w-[400px]">Default number of vacation days per year for new members. Existing members can be set individually.</div>
						</div>
						<div class="flex items-center gap-2">
							<input
								type="number"
								class="input input-bordered input-sm w-20 text-center"
								step="0.5"
								min="0"
								max="365"
								value={org.defaultVacationDays ?? 0}
								onchange={async (e) => {
									const val = parseFloat((e.target as HTMLInputElement).value);
									if (isNaN(val) || val < 0 || !org) return;
									settingsSaving = true;
									try {
										await organizationsApi.apiV1OrganizationsSlugSettingsPut(org.slug!, { defaultVacationDays: val });
										await loadOrg();
									} catch { /* ignore */ }
									settingsSaving = false;
								}}
								disabled={settingsSaving}
							/>
							<span class="text-sm text-base-content/60">days/year</span>
						</div>
					</div>
				</div>

				<!-- Pause Rules -->
				{#if org.autoPauseEnabled}
					<div class="mt-6">
						<div class="flex items-center justify-between mb-4">
							<h3 class="text-base font-bold text-base-content/70">Pause Rules</h3>
							<button class="btn btn-primary btn-sm" onclick={() => (showAddRule = !showAddRule)}>
								{showAddRule ? 'Cancel' : '+ Add Rule'}
							</button>
						</div>

						{#if showAddRule}
							<div class="bg-base-200/50 p-4 rounded-lg mb-4 border border-base-300">
								{#if addRuleError}
									<div class="alert alert-error mb-4 text-sm">{addRuleError}</div>
								{/if}
								<form onsubmit={addPauseRule} class="flex gap-2 items-center flex-wrap">
									<div class="flex items-center gap-1.5">
										<!-- svelte-ignore a11y_label_has_associated_control -->
										<label class="text-sm text-base-content/70 font-medium whitespace-nowrap">When tracked &ge;</label>
										<input type="number" class="input input-bordered input-xs w-[70px] text-center" step="0.5" min="0.5" bind:value={newRuleMinHours} required disabled={addingRule} />
										<span class="text-sm text-base-content/60 whitespace-nowrap">hours</span>
									</div>
									<div class="flex items-center gap-1.5">
										<!-- svelte-ignore a11y_label_has_associated_control -->
										<label class="text-sm text-base-content/70 font-medium whitespace-nowrap">deduct</label>
										<input type="number" class="input input-bordered input-xs w-[70px] text-center" min="1" bind:value={newRulePauseMinutes} required disabled={addingRule} />
										<span class="text-sm text-base-content/60 whitespace-nowrap">min pause</span>
									</div>
									<button type="submit" class="btn btn-primary btn-sm" disabled={addingRule}>
										{addingRule ? 'Adding...' : 'Add Rule'}
									</button>
								</form>
							</div>
						{/if}

						{#if getPauseRules().length === 0}
							<p class="text-base-content/40">No pause rules configured. Add rules to automatically deduct break time.</p>
						{:else}
							<div class="bg-base-100 border border-base-300 rounded-xl overflow-hidden">
								{#each getPauseRules() as rule}
									<div class="flex items-center justify-between p-3 border-b border-base-200 last:border-b-0 flex-wrap gap-2">
										{#if editingRuleId === rule.id}
											{#if editRuleError}
												<div class="alert alert-error mb-4 text-sm" style="width:100%">{editRuleError}</div>
											{/if}
											<div class="flex items-center gap-3 flex-wrap w-full">
												<div class="flex items-center gap-1.5">
													<!-- svelte-ignore a11y_label_has_associated_control -->
													<label class="text-sm text-base-content/70 font-medium whitespace-nowrap">&ge;</label>
													<input type="number" class="input input-bordered input-xs w-[70px] text-center" step="0.5" min="0.5" bind:value={editRuleMinHours} disabled={editingRuleSaving} />
													<span class="text-sm text-base-content/60 whitespace-nowrap">h</span>
												</div>
												<div class="flex items-center gap-1.5">
													<!-- svelte-ignore a11y_label_has_associated_control -->
													<label class="text-sm text-base-content/70 font-medium whitespace-nowrap">&rarr;</label>
													<input type="number" class="input input-bordered input-xs w-[70px] text-center" min="1" bind:value={editRulePauseMinutes} disabled={editingRuleSaving} />
													<span class="text-sm text-base-content/60 whitespace-nowrap">min</span>
												</div>
												<div class="flex items-center gap-1.5">
													<button class="btn btn-primary btn-sm" onclick={() => saveEditRule(rule.id!)} disabled={editingRuleSaving}>Save</button>
													<button class="btn btn-ghost btn-sm" onclick={cancelEditRule}>Cancel</button>
												</div>
											</div>
										{:else}
											<div class="text-[0.9375rem] text-base-content/70">
												<strong>&ge; {rule.minHours}h</strong> tracked &rarr; <strong>{rule.pauseMinutes} min</strong> pause deducted
											</div>
											<div class="flex items-center gap-1.5">
												<button class="btn btn-ghost btn-sm" onclick={() => startEditRule(rule)}>Edit</button>
												<button class="btn btn-ghost btn-xs text-error" title="Delete rule" onclick={() => deleteRule(rule.id!)}>&times;</button>
											</div>
										{/if}
									</div>
								{/each}
							</div>
						{/if}
					</div>
				{/if}
			</section>

<!-- Request History -->
			<section class="mt-8">
				<div class="flex items-center justify-between mb-4">
					<h2 class="text-xl font-bold text-base-content">Request History</h2>
				</div>

				{#if requestHistoryLoading}
					<p class="text-base-content/40">Loading requests...</p>
				{:else if requestHistoryLoaded}
					<div class="flex gap-1.5 mb-4 flex-wrap">
						<button class="btn btn-sm rounded-full {requestHistoryFilter === 'all' ? 'btn-neutral' : 'btn-outline'}" onclick={() => (requestHistoryFilter = 'all')}>All ({requestHistory.length})</button>
						<button class="btn btn-sm rounded-full {requestHistoryFilter === 'Pending' ? 'btn-neutral' : 'btn-outline'}" onclick={() => (requestHistoryFilter = 'Pending')}>Pending ({requestHistory.filter(r => r.status === 'Pending').length})</button>
						<button class="btn btn-sm rounded-full {requestHistoryFilter === 'Accepted' ? 'btn-neutral' : 'btn-outline'}" onclick={() => (requestHistoryFilter = 'Accepted')}>Accepted ({requestHistory.filter(r => r.status === 'Accepted').length})</button>
						<button class="btn btn-sm rounded-full {requestHistoryFilter === 'Declined' ? 'btn-neutral' : 'btn-outline'}" onclick={() => (requestHistoryFilter = 'Declined')}>Declined ({requestHistory.filter(r => r.status === 'Declined').length})</button>
					</div>

					{#if filteredRequests().length === 0}
						<p class="text-base-content/40">No requests found.</p>
					{:else}
						<div class="flex flex-col gap-3">
							{#each filteredRequests() as req}
								<div class="bg-base-200/30 border border-base-300 rounded-lg p-4">
									<div class="flex items-center gap-2 mb-2">
										<span class="badge badge-primary badge-sm">{formatRequestType(req.type)}</span>
										<span class="badge badge-sm {statusBadgeClass(req.status) === 'status-pending' ? 'badge-warning' : statusBadgeClass(req.status) === 'status-accepted' ? 'badge-success' : 'badge-error'}">{req.status}</span>
									</div>
									<div class="text-sm">
										<div class="mb-1">
											<strong>{req.userFirstName} {req.userLastName}</strong>
											<span class="text-base-content/40 text-sm ml-1.5">{req.userEmail}</span>
										</div>
										{#if req.requestData}
											<div class="text-primary text-sm my-1 py-1 px-2 bg-primary/10 rounded-md inline-block">{parseRequestData(req.type, req.requestData).join(', ')}</div>
										{/if}
										{#if req.message}
											<div class="text-base-content/60 italic text-sm my-1">"{req.message}"</div>
										{/if}
										<div class="text-base-content/40 text-xs mt-1.5">
											<span>Created {formatTimeAgo(req.createdAt)}</span>
											{#if req.respondedAt}
												<span>&middot; {req.status === 'Accepted' ? 'Accepted' : 'Declined'} by {req.respondedByName ?? 'Unknown'} {formatTimeAgo(req.respondedAt)}</span>
											{/if}
											{#if req.relatedEntityId}
												<span>&middot; Entry #{req.relatedEntityId}</span>
											{/if}
										</div>
									</div>
									{#if req.status === 'Pending'}
										<div class="flex gap-1.5 mt-3 pt-3 border-t border-base-300">
											<button class="btn btn-success btn-sm" onclick={async () => {
												try {
													await requestsApi.apiV1OrganizationsSlugRequestsIdPut(orgSlug, req.id!, { accept: true });
													requestHistoryLoaded = false;
													await loadRequestHistory();
												} catch (e) {
													actionError = extractErrorMessage(e, 'Failed to accept');
												}
											}}>Accept</button>
											<button class="btn btn-outline btn-error btn-sm" onclick={async () => {
												try {
													await requestsApi.apiV1OrganizationsSlugRequestsIdPut(orgSlug, req.id!, { accept: false });
													requestHistoryLoaded = false;
													await loadRequestHistory();
												} catch (e) {
													actionError = extractErrorMessage(e, 'Failed to decline');
												}
											}}>Decline</button>
										</div>
									{/if}
								</div>
							{/each}
						</div>
					{/if}
				{/if}
			</section>
		{:else}
			<!-- ==================== Member read-only Rules view ==================== -->
			<section>
				<h2 class="text-xl text-base-content/70 mb-4">Organization Rules</h2>

				<div class="bg-base-100 border border-base-300 rounded-xl overflow-hidden">
					{#each toggleSettings as ts}
						{@const value = (org as Record<string, any>)[ts.key]}
						<div class="flex items-center justify-between p-4 border-b border-base-200 last:border-b-0">
							<div class="setting-info">
								<div class="font-semibold text-base-content mb-0.5">{ts.label}</div>
								<div class="text-sm text-base-content/60 max-w-[400px]">{ts.description}</div>
							</div>
							<span class="badge {value ? 'badge-success' : 'badge-ghost'}">{value ? 'On' : 'Off'}</span>
						</div>
					{/each}

					{#each ruleSettings as rs}
						{@const mode = (org as Record<string, any>)[rs.key] as string | null}
						{@const label = rs.labelFn ? rs.labelFn(mode) : ruleModeLabel(mode)}
						<div class="flex items-center justify-between p-4 border-b border-base-200 last:border-b-0">
							<div class="setting-info">
								<div class="font-semibold text-base-content mb-0.5">{rs.label}</div>
								<div class="text-sm text-base-content/60 max-w-[400px]">{rs.description}</div>
							</div>
							<span class="badge {ruleModeButtonClass(mode)}">{label}</span>
						</div>
					{/each}

					{#each visibilitySettings as vs}
						{@const mode = (org as Record<string, any>)[vs.key] as string | null}
						<div class="flex items-center justify-between p-4 border-b border-base-200 last:border-b-0">
							<div class="setting-info">
								<div class="font-semibold text-base-content mb-0.5">{vs.label}</div>
								<div class="text-sm text-base-content/60 max-w-[400px]">{vs.description}</div>
							</div>
							<span class="badge {visibilityModeButtonClass(mode)}">{visibilityModeLabel(mode)}</span>
						</div>
					{/each}
				</div>

				{#if org.autoPauseEnabled}
					<div class="mt-6 pt-4 border-t border-base-300">
						<h3 class="text-base font-bold text-base-content mb-3">Pause Rules</h3>
						{#if (org.pauseRules ?? []).length === 0}
							<p class="text-base-content/40">No pause rules configured.</p>
						{:else}
							<div class="bg-base-100 border border-base-300 rounded-xl overflow-hidden">
								{#each (org.pauseRules ?? []) as rule}
									<div class="p-2 bg-base-200/50 border border-base-300 rounded-lg text-sm text-base-content/70 mb-2">
										<strong>&ge; {rule.minHours}h</strong> tracked &rarr; <strong>{rule.pauseMinutes} min</strong> pause deducted
									</div>
								{/each}
							</div>
						{/if}
					</div>
				{/if}
			</section>
		{/if}
	{/if}
</div>
