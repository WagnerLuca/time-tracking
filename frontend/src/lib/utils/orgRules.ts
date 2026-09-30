import type { EffectivePermissionsResponse, OrganizationDetailResponse } from '$lib/api';
import { t } from '$lib/i18n';

// ── Roles ──

/** Owners and admins manage the organization (mirrors the backend's OrganizationRole ordering). */
export function isOrgAdmin(role: string | null | undefined): boolean {
	return role === 'Owner' || role === 'Admin';
}

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

// ── Convenience helpers for common rules ──

/**
 * Anything that carries the rule modes: the caller's *effective* permissions
 * (EffectivePermissionsResponse, group overrides already applied — prefer it)
 * or the organization defaults (OrganizationDetailResponse).
 */
type RuleKey = 'editPastEntriesMode' | 'editPauseMode' | 'csvImportMode' | 'workScheduleChangeMode' | 'initialOvertimeMode' | 'absenceSelfServiceMode';
export type RuleSource = Partial<Pick<EffectivePermissionsResponse, RuleKey>> | Partial<Pick<OrganizationDetailResponse, RuleKey>> | null | undefined;

/**
 * The effective mode for a rule: the first source that defines it wins, so call
 * sites pass `(effectivePerms, orgDetail)` and group overrides are respected
 * while the org default still applies before the permissions have loaded.
 */
export function ruleMode(key: RuleKey, ...sources: RuleSource[]): string | null {
	for (const s of sources) {
		const mode = s?.[key];
		if (mode) return mode;
	}
	return null;
}

export function canEditEntries(...sources: RuleSource[]): boolean {
	return isAllowed(ruleMode('editPastEntriesMode', ...sources));
}

export function canRequestEditEntries(...sources: RuleSource[]): boolean {
	return isRequiresApproval(ruleMode('editPastEntriesMode', ...sources));
}

export function canEditPause(...sources: RuleSource[]): boolean {
	return isAllowed(ruleMode('editPauseMode', ...sources));
}

export function canRequestEditPause(...sources: RuleSource[]): boolean {
	return isRequiresApproval(ruleMode('editPauseMode', ...sources));
}

export function canImportCsv(...sources: RuleSource[]): boolean {
	return isAllowed(ruleMode('csvImportMode', ...sources));
}

export function csvImportEnabled(...sources: RuleSource[]): boolean {
	return !isDisabled(ruleMode('csvImportMode', ...sources));
}

// ── Settings page helpers ──

export function parseRuleMode(mode: string | null | undefined): number {
	if (mode === 'Disabled') return 0;
	if (mode === 'RequiresApproval') return 1;
	if (mode === 'Allowed') return 2;
	return 2;
}

export function ruleModeLabel(mode: string | null | undefined): string {
	if (mode === 'Disabled') return t('format.ruleMode.Disabled');
	if (mode === 'RequiresApproval') return t('format.ruleMode.RequiresApproval');
	return t('format.ruleMode.Allowed');
}

export function joinPolicyLabel(mode: string | null | undefined): string {
	if (mode === 'Disabled') return t('format.joinPolicy.Disabled');
	if (mode === 'RequiresApproval') return t('format.joinPolicy.RequiresApproval');
	return t('format.joinPolicy.Allowed');
}

export function ruleModeButtonClass(mode: string | null | undefined): string {
	if (mode === 'Disabled') return 'btn-neutral';
	if (mode === 'RequiresApproval') return 'btn-warning';
	return 'btn-success';
}

// ── Segmented-control option sets (all options visible, click to select) ──
// Functions, not constants, so the labels follow the current UI language.

export interface ModeOption {
	value: number | null;
	label: string;
	activeClass: string;
	hint?: string;
}

export function ruleModeOptions(): ModeOption[] {
	return [
		{ value: 0, label: t('format.options.disabled'), activeClass: 'btn-neutral', hint: t('format.options.disabledHint') },
		{ value: 1, label: t('format.options.approval'), activeClass: 'btn-warning', hint: t('format.options.approvalHint') },
		{ value: 2, label: t('format.options.allowed'), activeClass: 'btn-success', hint: t('format.options.allowedHint') }
	];
}

export function joinPolicyOptions(): ModeOption[] {
	return [
		{ value: 0, label: t('format.options.adminOnly'), activeClass: 'btn-neutral', hint: t('format.options.adminOnlyHint') },
		{ value: 1, label: t('format.options.approval'), activeClass: 'btn-warning', hint: t('format.options.joinApprovalHint') },
		{ value: 2, label: t('format.options.open'), activeClass: 'btn-success', hint: t('format.options.openHint') }
	];
}

export function visibilityModeOptions(): ModeOption[] {
	return [
		{ value: 0, label: t('format.options.private'), activeClass: 'btn-neutral', hint: t('format.options.privateHint') },
		{ value: 1, label: t('format.options.admins'), activeClass: 'btn-warning', hint: t('format.options.adminsHint') },
		{ value: 2, label: t('format.options.everyone'), activeClass: 'btn-success', hint: t('format.options.everyoneHint') }
	];
}

/** Prepends an Inherit option for group-level overrides (null = inherit from org). */
export function withInherit(options: ModeOption[]): ModeOption[] {
	return [{ value: null, label: t('format.options.inherit'), activeClass: 'btn-info', hint: t('format.options.inheritHint') }, ...options];
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

type RuleTextKey = 'editPastEntriesMode' | 'editPauseMode' | 'initialOvertimeMode' | 'joinPolicy' | 'workScheduleChangeMode' | 'csvImportMode' | 'absenceSelfServiceMode' | 'autoPauseEnabled' | 'memberTimeEntryVisibility' | 'require2fa' | 'vacationVisibility' | 'sickDayVisibility';

function setting(key: RuleTextKey) {
	return { key, label: t(`format.rules.${key}.label`), description: t(`format.rules.${key}.description`) };
}

export function ruleSettings(): RuleSetting[] {
	return [
		setting('editPastEntriesMode'),
		setting('editPauseMode'),
		setting('initialOvertimeMode'),
		{ ...setting('joinPolicy'), labelFn: joinPolicyLabel },
		setting('workScheduleChangeMode'),
		setting('csvImportMode'),
		setting('absenceSelfServiceMode')
	];
}

export function toggleSettings(): ToggleSetting[] {
	return [setting('autoPauseEnabled'), setting('memberTimeEntryVisibility'), setting('require2fa')];
}

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
	if (mode === 'AdminOnly') return t('format.visibility.AdminOnly');
	if (mode === 'AllMembers') return t('format.visibility.AllMembers');
	return t('format.visibility.Private');
}

export function visibilityModeButtonClass(mode: string | null | undefined): string {
	if (mode === 'Private') return 'btn-neutral';
	if (mode === 'AdminOnly') return 'btn-warning';
	if (mode === 'AllMembers') return 'btn-success';
	return 'btn-neutral';
}

export function visibilitySettings(): VisibilitySetting[] {
	return [setting('vacationVisibility'), setting('sickDayVisibility')];
}
