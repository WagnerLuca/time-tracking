<script lang="ts">
	import { onMount, untrack } from 'svelte';
	import { organizationsApi } from '$lib/apiClient';
	import type { TeamOverviewResponse } from '$lib/api';
	import { t } from '$lib/i18n';
	import { formatDelta, formatTime, getInitials } from '$lib/utils/formatters';
	import { getErrorStatus, extractErrorMessage } from '$lib/utils/errorHandler';
	import { TEAM_SEARCH_THRESHOLD, filterTeam, sortTeam } from '$lib/utils/team';
	import PresenceDot from '$lib/components/PresenceDot.svelte';

	/** Team cards for org admins/owners and group admins (issue #61); renders nothing for everyone else (403). */
	let { slug, available = $bindable(false) }: { slug: string; available?: boolean } = $props();

	let data = $state<TeamOverviewResponse | null>(null);
	let error = $state('');
	let groupId = $state<number | null>(null);
	let query = $state('');

	const members = $derived(sortTeam(data?.members ?? []));
	const shown = $derived(filterTeam(members, groupId, query));
	const presentCount = $derived(members.filter((m) => m.present === true).length);
	/** Members whose presence the caller may see (the count leaves out the others). */
	const presenceKnown = $derived(members.filter((m) => m.present != null).length);
	// Lets the dashboard switch to its two-column layout only when there is a team to show
	$effect(() => {
		available = !!data;
	});

	// Presence follows without reload: every minute while the tab is visible (like the bell), and on returning to it
	const POLL_MS = 60_000;
	let loadedAt = 0;
	let request = 0;

	async function load(s: string) {
		const mine = ++request;
		loadedAt = Date.now();
		try {
			const { data: d } = await organizationsApi.apiV1OrganizationsSlugTeamOverviewGet(s);
			if (mine !== request) return;
			data = d;
			error = '';
		} catch (err) {
			if (mine !== request) return;
			data = null;
			// Plain members lead nobody: no section, no error
			error = getErrorStatus(err) === 403 ? '' : extractErrorMessage(err, t('dashboard.team.loadFailed'));
		}
	}

	$effect(() => {
		const s = slug;
		untrack(() => {
			data = null;
			groupId = null;
			query = '';
			load(s);
		});
	});

	onMount(() => {
		const refresh = () => {
			if (document.visibilityState === 'visible' && (data || error) && Date.now() - loadedAt >= POLL_MS / 2) load(slug);
		};
		const interval = setInterval(refresh, POLL_MS);
		document.addEventListener('visibilitychange', refresh);
		return () => {
			clearInterval(interval);
			document.removeEventListener('visibilitychange', refresh);
		};
	});
</script>

{#if error}
	<div role="alert" class="alert alert-error mb-6 max-sm:alert-vertical">
		<span>{error}</span>
		<button type="button" class="btn btn-sm" onclick={() => load(slug)}>{t('common.retry')}</button>
	</div>
{:else if data}
	<section class="card bg-base-100 border border-base-300 p-4 sm:p-5 mb-6" aria-labelledby="team-heading">
		<div class="flex flex-wrap items-center justify-between gap-2 mb-3">
			<h2 id="team-heading" class="text-base font-semibold m-0">{t('dashboard.team.title')}</h2>
			{#if members.length > 0}
				{#if presenceKnown > 0}
					<span class="text-xs text-base-content/70">{t('dashboard.team.presentCount', { present: presentCount, total: presenceKnown })}</span>
				{/if}
			{/if}
		</div>

		{#if (data.groups?.length ?? 0) > 1 || members.length > TEAM_SEARCH_THRESHOLD}
			<div class="flex flex-wrap gap-2 mb-3">
				{#if (data.groups?.length ?? 0) > 1}
					<select class="select select-sm min-w-0 flex-1 sm:flex-none sm:w-48" aria-label={t('dashboard.team.group')} bind:value={groupId}>
						<option value={null}>{t('dashboard.team.allGroups')}</option>
						{#each data.groups ?? [] as g (g.id)}
							<option value={g.id}>{g.name}</option>
						{/each}
					</select>
				{/if}
				{#if members.length > TEAM_SEARCH_THRESHOLD}
					<input type="search" class="input input-sm min-w-0 flex-1" aria-label={t('dashboard.team.search')} placeholder={t('dashboard.team.searchPlaceholder')} bind:value={query} />
				{/if}
			</div>
		{/if}

		{#if members.length === 0}
			<p class="text-sm text-base-content/70 m-0">{t('dashboard.team.empty')}</p>
		{:else if shown.length === 0}
			<p class="text-sm text-base-content/70 m-0">{t('dashboard.team.noMatch')}</p>
		{:else}
			<ul class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-1 gap-2 list-none p-0 m-0">
				{#each shown as m (m.userId)}
					{@const balance = m.balanceHours != null ? Math.round(m.balanceHours * 60) : null}
					<li>
						<a
							href="/organizations/{slug}/members/{m.userId}"
							class="flex items-center gap-3 rounded-lg border p-3 no-underline text-base-content transition-colors hover:border-primary/40 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary {balance == null || balance === 0 ? 'border-base-300' : balance > 0 ? 'border-success/50 bg-success/5' : 'border-error/50 bg-error/5'}"
							data-testid="team-card"
						>
							<div class="relative shrink-0">
								{#if m.profileImageUrl}
									<img src={m.profileImageUrl} alt="" class="w-9 h-9 rounded-full object-cover" />
								{:else}
									<div class="w-9 h-9 rounded-full bg-gradient-to-br from-primary to-secondary text-primary-content flex items-center justify-center text-xs font-semibold" aria-hidden="true">
										{getInitials(m.firstName, m.lastName)}
									</div>
								{/if}
								<PresenceDot present={m.present} />
							</div>
							<div class="min-w-0 flex-1">
								<div class="flex items-center gap-1.5 font-semibold text-sm">
									<span class="truncate" title="{m.firstName} {m.lastName}">{m.firstName} {m.lastName}</span>
									{#if (m.failureCount ?? 0) > 0}
										<span class="badge badge-warning badge-sm shrink-0" title={t('dashboard.team.failures', { count: m.failureCount ?? 0 })} aria-label={t('dashboard.team.failures', { count: m.failureCount ?? 0 })}>!</span>
									{/if}
									{#if m.coreHoursViolationToday}
										<span class="badge badge-error badge-sm shrink-0" title={t('dashboard.team.coreHours')} aria-label={t('dashboard.team.coreHours')}>⚠</span>
									{/if}
								</div>
								{#if m.present != null}
									<div class="text-xs text-base-content/70 truncate">
										{m.present && m.presentSince ? t('dashboard.team.since', { time: formatTime(m.presentSince) }) : t('dashboard.team.notPresent')}
									</div>
								{:else if balance == null}
									<div class="text-xs text-base-content/60 truncate" data-testid="team-no-details">{t('dashboard.team.noDetails')}</div>
								{/if}
							</div>
							{#if balance != null}
								<div class="shrink-0 text-right">
									<div class="text-sm font-bold tabular-nums {balance > 0 ? 'text-success' : balance < 0 ? 'text-error' : ''}" data-testid="team-balance">{formatDelta(balance)}</div>
									<div class="text-[0.6875rem] text-base-content/70">{t('dashboard.team.balance')}</div>
								</div>
							{/if}
						</a>
					</li>
				{/each}
			</ul>
		{/if}
	</section>
{/if}
