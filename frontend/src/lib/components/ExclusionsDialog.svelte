<script lang="ts">
	import Modal from '$lib/components/Modal.svelte';
	import { t } from '$lib/i18n';

	/**
	 * Picks the members left out of a company holiday or a Betriebsurlaub. Saving hands back the whole list
	 * (the API replaces it).
	 */
	interface Props {
		open: boolean;
		name: string;
		members: { id: number; name: string }[];
		selected: number[];
		saving?: boolean;
		onclose: () => void;
		onsave: (userIds: number[]) => void;
	}
	let { open, name, members, selected, saving = false, onclose, onsave }: Props = $props();

	let chosen = $state<number[]>([]);
	$effect(() => {
		if (open) chosen = [...selected];
	});

	function toggle(id: number, on: boolean) {
		chosen = on ? [...chosen, id] : chosen.filter((x) => x !== id);
	}
</script>

<Modal {open} {onclose} title={t('holidays.exclusions.title', { name })}>
	<p class="text-sm text-base-content/70 mt-0">{t('holidays.exclusions.intro')}</p>
	<ul class="flex flex-col gap-1 max-h-80 overflow-y-auto m-0 p-0 list-none">
		{#each members as m (m.id)}
			<li>
				<label class="flex items-center gap-2 text-sm cursor-pointer py-1">
					<input type="checkbox" class="checkbox checkbox-sm" checked={chosen.includes(m.id)} disabled={saving}
						onchange={(e) => toggle(m.id, e.currentTarget.checked)} />
					{m.name}
				</label>
			</li>
		{/each}
	</ul>
	{#snippet actions()}
		<button type="button" class="btn btn-ghost btn-sm" onclick={onclose} disabled={saving}>{t('common.cancel')}</button>
		<button type="button" class="btn btn-primary btn-sm" onclick={() => onsave(chosen)} disabled={saving}>
			{saving ? t('common.saving') : t('common.save')}
		</button>
	{/snippet}
</Modal>
