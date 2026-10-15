# Terminal API (for device builders)

A terminal is an unattended device at a door or a desk, typically an RFID reader. When a card
is scanned it calls one endpoint, and the server starts or stops the timer of the member who
holds that card. This page is the contract for building one.

## Setup (organization admin)

1. **Settings → Terminal-API-Schlüssel**: create one key per device. The key's name (e.g.
   "Eingang") is the terminal name shown in the punch protocol and on the entries. The
   plaintext key is shown once; it can be renamed or revoked later.
2. **Cards**: either type the card ID on the member's page (*Terminal-Karten*), or simply scan
   an unknown card at the terminal and assign it under *Unbekannte Karten* in the settings.
   A member can hold several cards; a lost card can be blocked or removed.

## Request

```
POST /api/v1/terminal/punch
X-Api-Key: tt_...
Content-Type: application/json

{
  "badgeId": "04:A2:3B:9C",                 // required, at most 100 characters
  "scannedAt": "2026-10-02T06:58:12Z",      // optional, UTC (ISO 8601 with Z)
  "clientPunchId": "door-1-000042"           // optional, at most 64 characters
}
```

- **`badgeId`** is opaque to the server. It is normalised (trimmed, uppercased, `:` and spaces
  removed — `04:a2 3b 9c` and `04A23B9C` are the same card) and only its SHA-256 and last four
  characters are stored. Full card values are never returned or logged.
- **`scannedAt`** is when the card was scanned. Send it for every scan you queued while offline
  (you may always send it). Accepted from the organization's offline window (default 72 hours)
  ago up to 2 minutes ahead. Without it the arrival time counts.
- **`clientPunchId`** is your id for this scan, unique per key (e.g. a counter persisted in
  flash). It makes retries safe: a punch id the server has already accepted returns the stored
  result and changes nothing.

## Response

`200 OK`:

```json
{
  "action": "Started",          // "Started" | "Stopped" | "ClosedFailure"
  "repeated": false,
  "firstName": "Mia",
  "lastName": "Member",
  "entry": { "id": 812, "startTime": "2026-10-02T06:58:12Z", "endTime": null, "isRunning": true,
             "source": "Terminal", "terminalStartName": "Eingang", "...": "..." }
}
```

Show the name and the action on the display ("Guten Morgen, Mia — eingestempelt").

Errors are RFC 7807 problem details with a stable `code`:

| Status | `code` | Meaning | What the terminal should do |
|---|---|---|---|
| 400 | `terminal.scannedAtOutOfRange` | `scannedAt` older than the offline window (default 72 h) or more than 2 min ahead | drop the scan, check the clock (NTP) |
| 400 | `terminal.cardRequired` | empty `badgeId` | drop |
| 400 | `entry.*` | the resulting times are invalid (e.g. an entry longer than 24 h) | drop, show "please use the app" |
| 401 | `terminal.invalidKey` | key missing, wrong or revoked | stop sending, show "terminal not configured" |
| 403 | `terminal.cardBlocked` | the card is blocked | show "card blocked" |
| 403 | `org.twoFactorRequired` | the organization requires 2FA the member has not set up | show "please use the app" |
| 404 | `terminal.unknownCard` | nobody holds this card (an admin can assign it now) | show "unknown card" |
| 409 | `terminal.outOfOrder` | `scannedAt` is earlier than the member's last accepted punch | drop |
| 409 | `terminal.timerElsewhere` | the member has a timer running in another organization (not revealed further) | show "please use the app" |
| 409 | `entry.overlap` | a queued start would overlap an existing entry | drop |
| 429 | `terminal.tooManyUnknown` | more unknown/blocked scans from this key in 10 minutes than the budget (default 20) | wait, see below |
| 429 | — | more than 120 requests per minute from this key | back off |
| 5xx | — | server problem | keep the scan queued, retry |

## Rules

Three limits are **settings per organization** (Organisation → Terminal → "Terminal-Regeln", or
`PUT organizations/{slug}/settings`; read back on the organization detail). They apply to every
key of the organization from the next scan on:

| Setting | Field | Default | Range |
|---|---|---|---|
| Double-scan window | `terminalDoubleScanSeconds` | 60 s | 5–600 s |
| Offline window (oldest queued `scannedAt`) | `terminalOfflineHours` | 72 h | 1–168 h |
| Unknown-card budget per key and 10 minutes | `terminalUnknownCardLimit` | 20 | 5–200 |

Out-of-range values are refused with 400 `terminal.doubleScanRange` / `terminal.offlineRange` /
`terminal.unknownLimitRange`. A terminal that queues offline should keep its queue no longer than
the offline window; older scans are refused (`terminal.scannedAtOutOfRange`, the detail names the
current window).

- **Toggle**: a scan stops the member's running timer in this organization, otherwise it starts
  one. A timer running in another organization is neither stopped nor revealed (409).
- **Double scan**: a second scan of the same card within the double-scan window (default 60 seconds, by `scannedAt`) toggles
  nothing. It answers `200` with the previous result and `"repeated": true`. A different card
  of the same member is a real toggle.
- **Offline queue**: queue scans with their `scannedAt` and a `clientPunchId` while the server
  is unreachable, then send them **one at a time, oldest first**, waiting for each answer. A start
  uses `scannedAt` as its start time, a stop as its end time (pause rules apply as usual).
  - If no timer is running but the member has an entry the server auto-stopped at the business
    day end ("needs correction") that started within 24 hours before `scannedAt`, the queued scan
    closes that entry at `scannedAt` (`"action": "ClosedFailure"`) instead of starting a new timer.
  - A queued scan older than the member's last accepted punch is refused (409
    `terminal.outOfOrder`), so the queue must stay in order.
- **Idempotency**: only accepted punches (200) are remembered under their `clientPunchId`; a
  refused scan (4xx) may be sent again, but there is rarely a point.
- **What to retry**: retry on network errors, `5xx` and `429` with exponential backoff (e.g. 1 s,
  2 s, 4 s … capped at 5 min, with jitter), honouring `Retry-After` when present. Do **not** retry
  other `4xx` — drop the scan and move on to the next one in the queue. Always reuse the same
  `clientPunchId` when retrying.
- **Unknown-card budget**: after the budget (default 20) of unknown or blocked scans in 10 minutes, further unknown/blocked
  scans from that key get `429 terminal.tooManyUnknown` until older ones leave the 10-minute window.
  Known, active cards keep working throughout.
- Every scan that reaches the card lookup is written to the punch protocol (admins see it in the
  settings, kept for 400 days): result, terminal, card hint, member, scan and arrival time.

## Security notes

- **Plain card UIDs are not secrets.** The UID of a MIFARE Classic / Ultralight / NTAG card (and of
  most 125 kHz cards) is readable by any phone and can be cloned onto a "magic" card in seconds.
  With UIDs, the terminal records attendance — it does not prove identity. That is acceptable for
  many workplaces; just treat it accordingly.
- **For real security** use cards with cryptography, e.g. **MIFARE DESFire EV2/EV3** with your own
  application and AES application key. The terminal authenticates the card with that key and sends
  a value derived from the card's secret (e.g. an HMAC of the UID under a key stored in the card
  application, or a value read from an encrypted file) as `badgeId`. The server treats `badgeId` as
  opaque, so nothing changes on its side. Enrol such cards by scanning them (*Unbekannte Karten*).
- **The API key is the terminal's credential.** One key per device; never share a key between
  devices. Keep it out of firmware images and source control (provision it into NVS/secure storage
  during setup), enable flash encryption / secure boot where the platform offers it, and use HTTPS
  with certificate validation. If a device is lost or stolen, revoke its key in the settings —
  the protocol keeps its rows. A key can do nothing but punch cards of its own organization; it is
  not a user login.

## Examples

```sh
curl -sS https://zeit.example.com/api/v1/terminal/punch \
  -H "X-Api-Key: $TT_KEY" -H "Content-Type: application/json" \
  -d '{"badgeId":"04:A2:3B:9C","scannedAt":"2026-10-02T06:58:12Z","clientPunchId":"door-1-000042"}'
```

Minimal ESP32 / Arduino flow (pseudo code):

```cpp
// setup(): Wi-Fi, NTP (configTime), RC522/PN532 reader, key from NVS (never hard-coded)
// Queue: ring buffer in flash of {badgeId, scannedAt, clientPunchId}

void loop() {
  if (reader.cardPresent()) {
    String uid = reader.readUidHex();            // or a DESFire-derived value, see above
    queue.push({uid, nowIsoUtc(), deviceId + "-" + String(++counter)});
    persistCounter(counter);
    beep();
  }
  if (!queue.empty() && WiFi.isConnected() && millis() >= nextAttempt) {
    Punch p = queue.front();                      // oldest first, one at a time
    int status = postJson("/api/v1/terminal/punch", apiKey, p, &response);
    if (status == 200) {                          // includes "repeated": true
      display(response.firstName, response.action);
      queue.pop(); backoff = 1000;
    } else if (status == 429 || status >= 500 || status < 0) {
      nextAttempt = millis() + backoff + random(250);   // keep it queued
      backoff = min(backoff * 2, 300000);
    } else {                                      // other 4xx: show the code, drop the scan
      display(errorText(response.code));
      queue.pop();
    }
  }
}
```
