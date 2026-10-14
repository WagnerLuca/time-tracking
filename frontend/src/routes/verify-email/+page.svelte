<script lang="ts">
	import { onMount } from 'svelte';
	import { page } from '$app/state';
	import { authApi } from '$lib/apiClient';
	import { auth } from '$lib/stores/auth.svelte';
	import { extractErrorMessage } from '$lib/utils/errorHandler';
	import { t } from '$lib/i18n';
	import AuthLanguageToggle from '$lib/components/AuthLanguageToggle.svelte';

	let status = $state<'verifying' | 'success' | 'failed'>('verifying');
	let error = $state('');

	onMount(async () => {
		const token = page.url.searchParams.get('token');
		if (!token) {
			status = 'failed';
			error = t('auth.verify.missingToken');
			return;
		}
		try {
			await authApi.apiV1AuthVerifyEmailPost({ token });
			status = 'success';
			// Signed in in this browser? Pick up emailConfirmed / the changed address
			if (auth.isAuthenticated) await auth.fetchCurrentUser();
		} catch (err) {
			status = 'failed';
			error = extractErrorMessage(err, t('auth.verify.failed'));
		}
	});
</script>

<svelte:head>
	<title>{t('common.pageTitle', { page: t('auth.verify.pageTitle') })}</title>
</svelte:head>

<div class="min-h-screen flex flex-col items-center justify-center gap-4 bg-base-200 p-4">
	<AuthLanguageToggle />
	<div class="card bg-base-100 shadow-lg w-full max-w-[420px]">
		<div class="card-body p-6 sm:p-10">
			<h1 class="text-2xl font-bold mb-4">{t('auth.verify.pageTitle')}</h1>
			{#if status === 'verifying'}
				<p class="text-base-content/70 flex items-center gap-2" role="status"><span class="loading loading-spinner loading-sm"></span> {t('auth.verify.verifying')}</p>
			{:else if status === 'success'}
				<div class="alert alert-success text-sm mb-4" role="status">{t('auth.verify.success')}</div>
			{:else}
				<div class="alert alert-error text-sm mb-4" role="alert">{error}</div>
			{/if}
			{#if status !== 'verifying'}
				<a href={auth.isAuthenticated ? '/' : '/login'} class="btn btn-primary w-full">{auth.isAuthenticated ? t('auth.verify.toApp') : t('auth.verify.toLogin')}</a>
			{/if}
		</div>
	</div>
</div>
