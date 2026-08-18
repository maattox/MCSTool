/* jsonmin.h - minimal JSON reader/writer for the VM2 control plane.
 *
 * Covers exactly what the budget ledger, control state, and the shared test
 * fixtures need: objects, arrays, strings, numbers, booleans and null. It is
 * deliberately small and dependency-free so VM2 stays buildable on a bare
 * Always Free Micro with nothing but a C toolchain.
 */
#ifndef VM2_JSONMIN_H
#define VM2_JSONMIN_H

#include <stddef.h>

typedef enum {
  JSON_NULL = 0,
  JSON_BOOL,
  JSON_NUMBER,
  JSON_STRING,
  JSON_ARRAY,
  JSON_OBJECT
} JsonType;

typedef struct JsonValue JsonValue;

/* Parsing. Both return NULL on malformed input or I/O failure; the caller owns
 * the returned tree and must release it with json_free(). */
JsonValue *json_parse(const char *text);
JsonValue *json_parse_file(const char *path);
void json_free(JsonValue *value);

JsonType json_type(const JsonValue *value);
int json_is_null(const JsonValue *value);

/* Accessors are NULL-tolerant and type-tolerant: a missing key or a value of
 * the wrong type yields the caller's fallback, so readers can stay flat. */
const JsonValue *json_object_get(const JsonValue *object, const char *key);
size_t json_array_count(const JsonValue *array);
const JsonValue *json_array_at(const JsonValue *array, size_t index);
const char *json_as_string(const JsonValue *value, const char *fallback);
double json_as_number(const JsonValue *value, double fallback);
int json_as_bool(const JsonValue *value, int fallback);

/* Writing. Append failures are sticky: check `error` once before using `data`
 * rather than testing every call. */
typedef struct {
  char *data;
  size_t len;
  size_t cap;
  int error;
} JsonBuf;

void json_buf_init(JsonBuf *buf);
void json_buf_free(JsonBuf *buf);
int json_buf_raw(JsonBuf *buf, const char *text);
int json_buf_fmt(JsonBuf *buf, const char *fmt, ...);
int json_buf_string(JsonBuf *buf, const char *text);
int json_buf_number(JsonBuf *buf, double value);

/* Writes via `path`.tmp + rename so a crash cannot leave a half-written file. */
int json_write_file(const char *path, const char *text);

#endif /* VM2_JSONMIN_H */
