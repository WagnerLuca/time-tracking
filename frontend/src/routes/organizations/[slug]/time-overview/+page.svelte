<script lang="ts">
	import { onMount } from 'svelte';
	import { page } from '$app/stores';
	import { apiService } from '$lib/apiService';
	import type { MemberTimeOverviewResponse, OrganizationDetailResponse } from '$lib/types';

	let orgSlug: string;
	let orgName = $state('');
	let members = $state<MemberTimeOverviewResponse[]>([]);
	let loading = $state(true);
	let error = $state('');

	// Week navigation
	let weekOffset = $state(0);
	const weekRange = $derived(getWeekRange(weekOffset));

	// Expanded member detail
	let expandedUserId = $state<number | null>(null);
	let memberEntries = $state<any[]>([]);
	let entriesLoading = $state(false);

	onMount(() => {
		orgSlug = $page.params.slug ?? '';
		loadOrg();
		loadOverview();
	});

	function getWeekRange(offset: number) {
		const now = new Date();
		const start = new Date(now);
		const dayOfWeek = now.getDay() || 7;
		start.setDate(now.getDate() - dayOfWeek + 1 + offset * 7);
		start.setHours(0, 0, 0, 0);
		const end = new Date(start);
		end.setDate(start.getDate() + 6);
		end.setHours(23, 59, 59, 999);
		return { start, end };
	}

	async function loadOrg() {
		try {
			const org = await apiService.get<OrganizationDetailResponse>(`/api/Organizations/${orgSlug}`);
			orgName = org.name;
		} catch {}
	}

	async function loadOverview() {
		loading = true;
		error = '';
		try {
			const from = weekRange.start.toISOString();
			const to = weekRange.end.toISOString();
			members = await apiService.get<MemberTimeOverviewResponse[]>(
				`/api/Organizations/${orgSlug}/time-overview?from=${from}&to=${to}`
			);
		} catch (err: any) {
			if (err.response?.status === 403) {
				error = 'You need Admin or Owner role to view this page.';
			} else {
				error = err.response?.data?.message || 'Failed to load time overview.';
			}
		} finally {
			loading = false;
		}
	}

	function changeWeek(dir: number) {
		weekOffset += dir;
		expandedUserId = null;
		memberEntries = [];
		loadOverview();
	}

	async function toggleMemberDetail(userId: number) {
		if (expandedUserId === userId) {
			expandedUserId = null;
			memberEntries = [];
			return;
		}
		expandedUserId = userId;
		entriesLoading = true;
		try {
			const from = weekRange.start.toISOString();
			const to = weekRange.end.toISOString();
			memberEntries = await apiService.get<any[]>(
				`/api/Organizations/${orgSlug}/member-entries/${userId}?from=${from}&to=${to}`
			);
		} catch {
			memberEntries = [];
		} finally {
			entriesLoading = false;
		}
	}

	function formatHours(minutes: number): string {
		if (minutes === 0) return '-';
		return (minutes / 60).toFixed(1) + 'h';
	}

	function formatTime(iso: string): string {
		return new Date(iso).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
	}

	function formatDateShort(iso: string): string {
		return new Date(iso).toLocaleDateString([], { month: 'short', day: 'numeric' });
	}

	function formatWeekLabel(range: { start: Date; end: Date }): string {
		const opts: Intl.DateTimeFormatOptions = { month: 'short', day: 'numeric' };
		return `${range.start.toLocaleDateString([], opts)} – ${range.end.toLocaleDateString([], opts)}`;
	}

	function formatDuration(minutes?: number): string {
		if (minutes == null || minutes === 0) return '-';
		const h = Math.floor(minutes / 60);
		const m = Math.round(minutes % 60);
		if (h > 0) return `${h}h ${m}m`;
		return `${m}m`;
	}
</script>

<svelte:head>
	<title>Time Overview - {orgName || 'Organization'}</title>
</svelte:head>

<div class="page">
	<a href="/organizations/{orgSlug}" class="back-link">&larr; Back to {orgName || 'Organization'}</a>

	<h1>Team Time Overview</h1>
	{#if orgName}<p class="subtitle">{orgName}</p>{/if}

	{#if error}
		<div class="error-msg">{error}</div>
	{:else}
		<!-- Week nav -->
		<div class="week-header">
			<button class="week-nav" onclick={() => changeWeek(-1)}>&lsaquo;</button>
			<div class="week-title">
				<span class="week-label">{formatWeekLabel(weekRange)}</span>
			</div>
			<button class="week-nav" onclick={() => changeWeek(1)} disabled={weekOffset >= 0}>&rsaquo;</button>
		</div>

		{#if loading}
			<p class="muted">Loading...</p>
		{:else if members.length === 0}
			<p class="muted">No members found.</p>
		{:else}
			<div class="overview-table">
				<div class="table-header">
					<span class="col-name">Member</span>
					<span class="col-role">Role</span>
					<span class="col-target">Target</span>
					<span class="col-tracked">Tracked</span>
					<span class="col-net">Net</span>
					<span class="col-entries">Entries</span>
					<span class="col-status">Status</span>
				</div>

				{#each members as member}
					{@const targetMinutes = member.weeklyWorkHours ? member.weeklyWorkHours * 60 : 0}
					{@const pct = targetMinutes > 0 ? Math.round((member.netTrackedMinutes / targetMinutes) * 100) : 0}
					<button class="table-row" onclick={() => toggleMemberDetail(member.userId)}>
						<span class="col-name">
							<span class="member-name">{member.firstName} {member.lastName}</span>
							<span class="member-email">{member.email}</span>
						</span>
						<span class="col-role">
							<span class="role-badge role-{member.role.toLowerCase()}">{member.role}</span>
						</span>
						<span class="col-target">
							{member.weeklyWorkHours ? `${member.weeklyWorkHours}h` : '-'}
						</span>
						<span class="col-tracked">{formatHours(member.totalTrackedMinutes)}</span>
						<span class="col-net">{formatHours(member.netTrackedMinutes)}</span>
						<span class="col-entries">{member.entryCount}</span>
						<span class="col-status">
							{#if targetMinutes > 0}
								{#if pct >= 100}
									<span class="status-badge status-complete">&#10003; {pct}%</span>
								{:else if pct >= 75}
									<span class="status-badge status-good">{pct}%</span>
								{:else if pct >= 50}
									<span class="status-badge status-partial">{pct}%</span>
								{:else}
									<span class="status-badge status-low">{pct}%</span>
								{/if}
							{:else}
								<span class="muted">-</span>
							{/if}
						</span>
					</button>

					{#if expandedUserId === member.userId}
						<div class="member-detail">
							{#if entriesLoading}
								<p class="muted">Loading entries...</p>
							{:else if memberEntries.length === 0}
								<p class="muted">No entries this week.</p>
							{:else}
								<div class="detail-entries">
									{#each memberEntries as entry}
										<div class="detail-entry">
											<span class="de-date">{formatDateShort(entry.startTime)}</span>
											<span class="de-time">
												{formatTime(entry.startTime)}{entry.endTime ? ` – ${formatTime(entry.endTime)}` : ''}
											</span>
											<span class="de-desc">{entry.description || ''}</span>
											<span class="de-dur">
												{entry.isRunning ? 'Running' : formatDuration(entry.netDurationMinutes ?? entry.durationMinutes)}
											</span>
											{#if entry.pauseDurationMinutes > 0}
												<span class="de-pause">-{entry.pauseDurationMinutes}m pause</span>
											{/if}
										</div>
									{/each}
								</div>
							{/if}
						</div>
					{/if}
				{/each}
			</div>
		{/if}
	{/if}
</div>

<style>
	.back-link {
		color: #6b7280;
		text-decoration: none;
		font-size: 0.875rem;
		display: inline-block;
		margin-bottom: 0.75rem;
	}
	.back-link:hover { color: #3b82f6; }

	h1 {
		margin: 0 0 0.25rem;
		font-size: 1.5rem;
		color: #1a1a2e;
	}

	.subtitle {
		color: #6b7280;
		font-size: 0.875rem;
		margin: 0 0 1.5rem;
	}

	.muted { color: #9ca3af; }

	.error-msg {
		color: #dc2626;
		background: #fef2f2;
		padding: 0.75rem 1rem;
		border-radius: 8px;
		border-left: 3px solid #dc2626;
	}

	/* Week nav */
	.week-header {
		display: flex;
		align-items: center;
		justify-content: center;
		gap: 1rem;
		margin-bottom: 1.5rem;
	}

	.week-nav {
		background: none;
		border: 1px solid #e5e7eb;
		width: 32px;
		height: 32px;
		border-radius: 8px;
		font-size: 1.25rem;
		cursor: pointer;
		color: #374151;
		display: flex;
		align-items: center;
		justify-content: center;
	}
	.week-nav:hover:not(:disabled) { background: #f3f4f6; }
	.week-nav:disabled { opacity: 0.3; cursor: not-allowed; }

	.week-label {
		font-weight: 600;
		font-size: 1rem;
		color: #1a1a2e;
	}

	/* Overview table */
	.overview-table {
		background: white;
		border: 1px solid #e5e7eb;
		border-radius: 12px;
		overflow: hidden;
	}

	.table-header {
		display: grid;
		grid-template-columns: 2fr 0.7fr 0.7fr 0.8fr 0.8fr 0.6fr 0.8fr;
		gap: 0.5rem;
		padding: 0.75rem 1rem;
		background: #f9fafb;
		font-size: 0.6875rem;
		font-weight: 700;
		color: #6b7280;
		text-transform: uppercase;
		letter-spacing: 0.05em;
		border-bottom: 1px solid #e5e7eb;
	}

	.table-row {
		display: grid;
		grid-template-columns: 2fr 0.7fr 0.7fr 0.8fr 0.8fr 0.6fr 0.8fr;
		gap: 0.5rem;
		padding: 0.75rem 1rem;
		border: none;
		border-bottom: 1px solid #f3f4f6;
		background: white;
		width: 100%;
		text-align: left;
		cursor: pointer;
		transition: background 0.1s;
		font-size: 0.875rem;
		align-items: center;
	}

	.table-row:hover { background: #f9fafb; }
	.table-row:last-child { border-bottom: none; }

	.col-name { overflow: hidden; }
	.col-role, .col-target, .col-tracked, .col-net, .col-entries, .col-status {
		text-align: center;
	}

	.member-name {
		display: block;
		font-weight: 600;
		color: #1a1a2e;
		font-size: 0.875rem;
	}

	.member-email {
		display: block;
		font-size: 0.6875rem;
		color: #9ca3af;
		overflow: hidden;
		text-overflow: ellipsis;
		white-space: nowrap;
	}

	.role-badge {
		font-size: 0.6875rem;
		font-weight: 600;
		padding: 0.125rem 0.5rem;
		border-radius: 999px;
		text-transform: uppercase;
	}
	.role-owner { background: #fef3c7; color: #92400e; }
	.role-admin { background: #dbeafe; color: #1e40af; }
	.role-member { background: #f3f4f6; color: #4b5563; }

	.status-badge {
		font-size: 0.75rem;
		font-weight: 600;
		padding: 0.125rem 0.5rem;
		border-radius: 999px;
	}
	.status-complete { background: #dcfce7; color: #16a34a; }
	.status-good { background: #dbeafe; color: #2563eb; }
	.status-partial { background: #fef3c7; color: #d97706; }
	.status-low { background: #fef2f2; color: #dc2626; }

	/* Member detail */
	.member-detail {
		padding: 0.75rem 1.25rem 1rem;
		background: #f9fafb;
		border-bottom: 1px solid #e5e7eb;
	}

	.detail-entries {
		display: flex;
		flex-direction: column;
		gap: 0.375rem;
	}

	.detail-entry {
		display: flex;
		align-items: center;
		gap: 0.75rem;
		font-size: 0.8125rem;
		padding: 0.375rem 0.5rem;
		background: white;
		border-radius: 6px;
		border: 1px solid #e5e7eb;
	}

	.de-date {
		color: #6b7280;
		min-width: 56px;
		font-size: 0.75rem;
	}

	.de-time {
		color: #374151;
		min-width: 100px;
	}

	.de-desc {
		color: #9ca3af;
		flex: 1;
		overflow: hidden;
		text-overflow: ellipsis;
		white-space: nowrap;
	}

	.de-dur {
		font-weight: 600;
		color: #374151;
		min-width: 56px;
		text-align: right;
	}

	.de-pause {
		font-size: 0.6875rem;
		color: #c2410c;
		background: #fff7ed;
		padding: 0 0.375rem;
		border-radius: 999px;
	}
</style>
