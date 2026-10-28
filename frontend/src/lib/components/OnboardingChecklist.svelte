<script lang="ts">
	import { untrack } from 'svelte';
	import { auth } from '$lib/stores/auth.svelte';
	import { timer } from '$lib/stores/timer.svelte';
	import { getOrgDetail } from '$lib/stores/orgDetail';
	import { holidayApi, invitationsApi, timeTrackingApi, workScheduleApi } from '$lib/apiClient';
	import { adminSteps, dismissChecklist, isDismissed, markOpen, memberSteps, wasOpen, wasSeen, type Checklist, type Step } from '$lib/utils/onboarding';
	import { t } from '$lib/i18n';

	/** The dashboard's first-steps card: auto-ticked from real data, dismissible per user + org. */
	let { slug, list }: { slug: string; list: Checklist } = $props();

	let loaded = $state<Step[]>([]);
	let hidden = $state(true);
	let complete = $state(false);
	const userId = $derived(auth.user?.id ?? 0);
	// A timer started on the dashboard ticks its step right here
	const steps = $derived(loaded.map((s) => (s.id === 'timer' && timer.current ? { ...s, done: true } : s)));
	const done = $derived(steps.filter((s) => s.done).length);

	// The last item ticked while the card is shown: the once-only "all done" moment
	$effect(() => {
		if (!hidden && steps.length > 0 && done === steps.length) untrack(finish);
	});

	function finish() {
		if (complete) return;
		complete = true;
		dismissChecklist(userId, slug, list); // never again
	}

	$effect(() => {
		const s = slug;
		untrack(() => load(s));
	});

	async function load(s: string) {
		hidden = true;
		complete = false;
		if (!userId || isDismissed(userId, s, list)) return;
		try {
			if (list === 'member') {
				const [schedule, entries] = await Promise.all([
					workScheduleApi.apiV1OrganizationsSlugWorkScheduleGet(s).then((r) => !!r.data?.weeklyWorkHours).catch(() => false),
					timeTrackingApi.apiV1TimeTrackingGet(undefined, undefined, undefined, 1).then((r) => (r.data.items?.length ?? 0) > 0)
				]);
				loaded = memberSteps(s, { hasSchedule: schedule, hasEntries: entries || !!timer.current, sawTeamCalendar: wasSeen(userId, s, 'teamCalendar') });
			} else {
				const [org, invites, holidays] = await Promise.all([
					getOrgDetail(s),
					invitationsApi.apiV1OrganizationsSlugInvitationsGet(s).then((r) => r.data.length).catch(() => 0),
					holidayApi.apiV1OrganizationsSlugHolidaysGet(s).then((r) => r.data.length)
				]);
				loaded = adminSteps(s, { hasInvited: (org.members?.length ?? 0) > 1 || invites > 0, sawRules: wasSeen(userId, s, 'rules'), hasHolidays: holidays > 0 || !!org.holidayRegion });
			}
			if (loaded.every((st) => st.done)) {
				// Finished elsewhere since the card was last shown unfinished: celebrate once, else stay away
				if (wasOpen(userId, s, list)) finish();
				hidden = !complete;
			} else {
				hidden = false;
				markOpen(userId, s, list);
			}
		} catch {
			// Optional help only: no card when the facts cannot be loaded
		}
	}

	function dismiss() {
		dismissChecklist(userId, slug, list);
		hidden = true;
	}
</script>

{#if !hidden}
	<section class="card mb-6 border border-primary/40 bg-primary/5 p-4 sm:p-5" aria-labelledby="onboarding-{list}" data-testid="onboarding-{list}">
		<div class="flex items-start justify-between gap-2">
			<div>
				<h2 id="onboarding-{list}" class="text-base font-semibold m-0">{list === 'member' ? t('onboarding.memberTitle') : t('onboarding.adminTitle')}</h2>
				<p class="m-0 text-xs text-base-content/70">{t('onboarding.progress', { done, total: steps.length })}</p>
			</div>
			<button type="button" class="btn btn-ghost btn-sm btn-square" aria-label={t('onboarding.dismiss')} title={t('onboarding.dismiss')} onclick={dismiss}>✕</button>
		</div>
		{#if complete}
			<p class="onboarding-done m-0 mt-3 flex w-fit items-center gap-2 text-sm font-semibold text-primary" role="status">
				<span class="grid size-8 place-items-center rounded-full bg-primary text-primary-content" aria-hidden="true">✓</span>
				{t('onboarding.complete')}
			</p>
		{/if}
		<ol class="m-0 mt-3 flex list-none flex-col gap-1 p-0">
			{#each steps as step (step.id)}
				<li>
					<a href={step.href} class="flex min-h-11 items-center gap-3 rounded-lg px-2 py-1.5 no-underline text-base-content hover:bg-base-200">
						<span class="grid size-6 shrink-0 place-items-center rounded-full border-2 text-xs {step.done ? 'border-primary bg-primary text-primary-content' : 'border-base-content/40'}" aria-hidden="true">{step.done ? '✓' : ''}</span>
						<span class="min-w-0">
							<span class="block text-sm font-medium {step.done ? 'line-through text-base-content/70' : ''}">{t(`onboarding.steps.${step.id}.label`)}{#if step.done}<span class="sr-only"> ({t('onboarding.done')})</span>{/if}</span>
							<span class="block text-xs text-base-content/70">{t(`onboarding.steps.${step.id}.hint`)}</span>
						</span>
					</a>
				</li>
			{/each}
		</ol>
	</section>
{/if}
