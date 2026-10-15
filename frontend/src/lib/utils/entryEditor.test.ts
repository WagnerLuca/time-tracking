import { beforeEach, describe, expect, it, vi } from 'vitest';
import { t } from '$lib/i18n';
import { toast } from '$lib/stores/toast.svelte';
import { createEntryEditor } from './entryEditor.svelte';
import { RequestType, type OrganizationDetailResponse, type TimeEntryResponse } from '$lib/api';

/** The entry editor's two modes (own entries / admin corrections) against a mocked API client. */
const api = vi.hoisted(() => ({
	timeTrackingApi: { apiV1TimeTrackingIdPut: vi.fn(), apiV1TimeTrackingIdDelete: vi.fn(), apiV1TimeTrackingManualPost: vi.fn() },
	memberEntriesApi: {
		apiV1OrganizationsSlugMembersMemberIdEntriesEntryIdPut: vi.fn(),
		apiV1OrganizationsSlugMembersMemberIdEntriesEntryIdDelete: vi.fn(),
		apiV1OrganizationsSlugMembersMemberIdEntriesPost: vi.fn()
	},
	requestsApi: { apiV1OrganizationsSlugRequestsPost: vi.fn() }
}));
vi.mock('$lib/apiClient', () => api);
const confirm = vi.hoisted(() => ({ confirmDialog: vi.fn() }));
vi.mock('$lib/stores/confirm.svelte', () => confirm);

const ENTRY: TimeEntryResponse = { id: 3, startTime: '2026-10-14T07:00:00Z', endTime: '2026-10-14T15:00:00Z', pauseDurationMinutes: 30, description: 'Code' };
const httpError = (status: number, data: object = {}) => Object.assign(new Error(`HTTP ${status}`), { response: { status, data } });

function editor(mode: { memberId?: number; pauseMode?: string } = {}) {
	const onSaved = vi.fn(async () => {});
	const onDeleted = vi.fn(async () => {});
	const e = createEntryEditor({
		getOrgDetail: () => ({ editPauseMode: mode.pauseMode ?? 'Disabled' }) as OrganizationDetailResponse,
		getOrgSlug: () => 'acme',
		getOrgId: () => 1,
		getMemberId: () => mode.memberId ?? null,
		onSaved,
		onDeleted,
		findEntry: (id) => (id === ENTRY.id ? ENTRY : undefined)
	});
	return { e, onSaved, onDeleted };
}

const lastToast = () => toast.items.at(-1);

beforeEach(() => {
	vi.clearAllMocks();
	for (const i of toast.items) toast.dismiss(i.id);
});

describe('entry editor', () => {
	it('edits an own entry; the pause is only sent where the rule allows it', async () => {
		api.timeTrackingApi.apiV1TimeTrackingIdPut.mockResolvedValue({ data: { ...ENTRY, warnings: ['restPeriod'] } });
		const { e, onSaved } = editor();
		e.startEditEntry(ENTRY);
		expect([e.editingEntryId, e.editStartTime, e.editEndTime, e.editPause, e.editDescription]).toEqual([3, '2026-10-14T09:00', '2026-10-14T17:00', 30, 'Code']);
		expect(e.pauseEditable).toBe(false);

		e.editDescription = '  ';
		e.editEndTime = '2026-10-14T16:00';
		await e.saveEditEntry(3);
		expect(api.timeTrackingApi.apiV1TimeTrackingIdPut).toHaveBeenCalledWith(3, {
			startTime: '2026-10-14T07:00:00.000Z', endTime: '2026-10-14T14:00:00.000Z', description: undefined
		});
		expect([e.editingEntryId, e.editSaving, onSaved.mock.calls.length]).toEqual([null, false, 1]);
		expect(toast.items.map((i) => i.message)).toEqual([t('entries.saved'), t('entries.warnings.restPeriod')]);

		// Allowed pause edits: the (clamped) pause goes along
		const allowed = editor({ pauseMode: 'Allowed' }).e;
		allowed.startEditEntry(ENTRY);
		allowed.editPause = -5;
		await allowed.saveEditEntry(3);
		expect(api.timeTrackingApi.apiV1TimeTrackingIdPut.mock.calls[1][1].pauseDurationMinutes).toBe(0);
	});

	it('corrects a member\'s entry through the admin endpoint and shows save errors', async () => {
		api.memberEntriesApi.apiV1OrganizationsSlugMembersMemberIdEntriesEntryIdPut
			.mockResolvedValueOnce({ data: ENTRY })
			.mockRejectedValueOnce(httpError(400, { code: 'entry.tooLong' }));
		const { e } = editor({ memberId: 9 });
		e.startEditEntry({ ...ENTRY, endTime: null });
		expect(e.pauseEditable).toBe(true);
		await e.saveEditEntry(3);
		expect(api.memberEntriesApi.apiV1OrganizationsSlugMembersMemberIdEntriesEntryIdPut).toHaveBeenCalledWith('acme', 9, 3,
			{ startTime: '2026-10-14T07:00:00.000Z', description: 'Code', pauseDurationMinutes: 30 });

		e.startEditEntry(ENTRY);
		await e.saveEditEntry(3);
		expect(e.editingEntryId).toBe(3);   // stays open
		expect(e.editError).toBeTruthy();
		e.cancelEditEntry();
		expect([e.editingEntryId, e.editError]).toEqual([null, '']);
	});

	it('deletes only after confirmation, own or member entry', async () => {
		const { e, onDeleted } = editor();
		confirm.confirmDialog.mockResolvedValueOnce(false);
		await e.deleteEntry(3);
		expect(api.timeTrackingApi.apiV1TimeTrackingIdDelete).not.toHaveBeenCalled();

		confirm.confirmDialog.mockResolvedValue(true);
		e.startEditEntry(ENTRY);
		await e.deleteEntry(3);
		expect(api.timeTrackingApi.apiV1TimeTrackingIdDelete).toHaveBeenCalledWith(3);
		expect([e.editingEntryId, onDeleted.mock.calls.length, lastToast()?.message]).toEqual([null, 1, t('entries.deleted')]);

		const member = editor({ memberId: 9 });
		api.memberEntriesApi.apiV1OrganizationsSlugMembersMemberIdEntriesEntryIdDelete.mockRejectedValueOnce(httpError(500));
		await member.e.deleteEntry(3);
		expect(api.memberEntriesApi.apiV1OrganizationsSlugMembersMemberIdEntriesEntryIdDelete).toHaveBeenCalledWith('acme', 9, 3);
		expect([member.onDeleted.mock.calls.length, lastToast()?.kind]).toEqual([0, 'error']);
	});

	it('adds a missed entry: directly, as a request, or for a member', async () => {
		api.timeTrackingApi.apiV1TimeTrackingManualPost
			.mockResolvedValueOnce({ data: { entry: ENTRY } })
			.mockResolvedValueOnce({ data: { request: { id: 1 } } })
			.mockRejectedValueOnce(httpError(409))
			.mockRejectedValueOnce(httpError(403, { code: 'test.unknown', detail: 'Disabled here' }));
		const { e, onSaved } = editor();
		e.startEditEntry(ENTRY);
		e.startCreate('2026-10-13');
		expect([e.editingEntryId, e.creatingDay, e.createStart, e.createEnd, e.createPause]).toEqual([null, '2026-10-13', '2026-10-13T09:00', '2026-10-13T17:00', null]);

		e.createEnd = '';
		await e.saveCreate();
		expect(e.createError).toBe(t('entries.manual.endRequired'));
		e.createEnd = '2026-10-13T17:00';
		e.createDescription = 'Nachtrag';
		e.createPause = 15;   // not editable for own entries here → not sent
		await e.saveCreate();
		expect(api.timeTrackingApi.apiV1TimeTrackingManualPost).toHaveBeenLastCalledWith({
			startTime: '2026-10-13T07:00:00.000Z', endTime: '2026-10-13T15:00:00.000Z', description: 'Nachtrag', organizationId: 1
		});
		expect([e.creatingDay, onSaved.mock.calls.length, lastToast()?.message]).toEqual([null, 1, t('entries.manual.created')]);

		e.startCreate('2026-10-13');
		await e.saveCreate();
		expect(lastToast()?.message).toBe(t('entries.manual.requested'));

		e.startCreate('2026-10-13');
		await e.saveCreate();
		expect(e.createError).toBe(t('entries.manual.overlap'));
		await e.saveCreate();
		expect([e.createError, e.creatingDay, e.createSaving]).toEqual(['Disabled here', '2026-10-13', false]);
		e.cancelCreate();
		expect([e.creatingDay, e.createError]).toEqual([null, '']);

		api.memberEntriesApi.apiV1OrganizationsSlugMembersMemberIdEntriesPost.mockResolvedValue({ data: ENTRY });
		const member = editor({ memberId: 9 }).e;
		member.startCreate('2026-10-13');
		member.createPause = 20;
		await member.saveCreate();
		expect(api.memberEntriesApi.apiV1OrganizationsSlugMembersMemberIdEntriesPost).toHaveBeenCalledWith('acme', 9,
			expect.objectContaining({ pauseDurationMinutes: 20, description: null }));
	});

	it('asks the admins for an edit or a pause change of a locked entry', async () => {
		api.requestsApi.apiV1OrganizationsSlugRequestsPost.mockResolvedValueOnce({}).mockResolvedValueOnce({}).mockRejectedValueOnce(httpError(500));
		const { e } = editor();
		await e.submitRequest();   // nothing open
		expect(api.requestsApi.apiV1OrganizationsSlugRequestsPost).not.toHaveBeenCalled();

		e.startRequest(3, 'edit');
		expect([e.requestingEntryId, e.requestType, e.requestNewStart, e.requestNewEnd]).toEqual([3, 'edit', '2026-10-14T09:00', '2026-10-14T17:00']);
		e.requestNewEnd = '2026-10-14T18:00';
		e.requestMessage = 'Vergessen';
		await e.submitRequest();
		expect(api.requestsApi.apiV1OrganizationsSlugRequestsPost).toHaveBeenLastCalledWith('acme', {
			type: RequestType.NUMBER_1, relatedEntityId: 3, message: 'Vergessen',
			requestData: JSON.stringify({ startTime: '2026-10-14T07:00:00.000Z', endTime: '2026-10-14T16:00:00.000Z' })
		});
		expect([e.requestingEntryId, e.requestType, lastToast()?.message]).toEqual([null, null, t('entries.request.submitted')]);

		e.startRequest(3, 'pause');
		expect(e.requestNewPause).toBe(30);
		e.requestNewPause = -1;
		await e.submitRequest();
		expect(api.requestsApi.apiV1OrganizationsSlugRequestsPost).toHaveBeenLastCalledWith('acme',
			{ type: RequestType.NUMBER_2, relatedEntityId: 3, requestData: '0', message: undefined });

		e.startRequest(99, 'pause');   // unknown entry: nothing prefilled, still submittable
		await e.submitRequest();
		expect([e.requestingEntryId, e.requestError !== '', e.requestSending]).toEqual([99, true, false]);
		e.cancelRequest();
		expect([e.requestingEntryId, e.requestError]).toEqual([null, '']);
	});
});
