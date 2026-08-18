#include "jsonmin.h"

#include <stdarg.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#define JSON_MAX_DEPTH 32

struct JsonValue {
  JsonType type;
  double number;
  int boolean;
  char *string;      /* JSON_STRING */
  char **keys;       /* JSON_OBJECT, parallel to items */
  JsonValue **items; /* JSON_ARRAY / JSON_OBJECT */
  size_t count;
  size_t cap;
};

/* ---------------------------------------------------------------- values */

static JsonValue *value_new(JsonType type) {
  JsonValue *value = calloc(1, sizeof *value);
  if (value != NULL) {
    value->type = type;
  }
  return value;
}

void json_free(JsonValue *value) {
  if (value == NULL) {
    return;
  }
  for (size_t i = 0; i < value->count; i++) {
    if (value->keys != NULL) {
      free(value->keys[i]);
    }
    json_free(value->items[i]);
  }
  free(value->keys);
  free(value->items);
  free(value->string);
  free(value);
}

static int value_push(JsonValue *parent, char *key, JsonValue *child) {
  if (parent->count == parent->cap) {
    size_t cap = parent->cap != 0 ? parent->cap * 2 : 8;
    JsonValue **items = realloc(parent->items, cap * sizeof *items);
    if (items == NULL) {
      return -1;
    }
    parent->items = items;
    if (parent->type == JSON_OBJECT) {
      char **keys = realloc(parent->keys, cap * sizeof *keys);
      if (keys == NULL) {
        return -1;
      }
      parent->keys = keys;
    }
    parent->cap = cap;
  }
  if (parent->type == JSON_OBJECT) {
    parent->keys[parent->count] = key;
  }
  parent->items[parent->count] = child;
  parent->count++;
  return 0;
}

/* --------------------------------------------------------------- parsing */

typedef struct {
  const char *p;
  int depth;
} Parser;

static JsonValue *parse_value(Parser *ps);

static void skip_ws(Parser *ps) {
  while (*ps->p == ' ' || *ps->p == '\t' || *ps->p == '\n' || *ps->p == '\r') {
    ps->p++;
  }
}

static int hex4(const char *s, unsigned *out) {
  unsigned value = 0;
  for (int i = 0; i < 4; i++) {
    char c = s[i];
    value <<= 4;
    if (c >= '0' && c <= '9') {
      value |= (unsigned)(c - '0');
    } else if (c >= 'a' && c <= 'f') {
      value |= (unsigned)(c - 'a' + 10);
    } else if (c >= 'A' && c <= 'F') {
      value |= (unsigned)(c - 'A' + 10);
    } else {
      return -1;
    }
  }
  *out = value;
  return 0;
}

typedef struct {
  char *data;
  size_t len;
  size_t cap;
} StrBuf;

static int str_push(StrBuf *sb, char c) {
  if (sb->len + 1 >= sb->cap) {
    size_t cap = sb->cap != 0 ? sb->cap * 2 : 32;
    char *data = realloc(sb->data, cap);
    if (data == NULL) {
      return -1;
    }
    sb->data = data;
    sb->cap = cap;
  }
  sb->data[sb->len++] = c;
  return 0;
}

static int str_push_utf8(StrBuf *sb, unsigned cp) {
  if (cp < 0x80u) {
    return str_push(sb, (char)cp);
  }
  if (cp < 0x800u) {
    if (str_push(sb, (char)(0xC0u | (cp >> 6))) != 0) {
      return -1;
    }
    return str_push(sb, (char)(0x80u | (cp & 0x3Fu)));
  }
  if (cp < 0x10000u) {
    if (str_push(sb, (char)(0xE0u | (cp >> 12))) != 0 ||
        str_push(sb, (char)(0x80u | ((cp >> 6) & 0x3Fu))) != 0) {
      return -1;
    }
    return str_push(sb, (char)(0x80u | (cp & 0x3Fu)));
  }
  if (str_push(sb, (char)(0xF0u | (cp >> 18))) != 0 ||
      str_push(sb, (char)(0x80u | ((cp >> 12) & 0x3Fu))) != 0 ||
      str_push(sb, (char)(0x80u | ((cp >> 6) & 0x3Fu))) != 0) {
    return -1;
  }
  return str_push(sb, (char)(0x80u | (cp & 0x3Fu)));
}

/* Consumes a quoted string and returns a freshly allocated NUL-terminated copy. */
static char *parse_string_raw(Parser *ps) {
  if (*ps->p != '"') {
    return NULL;
  }
  ps->p++;
  StrBuf sb = {NULL, 0, 0};
  while (*ps->p != '"') {
    unsigned char c = (unsigned char)*ps->p;
    if (c == '\0' || c < 0x20u) {
      free(sb.data);
      return NULL;
    }
    if (c != '\\') {
      if (str_push(&sb, (char)c) != 0) {
        free(sb.data);
        return NULL;
      }
      ps->p++;
      continue;
    }
    ps->p++;
    char esc = *ps->p;
    int ok = 0;
    switch (esc) {
      case '"': ok = str_push(&sb, '"') == 0; ps->p++; break;
      case '\\': ok = str_push(&sb, '\\') == 0; ps->p++; break;
      case '/': ok = str_push(&sb, '/') == 0; ps->p++; break;
      case 'b': ok = str_push(&sb, '\b') == 0; ps->p++; break;
      case 'f': ok = str_push(&sb, '\f') == 0; ps->p++; break;
      case 'n': ok = str_push(&sb, '\n') == 0; ps->p++; break;
      case 'r': ok = str_push(&sb, '\r') == 0; ps->p++; break;
      case 't': ok = str_push(&sb, '\t') == 0; ps->p++; break;
      case 'u': {
        unsigned cp = 0;
        if (hex4(ps->p + 1, &cp) != 0) {
          break;
        }
        ps->p += 5;
        if (cp >= 0xD800u && cp <= 0xDBFFu && ps->p[0] == '\\' && ps->p[1] == 'u') {
          unsigned low = 0;
          if (hex4(ps->p + 2, &low) == 0 && low >= 0xDC00u && low <= 0xDFFFu) {
            cp = 0x10000u + ((cp - 0xD800u) << 10) + (low - 0xDC00u);
            ps->p += 6;
          }
        }
        ok = str_push_utf8(&sb, cp) == 0;
        break;
      }
      default: break;
    }
    if (!ok) {
      free(sb.data);
      return NULL;
    }
  }
  ps->p++;
  if (str_push(&sb, '\0') != 0) {
    free(sb.data);
    return NULL;
  }
  return sb.data;
}

static JsonValue *parse_number(Parser *ps) {
  char *end = NULL;
  double number = strtod(ps->p, &end);
  if (end == ps->p) {
    return NULL;
  }
  ps->p = end;
  JsonValue *value = value_new(JSON_NUMBER);
  if (value != NULL) {
    value->number = number;
  }
  return value;
}

static JsonValue *parse_literal(Parser *ps) {
  if (strncmp(ps->p, "true", 4) == 0) {
    ps->p += 4;
    JsonValue *value = value_new(JSON_BOOL);
    if (value != NULL) {
      value->boolean = 1;
    }
    return value;
  }
  if (strncmp(ps->p, "false", 5) == 0) {
    ps->p += 5;
    return value_new(JSON_BOOL);
  }
  if (strncmp(ps->p, "null", 4) == 0) {
    ps->p += 4;
    return value_new(JSON_NULL);
  }
  return NULL;
}

static JsonValue *parse_array(Parser *ps) {
  JsonValue *array = value_new(JSON_ARRAY);
  if (array == NULL) {
    return NULL;
  }
  ps->p++;
  skip_ws(ps);
  if (*ps->p == ']') {
    ps->p++;
    return array;
  }
  for (;;) {
    JsonValue *child = parse_value(ps);
    if (child == NULL || value_push(array, NULL, child) != 0) {
      json_free(child);
      json_free(array);
      return NULL;
    }
    skip_ws(ps);
    if (*ps->p == ',') {
      ps->p++;
      skip_ws(ps);
      continue;
    }
    if (*ps->p == ']') {
      ps->p++;
      return array;
    }
    json_free(array);
    return NULL;
  }
}

static JsonValue *parse_object(Parser *ps) {
  JsonValue *object = value_new(JSON_OBJECT);
  if (object == NULL) {
    return NULL;
  }
  ps->p++;
  skip_ws(ps);
  if (*ps->p == '}') {
    ps->p++;
    return object;
  }
  for (;;) {
    char *key = parse_string_raw(ps);
    if (key == NULL) {
      json_free(object);
      return NULL;
    }
    skip_ws(ps);
    if (*ps->p != ':') {
      free(key);
      json_free(object);
      return NULL;
    }
    ps->p++;
    skip_ws(ps);
    JsonValue *child = parse_value(ps);
    if (child == NULL || value_push(object, key, child) != 0) {
      free(key);
      json_free(child);
      json_free(object);
      return NULL;
    }
    skip_ws(ps);
    if (*ps->p == ',') {
      ps->p++;
      skip_ws(ps);
      continue;
    }
    if (*ps->p == '}') {
      ps->p++;
      return object;
    }
    json_free(object);
    return NULL;
  }
}

static JsonValue *parse_value(Parser *ps) {
  if (ps->depth >= JSON_MAX_DEPTH) {
    return NULL;
  }
  skip_ws(ps);
  switch (*ps->p) {
    case '{': {
      ps->depth++;
      JsonValue *value = parse_object(ps);
      ps->depth--;
      return value;
    }
    case '[': {
      ps->depth++;
      JsonValue *value = parse_array(ps);
      ps->depth--;
      return value;
    }
    case '"': {
      char *text = parse_string_raw(ps);
      if (text == NULL) {
        return NULL;
      }
      JsonValue *value = value_new(JSON_STRING);
      if (value == NULL) {
        free(text);
        return NULL;
      }
      value->string = text;
      return value;
    }
    case 't':
    case 'f':
    case 'n':
      return parse_literal(ps);
    default:
      return parse_number(ps);
  }
}

JsonValue *json_parse(const char *text) {
  if (text == NULL) {
    return NULL;
  }
  Parser ps = {text, 0};
  JsonValue *value = parse_value(&ps);
  if (value == NULL) {
    return NULL;
  }
  skip_ws(&ps);
  if (*ps.p != '\0') {
    json_free(value);
    return NULL;
  }
  return value;
}

JsonValue *json_parse_file(const char *path) {
  if (path == NULL) {
    return NULL;
  }
  FILE *fp = fopen(path, "rb");
  if (fp == NULL) {
    return NULL;
  }
  StrBuf sb = {NULL, 0, 0};
  char chunk[4096];
  size_t got = 0;
  while ((got = fread(chunk, 1, sizeof chunk, fp)) > 0) {
    for (size_t i = 0; i < got; i++) {
      if (str_push(&sb, chunk[i]) != 0) {
        free(sb.data);
        fclose(fp);
        return NULL;
      }
    }
  }
  fclose(fp);
  if (str_push(&sb, '\0') != 0) {
    free(sb.data);
    return NULL;
  }
  JsonValue *value = json_parse(sb.data);
  free(sb.data);
  return value;
}

/* ------------------------------------------------------------- accessors */

JsonType json_type(const JsonValue *value) {
  return value != NULL ? value->type : JSON_NULL;
}

int json_is_null(const JsonValue *value) {
  return value == NULL || value->type == JSON_NULL;
}

const JsonValue *json_object_get(const JsonValue *object, const char *key) {
  if (object == NULL || object->type != JSON_OBJECT || key == NULL) {
    return NULL;
  }
  for (size_t i = 0; i < object->count; i++) {
    if (object->keys[i] != NULL && strcmp(object->keys[i], key) == 0) {
      return object->items[i];
    }
  }
  return NULL;
}

size_t json_array_count(const JsonValue *array) {
  if (array == NULL || (array->type != JSON_ARRAY && array->type != JSON_OBJECT)) {
    return 0;
  }
  return array->count;
}

const JsonValue *json_array_at(const JsonValue *array, size_t index) {
  if (array == NULL || array->type != JSON_ARRAY || index >= array->count) {
    return NULL;
  }
  return array->items[index];
}

const char *json_as_string(const JsonValue *value, const char *fallback) {
  if (value == NULL || value->type != JSON_STRING) {
    return fallback;
  }
  return value->string;
}

double json_as_number(const JsonValue *value, double fallback) {
  if (value == NULL || value->type != JSON_NUMBER) {
    return fallback;
  }
  return value->number;
}

int json_as_bool(const JsonValue *value, int fallback) {
  if (value == NULL || value->type != JSON_BOOL) {
    return fallback;
  }
  return value->boolean;
}

/* ---------------------------------------------------------------- writing */

void json_buf_init(JsonBuf *buf) {
  buf->data = NULL;
  buf->len = 0;
  buf->cap = 0;
  buf->error = 0;
}

void json_buf_free(JsonBuf *buf) {
  free(buf->data);
  json_buf_init(buf);
}

static int buf_reserve(JsonBuf *buf, size_t extra) {
  if (buf->error) {
    return -1;
  }
  if (buf->len + extra + 1 <= buf->cap) {
    return 0;
  }
  size_t cap = buf->cap != 0 ? buf->cap : 256;
  while (cap < buf->len + extra + 1) {
    cap *= 2;
  }
  char *data = realloc(buf->data, cap);
  if (data == NULL) {
    buf->error = 1;
    return -1;
  }
  buf->data = data;
  buf->cap = cap;
  return 0;
}

int json_buf_raw(JsonBuf *buf, const char *text) {
  if (text == NULL) {
    return 0;
  }
  size_t len = strlen(text);
  if (buf_reserve(buf, len) != 0) {
    return -1;
  }
  memcpy(buf->data + buf->len, text, len);
  buf->len += len;
  buf->data[buf->len] = '\0';
  return 0;
}

int json_buf_fmt(JsonBuf *buf, const char *fmt, ...) {
  char scratch[512];
  va_list args;
  va_start(args, fmt);
  int written = vsnprintf(scratch, sizeof scratch, fmt, args);
  va_end(args);
  if (written < 0 || (size_t)written >= sizeof scratch) {
    buf->error = 1;
    return -1;
  }
  return json_buf_raw(buf, scratch);
}

int json_buf_string(JsonBuf *buf, const char *text) {
  if (text == NULL) {
    return json_buf_raw(buf, "null");
  }
  if (json_buf_raw(buf, "\"") != 0) {
    return -1;
  }
  for (const unsigned char *p = (const unsigned char *)text; *p != '\0'; p++) {
    int rc = 0;
    switch (*p) {
      case '"': rc = json_buf_raw(buf, "\\\""); break;
      case '\\': rc = json_buf_raw(buf, "\\\\"); break;
      case '\b': rc = json_buf_raw(buf, "\\b"); break;
      case '\f': rc = json_buf_raw(buf, "\\f"); break;
      case '\n': rc = json_buf_raw(buf, "\\n"); break;
      case '\r': rc = json_buf_raw(buf, "\\r"); break;
      case '\t': rc = json_buf_raw(buf, "\\t"); break;
      default:
        if (*p < 0x20u) {
          rc = json_buf_fmt(buf, "\\u%04x", (unsigned)*p);
        } else {
          char one[2] = {(char)*p, '\0'};
          rc = json_buf_raw(buf, one);
        }
        break;
    }
    if (rc != 0) {
      return -1;
    }
  }
  return json_buf_raw(buf, "\"");
}

int json_buf_number(JsonBuf *buf, double value) {
  return json_buf_fmt(buf, "%.10g", value);
}

int json_write_file(const char *path, const char *text) {
  if (path == NULL || text == NULL) {
    return -1;
  }
  size_t path_len = strlen(path);
  char *tmp = malloc(path_len + 5);
  if (tmp == NULL) {
    return -1;
  }
  memcpy(tmp, path, path_len);
  memcpy(tmp + path_len, ".tmp", 5);

  FILE *fp = fopen(tmp, "wb");
  if (fp == NULL) {
    free(tmp);
    return -1;
  }
  size_t len = strlen(text);
  int ok = fwrite(text, 1, len, fp) == len;
  if (fclose(fp) != 0) {
    ok = 0;
  }
  if (!ok) {
    remove(tmp);
    free(tmp);
    return -1;
  }
  /* POSIX rename replaces atomically; Windows refuses an existing target, so
   * fall back to unlink-then-rename there. */
  if (rename(tmp, path) != 0) {
    remove(path);
    if (rename(tmp, path) != 0) {
      remove(tmp);
      free(tmp);
      return -1;
    }
  }
  free(tmp);
  return 0;
}
