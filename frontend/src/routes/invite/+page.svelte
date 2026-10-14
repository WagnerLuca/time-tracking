<script lang="ts">
	import { onMount } from 'svelte';
	import { goto } from '$app/navigation';
	import { page } from '$app/state';
	import { invitationsApi } from '$lib/apiClient';
	import { auth } from '$lib/stores/auth.svelte';
	import { orgContext } from '$lib/stores/orgContext.svelte';
	import { toast } from '$lib/stores/toast.svelte';
	import { extractErrorMessage, getErrorCode } from '$lib/utils/errorHandler';
	import { withNext } from '$lib/utils/redirect';
	import { t } from '$lib/i18n';

	// Not a public route: signed-out visitors are sent to /login?next=/invite?token=… by the root
	// layout and come back here after signing in or registering.
	const token = page.url.searchParams.get('token');
	let error = $state('');
	let code = $state<string | null>(null);

	onMount(async () => {
		if (!token) {
			error = t('invitations.accept.missingToken');
			return;
		}
		try {
			const { data } = await invitationsApi.apiV1InvitationsAcceptPost({ token });
			if (auth.user?.id) await orgContext.loadOrganizations(auth.user.id);
			if (data.organizationId) orgContext.select(data.organizationId);
			toast.success(t('invitations.accept.joined', { org: data.organizationName ?? '' }));
			goto(data.organizationSlug ? `/organizations/${data.organizationSlug}` : '/organizations', { replaceState: true });
		} catch (err) {
			code = getErrorCode(err);
			error = extractErrorMessage(err, t('invitations.accept.failed'));
		}
	});

	async function switchAccount() {
		await auth.logout();
		goto(withNext('/login', `/invite?token=${encodeURIComponent(token ?? '')}`));
	}
</script>

<svelte:head>
	<title>{t('common.pageTitle', { page: t('invitations.accept.pageTitle') })}</title>
</svelte:head>

<div class="max-w-md mx-auto">
	<div class="card bg-base-100 border border-base-300 shadow-sm">
		<div class="card-body gap-4">
			<h1 class="text-xl font-bold">{t('invitations.accept.pageTitle')}</h1>
			{#if !error}
				<p class="text-base-content/70 flex items-center gap-2" role="status"><span class="loading loading-spinner loading-sm"></span> {t('invitations.accept.accepting')}</p>
			{:else}
				<div class="alert alert-error text-sm" role="alert">{error}</div>
				{#if code === 'invitation.wrongEmail'}
					<p class="text-sm text-base-content/70">{t('invitations.accept.signedInAs', { email: auth.user?.email ?? '' })}</p>
					<button type="button" class="btn btn-primary" onclick={switchAccount}>{t('invitations.accept.switchAccount')}</button>
				{:else if code === 'invitation.invalid'}
					<p class="text-sm text-base-content/70">{t('invitations.accept.askAgain')}</p>
				{/if}
				<a href="/organizations" class="btn btn-ghost">{t('invitations.accept.toOrgs')}</a>
			{/if}
		</div>
	</div>
</div>
