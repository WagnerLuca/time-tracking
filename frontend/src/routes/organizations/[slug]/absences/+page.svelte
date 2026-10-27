<script lang="ts">
	import Skeleton from '$lib/components/Skeleton.svelte';
	import { getOrgDetail } from '$lib/stores/orgDetail';
	import { untrack } from 'svelte';
	import { page } from '$app/state';
	import { goto } from '$app/navigation';
	import { auth } from '$lib/stores/auth.svelte';
	import { toast } from '$lib/stores/toast.svelte';
	import { confirmDialog } from '$lib/stores/confirm.svelte';
	import { holidayApi, absenceDayApi, groupApi } from '$lib/apiClient';
	import { t, tp } from '$lib/i18n';
	import { absenceTypeLabel, dayNamesShort, formatDate, formatDateFull, formatDateShort, formatNumber, getInitials } from '$lib/utils/formatters';
	import { memberCarryFigures, orgYear, vacationDetailLines, vacationTotal } from '$lib/utils/vacation';
	import { dayPress } from '$lib/utils/dayPress';
	import { dateKey, expandHolidays } from '$lib/utils/dateHelpers';
	import { fetchAllPages } from '$lib/utils/pagination';
	import { DAY_TYPE_CLASSES, dayTypeClasses, personChipStyle, personDotStyle, tintStyle } from '$lib/utils/colors';
	import { isOrgAdmin } from '$lib/utils/orgRules';
	import { canSee } from '$lib/utils/visibility';
	import { markSeen } from '$lib/utils/onboarding';
	import { buildGroupTree, flattenGroupTree } from '$lib/utils/groupTree';
	import { deleteAbsences } from '$lib/utils/absences';
	import AbsenceDialog from '$lib/components/AbsenceDialog.svelte';
	import LockIcon from '$lib/components/LockIcon.svelte';
	import { closedMonthSet, isClosedDay, lockText } from '$lib/utils/closedPeriods';
	import AbsenceTypeBadge from '$lib/components/AbsenceTypeBadge.svelte';
	import { extractErrorMessage, getErrorStatus } from '$lib/utils/errorHandler';
	import type {
		GroupResponse,
		OrganizationDetailResponse,
		HolidayResponse,
		AbsenceDayResponse,
		AbsenceTypeDefinitionResponse,
		EffectivePermissionsResponse
	} from '$lib/api';

	let org = $state<OrganizationDetailResponse | null>(null);
	const closed = $derived(closedMonthSet(org?.closedMonths));
	let loading = $state(true);
	let error = $state('');

	let myRole = $derived(
		org?.members?.find((m) => m.id === auth.user?.id)?.role ?? null
	);
	let canEdit = $derived(isOrgAdmin(myRole));
	// The matrix shows the viewer no absence kind of anyone else (the absences endpoint returns only their own)
	let seesNoOthers = $derived(
		!!org && !(org.members ?? []).some((m) => m.id !== auth.user?.id && (canSee(m, 'Vacation') || canSee(m, 'SickDays') || canSee(m, 'OtherAbsences')))
	);

	// Effective absence permissions for the current user (org + group overrides)
	let effectivePerms = $state<EffectivePermissionsResponse | null>(null);
	let absenceTypes = $state<AbsenceTypeDefinitionResponse[]>([]);
	let selfAbsenceMode = $derived(effectivePerms?.absenceSelfServiceMode ?? org?.absenceSelfServiceMode ?? 'Allowed');
	let mustRequest = $derived(!canEdit && selfAbsenceMode === 'RequiresApproval');
	let canAddSelf = $derived(!canEdit && selfAbsenceMode === 'Allowed');
	let canOpenDayDialog = $derived(canEdit || canAddSelf || mustRequest);

	// Holidays
	let holidays = $state<HolidayResponse[]>([]);

	// Absences
	let absences = $state<AbsenceDayResponse[]>([]);
	let absencesLoading = $state(false);
	let absencesError = $state('');
	let adminAbsenceFilter = $state<number | null>(null);
	let calendarUserFilter = $state<number | null>(null);
	let panelTypeFilter = $state<string>('all');
	let calendarMonth = $state(new Date().getMonth());
	let calendarYear = $state(new Date().getFullYear());
	let selectedListYear = $state(new Date().getFullYear());
	let deletingSpan = $state<string | null>(null);

	// Left click selects a day (panel below the calendar), right click / long press opens the dialog
	let selectedDayKey = $state(dateKey(new Date()));
	let showDayDialog = $state(false);
	let dayDialogDate = $state('');

	const orgSlug = $derived(page.params.slug ?? '');

	// "Gruppenkalender" (#66): ?group=<id> shows the group and its sub-groups (the backend filters), ?user=<id> one person
	const groupFilter = $derived(Number(page.url.searchParams.get('group')) || null);
	let groupOptions = $state<{ group: GroupResponse; depth: number }[]>([]);
	let loadedGroup: number | null = null;

	function setGroupFilter(id: number | null) {
		const url = new URL(page.url);
		if (id) url.searchParams.set('group', String(id));
		else url.searchParams.delete('group');
		goto(url, { replaceState: true, keepFocus: true, noScroll: true });
	}

	async function loadGroupOptions() {
		try {
			groupOptions = flattenGroupTree(buildGroupTree((await groupApi.apiV1OrganizationsSlugGroupsGet(orgSlug)).data));
		} catch {
			groupOptions = []; // the calendar works without the group filter
		}
	}

	// (Re)load whenever the slug in the URL changes, so switching orgs never shows stale data
	$effect(() => {
		if (!orgSlug) return;
		untrack(() => {
			if (auth.user?.id) markSeen(auth.user.id, orgSlug, 'teamCalendar');
			loadOrg();
		});
	});

	async function loadOrg() {
		loading = true;
		error = '';
		absences = [];
		loadedYears = null;
		try {
			const data = await getOrgDetail(orgSlug);
			org = data; // → the effect below loads the absences
			calendarUserFilter = Number(page.url.searchParams.get('user')) || null;
			loadGroupOptions();
			loadHolidays();
			loadEffectivePermissions();
			loadAbsenceTypes();
		} catch (err) {
			error = getErrorStatus(err) === 404 ? t('absences.notFound') : extractErrorMessage(err, t('absences.loadOrgFailed'));
		} finally {
			loading = false;
		}
	}

	async function loadEffectivePermissions() {
		try {
			const { data } = await groupApi.apiV1OrganizationsSlugEffectivePermissionsGet(orgSlug);
			effectivePerms = data;
		} catch (err) {
			// The org defaults still apply; group overrides are just not known
			effectivePerms = null;
			toast.error(err, t('absences.loadPermsFailed'));
		}
	}

	async function loadAbsenceTypes() {
		try {
			const { data } = await absenceDayApi.apiV1OrganizationsSlugAbsenceTypesGet(orgSlug);
			absenceTypes = data;
		} catch (err) {
			absenceTypes = [];
			toast.error(err, t('absences.loadTypesFailed'));
		}
	}

	async function reloadOrg() {
		try {
			const data = await getOrgDetail(orgSlug, { fresh: true });
			org = data;
		} catch (err) {
			toast.error(err, t('absences.refreshFailed'));
		}
	}

	async function loadHolidays() {
		try {
			const { data } = await holidayApi.apiV1OrganizationsSlugHolidaysGet(orgSlug);
			holidays = [...data].sort((a, b) => (a.date ?? '').localeCompare(b.date ?? ''));
		} catch (err) {
			holidays = [];
			toast.error(err, t('absences.loadHolidaysFailed'));
		}
	}

	/**
	 * Absences are loaded for a range of years — the calendar's and the list's year —
	 * and paged through, so the API's 200-item cap never hides the newest ones.
	 */
	interface YearRange { from: number; to: number }
	let loadedYears = $state<YearRange | null>(null);
	let absencesRequest = 0;
	const neededYears = $derived<YearRange>({ from: Math.min(calendarYear, selectedListYear), to: Math.max(calendarYear, selectedListYear) });

	$effect(() => {
		const years = neededYears;
		const group = groupFilter;
		if (!org) return;
		untrack(() => {
			if (!loadedYears || group !== loadedGroup || years.from < loadedYears.from || years.to > loadedYears.to) loadAbsences(years);
		});
	});

	async function loadAbsences(years: YearRange = loadedYears ?? neededYears) {
		const request = ++absencesRequest;
		const group = (loadedGroup = groupFilter);
		absencesLoading = true;
		absencesError = '';
		// A few days either side: the month grid shows the neighbouring years' first/last week
		const from = `${years.from - 1}-12-25`;
		const to = `${years.to + 1}-01-07`;
		try {
			const items = await fetchAllPages((limit, offset) =>
				absenceDayApi.apiV1OrganizationsSlugAbsencesGet(orgSlug, undefined, from, to, limit, offset, group ?? undefined).then((r) => r.data));
			if (request !== absencesRequest) return;
			absences = items.sort((a, b) => (b.date ?? '').localeCompare(a.date ?? ''));
			loadedYears = years;
		} catch (err) {
			if (request !== absencesRequest) return;
			absences = [];
			absencesError = extractErrorMessage(err, t('absences.loadFailed'));
		} finally {
			if (request === absencesRequest) absencesLoading = false;
		}
	}

	function canDeleteSpan(span: AbsenceSpan): boolean {
		return canEdit || (span.userId === auth.user?.id && selfAbsenceMode === 'Allowed');
	}

	async function deleteSpan(span: AbsenceSpan) {
		const ok = await confirmDialog({
			title: t('absences.deleteTitle'),
			message: `${span.userFirstName} ${span.userLastName}: ${spanDateLabel(span)} (${span.customTypeName ?? absenceTypeLabel(span.type)})`,
			confirmLabel: t('common.delete'),
			danger: true
		});
		if (!ok) return;
		deletingSpan = spanKey(span);
		try {
			await deleteAbsences(orgSlug, span.ids);
			toast.success(t('absences.deleted'));
		} catch (err) {
			toast.error(err, t('absences.deleteFailed'));
		} finally {
			deletingSpan = null;
			await loadAbsences();
			await reloadOrg();
		}
	}

	// Calendar helpers
	function calendarDays(year: number, month: number): { date: Date; inMonth: boolean }[] {
		const first = new Date(year, month, 1);
		const last = new Date(year, month + 1, 0);
		const startDay = (first.getDay() + 6) % 7;
		const days: { date: Date; inMonth: boolean }[] = [];
		for (let i = startDay - 1; i >= 0; i--) {
			days.push({ date: new Date(year, month, -i), inMonth: false });
		}
		for (let d = 1; d <= last.getDate(); d++) {
			days.push({ date: new Date(year, month, d), inMonth: true });
		}
		while (days.length % 7 !== 0) {
			days.push({ date: new Date(year, month + 1, days.length - last.getDate() - startDay + 1), inMonth: false });
		}
		return days;
	}

	function navigateMonth(delta: number) {
		const d = new Date(calendarYear, calendarMonth + delta, 1);
		calendarMonth = d.getMonth();
		calendarYear = d.getFullYear();
	}

	function goToToday() {
		calendarMonth = new Date().getMonth();
		calendarYear = new Date().getFullYear();
	}

	function openDayDialog(key: string) {
		selectedDayKey = key;
		dayDialogDate = key;
		showDayDialog = true;
	}

	function dayHandlers(key: string) {
		return { onselect: () => (selectedDayKey = key), onopen: canOpenDayDialog ? () => openDayDialog(key) : undefined };
	}

	/** Mobile "add" button: today in the current month, otherwise the 1st of the visible month. */
	function openDialogForVisibleMonth() {
		const now = new Date();
		openDayDialog(now.getMonth() === calendarMonth && now.getFullYear() === calendarYear ? dateKey(now) : dateKey(new Date(calendarYear, calendarMonth, 1)));
	}

	async function onAbsenceSaved(requestMessage?: string) {
		if (requestMessage) {
			toast.success(requestMessage);
			return;
		}
		toast.success(t('absences.saved'));
		await loadAbsences();
		await reloadOrg();
	}

	// Absence spans
	interface AbsenceSpan {
		userId: number;
		userFirstName: string;
		userLastName: string;
		type: string;
		customTypeName: string | null;
		customTypeColor: string | null;
		startDate: string;
		endDate: string;
		days: number;
		isHalfDay: boolean;
		note: string;
		ids: number[];
	}

	/** Grouping key: custom types group separately from the plain Other type. */
	function spanTypeKey(a: { type?: string | null; customTypeId?: number | null }): string {
		return a.customTypeId ? `c${a.customTypeId}` : (a.type ?? '');
	}

	function spanKey(span: AbsenceSpan): string {
		return span.ids.join(',');
	}

	function spanDateLabel(span: AbsenceSpan): string {
		return span.days === 1 ? formatDate(span.startDate) : `${formatDate(span.startDate)} – ${formatDate(span.endDate)}`;
	}

	function buildAbsenceSpans(absenceList: AbsenceDayResponse[]): AbsenceSpan[] {
		const sorted = [...absenceList].sort((a, b) => {
			if ((a.userId ?? 0) !== (b.userId ?? 0)) return (a.userId ?? 0) - (b.userId ?? 0);
			if (spanTypeKey(a) !== spanTypeKey(b)) return spanTypeKey(a).localeCompare(spanTypeKey(b));
			return (a.date ?? '').localeCompare(b.date ?? '');
		});
		const spans: AbsenceSpan[] = [];
		let current: (AbsenceSpan & { typeKey: string }) | null = null;
		for (const a of sorted) {
			if (current && a.userId === current.userId && spanTypeKey(a) === current.typeKey) {
				const prevDate = new Date(current.endDate + 'T00:00:00');
				const thisDate = new Date((a.date ?? '') + 'T00:00:00');
				const diffDays = Math.round((thisDate.getTime() - prevDate.getTime()) / (1000 * 60 * 60 * 24));
				// Up to 3 days apart still counts as one span (bridges a weekend)
				if (diffDays >= 1 && diffDays <= 3) {
					current.endDate = a.date ?? '';
					current.days++;
					current.ids.push(a.id ?? 0);
					if (a.note && !current.note) current.note = a.note;
					continue;
				}
			}
			current = {
				userId: a.userId ?? 0,
				userFirstName: a.userFirstName ?? '',
				userLastName: a.userLastName ?? '',
				type: a.type ?? '',
				typeKey: spanTypeKey(a),
				customTypeName: a.customTypeName ?? null,
				customTypeColor: a.customTypeColor ?? null,
				startDate: a.date ?? '',
				endDate: a.date ?? '',
				days: 1,
				isHalfDay: a.isHalfDay ?? false,
				note: a.note ?? '',
				ids: [a.id ?? 0]
			};
			spans.push(current);
		}
		return spans.sort((a, b) => a.startDate.localeCompare(b.startDate));
	}

	interface SpanEntry {
		span: AbsenceSpan;
		position: 'single' | 'start' | 'middle' | 'end';
	}

	function buildSpanMap(spans: AbsenceSpan[], absenceList: AbsenceDayResponse[]): Map<string, SpanEntry[]> {
		const dateById = new Map(absenceList.map((a) => [a.id ?? -1, a.date ?? '']));
		const map = new Map<string, SpanEntry[]>();
		for (const span of spans) {
			const dates = span.ids.map((id) => dateById.get(id) ?? '').sort();
			for (let i = 0; i < dates.length; i++) {
				const pos: SpanEntry['position'] = dates.length === 1 ? 'single' : i === 0 ? 'start' : i === dates.length - 1 ? 'end' : 'middle';
				const entries = map.get(dates[i]) ?? [];
				entries.push({ span, position: pos });
				map.set(dates[i], entries);
			}
		}
		return map;
	}

	/**
	 * Chip colours: vacation gets a per-person accent (tells people apart),
	 * custom types their own colour, sick/other the shared day-type colours.
	 */
	function chipStyle(span: AbsenceSpan): string {
		if (span.customTypeName) return tintStyle(span.customTypeColor, 25);
		if (span.type === 'Vacation') return personChipStyle(span.userId);
		return '';
	}

	function chipClass(span: AbsenceSpan): string {
		if (span.customTypeName || span.type === 'Vacation') return '';
		return `${dayTypeClasses(span.type === 'SickDay' ? 'sick' : 'other-absence').soft} border-base-content/20`;
	}

	function spanTitle(span: AbsenceSpan): string {
		return `${span.userFirstName} ${span.userLastName} — ${span.customTypeName ?? absenceTypeLabel(span.type)}${span.days > 1 ? ` (${tp('absences.days', span.days)})` : ''}${span.isHalfDay ? ' (½)' : ''}${span.note ? ': ' + span.note : ''}`;
	}

	interface WeekLane {
		userId: number;
	}

	function getWeeks(days: { date: Date; inMonth: boolean }[]): { date: Date; inMonth: boolean }[][] {
		const weeks: { date: Date; inMonth: boolean }[][] = [];
		for (let i = 0; i < days.length; i += 7) weeks.push(days.slice(i, i + 7));
		return weeks;
	}

	function getWeekLanes(weekDays: { date: Date }[], spanMap: Map<string, SpanEntry[]>, holidayMap: Map<string, HolidayResponse[]>): WeekLane[] {
		const seen = new Set<number>();
		let hasHoliday = false;
		for (const { date } of weekDays) {
			const key = dateKey(date);
			if ((holidayMap.get(key) ?? []).length > 0) hasHoliday = true;
			for (const entry of spanMap.get(key) ?? []) seen.add(entry.span.userId);
		}
		const lanes = [...seen].sort((a, b) => a - b).map((userId) => ({ userId }));
		// Reserve lane 0 for holidays if the week has any
		if (hasHoliday) lanes.unshift({ userId: -1 });
		return lanes;
	}

	function getLaneEntry(dayKey: string, lane: WeekLane, spanMap: Map<string, SpanEntry[]>): SpanEntry | null {
		return (spanMap.get(dayKey) ?? []).find((e) => e.span.userId === lane.userId) ?? null;
	}

	/** Next year back to five years ago (absences are loaded per selected year, so not derived from them). */
	function getAvailableAbsenceYears(): number[] {
		const now = new Date().getFullYear();
		const years = new Set([selectedListYear, ...Array.from({ length: 7 }, (_, i) => now + 1 - i)]);
		return [...years].sort((a, b) => b - a);
	}

	// Users pickable in the calendar filter: all org members for admins,
	// otherwise the distinct users visible in the loaded absences.
	function getCalendarUserOptions(): { id: number; name: string }[] {
		if (canEdit) {
			return (org?.members ?? [])
				.map((m) => ({ id: m.id ?? 0, name: `${m.firstName} ${m.lastName}` }))
				.sort((a, b) => a.name.localeCompare(b.name));
		}
		const seen = new Map<number, string>();
		for (const a of absences) {
			const id = a.userId ?? 0;
			if (!seen.has(id)) seen.set(id, `${a.userFirstName ?? ''} ${a.userLastName ?? ''}`.trim());
		}
		return [...seen.entries()].map(([id, name]) => ({ id, name })).sort((a, b) => a.name.localeCompare(b.name));
	}

	let calendarAbsences = $derived(calendarUserFilter === null ? absences : absences.filter((a) => a.userId === calendarUserFilter));
	let calendarSpans = $derived(buildAbsenceSpans(calendarAbsences));
	let spanMap = $derived(buildSpanMap(calendarSpans, calendarAbsences));
	// Recurring holidays repeat every year; ±1 year for the grid's overflow days
	let holidayMap = $derived(expandHolidays(holidays, calendarYear - 1, calendarYear + 1));
	let monthStart = $derived(dateKey(new Date(calendarYear, calendarMonth, 1)));
	let monthEnd = $derived(dateKey(new Date(calendarYear, calendarMonth + 1, 0)));

	/** Mobile agenda for the visible month: holidays and absence spans overlapping it, by start date. */
	let agenda = $derived.by(() => {
		const items: ({ kind: 'holiday'; date: string; holiday: HolidayResponse } | { kind: 'absence'; date: string; span: AbsenceSpan })[] = [];
		for (const [date, list] of holidayMap) {
			if (date >= monthStart && date <= monthEnd) for (const holiday of list) items.push({ kind: 'holiday', date, holiday });
		}
		for (const span of calendarSpans) {
			if (span.endDate >= monthStart && span.startDate <= monthEnd) items.push({ kind: 'absence', date: span.startDate, span });
		}
		return items.sort((a, b) => a.date.localeCompare(b.date));
	});

	/** The selected day's holidays and the visible members' absences (only while it is in the shown month). */
	let selectedDay = $derived.by(() => {
		if (selectedDayKey < monthStart || selectedDayKey > monthEnd) return null;
		return {
			key: selectedDayKey,
			holidays: holidayMap.get(selectedDayKey) ?? [],
			absences: calendarAbsences
				.filter((a) => a.date === selectedDayKey)
				.sort((a, b) => `${a.userFirstName} ${a.userLastName}`.localeCompare(`${b.userFirstName} ${b.userLastName}`))
		};
	});

	let dayNames = $derived(dayNamesShort());
	let today = dateKey(new Date());
</script>

<svelte:head>
	<title>{t('common.pageTitle', { page: org ? `${t('absences.title')} – ${org.name}` : t('absences.title') })}</title>
</svelte:head>

{#snippet personName(span: AbsenceSpan)}
	<span class="w-2.5 h-2.5 rounded-full shrink-0" style={personDotStyle(span.userId)} aria-hidden="true"></span>
	{span.userFirstName} {span.userLastName}
{/snippet}

<div class="max-w-5xl mx-auto sm:px-6 pb-6">
	<div class="flex items-center gap-2 mb-6">
		<a href="/organizations/{orgSlug}" class="text-base-content/70 no-underline text-sm hover:text-primary">&larr; {org?.name ?? t('nav.organization')}</a>
	</div>

	{#if loading}
		<Skeleton />
	{:else if error}
		<div class="alert alert-error">{error}</div>
	{:else if org}
		<h1 class="text-2xl font-bold text-base-content mb-1">{t('absences.title')}</h1>
		<p class="text-base-content/70 text-sm mb-6 leading-relaxed">
			{#if canEdit}
				{t('absences.intro.admin')}
			{:else if mustRequest}
				{t('absences.intro.request')}
			{:else if canAddSelf}
				{t('absences.intro.self')}
			{:else}
				{t('absences.intro.readOnly')}
			{/if}
		</p>
		{#if seesNoOthers}
			<div role="status" class="alert alert-info text-sm mb-6">{t('absences.noOthers')}</div>
		{/if}

		{#if absencesError}
			<div class="alert alert-error text-sm mb-4" role="alert">
				<span>{absencesError}</span>
				<button class="btn btn-sm" onclick={() => loadAbsences()}>{t('common.retry')}</button>
			</div>
		{/if}

		{@const calendarUserOptions = getCalendarUserOptions()}
		{@const monthLabel = formatDate(new Date(calendarYear, calendarMonth, 1), { month: 'long', year: 'numeric' })}

		<section aria-labelledby="calendar-heading">
			<!-- Calendar navigation -->
			<div class="flex items-center justify-between gap-2 mb-4">
				<button class="btn btn-ghost btn-sm" aria-label={t('absences.prevMonth')} title={t('absences.prevMonth')} onclick={() => navigateMonth(-1)}>
					<svg class="w-5 h-5" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true"><path fill-rule="evenodd" d="M12.79 5.23a.75.75 0 01-.02 1.06L8.832 10l3.938 3.71a.75.75 0 11-1.04 1.08l-4.5-4.25a.75.75 0 010-1.08l4.5-4.25a.75.75 0 011.06.02z" clip-rule="evenodd"/></svg>
				</button>
				<div class="flex items-center gap-2 sm:gap-3 flex-wrap justify-center">
					<h2 id="calendar-heading" class="text-lg sm:text-xl font-bold text-base-content m-0" aria-live="polite">{monthLabel}</h2>
					<button class="btn btn-ghost btn-xs" onclick={goToToday}>{t('absences.today')}</button>
				</div>
				<button class="btn btn-ghost btn-sm" aria-label={t('absences.nextMonth')} title={t('absences.nextMonth')} onclick={() => navigateMonth(1)}>
					<svg class="w-5 h-5" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true"><path fill-rule="evenodd" d="M7.21 14.77a.75.75 0 01.02-1.06L11.168 10 7.23 6.29a.75.75 0 111.04-1.08l4.5 4.25a.75.75 0 010 1.08l-4.5 4.25a.75.75 0 01-1.06-.02z" clip-rule="evenodd"/></svg>
				</button>
			</div>

			<!-- Calendar member filter + mobile add -->
			<div class="flex items-center justify-end gap-2 mb-3 flex-wrap">
				{#if groupOptions.length > 0}
					<label class="text-xs text-base-content/70" for="calendar-group-filter">{t('absences.group')}</label>
					<select id="calendar-group-filter" value={groupFilter ?? ''} onchange={(e) => setGroupFilter(Number(e.currentTarget.value) || null)} class="select select-sm max-w-[14rem]">
						<option value="">{t('absences.allGroups')}</option>
						{#each groupOptions as o (o.group.id)}
							<option value={o.group.id}>{'  '.repeat(o.depth - 1)}{o.group.name}</option>
						{/each}
					</select>
				{/if}
				{#if calendarUserOptions.length > 1}
					<label class="text-xs text-base-content/70" for="calendar-user-filter">{t('absences.show')}</label>
					<select id="calendar-user-filter" bind:value={calendarUserFilter} class="select select-sm max-w-[14rem]">
						<option value={null}>{t('absences.allMembers')}</option>
						{#each calendarUserOptions as u (u.id)}
							<option value={u.id}>{u.name}{u.id === auth.user?.id ? t('absences.youSuffix') : ''}</option>
						{/each}
					</select>
					{#if calendarUserFilter !== null}
						<button class="btn btn-ghost btn-xs" onclick={() => (calendarUserFilter = null)}>{t('absences.clearFilter')}</button>
					{/if}
				{/if}
				{#if canOpenDayDialog}
					<button class="btn btn-primary btn-sm sm:hidden" onclick={openDialogForVisibleMonth}>
						{mustRequest ? t('absences.requestAbsence') : t('absences.addAbsence')}
					</button>
				{/if}
			</div>

			<!-- Mobile: agenda list for the visible month -->
			<div class="sm:hidden">
				{#if absencesLoading}
					<p class="text-base-content/70 text-sm">{t('common.loading')}</p>
				{:else if agenda.length === 0}
					<p class="text-base-content/70 text-sm text-center py-6 bg-base-200/30 rounded-lg border border-base-300">{t('absences.emptyMonth')}</p>
				{:else}
					<ul class="flex flex-col gap-1.5">
						{#each agenda as item (item.kind === 'holiday' ? `h${item.holiday.id}-${item.date}` : `a${spanKey(item.span)}`)}
							{#if item.kind === 'holiday'}
								<li><button type="button" use:dayPress={dayHandlers(item.date)} aria-pressed={item.date === selectedDayKey} class="w-full text-left flex items-center gap-2 p-2.5 rounded-lg border border-base-300 text-sm {DAY_TYPE_CLASSES.holiday.soft} {item.date === selectedDayKey ? 'ring-2 ring-primary/40' : ''}">
									<span class="w-2.5 h-2.5 rounded-full shrink-0 {DAY_TYPE_CLASSES.holiday.dot}" aria-hidden="true"></span>
									<span class="text-base-content/70 shrink-0">{formatDate(item.date, { weekday: 'short', day: 'numeric', month: 'short' })}</span>
									<span class="font-medium truncate">{item.holiday.name}</span>
									{#if item.holiday.isHalfDay}<span class="badge badge-xs badge-outline" title={t('absences.halfDay')}>½</span>{/if}
								</button></li>
							{:else}
								{@const span = item.span}
								{@const pressed = selectedDayKey >= span.startDate && selectedDayKey <= span.endDate}
								<!-- A span selects its first day in this month; the member link inside keeps working -->
								<!-- svelte-ignore a11y_no_noninteractive_element_interactions -->
								<li class="flex flex-col gap-1 p-2.5 bg-base-100 rounded-lg border border-base-300 text-sm cursor-pointer {pressed ? 'ring-2 ring-primary/40' : ''}" use:dayPress={dayHandlers(span.startDate < monthStart ? monthStart : span.startDate)}>
									<div class="flex items-center justify-between gap-2">
										<span class="text-base-content/70">
											{span.days === 1
												? formatDate(span.startDate, { weekday: 'short', day: 'numeric', month: 'short' })
												: `${formatDateShort(span.startDate)} – ${formatDateShort(span.endDate)}`}
										</span>
										<span class="flex items-center gap-1">
											{#if span.days > 1}<span class="badge badge-xs badge-outline">{tp('absences.days', span.days)}</span>{/if}
											{#if span.isHalfDay}<span class="badge badge-xs badge-outline" title={t('absences.halfDay')}>½</span>{/if}
											<AbsenceTypeBadge type={span.type} customTypeName={span.customTypeName} customTypeColor={span.customTypeColor} size="xs" />
										</span>
									</div>
									<div class="flex items-center gap-1.5 font-medium">
										{#if canEdit}
											<a href="/organizations/{orgSlug}/members/{span.userId}" class="flex items-center gap-1.5 link link-hover min-w-0 truncate">{@render personName(span)}</a>
										{:else}
											{@render personName(span)}
										{/if}
									</div>
									{#if span.note}<span class="text-xs text-base-content/70 truncate">{span.note}</span>{/if}
								</li>
							{/if}
						{/each}
					</ul>
				{/if}
			</div>

			<!-- sm and up: month grid -->
			<div class="hidden sm:block">
				<div class="grid grid-cols-7 gap-px mb-px" aria-hidden="true">
					{#each dayNames as dow, i}
						<div class="text-center text-xs font-semibold py-2 {i >= 5 ? 'text-base-content/70' : 'text-base-content/70'}">{dow}</div>
					{/each}
				</div>

				<!-- Week-based grid with stable lanes per person -->
				<div class="border border-base-300 rounded-lg overflow-hidden">
					{#each getWeeks(calendarDays(calendarYear, calendarMonth)) as weekDays, wi}
						{@const weekLanes = getWeekLanes(weekDays, spanMap, holidayMap)}
						<div class="grid grid-cols-7 gap-px bg-base-300/50 {wi > 0 ? 'border-t-2 border-base-300' : ''}">
							{#each weekDays as { date, inMonth }}
								{@const key = dateKey(date)}
								{@const isToday = key === today}
								{@const isWeekend = date.getDay() === 0 || date.getDay() === 6}
								{@const selected = inMonth && key === selectedDayKey}
								<!-- In-month cells are buttons (a div: they contain member links); Enter/Space select, the context menu key opens the dialog -->
								<!-- svelte-ignore a11y_no_noninteractive_tabindex, a11y_no_static_element_interactions -->
								<div
									class="min-h-[60px] p-1 pt-1.5 pb-3 flex flex-col transition-colors {inMonth ? 'cursor-pointer hover:bg-primary/5 focus-visible:outline-2 focus-visible:outline-primary' : ''} {inMonth && !isWeekend && !selected ? 'bg-base-100' : ''} {inMonth && isWeekend && !selected ? 'bg-base-200/40' : ''} {!inMonth ? 'bg-base-200/70' : ''} {selected ? 'ring-2 ring-inset ring-primary/50 bg-primary/10' : ''}"
									role={inMonth ? 'button' : undefined}
									tabindex={inMonth ? 0 : undefined}
									aria-label={inMonth ? formatDate(date) : undefined}
									aria-pressed={inMonth ? selected : undefined}
									data-day={inMonth ? key : undefined}
									use:dayPress={inMonth ? dayHandlers(key) : { onselect: () => {} }}
									onkeydown={(e) => { if (inMonth && (e.key === 'Enter' || e.key === ' ')) { e.preventDefault(); selectedDayKey = key; } }}
								>
									<div class="flex items-center justify-between mb-1.5">
										<span class="text-[10px] font-medium leading-none {isToday ? 'bg-primary text-primary-content rounded-full w-5 h-5 flex items-center justify-center' : ''} {!inMonth ? 'text-base-content/70' : isWeekend ? 'text-base-content/70' : 'text-base-content'}">
											{date.getDate()}
										</span>
										{#if inMonth && isClosedDay(key, closed)}<LockIcon title={lockText(key)} />{/if}
									</div>
									<div class="flex flex-col gap-[2px] mt-1 min-h-[48px]">
										{#each weekLanes as lane (lane.userId)}
											{#if lane.userId === -1}
												{@const dayH = holidayMap.get(key) ?? []}
												{#if dayH.length > 0}
													<div class="h-[16px] text-[9px] leading-none flex items-center px-1 rounded border border-base-content/20 truncate {DAY_TYPE_CLASSES.holiday.soft}" title={dayH.map((h) => h.name).join(', ')}>
														{dayH[0].name}
													</div>
												{:else}
													<div class="h-[16px]"></div>
												{/if}
											{:else}
												{@const entry = getLaneEntry(key, lane, spanMap)}
												{#if entry}
													{@const shape = entry.position === 'start' ? 'rounded-l border-r-0 -mr-1' : entry.position === 'end' ? 'rounded-r border-l-0 -ml-1' : entry.position === 'single' ? 'rounded' : 'border-x-0 -mx-1'}
													{#snippet chipLabel()}
														{#if entry.position === 'start' || entry.position === 'single'}
															{entry.span.userFirstName}{#if entry.span.days > 1}&nbsp;→{/if}{#if entry.span.isHalfDay && entry.position === 'single'}&nbsp;½{/if}
														{:else if entry.position === 'end'}
															→&nbsp;{entry.span.userFirstName}
														{:else}
															&nbsp;
														{/if}
													{/snippet}
													{#if canEdit}
														<a
															href="/organizations/{orgSlug}/members/{entry.span.userId}"
															onclick={(e) => e.stopPropagation()}
															onkeydown={(e) => e.stopPropagation()}
															class="h-[16px] flex items-center text-[9px] leading-none px-1 border truncate no-underline text-base-content hover:brightness-95 transition-[filter] {chipClass(entry.span)} {shape}"
															style={chipStyle(entry.span)}
															title={spanTitle(entry.span)}
														>{@render chipLabel()}</a>
													{:else}
														<div
															class="h-[16px] flex items-center text-[9px] leading-none px-1 border truncate text-base-content {chipClass(entry.span)} {shape}"
															style={chipStyle(entry.span)}
															title={spanTitle(entry.span)}
														>{@render chipLabel()}</div>
													{/if}
												{:else}
													<div class="h-[16px]"></div>
												{/if}
											{/if}
										{/each}
									</div>
								</div>
							{/each}
						</div>
					{/each}
				</div>
			</div>

			<p class="text-xs text-base-content/70 mt-2 mb-0">
				{t('calendar.hintSelect')}{#if canOpenDayDialog}{' · '}{t('calendar.hintOpen')}{/if}
			</p>

			{#if selectedDay}
				<section class="card bg-base-100 border border-base-300 p-4 mt-3" aria-label={t('calendar.dayDetails')} data-testid="day-details">
					<div class="flex flex-wrap items-center justify-between gap-2 mb-3">
						<div>
							<h3 class="text-[0.9375rem] font-bold m-0">{t('calendar.dayDetails')}</h3>
							<span class="text-sm text-base-content/70">{formatDateFull(selectedDay.key)}</span>
						</div>
						{#if canOpenDayDialog}
							<button type="button" class="btn btn-outline btn-sm" onclick={() => openDayDialog(selectedDay.key)}>{mustRequest ? t('absences.requestAbsence') : t('absences.addAbsence')}</button>
						{/if}
					</div>
					{#if selectedDay.holidays.length > 0}
						<div class="flex flex-wrap gap-2 mb-2">
							{#each selectedDay.holidays as h (h.id)}
								<span class="badge {DAY_TYPE_CLASSES.holiday.badge}">{h.name}{h.isHalfDay ? ' ½' : ''}</span>
							{/each}
						</div>
					{/if}
					{#if selectedDay.absences.length === 0}
						<p class="text-sm text-base-content/70 m-0">{t('absences.emptyDay')}</p>
					{:else}
						<ul class="flex flex-col gap-1.5">
							{#each selectedDay.absences as a (a.id)}
								<li class="flex items-center gap-x-3 gap-y-1 p-2 rounded-lg border border-base-300 bg-base-200/40 text-sm flex-wrap">
									<span class="flex items-center gap-1.5 font-medium">
										<span class="w-2.5 h-2.5 rounded-full shrink-0" style={personDotStyle(a.userId ?? 0)} aria-hidden="true"></span>
										{#if canEdit}
											<a href="/organizations/{orgSlug}/members/{a.userId}" class="link link-hover">{a.userFirstName} {a.userLastName}</a>
										{:else}
											{a.userFirstName} {a.userLastName}
										{/if}
									</span>
									<AbsenceTypeBadge type={a.type ?? ''} customTypeName={a.customTypeName} customTypeColor={a.customTypeColor} size="xs" />
									{#if a.isHalfDay}<span class="badge badge-xs badge-outline" title={t('absences.halfDay')}>½</span>{/if}
									{#if a.note}<span class="text-xs text-base-content/70 truncate min-w-0 flex-1">{a.note}</span>{/if}
								</li>
							{/each}
						</ul>
					{/if}
				</section>
			{/if}

			<!-- Legend -->
			<div class="flex flex-wrap gap-x-4 gap-y-1 mt-3 text-xs text-base-content/70">
				<span class="flex items-center gap-1"><span class="w-3 h-3 rounded {DAY_TYPE_CLASSES.sick.dot}"></span> {t('format.dayType.sick')}</span>
				<span class="flex items-center gap-1"><span class="w-3 h-3 rounded {DAY_TYPE_CLASSES['other-absence'].dot}"></span> {t('format.absenceType.Other')}</span>
				<span class="flex items-center gap-1"><span class="w-3 h-3 rounded {DAY_TYPE_CLASSES.holiday.dot}"></span> {t('format.dayType.holiday')}</span>
				<span class="text-base-content/70">{t('absences.legend.vacationHint')}</span>
			</div>
		</section>

		<!-- Day-click Add/Request Absence Dialog -->
		<AbsenceDialog
			open={canOpenDayDialog && showDayDialog}
			{orgSlug}
			date={dayDialogDate}
			members={canEdit ? (org?.members ?? []) : null}
			isAdmin={canEdit}
			{mustRequest}
			{absenceTypes}
			dayAbsences={absences.filter((a) => a.date === dayDialogDate)}
			onclose={() => (showDayDialog = false)}
			onsaved={onAbsenceSaved}
		/>

		<!-- Vacation Days Summary — respects the org/group Vacation Visibility setting -->
		{@const vacationVisibleMembers = (org.members ?? []).filter((m) => m.vacationVisible && vacationTotal(m) > 0)}
		{#if canEdit || vacationVisibleMembers.length > 0}
		<section class="mt-8 bg-base-200/30 rounded-lg p-4 sm:p-5 border border-base-300">
			<div class="flex items-center justify-between flex-wrap gap-2 mb-4">
				<h2 class="text-xl font-bold text-base-content m-0">{t('absences.balance.title')}</h2>
				<div class="flex items-center gap-4 text-xs text-base-content/70">
					<span class="flex items-center gap-1"><span class="w-3 h-3 rounded-sm bg-primary"></span> {t('absences.balance.taken')}</span>
					<span class="flex items-center gap-1"><span class="w-3 h-3 rounded-sm bg-primary/40"></span> {t('absences.balance.planned')}</span>
				</div>
			</div>
			{#if vacationVisibleMembers.length === 0}
				<p class="text-base-content/70 text-sm">{t('absences.balance.none')}</p>
			{:else}
				{@const membersWithVacation = vacationVisibleMembers
					.filter((m) => calendarUserFilter === null || m.id === calendarUserFilter)
					.sort((a, b) => ((a.vacationDaysRemaining ?? 0) - (b.vacationDaysRemaining ?? 0)))}
				{#if membersWithVacation.length === 0}
					<p class="text-base-content/70 text-sm">{t('absences.balance.noneSelected')}</p>
				{:else}
				<div class="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
					{#each membersWithVacation as member (member.id)}
						{@const total = vacationTotal(member)}
						{@const taken = member.vacationDaysTaken ?? 0}
						{@const planned = member.vacationDaysPlanned ?? 0}
						{@const remaining = member.vacationDaysRemaining ?? total}
						{@const takenPct = total > 0 ? Math.min(100, (taken / total) * 100) : 0}
						{@const plannedPct = total > 0 ? Math.min(100 - takenPct, (planned / total) * 100) : 0}
						{@const pct = Math.round(takenPct + plannedPct)}
						<a href="/organizations/{orgSlug}/members/{member.id}" class="flex items-start gap-3 p-3 bg-base-100 rounded-lg border border-base-300 hover:border-primary/30 hover:shadow-sm transition-[border-color,box-shadow,background-color] no-underline text-base-content">
							<div class="w-9 h-9 rounded-full bg-primary text-primary-content flex items-center justify-center text-xs font-semibold shrink-0 mt-0.5" aria-hidden="true">
								{getInitials(member.firstName, member.lastName)}
							</div>
							<div class="flex-1 min-w-0">
								<div class="font-semibold text-sm truncate">{member.firstName} {member.lastName}</div>
								<div class="flex items-center gap-2 mt-1">
									<div class="flex-1 h-2 bg-base-200 rounded-full overflow-hidden flex" title={t('absences.balance.barTitle', { taken: formatNumber(taken), planned: formatNumber(planned) })}>
										<div class="h-full {pct >= 90 ? 'bg-error' : pct >= 70 ? 'bg-warning' : 'bg-primary'}" style="width: {takenPct}%"></div>
										<div class="h-full {pct >= 90 ? 'bg-error/40' : pct >= 70 ? 'bg-warning/40' : 'bg-primary/40'}" style="width: {plannedPct}%"></div>
									</div>
									<span class="text-xs text-base-content/70 shrink-0">{formatNumber(pct / 100, { style: 'percent' })}</span>
								</div>
								<div class="flex justify-between gap-2 mt-1 text-xs text-base-content/70">
									<span>{t('absences.balance.used', { taken: formatNumber(taken), total: formatNumber(total) })}{planned > 0 ? t('absences.balance.plannedSuffix', { planned: formatNumber(planned) }) : ''}</span>
									<span class="font-semibold shrink-0 {remaining <= 2 ? 'text-error' : remaining <= 5 ? 'text-warning' : 'text-success'}">{t('absences.balance.left', { remaining: formatNumber(remaining) })}</span>
								</div>
								{#each vacationDetailLines(memberCarryFigures(member), orgYear(org?.timeZone)) as line (line)}
									<div class="text-xs text-base-content/70 mt-0.5">{line}</div>
								{/each}
							</div>
						</a>
					{/each}
				</div>
				{/if}
			{/if}
		</section>
		{/if}

		<!-- Absence List -->
		<section class="mt-8 bg-base-200/30 rounded-lg p-4 sm:p-5 border border-base-300">
			<h2 class="text-xl font-bold text-base-content mb-4">{t('absences.list.title')}</h2>
			<div class="flex flex-wrap gap-2 mb-4">
				<select bind:value={selectedListYear} class="select select-sm w-auto" aria-label={t('absences.list.year')}>
					{#each getAvailableAbsenceYears() as y (y)}
						<option value={y}>{y}</option>
					{/each}
				</select>
				{#if canEdit}
				<select bind:value={adminAbsenceFilter} class="select select-sm w-auto max-w-full" aria-label={t('absences.list.member')}>
					<option value={null}>{t('absences.allMembers')}</option>
					{#each (org?.members ?? []) as m (m.id)}
						<option value={m.id}>{m.firstName} {m.lastName}</option>
					{/each}
				</select>
				{/if}
				<select bind:value={panelTypeFilter} class="select select-sm w-auto" aria-label={t('absences.list.type')}>
					<option value="all">{t('absences.list.allTypes')}</option>
					<option value="Vacation">{absenceTypeLabel('Vacation')}</option>
					<option value="SickDay">{absenceTypeLabel('SickDay')}</option>
					<option value="Other">{absenceTypeLabel('Other')}</option>
				</select>
			</div>
			{#if absencesLoading}
				<p class="text-base-content/70 text-sm">{t('common.loading')}</p>
			{:else}
				{@const yearStr = String(selectedListYear)}
				{@const filtered = absences
					.filter((a) => (a.date ?? '').startsWith(yearStr))
					.filter((a) => !adminAbsenceFilter || a.userId === adminAbsenceFilter)
					.filter((a) => panelTypeFilter === 'all' || a.type === panelTypeFilter)}
				{@const spans = buildAbsenceSpans(filtered)}
				{#if spans.length === 0}
					<p class="text-base-content/70 text-sm text-center py-4">{t('absences.list.empty', { year: selectedListYear })}</p>
				{:else}
					<ul class="flex flex-col gap-1.5">
						{#each spans as span (spanKey(span))}
							<li class="flex items-center gap-x-3 gap-y-1 p-2.5 bg-base-100 rounded-lg border border-base-300 text-sm flex-wrap">
								<span class="text-base-content/70 shrink-0 sm:min-w-[190px]">{spanDateLabel(span)}</span>
								{#if canEdit}
								<a href="/organizations/{orgSlug}/members/{span.userId}" class="flex items-center gap-1.5 font-medium link link-hover sm:min-w-[130px]">
									{@render personName(span)}
								</a>
								{:else}
								<span class="flex items-center gap-1.5 font-medium sm:min-w-[130px]">
									{@render personName(span)}
								</span>
								{/if}
								<AbsenceTypeBadge type={span.type} customTypeName={span.customTypeName} customTypeColor={span.customTypeColor} />
								{#if span.days > 1}
									<span class="badge badge-sm badge-outline">{tp('absences.days', span.days)}</span>
								{/if}
								{#if span.isHalfDay && span.days === 1}
									<span class="badge badge-sm badge-outline">{t('absences.list.halfDay')}</span>
								{/if}
								{#if span.note}
									<span class="text-base-content/70 truncate min-w-0 flex-1">{span.note}</span>
								{/if}
								{#if canDeleteSpan(span)}
									<button
										class="btn btn-ghost btn-xs text-error ml-auto"
										aria-label={t('absences.deleteLabel')}
										title={t('absences.deleteLabel')}
										disabled={deletingSpan === spanKey(span)}
										onclick={() => deleteSpan(span)}
									>{#if deletingSpan === spanKey(span)}<span class="loading loading-spinner loading-xs"></span>{:else}&times;{/if}</button>
								{/if}
							</li>
						{/each}
					</ul>
					<p class="text-xs text-base-content/70 mt-2">{tp('absences.list.entries', spans.length)} {tp('absences.list.totalDays', filtered.length)}</p>
				{/if}
			{/if}
		</section>
	{/if}
</div>
