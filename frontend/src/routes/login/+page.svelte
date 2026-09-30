<script lang="ts">
	import { auth } from '$lib/stores/auth.svelte';
	import { orgContext } from '$lib/stores/orgContext.svelte';
	import { goto } from '$app/navigation';
	import { page } from '$app/state';
	import { extractErrorMessage } from '$lib/utils/errorHandler';
	import { t } from '$lib/i18n';
	import AuthLanguageToggle from '$lib/components/AuthLanguageToggle.svelte';

	let email = $state('');
	let password = $state('');
	let error = $state('');
	let submitting = $state(false);

	// 2FA state
	let twoFactorCode = $state('');
	let codeInput = $state<HTMLInputElement>();

	// Focus the code input as soon as the 2FA step appears
	$effect(() => {
		if (auth.twoFactorRequired) codeInput?.focus();
	});

	/** Where to go after login: a same-origin path from ?next= (set by the 401 handler), else the default flow. */
	const next = $derived.by(() => {
		const value = page.url.searchParams.get('next');
		return value && value.startsWith('/') && !value.startsWith('//') && !value.startsWith('/\\') && !value.startsWith('/login') ? value : null;
	});

	async function navigateAfterLogin() {
		if (auth.user?.id) {
			await orgContext.loadOrganizations(auth.user.id);
		}
		if (next) {
			goto(next);
		} else if (orgContext.selectedOrgId) {
			goto('/');
		} else if (orgContext.organizations.length > 0) {
			goto('/select-org');
		} else {
			goto('/organizations');
		}
	}

	async function handleLogin(e: Event) {
		e.preventDefault();
		error = '';
		submitting = true;
		try {
			const result = await auth.login({ email, password });
			if (result === '2fa') return; // 2FA step — stay on page, show code input
			await navigateAfterLogin();
		} catch (err) {
			error = extractErrorMessage(err, t('auth.login.failed'));
		} finally {
			submitting = false;
		}
	}

	async function handleTwoFactor(e: Event) {
		e.preventDefault();
		error = '';
		submitting = true;
		try {
			await auth.verifyTwoFactor(twoFactorCode.replace(/\s/g, ''));
			await navigateAfterLogin();
		} catch (err) {
			error = extractErrorMessage(err, t('auth.twoFactor.failed'));
		} finally {
			submitting = false;
		}
	}

	function cancelTwoFactor() {
		auth.clearTwoFactor();
		twoFactorCode = '';
		error = '';
	}
</script>

<svelte:head>
	<title>{t('common.pageTitle', { page: auth.twoFactorRequired ? t('auth.twoFactor.title') : t('auth.login.pageTitle') })}</title>
</svelte:head>

<div class="min-h-screen flex flex-col items-center justify-center gap-4 bg-base-200 p-4">
	<AuthLanguageToggle />
	<div class="card bg-base-100 shadow-lg w-full max-w-[420px]">
		<div class="card-body p-6 sm:p-10">
			{#if auth.twoFactorRequired}
				<h1 class="text-2xl font-bold mb-1">{t('auth.twoFactor.title')}</h1>
				<p class="text-base-content/60 mb-6">{t('auth.twoFactor.subtitle')}</p>

				{#if error}
					<div id="loginError" class="alert alert-error text-sm mb-4" role="alert">{error}</div>
				{/if}

				<form onsubmit={handleTwoFactor}>
					<div class="mb-5">
						<label for="twoFactorCode" class="block font-medium mb-1.5 text-base-content/80 text-sm">{t('auth.twoFactor.code')}</label>
						<input
							bind:this={codeInput}
							id="twoFactorCode"
							type="text"
							inputmode="numeric"
							autocomplete="one-time-code"
							class="input input-bordered w-full text-center text-2xl tracking-[0.3em] font-mono"
							bind:value={twoFactorCode}
							placeholder="000000"
							maxlength="10"
							required
							aria-invalid={!!error}
							aria-describedby="twoFactorHint{error ? ' loginError' : ''}"
							disabled={submitting}
						/>
						<p id="twoFactorHint" class="text-xs text-base-content/60 mt-2">{t('auth.twoFactor.backupHint')}</p>
					</div>

					<button type="submit" class="btn btn-primary w-full" disabled={submitting || !twoFactorCode}>
						{submitting ? t('auth.twoFactor.verifying') : t('auth.twoFactor.verify')}
					</button>
				</form>

				<button type="button" class="btn btn-ghost btn-sm mt-3 w-full" onclick={cancelTwoFactor} disabled={submitting}>
					{t('auth.twoFactor.back')}
				</button>
			{:else}
				<h1 class="text-2xl font-bold mb-1">{t('auth.login.title')}</h1>
				<p class="text-base-content/60 mb-6">{t('auth.login.subtitle')}</p>

				{#if error}
					<div id="loginError" class="alert alert-error text-sm mb-4" role="alert">{error}</div>
				{/if}

				<form onsubmit={handleLogin}>
					<div class="mb-5">
						<label for="email" class="block font-medium mb-1.5 text-base-content/80 text-sm">{t('auth.email')}</label>
						<input
							id="email"
							type="email"
							autocomplete="email"
							class="input input-bordered w-full"
							bind:value={email}
							placeholder={t('auth.emailPlaceholder')}
							required
							disabled={submitting}
						/>
					</div>

					<div class="mb-5">
						<!-- TODO(backend): link "Passwort vergessen?" to the password-reset flow once the endpoints exist -->
						<label for="password" class="block font-medium mb-1.5 text-base-content/80 text-sm">{t('auth.password')}</label>
						<input
							id="password"
							type="password"
							autocomplete="current-password"
							class="input input-bordered w-full"
							bind:value={password}
							placeholder="••••••••"
							required
							disabled={submitting}
						/>
					</div>

					<button type="submit" class="btn btn-primary w-full" disabled={submitting || !email || !password}>
						{submitting ? t('auth.login.submitting') : t('auth.login.submit')}
					</button>
				</form>

				<p class="text-center mt-6 text-sm text-base-content/60">
					{t('auth.login.noAccount')} <a href="/register" class="link link-primary">{t('auth.login.register')}</a>
				</p>
			{/if}
		</div>
	</div>
</div>

