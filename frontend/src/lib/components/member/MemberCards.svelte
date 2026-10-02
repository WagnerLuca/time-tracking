<script lang="ts">
	import { apiKeysApi } from '$lib/apiClient';
	import type { BadgeCardResponse } from '$lib/api';
	import { extractErrorMessage } from '$lib/utils/errorHandler';
	import { formatTimeAgo } from '$lib/utils/formatters';
	import { confirmDialog } from '$lib/stores/confirm.svelte';
	import { toast } from '$lib/stores/toast.svelte';
	import { t } from '$lib/i18n';

	/** Terminal cards (RFID tags) of a member: add by ID, label, block, remove — admins only. */
	interface Props {
		orgSlug: string;
		memberId: number;
		onchanged?: () => void;
	}
	let { orgSlug, memberId, onchanged }: Props = $props();

	let cards = $state<BadgeCardResponse[]>([]);
	let loading = $state(true);
	let error = $state('');
	let newCardId = $state('');
	let newLabel = $state('');
	let adding = $state(false);
	let busyId = $state<number | null>(null);
	let editingId = $state<number | null>(null);
	let editLabel = $state('');
	const fieldId = $props.id();

	$effect(() => {
		if (orgSlug && memberId) load();
	});

	async function load() {
		loading = true;
		error = '';
		try {
			cards = (await apiKeysApi.apiV1OrganizationsSlugMembersMemberIdCardsGet(orgSlug, memberId)).data;
		} catch (err) {
			error = extractErrorMessage(err, t('memberDetail.cards.loadFailed'));
		} finally {
			loading = false;
		}
	}

	async function add(e: SubmitEvent) {
		e.preventDefault();
		if (!newCardId.trim()) return;
		adding = true;
		error = '';
		try {
			await apiKeysApi.apiV1OrganizationsSlugMembersMemberIdCardsPost(orgSlug, memberId, { cardId: newCardId.trim(), label: newLabel.trim() || null });
			toast.success(t('memberDetail.cards.added'));
			newCardId = '';
			newLabel = '';
			await load();
			onchanged?.();
		} catch (err) {
			error = extractErrorMessage(err, t('memberDetail.cards.addFailed'));
		} finally {
			adding = false;
		}
	}

	async function update(card: BadgeCardResponse, patch: { label?: string; isBlocked?: boolean }, success: string) {
		busyId = card.id!;
		try {
			await apiKeysApi.apiV1OrganizationsSlugMembersMemberIdCardsCardIdPut(orgSlug, memberId, card.id!, patch);
			toast.success(success);
			editingId = null;
			await load();
		} catch (err) {
			toast.error(err, t('memberDetail.cards.updateFailed'));
		} finally {
			busyId = null;
		}
	}

	async function remove(card: BadgeCardResponse) {
		const ok = await confirmDialog({
			title: t('memberDetail.cards.removeTitle', { hint: card.cardHint ?? '' }),
			message: t('memberDetail.cards.removeMessage'),
			confirmLabel: t('memberDetail.cards.remove'),
			danger: true
		});
		if (!ok) return;
		busyId = card.id!;
		try {
			await apiKeysApi.apiV1OrganizationsSlugMembersMemberIdCardsCardIdDelete(orgSlug, memberId, card.id!);
			toast.success(t('memberDetail.cards.removed'));
			await load();
			onchanged?.();
		} catch (err) {
			toast.error(err, t('memberDetail.cards.updateFailed'));
		} finally {
			busyId = null;
		}
	}
</script>

<section class="card bg-base-100 border border-base-300 p-5 mb-5">
	<h2 class="text-base font-bold text-base-content m-0 mb-2">{t('memberDetail.cards.title')}</h2>
	{#if error}<div class="alert alert-error text-sm py-2 mb-2" role="alert">{error}</div>{/if}

	{#if loading}
		<span class="loading loading-spinner loading-sm" aria-label={t('common.loading')}></span>
	{:else if cards.length === 0}
		<p class="text-sm text-base-content/60 m-0">{t('memberDetail.cards.empty')}</p>
	{:else}
		<ul class="divide-y divide-base-300 m-0 p-0" data-testid="member-cards">
			{#each cards as card (card.id)}
				<li class="flex items-center justify-between gap-3 py-2 flex-wrap">
					<div class="min-w-0 flex-1">
						{#if editingId === card.id}
							<form class="flex gap-2 flex-wrap" onsubmit={(e) => { e.preventDefault(); update(card, { label: editLabel }, t('memberDetail.cards.labelSaved')); }}>
								<label for="{fieldId}-label-{card.id}" class="sr-only">{t('memberDetail.cards.label')}</label>
								<input id="{fieldId}-label-{card.id}" class="input input-bordered input-xs w-48" maxlength="100" bind:value={editLabel} disabled={busyId === card.id} />
								<button type="submit" class="btn btn-primary btn-xs" disabled={busyId === card.id}>{t('common.save')}</button>
								<button type="button" class="btn btn-ghost btn-xs" onclick={() => (editingId = null)}>{t('common.cancel')}</button>
							</form>
						{:else}
							<div class="text-sm">
								<code class="font-medium">…{card.cardHint}</code>
								{#if card.label}<span class="text-base-content/80"> {card.label}</span>{/if}
								{#if card.isBlocked}<span class="badge badge-error badge-sm ml-1">{t('memberDetail.cards.blocked')}</span>{/if}
							</div>
							<div class="text-xs text-base-content/60">
								{card.lastUsedAt ? t('memberDetail.cards.lastUsed', { time: formatTimeAgo(card.lastUsedAt) }) : t('memberDetail.cards.neverUsed')}
							</div>
						{/if}
					</div>
					{#if editingId !== card.id}
						<div class="flex gap-1">
							<button type="button" class="btn btn-ghost btn-xs" disabled={busyId === card.id} onclick={() => { editingId = card.id!; editLabel = card.label ?? ''; }}>{t('memberDetail.cards.editLabel')}</button>
							<button type="button" class="btn btn-ghost btn-xs" disabled={busyId === card.id}
								onclick={() => update(card, { isBlocked: !card.isBlocked }, card.isBlocked ? t('memberDetail.cards.unblockedToast') : t('memberDetail.cards.blockedToast'))}>
								{card.isBlocked ? t('memberDetail.cards.unblock') : t('memberDetail.cards.block')}
							</button>
							<button type="button" class="btn btn-ghost btn-xs text-error" disabled={busyId === card.id} onclick={() => remove(card)}>{t('memberDetail.cards.remove')}</button>
						</div>
					{/if}
				</li>
			{/each}
		</ul>
	{/if}

	<form onsubmit={add} class="flex items-end gap-2 flex-wrap mt-3">
		<label class="flex flex-col gap-1 text-xs text-base-content/70">
			{t('memberDetail.cards.cardId')}
			<input id="{fieldId}-card" bind:value={newCardId} maxlength="100" placeholder={t('memberDetail.cards.cardIdPlaceholder')} class="input input-bordered input-sm font-mono w-48" disabled={adding} autocomplete="off" />
		</label>
		<label class="flex flex-col gap-1 text-xs text-base-content/70">
			{t('memberDetail.cards.label')}
			<input bind:value={newLabel} maxlength="100" placeholder={t('memberDetail.cards.labelPlaceholder')} class="input input-bordered input-sm w-44" disabled={adding} />
		</label>
		<button type="submit" class="btn btn-primary btn-sm" disabled={adding || !newCardId.trim()}>{adding ? t('common.adding') : t('memberDetail.cards.add')}</button>
	</form>
	<p class="text-base-content/50 text-xs mt-2 mb-0">{t('memberDetail.cards.hint')}</p>
</section>
