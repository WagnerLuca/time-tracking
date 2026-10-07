<script lang="ts">
	/**
	 * Admin panel for the organization's terminals (e.g. an RFID scanner): API keys (the
	 * plaintext key is shown once, right after creation; the key's name is the terminal name),
	 * unknown scanned cards to assign, and the punch protocol. A key can only punch members
	 * in/out by card via POST /api/v1/terminal/punch (docs/TERMINAL.md).
	 */
	import { apiKeysApi } from '$lib/apiClient';
	import { t } from '$lib/i18n';
	import { extractErrorMessage } from '$lib/utils/errorHandler';
	import { formatTimeAgo } from '$lib/utils/formatters';
	import { confirmDialog } from '$lib/stores/confirm.svelte';
	import { toast } from '$lib/stores/toast.svelte';
	import type { ApiKeyResponse, OrganizationMemberResponse } from '$lib/api';
	import UnknownCards from './terminal/UnknownCards.svelte';
	import PunchLog from './terminal/PunchLog.svelte';

	let { orgSlug, members = [] }: { orgSlug: string; members?: OrganizationMemberResponse[] } = $props();

	let keys = $state<ApiKeyResponse[]>([]);
	let loading = $state(true);
	let error = $state('');
	let newName = $state('');
	let creating = $state(false);
	let createdKey = $state<string | null>(null);
	let revokingId = $state<number | null>(null);
	let renamingId = $state<number | null>(null);
	let renameValue = $state('');
	let renameSaving = $state(false);
	let punchLog = $state<ReturnType<typeof PunchLog>>();
	const fieldId = $props.id();

	$effect(() => {
		if (orgSlug) load();
	});

	async function load() {
		loading = true;
		error = '';
		try {
			const { data } = await apiKeysApi.apiV1OrganizationsSlugApiKeysGet(orgSlug);
			keys = data;
		} catch (err) {
			error = extractErrorMessage(err, t('orgSettings.apiKeys.loadFailed'));
		} finally {
			loading = false;
		}
	}

	async function create(e: SubmitEvent) {
		e.preventDefault();
		if (!newName.trim()) return;
		creating = true;
		error = '';
		try {
			const { data } = await apiKeysApi.apiV1OrganizationsSlugApiKeysPost(orgSlug, { name: newName.trim() });
			createdKey = data.key ?? null;
			newName = '';
			toast.success(t('orgSettings.apiKeys.created'));
			await load();
		} catch (err) {
			error = extractErrorMessage(err, t('orgSettings.apiKeys.createFailed'));
		} finally {
			creating = false;
		}
	}

	async function revoke(key: ApiKeyResponse) {
		if (!key.id) return;
		const ok = await confirmDialog({
			title: t('orgSettings.apiKeys.revokeTitle', { name: key.name ?? '' }),
			message: t('orgSettings.apiKeys.revokeMessage'),
			confirmLabel: t('orgSettings.apiKeys.revoke'),
			danger: true
		});
		if (!ok) return;
		revokingId = key.id;
		try {
			await apiKeysApi.apiV1OrganizationsSlugApiKeysKeyIdDelete(orgSlug, key.id);
			toast.success(t('orgSettings.apiKeys.revoked'));
			await load();
		} catch (err) {
			toast.error(err, t('orgSettings.apiKeys.revokeFailed'));
		} finally {
			revokingId = null;
		}
	}

	function startRename(key: ApiKeyResponse) {
		renamingId = key.id ?? null;
		renameValue = key.name ?? '';
	}

	async function saveRename(e: SubmitEvent) {
		e.preventDefault();
		if (renamingId == null || !renameValue.trim()) return;
		renameSaving = true;
		try {
			await apiKeysApi.apiV1OrganizationsSlugApiKeysKeyIdPut(orgSlug, renamingId, { name: renameValue.trim() });
			toast.success(t('orgSettings.apiKeys.renamed'));
			renamingId = null;
			await load();
			punchLog?.load();
		} catch (err) {
			toast.error(err, t('orgSettings.apiKeys.renameFailed'));
		} finally {
			renameSaving = false;
		}
	}

	async function copyKey() {
		try {
			await navigator.clipboard.writeText(createdKey ?? '');
			toast.success(t('common.copied'));
		} catch {
			// Clipboard API missing or denied (e.g. plain http): the key stays visible and selectable
			toast.error(t('orgSettings.apiKeys.copyFailed'));
		}
	}
</script>

<section class="mt-8">
	<h2 class="text-xl font-bold text-base-content mb-1">{t('orgSettings.apiKeys.title')}</h2>
	<p class="text-sm text-base-content/60 mb-1">{t('orgSettings.apiKeys.intro')}</p>
	<p class="text-xs text-base-content/60 mb-4 flex flex-wrap gap-x-3 gap-y-1">
		<span>{t('orgSettings.apiKeys.endpoint')}: <code class="break-all">POST /api/v1/terminal/punch</code></span>
		<span>{t('orgSettings.apiKeys.header')}: <code>X-Api-Key</code></span>
		<span>{t('orgSettings.apiKeys.field')}: <code>badgeId</code></span>
	</p>

	{#if error}
		<div class="alert alert-error mb-3 text-sm" role="alert">{error}</div>
	{/if}

	{#if createdKey}
		<div class="alert alert-success mb-3 flex-col items-start gap-2 text-sm">
			<span class="font-semibold">{t('orgSettings.apiKeys.copyNow')}</span>
			<code class="w-full break-all select-all rounded bg-base-100 px-2 py-1 font-mono text-base-content">{createdKey}</code>
			<div class="flex gap-2">
				<button class="btn btn-xs" onclick={copyKey}>{t('common.copy')}</button>
				<button class="btn btn-ghost btn-xs" onclick={() => (createdKey = null)}>{t('common.done')}</button>
			</div>
		</div>
	{/if}

	<div class="card bg-base-100 border border-base-300">
		<div class="card-body p-4 gap-3">
			<form onsubmit={create} class="flex gap-2 flex-wrap">
				<label for="{fieldId}-name" class="sr-only">{t('orgSettings.apiKeys.keyName')}</label>
				<input id="{fieldId}-name" class="input input-sm flex-1 min-w-[12rem]" placeholder={t('orgSettings.apiKeys.namePlaceholder')} maxlength="100" bind:value={newName} disabled={creating} />
				<button type="submit" class="btn btn-primary btn-sm" disabled={creating || !newName.trim()}>{creating ? t('common.creating') : t('orgSettings.apiKeys.create')}</button>
			</form>

			{#if loading}
				<span class="loading loading-spinner loading-sm" aria-label={t('common.loading')}></span>
			{:else if keys.length === 0}
				<p class="text-sm text-base-content/60">{t('orgSettings.apiKeys.empty')}</p>
			{:else}
				<ul class="divide-y divide-base-300">
					{#each keys as key (key.id)}
						<li class="flex items-center justify-between gap-3 py-2">
							<div class="min-w-0 flex-1">
								{#if renamingId === key.id}
									<form class="flex gap-2 flex-wrap" onsubmit={saveRename}>
										<label for="{fieldId}-rename-{key.id}" class="sr-only">{t('orgSettings.apiKeys.keyName')}</label>
										<input id="{fieldId}-rename-{key.id}" class="input input-xs flex-1 min-w-[10rem]" maxlength="100" bind:value={renameValue} disabled={renameSaving} />
										<button type="submit" class="btn btn-primary btn-xs" disabled={renameSaving || !renameValue.trim()}>{renameSaving ? t('common.saving') : t('common.save')}</button>
										<button type="button" class="btn btn-ghost btn-xs" onclick={() => (renamingId = null)} disabled={renameSaving}>{t('common.cancel')}</button>
									</form>
								{:else}
									<div class="text-sm font-medium break-words">{key.name}</div>
								{/if}
								<div class="text-xs text-base-content/60">
									<code>{key.keyPrefix}…</code> · {t('orgSettings.apiKeys.createdAgo', { time: formatTimeAgo(key.createdAt) })} ·
									{key.lastUsedAt ? t('orgSettings.apiKeys.lastUsed', { time: formatTimeAgo(key.lastUsedAt) }) : t('orgSettings.apiKeys.neverUsed')}
								</div>
							</div>
							{#if renamingId !== key.id}
								<button class="btn btn-ghost btn-xs shrink-0" onclick={() => startRename(key)}>{t('orgSettings.apiKeys.rename')}</button>
							{/if}
							<button class="btn btn-ghost btn-xs text-error shrink-0" disabled={revokingId === key.id} onclick={() => revoke(key)}>{revokingId === key.id ? t('orgSettings.apiKeys.revoking') : t('orgSettings.apiKeys.revoke')}</button>
						</li>
					{/each}
				</ul>
			{/if}
		</div>
	</div>

	<UnknownCards {orgSlug} {members} onassigned={() => punchLog?.load()} />
	<PunchLog bind:this={punchLog} {orgSlug} />
</section>
