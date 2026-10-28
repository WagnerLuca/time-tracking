<script lang="ts">
	import QRCode from 'qrcode';
	import { auth } from '$lib/stores/auth.svelte';
	import { orgContext } from '$lib/stores/orgContext.svelte';
	import { goto } from '$app/navigation';
	import { page } from '$app/state';
	import { authApi, workScheduleApi } from '$lib/apiClient';
	import type { WorkScheduleResponse, UpdateProfileRequest, TwoFactorSetupResponse } from '$lib/api';
	import { extractErrorMessage, getErrorStatus } from '$lib/utils/errorHandler';
	import { roleLabel } from '$lib/utils/formatters';
	import { theme } from '$lib/stores/theme.svelte';
	import { locale, type Locale } from '$lib/stores/locale.svelte';
	import { toast } from '$lib/stores/toast.svelte';
	import { t } from '$lib/i18n';
	import WorkSchedulePlan from '$lib/components/WorkSchedulePlan.svelte';
	import AppSettings from '$lib/components/AppSettings.svelte';
	import InitialOvertimeEditor from '$lib/components/InitialOvertimeEditor.svelte';
	import Modal from '$lib/components/Modal.svelte';
	import { downloadBlob } from '$lib/utils/download';
	import { dateKey } from '$lib/utils/dateHelpers';

	/** The backend's minimum (RegisterRequest / ChangePasswordRequest). */
	const MIN_PASSWORD_LENGTH = 8;

	let activeTab = $state<'profile' | 'organization' | 'appearance'>('profile');
	// "App installieren" in the Mehr menu links to #app (install + push live on the appearance tab)
	$effect(() => {
		if (page.url.hash === '#app' || page.url.hash === '#push') activeTab = 'appearance';
	});

	// Profile
	let profileFirstName = $state('');
	let profileLastName = $state('');
	let profileEmail = $state('');
	let profileUserId = $state<number | null>(null);
	let profileSubmitted = $state(false);
	let profileSaving = $state(false);
	let profileError = $state('');
	let firstNameError = $derived(profileSubmitted && !profileFirstName.trim() ? t('settings.profile.firstNameRequired') : '');
	let lastNameError = $derived(profileSubmitted && !profileLastName.trim() ? t('settings.profile.lastNameRequired') : '');
	let emailError = $derived(
		!profileSubmitted ? '' : !profileEmail.trim() ? t('settings.profile.emailRequired') : !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(profileEmail.trim()) ? t('settings.profile.emailInvalid') : ''
	);

	/** Changing the email needs the current password; the new address only applies once confirmed. */
	let emailChanged = $derived(profileEmail.trim().toLowerCase() !== (auth.user?.email ?? '').toLowerCase());
	let profileCurrentPassword = $state('');
	let profileCurrentPasswordError = $derived(profileSubmitted && emailChanged && !profileCurrentPassword ? t('settings.profile.currentPasswordRequired') : '');

	// Password
	let currentPassword = $state('');
	let newPassword = $state('');
	let confirmPassword = $state('');
	let passwordSubmitted = $state(false);
	let passwordSaving = $state(false);
	let passwordError = $state('');
	let currentPasswordError = $derived(passwordSubmitted && !currentPassword ? t('settings.password.currentRequired') : '');
	let newPasswordError = $derived(
		!passwordSubmitted ? '' : newPassword.length < MIN_PASSWORD_LENGTH ? t('settings.password.minLength', { min: MIN_PASSWORD_LENGTH }) : newPassword === currentPassword ? t('settings.password.mustDiffer') : ''
	);
	let confirmPasswordError = $derived(passwordSubmitted && confirmPassword !== newPassword ? t('settings.password.mismatch') : '');

	// Two-factor authentication
	let twoFactorStep = $state<'idle' | 'setup' | 'backup'>('idle');
	let twoFactorSetup = $state<TwoFactorSetupResponse | null>(null);
	// QR rendered locally — the TOTP secret must never be sent to a third-party QR service
	let qrDataUrl = $state('');
	$effect(() => {
		const uri = twoFactorSetup?.authenticatorUri;
		qrDataUrl = '';
		if (uri) QRCode.toDataURL(uri, { width: 200, margin: 1 }).then((url) => (qrDataUrl = url));
	});
	let twoFactorConfirmCode = $state('');
	let twoFactorBackupCodes = $state<string[]>([]);
	let twoFactorError = $state('');
	let twoFactorSaving = $state(false);
	let twoFactorDisablePassword = $state('');
	let twoFactorDisableCode = $state('');

	// GDPR data export (JSON download of everything stored about the account)
	let exportingData = $state(false);

	// Account deletion (password-confirmed)
	let deleteOpen = $state(false);
	let deletePassword = $state('');
	let deleteError = $state('');
	let deleteAccountSaving = $state(false);

	// Schedule of the selected org (for the rule modes; periods load inside the editor)
	let currentSchedule = $state<WorkScheduleResponse | null>(null);
	let scheduleLoading = $state(false);
	let scheduleError = $state('');

	$effect(() => {
		const slug = orgContext.selectedOrgSlug;
		if (slug) loadSchedule(slug);
		else currentSchedule = null;
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

	async function loadSchedule(slug: string) {
		scheduleLoading = true;
		scheduleError = '';
		try {
			const { data } = await workScheduleApi.apiV1OrganizationsSlugWorkScheduleGet(slug);
			if (slug === orgContext.selectedOrgSlug) currentSchedule = data;
		} catch (err) {
			currentSchedule = null;
			if (getErrorStatus(err) !== 404) scheduleError = extractErrorMessage(err, t('settings.org.scheduleFailed'));
		} finally {
			scheduleLoading = false;
		}
	}

	async function handleSaveProfile(e: Event) {
		e.preventDefault();
		profileSubmitted = true;
		if (firstNameError || lastNameError || emailError || profileCurrentPasswordError) return;
		profileError = '';
		profileSaving = true;
		try {
			const changingEmail = emailChanged;
			const payload: UpdateProfileRequest = {
				firstName: profileFirstName.trim(),
				lastName: profileLastName.trim(),
				...(changingEmail ? { email: profileEmail.trim(), currentPassword: profileCurrentPassword } : {})
			};
			const { data: updatedUser } = await authApi.apiV1AuthProfilePut(payload);
			profileFirstName = updatedUser.firstName ?? '';
			profileLastName = updatedUser.lastName ?? '';
			profileEmail = updatedUser.email ?? ''; // still the old address until the link is used
			profileCurrentPassword = '';
			await auth.fetchCurrentUser();
			profileSubmitted = false;
			if (changingEmail && updatedUser.pendingEmail) toast.success(t('settings.profile.emailChangeSent', { email: updatedUser.pendingEmail }));
			else toast.success(t('settings.profile.saved'));
		} catch (err) {
			profileError = extractErrorMessage(err, t('settings.profile.saveFailed'));
		} finally {
			profileSaving = false;
		}
	}

	async function handleChangePassword(e: Event) {
		e.preventDefault();
		passwordSubmitted = true;
		if (currentPasswordError || newPasswordError || confirmPasswordError) return;
		passwordError = '';
		passwordSaving = true;
		try {
			await authApi.apiV1AuthChangePasswordPost({ currentPassword, newPassword });
			// The backend ends every session, this one included: sign out cleanly instead of running into 401s
			await auth.logout();
			toast.success(t('settings.password.changedSignIn'));
			goto('/login');
		} catch (err) {
			passwordError = extractErrorMessage(err, t('settings.password.failed'));
		} finally {
			passwordSaving = false;
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
			twoFactorError = extractErrorMessage(err, t('settings.twoFactor.setupFailed'));
		} finally {
			twoFactorSaving = false;
		}
	}

	async function handleConfirm2FA(e: Event) {
		e.preventDefault();
		if (!/^\d{6}$/.test(twoFactorConfirmCode)) {
			twoFactorError = t('settings.twoFactor.codeRequired');
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
			twoFactorError = extractErrorMessage(err, t('settings.twoFactor.invalidCode'));
		} finally {
			twoFactorSaving = false;
		}
	}

	function handleFinish2FASetup() {
		twoFactorStep = 'idle';
		twoFactorSetup = null;
		twoFactorBackupCodes = [];
		toast.success(t('settings.twoFactor.enabled'));
	}

	async function handleDisable2FA(e: Event) {
		e.preventDefault();
		if (!twoFactorDisablePassword) {
			twoFactorError = t('settings.twoFactor.passwordRequired');
			return;
		}
		if (!twoFactorDisableCode.trim()) {
			twoFactorError = t('settings.twoFactor.disableCodeRequired');
			return;
		}
		twoFactorError = '';
		twoFactorSaving = true;
		try {
			await authApi.apiV1Auth2faDisablePost({ currentPassword: twoFactorDisablePassword, code: twoFactorDisableCode.trim() });
			twoFactorDisablePassword = '';
			twoFactorDisableCode = '';
			await auth.fetchCurrentUser();
			toast.success(t('settings.twoFactor.disabled'));
		} catch (err) {
			twoFactorError = extractErrorMessage(err, t('settings.twoFactor.disableFailed'));
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

	async function handleExportData() {
		exportingData = true;
		try {
			const res = await authApi.apiV1AuthMeExportGet({ responseType: 'blob' });
			downloadBlob(res.data as unknown as BlobPart, 'application/json', `my-data-${dateKey(new Date())}.json`);
			toast.success(t('settings.account.exported'));
		} catch (err) {
			toast.error(err, t('settings.account.exportFailed'));
		} finally {
			exportingData = false;
		}
	}

	function openDeleteAccount() {
		deletePassword = '';
		deleteError = '';
		deleteOpen = true;
	}

	async function handleDeleteAccount(e: Event) {
		e.preventDefault();
		if (!deletePassword) {
			deleteError = t('settings.account.deletePasswordRequired');
			return;
		}
		deleteError = '';
		deleteAccountSaving = true;
		try {
			await authApi.apiV1AuthAccountDeletePost({ password: deletePassword });
			deleteOpen = false;
			await auth.logout();
			toast.success(t('settings.account.deleted'));
			goto('/login');
		} catch (err) {
			// e.g. 400 "You own …": the backend lists the organizations to hand over first
			deleteError = extractErrorMessage(err, t('settings.account.deleteFailed'));
		} finally {
			deleteAccountSaving = false;
		}
	}

	const LOCALES: Locale[] = ['de', 'en'];
</script>

<svelte:head>
	<title>{t('common.pageTitle', { page: t('settings.pageTitle') })}</title>
</svelte:head>

<div class="max-w-3xl mx-auto">
	<h1 class="text-2xl font-bold mb-4">{t('settings.title')}</h1>

	<div class="tabs p-1 bg-base-200/70 mb-6 w-full md:w-fit flex-wrap" role="tablist" aria-label={t('settings.tabsLabel')}>
		{#each ['profile', 'organization', 'appearance'] as const as tab}
			<button
				type="button"
				role="tab"
				class="tab {activeTab === tab ? 'tab-active' : ''}"
				onclick={() => (activeTab = tab)}
				aria-selected={activeTab === tab}
			>
				{t(`settings.tabs.${tab}`)}
			</button>
		{/each}
	</div>

	{#if activeTab === 'profile'}
		<section class="card bg-base-100 border border-base-300 shadow-sm mb-5">
			<form class="card-body" onsubmit={handleSaveProfile} novalidate>
				<h2 class="card-title text-base">{t('settings.profile.title')}</h2>
				{#if profileError}
					<div class="alert alert-error text-sm">{profileError}</div>
				{/if}
				<div class="grid grid-cols-1 md:grid-cols-2 gap-3">
					<div>
						<label for="profileFirstName" class="block text-sm font-medium text-base-content/80 mb-1.5">{t('settings.profile.firstName')}</label>
						<input id="profileFirstName" type="text" autocomplete="given-name" bind:value={profileFirstName} class="input w-full" class:input-error={firstNameError} aria-invalid={!!firstNameError} disabled={profileSaving} />
						{#if firstNameError}<p class="text-error text-xs mt-1">{firstNameError}</p>{/if}
					</div>
					<div>
						<label for="profileLastName" class="block text-sm font-medium text-base-content/80 mb-1.5">{t('settings.profile.lastName')}</label>
						<input id="profileLastName" type="text" autocomplete="family-name" bind:value={profileLastName} class="input w-full" class:input-error={lastNameError} aria-invalid={!!lastNameError} disabled={profileSaving} />
						{#if lastNameError}<p class="text-error text-xs mt-1">{lastNameError}</p>{/if}
					</div>
				</div>
				<div>
					<label for="profileEmail" class="block text-sm font-medium text-base-content/80 mb-1.5">{t('settings.profile.email')}</label>
					<input id="profileEmail" type="email" autocomplete="email" bind:value={profileEmail} class="input w-full max-w-md" class:input-error={emailError} aria-invalid={!!emailError} disabled={profileSaving} />
					{#if emailError}<p class="text-error text-xs mt-1">{emailError}</p>{/if}
					{#if auth.user?.pendingEmail}<p class="text-xs text-base-content/70 mt-1">{t('settings.profile.pendingEmail', { email: auth.user.pendingEmail })}</p>{/if}
				</div>
				{#if emailChanged}
					<div>
						<label for="profileCurrentPw" class="block text-sm font-medium text-base-content/80 mb-1.5">{t('settings.profile.currentPassword')}</label>
						<input id="profileCurrentPw" type="password" autocomplete="current-password" bind:value={profileCurrentPassword} class="input w-full max-w-xs" class:input-error={profileCurrentPasswordError} aria-invalid={!!profileCurrentPasswordError} aria-describedby="profileCurrentPwHint" disabled={profileSaving} />
						<p id="profileCurrentPwHint" class="text-xs mt-1 {profileCurrentPasswordError ? 'text-error' : 'text-base-content/70'}">{profileCurrentPasswordError || t('settings.profile.currentPasswordHint')}</p>
					</div>
				{/if}
				<div>
					<button type="submit" class="btn btn-primary btn-sm" disabled={profileSaving}>
						{profileSaving ? t('common.saving') : t('settings.profile.save')}
					</button>
				</div>
			</form>
		</section>

		<section class="card bg-base-100 border border-base-300 shadow-sm mb-5">
			<form class="card-body" onsubmit={handleChangePassword} novalidate>
				<h2 class="card-title text-base">{t('settings.password.title')}</h2>
				{#if passwordError}
					<div class="alert alert-error text-sm">{passwordError}</div>
				{/if}
				<div>
					<label for="currentPw" class="block text-sm font-medium text-base-content/80 mb-1.5">{t('settings.password.current')}</label>
					<input id="currentPw" type="password" autocomplete="current-password" bind:value={currentPassword} class="input w-full max-w-xs" class:input-error={currentPasswordError} aria-invalid={!!currentPasswordError} disabled={passwordSaving} />
					{#if currentPasswordError}<p class="text-error text-xs mt-1">{currentPasswordError}</p>{/if}
				</div>
				<div>
					<label for="newPw" class="block text-sm font-medium text-base-content/80 mb-1.5">{t('settings.password.new')}</label>
					<input id="newPw" type="password" autocomplete="new-password" minlength={MIN_PASSWORD_LENGTH} bind:value={newPassword} class="input w-full max-w-xs" class:input-error={newPasswordError} aria-invalid={!!newPasswordError} aria-describedby="newPwHint" disabled={passwordSaving} />
					<p id="newPwHint" class="text-xs mt-1 {newPasswordError ? 'text-error' : 'text-base-content/70'}">{newPasswordError || t('settings.password.minLength', { min: MIN_PASSWORD_LENGTH })}</p>
				</div>
				<div>
					<label for="confirmPw" class="block text-sm font-medium text-base-content/80 mb-1.5">{t('settings.password.repeat')}</label>
					<input id="confirmPw" type="password" autocomplete="new-password" bind:value={confirmPassword} class="input w-full max-w-xs" class:input-error={confirmPasswordError} aria-invalid={!!confirmPasswordError} disabled={passwordSaving} />
					{#if confirmPasswordError}<p class="text-error text-xs mt-1">{confirmPasswordError}</p>{/if}
				</div>
				<div>
					<button type="submit" class="btn btn-primary btn-sm" disabled={passwordSaving}>
						{passwordSaving ? t('common.saving') : t('settings.password.submit')}
					</button>
				</div>
			</form>
		</section>

		<section id="two-factor" class="card bg-base-100 border border-base-300 shadow-sm mb-5 scroll-mt-20">
			<div class="card-body">
				<h2 class="card-title text-base">{t('settings.twoFactor.title')}</h2>

				{#if twoFactorError}
					<div class="alert alert-error text-sm">{twoFactorError}</div>
				{/if}

				{#if twoFactorStep === 'idle'}
					{#if auth.user?.twoFactorEnabled}
						<div class="flex items-center gap-2 flex-wrap">
							<span class="badge badge-success badge-sm">{t('settings.twoFactor.on')}</span>
							<span class="text-sm text-base-content/70">{t('settings.twoFactor.activeText')}</span>
						</div>
						<form class="flex gap-2 items-end flex-wrap" onsubmit={handleDisable2FA}>
							<div>
								<label for="disable2faPassword" class="block text-sm font-medium text-base-content/80 mb-1.5">{t('settings.twoFactor.disablePassword')}</label>
								<input
									id="disable2faPassword"
									type="password"
									autocomplete="current-password"
									bind:value={twoFactorDisablePassword}
									class="input input-sm w-full max-w-xs"
									disabled={twoFactorSaving}
								/>
							</div>
							<div>
								<label for="disable2faCode" class="block text-sm font-medium text-base-content/80 mb-1.5">{t('settings.twoFactor.disableCode')}</label>
								<input
									id="disable2faCode"
									type="text"
									autocomplete="one-time-code"
									bind:value={twoFactorDisableCode}
									class="input input-sm w-[180px] font-mono"
									aria-describedby="disable2faCodeHint"
									maxlength="20"
									disabled={twoFactorSaving}
								/>
							</div>
							<button type="submit" class="btn btn-error btn-sm" disabled={twoFactorSaving || !twoFactorDisablePassword || !twoFactorDisableCode.trim()}>
								{twoFactorSaving ? t('settings.twoFactor.disabling') : t('settings.twoFactor.disable')}
							</button>
						</form>
						<p id="disable2faCodeHint" class="text-xs text-base-content/70">{t('settings.twoFactor.disableCodeHint')}</p>
					{:else}
						<p class="text-sm text-base-content/70">{t('settings.twoFactor.intro')}</p>
						<div>
							<button type="button" class="btn btn-primary btn-sm" onclick={handleSetup2FA} disabled={twoFactorSaving}>
								{twoFactorSaving ? t('settings.twoFactor.settingUp') : t('settings.twoFactor.setup')}
							</button>
						</div>
					{/if}
				{:else if twoFactorStep === 'setup'}
					<p class="text-sm text-base-content/70">{t('settings.twoFactor.scan')}</p>

					{#if twoFactorSetup}
						<div class="flex flex-col items-center gap-4 p-4 bg-base-200/50 rounded-lg border border-base-300">
							<div class="bg-white p-3 rounded-lg">
								<img
									src={qrDataUrl}
									alt={t('settings.twoFactor.qrAlt')}
									width="200"
									height="200"
									class="block"
								/>
							</div>
							<div class="text-center max-w-full">
								<p class="text-xs text-base-content/70 mb-1">{t('settings.twoFactor.manualKey')}</p>
								<code class="text-sm font-mono bg-base-300 px-3 py-1.5 rounded select-all break-all">{twoFactorSetup.sharedKey}</code>
							</div>
						</div>

						<form class="flex gap-2 items-end flex-wrap" onsubmit={handleConfirm2FA}>
							<div>
								<label for="confirm2faCode" class="block text-sm font-medium text-base-content/80 mb-1.5">{t('settings.twoFactor.codeLabel')}</label>
								<input
									id="confirm2faCode"
									type="text"
									inputmode="numeric"
									autocomplete="one-time-code"
									bind:value={twoFactorConfirmCode}
									class="input input-sm w-[160px] text-center tracking-[0.2em] font-mono"
									placeholder="000000"
									maxlength="6"
									disabled={twoFactorSaving}
								/>
							</div>
							<button type="submit" class="btn btn-primary btn-sm" disabled={twoFactorSaving || twoFactorConfirmCode.length !== 6}>
								{twoFactorSaving ? t('settings.twoFactor.verifying') : t('settings.twoFactor.verify')}
							</button>
						</form>
					{/if}

					<div>
						<button type="button" class="btn btn-ghost btn-sm" onclick={cancelSetup2FA} disabled={twoFactorSaving}>{t('common.cancel')}</button>
					</div>
				{:else if twoFactorStep === 'backup'}
					<div class="alert alert-warning text-sm">
						<span>{t('settings.twoFactor.backupWarning')}</span>
					</div>
					<div class="grid grid-cols-2 gap-2 p-4 bg-base-200/50 rounded-lg border border-base-300 max-w-xs">
						{#each twoFactorBackupCodes as code}
							<code class="text-sm font-mono text-center py-1">{code}</code>
						{/each}
					</div>
					<div>
						<button type="button" class="btn btn-primary btn-sm" onclick={handleFinish2FASetup}>{t('settings.twoFactor.backupSaved')}</button>
					</div>
				{/if}
			</div>
		</section>

		<section class="card bg-base-100 border border-error/30 shadow-sm mb-5">
			<div class="card-body gap-4">
				<h2 class="card-title text-base">{t('settings.account.title')}</h2>
				<p class="text-sm text-base-content/70 m-0">{t('settings.account.intro')}</p>
				<div class="flex flex-wrap gap-3">
					<button type="button" class="btn btn-outline btn-sm" onclick={handleLogout}>{t('settings.account.signOut')}</button>
					<button type="button" class="btn btn-outline btn-sm" onclick={handleExportData} disabled={exportingData}>
						{#if exportingData}<span class="loading loading-spinner loading-xs"></span>{/if}
						{t('settings.account.export')}
					</button>
					<button type="button" class="btn btn-error btn-sm" onclick={openDeleteAccount}>{t('settings.account.delete')}</button>
				</div>
			</div>
		</section>

		<Modal open={deleteOpen} title={t('settings.account.deleteTitle')} onclose={() => (deleteOpen = false)}>
			<form id="deleteAccountForm" onsubmit={handleDeleteAccount} novalidate>
				<p class="text-sm text-base-content/70 mb-4">{t('settings.account.deleteMessage')}</p>
				{#if deleteError}<div class="alert alert-error text-sm mb-3" role="alert">{deleteError}</div>{/if}
				<label for="deleteAccountPw" class="block text-sm font-medium text-base-content/80 mb-1.5">{t('settings.account.deletePassword')}</label>
				<input id="deleteAccountPw" type="password" autocomplete="current-password" bind:value={deletePassword} class="input w-full" disabled={deleteAccountSaving} />
			</form>
			{#snippet actions()}
				<button type="button" class="btn btn-ghost" onclick={() => (deleteOpen = false)} disabled={deleteAccountSaving}>{t('common.cancel')}</button>
				<button type="submit" form="deleteAccountForm" class="btn btn-error" disabled={deleteAccountSaving || !deletePassword}>
					{deleteAccountSaving ? t('common.deleting') : t('settings.account.deleteConfirm')}
				</button>
			{/snippet}
		</Modal>
	{:else if activeTab === 'organization'}
		<section class="card bg-base-100 border border-base-300 shadow-sm mb-5">
			<div class="card-body">
				<h2 class="card-title text-base">{t('settings.org.title')}</h2>
				<p class="text-sm text-base-content/70 mb-2">{t('settings.org.intro')}</p>

				<div class="flex flex-col gap-2" role="radiogroup" aria-label={t('settings.org.title')}>
					<button
						type="button"
						role="radio"
						aria-checked={orgContext.selectedOrgId === null}
						class="flex items-center gap-3 border rounded-xl px-4 py-3 text-left w-full transition-colors hover:border-base-content/20 hover:bg-base-200 {orgContext.selectedOrgId === null ? 'border-primary bg-primary/5' : 'bg-base-200/50 border-base-300'}"
						onclick={() => orgContext.select(null)}
					>
						<input type="radio" class="radio radio-primary radio-sm pointer-events-none" checked={orgContext.selectedOrgId === null} tabindex="-1" aria-hidden="true" />
						<span class="text-sm font-medium">{t('settings.org.personal')}</span>
					</button>

					{#each orgContext.organizations as org (org.organizationId)}
						<button
							type="button"
							role="radio"
							aria-checked={orgContext.selectedOrgId === org.organizationId}
							class="flex items-center gap-3 border rounded-xl px-4 py-3 text-left w-full transition-colors hover:border-base-content/20 hover:bg-base-200 {orgContext.selectedOrgId === org.organizationId ? 'border-primary bg-primary/5' : 'bg-base-200/50 border-base-300'}"
							onclick={() => orgContext.select(org.organizationId ?? null)}
						>
							<input type="radio" class="radio radio-primary radio-sm pointer-events-none" checked={orgContext.selectedOrgId === org.organizationId} tabindex="-1" aria-hidden="true" />
							<span class="flex gap-2 items-center flex-wrap min-w-0">
								<span class="text-sm font-medium break-words">{org.name}</span>
								<span class="text-[0.6875rem] text-base-content/70 uppercase font-semibold tracking-wide">{roleLabel(org.role)}</span>
							</span>
						</button>
					{/each}

					{#if orgContext.organizations.length === 0 && !orgContext.loading}
						<p class="text-sm text-base-content/70">{t('settings.org.none')} <a href="/organizations/new" class="link link-primary">{t('settings.org.createOne')}</a></p>
					{/if}
				</div>

				<div class="flex gap-4 mt-4 pt-4 border-t border-base-200 flex-wrap">
					<a href="/organizations" class="text-sm link link-primary font-medium no-underline hover:underline">{t('settings.org.browse')}</a>
					<a href="/organizations/new" class="text-sm link link-primary font-medium no-underline hover:underline">{t('settings.org.create')}</a>
				</div>
			</div>
		</section>

		{#if orgContext.selectedOrgSlug}
			<p class="text-sm text-base-content/70 mb-3">{t('settings.org.scheduleIn')} <strong>{orgContext.selectedOrg?.name}</strong>:</p>
			{#if scheduleLoading && !currentSchedule}
				<p class="text-sm text-base-content/70 flex items-center gap-2 mb-5"><span class="loading loading-spinner loading-xs"></span> {t('common.loading')}</p>
			{:else if scheduleError}
				<div class="alert alert-error text-sm mb-5">{scheduleError}</div>
			{:else if currentSchedule}
				{#key orgContext.selectedOrgSlug}
					<WorkSchedulePlan
						class="mb-5"
						orgSlug={orgContext.selectedOrgSlug}
						mode={currentSchedule.workScheduleChangeMode}
						onchanged={() => orgContext.selectedOrgSlug && loadSchedule(orgContext.selectedOrgSlug)}
					/>
					<InitialOvertimeEditor
						class="mb-5"
						orgSlug={orgContext.selectedOrgSlug}
						hours={currentSchedule.initialOvertimeHours ?? 0}
						editable={currentSchedule.initialOvertimeMode === 'Allowed'}
						mode={currentSchedule.initialOvertimeMode}
						onsaved={() => orgContext.selectedOrgSlug && loadSchedule(orgContext.selectedOrgSlug)}
					/>
				{/key}
			{/if}
		{/if}
	{:else}
		<AppSettings />

		<section class="card bg-base-100 border border-base-300 shadow-sm mb-5">
			<div class="card-body">
				<h2 class="card-title text-base">{t('settings.appearance.theme')}</h2>
				<p class="text-sm text-base-content/70 mb-2">{t('settings.appearance.themeIntro')}</p>
				<div class="join">
					{#each [{ dark: false, label: t('settings.appearance.light') }, { dark: true, label: t('settings.appearance.dark') }] as opt}
						<button
							type="button"
							class="btn btn-sm join-item {theme.isDark === opt.dark ? 'btn-neutral' : 'btn-ghost border-base-300'}"
							onclick={() => theme.set(opt.dark)}
							aria-pressed={theme.isDark === opt.dark}
						>{opt.label}</button>
					{/each}
				</div>
			</div>
		</section>

		<section class="card bg-base-100 border border-base-300 shadow-sm mb-5">
			<div class="card-body">
				<h2 class="card-title text-base">{t('settings.appearance.language')}</h2>
				<p class="text-sm text-base-content/70 mb-2">{t('settings.appearance.languageIntro')}</p>
				<div class="join">
					{#each LOCALES as l}
						<button
							type="button"
							lang={l}
							class="btn btn-sm join-item {locale.current === l ? 'btn-neutral' : 'btn-ghost border-base-300'}"
							onclick={() => locale.set(l)}
							aria-pressed={locale.current === l}
						>{t(`settings.appearance.${l}`)}</button>
					{/each}
				</div>
			</div>
		</section>
	{/if}
</div>
