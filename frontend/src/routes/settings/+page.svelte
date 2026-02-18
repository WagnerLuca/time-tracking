<script lang="ts">
	import { onMount } from 'svelte';
	import { auth } from '$lib/stores/auth.svelte';
	import { orgContext } from '$lib/stores/orgContext.svelte';
	import { goto } from '$app/navigation';
	import { organizationsApi, authApi, workScheduleApi } from '$lib/apiClient';
	import type { WorkScheduleResponse, UpdateWorkScheduleRequest } from '$lib/api';

	let changePasswordError = $state('');
	let changePasswordSuccess = $state('');
	let currentPassword = $state('');
	let newPassword = $state('');
	let saving = $state(false);

	// Work schedule state
	let weeklyHours = $state<number | null>(null);
	let distributeEvenly = $state(true);
	let targetMon = $state(0);
	let targetTue = $state(0);
	let targetWed = $state(0);
	let targetThu = $state(0);
	let targetFri = $state(0);
	let initialOvertimeHours = $state(0);
	let initialOvertimeMode = $state('Disabled');
	let scheduleLoading = $state(false);
	let scheduleSaving = $state(false);
	let scheduleError = $state('');
	let scheduleSuccess = $state('');

	// Overtime state (separate from schedule)
	let overtimeSaving = $state(false);
	let overtimeError = $state('');
	let overtimeSuccess = $state('');

	// Load work schedule when org changes
	$effect(() => {
		if (orgContext.selectedOrgSlug) {
			loadWorkSchedule(orgContext.selectedOrgSlug);
		} else {
			weeklyHours = null;
			targetMon = targetTue = targetWed = targetThu = targetFri = 0;
			initialOvertimeHours = 0;
			initialOvertimeMode = 'Disabled';
		}
	});

	async function loadWorkSchedule(orgSlug: string) {
		scheduleLoading = true;
		try {
			const { data: schedule } = await workScheduleApi.apiOrganizationsSlugWorkScheduleGet(orgSlug);
			weeklyHours = schedule.weeklyWorkHours ?? null;
			targetMon = schedule.targetMon ?? 0;
			targetTue = schedule.targetTue ?? 0;
			targetWed = schedule.targetWed ?? 0;
			targetThu = schedule.targetThu ?? 0;
			targetFri = schedule.targetFri ?? 0;
			initialOvertimeHours = schedule.initialOvertimeHours ?? 0;
			initialOvertimeMode = schedule.initialOvertimeMode ?? 'Disabled';
			// Check if all days are equal -> evenly distributed
			const allEqual = targetMon === targetTue && targetTue === targetWed && targetWed === targetThu && targetThu === targetFri;
			distributeEvenly = allEqual;
		} catch {
			// Not a member or endpoint error — just use defaults
		} finally {
			scheduleLoading = false;
		}
	}

	async function saveWorkSchedule() {
		if (!orgContext.selectedOrgSlug) return;
		scheduleSaving = true;
		scheduleError = '';
		scheduleSuccess = '';
		try {
			const payload: UpdateWorkScheduleRequest = {
				weeklyWorkHours: weeklyHours ?? undefined,
				distributeEvenly,
				targetMon: distributeEvenly ? undefined : targetMon,
				targetTue: distributeEvenly ? undefined : targetTue,
				targetWed: distributeEvenly ? undefined : targetWed,
				targetThu: distributeEvenly ? undefined : targetThu,
				targetFri: distributeEvenly ? undefined : targetFri
			};
			const { data: result } = await workScheduleApi.apiOrganizationsSlugWorkSchedulePut(orgContext.selectedOrgSlug, payload);
			// Update local state with server response
			weeklyHours = result.weeklyWorkHours ?? null;
			targetMon = result.targetMon ?? 0;
			targetTue = result.targetTue ?? 0;
			targetWed = result.targetWed ?? 0;
			targetThu = result.targetThu ?? 0;
			targetFri = result.targetFri ?? 0;
			scheduleSuccess = 'Work schedule saved.';
			setTimeout(() => (scheduleSuccess = ''), 3000);
		} catch (err: any) {
			scheduleError = err.response?.data?.message || 'Failed to save work schedule.';
		} finally {
			scheduleSaving = false;
		}
	}

	async function saveInitialOvertime() {
		if (!orgContext.selectedOrgSlug) return;
		overtimeSaving = true;
		overtimeError = '';
		overtimeSuccess = '';
		try {
			await workScheduleApi.apiOrganizationsSlugInitialOvertimePut(orgContext.selectedOrgSlug, {
				initialOvertimeHours: initialOvertimeHours
			});
			overtimeSuccess = 'Initial overtime saved.';
			setTimeout(() => (overtimeSuccess = ''), 3000);
		} catch (err: any) {
			overtimeError = err.response?.data?.message || 'Failed to save initial overtime.';
		} finally {
			overtimeSaving = false;
		}
	}

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
			await authApi.apiAuthChangePasswordPost({
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
					onclick={() => handleOrgSelect(org.organizationId ?? null)}
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

		<div class="org-manage-links">
			<a href="/organizations" class="org-manage-link">Browse Organizations</a>
			<a href="/organizations/new" class="org-manage-link">+ Create New</a>
		</div>
	</section>

	<!-- Work Schedule (when org is selected) -->
	{#if orgContext.selectedOrgId}
		<section class="card">
			<h2>Work Schedule</h2>
			<p class="card-desc">Set your weekly work hours target for <strong>{orgContext.selectedOrg?.name}</strong>. This helps track your progress.</p>

			{#if scheduleError}
				<div class="error-msg">{scheduleError}</div>
			{/if}
			{#if scheduleSuccess}
				<div class="success-msg">{scheduleSuccess}</div>
			{/if}

			{#if scheduleLoading}
				<p class="muted">Loading schedule...</p>
			{:else}
				<div class="form-group">
					<label for="weeklyHours">Weekly Work Hours</label>
					<input
						id="weeklyHours"
						type="number"
						step="0.5"
						min="0"
						max="80"
						bind:value={weeklyHours}
						placeholder="e.g. 40"
						class="input input-sm"
					/>
				</div>

				<div class="form-group">
					<label class="checkbox-label">
						<input type="checkbox" bind:checked={distributeEvenly} />
						Distribute equally (Mon–Fri)
					</label>
				</div>

				{#if !distributeEvenly}
					<div class="day-targets">
						<div class="day-target">
							<label for="tMon">Mon</label>
							<input id="tMon" type="number" step="0.5" min="0" max="24" bind:value={targetMon} class="input input-xs" />
						</div>
						<div class="day-target">
							<label for="tTue">Tue</label>
							<input id="tTue" type="number" step="0.5" min="0" max="24" bind:value={targetTue} class="input input-xs" />
						</div>
						<div class="day-target">
							<label for="tWed">Wed</label>
							<input id="tWed" type="number" step="0.5" min="0" max="24" bind:value={targetWed} class="input input-xs" />
						</div>
						<div class="day-target">
							<label for="tThu">Thu</label>
							<input id="tThu" type="number" step="0.5" min="0" max="24" bind:value={targetThu} class="input input-xs" />
						</div>
						<div class="day-target">
							<label for="tFri">Fri</label>
							<input id="tFri" type="number" step="0.5" min="0" max="24" bind:value={targetFri} class="input input-xs" />
						</div>
					</div>
				{/if}

				<button class="btn-primary" onclick={saveWorkSchedule} disabled={scheduleSaving || weeklyHours == null}>
					{scheduleSaving ? 'Saving...' : 'Save Schedule'}
				</button>
			{/if}
		</section>

		<!-- Initial Overtime Balance (separate card) -->
		{#if initialOvertimeMode !== 'Disabled'}
			<section class="card">
				<h2>Initial Overtime Balance</h2>
				<p class="card-desc">
					Set your starting overtime balance for <strong>{orgContext.selectedOrg?.name}</strong>.
					{#if initialOvertimeMode === 'RequiresApproval'}
						<em>This requires admin approval.</em>
					{/if}
				</p>

				{#if overtimeError}
					<div class="error-msg">{overtimeError}</div>
				{/if}
				{#if overtimeSuccess}
					<div class="success-msg">{overtimeSuccess}</div>
				{/if}

				<div class="form-group">
					<label for="initialOvertime">Overtime (hours)</label>
					<input
						id="initialOvertime"
						type="number"
						step="0.5"
						bind:value={initialOvertimeHours}
						placeholder="e.g. 2.5 or -1"
						class="input input-sm"
						disabled={initialOvertimeMode === 'RequiresApproval'}
					/>
					<span class="form-hint">
						{#if initialOvertimeHours > 0}
							+{initialOvertimeHours.toFixed(1)}h carried over
						{:else if initialOvertimeHours < 0}
							{initialOvertimeHours.toFixed(1)}h deficit
						{:else}
							Starting from zero
						{/if}
					</span>
				</div>

				{#if initialOvertimeMode === 'Allowed'}
					<button class="btn-primary" onclick={saveInitialOvertime} disabled={overtimeSaving}>
						{overtimeSaving ? 'Saving...' : 'Save Overtime'}
					</button>
				{:else}
					<p class="muted" style="font-size: 0.8125rem;">Submit a request through the Timer page to change this value.</p>
				{/if}
			</section>
		{/if}
	{/if}

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
		top: 50%;
		left: 50%;
		transform: translate(-50%, -50%);
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

	/* Work schedule */
	.input-sm {
		max-width: 120px;
	}

	.input-xs {
		max-width: 70px;
		text-align: center;
		padding: 0.5rem;
	}

	.checkbox-label {
		display: flex;
		align-items: center;
		gap: 0.5rem;
		font-size: 0.875rem;
		color: #374151;
		cursor: pointer;
	}

	.checkbox-label input[type="checkbox"] {
		width: 16px;
		height: 16px;
		accent-color: #3b82f6;
	}

	.day-targets {
		display: flex;
		gap: 0.75rem;
		margin-bottom: 1.25rem;
		flex-wrap: wrap;
	}

	.day-target {
		display: flex;
		flex-direction: column;
		align-items: center;
		gap: 0.25rem;
	}

	.day-target label {
		font-size: 0.75rem;
		font-weight: 600;
		color: #6b7280;
		text-transform: uppercase;
	}

	.form-hint {
		display: block;
		font-size: 0.75rem;
		color: #9ca3af;
		margin-top: 0.25rem;
	}

	.org-manage-links {
		display: flex;
		gap: 1rem;
		margin-top: 1rem;
		padding-top: 1rem;
		border-top: 1px solid #f3f4f6;
	}

	.org-manage-link {
		font-size: 0.8125rem;
		color: #3b82f6;
		text-decoration: none;
		font-weight: 500;
	}

	.org-manage-link:hover {
		text-decoration: underline;
	}
</style>
