<script lang="ts">
	/**
	 * Admin panel for organization terminal API keys (e.g. an RFID scanner).
	 * A key can only punch members in/out by badge ID via POST /api/v1/terminal/punch;
	 * the plaintext key is shown once, right after creation.
	 */
	import { apiKeysApi } from '$lib/apiClient';
	import { extractErrorMessage } from '$lib/utils/errorHandler';
	import { formatTimeAgo } from '$lib/utils/formatters';
	import { confirmDialog } from '$lib/stores/confirm.svelte';
	import { toast } from '$lib/stores/toast.svelte';
	import type { ApiKeyResponse } from '$lib/api';

	let { orgSlug }: { orgSlug: string } = $props();

	let keys = $state<ApiKeyResponse[]>([]);
	let loading = $state(true);
	let error = $state('');
	let newName = $state('');
	let creating = $state(false);
	let createdKey = $state<string | null>(null);
	let revokingId = $state<number | null>(null);
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
			error = extractErrorMessage(err, 'Failed to load API keys.');
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
			toast.success('API key created.');
			await load();
		} catch (err) {
			error = extractErrorMessage(err, 'Failed to create API key.');
		} finally {
			creating = false;
		}
	}

	async function revoke(key: ApiKeyResponse) {
		if (!key.id) return;
		const ok = await confirmDialog({
			title: `Revoke "${key.name}"?`,
			message: 'Terminals using it stop working immediately.',
			confirmLabel: 'Revoke',
			danger: true
		});
		if (!ok) return;
		revokingId = key.id;
		try {
			await apiKeysApi.apiV1OrganizationsSlugApiKeysKeyIdDelete(orgSlug, key.id);
			toast.success('API key revoked.');
			await load();
		} catch (err) {
			toast.error(err, 'Failed to revoke API key.');
		} finally {
			revokingId = null;
		}
	}

	async function copyKey() {
		try {
			await navigator.clipboard.writeText(createdKey ?? '');
			toast.success('Copied.');
		} catch (err) {
			toast.error(err, 'Could not copy. Select the key and copy it manually.');
		}
	}
</script>

<section class="mt-8">
	<h2 class="text-xl font-bold text-base-content mb-1">Terminal API keys</h2>
	<p class="text-sm text-base-content/60 mb-4 break-words">
		For devices such as an RFID scanner. A terminal sends the key in the <code>X-Api-Key</code> header to
		<code>POST /api/v1/terminal/punch</code> with a member's <code>badgeId</code>, which starts or stops that member's timer.
		Assign badge IDs on each member's page. A key can do nothing else.
	</p>

	{#if error}
		<div class="alert alert-error mb-3 text-sm" role="alert">{error}</div>
	{/if}

	{#if createdKey}
		<div class="alert alert-success mb-3 flex-col items-start gap-2 text-sm">
			<span class="font-semibold">Copy this key now. It will not be shown again.</span>
			<code class="w-full break-all select-all rounded bg-base-100 px-2 py-1 font-mono text-base-content">{createdKey}</code>
			<div class="flex gap-2">
				<button class="btn btn-xs" onclick={copyKey}>Copy</button>
				<button class="btn btn-ghost btn-xs" onclick={() => (createdKey = null)}>Done</button>
			</div>
		</div>
	{/if}

	<div class="card bg-base-100 border border-base-300">
		<div class="card-body p-4 gap-3">
			<form onsubmit={create} class="flex gap-2 flex-wrap">
				<label for="{fieldId}-name" class="sr-only">Key name</label>
				<input id="{fieldId}-name" class="input input-bordered input-sm flex-1 min-w-[12rem]" placeholder="Key name, e.g. Entrance scanner" maxlength="100" bind:value={newName} disabled={creating} />
				<button type="submit" class="btn btn-primary btn-sm" disabled={creating || !newName.trim()}>{creating ? 'Creating…' : 'Create key'}</button>
			</form>

			{#if loading}
				<span class="loading loading-spinner loading-sm"></span>
			{:else if keys.length === 0}
				<p class="text-sm text-base-content/50">No API keys yet.</p>
			{:else}
				<ul class="divide-y divide-base-300">
					{#each keys as key (key.id)}
						<li class="flex items-center justify-between gap-3 py-2">
							<div class="min-w-0">
								<div class="text-sm font-medium">{key.name}</div>
								<div class="text-xs text-base-content/50">
									<code>{key.keyPrefix}…</code> · created {formatTimeAgo(key.createdAt)} ·
									{key.lastUsedAt ? `last used ${formatTimeAgo(key.lastUsedAt)}` : 'never used'}
								</div>
							</div>
							<button class="btn btn-ghost btn-xs text-error" disabled={revokingId === key.id} onclick={() => revoke(key)}>{revokingId === key.id ? 'Revoking…' : 'Revoke'}</button>
						</li>
					{/each}
				</ul>
			{/if}
		</div>
	</div>
</section>
