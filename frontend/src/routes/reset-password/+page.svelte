<script lang="ts">
	import { page } from '$app/state';
	import { authApi } from '$lib/apiClient';
	import { extractErrorMessage } from '$lib/utils/errorHandler';
	import { MIN_PASSWORD_LENGTH, newPasswordProblem } from '$lib/utils/passwordForm';
	import { t } from '$lib/i18n';
	import AuthLanguageToggle from '$lib/components/AuthLanguageToggle.svelte';

	const token = $derived(page.url.searchParams.get('token') ?? '');

	let password = $state('');
	let confirmPassword = $state('');
	let attempted = $state(false);
	let submitting = $state(false);
	let done = $state(false);
	let error = $state('');

	const problem = $derived(newPasswordProblem(password, confirmPassword));
	const problemText = $derived(
		!attempted || !problem ? '' : problem === 'tooShort' ? t('auth.register.passwordHint', { min: MIN_PASSWORD_LENGTH }) : t('auth.register.mismatch')
	);

	async function handleSubmit(e: Event) {
		e.preventDefault();
		attempted = true;
		if (problem) return;
		error = '';
		submitting = true;
		try {
			await authApi.apiV1AuthResetPasswordPost({ token, newPassword: password });
			done = true;
		} catch (err) {
			error = extractErrorMessage(err, t('auth.reset.failed'));
		} finally {
			submitting = false;
		}
	}
</script>

<svelte:head>
	<title>{t('common.pageTitle', { page: t('auth.reset.pageTitle') })}</title>
</svelte:head>

<div class="min-h-screen flex flex-col items-center justify-center gap-4 bg-base-200 p-4">
	<AuthLanguageToggle />
	<div class="card bg-base-100 shadow-lg w-full max-w-[420px]">
		<div class="card-body p-6 sm:p-10">
			<h1 class="text-2xl font-bold mb-1">{t('auth.reset.title')}</h1>
			{#if !token}
				<div class="alert alert-error text-sm my-4" role="alert">{t('auth.reset.missingToken')}</div>
				<a href="/forgot-password" class="btn btn-primary w-full">{t('auth.reset.requestNew')}</a>
			{:else if done}
				<div class="alert alert-success text-sm my-4" role="status">{t('auth.reset.done')}</div>
				<a href="/login" class="btn btn-primary w-full">{t('auth.reset.toLogin')}</a>
			{:else}
				<p class="text-base-content/60 mb-6">{t('auth.reset.subtitle')}</p>
				{#if error}
					<div class="alert alert-error text-sm mb-4" role="alert">
						<span>{error} <a href="/forgot-password" class="link">{t('auth.reset.requestNew')}</a></span>
					</div>
				{/if}
				<form onsubmit={handleSubmit} novalidate>
					<div class="mb-5">
						<label for="newPassword" class="block font-medium mb-1.5 text-base-content/80 text-sm">{t('auth.reset.newPassword')}</label>
						<input id="newPassword" type="password" autocomplete="new-password" class="input input-bordered w-full" class:input-error={attempted && problem === 'tooShort'} bind:value={password} aria-describedby="newPasswordHint" disabled={submitting} />
						<p id="newPasswordHint" class="text-xs mt-1 {problemText ? 'text-error' : 'text-base-content/50'}">{problemText || t('auth.register.passwordHint', { min: MIN_PASSWORD_LENGTH })}</p>
					</div>
					<div class="mb-5">
						<label for="confirmPassword" class="block font-medium mb-1.5 text-base-content/80 text-sm">{t('auth.register.confirmPassword')}</label>
						<input id="confirmPassword" type="password" autocomplete="new-password" class="input input-bordered w-full" class:input-error={attempted && problem === 'mismatch'} bind:value={confirmPassword} disabled={submitting} />
					</div>
					<button type="submit" class="btn btn-primary w-full" disabled={submitting}>
						{submitting ? t('auth.reset.submitting') : t('auth.reset.submit')}
					</button>
				</form>
				<p class="text-center mt-6 text-sm"><a href="/login" class="link link-primary">{t('auth.reset.toLogin')}</a></p>
			{/if}
		</div>
	</div>
</div>
