#include "state.h"

#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include "budget.h"
#include "jsonmin.h"

static const struct {
  DoorState door;
  const char *name;
} DOOR_NAMES[] = {
    {DOOR_IDLE, "DOOR_IDLE"},
    {DOOR_STARTING, "STARTING"},
    {DOOR_PLAYABLE, "PLAYABLE"},
    {DOOR_BUDGET_EXHAUSTED, "BUDGET_EXHAUSTED"},
    {DOOR_SPEND_BRAKE, "SPEND_BRAKE"},
    {DOOR_SPEND_BRAKE, "DOOR_SPEND_BRAKE"}, /* parse alias; name() uses first */
    {DOOR_DEGRADED, "DEGRADED"},
};

const char *state_door_name(DoorState door) {
  for (size_t i = 0; i < sizeof DOOR_NAMES / sizeof DOOR_NAMES[0]; i++) {
    if (DOOR_NAMES[i].door == door) {
      return DOOR_NAMES[i].name;
    }
  }
  return "DEGRADED";
}

int state_door_from_name(const char *name, DoorState *out) {
  if (name == NULL || out == NULL) {
    return -1;
  }
  for (size_t i = 0; i < sizeof DOOR_NAMES / sizeof DOOR_NAMES[0]; i++) {
    if (strcmp(DOOR_NAMES[i].name, name) == 0) {
      *out = DOOR_NAMES[i].door;
      return 0;
    }
  }
  return -1;
}

void state_default(ControlState *state) {
  if (state == NULL) {
    return;
  }
  memset(state, 0, sizeof *state);
  state->door = DOOR_IDLE;
  state->daily_limit_ocpu_hours = BUDGET_DAILY_LIMIT_OCPU_HOURS;
  state->ocpus = 4.0;
  state->idle_timeout_minutes = 15;
  state->keepalive_enabled = 1;
}

static void load_string(char *dst, size_t size, const JsonValue *object, const char *key) {
  const char *text = json_as_string(json_object_get(object, key), NULL);
  if (text == NULL) {
    return; /* missing or null: keep the default */
  }
  size_t len = strlen(text);
  if (len >= size) {
    len = size - 1;
  }
  memcpy(dst, text, len);
  dst[len] = '\0';
}

int state_load(ControlState *state, const char *path) {
  if (state == NULL || path == NULL) {
    return -1;
  }
  state_default(state);

  FILE *probe = fopen(path, "rb");
  if (probe == NULL) {
    return 0; /* first boot */
  }
  fclose(probe);

  JsonValue *root = json_parse_file(path);
  if (root == NULL || json_type(root) != JSON_OBJECT) {
    json_free(root);
    return -1;
  }

  const char *door = json_as_string(json_object_get(root, "door_state"), NULL);
  if (door != NULL && state_door_from_name(door, &state->door) != 0) {
    json_free(root);
    return -1;
  }
  state->daily_limit_ocpu_hours = json_as_number(json_object_get(root, "daily_limit_ocpu_hours"),
                                                 state->daily_limit_ocpu_hours);
  state->ocpus = json_as_number(json_object_get(root, "ocpus"), state->ocpus);
  state->idle_timeout_minutes =
      (int)json_as_number(json_object_get(root, "idle_timeout_minutes"),
                          state->idle_timeout_minutes);
  state->used_ocpu_hours =
      json_as_number(json_object_get(root, "used_ocpu_hours"), state->used_ocpu_hours);
  state->keepalive_enabled =
      json_as_bool(json_object_get(root, "keepalive_enabled"), state->keepalive_enabled);

  load_string(state->updated_at, sizeof state->updated_at, root, "updated_at");
  load_string(state->la_day, sizeof state->la_day, root, "la_day");
  load_string(state->session_started_at, sizeof state->session_started_at, root,
              "session_started_at");
  load_string(state->hard_stop_deadline, sizeof state->hard_stop_deadline, root,
              "hard_stop_deadline");
  load_string(state->last_keepalive_at, sizeof state->last_keepalive_at, root,
              "last_keepalive_at");
  load_string(state->last_error, sizeof state->last_error, root, "last_error");

  json_free(root);
  return 0;
}

/* Optional timestamps round-trip as null rather than "" so the JSON reads the
 * same way the Python side writes it. */
static void put_optional(JsonBuf *buf, const char *key, const char *value, int last) {
  json_buf_raw(buf, "  ");
  json_buf_string(buf, key);
  json_buf_raw(buf, ": ");
  if (value[0] == '\0') {
    json_buf_raw(buf, "null");
  } else {
    json_buf_string(buf, value);
  }
  json_buf_raw(buf, last ? "\n" : ",\n");
}

int state_save(const ControlState *state, const char *path) {
  if (state == NULL || path == NULL) {
    return -1;
  }
  JsonBuf buf;
  json_buf_init(&buf);
  json_buf_raw(&buf, "{\n  \"version\": 1,\n");
  json_buf_raw(&buf, "  \"door_state\": ");
  json_buf_string(&buf, state_door_name(state->door));
  json_buf_raw(&buf, ",\n");
  put_optional(&buf, "updated_at", state->updated_at, 0);
  json_buf_raw(&buf, "  \"daily_limit_ocpu_hours\": ");
  json_buf_number(&buf, state->daily_limit_ocpu_hours);
  json_buf_raw(&buf, ",\n  \"ocpus\": ");
  json_buf_number(&buf, state->ocpus);
  json_buf_fmt(&buf, ",\n  \"idle_timeout_minutes\": %d,\n", state->idle_timeout_minutes);
  put_optional(&buf, "la_day", state->la_day, 0);
  json_buf_raw(&buf, "  \"used_ocpu_hours\": ");
  json_buf_number(&buf, state->used_ocpu_hours);
  json_buf_raw(&buf, ",\n");
  put_optional(&buf, "session_started_at", state->session_started_at, 0);
  put_optional(&buf, "hard_stop_deadline", state->hard_stop_deadline, 0);
  json_buf_fmt(&buf, "  \"keepalive_enabled\": %s,\n",
               state->keepalive_enabled ? "true" : "false");
  put_optional(&buf, "last_keepalive_at", state->last_keepalive_at, 0);
  json_buf_raw(&buf, "  \"last_error\": ");
  json_buf_string(&buf, state->last_error);
  json_buf_raw(&buf, "\n}\n");

  int rc = buf.error ? -1 : json_write_file(path, buf.data);
  json_buf_free(&buf);
  return rc;
}
