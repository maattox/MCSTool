#include "mcdoor.h"

#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include "budget.h"
#include "keepalive.h"
#include "mc_proto.h"

#ifdef _WIN32
#include <winsock2.h>
#include <ws2tcpip.h>
typedef SOCKET socket_t;
#define MC_INVALID_SOCKET INVALID_SOCKET
#define MC_SOCKET_ERROR SOCKET_ERROR
#define mc_close_socket closesocket
#else
#include <arpa/inet.h>
#include <errno.h>
#include <netinet/in.h>
#include <sys/socket.h>
#include <sys/time.h>
#include <unistd.h>
typedef int socket_t;
#define MC_INVALID_SOCKET (-1)
#define MC_SOCKET_ERROR (-1)
#define mc_close_socket close
#endif

/* Minecraft status/login that never finishes must not stall the single accept
 * loop (listen backlog fills → later clients TCP-timeout). */
#ifndef MCDOOR_CLIENT_IO_TIMEOUT_SEC
#define MCDOOR_CLIENT_IO_TIMEOUT_SEC 8
#endif

static void mc_set_io_timeouts(socket_t fd, int seconds) {
  if (seconds <= 0) {
    return;
  }
#ifdef _WIN32
  DWORD ms = (DWORD)seconds * 1000u;
  setsockopt(fd, SOL_SOCKET, SO_RCVTIMEO, (const char *)&ms, sizeof ms);
  setsockopt(fd, SOL_SOCKET, SO_SNDTIMEO, (const char *)&ms, sizeof ms);
#else
  struct timeval tv;
  tv.tv_sec = seconds;
  tv.tv_usec = 0;
  setsockopt(fd, SOL_SOCKET, SO_RCVTIMEO, &tv, sizeof tv);
  setsockopt(fd, SOL_SOCKET, SO_SNDTIMEO, &tv, sizeof tv);
#endif
}

static char ICON_IDLE_B64[16384];
static char ICON_STARTING_B64[16384];
static char ICON_EXHAUSTED_B64[16384];

static int net_init(void) {
#ifdef _WIN32
  static int started = 0;
  if (!started) {
    WSADATA wsa;
    if (WSAStartup(MAKEWORD(2, 2), &wsa) != 0) {
      return -1;
    }
    started = 1;
  }
#endif
  return 0;
}

static int load_icon_b64(const char *path, char *out, size_t out_cap) {
  uint8_t *data = NULL;
  long len = mc_read_file(path, &data);
  if (len < 0 || data == NULL) {
    free(data);
    return -1;
  }
  char *encoded = mc_base64_encode(data, (size_t)len);
  free(data);
  if (encoded == NULL) {
    return -1;
  }
  if (strlen(encoded) + 1 > out_cap) {
    free(encoded);
    return -1;
  }
  memcpy(out, encoded, strlen(encoded) + 1);
  free(encoded);
  return 0;
}

int mcdoor_load_icons(const char *icons_dir) {
  if (icons_dir == NULL) {
    return -1;
  }
  char path[512];
  snprintf(path, sizeof path, "%s/idle.png", icons_dir);
  if (load_icon_b64(path, ICON_IDLE_B64, sizeof ICON_IDLE_B64) != 0) {
    return -1;
  }
  snprintf(path, sizeof path, "%s/starting.png", icons_dir);
  if (load_icon_b64(path, ICON_STARTING_B64, sizeof ICON_STARTING_B64) != 0) {
    return -1;
  }
  snprintf(path, sizeof path, "%s/exhausted.png", icons_dir);
  if (load_icon_b64(path, ICON_EXHAUSTED_B64, sizeof ICON_EXHAUSTED_B64) != 0) {
    return -1;
  }
  return 0;
}

static void format_utc_reset_date(char *out, size_t out_cap) {
  char utc_today[16];
  if (budget_utc_date_for(NULL, utc_today, sizeof utc_today) != 0) {
    snprintf(out, out_cap, "midnight UTC");
    return;
  }
  long long day_start = 0;
  long long day_end = 0;
  if (budget_utc_day_bounds(utc_today, &day_start, &day_end) != 0) {
    snprintf(out, out_cap, "midnight UTC");
    return;
  }
  char reset_iso[32];
  if (budget_format_iso(day_end, reset_iso, sizeof reset_iso) != 0) {
    snprintf(out, out_cap, "midnight UTC");
    return;
  }
  char next_day[16];
  if (budget_utc_date_for(reset_iso, next_day, sizeof next_day) != 0) {
    snprintf(out, out_cap, "midnight UTC");
    return;
  }
  snprintf(out, out_cap, "%s 00:00 UTC", next_day);
}

void mcdoor_build_motd(const ControlState *state, char *out, size_t out_cap) {
  if (state == NULL || out == NULL || out_cap == 0) {
    return;
  }
  out[0] = '\0';

  switch (state->door) {
    case DOOR_IDLE: {
      if (budget_shape_always_on_capable(state->ocpus)) {
        snprintf(out, out_cap, "Server offline. Connect to wake the world.");
        break;
      }
      double remaining =
          budget_remaining_ocpu(state->used_ocpu_hours, state->daily_limit_ocpu_hours);
      if (remaining < 0.0) {
        remaining = 0.0;
      }
      char reset_when[64];
      format_utc_reset_date(reset_when, sizeof reset_when);
      snprintf(out, out_cap,
               "Server offline. ~%.1f OCPU-h remaining today (resets %s). "
               "Connect to wake the world.",
               remaining, reset_when);
      break;
    }
    case DOOR_STARTING:
      snprintf(out, out_cap,
               "Server is starting — try again in 3-5 minutes.");
      break;
    case DOOR_BUDGET_EXHAUSTED: {
      char reset_when[64];
      format_utc_reset_date(reset_when, sizeof reset_when);
      snprintf(out, out_cap,
               "DAILY BUDGET FULFILLED FOR THE DAY — COME BACK %s",
               reset_when);
      break;
    }
    case DOOR_SPEND_BRAKE:
      snprintf(out, out_cap,
               "MONTHLY SPEND BRAKE FIRED — the admin must use Manager after a new calendar month.");
      break;
    case DOOR_PLAYABLE:
      snprintf(out, out_cap, "Server is online — connect directly.");
      break;
    case DOOR_DEGRADED:
      if (state->last_error[0] != '\0') {
        snprintf(out, out_cap, "Control plane degraded: %s", state->last_error);
      } else {
        snprintf(out, out_cap, "Control plane degraded — manual intervention required.");
      }
      break;
    default:
      snprintf(out, out_cap, "Unavailable.");
      break;
  }
}

void mcdoor_build_kick_reason(const ControlState *state, char *out, size_t out_cap) {
  if (state == NULL || out == NULL || out_cap == 0) {
    return;
  }
  switch (state->door) {
    case DOOR_STARTING:
      snprintf(out, out_cap, "Server is starting. Try again in 3-5 minutes.");
      break;
    case DOOR_BUDGET_EXHAUSTED: {
      char reset_when[64];
      format_utc_reset_date(reset_when, sizeof reset_when);
      snprintf(out, out_cap,
               "DAILY BUDGET FULFILLED FOR THE DAY — COME BACK %s",
               reset_when);
      break;
    }
    case DOOR_SPEND_BRAKE:
      snprintf(out, out_cap,
               "MONTHLY SPEND BRAKE FIRED — the admin must use Manager after a new calendar month.");
      break;
    case DOOR_PLAYABLE:
      snprintf(out, out_cap, "Server is online on another host — reconnect.");
      break;
    case DOOR_DEGRADED:
      snprintf(out, out_cap, "Control plane degraded. Try again later or contact the admin.");
      break;
    case DOOR_IDLE:
    default:
      snprintf(out, out_cap,
               "Server is offline. Connect to wake the world.");
      break;
  }
}

const char *mcdoor_icon_for_state(DoorState door, const McdoorConfig *cfg) {
  if (cfg != NULL) {
    switch (door) {
      case DOOR_STARTING:
        if (cfg->icon_starting_b64 != NULL && cfg->icon_starting_b64[0] != '\0') {
          return cfg->icon_starting_b64;
        }
        break;
      case DOOR_BUDGET_EXHAUSTED:
      case DOOR_SPEND_BRAKE:
        if (cfg->icon_exhausted_b64 != NULL && cfg->icon_exhausted_b64[0] != '\0') {
          return cfg->icon_exhausted_b64;
        }
        break;
      default:
        break;
    }
    if (cfg->icon_idle_b64 != NULL && cfg->icon_idle_b64[0] != '\0') {
      return cfg->icon_idle_b64;
    }
  }
  switch (door) {
    case DOOR_STARTING:
      return ICON_STARTING_B64[0] != '\0' ? ICON_STARTING_B64 : NULL;
    case DOOR_BUDGET_EXHAUSTED:
    case DOOR_SPEND_BRAKE:
      return ICON_EXHAUSTED_B64[0] != '\0' ? ICON_EXHAUSTED_B64 : NULL;
    default:
      return ICON_IDLE_B64[0] != '\0' ? ICON_IDLE_B64 : NULL;
  }
}

static int send_all(int fd, const uint8_t *data, size_t len) {
  size_t sent = 0;
  while (sent < len) {
#ifdef _WIN32
    int n = send(fd, (const char *)(data + sent), (int)(len - sent), 0);
#else
    ssize_t n = send(fd, data + sent, len - sent, 0);
#endif
    if (n <= 0) {
      return -1;
    }
    sent += (size_t)n;
  }
  return 0;
}

static int send_packet(int fd, const uint8_t *body, size_t body_len) {
  uint8_t packet[65536];
  int packed = mc_pack_packet(body, body_len, packet, sizeof packet);
  if (packed < 0) {
    return -1;
  }
  return send_all(fd, packet, (size_t)packed);
}

static int send_status_response(int fd, const McdoorConfig *cfg) {
  char motd[512];
  mcdoor_build_motd(cfg->state, motd, sizeof motd);
  const char *icon = mcdoor_icon_for_state(cfg->state->door, cfg);
  const char *version = cfg->version_name != NULL ? cfg->version_name : MCDOOR_DEFAULT_VERSION;
  int protocol = cfg->protocol > 0 ? cfg->protocol : MCDOOR_DEFAULT_PROTOCOL;

  char *json = mc_build_status_response_json(motd, 0, 20, version, protocol, icon);
  if (json == NULL) {
    return -1;
  }

  uint8_t body[65536];
  uint8_t *p = body;
  size_t remain = sizeof body;
  int n = mc_varint_encode(0, p, remain);
  if (n < 0) {
    free(json);
    return -1;
  }
  p += (size_t)n;
  remain -= (size_t)n;
  n = mc_string_encode(json, p, remain);
  free(json);
  if (n < 0) {
    return -1;
  }
  return send_packet(fd, body, (size_t)(p - body) + (size_t)n);
}

static int send_login_disconnect(int fd, const char *reason_text) {
  char *component = mc_build_chat_component_json(reason_text);
  if (component == NULL) {
    return -1;
  }

  uint8_t body[1024];
  int n = mc_varint_encode(0, body, sizeof body);
  if (n < 0) {
    free(component);
    return -1;
  }
  int m = mc_string_encode(component, body + (size_t)n, sizeof body - (size_t)n);
  free(component);
  if (m < 0) {
    return -1;
  }
  return send_packet(fd, body, (size_t)n + (size_t)m);
}

static int send_pong(int fd, const uint8_t *payload, size_t payload_len) {
  uint8_t body[16];
  int n = mc_varint_encode(1, body, sizeof body);
  if (n < 0 || (size_t)n + payload_len > sizeof body) {
    return -1;
  }
  memcpy(body + (size_t)n, payload, payload_len);
  return send_packet(fd, body, (size_t)n + payload_len);
}

static int handle_status_flow(int fd, const McdoorConfig *cfg) {
  uint8_t packet[4096];
  int plen = mc_recv_packet(fd, packet, sizeof packet);
  if (plen < 0) {
    return -1;
  }
  McBuf buf = {packet, (size_t)plen, 0};
  uint32_t packet_id = 0;
  if (mc_varint_decode(&buf, &packet_id) < 0 || packet_id != 0) {
    return -1;
  }
  if (send_status_response(fd, cfg) != 0) {
    return -1;
  }

  plen = mc_recv_packet(fd, packet, sizeof packet);
  if (plen < 0) {
    return 0; /* client may close after status */
  }
  buf.pos = 0;
  buf.len = (size_t)plen;
  if (mc_varint_decode(&buf, &packet_id) < 0 || packet_id != 1) {
    return 0;
  }
  if (buf.len - buf.pos != 8) {
    return 0;
  }
  return send_pong(fd, packet + buf.pos, 8);
}

int mcdoor_handle_connection(int fd, const McdoorConfig *cfg) {
  if (cfg == NULL || cfg->state == NULL) {
    return -1;
  }

  uint8_t packet[4096];
  int plen = mc_recv_packet(fd, packet, sizeof packet);
  if (plen < 0) {
    return -1;
  }

  McBuf buf = {packet, (size_t)plen, 0};
  uint32_t packet_id = 0;
  if (mc_varint_decode(&buf, &packet_id) < 0 || packet_id != 0) {
    return -1;
  }

  uint32_t protocol = 0;
  if (mc_varint_decode(&buf, &protocol) < 0) {
    return -1;
  }
  (void)protocol;

  char host[256];
  if (mc_string_decode(&buf, host, sizeof host) < 0) {
    return -1;
  }
  (void)host;

  if (buf.pos + 2 > buf.len) {
    return -1;
  }
  buf.pos += 2; /* port, big-endian */

  uint32_t next_state = 0;
  if (mc_varint_decode(&buf, &next_state) < 0) {
    return -1;
  }

  if (next_state == 1) {
    return handle_status_flow(fd, cfg);
  }
  if (next_state == 2) {
    /* Always request wake when idle, daily-exhausted, or spend-brake locked
     * so do_wake can re-pull Object Storage: a raised daily budget or a
     * Manager DELETE of the lock can then recover. Do not pre-check here. */
    if (cfg->on_wake_request != NULL &&
        (cfg->state->door == DOOR_IDLE ||
         cfg->state->door == DOOR_BUDGET_EXHAUSTED ||
         cfg->state->door == DOOR_SPEND_BRAKE)) {
      cfg->on_wake_request(cfg->wake_userdata);
    }
    char reason[512];
    mcdoor_build_kick_reason(cfg->state, reason, sizeof reason);
    return send_login_disconnect(fd, reason);
  }
  return -1;
}

int mcdoor_serve(const McdoorConfig *cfg) {
  if (cfg == NULL || cfg->state == NULL) {
    return -1;
  }
  if (net_init() != 0) {
    return -1;
  }

  socket_t srv = socket(AF_INET, SOCK_STREAM, 0);
  if (srv == MC_INVALID_SOCKET) {
    return -1;
  }

  int yes = 1;
  setsockopt(srv, SOL_SOCKET, SO_REUSEADDR, (const char *)&yes, sizeof yes);

  struct sockaddr_in addr;
  memset(&addr, 0, sizeof addr);
  addr.sin_family = AF_INET;
  addr.sin_port = htons(cfg->port);
  const char *bind_host = cfg->bind_host != NULL ? cfg->bind_host : "0.0.0.0";
  if (inet_pton(AF_INET, bind_host, &addr.sin_addr) != 1) {
    mc_close_socket(srv);
    return -1;
  }

  if (bind(srv, (struct sockaddr *)&addr, sizeof addr) == MC_SOCKET_ERROR) {
    mc_close_socket(srv);
    return -1;
  }
  if (listen(srv, 128) == MC_SOCKET_ERROR) {
    mc_close_socket(srv);
    return -1;
  }

  struct sockaddr_in bound;
#ifdef _WIN32
  int bound_len = sizeof bound;
#else
  socklen_t bound_len = sizeof bound;
#endif
  if (getsockname(srv, (struct sockaddr *)&bound, &bound_len) == 0) {
    fprintf(stdout, "mcdoor_test listening on 0.0.0.0:%u (door=%s)\n",
            (unsigned)ntohs(bound.sin_port), state_door_name(cfg->state->door));
    fflush(stdout);
  }

  for (;;) {
    struct sockaddr_in client_addr;
#ifdef _WIN32
    int client_len = sizeof client_addr;
#else
    socklen_t client_len = sizeof client_addr;
#endif
    socket_t client = accept(srv, (struct sockaddr *)&client_addr, &client_len);
    if (client == MC_INVALID_SOCKET) {
      continue;
    }
    mc_set_io_timeouts(client, MCDOOR_CLIENT_IO_TIMEOUT_SEC);
    keepalive_activity_begin();
    mcdoor_handle_connection((int)client, cfg);
    keepalive_activity_end();
    mc_close_socket(client);
  }
}
