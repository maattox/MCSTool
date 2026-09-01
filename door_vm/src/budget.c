/* budget.c - daily OCPU-hour ledger for the VM2 control plane.
 *
 * A BudgetLedger is plain owned memory with no internal locking; the control
 * daemon serializes access from its request threads.
 */
#include "budget.h"

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>

#include "jsonmin.h"

#define SECONDS_PER_DAY 86400LL
#define BUDGET_EPSILON 1e-9

/* ------------------------------------------------------- civil date math */

static long long floor_div(long long a, long long b) {
  long long q = a / b;
  if ((a % b != 0) && ((a < 0) != (b < 0))) {
    q--;
  }
  return q;
}

/* Howard Hinnant's days_from_civil: days since 1970-01-01 for a proleptic
 * Gregorian date. */
static long long days_from_civil(long long y, unsigned m, unsigned d) {
  y -= (m <= 2) ? 1 : 0;
  const long long era = (y >= 0 ? y : y - 399) / 400;
  const unsigned yoe = (unsigned)(y - era * 400);
  const unsigned mp = (m + 9u) % 12u;
  const unsigned doy = (153u * mp + 2u) / 5u + d - 1u;
  const unsigned doe = yoe * 365u + yoe / 4u - yoe / 100u + doy;
  return era * 146097LL + (long long)doe - 719468LL;
}

static void civil_from_days(long long z, int *year, unsigned *month, unsigned *day) {
  z += 719468;
  const long long era = (z >= 0 ? z : z - 146096) / 146097;
  const unsigned doe = (unsigned)(z - era * 146097);
  const unsigned yoe = (doe - doe / 1460u + doe / 36524u - doe / 146096u) / 365u;
  const long long y = (long long)yoe + era * 400;
  const unsigned doy = doe - (365u * yoe + yoe / 4u - yoe / 100u);
  const unsigned mp = (5u * doy + 2u) / 153u;
  const unsigned d = doy - (153u * mp + 2u) / 5u + 1u;
  const unsigned m = mp < 10u ? mp + 3u : mp - 9u;
  *year = (int)(y + (m <= 2u ? 1 : 0));
  *month = m;
  *day = d;
}

/* 0 = Sunday. */
static unsigned weekday_from_days(long long z) {
  return (unsigned)(((z % 7) + 11) % 7);
}

static unsigned nth_sunday(int year, unsigned month, unsigned nth) {
  long long first = days_from_civil(year, month, 1);
  unsigned wd = weekday_from_days(first);
  unsigned day = 1u + ((7u - wd) % 7u);
  return day + 7u * (nth - 1u);
}

/* America/Los_Angeles UTC offset in seconds, from the US federal rules in
 * effect since 2007: PDT (-7h) from 02:00 local on the second Sunday of March
 * through 02:00 local on the first Sunday of November, PST (-8h) otherwise.
 * VM2 never accounts for dates before 2007, where the schedule differed.
 *
 * Hard-coding the rule keeps day boundaries identical on every host: the Micro
 * may ship without a tz database, and the MSYS/UCRT runtime used for developer
 * builds accepts `TZ=America/Los_Angeles` while silently ignoring it. */
static int la_offset_seconds(long long utc_epoch) {
  int year;
  unsigned month, day;
  civil_from_days(floor_div(utc_epoch, SECONDS_PER_DAY), &year, &month, &day);

  /* 02:00 PST == 10:00 UTC; 02:00 PDT == 09:00 UTC. */
  long long dst_start = days_from_civil(year, 3, nth_sunday(year, 3, 2)) * SECONDS_PER_DAY +
                        10LL * 3600LL;
  long long dst_end = days_from_civil(year, 11, nth_sunday(year, 11, 1)) * SECONDS_PER_DAY +
                      9LL * 3600LL;
  return (utc_epoch >= dst_start && utc_epoch < dst_end) ? -7 * 3600 : -8 * 3600;
}

/* UTC instant of local midnight. Midnight is never skipped or repeated by a
 * 02:00 transition, so a single offset correction settles it. */
static long long la_midnight_utc(int year, unsigned month, unsigned day) {
  long long naive = days_from_civil(year, month, day) * SECONDS_PER_DAY;
  int offset = la_offset_seconds(naive + 8LL * 3600LL);
  long long utc = naive - offset;
  int settled = la_offset_seconds(utc);
  if (settled != offset) {
    utc = naive - settled;
  }
  return utc;
}

/* ------------------------------------------------------------ ISO-8601 */

static int parse_date_parts(const char *text, int *year, unsigned *month, unsigned *day) {
  int y = 0, m = 0, d = 0;
  int consumed = 0;
  if (sscanf(text, "%4d-%2d-%2d%n", &y, &m, &d, &consumed) != 3 || consumed != 10) {
    return -1;
  }
  if (y < 0 || m < 1 || m > 12 || d < 1 || d > 31) {
    return -1;
  }
  *year = y;
  *month = (unsigned)m;
  *day = (unsigned)d;
  return 0;
}

int budget_parse_iso(const char *iso, long long *epoch) {
  if (iso == NULL || epoch == NULL || iso[0] == '\0') {
    return -1;
  }
  int year = 0;
  unsigned month = 0, day = 0;
  if (parse_date_parts(iso, &year, &month, &day) != 0) {
    return -1;
  }
  const char *p = iso + 10;
  int hour = 0, minute = 0, second = 0;
  if (*p == 'T' || *p == 't' || *p == ' ') {
    p++;
    int consumed = 0;
    if (sscanf(p, "%2d:%2d:%2d%n", &hour, &minute, &second, &consumed) != 3 || consumed != 8) {
      return -1;
    }
    p += consumed;
    if (*p == '.') {
      p++;
      while (*p >= '0' && *p <= '9') {
        p++;
      }
    }
  } else if (*p != '\0') {
    return -1;
  }
  if (hour < 0 || hour > 23 || minute < 0 || minute > 59 || second < 0 || second > 60) {
    return -1;
  }

  long long offset = 0;
  if (*p == 'Z' || *p == 'z') {
    p++;
  } else if (*p == '+' || *p == '-') {
    int sign = (*p == '-') ? -1 : 1;
    int oh = 0, om = 0;
    int consumed = 0;
    if (sscanf(p + 1, "%2d:%2d%n", &oh, &om, &consumed) == 2 && consumed == 5) {
      p += 1 + consumed;
    } else if (sscanf(p + 1, "%2d%2d%n", &oh, &om, &consumed) == 2 && consumed == 4) {
      p += 1 + consumed;
    } else if (sscanf(p + 1, "%2d%n", &oh, &consumed) == 1 && consumed == 2) {
      p += 1 + consumed;
    } else {
      return -1;
    }
    if (oh < 0 || oh > 23 || om < 0 || om > 59) {
      return -1;
    }
    offset = sign * (oh * 3600LL + om * 60LL);
  }
  if (*p != '\0') {
    return -1;
  }

  *epoch = days_from_civil(year, month, day) * SECONDS_PER_DAY + hour * 3600LL +
           minute * 60LL + second - offset;
  return 0;
}

int budget_format_iso(long long epoch, char *out, size_t size) {
  if (out == NULL || size < 21) {
    return -1;
  }
  long long days = floor_div(epoch, SECONDS_PER_DAY);
  long long rest = epoch - days * SECONDS_PER_DAY;
  int year;
  unsigned month, day;
  civil_from_days(days, &year, &month, &day);
  int written = snprintf(out, size, "%04d-%02u-%02uT%02lld:%02lld:%02lldZ", year, month, day,
                         rest / 3600, (rest % 3600) / 60, rest % 60);
  return (written > 0 && (size_t)written < size) ? 0 : -1;
}

int budget_now_iso(char *out, size_t size) {
  return budget_format_iso((long long)time(NULL), out, size);
}

static int resolve_now(const char *now_utc, long long *epoch) {
  if (now_utc != NULL && now_utc[0] != '\0') {
    return budget_parse_iso(now_utc, epoch);
  }
  *epoch = (long long)time(NULL);
  return 0;
}

int budget_la_day_bounds(const char *la_yyyy_mm_dd, long long *start, long long *end) {
  if (la_yyyy_mm_dd == NULL || start == NULL || end == NULL) {
    return -1;
  }
  int year;
  unsigned month, day;
  if (parse_date_parts(la_yyyy_mm_dd, &year, &month, &day) != 0) {
    return -1;
  }
  if (la_yyyy_mm_dd[10] != '\0') {
    return -1;
  }
  int next_year;
  unsigned next_month, next_day;
  civil_from_days(days_from_civil(year, month, day) + 1, &next_year, &next_month, &next_day);
  *start = la_midnight_utc(year, month, day);
  *end = la_midnight_utc(next_year, next_month, next_day);
  return 0;
}

int budget_la_date_for(const char *now_utc, char *out, size_t size) {
  if (out == NULL || size < 11) {
    return -1;
  }
  long long now = 0;
  if (resolve_now(now_utc, &now) != 0) {
    return -1;
  }
  long long local = now + la_offset_seconds(now);
  int year;
  unsigned month, day;
  civil_from_days(floor_div(local, SECONDS_PER_DAY), &year, &month, &day);
  int written = snprintf(out, size, "%04d-%02u-%02u", year, month, day);
  return (written > 0 && (size_t)written < size) ? 0 : -1;
}

int budget_utc_day_bounds(const char *yyyy_mm_dd, long long *start, long long *end) {
  if (yyyy_mm_dd == NULL || start == NULL || end == NULL) {
    return -1;
  }
  int year;
  unsigned month, day;
  if (parse_date_parts(yyyy_mm_dd, &year, &month, &day) != 0) {
    return -1;
  }
  if (yyyy_mm_dd[10] != '\0') {
    return -1;
  }
  *start = days_from_civil(year, month, day) * SECONDS_PER_DAY;
  *end = *start + SECONDS_PER_DAY;
  return 0;
}

int budget_utc_date_for(const char *now_utc, char *out, size_t size) {
  if (out == NULL || size < 11) {
    return -1;
  }
  long long now = 0;
  if (resolve_now(now_utc, &now) != 0) {
    return -1;
  }
  int year;
  unsigned month, day;
  civil_from_days(floor_div(now, SECONDS_PER_DAY), &year, &month, &day);
  int written = snprintf(out, size, "%04d-%02u-%02u", year, month, day);
  return (written > 0 && (size_t)written < size) ? 0 : -1;
}

/* --------------------------------------------------------- ledger memory */

static int copy_field(char *dst, size_t size, const char *src) {
  if (src == NULL) {
    dst[0] = '\0';
    return 0;
  }
  size_t len = strlen(src);
  if (len >= size) {
    return -1; /* truncating an id or timestamp would corrupt the ledger */
  }
  memcpy(dst, src, len + 1);
  return 0;
}

static int ledger_reserve(BudgetLedger *led, size_t needed) {
  if (needed <= led->cap) {
    return 0;
  }
  size_t cap = led->cap != 0 ? led->cap : 8;
  while (cap < needed) {
    cap *= 2;
  }
  BudgetInterval *items = realloc(led->items, cap * sizeof *items);
  if (items == NULL) {
    return -1;
  }
  led->items = items;
  led->cap = cap;
  return 0;
}

void budget_free(BudgetLedger *led) {
  if (led == NULL) {
    return;
  }
  free(led->items);
  led->items = NULL;
  led->count = 0;
  led->cap = 0;
}

/* Session ids only need to be unique within one ledger file, so a timestamp
 * plus a rolling counter beats pulling in a UUID dependency. */
static void gen_session_id(char *out, size_t size, const char *iso) {
  static unsigned long counter = 0;
  char compact[32];
  size_t j = 0;
  for (size_t i = 0; iso[i] != '\0' && j + 1 < sizeof compact; i++) {
    if (iso[i] != '-' && iso[i] != ':') {
      compact[j++] = iso[i];
    }
  }
  compact[j] = '\0';
  snprintf(out, size, "sess-%s-%04lx", compact, (counter++ ^ (unsigned long)time(NULL)) & 0xffffUL);
}

/* ---------------------------------------------------------- load / save */

int budget_load(BudgetLedger *led, const char *path) {
  if (led == NULL || path == NULL) {
    return -1;
  }
  budget_free(led);

  FILE *probe = fopen(path, "rb");
  if (probe == NULL) {
    return 0; /* no ledger yet is a normal cold start */
  }
  fclose(probe);

  JsonValue *root = json_parse_file(path);
  if (root == NULL || json_type(root) != JSON_OBJECT) {
    json_free(root);
    return -1;
  }
  const JsonValue *intervals = json_object_get(root, "intervals");
  if (intervals == NULL || json_is_null(intervals)) {
    json_free(root);
    return 0;
  }
  if (json_type(intervals) != JSON_ARRAY) {
    json_free(root);
    return -1;
  }

  size_t count = json_array_count(intervals);
  if (ledger_reserve(led, count) != 0) {
    json_free(root);
    return -1;
  }
  for (size_t i = 0; i < count; i++) {
    const JsonValue *item = json_array_at(intervals, i);
    if (json_type(item) != JSON_OBJECT) {
      json_free(root);
      budget_free(led);
      return -1;
    }
    BudgetInterval interval;
    memset(&interval, 0, sizeof interval);
    if (copy_field(interval.id, sizeof interval.id,
                   json_as_string(json_object_get(item, "id"), "")) != 0 ||
        copy_field(interval.started_at, sizeof interval.started_at,
                   json_as_string(json_object_get(item, "started_at"), "")) != 0 ||
        copy_field(interval.stopped_at, sizeof interval.stopped_at,
                   json_as_string(json_object_get(item, "stopped_at"), "")) != 0) {
      json_free(root);
      budget_free(led);
      return -1;
    }
    interval.ocpus = json_as_number(json_object_get(item, "ocpus"), 0.0);
    led->items[led->count++] = interval;
  }
  json_free(root);
  return 0;
}

int budget_save(const BudgetLedger *led, const char *path) {
  if (led == NULL || path == NULL) {
    return -1;
  }
  JsonBuf buf;
  json_buf_init(&buf);
  json_buf_raw(&buf, "{\n  \"version\": 1,\n  \"intervals\": [");
  for (size_t i = 0; i < led->count; i++) {
    const BudgetInterval *item = &led->items[i];
    json_buf_raw(&buf, i == 0 ? "\n" : ",\n");
    json_buf_raw(&buf, "    {\n      \"id\": ");
    json_buf_string(&buf, item->id);
    json_buf_raw(&buf, ",\n      \"started_at\": ");
    json_buf_string(&buf, item->started_at);
    json_buf_raw(&buf, ",\n      \"stopped_at\": ");
    if (item->stopped_at[0] == '\0') {
      json_buf_raw(&buf, "null");
    } else {
      json_buf_string(&buf, item->stopped_at);
    }
    json_buf_raw(&buf, ",\n      \"ocpus\": ");
    json_buf_number(&buf, item->ocpus);
    json_buf_raw(&buf, "\n    }");
  }
  json_buf_raw(&buf, led->count != 0 ? "\n  ]\n}\n" : "]\n}\n");

  int rc = buf.error ? -1 : json_write_file(path, buf.data);
  json_buf_free(&buf);
  return rc;
}

/* --------------------------------------------------------- record / read */

int budget_record_stop(BudgetLedger *led, const char *iso_utc_now) {
  if (led == NULL) {
    return -1;
  }
  long long now = 0;
  if (resolve_now(iso_utc_now, &now) != 0) {
    return -1;
  }
  char stamp[32];
  if (budget_format_iso(now, stamp, sizeof stamp) != 0) {
    return -1;
  }
  int closed = 0;
  for (size_t i = 0; i < led->count; i++) {
    if (led->items[i].stopped_at[0] != '\0') {
      continue;
    }
    memcpy(led->items[i].stopped_at, stamp, strlen(stamp) + 1);
    closed++;
  }
  return closed;
}

int budget_record_start(BudgetLedger *led, double ocpus, const char *iso_utc_now) {
  if (led == NULL || ocpus <= 0.0) {
    return -1;
  }
  long long now = 0;
  if (resolve_now(iso_utc_now, &now) != 0) {
    return -1;
  }
  char stamp[32];
  if (budget_format_iso(now, stamp, sizeof stamp) != 0) {
    return -1;
  }
  /* A start with an interval still open means a stop went unrecorded; close it
   * here so the gap is bounded instead of billing until the next read. */
  if (budget_record_stop(led, stamp) < 0) {
    return -1;
  }
  if (ledger_reserve(led, led->count + 1) != 0) {
    return -1;
  }
  BudgetInterval *interval = &led->items[led->count];
  memset(interval, 0, sizeof *interval);
  gen_session_id(interval->id, sizeof interval->id, stamp);
  memcpy(interval->started_at, stamp, strlen(stamp) + 1);
  interval->ocpus = ocpus;
  led->count++;
  return 0;
}

static int window_totals(const BudgetLedger *led, long long window_start, long long window_end,
                         const char *now_utc, double *uptime_hours, double *ocpu_hours) {
  if (led == NULL) {
    return -1;
  }
  long long now = 0;
  if (resolve_now(now_utc, &now) != 0) {
    return -1;
  }

  double uptime = 0.0;
  double ocpu = 0.0;
  for (size_t i = 0; i < led->count; i++) {
    const BudgetInterval *item = &led->items[i];
    long long start = 0;
    if (budget_parse_iso(item->started_at, &start) != 0) {
      continue; /* unreadable stamp: ignore rather than mis-bill */
    }
    long long end = now;
    if (item->stopped_at[0] != '\0' && budget_parse_iso(item->stopped_at, &end) != 0) {
      continue;
    }
    if (start < window_start) {
      start = window_start;
    }
    if (end > window_end) {
      end = window_end;
    }
    if (end <= start) {
      continue;
    }
    double hours = (double)(end - start) / 3600.0;
    uptime += hours;
    ocpu += hours * item->ocpus;
  }
  if (uptime_hours != NULL) {
    *uptime_hours = uptime;
  }
  if (ocpu_hours != NULL) {
    *ocpu_hours = ocpu;
  }
  return 0;
}

static int day_totals_la(const BudgetLedger *led, const char *la_yyyy_mm_dd, const char *now_utc,
                         double *uptime_hours, double *ocpu_hours) {
  long long window_start = 0, window_end = 0;
  if (budget_la_day_bounds(la_yyyy_mm_dd, &window_start, &window_end) != 0) {
    return -1;
  }
  return window_totals(led, window_start, window_end, now_utc, uptime_hours, ocpu_hours);
}

static int day_totals_utc(const BudgetLedger *led, const char *yyyy_mm_dd, const char *now_utc,
                          double *uptime_hours, double *ocpu_hours) {
  long long window_start = 0, window_end = 0;
  if (budget_utc_day_bounds(yyyy_mm_dd, &window_start, &window_end) != 0) {
    return -1;
  }
  return window_totals(led, window_start, window_end, now_utc, uptime_hours, ocpu_hours);
}

double budget_used_ocpu_for_la_day(const BudgetLedger *led, const char *la_yyyy_mm_dd,
                                   const char *now_utc) {
  double ocpu = 0.0;
  if (day_totals_la(led, la_yyyy_mm_dd, now_utc, NULL, &ocpu) != 0) {
    return -1.0;
  }
  return ocpu;
}

double budget_uptime_hours_for_la_day(const BudgetLedger *led, const char *la_yyyy_mm_dd,
                                      const char *now_utc) {
  double uptime = 0.0;
  if (day_totals_la(led, la_yyyy_mm_dd, now_utc, &uptime, NULL) != 0) {
    return -1.0;
  }
  return uptime;
}

double budget_used_ocpu_for_utc_day(const BudgetLedger *led, const char *yyyy_mm_dd,
                                    const char *now_utc) {
  double ocpu = 0.0;
  if (day_totals_utc(led, yyyy_mm_dd, now_utc, NULL, &ocpu) != 0) {
    return -1.0;
  }
  return ocpu;
}

double budget_uptime_hours_for_utc_day(const BudgetLedger *led, const char *yyyy_mm_dd,
                                       const char *now_utc) {
  double uptime = 0.0;
  if (day_totals_utc(led, yyyy_mm_dd, now_utc, &uptime, NULL) != 0) {
    return -1.0;
  }
  return uptime;
}

int budget_exhausted(double used, double daily_limit) {
  return used >= daily_limit - BUDGET_EPSILON;
}

double budget_remaining_ocpu(double used, double daily_limit) {
  double remaining = daily_limit - used;
  return remaining > 0.0 ? remaining : 0.0;
}
