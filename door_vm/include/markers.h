/* markers.h - shared named pins for the player map (door :80 /markers).
 *
 * Stored at /var/lib/mccontrol/player-map-markers.json — outside the tile
 * tree so pull_player_map.sh cannot wipe them. Anyone allowlisted may add,
 * rename, or delete. No owner field.
 */
#ifndef VM2_MARKERS_H
#define VM2_MARKERS_H

#include <stddef.h>

#define MARKERS_MAX 200
#define MARKER_ID_LEN 16
/* dim: overworld|nether|end, or ns/path extras (63-byte max). Buffer is 64 + NUL. */
#define MARKER_DIM_BYTES 64
#define MARKER_DIM_NS_MAX 32
#define MARKER_DIM_PATH_MAX 48
#define MARKER_DIM_TOTAL_MAX 63
#define MARKER_NAME_BYTES 192
#define MARKER_NAME_CODEPOINTS 48
#define MARKER_COORD_ABS_MAX 30000000.0
#define MARKERS_BODY_MAX 8192
#define MARKERS_DEFAULT_PATH "/var/lib/mccontrol/player-map-markers.json"

typedef struct {
  char id[MARKER_ID_LEN + 1];
  char dim[MARKER_DIM_BYTES + 1];
  double x;
  double z;
  char name[MARKER_NAME_BYTES + 1];
} Marker;

typedef struct {
  Marker items[MARKERS_MAX];
  size_t count;
} MarkerList;

void markers_clear(MarkerList *list);

/* Missing file → empty list and 0. Malformed JSON → -1. Invalid records in a
 * well-formed file are skipped. */
int markers_load(MarkerList *list, const char *path);

/* Atomic write (temp + rename) via jsonmin. */
int markers_save(const MarkerList *list, const char *path);

/* Heap JSON matching the on-disk object. Caller frees. */
char *markers_to_json(const MarkerList *list);

/* Mutators set *http_status (200/201/400/404/409). Return 0 on 2xx. */
int markers_add(MarkerList *list, const char *dim, double x, double z, const char *name,
                char *out_id, int *http_status);
int markers_rename(MarkerList *list, const char *id, const char *name, int *http_status);
int markers_delete(MarkerList *list, const char *id, int *http_status);

/* POST /markers body. Malformed JSON or unknown op → 400. */
int markers_apply_post(MarkerList *list, const char *json, int *http_status);

#endif /* VM2_MARKERS_H */
