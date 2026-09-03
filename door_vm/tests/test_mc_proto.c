/* test_mc_proto.c - VarInt/status JSON helpers and mcdoor MOTD strings. */
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include "mc_proto.h"
#include "mcdoor.h"
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
    }                                             \
  } while (0)

static void test_varint_roundtrip(void) {
  printf("varint roundtrip\n");
  const uint32_t values[] = {0, 1, 127, 128, 255, 300, 100000};
  for (size_t i = 0; i < sizeof values / sizeof values[0]; i++) {
    uint8_t buf[8];
    int n = mc_varint_encode(values[i], buf, sizeof buf);
    CHECK(n > 0, "encode %u failed", values[i]);
    McBuf b = {buf, (size_t)n, 0};
    uint32_t decoded = 0;
    CHECK(mc_varint_decode(&b, &decoded) > 0 && decoded == values[i], "decode %u", values[i]);
    CHECK(b.pos == (size_t)n, "trailing bytes for %u", values[i]);
  }
}

static void test_build_status_json(void) {
  printf("status response JSON\n");
  char *raw = mc_build_status_response_json("Test MOTD", 2, 20, "1.20.1", 763, NULL);
  CHECK(raw != NULL, "build_status_response_json returned NULL");
  CHECK(strstr(raw, "\"online\":2") != NULL, "missing online count: %s", raw != NULL ? raw : "");
  CHECK(strstr(raw, "\"protocol\":763") != NULL, "missing protocol");
  CHECK(strstr(raw, "Test MOTD") != NULL, "missing motd text");
  free(raw);

  raw = mc_build_status_response_json("hi", 0, 20, "1.20.1", 763, "abc123");
  CHECK(raw != NULL, "favicon json failed");
  CHECK(strstr(raw, "data:image/png;base64,abc123") != NULL, "missing favicon prefix");
  free(raw);
}

static void test_motd_by_door_state(void) {
  printf("mcdoor MOTD by door state\n");
  ControlState state;
  state_default(&state);
  char motd[512];

  state.door = DOOR_IDLE;
  state.used_ocpu_hours = 8.0;
  state.daily_limit_ocpu_hours = 48.0;
  state.ocpus = 4.0;
  mcdoor_build_motd(&state, motd, sizeof motd);
  CHECK(strstr(motd, "~10.0h remaining today") != NULL,
        "idle MOTD missing wall-clock remaining: %s", motd);
  CHECK(strstr(motd, "OCPU-h") == NULL, "idle MOTD must not say OCPU-h: %s", motd);

  state.ocpus = 2.0;
  mcdoor_build_motd(&state, motd, sizeof motd);
  CHECK(strstr(motd, "remaining today") == NULL,
        "2-OCPU idle MOTD must not nag remaining hours: %s", motd);
  CHECK(strstr(motd, "Connect to wake") != NULL, "2-OCPU idle MOTD missing wake hint: %s",
        motd);

  state.ocpus = 0.0;
  mcdoor_build_motd(&state, motd, sizeof motd);
  CHECK(strstr(motd, "remaining today") == NULL, "0-OCPU idle MOTD must omit remaining: %s",
        motd);

  state.door = DOOR_STARTING;
  mcdoor_build_motd(&state, motd, sizeof motd);
  CHECK(strstr(motd, "starting") != NULL, "starting MOTD missing hint: %s", motd);
  CHECK(strstr(motd, "3-5") != NULL, "starting MOTD missing retry window: %s", motd);

  state.door = DOOR_BUDGET_EXHAUSTED;
  mcdoor_build_motd(&state, motd, sizeof motd);
  CHECK(strstr(motd, "DAILY BUDGET FULFILLED") != NULL, "exhausted MOTD wrong: %s", motd);
  CHECK(strstr(motd, "COME BACK") != NULL, "exhausted MOTD missing reset hint: %s", motd);

  state.door = DOOR_SPEND_BRAKE;
  mcdoor_build_motd(&state, motd, sizeof motd);
  CHECK(strstr(motd, "MONTHLY SPEND BRAKE FIRED") != NULL, "spend-brake MOTD wrong: %s",
        motd);
  CHECK(strstr(motd, "Manager") != NULL, "spend-brake MOTD missing Manager hint: %s", motd);
  CHECK(strstr(motd, "DAILY") == NULL, "spend-brake MOTD must be distinct from daily: %s",
        motd);
}

static void test_kick_reason(void) {
  printf("mcdoor kick reasons\n");
  ControlState state;
  state_default(&state);
  char reason[512];

  state.door = DOOR_STARTING;
  mcdoor_build_kick_reason(&state, reason, sizeof reason);
  CHECK(strstr(reason, "starting") != NULL, "starting kick: %s", reason);

  state.door = DOOR_BUDGET_EXHAUSTED;
  mcdoor_build_kick_reason(&state, reason, sizeof reason);
  CHECK(strstr(reason, "DAILY BUDGET FULFILLED") != NULL, "exhausted kick: %s", reason);

  state.door = DOOR_SPEND_BRAKE;
  mcdoor_build_kick_reason(&state, reason, sizeof reason);
  CHECK(strstr(reason, "MONTHLY SPEND BRAKE FIRED") != NULL, "spend-brake kick: %s",
        reason);
  CHECK(strstr(reason, "DAILY") == NULL, "spend-brake kick must be distinct from daily: %s",
        reason);
}

static void test_status_json_with_icon(void) {
  printf("status JSON with icon is valid UTF-8\n");
  if (mcdoor_load_icons("assets/icons") != 0) {
    printf("  (skip: icons not found)\n");
    return;
  }
  ControlState state;
  state_default(&state);
  char motd[512];
  mcdoor_build_motd(&state, motd, sizeof motd);
  const char *icon = mcdoor_icon_for_state(state.door, NULL);
  char *json = mc_build_status_response_json(motd, 0, 20, "1.20.1", 763, icon);
  CHECK(json != NULL, "json build failed");
  if (json != NULL) {
    size_t len = strlen(json);
    CHECK(len > 300, "json too short: %zu", len);
    for (size_t i = 0; i < len; i++) {
      if (json[i] == '\0') {
        CHECK(0, "embedded null at %zu", i);
        break;
      }
      CHECK((unsigned char)json[i] < 0x80 || (unsigned char)json[i] >= 0xc0,
            "invalid utf-8 byte 0x%02x at %zu", (unsigned char)json[i], i);
    }
    CHECK(strstr(json, "data:image/png;base64,") != NULL, "missing favicon");
    free(json);
  }
}

int main(void) {
  test_varint_roundtrip();
  test_build_status_json();
  test_motd_by_door_state();
  test_kick_reason();
  test_status_json_with_icon();
  printf("%s: %d checks, %d failures\n", failures == 0 ? "PASS" : "FAIL", checks, failures);
  return failures == 0 ? 0 : 1;
}
