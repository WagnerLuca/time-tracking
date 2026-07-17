<script lang="ts">
	import type { MemberGroupResponse } from '$lib/api';

	/** Colored group tags for a member, e.g. "Frontend" or "Frontend Admin". */
	interface Props {
		groups?: MemberGroupResponse[] | null;
		size?: 'xs' | 'sm';
	}
	let { groups = [], size = 'xs' }: Props = $props();
</script>

{#each groups ?? [] as g}
	<span
		class="badge {size === 'xs' ? 'badge-xs' : 'badge-sm'} gap-1 border font-medium"
		style="border-color: {g.color ?? '#888888'}55; background: {g.color ?? '#888888'}1f;"
		title="{g.name}{g.isGroupAdmin ? ' — Group Admin' : ''}"
	>
		<span class="w-1.5 h-1.5 rounded-full shrink-0" style="background: {g.color ?? '#888888'}"></span>
		{g.name}{g.isGroupAdmin ? ' Admin' : ''}
	</span>
{/each}
