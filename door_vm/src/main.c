/* main.c - mccontrol daemon: mcdoor + HTTP API + static web UI. */
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include "control.h"
#include "httpmini.h"
#include "keepalive.h"
#include "mcdoor.h"

#ifdef _WIN32
#include <process.h>
#include <windows.h>
#else
#include <pthread.h>
#include <signal.h>
#include <unistd.h>
#endif

typedef struct {
  ControlContext *control;
  ControlConfig cfg;
} AppContext;

static AppContext g_app;

#ifdef _WIN32
static unsigned __stdcall mcdoor_thread_main(void *arg) {
#else
static void *mcdoor_thread_main(void *arg) {
#endif
  AppContext *app = (AppContext *)arg;
  if (mcdoor_load_icons(app->cfg.icons_dir) != 0) {
    fprintf(stderr, "mccontrol: warning: could not load icons from %s\n", app->cfg.icons_dir);
  }

  McdoorConfig mcfg = {
      .bind_host = app->cfg.bind_host,
      .port = app->cfg.mc_port,
      .state = control_state(app->control),
      .icon_idle_b64 = NULL,
      .icon_starting_b64 = NULL,
      .icon_exhausted_b64 = NULL,
      .version_name = MCDOOR_DEFAULT_VERSION,
      .protocol = MCDOOR_DEFAULT_PROTOCOL,
      .on_wake_request = control_on_login_wake,
      .wake_userdata = app->control,
  };

  fprintf(stdout, "mccontrol: mcdoor on %s:%u\n", mcfg.bind_host, (unsigned)mcfg.port);
  fflush(stdout);
  if (mcdoor_serve(&mcfg) != 0) {
    fprintf(stderr, "mccontrol: mcdoor serve failed\n");
  }
#ifdef _WIN32
  return 0;
#else
  return NULL;
#endif
}

#ifdef _WIN32
static unsigned __stdcall http_thread_main(void *arg) {
#else
static void *http_thread_main(void *arg) {
#endif
  AppContext *app = (AppContext *)arg;
  HttpMiniConfig hcfg = {
      .bind_host = app->cfg.bind_host,
      .port = app->cfg.http_port,
      .web_root = app->cfg.web_root,
      .control = app->control,
  };
  fprintf(stdout, "mccontrol: http on %s:%u (root %s)\n", hcfg.bind_host,
          (unsigned)hcfg.port, hcfg.web_root);
  fflush(stdout);
  if (httpmini_serve(&hcfg) != 0) {
    fprintf(stderr, "mccontrol: http serve failed\n");
  }
#ifdef _WIN32
  return 0;
#else
  return NULL;
#endif
}

static void usage(const char *prog) {
  fprintf(stderr, "Usage: %s [config.json]\n", prog);
}

int main(int argc, char **argv) {
  const char *config_path = NULL;
  if (argc > 1) {
    if (strcmp(argv[1], "-h") == 0 || strcmp(argv[1], "--help") == 0) {
      usage(argv[0]);
      return 0;
    }
    config_path = argv[1];
  }

  ControlConfig cfg;
  if (control_load_config(config_path, &cfg) != 0) {
    fprintf(stderr, "mccontrol: invalid config %s\n", config_path != NULL ? config_path : "(default)");
    return 1;
  }

  g_app.cfg = cfg;
  g_app.control = control_init(&cfg);
  if (g_app.control == NULL) {
    fprintf(stderr, "mccontrol: failed to init control state\n");
    return 1;
  }

#ifndef _WIN32
  signal(SIGPIPE, SIG_IGN);
#endif

  if (cfg.enable_mcdoor) {
#ifdef _WIN32
    uintptr_t h = _beginthreadex(NULL, 0, mcdoor_thread_main, &g_app, 0, NULL);
    if (h == 0) {
      fprintf(stderr, "mccontrol: failed to start mcdoor thread\n");
      return 1;
    }
    CloseHandle((HANDLE)h);
#else
    pthread_t tid;
    if (pthread_create(&tid, NULL, mcdoor_thread_main, &g_app) != 0) {
      fprintf(stderr, "mccontrol: failed to start mcdoor thread\n");
      return 1;
    }
    pthread_detach(tid);
#endif
  }

  if (cfg.enable_http) {
#ifdef _WIN32
    uintptr_t h = _beginthreadex(NULL, 0, http_thread_main, &g_app, 0, NULL);
    if (h == 0) {
      fprintf(stderr, "mccontrol: failed to start http thread\n");
      return 1;
    }
    CloseHandle((HANDLE)h);
#else
    pthread_t tid;
    if (pthread_create(&tid, NULL, http_thread_main, &g_app) != 0) {
      fprintf(stderr, "mccontrol: failed to start http thread\n");
      return 1;
    }
    pthread_detach(tid);
#endif
  }

  if (!cfg.enable_mcdoor && !cfg.enable_http) {
    fprintf(stderr, "mccontrol: nothing to do (enable_mcdoor and enable_http both false)\n");
    return 1;
  }

  KeepaliveConfig kcfg = {
      .control = g_app.control,
      .interval_sec = cfg.keepalive_interval_sec,
      .burst_sec = cfg.keepalive_burst_sec,
  };
  if (keepalive_start(&kcfg) != 0) {
    fprintf(stderr, "mccontrol: warning: keepalive thread failed to start\n");
  }

  fprintf(stdout, "mccontrol running (state %s, ledger %s)\n", cfg.state_path, cfg.ledger_path);
  fflush(stdout);

  for (;;) {
#ifdef _WIN32
    Sleep(1000);
#else
    pause();
#endif
  }
  return 0;
}
