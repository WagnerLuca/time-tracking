# Days off – holidays, company days off, absences and the vacation account

Design note, 2026-10-15. Covers how a member's day is valued (target, holidays,
absences), where the dates come from (public holidays per Bundesland, company holidays,
Betriebsurlaub), the vacation account over the years (entitlement, pro rata, contract
changes, carry-over, expiry) and how all of it stays correct and fast after years of data.
Section 11 is the order to build it in; step 1 is done (2026-10-27).

The legal notes describe German law as generally understood (BUrlG, case law); they are
not legal advice — every rule that depends on them can be overridden by an admin.

## 1. Decisions taken

| Question | Decision |
|---|---|
| Public holidays | follow the **Arbeitsort's Bundesland**, per member, dated |
| Company holidays (e.g. 24.12./31.12. half days) | org-wide or per group |
| Betriebsurlaub | org-wide or per group, consumes vacation days |
| Vacation running short during Betriebsurlaub | the vacation balance goes **negative** |
| Leaving single members out | yes, an admin can exclude a member from a company holiday or a Betriebsurlaub |
| Changing a past Betriebsurlaub | allowed through the closed-period reason prompt |
| Carry-over default | **unlimited** (an expiry date is an optional org setting) |
| Carry-over rule scope | per organization only |
| Statutory vs. contractual days | not distinguished |
| Pro rata (joining/leaving during the year) | in scope, **as the law says** (§ 5 BUrlG), overridable per member and year |
| Contract changes during the year | in scope: hours, working days and vacation days are dated, the entitlement is split by period |
| Fixed-date snapshots | yes, as a **cache** that is rebuilt on change, never as a source of truth |

## 2. Today

- `Holiday` rows per organization (`Date`, `Name`, `IsRecurring`, `IsHalfDay`, unique per
  org/date). No region anywhere — not on the org, a group or a member.
- `POST holidays/import-preset` copies `HolidayPresets` into rows: fixed dates as one
  recurring row, Easter-based dates and Buß- und Bettag as one row per imported year (≤ 10 years).
- Holidays are read by org id in six places: `WorkingDays.HolidayDatesAsync`,
  `CoreHours.cs` (due checks), `DailyReportAggregator` (single member + team balances),
  `OrganizationService` (vacation count) and the frontend's `utils/daysOff.ts`.
- Absences: `AbsenceDay` (`Vacation`/`SickDay`/`Other` + org-defined `AbsenceTypeDefinition`
  with `CreditMode` `None`/`FillToTarget`/`FullCredit` and `CountsAsVacation`); **one
  absence per member and day** (unique index user/org/date).
- Vacation: one number `UserOrganization.VacationDaysPerYear` (new members get
  `Organization.DefaultVacationDays`); "remaining" = that number − this year's vacation
  days, computed live. No history, no carry-over, no pro rata.
- Overtime balance: computed from the member's **first entry ever** on every request
  (`DailyReportAggregator.BuildWithRunningTotalAsync` / `BalancesAsync`); the team overview
  does it for the whole team every 60 s.

### Problems found

1. Importing two presets merges them — every member gets both states' holidays.
2. Movable holidays (Ostern, Pfingsten, Fronleichnam, …) silently end after the last
   imported year, while the fixed ones keep recurring.
3. `Holiday.DateIn` ignores the start year of a recurring row: Frauentag (BE from 2019,
   MV from 2023) and Reformationstag (HB/HH/NI/SH from 2018) also count in earlier years.
4. Missing one-offs: Berlin 08.05.2020 and 08.05.2025, Reformationstag 2017 nationwide.
   Mariä Himmelfahrt in Bavaria (most municipalities) is left out on purpose.
5. Vacation on a **half** holiday costs 0 days (`WorkingDays.FilterAsync` and the vacation
   count drop every holiday date) — it should cost 0.5.
6. A half-day vacation and a half sick day cannot be on the same day (unique index).
7. Balance work grows linearly with the years of data.
8. The vacation account forgets every year on 1 January (leftover and negative days).

## 3. Core principle

> **Calculations for a past day read only data that is dated for that member** (contract
> periods, generated day-off rows, absences, entries) **plus org-wide data. They never read
> today's group membership.**

Group membership has no history. If a calculation resolved "the member's group gives
31.12. off" or "the member's group is in Bavaria" live, moving someone from Team A to
Team B would rewrite their past. So group settings **write dated member data** when they
are set or when someone joins/leaves, and only for days from then on.

## 4. The day model

For one member and one org-local day:

1. **Target** = the contract period valid that day (`WorkSchedule.TargetFor(weekday)`).
   Target 0 → not a working day; nothing below applies (and no vacation is used).
2. **Public holiday** — from the region of that contract period (section 5). Full or half.
3. **Company holiday** — generated rows for the member (section 6). Full or half.
4. **Absence** — the member's absence rows (section 8), incl. generated Betriebsurlaub.
5. **Worked** = finished entries − pauses; failure entries count 0.

`holidayCredit` = the larger of public and company holiday (full beats half, never
both), `absenceCredit` per `CreditMode`, `effectiveTarget = max(0, target − holidayCredit −
absenceCredit)`, `balance = worked − effectiveTarget`. Hours worked beyond the effective
target are overtime — on a holiday, on Home-Office, beyond a Dienstreise's target.

### Combinations

| Case | Result |
|---|---|
| Public and company holiday on one day | credited once, full wins over half |
| Vacation on a half holiday (24.12.) | 0.5 vacation days |
| Vacation on a full holiday or a target-0 day | 0 vacation days |
| Sick during Betriebsurlaub | the sick day replaces the vacation day, which returns to the budget (§ 9 BUrlG) |
| Home-Office / Dienstreise on a holiday | holiday credit; all worked hours count as overtime |
| Half vacation + half sick on one day | both count (needs section 8.2) |

## 5. Public holidays by region

- **Calculated, not stored.** `HolidayPresets.For(region, year)` (built, step 1) is the single
  source: start years inside it, the one-offs added (problem 4), an extra region `DE-BY-MH`
  (Bayern incl. Mariä Himmelfahrt), Austria with Oster-/Pfingstsonntag and Switzerland reduced
  to its four nationwide days (cantons left out). Until step 2 the import still copies it into
  rows; holidays with a start year are copied per year, never as recurring rows (problem 3 for
  new imports). Reading it directly ends the yearly import (problem 2).
- **Where the dates come from**: the rules are in code (Easter algorithm + each state's
  Feiertagsgesetz); `PublicHolidayDatesTests` pins every region's dates for 2025–2027 against
  Nager.Date, cross-checked with feiertage-api.de. Refresh that list when a state changes its law.
- **Region on the contract period**: `WorkSchedule.HolidayRegion` (null = the org's).
  `Organization.HolidayRegion` is the default (`DE`, a state, `AT`, `CH` or none).
- **Groups**: `Group.HolidayRegion` (null = inherit along the tree). It is a *default that is
  written*, not read live: setting it, or a member joining the group, starts a new contract
  period from a chosen date (default: today) with that region — for members without a
  personal region. Leaving the group does the reverse. A member in two groups with
  different regions gets none written and a "Bundesland unklar" hint in the member list.
- **Single members**: an admin sets the region on the member's contract period, dated.
- Rule of thumb in the UI: the Arbeitsort counts, not where someone lives.

## 6. Company holidays (org or group)

- Definition `CompanyDayOff` (org, `GroupId` null = whole org, `Kind` = `Holiday`,
  `Name`, `From`, `To`, `IsHalfDay`, `IsRecurring` for 24.12./31.12. every year).
- **Generated per member** as `MemberDayOff` rows (member, org, date, `IsHalfDay`, source
  id) for the members of the org/group (incl. sub-groups) on that date. Recurring ones are
  generated for the current and next year by the minute loop.
- Joining/leaving the org or group: rows are added/removed **from tomorrow (org-local) on**.
  Past rows stay.
- **Exclusion**: an admin removes a member from one definition → `CompanyDayOffExclusion`
  (source, member), so regeneration never brings the row back.
- Several groups: the member gets every group's company holidays (a benefit, most
  permissive wins, as elsewhere).
- Writes go through `SaveChangesAsync`, so the closed-period guard covers them; changing a
  past one asks for the reason. `MemberDayOff` joins `ClosedPeriods.Collect`.

### Migration of today's `Holiday` rows

Existing rows become org-wide `CompanyDayOff` definitions (so nothing changes on day one).
After the admin picks a region, the holidays page offers "remove the imported rows the
region now covers" (same date and name). The import endpoint then goes away.

## 7. Betriebsurlaub (org or group)

- Same definition table, `Kind` = `CompanyLeave`, a date range, optional half days.
- Generates **ordinary vacation `AbsenceDay` rows** (with `CompanyDayOffId`) on the
  member's working days in the range — target-0 days and holidays are skipped by the
  existing working-day filter. Everything that handles vacation (budget, calendars, team
  calendar, exports, closed periods) works without new code.
- Too few vacation days → the budget goes negative (decision).
- Same generation, membership and exclusion rules as section 6.
- An existing absence on a day wins (no row generated). Entering a sick day replaces the
  generated vacation day; deleting that sick day re-applies the Betriebsurlaub for that day.
- Changing or deleting a definition updates its rows; past months go through the
  closed-period reason prompt (decision).

## 8. Absences

### 8.1 What exists and stays

| Absence | Setup | Effect |
|---|---|---|
| Vacation | built-in | full (half) target credited, uses vacation |
| Sick day | built-in | full (half) target credited |
| Home-Office | custom type, `None` | hours count as tracked |
| Dienstreise | custom type, `FillToTarget` | topped up to the target, more counts as overtime |
| Training etc. | custom type, `FullCredit` | full target credited |
| Counts against vacation | custom type, `CountsAsVacation` | uses vacation |

### 8.2 Two half days on one day (proposal)

`AbsenceDay.Half` (`Full`/`First`/`Second`), unique per member/org/date/half, and the
service refuses a full day next to a half. The aggregator sums the credits of both halves
(capped at the target). Core hours: two halves = a full day off.

## 9. Vacation account

### 9.1 Contract periods

`WorkSchedule` is the dated contract: weekday targets, `HolidayRegion` (section 5) and new
**`VacationDaysPerYear`** (null = `UserOrganization.VacationDaysPerYear`, which becomes the
default for new periods). A change of hours, working days or vacation days during the year
is a new period from that date — already guarded by closed periods.

### 9.2 Entitlement of a year

1. **Credited range** (§§ 4, 5 BUrlG, waiting period 6 months):
   - full calendar year, unless one of the cases below;
   - **1/12 per full month** of membership in that year when the waiting period is not
     completed within the year (joined on 2 July or later), when the member leaves before
     the waiting period ends, or when they leave after it in the first half of the year
     (on or before 30 June). Full months count from the start date (joined 15.10. → 15.10.–14.11.
     and 15.11.–14.12. = 2 months).
2. **Entitlement** = Σ over the contract periods in the credited range of
   `VacationDaysPerYear × days of the period ÷ days of the year` (whole year) or
   `VacationDaysPerYear ÷ 12` per full month (§ 5 case; a month with a contract change is
   split by days).
3. **Rounding** (§ 5 (2)): a fraction of at least half a day is rounded up to a full day;
   smaller fractions stay as they are (shown with one decimal).
4. Days already earned are never reduced by a later change to fewer working days — each
   period keeps its share (EuGH/BAG).
5. **Override** per member and year (`VacationYearOverride.Entitlement` + reason), e.g. for
   days already granted by a previous employer (§ 6) or a contract that always pro-rates.

### 9.3 Carry-over and expiry

- Org setting `VacationCarryOver`: `Unlimited` (default) / `UntilDate` (day + month,
  default 31.03.) / `None`, optional `VacationCarryOverMaxDays`.
- `carriedIn(year)` = remaining of the previous year (within the cap); negative remaining
  is always carried in full.
- **Expiry** (only `UntilDate`): vacation taken from 1 January up to the expiry date uses
  carried days first; what is left of them on that date expires. Planned vacation after the
  date that only fits thanks to carried days gets a warning when entered.
- **Notice** (only with expiry, a legal precondition for it): in Q4 and about 4 weeks before
  the expiry date each affected member gets an in-app notice + mail ("Du hast noch X Tage,
  Y davon verfallen am 31.03."). The stored notifications are the proof. Without a sent
  notice nothing expires.
- Per member and year: `VacationYearOverride.ExpiryDate` (long illness: at most 15 months
  after the year), `Adjustment` + reason (paid out, corrections).
- **Starting value** for members moved over from paper/another system:
  `UserOrganization.InitialVacationDays` + its year (like `InitialOvertimeHours`).

### 9.4 Shown

Member row, dashboard and member detail: entitlement + carried − used (taken / planned) =
remaining, "davon verfallen am 31.03.: Y", and per year the breakdown with overrides.

## 10. Correctness and speed over the years

### 10.1 Two kinds of stored values

| | Starting value | Cache |
|---|---|---|
| Examples | `InitialOvertimeHours`, `InitialVacationDays`, admin overrides | month-end balances |
| Truth | itself (admin input) | the entries; the cache only speeds up |
| A past entry changes | stays (it is input) | deleted and rebuilt |

### 10.2 Month-end cache

- `MemberMonthBalance` (org, member, month): cumulative overtime at the month end and the
  vacation account at the month end (year's entitlement, used, carried in, expired).
- Written by the minute loop for month ends at least about a month ago.
- A balance = the latest cached month end + a live calculation from there (typically 1–2
  months of entries instead of years) — the team overview, balance endpoint, reports and
  the vacation account all read it this way.

### 10.3 Invalidation — one place

`TimeTrackingDbContext.SaveChangesAsync`, next to the closed-period guard, which already
knows (`ClosedPeriods.Collect`) which member and days a change touches: delete that
member's cache rows from that month on. Org-wide changes delete the org's rows from the
affected date on:

- `CompanyDayOff` definitions (the generated rows are member-dated and covered anyway),
  `Organization.HolidayRegion`, region presets after a code change (startup: version stamp)
- pause rules, `TimeZone`, `BusinessDayEnd`
- an absence type's `CreditMode` / `CountsAsVacation`
- `InitialOvertimeHours`, `InitialVacationDays`, `VacationYearOverride`, carry-over settings

Closed months make these changes rare and audited; an org that never closes a month still
works, its cache is simply rebuilt more often.

### 10.4 Safety net

An admin action "Salden prüfen" and a Postgres test that rebuild every value from scratch
and compare them with the cache; a difference is logged as an error (a missed invalidation).

## 11. Build order

Each step works on its own and leaves the app consistent.

1. ✅ **Public holiday calculator** — `HolidayPresets.For(region, year)` with start years and
   one-offs, `DE-BY-MH`; `PublicHolidayDatesTests` pins every region's dates for 2025–2027.
2. **Region on org, group and contract period** + **one resolver**
   `DaysOff.ForMembers(org, userIds, from, to)` (public + company holidays, full/half) used
   by all six readers; endpoint `GET organizations/{slug}/members/{id}/days-off?from&to` for
   the calendars; fix problem 5 (half holiday = 0.5 vacation days).
3. **Company holidays** — definitions, generated rows, exclusions, membership hooks,
   migration of today's `Holiday` rows, holidays page rebuilt (org/group, region picker).
4. **Betriebsurlaub** — `CompanyLeave` kind generating vacation absences, sick replaces.
5. **Two half-day absences per day** (8.2).
6. **Vacation account** — `VacationDaysPerYear` on contract periods, § 5 entitlement,
   overrides, carry-over (unlimited default) and optional expiry with notices, initial
   vacation days, UI.
7. **Month-end cache** + invalidation + "Salden prüfen". Can move before step 6 if the
   balance load gets slow first.

## 12. Tests to add

- Every region × 2025–2027 against an official list; start years (BE 2018 no Frauentag).
- Member in BY and member in BE of one org: Fronleichnam only for BY, 08.03. only for BE.
- Contract period moving a member from BY to BE on 01.07.: Allerheiligen gone, the first
  half unchanged.
- Group move does not change past company holidays; joining a group adds only future days.
- Exclusion survives regeneration.
- Betriebsurlaub: target-0 days and holidays skipped, negative budget, sick day returns
  the day, closed month asks for the reason.
- Vacation on 24.12. half holiday = 0.5 days.
- § 5: joined 01.03. → full year, joined 15.10. → 2/12 rounded per § 5 (2), left 30.06. →
  6/12, left 01.07. → full year; contract change 5 → 3 days on 01.07.; override wins.
- Carry-over unlimited incl. negative; expiry with and without a sent notice; per-member
  expiry date.
- Cache: every invalidation trigger in 10.3 deletes the right rows; rebuilt value equals the
  from-scratch value (Postgres, several years of data).

## 13. Open points

- Should the team calendar show colleagues' holidays per their region? The region reveals
  the Arbeitsort — probably under the `WorkSchedule` visibility kind.
- An org setting "always pro rata" (common in contracts) instead of per-member overrides?
- How far ahead recurring company holidays are generated (proposal: current + next year).
