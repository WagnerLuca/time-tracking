<script lang="ts">
	import type { MemberGroupResponse } from '$lib/api';
	import { safeColor, tintStyle } from '$lib/utils/colors';

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
		style={tintStyle(g.color, 12)}
		title="{g.name}{g.isGroupAdmin ? ' — Group admin' : ''}"
	>
		<span class="w-1.5 h-1.5 rounded-full shrink-0" style="background: {safeColor(g.color)}" aria-hidden="true"></span>
		{g.name}{g.isGroupAdmin ? ' Admin' : ''}
	</span>
{/each}
