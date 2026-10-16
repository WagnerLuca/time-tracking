# Accounting role ("Buchhaltung") — design

Product request: *"an additional role 'Buchhaltung' on the complete organization, which will receive an
extra panel for reports, closing months and overview of the vacation days of everyone."*

## 1. Role model: a membership flag, not a hierarchy level

`OrganizationRole` is a strict hierarchy (Member ≤ Admin ≤ Owner) that every `[RequireOrgRole]` and
many `role >= Admin` checks in services rely on. Accounting does not fit into it:

- an accountant may be a plain Member who tracks own time, or an external bookkeeper who tracks nothing;
- an Admin (team lead) may or may not do the accounting;
- accounting needs *read* access to everybody's figures but none of the admin *write* powers
  (settings, members, corrections).

So accounting is a separate capability on the membership: **`UserOrganization.IsAccountant`** (bool,
default false). Existing role comparisons stay correct and nothing else changes for a Member who
gets the flag.

**Who may use the accounting panel** = `IsAccountant || Role == Owner`.
The Owner already controls everything (including the "admins may see members' hours" setting and the
flag itself), so the implicit access only saves a click. Admins do **not** get the panel implicitly:
an organization may keep members' hours hidden from admins (`MemberTimeEntryVisibility = false`),
and the accounting panel shows everybody's hours regardless of that setting.

**Who may grant the flag** (member management, `PUT …/members/{id}/accountant`):
- the Owner, for anyone;
- Admins, for Members only — not for themselves, other Admins or the Owner (same rule as role
  changes), so an Admin cannot lift the hours-visibility setting for themselves.

Leaving the organization clears the flag (a rejoin starts without it, like group roles).
The flag is shown with the person in the member list (`isAccountant`) — like the org role, people
should know who sees their figures — and in `GET organizations/mine` so the nav can show the panel.

### Authorization primitive

`[RequireOrgRole]` gets a named option **`OrAccountant`**: the request passes when
`role >= minimumRole` **or** (`OrAccountant` and the membership may do accounting). Checked per
request with the same single query, no cache, 2FA requirement unchanged.

- Accounting endpoints: `[RequireAccountant]` (= Owner, or accountant), code `org.accountantRequired`.
- Close / reopen months: `[RequireOrgRole(OrganizationRole.Admin, OrAccountant = true)]` — admins keep
  the existing right, accountants (even plain Members) get it too.

An accountant who is only a Member stays a Member everywhere else (403 on admin endpoints, the
closed-month write guard treats them as a Member).

### RLS

`TimeEntries` / `AbsenceDays` rows are visible to the owner and to **active members of the row's
organization**. An accountant is an active member, so RLS already returns all rows of their
organization; the per-person restrictions (`MemberVisibility`) are application rules, which the
accounting service deliberately does not apply. Verified by a Postgres test (non-superuser role).

## 2. Reports

`GET organizations/{slug}/accounting/summary?year&month` → one row per active member:
personnel number, name, email, target, worked (net), overtime of the month, overtime balance at the
month's end, vacation / sick / other absence days, failure entries ("Needs Correction" count).
It reuses the payroll export's row builder (`PayrollExportService.RowsAsync`, i.e.
`DailyReportAggregator` — same numbers as balances, reports and the payroll CSV).

- `…/summary/csv` — the payroll CSV format (German `;`, decimal comma, BOM; `lang` for headers);
- `…/summary/pdf` — one landscape A4 table (QuestPDF, same styling as the member reports);
- `…/members/{memberId}/report?year&month` — the existing day-by-day monthly PDF of one member,
  without the visibility check (accountants need the details behind a figure).

Not gated by `MemberTimeEntryVisibility` (that setting is about *admins*); the payroll export for
admins keeps its existing gate.

## 3. Month closing

Already implemented for admins (issue #57): `ClosedPeriod` (org, month, closedAt, closedBy) plus the
`ClosedPeriodEvent` audit list (Closed / Reopened / AdminChange with who, when, why). The one write
guard `ClosedPeriods.GuardAsync` runs inside `SaveChangesAsync` for every save made for a person, so
**every** path is covered without per-path code: timer, manual entries, edits/deletes, member
corrections, CSV imports, request creation and approval (approval fails with 409 `period.closed` and
the request stays pending), absences, work schedules and terminal punches (queued scans). Non-admins
are refused (`period.closed`); org admins may still correct with a reason (`X-Change-Reason`, logged
as AdminChange and flagged in the entry history). System work (business-day-end auto-stop, core-hours
check) is not affected. We keep that model instead of a second `MonthClosing` entity — reopening is
recorded by the Reopened event (who / when / reason), so the requested `reopenedBy/At` audit exists.

Changes for accounting:
- accountants may close and reopen (see primitive above) and see the audit list;
- an accountant who is not an admin cannot change data inside a closed month (guard unchanged);
- `GET accounting/months?year` lists the 12 months with closed state (closedAt / closedBy), whether
  the month has ended, and **warnings**: failure entries started in the month and pending requests
  touching it (same range logic the guard uses for requests). The UI asks for confirmation when a
  month to close has warnings; the server does not refuse (closing with open issues is a business
  decision).

The lock state in the editing UIs (calendar lock, entry editor, absences, inbox hints) already exists.

## 4. Vacation overview

`GET accounting/vacation?year` → per active member: entitlement (`VacationDaysPerYear`), taken
(up to today, org-local), planned (after today), remaining (`max(0, entitlement − taken − planned)`),
sick days and other absence days of the year. Taken/planned use the existing rule (vacation type or
custom type counting as vacation, only the member's working days, holidays excluded), extracted from
`OrganizationService` into `VacationBalances` so the member list and the overview stay identical.

## 5. Frontend

Route `organizations/[slug]/accounting` with tabs *Berichte* / *Monatsabschluss* / *Urlaubsübersicht*;
the org tab bar shows "Buchhaltung" only when `canUseAccounting(membership)` (accountant or Owner).
Member management gets a "Buchhaltung" toggle (where the caller may change it). i18n de/en, pure
helpers in `utils/accounting.ts` with Vitest.

## 6. Seed

New demo user `lisa.buchhaltung@example.com` / `Password123`: Member + accountant in `mueller-software`.

## Open questions for the product owner

1. Should Admins see the panel implicitly (today: only with the flag, which only the Owner can give them)?
2. Should admins keep the right to change closed months with a reason, or should only reopening be
   possible once accounting has closed a month?
3. Members who left during a month are not in the monthly summary (same as the payroll CSV). Needed?
4. Vacation entitlement has no carry-over / pro-rata for joiners — should accounting maintain that?
5. Should closing be refused (not only warned) while failure entries or pending requests exist?
