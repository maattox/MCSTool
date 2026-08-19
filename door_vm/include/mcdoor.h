/* mcdoor.h - VM2 doorbell: Minecraft status MOTD/icon and login kick.
 *
 * When the reserved public IP sits on VM2, mcdoor binds the play port and
 * answers server-list pings with state-aware MOTD/favicon while rejecting
 * login attempts with a clear retry message.
 */
#ifndef VM2_MCDOOR_H
#define VM2_MCDOOR_H

#include <stdint.h>

#include "state.h"

#define MCDOOR_DEFAULT_PORT 25565
#define MCDOOR_DEFAULT_PROTOCOL 763
#define MCDOOR_DEFAULT_VERSION "1.20.1"

typedef void (*McdoorWakeCallback)(void *userdata);

typedef struct {
  const char *bind_host;
  uint16_t port;
  const ControlState *state;
  const char *icon_idle_b64;
  const char *icon_starting_b64;
  const char *icon_exhausted_b64;
  const char *version_name;
  int protocol;
  McdoorWakeCallback on_wake_request;
  void *wake_userdata;
} McdoorConfig;

/* Fill `out` with the MOTD text for `state->door`. Idle MOTD includes remaining
 * daily OCPU-h on scarce shapes (4 OCPU); always-on-capable shapes (2 OCPU)
 * omit that scare figure. Exhausted / spend-brake copy is unchanged. */
void mcdoor_build_motd(const ControlState *state, char *out, size_t out_cap);

/* Kick reason shown on login (next_state=2). */
void mcdoor_build_kick_reason(const ControlState *state, char *out, size_t out_cap);

/* Favicon base64 (without the data: prefix) for the current door state. */
const char *mcdoor_icon_for_state(DoorState door, const McdoorConfig *cfg);

/* Handle one accepted TCP connection (status and/or login kick). */
int mcdoor_handle_connection(int fd, const McdoorConfig *cfg);

/* Blocking accept loop on `cfg->bind_host`:`cfg->port`. Returns 0 or -1. */
int mcdoor_serve(const McdoorConfig *cfg);

/* Load idle/starting/exhausted PNGs from `icons_dir` and base64-encode them.
 * Strings are stored in static buffers inside mcdoor.c; valid until reload. */
int mcdoor_load_icons(const char *icons_dir);

#endif /* VM2_MCDOOR_H */
