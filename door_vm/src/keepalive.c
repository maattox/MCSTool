#define _POSIX_C_SOURCE 200809L
/* keepalive.c - low-priority scheduled CPU bursts with activity preemption. */
#include "keepalive.h"

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>

#include "budget.h"
#include "control.h"
#include "state.h"

#ifdef _WIN32
#include <process.h>
#include <windows.h>
#else
#include <pthread.h>
#include <unistd.h>
#endif

#include <stdatomic.h>

#define DEFAULT_INTERVAL_SEC 7200
#define DEFAULT_BURST_SEC 750
#define SLEEP_MS 100

static atomic_int g_activity_refs;
static atomic_llong g_next_burst_epoch;
static KeepaliveConfig g_cfg;
static int g_started;

static void set_low_priority(void) {
#ifndef _WIN32
  nice(19);
#else
  SetThreadPriority(GetCurrentThread(), THREAD_PRIORITY_LOWEST);
#endif
}

void keepalive_activity_begin(void) {
  atomic_fetch_add(&g_activity_refs, 1);
}

void keepalive_activity_end(void) {
  int prev = atomic_fetch_sub(&g_activity_refs, 1);
  if (prev <= 0) {
    atomic_store(&g_activity_refs, 0);
  }
}

static int activity_active(void) {
  return atomic_load(&g_activity_refs) > 0;
}

static void sleep_ms(int ms) {
#ifdef _WIN32
  Sleep((DWORD)ms);
#else
  struct timespec ts;
  ts.tv_sec = ms / 1000;
  ts.tv_nsec = (long)(ms % 1000) * 1000000L;
  nanosleep(&ts, NULL);
#endif
}

static long long now_epoch(void) {
  return (long long)time(NULL);
}

static void cpu_chunk(void) {
  volatile unsigned long long sink = 0;
  for (int i = 0; i < 50000; i++) {
    sink = sink * 1664525ULL + 1013904223ULL;
  }
  (void)sink;
}

static int keepalive_enabled(void) {
  if (g_cfg.control == NULL) {
    return 0;
  }
  const ControlState *st = control_state(g_cfg.control);
  return st != NULL && st->keepalive_enabled;
}

static void record_burst_start(void) {
  char iso[32];
  if (budget_now_iso(iso, sizeof iso) != 0) {
    return;
  }
  ControlContext *ctx = g_cfg.control;
  if (ctx == NULL) {
    return;
  }
  control_set_last_keepalive(ctx, iso);
}

static void run_burst(int burst_sec) {
  set_low_priority();
  record_burst_start();

  long long burst_end = now_epoch() + burst_sec;
  long long paused_at = 0;

  while (now_epoch() < burst_end) {
    if (activity_active()) {
      if (paused_at == 0) {
        paused_at = now_epoch();
      }
      sleep_ms(SLEEP_MS);
      continue;
    }
    if (paused_at != 0) {
      burst_end += now_epoch() - paused_at;
      paused_at = 0;
    }
    cpu_chunk();
  }
}

static long long compute_initial_next(const char *last_iso, int interval_sec) {
  long long last = 0;
  if (last_iso != NULL && last_iso[0] != '\0') {
    if (budget_parse_iso(last_iso, &last) != 0) {
      last = 0;
    }
  }
  long long now = now_epoch();
  if (last <= 0) {
    return now + interval_sec;
  }
  long long next = last + interval_sec;
  return next > now ? next : now;
}

static void schedule_next(long long epoch) {
  atomic_store(&g_next_burst_epoch, epoch);
}

int keepalive_next_at_iso(char *out, size_t cap) {
  if (out == NULL || cap == 0) {
    return -1;
  }
  if (!keepalive_enabled()) {
    out[0] = '\0';
    return 0;
  }
  long long next = atomic_load(&g_next_burst_epoch);
  if (next <= 0) {
    out[0] = '\0';
    return 0;
  }
  return budget_format_iso(next, out, cap);
}

#ifdef _WIN32
static unsigned __stdcall keepalive_thread_main(void *arg) {
#else
static void *keepalive_thread_main(void *arg) {
#endif
  (void)arg;
  set_low_priority();

  int interval = g_cfg.interval_sec > 0 ? g_cfg.interval_sec : DEFAULT_INTERVAL_SEC;
  int burst = g_cfg.burst_sec > 0 ? g_cfg.burst_sec : DEFAULT_BURST_SEC;

  const ControlState *st = control_state(g_cfg.control);
  long long next = compute_initial_next(st != NULL ? st->last_keepalive_at : NULL, interval);
  schedule_next(next);

  for (;;) {
    if (!keepalive_enabled()) {
      schedule_next(0);
      sleep_ms(1000);
      continue;
    }

    long long now = now_epoch();
    long long target = atomic_load(&g_next_burst_epoch);
    if (target <= 0) {
      target = now + interval;
      schedule_next(target);
    }

    if (now < target) {
      sleep_ms(SLEEP_MS);
      continue;
    }

    while (activity_active()) {
      sleep_ms(SLEEP_MS);
    }

    run_burst(burst);
    schedule_next(now_epoch() + interval);
  }

#ifdef _WIN32
  return 0;
#else
  return NULL;
#endif
}

int keepalive_start(const KeepaliveConfig *cfg) {
  if (cfg == NULL || cfg->control == NULL || g_started) {
    return -1;
  }
  g_cfg = *cfg;
  if (g_cfg.interval_sec <= 0) {
    g_cfg.interval_sec = DEFAULT_INTERVAL_SEC;
  }
  if (g_cfg.burst_sec <= 0) {
    g_cfg.burst_sec = DEFAULT_BURST_SEC;
  }
  g_started = 1;

#ifdef _WIN32
  uintptr_t h = _beginthreadex(NULL, 0, keepalive_thread_main, NULL, 0, NULL);
  if (h == 0) {
    g_started = 0;
    return -1;
  }
  CloseHandle((HANDLE)h);
#else
  pthread_t tid;
  if (pthread_create(&tid, NULL, keepalive_thread_main, NULL) != 0) {
    g_started = 0;
    return -1;
  }
  pthread_detach(tid);
#endif
  return 0;
}
