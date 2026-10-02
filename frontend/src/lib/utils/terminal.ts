import type { TerminalPunchResponse, TimeEntryResponse } from '$lib/api';
import { t, type MessageKey } from '$lib/i18n';

/** Small source badge of an entry ("Terminal Eingang", "Nachgetragen", …); null for plain app timers. */
export function entrySourceBadge(entry: TimeEntryResponse): { label: string; title?: string } | null {
	const start = entry.terminalStartName ?? null;
	const stop = entry.terminalStopName ?? null;
	// An app timer stopped at a terminal still names the terminal
	if (entry.source === 'Terminal' || start || stop) {
		const name = start ?? stop;
		const label = name ? t('entries.source.terminalNamed', { name }) : t('entries.source.terminal');
		const title = start && stop && start !== stop ? t('entries.source.terminalRoute', { start, stop }) : undefined;
		return { label, title };
	}
	switch (entry.source) {
		case 'Manual':
			return { label: t('entries.source.manual') };
		case 'Import':
			return { label: t('entries.source.import') };
		case 'Admin':
			return { label: t('entries.source.admin') };
		default:
			return null;
	}
}

const resultKeys: Record<string, MessageKey> = {
	Started: 'orgSettings.terminal.results.Started',
	Stopped: 'orgSettings.terminal.results.Stopped',
	ClosedFailure: 'orgSettings.terminal.results.ClosedFailure',
	Repeated: 'orgSettings.terminal.results.Repeated',
	UnknownCard: 'orgSettings.terminal.results.UnknownCard',
	Blocked: 'orgSettings.terminal.results.Blocked',
	Rejected: 'orgSettings.terminal.results.Rejected'
};

export function punchResultLabel(result: string | null | undefined): string {
	return result && resultKeys[result] ? t(resultKeys[result]) : (result ?? '');
}

/** DaisyUI badge class per punch result: accepted, harmless, refused. */
export function punchResultClass(result: string | null | undefined): string {
	switch (result) {
		case 'Started':
		case 'Stopped':
		case 'ClosedFailure':
			return 'badge-success';
		case 'Repeated':
			return 'badge-ghost';
		case 'UnknownCard':
			return 'badge-warning';
		default:
			return 'badge-error';
	}
}

/** A scan the terminal queued while offline: it arrived more than a minute after it was scanned. */
export function isQueuedPunch(punch: Pick<TerminalPunchResponse, 'scannedAt' | 'receivedAt'>): boolean {
	if (!punch.scannedAt || !punch.receivedAt) return false;
	return new Date(punch.receivedAt).getTime() - new Date(punch.scannedAt).getTime() > 60_000;
}
