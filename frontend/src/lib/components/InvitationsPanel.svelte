<script lang="ts">
	import { invitationsApi } from '$lib/apiClient';
	import { OrganizationRole, type InvitationResponse } from '$lib/api';
	import { toast } from '$lib/stores/toast.svelte';
	import { confirmDialog } from '$lib/stores/confirm.svelte';
	import { extractErrorMessage } from '$lib/utils/errorHandler';
	import { formatDate, roleLabel } from '$lib/utils/formatters';
	import { t } from '$lib/i18n';

	/** Invite people by email and list/revoke pending invitations (org admins). Only the owner may invite admins. */
	interface Props {
		orgSlug: string;
		isOwner: boolean;
	}
	let { orgSlug, isOwner }: Props = $props();

	let invitations = $state<InvitationResponse[]>([]);
	let loadError = $state('');
	let email = $state('');
	let role = $state<OrganizationRole>(OrganizationRole.NUMBER_0);
	let sending = $state(false);
	let formError = $state('');

	$effect(() => {
		if (orgSlug) load();
	});

	async function load() {
		loadError = '';
		try {
			const { data } = await invitationsApi.apiV1OrganizationsSlugInvitationsGet(orgSlug);
			invitations = data;
		} catch (err) {
			loadError = extractErrorMessage(err, t('invitations.loadFailed'));
		}
	}

	async function invite(e: Event) {
		e.preventDefault();
		const address = email.trim();
		if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(address)) {
			formError = t('invitations.emailInvalid');
			return;
		}
		sending = true;
		formError = '';
		try {
			await invitationsApi.apiV1OrganizationsSlugInvitationsPost(orgSlug, { email: address, role });
			toast.success(t('invitations.sent', { email: address }));
			email = '';
			role = OrganizationRole.NUMBER_0;
			await load();
		} catch (err) {
			formError = extractErrorMessage(err, t('invitations.sendFailed'));
		} finally {
			sending = false;
		}
	}

	async function revoke(inv: InvitationResponse) {
		if (inv.id == null) return;
		const ok = await confirmDialog({
			title: t('invitations.revokeTitle'),
			message: t('invitations.revokeMessage', { email: inv.email ?? '' }),
			confirmLabel: t('invitations.revoke'),
			danger: true
		});
		if (!ok) return;
		try {
			await invitationsApi.apiV1OrganizationsSlugInvitationsInvitationIdDelete(orgSlug, inv.id);
			invitations = invitations.filter((i) => i.id !== inv.id);
			toast.success(t('invitations.revoked'));
		} catch (err) {
			toast.error(err, t('invitations.revokeFailed'));
		}
	}
</script>

<section class="card bg-base-100 border border-base-300 shadow-sm mb-6">
	<div class="card-body p-5 gap-4">
		<h2 class="text-base font-semibold m-0">{t('invitations.title')}</h2>
		<p class="text-sm text-base-content/60 m-0">{t('invitations.intro')}</p>
		<form class="flex flex-col sm:flex-row gap-2 sm:items-end" onsubmit={invite} novalidate>
			<div class="flex-1 min-w-0">
				<label for="inviteEmail" class="block text-sm font-medium text-base-content/80 mb-1.5">{t('invitations.email')}</label>
				<input
					id="inviteEmail"
					type="email"
					autocomplete="off"
					class="input input-sm w-full"
					class:input-error={formError}
					aria-invalid={!!formError}
					aria-describedby={formError ? 'inviteError' : undefined}
					bind:value={email}
					disabled={sending}
				/>
			</div>
			<div>
				<label for="inviteRole" class="block text-sm font-medium text-base-content/80 mb-1.5">{t('invitations.role')}</label>
				<select id="inviteRole" class="select select-sm w-full sm:w-36" bind:value={role} disabled={sending}>
					<option value={OrganizationRole.NUMBER_0}>{roleLabel('Member')}</option>
					{#if isOwner}<option value={OrganizationRole.NUMBER_1}>{roleLabel('Admin')}</option>{/if}
				</select>
			</div>
			<button type="submit" class="btn btn-primary btn-sm" disabled={sending || !email.trim()}>
				{#if sending}<span class="loading loading-spinner loading-xs"></span>{/if}
				{t('invitations.send')}
			</button>
		</form>
		{#if formError}<p id="inviteError" class="text-error text-sm m-0" role="alert">{formError}</p>{/if}

		{#if loadError}
			<div class="alert alert-error text-sm">{loadError}</div>
		{:else if invitations.length > 0}
			<div>
				<h3 class="text-sm font-semibold text-base-content/70 mb-2">{t('invitations.pending', { count: invitations.length })}</h3>
				<ul class="flex flex-col gap-2 list-none p-0 m-0" data-testid="pending-invitations">
					{#each invitations as inv (inv.id)}
						<li class="flex items-center gap-3 flex-wrap p-2.5 rounded-lg bg-base-200/50 border border-base-300">
							<div class="flex-1 min-w-0">
								<div class="text-sm font-medium break-all">{inv.email}</div>
								<div class="text-xs text-base-content/60">
									{roleLabel(inv.role)} · {t('invitations.expires', { date: inv.expiresAt ? formatDate(inv.expiresAt) : '–' })}
								</div>
							</div>
							<button type="button" class="btn btn-ghost btn-xs text-error" onclick={() => revoke(inv)} aria-label={t('invitations.revokeLabel', { email: inv.email ?? '' })}>
								{t('invitations.revoke')}
							</button>
						</li>
					{/each}
				</ul>
			</div>
		{/if}
	</div>
</section>
