/* mcdoor_test_main.c - Runnable doorbell server for manual/integration tests.
 *
 * Usage:
 *   ./build/mcdoor_test [port] [state.json]
 *
 * Defaults: port 25565, in-memory DOOR_IDLE state, icons from assets/icons.
 */
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include "mcdoor.h"
#include "state.h"

int main(int argc, char **argv) {
  ControlState state;
  state_default(&state);

  const char *state_path = NULL;
  uint16_t port = MCDOOR_DEFAULT_PORT;

  if (argc > 1) {
    port = (uint16_t)atoi(argv[1]);
  }
  if (argc > 2) {
    state_path = argv[2];
  }

  if (state_path != NULL && state_load(&state, state_path) != 0) {
    fprintf(stderr, "mcdoor_test: failed to load state from %s\n", state_path);
    return 1;
  }

  const char *icons_dir = "assets/icons";
  if (mcdoor_load_icons(icons_dir) != 0) {
    fprintf(stderr, "mcdoor_test: warning: could not load icons from %s (continuing without favicon)\n",
            icons_dir);
  }

  McdoorConfig cfg = {
      .bind_host = "0.0.0.0",
      .port = port,
      .state = &state,
      .icon_idle_b64 = NULL,
      .icon_starting_b64 = NULL,
      .icon_exhausted_b64 = NULL,
      .version_name = MCDOOR_DEFAULT_VERSION,
      .protocol = MCDOOR_DEFAULT_PROTOCOL,
  };

  printf("mcdoor_test starting (requested port %u)\n", (unsigned)port);
  if (mcdoor_serve(&cfg) != 0) {
    fprintf(stderr, "mcdoor_test: serve failed\n");
    return 1;
  }
  return 0;
}
