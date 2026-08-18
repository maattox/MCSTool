/* httpmini.h - minimal HTTP/1.1 server for the VM2 control UI and JSON API.
 *
 * Handles GET/POST for small JSON payloads and static files under a web root.
 * Deliberately tiny: no TLS, no HTTP/2, no chunked encoding. Sufficient for
 * Phase A on a LAN allowlisted Micro.
 */
#ifndef VM2_HTTPMINI_H
#define VM2_HTTPMINI_H

#include <stddef.h>
#include <stdint.h>

typedef struct ControlContext ControlContext;

typedef struct {
  const char *bind_host;
  uint16_t port;
  const char *web_root;
  ControlContext *control;
} HttpMiniConfig;

/* Blocking accept loop. Returns 0 or -1. */
int httpmini_serve(const HttpMiniConfig *cfg);

#endif /* VM2_HTTPMINI_H */
