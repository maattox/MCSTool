/* control.c - wake/stop orchestration, budget sync, OCI script runners. */
#include "control.h"

#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#ifdef _WIN32
#include <process.h>
#include <windows.h>
#define PATH_SEP '\\'
#else
#include <pthread.h>
#include <unistd.h>
#define PATH_SEP '/'
#endif

#include "jsonmin.h"
#include "keepalive.h"
#include "mcdoor.h"

struct ControlContext {
  ControlConfig cfg;
  ControlState state;
  BudgetLedger ledger;
#ifdef _WIN32
  CRITICAL_SECTION lock;
#else
  pthread_mutex_t lock;
#endif
  volatile int wake_in_progress;
  volatile int wake_admin_override;
  volatile int stop_in_progress;
  volatile int stop_exhausted;
};

static void lock_ctx(ControlContext *ctx) {
#ifdef _WIN32
  EnterCriticalSection(&ctx->lock);
#else
  pthread_mutex_lock(&ctx->lock);
#endif
}

static void unlock_ctx(ControlContext *ctx) {
#ifdef _WIN32
  LeaveCriticalSection(&ctx->lock);
#else
  pthread_mutex_unlock(&ctx->lock);
#endif
}

static void lock_init(ControlContext *ctx) {
#ifdef _WIN32
  InitializeCriticalSection(&ctx->lock);
#else
  pthread_mutex_init(&ctx->lock, NULL);
#endif
}

static void lock_destroy(ControlContext *ctx) {
#ifdef _WIN32
  DeleteCriticalSection(&ctx->lock);
#else
  pthread_mutex_destroy(&ctx->lock);
#endif
}

static void path_join(char *out, size_t cap, const char *dir, const char *name) {
  size_t len = strlen(dir);
  int need_sep = (len > 0 && dir[len - 1] != '/' && dir[len - 1] != '\\');
  snprintf(out, cap, "%s%s%s", dir, need_sep ? "/" : "", name);
}

static void set_error(ControlState *state, const char *msg) {
  if (msg == NULL) {
    state->last_error[0] = '\0';
    return;
  }
  size_t len = strlen(msg);
  if (len >= sizeof state->last_error) {
    len = sizeof state->last_error - 1;
  }
  memcpy(state->last_error, msg, len);
  state->last_error[len] = '\0';
}

static int touch_updated(ControlState *state) {
  return budget_now_iso(state->updated_at, sizeof state->updated_at);
}

static int persist(ControlContext *ctx) {
  if (state_save(&ctx->state, ctx->cfg.state_path) != 0) {
    return -1;
  }
  /* When Object Storage is authoritative, do not rewrite local ledger.json
   * (avoids stripping VM1 fields / competing with OS SoT). */
  if (ctx->cfg.object_storage_enabled) {
    return 0;
  }
  return budget_save(&ctx->ledger, ctx->cfg.ledger_path);
}

static int days_in_month(int year, int month) {
  static const int mdays[] = {0, 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31};
  if (month < 1 || month > 12) {
    return 30;
  }
  if (month == 2) {
    int leap = (year % 4 == 0 && (year % 100 != 0 || year % 400 == 0));
    return 28 + leap;
  }
  return mdays[month];
}

/* Prefer today's daily_ocpu sculpt (including 0 = park). Else monthly_ocpu_target
 * / UTC days-in-month. Fall back to daily_ocpu_limit_phase_a for older docs. */
static int apply_os_budget_file(ControlContext *ctx, const char *path) {
  if (ctx == NULL || path == NULL || path[0] == '\0') {
    return -1;
  }
  JsonValue *root = json_parse_file(path);
  if (root == NULL || json_type(root) != JSON_OBJECT) {
    json_free(root);
    return -1;
  }
  double monthly = json_as_number(json_object_get(root, "monthly_ocpu_target"), 0.0);
  double soft = json_as_number(json_object_get(root, "soft_ocpu_cap"), 0.0);
  if (soft > 0.0) {
    ctx->cfg.soft_ocpu_cap = soft;
  }
  int have_daily = 0;
  double daily = 0.0;
  char utc_day[16];
  if (budget_utc_date_for(NULL, utc_day, sizeof utc_day) == 0) {
    const JsonValue *map = json_object_get(root, "daily_ocpu");
    const JsonValue *val = json_object_get(map, utc_day);
    if (val != NULL && json_type(val) == JSON_NUMBER) {
      daily = json_as_number(val, 0.0);
      have_daily = 1;
    }
  }
  if (!have_daily && monthly > 0.0) {
    if (budget_utc_date_for(NULL, utc_day, sizeof utc_day) == 0) {
      int year = 0, month = 0, day = 0;
      if (sscanf(utc_day, "%d-%d-%d", &year, &month, &day) == 3) {
        int dim = days_in_month(year, month);
        if (dim > 0) {
          daily = monthly / (double)dim;
          have_daily = 1;
        }
      }
    }
  }
  if (!have_daily) {
    daily = json_as_number(json_object_get(root, "daily_ocpu_limit_phase_a"), 0.0);
    if (daily > 0.0) {
      have_daily = 1;
    }
  }
  if (!have_daily) {
    daily = json_as_number(json_object_get(root, "daily_ocpu_limit"), ctx->cfg.daily_ocpu_limit);
    have_daily = 1;
  }
  if (have_daily) {
    ctx->cfg.daily_ocpu_limit = daily;
  }
  double ocpus = json_as_number(json_object_get(root, "shape_ocpus"), 0.0);
  if (ocpus <= 0.0) {
    ocpus = json_as_number(json_object_get(root, "ocpus"), ctx->cfg.ocpus);
  }
  if (ocpus > 0.0) {
    ctx->cfg.ocpus = ocpus;
  }
  json_free(root);
  return 0;
}

/* Month-to-date OCPU-h for the current UTC calendar month (through today). */
static double month_used_ocpu_unlocked(ControlContext *ctx) {
  char utc_day[16];
  if (budget_utc_date_for(NULL, utc_day, sizeof utc_day) != 0) {
    return -1.0;
  }
  int year = 0, month = 0, day = 0;
  if (sscanf(utc_day, "%d-%d-%d", &year, &month, &day) != 3 || day < 1) {
    return -1.0;
  }
  double total = 0.0;
  for (int d = 1; d <= day; d++) {
    char buf[16];
    snprintf(buf, sizeof buf, "%04d-%02d-%02d", year, month, d);
    double used = budget_used_ocpu_for_utc_day(&ctx->ledger, buf, NULL);
    if (used < 0.0) {
      return -1.0;
    }
    total += used;
  }
  return total;
}

static int soft_cap_exhausted_unlocked(ControlContext *ctx) {
  if (ctx->cfg.soft_ocpu_cap <= 0.0) {
    return 0;
  }
  double month_used = month_used_ocpu_unlocked(ctx);
  if (month_used < 0.0) {
    return 0; /* unknown: do not soft-block on parse failure alone */
  }
  return budget_exhausted(month_used, ctx->cfg.soft_ocpu_cap);
}

/* Presence of the cached lock object means locked (fail closed even if JSON
 * is empty, malformed, or a newer version). Absence means no known lock. */
static int spend_brake_locked_unlocked(const ControlContext *ctx) {
  if (ctx == NULL || !ctx->cfg.object_storage_enabled) {
    return 0;
  }
  if (ctx->cfg.os_spend_brake_cache_path[0] == '\0') {
    return 0;
  }
  FILE *f = fopen(ctx->cfg.os_spend_brake_cache_path, "rb");
  if (f == NULL) {
    return 0;
  }
  fclose(f);
  return 1;
}

/* Reload OS caches into in-memory ledger + daily limit. Returns 0 or -1. */
static int reload_os_caches_unlocked(ControlContext *ctx) {
  if (budget_load(&ctx->ledger, ctx->cfg.os_ledger_cache_path) != 0) {
    return -1;
  }
  (void)apply_os_budget_file(ctx, ctx->cfg.os_budget_cache_path);
  return 0;
}

static int nearly_equal(double a, double b) {
  double d = a - b;
  if (d < 0.0) {
    d = -d;
  }
  return d < 1e-9;
}

static int refresh_budget_unlocked(ControlContext *ctx) {
  char utc_day[16];
  if (budget_utc_date_for(NULL, utc_day, sizeof utc_day) != 0) {
    return -1;
  }
  double used = budget_used_ocpu_for_utc_day(&ctx->ledger, utc_day, NULL);
  if (used < 0.0) {
    return -1;
  }
  char old_day[16];
  memcpy(old_day, ctx->state.la_day, sizeof old_day);
  double old_used = ctx->state.used_ocpu_hours;
  double old_limit = ctx->state.daily_limit_ocpu_hours;
  double old_ocpus = ctx->state.ocpus;
  DoorState old_door = ctx->state.door;
  memcpy(ctx->state.la_day, utc_day, sizeof ctx->state.la_day);
  ctx->state.used_ocpu_hours = used;
  ctx->state.daily_limit_ocpu_hours = ctx->cfg.daily_ocpu_limit;
  ctx->state.ocpus = ctx->cfg.ocpus;
  if (ctx->state.door != DOOR_PLAYABLE && ctx->state.door != DOOR_STARTING) {
    int locked = spend_brake_locked_unlocked(ctx);
    int daily_or_soft = budget_exhausted(used, ctx->cfg.daily_ocpu_limit) ||
                        soft_cap_exhausted_unlocked(ctx);
    if (locked) {
      ctx->state.door = DOOR_SPEND_BRAKE;
    } else if (daily_or_soft) {
      ctx->state.door = DOOR_BUDGET_EXHAUSTED;
    } else if (ctx->state.door == DOOR_BUDGET_EXHAUSTED ||
               ctx->state.door == DOOR_SPEND_BRAKE) {
      ctx->state.door = DOOR_IDLE;
    }
  }
  if (old_door == ctx->state.door && strcmp(old_day, utc_day) == 0 &&
      nearly_equal(old_used, used) && nearly_equal(old_limit, ctx->cfg.daily_ocpu_limit) &&
      nearly_equal(old_ocpus, ctx->cfg.ocpus)) {
    return 0;
  }
  touch_updated(&ctx->state);
  return persist(ctx);
}

int control_refresh_budget(ControlContext *ctx) {
  if (ctx == NULL) {
    return -1;
  }
  lock_ctx(ctx);
  int rc = refresh_budget_unlocked(ctx);
  unlock_ctx(ctx);
  return rc;
}

/* extra_args: optional argv after script path (e.g. "--force"); may be NULL. */
static int run_script_args(const ControlConfig *cfg, const char *script_name,
                           const char *extra_args) {
  char script[768];
  path_join(script, sizeof script, cfg->oci_dir, script_name);

  /* Use bash -c (not -l) so /etc/profile cannot clobber PATH.
   * Source env via tr -d CR (WinSCP). Run script as `bash -- path` so a
   * CRLF shebang cannot become /usr/bin/env: 'bash\r'.
   * Default OCI_CLI_AUTH — systemd root has no ~/.oci/config. */
  const char *args = (extra_args != NULL && extra_args[0] != '\0') ? extra_args : "";
  char cmd[2304];
  if (cfg->oci_env_file[0] != '\0') {
    snprintf(cmd, sizeof cmd,
             "bash -c \"set -a; source <(tr -d '\\\\r' < '%s'); set +a; "
             "export OCI_CLI_AUTH=\\${OCI_CLI_AUTH:-instance_principal}; "
             "exec bash -- '%s' %s\"",
             cfg->oci_env_file, script, args);
  } else {
    snprintf(cmd, sizeof cmd,
             "bash -c \"export OCI_CLI_AUTH=\\${OCI_CLI_AUTH:-instance_principal}; "
             "exec bash -- '%s' %s\"",
             script, args);
  }

  if (cfg->vm1_private_ip[0] != '\0') {
    char env_cmd[2816];
    snprintf(env_cmd, sizeof env_cmd, "VM1_PRIVATE_IP='%s' %s", cfg->vm1_private_ip, cmd);
    return system(env_cmd);
  }
  return system(cmd);
}

static int run_script(const ControlConfig *cfg, const char *script_name) {
  return run_script_args(cfg, script_name, NULL);
}

int control_os_refresh(ControlContext *ctx) {
  if (ctx == NULL) {
    return -1;
  }
  if (!ctx->cfg.object_storage_enabled) {
    return control_refresh_budget(ctx);
  }
  /* Pull without holding the control lock (network I/O). Always --force so
   * wake/refresh do not trust a stale cache when dirty flags were lost. */
  int pull_rc = run_script_args(&ctx->cfg, "pull_os_budget.sh", "--force");
  (void)run_script_args(&ctx->cfg, "pull_os_icons.sh", "--force");
  if (ctx->cfg.icons_dir[0] != '\0') {
    (void)mcdoor_load_icons(ctx->cfg.icons_dir);
  }
  lock_ctx(ctx);
  if (reload_os_caches_unlocked(ctx) != 0) {
    ctx->state.door = DOOR_DEGRADED;
    set_error(&ctx->state, "Object Storage cache unreadable after refresh");
    touch_updated(&ctx->state);
    persist(ctx);
    unlock_ctx(ctx);
    return -1;
  }
  if (pull_rc != 0) {
    ctx->state.door = DOOR_DEGRADED;
    set_error(&ctx->state, "pull_os_budget.sh failed during refresh");
    touch_updated(&ctx->state);
    persist(ctx);
    unlock_ctx(ctx);
    return -1;
  }
  int rc = refresh_budget_unlocked(ctx);
  unlock_ctx(ctx);
  return rc;
}

static int compute_hard_stop(const ControlContext *ctx, char *out, size_t out_cap) {
  double remaining =
      budget_remaining_ocpu(ctx->state.used_ocpu_hours, ctx->cfg.daily_ocpu_limit);
  if (remaining <= 0.0 || ctx->cfg.ocpus <= 0.0) {
    out[0] = '\0';
    return 0;
  }
  double wall_hours = remaining / ctx->cfg.ocpus;
  long long now = 0;
  if (budget_parse_iso(ctx->state.session_started_at, &now) != 0) {
    char now_iso[32];
    if (budget_now_iso(now_iso, sizeof now_iso) != 0) {
      return -1;
    }
    if (budget_parse_iso(now_iso, &now) != 0) {
      return -1;
    }
  }
  long long deadline = now + (long long)(wall_hours * 3600.0);
  return budget_format_iso(deadline, out, out_cap);
}

static int do_wake(ControlContext *ctx, int admin_override) {
  int pull_rc = 0;
  if (ctx->cfg.object_storage_enabled) {
    /* Network I/O must not hold the control lock. Force-refresh OS SoT. */
    pull_rc = run_script_args(&ctx->cfg, "pull_os_budget.sh", "--force");
    (void)run_script_args(&ctx->cfg, "pull_os_icons.sh", NULL);
    if (ctx->cfg.icons_dir[0] != '\0') {
      (void)mcdoor_load_icons(ctx->cfg.icons_dir);
    }
  }

  lock_ctx(ctx);
  if (ctx->cfg.object_storage_enabled) {
    if (reload_os_caches_unlocked(ctx) != 0) {
      ctx->state.door = DOOR_DEGRADED;
      set_error(&ctx->state,
                pull_rc != 0 ? "Object Storage pull/cache failed"
                             : "Object Storage ledger cache unreadable");
      touch_updated(&ctx->state);
      persist(ctx);
      unlock_ctx(ctx);
      return -1;
    }
    if (pull_rc != 0) {
      /* Fail closed for Always Free: do not wake on stale/unknown budget. */
      ctx->state.door = DOOR_DEGRADED;
      set_error(&ctx->state, "pull_os_budget.sh failed");
      touch_updated(&ctx->state);
      persist(ctx);
      unlock_ctx(ctx);
      return -1;
    }
  }

  refresh_budget_unlocked(ctx);
  if (spend_brake_locked_unlocked(ctx)) {
    ctx->state.door = DOOR_SPEND_BRAKE;
    set_error(&ctx->state, "monthly spend brake fired");
    touch_updated(&ctx->state);
    persist(ctx);
    unlock_ctx(ctx);
    return -1;
  }
  if (soft_cap_exhausted_unlocked(ctx)) {
    ctx->state.door = DOOR_BUDGET_EXHAUSTED;
    set_error(&ctx->state, "soft monthly OCPU cap reached");
    touch_updated(&ctx->state);
    persist(ctx);
    unlock_ctx(ctx);
    return -1;
  }
  if (!admin_override &&
      budget_exhausted(ctx->state.used_ocpu_hours, ctx->cfg.daily_ocpu_limit)) {
    ctx->state.door = DOOR_BUDGET_EXHAUSTED;
    set_error(&ctx->state, "daily budget exhausted");
    touch_updated(&ctx->state);
    persist(ctx);
    unlock_ctx(ctx);
    return -1;
  }
  if (ctx->state.door == DOOR_STARTING || ctx->state.door == DOOR_PLAYABLE) {
    unlock_ctx(ctx);
    return 0;
  }
  ctx->state.door = DOOR_STARTING;
  set_error(&ctx->state, NULL);
  touch_updated(&ctx->state);
  persist(ctx);
  unlock_ctx(ctx);

  if (run_script(&ctx->cfg, "start_vm1.sh") != 0) {
    lock_ctx(ctx);
    ctx->state.door = DOOR_DEGRADED;
    set_error(&ctx->state, "start_vm1.sh failed");
    touch_updated(&ctx->state);
    persist(ctx);
    unlock_ctx(ctx);
    return -1;
  }

  lock_ctx(ctx);
  char started[32];
  if (budget_now_iso(started, sizeof started) == 0) {
    memcpy(ctx->state.session_started_at, started, sizeof ctx->state.session_started_at);
    /* VM1 publishes intervals to Object Storage; door must not dual-write. */
    if (!ctx->cfg.object_storage_enabled) {
      budget_record_start(&ctx->ledger, ctx->cfg.ocpus, started);
    }
    compute_hard_stop(ctx, ctx->state.hard_stop_deadline, sizeof ctx->state.hard_stop_deadline);
    refresh_budget_unlocked(ctx);
  }
  unlock_ctx(ctx);

  if (run_script(&ctx->cfg, "wait_forge.sh") != 0) {
    lock_ctx(ctx);
    ctx->state.door = DOOR_DEGRADED;
    set_error(&ctx->state, "wait_forge.sh timed out");
    touch_updated(&ctx->state);
    persist(ctx);
    unlock_ctx(ctx);
    return -1;
  }

  if (run_script(&ctx->cfg, "ip_to_vm1.sh") != 0) {
    lock_ctx(ctx);
    ctx->state.door = DOOR_DEGRADED;
    set_error(&ctx->state, "ip_to_vm1.sh failed");
    touch_updated(&ctx->state);
    persist(ctx);
    unlock_ctx(ctx);
    return -1;
  }

  lock_ctx(ctx);
  ctx->state.door = DOOR_PLAYABLE;
  set_error(&ctx->state, NULL);
  touch_updated(&ctx->state);
  persist(ctx);
  unlock_ctx(ctx);
  return 0;
}

#ifdef _WIN32
static unsigned __stdcall wake_thread_main(void *arg) {
  ControlContext *ctx = (ControlContext *)arg;
  int admin = ctx->wake_admin_override;
  do_wake(ctx, admin);
  ctx->wake_in_progress = 0;
  return 0;
}
#else
static void *wake_thread_main(void *arg) {
  ControlContext *ctx = (ControlContext *)arg;
  int admin = ctx->wake_admin_override;
  do_wake(ctx, admin);
  ctx->wake_in_progress = 0;
  return NULL;
}
#endif

static int start_wake_thread(ControlContext *ctx, int admin_override) {
  if (ctx->wake_in_progress) {
    return 0;
  }
  ctx->wake_in_progress = 1;
  ctx->wake_admin_override = admin_override;
#ifdef _WIN32
  uintptr_t h = _beginthreadex(NULL, 0, wake_thread_main, ctx, 0, NULL);
  if (h == 0) {
    ctx->wake_in_progress = 0;
    return -1;
  }
  CloseHandle((HANDLE)h);
#else
  pthread_t tid;
  if (pthread_create(&tid, NULL, wake_thread_main, ctx) != 0) {
    ctx->wake_in_progress = 0;
    return -1;
  }
  pthread_detach(tid);
#endif
  return 0;
}

int control_wake(ControlContext *ctx, int async, int admin_override) {
  if (ctx == NULL) {
    return -1;
  }
  lock_ctx(ctx);
  if (ctx->stop_in_progress) {
    unlock_ctx(ctx);
    return -1;
  }
  if (ctx->state.door == DOOR_STARTING || ctx->state.door == DOOR_PLAYABLE) {
    unlock_ctx(ctx);
    return 0;
  }
  if (ctx->cfg.object_storage_enabled) {
    /* Never reject on cached exhaustion or spend-brake alone: do_wake
     * re-pulls Object Storage first so a raised budget or a Manager
     * DELETE of the lock can recover. Daily refuse is inside do_wake for
     * the player path only (admin_override == 0). */
    unlock_ctx(ctx);
    if (async) {
      return start_wake_thread(ctx, admin_override);
    }
    return do_wake(ctx, admin_override);
  }
  (void)refresh_budget_unlocked(ctx);
  if (soft_cap_exhausted_unlocked(ctx) ||
      (!admin_override &&
       budget_exhausted(ctx->state.used_ocpu_hours, ctx->cfg.daily_ocpu_limit))) {
    ctx->state.door = DOOR_BUDGET_EXHAUSTED;
    touch_updated(&ctx->state);
    persist(ctx);
    unlock_ctx(ctx);
    return -1;
  }
  unlock_ctx(ctx);
  if (async) {
    return start_wake_thread(ctx, admin_override);
  }
  return do_wake(ctx, admin_override);
}

static int do_stop(ControlContext *ctx, int exhausted) {
  if (run_script(&ctx->cfg, "stop_vm1.sh") != 0) {
    lock_ctx(ctx);
    set_error(&ctx->state, "stop_vm1.sh failed (continuing IP handback)");
    touch_updated(&ctx->state);
    persist(ctx);
    unlock_ctx(ctx);
  }

  /* Park the reserved IP before flipping IDLE so Manager Stop does not
   * report "stopped" while the play address is still on a dying VM1. */
  int ip_rc = run_script(&ctx->cfg, "ip_to_vm2.sh");

  lock_ctx(ctx);
  char stopped[32];
  if (!ctx->cfg.object_storage_enabled && budget_now_iso(stopped, sizeof stopped) == 0) {
    budget_record_stop(&ctx->ledger, stopped);
  }
  ctx->state.session_started_at[0] = '\0';
  ctx->state.hard_stop_deadline[0] = '\0';
  if (ctx->cfg.object_storage_enabled) {
    (void)reload_os_caches_unlocked(ctx);
  }
  refresh_budget_unlocked(ctx);
  if (ip_rc != 0) {
    ctx->state.door = DOOR_DEGRADED;
    set_error(&ctx->state, "ip_to_vm2.sh failed");
    touch_updated(&ctx->state);
    persist(ctx);
    unlock_ctx(ctx);
    return -1;
  }
  if (spend_brake_locked_unlocked(ctx)) {
    ctx->state.door = DOOR_SPEND_BRAKE;
  } else {
    ctx->state.door = exhausted ? DOOR_BUDGET_EXHAUSTED : DOOR_IDLE;
  }
  set_error(&ctx->state, NULL);
  touch_updated(&ctx->state);
  persist(ctx);
  unlock_ctx(ctx);
  return 0;
}

#ifdef _WIN32
static unsigned __stdcall stop_thread_main(void *arg) {
  ControlContext *ctx = (ControlContext *)arg;
  do_stop(ctx, ctx->stop_exhausted);
  ctx->stop_in_progress = 0;
  return 0;
}
#else
static void *stop_thread_main(void *arg) {
  ControlContext *ctx = (ControlContext *)arg;
  do_stop(ctx, ctx->stop_exhausted);
  ctx->stop_in_progress = 0;
  return NULL;
}
#endif

static int start_stop_thread(ControlContext *ctx, int exhausted) {
  if (ctx->stop_in_progress) {
    return 0;
  }
  ctx->stop_exhausted = exhausted;
  ctx->stop_in_progress = 1;
#ifdef _WIN32
  uintptr_t h = _beginthreadex(NULL, 0, stop_thread_main, ctx, 0, NULL);
  if (h == 0) {
    ctx->stop_in_progress = 0;
    return -1;
  }
  CloseHandle((HANDLE)h);
#else
  pthread_t tid;
  if (pthread_create(&tid, NULL, stop_thread_main, ctx) != 0) {
    ctx->stop_in_progress = 0;
    return -1;
  }
  pthread_detach(tid);
#endif
  return 0;
}

int control_stop(ControlContext *ctx, int exhausted, int async) {
  if (ctx == NULL) {
    return -1;
  }
  if (async) {
    return start_stop_thread(ctx, exhausted);
  }
  return do_stop(ctx, exhausted);
}

static int interval_exists(const BudgetLedger *led, const char *id) {
  for (size_t i = 0; i < led->count; i++) {
    if (strcmp(led->items[i].id, id) == 0) {
      return 1;
    }
  }
  return 0;
}

int control_session_sync(ControlContext *ctx, const char *json_body) {
  if (ctx == NULL || json_body == NULL) {
    return -1;
  }
  JsonValue *root = json_parse(json_body);
  if (root == NULL || json_type(root) != JSON_OBJECT) {
    json_free(root);
    return -1;
  }
  const JsonValue *intervals = json_object_get(root, "intervals");
  if (intervals == NULL || json_type(intervals) != JSON_ARRAY) {
    json_free(root);
    return -1;
  }

  lock_ctx(ctx);
  size_t n = json_array_count(intervals);
  for (size_t i = 0; i < n; i++) {
    const JsonValue *item = json_array_at(intervals, i);
    if (item == NULL || json_type(item) != JSON_OBJECT) {
      continue;
    }
    const char *id = json_as_string(json_object_get(item, "id"), NULL);
    if (id == NULL || id[0] == '\0' || interval_exists(&ctx->ledger, id)) {
      continue;
    }
    if (ctx->ledger.count == ctx->ledger.cap) {
      size_t cap = ctx->ledger.cap != 0 ? ctx->ledger.cap * 2 : 8;
      BudgetInterval *items = realloc(ctx->ledger.items, cap * sizeof *items);
      if (items == NULL) {
        break;
      }
      ctx->ledger.items = items;
      ctx->ledger.cap = cap;
    }
    BudgetInterval *dst = &ctx->ledger.items[ctx->ledger.count];
    memset(dst, 0, sizeof *dst);
    snprintf(dst->id, sizeof dst->id, "%s", id);
    const char *started = json_as_string(json_object_get(item, "started_at"), "");
    snprintf(dst->started_at, sizeof dst->started_at, "%s", started);
    const JsonValue *stopped_val = json_object_get(item, "stopped_at");
    if (!json_is_null(stopped_val)) {
      const char *stopped = json_as_string(stopped_val, NULL);
      if (stopped != NULL) {
        snprintf(dst->stopped_at, sizeof dst->stopped_at, "%s", stopped);
      }
    }
    dst->ocpus = json_as_number(json_object_get(item, "ocpus"), ctx->cfg.ocpus);
    ctx->ledger.count++;
  }
  refresh_budget_unlocked(ctx);
  unlock_ctx(ctx);
  json_free(root);
  return 0;
}

int control_set_idle_timeout(ControlContext *ctx, int minutes) {
  if (ctx == NULL || minutes < 1 || minutes > 24 * 60) {
    return -1;
  }
  lock_ctx(ctx);
  ctx->state.idle_timeout_minutes = minutes;
  touch_updated(&ctx->state);
  int rc = persist(ctx);
  unlock_ctx(ctx);
  return rc;
}

int control_set_last_keepalive(ControlContext *ctx, const char *iso) {
  if (ctx == NULL || iso == NULL || iso[0] == '\0') {
    return -1;
  }
  lock_ctx(ctx);
  snprintf(ctx->state.last_keepalive_at, sizeof ctx->state.last_keepalive_at, "%s", iso);
  touch_updated(&ctx->state);
  int rc = persist(ctx);
  unlock_ctx(ctx);
  return rc;
}

static void format_utc_reset_iso(char *out, size_t cap) {
  char utc_today[16];
  if (budget_utc_date_for(NULL, utc_today, sizeof utc_today) != 0) {
    if (cap > 0) {
      out[0] = '\0';
    }
    return;
  }
  long long day_end = 0;
  long long day_start = 0;
  if (budget_utc_day_bounds(utc_today, &day_start, &day_end) != 0) {
    if (cap > 0) {
      out[0] = '\0';
    }
    return;
  }
  budget_format_iso(day_end, out, cap);
}

int control_status_json(const ControlContext *ctx, char *buf, size_t buf_cap) {
  if (ctx == NULL || buf == NULL || buf_cap == 0) {
    return -1;
  }
  lock_ctx((ControlContext *)ctx);
  const ControlState *s = &ctx->state;
  double remaining =
      budget_remaining_ocpu(s->used_ocpu_hours, ctx->cfg.daily_ocpu_limit);
  char reset_at[32];
  format_utc_reset_iso(reset_at, sizeof reset_at);
  char next_keepalive[32] = "";
  keepalive_next_at_iso(next_keepalive, sizeof next_keepalive);
  JsonBuf jb;
  json_buf_init(&jb);
  json_buf_raw(&jb, "{\n  \"door\": ");
  json_buf_string(&jb, state_door_name(s->door));
  json_buf_fmt(&jb, ",\n  \"used_ocpu_hours\": %.6f,\n  \"remaining_ocpu_hours\": %.6f,\n",
               s->used_ocpu_hours, remaining);
  json_buf_raw(&jb, "  \"daily_limit_ocpu_hours\": ");
  json_buf_number(&jb, ctx->cfg.daily_ocpu_limit);
  json_buf_fmt(&jb, ",\n  \"ocpus\": %.1f,\n  \"idle_timeout_minutes\": %d,\n",
               ctx->cfg.ocpus, s->idle_timeout_minutes);
  json_buf_raw(&jb, "  \"la_day\": ");
  json_buf_string(&jb, s->la_day);
  json_buf_raw(&jb, ",\n  \"reset_at_utc\": ");
  json_buf_string(&jb, reset_at);
  json_buf_raw(&jb, ",\n  \"session_started_at\": ");
  if (s->session_started_at[0] == '\0') {
    json_buf_raw(&jb, "null");
  } else {
    json_buf_string(&jb, s->session_started_at);
  }
  json_buf_raw(&jb, ",\n  \"hard_stop_deadline\": ");
  if (s->hard_stop_deadline[0] == '\0') {
    json_buf_raw(&jb, "null");
  } else {
    json_buf_string(&jb, s->hard_stop_deadline);
  }
  json_buf_fmt(&jb, ",\n  \"keepalive_enabled\": %s,\n",
               s->keepalive_enabled ? "true" : "false");
  json_buf_raw(&jb, "  \"last_keepalive_at\": ");
  if (s->last_keepalive_at[0] == '\0') {
    json_buf_raw(&jb, "null");
  } else {
    json_buf_string(&jb, s->last_keepalive_at);
  }
  json_buf_raw(&jb, ",\n  \"next_keepalive_at\": ");
  if (next_keepalive[0] == '\0') {
    json_buf_raw(&jb, "null");
  } else {
    json_buf_string(&jb, next_keepalive);
  }
  json_buf_raw(&jb, ",\n  \"last_error\": ");
  json_buf_string(&jb, s->last_error);
  json_buf_fmt(&jb, ",\n  \"wake_in_progress\": %s,\n  \"stop_in_progress\": %s\n}\n",
               ctx->wake_in_progress ? "true" : "false",
               ctx->stop_in_progress ? "true" : "false");
  int rc = -1;
  if (!jb.error && jb.len < buf_cap) {
    memcpy(buf, jb.data, jb.len + 1);
    rc = (int)jb.len;
  }
  json_buf_free(&jb);
  unlock_ctx((ControlContext *)ctx);
  return rc;
}

void control_on_login_wake(void *userdata) {
  ControlContext *ctx = (ControlContext *)userdata;
  if (ctx == NULL) {
    return;
  }
  control_wake(ctx, 1, 0);
}

void control_on_status_refresh(void *userdata) {
  ControlContext *ctx = (ControlContext *)userdata;
  if (ctx == NULL) {
    return;
  }
  lock_ctx(ctx);
  DoorState door = ctx->state.door;
  if (door == DOOR_PLAYABLE || door == DOOR_STARTING) {
    unlock_ctx(ctx);
    return;
  }
  if (ctx->cfg.object_storage_enabled) {
    (void)reload_os_caches_unlocked(ctx);
  }
  (void)refresh_budget_unlocked(ctx);
  unlock_ctx(ctx);
}

static void config_default(ControlConfig *cfg) {
  memset(cfg, 0, sizeof *cfg);
  snprintf(cfg->state_path, sizeof cfg->state_path, "/var/lib/mccontrol/state.json");
  snprintf(cfg->ledger_path, sizeof cfg->ledger_path, "/var/lib/mccontrol/ledger.json");
  snprintf(cfg->oci_dir, sizeof cfg->oci_dir, "/opt/mccontrol/oci");
  snprintf(cfg->web_root, sizeof cfg->web_root, "/opt/mccontrol/web/static");
  snprintf(cfg->icons_dir, sizeof cfg->icons_dir, "assets/icons");
  snprintf(cfg->bind_host, sizeof cfg->bind_host, "0.0.0.0");
  cfg->http_port = 8080;
  cfg->mc_port = 25565;
  cfg->daily_ocpu_limit = BUDGET_DAILY_LIMIT_OCPU_HOURS;
  cfg->soft_ocpu_cap = 0.0;
  cfg->ocpus = 4.0;
  cfg->enable_mcdoor = 1;
  cfg->enable_http = 1;
  cfg->keepalive_enabled = 1;
  cfg->keepalive_interval_sec = 7200;
  cfg->keepalive_burst_sec = 750;
  cfg->object_storage_enabled = 0;
  snprintf(cfg->os_ledger_cache_path, sizeof cfg->os_ledger_cache_path,
           "%s", "/var/lib/mccontrol/os-cache/usage.json");
  snprintf(cfg->os_budget_cache_path, sizeof cfg->os_budget_cache_path,
           "%s", "/var/lib/mccontrol/os-cache/budget.json");
  snprintf(cfg->os_spend_brake_cache_path, sizeof cfg->os_spend_brake_cache_path,
           "%s", "/var/lib/mccontrol/os-cache/spend-brake-triggered.json");
}

int control_load_config(const char *path, ControlConfig *out) {
  if (out == NULL) {
    return -1;
  }
  config_default(out);
  if (path == NULL) {
    return 0;
  }
  JsonValue *root = json_parse_file(path);
  if (root == NULL) {
    return 0; /* use defaults */
  }
  if (json_type(root) != JSON_OBJECT) {
    json_free(root);
    return -1;
  }
#define LOAD_STR(field, key)                                              \
  do {                                                                    \
    const char *v = json_as_string(json_object_get(root, key), NULL);    \
    if (v != NULL) {                                                      \
      snprintf(out->field, sizeof out->field, "%s", v);                   \
    }                                                                     \
  } while (0)
  LOAD_STR(state_path, "state_path");
  LOAD_STR(ledger_path, "ledger_path");
  LOAD_STR(oci_dir, "oci_dir");
  LOAD_STR(web_root, "web_root");
  LOAD_STR(icons_dir, "icons_dir");
  LOAD_STR(oci_env_file, "oci_env_file");
  LOAD_STR(vm1_private_ip, "vm1_private_ip");
  LOAD_STR(bind_host, "bind_host");
#undef LOAD_STR
  out->http_port = (uint16_t)json_as_number(json_object_get(root, "http_port"), out->http_port);
  out->mc_port = (uint16_t)json_as_number(json_object_get(root, "mc_port"), out->mc_port);
  out->daily_ocpu_limit =
      json_as_number(json_object_get(root, "daily_ocpu_limit"), out->daily_ocpu_limit);
  out->ocpus = json_as_number(json_object_get(root, "ocpus"), out->ocpus);
  out->enable_mcdoor = json_as_bool(json_object_get(root, "enable_mcdoor"), out->enable_mcdoor);
  out->enable_http = json_as_bool(json_object_get(root, "enable_http"), out->enable_http);
  out->keepalive_enabled =
      json_as_bool(json_object_get(root, "keepalive_enabled"), out->keepalive_enabled);
  out->keepalive_interval_sec =
      (int)json_as_number(json_object_get(root, "keepalive_interval_sec"), out->keepalive_interval_sec);
  out->keepalive_burst_sec =
      (int)json_as_number(json_object_get(root, "keepalive_burst_sec"), out->keepalive_burst_sec);
  out->object_storage_enabled =
      json_as_bool(json_object_get(root, "object_storage_enabled"), out->object_storage_enabled);
  {
    const char *v = json_as_string(json_object_get(root, "os_ledger_cache_path"), NULL);
    if (v != NULL) {
      snprintf(out->os_ledger_cache_path, sizeof out->os_ledger_cache_path, "%s", v);
    }
    v = json_as_string(json_object_get(root, "os_budget_cache_path"), NULL);
    if (v != NULL) {
      snprintf(out->os_budget_cache_path, sizeof out->os_budget_cache_path, "%s", v);
    }
    v = json_as_string(json_object_get(root, "os_spend_brake_cache_path"), NULL);
    if (v != NULL) {
      snprintf(out->os_spend_brake_cache_path, sizeof out->os_spend_brake_cache_path, "%s",
               v);
    }
  }
  json_free(root);
  return 0;
}

ControlContext *control_init(const ControlConfig *cfg) {
  if (cfg == NULL) {
    return NULL;
  }
  ControlContext *ctx = calloc(1, sizeof *ctx);
  if (ctx == NULL) {
    return NULL;
  }
  ctx->cfg = *cfg;
  lock_init(ctx);
  state_default(&ctx->state);
  ctx->state.daily_limit_ocpu_hours = cfg->daily_ocpu_limit;
  ctx->state.ocpus = cfg->ocpus;
  if (state_load(&ctx->state, cfg->state_path) != 0) {
    control_free(ctx);
    return NULL;
  }
  if (budget_load(&ctx->ledger, cfg->ledger_path) != 0) {
    control_free(ctx);
    return NULL;
  }
  if (ctx->cfg.object_storage_enabled) {
    (void)reload_os_caches_unlocked(ctx);
  }
  control_refresh_budget(ctx);
  return ctx;
}

void control_free(ControlContext *ctx) {
  if (ctx == NULL) {
    return;
  }
  budget_free(&ctx->ledger);
  lock_destroy(ctx);
  free(ctx);
}

const ControlState *control_state(const ControlContext *ctx) {
  return ctx != NULL ? &ctx->state : NULL;
}
