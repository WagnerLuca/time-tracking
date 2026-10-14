<script lang="ts">
	import { auth } from '$lib/stores/auth.svelte';
	import { goto } from '$app/navigation';
	import { toast } from '$lib/stores/toast.svelte';
	import { extractErrorMessage } from '$lib/utils/errorHandler';
	import { t } from '$lib/i18n';
	import AuthLanguageToggle from '$lib/components/AuthLanguageToggle.svelte';
	import { page } from '$app/state';
	import { safeNext, withNext } from '$lib/utils/redirect';

	/** Where to continue after registering (e.g. back to an invitation link). */
	const next = $derived(safeNext(page.url.searchParams.get('next')));

	let email = $state('');
	let password = $state('');
	let confirmPassword = $state('');
	let firstName = $state('');
	let lastName = $state('');
	let error = $state('');
	let submitting = $state(false);
	/** Inline errors show after the first submit attempt. */
	let attempted = $state(false);

	/** Mirrors the backend's RegisterRequest ([MinLength(8), MaxLength(128)]). */
	const MIN_PASSWORD = 8;
	const passwordTooShort = $derived(password.length < MIN_PASSWORD);
	const passwordMismatch = $derived((attempted || confirmPassword.length > 0) && password !== confirmPassword);

	const emailValid = $derived(/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email.trim()));
	const formValid = $derived(!!firstName.trim() && !!lastName.trim() && emailValid && !passwordTooShort && password === confirmPassword);

	async function handleRegister(e: Event) {
		e.preventDefault();
		error = '';
		attempted = true;
		if (!formValid) return;

		submitting = true;
		try {
			await auth.register({ email: email.trim(), password, firstName: firstName.trim(), lastName: lastName.trim() });
			toast.success(t('auth.register.verifySent'));
			goto(next ?? '/organizations');
		} catch (err) {
			error = extractErrorMessage(err, t('auth.register.failed'));
		} finally {
			submitting = false;
		}
	}
</script>

<svelte:head>
	<title>{t('common.pageTitle', { page: t('auth.register.pageTitle') })}</title>
</svelte:head>

<div class="min-h-screen flex flex-col items-center justify-center gap-4 bg-base-200 p-4">
	<AuthLanguageToggle />
	<div class="card bg-base-100 shadow-lg w-full max-w-[420px]">
		<div class="card-body p-6 sm:p-10">
			<h1 class="text-2xl font-bold mb-1">{t('auth.register.title')}</h1>
			<p class="text-base-content/70 mb-6">{t('auth.register.subtitle')}</p>
			{#if next?.startsWith('/invite')}
				<div class="alert alert-info text-sm mb-4" role="status">{t('auth.inviteHint')}</div>
			{/if}

			{#if error}
				<div class="alert alert-error text-sm mb-4" role="alert">{error}</div>
			{/if}

			<form onsubmit={handleRegister} novalidate>
				<div class="grid grid-cols-1 sm:grid-cols-2 gap-x-3">
					<div class="mb-5">
						<label for="firstName" class="block font-medium mb-1.5 text-base-content/80 text-sm">{t('auth.register.firstName')}</label>
						<input
							id="firstName"
							type="text"
							autocomplete="given-name"
							class="input w-full"
							class:input-error={attempted && !firstName.trim()}
							bind:value={firstName}
							placeholder={t('auth.register.firstNamePlaceholder')}
							required
							disabled={submitting}
						/>
					</div>
					<div class="mb-5">
						<label for="lastName" class="block font-medium mb-1.5 text-base-content/80 text-sm">{t('auth.register.lastName')}</label>
						<input
							id="lastName"
							type="text"
							autocomplete="family-name"
							class="input w-full"
							class:input-error={attempted && !lastName.trim()}
							bind:value={lastName}
							placeholder={t('auth.register.lastNamePlaceholder')}
							required
							disabled={submitting}
						/>
					</div>
				</div>

				<div class="mb-5">
					<label for="email" class="block font-medium mb-1.5 text-base-content/80 text-sm">{t('auth.email')}</label>
					<input
						id="email"
						type="email"
						autocomplete="email"
						class="input w-full"
						class:input-error={attempted && !emailValid}
						bind:value={email}
						placeholder={t('auth.emailPlaceholder')}
						required
						disabled={submitting}
					/>
				</div>

				<div class="mb-5">
					<label for="password" class="block font-medium mb-1.5 text-base-content/80 text-sm">{t('auth.password')}</label>
					<input
						id="password"
						type="password"
						autocomplete="new-password"
						class="input w-full"
						class:input-error={attempted && passwordTooShort}
						bind:value={password}
						placeholder="••••••••"
						required
						minlength={MIN_PASSWORD}
						aria-invalid={attempted && passwordTooShort}
						aria-describedby="passwordHint"
						disabled={submitting}
					/>
					<p id="passwordHint" class="text-xs mt-1 {attempted && passwordTooShort ? 'text-error' : 'text-base-content/70'}">
						{t('auth.register.passwordHint', { min: MIN_PASSWORD })}
					</p>
				</div>

				<div class="mb-5">
					<label for="confirmPassword" class="block font-medium mb-1.5 text-base-content/80 text-sm">{t('auth.register.confirmPassword')}</label>
					<input
						id="confirmPassword"
						type="password"
						autocomplete="new-password"
						class="input w-full"
						class:input-error={passwordMismatch}
						bind:value={confirmPassword}
						placeholder="••••••••"
						required
						aria-invalid={passwordMismatch}
						aria-describedby={passwordMismatch ? 'confirmError' : undefined}
						disabled={submitting}
					/>
					{#if passwordMismatch}
						<p id="confirmError" class="text-xs mt-1 text-error">{t('auth.register.mismatch')}</p>
					{/if}
				</div>

				<button type="submit" class="btn btn-primary w-full" disabled={submitting || (attempted && !formValid)}>
					{submitting ? t('auth.register.submitting') : t('auth.register.submit')}
				</button>
			</form>

			<p class="text-center mt-6 text-sm text-base-content/70">
				{t('auth.register.haveAccount')} <a href={withNext('/login', next)} class="link link-primary">{t('auth.register.login')}</a>
			</p>
		</div>
	</div>
</div>

