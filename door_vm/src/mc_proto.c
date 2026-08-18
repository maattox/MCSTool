#include "mc_proto.h"

#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#ifdef _WIN32
#include <io.h>
#include <winsock2.h>
#else
#include <unistd.h>
#endif

static int append_json_escaped(const char *text, char *out, size_t out_cap, size_t *pos) {
  if (text == NULL) {
    return 0;
  }
  for (size_t i = 0; text[i] != '\0'; i++) {
    const char *chunk = NULL;
    switch (text[i]) {
      case '\\':
        chunk = "\\\\";
        break;
      case '"':
        chunk = "\\\"";
        break;
      case '\n':
        chunk = "\\n";
        break;
      case '\r':
        chunk = "\\r";
        break;
      case '\t':
        chunk = "\\t";
        break;
      default:
        if (*pos + 1 >= out_cap) {
          return -1;
        }
        out[(*pos)++] = text[i];
        continue;
    }
    size_t elen = strlen(chunk);
    if (*pos + elen >= out_cap) {
      return -1;
    }
    memcpy(out + *pos, chunk, elen);
    *pos += elen;
  }
  if (*pos < out_cap) {
    out[*pos] = '\0';
  }
  return 0;
}

int mc_varint_encode(uint32_t value, uint8_t *out, size_t out_cap) {
  size_t n = 0;
  while (1) {
    if (n >= out_cap) {
      return -1;
    }
    uint8_t byte = (uint8_t)(value & 0x7Fu);
    value >>= 7;
    if (value != 0) {
      byte |= 0x80u;
    }
    out[n++] = byte;
    if (value == 0) {
      break;
    }
  }
  return (int)n;
}

int mc_varint_decode(McBuf *buf, uint32_t *out) {
  if (buf == NULL || out == NULL) {
    return -1;
  }
  uint32_t result = 0;
  int shift = 0;
  int consumed = 0;
  while (1) {
    if (buf->pos >= buf->len) {
      return -1;
    }
    uint8_t byte = buf->data[buf->pos++];
    consumed++;
    result |= (uint32_t)(byte & 0x7Fu) << shift;
    if ((byte & 0x80u) == 0) {
      break;
    }
    shift += 7;
    if (shift > 35) {
      return -1;
    }
  }
  *out = result;
  return consumed;
}

int mc_string_encode(const char *text, uint8_t *out, size_t out_cap) {
  if (text == NULL) {
    return -1;
  }
  size_t slen = strlen(text);
  if (slen > 0x7FFFFFFFu) {
    return -1;
  }
  int n = mc_varint_encode((uint32_t)slen, out, out_cap);
  if (n < 0 || (size_t)n + slen > out_cap) {
    return -1;
  }
  memcpy(out + (size_t)n, text, slen);
  return n + (int)slen;
}

int mc_string_decode(McBuf *buf, char *out, size_t out_cap) {
  uint32_t length = 0;
  if (mc_varint_decode(buf, &length) < 0) {
    return -1;
  }
  if (length + 1 > out_cap || buf->pos + length > buf->len) {
    return -1;
  }
  memcpy(out, buf->data + buf->pos, length);
  out[length] = '\0';
  buf->pos += length;
  return (int)length;
}

int mc_pack_packet(const uint8_t *body, size_t body_len, uint8_t *out, size_t out_cap) {
  int n = mc_varint_encode((uint32_t)body_len, out, out_cap);
  if (n < 0 || (size_t)n + body_len > out_cap) {
    return -1;
  }
  memcpy(out + (size_t)n, body, body_len);
  return n + (int)body_len;
}

static int recv_exact(int fd, uint8_t *buf, size_t n) {
  size_t got = 0;
  while (got < n) {
#ifdef _WIN32
    int chunk = recv(fd, (char *)(buf + got), (int)(n - got), 0);
#else
    ssize_t chunk = recv(fd, buf + got, n - got, 0);
#endif
    if (chunk <= 0) {
      return -1;
    }
    got += (size_t)chunk;
  }
  return 0;
}

int mc_recv_packet(int fd, uint8_t *body, size_t body_cap) {
  uint8_t length_bytes[5];
  size_t length_len = 0;
  while (1) {
    if (recv_exact(fd, length_bytes + length_len, 1) != 0) {
      return -1;
    }
    if ((length_bytes[length_len] & 0x80u) == 0) {
      length_len++;
      break;
    }
    length_len++;
    if (length_len >= sizeof length_bytes) {
      return -1;
    }
  }
  McBuf buf = {length_bytes, length_len, 0};
  uint32_t length = 0;
  if (mc_varint_decode(&buf, &length) < 0) {
    return -1;
  }
  if (length > body_cap) {
    return -1;
  }
  if (length > 0 && recv_exact(fd, body, length) != 0) {
    return -1;
  }
  return (int)length;
}

char *mc_build_status_response_json(const char *motd, int online, int max_players,
                                    const char *version_name, int protocol,
                                    const char *favicon_b64) {
  char escaped_motd[1024];
  size_t pos = 0;
  escaped_motd[0] = '\0';
  if (append_json_escaped(motd != NULL ? motd : "", escaped_motd, sizeof escaped_motd, &pos) != 0) {
    return NULL;
  }

  char escaped_version[128];
  pos = 0;
  escaped_version[0] = '\0';
  if (append_json_escaped(version_name != NULL ? version_name : "1.20.1", escaped_version,
                          sizeof escaped_version, &pos) != 0) {
    return NULL;
  }

  size_t cap = 2048;
  if (favicon_b64 != NULL) {
    cap += strlen(favicon_b64);
  }
  char *json = malloc(cap);
  if (json == NULL) {
    return NULL;
  }

  int n = snprintf(json, cap,
                   "{\"version\":{\"name\":\"%s\",\"protocol\":%d},"
                   "\"players\":{\"max\":%d,\"online\":%d,\"sample\":[]},"
                   "\"description\":{\"text\":\"%s\"}",
                   escaped_version, protocol, max_players, online, escaped_motd);
  if (n < 0 || (size_t)n >= cap) {
    free(json);
    return NULL;
  }

  if (favicon_b64 != NULL && favicon_b64[0] != '\0') {
    size_t used = (size_t)n;
    int m = snprintf(json + used, cap - used, ",\"favicon\":\"data:image/png;base64,%s\"}",
                     favicon_b64);
    if (m < 0 || used + (size_t)m >= cap) {
      free(json);
      return NULL;
    }
  } else {
    size_t used = (size_t)n;
    if (used + 2 >= cap) {
      free(json);
      return NULL;
    }
    json[used] = '}';
    json[used + 1] = '\0';
  }
  return json;
}

char *mc_base64_encode(const uint8_t *data, size_t len) {
  static const char table[] =
      "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
  if (data == NULL) {
    return NULL;
  }
  size_t out_len = 4 * ((len + 2) / 3);
  char *out = malloc(out_len + 1);
  if (out == NULL) {
    return NULL;
  }
  size_t i = 0;
  size_t o = 0;
  while (i + 2 < len) {
    uint32_t triple = ((uint32_t)data[i] << 16) | ((uint32_t)data[i + 1] << 8) | data[i + 2];
    out[o++] = table[(triple >> 18) & 0x3Fu];
    out[o++] = table[(triple >> 12) & 0x3Fu];
    out[o++] = table[(triple >> 6) & 0x3Fu];
    out[o++] = table[triple & 0x3Fu];
    i += 3;
  }
  if (i < len) {
    uint32_t triple = (uint32_t)data[i] << 16;
    if (i + 1 < len) {
      triple |= (uint32_t)data[i + 1] << 8;
    }
    out[o++] = table[(triple >> 18) & 0x3Fu];
    out[o++] = table[(triple >> 12) & 0x3Fu];
    if (i + 1 < len) {
      out[o++] = table[(triple >> 6) & 0x3Fu];
      out[o++] = '=';
    } else {
      out[o++] = '=';
      out[o++] = '=';
    }
  }
  out[o] = '\0';
  return out;
}

char *mc_build_chat_component_json(const char *text) {
  char escaped[1024];
  size_t pos = 0;
  escaped[0] = '\0';
  if (append_json_escaped(text != NULL ? text : "", escaped, sizeof escaped, &pos) != 0) {
    return NULL;
  }
  size_t cap = pos + 16;
  char *out = malloc(cap);
  if (out == NULL) {
    return NULL;
  }
  snprintf(out, cap, "{\"text\":\"%s\"}", escaped);
  return out;
}

long mc_read_file(const char *path, uint8_t **out) {
  if (path == NULL || out == NULL) {
    return -1;
  }
  FILE *fp = fopen(path, "rb");
  if (fp == NULL) {
    return -1;
  }
  if (fseek(fp, 0, SEEK_END) != 0) {
    fclose(fp);
    return -1;
  }
  long size = ftell(fp);
  if (size < 0) {
    fclose(fp);
    return -1;
  }
  if (fseek(fp, 0, SEEK_SET) != 0) {
    fclose(fp);
    return -1;
  }
  uint8_t *buf = malloc((size_t)size);
  if (buf == NULL) {
    fclose(fp);
    return -1;
  }
  if (size > 0 && fread(buf, 1, (size_t)size, fp) != (size_t)size) {
    free(buf);
    fclose(fp);
    return -1;
  }
  fclose(fp);
  *out = buf;
  return size;
}
