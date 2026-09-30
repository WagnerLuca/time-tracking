<script lang="ts">
	import { absenceTypeLabel } from '$lib/utils/formatters';
	import { absenceTypeClasses, tintStyle } from '$lib/utils/colors';

	/** Badge for an absence: built-in types use the theme's day-type colours, custom types their own colour as a tint. */
	interface Props {
		type: string | null | undefined;
		customTypeName?: string | null;
		customTypeColor?: string | null;
		size?: 'xs' | 'sm';
	}
	let { type, customTypeName = null, customTypeColor = null, size = 'sm' }: Props = $props();
</script>

{#if customTypeName}
	<span class="badge {size === 'xs' ? 'badge-xs' : 'badge-sm'} border shrink-0" style={tintStyle(customTypeColor, 20)}>{customTypeName}</span>
{:else}
	<span class="badge {size === 'xs' ? 'badge-xs' : 'badge-sm'} shrink-0 {absenceTypeClasses(type).badge}">{absenceTypeLabel(type)}</span>
{/if}
