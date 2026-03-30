import type { OrganizationDetailResponse } from '$lib/api';

// ── Rule mode checks ──

export function isAllowed(mode: string | null | undefined): boolean {
	return mode === 'Allowed';
}

export function isRequiresApproval(mode: string | null | undefined): boolean {
	return mode === 'RequiresApproval';
}

export function isDisabled(mode: string | null | undefined): boolean {
	return mode === 'Disabled';
}

// ── Convenience helpers for common org rules ──

export function canEditEntries(org: OrganizationDetailResponse | null): boolean {
	return isAllowed(org?.editPastEntriesMode);
}

export function canRequestEditEntries(org: OrganizationDetailResponse | null): boolean {
	return isRequiresApproval(org?.editPastEntriesMode);
}

export function canEditPause(org: OrganizationDetailResponse | null): boolean {
	return isAllowed(org?.editPauseMode);
}

export function canRequestEditPause(org: OrganizationDetailResponse | null): boolean {
	return isRequiresApproval(org?.editPauseMode);
}

// ── Settings page helpers ──

export function parseRuleMode(mode: string | null | undefined): number {
	if (mode === 'Disabled') return 0;
	if (mode === 'RequiresApproval') return 1;
	if (mode === 'Allowed') return 2;
	return 2;
}

export function ruleModeLabel(mode: string | null | undefined): string {
	if (mode === 'Disabled') return 'Disabled';
	if (mode === 'RequiresApproval') return 'Requires Approval';
	return 'Allowed';
}

export function joinPolicyLabel(mode: string | null | undefined): string {
	if (mode === 'Disabled') return 'Admin Only';
	if (mode === 'RequiresApproval') return 'Requires Approval';
	return 'Open';
}

export function ruleModeButtonClass(mode: string | null | undefined): string {
	if (mode === 'Disabled') return 'btn-neutral';
	if (mode === 'RequiresApproval') return 'btn-warning';
	return 'btn-success';
}

// ── Data-driven settings definitions ──

export interface RuleSetting {
	key: string;
	label: string;
	description: string;
	labelFn?: (mode: string | null | undefined) => string;
}

export interface ToggleSetting {
	key: string;
	label: string;
	description: string;
}

export const ruleSettings: RuleSetting[] = [
	{
		key: 'editPastEntriesMode',
		label: 'Edit Past Entries',
		description: 'Control whether members can edit start/end times of completed time entries.'
	},
	{
		key: 'editPauseMode',
		label: 'Edit Pause Duration',
		description: 'Control whether members can override auto-deducted break time.'
	},
	{
		key: 'initialOvertimeMode',
		label: 'Initial Overtime',
		description: 'Control whether members can set their own initial overtime balance.'
	},
	{
		key: 'joinPolicy',
		label: 'Join Policy',
		description: 'Control how new members can join the organization.',
		labelFn: joinPolicyLabel
	},
	{
		key: 'workScheduleChangeMode',
		label: 'Schedule Periods',
		description: 'Control whether members can create/modify their own schedule periods.'
	}
];

export const toggleSettings: ToggleSetting[] = [
	{
		key: 'autoPauseEnabled',
		label: 'Auto-Pause Tracking',
		description: 'Automatically deduct break time from tracked hours based on configurable rules.'
	},
	{
		key: 'memberTimeEntryVisibility',
		label: 'Member Time Visibility',
		description: "When enabled, admins can view members' tracked working hours. Members will see a notification about this."
	},
	{
		key: 'require2fa',
		label: 'Require Two-Factor Authentication',
		description: 'When enabled, all members must set up 2FA before they can use the application.'
	}
];
