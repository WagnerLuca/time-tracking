import { describe, expect, it } from 'vitest';
import { canEditEntries, canEditPause, canImportCsv, canRequestEditEntries, csvImportEnabled, isOrgAdmin, ruleMode, ruleModeOptions, withInherit } from './orgRules';

describe('orgRules', () => {
	it('isOrgAdmin', () => {
		expect(isOrgAdmin('Owner')).toBe(true);
		expect(isOrgAdmin('Admin')).toBe(true);
		expect(isOrgAdmin('Member')).toBe(false);
		expect(isOrgAdmin(null)).toBe(false);
		expect(isOrgAdmin(undefined)).toBe(false);
	});

	it('effective permissions (group overrides) win over the org default', () => {
		const org = { editPastEntriesMode: 'Disabled', editPauseMode: 'Allowed', csvImportMode: 'Allowed' };
		const effective = { editPastEntriesMode: 'Allowed', editPauseMode: 'Disabled', csvImportMode: 'RequiresApproval' };
		expect(canEditEntries(effective, org)).toBe(true);
		expect(canEditPause(effective, org)).toBe(false);
		expect(canImportCsv(effective, org)).toBe(false);
		expect(csvImportEnabled(effective, org)).toBe(true);
	});

	it('falls back to the org default while effective permissions are not loaded', () => {
		const org = { editPastEntriesMode: 'RequiresApproval' };
		expect(canEditEntries(null, org)).toBe(false);
		expect(canRequestEditEntries(null, org)).toBe(true);
		expect(ruleMode('editPastEntriesMode', undefined, org)).toBe('RequiresApproval');
		expect(ruleMode('editPauseMode', null, null)).toBeNull();
	});

	it('csv import is enabled unless explicitly disabled', () => {
		expect(csvImportEnabled(null)).toBe(true);
		expect(csvImportEnabled({ csvImportMode: 'Disabled' })).toBe(false);
	});

	it('option sets are translated and inherit is prepended', () => {
		const options = withInherit(ruleModeOptions());
		expect(options.map((o) => o.value)).toEqual([null, 0, 1, 2]);
		expect(options[0].label).toBe('Erben');
	});
});
