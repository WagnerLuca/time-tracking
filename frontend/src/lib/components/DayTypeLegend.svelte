<script lang="ts">
	import { DAY_TYPE_CLASSES } from '$lib/utils/colors';

	let {
		holidayDates,
		sickDayDates,
		vacationDates,
		otherAbsenceDates
	}: {
		holidayDates: Map<string, string>;
		sickDayDates: Set<string>;
		vacationDates: Set<string>;
		otherAbsenceDates: Set<string>;
	} = $props();

	const items = $derived([
		{ show: holidayDates.size > 0, dot: DAY_TYPE_CLASSES.holiday.dot, label: 'Holiday' },
		{ show: sickDayDates.size > 0, dot: DAY_TYPE_CLASSES.sick.dot, label: 'Sick day' },
		{ show: vacationDates.size > 0, dot: DAY_TYPE_CLASSES.vacation.dot, label: 'Vacation' },
		{ show: otherAbsenceDates.size > 0, dot: DAY_TYPE_CLASSES['other-absence'].dot, label: 'Other absence' }
	].filter((i) => i.show));
</script>

{#if items.length > 0}
	<div class="flex gap-4 flex-wrap text-xs text-base-content/60 mt-3">
		{#each items as item (item.label)}
			<span class="flex items-center gap-1.5"><span class="w-2 h-2 rounded-full shrink-0 inline-block {item.dot}" aria-hidden="true"></span> {item.label}</span>
		{/each}
	</div>
{/if}
