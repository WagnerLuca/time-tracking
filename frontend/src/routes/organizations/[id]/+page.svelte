<script lang="ts">
	import { onMount } from 'svelte';
	import { page } from '$app/stores';
	import { goto } from '$app/navigation';
	import { auth } from '$lib/stores/auth.svelte';
	import { apiService } from '$lib/apiService';
	import type {
		OrganizationDetailResponse,
		UpdateOrganizationRequest,
		AddMemberRequest
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
</style>
