/**
 * Shared constants used across the frontend.
 */

/** Day name labels for Mon-Sun week display. */
export const DAY_NAMES = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'] as const;

/** Maximum number of entries to fetch for cumulative overtime calculations. */
export const MAX_ENTRIES_FOR_OVERTIME = 10000;

/** Default fetch limit for weekly/monthly entry views. */
export const WEEKLY_ENTRY_LIMIT = 200;
export const MONTHLY_ENTRY_LIMIT = 500;
