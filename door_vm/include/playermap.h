/* playermap.h - static HTTP for the player 2D map (door :80).
 *
 * GET/HEAD only. No /api/ routes. No TLS. Serves files under a map root with
 * sendfile (or a chunked fallback). Separate from httpmini on :8080.
 */
#ifndef VM2_PLAYERMAP_H
#define VM2_PLAYERMAP_H

#include <stdint.h>

typedef struct {
  const char *bind_host;
  uint16_t port;
  const char *map_root;
} PlayerMapConfig;

/* Blocking accept loop. Returns 0 or -1. If `port` is 80 and bind fails,
 * retries 8081 and logs. */
int playermap_serve(const PlayerMapConfig *cfg);

#endif /* VM2_PLAYERMAP_H */
