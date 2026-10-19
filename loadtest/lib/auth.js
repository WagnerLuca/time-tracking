// Sign-in helpers for the k6 scenarios: every user logs in once in setup() and the VUs reuse the tokens.
//
// Logins are rate-limited per IP (production 10/min, Development 200/min — RateLimiting:AuthStrictPerMinute),
// so setup() paces them with LOGINS_PER_MINUTE (default 9). Tokens are never refreshed during the run: refresh
// tokens rotate with reuse detection, and two VUs refreshing the same user's token would end that session. Keep
// the run shorter than the access token lifetime (Development 60 min; production 15 min — set
// JwtSettings__AccessTokenExpiryMinutes for the test instance).
import http from 'k6/http';
import { sleep, fail } from 'k6';

export const BASE_URL = (__ENV.BASE_URL || 'http://localhost:7000').replace(/\/$/, '');
export const API = `${BASE_URL}/api/v1`;
// The password perf-seed printed (random unless seeded with --password)
export const PASSWORD = __ENV.PASSWORD;
if (!PASSWORD) throw new Error('Set PASSWORD to the password perf-seed printed.');
const LOGINS_PER_MINUTE = Number(__ENV.LOGINS_PER_MINUTE || 9);

/** Logs one user in; returns { id, email, accessToken }. */
export function login(email) {
  const res = http.post(`${API}/Auth/login`, JSON.stringify({ email, password: PASSWORD }), {
    headers: { 'Content-Type': 'application/json' },
    tags: { name: 'login', kind: 'setup' }
  });
  if (res.status !== 200) fail(`login ${email}: ${res.status} ${res.body}`);
  const body = res.json();
  return { id: body.user.id, email, accessToken: body.accessToken };
}

/** Logs several users in, pausing between logins so the per-IP login limit is never hit. */
export function loginAll(emails) {
  const pause = 60 / Math.max(1, LOGINS_PER_MINUTE);
  return emails.map((email, i) => {
    if (i > 0) sleep(pause);
    return login(email);
  });
}

/** Request params for a signed-in user, tagged with the endpoint name and kind (interactive | export | setup). */
export function auth(user, name, kind = 'interactive', extra = {}) {
  return {
    headers: { Authorization: `Bearer ${user.accessToken}`, 'Content-Type': 'application/json' },
    tags: { name, kind },
    ...extra
  };
}

/** The seeded logins of perf-seed (`dotnet TimeTracking.Api.dll perf-seed`). */
export const emails = {
  owner: (slug) => `perf-owner@${slug}.test`,
  admin: (slug, n) => `perf-admin${n}@${slug}.test`,
  member: (slug, n) => `perf-user${n}@${slug}.test`
};
