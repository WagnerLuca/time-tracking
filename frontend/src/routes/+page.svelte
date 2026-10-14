<script lang="ts">
	import { untrack } from 'svelte';
	import { auth } from '$lib/stores/auth.svelte';
	import { orgContext } from '$lib/stores/orgContext.svelte';
	import { timer } from '$lib/stores/timer.svelte';
	import { toast } from '$lib/stores/toast.svelte';
	import { timeTrackingApi, workScheduleApi, reportsApi } from '$lib/apiClient';
	import type { WorkScheduleResponse } from '$lib/api';
	import { formatHours, formatDelta, formatClock, barWidth, getMonthName, dayNamesShort } from '$lib/utils/formatters';
	import { isToday, isFuture, sumMinutes, dateKey } from '$lib/utils/dateHelpers';
	import { getDayType, getDayTypeLabel } from '$lib/utils/scheduleHelpers';
	import { WEEKLY_ENTRY_LIMIT } from '$lib/utils/constants';
	import { fetchAllPages } from '$lib/utils/pagination';
	import { balanceToMinutes } from '$lib/utils/balance';
	import { extractErrorMessage } from '$lib/utils/errorHandler';
	import { dayTypeClasses } from '$lib/utils/colors';
	import { loadDaysOff, emptyDaysOff, type DaysOffData } from '$lib/utils/daysOff';
	import Skeleton from '$lib/components/Skeleton.svelte';
	import DayTypeLegend from '$lib/components/DayTypeLegend.svelte';
	import TeamOverview from '$lib/components/TeamOverview.svelte';
	import OnboardingChecklist from '$lib/components/OnboardingChecklist.svelte';
	import { isOrgAdmin } from '$lib/utils/orgRules';
	import { t } from '$lib/i18n';

	interface Stats {
		todayMinutes: number;
		weekMinutes: number;
		monthMinutes: number;
		todayTarget: number;
		weekTarget: number;
		monthTarget: number;
		/** From the balance endpoint; null without an org or work schedule (no target → no balance). */
		cumulative: number | null;
		weekDays: Array<{ label: string; date: Date; worked: number; target: number }>;
		monthWeeks: Array<{ label: string; worked: number; target: number }>;
	}

	let stats = $state<Stats | null>(null);
	let statsLoading = $state(true);
	let statsError = $state('');
	let workSchedule = $state<WorkScheduleResponse | null>(null);
	let daysOff = $state<DaysOffData>(emptyDaysOff());
	let pending = $state(false);

	const selectedSlug = $derived(orgContext.selectedOrgSlug);
	/** The forgotten day is usually yesterday. */
	const yesterday = dateKey(new Date(Date.now() - 86_400_000));
	const hasSchedule = $derived(!!workSchedule?.weeklyWorkHours);
	const noOrganizations = $derived(!orgContext.loading && orgContext.organizations.length === 0);

	/** Running time counts towards the stats only when it belongs to the org they are computed for. */
	const runningMinutes = $derived(
		timer.current && (!orgContext.selectedOrgId || timer.current.organizationId === orgContext.selectedOrgId) ? timer.elapsedMinutes : 0
	);
	const today = $derived(stats ? stats.todayMinutes + runningMinutes : 0);
	const week = $derived(stats ? stats.weekMinutes + runningMinutes : 0);
	const month = $derived(stats ? stats.monthMinutes + runningMinutes : 0);
	const cumulative = $derived(stats?.cumulative != null ? stats.cumulative + runningMinutes : null);

	// Legend only for day types visible this week
	const weekKeys = $derived(new Set(stats?.weekDays.map((d) => dateKey(d.date)) ?? []));
	const weekHolidayDates = $derived(new Map([...daysOff.holidayDates].filter(([k]) => weekKeys.has(k))));
	const weekSickDates = $derived(new Set([...daysOff.sickDayDates].filter((k) => weekKeys.has(k))));
	const weekVacationDates = $derived(new Set([...daysOff.vacationDates].filter((k) => weekKeys.has(k))));
	const weekOtherAbsenceDates = $derived(new Set([...daysOff.otherAbsenceDates].filter((k) => weekKeys.has(k))));

	// (Re)load once the org list is known and whenever the selected org changes
	$effect(() => {
		const slug = selectedSlug;
		if (orgContext.loading) return;
		untrack(() => loadStats(slug));
	});

	async function loadStats(slug: string | null) {
		statsLoading = true;
		statsError = '';
		try {
			const now = new Date();
			const todayStart = new Date(now);
			todayStart.setHours(0, 0, 0, 0);
			const todayEnd = new Date(now);
			todayEnd.setHours(23, 59, 59, 999);
			// This week (Mon–Sun)
			const weekStart = new Date(todayStart);
			weekStart.setDate(now.getDate() - (now.getDay() || 7) + 1);
			const weekEnd = new Date(weekStart);
			weekEnd.setDate(weekStart.getDate() + 6);
			weekEnd.setHours(23, 59, 59, 999);
			const monthStart = new Date(now.getFullYear(), now.getMonth(), 1);
			const monthEnd = new Date(now.getFullYear(), now.getMonth() + 1, 0, 23, 59, 59, 999);

			const userId = auth.user?.id;
			const dayNames = dayNamesShort();
			const weekRange = (from: Date, to: Date) => t('dashboard.weekRange', { from: from.getDate(), to: to.getDate() });

			if (slug && userId) {
				// Everything from the balance endpoint (report rules: absences credited, failure entries excluded)
				const [schedule, off, b] = await Promise.all([
					workScheduleApi.apiV1OrganizationsSlugWorkScheduleGet(slug).then((r) => r.data),
					loadDaysOff(slug, userId, weekStart < monthStart ? weekStart : monthStart, weekEnd > monthEnd ? weekEnd : monthEnd),
					reportsApi.apiV1OrganizationsSlugMembersMemberIdBalanceGet(slug, userId).then((r) => balanceToMinutes(r.data))
				]);
				workSchedule = schedule;
				daysOff = off;
				stats = {
					todayMinutes: b.today.worked,
					weekMinutes: b.week.worked,
					monthMinutes: b.month.worked,
					todayTarget: b.today.target,
					weekTarget: b.week.target,
					monthTarget: b.month.target,
					cumulative: schedule?.weeklyWorkHours ? b.cumulative : null,
					weekDays: b.weekDays.map((d) => ({ label: dayNames[(d.date.getDay() + 6) % 7], date: d.date, worked: d.worked, target: d.target })),
					monthWeeks: b.monthWeeks.map((w) => ({ label: weekRange(w.from, w.to), worked: w.worked, target: w.target }))
				};
				return;
			}

			// Without an org there is no schedule and no balance: just the tracked time
			workSchedule = null;
			daysOff = emptyDaysOff();
			const [weekRes, monthEntries] = await Promise.all([
				timeTrackingApi.apiV1TimeTrackingGet(undefined, weekStart.toISOString(), weekEnd.toISOString(), WEEKLY_ENTRY_LIMIT),
				fetchAllPages((limit, offset) => timeTrackingApi.apiV1TimeTrackingGet(undefined, monthStart.toISOString(), monthEnd.toISOString(), limit, offset).then((r) => r.data))
			]);
			const inRange = (from: Date, to: Date) => (e: { startTime?: string }) => {
				const ts = new Date(e.startTime!);
				return ts >= from && ts <= to;
			};
			const weekEntries = weekRes.data.items ?? [];
			const weekDays: Stats['weekDays'] = dayNames.map((label, i) => {
				const d = new Date(weekStart);
				d.setDate(weekStart.getDate() + i);
				const dEnd = new Date(d);
				dEnd.setHours(23, 59, 59, 999);
				return { label, date: d, worked: sumMinutes(weekEntries.filter(inRange(d, dEnd))), target: 0 };
			});
			// Month split into Mon–Sun weeks (clipped to the month)
			const monthWeeks: Stats['monthWeeks'] = [];
			let wkStart = new Date(monthStart);
			while (wkStart <= monthEnd) {
				let wkEnd = new Date(wkStart);
				wkEnd.setDate(wkStart.getDate() + (7 - (wkStart.getDay() || 7)));
				wkEnd.setHours(23, 59, 59, 999);
				if (wkEnd > monthEnd) wkEnd = new Date(monthEnd);
				monthWeeks.push({ label: weekRange(wkStart, wkEnd), worked: sumMinutes(monthEntries.filter(inRange(wkStart, wkEnd))), target: 0 });
				wkStart = new Date(wkEnd);
				wkStart.setDate(wkStart.getDate() + 1);
				wkStart.setHours(0, 0, 0, 0);
			}
			stats = {
				todayMinutes: sumMinutes(weekEntries.filter(inRange(todayStart, todayEnd))),
				weekMinutes: sumMinutes(weekEntries),
				monthMinutes: sumMinutes(monthEntries),
				todayTarget: 0,
				weekTarget: 0,
				monthTarget: 0,
				cumulative: null,
				weekDays,
				monthWeeks
			};
		} catch (err) {
			stats = null;
			statsError = extractErrorMessage(err, t('dashboard.statsFailed'));
		} finally {
			statsLoading = false;
		}
	}

	async function handleStart() {
		pending = true;
		try {
			await timer.start({ organizationSlug: selectedSlug ?? undefined });
		} catch (err) {
			toast.error(err, t('timer.startFailed'));
		} finally {
			pending = false;
		}
	}

	async function handleStop() {
		pending = true;
		try {
			await timer.stop();
			await loadStats(selectedSlug);
		} catch (err) {
			toast.error(err, t('timer.stopFailed'));
		} finally {
			pending = false;
		}
	}

	function deltaClass(delta: number): string {
		return delta > 0 ? 'text-success' : delta < 0 ? 'text-error' : '';
	}
</script>

<svelte:head>
	<title>{t('common.pageTitle', { page: t('dashboard.pageTitle') })}</title>
</svelte:head>

<div class="max-w-3xl mx-auto">
	<div class="flex flex-wrap items-center justify-between gap-2 mb-5">
		<h1 class="text-2xl font-bold text-base-content m-0">{t('dashboard.greeting', { name: auth.user?.firstName ?? '' })}</h1>
	</div>

	<!-- Timer: the primary action -->
	<section class="card mb-6 border-2 p-5 sm:p-6 {timer.current ? 'border-success bg-success/5' : 'border-base-300 bg-base-100'}" aria-label={t('dashboard.timer')}>
		<div class="flex flex-wrap items-center gap-4">
			<div class="min-w-0 flex-1">
				{#if timer.current}
					<div class="flex items-center gap-2 text-sm font-semibold text-success">
						<span class="w-2.5 h-2.5 bg-success rounded-full animate-pulse" aria-hidden="true"></span>
						{t('dashboard.running')}
					</div>
					<div class="text-4xl sm:text-5xl font-bold tabular-nums text-base-content tracking-wide mt-1" role="timer" aria-live="off">{formatClock(timer.elapsedSeconds)}</div>
					<div class="text-sm text-base-content/70 mt-1 truncate">{t('dashboard.forOrg', { org: timer.current.organizationName ?? t('timer.personal') })}</div>
				{:else}
					<div class="text-sm font-semibold text-base-content/70">{t('dashboard.notRunning')}</div>
					<div class="text-4xl sm:text-5xl font-bold tabular-nums text-base-content/60 tracking-wide mt-1">{formatClock(0)}</div>
					<div class="text-sm text-base-content/70 mt-1 truncate">
						{orgContext.selectedOrg ? t('dashboard.startsFor', { org: orgContext.selectedOrg.name ?? '' }) : t('dashboard.startsWithoutOrg')}
					</div>
				{/if}
			</div>
			<div class="flex flex-col items-stretch gap-2 max-sm:w-full">
				{#if timer.current}
					<button type="button" class="btn btn-error btn-lg min-w-40" onclick={handleStop} disabled={pending}>{pending ? t('timer.stopping') : t('timer.stop')}</button>
				{:else}
					<button type="button" class="btn btn-success btn-lg min-w-40" onclick={handleStart} disabled={pending || !timer.loaded}>{pending ? t('timer.starting') : t('timer.start')}</button>
				{/if}
				<a href="/time" class="btn btn-ghost btn-sm">{t('dashboard.noteAndWeek')}</a>
			</div>
		</div>
	</section>

	<!-- Quick actions: the everyday tasks one tap away -->
	{#if selectedSlug}
		<nav class="grid grid-cols-1 gap-2 mb-6 sm:grid-cols-3" aria-label={t('dashboard.quick.label')}>
			<a href="/history?date={yesterday}&do=add" class="btn btn-outline justify-start">{t('dashboard.quick.addDay')}</a>
			<a href="/history?date={dateKey(new Date())}&do=absence" class="btn btn-outline justify-start">{t('dashboard.quick.absence')}</a>
			<a href="/organizations/{selectedSlug}/absences" class="btn btn-outline justify-start">{t('dashboard.quick.teamCalendar')}</a>
		</nav>
	{/if}

	<!-- Actionable empty states -->
	{#if noOrganizations}
		<section class="card bg-base-100 border border-base-300 p-5 mb-6">
			<h2 class="text-base font-semibold m-0">{t('dashboard.noOrg.title')}</h2>
			<p class="text-sm text-base-content/70 mt-1 mb-4">{t('dashboard.noOrg.text')}</p>
			<div class="flex flex-wrap gap-2">
				<a href="/organizations/new" class="btn btn-primary btn-sm">{t('dashboard.noOrg.create')}</a>
				<a href="/organizations" class="btn btn-outline btn-sm">{t('dashboard.noOrg.join')}</a>
			</div>
		</section>
	{:else if !orgContext.loading && !orgContext.selectedOrg}
		<div role="status" class="alert alert-info mb-6 text-sm">{t('dashboard.selectOrg')}</div>
	{/if}

	{#if selectedSlug}
		{#if isOrgAdmin(orgContext.selectedOrg?.role)}<OnboardingChecklist slug={selectedSlug} list="admin" />{/if}
		<OnboardingChecklist slug={selectedSlug} list="member" />
		<TeamOverview slug={selectedSlug} />
	{/if}

	{#if statsLoading && !stats}
		<Skeleton />
	{:else if statsError}
		<div role="alert" class="alert alert-error mb-6 max-sm:alert-vertical">
			<span>{statsError}</span>
			<button type="button" class="btn btn-sm" onclick={() => loadStats(selectedSlug)}>{t('common.retry')}</button>
		</div>
	{:else if stats}
		{#if orgContext.selectedOrg && !hasSchedule}
			<div role="status" class="alert alert-warning mb-6 max-sm:alert-vertical">
				<span>
					<strong>{t('dashboard.noSchedule.title')}</strong>
					{t('dashboard.noSchedule.text')}
				</span>
				<a href="/organizations/{selectedSlug}" class="btn btn-sm">{t('dashboard.noSchedule.action')}</a>
			</div>
		{/if}

		<!-- Key figures -->
		<div class="grid grid-cols-1 sm:grid-cols-3 gap-3 mb-6">
			<div class="card bg-base-100 border border-base-300 p-4">
				<span class="text-xs text-base-content/70 uppercase font-semibold tracking-wider">{t('dashboard.today')}</span>
				<span class="text-2xl font-bold tabular-nums mt-1">
					{formatHours(today)}{#if stats.todayTarget > 0}<span class="text-sm font-normal text-base-content/70"> / {formatHours(stats.todayTarget)}</span>{/if}
				</span>
				{#if stats.todayTarget > 0}
					<progress class="progress {today >= stats.todayTarget ? 'progress-success' : 'progress-primary'} mt-2" value={Math.min(today, stats.todayTarget)} max={stats.todayTarget} aria-label={t('dashboard.todayProgress')}></progress>
					<span class="text-xs mt-1 {deltaClass(today - stats.todayTarget)}">{formatDelta(today - stats.todayTarget)}</span>
				{/if}
			</div>
			<div class="card bg-base-100 border border-base-300 p-4">
				<span class="text-xs text-base-content/70 uppercase font-semibold tracking-wider">{t('dashboard.weekBalance')}</span>
				{#if stats.weekTarget > 0}
					<span class="text-2xl font-bold tabular-nums mt-1 {deltaClass(week - stats.weekTarget)}">{formatDelta(week - stats.weekTarget)}</span>
					<span class="text-xs text-base-content/70 mt-1">{t('dashboard.weekOfTarget', { worked: formatHours(week), target: formatHours(stats.weekTarget) })}</span>
				{:else}
					<span class="text-2xl font-bold tabular-nums mt-1">{formatHours(week)}</span>
					<span class="text-xs text-base-content/70 mt-1">{t('dashboard.workedThisWeek')}</span>
				{/if}
			</div>
			<div class="card border p-4 {cumulative == null ? 'bg-base-100 border-base-300' : cumulative > 0 ? 'border-success/50 bg-success/5' : cumulative < 0 ? 'border-error/50 bg-error/5' : 'bg-base-100 border-base-300'}">
				<span class="text-xs text-base-content/70 uppercase font-semibold tracking-wider">{t('dashboard.cumulativeBalance')}</span>
				{#if cumulative != null}
					<span class="text-2xl font-bold tabular-nums mt-1 {deltaClass(cumulative)}">{formatDelta(cumulative)}</span>
					<span class="text-xs text-base-content/70 mt-1">{t('dashboard.asOfToday')}</span>
				{:else}
					<span class="text-2xl font-bold tabular-nums mt-1 text-base-content/60">–</span>
					<span class="text-xs text-base-content/70 mt-1">{t('dashboard.needsSchedule')}</span>
				{/if}
			</div>
		</div>

		<!-- This week -->
		<section class="card bg-base-100 border border-base-300 p-4 sm:p-5 mb-6">
			<div class="flex items-center justify-between mb-3">
				<h2 class="text-base font-semibold m-0">{t('dashboard.thisWeek')}</h2>
				<a href="/time" class="text-xs text-primary font-semibold">{t('dashboard.openTimer')}</a>
			</div>
			<div class="flex flex-col gap-1.5">
				{#each stats.weekDays as day (day.date.getTime())}
					{@const dayType = getDayType(day.date, daysOff.holidayDates, daysOff.sickDayDates, daysOff.vacationDates, daysOff.otherAbsenceDates, daysOff.halfDayHolidays)}
					{@const dt = dayTypeClasses(dayType)}
					{@const worked = isToday(day.date) ? day.worked + runningMinutes : day.worked}
					<div class="grid grid-cols-[2rem_0.5rem_1fr_auto] items-center gap-2 rounded px-1 -mx-1 {dt.soft} {isToday(day.date) ? 'font-semibold' : ''}">
						<span class="text-sm text-base-content/70">{day.label}</span>
						<span class="w-2 h-2 rounded-full {dt.dot}" title={dayType ? getDayTypeLabel(day.date, daysOff.holidayDates, daysOff.sickDayDates, daysOff.vacationDates, daysOff.otherAbsenceDates, daysOff.halfDayHolidays, daysOff.halfDayAbsences) : undefined}></span>
						<div class="h-2 bg-base-200 rounded-full overflow-hidden">
							<div class="h-full rounded-full transition-all duration-300 {worked > day.target && day.target > 0 ? 'bg-success' : 'bg-primary'}" style="width: {barWidth(worked, day.target)}%"></div>
						</div>
						<span class="text-sm tabular-nums text-right whitespace-nowrap">
							{formatHours(worked)}{#if day.target > 0}<span class="text-xs text-base-content/70"> / {formatHours(day.target)}</span>{/if}
						</span>
					</div>
				{/each}
			</div>
			<DayTypeLegend holidayDates={weekHolidayDates} sickDayDates={weekSickDates} vacationDates={weekVacationDates} otherAbsenceDates={weekOtherAbsenceDates} />
		</section>

		<!-- This month -->
		<section class="card bg-base-100 border border-base-300 p-4 sm:p-5 mb-6">
			<div class="flex items-center justify-between mb-3">
				<h2 class="text-base font-semibold m-0">{getMonthName()}</h2>
				<a href="/history" class="text-xs text-primary font-semibold">{t('dashboard.openHistory')}</a>
			</div>
			<div class="flex flex-col gap-1.5">
				{#each stats.monthWeeks as wk (wk.label)}
					<div class="grid grid-cols-[3.5rem_1fr_auto] items-center gap-2">
						<span class="text-sm text-base-content/70 tabular-nums">{wk.label}</span>
						<div class="h-2 bg-base-200 rounded-full overflow-hidden">
							<div class="h-full rounded-full transition-all duration-300 {wk.worked > wk.target && wk.target > 0 ? 'bg-success' : 'bg-secondary'}" style="width: {barWidth(wk.worked, wk.target)}%"></div>
						</div>
						<span class="text-sm tabular-nums text-right whitespace-nowrap">
							{formatHours(wk.worked)}{#if wk.target > 0}<span class="text-xs text-base-content/70"> / {formatHours(wk.target)}</span>{/if}
						</span>
					</div>
				{/each}
			</div>
			<div class="flex justify-between items-center mt-3 pt-3 border-t border-base-200 text-sm font-semibold">
				<span>{t('dashboard.total', { value: formatHours(month) })}</span>
				{#if stats.monthTarget > 0}
					<span class={deltaClass(month - stats.monthTarget)}>{formatDelta(month - stats.monthTarget)}</span>
				{/if}
			</div>
		</section>
	{/if}
</div>
