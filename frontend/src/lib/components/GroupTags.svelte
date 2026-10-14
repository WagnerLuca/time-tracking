<script lang="ts">
	import type { MemberGroupResponse } from '$lib/api';
	import { t } from '$lib/i18n';
	import { safeColor, tintStyle } from '$lib/utils/colors';

	/**
	 * Colored group tags for a member, e.g. "Frontend" or "Frontend Admin". Inherited memberships
	 * (an ancestor of a direct group) are dashed and, at size sm, say "über Team Küche".
	 */
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
		class:border-dashed={!!g.viaGroupName}
		title={g.viaGroupName ? `${g.name} — ${t('groups.via', { group: g.viaGroupName })}` : g.isGroupAdmin ? `${g.name} — ${t('groups.groupAdmin')}` : (g.name ?? '')}
	>
		<span class="w-1.5 h-1.5 rounded-full shrink-0" style="background: {safeColor(g.color)}" aria-hidden="true"></span>
		{g.name}{g.isGroupAdmin ? t('groups.groupAdminSuffix') : ''}
		{#if g.viaGroupName}<span class="font-normal opacity-70" class:sr-only={size === 'xs'}>({t('groups.via', { group: g.viaGroupName })})</span>{/if}
	</span>
{/each}
