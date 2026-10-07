<script lang="ts">
	import { authApi } from '$lib/apiClient';
	import { extractErrorMessage } from '$lib/utils/errorHandler';
	import { t } from '$lib/i18n';
	import AuthLanguageToggle from '$lib/components/AuthLanguageToggle.svelte';

	let email = $state('');
	let submitting = $state(false);
	let sent = $state(false);
	let error = $state('');

	async function handleSubmit(e: Event) {
		e.preventDefault();
		error = '';
		submitting = true;
		try {
			// The backend answers the same whether the address exists or not
			await authApi.apiV1AuthForgotPasswordPost({ email: email.trim() });
			sent = true;
		} catch (err) {
			error = extractErrorMessage(err, t('auth.forgot.failed'));
		} finally {
			submitting = false;
		}
	}
</script>

<svelte:head>
	<title>{t('common.pageTitle', { page: t('auth.forgot.pageTitle') })}</title>
</svelte:head>

<div class="min-h-screen flex flex-col items-center justify-center gap-4 bg-base-200 p-4">
	<AuthLanguageToggle />
	<div class="card bg-base-100 shadow-lg w-full max-w-[420px]">
		<div class="card-body p-6 sm:p-10">
			<h1 class="text-2xl font-bold mb-1">{t('auth.forgot.title')}</h1>
			{#if sent}
				<div class="alert alert-success text-sm my-4" role="status">{t('auth.forgot.sent')}</div>
			{:else}
				<p class="text-base-content/60 mb-6">{t('auth.forgot.subtitle')}</p>
				{#if error}
					<div class="alert alert-error text-sm mb-4" role="alert">{error}</div>
				{/if}
				<form onsubmit={handleSubmit}>
					<div class="mb-5">
						<label for="email" class="block font-medium mb-1.5 text-base-content/80 text-sm">{t('auth.email')}</label>
						<input id="email" type="email" autocomplete="email" class="input w-full" bind:value={email} placeholder={t('auth.emailPlaceholder')} required disabled={submitting} />
					</div>
					<button type="submit" class="btn btn-primary w-full" disabled={submitting || !email.trim()}>
						{submitting ? t('auth.forgot.submitting') : t('auth.forgot.submit')}
					</button>
				</form>
			{/if}
			<p class="text-center mt-6 text-sm"><a href="/login" class="link link-primary">{t('auth.forgot.back')}</a></p>
		</div>
	</div>
</div>
