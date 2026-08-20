# V1 modpack robustness — exclude lists + mixed archives

**Status:** Living. Created 2026-08-20 (docs only). **NEXT = R2**.  
**Parent:** [`V1-Implementation-Plan.md`](V1-Implementation-Plan.md) Step **4.13**.  
**Why now:** operator 2026-08-20 — do this **before** QA Pass 2 (Step **8.5.2**) so Modded greenfield is not tested twice.  
**Design SoT:** blueprint **§24.3** (Layers 1–2 this plan; Layer 3 **parked**), **§22.1** (trust `env.server` then override), **§23.3** (CurseForge has no side field).

This file’s creation session **must not implement code**. Later agents implement **only the single section marked NEXT**.

**Cost:** $0. TESTING profile only. Never `DEFAULT` / live Forge lab. Never Minecraft `0.0.0.0/0`.  
**Tofu:** do **not** `tofu apply` / `destroy` in this plan. Pass 2 Phase A still owns greenfield destroy.  
**SSH:** R1–R3 are Core/fixtures (no VM required). R4 is Setup UI. Do not Deploy a pack to TESTING in this plan.  
**Functions:** unused here.

---

## How agents must use this file

1. Read **this protocol**, the [Progress dashboard](#progress-dashboard), and **only the NEXT section**.  
2. Implement only that section. Do not start neighbors “while you are here.”  
3. After finishing: mark **DONE**, set the next incomplete section to **NEXT**, changelog line, update V1 plan Step **4.13** + dashboard, **stop**.  
4. Never create git commits. Suggest a message.  
5. Do **not** start Step **8.5.2**, **8.6.1**, or **9.1**. Do not implement Layer 3 quarantine, CurseForge API (4.12), Quilt Setup, or pack replace.  
6. If this plan disagrees with lab `PRODUCT-IDEAS.md`, **follow this plan** and note drift.

### Context budget

This header + **one** R-section + the files listed there. Blueprint: **named §§ only**. Do not load Pass 2, the full V1 plan, or PRODUCT-IDEAS.

### Operator prompt (copy-paste)

```text
Read docs/V1-Modpack-Robustness-Plan.md in OCI-mc-server. Implement only the section marked NEXT (R1 unless I named another).
You MAY use OCI CLI/API with profile TESTING (not DEFAULT) and SSH both test VMs with %USERPROFILE%\.ssh\mcmgr_ed25519_20260817_125552. Stay at $0. Do not tofu apply/destroy. Do not commit. Do not start Step 8.5.2, 8.6.1, or 9.1.
This detour does not need the test VMs unless the NEXT section says so. If you do use VM1: START if STOPPED, disable idle, re-enable when finished (OS-ISSUE-7 after Minecraft start).
When done: update this plan’s statuses and V1 plan Step 4.13, stop, tell me what you did, how to test, what’s next, and ask if I want to continue.
Prompt sequential steps in Agent mode (not Plan mode). Use Build in Parallel / Plan mode only if the NEXT step is marked PARALLEL-OK. Include this same Agent-vs-Plan instruction in the prompt you give me for the following step.
```

---

## What already exists (do not rediscover)

Phase **4.7–4.11** already import a local file (no catalog). Strip today:

| Adapter | Strips | Gap |
|---------|--------|-----|
| `.mrpack` | `env.server == unsupported` only | Mis-tagged `required` (Fabulously Optimized, Simply Optimized, MMC3, OptiFine-for-Fabric) installs client mods. |
| Manual / CF Server Files zip | In-jar Fabric/Quilt/Forge `environment`/`side == client` | No itzg list. Jars without metadata are **kept**. |
| `overrides/` | Copied wholesale | Can put client jars back after an index strip. |
| Index file with empty `downloads` | **Fails** | Mixed packs (some CDN URLs, some jars in the zip) cannot install. |
| Zip of jars at archive **root** (no `mods/`, no manifest) | Likely **refused** as unknown | [`custom-forge-1.20.1-MilesPack.zip`](Sample-Packs.md) is this shape. |
| CurseForge client export / jar-less zip | Hard-block (P7) | Keep. Step **4.12** stays deferred. |

Vendored itzg JSON (operator 2026-08-20, full files): [`docs/modrinth-exclude-include.json`](modrinth-exclude-include.json), [`docs/cf-exclude-include.json`](cf-exclude-include.json). Attribution: [`docs/itzg-exclude-include-NOTICE.txt`](itzg-exclude-include-NOTICE.txt). **R1** embeds them in Core (`ExcludeIncludeMatcher`); installers do not call them yet.

---

## Progress dashboard

| ID | Section | Status | Live SSH/OCI? |
|----|---------|--------|----------------|
| **R1** | Matcher + vendor lists (Core only) | **DONE** | No |
| **R2** | Apply to `.mrpack` (env + list + overrides + embedded jars) | **NEXT** | No (temp dir; optional CDN) |
| **R3** | Apply to manual / jar-root / CF-with-jars zip | TODO | No (temp dir) |
| **R4** | Setup pre-check copy + optional list refresh + Guide | TODO | No |

When **R4** is DONE: point V1 **NEXT** at Step **8.5.2**, update [`V1-QA-Pass-2-Scope.md`](V1-QA-Pass-2-Scope.md) pack row, then stop for the operator to start Pass 2.

---

## Cases this plan must handle

Handle these. Do **not** expand into CurseForge API or crash-quarantine.

| # | Shape | Expected |
|---|--------|----------|
| 1 | `.mrpack` with correct `env.server` | Still skip `unsupported`. List is a second filter. |
| 2 | `.mrpack` with everything `required` (FO / Simply Optimized / MMC3) | Strip names that match the Modrinth exclude list; keep real server mods. |
| 3 | Force-include | Keep a file the pack marked `unsupported` if the list force-includes it (schema has this; rare). |
| 4 | Per-pack `modpacks.<slug>` | Apply when a slug can be matched from pack name / index; skip silently if not. |
| 5 | Index entry with **no URL**, file present in the zip / `overrides/` | Copy + hash-verify. Do not fail for “no download URL.” |
| 6 | Mixed URL + embedded | Both paths use the same include/exclude decision. |
| 7 | `overrides/` (then `server-overrides/`) | Copy configs/datapacks; **do not** copy jars the matcher excluded. Skip `client-overrides/`. |
| 8 | CurseForge Server Files / unstructured `mods/` zip with jars present | CF list + in-jar `client` (in-jar client still strips). |
| 9 | Zip of **only jars at archive root** (MilesPack) | Treat as unstructured server mods → install into `mods/`. Detect loader/MC from in-jar metadata when possible. |
| 10 | CF client export / listed file IDs with **zero** `mods/` jars | Stay **hard-block** (P7). Name the missing files; do not half-install. |
| 11 | Mixed CF: some jars + some ID-only files | Stay **hard-block**. Need 4.12 to fetch the rest. |
| 12 | Analyze/Setup **pre-check** | If the list will skip mods the pack declared server-side, warn **and still continue**. If Deploy later fails, that list is the first thing to check. |

**Parked (not this plan):** blueprint §24.3 Layer 3 crash quarantine; CurseForge API; Quilt Setup; day-2 pack replace.

---

## Precedence (implement in R1, use in R2–R4)

After reading itzg’s matcher, **document the actual rule in code comments** and follow it. Default if itzg is “contains, case-insensitive, against filename and/or slug”:

1. Layer 2 product overlay (same schema; wins over Layer 1).  
2. Per-pack `modpacks` entry when a slug matches.  
3. `globalForceIncludes` (keep).  
4. `globalExcludes` (skip as client-only).  
5. Pack declaration (`env.server` / in-jar side).

Force-include beats exclude at the same layer (confirm against itzg). Lists mix slugs (`sodium`), filename stems (`Xaeros_Minimap`), and display names (`Cull Less Leaves`) — do not assume kebab-case only.

`.mrpack` files with **still-unclear** `env.server` after the list: still **fail** install (do not guess). If the list excludes them, that **resolves** the row (skip, do not fail).  
Manual zips: remaining unclear jars **stay kept** (server pack assumed), same as 4.9.

Record skip **reason** for Setup: `pack_declared` vs `override_list` vs `in_jar_metadata`.

---

## Lists: vendor + optional refresh

- **Layer 1 runtime copy:** embed the two `docs/*-exclude-include.json` files in `McManager.Core` (same EmbeddedResource pattern as Mojang/Paper fixtures). Do not live-fetch from VM1.  
- **Layer 2:** new empty product file `src/McManager.Core/Setup/pack-lists/mcmgr-exclude-include.json` (same schema, empty `globalExcludes` / `globalForceIncludes` / `modpacks` unless a test needs a stub).  
- **Refresh (R4, not R1):** at **analyze on the admin PC**, optional HTTPS GET of itzg GitHub raw URLs (short timeout). On timeout/non-JSON/any error → bundled copy. **Never fail Setup** because GitHub was down.  
- URLs: [modrinth-exclude-include.json](https://raw.githubusercontent.com/itzg/docker-minecraft-server/master/files/modrinth-exclude-include.json), [cf-exclude-include.json](https://raw.githubusercontent.com/itzg/docker-minecraft-server/master/files/cf-exclude-include.json).  
- Apache-2.0 attribution stays in [`docs/itzg-exclude-include-NOTICE.txt`](itzg-exclude-include-NOTICE.txt).

---

## Samples (gitignored `data/sample-packs/`)

See [`Sample-Packs.md`](Sample-Packs.md). CI must use `tests/fixtures/` only.

| Use | File |
|-----|------|
| Correct `env.server` strip | `homemade/fabric-strip.mrpack` |
| Real small mis-tag `.mrpack` | `real/modrinth-fabric-Simply-Optimized-Continued-v2.1+26.2.mrpack` (~9 KB, MC **26.2**, every file `required`) |
| Jar-root custom zip | `real/custom-forge-1.20.1-MilesPack.zip` (~300 MB — **analyze / temp-dir only**, not TESTING Deploy) |
| P7 refuse | `homemade/curseforge-synthetic.zip` |
| Do **not** Deploy in this plan | MMC3 (~57 MB / thousands of overrides), Infinite Horizons, FO, MilesPack, jar-less CF |

R2 may GET Modrinth CDN URLs already in a homemade/Simply Optimized index (admin PC). Do not call the CurseForge API.

---

## R1 — Matcher + vendor lists (Core only)

**Status:** DONE  
**Depends on:** Phase 4 DONE  

**Read first**

- This R1 section + [Precedence](#precedence-implement-in-r1-use-in-r2r4)  
- Blueprint **§24.3** Layer 1–2 only (not Layer 3)  
- [`docs/modrinth-exclude-include.json`](modrinth-exclude-include.json) / [`docs/cf-exclude-include.json`](cf-exclude-include.json) (schema; do not memorize every slug)  
- [itzg exclude/include schema](https://github.com/itzg/mc-image-helper#excludeinclude-file-schema) — then the **mc-image-helper Java** that matches `globalExcludes` (fetch/read; do not guess)  
- `src/McManager.Core/McManager.Core.csproj` (EmbeddedResource pattern)  
- `src/McManager.Core.Tests/` pack tests as a style reference only

**Do**

- Research itzg matching; implement the same rule (filename and/or slug; case; substring vs exact). Comment the rule + source.  
- Parse the itzg schema (`globalExcludes`, `globalForceIncludes`, `modpacks`).  
- Embed both Layer 1 JSON files from `docs/`. Add empty Layer 2 overlay.  
- `ExcludeIncludeMatcher` (name as you like) in Core: given pack slug (optional) + file path/slug/filename → keep / exclude + reason. No HTTP. No installer changes.  
- Tracked **tiny** fixture JSON under `tests/fixtures/pack-lists/` for logic tests (do not make CI depend on listing every itzg slug). Also assert the real vendored files **parse**.  
- Apache-2.0 notice already in `docs/itzg-exclude-include-NOTICE.txt` — keep it.

**Test**

- `dotnet test` matcher tests: exclude, force-include, per-pack overlay, Layer 2 wins, no-match falls through.  
- Real vendored JSON deserializes.

**Done when:** Core can answer keep/exclude offline. `MrpackInstaller` / Setup UI unchanged.

**Changelog:** 2026-08-20 — Core `ExcludeIncludeMatcher` + embedded Layer 1 (itzg JSON) + empty Layer 2 overlay. itzg `MultiMatcher` contains/`/regex/` plus collapsed name/slug. Installers unchanged. **NEXT = R2**.

---

## R2 — Apply to `.mrpack`

**Status:** NEXT  
**Depends on:** R1  

**Read first**

- This R2 section + case table rows 1–7  
- Blueprint **§22.1** (env.server + overrides order)  
- `src/McManager.Core/Setup/MrpackAnalyzer.cs`  
- `src/McManager.Core/Setup/MrpackInstaller.cs`  
- `src/McManager.Core.Tests/MrpackAnalyzerTests.cs` + `MrpackInstallerTests.cs`  
- [`Sample-Packs.md`](Sample-Packs.md) (Simply Optimized + fabric-strip)

**Do**

- Analyze + install: apply matcher **after** reading `env.server`. Skip override-listed files even when `required`. Force-include can keep `unsupported`.  
- Empty `downloads`: copy from the zip (`overrides/` or same relative path) and verify hash when present.  
- Filter `overrides/` / `server-overrides/` so excluded **jars** are not copied; keep configs.  
- Confirmable summary counts: pack-declared client-only vs override-list skips.  
- Tracked homemade/CI fixture: like fabric-strip but Sodium (or equivalent) tagged `required` — list must still skip it. Do not commit real packs.

**Test**

- CI fixture install into a temp dir (no network if the fixture has no CDN).  
- Optional on this PC: analyze Simply Optimized — expect many override-list skips, `CanContinue` still true.  
- Existing fabric-strip: Sodium still skipped via `unsupported` (regression).

**Done when:** Mis-tagged `.mrpack` strips known client mods; mixed embedded+URL works; overrides do not reintroduce excluded jars.

**Changelog:** _(empty)_

---

## R3 — Manual zip, jar-root, CF-with-jars

**Status:** TODO  
**Depends on:** R2  

**Read first**

- This R3 section + case table rows 8–11  
- Blueprint **§24.1** (refuse raw client packs; Server Files / filled zip)  
- `src/McManager.Core/Setup/ManualServerPackAnalyzer.cs`  
- `src/McManager.Core/Setup/ManualServerPackInstaller.cs`  
- `src/McManager.Core/Setup/SetupPackImport.cs` (P7 refusals — keep)  
- [`Sample-Packs.md`](Sample-Packs.md) MilesPack row

**Do**

- Apply CF Layer 1 list (plus Layer 2) to jar names/slugs. In-jar `client` still strips.  
- **Jar-root zip:** if there is no `mods/` but the archive is (almost) only `*.jar` at the root, treat those as `mods/` (install into dest `mods/`). Peek in-jar metadata for loader / Minecraft when the zip has no manifest.  
- Keep P7: CF `minecraftModpack` with listed files and **zero** jars → `CanInstall=false`. Mixed jars + leftover ID-only files → still refuse (no API).  
- Tracked tiny `tests/fixtures/packs/` jar-root zip (dummy jars), not MilesPack.

**Test**

- CI: jar-root fixture installs to `mods/`; exclude-list name skipped; P7 zip still blocked.  
- Optional: analyze MilesPack locally (300 MB) — should not refuse as unknown; should skip known client jars (embeddium, entityculling, ImmediatelyFast, …). Do not upload to VM1.

**Done when:** MilesPack-shaped zips analyze; CF-with-jars uses the list; jar-less CF still hard-blocks.

**Changelog:** _(empty)_

---

## R4 — Setup pre-check + list refresh + Guide

**Status:** TODO  
**Depends on:** R3  

**Read first**

- This R4 section + case table row 12  
- `src/McManager.Core/Setup/SetupPackImport.cs`  
- Hybrid Setup Game step that shows the pack summary (open only that Razor/VM)  
- [`Guide.md`](Guide.md) **Modded** paragraphs only  
- Blueprint **§25** (client-pack copy stays; this is extra)

**Do**

- After analyze, if any file is skipped because of the **override list** while the pack called it server-side (or had no side): show a **warning in the confirmable summary** (not a third required checkbox). Novice wording, e.g. *This pack marks some mods as needed on the server that are known client-only mods. Setup will skip those on the game computer. If the server fails to start, check this skipped list first.* List examples (capped). Still **CanContinue**.  
- Optional GitHub raw refresh of Layer 1 on the **admin PC** at analyze time; fallback to embedded; never fail Setup on refresh failure.  
- One short Guide paragraph under Modded.  
- Do not implement Layer 3 UI.

**Test**

- Unit: preview flags `OverrideListSkipCount` (or equivalent) and warning text.  
- Operator: drop Simply Optimized in Setup (no Deploy) — warning visible, continue enabled. Drop fabric-strip — no mis-declaration warning (Sodium is pack-declared). Drop synthetic CF — still blocked.

**Done when:** Setup tells the operator about auto-corrections before Deploy; Guide mentions it; refresh cannot brick Setup.

**Changelog:** _(empty)_

---

## After R4 (docs in that session or a tiny follow-up)

1. V1 dashboard: **4.13 DONE**, **NEXT = Step 8.5.2**.  
2. Pass 2 pack row: keep a **small** live Deploy pack (BlockFront still fine for NeoForge doorbell). S6-02 must include the mis-declaration warning (Simply Optimized or the CI mistag fixture — **not** a 300 MB zip). Still do not Deploy MMC3 / MilesPack / Infinite Horizons / jar-less CF.  
3. Do not start Pass 2 Phase A until the operator says so (tofu destroy).

---

## Plan changelog

| Date | Note |
|------|------|
| 2026-08-20 | **R1 DONE.** Core matcher + embedded itzg lists + empty `mcmgr-exclude-include.json`. **NEXT = R2**. |
| 2026-08-20 | Created (docs only). Operator: itzg lists in `docs/`, extra samples, Setup pre-check, pause Pass 2. **NEXT = R1**. Layer 3 and CurseForge API parked. Do not implement in the creation session. |
