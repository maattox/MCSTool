/* httpmini.c - minimal HTTP/1.1 server for VM2 control API and static UI. */
#include "httpmini.h"

#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include "control.h"
#include "jsonmin.h"
#include "keepalive.h"

#ifdef _WIN32
#include <winsock2.h>
#include <ws2tcpip.h>
typedef SOCKET socket_t;
#define HTTP_INVALID_SOCKET INVALID_SOCKET
#define HTTP_SOCKET_ERROR SOCKET_ERROR
#define http_close closesocket
#else
#include <arpa/inet.h>
#include <netinet/in.h>
#include <sys/socket.h>
#include <unistd.h>
typedef int socket_t;
#define HTTP_INVALID_SOCKET (-1)
#define HTTP_SOCKET_ERROR (-1)
#define http_close close
#endif

#define REQ_BUF 65536
#define BODY_MAX 65536

typedef struct {
  char method[16];
  char path[512];
  char *body;
  size_t body_len;
} HttpRequest;

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

static int send_all(socket_t fd, const char *data, size_t len) {
  size_t sent = 0;
  while (sent < len) {
#ifdef _WIN32
    int n = send(fd, data + sent, (int)(len - sent), 0);
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

static void respond(socket_t fd, int status, const char *status_text, const char *content_type,
                    const char *body, size_t body_len) {
  char header[512];
  int hlen = snprintf(header, sizeof header,
                      "HTTP/1.1 %d %s\r\n"
                      "Content-Type: %s\r\n"
                      "Content-Length: %zu\r\n"
                      "Connection: close\r\n"
                      "Access-Control-Allow-Origin: *\r\n"
                      "\r\n",
                      status, status_text, content_type, body_len);
  if (hlen > 0) {
    send_all(fd, header, (size_t)hlen);
  }
  if (body != NULL && body_len > 0) {
    send_all(fd, body, body_len);
  }
}

static void respond_json(socket_t fd, int status, const char *body) {
  respond(fd, status, status == 200 ? "OK" : "Error", "application/json; charset=utf-8", body,
          body != NULL ? strlen(body) : 0);
}

static int read_request(socket_t fd, HttpRequest *req) {
  char buf[REQ_BUF];
  size_t total = 0;
  while (total < sizeof buf - 1) {
#ifdef _WIN32
    int n = recv(fd, buf + total, (int)(sizeof buf - 1 - total), 0);
#else
    ssize_t n = recv(fd, buf + total, sizeof buf - 1 - total, 0);
#endif
    if (n <= 0) {
      return -1;
    }
    total += (size_t)n;
    buf[total] = '\0';
    char *hdr_end = strstr(buf, "\r\n\r\n");
    if (hdr_end != NULL) {
      size_t hdr_len = (size_t)(hdr_end - buf);
      size_t body_start = hdr_len + 4;
      char *line_end = strstr(buf, "\r\n");
      if (line_end == NULL) {
        return -1;
      }
      *line_end = '\0';
      if (sscanf(buf, "%15s %511s", req->method, req->path) != 2) {
        return -1;
      }
      size_t content_length = 0;
      char *cl = strstr(buf, "Content-Length:");
      if (cl == NULL) {
        cl = strstr(buf, "content-length:");
      }
      if (cl != NULL) {
        content_length = (size_t)strtoul(cl + 15, NULL, 10);
      }
      if (content_length > BODY_MAX) {
        return -1;
      }
      while (total < body_start + content_length) {
#ifdef _WIN32
        int m = recv(fd, buf + total, (int)(sizeof buf - 1 - total), 0);
#else
        ssize_t m = recv(fd, buf + total, sizeof buf - 1 - total, 0);
#endif
        if (m <= 0) {
          return -1;
        }
        total += (size_t)m;
      }
      req->body_len = content_length;
      if (content_length > 0) {
        req->body = malloc(content_length + 1);
        if (req->body == NULL) {
          return -1;
        }
        memcpy(req->body, buf + body_start, content_length);
        req->body[content_length] = '\0';
      }
      return 0;
    }
  }
  return -1;
}

static void free_request(HttpRequest *req) {
  free(req->body);
  req->body = NULL;
  req->body_len = 0;
}

static int path_is_safe(const char *rel) {
  if (rel[0] == '\0' || strchr(rel, '.') == rel || strstr(rel, "..") != NULL) {
    return 0;
  }
  for (const char *p = rel; *p != '\0'; p++) {
    if (*p == '\\') {
      return 0;
    }
  }
  return 1;
}

static const char *mime_for(const char *path) {
  const char *dot = strrchr(path, '.');
  if (dot == NULL) {
    return "application/octet-stream";
  }
  if (strcmp(dot, ".html") == 0) {
    return "text/html; charset=utf-8";
  }
  if (strcmp(dot, ".css") == 0) {
    return "text/css; charset=utf-8";
  }
  if (strcmp(dot, ".js") == 0) {
    return "application/javascript; charset=utf-8";
  }
  if (strcmp(dot, ".png") == 0) {
    return "image/png";
  }
  if (strcmp(dot, ".ico") == 0) {
    return "image/x-icon";
  }
  return "application/octet-stream";
}

static void serve_static(socket_t fd, const HttpMiniConfig *cfg, const char *rel) {
  if (!path_is_safe(rel)) {
    respond(fd, 403, "Forbidden", "text/plain", "forbidden", 9);
    return;
  }
  char full[1024];
  snprintf(full, sizeof full, "%s/%s", cfg->web_root, rel);
  FILE *f = fopen(full, "rb");
  if (f == NULL) {
    respond(fd, 404, "Not Found", "text/plain", "not found", 9);
    return;
  }
  fseek(f, 0, SEEK_END);
  long len = ftell(f);
  if (len < 0 || len > (long)BODY_MAX) {
    fclose(f);
    respond(fd, 500, "Internal Server Error", "text/plain", "file too large", 14);
    return;
  }
  fseek(f, 0, SEEK_SET);
  char *data = malloc((size_t)len);
  if (data == NULL) {
    fclose(f);
    respond(fd, 500, "Internal Server Error", "text/plain", "oom", 3);
    return;
  }
  if (fread(data, 1, (size_t)len, f) != (size_t)len) {
    free(data);
    fclose(f);
    respond(fd, 500, "Internal Server Error", "text/plain", "read error", 10);
    return;
  }
  fclose(f);
  respond(fd, 200, "OK", mime_for(rel), data, (size_t)len);
  free(data);
}

static void handle_api(socket_t fd, const HttpMiniConfig *cfg, const HttpRequest *req) {
  ControlContext *ctl = cfg->control;
  char json[4096];

  if (strcmp(req->path, "/api/status") == 0 && strcmp(req->method, "GET") == 0) {
    if (control_status_json(ctl, json, sizeof json) < 0) {
      respond_json(fd, 500, "{\"ok\":false,\"error\":\"status failed\"}");
      return;
    }
    respond_json(fd, 200, json);
    return;
  }

  if (strcmp(req->path, "/api/os-refresh") == 0 && strcmp(req->method, "POST") == 0) {
    if (control_os_refresh(ctl) != 0) {
      respond_json(fd, 500, "{\"ok\":false,\"error\":\"os refresh failed\"}");
      return;
    }
    respond_json(fd, 200, "{\"ok\":true,\"refreshed\":true}");
    return;
  }

  if (strcmp(req->path, "/api/wake") == 0 && strcmp(req->method, "POST") == 0) {
    /* Admin HTTP (Security List admin /32). Skips daily exhaustion; spend-brake
     * and soft monthly cap still refuse inside do_wake. Player wake is mcdoor. */
    if (control_wake(ctl, 1, 1) != 0) {
      respond_json(fd, 409, "{\"ok\":false,\"error\":\"wake rejected\"}");
      return;
    }
    respond_json(fd, 202, "{\"ok\":true,\"door\":\"STARTING\"}");
    return;
  }

  if (strcmp(req->path, "/api/idle-empty") == 0 && strcmp(req->method, "POST") == 0) {
    if (control_stop(ctl, 0, 1) != 0) {
      respond_json(fd, 500, "{\"ok\":false,\"error\":\"stop failed\"}");
      return;
    }
    respond_json(fd, 202, "{\"ok\":true,\"door\":\"STOPPING\"}");
    return;
  }

  if (strcmp(req->path, "/api/budget-exhausted") == 0 && strcmp(req->method, "POST") == 0) {
    if (control_stop(ctl, 1, 1) != 0) {
      respond_json(fd, 500, "{\"ok\":false,\"error\":\"stop failed\"}");
      return;
    }
    respond_json(fd, 202, "{\"ok\":true,\"door\":\"STOPPING\"}");
    return;
  }

  if (strcmp(req->path, "/api/session-sync") == 0 && strcmp(req->method, "POST") == 0) {
    if (req->body == NULL || control_session_sync(ctl, req->body) != 0) {
      respond_json(fd, 400, "{\"ok\":false,\"error\":\"bad session-sync body\"}");
      return;
    }
    respond_json(fd, 200, "{\"ok\":true}");
    return;
  }

  if (strcmp(req->path, "/api/config/idle") == 0 && strcmp(req->method, "POST") == 0) {
    JsonValue *root = req->body != NULL ? json_parse(req->body) : NULL;
    double minutes = json_as_number(json_object_get(root, "idle_timeout_minutes"), -1);
    json_free(root);
    if (minutes < 1 || minutes > 24 * 60) {
      respond_json(fd, 400, "{\"ok\":false,\"error\":\"invalid idle_timeout_minutes\"}");
      return;
    }
    if (control_set_idle_timeout(ctl, (int)minutes) != 0) {
      respond_json(fd, 500, "{\"ok\":false,\"error\":\"persist failed\"}");
      return;
    }
    snprintf(json, sizeof json, "{\"ok\":true,\"idle_timeout_minutes\":%d}", (int)minutes);
    respond_json(fd, 200, json);
    return;
  }

  respond_json(fd, 404, "{\"ok\":false,\"error\":\"not found\"}");
}

static void handle_connection(socket_t fd, const HttpMiniConfig *cfg) {
  HttpRequest req;
  memset(&req, 0, sizeof req);
  if (read_request(fd, &req) != 0) {
    return;
  }

  if (strcmp(req.method, "GET") == 0) {
    if (strncmp(req.path, "/api/", 5) == 0) {
      handle_api(fd, cfg, &req);
      free_request(&req);
      return;
    }
    const char *rel = req.path;
    if (rel[0] == '/') {
      rel++;
    }
    if (rel[0] == '\0' || strcmp(rel, "/") == 0) {
      serve_static(fd, cfg, "index.html");
    } else {
      serve_static(fd, cfg, rel);
    }
    free_request(&req);
    return;
  }

  if (strcmp(req.method, "POST") == 0 && strncmp(req.path, "/api/", 5) == 0) {
    handle_api(fd, cfg, &req);
    free_request(&req);
    return;
  }

  respond(fd, 405, "Method Not Allowed", "text/plain", "method not allowed", 18);
  free_request(&req);
}

int httpmini_serve(const HttpMiniConfig *cfg) {
  if (cfg == NULL || cfg->control == NULL) {
    return -1;
  }
  if (net_init() != 0) {
    return -1;
  }

  socket_t srv = socket(AF_INET, SOCK_STREAM, 0);
  if (srv == HTTP_INVALID_SOCKET) {
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
    http_close(srv);
    return -1;
  }

  if (bind(srv, (struct sockaddr *)&addr, sizeof addr) == HTTP_SOCKET_ERROR) {
    http_close(srv);
    return -1;
  }
  if (listen(srv, 16) == HTTP_SOCKET_ERROR) {
    http_close(srv);
    return -1;
  }

  fprintf(stdout, "httpmini listening on %s:%u\n", bind_host, (unsigned)cfg->port);
  fflush(stdout);

  for (;;) {
    struct sockaddr_in client_addr;
#ifdef _WIN32
    int client_len = sizeof client_addr;
#else
    socklen_t client_len = sizeof client_addr;
#endif
    socket_t client = accept(srv, (struct sockaddr *)&client_addr, &client_len);
    if (client == HTTP_INVALID_SOCKET) {
      continue;
    }
    keepalive_activity_begin();
    handle_connection(client, cfg);
    keepalive_activity_end();
    http_close(client);
  }
}
