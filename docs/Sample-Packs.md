# Sample modpacks (operator PC, gitignored)

**Audience:** agents implementing pack import (V1 Phase 4, Step **4.13** robustness, later pack-related tests).  
**On disk:** gitignored [`data/sample-packs/`](../data/sample-packs/) in this repo (covered by the existing `data/` gitignore). Exact filenames on this machine are listed in `data/sample-packs/README.txt`.

This product **must not redistribute** pack contents (`PRODUCT-IDEAS.md`). Real `.mrpack` / CurseForge zips / jars stay on the operator PC only. Do not copy them into `tests/`, git, Object Storage, or the Manager installer.

## Two layers

| Layer | Where | Use for |
|-------|--------|---------|
| **CI / unit tests** | Tracked `tests/fixtures/` (blueprint [§15](Minecraft-Server-Deployment-Blueprint.md#15-offline-fixtures-and-tests-for-version-metadata)) | Offline parse of synthetic `modrinth.index.json` / `manifest.json` / tiny dummy archives. No network, no licensed packs. |
| **Operator samples** | Gitignored `data/sample-packs/` | Format × loader reference and optional live smoke. Homemade archives first; real published packs second. |

Do **not** point default CI at `data/sample-packs/`. That folder is absent on a clean clone.

## What is already collected

`homemade/` (always prefer these for parser work):

- `fabric-strip.mrpack` — Fabric 1.21.1; Fabric API + Lithium `env.server=required`, Sodium `unsupported`.
- `manual-server.zip` — unstructured `mods/` + `config/` (Step 4.9).
- `curseforge-synthetic.zip` — fake CurseForge **client-export** shape (manifest IDs, no jars). **Do not** call the live CurseForge API. Step 4.12 is deferred; Setup refuses this shape (P7).

`real/` (published exports unless noted; Minecraft **1.21.1** unless the filename says otherwise):

- Modrinth Fabric + matching CurseForge export of **Fabulously Optimized** (same pack, two formats).
- Extra Modrinth Fabric: OptiFine for Fabric; **MMC3** (`MMC3 [FABRIC] 1.21.1 v18`); **Simply Optimized Continued** (`v2.1+26.2`, Minecraft **26.2**).
- Modrinth NeoForge: **BlockFront** (tiny; best real NeoForge sample) and Lucky Block Challenge (has an embedded world under `overrides/saves/`).
- CurseForge Forge **1.20.1** Infinite Horizons and Forge **1.12.2** Modded Superflat Survival (same CurseForge project family; two Forge eras).
- **Custom jar-root zip:** `custom-forge-1.20.1-MilesPack.zip` — not a published Modrinth/CurseForge pack. User-made Forge 1.20.1 archive of **jars at the zip root** (no `mods/` folder, no manifest). ~300 MB / 77 jars + one `.disabled`. Step **4.13 R3** target; **not** a Pass 2 Deploy pack.

There is **no** CurseForge “Server Files” zip in the set. Mega-packs (Better MC, ATM, RLCraft, …) are **not** wanted as defaults.

## Gotchas

1. **Many Fabric “optimized” packs mark every file `env.server = required`**, including obvious client-only mods (Fabulously Optimized, OptiFine for Fabric, MMC3, Simply Optimized Continued). They are **not** a valid “strip using `env.server` only” test — use `homemade/fabric-strip.mrpack` (and BlockFront, which tags Sodium `unsupported` correctly) for that. They **are** the right samples for Step **4.13** override-list stripping. Prefer **Simply Optimized Continued** (~9 KB) for routine mis-tag analyze; FO and MMC3 are heavier (MMC3 is ~57 MB with thousands of override files). The CurseForge FO *client* export is **not** a v1 import target (Step 4.12 deferred). If a **Server Files** zip is added later, that is the 4.9 / R3 path.
2. **Infinite Horizons (Forge 1.20.1) is ~305 mods / ~20 MB.** Correct argfile-era Forge export, too heavy for routine parser tests. Confirm the 1.20.1 shape once, then use homemade + the 1.12.2 pack for day-to-day work.
3. **MilesPack** has **no folders and no manifest** — only jars (plus one `.disabled`) at the archive root. Today’s 4.9 analyzer looks for `mods/`. R3 must treat this as unstructured server mods. It includes known client-only jars (embeddium, entityculling, ImmediatelyFast, entity model/texture features, …). Too large to Deploy on TESTING as the Pass 2 pack.

## Which file for which V1 step

| Step | Use |
|------|-----|
| 4.7 analyze `.mrpack` | `homemade/fabric-strip.mrpack`, then one small real Modrinth pack |
| 4.8 install `.mrpack` | `fabric-strip.mrpack` (real CDN URLs) into a **temp dir** — not the live Forge lab |
| 4.9 manual zip | `homemade/manual-server.zip` (CurseForge **Server Files** if the operator adds one) |
| 4.12 CurseForge API | **Deferred** — do **not** call the live CurseForge API; do not implement a mocked resolver unless the operator reopens 4.12 |
| **4.13 R1** | Tracked `tests/fixtures/pack-lists/` + vendored `docs/*-exclude-include.json` (no real packs) |
| **4.13 R2** | CI mistag fixture + `fabric-strip.mrpack`; optional analyze `Simply-Optimized-Continued` |
| **4.13 R3** | CI jar-root fixture; optional analyze MilesPack (temp dir only); P7 synthetic CF still refuse |
| **4.13 R4** | Setup analyze only (no Deploy): Simply Optimized warning; fabric-strip no mis-tag warning; synthetic CF blocked |

Live **test** VM1: at most one small Fabric or NeoForge pack; disable idle for the session; never the live Forge lab. **Do not Deploy** during 4.13.

**V1 QA Pass 2 (greenfield):** **paused** until 4.13 exits. Pack row will be updated then. Current default remains Modrinth NeoForge **BlockFront**; still do **not** Deploy MMC3, MilesPack, Infinite Horizons, or a CurseForge client export. See [`V1-QA-Pass-2-Scope.md`](V1-QA-Pass-2-Scope.md).

## Missing a pack? Ask the operator

Agents **must not** go hunting the internet for “a few popular packs” or download kitchen-sink zips on their own initiative.

If a step needs a format, loader, Minecraft era, or pack property that is **not** in `data/sample-packs/` (examples: CurseForge Server Files, a Quilt `quilt-loader` `.mrpack`, a small NeoForge CurseForge export):

1. **Pause.**
2. Tell the operator exactly what is needed (platform, loader, Minecraft version, size bound, why).
3. Wait for them to drop a file into `data/sample-packs/` and say so.

Do not add an in-app catalog/search to obtain samples. That product idea is **rejected** (`PRODUCT-IDEAS.md` / blueprint §2.4): users (and the operator) always obtain pack files outside the Manager. Do not commit the file they download.
