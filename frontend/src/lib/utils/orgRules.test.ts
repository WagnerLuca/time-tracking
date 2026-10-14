import { afterEach, describe, expect, it } from 'vitest';
import { locale } from '$lib/stores/locale.svelte';
import {
	canEditEntries,
	canEditPause,
	canImportCsv,
	canRequestEditEntries,
	canRequestEditPause,
	csvImportEnabled,
	isAllowed,
	isDisabled,
	isOrgAdmin,
	isRequiresApproval,
	joinPolicyLabel,
	joinPolicyOptions,
	parseRuleMode,
	ruleMode,
	ruleModeButtonClass,
	ruleModeLabel,
	ruleModeOptions,
	settingTopics,
	appliedSetting,
	withInherit
} from './orgRules';

const MODES = ['Disabled', 'RequiresApproval', 'Allowed'] as const;

describe('orgRules', () => {
	afterEach(() => locale.set('de'));

	it.each([
		['Owner', true],
		['Admin', true],
		['Member', false],
		['owner', false],
		['', false],
		[null, false],
		[undefined, false]
	])('isOrgAdmin(%s) is %s', (role, expected) => {
		expect(isOrgAdmin(role)).toBe(expected);
	});

	it('mode predicates match exactly one mode each', () => {
		for (const m of MODES) {
			expect([isDisabled(m), isRequiresApproval(m), isAllowed(m)]).toEqual(MODES.map((x) => x === m));
		}
		for (const m of [null, undefined, '', 'allowed']) {
			expect(isAllowed(m) || isRequiresApproval(m) || isDisabled(m)).toBe(false);
		}
	});

	it('effective permissions (group overrides) win over the org default', () => {
		const org = { editPastEntriesMode: 'Disabled', editPauseMode: 'Allowed', csvImportMode: 'Allowed' };
		const effective = { editPastEntriesMode: 'Allowed', editPauseMode: 'Disabled', csvImportMode: 'RequiresApproval' };
		expect(canEditEntries(effective, org)).toBe(true);
		expect(canEditPause(effective, org)).toBe(false);
		expect(canImportCsv(effective, org)).toBe(false);
		expect(csvImportEnabled(effective, org)).toBe(true);
	});

	// Each helper against every (effective, org default) pair: the effective mode decides, whatever the default
	const helpers = [
		{ fn: canEditEntries, key: 'editPastEntriesMode', on: 'Allowed' },
		{ fn: canRequestEditEntries, key: 'editPastEntriesMode', on: 'RequiresApproval' },
		{ fn: canEditPause, key: 'editPauseMode', on: 'Allowed' },
		{ fn: canRequestEditPause, key: 'editPauseMode', on: 'RequiresApproval' },
		{ fn: canImportCsv, key: 'csvImportMode', on: 'Allowed' }
	] as const;

	it.each(helpers)('$fn.name: effective wins in both directions, org default applies otherwise', ({ fn, key, on }) => {
		for (const effective of MODES) {
			for (const orgDefault of MODES) {
				expect(fn({ [key]: effective }, { [key]: orgDefault })).toBe(effective === on);
				// effective permissions not loaded / not set → the org default decides
				expect(fn(null, { [key]: orgDefault })).toBe(orgDefault === on);
				expect(fn(undefined, { [key]: orgDefault })).toBe(orgDefault === on);
				expect(fn({ [key]: undefined }, { [key]: orgDefault })).toBe(orgDefault === on);
				expect(fn({ [key]: null } as never, { [key]: orgDefault })).toBe(orgDefault === on);
			}
		}
		// nothing known at all → never granted
		expect(fn()).toBe(false);
		expect(fn(null, null)).toBe(false);
		expect(fn({}, {})).toBe(false);
	});

	it('csvImportEnabled: on unless the winning source says Disabled', () => {
		expect(csvImportEnabled()).toBe(true);
		expect(csvImportEnabled(null)).toBe(true);
		expect(csvImportEnabled({ csvImportMode: 'Disabled' })).toBe(false);
		expect(csvImportEnabled({ csvImportMode: 'RequiresApproval' })).toBe(true);
		expect(csvImportEnabled({ csvImportMode: 'Allowed' }, { csvImportMode: 'Disabled' })).toBe(true);
		expect(csvImportEnabled({ csvImportMode: 'Disabled' }, { csvImportMode: 'Allowed' })).toBe(false);
		expect(csvImportEnabled(null, { csvImportMode: 'Disabled' })).toBe(false);
	});

	it('ruleMode returns the first defined source, else null', () => {
		const org = { editPastEntriesMode: 'RequiresApproval' };
		expect(ruleMode('editPastEntriesMode', undefined, org)).toBe('RequiresApproval');
		expect(ruleMode('editPastEntriesMode', { editPastEntriesMode: 'Allowed' }, org)).toBe('Allowed');
		expect(ruleMode('editPauseMode', null, null)).toBeNull();
		expect(ruleMode('editPauseMode')).toBeNull();
		// other keys of the same source do not leak
		expect(ruleMode('workScheduleChangeMode', { editPauseMode: 'Allowed' })).toBeNull();
	});

	it('parseRuleMode maps modes to segment indexes, unknown → Allowed', () => {
		expect(MODES.map(parseRuleMode)).toEqual([0, 1, 2]);
		expect(parseRuleMode(null)).toBe(2);
		expect(parseRuleMode('bogus')).toBe(2);
	});

	it('ruleModeLabel / joinPolicyLabel / ruleModeButtonClass, unknown → Allowed', () => {
		expect(MODES.map(ruleModeLabel)).toEqual(['Deaktiviert', 'Genehmigung erforderlich', 'Erlaubt']);
		expect(ruleModeLabel(undefined)).toBe('Erlaubt');
		expect(MODES.map(joinPolicyLabel)).toEqual(['Nur durch Admins', 'Genehmigung erforderlich', 'Offen']);
		expect(joinPolicyLabel(null)).toBe('Offen');
		expect(MODES.map(ruleModeButtonClass)).toEqual(['btn-neutral', 'btn-warning', 'btn-success']);
		expect(ruleModeButtonClass(null)).toBe('btn-success');
	});

	it('option sets have values 0..2 with matching classes, inherit is prepended', () => {
		for (const options of [ruleModeOptions(), joinPolicyOptions()]) {
			expect(options.map((o) => [o.value, o.activeClass])).toEqual([
				[0, 'btn-neutral'],
				[1, 'btn-warning'],
				[2, 'btn-success']
			]);
			expect(options.every((o) => o.label && o.hint)).toBe(true);
		}
		const options = withInherit(ruleModeOptions());
		expect(options.map((o) => o.value)).toEqual([null, 0, 1, 2]);
		expect(options[0]).toMatchObject({ label: 'Erben', activeClass: 'btn-info' });
	});

	it('labels follow the current UI language (functions, not constants)', () => {
		locale.set('en');
		expect(ruleModeLabel('Disabled')).toBe('Disabled');
		expect(withInherit([])[0].label).toBe('Inherit');
		expect(joinPolicyOptions()[2].label).toBe('Open');
	});

	it('settings topics cover every rule once, with translated one-line text', () => {
		locale.set('de');
		const keys = settingTopics().flatMap((topic) => topic.settings.map((s) => s.key));
		expect(new Set(keys).size).toBe(10);
		for (const s of settingTopics().flatMap((topic) => topic.settings)) {
			// a missing key would come back as the key path itself
			expect(s.label).not.toContain('format.rules');
			expect(s.description).not.toContain('format.rules');
		}
		expect(settingTopics()[0].settings.find((s) => s.key === 'autoPauseEnabled')?.toggle).toBe(true);
		expect(settingTopics()[0].settings[0].label).toBe('Eigene Einträge nachträglich ändern');
	});

	it('says what applies to the caller, group overrides first', () => {
		locale.set('de');
		const org = { editPastEntriesMode: 'RequiresApproval', autoPauseEnabled: true, joinPolicy: 'Disabled', csvImportMode: 'Allowed' } as never;
		expect(appliedSetting('editPastEntriesMode', { editPastEntriesMode: 'Allowed' }, org)).toEqual({ label: ruleModeLabel('Allowed'), badge: 'badge-success', fromGroup: true });
		expect(appliedSetting('csvImportMode', null, org)).toMatchObject({ label: ruleModeLabel('Allowed'), fromGroup: false });
		expect(appliedSetting('autoPauseEnabled', null, org)).toMatchObject({ badge: 'badge-success', fromGroup: false });
		expect(appliedSetting('require2fa', null, org).badge).toBe('badge-ghost');
		expect(appliedSetting('joinPolicy', null, org).label).toBe(joinPolicyLabel('Disabled'));
	});
});
