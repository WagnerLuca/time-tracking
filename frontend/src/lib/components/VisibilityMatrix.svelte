<script lang="ts">
	import { t } from '$lib/i18n';
	import {
		VISIBILITY_KINDS, cell, groupCell, overrideValue, parseOverride,
		type Column, type GroupMatrixRow, type MatrixRow, type VisibilityKind
	} from '$lib/utils/visibility';

	/**
	 * The member visibility matrix: rows = kinds of information, columns = in the viewer's own
	 * groups / outside. `org` mode edits the organization's switches, `group` mode a group's
	 * inherit / allow / deny per cell, `readonly` just shows the org values. In group mode `org` is
	 * what the group inherits (for a sub-group: its parent's effective values), and
	 * `inheritedSource` names the group a cell's value comes from (null = the organization).
	 */
	interface Props {
		mode: 'org' | 'group' | 'readonly';
		org: MatrixRow[] | null | undefined;
		/** The group's overrides (group mode). */
		group?: GroupMatrixRow[] | null;
		disabled?: boolean;
		/** Org mode: the new values of one row. */
		onorgchange?: (kind: VisibilityKind, inGroups: boolean, outsideGroups: boolean) => void;
		/** Group mode: the new overrides of one row (null = inherit). */
		ongroupchange?: (kind: VisibilityKind, inGroups: boolean | null, outsideGroups: boolean | null) => void;
		/** Group mode: the ancestor group an inherited cell comes from (null = organization). */
		inheritedSource?: (kind: VisibilityKind, column: Column) => string | null;
	}
	let { mode, org, group = null, disabled = false, onorgchange, ongroupchange, inheritedSource }: Props = $props();

	function inheritLabel(kind: VisibilityKind, column: Column): string {
		const yes = cell(org, kind, column);
		const source = inheritedSource?.(kind, column);
		if (source) return t(yes ? 'visibility.inheritFromYes' : 'visibility.inheritFromNo', { source });
		return yes ? t('visibility.inheritYes') : t('visibility.inheritNo');
	}

	const COLUMNS: Column[] = ['inGroups', 'outsideGroups'];
	const columnLabel = (c: Column) => (c === 'inGroups' ? t('visibility.inGroups') : t('visibility.outsideGroups'));

	function setOrg(kind: VisibilityKind, column: Column, value: boolean) {
		const row = { inGroups: cell(org, kind, 'inGroups'), outsideGroups: cell(org, kind, 'outsideGroups'), [column]: value };
		onorgchange?.(kind, row.inGroups, row.outsideGroups);
	}

	function setGroup(kind: VisibilityKind, column: Column, value: boolean | null) {
		const row = { inGroups: groupCell(group, kind, 'inGroups'), outsideGroups: groupCell(group, kind, 'outsideGroups'), [column]: value };
		ongroupchange?.(kind, row.inGroups, row.outsideGroups);
	}

	/** Org mode: a detail cell is moot while the person is hidden in that column. */
	const moot = (kind: VisibilityKind, column: Column) => mode === 'org' && kind !== 'Member' && !cell(org, 'Member', column);
</script>

{#if mode === 'group'}
	<!-- Stacked below sm (label above its two selects), three columns from sm up -->
	<div class="bg-base-100 border border-base-300 rounded-xl divide-y divide-base-200" data-testid="visibility-matrix">
		<div class="hidden sm:grid grid-cols-[1fr_8rem_8rem] gap-2 px-3 py-2 text-xs font-semibold bg-base-200/50 rounded-t-xl" aria-hidden="true">
			<span>{t('visibility.kind')}</span>
			{#each COLUMNS as c (c)}<span class="text-center">{columnLabel(c)}</span>{/each}
		</div>
		{#each VISIBILITY_KINDS as kind (kind)}
			<div class="grid grid-cols-2 sm:grid-cols-[1fr_8rem_8rem] gap-x-2 gap-y-1 px-3 py-2 items-center">
				<span class="col-span-2 sm:col-span-1 text-sm font-medium">{t(`visibility.kinds.${kind}`)}</span>
				{#each COLUMNS as c (c)}
					{@const value = groupCell(group, kind, c)}
					<div class="flex flex-col gap-0.5 min-w-0">
						<span class="text-[11px] text-base-content/50 sm:hidden" aria-hidden="true">{columnLabel(c)}</span>
						<select
							class="select select-xs w-full {value === true ? 'text-success' : value === false ? 'text-error' : 'text-base-content/60'}"
							aria-label={t('visibility.cellLabel', { kind: t(`visibility.kinds.${kind}`), column: columnLabel(c) })}
							value={overrideValue(value)}
							{disabled}
							onchange={(e) => setGroup(kind, c, parseOverride(e.currentTarget.value))}
						>
							<option value="inherit">{inheritLabel(kind, c)}</option>
							<option value="allow">{t('visibility.allow')}</option>
							<option value="deny">{t('visibility.deny')}</option>
						</select>
					</div>
				{/each}
			</div>
		{/each}
	</div>
{:else}
<div class="overflow-x-auto bg-base-100 border border-base-300 rounded-xl" data-testid="visibility-matrix">
	<table class="table table-sm w-full">
		<thead>
			<tr>
				<th class="bg-base-200/50 text-xs">{t('visibility.kind')}</th>
				{#each COLUMNS as c (c)}
					<th class="bg-base-200/50 text-xs text-center w-24 sm:w-36 px-1 whitespace-normal leading-tight" scope="col">{columnLabel(c)}</th>
				{/each}
			</tr>
		</thead>
		<tbody>
			{#each VISIBILITY_KINDS as kind (kind)}
				<tr>
					<th scope="row" class="font-medium text-sm text-left">{t(`visibility.kinds.${kind}`)}</th>
					{#each COLUMNS as c (c)}
						{@const label = t('visibility.cellLabel', { kind: t(`visibility.kinds.${kind}`), column: columnLabel(c) })}
						{@const value = cell(org, kind, c)}
						<td class="text-center px-1">
							{#if mode === 'org'}
								<input
									type="checkbox"
									class="toggle toggle-primary"
									role="switch"
									checked={value}
									aria-checked={value}
									aria-label={label}
									title={moot(kind, c) ? t('visibility.needsMember') : label}
									class:opacity-40={moot(kind, c)}
									{disabled}
									onchange={(e) => { e.currentTarget.checked = value; setOrg(kind, c, !value); }}
								/>
							{:else}
								<span class="badge badge-sm {value ? 'badge-success' : 'badge-ghost'}">{value ? t('visibility.visible') : t('visibility.hidden')}</span>
							{/if}
						</td>
					{/each}
				</tr>
			{/each}
		</tbody>
	</table>
</div>
{/if}
