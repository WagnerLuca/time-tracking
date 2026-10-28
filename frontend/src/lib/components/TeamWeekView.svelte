<script lang="ts">
	import { untrack } from 'svelte';
	import { appointmentsApi } from '$lib/apiClient';
	import { t } from '$lib/i18n';
	import { absenceTypeLabel, formatDate, getInitials } from '$lib/utils/formatters';
	import { dateKey, parseDateKey } from '$lib/utils/dateHelpers';
	import { absenceChipClass, absenceChipStyle } from '$lib/utils/colors';
	import { extractErrorMessage } from '$lib/utils/errorHandler';
	import { appointmentLabel, buildWeekRows, isoWeek, weekDayKeys, weekStart, type WeekRow } from '$lib/utils/weekGrid';
	import AppointmentDialog from '$lib/components/AppointmentDialog.svelte';
	import PresenceDot from '$lib/components/PresenceDot.svelte';
	import type { AbsenceDayResponse, AppointmentResponse, HolidayResponse, OrganizationMemberResponse } from '$lib/api';

	/**
	 * Team calendar week view (#83): one row per visible member, Mo–So columns with absences and
	 * appointments ("Termine"). The page loads absences/holidays; appointments are loaded here.
	 */
	interface Props {
		orgSlug: string;
		members: OrganizationMemberResponse[];
		me: number | null | undefined;
		isAdmin: boolean;
		absences: AbsenceDayResponse[];
		holidayMap: Map<string, HolidayResponse[]>;
		groupId: number | null;
		/** The page's member filter (one person), null for everyone. */
		userFilter: number | null;
		/** A day in the week to show first. */
		initialDate: Date;
		hideWeekend: boolean;
		/** Called with the shown Monday, so the page loads that year's absences. */
		onweekchange: (monday: Date) => void;
		/** Opens the page's absence dialog for a date (when the viewer may add absences). */
		onabsence?: (date: string) => void;
	}
	let { orgSlug, members, me, isAdmin, absences, holidayMap, groupId, userFilter, initialDate, hideWeekend = $bindable(), onweekchange, onabsence }: Props = $props();

	let monday = $state(untrack(() => weekStart(initialDate)));
	const allDays = $derived(weekDayKeys(monday));
	const days = $derived(hideWeekend ? allDays.slice(0, 5) : allDays);
	const today = dateKey(new Date());
	const showsToday = $derived(allDays.includes(today));

	$effect(() => {
		const m = monday;
		untrack(() => onweekchange(m));
	});

	function move(weeks: number) {
		monday = new Date(monday.getFullYear(), monday.getMonth(), monday.getDate() + weeks * 7);
	}

	let appointments = $state<AppointmentResponse[]>([]);
	let loadError = $state('');
	let request = 0;

	async function loadAppointments() {
		const id = ++request;
		const [from, to] = [allDays[0], allDays[6]];
		try {
			const { data } = await appointmentsApi.apiV1OrganizationsSlugAppointmentsGet(orgSlug, from, to, undefined, groupId ?? undefined);
			if (id !== request) return;
			appointments = data;
			loadError = '';
		} catch (err) {
			if (id !== request) return;
			appointments = [];
			loadError = extractErrorMessage(err, t('appointments.week.loadFailed'));
		}
	}

	$effect(() => {
		void [orgSlug, allDays[0], groupId];
		untrack(loadAppointments);
	});

	const rows = $derived(
		buildWeekRows({
			members: userFilter === null ? members : members.filter((m) => m.id === userFilter),
			me,
			days: allDays,
			absences,
			appointments,
			groupId
		})
	);

	function name(m: OrganizationMemberResponse): string {
		return `${m.firstName ?? ''} ${m.lastName ?? ''}`.trim();
	}

	function canAdd(row: WeekRow): boolean {
		return row.isMe || isAdmin;
	}

	function showGroupHeader(i: number): boolean {
		const row = rows[i];
		return !row.isMe && (i === 0 || rows[i - 1].isMe || rows[i - 1].group !== row.group);
	}

	// Appointment form
	let dialogOpen = $state(false);
	let dialogDate = $state('');
	let dialogUser = $state<OrganizationMemberResponse | null>(null);
	let editing = $state<AppointmentResponse | null>(null);

	function openNew(member: OrganizationMemberResponse | null, date: string) {
		editing = null;
		dialogUser = member;
		dialogDate = date;
		dialogOpen = true;
	}

	function openEdit(a: AppointmentResponse) {
		editing = a;
		dialogUser = members.find((m) => m.id === a.userId) ?? null;
		dialogOpen = true;
	}

	/** Header "+ Termin": today when it is in the shown week, else its Monday. */
	function openNewForWeek() {
		openNew(members.find((m) => m.id === me) ?? null, showsToday ? today : allDays[0]);
	}

	function isWeekend(key: string): boolean {
		const d = parseDateKey(key).getDay();
		return d === 0 || d === 6;
	}

	function absenceTitle(a: AbsenceDayResponse): string {
		return `${a.customTypeName ?? absenceTypeLabel(a.type)}${a.isHalfDay ? ' (½)' : ''}${a.note ? `: ${a.note}` : ''}`;
	}
</script>

<div data-testid="week-view">
	<!-- Week navigation -->
	<div class="flex items-center justify-between gap-2 mb-3">
		<button class="btn btn-ghost btn-sm" aria-label={t('appointments.week.prev')} title={t('appointments.week.prev')} onclick={() => move(-1)}>
			<svg class="w-5 h-5" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true"><path fill-rule="evenodd" d="M12.79 5.23a.75.75 0 01-.02 1.06L8.832 10l3.938 3.71a.75.75 0 11-1.04 1.08l-4.5-4.25a.75.75 0 010-1.08l4.5-4.25a.75.75 0 011.06.02z" clip-rule="evenodd"/></svg>
		</button>
		<div class="flex items-center gap-2 sm:gap-3 flex-wrap justify-center">
			<h2 id="calendar-heading" class="text-lg sm:text-xl font-bold text-base-content m-0" aria-live="polite">
				{t('appointments.week.label', { week: isoWeek(monday) })}
				<span class="text-sm font-normal text-base-content/70">{formatDate(allDays[0], { day: 'numeric', month: 'short' })} – {formatDate(allDays[6], { day: 'numeric', month: 'short', year: 'numeric' })}</span>
			</h2>
			<button class="btn btn-ghost btn-xs" onclick={() => (monday = weekStart(new Date()))}>{t('absences.today')}</button>
		</div>
		<button class="btn btn-ghost btn-sm" aria-label={t('appointments.week.next')} title={t('appointments.week.next')} onclick={() => move(1)}>
			<svg class="w-5 h-5" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true"><path fill-rule="evenodd" d="M7.21 14.77a.75.75 0 01.02-1.06L11.168 10 7.23 6.29a.75.75 0 111.04-1.08l4.5 4.25a.75.75 0 010 1.08l-4.5 4.25a.75.75 0 01-1.06-.02z" clip-rule="evenodd"/></svg>
		</button>
	</div>

	<div class="flex items-center justify-between gap-2 mb-3 flex-wrap">
		<label class="label cursor-pointer flex items-center gap-2 text-sm">
			<input type="checkbox" class="checkbox checkbox-sm" bind:checked={hideWeekend} />
			{t('appointments.week.hideWeekend')}
		</label>
		<button type="button" class="btn btn-primary btn-sm" data-testid="add-appointment" onclick={openNewForWeek}>{t('appointments.add')}</button>
	</div>

	{#if loadError}<div class="alert alert-error text-sm mb-3" role="alert">{loadError}</div>{/if}

	<!-- Scrolls sideways on its own on a phone; the page never does -->
	<div class="overflow-x-auto border border-base-300 rounded-lg">
		<table class="w-full table-fixed border-collapse text-sm" style="min-width: {hideWeekend ? 34 : 40}rem">
			<colgroup>
				<col class="w-28 sm:w-40" />
				{#each days as d (d)}<col style={isWeekend(d) ? 'width: 4.5rem' : ''} />{/each}
			</colgroup>
			<thead>
				<tr>
					<th scope="col" class="sticky left-0 z-20 bg-base-100 text-left text-xs font-semibold text-base-content/70 p-2">{t('appointments.week.member')}</th>
					{#each days as d (d)}
						{@const holidays = holidayMap.get(d) ?? []}
						<th scope="col" class="p-1.5 text-left align-top border-l border-base-300 text-xs font-semibold {d === today ? 'bg-primary/10' : isWeekend(d) ? 'bg-base-200/40' : ''}" aria-current={d === today ? 'date' : undefined}>
							<span class={d === today ? 'text-primary' : 'text-base-content/70'}>{formatDate(d, { weekday: 'short', day: 'numeric', month: 'numeric' })}</span>
							{#each holidays as h (h.id)}
								<span class="block truncate font-normal rounded px-1 mt-0.5 bg-secondary/10" title={h.name ?? ''}>{h.name}{h.isHalfDay ? ' ½' : ''}</span>
							{/each}
						</th>
					{/each}
				</tr>
			</thead>
			<tbody>
				{#each rows as row, i (row.member.id)}
					{@const id = row.member.id}
					{#if showGroupHeader(i)}
						<tr>
							<th scope="colgroup" colspan={days.length + 1} class="text-left text-xs font-semibold text-base-content/70 bg-base-200/60 px-2 py-1 border-t border-base-300">{row.group ?? t('appointments.week.noGroup')}</th>
						</tr>
					{/if}
					<tr data-testid="week-row-{id}">
						<th scope="row" class="sticky left-0 z-10 bg-base-100 p-1.5 text-left font-medium border-t border-base-300">
							<span class="flex items-center gap-2 min-w-0">
								<span class="relative w-7 h-7 rounded-full bg-primary text-primary-content flex items-center justify-center text-[10px] font-semibold shrink-0">
									<span aria-hidden="true">{getInitials(row.member.firstName, row.member.lastName)}</span>
									{#if showsToday}<PresenceDot present={row.member.isPresent} />{/if}
								</span>
								<span class="truncate">{name(row.member)}{#if row.isMe}<span class="text-base-content/70 font-normal"> {t('appointments.week.you')}</span>{/if}</span>
							</span>
						</th>
						{#each days as d (d)}
							{@const cell = row.cells[d]}
							<td class="relative align-top p-1 border-t border-l border-base-300 {d === today ? 'bg-primary/5' : isWeekend(d) ? 'bg-base-200/40' : ''}" data-testid="week-cell-{id}-{d}">
								{#if canAdd(row)}
									<!-- The cell's empty area: a button behind the chips -->
									<button
										type="button"
										class="absolute inset-0 w-full h-full hover:bg-primary/5 focus-visible:outline-2 focus-visible:outline-primary focus-visible:-outline-offset-2"
										aria-label={t('appointments.addFor', { date: formatDate(d), name: name(row.member) })}
										onclick={() => openNew(row.member, d)}
									></button>
								{/if}
								<div class="relative flex flex-col gap-0.5 min-h-11 pointer-events-none">
									{#each cell.absences as a (a.id)}
										<span class="pointer-events-auto block truncate rounded border px-1 text-[11px] leading-5 text-base-content {absenceChipClass(a)}" style={absenceChipStyle(a)} title={absenceTitle(a)}>
											{a.customTypeName ?? absenceTypeLabel(a.type)}{a.isHalfDay ? ' ½' : ''}
										</span>
									{/each}
									{#each cell.appointments as a (a.id)}
										{@const label = appointmentLabel(a)}
										{#if a.canEdit}
											<button type="button" class="pointer-events-auto block w-full truncate text-left rounded border border-primary/40 bg-primary/10 px-1 text-[11px] leading-5 pointer-coarse:min-h-11 hover:bg-primary/20" title={label} data-testid="appointment-chip" onclick={() => openEdit(a)}>{label}</button>
										{:else}
											<span class="pointer-events-auto block truncate rounded border border-primary/30 bg-primary/5 px-1 text-[11px] leading-5" title={label} data-testid="appointment-chip">{label}</span>
										{/if}
									{/each}
								</div>
							</td>
						{/each}
					</tr>
				{:else}
					<tr><td colspan={days.length + 1} class="p-4 text-center text-base-content/70">{t('appointments.week.empty')}</td></tr>
				{/each}
			</tbody>
		</table>
	</div>
</div>

<AppointmentDialog
	open={dialogOpen}
	{orgSlug}
	date={dialogDate}
	appointment={editing}
	userId={dialogUser?.id ?? null}
	userName={dialogUser ? name(dialogUser) : undefined}
	onclose={() => (dialogOpen = false)}
	onsaved={loadAppointments}
	{onabsence}
/>
