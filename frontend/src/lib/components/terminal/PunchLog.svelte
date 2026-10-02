<script lang="ts">
	/** Punch protocol of the organization's terminals, newest first (admins only). */
	import { apiKeysApi } from '$lib/apiClient';
	import type { TerminalPunchResponse } from '$lib/api';
	import { t } from '$lib/i18n';
	import { extractErrorMessage } from '$lib/utils/errorHandler';
	import { formatDateTime, formatTimeAgo } from '$lib/utils/formatters';
	import { isQueuedPunch, punchResultClass, punchResultLabel } from '$lib/utils/terminal';

	let { orgSlug }: { orgSlug: string } = $props();

	let punches = $state<TerminalPunchResponse[]>([]);
	let loading = $state(true);
	let error = $state('');

	$effect(() => {
		if (orgSlug) load();
	});

	export async function load() {
		loading = true;
		error = '';
		try {
			punches = (await apiKeysApi.apiV1OrganizationsSlugTerminalPunchesGet(orgSlug, undefined, undefined, 100)).data;
		} catch (err) {
			error = extractErrorMessage(err, t('orgSettings.terminal.loadFailed'));
		} finally {
			loading = false;
		}
	}
</script>

<div class="mt-5">
	<div class="flex items-center justify-between gap-2 mb-1">
		<h3 class="text-base font-bold text-base-content m-0">{t('orgSettings.terminal.punchesTitle')}</h3>
		<button type="button" class="btn btn-ghost btn-xs" onclick={load} disabled={loading}>{t('orgSettings.terminal.refresh')}</button>
	</div>
	<p class="text-sm text-base-content/60 mb-2">{t('orgSettings.terminal.punchesIntro')}</p>
	<div class="card bg-base-100 border border-base-300">
		<div class="card-body p-0">
			{#if error}
				<div class="alert alert-error text-sm m-4" role="alert">{error}</div>
			{/if}
			{#if loading && punches.length === 0}
				<span class="loading loading-spinner loading-sm m-4" aria-label={t('common.loading')}></span>
			{:else if punches.length === 0}
				<p class="text-sm text-base-content/60 m-4">{t('orgSettings.terminal.punchesEmpty')}</p>
			{:else}
				<div class="overflow-x-auto max-h-96">
					<table class="table table-sm table-pin-rows" data-testid="punch-log">
						<thead>
							<tr>
								<th>{t('orgSettings.terminal.colTime')}</th>
								<th>{t('orgSettings.terminal.colTerminal')}</th>
								<th>{t('orgSettings.terminal.colCard')}</th>
								<th>{t('orgSettings.terminal.colMember')}</th>
								<th>{t('orgSettings.terminal.colResult')}</th>
							</tr>
						</thead>
						<tbody>
							{#each punches as p (p.id)}
								<tr>
									<td class="whitespace-nowrap tabular-nums">
										{formatDateTime(p.scannedAt!)}
										{#if isQueuedPunch(p)}<span class="block text-xs text-base-content/50">{t('orgSettings.terminal.queued', { time: formatTimeAgo(p.receivedAt) })}</span>{/if}
									</td>
									<td>{p.terminalName ?? t('orgSettings.terminal.deletedTerminal')}</td>
									<td><code>…{p.cardHint}</code>{#if p.cardLabel}<span class="text-xs text-base-content/60"> {p.cardLabel}</span>{/if}</td>
									<td>{p.memberName ?? '—'}</td>
									<td><span class="badge badge-sm {punchResultClass(p.result)}">{punchResultLabel(p.result)}</span></td>
								</tr>
							{/each}
						</tbody>
					</table>
				</div>
			{/if}
		</div>
	</div>
</div>
