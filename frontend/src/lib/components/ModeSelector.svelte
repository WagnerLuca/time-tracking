<script lang="ts">
	/**
	 * Segmented control for tri-state/option settings: all options are visible
	 * and the active one is highlighted; click any option to select it.
	 */
	export interface ModeOption {
		value: string | number | null;
		label: string;
		/** btn class applied when this option is active (e.g. 'btn-success'). */
		activeClass?: string;
		hint?: string;
	}

	interface Props {
		options: ModeOption[];
		value: string | number | null | undefined;
		onselect: (value: string | number | null) => void;
		disabled?: boolean;
		size?: 'xs' | 'sm';
	}
	let { options, value, onselect, disabled = false, size = 'sm' }: Props = $props();
</script>

<div class="join shrink-0">
	{#each options as o}
		{@const active = value === o.value}
		<button
			type="button"
			class="join-item btn {size === 'xs' ? 'btn-xs' : 'btn-sm'} font-semibold whitespace-nowrap {active ? (o.activeClass ?? 'btn-primary') : 'btn-ghost border border-base-300 text-base-content/70'}"
			onclick={() => { if (!active) onselect(o.value); }}
			title={o.hint ?? ''}
			{disabled}
			aria-pressed={active}
		>
			{o.label}
		</button>
	{/each}
</div>
