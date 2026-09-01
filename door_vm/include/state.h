/* state.h - VM2 control-plane state, persisted as JSON.
 *
 * This is the small record the doorbell, the web UI, and the OCI wrappers all
 * read: which play/IP state we are in, the budget knobs, and the current
 * session's hard-stop deadline. Timestamps are UTC ISO-8601 Z; empty string
 * means "not set". The door states mirror the design spec's state table.
 */
#ifndef VM2_STATE_H
#define VM2_STATE_H

#include <stddef.h>

typedef enum {
  DOOR_IDLE = 0,        /* reserved IP on VM2, mcdoor answers, VM1 stopped */
  DOOR_STARTING,        /* wake in flight, Forge not accepting yet */
  DOOR_PLAYABLE,        /* reserved IP on VM1, players connect directly */
  DOOR_BUDGET_EXHAUSTED, /* daily cap hit; no wake until UTC midnight */
  DOOR_SPEND_BRAKE,     /* $1 monthly lock flag present; no START VM1 */
  DOOR_DEGRADED         /* VM2 lost track of reality; manual intervention */
} DoorState;

typedef struct {
  DoorState door;
  char updated_at[32];           /* when this record was last written */
  double daily_limit_ocpu_hours; /* Phase A: 45 */
  double ocpus;                  /* VM1 shape OCPU count used for billing */
  int idle_timeout_minutes;      /* empty-server window enforced by VM1 */
  char la_day[16];               /* UTC date the cached usage figures cover */
  double used_ocpu_hours;        /* cached usage for la_day */
  char session_started_at[32];   /* open session start, empty if none */
  char hard_stop_deadline[32];   /* T-0 sent to VM1 for chat warnings */
  int keepalive_enabled;
  char last_keepalive_at[32];
  char last_error[192]; /* surfaced in the UI; empty when healthy */
} ControlState;

/* Phase A defaults from the design spec. */
void state_default(ControlState *state);

/* Reads `path`. A missing file yields defaults and returns 0, so a fresh VM2
 * boots into DOOR_IDLE. Returns -1 on malformed JSON. Unknown fields are
 * ignored and missing fields keep their default. */
int state_load(ControlState *state, const char *path);

/* Writes `path` via temp file + rename. Returns 0 or -1. */
int state_save(const ControlState *state, const char *path);

/* Spec-facing names: "DOOR_IDLE", "STARTING", "PLAYABLE", "BUDGET_EXHAUSTED",
 * "SPEND_BRAKE", "DEGRADED". These are what land in JSON and in the API. */
const char *state_door_name(DoorState door);
int state_door_from_name(const char *name, DoorState *out);

#endif /* VM2_STATE_H */
