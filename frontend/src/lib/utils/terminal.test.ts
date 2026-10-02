import { afterEach, describe, expect, it } from 'vitest';
import { locale } from '$lib/stores/locale.svelte';
import type { TimeEntryResponse } from '$lib/api';
import { entrySourceBadge, isQueuedPunch, punchResultClass, punchResultLabel } from './terminal';

const entry = (e: Partial<TimeEntryResponse>): TimeEntryResponse => ({ id: 1, source: 'App', ...e });

describe('entrySourceBadge', () => {
	afterEach(() => locale.set('de'));

	it('shows nothing for a plain app timer', () => {
		expect(entrySourceBadge(entry({}))).toBeNull();
	});

	it('names the terminal, and both when start and stop differ', () => {
		expect(entrySourceBadge(entry({ source: 'Terminal', terminalStartName: 'Eingang' }))).toEqual({ label: 'Terminal Eingang', title: undefined });
		expect(entrySourceBadge(entry({ source: 'Terminal', terminalStartName: 'Eingang', terminalStopName: 'Hof' }))?.title).toBe('Gestartet an Eingang, gestoppt an Hof');
		expect(entrySourceBadge(entry({ source: 'Terminal' }))?.label).toBe('Terminal');
	});

	it('an app timer stopped at a terminal names that terminal', () => {
		expect(entrySourceBadge(entry({ source: 'App', terminalStopName: 'Hof' }))?.label).toBe('Terminal Hof');
	});

	it('labels manual, imported and admin entries in the current locale', () => {
		expect(entrySourceBadge(entry({ source: 'Manual' }))?.label).toBe('Nachgetragen');
		locale.set('en');
		expect(entrySourceBadge(entry({ source: 'Import' }))?.label).toBe('Imported');
		expect(entrySourceBadge(entry({ source: 'Admin' }))?.label).toBe('Entered by an admin');
	});
});

describe('punch protocol helpers', () => {
	it('translates results and falls back to the raw value', () => {
		expect(punchResultLabel('UnknownCard')).toBe('Unbekannte Karte');
		expect(punchResultLabel('Future')).toBe('Future');
		expect(punchResultClass('Stopped')).toBe('badge-success');
		expect(punchResultClass('Blocked')).toBe('badge-error');
	});

	it('flags scans that arrived more than a minute late as queued', () => {
		expect(isQueuedPunch({ scannedAt: '2026-10-02T08:00:00Z', receivedAt: '2026-10-02T08:00:30Z' })).toBe(false);
		expect(isQueuedPunch({ scannedAt: '2026-10-02T08:00:00Z', receivedAt: '2026-10-02T09:00:00Z' })).toBe(true);
	});
});
