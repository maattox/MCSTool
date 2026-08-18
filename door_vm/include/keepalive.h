/* keepalive.h - scheduled CPU bursts to avoid Always Free Micro reclaim.
 *
 * Runs on a background thread at low priority. Bursts pause immediately when
 * mcdoor or HTTP report inbound activity and resume only after idle.
 */
#ifndef VM2_KEEPALIVE_H
#define VM2_KEEPALIVE_H

#include <stddef.h>

typedef struct ControlContext ControlContext;

typedef struct {
  ControlContext *control;
  int interval_sec; /* wall seconds between burst starts (default 7200) */
  int burst_sec;    /* target active burst length (default 750) */
} KeepaliveConfig;

/* Call when a user-facing accept/handshake begins / ends (mcdoor, HTTP). */
void keepalive_activity_begin(void);
void keepalive_activity_end(void);

/* Starts the detached keepalive thread. Returns 0 or -1. */
int keepalive_start(const KeepaliveConfig *cfg);

/* ISO-8601 UTC of the next scheduled burst, or empty if disabled/unscheduled. */
int keepalive_next_at_iso(char *out, size_t cap);

#endif /* VM2_KEEPALIVE_H */
