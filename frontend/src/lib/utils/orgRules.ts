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

export function canImportCsv(org: OrganizationDetailResponse | null): boolean {
	return isAllowed(org?.csvImportMode);
}

export function csvImportEnabled(org: OrganizationDetailResponse | null): boolean {
	return !isDisabled(org?.csvImportMode);
}

export function canAddOwnAbsences(org: OrganizationDetailResponse | null): boolean {
	return isAllowed(org?.absenceSelfServiceMode);
}

export function canRequestAbsences(org: OrganizationDetailResponse | null): boolean {
	return isRequiresApproval(org?.absenceSelfServiceMode);
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

// ── Segmented-control option sets (all options visible, click to select) ──

export interface ModeOption {
	value: number | null;
	label: string;
	activeClass: string;
	hint?: string;
}

export const ruleModeOptions: ModeOption[] = [
	{ value: 0, label: 'Disabled', activeClass: 'btn-neutral', hint: 'Feature is off for members' },
	{ value: 1, label: 'Approval', activeClass: 'btn-warning', hint: 'Members must request; an admin approves' },
	{ value: 2, label: 'Allowed', activeClass: 'btn-success', hint: 'Members can do this themselves' }
];

export const joinPolicyOptions: ModeOption[] = [
	{ value: 0, label: 'Admin Only', activeClass: 'btn-neutral', hint: 'Only admins can add members' },
	{ value: 1, label: 'Approval', activeClass: 'btn-warning', hint: 'Join requests need admin approval' },
	{ value: 2, label: 'Open', activeClass: 'btn-success', hint: 'Anyone can join freely' }
];

export const visibilityModeOptions: ModeOption[] = [
	{ value: 0, label: 'Private', activeClass: 'btn-neutral', hint: 'Members only see their own data' },
	{ value: 1, label: 'Admins', activeClass: 'btn-warning', hint: 'Admins can see all members' },
	{ value: 2, label: 'Everyone', activeClass: 'btn-success', hint: 'All members see each other' }
];

/** Prepends an Inherit option for group-level overrides (null = inherit from org). */
export function withInherit(options: ModeOption[]): ModeOption[] {
	return [{ value: null, label: 'Inherit', activeClass: 'btn-info', hint: 'Use the organization default' }, ...options];
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
	},
	{
		key: 'csvImportMode',
		label: 'CSV Import',
		description: 'Control whether members can import time entries from CSV files.'
	},
	{
		key: 'absenceSelfServiceMode',
		label: 'Absences & Vacation',
		description: 'Control whether members can add their own absences directly, must request approval (group/org admins), or only admins can add them.'
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

// ── Visibility mode settings ──

export interface VisibilitySetting {
	key: string;
	label: string;
	description: string;
}

export function parseVisibilityMode(mode: string | null | undefined): number {
	if (mode === 'Private') return 0;
	if (mode === 'AdminOnly') return 1;
	if (mode === 'AllMembers') return 2;
	return 0;
}

export function visibilityModeLabel(mode: string | null | undefined): string {
	if (mode === 'Private') return 'Private';
	if (mode === 'AdminOnly') return 'Admin Only';
	if (mode === 'AllMembers') return 'All Members';
	return 'Private';
}

export function visibilityModeButtonClass(mode: string | null | undefined): string {
	if (mode === 'Private') return 'btn-neutral';
	if (mode === 'AdminOnly') return 'btn-warning';
	if (mode === 'AllMembers') return 'btn-success';
	return 'btn-neutral';
}

export const visibilitySettings: VisibilitySetting[] = [
	{
		key: 'vacationVisibility',
		label: 'Vacation Day Visibility',
		description: "Control who can see members' vacation days."
	},
	{
		key: 'sickDayVisibility',
		label: 'Sick Day Visibility',
		description: "Control who can see members' sick days."
	}
];
