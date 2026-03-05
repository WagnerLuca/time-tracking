<script lang="ts">
	import { onMount } from 'svelte';
	import { auth } from '$lib/stores/auth.svelte';
	import { orgContext } from '$lib/stores/orgContext.svelte';
	import { goto } from '$app/navigation';
	import { organizationsApi, authApi, workScheduleApi } from '$lib/apiClient';
	import type { WorkScheduleResponse, UpdateWorkScheduleRequest, CreateWorkScheduleRequest } from '$lib/api';
	import { extractErrorMessage } from '$lib/utils/errorHandler';

	let changePasswordError = $state('');
	let changePasswordSuccess = $state('');
	let currentPassword = $state('');
	let newPassword = $state('');
	let saving = $state(false);

	// Work schedule state
	let currentScheduleId = $state<number | null>(null);
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
			currentScheduleId = schedule.id ?? null;
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
			let result: WorkScheduleResponse;
			if (currentScheduleId && currentScheduleId > 0) {
				// Update existing schedule
				const payload: UpdateWorkScheduleRequest = {
					weeklyWorkHours: weeklyHours ?? undefined,
					distributeEvenly,
					targetMon: distributeEvenly ? undefined : targetMon,
					targetTue: distributeEvenly ? undefined : targetTue,
					targetWed: distributeEvenly ? undefined : targetWed,
					targetThu: distributeEvenly ? undefined : targetThu,
					targetFri: distributeEvenly ? undefined : targetFri
				};
				const { data } = await workScheduleApi.apiOrganizationsSlugWorkSchedulesIdPut(orgContext.selectedOrgSlug, currentScheduleId, payload);
				result = data;
			} else {
				// Create new schedule
				const today = new Date();
				const validFrom = `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, '0')}-${String(today.getDate()).padStart(2, '0')}`;
				const payload: CreateWorkScheduleRequest = {
					validFrom,
					weeklyWorkHours: weeklyHours ?? undefined,
					distributeEvenly,
					targetMon: distributeEvenly ? undefined : targetMon,
					targetTue: distributeEvenly ? undefined : targetTue,
					targetWed: distributeEvenly ? undefined : targetWed,
					targetThu: distributeEvenly ? undefined : targetThu,
					targetFri: distributeEvenly ? undefined : targetFri
				};
				const { data } = await workScheduleApi.apiOrganizationsSlugWorkSchedulesPost(orgContext.selectedOrgSlug, payload);
				result = data;
			}
			// Update local state with server response
			currentScheduleId = result.id ?? null;
			weeklyHours = result.weeklyWorkHours ?? null;
			targetMon = result.targetMon ?? 0;
			targetTue = result.targetTue ?? 0;
			targetWed = result.targetWed ?? 0;
			targetThu = result.targetThu ?? 0;
			targetFri = result.targetFri ?? 0;
			scheduleSuccess = 'Work schedule saved.';
			setTimeout(() => (scheduleSuccess = ''), 3000);
		} catch (err) {
			scheduleError = extractErrorMessage(err, 'Failed to save work schedule.');
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
		} catch (err) {
			overtimeError = extractErrorMessage(err, 'Failed to save initial overtime.');
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
		} catch (err) {
			changePasswordError = extractErrorMessage(err, 'Failed to change password.');
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

<div class="max-w-2xl mx-auto">
	<h1 class="text-2xl font-bold mb-6">Settings</h1>

	<!-- Profile section -->
	<section class="card bg-base-100 border border-base-300 shadow-sm mb-5">
		<div class="card-body">
			<h2 class="card-title text-base">Profile</h2>
			<div class="flex flex-col gap-2">
				<div class="flex gap-4 items-baseline">
					<span class="text-xs text-base-content/50 min-w-[60px]">Name</span>
					<span class="text-sm text-base-content font-medium">{auth.user?.firstName} {auth.user?.lastName}</span>
				</div>
				<div class="flex gap-4 items-baseline">
					<span class="text-xs text-base-content/50 min-w-[60px]">Email</span>
					<span class="text-sm text-base-content font-medium">{auth.user?.email}</span>
				</div>
			</div>
		</div>
	</section>

	<!-- Organization section -->
	<section class="card bg-base-100 border border-base-300 shadow-sm mb-5">
		<div class="card-body">
			<h2 class="card-title text-base">Active Organization</h2>
			<p class="text-sm text-base-content/60 mb-4">Select which organization to track time for. This applies globally to new time entries.</p>

			<div class="flex flex-col gap-2">
				<button
					class={"flex items-center gap-3 border rounded-xl px-4 py-3 cursor-pointer text-left w-full transition-colors hover:border-base-content/20 hover:bg-base-200 " + (orgContext.selectedOrgId === null ? "border-primary bg-primary/5" : "bg-base-200/50 border-base-300")}
					onclick={() => handleOrgSelect(null)}
				>
					<input type="radio" class="radio radio-primary radio-sm" checked={orgContext.selectedOrgId === null} tabindex="-1" />
					<span class="text-sm font-medium">Personal (no organization)</span>
				</button>

				{#each orgContext.organizations as org}
					<button
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

	<!-- Work Schedule (when org is selected) -->
	{#if orgContext.selectedOrgId}
		<section class="card bg-base-100 border border-base-300 shadow-sm mb-5">
			<div class="card-body">
				<h2 class="card-title text-base">Work Schedule</h2>
				<p class="text-sm text-base-content/60 mb-4">Set your weekly work hours target for <strong>{orgContext.selectedOrg?.name}</strong>. This helps track your progress.</p>

				{#if scheduleError}
					<div class="alert alert-error text-sm mb-4">{scheduleError}</div>
				{/if}
				{#if scheduleSuccess}
					<div class="alert alert-success text-sm mb-4">{scheduleSuccess}</div>
				{/if}

				{#if scheduleLoading}
					<p class="text-sm text-base-content/50">Loading schedule...</p>
				{:else}
					<div class="mb-4">
						<label for="weeklyHours" class="block text-sm font-medium text-base-content/80 mb-1.5">Weekly Work Hours</label>
						<input
							id="weeklyHours"
							type="number"
							step="0.5"
							min="0"
							max="80"
							bind:value={weeklyHours}
							placeholder="e.g. 40"
							class="input input-bordered input-sm max-w-[120px]"
						/>
					</div>

					<div class="mb-4">
						<label class="flex items-center gap-2 text-sm text-base-content/80 cursor-pointer">
							<input type="checkbox" class="checkbox checkbox-primary checkbox-sm" bind:checked={distributeEvenly} />
							Distribute equally (Mon–Fri)
						</label>
					</div>

					{#if !distributeEvenly}
						<div class="flex gap-3 mb-5 flex-wrap">
							<div class="flex flex-col items-center gap-1">
								<label for="tMon" class="text-xs font-semibold text-base-content/60 uppercase">Mon</label>
								<input id="tMon" type="number" step="0.5" min="0" max="24" bind:value={targetMon} class="input input-bordered input-xs max-w-[70px] text-center" />
							</div>
							<div class="flex flex-col items-center gap-1">
								<label for="tTue" class="text-xs font-semibold text-base-content/60 uppercase">Tue</label>
								<input id="tTue" type="number" step="0.5" min="0" max="24" bind:value={targetTue} class="input input-bordered input-xs max-w-[70px] text-center" />
							</div>
							<div class="flex flex-col items-center gap-1">
								<label for="tWed" class="text-xs font-semibold text-base-content/60 uppercase">Wed</label>
								<input id="tWed" type="number" step="0.5" min="0" max="24" bind:value={targetWed} class="input input-bordered input-xs max-w-[70px] text-center" />
							</div>
							<div class="flex flex-col items-center gap-1">
								<label for="tThu" class="text-xs font-semibold text-base-content/60 uppercase">Thu</label>
								<input id="tThu" type="number" step="0.5" min="0" max="24" bind:value={targetThu} class="input input-bordered input-xs max-w-[70px] text-center" />
							</div>
							<div class="flex flex-col items-center gap-1">
								<label for="tFri" class="text-xs font-semibold text-base-content/60 uppercase">Fri</label>
								<input id="tFri" type="number" step="0.5" min="0" max="24" bind:value={targetFri} class="input input-bordered input-xs max-w-[70px] text-center" />
							</div>
						</div>
					{/if}

					<button class="btn btn-primary btn-sm" onclick={saveWorkSchedule} disabled={scheduleSaving || weeklyHours == null}>
						{scheduleSaving ? 'Saving...' : 'Save Schedule'}
					</button>
				{/if}
			</div>
		</section>

		<!-- Initial Overtime Balance (separate card) -->
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

	<!-- Change password -->
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

	<!-- Sign out -->
	<section class="card bg-base-100 border border-error/30 shadow-sm mb-5">
		<div class="card-body">
			<button class="btn btn-error btn-sm" onclick={handleLogout}>Sign Out</button>
		</div>
	</section>
</div>
