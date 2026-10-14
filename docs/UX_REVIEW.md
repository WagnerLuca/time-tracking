# UX review – main tasks for members, leads and admins (issue #62)

Walked through on 2026-10-14/15 against the seeded dev app (in-memory DB) as
**member** Tom Weber, **group lead** Niklas Hoffmann (admin of DevOps, may approve
absences) and **admin** Anna Schmidt, on desktop (1280 px) and phone (390 px), light
and dark. Screenshots: `ux/before/…` and `ux/after/…` (desktop-light, desktop-dark,
phone-light, phone-dark), taken with a Playwright script; contrast was measured with
a script that blends every text colour over its real background (WCAG 2.x formula).

Clicks are counted from the dashboard, desktop, up to and including the final
"save/send" click; typing is not counted. "Scroll" marks a task whose control was
only found by scrolling a long page.

## Click counts per task

| Role | Task | Before | After | How (after) |
|---|---|---|---|---|
| Member | Start / stop the timer | 1 | 1 | Dashboard "Timer starten" (also by keyboard: Tab + Enter) |
| Member | Correct yesterday's entry | 3 | 3 | Zeit → ✎ → Speichern |
| Member | Add a forgotten day | 4 | **2** | Dashboard "+ Vergessenen Tag nachtragen" (form for yesterday opens) → Speichern |
| Member | Request vacation | 3 | **2** | Dashboard "+ Urlaub oder Abwesenheit" (dialog for today opens) → Antrag senden |
| Member | See own balance | 0 | 0 | Dashboard "Saldo gesamt" |
| Member | See remaining vacation | 1 | 1 | Organisation → "Mein Arbeitszeitmodell" |
| Member | See who is absent this week | 1 | 1 | Teamkalender (nav, bottom bar "Team", or dashboard "Wer ist diese Woche da?") |
| Member | Fix a failure entry | 2 | 2 | Banner "Jetzt korrigieren" (or bell → "Endzeit korrigieren") → Speichern |
| Member | What applies to me (rules) | 2 + scroll, no effective values | 2 | Organisation → Regeln: "Für dich gilt: …" per rule |
| Lead | See who is present | 0 | 0 | Dashboard "Mein Team" |
| Lead | Approve a request | 3 | **2** | Anfragen (nav, with count) → Annehmen |
| Lead | Decline with reason | 4 | **3** | Anfragen → Ablehnen → reason → Ablehnen |
| Lead | Correct a member's entry | 4 | 4 | Team card → day → ✎ → Speichern |
| Lead | See a member's balance | 0 | 0 | Team card (needs the capability) |
| Admin | Invite a member | 3 | 3 (2 via checklist) | Organisation → Mitglieder → Einladen |
| Admin | Set rules and visibility | 3 + long scroll (4 screens) | 3 | Organisation → Regeln & Sichtbarkeit → topic chip → switch |
| Admin | Create a group | 4 | 4 | Organisation → Gruppen → + Gruppe erstellen → Erstellen |
| Admin | Import holidays | 4 | 4 | Organisation → Feiertage → import |
| Admin | Export payroll | 2 | 3 | Organisation → Export → CSV herunterladen (moved under Organisation by the AC) |
| Admin | Set up a terminal | 3 + scroll to the very end (6 screens) | 3 | Organisation → Terminal → Schlüssel erstellen |
| All | Find pending work | bell popover, approvals in a modal, request history at the end of Einstellungen | 1 | Bell → inbox |

## Problems found

| # | Problem | Role | Status |
|---|---|---|---|
| 1 | Main navigation mixes everyday and admin places (Export for everyone, no Anfragen); "Organisation" opens a personal page | all | **Fixed** – Dashboard, Zeit, Verlauf, Teamkalender, Anfragen (approvers only, with count), Organisation |
| 2 | Administration is one 4 500 px page: settings, absence types, group matrix, pause rules, terminal keys, punch log and request history | admin | **Fixed** – Organisation sections: Mein Arbeitszeitmodell, Mitglieder, Gruppen, Regeln & Sichtbarkeit, Feiertage, Terminal, Export (sub-routes; `/export` redirects) |
| 3 | Pending work in three places: bell popover, a modal per request, request history at the bottom of Einstellungen; own open requests nowhere | all | **Fixed** – `/inbox`: decide inline (accept / decline with reason), failure entries, notices (core hours, reminders, settings), answers to own requests, own open requests, decided requests for approvers; the bell links there |
| 4 | Rule texts say "Legt fest, ob …" without saying what changes; no grouping; members cannot see what applies to *them* (group overrides) | all | **Fixed** – plain one-line effects, topics (Zeiterfassung, Arbeitszeit & Überstunden, Urlaub & Abwesenheiten, Mitglieder & Sicherheit, Sichtbarkeit, Gruppen-Abweichungen) with jump chips; members see "Für dich gilt: …" from their effective permissions ("durch deine Gruppe abweichend"); admins see "Abweichend in Gruppen: …" under each rule |
| 5 | Phone: hamburger at the top, far from the thumb; 10–45 targets per page under 44 px | all | **Fixed** – bottom bar (Dashboard, Zeit, Verlauf, Team, Mehr) above the safe area; buttons, inputs, selects, tabs, day rows and text links ≥ 44 px on touch; no horizontal scroll at 390 px on any reviewed page |
| 6 | "Add a forgotten day" and "request vacation" need knowing that History's day panel (or right-click) has them | member | **Fixed** – dashboard quick actions open the form/dialog for yesterday/today (`/history?date=…&do=add|absence`) |
| 7 | New users get no guidance | all | **Fixed** – dismissible checklists on the dashboard: member (work schedule, timer, team calendar), admin (invite, rules, holidays); ticked from real data, dismissal per user + org in localStorage |
| 8 | Page loads show a spinner; empty lists are bare text | all | **Fixed** – skeletons on every page load; empty states with the next step ("Noch keine Einträge in dieser Woche." + start button, empty day → "Eintrag nachtragen", inbox "Alles erledigt" → Dashboard) |
| 9 | Muted text (`/40`–`/60`), table headers, warning badges and an info-banner link below WCAG AA (down to 1.2:1) | all | **Fixed** – scan reports 0 violations on 13 pages × 2 themes; warning text uses the package's `--on-warning`; fallback tokens synced to `@wagnerluca/ui` 0.1.0 |
| 10 | No skip link; plain links/buttons had no focus ring; pulsing dots ignore reduced motion | all | **Fixed** – "Zum Inhalt springen", base `:focus-visible` outline, global `prefers-reduced-motion` rule |
| 11 | Desktop nav at 1280 px clipped its first entry once Anfragen appears | admin | **Fixed** – brand text only from 2xl |
| 12 | Confirmation dialogs | all | Checked – all 15 are for destructive actions (delete/revoke/remove/deactivate) except "switch timer to another org", which stops a running timer and stays |
| 13 | Toasts | all | Checked – every save/fail already toasts; inline approval adds "Antrag von X angenommen/abgelehnt" |
| 14 | Forms: date/time inputs | all | Checked – native `type=date/time` everywhere (phone pickers), inline validation existed; invite form capped at `max-w-2xl` |
| 15 | Settings toggles (32 px) and the dense group-override matrix on phones | admin | **Deferred** – switches grew to 32 px on touch (WCAG 2.5.8 AA's 24 px met), the matrix is a desktop table that scrolls inside its card; a phone layout for the matrix is its own task |
| 16 | Remaining vacation is not on the dashboard | member | **Deferred** – one click away (Organisation → Mein Arbeitszeitmodell); a fourth key figure needs the vacation endpoint on the dashboard and a layout decision |
| 17 | Withdrawing an own open request | member | **Deferred** – the inbox lists open requests, but the API has no withdraw endpoint |
| 18 | Admin "edit/delete organization" sits on the personal "Mein Arbeitszeitmodell" page | admin | **Deferred** – rarely used; moving it to a settings section touches the slug-change redirect |
| 19 | Days outside the current month in calendars stay faint (`/30`) | all | **Deferred** – inactive filler days, intentionally de-emphasised |
| 20 | e2e runs hit the 10 logins/min limit, so specs used odd users (an org admin as plain team member) | – | **Fixed** – `RateLimiting:AuthStrictPerMinute` (10 in production, 200 only in Development, pinned by `RateLimitConfigTests`); `sub-groups.spec` now uses Tom (member) in the team and Niklas (lead) as department admin |

## Tests

- e2e: request specs moved to the inbox; `roles.spec.ts` walks each role's main tasks
  (member incl. keyboard timer, quick actions, phone bottom bar and no horizontal
  scroll; lead approving inline; admin checklist + every Organisation section).
- Unit: inbox store, inbox helpers, onboarding, settings topics / "Für dich gilt".
- Seed: sub-group "API" under Backend (Felix leads it), so the hierarchy is visible.
