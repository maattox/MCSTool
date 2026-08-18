/* mc_proto.h - Minimal Minecraft Java protocol helpers (status + login kick).
 *
 * Mirrors shared/mc_status.py: VarInt framing, length-prefixed strings, and
 * the JSON payload for a Server List Ping status response.
 */
#ifndef VM2_MC_PROTO_H
#define VM2_MC_PROTO_H

#include <stddef.h>
#include <stdint.h>

typedef struct {
  const uint8_t *data;
  size_t len;
  size_t pos;
} McBuf;

/* Encode/decode VarInts. Returns bytes written/consumed, or -1 on error. */
int mc_varint_encode(uint32_t value, uint8_t *out, size_t out_cap);
int mc_varint_decode(McBuf *buf, uint32_t *out);

/* UTF-8 strings: VarInt length prefix + bytes. */
int mc_string_encode(const char *text, uint8_t *out, size_t out_cap);
int mc_string_decode(McBuf *buf, char *out, size_t out_cap);

/* Prefix `body` with its VarInt length. Returns total bytes written or -1. */
int mc_pack_packet(const uint8_t *body, size_t body_len, uint8_t *out, size_t out_cap);

/* Read one length-prefixed packet from `fd`. Returns body length or -1. `body`
 * must hold at least `body_cap` bytes. */
int mc_recv_packet(int fd, uint8_t *body, size_t body_cap);

/* Build the JSON string sent in a Status Response packet. Caller frees. */
char *mc_build_status_response_json(const char *motd, int online, int max_players,
                                    const char *version_name, int protocol,
                                    const char *favicon_b64);

/* Base64-encode `data` (no newlines). Caller frees. Returns NULL on failure. */
char *mc_base64_encode(const uint8_t *data, size_t len);

/* Read a file into a malloc'd buffer. Caller frees. Returns length or -1. */
long mc_read_file(const char *path, uint8_t **out);

/* Build {"text":"..."} with JSON escaping. Caller frees. */
char *mc_build_chat_component_json(const char *text);

#endif /* VM2_MC_PROTO_H */
