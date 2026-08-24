# Pack import — intended design

**Status:** Living **product spec** (operator 2026-08-23). Implementation queue: Step **8.9** ([`V1-Pack-Import-Assisted-Review-Plan.md`](V1-Pack-Import-Assisted-Review-Plan.md)); live pointer [`NEXT.md`](NEXT.md).  
Pass 3 stays blocked until 8.9 completes and the operator says otherwise.  
**Code today** still auto-keeps unknown homemade jars after a warning until 8.9 ships. This file is the **target** contract.

**Authority:** operator will (this chat) wins. When this file disagrees with [`PRODUCT-IDEAS.md`](PRODUCT-IDEAS.md), follow **this file** and note drift. Mechanism details stay in the blueprint (named §§ only) and Core analyzers.

**Related:** [`PRODUCT-IDEAS.md`](PRODUCT-IDEAS.md) Modded branch · [`Sample-Packs.md`](Sample-Packs.md) · [`Guide.md`](Guide.md) (update **when code ships**, not at design lock) · Layer 3 quarantine is already v1 ([Step 8.8 P10](V1-Implementation-Plan.md)).

---

## Goal

Support **local file import only** (picker / drag-and-drop). Automate what is **high-confidence**. When the pack is a client dump or metadata is missing, **detect and pinpoint**, then let the operator **fix it in the app** — not by editing JSON by hand.

Homemade zips of client jars **stay supported**. The promise that they **boot unattended** is dropped. Reliability for that path is **assisted review + dependency freeze + crash follow-up**, not a smarter bytecode oracle.

Non-experts who do not want to classify mods should use a **Modrinth `.mrpack`** or a CurseForge **Server Files** zip. The app should say that in copy. Homemade zip is the fallback for people who have a folder of jars and will click through a review (and maybe one named crash).

---

## Formats (v1)

| Input | Treat as | Continue? |
|--------|----------|-----------|
| Modrinth **`.mrpack`** with usable `env.server` | **Automatic** | Yes after the existing friend-pack checkboxes. Unclear `env.server` after filters still **blocks** (do not guess). |
| Zip that is a **server layout** (`mods/` + configs, or filled CurseForge **Server Files**) | **Automatic** when leftover unknowns are few; **assisted** when many jars have no side (same thresholds as today’s high-unclear warning, or the review UI — implementer’s choice, keep it consistent). | Yes after identity + review rules below. |
| **Jar-root** / unstructured zip (jars at archive root, no `mods/`) | **Assisted** | Identity confirm (already v1) **and** unknown-side review. Save a **derived copy** with sidecar (already v1) for Download pack. |
| CurseForge **client** export (manifest IDs, no jars / mixed IDs) | **Refuse** | Guide: download Server Files or a `.mrpack`. No product CF API key. |
| Quilt loader | **Refuse install** (detect + explain). | Not a Setup entry in v1. |
| Unknown loader / no Minecraft version after confirm | **Refuse** | Operator must correct identity or pick another file. |
| Launcher client zip (`options.txt` / shaders as the only signal, no mods) | **Refuse** | Already v1. |

No in-app Modrinth/CurseForge/FTB **catalog**. Fetching URLs **already named** inside an uploaded `.mrpack` remains import plumbing.

**Optional copy (not a catalog):** if the file looks like a MultiMC/Prism **client** instance, say that exporting a Modrinth `.mrpack` from Prism (which can tag `env.server`) is easier than reviewing dozens of unknown jars. Do not require that export.

---

## Tiers

### Automatic

High-confidence skip + install. Used for well-formed `.mrpack` and clean server zips.

Operator still sees the summary (name, Minecraft, loader, Java, skip counts, friend-pack checkboxes). They do **not** have to classify unknown jars when there are none.

### Assisted

Used when the archive is unstructured **or** a material set of mod jars still have **unknown side** after automatic skips.

**Cannot continue** until:

1. Identity is complete: Minecraft version, loader (`fabric` / `forge` / `neoforge`), loader version, Java major — detected or operator-corrected (jar-root already has this).
2. The unknown-side list is **acknowledged**: default **Keep** all unknowns, or per-jar **Skip on server**, then a Continue control.
3. Existing friend-pack checkboxes remain.

No more “64 unknown sides, keep, Deploy” as the only gate.

---

## Skip order

Run in this order. Re-run **dependency freeze** after the operator marks Skip.

1. **Force-include** (Layer 2 local overlay, then Layer 1 itzg/product include). Wins over client tags.
2. Pack **`env.server = unsupported`** (`.mrpack` only).
3. **Exclude lists** (Layer 1 itzg JSON, Layer 2 product + per-archive local overlay).
4. **High-confidence in-jar client** (explicit Fabric/Quilt environment / client-only entrypoints; Forge/NeoForge `clientSideOnly` / `side=CLIENT`; common mixin **class** annotated `@OnlyIn(CLIENT)` / `@Environment(CLIENT)`). Do **not** treat `displayTest` / `IGNORE_SERVER_VERSION` as client-only. Do **not** strip a dual-side library just because one common mixin **targets** a client class (CoFH Core class of false positive).
5. **Dependency freeze** (below).
6. **Operator Skip** marks from the review UI (persisted per archive hash).
7. Remaining **unknown → Keep** (server assumed). Same bias as itzg.

`.mrpack` files that are **still unclear** on `env.server` after 1–5 **fail** (do not guess). Manual/jar-root unknowns go to the review list instead of a hard fail.

---

## Dependency freeze

**Never skip a jar that a kept jar declares as a required dependency.**

This would have blocked skipping CoFH Core while keeping Thermal.

| Case | Behavior |
|------|----------|
| Required dep in `mods.toml` / `neoforge.mods.toml` / `fabric.mod.json` | If skip-A would break kept-B, **do not skip A**. Put A in **Must keep** with “required by B”. |
| Optional / embedded / jar-in-jar | Do not force-keep a sibling already classified client-only. |
| Operator then **force-skips** A | **Block install** (or equivalent hard warn they must dismiss by unskipping A). Name B. Do not boot into “mod X requires Y”. |
| Missing or unreadable metadata | No edge. Jar may stay in **Needs your call**. |

Apply after automatic skips and **again** after user Skip ticks, before install.

---

## Review UI (assisted)

Not a dump of 60 filenames with no help. Three groups:

1. **Will skip** (automatic) — read-only, with why (list / `env.server` / in-jar).
2. **Needs your call** — unknown side, **and** not required by a kept jar. Default **Keep**. Optional **Skip on server**. Search if the list is long.
3. **Must keep** — required dependency of something being kept. Locked. Short reason.

Plus the existing identity fields for unstructured packs (Minecraft, loader, loader version, Java).

Copy should say: *We skip obvious client mods. Everything else stays unless you mark it. If the server crashes and the game names one mod, you can exclude it here.*

Do **not** require an explicit Keep/Skip on every unknown row (people will click through blindly). Default-Keep plus optional Skip is the easy path.

Persist per-archive-SHA on the admin PC (Layer 2 local overlay already exists). Same file later → same answers. If the zip bytes change, treat it as a new archive (or show a short “this file changed” note — implementer picks one).

---

## Crash follow-up (already v1; keep these bounds)

Health: crash-loop / FATAL fails fast with a capped journal; success is still RCON `list`.

Layer 3: when the loader blames **exactly one** mod → move jar to `mods.quarantined/` (never delete), retry **once**, record `modpack.quarantined_files`. Operator **Keep excluded** (feeds local Layer 2 for this archive) or **Put back**.

Several blamed mods, or no loader report → normal crash with log. **Do not** auto-strip. **Do not** loop “strip until RCON comes up.”

Assisted review reduces some first crashes. It will not make every client dump boot on the first try. Plan the loop: **review → install → maybe one named crash → Keep excluded**.

---

## Loaders and platforms (v1 surface)

**In:** Fabric, Forge, NeoForge. Paper = Optimized Vanilla, not a mod loader.  
**Out of this spec / not this wave:** Quilt Setup entry, CurseForge **API** (jar-less client export), FTB/Technic/ATLauncher/GDLauncher adapters, hybrid loaders (Mohist, Magma, Arclight, Folia), Bedrock.

itzg `TYPE=` scripts are a **reference**, not something to port. Each extra loader here is Manager analyze + Java floor + on-box installer + unit args + tests. Quilt is the closest “later” loader and is still a real project.

---

## Non-goals

- Fully automatic “client folder → dedicated server” with no operator Minecraft knowledge.
- Growing in-jar heuristics until homemade packs stop crashing.
- Copying itzg’s full TYPE matrix or running the itzg Docker image as the game runtime.
- In-app mod/modpack browser (already **rejected**).
- Turning Layer 3 into unbounded auto-strip.
- Encoding MilesPack (or any one test pack) jar names into the product denylist.
- Shipping this UI in Pass 3 or as a drive-by during QA.

---

## Implementation notes (when NEXT names it)

Suggested order is locked in [`V1-Pack-Import-Assisted-Review-Plan.md`](V1-Pack-Import-Assisted-Review-Plan.md):

1. **P1** — Dependency freeze + skip-order fix + review grouping in Core (testable without Hybrid).
2. **P2** — Unknown-side review UI + persist Skip (Setup + Change pack) + copy in [`Guide.md`](Guide.md).

Until P2 ships: do not treat informal homemade-zip failures as proof the **loader/Java/bootstrap** pipeline is wrong. Prefer well-formed `.mrpack` / Server Files for “must Just Work” checks.

---

## Changelog

| Date | Note |
|------|------|
| 2026-08-23 | **Scheduled** as Step **8.9** ([`V1-Pack-Import-Assisted-Review-Plan.md`](V1-Pack-Import-Assisted-Review-Plan.md)). P1 NEXT. |
| 2026-08-23 | **Design lock.** Homemade zip kept; unattended success dropped. Automatic vs assisted tiers, skip order, dep freeze, review UI, crash bounds, non-goals. No code. Pass 3 unchanged. |
