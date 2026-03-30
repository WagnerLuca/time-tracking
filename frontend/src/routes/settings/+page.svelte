<script lang="ts">
	import { auth } from '$lib/stores/auth.svelte';
	import { orgContext } from '$lib/stores/orgContext.svelte';
	import { goto } from '$app/navigation';
	import { authApi, workScheduleApi } from '$lib/apiClient';
	import type { WorkScheduleResponse, UpdateProfileRequest, TwoFactorSetupResponse } from '$lib/api';
	import { extractErrorMessage } from '$lib/utils/errorHandler';
	import { theme } from '$lib/stores/theme.svelte';

	let changePasswordError = $state('');
	let changePasswordSuccess = $state('');
	let currentPassword = $state('');
	let newPassword = $state('');
	let saving = $state(false);
	let activeTab = $state<'profile' | 'organization' | 'appearance'>('profile');

	// Profile/account state
	let profileFirstName = $state('');
	let profileLastName = $state('');
	let profileEmail = $state('');
	let profileUserId = $state<number | null>(null);
	let profileSaving = $state(false);
	let profileError = $state('');
	let profileSuccess = $state('');
	let deleteAccountSaving = $state(false);
	let deleteAccountError = $state('');

	// Two-Factor Authentication state
	let twoFactorStep = $state<'idle' | 'setup' | 'confirm' | 'backup'>('idle');
	let twoFactorSetup = $state<TwoFactorSetupResponse | null>(null);
	let twoFactorConfirmCode = $state('');
	let twoFactorBackupCodes = $state<string[]>([]);
	let twoFactorError = $state('');
	let twoFactorSuccess = $state('');
	let twoFactorSaving = $state(false);
	let twoFactorDisablePassword = $state('');

	// Schedule periods state
	let currentSchedule = $state<WorkScheduleResponse | null>(null);
	let schedulePeriods = $state<WorkScheduleResponse[]>([]);
	let showAddSchedulePeriod = $state(false);
	let periodFrom = $state('');
	let periodTo = $state('');
	let periodWeeklyHours = $state<number | null>(null);
	let periodDistributeEvenly = $state(true);
	let periodMon = $state(0);
	let periodTue = $state(0);
	let periodWed = $state(0);
	let periodThu = $state(0);
	let periodFri = $state(0);
	let initialOvertimeHours = $state(0);
	let initialOvertimeMode = $state('Disabled');
	let scheduleLoading = $state(false);
	let periodSaving = $state(false);
	let periodError = $state('');
	let periodSuccess = $state('');

	// Overtime state (separate from schedule)
	let overtimeSaving = $state(false);
	let overtimeError = $state('');
	let overtimeSuccess = $state('');

	// Load work schedule when org changes
	$effect(() => {
		if (orgContext.selectedOrgSlug) {
			loadSchedulePeriodsForOrg(orgContext.selectedOrgSlug);
		} else {
			currentSchedule = null;
			schedulePeriods = [];
			showAddSchedulePeriod = false;
			periodFrom = '';
			periodTo = '';
			periodWeeklyHours = null;
			periodDistributeEvenly = true;
			periodMon = periodTue = periodWed = periodThu = periodFri = 0;
			initialOvertimeHours = 0;
			initialOvertimeMode = 'Disabled';
		}
	});

	$effect(() => {
		if (!auth.user) {
			profileUserId = null;
			profileFirstName = '';
			profileLastName = '';
			profileEmail = '';
			return;
		}

		if (profileUserId !== auth.user.id) {
			profileUserId = auth.user.id;
			profileFirstName = auth.user.firstName ?? '';
			profileLastName = auth.user.lastName ?? '';
			profileEmail = auth.user.email ?? '';
		}
	});

	function clearSchedulePeriodForm() {
		periodFrom = '';
		periodTo = '';
		periodWeeklyHours = null;
		periodDistributeEvenly = true;
		periodMon = periodTue = periodWed = periodThu = periodFri = 0;
	}

	function normalizeDateOnly(value: string): string {
		return value?.split('T')[0] ?? '';
	}

	async function loadSchedulePeriodsForOrg(orgSlug: string) {
		scheduleLoading = true;
		periodError = '';
		try {
			const { data: schedule } = await workScheduleApi.apiV1OrganizationsSlugWorkScheduleGet(orgSlug);
			currentSchedule = schedule;
			initialOvertimeHours = schedule.initialOvertimeHours ?? 0;
			initialOvertimeMode = schedule.initialOvertimeMode ?? 'Disabled';
		} catch {
			currentSchedule = null;
			initialOvertimeHours = 0;
			initialOvertimeMode = 'Disabled';
		}

		try {
			const { data } = await workScheduleApi.apiV1OrganizationsSlugWorkSchedulesGet(orgSlug);
			schedulePeriods = (data as WorkScheduleResponse[]).sort((a, b) =>
				(b.validFrom ?? '').localeCompare(a.validFrom ?? '')
			);
		} catch {
			schedulePeriods = [];
		} finally {
			scheduleLoading = false;
		}
	}

	async function saveSchedulePeriod() {
		if (!orgContext.selectedOrgSlug) return;
		if (!periodFrom) {
			periodError = 'A valid start date is required.';
			return;
		}

		const validFrom = normalizeDateOnly(periodFrom);
		const validTo = normalizeDateOnly(periodTo);
		if (validTo && validTo < validFrom) {
			periodError = 'Valid to must be on or after valid from.';
			return;
		}

		periodSaving = true;
		periodError = '';
		periodSuccess = '';
		try {
			const payload = {
				validFrom,
				validTo: validTo || undefined,
				weeklyWorkHours: periodWeeklyHours ?? undefined,
				distributeEvenly: periodDistributeEvenly,
				targetMon: periodDistributeEvenly ? undefined : periodMon,
				targetTue: periodDistributeEvenly ? undefined : periodTue,
				targetWed: periodDistributeEvenly ? undefined : periodWed,
				targetThu: periodDistributeEvenly ? undefined : periodThu,
				targetFri: periodDistributeEvenly ? undefined : periodFri
			};

			const existingPeriod = schedulePeriods.find((p) => p.validFrom === validFrom);
			if (existingPeriod?.id) {
				await workScheduleApi.apiV1OrganizationsSlugWorkSchedulesIdPut(
					orgContext.selectedOrgSlug,
					existingPeriod.id,
					payload
				);
			} else {
				await workScheduleApi.apiV1OrganizationsSlugWorkSchedulesPost(orgContext.selectedOrgSlug, payload);
			}

			await loadSchedulePeriodsForOrg(orgContext.selectedOrgSlug);
			showAddSchedulePeriod = false;
			clearSchedulePeriodForm();
			periodSuccess = existingPeriod ? 'Schedule period updated.' : 'Schedule period added.';
			setTimeout(() => (periodSuccess = ''), 3000);
		} catch (err) {
			periodError = extractErrorMessage(err, 'Failed to save schedule period.');
		} finally {
			periodSaving = false;
		}
	}

	async function deleteSchedulePeriod(periodId: number) {
		if (!orgContext.selectedOrgSlug) return;
		try {
			await workScheduleApi.apiV1OrganizationsSlugWorkSchedulesIdDelete(orgContext.selectedOrgSlug, periodId);
			periodSuccess = 'Schedule period deleted.';
			setTimeout(() => (periodSuccess = ''), 3000);
			await loadSchedulePeriodsForOrg(orgContext.selectedOrgSlug);
		} catch (err) {
			periodError = extractErrorMessage(err, 'Failed to delete schedule period.');
		}
	}

	async function saveInitialOvertime() {
		if (!orgContext.selectedOrgSlug) return;
		overtimeSaving = true;
		overtimeError = '';
		overtimeSuccess = '';
		try {
			await workScheduleApi.apiV1OrganizationsSlugInitialOvertimePut(orgContext.selectedOrgSlug, {
				initialOvertimeHours: initialOvertimeHours
			});
			overtimeSuccess = 'Initial overtime saved.';
			setTimeout(() => (overtimeSuccess = ''), 3000);
		} catch (err) {
			overtimeError = extractErrorMessage(err, 'Failed to save initial overtime.');
		} finally {
			overtimeSaving = false;
		}
	}

	function handleOrgSelect(orgId: number | null) {
		orgContext.select(orgId);
	}

	function formatThemeName(themeName: string): string {
		return themeName
			.split(/[-_]/g)
			.map((part) => part.charAt(0).toUpperCase() + part.slice(1))
			.join(' ');
	}

	async function handleSaveProfile() {
		profileError = '';
		profileSuccess = '';

		const trimmedFirstName = profileFirstName.trim();
		const trimmedLastName = profileLastName.trim();
		const trimmedEmail = profileEmail.trim();

		if (!trimmedFirstName || !trimmedLastName) {
			profileError = 'First and last name are required.';
			return;
		}

		if (!trimmedEmail) {
			profileError = 'Email is required.';
			return;
		}

		profileSaving = true;
		try {
			const payload: UpdateProfileRequest = {
				firstName: trimmedFirstName,
				lastName: trimmedLastName,
				email: trimmedEmail
			};
			const { data: updatedUser } = await authApi.apiV1AuthProfilePut(payload);

			profileFirstName = updatedUser.firstName ?? '';
			profileLastName = updatedUser.lastName ?? '';
			profileEmail = updatedUser.email ?? '';
			await auth.fetchCurrentUser();

			profileSuccess = 'Profile updated.';
			setTimeout(() => (profileSuccess = ''), 3000);
		} catch (err) {
			profileError = extractErrorMessage(err, 'Failed to update profile.');
		} finally {
			profileSaving = false;
		}
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
			await authApi.apiV1AuthChangePasswordPost({
				currentPassword,
				newPassword
			});
			changePasswordSuccess = 'Password changed successfully.';
			currentPassword = '';
			newPassword = '';
		} catch (err) {
			changePasswordError = extractErrorMessage(err, 'Failed to change password.');
		} finally {
			saving = false;
		}
	}

	async function handleSetup2FA() {
		twoFactorError = '';
		twoFactorSaving = true;
		try {
			const { data } = await authApi.apiV1Auth2faSetupPost();
			twoFactorSetup = data;
			twoFactorStep = 'setup';
		} catch (err) {
			twoFactorError = extractErrorMessage(err, 'Failed to start 2FA setup.');
		} finally {
			twoFactorSaving = false;
		}
	}

	async function handleConfirm2FA() {
		if (!twoFactorConfirmCode || twoFactorConfirmCode.length !== 6) {
			twoFactorError = 'Please enter a 6-digit code.';
			return;
		}
		twoFactorError = '';
		twoFactorSaving = true;
		try {
			const { data } = await authApi.apiV1Auth2faConfirmPost({ code: twoFactorConfirmCode });
			twoFactorBackupCodes = data.backupCodes ?? [];
			twoFactorStep = 'backup';
			twoFactorConfirmCode = '';
			await auth.fetchCurrentUser();
		} catch (err) {
			twoFactorError = extractErrorMessage(err, 'Invalid code. Please try again.');
		} finally {
			twoFactorSaving = false;
		}
	}

	function handleFinish2FASetup() {
		twoFactorStep = 'idle';
		twoFactorSetup = null;
		twoFactorBackupCodes = [];
		twoFactorSuccess = 'Two-factor authentication is now enabled.';
		setTimeout(() => (twoFactorSuccess = ''), 5000);
	}

	async function handleDisable2FA() {
		if (!twoFactorDisablePassword) {
			twoFactorError = 'Password is required to disable 2FA.';
			return;
		}
		twoFactorError = '';
		twoFactorSaving = true;
		try {
			await authApi.apiV1Auth2faDisablePost({ currentPassword: twoFactorDisablePassword, newPassword: twoFactorDisablePassword });
			twoFactorDisablePassword = '';
			twoFactorSuccess = 'Two-factor authentication has been disabled.';
			setTimeout(() => (twoFactorSuccess = ''), 5000);
			await auth.fetchCurrentUser();
		} catch (err) {
			twoFactorError = extractErrorMessage(err, 'Failed to disable 2FA.');
		} finally {
			twoFactorSaving = false;
		}
	}

	function cancelSetup2FA() {
		twoFactorStep = 'idle';
		twoFactorSetup = null;
		twoFactorConfirmCode = '';
		twoFactorBackupCodes = [];
		twoFactorError = '';
	}

	async function handleLogout() {
		await auth.logout();
		goto('/login');
	}

	async function handleDeleteAccount() {
		deleteAccountError = '';
		const confirmation = window.prompt('Type DELETE to permanently remove your account.');
		if (confirmation !== 'DELETE') {
			return;
		}

		deleteAccountSaving = true;
		try {
			await authApi.apiV1AuthAccountDelete();
			await auth.logout();
			goto('/register');
		} catch (err) {
			deleteAccountError = extractErrorMessage(err, 'Failed to delete account.');
		} finally {
			deleteAccountSaving = false;
		}
	}
</script>

<svelte:head>
	<title>Settings - Time Tracking</title>
</svelte:head>

<div class="max-w-3xl mx-auto">
	<h1 class="text-2xl font-bold mb-4">Settings</h1>

	<div class="tabs tabs-boxed p-1 bg-base-200/70 mb-6 w-full md:w-fit" role="tablist" aria-label="Settings sections">
		<button
			type="button"
			role="tab"
			class={"tab " + (activeTab === 'profile' ? 'tab-active' : '')}
			onclick={() => (activeTab = 'profile')}
			aria-selected={activeTab === 'profile'}
		>
			Profile
		</button>
		<button
			type="button"
			role="tab"
			class={"tab " + (activeTab === 'organization' ? 'tab-active' : '')}
			onclick={() => (activeTab = 'organization')}
			aria-selected={activeTab === 'organization'}
		>
			Org Settings
		</button>
		<button
			type="button"
			role="tab"
			class={"tab " + (activeTab === 'appearance' ? 'tab-active' : '')}
			onclick={() => (activeTab = 'appearance')}
			aria-selected={activeTab === 'appearance'}
		>
			Appearance
		</button>
	</div>

	{#if activeTab === 'profile'}
		<section class="card bg-base-100 border border-base-300 shadow-sm mb-5">
			<div class="card-body">
				<h2 class="card-title text-base">Profile</h2>

				{#if profileError}
					<div class="alert alert-error text-sm mb-4">{profileError}</div>
				{/if}
				{#if profileSuccess}
					<div class="alert alert-success text-sm mb-4">{profileSuccess}</div>
				{/if}

				<div class="grid grid-cols-1 md:grid-cols-2 gap-3 mb-4">
					<div>
						<label for="profileFirstName" class="block text-sm font-medium text-base-content/80 mb-1.5">First Name</label>
						<input id="profileFirstName" type="text" bind:value={profileFirstName} class="input input-bordered w-full" />
					</div>
					<div>
						<label for="profileLastName" class="block text-sm font-medium text-base-content/80 mb-1.5">Last Name</label>
						<input id="profileLastName" type="text" bind:value={profileLastName} class="input input-bordered w-full" />
					</div>
				</div>

				<div class="mb-4">
					<label for="profileEmail" class="block text-sm font-medium text-base-content/80 mb-1.5">Email</label>
					<input id="profileEmail" type="email" bind:value={profileEmail} class="input input-bordered w-full max-w-md" />
				</div>

				<button class="btn btn-primary btn-sm" onclick={handleSaveProfile} disabled={profileSaving}>
					{profileSaving ? 'Saving...' : 'Save Profile'}
				</button>
			</div>
		</section>

		<section class="card bg-base-100 border border-base-300 shadow-sm mb-5">
			<div class="card-body">
				<h2 class="card-title text-base">Change Password</h2>

				{#if changePasswordError}
					<div class="alert alert-error text-sm mb-4">{changePasswordError}</div>
				{/if}
				{#if changePasswordSuccess}
					<div class="alert alert-success text-sm mb-4">{changePasswordSuccess}</div>
				{/if}

				<div class="mb-4">
					<label for="currentPw" class="block text-sm font-medium text-base-content/80 mb-1.5">Current Password</label>
					<input id="currentPw" type="password" bind:value={currentPassword} class="input input-bordered w-full max-w-xs" />
				</div>
				<div class="mb-4">
					<label for="newPw" class="block text-sm font-medium text-base-content/80 mb-1.5">New Password</label>
					<input id="newPw" type="password" bind:value={newPassword} class="input input-bordered w-full max-w-xs" />
				</div>
				<button class="btn btn-primary btn-sm" onclick={handleChangePassword} disabled={saving}>
					{saving ? 'Saving...' : 'Change Password'}
				</button>
			</div>
		</section>

		<section class="card bg-base-100 border border-base-300 shadow-sm mb-5">
			<div class="card-body">
				<h2 class="card-title text-base">Two-Factor Authentication</h2>

				{#if twoFactorError}
					<div class="alert alert-error text-sm mb-4">{twoFactorError}</div>
				{/if}
				{#if twoFactorSuccess}
					<div class="alert alert-success text-sm mb-4">{twoFactorSuccess}</div>
				{/if}

				{#if twoFactorStep === 'idle'}
					{#if auth.user?.twoFactorEnabled}
						<div class="flex items-center gap-2 mb-4">
							<span class="badge badge-success badge-sm">Enabled</span>
							<span class="text-sm text-base-content/60">Two-factor authentication is active on your account.</span>
						</div>
						<p class="text-sm text-base-content/60 mb-3">To disable 2FA, enter your password below.</p>
						<div class="flex gap-2 items-end">
							<div>
								<label for="disable2faPassword" class="block text-sm font-medium text-base-content/80 mb-1.5">Password</label>
								<input
									id="disable2faPassword"
									type="password"
									bind:value={twoFactorDisablePassword}
									class="input input-bordered input-sm w-full max-w-xs"
									placeholder="Your current password"
								/>
							</div>
							<button class="btn btn-error btn-sm" onclick={handleDisable2FA} disabled={twoFactorSaving || !twoFactorDisablePassword}>
								{twoFactorSaving ? 'Disabling...' : 'Disable 2FA'}
							</button>
						</div>
					{:else}
						<p class="text-sm text-base-content/60 mb-3">Add an extra layer of security to your account using an authenticator app (e.g. Google Authenticator, Authy).</p>
						<button class="btn btn-primary btn-sm" onclick={handleSetup2FA} disabled={twoFactorSaving}>
							{twoFactorSaving ? 'Setting up...' : 'Set Up 2FA'}
						</button>
					{/if}
				{:else if twoFactorStep === 'setup'}
					<p class="text-sm text-base-content/60 mb-4">Scan this QR code with your authenticator app or enter the key manually.</p>

					{#if twoFactorSetup}
						<div class="flex flex-col items-center gap-4 mb-4 p-4 bg-base-200/50 rounded-lg border border-base-300">
							<div class="bg-white p-3 rounded-lg">
								<img
										src="https://api.qrserver.com/v1/create-qr-code/?size=200x200&data={encodeURIComponent(twoFactorSetup.authenticatorUri ?? '')}"
									alt="2FA QR Code"
									width="200"
									height="200"
									class="block"
								/>
							</div>
							<div class="text-center">
								<p class="text-xs text-base-content/50 mb-1">Or enter this key manually:</p>
								<code class="text-sm font-mono bg-base-300 px-3 py-1.5 rounded select-all">{twoFactorSetup.sharedKey}</code>
							</div>
						</div>

						<p class="text-sm text-base-content/60 mb-3">Enter the 6-digit code from your authenticator app to confirm:</p>
						<div class="flex gap-2 items-end">
							<div>
								<label for="confirm2faCode" class="block text-sm font-medium text-base-content/80 mb-1.5">Verification Code</label>
								<input
									id="confirm2faCode"
									type="text"
									inputmode="numeric"
									autocomplete="one-time-code"
									bind:value={twoFactorConfirmCode}
									class="input input-bordered input-sm w-[160px] text-center tracking-[0.2em] font-mono"
									placeholder="000000"
									maxlength="6"
								/>
							</div>
							<button class="btn btn-primary btn-sm" onclick={handleConfirm2FA} disabled={twoFactorSaving || twoFactorConfirmCode.length !== 6}>
								{twoFactorSaving ? 'Verifying...' : 'Verify & Enable'}
							</button>
						</div>
					{/if}

					<button type="button" class="btn btn-ghost btn-sm mt-3" onclick={cancelSetup2FA}>Cancel</button>
				{:else if twoFactorStep === 'backup'}
					<div class="alert alert-warning text-sm mb-4">
						<span>Save these backup codes in a safe place. Each code can only be used once. You will not see them again.</span>
					</div>

					<div class="grid grid-cols-2 gap-2 mb-4 p-4 bg-base-200/50 rounded-lg border border-base-300 max-w-xs">
						{#each twoFactorBackupCodes as code}
							<code class="text-sm font-mono text-center py-1">{code}</code>
						{/each}
					</div>

					<button class="btn btn-primary btn-sm" onclick={handleFinish2FASetup}>
						I've saved my backup codes
					</button>
				{/if}
			</div>
		</section>

		<section class="card bg-base-100 border border-error/30 shadow-sm mb-5">
			<div class="card-body gap-4">
				<h2 class="card-title text-base">Account Actions</h2>
				<p class="text-sm text-base-content/60 m-0">Sign out of this device or permanently delete your account.</p>

				{#if deleteAccountError}
					<div class="alert alert-error text-sm">{deleteAccountError}</div>
				{/if}

				<div class="flex flex-wrap gap-3">
					<button class="btn btn-outline btn-sm" onclick={handleLogout}>Sign Out</button>
					<button class="btn btn-error btn-sm" onclick={handleDeleteAccount} disabled={deleteAccountSaving}>
						{deleteAccountSaving ? 'Deleting...' : 'Delete Account'}
					</button>
				</div>
			</div>
		</section>
	{:else if activeTab === 'organization'}
		<section class="card bg-base-100 border border-base-300 shadow-sm mb-5">
			<div class="card-body">
				<h2 class="card-title text-base">Active Organization</h2>
				<p class="text-sm text-base-content/60 mb-4">Select which organization to track time for. This applies globally to new time entries.</p>

				<div class="flex flex-col gap-2">
					<button
						type="button"
						class={"flex items-center gap-3 border rounded-xl px-4 py-3 cursor-pointer text-left w-full transition-colors hover:border-base-content/20 hover:bg-base-200 " + (orgContext.selectedOrgId === null ? "border-primary bg-primary/5" : "bg-base-200/50 border-base-300")}
						onclick={() => handleOrgSelect(null)}
					>
						<input type="radio" class="radio radio-primary radio-sm" checked={orgContext.selectedOrgId === null} tabindex="-1" />
						<span class="text-sm font-medium">Personal (no organization)</span>
					</button>

					{#each orgContext.organizations as org}
						<button
							type="button"
							class={"flex items-center gap-3 border rounded-xl px-4 py-3 cursor-pointer text-left w-full transition-colors hover:border-base-content/20 hover:bg-base-200 " + (orgContext.selectedOrgId === org.organizationId ? "border-primary bg-primary/5" : "bg-base-200/50 border-base-300")}
							onclick={() => handleOrgSelect(org.organizationId ?? null)}
						>
							<input type="radio" class="radio radio-primary radio-sm" checked={orgContext.selectedOrgId === org.organizationId} tabindex="-1" />
							<div class="flex gap-2 items-center">
								<span class="text-sm font-medium">{org.name}</span>
								<span class="text-[0.6875rem] text-base-content/60 uppercase font-semibold tracking-wide">{org.role}</span>
							</div>
						</button>
					{/each}

					{#if orgContext.organizations.length === 0 && !orgContext.loading}
						<p class="text-sm text-base-content/50">No organizations yet. <a href="/organizations/new" class="link link-primary">Create one</a>.</p>
					{/if}
				</div>

				<div class="flex gap-4 mt-4 pt-4 border-t border-base-200">
					<a href="/organizations" class="text-sm link link-primary font-medium no-underline hover:underline">Browse Organizations</a>
					<a href="/organizations/new" class="text-sm link link-primary font-medium no-underline hover:underline">+ Create New</a>
				</div>
			</div>
		</section>

		{#if orgContext.selectedOrgId}
			<section class="card bg-base-100 border border-base-300 shadow-sm mb-5">
				<div class="card-body">
					<h2 class="card-title text-base">Schedule Periods</h2>
					<p class="text-sm text-base-content/60 mb-4">Manage your work schedule in <strong>{orgContext.selectedOrg?.name}</strong> through schedule periods only.</p>

					{#if periodError}
						<div class="alert alert-error text-sm mb-4">{periodError}</div>
					{/if}
					{#if periodSuccess}
						<div class="alert alert-success text-sm mb-4">{periodSuccess}</div>
					{/if}

					{#if scheduleLoading}
						<p class="text-sm text-base-content/50">Loading schedule periods...</p>
					{:else}
						{#if currentSchedule && (currentSchedule.id ?? 0) > 0}
							<div class="mb-4 p-3 rounded-lg border border-base-300 bg-base-200/40">
								<p class="text-sm text-base-content/70 mb-1">Current active period</p>
								<p class="text-sm font-medium text-base-content">
									{currentSchedule.validFrom} &rarr; {currentSchedule.validTo ?? 'ongoing'}
									<span class="ml-2 text-primary">{currentSchedule.weeklyWorkHours ?? '—'}h/week</span>
								</p>
							</div>
						{/if}

						{#if currentSchedule?.workScheduleChangeMode === 'RequiresApproval'}
							<div class="alert alert-warning text-sm mb-4">Schedule period changes require admin approval.</div>
						{:else if currentSchedule?.workScheduleChangeMode === 'Disabled'}
							<div class="alert alert-info text-sm mb-4">Schedule period changes are disabled in this organization.</div>
						{/if}

						{#if currentSchedule?.workScheduleChangeMode === 'Allowed'}
							<button
								class="btn btn-primary btn-sm mb-4"
								onclick={() => {
									showAddSchedulePeriod = !showAddSchedulePeriod;
									if (!showAddSchedulePeriod) clearSchedulePeriodForm();
								}}
							>
								{showAddSchedulePeriod ? 'Cancel' : '+ Add Period'}
							</button>
						{/if}

						{#if showAddSchedulePeriod && currentSchedule?.workScheduleChangeMode === 'Allowed'}
							<div class="flex flex-col gap-3 p-3 rounded-lg mb-4 border border-base-300 bg-base-200/40">
								<div class="grid grid-cols-1 md:grid-cols-2 gap-3">
									<div>
										<label for="periodFrom" class="block text-sm font-medium text-base-content/80 mb-1.5">Valid From</label>
										<input id="periodFrom" type="date" bind:value={periodFrom} class="input input-bordered input-sm w-full" />
									</div>
									<div>
										<label for="periodTo" class="block text-sm font-medium text-base-content/80 mb-1.5">Valid To (optional)</label>
										<input id="periodTo" type="date" min={periodFrom || undefined} bind:value={periodTo} class="input input-bordered input-sm w-full" />
									</div>
								</div>

								<div>
									<label for="periodWeekly" class="block text-sm font-medium text-base-content/80 mb-1.5">Weekly Work Hours</label>
									<input id="periodWeekly" type="number" step="0.5" min="0" max="80" bind:value={periodWeeklyHours} class="input input-bordered input-sm max-w-[120px]" />
								</div>

								<label class="flex items-center gap-2 text-sm text-base-content/80 cursor-pointer">
									<input type="checkbox" class="checkbox checkbox-primary checkbox-sm" bind:checked={periodDistributeEvenly} />
									Distribute evenly (Mon-Fri)
								</label>

								{#if !periodDistributeEvenly}
									<div class="grid grid-cols-5 gap-2">
										<div class="flex flex-col items-center gap-1"><label for="periodMon" class="text-xs font-semibold text-base-content/60 uppercase">Mon</label><input id="periodMon" type="number" step="0.5" min="0" max="24" bind:value={periodMon} class="input input-bordered input-xs w-full text-center" /></div>
										<div class="flex flex-col items-center gap-1"><label for="periodTue" class="text-xs font-semibold text-base-content/60 uppercase">Tue</label><input id="periodTue" type="number" step="0.5" min="0" max="24" bind:value={periodTue} class="input input-bordered input-xs w-full text-center" /></div>
										<div class="flex flex-col items-center gap-1"><label for="periodWed" class="text-xs font-semibold text-base-content/60 uppercase">Wed</label><input id="periodWed" type="number" step="0.5" min="0" max="24" bind:value={periodWed} class="input input-bordered input-xs w-full text-center" /></div>
										<div class="flex flex-col items-center gap-1"><label for="periodThu" class="text-xs font-semibold text-base-content/60 uppercase">Thu</label><input id="periodThu" type="number" step="0.5" min="0" max="24" bind:value={periodThu} class="input input-bordered input-xs w-full text-center" /></div>
										<div class="flex flex-col items-center gap-1"><label for="periodFri" class="text-xs font-semibold text-base-content/60 uppercase">Fri</label><input id="periodFri" type="number" step="0.5" min="0" max="24" bind:value={periodFri} class="input input-bordered input-xs w-full text-center" /></div>
									</div>
								{/if}

								<div class="flex gap-2">
									<button class="btn btn-primary btn-sm" onclick={saveSchedulePeriod} disabled={periodSaving || !periodFrom}>
										{periodSaving ? 'Saving...' : 'Save Period'}
									</button>
								</div>
							</div>
						{/if}

						{#if schedulePeriods.length === 0}
							<p class="text-sm text-base-content/50">No schedule periods configured yet.</p>
						{:else}
							<div class="flex flex-col gap-2">
								{#each schedulePeriods as period}
									{@const today = new Date().toISOString().slice(0, 10)}
									{@const isActive = period.validFrom && period.validFrom <= today && (!period.validTo || period.validTo >= today)}
									<div class="flex items-center gap-3 py-2 px-3 rounded-lg border text-sm {isActive ? 'border-success/40 bg-success/5' : 'border-base-300 bg-base-200/30'}">
										<div class="flex items-center gap-1.5 flex-1 min-w-0">
											<span class="font-medium text-base-content/70">{period.validFrom}</span>
											<span class="text-base-content/40 text-xs">&rarr;</span>
											<span class="font-medium text-base-content/70">{period.validTo ?? 'ongoing'}</span>
											{#if isActive}
												<span class="badge badge-success badge-xs">Active</span>
											{/if}
										</div>
										<span class="font-semibold text-base-content whitespace-nowrap">{period.weeklyWorkHours ?? '—'}h/week</span>
										{#if currentSchedule?.workScheduleChangeMode === 'Allowed'}
											<button class="btn btn-ghost btn-xs text-error" onclick={() => deleteSchedulePeriod(period.id!)} title="Delete period">&times;</button>
										{/if}
									</div>
								{/each}
							</div>
						{/if}
					{/if}
				</div>
			</section>

			{#if initialOvertimeMode !== 'Disabled'}
				<section class="card bg-base-100 border border-base-300 shadow-sm mb-5">
					<div class="card-body">
						<h2 class="card-title text-base">Initial Overtime Balance</h2>
						<p class="text-sm text-base-content/60 mb-4">
							Set your starting overtime balance for <strong>{orgContext.selectedOrg?.name}</strong>.
							{#if initialOvertimeMode === 'RequiresApproval'}
								<em>This requires admin approval.</em>
							{/if}
						</p>

						{#if overtimeError}
							<div class="alert alert-error text-sm mb-4">{overtimeError}</div>
						{/if}
						{#if overtimeSuccess}
							<div class="alert alert-success text-sm mb-4">{overtimeSuccess}</div>
						{/if}

						<div class="mb-4">
							<label for="initialOvertime" class="block text-sm font-medium text-base-content/80 mb-1.5">Overtime (hours)</label>
							<input
								id="initialOvertime"
								type="number"
								step="0.5"
								bind:value={initialOvertimeHours}
								placeholder="e.g. 2.5 or -1"
								class="input input-bordered input-sm max-w-[120px]"
								disabled={initialOvertimeMode === 'RequiresApproval'}
							/>
							<span class="block text-xs text-base-content/50 mt-1">
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
							<button class="btn btn-primary btn-sm" onclick={saveInitialOvertime} disabled={overtimeSaving}>
								{overtimeSaving ? 'Saving...' : 'Save Overtime'}
							</button>
						{:else}
							<p class="text-sm text-base-content/50" style="font-size: 0.8125rem;">Submit a request through the Timer page to change this value.</p>
						{/if}
					</div>
				</section>
			{/if}
		{/if}
	{:else}
		<section class="card bg-base-100 border border-base-300 shadow-sm mb-5">
			<div class="card-body">
				<h2 class="card-title text-base">Theme</h2>
				<p class="text-sm text-base-content/60 mb-4">Choose your preferred theme. The active tile is highlighted, and each tile previews its look.</p>

				<div class="grid grid-cols-2 md:grid-cols-3 gap-3">
					{#each theme.themes as t}
						<button
							type="button"
							class={"group rounded-xl border p-2 text-left transition-all " + (theme.current === t
								? 'border-primary bg-primary/5 ring-2 ring-primary/25'
								: 'border-base-300 hover:border-primary/50 hover:bg-base-200/40')}
							onclick={() => theme.set(t)}
							aria-pressed={theme.current === t}
						>
							<div data-theme={t} class="rounded-lg border border-base-300 bg-base-100 p-3 shadow-sm">
								<div class="mb-2 flex items-center justify-between">
									<span class="text-[0.65rem] font-semibold uppercase tracking-wider text-base-content/70">{formatThemeName(t)}</span>
									{#if theme.current === t}
										<span class="badge badge-primary badge-xs">Active</span>
									{/if}
								</div>
								<div class="space-y-1.5">
									<div class="h-1.5 w-full rounded bg-primary"></div>
									<div class="h-1.5 w-3/4 rounded bg-secondary"></div>
									<div class="h-1.5 w-1/2 rounded bg-accent"></div>
									<div class="mt-2 flex gap-1">
										<span class="badge badge-primary badge-xs">Aa</span>
										<span class="badge badge-outline badge-xs">UI</span>
									</div>
								</div>
							</div>
						</button>
					{/each}
				</div>
			</div>
		</section>
	{/if}
</div>
