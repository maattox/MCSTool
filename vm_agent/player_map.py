"""Niced MinedMap render + publish tree for the door VCN pull.

Sibling of idle_watch. Invoked by mc-player-map.timer (not dumped into idle_watch).
Explored chunks only. Overworld / Nether / End first, then extra namespaced
dimensions (cap 12, shim, shape-not-palette).
"""

from __future__ import annotations

import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import html
from datetime import datetime, timezone
from pathlib import Path
from typing import Callable

LIB = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "lib")
HERE = os.path.dirname(os.path.abspath(__file__))
if LIB not in sys.path:
    sys.path.insert(0, LIB)
if HERE not in sys.path:
    sys.path.insert(0, HERE)

from rcon_client import RconClient, RconError  # noqa: E402

CONFIG_PATH = os.environ.get("MC_MANAGER_CONFIG", "/etc/mc-manager/config.json")
DEFAULT_WORLD = "/opt/mcmgr/server/world"
DEFAULT_MINEDMAP = "/opt/mcmgr/bin/minedmap"
DEFAULT_MAP_ROOT = "/var/lib/mcmgr-map"
CAP_BYTES = 2 * 1024 * 1024 * 1024
RENDER_TIMEOUT_SEC = 15 * 60
DIMS = ("overworld", "nether", "end")
DIM_LABELS = {"overworld": "Overworld", "nether": "Nether", "end": "End"}
EXTRA_DIM_CAP = 12
SKIP_PUBLISH_NAMES = {"processed"}
_VANILLA_DIM_FOLDERS = {("minecraft", "overworld"), ("minecraft", "the_nether"), ("minecraft", "the_end")}

# P1 visual lock: oak night + binding bar; hotbar-slot tabs; system UI fonts.
CHROME_CSS = """:root {
  --void: #161310;
  --binding: #2A2218;
  --item: #E8DCC0;
  --quiet: #9A8E78;
  --grass: #4A7A36;
  --netherrack: #A34B40;
  --end-stone: #C9C07A;
  --well: #1A1510;
  --bevel-dark: #0A0907;
  --bevel-light: #5C4E3C;
  --font: ui-sans-serif, system-ui, "Segoe UI", sans-serif;
}
*, *::before, *::after { box-sizing: border-box; }
html { color-scheme: dark; height: 100%; }
body {
  margin: 0;
  height: 100%;
  height: 100dvh;
  background: var(--void);
  color: var(--item);
  font-family: var(--font);
}
::selection { background: var(--grass); color: var(--item); }
.skip {
  position: absolute;
  left: 12px;
  top: 8px;
  z-index: 2;
  padding: 6px 10px;
  background: var(--binding);
  color: var(--item);
  text-decoration: none;
  transform: translateY(-120%);
}
.skip:focus { transform: none; }
.shell {
  display: grid;
  grid-template-rows: auto minmax(0, 1fr);
  height: 100%;
}
.bar {
  display: grid;
  grid-template-columns: auto minmax(0, 1fr) auto;
  grid-template-areas: "title tabs note";
  align-items: center;
  gap: 10px 16px;
  padding: 8px 14px;
  padding-top: max(8px, env(safe-area-inset-top));
  padding-left: max(14px, env(safe-area-inset-left));
  padding-right: max(14px, env(safe-area-inset-right));
  background: var(--binding);
  border-bottom: 1px solid var(--bevel-dark);
  box-shadow: inset 0 1px 0 #3D3428;
}
.brand {
  grid-area: title;
  margin: 0;
  font-size: 15px;
  font-weight: 600;
  letter-spacing: 0.01em;
  color: var(--item);
  text-wrap: balance;
}
.dims {
  grid-area: tabs;
  display: flex;
  flex-wrap: nowrap;
  gap: 4px;
  min-width: 0;
  overflow-x: auto;
  overflow-y: hidden;
  -webkit-overflow-scrolling: touch;
  overscroll-behavior-x: contain;
  scrollbar-width: thin;
  scrollbar-color: var(--bevel-light) var(--well);
}
.dims::-webkit-scrollbar { height: 6px; }
.dims::-webkit-scrollbar-track { background: var(--well); }
.dims::-webkit-scrollbar-thumb { background: var(--bevel-light); }
.note {
  grid-area: note;
  margin: 0;
  font-size: 11px;
  font-weight: 400;
  line-height: 1.35;
  color: var(--quiet);
  text-align: right;
  min-width: 0;
}
.slot {
  display: inline-flex;
  flex: 0 0 auto;
  align-items: center;
  justify-content: center;
  min-width: 0;
  padding: 0 14px;
  height: 32px;
  white-space: nowrap;
  border-radius: 0;
  background: var(--well);
  color: var(--quiet);
  font-size: 13px;
  font-weight: 500;
  font-family: inherit;
  text-decoration: none;
  touch-action: manipulation;
  -webkit-tap-highlight-color: transparent;
  box-shadow:
    inset 2px 2px 0 var(--bevel-dark),
    inset -2px -2px 0 var(--bevel-light);
}
.slot:hover { color: var(--item); background: #211A14; }
.slot:focus { outline: none; }
.slot:focus-visible {
  outline: 2px solid var(--item);
  outline-offset: 2px;
}
.slot[aria-current="page"] {
  --slot-ink: var(--item);
  color: var(--item);
  box-shadow:
    inset 0 0 0 3px var(--slot-ink),
    inset -2px -2px 0 var(--bevel-dark),
    inset 2px 2px 0 var(--bevel-light);
}
.slot[data-dim="overworld"][aria-current="page"] { --slot-ink: var(--grass); }
.slot[data-dim="nether"][aria-current="page"] { --slot-ink: var(--netherrack); }
.slot[data-dim="end"][aria-current="page"] { --slot-ink: var(--end-stone); }
.slot[aria-disabled="true"] {
  pointer-events: none;
  opacity: 0.55;
}
.stage { min-height: 0; background: var(--void); }
.map-frame {
  display: block;
  width: 100%;
  height: 100%;
  border: 0;
  background: var(--void);
}
@media (max-width: 639px) {
  .bar {
    grid-template-columns: minmax(0, 1fr) auto;
    grid-template-areas:
      "title note"
      "tabs tabs";
  }
  .note { text-align: left; }
}
@media (pointer: coarse) {
  .slot { height: 44px; min-height: 44px; padding: 0 8px; }
}
@media (prefers-reduced-motion: reduce) {
  .slot { transition: none; }
}
"""

CHROME_JS = """(function () {
  var nav = document.querySelector("[data-map-nav]");
  var frame = document.getElementById("map-frame");
  if (!nav || !frame) return;

  function applyLink(a) {
    if (!a) return;
    var src = a.getAttribute("data-map");
    var label = a.textContent.trim();
    if (src) frame.src = src;
    frame.title = label + " map";
    nav.querySelectorAll("a[data-dim]").forEach(function (el) {
      if (el === a) el.setAttribute("aria-current", "page");
      else el.removeAttribute("aria-current");
    });
  }

  function linkForPath() {
    var path = (location.pathname || "/").replace(/\\/+$/, "") || "/";
    var links = Array.prototype.slice.call(nav.querySelectorAll("a[data-dim]"));
    var match = links.find(function (a) {
      try {
        var p = new URL(a.href, location.href).pathname.replace(/\\/+$/, "") || "/";
        return p === path;
      } catch (e) {
        return false;
      }
    });
    if (match) return match;
    var dim = /\\/nether$/.test(path) ? "nether" : /\\/end$/.test(path) ? "end" : "overworld";
    return nav.querySelector('a[data-dim="' + dim + '"]');
  }

  nav.addEventListener("click", function (e) {
    var a = e.target.closest("a[data-dim]");
    if (!a || e.defaultPrevented) return;
    if (e.metaKey || e.ctrlKey || e.shiftKey || e.altKey || e.button !== 0) return;
    e.preventDefault();
    applyLink(a);
    var href = a.getAttribute("href");
    if (href && history.pushState) history.pushState({ dim: a.getAttribute("data-dim") }, "", href);
  });

  window.addEventListener("popstate", function () {
    applyLink(linkForPath());
  });
})();
"""

# Shared pins: oak night tokens (same as chrome). Runs inside the map.html iframe.
MARKERS_CSS = """:root {
  --void: #161310;
  --binding: #2A2218;
  --item: #E8DCC0;
  --quiet: #9A8E78;
  --grass: #4A7A36;
  --netherrack: #A34B40;
  --end-stone: #C9C07A;
  --well: #1A1510;
  --bevel-dark: #0A0907;
  --bevel-light: #5C4E3C;
  --font: ui-sans-serif, system-ui, "Segoe UI", sans-serif;
}
.leaflet-div-icon.mc-pin-icon {
  background: transparent;
  border: none;
  width: auto !important;
  height: auto !important;
  overflow: visible !important;
}
.mc-pin {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 4px;
  font-family: var(--font);
  pointer-events: auto;
  --pin-ink: var(--item);
}
.mc-pin-slot {
  display: block;
  width: 14px;
  height: 14px;
  border-radius: 0;
  background: var(--well);
  box-shadow:
    inset 2px 2px 0 var(--bevel-dark),
    inset -2px -2px 0 var(--bevel-light),
    inset 0 0 0 3px var(--pin-ink);
}
.mc-pin--overworld { --pin-ink: var(--grass); }
.mc-pin--nether { --pin-ink: var(--netherrack); }
.mc-pin--end { --pin-ink: var(--end-stone); }
.mc-pin-name {
  color: var(--item);
  font-size: 12px;
  font-weight: 500;
  line-height: 1.2;
  letter-spacing: 0.01em;
  text-shadow: 0 1px 2px var(--void);
  white-space: nowrap;
  max-width: 12em;
  overflow: hidden;
  text-overflow: ellipsis;
}
.mc-popup .leaflet-popup-content-wrapper,
.mc-popup .leaflet-popup-tip {
  background: var(--binding);
  color: var(--item);
  border-radius: 0;
  box-shadow: inset 0 1px 0 #3D3428, 0 8px 20px var(--bevel-dark);
}
.mc-popup .leaflet-popup-content {
  margin: 10px 12px;
  min-width: 12rem;
}
.mc-popup,
.mc-popup *,
.mc-panel,
.mc-panel * {
  box-sizing: border-box;
}
.mc-panel {
  display: grid;
  gap: 8px;
  width: 100%;
  min-width: 12rem;
  max-width: 100%;
  font-family: var(--font);
  color: var(--item);
}
.mc-panel-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  min-width: 0;
}
.mc-panel-title {
  font-size: 12px;
  font-weight: 500;
  color: var(--quiet);
}
.mc-panel-close {
  appearance: none;
  flex: 0 0 auto;
  margin: 0;
  padding: 0;
  width: 32px;
  height: 32px;
  border: 0;
  border-radius: 0;
  background: transparent;
  color: var(--quiet);
  font: 500 18px/1 var(--font);
  cursor: pointer;
  touch-action: manipulation;
  -webkit-tap-highlight-color: transparent;
}
.mc-panel-close:hover { color: var(--item); }
.mc-panel-close:focus { outline: none; }
.mc-panel-close:focus-visible {
  outline: 2px solid var(--item);
  outline-offset: 2px;
}
.mc-panel-label {
  display: grid;
  gap: 4px;
  margin: 0;
  min-width: 0;
  font-size: 12px;
  font-weight: 500;
  color: var(--quiet);
}
.mc-panel-input {
  display: block;
  width: 100%;
  max-width: 100%;
  min-width: 0;
  height: 32px;
  padding: 0 8px;
  border: 0;
  border-radius: 0;
  background: var(--well);
  color: var(--item);
  font: 500 13px/1.2 var(--font);
  caret-color: var(--item);
  box-shadow:
    inset 2px 2px 0 var(--bevel-dark),
    inset -2px -2px 0 var(--bevel-light);
}
.mc-panel-input:focus { outline: none; }
.mc-panel-input:focus-visible {
  outline: 2px solid var(--item);
  outline-offset: 2px;
}
.mc-panel-input::selection { background: var(--grass); color: var(--item); }
.mc-panel-err {
  margin: 0;
  font-size: 12px;
  font-weight: 500;
  color: var(--netherrack);
}
.mc-panel-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 4px 12px;
}
.mc-action {
  appearance: none;
  margin: 0;
  padding: 0 4px;
  min-height: 32px;
  border: 0;
  border-radius: 0;
  background: transparent;
  color: var(--item);
  font: 500 13px/1.2 var(--font);
  cursor: pointer;
  touch-action: manipulation;
  -webkit-tap-highlight-color: transparent;
}
.mc-action:hover { color: #fff; }
.mc-action:focus { outline: none; }
.mc-action:focus-visible {
  outline: 2px solid var(--item);
  outline-offset: 2px;
}
.mc-action:disabled {
  opacity: 0.55;
  cursor: default;
}
.mc-action-quiet { color: var(--quiet); }
.mc-action-quiet:hover { color: var(--item); }
.mc-action-danger { color: var(--netherrack); }
.mc-action-danger:hover { color: #c96a5e; }
@media (max-width: 639px) {
  .mc-action { min-height: 44px; min-width: 44px; padding: 0 8px; }
  .mc-panel-close { width: 44px; height: 44px; }
  .mc-panel-input { height: 44px; }
}
@media (prefers-reduced-motion: reduce) {
  .mc-action, .mc-panel-input { transition: none; }
}
.mc-cursor-xz {
  position: absolute;
  z-index: 650;
  pointer-events: none;
  padding: 2px 7px;
  background: var(--binding);
  color: var(--item);
  font: 500 12px/1.2 var(--font);
  letter-spacing: 0.01em;
  white-space: nowrap;
  box-shadow: inset 0 1px 0 #3D3428, 0 2px 8px var(--bevel-dark);
}
@media (pointer: coarse) {
  .mc-cursor-xz { display: none; }
}
"""

MARKERS_JS = """(function () {
  var POLL_MS = 5000;
  var LONG_MS = 500;
  var NAME_MAX = 48;
  var NAME_BYTES = 192;
  var COORD_MAX = 30000000;
  var CAP = 200;
  var MOVE_PX = 12;

  function currentDim() {
    var fromDoc = document.documentElement.getAttribute("data-mc-dim");
    if (fromDoc) return fromDoc;
    var body = document.body && document.body.getAttribute("data-mc-dim");
    if (body) return body;
    var parts = (location.pathname || "").split("/").filter(Boolean);
    var i = parts.indexOf("map.html");
    var folder = i > 0 ? parts[i - 1] : (parts[0] || "");
    if (folder === "nether" || folder === "end" || folder === "overworld") return folder;
    return "overworld";
  }

  function codePoints(s) {
    return Array.from(s).length;
  }

  function utf8Len(s) {
    if (window.TextEncoder) return new TextEncoder().encode(s).length;
    return encodeURIComponent(s).replace(/%../g, "x").length;
  }

  function validateName(raw) {
    var name = String(raw || "").trim();
    if (!name) return { ok: false, name: name, err: "Enter a name." };
    if (/[\\x00-\\x1F]/.test(name)) {
      return { ok: false, name: name, err: "Name cannot include control characters." };
    }
    if (codePoints(name) > NAME_MAX || utf8Len(name) > NAME_BYTES) {
      return { ok: false, name: name, err: "Name is too long." };
    }
    return { ok: true, name: name, err: "" };
  }

  function isCoarsePointer(ev) {
    if (ev && (ev.pointerType === "touch" || ev.pointerType === "pen")) return true;
    return !!(window.matchMedia && window.matchMedia("(pointer: coarse)").matches);
  }

  function skipTarget(node) {
    return !!(node && node.closest && node.closest(".mc-pin, .mc-popup, .mc-panel, .leaflet-control"));
  }

  function whenLeaflet(cb) {
    function hook() {
      if (!window.L || !L.map) return false;
      if (L.map.__mcMarkersHook) return true;
      L.map.__mcMarkersHook = true;
      var orig = L.map;
      L.map = function () {
        var m = orig.apply(this, arguments);
        cb(m);
        return m;
      };
      if (L.Map && L.Map.addInitHook) {
        L.Map.addInitHook(function () { cb(this); });
      }
      return true;
    }
    if (hook()) return;
    var n = 0;
    var t = setInterval(function () {
      n += 1;
      if (hook() || n > 200) clearInterval(t);
    }, 50);
  }

  function start(map) {
    if (!map || map.__mcMarkersStarted) return;
    map.__mcMarkersStarted = true;
    if (map.doubleClickZoom) map.doubleClickZoom.disable();

    var dim = currentDim();
    var layer = L.layerGroup().addTo(map);
    var byId = {};
    var cached = [];
    var draft = null;
    var openPopup = null;
    var lpTimer = null;
    var lpOrigin = null;
    var suppressUntil = 0;

    function mcFromLatlng(ll) {
      return { x: Math.round(ll.lng), z: Math.round(-ll.lat) };
    }

    function latlngFromMc(x, z) {
      return L.latLng(-z, x);
    }

    function pinIcon() {
      var pinClass = (dim === "overworld" || dim === "nether" || dim === "end")
        ? ("mc-pin mc-pin--" + dim)
        : "mc-pin";
      return L.divIcon({
        className: "mc-pin-icon",
        html:
          '<div class="' + pinClass + '">' +
            '<span class="mc-pin-slot" aria-hidden="true"></span>' +
            '<span class="mc-pin-name"></span>' +
          "</div>",
        iconSize: [14, 14],
        iconAnchor: [7, 7],
        popupAnchor: [0, -12]
      });
    }

    function setPinName(marker, name) {
      var el = marker.getElement();
      if (!el) return;
      var label = el.querySelector(".mc-pin-name");
      if (label) label.textContent = name || "";
      el.setAttribute("role", "button");
      el.setAttribute("tabindex", "0");
      el.setAttribute("aria-label", name || "New pin");
    }

    function closeOpen() {
      var p = openPopup;
      openPopup = null;
      if (p && map.closePopup) map.closePopup();
    }

    function clearDraft() {
      if (!draft) return;
      var m = draft.marker;
      draft = null;
      closeOpen();
      if (m) layer.removeLayer(m);
    }

    function setErr(node, msg) {
      if (!node) return;
      node.hidden = !msg;
      node.textContent = msg || "";
    }

    function fieldPanel(opts) {
      var form = document.createElement("form");
      form.className = "mc-panel";
      form.setAttribute("novalidate", "");

      var head = document.createElement("div");
      head.className = "mc-panel-head";
      var title = document.createElement("span");
      title.className = "mc-panel-title";
      title.textContent = "Name";
      var closeBtn = document.createElement("button");
      closeBtn.type = "button";
      closeBtn.className = "mc-panel-close";
      closeBtn.setAttribute("aria-label", "Close");
      closeBtn.textContent = "\\u00d7";
      head.appendChild(title);
      head.appendChild(closeBtn);

      var lab = document.createElement("label");
      lab.className = "mc-panel-label";
      var input = document.createElement("input");
      input.type = "text";
      input.className = "mc-panel-input";
      input.maxLength = NAME_MAX;
      input.autocomplete = "off";
      input.spellcheck = false;
      input.value = opts.value || "";
      input.setAttribute("aria-required", "true");
      input.setAttribute("aria-label", "Name");
      lab.appendChild(input);

      var err = document.createElement("p");
      err.className = "mc-panel-err";
      err.hidden = true;

      var actions = document.createElement("div");
      actions.className = "mc-panel-actions";
      var primary = document.createElement("button");
      primary.type = "submit";
      primary.className = "mc-action";
      primary.textContent = opts.primary;
      var secondary = document.createElement("button");
      secondary.type = "button";
      secondary.className = "mc-action" + (opts.secondaryClass ? " " + opts.secondaryClass : " mc-action-quiet");
      secondary.textContent = opts.secondary;
      actions.appendChild(primary);
      actions.appendChild(secondary);

      form.appendChild(head);
      form.appendChild(lab);
      form.appendChild(err);
      form.appendChild(actions);

      form.addEventListener("submit", function (e) {
        e.preventDefault();
        var v = validateName(input.value);
        if (!v.ok) {
          setErr(err, v.err);
          input.focus();
          return;
        }
        setErr(err, "");
        opts.onPrimary(v.name, err, primary, secondary);
      });
      secondary.addEventListener("click", function () {
        opts.onSecondary(err, primary, secondary);
      });
      closeBtn.addEventListener("click", function () {
        opts.onCancel();
      });
      input.addEventListener("input", function () {
        if (opts.onInput) opts.onInput(input.value);
      });
      form.addEventListener("keydown", function (e) {
        if (e.key === "Escape") {
          e.preventDefault();
          opts.onCancel();
        }
      });
      return { form: form, input: input, err: err };
    }

    function busy(a, b, on) {
      a.disabled = on;
      b.disabled = on;
    }

    function postOp(body) {
      return fetch("/markers", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body)
      }).then(function (r) {
        return r.text().then(function (text) {
          var data = null;
          if (text) {
            try { data = JSON.parse(text); } catch (e) { data = null; }
          }
          return { status: r.status, data: data };
        });
      });
    }

    function applyList(doc) {
      cached = (doc && doc.markers) || [];
      var seen = {};
      cached.forEach(function (rec) {
        if (!rec || rec.dim !== dim || !rec.id) return;
        seen[rec.id] = true;
        if (byId[rec.id]) {
          var m = byId[rec.id];
          m.__mcRec = rec;
          m.setLatLng(latlngFromMc(rec.x, rec.z));
          setPinName(m, rec.name);
        } else {
          byId[rec.id] = addPin(rec);
        }
      });
      Object.keys(byId).forEach(function (id) {
        if (seen[id]) return;
        layer.removeLayer(byId[id]);
        delete byId[id];
      });
    }

    function addPin(rec) {
      var marker = L.marker(latlngFromMc(rec.x, rec.z), {
        icon: pinIcon(),
        keyboard: true,
        zIndexOffset: 600
      });
      marker.__mcRec = rec;
      marker.on("add", function () {
        setPinName(marker, rec.name);
        var el = marker.getElement();
        if (!el || el.__mcKey) return;
        el.__mcKey = true;
        el.addEventListener("keydown", function (ev) {
          if (ev.key === "Enter" || ev.key === " ") {
            ev.preventDefault();
            openEdit(marker);
          }
        });
      });
      marker.addTo(layer);
      setPinName(marker, rec.name);
      marker.on("click", function (e) {
        if (L.DomEvent) L.DomEvent.stop(e);
        openEdit(marker);
      });
      marker.on("dblclick", function (e) {
        if (L.DomEvent) L.DomEvent.stop(e);
      });
      return marker;
    }

    function statusMessage(status) {
      if (status === 409) return "The map already has 200 pins.";
      if (status === 404) return "That pin is gone.";
      return "Could not save. Try again.";
    }

    function bindPanel(marker, ui, selectName) {
      if (map.options) map.options.closePopupOnClick = true;
      var popup = L.popup({
        className: "mc-popup",
        closeButton: false,
        autoClose: true,
        closeOnClick: true,
        closeOnEscapeKey: true,
        maxWidth: 280
      }).setContent(ui.form);
      marker.unbindPopup();
      marker.bindPopup(popup).openPopup();
      openPopup = popup;
      setTimeout(function () {
        ui.input.focus();
        if (selectName) ui.input.select();
      }, 0);
    }

    function openCreate(latlng) {
      clearDraft();
      var xz = mcFromLatlng(latlng);
      if (Math.abs(xz.x) > COORD_MAX || Math.abs(xz.z) > COORD_MAX) return;
      var marker = L.marker(latlng, { icon: pinIcon(), zIndexOffset: 700, keyboard: false });
      marker.on("add", function () { setPinName(marker, ""); });
      marker.addTo(layer);
      var ui = fieldPanel({
        value: "",
        primary: "Add",
        secondary: "Cancel",
        onInput: function (raw) { setPinName(marker, String(raw || "").trim()); },
        onPrimary: function (name, err, primary, secondary) {
          if (cached.length >= CAP) {
            setErr(err, "The map already has 200 pins.");
            return;
          }
          busy(primary, secondary, true);
          postOp({ op: "add", dim: dim, x: xz.x, z: xz.z, name: name }).then(function (res) {
            busy(primary, secondary, false);
            if (res.status === 201 && res.data) {
              clearDraft();
              applyList(res.data);
              return;
            }
            setErr(err, statusMessage(res.status));
          }).catch(function () {
            busy(primary, secondary, false);
            setErr(err, "Could not save. Try again.");
          });
        },
        onSecondary: function () { clearDraft(); },
        onCancel: function () { clearDraft(); }
      });
      bindPanel(marker, ui);
      draft = { marker: marker };
    }

    function openEdit(marker) {
      var rec = marker.__mcRec;
      if (!rec) return;
      clearDraft();
      var ui = fieldPanel({
        value: rec.name || "",
        primary: "Rename",
        secondary: "Delete",
        secondaryClass: "mc-action-danger",
        onPrimary: function (name, err, primary, secondary) {
          busy(primary, secondary, true);
          postOp({ op: "rename", id: rec.id, name: name }).then(function (res) {
            busy(primary, secondary, false);
            if (res.status === 200 && res.data) {
              closeOpen();
              applyList(res.data);
              return;
            }
            setErr(err, statusMessage(res.status));
          }).catch(function () {
            busy(primary, secondary, false);
            setErr(err, "Could not save. Try again.");
          });
        },
        onSecondary: function (err, primary, secondary) {
          busy(primary, secondary, true);
          postOp({ op: "delete", id: rec.id }).then(function (res) {
            busy(primary, secondary, false);
            if (res.status === 200 && res.data) {
              closeOpen();
              applyList(res.data);
              return;
            }
            setErr(err, statusMessage(res.status));
          }).catch(function () {
            busy(primary, secondary, false);
            setErr(err, "Could not save. Try again.");
          });
        },
        onCancel: function () { closeOpen(); }
      });
      bindPanel(marker, ui, true);
    }

    map.on("popupclose", function () {
      openPopup = null;
      if (!draft) return;
      var m = draft.marker;
      draft = null;
      if (m) layer.removeLayer(m);
    });

    map.on("dblclick", function (e) {
      var t = e.originalEvent && e.originalEvent.target;
      if (skipTarget(t)) return;
      if (Date.now() < suppressUntil) return;
      openCreate(e.latlng);
    });

    var container = map.getContainer();
    var hud = document.createElement("div");
    hud.className = "mc-cursor-xz";
    hud.hidden = true;
    hud.setAttribute("aria-hidden", "true");
    container.appendChild(hud);

    function hideHud() {
      hud.hidden = true;
    }
    function finePointer() {
      return !(window.matchMedia && window.matchMedia("(pointer: coarse)").matches);
    }
    map.on("mousemove", function (e) {
      if (!finePointer() || openPopup || draft || !e.latlng) {
        hideHud();
        return;
      }
      var t = e.originalEvent && e.originalEvent.target;
      if (skipTarget(t)) {
        hideHud();
        return;
      }
      var xz = mcFromLatlng(e.latlng);
      hud.textContent = "X: " + xz.x + "   Z: " + xz.z;
      hud.hidden = false;
      var oe = e.originalEvent;
      var rect = container.getBoundingClientRect();
      var left = oe.clientX - rect.left + 14;
      var top = oe.clientY - rect.top + 18;
      var pad = 8;
      if (left + hud.offsetWidth > rect.width - pad) {
        left = oe.clientX - rect.left - hud.offsetWidth - 12;
      }
      if (top + hud.offsetHeight > rect.height - pad) {
        top = oe.clientY - rect.top - hud.offsetHeight - 12;
      }
      if (left < pad) left = pad;
      if (top < pad) top = pad;
      hud.style.left = left + "px";
      hud.style.top = top + "px";
    });
    container.addEventListener("mouseleave", hideHud);
    map.on("popupopen", hideHud);

    function clearLp() {
      if (lpTimer) {
        clearTimeout(lpTimer);
        lpTimer = null;
      }
      lpOrigin = null;
    }
    function onPointerDown(ev) {
      if (!isCoarsePointer(ev)) return;
      if (ev.button != null && ev.button !== 0) return;
      if (skipTarget(ev.target)) return;
      clearLp();
      lpOrigin = { x: ev.clientX, y: ev.clientY };
      var pt = map.mouseEventToLatLng(ev);
      lpTimer = setTimeout(function () {
        lpTimer = null;
        suppressUntil = Date.now() + 800;
        if (pt) openCreate(pt);
      }, LONG_MS);
    }
    function onPointerMove(ev) {
      if (!lpOrigin) return;
      var dx = ev.clientX - lpOrigin.x;
      var dy = ev.clientY - lpOrigin.y;
      if ((dx * dx + dy * dy) > (MOVE_PX * MOVE_PX)) clearLp();
    }
    container.addEventListener("pointerdown", onPointerDown);
    container.addEventListener("pointermove", onPointerMove);
    container.addEventListener("pointerup", clearLp);
    container.addEventListener("pointercancel", clearLp);
    container.addEventListener("contextmenu", function (ev) {
      if (Date.now() < suppressUntil) ev.preventDefault();
    });

    function poll() {
      if (document.visibilityState && document.visibilityState !== "visible") return;
      fetch("/markers", { cache: "no-store" })
        .then(function (r) { return r.json(); })
        .then(applyList)
        .catch(function () {});
    }
    poll();
    setInterval(poll, POLL_MS);
    document.addEventListener("visibilitychange", function () {
      if (document.visibilityState === "visible") poll();
    });
  }

  whenLeaflet(start);
})();
"""

_MARKERS_LINK = '<link rel="stylesheet" href="/markers.css">'
_MARKERS_SCRIPT = '<script src="/markers.js"></script>'
_LEAFLET_SCRIPT_RE = re.compile(
    r'<script\b[^>]*\bsrc=["\'][^"\']*leaflet[^"\']*["\'][^>]*>\s*</script>',
    re.IGNORECASE,
)
_CREATE_MAP_RE = re.compile(
    r'<script\b[^>]*>\s*createMap\s*\(\s*\)\s*;?\s*</script>',
    re.IGNORECASE,
)
_HEAD_CLOSE_RE = re.compile(r"</head>", re.IGNORECASE)
_BODY_CLOSE_RE = re.compile(r"</body>", re.IGNORECASE)
_HTML_OPEN_RE = re.compile(r"<html\b[^>]*>", re.IGNORECASE)
_MC_DIM_ATTR_RE = re.compile(r'\s*data-mc-dim="[^"]*"')


def _ensure_mc_dim(text: str, dim: str) -> str:
    attr = f'data-mc-dim="{html.escape(dim, quote=True)}"'
    found = _HTML_OPEN_RE.search(text)
    if not found:
        return text
    tag = found.group(0)
    if _MC_DIM_ATTR_RE.search(tag):
        new_tag = _MC_DIM_ATTR_RE.sub(" " + attr, tag, count=1)
    else:
        new_tag = tag[:-1] + f" {attr}>"
    return text[: found.start()] + new_tag + text[found.end() :]


def hook_map_html(path: Path, dim: str = "overworld") -> None:
    """Append markers assets onto stock map.html after Leaflet, before createMap.

    createMap() fetches info.json then calls L.map. Loading markers.js before that
    call lets us wrap L.map without forking MinedMap.js. Sets data-mc-dim on <html>
    so pins use the product dim id (including nested ns/path extras).
    """
    text = path.read_text(encoding="utf-8")
    if 'src="/markers.js"' not in text:
        hook = f"{_MARKERS_LINK}\n{_MARKERS_SCRIPT}\n"
        found = _LEAFLET_SCRIPT_RE.search(text)
        if found:
            text = text[: found.end()] + "\n" + hook + text[found.end() :]
        else:
            created = _CREATE_MAP_RE.search(text)
            if created:
                text = text[: created.start()] + hook + text[created.start() :]
            else:
                head = _HEAD_CLOSE_RE.search(text)
                if head:
                    text = text[: head.start()] + hook + text[head.start() :]
                else:
                    body = _BODY_CLOSE_RE.search(text)
                    if body:
                        text = text[: body.start()] + hook + text[body.start() :]
                    else:
                        if text and not text.endswith("\n"):
                            text += "\n"
                        text += hook
    path.write_text(_ensure_mc_dim(text, dim), encoding="utf-8")


def extra_tab_label(dim_id: str, extra_ids: list[str]) -> str:
    """Title-case the last path segment; prefix ns when that label is shared."""
    last = dim_id.rsplit("/", 1)[-1]
    pretty = " ".join(part.capitalize() for part in last.split("_"))
    lasts = [item.rsplit("/", 1)[-1] for item in extra_ids]
    if lasts.count(last) > 1:
        ns = dim_id.split("/", 1)[0]
        return f"{ns} {pretty}"
    return pretty


def tab_label(dim: str, live: list[str]) -> str:
    if dim in DIM_LABELS:
        return DIM_LABELS[dim]
    extras = [item for item in live if item not in DIM_LABELS]
    return extra_tab_label(dim, extras)


def _chrome_hrefs(dims: list[str] | None = None) -> dict[str, tuple[str, str]]:
    """dim -> (page href, map.html src). Root-relative so in-page tab switches stay valid."""
    hrefs: dict[str, tuple[str, str]] = {
        "overworld": ("/", "/overworld/map.html"),
        "nether": ("/nether/", "/nether/map.html"),
        "end": ("/end/", "/end/map.html"),
    }
    for dim in dims or []:
        if dim in hrefs:
            continue
        hrefs[dim] = (f"/{dim}/", f"/{dim}/map.html")
    return hrefs


def chrome_html(dim: str, *, at_root: bool = False, dims: list[str] | None = None) -> str:
    _ = at_root  # same root-relative assets on / and /overworld/
    live = list(dims) if dims is not None else list(DIMS)
    hrefs = _chrome_hrefs(live)
    css = "/chrome.css"
    js = "/chrome.js"
    iframe_src = hrefs.get(dim, hrefs["overworld"])[1]
    iframe_title = html.escape(f"{tab_label(dim, live)} map")
    slots = []
    for name in live:
        page_href, map_src = hrefs[name]
        current = ' aria-current="page"' if name == dim else ""
        title_attr = ""
        if name not in DIM_LABELS:
            title_attr = f' title="{html.escape(name, quote=True)}"'
        slots.append(
            f'<a class="slot" data-dim="{html.escape(name, quote=True)}" data-map="{html.escape(map_src, quote=True)}" '
            f'href="{html.escape(page_href, quote=True)}"{current}{title_attr}>'
            f"{html.escape(tab_label(name, live))}</a>"
        )
    slot_markup = "\n        ".join(slots)
    return f"""<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <meta name="theme-color" content="#161310">
  <title>Map</title>
  <link rel="stylesheet" href="{css}">
</head>
<body>
  <a class="skip" href="#map-frame">Skip to map</a>
  <div class="shell">
    <header class="bar">
      <h1 class="brand">Map</h1>
      <nav class="dims" data-map-nav aria-label="Dimension">
        {slot_markup}
      </nav>
      <p class="note">Explored terrain only</p>
    </header>
    <main class="stage">
      <iframe class="map-frame" id="map-frame" title="{iframe_title}" src="{iframe_src}"></iframe>
    </main>
  </div>
  <script src="{js}"></script>
</body>
</html>
"""



def utc_iso() -> str:
    return datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def load_config(path: str | None = None) -> dict:
    cfg_path = path or CONFIG_PATH
    with open(cfg_path, "r", encoding="utf-8") as f:
        return json.load(f)


def map_root(cfg: dict) -> Path:
    return Path(cfg.get("player_map_root") or DEFAULT_MAP_ROOT)


def world_path(cfg: dict) -> Path:
    return Path(cfg.get("world_path") or DEFAULT_WORLD)


def minedmap_bin(cfg: dict) -> Path:
    return Path(cfg.get("minedmap_path") or DEFAULT_MINEDMAP)


def tree_bytes(path: Path) -> int:
    total = 0
    if not path.exists():
        return 0
    if path.is_file():
        return path.stat().st_size
    for root, _dirs, files in os.walk(path):
        for name in files:
            fp = Path(root) / name
            try:
                total += fp.stat().st_size
            except OSError:
                pass
    return total


def _has_region_files(region: Path) -> bool:
    try:
        next(region.glob("*.mca"))
        return True
    except StopIteration:
        return False


def detect_region_dir(world: Path, dim: str) -> Path | None:
    """Return the live region/ directory for a dimension, or None."""
    if dim == "overworld":
        candidates = [
            world / "dimensions" / "minecraft" / "overworld" / "region",
            world / "region",
        ]
    elif dim == "nether":
        candidates = [
            world / "dimensions" / "minecraft" / "the_nether" / "region",
            world.parent / "world_nether" / "region",
            world.parent / "world_nether" / "DIM-1" / "region",
            world / "DIM-1" / "region",
        ]
    elif dim == "end":
        candidates = [
            world / "dimensions" / "minecraft" / "the_end" / "region",
            world.parent / "world_the_end" / "region",
            world.parent / "world_the_end" / "DIM1" / "region",
            world / "DIM1" / "region",
        ]
    elif "/" in dim:
        ns, path = dim.split("/", 1)
        candidates = [world / "dimensions" / ns / path / "region"]
    else:
        return None

    for cand in candidates:
        if cand.is_dir() and _has_region_files(cand):
            return cand
    for cand in candidates:
        if cand.is_dir():
            return cand
    return None


def extra_dimension_ids(world: Path) -> list[str]:
    """Namespaced dimensions beyond Overworld/Nether/End with at least one .mca."""
    root = world / "dimensions"
    found: list[str] = []
    if not root.is_dir():
        return found
    for ns in sorted(root.iterdir()):
        if not ns.is_dir():
            continue
        for name in sorted(ns.iterdir()):
            if not name.is_dir():
                continue
            if (ns.name, name.name) in _VANILLA_DIM_FOLDERS:
                continue
            region = name / "region"
            if region.is_dir() and _has_region_files(region):
                found.append(f"{ns.name}/{name.name}")
    return found


def extras_for_tick(ids: list[str]) -> tuple[list[str], list[str]]:
    """First EXTRA_DIM_CAP extras (already sorted); remainder is over-cap skip."""
    return ids[:EXTRA_DIM_CAP], ids[EXTRA_DIM_CAP:]


def prepare_shim(world: Path, region: Path, shim_root: Path) -> Path:
    """MinedMap 2.8.0 has no --dimension flag. Nether/End need a fake world dir."""
    shim_root.mkdir(parents=True, exist_ok=True)
    level = world / "level.dat"
    link_level = shim_root / "level.dat"
    link_region = shim_root / "region"
    if link_level.exists() or link_level.is_symlink():
        link_level.unlink()
    if link_region.exists() or link_region.is_symlink():
        if link_region.is_dir() and not link_region.is_symlink():
            shutil.rmtree(link_region)
        else:
            link_region.unlink()
    if level.is_file():
        os.symlink(level, link_level)
    os.symlink(region, link_region)
    return shim_root


def overworld_input(world: Path) -> Path:
    return world


def copy_viewer_into(dest: Path, viewer_src: Path) -> None:
    if dest.exists():
        shutil.rmtree(dest)
    dest.mkdir(parents=True, exist_ok=True)
    if not viewer_src.is_dir():
        raise FileNotFoundError(f"viewer template missing: {viewer_src}")
    for item in viewer_src.iterdir():
        if item.name == "data":
            continue
        target = dest / item.name
        if item.is_dir():
            shutil.copytree(item, target, symlinks=False)
        else:
            shutil.copy2(item, target)


def copy_tiles_without_processed(src: Path, dest_data: Path) -> None:
    dest_data.mkdir(parents=True, exist_ok=True)
    if not src.is_dir():
        return
    for item in src.iterdir():
        if item.name in SKIP_PUBLISH_NAMES:
            continue
        target = dest_data / item.name
        if item.is_dir():
            shutil.copytree(item, target, dirs_exist_ok=True)
        else:
            shutil.copy2(item, target)


def install_chrome(dest: Path, dim: str, *, at_root: bool = False, dims: list[str] | None = None) -> None:
    """Wrap a copied MinedMap viewer: keep stock page as map.html, write product chrome."""
    dest.mkdir(parents=True, exist_ok=True)
    if not at_root:
        stock = dest / "index.html"
        if stock.is_file():
            stock.replace(dest / "map.html")
        map_html = dest / "map.html"
        if map_html.is_file():
            hook_map_html(map_html, dim)
    (dest / "index.html").write_text(
        chrome_html(dim, at_root=at_root, dims=dims), encoding="utf-8"
    )


def write_switcher(publish: Path, dims: list[str] | None = None) -> None:
    live = list(dims) if dims is not None else list(DIMS)
    publish.mkdir(parents=True, exist_ok=True)
    (publish / "chrome.css").write_text(CHROME_CSS, encoding="utf-8")
    (publish / "chrome.js").write_text(CHROME_JS, encoding="utf-8")
    (publish / "markers.css").write_text(MARKERS_CSS, encoding="utf-8")
    (publish / "markers.js").write_text(MARKERS_JS, encoding="utf-8")
    install_chrome(publish, "overworld", at_root=True, dims=live)


def sha256_file(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def build_tar_and_manifest(publish: Path, http_root: Path) -> dict:
    http_root.mkdir(parents=True, exist_ok=True)
    tar_path = http_root / "tiles.tar"
    tmp_tar = http_root / "tiles.tar.tmp"
    if tmp_tar.exists():
        tmp_tar.unlink()
    subprocess.run(
        ["tar", "-C", str(publish), "-cf", str(tmp_tar), "."],
        check=True,
    )
    tmp_tar.replace(tar_path)
    digest = sha256_file(tar_path)
    size = tar_path.stat().st_size
    doc = {
        "sha256": digest,
        "bytes": size,
        "rendered_at": utc_iso(),
    }
    (http_root / "manifest.json").write_text(json.dumps(doc) + "\n", encoding="utf-8")
    return doc


def minecraft_active(unit: str) -> bool:
    r = subprocess.run(
        ["systemctl", "is-active", unit],
        capture_output=True,
        text=True,
        check=False,
    )
    return r.returncode == 0 and r.stdout.strip() == "active"


def rcon_save_flush(cfg: dict) -> None:
    with RconClient(
        cfg.get("rcon_host", "127.0.0.1"),
        int(cfg.get("rcon_port", 25575)),
        cfg.get("rcon_password", ""),
    ) as r:
        r.command("save-all flush")


def run_minedmap(
    binary: Path,
    input_dir: Path,
    output_dir: Path,
    *,
    runner: Callable[..., subprocess.CompletedProcess] | None = None,
) -> subprocess.CompletedProcess:
    output_dir.mkdir(parents=True, exist_ok=True)
    cmd = [
        "nice",
        "-n",
        "19",
        str(binary),
        "--image-format",
        "webp",
        "-j",
        "1",
        str(input_dir),
        str(output_dir),
    ]
    run = runner or subprocess.run
    return run(
        cmd,
        check=False,
        capture_output=True,
        text=True,
        timeout=RENDER_TIMEOUT_SEC,
    )


def publish_from_render(root: Path, dims: list[str] | None = None) -> None:
    live = list(dims) if dims is not None else list(DIMS)
    viewer = root / "viewer"
    render = root / "render"
    publish = root / "publish"
    staging = root / "publish.next"
    if staging.exists():
        shutil.rmtree(staging)
    staging.mkdir(parents=True, exist_ok=True)
    write_switcher(staging, live)
    for dim in live:
        dest = staging / dim
        copy_viewer_into(dest, viewer)
        install_chrome(dest, dim, at_root=False, dims=live)
        copy_tiles_without_processed(render / dim, dest / "data")
    if publish.exists():
        shutil.rmtree(publish)
    staging.replace(publish)


def tick(
    cfg: dict,
    *,
    game_up: bool | None = None,
    save_flush: Callable[[], None] | None = None,
    runner: Callable[..., subprocess.CompletedProcess] | None = None,
) -> str:
    unit = cfg.get("minecraft_unit", "minecraft")
    if game_up is None:
        game_up = minecraft_active(unit)
    if not game_up:
        return "player-map skip (minecraft down)"

    binary = minedmap_bin(cfg)
    if not binary.is_file():
        return f"player-map skip (minedmap missing: {binary})"

    world = world_path(cfg)
    if not world.is_dir():
        return f"player-map skip (world missing: {world})"

    root = map_root(cfg)
    root.mkdir(parents=True, exist_ok=True)
    extras_all = extra_dimension_ids(world)
    extras, over_cap = extras_for_tick(extras_all)

    flush = save_flush or (lambda: rcon_save_flush(cfg))
    try:
        flush()
    except (RconError, OSError, TimeoutError) as exc:
        return f"player-map skip (save-all flush failed: {exc})"

    rendered: list[str] = []
    failed_extras: list[str] = []
    for dim in list(DIMS) + extras:
        region = detect_region_dir(world, dim)
        if region is None:
            continue
        is_extra = dim not in DIMS
        if dim == "overworld":
            input_dir = overworld_input(world)
        else:
            input_dir = prepare_shim(world, region, root / "shim" / dim)
        out_dir = root / "render" / dim
        try:
            proc = run_minedmap(binary, input_dir, out_dir, runner=runner)
        except subprocess.TimeoutExpired:
            if is_extra:
                failed_extras.append(dim)
                continue
            return f"player-map fail ({dim} render timed out)"
        if proc.returncode != 0:
            err = (proc.stderr or proc.stdout or "").strip().splitlines()
            tail = err[-1] if err else f"exit {proc.returncode}"
            if is_extra:
                failed_extras.append(dim)
                continue
            return f"player-map fail ({dim}: {tail})"
        rendered.append(dim)

    notes: list[str] = []
    if over_cap:
        notes.append(f"skipped extra dimensions over cap {', '.join(over_cap)}")
    if failed_extras:
        notes.append(f"skipped extra dimensions failed {', '.join(failed_extras)}")
    extra_note = f"; {'; '.join(notes)}" if notes else ""

    if not rendered:
        return "player-map skip (no region dirs)" + extra_note

    render_bytes = tree_bytes(root / "render")
    if render_bytes > CAP_BYTES:
        return f"player-map skip (render tree {render_bytes} bytes over 2 GiB cap)"

    publish_from_render(root, rendered)
    pub_bytes = tree_bytes(root / "publish")
    if pub_bytes > CAP_BYTES:
        return f"player-map skip (publish tree {pub_bytes} bytes over 2 GiB cap)"

    http_root = root / "http"
    build_tar_and_manifest(root / "publish", http_root)
    tar_bytes = tree_bytes(http_root / "tiles.tar")
    if tar_bytes > CAP_BYTES:
        return f"player-map skip (tiles.tar {tar_bytes} bytes over 2 GiB cap)"

    return f"player-map rendered {', '.join(rendered)}{extra_note}"


def main() -> int:
    cfg = load_config()
    try:
        print(tick(cfg))
    except Exception as exc:  # noqa: BLE001
        print(f"player-map tick failed: {exc}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
