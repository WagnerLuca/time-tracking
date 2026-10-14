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

/** Prepends an Inherit option for group-level overrides (null = inherit from org). */
export function withInherit(options: ModeOption[]): ModeOption[] {
	return [{ value: null, label: t('format.options.inherit'), activeClass: 'btn-info', hint: t('format.options.inheritHint') }, ...options];
}

// ── Data-driven settings definitions ──

type RuleTextKey = 'editPastEntriesMode' | 'editPauseMode' | 'initialOvertimeMode' | 'joinPolicy' | 'workScheduleChangeMode' | 'csvImportMode' | 'absenceSelfServiceMode' | 'autoPauseEnabled' | 'memberTimeEntryVisibility' | 'require2fa';
export type SettingKey = RuleTextKey;

export interface TopicSetting {
	key: SettingKey;
	label: string;
	/** One line: what the setting does. */
	description: string;
	toggle: boolean;
}

export interface SettingTopic {
	id: 'time' | 'schedule' | 'absences' | 'access';
	settings: TopicSetting[];
}

const TOGGLE_KEYS: SettingKey[] = ['autoPauseEnabled', 'memberTimeEntryVisibility', 'require2fa'];

function setting(key: SettingKey): TopicSetting {
	return { key, label: t(`format.rules.${key}.label`), description: t(`format.rules.${key}.description`), toggle: TOGGLE_KEYS.includes(key) };
}

/** The org settings grouped by what they are about (rules & visibility page). Functions, so labels follow the UI language. */
export function settingTopics(): SettingTopic[] {
	return [
		{ id: 'time', settings: [setting('editPastEntriesMode'), setting('editPauseMode'), setting('csvImportMode'), setting('autoPauseEnabled')] },
		{ id: 'schedule', settings: [setting('workScheduleChangeMode'), setting('initialOvertimeMode')] },
		{ id: 'absences', settings: [setting('absenceSelfServiceMode')] },
		{ id: 'access', settings: [setting('joinPolicy'), setting('require2fa'), setting('memberTimeEntryVisibility')] }
	];
}

const RULE_KEYS: string[] = ['editPastEntriesMode', 'editPauseMode', 'csvImportMode', 'workScheduleChangeMode', 'initialOvertimeMode', 'absenceSelfServiceMode'];

/**
 * What a setting means for the caller ("Für dich gilt: …"): rule modes from the effective
 * permissions (group overrides applied) before the org default; `fromGroup` when a group
 * of the caller changed it.
 */
export function appliedSetting(key: SettingKey, perms: RuleSource, org: Partial<OrganizationDetailResponse> | null | undefined): { label: string; badge: string; fromGroup: boolean } {
	const value = (org as Record<string, unknown> | null | undefined)?.[key];
	if (TOGGLE_KEYS.includes(key)) return { label: value ? t('format.on') : t('format.off'), badge: value ? 'badge-success' : 'badge-ghost', fromGroup: false };
	if (key === 'joinPolicy') return { label: joinPolicyLabel(value as string), badge: ruleModeButtonClass(value as string).replace('btn-', 'badge-'), fromGroup: false };
	const mode = RULE_KEYS.includes(key) ? ruleMode(key as RuleKey, perms, org as RuleSource) : (value as string);
	return { label: ruleModeLabel(mode), badge: ruleModeButtonClass(mode).replace('btn-', 'badge-'), fromGroup: !!mode && !!value && mode !== value };
}
