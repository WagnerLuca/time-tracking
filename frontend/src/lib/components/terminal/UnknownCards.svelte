<script lang="ts">
	/**
	 * Cards scanned at a terminal in the last 7 days that belong to nobody (admins only).
	 * "Assign to member" adopts the scan as that member's card; the card value itself never
	 * reaches the browser, only its last characters.
	 */
	import { apiKeysApi } from '$lib/apiClient';
	import type { OrganizationMemberResponse, UnknownCardResponse } from '$lib/api';
	import { t, tp } from '$lib/i18n';
	import { extractErrorMessage } from '$lib/utils/errorHandler';
	import { formatTimeAgo } from '$lib/utils/formatters';
	import { toast } from '$lib/stores/toast.svelte';

	let { orgSlug, members, onassigned }: { orgSlug: string; members: OrganizationMemberResponse[]; onassigned?: () => void } = $props();

	let cards = $state<UnknownCardResponse[]>([]);
	let loading = $state(true);
	let error = $state('');
	let assigningId = $state<number | null>(null);
	let memberId = $state<number | null>(null);
	let label = $state('');
	let saving = $state(false);

	const sortedMembers = $derived(
		[...members].sort((a, b) => `${a.lastName} ${a.firstName}`.localeCompare(`${b.lastName} ${b.firstName}`))
	);

	$effect(() => {
		if (orgSlug) load();
	});

	export async function load() {
		loading = true;
		error = '';
		try {
			cards = (await apiKeysApi.apiV1OrganizationsSlugTerminalUnknownCardsGet(orgSlug)).data;
		} catch (err) {
			error = extractErrorMessage(err, t('orgSettings.terminal.loadFailed'));
		} finally {
			loading = false;
		}
	}

	function startAssign(card: UnknownCardResponse) {
		assigningId = card.punchId ?? null;
		memberId = null;
		label = '';
	}

	async function assign(e: SubmitEvent) {
		e.preventDefault();
		if (assigningId == null || memberId == null) return;
		saving = true;
		try {
			await apiKeysApi.apiV1OrganizationsSlugMembersMemberIdCardsPost(orgSlug, memberId, { fromPunchId: assigningId, label: label.trim() || null });
			toast.success(t('orgSettings.terminal.assigned'));
			assigningId = null;
			await load();
			onassigned?.();
		} catch (err) {
			toast.error(err, t('orgSettings.terminal.assignFailed'));
		} finally {
			saving = false;
		}
	}
</script>

<div class="mt-5">
	<h3 class="text-base font-bold text-base-content mb-1">{t('orgSettings.terminal.unknownTitle')}</h3>
	<p class="text-sm text-base-content/70 mb-2">{t('orgSettings.terminal.unknownIntro')}</p>
	<div class="card bg-base-100 border border-base-300">
		<div class="card-body p-4 gap-2">
			{#if error}
				<div class="alert alert-error text-sm" role="alert">{error}</div>
			{/if}
			{#if loading}
				<span class="loading loading-spinner loading-sm" aria-label={t('common.loading')}></span>
			{:else if cards.length === 0}
				<p class="text-sm text-base-content/70 m-0">{t('orgSettings.terminal.unknownEmpty')}</p>
			{:else}
				<ul class="divide-y divide-base-300" data-testid="unknown-cards">
					{#each cards as card (card.punchId)}
						<li class="py-2">
							<div class="flex items-center justify-between gap-3 flex-wrap">
								<div class="min-w-0">
									<code class="text-sm font-medium">…{card.cardHint}</code>
									<span class="text-xs text-base-content/70">
										· {tp('orgSettings.terminal.scans', card.count ?? 0)} · {t('orgSettings.terminal.lastSeen', { time: formatTimeAgo(card.lastSeenAt) })}
										{#if card.terminalName}· {t('orgSettings.terminal.atTerminal', { terminal: card.terminalName })}{/if}
									</span>
								</div>
								{#if assigningId !== card.punchId}
									<button type="button" class="btn btn-ghost btn-xs" onclick={() => startAssign(card)}>{t('orgSettings.terminal.assign')}</button>
								{/if}
							</div>
							{#if assigningId === card.punchId}
								<form class="flex flex-wrap items-end gap-2 mt-2" onsubmit={assign}>
									<label class="flex flex-col gap-1 text-xs text-base-content/70">
										{t('orgSettings.terminal.member')}
										<select class="select select-sm min-w-[12rem]" bind:value={memberId} disabled={saving} required>
											<option value={null} disabled>{t('orgSettings.terminal.chooseMember')}</option>
											{#each sortedMembers as m (m.id)}
												<option value={m.id}>{m.firstName} {m.lastName}</option>
											{/each}
										</select>
									</label>
									<label class="flex flex-col gap-1 text-xs text-base-content/70">
										{t('orgSettings.terminal.cardLabel')}
										<input class="input input-sm w-40" maxlength="100" bind:value={label} disabled={saving} />
									</label>
									<button type="submit" class="btn btn-primary btn-sm" disabled={saving || memberId == null}>{saving ? t('common.saving') : t('orgSettings.terminal.assign')}</button>
									<button type="button" class="btn btn-ghost btn-sm" onclick={() => (assigningId = null)} disabled={saving}>{t('common.cancel')}</button>
								</form>
							{/if}
						</li>
					{/each}
				</ul>
			{/if}
		</div>
	</div>
</div>
