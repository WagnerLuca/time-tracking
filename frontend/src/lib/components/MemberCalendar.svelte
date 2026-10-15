<script lang="ts">
	import EmptyState from '$lib/components/EmptyState.svelte';
	import { getOrgDetail, invalidateOrgDetail } from '$lib/stores/orgDetail';
	import { onMount, tick, untrack } from 'svelte';
	import { auth } from '$lib/stores/auth.svelte';
	import { toast } from '$lib/stores/toast.svelte';
	import { organizationsApi, timeTrackingApi, workScheduleApi, absenceDayApi, groupApi, reportsApi, memberEntriesApi } from '$lib/apiClient';
	import type { TimeEntryResponse, WorkScheduleResponse, OrganizationDetailResponse, AbsenceTypeDefinitionResponse, EffectivePermissionsResponse, ComplianceDayResponse } from '$lib/api';
	import { formatHours, formatDelta, formatDate, formatDateFull, formatNumber, getMonthName, dayNamesShort, barWidth } from '$lib/utils/formatters';
	import { dateKey } from '$lib/utils/dateHelpers';
	import { getDayTarget, getAbsenceCredit, getDayType, getDayTypeLabel, type CustomCreditSets } from '$lib/utils/scheduleHelpers';
	import { extractErrorMessage, getErrorStatus } from '$lib/utils/errorHandler';
	import { balanceToMinutes, type BalanceMinutes } from '$lib/utils/balance';
	import { DAY_TYPE_CLASSES, dayTypeClasses, heatColor } from '$lib/utils/colors';
	import { isOrgAdmin, canEditEntries, canRequestEditEntries, canEditPause, canRequestEditPause } from '$lib/utils/orgRules';
	import { loadDaysOff as fetchDaysOff, emptyDaysOff, type DaysOffData } from '$lib/utils/daysOff';
	import { createEntryEditor } from '$lib/utils/entryEditor.svelte';
	import { fetchAllPages } from '$lib/utils/pagination';
	import { dayPress } from '$lib/utils/dayPress';
	import Skeleton from '$lib/components/Skeleton.svelte';
	import EntryRow from '$lib/components/EntryRow.svelte';
	import EntryEditForm from '$lib/components/EntryEditForm.svelte';
	import RequestModal from '$lib/components/RequestModal.svelte';
	import AbsenceDialog from '$lib/components/AbsenceDialog.svelte';
	import LockIcon from '$lib/components/LockIcon.svelte';
	import { closedMonthSet, formatMonth, isClosedDay, lockText } from '$lib/utils/closedPeriods';
	import { t, tp } from '$lib/i18n';

	/**
	 * Shared month/year calendar of a member's tracked time, absences and holidays.
	 * Used by the personal History page (isSelf) and the member detail page.
	 */
	interface Props {
		orgSlug: string;
		orgId?: number | null;
		userId: number;
		/** When true, uses the caller's own endpoints and enables entry editing. */
		isSelf?: boolean;
		/** Optional preloaded org detail (avoids an extra fetch). */
		orgDetail?: OrganizationDetailResponse | null;
		/** Deep link: open this day ('YYYY-MM-DD') … */
		focusDate?: string | null;
		/** … and pre-open the editor (or edit request) for this entry. */
		focusEntryId?: number | null;
		/** … or open the "add entry" form / the absence dialog for that day (dashboard quick actions). */
		focusAction?: 'add' | 'absence' | null;
		/** Called whenever the balance was (re)loaded, e.g. after an entry edit. */
		onbalance?: (balance: BalanceMinutes | null) => void;
		/** Admin corrections: the caller may add, edit and delete this member's entries (not with isSelf). */
		canCorrect?: boolean;
	}
	let { orgSlug, orgId = null, userId, isSelf = false, orgDetail: orgDetailProp = null, focusDate = null, focusEntryId = null, focusAction = null, onbalance, canCorrect = false }: Props = $props();
	/** Correcting someone else's entries through the admin endpoints. */
	const correcting = $derived(!isSelf && canCorrect);

	// View mode
	let viewMode = $state<'month' | 'year'>('month');

	// Navigation
	let currentYear = $state(new Date().getFullYear());
	let currentMonth = $state(new Date().getMonth()); // 0-indexed

	// Data
	let loading = $state(true);
	let loadError = $state('');
	let entries = $state<TimeEntryResponse[]>([]);
	let workSchedule = $state<WorkScheduleResponse | null>(null);
	let schedulePeriods = $state<WorkScheduleResponse[]>([]);

	// Day-type maps
	let daysOff = $state<DaysOffData>(emptyDaysOff());
	let holidayDates = $derived(daysOff.holidayDates);
	let halfDayHolidays = $derived(daysOff.halfDayHolidays);
	let sickDayDates = $derived(daysOff.sickDayDates);
	let vacationDates = $derived(daysOff.vacationDates);
	let otherAbsenceDates = $derived(daysOff.otherAbsenceDates);
	let halfDayAbsences = $derived(daysOff.halfDayAbsences);
	let customTypeDates = $derived(daysOff.customTypeDates);
	let customCredits = $derived<CustomCreditSets>({ noCredit: daysOff.customNoCreditDates, topUp: daysOff.customTopUpDates });

	// Year view data
	let yearEntries = $state<TimeEntryResponse[]>([]);
	let yearLoading = $state(false);

	/** Cumulative balance as of today (balance endpoint); null when not visible to the caller. */
	let balance = $state<BalanceMinutes | null>(null);
	let selectedDayKey = $state<string | null>(null);
	let fetchedOrgDetail = $state<OrganizationDetailResponse | null>(null);
	const orgDetail = $derived(orgDetailProp ?? fetchedOrgDetail);
	let dayDetailsEl = $state<HTMLElement>();

	// Day-click add-absence popup
	let absenceTypes = $state<AbsenceTypeDefinitionResponse[]>([]);
	let effectivePerms = $state<EffectivePermissionsResponse | null>(null);
	let showAbsenceDialog = $state(false);
	let absenceDialogDate = $state('');

	let myRole = $derived(orgDetail?.members?.find((m) => m.id === auth.user?.id)?.role ?? null);
	let isAdmin = $derived(isOrgAdmin(myRole));
	/** Closed months (issue #57): marked for everyone; locked for non-admins (admins change them with a reason). */
	const closed = $derived(closedMonthSet(orgDetail?.closedMonths));
	const lockedDay = (key: string) => !isAdmin && isClosedDay(key, closed);
	let selfAbsenceMode = $derived(effectivePerms?.absenceSelfServiceMode ?? orgDetail?.absenceSelfServiceMode ?? 'Allowed');
	/** Admins can add for the viewed person; members can add/request for themselves. */
	let canAddAbsence = $derived(isAdmin || (isSelf && selfAbsenceMode !== 'Disabled'));
	let mustRequest = $derived(!isAdmin && isSelf && selfAbsenceMode === 'RequiresApproval');

	/** Entry permissions: own entries by the org rules (isSelf), someone else's only as an admin correction. */
	const entryPerms = $derived(
		correcting
			? { canEdit: true, canRequestEdit: false, canEditPause: true, canRequestEditPause: false }
			: {
				canEdit: isSelf && canEditEntries(effectivePerms, orgDetail),
				canRequestEdit: isSelf && canRequestEditEntries(effectivePerms, orgDetail),
				canEditPause: isSelf && canEditPause(effectivePerms, orgDetail),
				canRequestEditPause: isSelf && canRequestEditPause(effectivePerms, orgDetail)
			}
	);
	/** Adding a finished entry by hand: own entries follow the "edit past entries" rule (approval → request). */
	const canAddEntry = $derived(correcting || entryPerms.canEdit || entryPerms.canRequestEdit);
	const addEntryNeedsApproval = $derived(!correcting && !entryPerms.canEdit && entryPerms.canRequestEdit);

	// Shared entry editing + request logic (exposed in the UI for own entries and admin corrections)
	const editor = createEntryEditor({
		getOrgDetail: () => orgDetail,
		getPermissions: () => effectivePerms,
		getOrgSlug: () => orgSlug,
		getOrgId: () => orgId ?? orgDetail?.id ?? null,
		getMemberId: () => (correcting ? userId : null),
		onSaved: async () => { await Promise.all([loadEntries(), loadCumulativeBalance()]); },
		onDeleted: async () => { await Promise.all([loadEntries(), loadCumulativeBalance()]); },
		findEntry: (id) => entries.find((e) => e.id === id)
	});

	/** Working Time Act and core working hours violations of the shown month, by day ('YYYY-MM-DD'). */
	let compliance = $state(new Map<string, ComplianceDayResponse>());

	function complianceText(day: ComplianceDayResponse): string {
		const parts: string[] = [];
		if (day.dailyMax) parts.push(t('calendar.compliance.dailyMax', { hours: formatNumber(day.netHours ?? 0, { maximumFractionDigits: 1 }) }));
		if (day.restPeriod) parts.push(t('calendar.compliance.restPeriod', { hours: formatNumber(day.restHoursBefore ?? 0, { maximumFractionDigits: 1 }) }));
		if (day.coreHours)
			parts.push(t('calendar.compliance.coreHours', {
				window: (day.coreHoursWindow ?? '').replace('-', '–'),
				issues: (day.coreHoursIssues ?? []).map((i) => t(`calendar.compliance.issues.${i as 'late' | 'early' | 'gap' | 'missing'}`)).join(', '),
				minutes: day.coreHoursMissingMinutes ?? 0
			}));
		if (day.coreHours && day.coreHoursBackfilled) parts.push(t('calendar.compliance.coreHoursBackfilled'));
		return parts.join(' ');
	}

	// Computed
	const weekdayNames = $derived(dayNamesShort());
	const monthName = $derived(getMonthName(new Date(currentYear, currentMonth, 1)));
	const calendarDays = $derived(buildCalendarDays(currentYear, currentMonth, entries));
	const monthDays = $derived(calendarDays.filter((d) => d.isCurrentMonth));
	const monthStats = $derived(computeMonthStats(calendarDays));
	const yearMonths = $derived(viewMode === 'year' ? buildYearMonths(currentYear, yearEntries) : []);

	const selectedDay = $derived(selectedDayKey ? (monthDays.find((day) => day.key === selectedDayKey) ?? null) : null);

	const selectedDayEntries = $derived(
		selectedDay
			? entries
				.filter((entry) => entry.startTime && dateKey(new Date(entry.startTime)) === selectedDay.key)
				.sort((a, b) => new Date(a.startTime!).getTime() - new Date(b.startTime!).getTime())
			: []
	);

	onMount(loadAll);

	// Deep link (?date=…&entry=…): react to the props, so a changed query on the same page works too
	$effect(() => {
		const date = focusDate;
		const entryId = focusEntryId;
		const action = focusAction;
		if (loading || !date || !/^\d{4}-\d{2}-\d{2}$/.test(date)) return;
		untrack(() => applyFocus(date, entryId, action));
	});

	async function applyFocus(date: string, entryId: number | null, action: 'add' | 'absence' | null = null) {
		const [y, m] = date.split('-').map(Number);
		viewMode = 'month';
		if (y !== currentYear || m - 1 !== currentMonth) {
			currentYear = y;
			currentMonth = m - 1;
			selectedDayKey = date;
			await loadEntries();
		}
		selectedDayKey = date;
		editor.cancelEditEntry();
		const entry = entryId != null ? entries.find((e) => e.id === entryId) : undefined;
		if (entry && !entry.isRunning) {
			if (entryPerms.canEdit) editor.startEditEntry(entry);
			else if (entryPerms.canRequestEdit) editor.startRequest(entry.id!, 'edit');
		}
		if (action === 'add' && canAddEntry) editor.startCreate(date);
		if (action === 'absence' && canAddAbsence) openAbsenceDialog(date);
		await tick();
		dayDetailsEl?.scrollIntoView({ behavior: 'smooth', block: 'start' });
	}

	// ── Data loading (self vs. member endpoints) ─────────────────────────

	function monthKey(year: number, month1: number): string {
		return `${year}-${String(month1).padStart(2, '0')}`;
	}

	async function fetchEntriesRange(from: Date, to: Date): Promise<TimeEntryResponse[]> {
		try {
			return await fetchAllPages((limit, offset) =>
				(isSelf
					? timeTrackingApi.apiV1TimeTrackingGet(orgId ?? undefined, from.toISOString(), to.toISOString(), limit, offset)
					: organizationsApi.apiV1OrganizationsSlugMemberEntriesMemberIdGet(orgSlug, userId, from.toISOString(), to.toISOString(), limit, offset)
				).then((r) => r.data));
		} catch (err) {
			loadError = extractErrorMessage(err, t('calendar.loadEntriesFailed'));
			return [];
		}
	}

	async function loadAll() {
		loading = true;
		loadError = '';
		await Promise.all([
			loadDaysOff(), // first: marks the year as loaded, so loadEntries doesn't fetch it again
			loadEntries(),
			loadWorkSchedule(),
			loadOrgDetail(),
			loadSchedulePeriods(),
			loadAbsenceTypes(),
			loadEffectivePermissions(),
			loadCumulativeBalance()
		]);
		if (viewMode === 'year') await loadYearEntries();
		loading = false;
	}

	async function loadOrgDetail() {
		if (orgDetail) return;
		try {
			const data = await getOrgDetail(orgSlug);
			fetchedOrgDetail = data;
		} catch (err) {
			fetchedOrgDetail = null;
			loadError = extractErrorMessage(err, t('calendar.loadOrgFailed'));
		}
	}

	async function loadEntries() {
		const from = new Date(currentYear, currentMonth, 1);
		const to = new Date(currentYear, currentMonth + 1, 0, 23, 59, 59, 999);
		if (daysOffYear !== currentYear) loadDaysOff();
		loadCompliance(from, to);
		entries = await fetchEntriesRange(from, to);
		ensureSelectedDayForCurrentMonth();
	}

	async function loadCompliance(from: Date, to: Date) {
		const month = dateKey(from);
		try {
			const { data } = await memberEntriesApi.apiV1OrganizationsSlugMembersMemberIdComplianceGet(orgSlug, userId, dateKey(from), dateKey(to));
			if (month === dateKey(new Date(currentYear, currentMonth, 1))) compliance = new Map(data.map((d) => [d.date ?? '', d]));
		} catch {
			compliance = new Map(); // 403: not visible to the caller; the calendar works without markers
		}
	}

	async function loadYearEntries() {
		yearLoading = true;
		const from = new Date(currentYear, 0, 1);
		const to = new Date(currentYear, 11, 31, 23, 59, 59, 999);
		if (daysOffYear !== currentYear) loadDaysOff();
		yearEntries = await fetchEntriesRange(from, to);
		yearLoading = false;
	}

	async function loadWorkSchedule() {
		try {
			const { data } = isSelf
				? await workScheduleApi.apiV1OrganizationsSlugWorkScheduleGet(orgSlug)
				: await workScheduleApi.apiV1OrganizationsSlugMembersMemberIdWorkScheduleGet(orgSlug, userId);
			workSchedule = data;
		} catch (err) {
			workSchedule = null;
			loadError = extractErrorMessage(err, t('calendar.loadScheduleFailed'));
		}
	}

	async function loadSchedulePeriods() {
		try {
			const { data } = isSelf
				? await workScheduleApi.apiV1OrganizationsSlugWorkSchedulesGet(orgSlug)
				: await workScheduleApi.apiV1OrganizationsSlugMembersMemberIdWorkSchedulesGet(orgSlug, userId);
			schedulePeriods = data;
		} catch (err) {
			schedulePeriods = [];
			loadError = extractErrorMessage(err, t('calendar.loadScheduleFailed'));
		}
	}

	/** Holidays/absences are loaded for the shown year (month and year view never leave it). */
	let daysOffYear = $state<number | null>(null);
	async function loadDaysOff() {
		const year = (daysOffYear = currentYear);
		const data = await fetchDaysOff(orgSlug, userId, new Date(year, 0, 1), new Date(year, 11, 31));
		if (year === currentYear) daysOff = data; // ignore a slow response for a year already left
	}

	async function loadAbsenceTypes() {
		try {
			const { data } = await absenceDayApi.apiV1OrganizationsSlugAbsenceTypesGet(orgSlug);
			absenceTypes = data;
		} catch {
			absenceTypes = []; // only the built-in types are offered then
		}
	}

	async function loadEffectivePermissions() {
		try {
			const { data } = await groupApi.apiV1OrganizationsSlugEffectivePermissionsGet(orgSlug);
			effectivePerms = data;
		} catch {
			effectivePerms = null; // orgRules falls back to the org defaults
		}
	}

	async function onAbsenceSaved(requestMessage?: string) {
		if (requestMessage) {
			toast.success(requestMessage);
			return;
		}
		invalidateOrgDetail(orgSlug); // vacation counts changed
		await Promise.all([loadDaysOff(), loadEntries()]);
		await loadCumulativeBalance();
	}

	/** Lets the parent refresh the balance after changes made outside the calendar (e.g. initial overtime). */
	export function reloadBalance() {
		return loadCumulativeBalance();
	}

	async function loadCumulativeBalance() {
		try {
			const { data } = await reportsApi.apiV1OrganizationsSlugMembersMemberIdBalanceGet(orgSlug, userId);
			balance = balanceToMinutes(data);
		} catch (err) {
			balance = null;
			// 403: the caller may see the calendar but not this member's balance
			if (getErrorStatus(err) !== 403) loadError = extractErrorMessage(err, t('calendar.loadBalanceFailed'));
		}
		onbalance?.(balance);
	}

	// ── Navigation ────────────────────────────────────────────────────────

	function prevMonth() {
		if (currentMonth === 0) { currentMonth = 11; currentYear--; }
		else currentMonth--;
		loadEntries();
	}
	function nextMonth() {
		if (currentMonth === 11) { currentMonth = 0; currentYear++; }
		else currentMonth++;
		loadEntries();
	}
	function prevYear() { currentYear--; if (viewMode === 'year') loadYearEntries(); else loadEntries(); }
	function nextYear() { currentYear++; if (viewMode === 'year') loadYearEntries(); else loadEntries(); }

	function switchView(mode: 'month' | 'year') {
		viewMode = mode;
		if (mode === 'year' && yearEntries.length === 0) loadYearEntries();
	}

	function openMonth(month: number) {
		currentMonth = month;
		viewMode = 'month';
		loadEntries();
	}

	function ensureSelectedDayForCurrentMonth() {
		const monthPrefix = `${monthKey(currentYear, currentMonth + 1)}-`;
		if (selectedDayKey?.startsWith(monthPrefix)) return;

		const today = new Date();
		if (today.getFullYear() === currentYear && today.getMonth() === currentMonth) {
			selectedDayKey = dateKey(today);
			return;
		}
		selectedDayKey = `${monthPrefix}01`;
	}

	/** Left click: select the day and bring its details into view (`block`). */
	async function selectDay(day: CalendarDay, block: ScrollLogicalPosition) {
		if (!day.isCurrentMonth) return;
		if (selectedDayKey !== day.key) {
			editor.cancelEditEntry();
			editor.cancelCreate();
		}
		selectedDayKey = day.key;
		await tick();
		dayDetailsEl?.scrollIntoView({ behavior: 'smooth', block });
	}

	/** Right click / long press: select the day and open the absence dialog for it. */
	function dayOpenHandler(day: CalendarDay): (() => void) | undefined {
		if (!canAddAbsence || !day.isCurrentMonth || lockedDay(day.key)) return undefined;
		return () => {
			if (selectedDayKey !== day.key) editor.cancelEditEntry();
			selectedDayKey = day.key;
			openAbsenceDialog(day.key);
		};
	}

	function openAbsenceDialog(key: string) {
		absenceDialogDate = key;
		showAbsenceDialog = true;
	}

	function isPastOrToday(date: Date): boolean {
		const endOfToday = new Date();
		endOfToday.setHours(23, 59, 59, 999);
		return date <= endOfToday;
	}

	// ── Helpers ───────────────────────────────────────────────────────────

	function deltaClass(delta: number, neutral = 'text-base-content'): string {
		return delta > 0 ? 'text-success' : delta < 0 ? 'text-error' : neutral;
	}

	interface CalendarDay {
		date: Date;
		key: string;
		dayOfMonth: number;
		isCurrentMonth: boolean;
		isToday: boolean;
		isWeekend: boolean;
		workedMinutes: number;
		creditedMinutes: number;
		targetMinutes: number;
		entryCount: number;
		/** A timer on this day was auto-stopped at the business day end and still needs correcting. */
		hasFailure: boolean;
		dayType: string | null;
		dayTypeLabel: string;
		delta: number;
	}

	function buildCalendarDays(year: number, month: number, entries: TimeEntryResponse[]): CalendarDay[] {
		const firstDay = new Date(year, month, 1);
		const lastDay = new Date(year, month + 1, 0);
		const todayKey = dateKey(new Date());

		// Start from Monday before the first day
		const startOffset = (firstDay.getDay() + 6) % 7; // Mon=0
		const calStart = new Date(firstDay);
		calStart.setDate(calStart.getDate() - startOffset);

		// End on Sunday after the last day
		const endOffset = (7 - ((lastDay.getDay() + 6) % 7 + 1)) % 7;
		const calEnd = new Date(lastDay);
		calEnd.setDate(calEnd.getDate() + endOffset);

		// Build entry map
		const entryMap = new Map<string, { minutes: number; count: number; failure: boolean }>();
		for (const entry of entries) {
			if (entry.isRunning || !entry.startTime) continue;
			const key = dateKey(new Date(entry.startTime));
			const existing = entryMap.get(key) ?? { minutes: 0, count: 0, failure: false };
			existing.minutes += entry.netDurationMinutes ?? entry.durationMinutes ?? 0;
			existing.count++;
			existing.failure ||= !!entry.isFailure;
			entryMap.set(key, existing);
		}

		const days: CalendarDay[] = [];
		const cursor = new Date(calStart);
		while (cursor <= calEnd) {
			const key = dateKey(cursor);
			const isCurrentMonth = cursor.getMonth() === month;
			const entryData = entryMap.get(key) ?? { minutes: 0, count: 0, failure: false };
			const targetMinutes = isCurrentMonth ? getDayTarget(new Date(cursor), workSchedule, holidayDates, schedulePeriods, halfDayHolidays) : 0;
			const absCredit = isCurrentMonth ? getAbsenceCredit(new Date(cursor), workSchedule, holidayDates, sickDayDates, vacationDates, otherAbsenceDates, schedulePeriods, halfDayAbsences, halfDayHolidays, customCredits, entryData.minutes) : 0;

			days.push({
				date: new Date(cursor),
				key,
				dayOfMonth: cursor.getDate(),
				isCurrentMonth,
				isToday: key === todayKey,
				isWeekend: cursor.getDay() === 0 || cursor.getDay() === 6,
				workedMinutes: entryData.minutes,
				creditedMinutes: absCredit,
				targetMinutes,
				entryCount: entryData.count,
				hasFailure: entryData.failure,
				dayType: getDayType(key, holidayDates, sickDayDates, vacationDates, otherAbsenceDates, halfDayHolidays),
				dayTypeLabel: getDayTypeLabel(key, holidayDates, sickDayDates, vacationDates, otherAbsenceDates, halfDayHolidays, halfDayAbsences, customTypeDates),
				delta: entryData.minutes + absCredit - targetMinutes
			});

			cursor.setDate(cursor.getDate() + 1);
		}
		return days;
	}

	function computeMonthStats(days: CalendarDay[]) {
		// Only count days up to today — future days haven't happened yet and
		// would otherwise inflate "Missing"/target figures for days not yet elapsed.
		const today = new Date();
		today.setHours(23, 59, 59, 999);
		const currentDays = days.filter((d) => d.isCurrentMonth && d.date <= today);
		const totalWorked = currentDays.reduce((s, d) => s + d.workedMinutes, 0);
		const totalCredits = currentDays.reduce((s, d) => s + d.creditedMinutes, 0);
		const totalTarget = currentDays.reduce((s, d) => s + d.targetMinutes, 0);
		const totalDelta = totalWorked + totalCredits - totalTarget;
		const workDays = currentDays.filter((d) => d.targetMinutes > 0).length;
		const workedDays = currentDays.filter((d) => d.workedMinutes > 0).length;
		const holidays = currentDays.filter((d) => d.dayType === 'holiday').length;
		const sickDays = currentDays.filter((d) => d.dayType === 'sick').length;
		const vacationDays = currentDays.filter((d) => d.dayType === 'vacation').length;
		const avgPerDay = workedDays > 0 ? totalWorked / workedDays : 0;
		return { totalWorked, totalCredits, totalTarget, totalDelta, workDays, workedDays, holidays, sickDays, vacationDays, avgPerDay };
	}

	interface YearMonth {
		month: number;
		name: string;
		workedMinutes: number;
		creditedMinutes: number;
		targetMinutes: number;
		delta: number;
		holidays: number;
		sickDays: number;
		vacationDays: number;
	}

	function buildYearMonths(year: number, entries: TimeEntryResponse[]): YearMonth[] {
		// Only count days up to today — future months/days haven't happened yet.
		const today = new Date();
		today.setHours(23, 59, 59, 999);

		// Per-day worked minutes (needed for top-up credits)
		const dailyWorked = new Map<string, number>();
		for (const entry of entries) {
			if (entry.isRunning || !entry.startTime) continue;
			const dk = dateKey(new Date(entry.startTime));
			dailyWorked.set(dk, (dailyWorked.get(dk) ?? 0) + (entry.netDurationMinutes ?? entry.durationMinutes ?? 0));
		}

		const months: YearMonth[] = [];
		for (let m = 0; m < 12; m++) {
			const firstDay = new Date(year, m, 1);
			const lastDay = new Date(year, m + 1, 0);

			let holidays = 0, sickDays = 0, vacationDays = 0, targetMinutes = 0, absenceCredits = 0, workedMinutes = 0;
			const cursor = new Date(firstDay);
			while (cursor <= lastDay && cursor <= today) {
				const key = dateKey(cursor);
				const dt = getDayType(key, holidayDates, sickDayDates, vacationDates, otherAbsenceDates, halfDayHolidays);
				if (dt === 'holiday') holidays++;
				else if (dt === 'sick') sickDays++;
				else if (dt === 'vacation') vacationDays++;

				const worked = dailyWorked.get(key) ?? 0;
				workedMinutes += worked;
				targetMinutes += getDayTarget(new Date(cursor), workSchedule, holidayDates, schedulePeriods, halfDayHolidays);
				absenceCredits += getAbsenceCredit(new Date(cursor), workSchedule, holidayDates, sickDayDates, vacationDates, otherAbsenceDates, schedulePeriods, halfDayAbsences, halfDayHolidays, customCredits, worked);

				cursor.setDate(cursor.getDate() + 1);
			}

			months.push({
				month: m,
				name: formatDate(firstDay, { month: 'short' }),
				workedMinutes,
				creditedMinutes: absenceCredits,
				targetMinutes,
				delta: workedMinutes + absenceCredits - targetMinutes,
				holidays,
				sickDays,
				vacationDays
			});
		}
		return months;
	}
</script>

{#snippet stat(value: string, label: string, valueClass = 'text-base-content', extra = '')}
	<div class="bg-base-100 border border-base-300 rounded-lg p-3 text-center {extra}">
		<span class="block text-lg sm:text-xl font-bold tabular-nums {valueClass}">{value}</span>
		<span class="text-xs text-base-content/70">{label}</span>
	</div>
{/snippet}

<div>
	<div class="flex items-center justify-end mb-4">
		<div class="join" role="group" aria-label={t('calendar.view')}>
			<button type="button" class="join-item btn btn-sm {viewMode === 'month' ? 'btn-primary' : 'btn-ghost'}" aria-pressed={viewMode === 'month'} onclick={() => switchView('month')}>{t('calendar.month')}</button>
			<button type="button" class="join-item btn btn-sm {viewMode === 'year' ? 'btn-primary' : 'btn-ghost'}" aria-pressed={viewMode === 'year'} onclick={() => switchView('year')}>{t('calendar.year')}</button>
		</div>
	</div>

	{#if loadError}
		<div role="alert" class="alert alert-error text-sm mb-4 max-sm:alert-vertical">
			<span>{loadError}</span>
			<button type="button" class="btn btn-sm" onclick={loadAll}>{t('common.retry')}</button>
		</div>
	{/if}

	{#if loading}
		<Skeleton />
	{:else if viewMode === 'month'}
		<!-- MONTH VIEW -->
		<div>
			<!-- Navigation -->
			<div class="flex items-center justify-center gap-1 sm:gap-3 mb-5">
				<button type="button" class="btn btn-ghost btn-sm btn-square" onclick={prevYear} title={t('calendar.prevYear')} aria-label={t('calendar.prevYear')}>«</button>
				<button type="button" class="btn btn-ghost btn-sm btn-square" onclick={prevMonth} title={t('calendar.prevMonth')} aria-label={t('calendar.prevMonth')}>‹</button>
				<h2 class="text-lg font-bold min-w-0 flex-1 sm:flex-none sm:min-w-[180px] text-center m-0" aria-live="polite">{monthName} {currentYear}</h2>
				<button type="button" class="btn btn-ghost btn-sm btn-square" onclick={nextMonth} title={t('calendar.nextMonth')} aria-label={t('calendar.nextMonth')}>›</button>
				<button type="button" class="btn btn-ghost btn-sm btn-square" onclick={nextYear} title={t('calendar.nextYear')} aria-label={t('calendar.nextYear')}>»</button>
			</div>

			<!-- Month stats -->
			<div class="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-6 gap-2 sm:gap-3 mb-4">
				{@render stat(formatHours(monthStats.totalWorked), t('calendar.worked'))}
				{@render stat(formatHours(monthStats.totalCredits), t('calendar.credits'))}
				{@render stat(formatHours(monthStats.totalTarget), t('calendar.target'))}
				{@render stat(formatDelta(monthStats.totalDelta), t('calendar.monthBalance'), deltaClass(monthStats.totalDelta))}
				{#if balance}{@render stat(formatDelta(balance.cumulative), t('calendar.cumulative'), deltaClass(balance.cumulative), 'border-l-3 border-l-secondary')}{/if}
				{@render stat(formatHours(monthStats.avgPerDay), t('calendar.avgPerDay'))}
			</div>

			<!-- Summary badges -->
			{#if monthStats.workDays > 0 || monthStats.holidays > 0 || monthStats.sickDays > 0 || monthStats.vacationDays > 0}
				<div class="flex flex-wrap gap-2 mb-5">
					<span class="badge badge-ghost badge-sm">{tp('calendar.workDays', monthStats.workDays)}</span>
					<span class="badge badge-info badge-sm">{tp('calendar.daysWorked', monthStats.workedDays)}</span>
					{#if monthStats.holidays > 0}<span class="badge badge-sm {DAY_TYPE_CLASSES.holiday.badge}">{tp('calendar.holidays', monthStats.holidays)}</span>{/if}
					{#if monthStats.sickDays > 0}<span class="badge badge-sm {DAY_TYPE_CLASSES.sick.badge}">{tp('calendar.sickDays', monthStats.sickDays)}</span>{/if}
					{#if monthStats.vacationDays > 0}<span class="badge badge-sm {DAY_TYPE_CLASSES.vacation.badge}">{tp('calendar.vacationDays', monthStats.vacationDays)}</span>{/if}
				</div>
			{/if}

			<!-- Calendar grid (sm and up) -->
			<div class="hidden sm:block bg-base-100 border border-base-300 rounded-xl overflow-hidden mb-5">
				<div class="grid grid-cols-7 bg-base-200/50 border-b border-base-300">
					{#each weekdayNames as dow (dow)}
						<span class="text-center text-xs font-semibold text-base-content/70 py-2">{dow}</span>
					{/each}
				</div>
				<div class="grid grid-cols-7">
					{#each calendarDays as day (day.key)}
						{@const dt = dayTypeClasses(day.dayType)}
						<button
							type="button"
							class="min-h-[72px] p-1.5 border-r border-b border-base-200 [&:nth-child(7n)]:border-r-0 flex flex-col gap-0.5 relative transition-colors text-left {!day.isCurrentMonth ? 'opacity-30' : ''} {day.isWeekend ? 'bg-base-200/30' : ''} {dt.soft} {day.isToday ? 'outline-2 outline-primary -outline-offset-2 rounded' : ''} {day.key === selectedDayKey ? 'ring-2 ring-primary/40 bg-primary/10' : ''}"
							title={(day.dayTypeLabel || (day.workedMinutes > 0 ? t('calendar.workedValue', { value: formatHours(day.workedMinutes) }) : '')) + (canAddAbsence && day.isCurrentMonth ? (mustRequest ? t('calendar.clickToRequest') : t('calendar.clickToAdd')) : '')}
							aria-pressed={day.key === selectedDayKey}
							data-day={day.key}
							use:dayPress={{ onselect: () => selectDay(day, 'nearest'), onopen: dayOpenHandler(day) }}
							disabled={!day.isCurrentMonth}
						>
							<span class="text-xs font-medium {day.isToday ? 'text-primary font-bold' : 'text-base-content/70'}">{day.dayOfMonth}</span>
							{#if day.hasFailure}
								<span class="absolute top-1 right-1 h-2 w-2 rounded-full bg-error" title={t('calendar.needsCorrection')}></span>
							{/if}
							{#if day.isCurrentMonth && isClosedDay(day.key, closed)}
								<LockIcon title={lockText(day.key)} class="absolute bottom-1 right-1" />
							{/if}
							{#if compliance.has(day.key)}
								<span class="absolute top-0.5 right-4 text-[0.7rem] leading-none text-warning" title={complianceText(compliance.get(day.key)!)} data-testid="compliance-marker">⚠<span class="sr-only">{complianceText(compliance.get(day.key)!)}</span></span>
							{/if}
							{#if day.isCurrentMonth}
								{#if day.workedMinutes > 0}
									<span class="text-xs font-semibold text-base-content">{formatHours(day.workedMinutes)}</span>
									{#if day.targetMinutes > 0}
										<div class="h-[3px] bg-base-200 rounded-sm overflow-hidden mt-auto">
											<div class="h-full rounded-sm min-w-[2px]" style="width: {barWidth(day.workedMinutes, day.targetMinutes)}%; background: {heatColor(day.workedMinutes, day.targetMinutes)}"></div>
										</div>
									{/if}
								{:else if day.dayType}
									<span class="text-xs mt-0.5" aria-hidden="true">{dt.icon}</span>
								{:else if day.targetMinutes > 0 && !day.isToday && day.date < new Date()}
									<span class="text-xs text-base-content/20 mt-0.5">—</span>
								{/if}
							{/if}
						</button>
					{/each}
				</div>
			</div>

			<!-- Agenda list (below sm): one row per day -->
			<ul class="sm:hidden bg-base-100 border border-base-300 rounded-xl overflow-hidden mb-5 divide-y divide-base-200">
				{#each monthDays as day (day.key)}
					{@const dt = dayTypeClasses(day.dayType)}
					<li>
						<button
							type="button"
							class="w-full flex items-center gap-2 px-3 py-2 min-h-11 text-left {dt.soft} {day.isWeekend && !day.dayType ? 'bg-base-200/30' : ''} {day.key === selectedDayKey ? 'ring-2 ring-inset ring-primary/40 bg-primary/10' : ''}"
							aria-pressed={day.key === selectedDayKey}
							use:dayPress={{ onselect: () => selectDay(day, 'start'), onopen: dayOpenHandler(day) }}
						>
							<span class="w-8 text-xs text-base-content/70 shrink-0">{formatDate(day.date, { weekday: 'short' })}</span>
							<span class="w-6 text-sm tabular-nums shrink-0 {day.isToday ? 'text-primary font-bold' : 'font-medium'}">{day.dayOfMonth}</span>
							<span class="min-w-0 flex-1 truncate text-xs text-base-content/70">
								{#if day.dayType}<span class="inline-block w-2 h-2 rounded-full mr-1 {dt.dot}" aria-hidden="true"></span>{day.dayTypeLabel}{/if}
							</span>
							{#if day.hasFailure}
								<span class="badge badge-error badge-xs" title={t('calendar.needsCorrection')}>!</span>
							{/if}
							{#if isClosedDay(day.key, closed)}
								<LockIcon title={lockText(day.key)} />
							{/if}
							{#if compliance.has(day.key)}
								<span class="text-warning text-xs" title={complianceText(compliance.get(day.key)!)}>⚠<span class="sr-only">{complianceText(compliance.get(day.key)!)}</span></span>
							{/if}
							<span class="text-sm tabular-nums whitespace-nowrap">
								{#if day.workedMinutes > 0}
									<span class="inline-block w-2 h-2 rounded-full align-middle mr-1" style="background: {heatColor(day.workedMinutes, day.targetMinutes)}" aria-hidden="true"></span>{formatHours(day.workedMinutes)}
								{:else}
									<span class="text-base-content/30">–</span>
								{/if}
								{#if day.targetMinutes > 0}<span class="text-xs text-base-content/70"> / {formatHours(day.targetMinutes)}</span>{/if}
							</span>
						</button>
					</li>
				{/each}
			</ul>

			<p class="text-xs text-base-content/70 -mt-3 mb-4">
				{t('calendar.hintSelect')}{#if canAddAbsence}{' · '}{t('calendar.hintOpen')}{/if}
			</p>

			{#if selectedDay}
				{@const dayDetails = selectedDay}
				{@const showUnderTarget = isPastOrToday(dayDetails.date) && dayDetails.targetMinutes > 0 && dayDetails.delta < 0}
				<section bind:this={dayDetailsEl} class="card bg-base-100 border border-base-300 p-4 mb-5 scroll-mt-20" aria-label={t('calendar.dayDetails')} data-testid="day-details">
					<div class="flex flex-wrap items-center justify-between gap-2 mb-3">
						<div>
							<h3 class="text-[0.9375rem] font-bold m-0">{t('calendar.dayDetails')}</h3>
							<span class="text-sm text-base-content/70">{formatDateFull(dayDetails.date)}</span>
						</div>
						<div class="flex flex-wrap gap-2">
							{#if canAddEntry && !lockedDay(dayDetails.key) && isPastOrToday(dayDetails.date) && editor.creatingDay !== dayDetails.key}
								<button type="button" class="btn btn-outline btn-sm" onclick={() => editor.startCreate(dayDetails.key)}>{correcting ? t('entries.manual.addAdmin') : t('entries.manual.add')}</button>
							{/if}
							{#if canAddAbsence && !lockedDay(dayDetails.key)}
								<button type="button" class="btn btn-outline btn-sm" onclick={() => openAbsenceDialog(dayDetails.key)}>{mustRequest ? t('calendar.requestAbsence') : t('calendar.addAbsence')}</button>
							{/if}
						</div>
					</div>

					<div class="flex flex-wrap gap-2 mb-4">
						<span class="badge badge-outline">{t('calendar.workedBadge', { value: formatHours(dayDetails.workedMinutes) })}</span>
						{#if dayDetails.creditedMinutes > 0}
							<span class="badge badge-secondary badge-outline">{t('calendar.creditsBadge', { value: formatHours(dayDetails.creditedMinutes) })}</span>
						{/if}
						<span class="badge badge-outline">{t('calendar.targetBadge', { value: formatHours(dayDetails.targetMinutes) })}</span>
						<span class="badge {dayDetails.delta > 0 ? 'badge-success' : dayDetails.delta < 0 ? 'badge-error' : 'badge-ghost'}">{formatDelta(dayDetails.delta)}</span>
						{#if showUnderTarget}
							<span class="badge badge-warning">{t('calendar.underTarget')}</span>
						{/if}
						{#if dayDetails.dayType}
							<span class="badge {dayTypeClasses(dayDetails.dayType).badge}">{dayDetails.dayTypeLabel}</span>
						{/if}
					</div>
					{#if isClosedDay(dayDetails.key, closed)}
						<div role="status" class="alert alert-info text-sm mb-3 py-2" data-testid="closed-day-notice">
							<LockIcon title={lockText(dayDetails.key)} muted={false} />
							<span>{t(isAdmin ? 'closedPeriods.lockedDayAdmin' : 'closedPeriods.lockedDay', { month: formatMonth(dayDetails.key) })}</span>
						</div>
					{/if}
					{#if compliance.has(dayDetails.key)}
						<div role="status" class="alert alert-warning text-sm mb-3 py-2">{complianceText(compliance.get(dayDetails.key)!)}</div>
					{/if}

					{#if editor.creatingDay === dayDetails.key}
						<div class="rounded-lg border border-primary/40 overflow-hidden mb-3">
							<EntryEditForm
								bind:startTime={editor.createStart}
								bind:endTime={editor.createEnd}
								bind:description={editor.createDescription}
								bind:pause={editor.createPause}
								error={editor.createError}
								saving={editor.createSaving}
								canEditPause={editor.pauseEditable}
								requireEnd
								title={correcting ? t('entries.manual.titleAdmin', { date: formatDate(dayDetails.date) }) : t('entries.manual.title', { date: formatDate(dayDetails.date) })}
								hint={addEntryNeedsApproval ? t('entries.manual.needsApprovalHint') : ''}
								onsave={editor.saveCreate}
								oncancel={editor.cancelCreate}
							/>
						</div>
					{/if}

					{#if selectedDayEntries.length > 0}
						<ul class="flex flex-col gap-2">
							{#each selectedDayEntries as entry (entry.id)}
								<li class="rounded-lg border border-base-300 bg-base-200/40 overflow-hidden">
									<EntryRow {entry} editor={isSelf || correcting ? editor : undefined} {...entryPerms} compact locked={lockedDay(dayDetails.key) ? lockText(dayDetails.key) : ''} />
								</li>
							{/each}
						</ul>
					{:else}
						<EmptyState text={canAddEntry && !lockedDay(dayDetails.key) && isPastOrToday(dayDetails.date) ? t('calendar.noEntriesAdd') : t('calendar.noEntries')} />
					{/if}
				</section>
			{/if}

			<!-- Legend -->
			<div class="flex gap-4 flex-wrap text-xs text-base-content/70 mb-6">
				{#if holidayDates.size > 0}<span class="flex items-center gap-1.5"><span class="w-2.5 h-2.5 rounded-sm shrink-0 {DAY_TYPE_CLASSES.holiday.dot}"></span> {t('format.dayType.holiday')}</span>{/if}
				{#if sickDayDates.size > 0}<span class="flex items-center gap-1.5"><span class="w-2.5 h-2.5 rounded-sm shrink-0 {DAY_TYPE_CLASSES.sick.dot}"></span> {t('format.dayType.sick')}</span>{/if}
				{#if vacationDates.size > 0}<span class="flex items-center gap-1.5"><span class="w-2.5 h-2.5 rounded-sm shrink-0 {DAY_TYPE_CLASSES.vacation.dot}"></span> {t('format.dayType.vacation')}</span>{/if}
				{#if otherAbsenceDates.size > 0}<span class="flex items-center gap-1.5"><span class="w-2.5 h-2.5 rounded-sm shrink-0 {DAY_TYPE_CLASSES['other-absence'].dot}"></span> {t('format.dayType.otherAbsence')}</span>{/if}
				<span class="flex items-center gap-1.5"><span class="w-2.5 h-2.5 rounded-sm shrink-0" style="background: {heatColor(1, 1)}"></span> {t('calendar.targetMet')}</span>
				<span class="flex items-center gap-1.5"><span class="w-2.5 h-2.5 rounded-sm shrink-0" style="background: {heatColor(1, 2)}"></span> {t('calendar.underTarget')}</span>
				<span class="flex items-center gap-1.5"><span class="w-2 h-2 rounded-full shrink-0 bg-error"></span> {t('calendar.correctionLegend')}</span>
				{#if closed.has(`${currentYear}-${String(currentMonth + 1).padStart(2, '0')}`)}<span class="flex items-center gap-1.5"><LockIcon title={t('closedPeriods.closed')} /> {t('closedPeriods.closed')}</span>{/if}
			</div>

			<!-- Daily breakdown for month (the agenda list covers this on phones) -->
			<div class="card bg-base-100 border border-base-300 p-4 hidden sm:flex">
				<h3 class="text-[0.9375rem] font-bold mb-3 m-0">{t('calendar.dailyBreakdown')}</h3>
				<div class="flex flex-col gap-1.5">
					{#each monthDays.filter((d) => d.workedMinutes > 0 || d.targetMinutes > 0 || d.dayType) as day (day.key)}
						{@const dt = dayTypeClasses(day.dayType)}
						<button
							type="button"
							class="flex items-center gap-2 py-1 px-1.5 rounded text-left transition-colors {dt.soft} {day.isToday ? 'font-semibold' : ''} {day.key === selectedDayKey ? 'ring-2 ring-primary/30 bg-primary/10' : ''}"
							aria-pressed={day.key === selectedDayKey}
							use:dayPress={{ onselect: () => selectDay(day, 'start'), onopen: dayOpenHandler(day) }}
						>
							<span class="w-7 text-xs text-base-content/70 shrink-0">{formatDate(day.date, { weekday: 'short' })}</span>
							<span class="w-5 text-xs text-base-content/70 shrink-0 text-right">{day.dayOfMonth}</span>
							<span class="w-2 h-2 rounded-full shrink-0 {dt.dot}" title={day.dayTypeLabel || undefined}></span>
							<div class="flex-1 h-1.5 bg-base-200 rounded-full overflow-hidden">
								<div class="h-full {day.workedMinutes > day.targetMinutes && day.targetMinutes > 0 ? 'bg-success' : 'bg-primary'} rounded-full transition-all duration-300" style="width: {barWidth(day.workedMinutes, day.targetMinutes)}%"></div>
							</div>
							<span class="text-xs text-base-content/70 min-w-[36px] text-right">{formatHours(day.workedMinutes)}</span>
							{#if day.targetMinutes > 0}
								<span class="text-xs text-base-content/70">/ {formatHours(day.targetMinutes)}</span>
							{/if}
						</button>
					{/each}
				</div>
			</div>


			<AbsenceDialog
				open={showAbsenceDialog}
				{orgSlug}
				date={absenceDialogDate}
				fixedUserId={userId}
				isAdmin={isAdmin}
				{mustRequest}
				{absenceTypes}
				onclose={() => (showAbsenceDialog = false)}
				onsaved={onAbsenceSaved}
			/>

			{#if isSelf}
				<RequestModal
					open={!!editor.requestingEntryId}
					type={editor.requestType}
					bind:newStart={editor.requestNewStart}
					bind:newEnd={editor.requestNewEnd}
					bind:newPause={editor.requestNewPause}
					bind:message={editor.requestMessage}
					sending={editor.requestSending}
					error={editor.requestError}
					onsubmit={editor.submitRequest}
					oncancel={editor.cancelRequest}
				/>
			{/if}
		</div>

	{:else}
		<!-- YEAR VIEW -->
		<div>
			<div class="flex items-center justify-center gap-3 mb-5">
				<button type="button" class="btn btn-ghost btn-sm btn-square" onclick={prevYear} title={t('calendar.prevYear')} aria-label={t('calendar.prevYear')}>‹</button>
				<h2 class="text-lg font-bold min-w-[120px] text-center m-0" aria-live="polite">{currentYear}</h2>
				<button type="button" class="btn btn-ghost btn-sm btn-square" onclick={nextYear} title={t('calendar.nextYear')} aria-label={t('calendar.nextYear')}>›</button>
			</div>

			{#if yearLoading}
				<Skeleton />
			{:else}
				<!-- Year summary -->
				{@const yearTotal = yearMonths.reduce((s, m) => s + m.workedMinutes, 0)}
				{@const yearCredits = yearMonths.reduce((s, m) => s + m.creditedMinutes, 0)}
				{@const yearTarget = yearMonths.reduce((s, m) => s + m.targetMinutes, 0)}
				{@const yearDelta = yearTotal + yearCredits - yearTarget}
				{@const yearHolidays = yearMonths.reduce((s, m) => s + m.holidays, 0)}
				{@const yearSick = yearMonths.reduce((s, m) => s + m.sickDays, 0)}
				{@const yearVacation = yearMonths.reduce((s, m) => s + m.vacationDays, 0)}

				<div class="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-5 gap-2 sm:gap-3 mb-4">
					{@render stat(formatHours(yearTotal), t('calendar.totalWorked'))}
					{@render stat(formatHours(yearCredits), t('calendar.credits'))}
					{@render stat(formatHours(yearTarget), t('calendar.totalTarget'))}
					{@render stat(formatDelta(yearDelta), t('calendar.yearBalance'), deltaClass(yearDelta))}
					{#if balance}{@render stat(formatDelta(balance.cumulative), t('calendar.cumulative'), deltaClass(balance.cumulative), 'border-l-3 border-l-secondary')}{/if}
				</div>

				<div class="flex flex-wrap gap-2 mb-5">
					{#if yearHolidays > 0}<span class="badge badge-sm {DAY_TYPE_CLASSES.holiday.badge}">{tp('calendar.holidays', yearHolidays)}</span>{/if}
					{#if yearSick > 0}<span class="badge badge-sm {DAY_TYPE_CLASSES.sick.badge}">{tp('calendar.sickDays', yearSick)}</span>{/if}
					{#if yearVacation > 0}<span class="badge badge-sm {DAY_TYPE_CLASSES.vacation.badge}">{tp('calendar.vacationDays', yearVacation)}</span>{/if}
				</div>

				<!-- Monthly cards -->
				<div class="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-4 gap-2 sm:gap-3">
					{#each yearMonths as mo (mo.month)}
						{@const isCurrent = mo.month === new Date().getMonth() && currentYear === new Date().getFullYear()}
						<button type="button" class="bg-base-100 border rounded-lg p-3 cursor-pointer text-left transition-all w-full min-w-0 hover:border-primary/30 hover:shadow-md {isCurrent ? 'border-primary shadow-[0_0_0_1px] shadow-primary' : 'border-base-300'}" onclick={() => openMonth(mo.month)}>
							<div class="flex flex-wrap justify-between items-center gap-1 mb-2">
								<span class="font-semibold text-sm text-base-content">{mo.name}</span>
								{#if mo.workedMinutes > 0 || mo.creditedMinutes > 0}
									<span class="text-xs font-medium {deltaClass(mo.delta, '')}">{formatDelta(mo.delta)}</span>
								{/if}
							</div>
							<div class="h-1.5 bg-base-200 rounded-full overflow-hidden mb-2">
								<div class="h-full {mo.workedMinutes > mo.targetMinutes && mo.targetMinutes > 0 ? 'bg-success' : 'bg-primary'} rounded-full transition-all duration-300" style="width: {barWidth(mo.workedMinutes, mo.targetMinutes)}%"></div>
							</div>
							<div class="text-sm text-base-content/70 mb-1.5">
								<span class="font-semibold">{formatHours(mo.workedMinutes)}</span>
								{#if mo.targetMinutes > 0}
									<span class="text-base-content/70">/ {formatHours(mo.targetMinutes)}</span>
								{/if}
							</div>
							{#if mo.creditedMinutes > 0}
								<div class="text-xs text-base-content/70 mb-1">{t('calendar.creditsSuffix', { value: formatHours(mo.creditedMinutes) })}</div>
							{/if}
							<div class="flex gap-1.5 flex-wrap">
								{#if mo.holidays > 0}<span class="text-xs">{mo.holidays}{DAY_TYPE_CLASSES.holiday.icon}</span>{/if}
								{#if mo.sickDays > 0}<span class="text-xs">{mo.sickDays}{DAY_TYPE_CLASSES.sick.icon}</span>{/if}
								{#if mo.vacationDays > 0}<span class="text-xs">{mo.vacationDays}{DAY_TYPE_CLASSES.vacation.icon}</span>{/if}
							</div>
						</button>
					{/each}
				</div>
			{/if}
		</div>
	{/if}
</div>
