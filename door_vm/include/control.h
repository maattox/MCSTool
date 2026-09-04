/* control.h - VM2 control plane: wake/stop orchestration and shared state.
 *
 * mccontrol and mcdoor share ControlState + BudgetLedger on disk. All
 * mutations go through this module so budget figures, door transitions, and
 * OCI script invocation stay consistent.
 */
#ifndef VM2_CONTROL_H
#define VM2_CONTROL_H

#include <stddef.h>
#include <stdint.h>

#include "budget.h"
#include "state.h"

typedef struct ControlContext ControlContext;

typedef struct {
  char state_path[512];
  char ledger_path[512];
  char oci_dir[512];
  char web_root[512];
  char icons_dir[512];
  char oci_env_file[512];
  char vm1_private_ip[64];
  char bind_host[64];
  uint16_t http_port;
  uint16_t mc_port;
  double daily_ocpu_limit;
  double soft_ocpu_cap; /* monthly soft brake from Object Storage budget SoT */
  double ocpus;
  int enable_mcdoor;
  int enable_http;
  int keepalive_enabled;
  int keepalive_interval_sec;
  int keepalive_burst_sec;
  /* Object Storage wake-gate (Phase 3): door reads shared ledger/budget. */
  int object_storage_enabled;
  char os_ledger_cache_path[512];
  char os_budget_cache_path[512];
  char os_spend_brake_cache_path[512];
} ControlConfig;

/* Load `path` into `out`. Missing keys keep built-in defaults. Returns 0 or -1. */
int control_load_config(const char *path, ControlConfig *out);

/* Opens state + ledger from config paths. Returns NULL on fatal error. */
ControlContext *control_init(const ControlConfig *cfg);

void control_free(ControlContext *ctx);

/* Thread-safe read-only pointer; valid until control_free. Brief lock. */
const ControlState *control_state(const ControlContext *ctx);

/* Recompute used_ocpu_hours / la_day from the ledger and persist state. */
int control_refresh_budget(ControlContext *ctx);

/* Object Storage mode: pull (dirty-aware) + reload caches + refresh door state.
 * Does not start VM1. Clears BUDGET_EXHAUSTED when limits allow. */
int control_os_refresh(ControlContext *ctx);

/* Wake VM1 if the spend-brake lock is absent. Runs asynchronously when
 * `async` is non-zero. `admin_override` non-zero (Manager POST /api/wake,
 * admin-CIDR HTTP) skips the daily OCPU gate; player login must pass 0 so
 * friends stay refused after daily exhaustion. Soft monthly cap and the $1
 * spend-brake lock still refuse both paths.
 * Returns 0 if wake started or is already in progress, -1 on immediate reject. */
int control_wake(ControlContext *ctx, int async, int admin_override);

/* Stop VM1, record session end, move IP to VM2. `exhausted` sets BUDGET_EXHAUSTED.
 * Runs asynchronously when `async` is non-zero (HTTP idle-empty / budget-exhausted).
 * Returns 0 if stop started or is already in progress, -1 on thread create failure. */
int control_stop(ControlContext *ctx, int exhausted, int async);

/* Best-effort merge of VM1 catch-up intervals JSON body. */
int control_session_sync(ControlContext *ctx, const char *json_body);

int control_set_idle_timeout(ControlContext *ctx, int minutes);

/* Updates persisted last_keepalive_at (UTC ISO). */
int control_set_last_keepalive(ControlContext *ctx, const char *iso);

/* Writes status JSON into `buf` (caller provides buffer). Returns bytes written or -1. */
int control_status_json(const ControlContext *ctx, char *buf, size_t buf_cap);

/* mcdoor wake-on-join hook: player path — control_wake(ctx, 1, 0). */
void control_on_login_wake(void *userdata);

/* Idle MOTD / GET /api/status: reload local OS cache files (no Object Storage
 * GET) and recompute used hours / UTC day. No-op while PLAYABLE/STARTING. */
void control_on_status_refresh(void *userdata);

#endif /* VM2_CONTROL_H */
