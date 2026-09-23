/* playermap.c - static HTTP for player map tiles (TCP 80) + /markers. */
#include "playermap.h"

#include <errno.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include "markers.h"

#ifdef _WIN32
#include <winsock2.h>
#include <ws2tcpip.h>
#include <io.h>
#include <fcntl.h>
#include <sys/stat.h>
typedef SOCKET socket_t;
#define PLAYER_INVALID_SOCKET INVALID_SOCKET
#define PLAYER_SOCKET_ERROR SOCKET_ERROR
#define player_close closesocket
#else
#include <arpa/inet.h>
#include <fcntl.h>
#include <limits.h>
#include <netinet/in.h>
#include <sys/socket.h>
#include <sys/stat.h>
#include <unistd.h>
#ifdef __linux__
#include <sys/sendfile.h>
#endif
typedef int socket_t;
#define PLAYER_INVALID_SOCKET (-1)
#define PLAYER_SOCKET_ERROR (-1)
#define player_close close
#endif

#define REQ_BUF 8192
#define PATH_MAX_REL 512
#define FALLBACK_PORT 8081
#define SEND_CHUNK 65536
#define PARSE_OK 0
#define PARSE_FAIL -1
#ifndef PATH_MAX
#define PATH_MAX 4096
#endif

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

static void respond_text(socket_t fd, int status, const char *status_text, const char *body) {
  char header[256];
  size_t body_len = body != NULL ? strlen(body) : 0;
  int hlen = snprintf(header, sizeof header,
                      "HTTP/1.1 %d %s\r\n"
                      "Content-Type: text/plain; charset=utf-8\r\n"
                      "Content-Length: %zu\r\n"
                      "Connection: close\r\n"
                      "\r\n",
                      status, status_text, body_len);
  if (hlen > 0) {
    send_all(fd, header, (size_t)hlen);
  }
  if (body_len > 0) {
    send_all(fd, body, body_len);
  }
}

static void respond_json(socket_t fd, int status, const char *status_text, const char *body) {
  char header[320];
  size_t body_len = body != NULL ? strlen(body) : 0;
  int hlen = snprintf(header, sizeof header,
                      "HTTP/1.1 %d %s\r\n"
                      "Content-Type: application/json; charset=utf-8\r\n"
                      "Content-Length: %zu\r\n"
                      "Cache-Control: no-store\r\n"
                      "Connection: close\r\n"
                      "\r\n",
                      status, status_text, body_len);
  if (hlen > 0) {
    send_all(fd, header, (size_t)hlen);
  }
  if (body_len > 0) {
    send_all(fd, body, body_len);
  }
}

static const char *http_phrase(int status) {
  switch (status) {
    case 200:
      return "OK";
    case 201:
      return "Created";
    case 400:
      return "Bad Request";
    case 404:
      return "Not Found";
    case 405:
      return "Method Not Allowed";
    case 409:
      return "Conflict";
    case 413:
      return "Payload Too Large";
    default:
      return "Internal Server Error";
  }
}

static int header_is_content_length(const char *line) {
  return strncmp(line, "Content-Length:", 15) == 0 ||
         strncmp(line, "content-length:", 15) == 0 ||
         strncmp(line, "CONTENT-LENGTH:", 15) == 0;
}

static const char *markers_file(const PlayerMapConfig *cfg) {
  if (cfg != NULL && cfg->markers_path != NULL && cfg->markers_path[0] != '\0') {
    return cfg->markers_path;
  }
  return MARKERS_DEFAULT_PATH;
}

static void handle_markers(socket_t fd, const char *method, const char *body, int too_large,
                           const PlayerMapConfig *cfg) {
  if (too_large) {
    respond_text(fd, 413, http_phrase(413), "too large");
    return;
  }
  const char *path = markers_file(cfg);
  if (strcmp(method, "GET") == 0) {
    MarkerList list;
    if (markers_load(&list, path) != 0) {
      respond_text(fd, 500, http_phrase(500), "error");
      return;
    }
    char *json = markers_to_json(&list);
    if (json == NULL) {
      respond_text(fd, 500, http_phrase(500), "error");
      return;
    }
    respond_json(fd, 200, http_phrase(200), json);
    free(json);
    return;
  }
  if (strcmp(method, "POST") == 0) {
    MarkerList list;
    if (markers_load(&list, path) != 0) {
      respond_text(fd, 500, http_phrase(500), "error");
      return;
    }
    int status = 400;
    if (markers_apply_post(&list, body != NULL ? body : "", &status) != 0) {
      const char *msg = "bad request";
      if (status == 409) {
        msg = "full";
      } else if (status == 404) {
        msg = "not found";
      }
      respond_text(fd, status, http_phrase(status), msg);
      return;
    }
    if (markers_save(&list, path) != 0) {
      respond_text(fd, 500, http_phrase(500), "error");
      return;
    }
    char *json = markers_to_json(&list);
    if (json == NULL) {
      respond_text(fd, 500, http_phrase(500), "error");
      return;
    }
    respond_json(fd, status, http_phrase(status), json);
    free(json);
    return;
  }
  respond_text(fd, 405, http_phrase(405), "method not allowed");
}

static int hex_nibble(char c) {
  if (c >= '0' && c <= '9') {
    return c - '0';
  }
  if (c >= 'a' && c <= 'f') {
    return c - 'a' + 10;
  }
  if (c >= 'A' && c <= 'F') {
    return c - 'A' + 10;
  }
  return -1;
}

static int percent_decode(char *s) {
  char *in = s;
  char *out = s;
  while (*in != '\0') {
    if (*in == '%') {
      int hi = hex_nibble(in[1]);
      int lo = hex_nibble(in[2]);
      if (hi < 0 || lo < 0) {
        return -1;
      }
      *out++ = (char)((hi << 4) | lo);
      in += 3;
    } else if (*in == '+') {
      *out++ = ' ';
      in++;
    } else {
      *out++ = *in++;
    }
  }
  *out = '\0';
  return 0;
}

static int path_is_safe(const char *rel) {
  if (rel[0] == '\0') {
    return 0;
  }
  if (rel[0] == '/' || rel[0] == '\\') {
    return 0;
  }
  for (const char *p = rel; *p != '\0'; p++) {
    if (*p == '\\' || *p == '\0' || (unsigned char)*p < 0x20) {
      return 0;
    }
  }
  if (strstr(rel, "..") != NULL) {
    return 0;
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
  if (strcmp(dot, ".json") == 0) {
    return "application/json; charset=utf-8";
  }
  if (strcmp(dot, ".webp") == 0) {
    return "image/webp";
  }
  if (strcmp(dot, ".png") == 0) {
    return "image/png";
  }
  if (strcmp(dot, ".svg") == 0) {
    return "image/svg+xml";
  }
  if (strcmp(dot, ".ico") == 0) {
    return "image/x-icon";
  }
  return "application/octet-stream";
}

#ifndef _WIN32
static int under_root(const char *root_real, const char *full_real) {
  size_t n = strlen(root_real);
  if (n == 0) {
    return 0;
  }
  if (strncmp(full_real, root_real, n) != 0) {
    return 0;
  }
  return full_real[n] == '\0' || full_real[n] == '/';
}
#endif

static int send_file_bytes(socket_t fd, int file_fd, size_t len) {
#ifdef __linux__
  off_t offset = 0;
  size_t left = len;
  while (left > 0) {
    ssize_t n = sendfile(fd, file_fd, &offset, left);
    if (n < 0) {
      if (errno == EINTR) {
        continue;
      }
      return -1;
    }
    if (n == 0) {
      break;
    }
    left -= (size_t)n;
  }
  return left == 0 ? 0 : -1;
#else
  char buf[SEND_CHUNK];
  size_t left = len;
  while (left > 0) {
    size_t chunk = left > sizeof buf ? sizeof buf : left;
#ifdef _WIN32
    int n = _read(file_fd, buf, (unsigned)chunk);
#else
    ssize_t n = read(file_fd, buf, chunk);
#endif
    if (n <= 0) {
      return -1;
    }
    if (send_all(fd, buf, (size_t)n) != 0) {
      return -1;
    }
    left -= (size_t)n;
  }
  return 0;
#endif
}

static void serve_file(socket_t fd, const char *full, const char *rel, int head_only) {
#ifdef _WIN32
  struct _stat64 st;
  if (_stat64(full, &st) != 0 || (st.st_mode & _S_IFREG) == 0) {
    respond_text(fd, 404, "Not Found", "not found");
    return;
  }
  int file_fd = _open(full, _O_RDONLY | _O_BINARY);
  if (file_fd < 0) {
    respond_text(fd, 404, "Not Found", "not found");
    return;
  }
  size_t len = (size_t)st.st_size;
#else
  struct stat st;
  if (stat(full, &st) != 0 || !S_ISREG(st.st_mode)) {
    respond_text(fd, 404, "Not Found", "not found");
    return;
  }
  int file_fd = open(full, O_RDONLY);
  if (file_fd < 0) {
    respond_text(fd, 404, "Not Found", "not found");
    return;
  }
  size_t len = (size_t)st.st_size;
#endif

  char header[512];
  int hlen = snprintf(header, sizeof header,
                      "HTTP/1.1 200 OK\r\n"
                      "Content-Type: %s\r\n"
                      "Content-Length: %zu\r\n"
                      "Cache-Control: no-store\r\n"
                      "Connection: close\r\n"
                      "\r\n",
                      mime_for(rel), len);
  if (hlen <= 0 || send_all(fd, header, (size_t)hlen) != 0) {
#ifdef _WIN32
    _close(file_fd);
#else
    close(file_fd);
#endif
    return;
  }
  if (!head_only && len > 0) {
    (void)send_file_bytes(fd, file_fd, len);
  }
#ifdef _WIN32
  _close(file_fd);
#else
  close(file_fd);
#endif
}

static int parse_request(socket_t fd, char *method, size_t method_cap, char *path,
                         size_t path_cap, char **body_out, size_t *body_len_out,
                         int *too_large) {
  char buf[REQ_BUF];
  size_t total = 0;
  *body_out = NULL;
  *body_len_out = 0;
  *too_large = 0;
  (void)method_cap;
  (void)path_cap;

  char *hdr_end = NULL;
  size_t sep = 4;
  while (total < sizeof buf - 1) {
#ifdef _WIN32
    int n = recv(fd, buf + total, (int)(sizeof buf - 1 - total), 0);
#else
    ssize_t n = recv(fd, buf + total, sizeof buf - 1 - total, 0);
#endif
    if (n <= 0) {
      return PARSE_FAIL;
    }
    total += (size_t)n;
    buf[total] = '\0';
    hdr_end = strstr(buf, "\r\n\r\n");
    if (hdr_end != NULL) {
      sep = 4;
      break;
    }
    hdr_end = strstr(buf, "\n\n");
    if (hdr_end != NULL) {
      sep = 2;
      break;
    }
  }
  if (hdr_end == NULL) {
    return PARSE_FAIL;
  }

  char *line_end = strstr(buf, "\r\n");
  if (line_end == NULL || line_end > hdr_end) {
    line_end = strchr(buf, '\n');
  }
  if (line_end == NULL || line_end > hdr_end) {
    return PARSE_FAIL;
  }
  char saved = *line_end;
  *line_end = '\0';
  if (sscanf(buf, "%15s %511s", method, path) != 2) {
    return PARSE_FAIL;
  }
  *line_end = saved;

  size_t content_length = 0;
  const char *h = line_end + (saved == '\r' ? 2 : 1);
  while (h < hdr_end) {
    if (header_is_content_length(h)) {
      content_length = (size_t)strtoul(h + 15, NULL, 10);
      break;
    }
    const char *nl = memchr(h, '\n', (size_t)(hdr_end - h));
    if (nl == NULL) {
      break;
    }
    h = nl + 1;
  }

  if (strcmp(method, "POST") != 0) {
    return PARSE_OK;
  }
  if (content_length > MARKERS_BODY_MAX) {
    *too_large = 1;
    return PARSE_OK;
  }
  if (content_length == 0) {
    return PARSE_OK;
  }

  char *body = malloc(content_length + 1);
  if (body == NULL) {
    return PARSE_FAIL;
  }
  size_t body_start = (size_t)(hdr_end - buf) + sep;
  size_t have = total > body_start ? total - body_start : 0;
  if (have > content_length) {
    have = content_length;
  }
  if (have > 0) {
    memcpy(body, buf + body_start, have);
  }
  while (have < content_length) {
#ifdef _WIN32
    int m = recv(fd, body + have, (int)(content_length - have), 0);
#else
    ssize_t m = recv(fd, body + have, content_length - have, 0);
#endif
    if (m <= 0) {
      free(body);
      return PARSE_FAIL;
    }
    have += (size_t)m;
  }
  body[content_length] = '\0';
  *body_out = body;
  *body_len_out = content_length;
  return PARSE_OK;
}

static void handle_connection(socket_t fd, const PlayerMapConfig *cfg) {
  char method[16];
  char raw_path[PATH_MAX_REL];
  char *body = NULL;
  size_t body_len = 0;
  int too_large = 0;
  memset(method, 0, sizeof method);
  memset(raw_path, 0, sizeof raw_path);
  if (parse_request(fd, method, sizeof method, raw_path, sizeof raw_path, &body, &body_len,
                    &too_large) != PARSE_OK) {
    return;
  }
  (void)body_len;

  char *q = strchr(raw_path, '?');
  if (q != NULL) {
    *q = '\0';
  }
  if (percent_decode(raw_path) != 0) {
    respond_text(fd, 400, "Bad Request", "bad path");
    free(body);
    return;
  }

  if (strncmp(raw_path, "/api", 4) == 0 && (raw_path[4] == '\0' || raw_path[4] == '/')) {
    respond_text(fd, 404, "Not Found", "not found");
    free(body);
    return;
  }

  if (strcmp(raw_path, "/markers") == 0) {
    handle_markers(fd, method, body, too_large, cfg);
    free(body);
    return;
  }

  int head_only = 0;
  if (strcmp(method, "HEAD") == 0) {
    head_only = 1;
  } else if (strcmp(method, "GET") != 0) {
    respond_text(fd, 405, "Method Not Allowed", "method not allowed");
    free(body);
    return;
  }

  const char *rel = raw_path;
  if (rel[0] == '/') {
    rel++;
  }

  char relbuf[PATH_MAX_REL];
  if (rel[0] == '\0') {
    snprintf(relbuf, sizeof relbuf, "index.html");
  } else {
    size_t rlen = strlen(rel);
    if (rlen > 0 && rel[rlen - 1] == '/') {
      if (rlen + 10 >= sizeof relbuf) {
        respond_text(fd, 414, "URI Too Long", "too long");
        free(body);
        return;
      }
      memcpy(relbuf, rel, rlen);
      memcpy(relbuf + rlen, "index.html", 11);
    } else {
      if (rlen >= sizeof relbuf) {
        respond_text(fd, 414, "URI Too Long", "too long");
        free(body);
        return;
      }
      memcpy(relbuf, rel, rlen + 1);
    }
  }
  rel = relbuf;
  if (!path_is_safe(rel)) {
    respond_text(fd, 403, "Forbidden", "forbidden");
    free(body);
    return;
  }

  const char *root = cfg->map_root != NULL ? cfg->map_root : "/var/lib/mc-player-map";
  char full[1024];
  int n = snprintf(full, sizeof full, "%s/%s", root, rel);
  if (n < 0 || n >= (int)sizeof full) {
    respond_text(fd, 414, "URI Too Long", "too long");
    free(body);
    return;
  }

#ifndef _WIN32
  {
    struct stat st;
    if (stat(full, &st) == 0 && S_ISDIR(st.st_mode)) {
      size_t rlen = strlen(relbuf);
      if (rlen + 12 >= sizeof relbuf) {
        respond_text(fd, 414, "URI Too Long", "too long");
        free(body);
        return;
      }
      if (rlen == 0 || relbuf[rlen - 1] != '/') {
        relbuf[rlen] = '/';
        relbuf[rlen + 1] = '\0';
        rlen++;
      }
      memcpy(relbuf + rlen, "index.html", 11);
      n = snprintf(full, sizeof full, "%s/%s", root, relbuf);
      if (n < 0 || n >= (int)sizeof full) {
        respond_text(fd, 414, "URI Too Long", "too long");
        free(body);
        return;
      }
    }
  }
  char root_real[PATH_MAX];
  char full_real[PATH_MAX];
  if (realpath(root, root_real) == NULL) {
    respond_text(fd, 404, "Not Found", "not found");
    free(body);
    return;
  }
  if (realpath(full, full_real) == NULL) {
    respond_text(fd, 404, "Not Found", "not found");
    free(body);
    return;
  }
  if (!under_root(root_real, full_real)) {
    respond_text(fd, 403, "Forbidden", "forbidden");
    free(body);
    return;
  }
  serve_file(fd, full_real, rel, head_only);
#else
  serve_file(fd, full, rel, head_only);
#endif
  free(body);
}

static int bind_listen(const char *bind_host, uint16_t port, socket_t *out) {
  socket_t srv = socket(AF_INET, SOCK_STREAM, 0);
  if (srv == PLAYER_INVALID_SOCKET) {
    return -1;
  }
  int yes = 1;
  setsockopt(srv, SOL_SOCKET, SO_REUSEADDR, (const char *)&yes, sizeof yes);

  struct sockaddr_in addr;
  memset(&addr, 0, sizeof addr);
  addr.sin_family = AF_INET;
  addr.sin_port = htons(port);
  if (inet_pton(AF_INET, bind_host, &addr.sin_addr) != 1) {
    player_close(srv);
    return -1;
  }
  if (bind(srv, (struct sockaddr *)&addr, sizeof addr) == PLAYER_SOCKET_ERROR) {
    player_close(srv);
    return -1;
  }
  if (listen(srv, 32) == PLAYER_SOCKET_ERROR) {
    player_close(srv);
    return -1;
  }
  *out = srv;
  return 0;
}

int playermap_serve(const PlayerMapConfig *cfg) {
  if (cfg == NULL) {
    return -1;
  }
  if (net_init() != 0) {
    return -1;
  }

  const char *bind_host = cfg->bind_host != NULL ? cfg->bind_host : "0.0.0.0";
  uint16_t port = cfg->port != 0 ? cfg->port : 80;
  const char *root = cfg->map_root != NULL ? cfg->map_root : "/var/lib/mc-player-map";
  socket_t srv = PLAYER_INVALID_SOCKET;

  if (bind_listen(bind_host, port, &srv) != 0) {
    int err = errno;
    if (port == 80) {
      fprintf(stderr, "playerhttp: bind %s:80 failed (%s); falling back to %u\n", bind_host,
              strerror(err), (unsigned)FALLBACK_PORT);
      fflush(stderr);
      if (bind_listen(bind_host, FALLBACK_PORT, &srv) != 0) {
        fprintf(stderr, "playerhttp: bind %s:%u failed\n", bind_host, (unsigned)FALLBACK_PORT);
        return -1;
      }
      port = FALLBACK_PORT;
    } else {
      fprintf(stderr, "playerhttp: bind %s:%u failed (%s)\n", bind_host, (unsigned)port,
              strerror(err));
      return -1;
    }
  }

  fprintf(stdout, "playerhttp listening on %s:%u (root %s)\n", bind_host, (unsigned)port, root);
  fflush(stdout);

  for (;;) {
    struct sockaddr_in client_addr;
#ifdef _WIN32
    int client_len = sizeof client_addr;
#else
    socklen_t client_len = sizeof client_addr;
#endif
    socket_t client = accept(srv, (struct sockaddr *)&client_addr, &client_len);
    if (client == PLAYER_INVALID_SOCKET) {
      continue;
    }
    handle_connection(client, cfg);
    player_close(client);
  }
}
