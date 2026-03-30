<script lang="ts">
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
</script>

{#if holidayDates.size > 0 || sickDayDates.size > 0 || vacationDates.size > 0 || otherAbsenceDates.size > 0}
	<div class="flex gap-4 flex-wrap text-xs text-base-content/60 mt-3">
		{#if holidayDates.size > 0}<span class="flex items-center gap-1.5"><span class="w-2 h-2 rounded-full shrink-0 inline-block bg-secondary"></span> Holiday</span>{/if}
		{#if sickDayDates.size > 0}<span class="flex items-center gap-1.5"><span class="w-2 h-2 rounded-full shrink-0 inline-block bg-error"></span> Sick Day</span>{/if}
		{#if vacationDates.size > 0}<span class="flex items-center gap-1.5"><span class="w-2 h-2 rounded-full shrink-0 inline-block bg-accent"></span> Vacation</span>{/if}
		{#if otherAbsenceDates.size > 0}<span class="flex items-center gap-1.5"><span class="w-2 h-2 rounded-full shrink-0 inline-block bg-base-content/40"></span> Other Absence</span>{/if}
	</div>
{/if}
