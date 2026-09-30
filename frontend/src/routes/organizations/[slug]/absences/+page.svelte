<script lang="ts">
	import { untrack } from 'svelte';
	import { page } from '$app/state';
	import { auth } from '$lib/stores/auth.svelte';
	import { toast } from '$lib/stores/toast.svelte';
	import { confirmDialog } from '$lib/stores/confirm.svelte';
	import { organizationsApi, holidayApi, absenceDayApi, groupApi } from '$lib/apiClient';
	import { absenceTypeLabel, dayNamesShort, formatDate, formatDateShort, getInitials } from '$lib/utils/formatters';
	import { dateKey } from '$lib/utils/dateHelpers';
	import { DAY_TYPE_CLASSES, dayTypeClasses, personChipStyle, personDotStyle, tintStyle } from '$lib/utils/colors';
	import { isOrgAdmin } from '$lib/utils/orgRules';
	import AbsenceDialog from '$lib/components/AbsenceDialog.svelte';
	import AbsenceTypeBadge from '$lib/components/AbsenceTypeBadge.svelte';
	import { extractErrorMessage, getErrorStatus } from '$lib/utils/errorHandler';
	import type {
		OrganizationDetailResponse,
		HolidayResponse,
		AbsenceDayResponse,
		AbsenceTypeDefinitionResponse,
		EffectivePermissionsResponse
	} from '$lib/api';

	let org = $state<OrganizationDetailResponse | null>(null);
	let loading = $state(true);
	let error = $state('');

	let myRole = $derived(
		org?.members?.find((m) => m.id === auth.user?.id)?.role ?? null
	);
	let canEdit = $derived(isOrgAdmin(myRole));

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

	// Day-click add-absence dialog
	let showDayDialog = $state(false);
	let dayDialogDate = $state('');

	const orgSlug = $derived(page.params.slug ?? '');

	// (Re)load whenever the slug in the URL changes, so switching orgs never shows stale data
	$effect(() => {
		if (!orgSlug) return;
		untrack(() => {
			loadOrg();
		});
	});

	async function loadOrg() {
		loading = true;
		error = '';
		try {
			const { data } = await organizationsApi.apiV1OrganizationsSlugGet(orgSlug);
			org = data;
			loadHolidays();
			loadAbsences();
			loadEffectivePermissions();
			loadAbsenceTypes();
		} catch (err) {
			error = getErrorStatus(err) === 404 ? 'Organization not found.' : extractErrorMessage(err, 'Failed to load organization.');
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
			toast.error(err, 'Failed to load your permissions.');
		}
	}

	async function loadAbsenceTypes() {
		try {
			const { data } = await absenceDayApi.apiV1OrganizationsSlugAbsenceTypesGet(orgSlug);
			absenceTypes = data;
		} catch (err) {
			absenceTypes = [];
			toast.error(err, 'Failed to load absence types.');
		}
	}

	async function reloadOrg() {
		try {
			const { data } = await organizationsApi.apiV1OrganizationsSlugGet(orgSlug);
			org = data;
		} catch (err) {
			toast.error(err, 'Failed to refresh vacation balances.');
		}
	}

	async function loadHolidays() {
		try {
			const { data } = await holidayApi.apiV1OrganizationsSlugHolidaysGet(orgSlug);
			holidays = [...data].sort((a, b) => (a.date ?? '').localeCompare(b.date ?? ''));
		} catch (err) {
			holidays = [];
			toast.error(err, 'Failed to load holidays.');
		}
	}

	async function loadAbsences() {
		absencesLoading = true;
		absencesError = '';
		try {
			const { data } = await absenceDayApi.apiV1OrganizationsSlugAbsencesGet(orgSlug, undefined, undefined, undefined, 200);
			absences = [...(data.items ?? [])].sort((a, b) => (b.date ?? '').localeCompare(a.date ?? ''));
		} catch (err) {
			absences = [];
			absencesError = extractErrorMessage(err, 'Failed to load absences.');
		} finally {
			absencesLoading = false;
		}
	}

	function canDeleteSpan(span: AbsenceSpan): boolean {
		return canEdit || (span.userId === auth.user?.id && selfAbsenceMode === 'Allowed');
	}

	async function deleteSpan(span: AbsenceSpan) {
		const ok = await confirmDialog({
			title: 'Delete absence?',
			message: `${span.userFirstName} ${span.userLastName}: ${spanDateLabel(span)} (${span.customTypeName ?? absenceTypeLabel(span.type)})`,
			confirmLabel: 'Delete',
			danger: true
		});
		if (!ok) return;
		deletingSpan = spanKey(span);
		try {
			for (const id of span.ids) await absenceDayApi.apiV1OrganizationsSlugAbsencesIdDelete(orgSlug, id);
			toast.success('Absence deleted.');
		} catch (err) {
			toast.error(err, 'Failed to delete absence.');
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

	function buildHolidayMap(holidayList: HolidayResponse[]): Map<string, HolidayResponse[]> {
		const map = new Map<string, HolidayResponse[]>();
		for (const h of holidayList) {
			if (!h.date) continue;
			// Recurring holidays repeat in the visible calendar year
			const key = h.isRecurring ? `${calendarYear}-${h.date.substring(5)}` : h.date;
			if (!map.has(key)) map.set(key, []);
			map.get(key)!.push(h);
		}
		return map;
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
		dayDialogDate = key;
		showDayDialog = true;
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
		toast.success('Absence saved.');
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
		return `${span.userFirstName} ${span.userLastName} — ${span.customTypeName ?? absenceTypeLabel(span.type)}${span.days > 1 ? ` (${span.days} days)` : ''}${span.isHalfDay ? ' (½)' : ''}${span.note ? ': ' + span.note : ''}`;
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

	function getAvailableAbsenceYears(): number[] {
		const years = new Set(absences.map((a) => parseInt((a.date ?? '').substring(0, 4))).filter((y) => !isNaN(y)));
		years.add(new Date().getFullYear());
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
	let holidayMap = $derived(buildHolidayMap(holidays));
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

	let dayNames = $derived(dayNamesShort());
	let today = dateKey(new Date());
</script>

<svelte:head>
	<title>{org ? `Absences - ${org.name}` : 'Absences'} - Time Tracking</title>
</svelte:head>

{#snippet personName(span: AbsenceSpan)}
	<span class="w-2.5 h-2.5 rounded-full shrink-0" style={personDotStyle(span.userId)} aria-hidden="true"></span>
	{span.userFirstName} {span.userLastName}
{/snippet}

<div class="max-w-5xl mx-auto sm:px-6 pb-6">
	<div class="flex items-center gap-2 mb-6">
		<a href="/organizations/{orgSlug}" class="text-base-content/60 no-underline text-sm hover:text-primary">&larr; {org?.name ?? 'Organization'}</a>
	</div>

	{#if loading}
		<div class="flex items-center gap-3 justify-center py-12 text-base-content/50"><span class="loading loading-spinner loading-sm"></span><span>Loading…</span></div>
	{:else if error}
		<div class="alert alert-error">{error}</div>
	{:else if org}
		<h1 class="text-2xl font-bold text-base-content mb-1">Absences</h1>
		<p class="text-base-content/60 text-sm mb-6 leading-relaxed">
			{#if canEdit}
				Calendar overview of absences and vacation days across all members. Click a day to add an absence.
			{:else if mustRequest}
				Calendar overview of absences visible to you. Click a day to request an absence — a group or org admin approves it.
			{:else if canAddSelf}
				Calendar overview of absences visible to you. Click a day to add your own absence.
			{:else}
				Calendar overview of absences visible to you.
			{/if}
		</p>

		{#if absencesError}
			<div class="alert alert-error text-sm mb-4" role="alert">
				<span>{absencesError}</span>
				<button class="btn btn-sm" onclick={loadAbsences}>Try again</button>
			</div>
		{/if}

		{@const calendarUserOptions = getCalendarUserOptions()}
		{@const monthLabel = formatDate(new Date(calendarYear, calendarMonth, 1), { month: 'long', year: 'numeric' })}

		<section aria-labelledby="calendar-heading">
			<!-- Calendar navigation -->
			<div class="flex items-center justify-between gap-2 mb-4">
				<button class="btn btn-ghost btn-sm" aria-label="Previous month" title="Previous month" onclick={() => navigateMonth(-1)}>
					<svg class="w-5 h-5" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true"><path fill-rule="evenodd" d="M12.79 5.23a.75.75 0 01-.02 1.06L8.832 10l3.938 3.71a.75.75 0 11-1.04 1.08l-4.5-4.25a.75.75 0 010-1.08l4.5-4.25a.75.75 0 011.06.02z" clip-rule="evenodd"/></svg>
				</button>
				<div class="flex items-center gap-2 sm:gap-3 flex-wrap justify-center">
					<h2 id="calendar-heading" class="text-lg sm:text-xl font-bold text-base-content m-0" aria-live="polite">{monthLabel}</h2>
					<button class="btn btn-ghost btn-xs" onclick={goToToday}>Today</button>
				</div>
				<button class="btn btn-ghost btn-sm" aria-label="Next month" title="Next month" onclick={() => navigateMonth(1)}>
					<svg class="w-5 h-5" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true"><path fill-rule="evenodd" d="M7.21 14.77a.75.75 0 01.02-1.06L11.168 10 7.23 6.29a.75.75 0 111.04-1.08l4.5 4.25a.75.75 0 010 1.08l-4.5 4.25a.75.75 0 01-1.06-.02z" clip-rule="evenodd"/></svg>
				</button>
			</div>

			<!-- Calendar member filter + mobile add -->
			<div class="flex items-center justify-end gap-2 mb-3 flex-wrap">
				{#if calendarUserOptions.length > 1}
					<label class="text-xs text-base-content/60" for="calendar-user-filter">Show</label>
					<select id="calendar-user-filter" bind:value={calendarUserFilter} class="select select-bordered select-sm max-w-[14rem]">
						<option value={null}>All members</option>
						{#each calendarUserOptions as u (u.id)}
							<option value={u.id}>{u.name}{u.id === auth.user?.id ? ' (you)' : ''}</option>
						{/each}
					</select>
					{#if calendarUserFilter !== null}
						<button class="btn btn-ghost btn-xs" onclick={() => (calendarUserFilter = null)}>Clear</button>
					{/if}
				{/if}
				{#if canOpenDayDialog}
					<button class="btn btn-primary btn-sm sm:hidden" onclick={openDialogForVisibleMonth}>
						{mustRequest ? 'Request absence' : 'Add absence'}
					</button>
				{/if}
			</div>

			<!-- Mobile: agenda list for the visible month -->
			<div class="sm:hidden">
				{#if absencesLoading}
					<p class="text-base-content/50 text-sm">Loading…</p>
				{:else if agenda.length === 0}
					<p class="text-base-content/50 text-sm text-center py-6 bg-base-200/30 rounded-lg border border-base-300">No absences or holidays this month.</p>
				{:else}
					<ul class="flex flex-col gap-1.5">
						{#each agenda as item (item.kind === 'holiday' ? `h${item.holiday.id}-${item.date}` : `a${spanKey(item.span)}`)}
							{#if item.kind === 'holiday'}
								<li class="flex items-center gap-2 p-2.5 rounded-lg border border-base-300 text-sm {DAY_TYPE_CLASSES.holiday.soft}">
									<span class="w-2.5 h-2.5 rounded-full shrink-0 {DAY_TYPE_CLASSES.holiday.dot}" aria-hidden="true"></span>
									<span class="text-base-content/60 shrink-0">{formatDate(item.date, { weekday: 'short', day: 'numeric', month: 'short' })}</span>
									<span class="font-medium truncate">{item.holiday.name}</span>
									{#if item.holiday.isHalfDay}<span class="badge badge-xs badge-outline" title="Half day">½</span>{/if}
								</li>
							{:else}
								{@const span = item.span}
								<li class="flex flex-col gap-1 p-2.5 bg-base-100 rounded-lg border border-base-300 text-sm">
									<div class="flex items-center justify-between gap-2">
										<span class="text-base-content/60">
											{span.days === 1
												? formatDate(span.startDate, { weekday: 'short', day: 'numeric', month: 'short' })
												: `${formatDateShort(span.startDate)} – ${formatDateShort(span.endDate)}`}
										</span>
										<span class="flex items-center gap-1">
											{#if span.days > 1}<span class="badge badge-xs badge-outline">{span.days} days</span>{/if}
											{#if span.isHalfDay}<span class="badge badge-xs badge-outline" title="Half day">½</span>{/if}
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
									{#if span.note}<span class="text-xs text-base-content/50 truncate">{span.note}</span>{/if}
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
						<div class="text-center text-xs font-semibold py-2 {i >= 5 ? 'text-base-content/40' : 'text-base-content/60'}">{dow}</div>
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
								{@const clickable = canOpenDayDialog && inMonth}
								<!-- role is "button" whenever the cell is focusable -->
								<!-- svelte-ignore a11y_no_noninteractive_tabindex -->
								<div
									class="min-h-[60px] p-1 pt-1.5 pb-3 flex flex-col transition-colors {clickable ? 'cursor-pointer hover:bg-primary/5 focus-visible:outline-2 focus-visible:outline-primary' : ''} {inMonth && !isWeekend ? 'bg-base-100' : ''} {inMonth && isWeekend ? 'bg-base-200/40' : ''} {!inMonth ? 'bg-base-200/70' : ''}"
									role={clickable ? 'button' : undefined}
									tabindex={clickable ? 0 : undefined}
									aria-label={clickable ? `${mustRequest ? 'Request absence' : 'Add absence'}: ${formatDate(date)}` : undefined}
									onclick={() => { if (clickable) openDayDialog(key); }}
									onkeydown={(e) => { if (clickable && (e.key === 'Enter' || e.key === ' ')) { e.preventDefault(); openDayDialog(key); } }}
								>
									<div class="flex items-center justify-between mb-1.5">
										<span class="text-[10px] font-medium leading-none {isToday ? 'bg-primary text-primary-content rounded-full w-5 h-5 flex items-center justify-center' : ''} {!inMonth ? 'text-base-content/30' : isWeekend ? 'text-base-content/50' : 'text-base-content'}">
											{date.getDate()}
										</span>
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
															class="h-[16px] flex items-center text-[9px] leading-none px-1 border truncate no-underline text-base-content hover:brightness-95 transition-all {chipClass(entry.span)} {shape}"
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

			<!-- Legend -->
			<div class="flex flex-wrap gap-x-4 gap-y-1 mt-3 text-xs text-base-content/60">
				<span class="flex items-center gap-1"><span class="w-3 h-3 rounded {DAY_TYPE_CLASSES.sick.dot}"></span> Sick day</span>
				<span class="flex items-center gap-1"><span class="w-3 h-3 rounded {DAY_TYPE_CLASSES['other-absence'].dot}"></span> Other</span>
				<span class="flex items-center gap-1"><span class="w-3 h-3 rounded {DAY_TYPE_CLASSES.holiday.dot}"></span> Holiday</span>
				<span class="text-base-content/50">Vacation colours vary by person</span>
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
		{@const vacationVisibleMembers = (org.members ?? []).filter((m) => m.vacationVisible && (m.vacationDaysPerYear ?? 0) > 0)}
		{#if canEdit || vacationVisibleMembers.length > 0}
		<section class="mt-8 bg-base-200/30 rounded-lg p-4 sm:p-5 border border-base-300">
			<div class="flex items-center justify-between flex-wrap gap-2 mb-4">
				<h2 class="text-xl font-bold text-base-content m-0">Vacation days balance</h2>
				<div class="flex items-center gap-4 text-xs text-base-content/60">
					<span class="flex items-center gap-1"><span class="w-3 h-3 rounded-sm bg-primary"></span> Taken</span>
					<span class="flex items-center gap-1"><span class="w-3 h-3 rounded-sm bg-primary/40"></span> Planned</span>
				</div>
			</div>
			{#if vacationVisibleMembers.length === 0}
				<p class="text-base-content/50 text-sm">No members have vacation days configured.</p>
			{:else}
				{@const membersWithVacation = vacationVisibleMembers
					.filter((m) => calendarUserFilter === null || m.id === calendarUserFilter)
					.sort((a, b) => ((a.vacationDaysRemaining ?? 0) - (b.vacationDaysRemaining ?? 0)))}
				{#if membersWithVacation.length === 0}
					<p class="text-base-content/50 text-sm">The selected member has no vacation days configured.</p>
				{:else}
				<div class="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
					{#each membersWithVacation as member (member.id)}
						{@const total = member.vacationDaysPerYear ?? 0}
						{@const taken = member.vacationDaysTaken ?? 0}
						{@const planned = member.vacationDaysPlanned ?? 0}
						{@const remaining = member.vacationDaysRemaining ?? total}
						{@const takenPct = total > 0 ? Math.min(100, (taken / total) * 100) : 0}
						{@const plannedPct = total > 0 ? Math.min(100 - takenPct, (planned / total) * 100) : 0}
						{@const pct = Math.round(takenPct + plannedPct)}
						<a href="/organizations/{orgSlug}/members/{member.id}" class="flex items-start gap-3 p-3 bg-base-100 rounded-lg border border-base-300 hover:border-primary/30 hover:shadow-sm transition-all no-underline text-base-content">
							<div class="w-9 h-9 rounded-full bg-primary text-primary-content flex items-center justify-center text-xs font-semibold shrink-0 mt-0.5" aria-hidden="true">
								{getInitials(member.firstName, member.lastName)}
							</div>
							<div class="flex-1 min-w-0">
								<div class="font-semibold text-sm truncate">{member.firstName} {member.lastName}</div>
								<div class="flex items-center gap-2 mt-1">
									<div class="flex-1 h-2 bg-base-200 rounded-full overflow-hidden flex" title="{taken}d taken, {planned}d planned">
										<div class="h-full {pct >= 90 ? 'bg-error' : pct >= 70 ? 'bg-warning' : 'bg-primary'}" style="width: {takenPct}%"></div>
										<div class="h-full {pct >= 90 ? 'bg-error/40' : pct >= 70 ? 'bg-warning/40' : 'bg-primary/40'}" style="width: {plannedPct}%"></div>
									</div>
									<span class="text-xs text-base-content/60 shrink-0">{pct}%</span>
								</div>
								<div class="flex justify-between gap-2 mt-1 text-xs text-base-content/60">
									<span>{taken}d taken{planned > 0 ? ` · ${planned}d planned` : ''} / {total}d</span>
									<span class="font-semibold shrink-0 {remaining <= 2 ? 'text-error' : remaining <= 5 ? 'text-warning' : 'text-success'}">{remaining}d left</span>
								</div>
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
			<h2 class="text-xl font-bold text-base-content mb-4">Absence list</h2>
			<div class="flex flex-wrap gap-2 mb-4">
				<select bind:value={selectedListYear} class="select select-bordered select-sm w-auto" aria-label="Year">
					{#each getAvailableAbsenceYears() as y (y)}
						<option value={y}>{y}</option>
					{/each}
				</select>
				{#if canEdit}
				<select bind:value={adminAbsenceFilter} class="select select-bordered select-sm w-auto max-w-full" aria-label="Member">
					<option value={null}>All members</option>
					{#each (org?.members ?? []) as m (m.id)}
						<option value={m.id}>{m.firstName} {m.lastName}</option>
					{/each}
				</select>
				{/if}
				<select bind:value={panelTypeFilter} class="select select-bordered select-sm w-auto" aria-label="Type">
					<option value="all">All types</option>
					<option value="Vacation">Vacation</option>
					<option value="SickDay">Sick day</option>
					<option value="Other">Other</option>
				</select>
			</div>
			{#if absencesLoading}
				<p class="text-base-content/50 text-sm">Loading…</p>
			{:else}
				{@const yearStr = String(selectedListYear)}
				{@const filtered = absences
					.filter((a) => (a.date ?? '').startsWith(yearStr))
					.filter((a) => !adminAbsenceFilter || a.userId === adminAbsenceFilter)
					.filter((a) => panelTypeFilter === 'all' || a.type === panelTypeFilter)}
				{@const spans = buildAbsenceSpans(filtered)}
				{#if spans.length === 0}
					<p class="text-base-content/50 text-sm text-center py-4">No absences found for {selectedListYear}.</p>
				{:else}
					<ul class="flex flex-col gap-1.5">
						{#each spans as span (spanKey(span))}
							<li class="flex items-center gap-x-3 gap-y-1 p-2.5 bg-base-100 rounded-lg border border-base-300 text-sm flex-wrap">
								<span class="text-base-content/60 shrink-0 sm:min-w-[190px]">{spanDateLabel(span)}</span>
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
									<span class="badge badge-sm badge-outline">{span.days} days</span>
								{/if}
								{#if span.isHalfDay && span.days === 1}
									<span class="badge badge-sm badge-outline">½ day</span>
								{/if}
								{#if span.note}
									<span class="text-base-content/50 truncate min-w-0 flex-1">{span.note}</span>
								{/if}
								{#if canDeleteSpan(span)}
									<button
										class="btn btn-ghost btn-xs text-error ml-auto"
										aria-label="Delete absence"
										title="Delete absence"
										disabled={deletingSpan === spanKey(span)}
										onclick={() => deleteSpan(span)}
									>{#if deletingSpan === spanKey(span)}<span class="loading loading-spinner loading-xs"></span>{:else}&times;{/if}</button>
								{/if}
							</li>
						{/each}
					</ul>
					<p class="text-xs text-base-content/50 mt-2">{spans.length} entries ({filtered.length} days total)</p>
				{/if}
			{/if}
		</section>
	{/if}
</div>
