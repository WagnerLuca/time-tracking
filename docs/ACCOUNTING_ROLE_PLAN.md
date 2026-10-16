# Accounting role ("Buchhaltung") — design

Product request: *"an additional role 'Buchhaltung' on the complete organization, which will receive an
extra panel for reports, closing months and overview of the vacation days of everyone."*

## 1. Role model: a membership flag, not a hierarchy level

`OrganizationRole` is a strict hierarchy (Member ≤ Admin ≤ Owner) that every `[RequireOrgRole]` and
many `role >= Admin` checks in services rely on. Accounting does not fit into it:

- an accountant may be a plain Member who tracks own time, or an external bookkeeper who tracks nothing;
- an Admin (team lead) may or may not do the accounting;
- accounting needs *read* access to everybody's figures but none of the admin *write* powers.

So accounting is a separate capability on the membership: **`UserOrganization.IsAccountant`** (bool,
default false, migration `AccountantRole`). Existing role comparisons stay correct; a Member with the flag
is a Member everywhere else: no rules, roles, groups, terminals/punch log, approvals, presence, absence
notes or entry edits (tested).

**Who may grant the flag** (member page, `PUT …/members/{id}/accountant`):
- the Owner, for anyone (themselves included);
- Admins, for Members only — not for themselves, other Admins or the Owner (same rule as role changes,
  code `accounting.ownerOnly`), so an Admin cannot lift the hours-visibility setting for themselves.

Leaving the organization clears the flag (a rejoin starts without it). The flag is shown with the person
in the member list (`isAccountant`, a "Buchhaltung" badge) — like the role, people should know who sees
their figures — and in `GET organizations/mine` for the navigation.

### Authorization primitive

`[RequireOrgRole]` gets a named option **`OrAccountant`**: the request passes when `role >= minimumRole`
**or** (`OrAccountant` and the membership has `IsAccountant`). Same single per-request query, no cache,
2FA requirement unchanged; a refusal then carries `org.accountantRequired`.

- Accounting endpoints and close/reopen: `[RequireOrgRole(OrganizationRole.Admin, OrAccountant = true)]`.

### Who sees what ("Auswertungen")

One top-level navigation entry **"Auswertungen"** for Owner, Admins and accountants
(`canUseAccounting` in `orgRules.ts`), with tabs Berichte (incl. Lohnexport) / Monatsabschluss /
Urlaubsübersicht.

| | Accountant (any role) | Owner / Admin without the flag |
|---|---|---|
| Monthly summary, CSV/PDF, member day report, payroll CSV | always | only while `MemberTimeEntryVisibility` ("Admins sehen Arbeitszeiten") is on — unchanged existing rule |
| Month list with warnings, close / reopen, audit log | yes | yes (unchanged) |
| Vacation overview | yes | yes (admins already see everyone's vacation balance) |

**Decision on `MemberTimeEntryVisibility`:** the switch governs *admins* (and owners). The accounting
flag deliberately ignores it — accounting needs full data, and granting the flag is the explicit,
logged decision of the Owner (or of an Admin for a plain member). An Owner who wants the panel's
hours while the switch is off grants themselves the flag.

Accountants do **not** get extra rights in the member visibility matrix; the panel's endpoints are the
only place they see others' figures, and the member day report leaves absence notes out.

### RLS

`TimeEntries` / `AbsenceDays` rows are visible to the owner and to **active members of the row's
organization**. An accountant is an active member, so RLS returns all rows of their organization —
including rows of members who have left. Verified by `AccountingPostgresTests` (non-superuser role,
real migrations).

## 2. Reports

`GET organizations/{slug}/accounting/summary?year&month` → one row per member: personnel number, name,
email, target, worked (net), overtime of the month, overtime balance at the month's end, vacation / sick /
other absence days, failure entries ("Needs Correction" count; they count 0 hours), `isFormerMember`.
It reuses the payroll export's row builder (`PayrollExportService.RowsAsync` → `DailyReportAggregator`,
the same numbers as balances, reports and the payroll CSV).

- **Former members:** rows include members who left but tracked time or were absent in the period
  (memberships read with `IgnoreQueryFilters`). This applies to the payroll CSV as well.
- `…/summary/csv` — the payroll CSV format (German `;`, decimal comma, BOM; `lang` for headers);
- `…/summary/pdf` — one landscape A4 table with totals (QuestPDF, same styling as the member reports);
- `…/members/{memberId}/report?year&month` — the existing day-by-day PDF without the visibility check,
  also for former members, absence notes left out.
- The existing payroll CSV (`POST ExportImport/payroll`, export page) is open to accountants too.

## 3. Month closing — the existing feature, extended

Month closing already existed (issue #57): `ClosedPeriod` (org, month, closedAt, closedBy) plus the
`ClosedPeriodEvent` audit list (Closed / Reopened / AdminChange with who, when, why). No second entity.
The single write guard `ClosedPeriods.GuardAsync` runs inside `TimeTrackingDbContext.SaveChangesAsync`
for every save made for a person, so every path is covered without per-path code: timer, manual entries,
edits/deletes, member corrections, CSV imports, request creation and approval (409 `period.closed`, the
request stays pending), absences, work schedules and terminal punches (`ActAsUser`). Audit for this
change: no `ExecuteUpdate/ExecuteDelete`/raw SQL touches entries, absences or schedules, and account
deletion anonymizes without deleting entries — so no path bypasses the guard. Only system work (the
business-day-end auto-stop, core-hours checks) is exempt, by design.

Non-admins are refused (`period.closed`); org admins may still correct with a reason
(`X-Change-Reason`, logged as AdminChange, flagged in the entry history). An accountant who is not an
admin is treated like any member by the guard (cannot change a closed month, even with a reason).

Changes:
- accountants may close and reopen (`OrAccountant`) and see the audit list;
- `GET accounting/months?year` lists the 12 months with closed state (closedAt / closedBy), whether the
  month has ended, and **warnings**: failure entries started in the month and pending requests touching it
  (`ClosedPeriods.PendingRequestsPerMonthAsync`, the same ranges the guard uses for requests);
- the UI warns (confirm dialog "Trotzdem abschließen") before closing a month with warnings — in the
  month list and in the range form; the server does not refuse (closing with open issues stays a
  business decision);
- the closing UI (`ClosedPeriodsPanel`) moved from "Allgemein" to Auswertungen → Monatsabschluss;
  "Allgemein" links there. Lock badges in calendars/editors already existed.

## 4. Vacation overview

`GET accounting/vacation?year` → per active member: entitlement (`VacationDaysPerYear`), taken (up to
today, org-local), planned (after today), remaining (`max(0, entitlement − taken − planned)`), sick days
and other absence days of the year. Taken/planned use the existing rule (vacation type or custom type
counting as vacation, only the member's working days, holidays excluded), extracted from
`OrganizationService` into `VacationBalances` so the member list and the overview stay identical.

## 5. Seed

`lisa.buchhaltung@example.com` / `Password123`: Member + accountant in `mueller-software`.

## Open questions for the product owner

1. Should admins keep the right to change closed months with a reason, or should only reopening be
   possible once accounting has closed a month?
2. Should closing be refused (not only warned) while failure entries or pending requests exist?
3. Vacation entitlement has no carry-over / pro-rata for joiners and leavers — should accounting maintain that?
4. The vacation overview lists active members only; former members appear in the monthly summary. Enough?
5. Should the accountant flag also widen the member visibility matrix (e.g. see time overview pages), or
   stay limited to the Auswertungen panel as now?
