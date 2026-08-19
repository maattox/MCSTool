/* budget.h - VM2 daily OCPU-hour ledger.
 *
 * VM2 is the budget authority: it opens an interval when the primary VM (VM1)
 * is started through OCI and closes it when VM1 stops, then charges
 * `(stop - start) x ocpus` against a flat daily cap. Timestamps are always
 * stored in UTC (ISO-8601, `YYYY-MM-DDTHH:MM:SSZ`) while the day boundary is
 * midnight America/Los_Angeles, matching `vm1/ledger.py`.
 *
 * Day bounds are computed from the US federal daylight-saving rules built into
 * budget.c rather than from libc `TZ`, because the target hosts cannot be
 * assumed to carry a tz database (and the MSYS/UCRT runtime used for local
 * builds silently ignores IANA zone names). See `la_offset_seconds`.
 */
#ifndef VM2_BUDGET_H
#define VM2_BUDGET_H

#include <stddef.h>

/* Phase A cap from the design spec: 45 OCPU-hours per LA calendar day. */
#define BUDGET_DAILY_LIMIT_OCPU_HOURS 45.0

typedef struct {
  char id[64];
  char started_at[32]; /* UTC ISO-8601 Z */
  char stopped_at[32]; /* empty if open */
  double ocpus;
} BudgetInterval;

typedef struct {
  BudgetInterval *items;
  size_t count;
  size_t cap;
} BudgetLedger;

/* Reads `path` into `led` (replacing any prior contents). A missing file is not
 * an error: it yields an empty ledger. Returns 0 on success, -1 on malformed
 * JSON or allocation failure. */
int budget_load(BudgetLedger *led, const char *path);

/* Serializes `led` to `path` via a temp file + rename. Returns 0 or -1. */
int budget_save(const BudgetLedger *led, const char *path);

/* Opens a new interval. Any intervals still open are closed at the same instant
 * first, so a missed stop cannot bill forever. `iso_utc_now` may be NULL to use
 * the current wall clock. Returns 0 or -1. */
int budget_record_start(BudgetLedger *led, double ocpus, const char *iso_utc_now);

/* Closes every open interval at `iso_utc_now` (NULL = current wall clock).
 * Returns the number of intervals closed, or -1 on error. */
int budget_record_stop(BudgetLedger *led, const char *iso_utc_now);

/* OCPU-hours charged to the LA calendar day `la_yyyy_mm_dd` ("2026-08-04").
 * Open intervals are billed up to `now_utc` (NULL = current wall clock).
 * Returns -1.0 if the ledger or date is unusable; callers enforcing the cap
 * should treat a negative result as "unknown", not as "nothing used". */
double budget_used_ocpu_for_la_day(const BudgetLedger *led, const char *la_yyyy_mm_dd,
                                   const char *now_utc);

/* Wall-clock hours (ignoring OCPU count) for the same window; used by the tests
 * and by the UI's "uptime today" readout. Returns -1.0 on bad input. */
double budget_uptime_hours_for_la_day(const BudgetLedger *led, const char *la_yyyy_mm_dd,
                                      const char *now_utc);

/* True once `used` reaches `daily_limit` (with a small epsilon, matching
 * `vm1.ledger.is_exhausted`). */
int budget_exhausted(double used, double daily_limit);

double budget_remaining_ocpu(double used, double daily_limit);

void budget_free(BudgetLedger *led);

/* --- time helpers (shared with state.c and the control daemon) ------------ */

/* Parses `YYYY-MM-DDTHH:MM:SS` with an optional fraction and either `Z` or a
 * `+HH:MM` / `-HH:MM` offset. Returns 0 and sets `*epoch` (UTC seconds), or -1. */
int budget_parse_iso(const char *iso, long long *epoch);

/* Formats UTC seconds as `YYYY-MM-DDTHH:MM:SSZ`. `size` must be >= 21. */
int budget_format_iso(long long epoch, char *out, size_t size);

/* Current UTC instant in the same format. */
int budget_now_iso(char *out, size_t size);

/* UTC seconds for the half-open window [start, end) covering one LA calendar
 * day. Returns 0 or -1 if `la_yyyy_mm_dd` is not a valid date. */
int budget_la_day_bounds(const char *la_yyyy_mm_dd, long long *start, long long *end);

/* LA calendar date ("YYYY-MM-DD") for `now_utc` (NULL = current wall clock).
 * `size` must be >= 11. Returns 0 or -1. */
int budget_la_date_for(const char *now_utc, char *out, size_t size);

/* Always Free Ampere envelope used in MOTD copy (~1500 OCPU-h/month).
 * 2 OCPU × 24h × 31d = 1488; 4 OCPU cannot stay up around the clock. */
#define ALWAYS_FREE_OCPU_HOUR_ENVELOPE 1500.0

static inline int budget_shape_always_on_capable(double ocpus) {
  if (ocpus <= 0.0) {
    return 0;
  }
  return (ocpus * 24.0 * 31.0) <= (ALWAYS_FREE_OCPU_HOUR_ENVELOPE + 0.5);
}

#endif /* VM2_BUDGET_H */
