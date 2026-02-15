<script lang="ts">
	import { auth } from '$lib/stores/auth.svelte';
	import { orgContext } from '$lib/stores/orgContext.svelte';
	import { goto } from '$app/navigation';

	let changePasswordError = $state('');
	let changePasswordSuccess = $state('');
	let currentPassword = $state('');
	let newPassword = $state('');
	let saving = $state(false);

	function handleOrgSelect(orgId: number | null) {
		orgContext.select(orgId);
	}

	async function handleChangePassword() {
		changePasswordError = '';
		changePasswordSuccess = '';
		if (!currentPassword || !newPassword) {
			changePasswordError = 'Both fields are required.';
			return;
		}
		if (newPassword.length < 6) {
			changePasswordError = 'New password must be at least 6 characters.';
			return;
		}
		saving = true;
		try {
			const { apiService } = await import('$lib/apiService');
			await apiService.post('/api/Auth/change-password', {
				currentPassword,
				newPassword
			});
			changePasswordSuccess = 'Password changed successfully.';
			currentPassword = '';
			newPassword = '';
		} catch (err: any) {
			changePasswordError = err.response?.data?.message || 'Failed to change password.';
		} finally {
			saving = false;
		}
	}

	async function handleLogout() {
		await auth.logout();
		goto('/login');
	}
</script>

<svelte:head>
	<title>Settings - Time Tracking</title>
</svelte:head>

<div class="page">
	<h1>Settings</h1>

	<!-- Profile section -->
	<section class="card">
		<h2>Profile</h2>
		<div class="profile-info">
			<div class="info-row">
				<span class="info-label">Name</span>
				<span class="info-value">{auth.user?.firstName} {auth.user?.lastName}</span>
			</div>
			<div class="info-row">
				<span class="info-label">Email</span>
				<span class="info-value">{auth.user?.email}</span>
			</div>
		</div>
	</section>

	<!-- Organization section -->
	<section class="card">
		<h2>Active Organization</h2>
		<p class="card-desc">Select which organization to track time for. This applies globally to new time entries.</p>

		<div class="org-options">
			<button
				class="org-option"
				class:selected={orgContext.selectedOrgId === null}
				onclick={() => handleOrgSelect(null)}
			>
				<span class="org-option-radio" class:checked={orgContext.selectedOrgId === null}></span>
				<span class="org-option-label">Personal (no organization)</span>
			</button>

			{#each orgContext.organizations as org}
				<button
					class="org-option"
					class:selected={orgContext.selectedOrgId === org.organizationId}
					onclick={() => handleOrgSelect(org.organizationId)}
				>
					<span class="org-option-radio" class:checked={orgContext.selectedOrgId === org.organizationId}></span>
					<div class="org-option-info">
						<span class="org-option-label">{org.name}</span>
						<span class="org-option-role">{org.role}</span>
					</div>
				</button>
			{/each}

			{#if orgContext.organizations.length === 0 && !orgContext.loading}
				<p class="muted">No organizations yet. <a href="/organizations/new">Create one</a>.</p>
			{/if}
		</div>
	</section>

	<!-- Change password -->
	<section class="card">
		<h2>Change Password</h2>

		{#if changePasswordError}
			<div class="error-msg">{changePasswordError}</div>
		{/if}
		{#if changePasswordSuccess}
			<div class="success-msg">{changePasswordSuccess}</div>
		{/if}

		<div class="form-group">
			<label for="currentPw">Current Password</label>
			<input id="currentPw" type="password" bind:value={currentPassword} class="input" />
		</div>
		<div class="form-group">
			<label for="newPw">New Password</label>
			<input id="newPw" type="password" bind:value={newPassword} class="input" />
		</div>
		<button class="btn-primary" onclick={handleChangePassword} disabled={saving}>
			{saving ? 'Saving...' : 'Change Password'}
		</button>
	</section>

	<!-- Sign out -->
	<section class="card danger-zone">
		<button class="btn-danger" onclick={handleLogout}>Sign Out</button>
	</section>
</div>

<style>
	h1 {
		margin: 0 0 1.5rem;
		font-size: 1.75rem;
		color: #1a1a2e;
	}

	.card {
		background: white;
		border: 1px solid #e5e7eb;
		border-radius: 12px;
		padding: 1.5rem;
		margin-bottom: 1.25rem;
	}

	.card h2 {
		margin: 0 0 0.75rem;
		font-size: 1.0625rem;
		color: #1a1a2e;
	}

	.card-desc {
		color: #6b7280;
		font-size: 0.875rem;
		margin: 0 0 1rem;
	}

	.profile-info {
		display: flex;
		flex-direction: column;
		gap: 0.5rem;
	}

	.info-row {
		display: flex;
		gap: 1rem;
		align-items: baseline;
	}

	.info-label {
		font-size: 0.8125rem;
		color: #9ca3af;
		min-width: 60px;
	}

	.info-value {
		font-size: 0.9375rem;
		color: #1a1a2e;
		font-weight: 500;
	}

	/* Org selection */
	.org-options {
		display: flex;
		flex-direction: column;
		gap: 0.5rem;
	}

	.org-option {
		display: flex;
		align-items: center;
		gap: 0.75rem;
		background: #fafafa;
		border: 1px solid #e5e7eb;
		border-radius: 10px;
		padding: 0.75rem 1rem;
		cursor: pointer;
		transition: border-color 0.15s, background 0.15s;
		text-align: left;
		width: 100%;
	}

	.org-option:hover {
		border-color: #d1d5db;
		background: #f3f4f6;
	}

	.org-option.selected {
		border-color: #3b82f6;
		background: #eff6ff;
	}

	.org-option-radio {
		width: 18px;
		height: 18px;
		border-radius: 50%;
		border: 2px solid #d1d5db;
		flex-shrink: 0;
		position: relative;
		transition: border-color 0.15s;
	}

	.org-option-radio.checked {
		border-color: #3b82f6;
	}

	.org-option-radio.checked::after {
		content: '';
		position: absolute;
		top: 3px;
		left: 3px;
		width: 8px;
		height: 8px;
		border-radius: 50%;
		background: #3b82f6;
	}

	.org-option-info {
		display: flex;
		gap: 0.5rem;
		align-items: center;
	}

	.org-option-label {
		font-size: 0.9375rem;
		font-weight: 500;
		color: #1a1a2e;
	}

	.org-option-role {
		font-size: 0.6875rem;
		color: #6b7280;
		text-transform: uppercase;
		font-weight: 600;
		letter-spacing: 0.03em;
	}

	.muted {
		color: #9ca3af;
		font-size: 0.875rem;
	}

	.muted a {
		color: #3b82f6;
	}

	/* Forms */
	.form-group {
		margin-bottom: 1rem;
	}

	.form-group label {
		display: block;
		font-size: 0.8125rem;
		color: #374151;
		font-weight: 500;
		margin-bottom: 0.375rem;
	}

	.input {
		width: 100%;
		max-width: 320px;
		padding: 0.625rem 0.75rem;
		border: 1px solid #d1d5db;
		border-radius: 8px;
		font-size: 0.9375rem;
	}

	.input:focus {
		outline: none;
		border-color: #3b82f6;
		box-shadow: 0 0 0 2px rgba(59, 130, 246, 0.15);
	}

	.error-msg {
		color: #dc2626;
		background: #fef2f2;
		padding: 0.625rem 0.75rem;
		border-radius: 8px;
		font-size: 0.875rem;
		margin-bottom: 1rem;
		border-left: 3px solid #dc2626;
	}

	.success-msg {
		color: #16a34a;
		background: #f0fdf4;
		padding: 0.625rem 0.75rem;
		border-radius: 8px;
		font-size: 0.875rem;
		margin-bottom: 1rem;
		border-left: 3px solid #16a34a;
	}

	.btn-primary {
		padding: 0.625rem 1.25rem;
		background: #3b82f6;
		color: white;
		border: none;
		border-radius: 8px;
		font-size: 0.875rem;
		font-weight: 600;
		cursor: pointer;
		transition: background 0.15s;
	}

	.btn-primary:hover:not(:disabled) { background: #2563eb; }
	.btn-primary:disabled { opacity: 0.6; cursor: not-allowed; }

	.danger-zone {
		border-color: #fecaca;
	}

	.btn-danger {
		padding: 0.625rem 1.25rem;
		background: #ef4444;
		color: white;
		border: none;
		border-radius: 8px;
		font-size: 0.875rem;
		font-weight: 600;
		cursor: pointer;
		transition: background 0.15s;
	}

	.btn-danger:hover { background: #dc2626; }
</style>
