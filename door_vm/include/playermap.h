/* playermap.h - static HTTP for the player 2D map (door :80).
 *
 * GET/HEAD files under the map root (sendfile). GET/POST /markers for shared
 * pins (file lives under /var/lib/mccontrol/, not the tile tree). /api* is
 * 404. No TLS. Separate from httpmini on :8080.
 */
#ifndef VM2_PLAYERMAP_H
#define VM2_PLAYERMAP_H

#include <stdint.h>

typedef struct {
  const char *bind_host;
  uint16_t port;
  const char *map_root;
  const char *markers_path; /* NULL → MARKERS_DEFAULT_PATH */
} PlayerMapConfig;

/* Blocking accept loop. Returns 0 or -1. If `port` is 80 and bind fails,
 * retries 8081 and logs. */
int playermap_serve(const PlayerMapConfig *cfg);

#endif /* VM2_PLAYERMAP_H */
