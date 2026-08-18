/* test_state.c - VM2 control state persistence. */
#include <stdio.h>
#include <string.h>

#include "budget.h"
#include "state.h"

static int failures = 0;
static int checks = 0;

#define CHECK(cond, ...)                            \
  do {                                              \
    checks++;                                       \
    if (!(cond)) {                                  \
      failures++;                                   \
      printf("  FAIL %s:%d: ", __FILE__, __LINE__); \
      printf(__VA_ARGS__);                          \
      printf("\n");                                 \
    }                                               \
  } while (0)

static void test_door_names(void) {
  printf("door state names\n");
  CHECK(strcmp(state_door_name(DOOR_IDLE), "DOOR_IDLE") == 0, "DOOR_IDLE name");
  CHECK(strcmp(state_door_name(DOOR_STARTING), "STARTING") == 0, "STARTING name");
  CHECK(strcmp(state_door_name(DOOR_PLAYABLE), "PLAYABLE") == 0, "PLAYABLE name");
  CHECK(strcmp(state_door_name(DOOR_BUDGET_EXHAUSTED), "BUDGET_EXHAUSTED") == 0,
        "BUDGET_EXHAUSTED name");
  CHECK(strcmp(state_door_name(DOOR_SPEND_BRAKE), "SPEND_BRAKE") == 0,
        "SPEND_BRAKE name");
  CHECK(strcmp(state_door_name(DOOR_DEGRADED), "DEGRADED") == 0, "DEGRADED name");

  DoorState door = DOOR_DEGRADED;
  CHECK(state_door_from_name("PLAYABLE", &door) == 0 && door == DOOR_PLAYABLE,
        "PLAYABLE did not parse back");
  CHECK(state_door_from_name("SPEND_BRAKE", &door) == 0 && door == DOOR_SPEND_BRAKE,
        "SPEND_BRAKE did not parse back");
  CHECK(state_door_from_name("DOOR_SPEND_BRAKE", &door) == 0 &&
            door == DOOR_SPEND_BRAKE,
        "DOOR_SPEND_BRAKE alias did not parse");
  CHECK(state_door_from_name("NOPE", &door) != 0, "unknown door name accepted");
}

static void test_defaults(void) {
  printf("defaults\n");
  ControlState state;
  state_default(&state);
  CHECK(state.door == DOOR_IDLE, "fresh VM2 should start idle");
  CHECK(state.daily_limit_ocpu_hours == BUDGET_DAILY_LIMIT_OCPU_HOURS, "default cap is 45");
  CHECK(state.idle_timeout_minutes == 15, "default idle window is 15 minutes");
  CHECK(state.keepalive_enabled == 1, "keepalive defaults on");
  CHECK(state.session_started_at[0] == '\0', "no session on a fresh state");
}

static void test_round_trip(const char *path) {
  printf("save/load round trip (%s)\n", path);
  ControlState state;
  state_default(&state);
  state.door = DOOR_BUDGET_EXHAUSTED;
  state.used_ocpu_hours = 45.0;
  state.ocpus = 4.0;
  state.idle_timeout_minutes = 20;
  state.keepalive_enabled = 0;
  snprintf(state.updated_at, sizeof state.updated_at, "2026-08-04T20:00:00Z");
  snprintf(state.la_day, sizeof state.la_day, "2026-08-04");
  snprintf(state.hard_stop_deadline, sizeof state.hard_stop_deadline, "2026-08-04T19:15:00Z");
  snprintf(state.last_error, sizeof state.last_error, "oci stop returned 1: \"timeout\"");

  CHECK(state_save(&state, path) == 0, "save failed");

  ControlState loaded;
  CHECK(state_load(&loaded, path) == 0, "load failed");
  CHECK(loaded.door == DOOR_BUDGET_EXHAUSTED, "door state changed");
  CHECK(loaded.used_ocpu_hours == 45.0, "used_ocpu_hours changed");
  CHECK(loaded.idle_timeout_minutes == 20, "idle_timeout_minutes changed");
  CHECK(loaded.keepalive_enabled == 0, "keepalive_enabled changed");
  CHECK(strcmp(loaded.la_day, "2026-08-04") == 0, "la_day changed");
  CHECK(strcmp(loaded.hard_stop_deadline, "2026-08-04T19:15:00Z") == 0, "deadline changed");
  CHECK(strcmp(loaded.last_error, "oci stop returned 1: \"timeout\"") == 0,
        "quoted error text did not survive escaping: %s", loaded.last_error);
  CHECK(loaded.session_started_at[0] == '\0', "empty session should reload empty");

  ControlState missing;
  CHECK(state_load(&missing, "does-not-exist-97531.json") == 0, "missing file should be defaults");
  CHECK(missing.door == DOOR_IDLE, "missing file should default to idle");
}

int main(int argc, char **argv) {
  const char *path = argc > 1 ? argv[1] : "build/test_state.json";
  test_door_names();
  test_defaults();
  test_round_trip(path);
  printf("%s: %d checks, %d failures\n", failures == 0 ? "PASS" : "FAIL", checks, failures);
  return failures == 0 ? 0 : 1;
}
