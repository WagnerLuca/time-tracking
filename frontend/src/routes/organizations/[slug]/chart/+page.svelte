<script lang="ts">
	import { onMount, tick, untrack } from 'svelte';
	import { page } from '$app/state';
	import { organizationsApi, groupApi, reportsApi } from '$lib/apiClient';
	import { auth } from '$lib/stores/auth.svelte';
	import { getOrgDetail } from '$lib/stores/orgDetail';
	import { t, tp } from '$lib/i18n';
	import { extractErrorMessage } from '$lib/utils/errorHandler';
	import { formatDelta, formatTime, getInitials, roleLabel } from '$lib/utils/formatters';
	import { safeColor } from '$lib/utils/colors';
	import { ruleModeLabel } from '$lib/utils/orgRules';
	import { canSee } from '$lib/utils/visibility';
	import {
		ROOT_KEY, buildOrgChart, directPeople, fullName, groupLeads, groupRules, indexChart, initialExpanded,
		personGroups, searchChart, treeKey, visibleNodes, type BranchNode, type ChartNode, type GroupRule, type PersonNode
	} from '$lib/utils/orgChart';
	import Skeleton from '$lib/components/Skeleton.svelte';
	import Modal from '$lib/components/Modal.svelte';
	import PresenceDot from '$lib/components/PresenceDot.svelte';
	import type { GroupPermissionsResponse, OrgChartResponse, OrganizationDetailResponse } from '$lib/api';

	const slug = $derived(page.params.slug ?? '');

	let chart = $state<OrgChartResponse | null>(null);
	let loading = $state(true);
	let error = $state('');

	const root = $derived(chart ? buildOrgChart(chart) : null);
	const index = $derived(root ? indexChart(root) : null);
	let expanded = $state(new Set<string>());
	const flat = $derived(root ? visibleNodes(root, expanded) : []);

	/** The tree item that takes Tab (roving tabindex), and the node whose details are open. */
	let activeKey = $state(ROOT_KEY);
	let selectedKey = $state<string | null>(null);
	const selected = $derived(selectedKey ? (index?.byKey.get(selectedKey) ?? null) : null);

	let query = $state('');
	const search = $derived(root ? searchChart(root, query) : { matches: [], expand: new Set<string>() });
	const matchSet = $derived(new Set(search.matches));
	let matchIndex = $state(0);

	// Desktop draws the tree (and a side panel), phones get the indented list (and a bottom sheet)
	let desktop = $state(true);
	onMount(() => {
		const mq = window.matchMedia('(min-width: 768px)');
		desktop = mq.matches;
		const change = () => (desktop = mq.matches);
		mq.addEventListener('change', change);
		return () => mq.removeEventListener('change', change);
	});

	$effect(() => {
		if (!slug) return;
		untrack(() => load(slug));
	});

	async function load(s: string) {
		loading = true;
		error = '';
		chart = null;
		selectedKey = null;
		query = '';
		perms = null;
		try {
			const { data } = await organizationsApi.apiV1OrganizationsSlugOrgChartGet(s);
			if (s !== slug) return;
			chart = data;
			expanded = initialExpanded(buildOrgChart(data));
			activeKey = ROOT_KEY;
			fitWidthOnce = true;
		} catch (err) {
			error = extractErrorMessage(err, t('orgChart.loadFailed'));
		} finally {
			loading = false;
		}
	}

	// Typing a search opens the path to every match and brings the first one into view
	$effect(() => {
		const found = search;
		untrack(() => {
			matchIndex = 0;
			if (!found.matches.length) return;
			expanded = new Set([...expanded, ...found.expand]);
			reveal(found.matches[0], false);
		});
	});

	const domId = (key: string) => `oc-${key}`;

	async function reveal(key: string, focus: boolean) {
		await tick();
		const el = document.getElementById(domId(key));
		el?.scrollIntoView({ block: 'nearest', inline: 'nearest' });
		if (focus) el?.focus({ preventScroll: true });
	}

	function nextMatch() {
		if (!search.matches.length) return;
		const key = search.matches[matchIndex % search.matches.length];
		matchIndex = (matchIndex + 1) % search.matches.length;
		activeKey = key;
		reveal(key, true);
	}

	function toggle(key: string) {
		const next = new Set(expanded);
		if (next.has(key)) next.delete(key);
		else next.add(key);
		expanded = next;
	}

	function setAll(open: boolean) {
		if (!root) return;
		expanded = open ? initialExpanded(root, Infinity) : new Set([ROOT_KEY]);
		if (!open && activeKey !== ROOT_KEY && !index?.parent.get(activeKey)?.startsWith(ROOT_KEY)) activeKey = ROOT_KEY;
	}

	function focusNode(key: string) {
		activeKey = key;
		reveal(key, true);
	}

	/** Click / Enter: groups and people open their details; the organization itself just opens or closes. */
	function activate(key: string) {
		activeKey = key;
		const node = index?.byKey.get(key);
		if (!node) return;
		if (node.kind === 'org') {
			toggle(key);
			return;
		}
		selectedKey = key;
		if (node.kind === 'group') loadRules();
		if (node.kind === 'person') loadBalance(node);
		// The side panel takes the focus so screen readers announce it; Esc brings it back
		if (desktop) tick().then(() => panel?.focus());
	}

	function closePanel() {
		if (!selectedKey) return;
		selectedKey = null;
		reveal(activeKey, true);
	}

	function onItemKey(e: KeyboardEvent, key: string) {
		if (e.target !== e.currentTarget) return;
		if (e.key === 'Escape' && selectedKey) {
			e.preventDefault();
			closePanel();
			return;
		}
		const r = treeKey(e.key, key, flat, expanded);
		if (!r) return;
		e.preventDefault();
		if (r.toggle) toggle(r.toggle);
		if (r.expandSiblings) expanded = new Set([...expanded, ...r.expandSiblings]);
		if (r.open) activate(r.open);
		if (r.focus) focusNode(r.focus);
	}

	function onItemClick(e: MouseEvent, key: string) {
		e.stopPropagation();
		if ((e.target as Element).closest('[data-toggle]')) {
			activeKey = key;
			toggle(key);
			return;
		}
		activate(key);
	}

	function onWindowKey(e: KeyboardEvent) {
		// The bottom sheet (Modal) closes itself on Esc; the side panel is not modal
		if (e.key === 'Escape' && selectedKey && desktop && !e.defaultPrevented) closePanel();
	}

	// ── Zoom (desktop drawing) ──
	let canvas = $state<HTMLDivElement>();
	let drawing = $state<HTMLUListElement>();
	let zoom = $state(1);
	const clampZoom = (z: number) => Math.min(1.5, Math.max(0.3, Math.round(z * 100) / 100));

	// A drawing a little wider than the canvas starts zoomed out to fit its width (not below 75 %)
	let fitWidthOnce = false;
	$effect(() => {
		if (!drawing || !canvas || !desktop || !fitWidthOnce) return;
		fitWidthOnce = false;
		untrack(() => {
			const ratio = (canvas!.clientWidth - 32) / drawing!.scrollWidth;
			if (ratio < 1) zoom = clampZoom(Math.max(0.75, ratio));
		});
	});

	async function zoomToFit() {
		if (!canvas || !drawing) return;
		zoom = 1;
		await tick();
		// The whole width; the whole height only while the cards stay readable (a long list scrolls instead)
		const width = (canvas.clientWidth - 32) / drawing.scrollWidth;
		const height = (canvas.clientHeight - 32) / drawing.scrollHeight;
		const fit = Math.min(width, Math.max(height, 0.6), 1);
		zoom = clampZoom(fit);
		canvas.scrollTo({ left: 0, top: 0 });
	}

	// ── Group details: the effective rules (loaded once, on the first group opened) ──
	let perms = $state<GroupPermissionsResponse[] | null>(null);
	let orgDetail = $state<OrganizationDetailResponse | null>(null);
	let rulesError = $state('');

	async function loadRules() {
		if (perms) return;
		rulesError = '';
		try {
			const [p, o] = await Promise.all([groupApi.apiV1OrganizationsSlugGroupsPermissionsGet(slug).then((r) => r.data), getOrgDetail(slug)]);
			perms = p;
			orgDetail = o;
		} catch (err) {
			rulesError = extractErrorMessage(err, t('orgChart.group.rulesFailed'));
		}
	}

	function ruleLabel(rule: GroupRule): string {
		return rule.key === 'coreHours' ? t('orgChart.group.coreHours') : t(`format.rules.${rule.key}.label`);
	}

	function ruleValue(rule: GroupRule): string {
		if (rule.key !== 'coreHours') return ruleModeLabel(rule.value);
		return rule.value === 'Off' || !rule.value ? t('orgChart.group.coreOff') : rule.value;
	}

	// ── Person details: the balance comes from the balance endpoint, for the one person opened ──
	let balance = $state<{ userId: number; minutes: number | null; error: string } | null>(null);

	async function loadBalance(node: PersonNode) {
		const userId = node.person.userId!;
		if (!canSee(node.person, 'TrackedTime')) {
			balance = null;
			return;
		}
		if (balance?.userId === userId && !balance.error) return;
		balance = { userId, minutes: null, error: '' };
		try {
			const { data } = await reportsApi.apiV1OrganizationsSlugMembersMemberIdBalanceGet(slug, userId);
			if (balance?.userId === userId) balance = { userId, minutes: Math.round((data.cumulativeOvertimeHours ?? 0) * 60), error: '' };
		} catch (err) {
			if (balance?.userId === userId) balance = { userId, minutes: null, error: extractErrorMessage(err, t('orgChart.person.balanceFailed')) };
		}
	}

	let panel = $state<HTMLElement>();
	const panelTitle = $derived(selected ? (selected.kind === 'person' ? fullName(selected.person) : groupTitle(selected)) : '');

	function groupTitle(node: BranchNode): string {
		if (node.kind === 'org') return chart?.organizationName ?? '';
		if (node.kind === 'none') return t('orgChart.noGroup');
		if (node.kind === 'hidden') return t('orgChart.hiddenGroups');
		return node.group?.name ?? '';
	}

	function nodeLabel(node: ChartNode): string {
		if (node.kind === 'person') {
			const parts = [fullName(node.person)];
			if (node.isLead) parts.push(t('orgChart.lead'));
			if (node.person.present != null) parts.push(node.person.present ? t('visibility.present') : t('visibility.absent'));
			if (node.groupCount > 1) parts.push(t('orgChart.multi', { count: node.groupCount }));
			return parts.join(', ');
		}
		const parts = [groupTitle(node), tp('orgChart.people', node.count)];
		if (node.present != null) parts.push(t('orgChart.present', { count: node.present }));
		return parts.join(', ');
	}

	const isYou = (userId: number | undefined) => userId === auth.user?.id;
	const groupName = (id: number | null | undefined) => chart?.groups?.find((g) => g.id === id)?.name ?? '';
	const personNodeOf = (userId: number) => [...(index?.byKey.values() ?? [])].find((n): n is PersonNode => n.kind === 'person' && n.person.userId === userId);
	const groupNodeKey = (id: number) => `g${id}`;

	function openPerson(userId: number) {
		const node = personNodeOf(userId);
		if (!node) return;
		for (const key of [...(index ? [...ancestorsOf(node.key)] : [])]) expanded.add(key);
		expanded = new Set(expanded);
		activate(node.key);
	}

	function openGroup(id: number) {
		const key = groupNodeKey(id);
		expanded = new Set([...expanded, ...ancestorsOf(key)]);
		activate(key);
	}

	function ancestorsOf(key: string): string[] {
		const result: string[] = [];
		for (let cur = index?.parent.get(key); cur; cur = index?.parent.get(cur)) result.push(cur);
		return result;
	}
</script>

<svelte:head>
	<title>{t('common.pageTitle', { page: chart ? `${t('orgChart.pageTitle')} – ${chart.organizationName}` : t('orgChart.pageTitle') })}</title>
</svelte:head>

<svelte:window onkeydown={onWindowKey} />

{#snippet avatar(person: { firstName?: string | null; lastName?: string | null; present?: boolean | null }, size: string)}
	<span class="relative shrink-0">
		<span class="{size} rounded-full bg-gradient-to-br from-primary to-secondary text-primary-content flex items-center justify-center font-semibold" aria-hidden="true">
			{getInitials(person.firstName, person.lastName)}
		</span>
		<PresenceDot present={person.present} />
	</span>
{/snippet}

{#snippet item(node: ChartNode, level: number, pos: number, size: number)}
	{@const isBranch = node.kind !== 'person'}
	{@const hasChildren = node.kind !== 'person' && node.children.length > 0}
	{@const open = hasChildren && expanded.has(node.key)}
	<li
		id={domId(node.key)}
		role="treeitem"
		class="oc-item"
		data-kind={node.kind}
		aria-level={level}
		aria-posinset={pos}
		aria-setsize={size}
		aria-expanded={hasChildren ? open : undefined}
		aria-selected={selectedKey === node.key}
		aria-label={nodeLabel(node)}
		tabindex={activeKey === node.key ? 0 : -1}
		onkeydown={(e) => onItemKey(e, node.key)}
		onclick={(e) => onItemClick(e, node.key)}
	>
		<div class="oc-row">
			<div
				class="oc-node {isBranch ? 'oc-branch' : 'oc-person'}"
				class:oc-selected={selectedKey === node.key}
				class:oc-match={matchSet.has(node.key)}
				data-testid="oc-node"
				style={node.kind === 'group' ? `--oc-color: ${safeColor(node.group?.color)}` : undefined}
			>
				{#if hasChildren}
					<span data-toggle class="oc-toggle" aria-hidden="true" title={open ? t('orgChart.collapse', { name: isBranch ? groupTitle(node) : '' }) : t('orgChart.expand', { name: isBranch ? groupTitle(node) : '' })}>
						<svg class="w-4 h-4 transition-transform" class:-rotate-90={!open} viewBox="0 0 20 20" fill="currentColor"><path fill-rule="evenodd" d="M5.293 7.293a1 1 0 011.414 0L10 10.586l3.293-3.293a1 1 0 111.414 1.414l-4 4a1 1 0 01-1.414 0l-4-4a1 1 0 010-1.414z" clip-rule="evenodd"/></svg>
					</span>
				{/if}
				{#if node.kind === 'person'}
					{@render avatar(node.person, 'w-8 h-8 text-xs')}
					<span class="min-w-0 flex-1">
						<span class="block font-semibold text-sm truncate">{fullName(node.person)}{#if isYou(node.person.userId)}<span class="text-base-content/70 font-normal">&nbsp;· {t('orgChart.you')}</span>{/if}</span>
						<span class="flex items-center gap-1 mt-0.5 min-w-0 overflow-hidden">
							{#if node.isLead}<span class="badge badge-xs badge-primary shrink-0" data-testid="oc-lead">{t('orgChart.lead')}</span>{/if}
							{#if node.groupCount > 1}<span class="badge badge-xs badge-outline shrink-0" title={t('orgChart.multiTitle', { count: node.groupCount })} data-testid="oc-multi">{t('orgChart.multi', { count: node.groupCount })}</span>{/if}
							{#if !node.isLead && node.groupCount <= 1}<span class="text-xs text-base-content/70 truncate">{roleLabel(node.person.role)}</span>{/if}
						</span>
					</span>
				{:else}
					{#if node.kind === 'org'}
						<span class="oc-icon bg-primary text-primary-content" aria-hidden="true">
							<svg class="w-4 h-4" viewBox="0 0 20 20" fill="currentColor"><path fill-rule="evenodd" d="M4 16.5v-13h-.25a.75.75 0 010-1.5h12.5a.75.75 0 010 1.5H16v13h.25a.75.75 0 010 1.5h-3.5a.75.75 0 01-.75-.75v-2.5a.75.75 0 00-.75-.75h-2.5a.75.75 0 00-.75.75v2.5a.75.75 0 01-.75.75h-3.5a.75.75 0 010-1.5H4zm3-11a.5.5 0 01.5-.5h1a.5.5 0 01.5.5v1a.5.5 0 01-.5.5h-1a.5.5 0 01-.5-.5v-1zM7.5 9a.5.5 0 00-.5.5v1a.5.5 0 00.5.5h1a.5.5 0 00.5-.5v-1a.5.5 0 00-.5-.5h-1zM11 5.5a.5.5 0 01.5-.5h1a.5.5 0 01.5.5v1a.5.5 0 01-.5.5h-1a.5.5 0 01-.5-.5v-1zm.5 3.5a.5.5 0 00-.5.5v1a.5.5 0 00.5.5h1a.5.5 0 00.5-.5v-1a.5.5 0 00-.5-.5h-1z" clip-rule="evenodd"/></svg>
						</span>
					{:else if node.kind === 'group'}
						<span class="oc-swatch" aria-hidden="true"></span>
					{:else}
						<span class="oc-icon bg-base-200 text-base-content/70" aria-hidden="true">
							<svg class="w-4 h-4" viewBox="0 0 20 20" fill="currentColor"><path d="M10 9a3 3 0 100-6 3 3 0 000 6zM6 8a2 2 0 11-4 0 2 2 0 014 0zM1.49 15.326a.78.78 0 01-.358-.442 3 3 0 014.308-3.516 6.484 6.484 0 00-1.905 3.959c-.023.222-.014.442.025.654a4.97 4.97 0 01-2.07-.655zM16.44 15.98a4.97 4.97 0 002.07-.654.78.78 0 00.357-.442 3 3 0 00-4.308-3.517 6.484 6.484 0 011.907 3.96 2.32 2.32 0 01-.026.654zM18 8a2 2 0 11-4 0 2 2 0 014 0zM5.304 16.19a.844.844 0 01-.277-.71 5 5 0 019.947 0 .843.843 0 01-.277.71A6.975 6.975 0 0110 18a6.974 6.974 0 01-4.696-1.81z"/></svg>
						</span>
					{/if}
					<span class="min-w-0 flex-1">
						<span class="block font-semibold text-sm truncate" class:italic={node.kind === 'none' || node.kind === 'hidden'}>{groupTitle(node)}</span>
						<span class="flex items-center gap-2 text-xs text-base-content/70 mt-0.5">
							<span>{tp('orgChart.people', node.count)}</span>
							{#if node.present != null}
								<span class="inline-flex items-center gap-1"><span class="w-1.5 h-1.5 rounded-full {node.present > 0 ? 'bg-success' : 'bg-base-300'}" aria-hidden="true"></span>{t('orgChart.present', { count: node.present })}</span>
							{/if}
						</span>
					</span>
				{/if}
			</div>
			{#if open}
				<ul role="group" class="oc-children">
					{#each (node as BranchNode).children as child, i (child.key)}
						{@render item(child, level + 1, i + 1, (node as BranchNode).children.length)}
					{/each}
				</ul>
			{/if}
		</div>
	</li>
{/snippet}

{#snippet groupDetails(node: BranchNode)}
	{@const leads = groupLeads(node)}
	{@const subGroups = node.children.filter((c) => c.kind === 'group')}
	{@const rules = node.group && perms ? groupRules(perms, node.group.id!, orgDetail) : null}
	<dl class="oc-facts">
		{#if node.kind === 'group'}
			{#if node.group?.description}<p class="text-sm text-base-content/70 m-0 col-span-2">{node.group.description}</p>{/if}
			<dt>{t('orgChart.group.subGroupOf')}</dt>
			<dd>
				{#if node.group?.parentGroupId}
					<button type="button" class="link link-hover text-left" onclick={() => openGroup(node.group!.parentGroupId!)}>{groupName(node.group.parentGroupId)}</button>
				{:else}
					<span class="text-base-content/70">{t('orgChart.group.topLevel')}</span>
				{/if}
			</dd>
			<dt>{t('orgChart.group.leads')}</dt>
			<dd>
				{#if leads.length}
					<span class="flex flex-wrap gap-x-2">
						{#each leads as lead (lead.userId)}
							<button type="button" class="link link-hover" onclick={() => openPerson(lead.userId!)}>{fullName(lead)}</button>
						{/each}
					</span>
				{:else}
					<span class="text-base-content/70">{t('orgChart.group.noLeads')}</span>
				{/if}
			</dd>
		{:else}
			<p class="text-sm text-base-content/70 m-0 col-span-2">{node.kind === 'none' ? t('orgChart.group.noGroupHint') : t('orgChart.group.hiddenHint')}</p>
		{/if}
		<dt>{t('orgChart.group.members')}</dt>
		<dd data-testid="oc-group-count">{t('orgChart.group.membersValue', { count: node.count, direct: directPeople(node) })}</dd>
		{#if subGroups.length}
			<dt>{t('orgChart.group.subGroups')}</dt>
			<dd>
				<span class="flex flex-wrap gap-x-2">
					{#each subGroups as sub (sub.key)}
						<button type="button" class="link link-hover" onclick={() => sub.kind === 'group' && openGroup(sub.group!.id!)}>{sub.kind === 'group' ? sub.group?.name : ''}</button>
					{/each}
				</span>
			</dd>
		{/if}
		{#if node.present != null}
			<dt>{t('orgChart.group.presence')}</dt>
			<dd>
				<span data-testid="oc-group-presence">{t('orgChart.group.presentValue', { present: node.present, count: node.count })}</span>
				<span class="block text-xs text-base-content/70">{t('orgChart.group.presenceHint')}</span>
			</dd>
		{/if}
	</dl>

	{#if node.kind === 'group'}
		<section class="mt-4" aria-labelledby="oc-rules-heading">
			<h3 id="oc-rules-heading" class="text-sm font-semibold mb-2">{t('orgChart.group.rules')}</h3>
			{#if rulesError}
				<p class="text-sm text-error">{rulesError}</p>
			{:else if !rules}
				<p class="text-sm text-base-content/70">{t('orgChart.group.rulesLoading')}</p>
			{:else}
				<ul class="flex flex-col gap-2 list-none p-0 m-0 text-sm" data-testid="oc-rules">
					{#each rules as rule (rule.key)}
						<li class="flex items-start justify-between gap-3">
							<span class="min-w-0">
								<span class="block">{ruleLabel(rule)}</span>
								<span class="block text-xs {rule.inheritedFrom === undefined ? 'text-primary font-medium' : 'text-base-content/70'}">
									{rule.inheritedFrom === undefined ? t('orgChart.group.own') : rule.inheritedFrom ? t('orgChart.group.inheritedGroup', { group: rule.inheritedFrom }) : t('orgChart.group.inheritedOrg')}
								</span>
							</span>
							<span class="font-semibold text-right max-w-[45%]">{ruleValue(rule)}</span>
						</li>
					{/each}
				</ul>
			{/if}
		</section>

		<div class="flex flex-wrap gap-2 mt-5">
			<a class="btn btn-primary btn-sm" href="/organizations/{slug}/absences?group={node.group?.id}" data-testid="oc-group-calendar">{t('orgChart.group.calendar')}</a>
			{#if node.group?.canManageMembers}
				<a class="btn btn-sm" href="/organizations/{slug}/groups?group={node.group?.id}">{t('orgChart.group.manage')}</a>
			{/if}
		</div>
	{/if}
{/snippet}

{#snippet personDetails(node: PersonNode)}
	{@const person = node.person}
	{@const groups = personGroups(person, chart?.groups ?? [])}
	{@const seesTime = canSee(person, 'TrackedTime')}
	{@const b = balance?.userId === person.userId ? balance : null}
	{@const seesAbsences = canSee(person, 'Vacation') || canSee(person, 'SickDays') || canSee(person, 'OtherAbsences')}
	<div class="flex items-center gap-3 mb-4">
		{@render avatar(person, 'w-12 h-12 text-base')}
		<div class="min-w-0">
			<span class="badge badge-sm uppercase tracking-wide {person.role === 'Owner' ? 'badge-warning' : person.role === 'Admin' ? 'badge-info' : 'badge-ghost'}">{roleLabel(person.role)}</span>
			{#if person.email}<a class="block text-sm truncate mt-1" href="mailto:{person.email}" data-testid="oc-email">{person.email}</a>{/if}
		</div>
	</div>
	<dl class="oc-facts">
		<dt>{t('orgChart.person.groups')}</dt>
		<dd data-testid="oc-person-groups">
			{#if person.groups == null}
				<span class="text-base-content/70">{t('orgChart.person.groupsHidden')}</span>
			{:else if groups.length === 0}
				<span class="text-base-content/70">{t('orgChart.person.noGroups')}</span>
			{:else}
				<ul class="list-none p-0 m-0 flex flex-col gap-0.5">
					{#each groups as g (g.group.id)}
						<li>
							<button type="button" class="link link-hover text-left" class:text-base-content={!!g.via} onclick={() => openGroup(g.group.id!)}>{g.group.name}</button>
							{#if g.isLead}<span class="badge badge-xs badge-primary ml-1">{t('orgChart.lead')}</span>{/if}
							{#if g.via}<span class="text-xs text-base-content/70 ml-1">{t('orgChart.person.via', { group: g.via })}</span>{/if}
						</li>
					{/each}
				</ul>
			{/if}
		</dd>
		{#if person.present != null}
			<dt>{t('orgChart.person.presence')}</dt>
			<dd data-testid="oc-person-presence">
				{#if person.present && person.presentSince}
					<span class="inline-flex items-center gap-1.5"><span class="w-2 h-2 rounded-full bg-success" aria-hidden="true"></span>{t('orgChart.person.presentSince', { time: formatTime(person.presentSince) })}</span>
				{:else}
					<span class="text-base-content/70">{t('orgChart.person.absent')}</span>
				{/if}
			</dd>
		{/if}
		{#if seesTime}
			<dt>{t('orgChart.person.balance')}</dt>
			<dd data-testid="oc-person-balance">
				{#if b?.error}
					<span class="text-error">{b.error}</span>
				{:else if b?.minutes == null}
					<span class="text-base-content/70">{t('orgChart.person.balanceLoading')}</span>
				{:else}
					<span class="font-semibold tabular-nums {b.minutes > 0 ? 'text-success' : b.minutes < 0 ? 'text-error' : ''}">{formatDelta(b.minutes)}</span>
				{/if}
			</dd>
		{/if}
	</dl>
	{#if person.present == null && !seesTime}
		<p class="text-sm text-base-content/70 mt-3 mb-0" data-testid="oc-no-details">{t('orgChart.person.noDetails')}</p>
	{/if}
	<div class="flex flex-wrap gap-2 mt-5">
		<a class="btn btn-sm" href="/organizations/{slug}/members/{person.userId}">{t('orgChart.person.memberPage')}</a>
		{#if seesAbsences}
			<a class="btn btn-sm" href="/organizations/{slug}/absences?user={person.userId}">{t('orgChart.person.absences')}</a>
		{/if}
	</div>
{/snippet}

{#snippet details()}
	{#if selected && selected.kind === 'person'}
		{@render personDetails(selected)}
	{:else if selected}
		{@render groupDetails(selected)}
	{/if}
{/snippet}

<div class="max-w-7xl mx-auto px-4 sm:px-6 pb-6">
	{#if loading}
		<Skeleton />
	{:else if error}
		<div class="alert alert-error">{error}</div>
	{:else if root}
		<p class="text-base-content/70 text-sm mt-2 mb-4 max-w-3xl">{t('orgChart.intro')}</p>

		<div class="flex flex-wrap items-center gap-2 mb-3">
			<div class="relative flex-1 min-w-0 sm:flex-none sm:w-72">
				<label for="oc-search" class="sr-only">{t('orgChart.search')}</label>
				<input
					id="oc-search"
					type="search"
					class="input input-sm w-full"
					placeholder={t('orgChart.searchPlaceholder')}
					bind:value={query}
					onkeydown={(e) => { if (e.key === 'Enter') { e.preventDefault(); nextMatch(); } }}
					aria-describedby="oc-search-status"
				/>
			</div>
			<span id="oc-search-status" class="text-xs text-base-content/70" aria-live="polite">
				{#if query.trim()}{search.matches.length ? tp('orgChart.matches', search.matches.length) : t('orgChart.noMatches')}{/if}
			</span>
			{#if query.trim() && search.matches.length > 1}
				<button type="button" class="btn btn-ghost btn-xs" onclick={nextMatch}>{t('orgChart.nextMatch')}</button>
			{/if}
			<div class="flex items-center gap-1 ml-auto">
				<button type="button" class="btn btn-ghost btn-xs" onclick={() => setAll(true)}>{t('orgChart.expandAll')}</button>
				<button type="button" class="btn btn-ghost btn-xs" onclick={() => setAll(false)}>{t('orgChart.collapseAll')}</button>
				{#if desktop}
					<span class="w-px h-5 bg-base-300 mx-1" aria-hidden="true"></span>
					<button type="button" class="btn btn-ghost btn-xs btn-square" aria-label={t('orgChart.zoomOut')} title={t('orgChart.zoomOut')} onclick={() => (zoom = clampZoom(zoom - 0.1))}>−</button>
					<button type="button" class="btn btn-ghost btn-xs tabular-nums whitespace-nowrap w-14" title={t('orgChart.zoomReset')} aria-label={t('orgChart.zoomReset')} onclick={() => (zoom = 1)}>{Math.round(zoom * 100)} %</button>
					<button type="button" class="btn btn-ghost btn-xs btn-square" aria-label={t('orgChart.zoomIn')} title={t('orgChart.zoomIn')} onclick={() => (zoom = clampZoom(zoom + 0.1))}>+</button>
					<button type="button" class="btn btn-ghost btn-xs" onclick={zoomToFit}>{t('orgChart.zoomFit')}</button>
				{/if}
			</div>
		</div>

		<div class="oc-layout" class:oc-with-panel={desktop && !!selected}>
			<div class="oc-canvas" bind:this={canvas}>
				<ul
					bind:this={drawing}
					role="tree"
					class="oc-tree"
					aria-label={t('orgChart.treeLabel', { name: chart?.organizationName ?? '' })}
					aria-describedby="oc-keys"
					style:zoom={desktop ? zoom : undefined}
				>
					{@render item(root, 1, 1, 1)}
				</ul>
			</div>

			{#if desktop && selected}
				<aside class="oc-panel card" aria-labelledby="oc-panel-title" bind:this={panel} tabindex="-1" data-testid="oc-panel">
					<div class="flex items-start justify-between gap-2 mb-3">
						<div class="min-w-0">
							<p class="text-xs uppercase tracking-wide text-base-content/70 m-0">{selected.kind === 'person' ? t('orgChart.person.heading') : t('orgChart.group.heading')}</p>
							<h2 id="oc-panel-title" class="text-lg font-bold m-0 break-words">{panelTitle}</h2>
						</div>
						<button type="button" class="btn btn-ghost btn-sm btn-circle -mr-2" aria-label={t('orgChart.close')} onclick={closePanel}>✕</button>
					</div>
					{@render details()}
				</aside>
			{/if}
		</div>
		<p id="oc-keys" class="text-xs text-base-content/70 mt-2 hidden md:block">{t('orgChart.keyboardHint')}</p>
	{/if}
</div>

<Modal open={!desktop && !!selected} title={panelTitle} onclose={closePanel}>
	<div data-testid="oc-panel">{@render details()}</div>
</Modal>

<style>
	.oc-tree,
	.oc-tree :global(ul) {
		list-style: none;
		margin: 0;
		padding: 0;
	}
	.oc-item {
		outline: none;
		--oc-line: color-mix(in srgb, var(--color-base-content) 22%, transparent);
	}
	.oc-node {
		position: relative;
		display: flex;
		align-items: center;
		gap: 0.625rem;
		height: 3.5rem;
		padding: 0 0.75rem;
		border: 1px solid var(--color-base-300);
		border-radius: 0.75rem;
		background: var(--color-base-100);
		color: var(--color-base-content);
		cursor: pointer;
		transition: border-color 0.15s, box-shadow 0.15s;
	}
	.oc-node:hover {
		border-color: color-mix(in srgb, var(--color-primary) 45%, transparent);
	}
	.oc-branch {
		width: 15rem;
	}
	.oc-person {
		width: 14rem;
	}
	[data-kind='org'] > .oc-row > .oc-node {
		border-color: color-mix(in srgb, var(--color-primary) 40%, transparent);
		background: color-mix(in srgb, var(--color-primary) 7%, var(--color-base-100));
	}
	[data-kind='none'] > .oc-row > .oc-node,
	[data-kind='hidden'] > .oc-row > .oc-node {
		border-style: dashed;
	}
	.oc-item:focus-visible > .oc-row > .oc-node {
		outline: 2px solid var(--color-primary);
		outline-offset: 2px;
	}
	.oc-selected {
		border-color: var(--color-primary);
		box-shadow: 0 0 0 1px var(--color-primary), 0 4px 12px -4px color-mix(in srgb, var(--color-primary) 40%, transparent);
	}
	.oc-match {
		background: color-mix(in srgb, var(--color-warning) 18%, var(--color-base-100));
		border-color: var(--color-warning);
	}
	.oc-swatch {
		width: 0.375rem;
		align-self: stretch;
		margin: 0.625rem 0;
		border-radius: 999px;
		background: var(--oc-color);
		flex-shrink: 0;
	}
	.oc-icon {
		width: 2rem;
		height: 2rem;
		border-radius: 0.5rem;
		display: flex;
		align-items: center;
		justify-content: center;
		flex-shrink: 0;
	}
	.oc-toggle {
		position: absolute;
		right: -0.6rem;
		top: 50%;
		translate: 0 -50%;
		z-index: 1;
		width: 1.25rem;
		height: 1.25rem;
		display: flex;
		align-items: center;
		justify-content: center;
		border-radius: 999px;
		border: 1px solid var(--color-base-300);
		background: var(--color-base-100);
		color: color-mix(in srgb, var(--color-base-content) 70%, transparent);
	}
	.oc-toggle:hover {
		color: var(--color-primary);
		border-color: var(--color-primary);
	}

	.oc-layout {
		display: grid;
		grid-template-columns: minmax(0, 1fr);
		gap: 1rem;
		align-items: start;
	}
	.oc-facts {
		display: grid;
		grid-template-columns: auto minmax(0, 1fr);
		gap: 0.5rem 1rem;
		margin: 0;
		font-size: 0.875rem;
	}
	.oc-facts :global(dt) {
		color: color-mix(in srgb, var(--color-base-content) 70%, transparent);
	}
	.oc-facts :global(dd) {
		margin: 0;
		min-width: 0;
	}

	/* Phones: an indented, collapsible list */
	@media (max-width: 767.98px) {
		.oc-node {
			width: 100%;
			height: auto;
			min-height: 3.25rem;
			padding: 0.5rem 2.25rem 0.5rem 0.75rem;
		}
		.oc-toggle {
			right: 0.5rem;
			width: 1.75rem;
			height: 1.75rem;
		}
		.oc-children {
			display: flex;
			flex-direction: column;
			gap: 0.375rem;
			margin: 0.375rem 0 0 0.75rem !important;
			padding-left: 0.625rem !important;
			border-left: 2px solid var(--oc-line);
		}
		.oc-tree {
			display: flex;
			flex-direction: column;
		}
	}

	/* Desktop: a left-to-right drawing with connector lines, in a scroll container */
	@media (min-width: 768px) {
		.oc-layout.oc-with-panel {
			grid-template-columns: minmax(0, 1fr) 21rem;
		}
		.oc-canvas {
			overflow: auto;
			height: max(24rem, calc(100vh - 17rem));
			padding: 1rem;
			border: 1px solid var(--color-base-300);
			border-radius: 1rem;
			background-color: color-mix(in srgb, var(--color-base-200) 55%, var(--color-base-100));
			background-image: radial-gradient(color-mix(in srgb, var(--color-base-content) 9%, transparent) 1px, transparent 1px);
			background-size: 1.25rem 1.25rem;
		}
		.oc-tree {
			width: max-content;
		}
		.oc-row {
			display: flex;
			align-items: flex-start;
		}
		.oc-children {
			--oc-mid: 1.75rem;
			position: relative;
			display: flex;
			flex-direction: column;
			gap: 0.5rem;
			padding-left: 1.5rem !important;
		}
		.oc-children::before {
			content: '';
			position: absolute;
			left: 0;
			top: var(--oc-mid);
			width: 1.5rem;
			border-top: 2px solid var(--oc-line);
		}
		.oc-children > :global(.oc-item) {
			position: relative;
			padding-left: 1.25rem;
		}
		.oc-children > :global(.oc-item)::before {
			content: '';
			position: absolute;
			left: 0;
			top: -0.25rem;
			bottom: -0.25rem;
			border-left: 2px solid var(--oc-line);
		}
		.oc-children > :global(.oc-item)::after {
			content: '';
			position: absolute;
			left: 0;
			top: var(--oc-mid);
			width: 1.25rem;
			border-top: 2px solid var(--oc-line);
		}
		.oc-children > :global(.oc-item:first-child)::before {
			top: var(--oc-mid);
			border-top-left-radius: 0.5rem;
		}
		.oc-children > :global(.oc-item:last-child)::before {
			bottom: auto;
			height: calc(var(--oc-mid) + 0.25rem);
		}
		.oc-children > :global(.oc-item:only-child)::before {
			display: none;
		}
		.oc-panel {
			position: sticky;
			top: 5rem;
			max-height: calc(100vh - 6rem);
			overflow-y: auto;
			padding: 1.25rem;
			outline: none;
		}
	}
</style>
