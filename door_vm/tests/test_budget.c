/* test_budget.c - VM2 budget ledger tests.
 *
 * The authority for the LA-day numbers is tests/fixtures/budget_sessions.json,
 * the same fixture `tests/test_budget_la.py` runs against: VM1 (Python) and VM2
 * (C) must charge identical OCPU-hours to identical UTC intervals, including
 * sessions that straddle America/Los_Angeles midnight.
 *
 * Pass the fixture path as argv[1]; the default assumes `make test` runs from
 * vm2/.
 */
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include "budget.h"
#include "jsonmin.h"

#define DEFAULT_FIXTURE "../tests/fixtures/budget_sessions.json"
#define EPS 1e-6

static int failures = 0;
static int checks = 0;

#define CHECK(cond, ...)                       \
  do {                                         \
    checks++;                                  \
    if (!(cond)) {                             \
      failures++;                              \
      printf("  FAIL %s:%d: ", __FILE__, __LINE__); \
      printf(__VA_ARGS__);                     \
      printf("\n");                            \
    }                                          \
  } while (0)

#define CHECK_NEAR(actual, expected, ...)                                  \
  do {                                                                     \
    double a_ = (actual);                                                  \
    double e_ = (expected);                                                \
    checks++;                                                              \
    if (!(a_ - e_ < EPS && e_ - a_ < EPS)) {                               \
      failures++;                                                          \
      printf("  FAIL %s:%d: ", __FILE__, __LINE__);                        \
      printf(__VA_ARGS__);                                                 \
      printf(" (got %.9f, want %.9f)\n", a_, e_);                          \
    }                                                                      \
  } while (0)

/* ------------------------------------------------------------- helpers */

static int ledger_append(BudgetLedger *led, const char *started_at, const char *stopped_at,
                         double ocpus) {
  BudgetInterval *items = realloc(led->items, (led->count + 1) * sizeof *items);
  if (items == NULL) {
    return -1;
  }
  led->items = items;
  led->cap = led->count + 1;
  BudgetInterval *item = &items[led->count];
  memset(item, 0, sizeof *item);
  snprintf(item->id, sizeof item->id, "fixture-%zu", led->count);
  snprintf(item->started_at, sizeof item->started_at, "%s", started_at);
  if (stopped_at != NULL) {
    snprintf(item->stopped_at, sizeof item->stopped_at, "%s", stopped_at);
  }
  item->ocpus = ocpus;
  led->count++;
  return 0;
}

/* ---------------------------------------------------- fixture-driven test */

static void test_fixture_scenarios(const char *fixture_path) {
  printf("fixture scenarios (%s)\n", fixture_path);
  JsonValue *root = json_parse_file(fixture_path);
  CHECK(root != NULL, "could not read fixture %s", fixture_path);
  if (root == NULL) {
    return;
  }
  const JsonValue *scenarios = json_object_get(root, "scenarios");
  size_t count = json_array_count(scenarios);
  CHECK(count >= 2, "expected at least 2 scenarios, got %zu", count);

  int saw_same_day = 0;
  int saw_spans_midnight = 0;

  for (size_t i = 0; i < count; i++) {
    const JsonValue *scenario = json_array_at(scenarios, i);
    const char *name = json_as_string(json_object_get(scenario, "name"), "?");
    if (strcmp(name, "same_day_session") == 0) {
      saw_same_day = 1;
    } else if (strcmp(name, "spans_la_midnight") == 0) {
      saw_spans_midnight = 1;
    }
    double ocpus = json_as_number(json_object_get(scenario, "ocpus"), 0.0);
    const char *now = json_as_string(json_object_get(scenario, "now"), NULL);

    BudgetLedger led;
    memset(&led, 0, sizeof led);
    const JsonValue *intervals = json_object_get(scenario, "intervals");
    for (size_t j = 0; j < json_array_count(intervals); j++) {
      const JsonValue *interval = json_array_at(intervals, j);
      const char *started = json_as_string(json_object_get(interval, "started_at"), "");
      const char *stopped = json_as_string(json_object_get(interval, "stopped_at"), NULL);
      CHECK(ledger_append(&led, started, stopped, ocpus) == 0, "%s: out of memory", name);
    }

    const JsonValue *expectations = json_object_get(scenario, "expectations");
    for (size_t j = 0; j < json_array_count(expectations); j++) {
      const JsonValue *expectation = json_array_at(expectations, j);
      const char *la_date = json_as_string(json_object_get(expectation, "la_date"), "");
      double want_uptime = json_as_number(json_object_get(expectation, "uptime_hours"), -1.0);
      double want_ocpu = json_as_number(json_object_get(expectation, "ocpu_hours"), -1.0);
      CHECK_NEAR(budget_uptime_hours_for_la_day(&led, la_date, now), want_uptime,
                 "%s (%s): uptime_hours", name, la_date);
      CHECK_NEAR(budget_used_ocpu_for_la_day(&led, la_date, now), want_ocpu,
                 "%s (%s): ocpu_hours", name, la_date);
    }
    budget_free(&led);
  }

  CHECK(saw_same_day, "fixture is missing the same_day_session scenario");
  CHECK(saw_spans_midnight, "fixture is missing the spans_la_midnight scenario");
  json_free(root);
}

/* The two headline fixture cases, spelled out so a broken or missing fixture
 * file cannot quietly reduce this suite to a no-op. */
static void test_headline_numbers_hardcoded(void) {
  printf("headline fixture numbers (hardcoded)\n");

  /* same_day_session: 4 OCPUs, 18:00Z-20:00Z on 2026-08-04 (11:00-13:00 PDT). */
  BudgetLedger same_day;
  memset(&same_day, 0, sizeof same_day);
  ledger_append(&same_day, "2026-08-04T18:00:00Z", "2026-08-04T20:00:00Z", 4.0);
  CHECK_NEAR(budget_uptime_hours_for_la_day(&same_day, "2026-08-04", NULL), 2.0,
             "same_day_session uptime");
  CHECK_NEAR(budget_used_ocpu_for_la_day(&same_day, "2026-08-04", NULL), 8.0,
             "same_day_session ocpu-hours");
  budget_free(&same_day);

  /* spans_la_midnight: 4 OCPUs, 2026-08-05 06:00Z-08:00Z straddles 00:00 PDT,
   * so the LA days 08-04 and 08-05 take one hour each. */
  BudgetLedger spans;
  memset(&spans, 0, sizeof spans);
  ledger_append(&spans, "2026-08-05T06:00:00Z", "2026-08-05T08:00:00Z", 4.0);
  CHECK_NEAR(budget_uptime_hours_for_la_day(&spans, "2026-08-04", NULL), 1.0,
             "spans_la_midnight 08-04 uptime");
  CHECK_NEAR(budget_used_ocpu_for_la_day(&spans, "2026-08-04", NULL), 4.0,
             "spans_la_midnight 08-04 ocpu-hours");
  CHECK_NEAR(budget_uptime_hours_for_la_day(&spans, "2026-08-05", NULL), 1.0,
             "spans_la_midnight 08-05 uptime");
  CHECK_NEAR(budget_used_ocpu_for_la_day(&spans, "2026-08-05", NULL), 4.0,
             "spans_la_midnight 08-05 ocpu-hours");
  budget_free(&spans);
}

/* ------------------------------------------------------------ time rules */

static void test_la_day_bounds(void) {
  printf("LA day bounds\n");
  long long start = 0, end = 0;

  /* Summer (PDT, UTC-7): 2026-08-04 runs 07:00Z to 07:00Z. */
  CHECK(budget_la_day_bounds("2026-08-04", &start, &end) == 0, "summer bounds failed");
  char buf[32];
  budget_format_iso(start, buf, sizeof buf);
  CHECK(strcmp(buf, "2026-08-04T07:00:00Z") == 0, "PDT day start was %s", buf);
  budget_format_iso(end, buf, sizeof buf);
  CHECK(strcmp(buf, "2026-08-05T07:00:00Z") == 0, "PDT day end was %s", buf);
  CHECK(end - start == 86400, "PDT day length was %lld", end - start);

  /* Winter (PST, UTC-8). */
  CHECK(budget_la_day_bounds("2026-01-15", &start, &end) == 0, "winter bounds failed");
  budget_format_iso(start, buf, sizeof buf);
  CHECK(strcmp(buf, "2026-01-15T08:00:00Z") == 0, "PST day start was %s", buf);

  /* Spring forward: 2026-03-08 is 23 hours long. */
  CHECK(budget_la_day_bounds("2026-03-08", &start, &end) == 0, "spring bounds failed");
  budget_format_iso(start, buf, sizeof buf);
  CHECK(strcmp(buf, "2026-03-08T08:00:00Z") == 0, "spring day start was %s", buf);
  CHECK(end - start == 23 * 3600, "spring-forward day was %lld s", end - start);

  /* Fall back: 2026-11-01 is 25 hours long. */
  CHECK(budget_la_day_bounds("2026-11-01", &start, &end) == 0, "fall bounds failed");
  budget_format_iso(start, buf, sizeof buf);
  CHECK(strcmp(buf, "2026-11-01T07:00:00Z") == 0, "fall day start was %s", buf);
  CHECK(end - start == 25 * 3600, "fall-back day was %lld s", end - start);

  CHECK(budget_la_day_bounds("not-a-date", &start, &end) != 0, "garbage date accepted");
  CHECK(budget_la_day_bounds("2026-08-04T00:00:00Z", &start, &end) != 0,
        "timestamp accepted as a date");
}

static void test_la_date_for(void) {
  printf("LA date for UTC instant\n");
  char day[16];
  CHECK(budget_la_date_for("2026-08-05T06:59:59Z", day, sizeof day) == 0, "la_date_for failed");
  CHECK(strcmp(day, "2026-08-04") == 0, "23:59:59 PDT mapped to %s", day);
  CHECK(budget_la_date_for("2026-08-05T07:00:00Z", day, sizeof day) == 0, "la_date_for failed");
  CHECK(strcmp(day, "2026-08-05") == 0, "00:00:00 PDT mapped to %s", day);
}

static void test_iso_round_trip(void) {
  printf("ISO-8601 parsing\n");
  long long epoch = 0;
  CHECK(budget_parse_iso("2026-08-04T18:00:00Z", &epoch) == 0, "Z stamp rejected");
  char buf[32];
  CHECK(budget_format_iso(epoch, buf, sizeof buf) == 0, "format failed");
  CHECK(strcmp(buf, "2026-08-04T18:00:00Z") == 0, "round trip produced %s", buf);

  long long offset_epoch = 0;
  CHECK(budget_parse_iso("2026-08-04T11:00:00-07:00", &offset_epoch) == 0, "offset stamp rejected");
  CHECK(offset_epoch == epoch, "offset stamp mismatched by %lld s", offset_epoch - epoch);

  long long frac_epoch = 0;
  CHECK(budget_parse_iso("2026-08-04T18:00:00.123456Z", &frac_epoch) == 0, "fraction rejected");
  CHECK(frac_epoch == epoch, "fractional seconds shifted the instant");

  CHECK(budget_parse_iso("", &epoch) != 0, "empty stamp accepted");
  CHECK(budget_parse_iso("2026-13-04T18:00:00Z", &epoch) != 0, "month 13 accepted");
  CHECK(budget_parse_iso("2026-08-04T18:00:00Zjunk", &epoch) != 0, "trailing junk accepted");
}

/* ------------------------------------------------------ ledger behaviour */

static void test_record_and_persist(const char *tmp_path) {
  printf("record start/stop and persistence\n");
  BudgetLedger led;
  memset(&led, 0, sizeof led);

  CHECK(budget_record_start(&led, 4.0, "2026-08-04T18:00:00Z") == 0, "record_start failed");
  CHECK(led.count == 1, "expected 1 interval, got %zu", led.count);
  CHECK(led.items[0].stopped_at[0] == '\0', "new interval should be open");
  CHECK(led.items[0].id[0] != '\0', "interval should get an id");

  /* An open interval bills up to `now`. */
  CHECK_NEAR(budget_used_ocpu_for_la_day(&led, "2026-08-04", "2026-08-04T20:30:00Z"), 10.0,
             "open interval usage");

  /* A start while another is open closes the stale one at the same instant. */
  CHECK(budget_record_start(&led, 4.0, "2026-08-04T19:00:00Z") == 0, "second start failed");
  CHECK(led.count == 2, "expected 2 intervals, got %zu", led.count);
  CHECK(strcmp(led.items[0].stopped_at, "2026-08-04T19:00:00Z") == 0,
        "stale interval was not closed at the new start");

  CHECK(budget_record_stop(&led, "2026-08-04T20:00:00Z") == 1, "expected 1 interval closed");
  CHECK(budget_record_stop(&led, "2026-08-04T21:00:00Z") == 0, "second stop should close none");
  CHECK_NEAR(budget_used_ocpu_for_la_day(&led, "2026-08-04", NULL), 8.0, "closed usage");

  CHECK(budget_save(&led, tmp_path) == 0, "save failed");
  BudgetLedger reloaded;
  memset(&reloaded, 0, sizeof reloaded);
  CHECK(budget_load(&reloaded, tmp_path) == 0, "load failed");
  CHECK(reloaded.count == led.count, "reloaded %zu intervals, wrote %zu", reloaded.count,
        led.count);
  CHECK_NEAR(budget_used_ocpu_for_la_day(&reloaded, "2026-08-04", NULL), 8.0, "reloaded usage");
  CHECK(strcmp(reloaded.items[0].id, led.items[0].id) == 0, "ids did not survive the round trip");

  budget_free(&reloaded);
  budget_free(&led);

  /* An open interval must round-trip as open (stopped_at: null). */
  BudgetLedger open_led;
  memset(&open_led, 0, sizeof open_led);
  CHECK(budget_record_start(&open_led, 2.0, "2026-08-04T19:00:00Z") == 0, "start failed");
  CHECK(budget_save(&open_led, tmp_path) == 0, "save failed");
  BudgetLedger open_reloaded;
  memset(&open_reloaded, 0, sizeof open_reloaded);
  CHECK(budget_load(&open_reloaded, tmp_path) == 0, "load failed");
  CHECK(open_reloaded.count == 1 && open_reloaded.items[0].stopped_at[0] == '\0',
        "open interval did not survive as open");
  /* open_interval_fixed_now fixture: 2 OCPUs, 19:00Z open, now 21:30Z. */
  CHECK_NEAR(budget_used_ocpu_for_la_day(&open_reloaded, "2026-08-04", "2026-08-04T21:30:00Z"),
             5.0, "open interval usage after reload");
  budget_free(&open_reloaded);
  budget_free(&open_led);

  /* Missing files are a cold start, not an error. */
  BudgetLedger missing;
  memset(&missing, 0, sizeof missing);
  CHECK(budget_load(&missing, "does-not-exist-97531.json") == 0, "missing file should load empty");
  CHECK(missing.count == 0, "missing file produced %zu intervals", missing.count);
  budget_free(&missing);
}

static void test_limits(void) {
  printf("cap enforcement\n");
  CHECK(!budget_exhausted(44.9, BUDGET_DAILY_LIMIT_OCPU_HOURS), "44.9 should not be exhausted");
  CHECK(budget_exhausted(45.0, BUDGET_DAILY_LIMIT_OCPU_HOURS), "45.0 should be exhausted");
  CHECK(budget_exhausted(60.0, BUDGET_DAILY_LIMIT_OCPU_HOURS), "60.0 should be exhausted");
  CHECK_NEAR(budget_remaining_ocpu(11.0, BUDGET_DAILY_LIMIT_OCPU_HOURS), 34.0, "remaining");
  CHECK_NEAR(budget_remaining_ocpu(99.0, BUDGET_DAILY_LIMIT_OCPU_HOURS), 0.0, "remaining floor");

  /* 4 OCPUs against 45 OCPU-hours is 11.25 h of wall clock per the spec. */
  BudgetLedger led;
  memset(&led, 0, sizeof led);
  ledger_append(&led, "2026-08-04T08:00:00Z", "2026-08-04T19:15:00Z", 4.0);
  double used = budget_used_ocpu_for_la_day(&led, "2026-08-04", NULL);
  CHECK_NEAR(used, 45.0, "11.25 h at 4 OCPUs");
  CHECK(budget_exhausted(used, BUDGET_DAILY_LIMIT_OCPU_HOURS), "11.25 h at 4 OCPUs should cap out");
  budget_free(&led);

  BudgetLedger bad;
  memset(&bad, 0, sizeof bad);
  CHECK(budget_used_ocpu_for_la_day(&bad, "nonsense", NULL) < 0.0,
        "an unusable date should report -1, not 0 used");
  budget_free(&bad);
}

static void test_utc_day_bounds(void) {
  printf("UTC day bounds\n");
  long long start = 0, end = 0;
  CHECK(budget_utc_day_bounds("2026-08-31", &start, &end) == 0, "utc bounds failed");
  char buf[32];
  budget_format_iso(start, buf, sizeof buf);
  CHECK(strcmp(buf, "2026-08-31T00:00:00Z") == 0, "UTC day start was %s", buf);
  budget_format_iso(end, buf, sizeof buf);
  CHECK(strcmp(buf, "2026-09-01T00:00:00Z") == 0, "UTC day end was %s", buf);
  CHECK(end - start == 86400, "UTC day length was %lld", end - start);

  char day[16];
  CHECK(budget_utc_date_for("2026-08-31T23:59:59Z", day, sizeof day) == 0, "utc_date_for failed");
  CHECK(strcmp(day, "2026-08-31") == 0, "23:59:59Z mapped to %s", day);
  CHECK(budget_utc_date_for("2026-09-01T00:00:00Z", day, sizeof day) == 0, "utc_date_for failed");
  CHECK(strcmp(day, "2026-09-01") == 0, "00:00:00Z mapped to %s", day);

  BudgetLedger spans;
  memset(&spans, 0, sizeof spans);
  ledger_append(&spans, "2026-08-31T22:00:00Z", "2026-09-01T02:00:00Z", 4.0);
  CHECK_NEAR(budget_uptime_hours_for_utc_day(&spans, "2026-08-31", NULL), 2.0,
             "utc midnight 08-31 uptime");
  CHECK_NEAR(budget_used_ocpu_for_utc_day(&spans, "2026-08-31", NULL), 8.0,
             "utc midnight 08-31 ocpu-hours");
  CHECK_NEAR(budget_uptime_hours_for_utc_day(&spans, "2026-09-01", NULL), 2.0,
             "utc midnight 09-01 uptime");
  CHECK_NEAR(budget_used_ocpu_for_utc_day(&spans, "2026-09-01", NULL), 8.0,
             "utc midnight 09-01 ocpu-hours");
  budget_free(&spans);
}

int main(int argc, char **argv) {
  const char *fixture_path = argc > 1 ? argv[1] : DEFAULT_FIXTURE;
  const char *tmp_path = argc > 2 ? argv[2] : "build/test_ledger.json";

  test_fixture_scenarios(fixture_path);
  test_headline_numbers_hardcoded();
  test_la_day_bounds();
  test_la_date_for();
  test_iso_round_trip();
  test_record_and_persist(tmp_path);
  test_limits();
  test_utc_day_bounds();

  printf("%s: %d checks, %d failures\n", failures == 0 ? "PASS" : "FAIL", checks, failures);
  return failures == 0 ? 0 : 1;
}
