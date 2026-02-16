<script lang="ts">
	import { onMount } from 'svelte';
	import { page } from '$app/stores';
	import { goto } from '$app/navigation';
	import { auth } from '$lib/stores/auth.svelte';
	import { apiService } from '$lib/apiService';
	import type {
		OrganizationDetailResponse,
		UpdateOrganizationRequest,
		AddMemberRequest,
		UpdateOrganizationSettingsRequest,
		PauseRuleResponse,
		CreatePauseRuleRequest
	} from '$lib/types';

	let org = $state<OrganizationDetailResponse | null>(null);
	let loading = $state(true);
	let error = $state('');

	// My role in this org
	let myRole = $derived(
		org?.members.find((m) => m.id === auth.user?.id)?.role ?? null
	);
	let canEdit = $derived(myRole === 'Owner' || myRole === 'Admin');
	let isOwner = $derived(myRole === 'Owner');

	// Edit mode
	let editing = $state(false);
	let editName = $state('');
	let editDescription = $state('');
	let editSlug = $state('');
	let editWebsite = $state('');
	let editError = $state('');
	let editSaving = $state(false);

	// Add member
	let showAddMember = $state(false);
	let newMemberEmail = $state('');
	let newMemberRole = $state(0);
	let addMemberError = $state('');
	let addingMember = $state(false);

	// Action feedback
	let actionError = $state('');

	let orgId: number;

	onMount(() => {
		orgId = parseInt($page.params.id ?? '0');
		loadOrg();
	});

	async function loadOrg() {
		loading = true;
		error = '';
		try {
			org = await apiService.get<OrganizationDetailResponse>(`/api/Organizations/${orgId}`);
		} catch (err: any) {
			error = err.response?.status === 404 ? 'Organization not found.' : 'Failed to load organization.';
		} finally {
			loading = false;
		}
	}

	function startEdit() {
		if (!org) return;
		editName = org.name;
		editDescription = org.description ?? '';
		editSlug = org.slug;
		editWebsite = org.website ?? '';
		editError = '';
		editing = true;
	}

	function cancelEdit() {
		editing = false;
	}

	async function saveEdit(e: Event) {
		e.preventDefault();
		editError = '';
		editSaving = true;
		try {
			const payload: UpdateOrganizationRequest = {
				name: editName.trim(),
				description: editDescription.trim() || undefined,
				slug: editSlug.trim(),
				website: editWebsite.trim() || undefined
			};
			await apiService.put(`/api/Organizations/${orgId}`, payload);
			await loadOrg();
			editing = false;
		} catch (err: any) {
			editError = err.response?.data?.message || 'Failed to update organization.';
		} finally {
			editSaving = false;
		}
	}

	async function deleteOrg() {
		if (!confirm('Are you sure you want to delete this organization? This cannot be undone.'))
			return;
		try {
			await apiService.delete(`/api/Organizations/${orgId}`);
			goto('/organizations');
		} catch (err: any) {
			actionError = err.response?.data?.message || 'Failed to delete organization.';
		}
	}

	async function addMember(e: Event) {
		e.preventDefault();
		addMemberError = '';
		addingMember = true;
		try {
			// First search user by email via the Users endpoint
			const users = await apiService.get<any[]>('/api/Users');
			const user = users.find(
				(u: any) => u.email.toLowerCase() === newMemberEmail.toLowerCase().trim()
			);
			if (!user) {
				addMemberError = 'User not found with that email.';
				addingMember = false;
				return;
			}

			const payload: AddMemberRequest = {
				userId: user.id,
				role: newMemberRole
			};
			await apiService.post(`/api/Organizations/${orgId}/members`, payload);
			await loadOrg();
			showAddMember = false;
			newMemberEmail = '';
			newMemberRole = 0;
		} catch (err: any) {
			addMemberError = err.response?.data?.message || 'Failed to add member.';
		} finally {
			addingMember = false;
		}
	}

	async function updateMemberRole(userId: number, newRole: number) {
		actionError = '';
		try {
			await apiService.put(`/api/Organizations/${orgId}/members/${userId}`, { role: newRole });
			await loadOrg();
		} catch (err: any) {
			actionError = err.response?.data?.message || 'Failed to update member role.';
		}
	}

	async function removeMember(userId: number, memberName: string) {
		if (!confirm(`Remove ${memberName} from this organization?`)) return;
		actionError = '';
		try {
			await apiService.delete(`/api/Organizations/${orgId}/members/${userId}`);
			await loadOrg();
		} catch (err: any) {
			actionError = err.response?.data?.message || 'Failed to remove member.';
		}
	}

	function roleName(role: number): string {
		switch (role) {
			case 0: return 'Member';
			case 1: return 'Admin';
			case 2: return 'Owner';
			default: return 'Member';
		}
	}

	// Settings
	let settingsSaving = $state(false);
	let settingsError = $state('');

	async function toggleAutoPause() {
		if (!org) return;
		settingsSaving = true;
		settingsError = '';
		try {
			const payload: UpdateOrganizationSettingsRequest = {
				autoPauseEnabled: !org.autoPauseEnabled
			};
			await apiService.put(`/api/Organizations/${orgId}/settings`, payload);
			await loadOrg();
		} catch (err: any) {
			settingsError = err.response?.data?.message || 'Failed to update setting.';
		} finally {
			settingsSaving = false;
		}
	}

	async function toggleAllowEditPast() {
		if (!org) return;
		settingsSaving = true;
		settingsError = '';
		try {
			const payload: UpdateOrganizationSettingsRequest = {
				allowEditPastEntries: !org.allowEditPastEntries
			};
			await apiService.put(`/api/Organizations/${orgId}/settings`, payload);
			await loadOrg();
		} catch (err: any) {
			settingsError = err.response?.data?.message || 'Failed to update setting.';
		} finally {
			settingsSaving = false;
		}
	}

	// Pause Rules
	function getPauseRules(): import('$lib/types').PauseRuleResponse[] {
		return org?.pauseRules ? [...org.pauseRules].sort((a, b) => a.minHours - b.minHours) : [];
	}
	let showAddRule = $state(false);
	let newRuleMinHours = $state(6);
	let newRulePauseMinutes = $state(30);
	let addRuleError = $state('');
	let addingRule = $state(false);

	// Edit rule
	let editingRuleId = $state<number | null>(null);
	let editRuleMinHours = $state(0);
	let editRulePauseMinutes = $state(0);
	let editRuleError = $state('');
	let editingRuleSaving = $state(false);

	async function addPauseRule(e: Event) {
		e.preventDefault();
		addRuleError = '';
		addingRule = true;
		try {
			const payload: CreatePauseRuleRequest = {
				minHours: newRuleMinHours,
				pauseMinutes: newRulePauseMinutes
			};
			await apiService.post(`/api/Organizations/${orgId}/pause-rules`, payload);
			await loadOrg();
			showAddRule = false;
			newRuleMinHours = 6;
			newRulePauseMinutes = 30;
		} catch (err: any) {
			addRuleError = err.response?.data?.message || err.response?.data || 'Failed to add rule.';
		} finally {
			addingRule = false;
		}
	}

	function startEditRule(rule: PauseRuleResponse) {
		editingRuleId = rule.id;
		editRuleMinHours = rule.minHours;
		editRulePauseMinutes = rule.pauseMinutes;
		editRuleError = '';
	}

	function cancelEditRule() {
		editingRuleId = null;
	}

	async function saveEditRule(ruleId: number) {
		editRuleError = '';
		editingRuleSaving = true;
		try {
			await apiService.put(`/api/Organizations/${orgId}/pause-rules/${ruleId}`, {
				minHours: editRuleMinHours,
				pauseMinutes: editRulePauseMinutes
			});
			await loadOrg();
			editingRuleId = null;
		} catch (err: any) {
			editRuleError = err.response?.data?.message || err.response?.data || 'Failed to update rule.';
		} finally {
			editingRuleSaving = false;
		}
	}

	async function deleteRule(ruleId: number) {
		if (!confirm('Delete this pause rule?')) return;
		try {
			await apiService.delete(`/api/Organizations/${orgId}/pause-rules/${ruleId}`);
			await loadOrg();
		} catch (err: any) {
			settingsError = err.response?.data?.message || 'Failed to delete rule.';
		}
	}
</script>

<svelte:head>
	<title>{org ? org.name : 'Organization'} - Time Tracking</title>
</svelte:head>

<div class="page">
	<a href="/organizations" class="back-link">&larr; Back to Organizations</a>

	{#if loading}
		<p class="muted">Loading...</p>
	{:else if error}
		<div class="error-msg">{error}</div>
	{:else if org}
		{#if actionError}
			<div class="error-banner">{actionError}</div>
		{/if}

		{#if editing}
			<!-- Edit form -->
			<div class="card">
				<h2>Edit Organization</h2>
				{#if editError}
					<div class="error-banner">{editError}</div>
				{/if}
				<form onsubmit={saveEdit}>
					<div class="field">
						<label for="editName">Name</label>
						<input id="editName" type="text" bind:value={editName} required disabled={editSaving} />
					</div>
					<div class="field">
						<label for="editSlug">Slug</label>
						<input id="editSlug" type="text" bind:value={editSlug} required disabled={editSaving} />
					</div>
					<div class="field">
						<label for="editDesc">Description</label>
						<textarea id="editDesc" bind:value={editDescription} rows="3" disabled={editSaving}></textarea>
					</div>
					<div class="field">
						<label for="editWeb">Website</label>
						<input id="editWeb" type="url" bind:value={editWebsite} disabled={editSaving} />
					</div>
					<div class="form-actions">
						<button type="button" class="btn-secondary" onclick={cancelEdit}>Cancel</button>
						<button type="submit" class="btn-primary" disabled={editSaving}>
							{editSaving ? 'Saving...' : 'Save Changes'}
						</button>
					</div>
				</form>
			</div>
		{:else}
			<!-- Organization details -->
			<div class="org-header">
				<div>
					<h1>{org.name}</h1>
					<span class="slug">/{org.slug}</span>
				</div>
				{#if canEdit}
					<div class="header-actions">
						<a href="/organizations/{orgId}/time-overview" class="btn-secondary">Time Overview</a>
						<button class="btn-secondary" onclick={startEdit}>Edit</button>
						{#if isOwner}
							<button class="btn-danger" onclick={deleteOrg}>Delete</button>
						{/if}
					</div>
				{/if}
			</div>

			{#if org.description}
				<p class="description">{org.description}</p>
			{/if}

			{#if org.website}
				<a href={org.website} target="_blank" class="website-link">{org.website}</a>
			{/if}

			<!-- Members section -->
			<section class="members-section">
				<div class="section-header">
					<h2>Members ({org.members.length})</h2>
					{#if canEdit}
						<button class="btn-primary-sm" onclick={() => (showAddMember = !showAddMember)}>
							{showAddMember ? 'Cancel' : '+ Add Member'}
						</button>
					{/if}
				</div>

				{#if showAddMember}
					<div class="add-member-form">
						{#if addMemberError}
							<div class="error-banner">{addMemberError}</div>
						{/if}
						<form onsubmit={addMember} class="inline-form">
							<input
								type="email"
								bind:value={newMemberEmail}
								placeholder="user@example.com"
								required
								disabled={addingMember}
							/>
							<select bind:value={newMemberRole} disabled={addingMember}>
								<option value={0}>Member</option>
								<option value={1}>Admin</option>
								{#if isOwner}
									<option value={2}>Owner</option>
								{/if}
							</select>
							<button type="submit" class="btn-primary-sm" disabled={addingMember}>
								{addingMember ? 'Adding...' : 'Add'}
							</button>
						</form>
					</div>
				{/if}

				<div class="members-list">
					{#each org.members as member}
						<div class="member-row">
							<div class="member-info">
								<div class="member-name">
									{member.firstName} {member.lastName}
									{#if member.id === auth.user?.id}
										<span class="you-badge">You</span>
									{/if}
								</div>
								<div class="member-email">{member.email}</div>
							</div>
							<div class="member-actions">
								{#if canEdit && member.role !== 'Owner' && member.id !== auth.user?.id}
									<select
										value={member.role === 'Admin' ? 1 : 0}
										onchange={(e) => updateMemberRole(member.id, parseInt(e.currentTarget.value))}
									>
										<option value={0}>Member</option>
										<option value={1}>Admin</option>
										{#if isOwner}
											<option value={2}>Owner</option>
										{/if}
									</select>
									<button
										class="btn-icon-danger"
										title="Remove member"
										onclick={() => removeMember(member.id, `${member.firstName} ${member.lastName}`)}
									>
										&times;
									</button>
								{:else}
									<span class="role-badge role-{member.role.toLowerCase()}">{member.role}</span>
								{/if}
							</div>
						</div>
					{/each}
				</div>
			</section>

			<!-- Settings section (Admin+) -->
			{#if canEdit}
				<section class="settings-section">
					<h2>Organization Settings</h2>
					{#if settingsError}
						<div class="error-banner">{settingsError}</div>
					{/if}

					<div class="settings-grid">
						<div class="setting-row">
							<div class="setting-info">
								<div class="setting-label">Auto-Pause Tracking</div>
								<div class="setting-desc">Automatically deduct break time from tracked hours based on configurable rules.</div>
							</div>
							<button
								class="toggle-switch"
								class:active={org.autoPauseEnabled}
								onclick={toggleAutoPause}
								disabled={settingsSaving}
								aria-label="Toggle auto-pause"
							>
								<span class="toggle-knob"></span>
							</button>
						</div>

						<div class="setting-row">
							<div class="setting-info">
								<div class="setting-label">Allow Editing Past Entries</div>
								<div class="setting-desc">Let members edit start/end times of completed time entries (e.g. if they forgot to track).</div>
							</div>
							<button
								class="toggle-switch"
								class:active={org.allowEditPastEntries}
								onclick={toggleAllowEditPast}
								disabled={settingsSaving}
								aria-label="Toggle edit past entries"
							>
								<span class="toggle-knob"></span>
							</button>
						</div>
					</div>

					<!-- Pause Rules (only when auto-pause is enabled) -->
					{#if org.autoPauseEnabled}
						<div class="pause-rules-section">
							<div class="section-header">
								<h3>Pause Rules</h3>
								<button class="btn-primary-sm" onclick={() => (showAddRule = !showAddRule)}>
									{showAddRule ? 'Cancel' : '+ Add Rule'}
								</button>
							</div>

							{#if showAddRule}
								<div class="add-rule-form">
									{#if addRuleError}
										<div class="error-banner">{addRuleError}</div>
									{/if}
									<form onsubmit={addPauseRule} class="inline-form">
										<div class="rule-field">
											<!-- svelte-ignore a11y_label_has_associated_control -->
											<label>When tracked &ge;</label>
											<input type="number" step="0.5" min="0.5" bind:value={newRuleMinHours} required disabled={addingRule} />
											<span>hours</span>
										</div>
										<div class="rule-field">
											<!-- svelte-ignore a11y_label_has_associated_control -->
											<label>deduct</label>
											<input type="number" min="1" bind:value={newRulePauseMinutes} required disabled={addingRule} />
											<span>min pause</span>
										</div>
										<button type="submit" class="btn-primary-sm" disabled={addingRule}>
											{addingRule ? 'Adding...' : 'Add Rule'}
										</button>
									</form>
								</div>
							{/if}

							{#if getPauseRules().length === 0}
								<p class="muted">No pause rules configured. Add rules to automatically deduct break time.</p>
							{:else}
								<div class="rules-list">
									{#each getPauseRules() as rule}
										<div class="rule-row">
											{#if editingRuleId === rule.id}
												{#if editRuleError}
													<div class="error-banner" style="width:100%">{editRuleError}</div>
												{/if}
												<div class="rule-edit-form">
													<div class="rule-field">
														<!-- svelte-ignore a11y_label_has_associated_control -->
														<label>&ge;</label>
														<input type="number" step="0.5" min="0.5" bind:value={editRuleMinHours} disabled={editingRuleSaving} />
														<span>h</span>
													</div>
													<div class="rule-field">
														<!-- svelte-ignore a11y_label_has_associated_control -->
														<label>&rarr;</label>
														<input type="number" min="1" bind:value={editRulePauseMinutes} disabled={editingRuleSaving} />
														<span>min</span>
													</div>
													<div class="rule-actions">
														<button class="btn-primary-sm" onclick={() => saveEditRule(rule.id)} disabled={editingRuleSaving}>Save</button>
														<button class="btn-secondary-sm" onclick={cancelEditRule}>Cancel</button>
													</div>
												</div>
											{:else}
												<div class="rule-text">
													<strong>&ge; {rule.minHours}h</strong> tracked &rarr; <strong>{rule.pauseMinutes} min</strong> pause deducted
												</div>
												<div class="rule-actions">
													<button class="btn-secondary-sm" onclick={() => startEditRule(rule)}>Edit</button>
													<button class="btn-icon-danger" title="Delete rule" onclick={() => deleteRule(rule.id)}>&times;</button>
												</div>
											{/if}
										</div>
									{/each}
								</div>
							{/if}
						</div>
					{/if}
				</section>
			{/if}
		{/if}
	{/if}
</div>

<style>
	.back-link {
		color: #6b7280;
		text-decoration: none;
		font-size: 0.875rem;
		display: inline-block;
		margin-bottom: 0.75rem;
	}

	.back-link:hover {
		color: #3b82f6;
	}

	.muted {
		color: #9ca3af;
	}

	.error-msg {
		color: #dc2626;
		background: #fef2f2;
		padding: 0.75rem 1rem;
		border-radius: 8px;
		border-left: 3px solid #dc2626;
	}

	.error-banner {
		background: #fef2f2;
		color: #dc2626;
		padding: 0.75rem 1rem;
		border-radius: 8px;
		margin-bottom: 1rem;
		font-size: 0.875rem;
		border-left: 3px solid #dc2626;
	}

	/* Organization header */
	.org-header {
		display: flex;
		align-items: flex-start;
		justify-content: space-between;
		margin-bottom: 0.5rem;
	}

	.org-header h1 {
		margin: 0;
		font-size: 1.75rem;
		color: #1a1a2e;
	}

	.slug {
		color: #9ca3af;
		font-size: 0.875rem;
	}

	.header-actions {
		display: flex;
		gap: 0.5rem;
	}

	.description {
		color: #4b5563;
		margin: 0.75rem 0;
	}

	.website-link {
		color: #3b82f6;
		font-size: 0.875rem;
		display: inline-block;
		margin-bottom: 1.5rem;
	}

	/* Members */
	.members-section {
		margin-top: 2rem;
	}

	.section-header {
		display: flex;
		align-items: center;
		justify-content: space-between;
		margin-bottom: 1rem;
	}

	.section-header h2 {
		margin: 0;
		font-size: 1.25rem;
		color: #374151;
	}

	.add-member-form {
		background: #f9fafb;
		padding: 1rem;
		border-radius: 8px;
		margin-bottom: 1rem;
		border: 1px solid #e5e7eb;
	}

	.inline-form {
		display: flex;
		gap: 0.5rem;
		align-items: center;
		flex-wrap: wrap;
	}

	.inline-form input {
		flex: 1;
		min-width: 200px;
		padding: 0.5rem 0.75rem;
		border: 1px solid #d1d5db;
		border-radius: 6px;
		font-size: 0.875rem;
	}

	.inline-form select {
		padding: 0.5rem;
		border: 1px solid #d1d5db;
		border-radius: 6px;
		font-size: 0.875rem;
	}

	.members-list {
		background: white;
		border: 1px solid #e5e7eb;
		border-radius: 12px;
		overflow: hidden;
	}

	.member-row {
		display: flex;
		align-items: center;
		justify-content: space-between;
		padding: 0.75rem 1rem;
		border-bottom: 1px solid #f3f4f6;
	}

	.member-row:last-child {
		border-bottom: none;
	}

	.member-name {
		font-weight: 500;
		color: #1a1a2e;
		display: flex;
		align-items: center;
		gap: 0.5rem;
	}

	.member-email {
		font-size: 0.8125rem;
		color: #9ca3af;
	}

	.member-actions {
		display: flex;
		align-items: center;
		gap: 0.5rem;
	}

	.member-actions select {
		padding: 0.375rem 0.5rem;
		border: 1px solid #d1d5db;
		border-radius: 6px;
		font-size: 0.8125rem;
	}

	.you-badge {
		font-size: 0.6875rem;
		background: #dbeafe;
		color: #1e40af;
		padding: 0.0625rem 0.375rem;
		border-radius: 999px;
		font-weight: 600;
	}

	.role-badge {
		font-size: 0.75rem;
		font-weight: 600;
		padding: 0.125rem 0.5rem;
		border-radius: 999px;
		text-transform: uppercase;
		letter-spacing: 0.025em;
	}

	.role-owner {
		background: #fef3c7;
		color: #92400e;
	}

	.role-admin {
		background: #dbeafe;
		color: #1e40af;
	}

	.role-member {
		background: #f3f4f6;
		color: #4b5563;
	}

	.btn-icon-danger {
		background: none;
		border: none;
		color: #dc2626;
		font-size: 1.25rem;
		cursor: pointer;
		padding: 0.25rem 0.5rem;
		border-radius: 4px;
		line-height: 1;
	}

	.btn-icon-danger:hover {
		background: #fef2f2;
	}

	/* Buttons */
	.btn-primary {
		background: #3b82f6;
		color: white;
		padding: 0.5rem 1rem;
		border-radius: 8px;
		font-size: 0.875rem;
		font-weight: 600;
		border: none;
		cursor: pointer;
		transition: background 0.15s;
	}

	.btn-primary:hover:not(:disabled) {
		background: #2563eb;
	}

	.btn-primary:disabled {
		opacity: 0.6;
		cursor: not-allowed;
	}

	.btn-primary-sm {
		background: #3b82f6;
		color: white;
		padding: 0.375rem 0.75rem;
		border-radius: 6px;
		font-size: 0.8125rem;
		font-weight: 600;
		border: none;
		cursor: pointer;
		transition: background 0.15s;
	}

	.btn-primary-sm:hover:not(:disabled) {
		background: #2563eb;
	}

	.btn-primary-sm:disabled {
		opacity: 0.6;
		cursor: not-allowed;
	}

	.btn-secondary {
		background: white;
		color: #4b5563;
		padding: 0.5rem 1rem;
		border-radius: 8px;
		font-size: 0.875rem;
		font-weight: 500;
		border: 1px solid #d1d5db;
		cursor: pointer;
		transition: background 0.15s;
	}

	.btn-secondary:hover {
		background: #f9fafb;
	}

	.btn-danger {
		background: white;
		color: #dc2626;
		padding: 0.5rem 1rem;
		border-radius: 8px;
		font-size: 0.875rem;
		font-weight: 500;
		border: 1px solid #fecaca;
		cursor: pointer;
		transition: background 0.15s;
	}

	.btn-danger:hover {
		background: #fef2f2;
	}

	/* Card / form */
	.card {
		background: white;
		border-radius: 12px;
		padding: 2rem;
		border: 1px solid #e5e7eb;
		max-width: 560px;
	}

	.card h2 {
		margin: 0 0 1.25rem;
		color: #1a1a2e;
	}

	.field {
		margin-bottom: 1.25rem;
	}

	.field label {
		display: block;
		font-weight: 500;
		margin-bottom: 0.375rem;
		color: #374151;
		font-size: 0.875rem;
	}

	.field input,
	.field textarea {
		width: 100%;
		padding: 0.625rem 0.75rem;
		border: 1px solid #d1d5db;
		border-radius: 8px;
		font-size: 0.9375rem;
		box-sizing: border-box;
		font-family: inherit;
	}

	.field input:focus,
	.field textarea:focus {
		outline: none;
		border-color: #3b82f6;
		box-shadow: 0 0 0 3px rgba(59, 130, 246, 0.1);
	}

	.form-actions {
		display: flex;
		gap: 0.75rem;
		justify-content: flex-end;
		margin-top: 1.5rem;
	}

	/* Settings section */
	.settings-section {
		margin-top: 2.5rem;
	}

	.settings-section h2 {
		font-size: 1.25rem;
		color: #374151;
		margin: 0 0 1rem;
	}

	.settings-grid {
		background: white;
		border: 1px solid #e5e7eb;
		border-radius: 12px;
		overflow: hidden;
	}

	.setting-row {
		display: flex;
		align-items: center;
		justify-content: space-between;
		padding: 1rem 1.25rem;
		border-bottom: 1px solid #f3f4f6;
	}

	.setting-row:last-child {
		border-bottom: none;
	}

	.setting-label {
		font-weight: 600;
		color: #1a1a2e;
		margin-bottom: 0.125rem;
	}

	.setting-desc {
		font-size: 0.8125rem;
		color: #6b7280;
		max-width: 400px;
	}

	/* Toggle switch */
	.toggle-switch {
		position: relative;
		width: 48px;
		height: 26px;
		background: #d1d5db;
		border: none;
		border-radius: 999px;
		cursor: pointer;
		transition: background 0.2s;
		flex-shrink: 0;
		padding: 0;
	}

	.toggle-switch.active {
		background: #3b82f6;
	}

	.toggle-switch:disabled {
		opacity: 0.5;
		cursor: not-allowed;
	}

	.toggle-knob {
		position: absolute;
		top: 3px;
		left: 3px;
		width: 20px;
		height: 20px;
		background: white;
		border-radius: 50%;
		transition: transform 0.2s;
		box-shadow: 0 1px 3px rgba(0, 0, 0, 0.15);
	}

	.toggle-switch.active .toggle-knob {
		transform: translateX(22px);
	}

	/* Pause rules */
	.pause-rules-section {
		margin-top: 1.5rem;
	}

	.pause-rules-section h3 {
		margin: 0;
		font-size: 1.05rem;
		color: #374151;
	}

	.add-rule-form {
		background: #f9fafb;
		padding: 1rem;
		border-radius: 8px;
		margin-bottom: 1rem;
		border: 1px solid #e5e7eb;
	}

	.rule-field {
		display: flex;
		align-items: center;
		gap: 0.375rem;
	}

	.rule-field label {
		font-size: 0.875rem;
		color: #4b5563;
		font-weight: 500;
		white-space: nowrap;
	}

	.rule-field input {
		width: 70px;
		padding: 0.375rem 0.5rem;
		border: 1px solid #d1d5db;
		border-radius: 6px;
		font-size: 0.875rem;
		text-align: center;
	}

	.rule-field span {
		font-size: 0.8125rem;
		color: #6b7280;
		white-space: nowrap;
	}

	.rules-list {
		background: white;
		border: 1px solid #e5e7eb;
		border-radius: 12px;
		overflow: hidden;
	}

	.rule-row {
		display: flex;
		align-items: center;
		justify-content: space-between;
		padding: 0.75rem 1rem;
		border-bottom: 1px solid #f3f4f6;
		flex-wrap: wrap;
		gap: 0.5rem;
	}

	.rule-row:last-child {
		border-bottom: none;
	}

	.rule-text {
		font-size: 0.9375rem;
		color: #374151;
	}

	.rule-edit-form {
		display: flex;
		align-items: center;
		gap: 0.75rem;
		flex-wrap: wrap;
		width: 100%;
	}

	.rule-actions {
		display: flex;
		align-items: center;
		gap: 0.375rem;
	}

	.btn-secondary-sm {
		background: white;
		color: #4b5563;
		padding: 0.375rem 0.75rem;
		border-radius: 6px;
		font-size: 0.8125rem;
		font-weight: 500;
		border: 1px solid #d1d5db;
		cursor: pointer;
		transition: background 0.15s;
	}

	.btn-secondary-sm:hover {
		background: #f9fafb;
	}
</style>
