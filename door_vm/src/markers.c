/* markers.c - load/mutate/save player-map pins on door disk. */
#ifdef _WIN32
#define _CRT_RAND_S
#endif

#include "markers.h"

#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include "jsonmin.h"

#ifndef _WIN32
#include <fcntl.h>
#include <unistd.h>
#endif

void markers_clear(MarkerList *list) {
  if (list == NULL) {
    return;
  }
  memset(list, 0, sizeof *list);
}

static int dim_seg_char(unsigned char c) {
  return (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' || c == '.' || c == '-';
}

static int dim_segment_ok(const char *s, size_t n) {
  if (s == NULL || n == 0) {
    return 0;
  }
  if (n == 2 && s[0] == '.' && s[1] == '.') {
    return 0;
  }
  for (size_t i = 0; i < n; i++) {
    if (!dim_seg_char((unsigned char)s[i])) {
      return 0;
    }
  }
  return 1;
}

static int dim_ok(const char *dim) {
  if (dim == NULL) {
    return 0;
  }
  if (strcmp(dim, "overworld") == 0 || strcmp(dim, "nether") == 0 || strcmp(dim, "end") == 0) {
    return 1;
  }
  /* Vanilla folder / protocol aliases are not product ids. */
  if (strcmp(dim, "the_nether") == 0 || strcmp(dim, "minecraft/overworld") == 0 ||
      strcmp(dim, "minecraft/the_nether") == 0 || strcmp(dim, "minecraft/the_end") == 0) {
    return 0;
  }
  size_t len = strlen(dim);
  if (len == 0 || len > MARKER_DIM_TOTAL_MAX) {
    return 0;
  }
  if (strchr(dim, ':') != NULL || dim[0] == '/' || dim[len - 1] == '/') {
    return 0;
  }
  const char *slash = strchr(dim, '/');
  if (slash == NULL) {
    return 0;
  }
  size_t ns_len = (size_t)(slash - dim);
  if (ns_len < 1 || ns_len > MARKER_DIM_NS_MAX || !dim_segment_ok(dim, ns_len)) {
    return 0;
  }
  const char *path = slash + 1;
  size_t path_len = strlen(path);
  if (path_len < 1 || path_len > MARKER_DIM_PATH_MAX) {
    return 0;
  }
  const char *p = path;
  while (*p != '\0') {
    const char *next = strchr(p, '/');
    size_t seg = next == NULL ? strlen(p) : (size_t)(next - p);
    if (!dim_segment_ok(p, seg)) {
      return 0;
    }
    if (next == NULL) {
      break;
    }
    p = next + 1;
  }
  return 1;
}

static int id_ok(const char *id) {
  if (id == NULL || strlen(id) != MARKER_ID_LEN) {
    return 0;
  }
  for (size_t i = 0; i < MARKER_ID_LEN; i++) {
    char c = id[i];
    if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) {
      return 0;
    }
  }
  return 1;
}

static int is_trim_ws(unsigned char c) {
  return c == ' ' || c == '\t' || c == '\r' || c == '\n';
}

static int name_ok(const char *raw, char *out, size_t out_cap) {
  if (raw == NULL || out == NULL || out_cap == 0) {
    return 0;
  }
  while (is_trim_ws((unsigned char)*raw)) {
    raw++;
  }
  size_t bytes = strlen(raw);
  while (bytes > 0 && is_trim_ws((unsigned char)raw[bytes - 1])) {
    bytes--;
  }
  if (bytes == 0 || bytes > MARKER_NAME_BYTES || bytes >= out_cap) {
    return 0;
  }
  size_t cps = 0;
  for (size_t i = 0; i < bytes; i++) {
    unsigned char c = (unsigned char)raw[i];
    if (c < 0x20u) {
      return 0;
    }
    if ((c & 0xC0u) != 0x80u) {
      cps++;
    }
  }
  if (cps < 1 || cps > MARKER_NAME_CODEPOINTS) {
    return 0;
  }
  memcpy(out, raw, bytes);
  out[bytes] = '\0';
  return 1;
}

static int coord_ok(double v) {
  return v == v && v >= -MARKER_COORD_ABS_MAX && v <= MARKER_COORD_ABS_MAX;
}

static int find_id(const MarkerList *list, const char *id) {
  if (list == NULL || id == NULL) {
    return -1;
  }
  for (size_t i = 0; i < list->count; i++) {
    if (strcmp(list->items[i].id, id) == 0) {
      return (int)i;
    }
  }
  return -1;
}

static int fill_random(unsigned char *buf, size_t n) {
#ifdef _WIN32
  for (size_t i = 0; i < n; i++) {
    unsigned int r = 0;
    if (rand_s(&r) != 0) {
      return -1;
    }
    buf[i] = (unsigned char)r;
  }
  return 0;
#else
  int fd = open("/dev/urandom", O_RDONLY);
  if (fd < 0) {
    return -1;
  }
  size_t got = 0;
  while (got < n) {
    ssize_t r = read(fd, buf + got, n - got);
    if (r <= 0) {
      close(fd);
      return -1;
    }
    got += (size_t)r;
  }
  close(fd);
  return 0;
#endif
}

static int markers_new_id(MarkerList *list, char *out) {
  for (int attempt = 0; attempt < 16; attempt++) {
    unsigned char raw[8];
    if (fill_random(raw, sizeof raw) != 0) {
      return -1;
    }
    for (size_t i = 0; i < sizeof raw; i++) {
      snprintf(out + i * 2, 3, "%02x", raw[i]);
    }
    out[MARKER_ID_LEN] = '\0';
    if (find_id(list, out) < 0) {
      return 0;
    }
  }
  return -1;
}

static int marker_from_json(const JsonValue *obj, Marker *out) {
  if (obj == NULL || json_type(obj) != JSON_OBJECT || out == NULL) {
    return -1;
  }
  const char *id = json_as_string(json_object_get(obj, "id"), NULL);
  const char *dim = json_as_string(json_object_get(obj, "dim"), NULL);
  const JsonValue *xv = json_object_get(obj, "x");
  const JsonValue *zv = json_object_get(obj, "z");
  const char *name = json_as_string(json_object_get(obj, "name"), NULL);
  if (!id_ok(id) || !dim_ok(dim) || json_type(xv) != JSON_NUMBER || json_type(zv) != JSON_NUMBER) {
    return -1;
  }
  double x = json_as_number(xv, 0.0);
  double z = json_as_number(zv, 0.0);
  if (!coord_ok(x) || !coord_ok(z)) {
    return -1;
  }
  char trimmed[MARKER_NAME_BYTES + 1];
  if (!name_ok(name, trimmed, sizeof trimmed)) {
    return -1;
  }
  memset(out, 0, sizeof *out);
  memcpy(out->id, id, MARKER_ID_LEN + 1);
  snprintf(out->dim, sizeof out->dim, "%s", dim);
  out->x = x;
  out->z = z;
  snprintf(out->name, sizeof out->name, "%s", trimmed);
  return 0;
}

char *markers_to_json(const MarkerList *list) {
  if (list == NULL) {
    return NULL;
  }
  JsonBuf buf;
  json_buf_init(&buf);
  json_buf_raw(&buf, "{ \"version\": 1, \"markers\": [");
  for (size_t i = 0; i < list->count; i++) {
    const Marker *m = &list->items[i];
    if (i > 0) {
      json_buf_raw(&buf, ", ");
    }
    json_buf_raw(&buf, "{ \"id\": ");
    json_buf_string(&buf, m->id);
    json_buf_raw(&buf, ", \"dim\": ");
    json_buf_string(&buf, m->dim);
    json_buf_raw(&buf, ", \"x\": ");
    json_buf_number(&buf, m->x);
    json_buf_raw(&buf, ", \"z\": ");
    json_buf_number(&buf, m->z);
    json_buf_raw(&buf, ", \"name\": ");
    json_buf_string(&buf, m->name);
    json_buf_raw(&buf, " }");
  }
  json_buf_raw(&buf, "] }");
  if (buf.error || buf.data == NULL) {
    json_buf_free(&buf);
    return NULL;
  }
  return buf.data;
}

int markers_save(const MarkerList *list, const char *path) {
  if (list == NULL || path == NULL) {
    return -1;
  }
  char *text = markers_to_json(list);
  if (text == NULL) {
    return -1;
  }
  int rc = json_write_file(path, text);
  free(text);
  return rc;
}

int markers_load(MarkerList *list, const char *path) {
  if (list == NULL || path == NULL) {
    return -1;
  }
  markers_clear(list);
  FILE *probe = fopen(path, "rb");
  if (probe == NULL) {
    return 0;
  }
  fclose(probe);

  JsonValue *root = json_parse_file(path);
  if (root == NULL) {
    return -1;
  }
  if (json_type(root) != JSON_OBJECT) {
    json_free(root);
    return -1;
  }
  const JsonValue *arr = json_object_get(root, "markers");
  size_t n = json_array_count(arr);
  for (size_t i = 0; i < n && list->count < MARKERS_MAX; i++) {
    Marker m;
    if (marker_from_json(json_array_at(arr, i), &m) == 0) {
      if (find_id(list, m.id) < 0) {
        list->items[list->count++] = m;
      }
    }
  }
  json_free(root);
  return 0;
}

int markers_add(MarkerList *list, const char *dim, double x, double z, const char *name,
                char *out_id, int *http_status) {
  if (http_status == NULL) {
    return -1;
  }
  if (list == NULL) {
    *http_status = 400;
    return -1;
  }
  if (!dim_ok(dim) || !coord_ok(x) || !coord_ok(z)) {
    *http_status = 400;
    return -1;
  }
  char trimmed[MARKER_NAME_BYTES + 1];
  if (!name_ok(name, trimmed, sizeof trimmed)) {
    *http_status = 400;
    return -1;
  }
  if (list->count >= MARKERS_MAX) {
    *http_status = 409;
    return -1;
  }
  Marker *m = &list->items[list->count];
  memset(m, 0, sizeof *m);
  if (markers_new_id(list, m->id) != 0) {
    *http_status = 400;
    return -1;
  }
  snprintf(m->dim, sizeof m->dim, "%s", dim);
  m->x = x;
  m->z = z;
  snprintf(m->name, sizeof m->name, "%s", trimmed);
  list->count++;
  if (out_id != NULL) {
    memcpy(out_id, m->id, MARKER_ID_LEN + 1);
  }
  *http_status = 201;
  return 0;
}

int markers_rename(MarkerList *list, const char *id, const char *name, int *http_status) {
  if (http_status == NULL) {
    return -1;
  }
  if (list == NULL || !id_ok(id)) {
    *http_status = 400;
    return -1;
  }
  char trimmed[MARKER_NAME_BYTES + 1];
  if (!name_ok(name, trimmed, sizeof trimmed)) {
    *http_status = 400;
    return -1;
  }
  int idx = find_id(list, id);
  if (idx < 0) {
    *http_status = 404;
    return -1;
  }
  snprintf(list->items[idx].name, sizeof list->items[idx].name, "%s", trimmed);
  *http_status = 200;
  return 0;
}

int markers_delete(MarkerList *list, const char *id, int *http_status) {
  if (http_status == NULL) {
    return -1;
  }
  if (list == NULL || !id_ok(id)) {
    *http_status = 400;
    return -1;
  }
  int idx = find_id(list, id);
  if (idx < 0) {
    *http_status = 404;
    return -1;
  }
  size_t rest = list->count - (size_t)idx - 1;
  if (rest > 0) {
    memmove(&list->items[idx], &list->items[idx + 1], rest * sizeof(Marker));
  }
  list->count--;
  memset(&list->items[list->count], 0, sizeof(Marker));
  *http_status = 200;
  return 0;
}

int markers_apply_post(MarkerList *list, const char *json, int *http_status) {
  if (http_status == NULL) {
    return -1;
  }
  if (list == NULL || json == NULL) {
    *http_status = 400;
    return -1;
  }
  JsonValue *root = json_parse(json);
  if (root == NULL || json_type(root) != JSON_OBJECT) {
    json_free(root);
    *http_status = 400;
    return -1;
  }
  const char *op = json_as_string(json_object_get(root, "op"), NULL);
  int rc = -1;
  if (op == NULL) {
    *http_status = 400;
  } else if (strcmp(op, "add") == 0) {
    const char *dim = json_as_string(json_object_get(root, "dim"), NULL);
    const JsonValue *xv = json_object_get(root, "x");
    const JsonValue *zv = json_object_get(root, "z");
    const char *name = json_as_string(json_object_get(root, "name"), NULL);
    if (json_type(xv) != JSON_NUMBER || json_type(zv) != JSON_NUMBER) {
      *http_status = 400;
    } else {
      rc = markers_add(list, dim, json_as_number(xv, 0.0), json_as_number(zv, 0.0), name, NULL,
                       http_status);
    }
  } else if (strcmp(op, "rename") == 0) {
    rc = markers_rename(list, json_as_string(json_object_get(root, "id"), NULL),
                        json_as_string(json_object_get(root, "name"), NULL), http_status);
  } else if (strcmp(op, "delete") == 0) {
    rc = markers_delete(list, json_as_string(json_object_get(root, "id"), NULL), http_status);
  } else {
    *http_status = 400;
  }
  json_free(root);
  return rc;
}
