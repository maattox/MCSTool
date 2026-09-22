/* test_markers.c - player-map pin store (temp path, not the live door file). */
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include "jsonmin.h"
#include "markers.h"

static int failures = 0;
static int checks = 0;

#define CHECK(cond, ...)                            \
  do {                                              \
    checks++;                                       \
    if (!(cond)) {                                  \
      failures++;                                   \
      printf("  FAIL %s:%d: ", __FILE__, __LINE__); \
      printf(__VA_ARGS__);                          \
      printf("\n");                                 \
    }                                               \
  } while (0)

static void test_add_rename_delete(void) {
  printf("add / rename / delete\n");
  MarkerList list;
  markers_clear(&list);
  int status = 0;
  char id[MARKER_ID_LEN + 1];
  memset(id, 0, sizeof id);

  CHECK(markers_add(&list, "overworld", 12.0, -40.0, "Home", id, &status) == 0 && status == 201,
        "add Home failed (%d)", status);
  CHECK(strlen(id) == MARKER_ID_LEN, "id length %zu", strlen(id));
  CHECK(list.count == 1, "count after add %zu", list.count);
  CHECK(strcmp(list.items[0].dim, "overworld") == 0, "dim");
  CHECK(list.items[0].x == 12.0 && list.items[0].z == -40.0, "coords");
  CHECK(strcmp(list.items[0].name, "Home") == 0, "name");

  CHECK(markers_add(&list, "nether", 1.0, 2.0, "  Portal  ", NULL, &status) == 0 && status == 201,
        "trim name failed");
  CHECK(list.count == 2 && strcmp(list.items[1].name, "Portal") == 0, "trimmed name %s",
        list.items[1].name);

  CHECK(markers_rename(&list, id, "Spawn", &status) == 0 && status == 200, "rename failed (%d)",
        status);
  CHECK(strcmp(list.items[0].name, "Spawn") == 0, "rename did not stick");

  CHECK(markers_delete(&list, id, &status) == 0 && status == 200, "delete failed (%d)", status);
  CHECK(list.count == 1, "count after delete %zu", list.count);
  CHECK(strcmp(list.items[0].name, "Portal") == 0, "wrong survivor");

  CHECK(markers_delete(&list, id, &status) != 0 && status == 404, "deleted id should 404");
  CHECK(markers_rename(&list, "zzzzzzzzzzzzzzzz", "Nope", &status) != 0 && status == 400,
        "non-hex id should 400");
  CHECK(markers_rename(&list, "aaaaaaaaaaaaaaaa", "Nope", &status) != 0 && status == 404,
        "unknown rename should 404");
}

static void test_rejects(void) {
  printf("rejects\n");
  MarkerList list;
  markers_clear(&list);
  int status = 0;

  CHECK(markers_add(&list, "overworld", 0, 0, "", NULL, &status) != 0 && status == 400,
        "empty name accepted");
  CHECK(markers_add(&list, "overworld", 0, 0, "   ", NULL, &status) != 0 && status == 400,
        "whitespace name accepted");
  CHECK(markers_add(&list, "overworld", 0, 0, "bad\nname", NULL, &status) != 0 && status == 400,
        "control char accepted");
  CHECK(markers_add(&list, "the_nether", 0, 0, "X", NULL, &status) != 0 && status == 400,
        "bad dim accepted");
  CHECK(markers_add(&list, "Overworld", 0, 0, "X", NULL, &status) != 0 && status == 400,
        "capital dim accepted");
  CHECK(markers_add(&list, "overworld", 30000001.0, 0, "X", NULL, &status) != 0 && status == 400,
        "x over cap accepted");
  CHECK(markers_add(&list, "end", 0, -30000001.0, "X", NULL, &status) != 0 && status == 400,
        "z under cap accepted");
  CHECK(list.count == 0, "rejects mutated list");

  char long_name[MARKER_NAME_CODEPOINTS + 2];
  memset(long_name, 'a', MARKER_NAME_CODEPOINTS + 1);
  long_name[MARKER_NAME_CODEPOINTS + 1] = '\0';
  CHECK(markers_add(&list, "overworld", 0, 0, long_name, NULL, &status) != 0 && status == 400,
        "49-char name accepted");

  char ok48[MARKER_NAME_CODEPOINTS + 1];
  memset(ok48, 'b', MARKER_NAME_CODEPOINTS);
  ok48[MARKER_NAME_CODEPOINTS] = '\0';
  CHECK(markers_add(&list, "end", 0, 0, ok48, NULL, &status) == 0 && status == 201,
        "48-char name rejected");
  CHECK(markers_add(&list, "overworld", 30000000.0, -30000000.0, "Edge", NULL, &status) == 0,
        "edge coords rejected");
}

static void test_cap(void) {
  printf("cap 200\n");
  MarkerList list;
  markers_clear(&list);
  int status = 0;
  for (int i = 0; i < MARKERS_MAX; i++) {
    char name[16];
    snprintf(name, sizeof name, "m%d", i);
    CHECK(markers_add(&list, "overworld", (double)i, 0, name, NULL, &status) == 0 && status == 201,
          "add %d failed (%d)", i, status);
    if (status != 201) {
      break;
    }
  }
  CHECK(list.count == MARKERS_MAX, "count %zu", list.count);
  CHECK(markers_add(&list, "nether", 0, 0, "overflow", NULL, &status) != 0 && status == 409,
        "201st should 409, got %d", status);
  CHECK(list.count == MARKERS_MAX, "cap overflow mutated count");
}

static void test_round_trip(const char *path) {
  printf("file round trip (%s)\n", path);
  MarkerList list;
  markers_clear(&list);
  int status = 0;
  char id[MARKER_ID_LEN + 1];
  CHECK(markers_add(&list, "overworld", 12.0, -40.0, "Home", id, &status) == 0, "seed add");
  CHECK(markers_add(&list, "nether", 8.5, 16.25, "Fort", NULL, &status) == 0, "seed add 2");
  CHECK(markers_save(&list, path) == 0, "save failed");

  MarkerList loaded;
  CHECK(markers_load(&loaded, path) == 0, "load failed");
  CHECK(loaded.count == 2, "loaded count %zu", loaded.count);
  CHECK(strcmp(loaded.items[0].id, id) == 0, "id round trip");
  CHECK(strcmp(loaded.items[0].dim, "overworld") == 0, "dim round trip");
  CHECK(loaded.items[0].x == 12.0 && loaded.items[0].z == -40.0, "coords round trip");
  CHECK(strcmp(loaded.items[0].name, "Home") == 0, "name round trip");
  CHECK(strcmp(loaded.items[1].dim, "nether") == 0, "second dim");
  CHECK(loaded.items[1].x == 8.5 && loaded.items[1].z == 16.25, "second coords");

  char *text = markers_to_json(&loaded);
  CHECK(text != NULL, "to_json NULL");
  JsonValue *parsed = text != NULL ? json_parse(text) : NULL;
  CHECK(parsed != NULL, "serialized JSON did not parse");
  CHECK(json_as_number(json_object_get(parsed, "version"), 0) == 1, "version");
  CHECK(json_array_count(json_object_get(parsed, "markers")) == 2, "markers array");
  json_free(parsed);
  free(text);

  MarkerList missing;
  CHECK(markers_load(&missing, "does-not-exist-markers-97531.json") == 0, "missing should be empty");
  CHECK(missing.count == 0, "missing count %zu", missing.count);
}

static void test_apply_post(void) {
  printf("POST body ops\n");
  MarkerList list;
  markers_clear(&list);
  int status = 0;

  CHECK(markers_apply_post(&list, "not json", &status) != 0 && status == 400, "malformed JSON");
  CHECK(markers_apply_post(&list, "{ \"op\": \"nope\" }", &status) != 0 && status == 400,
        "unknown op");
  CHECK(markers_apply_post(&list,
                           "{ \"op\": \"add\", \"dim\": \"overworld\", \"x\": 1, \"z\": 2, "
                           "\"name\": \"A\" }",
                           &status) == 0 &&
            status == 201,
        "add post (%d)", status);
  CHECK(list.count == 1, "post add count");

  char body[256];
  snprintf(body, sizeof body,
           "{ \"op\": \"rename\", \"id\": \"%s\", \"name\": \"B\" }", list.items[0].id);
  CHECK(markers_apply_post(&list, body, &status) == 0 && status == 200, "rename post");
  CHECK(strcmp(list.items[0].name, "B") == 0, "rename post name");

  snprintf(body, sizeof body, "{ \"op\": \"delete\", \"id\": \"%s\" }", list.items[0].id);
  CHECK(markers_apply_post(&list, body, &status) == 0 && status == 200, "delete post");
  CHECK(list.count == 0, "delete post count");
}

int main(int argc, char **argv) {
  const char *path = argc > 1 ? argv[1] : "build/test_markers.json";
  test_add_rename_delete();
  test_rejects();
  test_cap();
  test_round_trip(path);
  test_apply_post();
  printf("%s: %d checks, %d failures\n", failures == 0 ? "PASS" : "FAIL", checks, failures);
  return failures == 0 ? 0 : 1;
}
