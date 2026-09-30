<script lang="ts">
	import { untrack } from 'svelte';
	import { page } from '$app/state';
	import { auth } from '$lib/stores/auth.svelte';
	import { organizationsApi, holidayApi, absenceDayApi, groupApi } from '$lib/apiClient';
	import { absenceTypeLabel } from '$lib/utils/formatters';
	import AbsenceDialog from '$lib/components/AbsenceDialog.svelte';
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
	let canEdit = $derived(myRole === 'Owner' || myRole === 'Admin');

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
	let absencesLoaded = $state(false);
	let adminAbsenceFilter = $state<number | null>(null);
	let calendarUserFilter = $state<number | null>(null);
	let panelTypeFilter = $state<string>('all');
	let calendarMonth = $state(new Date().getMonth());
	let calendarYear = $state(new Date().getFullYear());
	let selectedListYear = $state(new Date().getFullYear());

	// Day-click add-absence dialog
	let showDayDialog = $state(false);
	let dayDialogDate = $state('');
	let requestSentMessage = $state('');

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
			if (getErrorStatus(err) === 404) { error = 'Organization not found.'; }
			else { error = 'Failed to load organization.'; }
		} finally {
			loading = false;
		}
	}

	async function loadEffectivePermissions() {
		try {
			const { data } = await groupApi.apiV1OrganizationsSlugEffectivePermissionsGet(orgSlug);
			effectivePerms = data;
		} catch { effectivePerms = null; }
	}

	async function loadAbsenceTypes() {
		try {
			const { data } = await absenceDayApi.apiV1OrganizationsSlugAbsenceTypesGet(orgSlug);
			absenceTypes = data;
		} catch { absenceTypes = []; }
	}

	async function reloadOrg() {
		try {
			const { data } = await organizationsApi.apiV1OrganizationsSlugGet(orgSlug);
			org = data;
		} catch {}
	}

	async function loadHolidays() {
		try {
			const { data } = await holidayApi.apiV1OrganizationsSlugHolidaysGet(orgSlug);
			holidays = (data as HolidayResponse[]).sort((a, b) => (a.date ?? '').localeCompare(b.date ?? ''));
		} catch {
			holidays = [];
		}
	}

	async function loadAbsences() {
		absencesLoading = true;
		try {
			const { data } = await absenceDayApi.apiV1OrganizationsSlugAbsencesGet(orgSlug, undefined, undefined, undefined, 200);
			absences = [...(data.items ?? [])].sort((a, b) => (b.date ?? '').localeCompare(a.date ?? ''));
			absencesLoaded = true;
		} catch {
			absences = [];
		} finally {
			absencesLoading = false;
		}
	}

	async function deleteAbsence(id: number) {
		if (!confirm('Delete this absence?')) return;
		try {
			await absenceDayApi.apiV1OrganizationsSlugAbsencesIdDelete(orgSlug, id);
			absencesLoaded = false;
			await loadAbsences();
			await reloadOrg();
		} catch {}
	}

	function absenceColor(type: string | null | undefined): string {
		switch (type) {
			case 'Vacation': return 'bg-info/20 text-info border-info/30';
			case 'SickDay': return 'bg-error/20 text-error border-error/30';
			default: return 'bg-warning/20 text-warning border-warning/30';
		}
	}

	// Calendar helpers
	function calendarDays(year: number, month: number): { date: Date; inMonth: boolean }[] {
		const first = new Date(year, month, 1);
		const last = new Date(year, month + 1, 0);
		const startDay = (first.getDay() + 6) % 7;
		const days: { date: Date; inMonth: boolean }[] = [];
		for (let i = startDay - 1; i >= 0; i--) {
			const d = new Date(year, month, -i);
			days.push({ date: d, inMonth: false });
		}
		for (let d = 1; d <= last.getDate(); d++) {
			days.push({ date: new Date(year, month, d), inMonth: true });
		}
		while (days.length % 7 !== 0) {
			const d = new Date(year, month + 1, days.length - last.getDate() - startDay + 1);
			days.push({ date: d, inMonth: false });
		}
		return days;
	}

	function dateToKey(d: Date): string {
		return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
	}

	function buildHolidayMap(holidayList: HolidayResponse[]): Map<string, HolidayResponse[]> {
		const map = new Map<string, HolidayResponse[]>();
		for (const h of holidayList) {
			if (!h.date) continue;
			if (h.isRecurring) {
				// For recurring holidays, add entries for the visible calendar year
				const monthDay = h.date.substring(5); // "MM-DD"
				const key = `${calendarYear}-${monthDay}`;
				if (!map.has(key)) map.set(key, []);
				map.get(key)!.push(h);
			} else {
				const key = h.date;
				if (!map.has(key)) map.set(key, []);
				map.get(key)!.push(h);
			}
		}
		return map;
	}

	function navigateMonth(delta: number) {
		let m = calendarMonth + delta;
		let y = calendarYear;
		if (m < 0) { m = 11; y--; }
		else if (m > 11) { m = 0; y++; }
		calendarMonth = m;
		calendarYear = y;
	}

	function openDayDialog(dateKey: string) {
		dayDialogDate = dateKey;
		showDayDialog = true;
	}

	async function onAbsenceSaved(requestMessage?: string) {
		if (requestMessage) {
			requestSentMessage = requestMessage;
			setTimeout(() => (requestSentMessage = ''), 6000);
			return;
		}
		absencesLoaded = false;
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

	function buildSpanMap(absenceList: AbsenceDayResponse[]): Map<string, SpanEntry[]> {
		const spans = buildAbsenceSpans(absenceList);
		const map = new Map<string, SpanEntry[]>();
		for (const span of spans) {
			if (span.days === 1) {
				const entries = map.get(span.startDate) ?? [];
				entries.push({ span, position: 'single' });
				map.set(span.startDate, entries);
			} else {
				const dates = absenceList
					.filter(a => span.ids.includes(a.id ?? -1))
					.map(a => a.date ?? '')
					.sort();
				for (let i = 0; i < dates.length; i++) {
					const pos: SpanEntry['position'] = i === 0 ? 'start' : i === dates.length - 1 ? 'end' : 'middle';
					const entries = map.get(dates[i]) ?? [];
					entries.push({ span, position: pos });
					map.set(dates[i], entries);
				}
			}
		}
		return map;
	}

	const userColorPalette = [
		{ bg: 'bg-sky-200/60 border-sky-400/60', text: 'text-sky-900' },
		{ bg: 'bg-violet-200/60 border-violet-400/60', text: 'text-violet-900' },
		{ bg: 'bg-teal-200/60 border-teal-400/60', text: 'text-teal-900' },
		{ bg: 'bg-amber-200/60 border-amber-400/60', text: 'text-amber-900' },
		{ bg: 'bg-rose-200/60 border-rose-400/60', text: 'text-rose-900' },
		{ bg: 'bg-emerald-200/60 border-emerald-400/60', text: 'text-emerald-900' },
		{ bg: 'bg-indigo-200/60 border-indigo-400/60', text: 'text-indigo-900' },
		{ bg: 'bg-orange-200/60 border-orange-400/60', text: 'text-orange-900' },
		{ bg: 'bg-cyan-200/60 border-cyan-400/60', text: 'text-cyan-900' },
		{ bg: 'bg-fuchsia-200/60 border-fuchsia-400/60', text: 'text-fuchsia-900' },
		{ bg: 'bg-lime-200/60 border-lime-400/60', text: 'text-lime-900' },
		{ bg: 'bg-pink-200/60 border-pink-400/60', text: 'text-pink-900' },
		{ bg: 'bg-blue-200/60 border-blue-400/60', text: 'text-blue-900' },
	];

	function userSpanBg(userId: number, type: string | null | undefined): string {
		if (type === 'SickDay') return 'bg-error/25 border-error/40';
		if (type === 'Other') return 'bg-warning/25 border-warning/40';
		const idx = (userId ?? 0) % userColorPalette.length;
		return userColorPalette[idx].bg;
	}

	function userSpanText(userId: number, type: string | null | undefined): string {
		if (type === 'SickDay') return 'text-error';
		if (type === 'Other') return 'text-warning';
		const idx = (userId ?? 0) % userColorPalette.length;
		return userColorPalette[idx].text;
	}

	interface WeekLane {
		userId: number;
		userFirstName: string;
		userLastName: string;
	}

	function getWeeks(days: { date: Date; inMonth: boolean }[]): { date: Date; inMonth: boolean }[][] {
		const weeks: { date: Date; inMonth: boolean }[][] = [];
		for (let i = 0; i < days.length; i += 7) {
			weeks.push(days.slice(i, i + 7));
		}
		return weeks;
	}

	function getWeekLanes(weekDays: { date: Date }[], spanMap: Map<string, SpanEntry[]>, holidayMap: Map<string, HolidayResponse[]>): WeekLane[] {
		const seen = new Map<number, WeekLane>();
		let hasHoliday = false;
		for (const { date } of weekDays) {
			const key = dateToKey(date);
			if ((holidayMap.get(key) ?? []).length > 0) hasHoliday = true;
			for (const entry of (spanMap.get(key) ?? [])) {
				if (!seen.has(entry.span.userId)) {
					seen.set(entry.span.userId, {
						userId: entry.span.userId,
						userFirstName: entry.span.userFirstName,
						userLastName: entry.span.userLastName,
					});
				}
			}
		}
		const lanes = [...seen.values()].sort((a, b) => a.userId - b.userId);
		// Reserve lane 0 for holidays if the week has any
		if (hasHoliday) {
			lanes.unshift({ userId: -1, userFirstName: 'Holiday', userLastName: '' });
		}
		return lanes;
	}

	function getLaneEntry(dayKey: string, lane: WeekLane, spanMap: Map<string, SpanEntry[]>): SpanEntry | null {
		const entries = spanMap.get(dayKey) ?? [];
		return entries.find(e => e.span.userId === lane.userId) ?? null;
	}

	function getAvailableAbsenceYears(): number[] {
		const years = [...new Set(absences.map(a => (a.date ?? '').substring(0, 4)).filter(y => y))].sort((a, b) => b.localeCompare(a));
		return years.length > 0 ? years.map(y => parseInt(y)) : [new Date().getFullYear()];
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
</script>

<svelte:head>
	<title>{org ? `Absences - ${org.name}` : 'Absences'} - Time Tracking</title>
</svelte:head>

<div class="max-w-5xl mx-auto px-6 pb-6">
	<div class="flex items-center gap-2 mb-6">
		<a href="/organizations/{orgSlug}" class="text-base-content/60 no-underline text-sm hover:text-primary">&larr; {org?.name ?? 'Organization'}</a>
	</div>

	{#if loading}
		<div class="flex items-center gap-3 justify-center py-12 text-base-content/40"><span class="loading loading-spinner loading-sm"></span><span>Loading...</span></div>
	{:else if error}
		<div class="alert alert-error">{error}</div>
	{:else if org}
		<h1 class="text-2xl font-bold text-base-content mb-1">Absences</h1>
		<p class="text-base-content/50 text-sm mb-6 leading-relaxed">
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

		{#if requestSentMessage}
			<div class="alert alert-success text-sm py-2 px-3 mb-4">{requestSentMessage}</div>
		{/if}

		<!-- Calendar Navigation -->
		{@const calendarAbsences = calendarUserFilter === null ? absences : absences.filter((a) => a.userId === calendarUserFilter)}
		{@const calendarUserOptions = getCalendarUserOptions()}
		{@const spanMap = buildSpanMap(calendarAbsences)}
		{@const holidayMap = buildHolidayMap(holidays)}
		{@const days = calendarDays(calendarYear, calendarMonth)}
		{@const monthLabel = new Date(calendarYear, calendarMonth).toLocaleDateString('en-US', { month: 'long', year: 'numeric' })}
		{@const today = dateToKey(new Date())}

		<section>
			<div class="flex items-center justify-between mb-4">
				<button class="btn btn-ghost btn-sm" aria-label="Previous month" onclick={() => navigateMonth(-1)}>
					<svg class="w-5 h-5" viewBox="0 0 20 20" fill="currentColor"><path fill-rule="evenodd" d="M12.79 5.23a.75.75 0 01-.02 1.06L8.832 10l3.938 3.71a.75.75 0 11-1.04 1.08l-4.5-4.25a.75.75 0 010-1.08l4.5-4.25a.75.75 0 011.06.02z" clip-rule="evenodd"/></svg>
				</button>
				<div class="flex items-center gap-3">
					<h2 class="text-xl font-bold text-base-content">{monthLabel}</h2>
					<button class="btn btn-ghost btn-xs" onclick={() => { calendarMonth = new Date().getMonth(); calendarYear = new Date().getFullYear(); }}>Today</button>
				</div>
				<div class="flex items-center gap-1">
					<button class="btn btn-ghost btn-sm" aria-label="Next month" onclick={() => navigateMonth(1)}>
						<svg class="w-5 h-5" viewBox="0 0 20 20" fill="currentColor"><path fill-rule="evenodd" d="M7.21 14.77a.75.75 0 01.02-1.06L11.168 10 7.23 6.29a.75.75 0 111.04-1.08l4.5 4.25a.75.75 0 010 1.08l-4.5 4.25a.75.75 0 01-1.06-.02z" clip-rule="evenodd"/></svg>
					</button>
				</div>
			</div>

			<!-- Calendar member filter -->
			{#if calendarUserOptions.length > 1}
				<div class="flex items-center justify-end gap-2 mb-3">
					<label class="text-xs text-base-content/50" for="calendar-user-filter">Show</label>
					<select id="calendar-user-filter" bind:value={calendarUserFilter} class="select select-bordered select-sm">
						<option value={null}>All members</option>
						{#each calendarUserOptions as u}
							<option value={u.id}>{u.name}{u.id === auth.user?.id ? ' (You)' : ''}</option>
						{/each}
					</select>
					{#if calendarUserFilter !== null}
						<button class="btn btn-ghost btn-xs" onclick={() => (calendarUserFilter = null)}>Clear</button>
					{/if}
				</div>
			{/if}

			<!-- Day-of-week headers -->
			<div class="grid grid-cols-7 gap-px mb-px">
				{#each ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'] as dow}
					<div class="text-center text-xs font-semibold text-base-content/50 py-2 {dow === 'Sat' || dow === 'Sun' ? 'text-base-content/30' : ''}">{dow}</div>
				{/each}
			</div>

			<!-- Calendar grid (week-based with stable lanes) -->
			<div class="border border-base-300 rounded-lg overflow-hidden">
				{#each getWeeks(days) as weekDays, wi}
					{@const weekLanes = getWeekLanes(weekDays, spanMap, holidayMap)}
					<div class="grid grid-cols-7 gap-px bg-base-300/50 {wi > 0 ? 'border-t-2 border-base-300' : ''}">
						{#each weekDays as { date, inMonth }}
							{@const key = dateToKey(date)}
							{@const isToday = key === today}
							{@const isWeekend = date.getDay() === 0 || date.getDay() === 6}
							<!-- svelte-ignore a11y_click_events_have_key_events -->
							<!-- svelte-ignore a11y_no_static_element_interactions -->
							<div
							class="min-h-[60px] p-1 pt-1.5 pb-3 flex flex-col {canOpenDayDialog ? 'cursor-pointer hover:bg-primary/5' : ''} transition-colors {inMonth && !isWeekend ? 'bg-primary/[0.03]' : ''} {inMonth && isWeekend ? 'bg-base-200/30' : ''} {!inMonth ? 'bg-base-200/50' : ''}"
							onclick={() => { if (canOpenDayDialog && inMonth) openDayDialog(key); }}
							title={canEdit ? `Click to add absence on ${key}` : mustRequest ? `Click to request absence on ${key}` : canAddSelf ? `Click to add your absence on ${key}` : ''}
							>
								<div class="flex items-center justify-between mb-1.5">
									<span class="text-[10px] font-medium leading-none {isToday ? 'bg-primary text-primary-content rounded-full w-5 h-5 flex items-center justify-center' : ''} {inMonth ? 'text-base-content' : 'text-base-content/25'} {isWeekend && inMonth ? 'text-base-content/40' : ''}">
										{date.getDate()}
									</span>
								</div>
								<div class="flex flex-col gap-[2px] mt-1 min-h-[48px]">
									{#each weekLanes as lane}
										{#if lane.userId === -1}
											{@const dayH = holidayMap.get(key) ?? []}
											{#if dayH.length > 0}
												<div class="h-[16px] text-[9px] leading-none flex items-center px-1 rounded bg-success/15 text-success border border-success/20 truncate" title={dayH.map(h => h.name).join(', ')}>
													{dayH[0].name}
												</div>
											{:else}
												<div class="h-[16px]"></div>
											{/if}
										{:else}
										{@const entry = getLaneEntry(key, lane, spanMap)}
										{#if entry}
											<!-- svelte-ignore a11y_click_events_have_key_events -->
											{#if canEdit}
											<a
												href="/organizations/{orgSlug}/members/{entry.span.userId}"
												onclick={(e) => e.stopPropagation()}
												class="h-[16px] flex items-center text-[9px] leading-none px-1 border truncate no-underline hover:brightness-90 transition-all cursor-pointer {userSpanBg(entry.span.userId, entry.span.type)} {userSpanText(entry.span.userId, entry.span.type)} {entry.position === 'start' ? 'rounded-l border-r-0 -mr-1' : entry.position === 'end' ? 'rounded-r border-l-0 -ml-1' : entry.position === 'single' ? 'rounded' : 'border-x-0 -mx-1'}"
												title="{entry.span.userFirstName} {entry.span.userLastName} — {entry.span.customTypeName ?? absenceTypeLabel(entry.span.type)}{entry.span.days > 1 ? ` (${entry.span.days} days)` : ''}{entry.span.isHalfDay ? ' (½)' : ''}{entry.span.note ? ': ' + entry.span.note : ''}"
											>
												{#if entry.position === 'start' || entry.position === 'single'}
													{entry.span.userFirstName}{#if entry.span.days > 1}&nbsp;→{/if}{#if entry.span.isHalfDay && entry.position === 'single'}&nbsp;½{/if}
												{:else if entry.position === 'end'}
													→&nbsp;{entry.span.userFirstName}
												{:else}
													&nbsp;
												{/if}
											</a>
											{:else}
											<div
												class="h-[16px] flex items-center text-[9px] leading-none px-1 border truncate {userSpanBg(entry.span.userId, entry.span.type)} {userSpanText(entry.span.userId, entry.span.type)} {entry.position === 'start' ? 'rounded-l border-r-0 -mr-1' : entry.position === 'end' ? 'rounded-r border-l-0 -ml-1' : entry.position === 'single' ? 'rounded' : 'border-x-0 -mx-1'}"
												title="{entry.span.userFirstName} {entry.span.userLastName} — {entry.span.customTypeName ?? absenceTypeLabel(entry.span.type)}{entry.span.days > 1 ? ` (${entry.span.days} days)` : ''}{entry.span.isHalfDay ? ' (½)' : ''}{entry.span.note ? ': ' + entry.span.note : ''}"
											>
												{#if entry.position === 'start' || entry.position === 'single'}
													{entry.span.userFirstName}{#if entry.span.days > 1}&nbsp;→{/if}{#if entry.span.isHalfDay && entry.position === 'single'}&nbsp;½{/if}
												{:else if entry.position === 'end'}
													→&nbsp;{entry.span.userFirstName}
												{:else}
													&nbsp;
												{/if}
											</div>
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

			<!-- Legend -->
			<div class="flex flex-wrap gap-4 mt-3 text-xs text-base-content/60">
				<span class="flex items-center gap-1"><span class="w-3 h-3 rounded bg-error/20 border border-error/30"></span> Sick Day</span>
				<span class="flex items-center gap-1"><span class="w-3 h-3 rounded bg-warning/20 border border-warning/30"></span> Other</span>
				<span class="flex items-center gap-1"><span class="w-3 h-3 rounded bg-success/15 border border-success/20"></span> Holiday</span>
				<span class="text-base-content/40">Vacation colors vary by person</span>
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
		{@const vacationVisibleMembers = (org.members ?? []).filter(m => m.vacationVisible && (m.vacationDaysPerYear ?? 0) > 0)}
		{#if canEdit || vacationVisibleMembers.length > 0}
		<section class="mt-8 bg-base-200/30 rounded-lg p-5 border border-base-300">
			<div class="flex items-center justify-between flex-wrap gap-2 mb-4">
				<h2 class="text-xl font-bold text-base-content m-0">Vacation Days Balance</h2>
				<div class="flex items-center gap-4 text-xs text-base-content/60">
					<span class="flex items-center gap-1"><span class="w-3 h-3 rounded-sm bg-primary"></span> Taken</span>
					<span class="flex items-center gap-1"><span class="w-3 h-3 rounded-sm bg-primary/40"></span> Planned</span>
				</div>
			</div>
			{#if vacationVisibleMembers.length === 0}
				<p class="text-base-content/40 text-sm">No members have vacation days configured.</p>
			{:else}
				{@const membersWithVacation = vacationVisibleMembers
					.filter(m => calendarUserFilter === null || m.id === calendarUserFilter)
					.sort((a, b) => ((a.vacationDaysRemaining ?? 0) - (b.vacationDaysRemaining ?? 0)))}
				{#if membersWithVacation.length === 0}
					<p class="text-base-content/40 text-sm">The selected member has no vacation days configured.</p>
				{:else}
				<div class="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
					{#each membersWithVacation as member}
						{@const total = member.vacationDaysPerYear ?? 0}
						{@const taken = member.vacationDaysTaken ?? 0}
						{@const planned = member.vacationDaysPlanned ?? 0}
						{@const remaining = member.vacationDaysRemaining ?? total}
						{@const takenPct = total > 0 ? Math.min(100, (taken / total) * 100) : 0}
						{@const plannedPct = total > 0 ? Math.min(100 - takenPct, (planned / total) * 100) : 0}
						{@const pct = Math.round(takenPct + plannedPct)}
						<a href="/organizations/{orgSlug}/members/{member.id}" class="flex items-start gap-3 p-3 bg-base-100 rounded-lg border border-base-300 hover:border-primary/30 hover:shadow-sm transition-all no-underline text-base-content">
							<div class="w-9 h-9 rounded-full bg-gradient-to-br from-primary to-secondary text-primary-content flex items-center justify-center text-xs font-semibold shrink-0 mt-0.5">
								{(member.firstName?.[0] ?? '').toUpperCase()}{(member.lastName?.[0] ?? '').toUpperCase()}
							</div>
							<div class="flex-1 min-w-0">
								<div class="font-semibold text-sm truncate">{member.firstName} {member.lastName}</div>
								<div class="flex items-center gap-2 mt-1">
									<div class="flex-1 h-2 bg-base-200 rounded-full overflow-hidden flex" title="{taken}d taken, {planned}d planned">
										<div class="h-full {pct >= 90 ? 'bg-error' : pct >= 70 ? 'bg-warning' : 'bg-primary'}" style="width: {takenPct}%"></div>
										<div class="h-full {pct >= 90 ? 'bg-error/40' : pct >= 70 ? 'bg-warning/40' : 'bg-primary/40'}" style="width: {plannedPct}%"></div>
									</div>
									<span class="text-xs text-base-content/50 shrink-0">{pct}%</span>
								</div>
								<div class="flex justify-between mt-1 text-xs text-base-content/50">
									<span>{taken}d taken{planned > 0 ? ` · ${planned}d planned` : ''} / {total}d</span>
									<span class="font-semibold {remaining <= 2 ? 'text-error' : remaining <= 5 ? 'text-warning' : 'text-success'}">{remaining}d left</span>
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
		<section class="mt-8 bg-base-200/30 rounded-lg p-5 border border-base-300">
			<h2 class="text-xl font-bold text-base-content mb-4">Absence List</h2>
			<div class="flex flex-wrap gap-2 mb-4">
				<select bind:value={selectedListYear} class="select select-bordered select-sm">
					{#each getAvailableAbsenceYears() as y}
						<option value={y}>{y}</option>
					{/each}
				</select>
				{#if canEdit}
				<select bind:value={adminAbsenceFilter} class="select select-bordered select-sm">
					<option value={null}>All members</option>
					{#each (org?.members ?? []) as m}
						<option value={m.id}>{m.firstName} {m.lastName}</option>
					{/each}
				</select>
				{/if}
				<select bind:value={panelTypeFilter} class="select select-bordered select-sm">
					<option value="all">All types</option>
					<option value="Vacation">Vacation</option>
					<option value="SickDay">Sick Day</option>
					<option value="Other">Other</option>
				</select>
			</div>
			{#if absencesLoading}
				<p class="text-base-content/40 text-sm">Loading...</p>
			{:else}
				{@const yearStr = String(selectedListYear)}
				{@const filtered = absences
					.filter(a => (a.date ?? '').startsWith(yearStr))
					.filter(a => !adminAbsenceFilter || a.userId === adminAbsenceFilter)
					.filter(a => panelTypeFilter === 'all' || a.type === panelTypeFilter)}
				{@const spans = buildAbsenceSpans(filtered)}
				{#if spans.length === 0}
					<p class="text-base-content/40 text-sm text-center py-4">No absences found for {selectedListYear}.</p>
				{:else}
					<div class="flex flex-col gap-1.5">
						{#each spans as span}
							{@const dateLabel = span.days === 1
								? span.startDate
								: `${span.startDate} → ${span.endDate}`}
							<div class="flex items-center gap-3 p-2.5 bg-base-100 rounded-lg border border-base-300 text-sm">
								<span class="text-base-content/50 font-mono shrink-0 min-w-[180px]">{dateLabel}</span>
								{#if canEdit}
								<a href="/organizations/{orgSlug}/members/{span.userId}" class="flex items-center gap-1.5 font-medium link link-hover min-w-[130px]">
									<div class="w-6 h-6 rounded-full bg-gradient-to-br from-primary to-secondary text-primary-content flex items-center justify-center text-[10px] font-semibold shrink-0">
										{span.userFirstName[0]?.toUpperCase() ?? ''}{span.userLastName[0]?.toUpperCase() ?? ''}
									</div>
									{span.userFirstName} {span.userLastName}
								</a>
								{:else}
								<span class="flex items-center gap-1.5 font-medium min-w-[130px]">
									<div class="w-6 h-6 rounded-full bg-gradient-to-br from-primary to-secondary text-primary-content flex items-center justify-center text-[10px] font-semibold shrink-0">
										{span.userFirstName[0]?.toUpperCase() ?? ''}{span.userLastName[0]?.toUpperCase() ?? ''}
									</div>
									{span.userFirstName} {span.userLastName}
								</span>
								{/if}
								<span class="badge badge-sm {absenceColor(span.type)}">
									{span.customTypeName ?? absenceTypeLabel(span.type)}
								</span>
								{#if span.days > 1}
									<span class="badge badge-sm badge-outline">{span.days} days</span>
								{/if}
								{#if span.isHalfDay && span.days === 1}
									<span class="badge badge-sm badge-outline">½ day</span>
								{/if}
								{#if span.note}
									<span class="text-base-content/40 truncate">{span.note}</span>
								{/if}
							</div>
						{/each}
					</div>
					<p class="text-xs text-base-content/40 mt-2">{spans.length} entries ({filtered.length} days total)</p>
				{/if}
			{/if}
		</section>
	{/if}
</div>
