# V1 modpack-test follow-on (living)

**Status:** Living. Created 2026-08-21 (docs only). **NEXT = P2.**  
**Parent:** [`V1-Implementation-Plan.md`](V1-Implementation-Plan.md) Step **8.7**.  
**Why now:** operator 2026-08-21 — informal **Change pack** tests in [`Mod-Pack-Tests.md`](Mod-Pack-Tests.md) failed **4 / 5**. Pause Step **8.5.2** Pass 3. Fix **generalizable** pack-start gaps **before** [`V1-Operator-Notes-Follow-On-Plan.md`](V1-Operator-Notes-Follow-On-Plan.md) (Step **8.8**) and before QA Pass 3.

This file’s creation session **must not implement code**. Later agents implement **only the single section marked NEXT**.

**Cost:** $0. TESTING profile only. Never `DEFAULT` / live Forge lab. Never Minecraft `0.0.0.0/0`.  
**Functions:** agents **may** `fn build` / `fn push` / invoke **product** Functions on TESTING without asking, still $0 — no real $1 budget fire; do not SoftStop the door.  
**Tofu:** do **not** `tofu apply` / `destroy` in this plan. Keep the Pass 2 TESTING stack.  
**SSH:** `%USERPROFILE%\.ssh\mcmgr_ed25519_20260817_125552` (confirm in TESTING `config.local.json`).  
**Hybrid config dir:** `MCMANAGER_CONFIG_DIR` = `mcmgr-blank-test` — **not** repo `data/config.local.json` (Forge / `DEFAULT`).  
**Hosts / OCIDs:** TESTING `config.local.json` and `%LOCALAPPDATA%\McManager\tofu\<stack-id>\`. **Do not paste live OCIDs, IPs, Auth Tokens, or key material into tracked docs or chat dumps.**

---



## How agents must use this file

1. Read **this protocol**, the [Progress dashboard](#progress-dashboard), and **only the NEXT section**.
2. Implement only that section. Do not start neighbors “while you are here.”
3. After finishing: mark **DONE**, set the next incomplete section to **NEXT**, changelog line, update V1 plan Step **8.7** + dashboard, **stop**.
4. If you change a test VM or TESTING cloud resource, make the **same** change in local SoT (`onbox/`, `infra/`, `door_vm/`, `vm_agent/`, `functions/shutdown_vm/`, Manager/Setup). File [`Issues.md`](Issues.md) for on-box/Setup/door bugs.
5. Never create git commits. Suggest a message.
6. Do **not** start Step **8.8** (operator notes), **8.5.2** (Pass 3), **8.6.1**, or **9.1**.
7. If this plan disagrees with [`PRODUCT-IDEAS.md`](PRODUCT-IDEAS.md), **follow this plan** and note drift (do not rewrite PRODUCT-IDEAS to match).
8. VM1: START if needed, **disable idle** while working, **re-enable** when finished (re-disable after Minecraft start — OS-ISSUE-7).
9. **Generalize.** Do not ship denylist entries that only name `holdmyitems`, `mod-loading-screen`, or other jars from [`Mod-Pack-Tests.md`](Mod-Pack-Tests.md). Those packs are **repro fixtures**, not the product rule. A fixture test that uses one of those jars is fine.

Vague notes / missing detail: **decide inside the section bounds** (mechanism, copy, file layout) and record the choice in the section changelog. **Stop and ask** only for spend, `0.0.0.0/0`, CurseForge API keys, tofu destroy, or expanding into Step **8.8** work.



### Context budget

This header + **one** P-section + the files listed there. Blueprint: **named §§ only**. Do not load Pass 3, Step **8.8**, the full V1 plan, or PRODUCT-IDEAS unless a heading is named.

### Operator prompt (copy-paste for the next agent)

```text
Read docs/V1-Modpack-Test-Follow-On-Plan.md in OCI-mc-server. Implement only the section marked NEXT (or the PARALLEL-OK section I named).
You MAY use OCI CLI/API with profile TESTING (not DEFAULT) and SSH both test VMs with %USERPROFILE%\.ssh\mcmgr_ed25519_20260817_125552 (confirm path in the TESTING config). You MAY fn build/push/invoke product Functions on TESTING. Stay at $0. Do not tofu apply/destroy unless I authorize it in this chat. Do not commit. Do not start Step 8.8, 8.5.2 (Pass 3), 8.6.1, or 9.1.
Use MCMANAGER_CONFIG_DIR for mcmgr-blank-test, not repo data/config.local.json (Forge / DEFAULT).
If you need VM1, START it, disable idle, re-enable when finished. Minecraft boot force-enables idle (OS-ISSUE-7) — disable again after a game start.
Fixes must be generalizable (in-jar metadata, exclude-list mechanism, Java major lifecycle, crash-aware health) — do not only denylist the exact jars from docs/Mod-Pack-Tests.md.
When done: update this plan’s statuses and V1 Step 8.7, file Issues.md if on-box/Setup/door, stop, tell me what you did, how to test, what’s next, and ask if I want to continue.
Prompt sequential steps in Agent mode (not Plan mode). Use Build in Parallel / Plan mode only if the NEXT step is marked PARALLEL-OK. Include this same Agent-vs-Plan instruction in the prompt you give me for the following step.
```



### PARALLEL-OK

Only when two sections **do not** edit the same files **and** do not both own the TESTING stack. Hybrid Razor/CSS is sequential by default. P2 (in-jar side) and P3 (Fabric overlay) both touch pack analyze/install — **SEQUENTIAL**. P1 may run in parallel with P2 **only** if P1 stays in health-check / Manager fail copy and P2 stays in analyzers (no shared files). Default: **SEQUENTIAL**.

---



## What already happened (do not rediscover)

- Step **4.13** / R1–R4 **DONE**: itzg Layer 1 + product Layer 2 overlay; `.mrpack` uses `env.server` then matcher; manual/jar-root use CF matcher; Setup warns when the list skips server-side/unknown-side mods.
- Step **8.4** P9 **DONE**: manual / jar-root **may continue** with leftover unclear jars; `.mrpack` still **fails** on unclear `env.server`. Install keeps leftover unclear jars after the list (“server pack assumed”).
- Step **8.4** P10–P11 **DONE**: pack replace is **full re-setup** (keep world unless wipe). Manager **Change pack** is the path used in [`Mod-Pack-Tests.md`](Mod-Pack-Tests.md).
- CurseForge **API** (Step **4.12**) stays **deferred**. Jar-less / mixed-ID CF zips stay hard-blocked.
- Blueprint **§24.3 Layer 3** crash quarantine is **not this plan** — it is Step **8.8** P10. This plan must still **detect crash-loops** so quarantine has a signal later.
- On-box Java: `onbox/mcmgr/common/java.sh` already installs Temurin by **major** (apt, then Adoptium REST). `forge_meta.py` already maps MC **26.x → Java 25**. The 2026-08-21 Simply Optimized failure is **lifecycle** (Change pack did not select/install Java 25), not “Java 25 is impossible.”
- Health fail copy (P1): crash-loop / FATAL fail fast with a capped journal excerpt; timeout without a crash says RCON never came up. Success is still RCON `list`. Pre-P1 copy was `Minecraft unit started but RCON list did not succeed in time. Re-Deploy can resume on-box stages.`

---



## Informal tests (2026-08-21)

Source: [`Mod-Pack-Tests.md`](Mod-Pack-Tests.md). Path: Manager **Change pack** + **wipe world**. Not greenfield Setup.

| # | Pack | Result | Cluster |
|---|------|--------|---------|
| 1 | MilesPack (unstructured Forge 1.20.1 zip) | FAIL | B + A |
| 2 | MilesPackV2 (same + extra jars) | FAIL | B + A |
| 3 | Fabulously Optimized 6.5.0 `.mrpack` | SUCCESS | (benign `e4mc` `/tmp` + missing server icon) |
| 4 | OptiFine for Fabric `.mrpack` | FAIL | C + A |
| 5 | Simply Optimized Continued (MC 26.2 / Java 25) | FAIL | D + A |

All four failures used the RCON-timeout Manager message. Underlying causes differ.

---



## Failure clusters (fix these, not the pack names)


| ID | Cluster | Generalizable gap |
|----|---------|-------------------|
| **A** | RCON timeout masks crash-loops | Health check treats “unit started” + RCON miss as the only failure. systemd `status=1` restart loops and journal `FATAL` are not surfaced. |
| **B** | Unstructured Forge zips keep client mods | Override list stripped a handful of jars; dozens with **no side metadata** stayed. Client mixins (`invalid dist DEDICATED_SERVER`) killed the dedicated server. |
| **C** | Fabric `.mrpack` still ships client/GUI mods | Pack-declared client metadata was unused for some jars; overlay missed loading-screen / FlatLaf-class of client mods. |
| **D** | Required Java not applied on pack change | Analyze showed Java **25**; VM stayed on **21** → `UnsupportedClassVersionError` (class 69 vs ≤65). |


**Do not** turn MilesPack’s `holdmyitems` jar or OptiFine-for-Fabric’s `mod-loading-screen` into the product rule. They are examples of **B** and **C**.

---



## Drift vs PRODUCT-IDEAS (follow this plan)


| Topic | PRODUCT-IDEAS / older V1 | This plan |
|-------|--------------------------|-----------|
| Layer 1–2 only | 4.13 parked deeper heuristics | **In-jar** side signals for unstructured zips (P2) and leftover Fabric client mods (P3) |
| Java | Install matching major at first bootstrap | **Re-run** Java major on Change pack / full re-setup (P4), including 25 when Adoptium has it |
| Health | RCON `list` = first boot succeeded (blueprint §12.1) | Still require RCON for **success**; **fail fast** on crash-loop / FATAL (P1) |

Do **not** rewrite PRODUCT-IDEAS to match.

---



## Parked (not this plan)


| Item | Why |
|------|-----|
| Blueprint **§24.3 Layer 3** quarantine (move blamed jar, retry, UI keep/restore) | Operator notes plan Step **8.8** P10. P1 here only improves detection/copy. |
| User-editable loader/MC/Java on jar-root + derived `.mrpack` | Step **8.8** P9 |
| CurseForge API / catalog | 4.12 deferred; catalog **rejected** |
| `e4mc` `/tmp` read-only + “Couldn't load server icon” on the successful FO run | Server still `Done`. Optional later; do not block this plan. |
| Pack-specific denylists | Forbidden as the fix. Overlay **classes** of client mods (loading screens, GUI-only) are P3. |

---



## Progress dashboard


| ID | Section | Status | Parallel? | Live SSH/OCI? |
|----|---------|--------|-----------|----------------|
| **P1** | Crash-aware readiness (fail fast + journal) | **DONE** | SEQUENTIAL | Yes |
| **P2** | Unstructured zip in-jar side detection | **NEXT** | SEQUENTIAL | Optional |
| **P3** | Fabric / `.mrpack` leftover client mods | TODO | SEQUENTIAL | Optional |
| **P4** | Java major on Setup + Change pack | TODO | SEQUENTIAL | Yes |
| **P5** | Analyze warnings when many jars lack side data | TODO | SEQUENTIAL | No |


When **P5** is DONE: point V1 **NEXT** at Step **8.8** ([`V1-Operator-Notes-Follow-On-Plan.md`](V1-Operator-Notes-Follow-On-Plan.md) **P1**). Do **not** start Pass 3.

---



## P1 — Crash-aware readiness

**Status:** DONE  
**Catalog IDs:** new (Pass 3); related fail copy in Change pack / Setup health

**Read first**

- `src/McManager.Core/Setup/SetupBootstrapService.cs` (RCON health + `"RCON list did not succeed in time"`)
- `onbox/mcmgr/common/driver.sh` (optional start / health)
- Blueprint **§12.1 step 9** and **§14.3** health-check failure row **only**
- `src/McManager.Core/Services/MinecraftConsoleRemote.cs` (journal pull — reuse, do not open 25575 on the Security List)
- [`Mod-Pack-Tests.md`](Mod-Pack-Tests.md) — Tests 1, 2, 4, 5 symptoms only (do not load the whole file if the cluster table above is enough)

**Do**

1. **Success still means RCON `list` succeeded** (blueprint). Do not treat `systemctl is-active` alone as joinable.
2. **Fail fast** when the unit is crash-looping: `NRestarts` climbing, exit `status=1`, or journal `FATAL` / loader “the following mod(s) caused the server to crash” **before** exhausting the full RCON attempt budget. Bound extra journal/SSH polls with [`OCI-API-Usage.md`](OCI-API-Usage.md) spirit (no 1s busy loops; reuse existing waiter cadence).
3. Manager / Setup error copy must **distinguish**:
   - crash-loop / fatal (include **capped** journal excerpt + implicated mod name if the loader printed one)
   - still starting (slow world gen — keep waiting up to the existing budget)
   - RCON never came up without a crash (keep a timeout, but say so)
4. Core/on-box tests with **fixture journal text** (Forge mixin dist, Fabric `NoClassDefFoundError` abort, `UnsupportedClassVersionError`) — not live MilesPack unless you already have VM1 up for another reason.

**Decide if unclear:** How many journal lines to show (cap ~30). Whether to `systemctl stop minecraft` after a detected crash-loop so the unit does not keep restarting while the user reads the error (prefer **yes** — stop the loop; leave files on disk).

**Test**

- Unit tests: crash-loop sample → fail fast with FATAL excerpt; healthy “still loading” sample → wait; RCON success → pass.
- If VM1 is already RUNNING for this chat: optional Change pack that is known to crash should **not** sit on the generic RCON message.

**Done when:** Fail copy is specific; crash-loops do not consume the full RCON budget; tests exist; Guide one-liner if user-visible Setup/Change pack errors change.

**Changelog:** 2026-08-21 — **P1 DONE.** Journal excerpt cap **30** lines (probe 80; `--since` health-wait start). After a detected crash-loop, **`systemctl stop minecraft`** (leave files on disk). Classifier is Core `MinecraftReadiness` (fixture journals: Forge mixin invalid dist, Fabric `NoClassDefFoundError` abort, `UnsupportedClassVersionError`, healthy spawn-area). Success remains RCON `list`. Cadence unchanged (12×10s). Not Layer 3 quarantine (8.8 P10). **NEXT = P2.**

---



## P2 — Unstructured zip in-jar side detection

**Status:** NEXT  
**Catalog IDs:** S6-02 (expected may change); Change pack analyze

**Read first**

- `src/McManager.Core/Setup/ManualServerPackAnalyzer.cs` / installers (jar-root, UnstructuredServer)
- `src/McManager.Core/Setup/ExcludeIncludeMatcher.cs` + embedded CF / product overlay JSON
- Blueprint **§24.3** Layers 1–2 (in-jar is an extra signal, not a third list format unless you must)
- Tracked fixtures under `tests/fixtures/packs/` — add a **tiny** zip if needed (do not commit MilesPack)

**Do**

1. After Layer 1–2 matching, for leftover jars with **no pack-declared side**, peek **in-jar** metadata:
   - Forge/NeoForge `mods.toml` / `neoforge.mods.toml`: `displayTest`, `clientSideOnly`, side-like fields
   - Fabric `fabric.mod.json`: `environment` / `client` entrypoints
   - Cheap extra: mixin configs that target **client-only** Minecraft classes / `invalid dist` patterns **only if** that is a clear dedicated-server killer — do not strip a jar because one mixin file exists
2. Strip jars that are **clearly client-only**. Leave true unknowns with the existing “continue + warn” path (P9 of 8.4). Prefer **false keep** of a harmless unknown over stripping a server API jar.
3. Do **not** add `holdmyitems` as a special case. If that jar is client-only by metadata or mixin-dist, the general rule should catch it; if it is a server jar that still crashes, that is P1 copy + later quarantine (8.8), not a denylist.
4. Same code path for **Setup analyze/install** and **Change pack**.
5. Tests: fixture jar with `environment: client` / `clientSideOnly` is stripped; a server-only toml is kept; unclear stays unclear.

**Decide if unclear:** How deep to parse mixins (prefer metadata first; mixin heuristic only for high-confidence client class names you document in the changelog). Do not invent a CurseForge API call.

**Test**

- `dotnet test` on Core pack tests. Optional: re-analyze a local MilesPack **if the operator has it** — do not commit it.

**Done when:** Unstructured/manual leftover jars use in-jar signals; fixtures cover strip vs keep vs unclear; Guide one-liner if the confirmable summary changes.

**Changelog:** *(empty until implemented)*

---



## P3 — Fabric / `.mrpack` leftover client mods

**Status:** TODO  
**Catalog IDs:** Setup / Change pack `.mrpack` strip

**Read first**

- `MrpackAnalyzer` / `MrpackInstaller` (names as in Core)
- `ExcludeIncludeMatcher` + Modrinth embedded list + product overlay
- Blueprint **§22.1** (trust `env.server`, then override)
- Test 4 in [`Mod-Pack-Tests.md`](Mod-Pack-Tests.md) (symptom: loading-screen / FlatLaf abort) — treat as a **class** of client GUI mods

**Do**

1. Honor pack-declared client / unsupported **and** overlay lists. If Test 4 skipped mods with **0** pack-declared client metadata, find why `env` was ignored and fix the matcher/install order.
2. Expand the **product overlay** with **classes** of known client-only Fabric mods (loading screens, Sodium/Iris already present, GUI/FlatLaf loaders) using slugs/names — not a one-line exclude of a single Test 4 filename unless that slug is the general identity.
3. Optional GitHub Layer 1 refresh already exists (R4) — do not replace it. Overlay is the product-owned delta.
4. Same path for Setup and Change pack.
5. Fixture: tiny `.mrpack` (or tracked `fabric-mistag` extension) with a client GUI mod that today would be kept.

**Decide if unclear:** Whether to treat “no `env` + client entrypoints only” as client (prefer **yes** if `fabric.mod.json` has only client entrypoints). Stop and ask before downloading live Modrinth packs in CI.

**Test**

- `dotnet test`. Optional live FO pack should still succeed (do not strip server-side FO mods).

**Done when:** Leftover client GUI mods are stripped by metadata and/or overlay class; fixture test; Guide if summary counts change.

**Changelog:** *(empty until implemented)*

---



## P4 — Java major on Setup and Change pack

**Status:** TODO  
**Catalog IDs:** new (Pass 3 pack replace / Setup game)

**Read first**

- `onbox/mcmgr/common/java.sh` (`java_install` by major; Adoptium fallback)
- `onbox/mcmgr/common/driver.sh` (module `*_JAVA_MAJOR` → `java_install`)
- Pack replace: `SetupBootstrapService.ReplacePackAsync` (or current name) + on-box prepare
- Analyze summary `Required Java`
- `onbox/mcmgr/common/forge_meta.py` `java_major_for_minecraft` (26.x → 25 already)

**Do**

1. After pack analyze (Setup or Change pack), the **required Java major** must drive `java_install` **before** the new game start — including when the previous pack used a different major (21 → 25, 21 → 17, etc.).
2. Point `minecraft.service` / `JAVA_EXECUTABLE` at the **new** major (do not leave `temurin-21` on the unit after a Java 25 pack).
3. If Adoptium/apt cannot install that major: **fail before start** with copy like “This pack needs Java 25, and the installer could not provide it.” Never a generic RCON timeout.
4. Do not hard-code a max of 21. Majors the existing Temurin path can install are in scope (8 / 17 / 21 / 25 as Adoptium publishes aarch64 JRE).
5. Tests: dry-run or Core/on-box unit that pack replace passes `JAVA_MAJOR=25` into the driver; fail path when install stub returns missing.

**Decide if unclear:** Whether to keep older JREs installed side-by-side (prefer **yes**, select via unit `ExecStart`). Do not add a paid Oracle JDK.

**Test**

- `dotnet test` / on-box dry-run. Live Simply Optimized only if the operator has the file and VM1 is up — do not commit the pack.

**Done when:** Change pack / Setup bootstrap installs and selects the analyzed Java major; missing major is a clear pre-start error; Guide one-liner.

**Changelog:** *(empty until implemented)*

---



## P5 — Analyze warnings when many jars lack side data

**Status:** TODO  
**Catalog IDs:** Setup Game step / Change pack confirm

**Read first**

- Setup pack summary UI + Change pack confirm (Hybrid)
- Analyzer warning fields (`UnclearSideCount`, override-list skip)
- R4 confirmable summary (do not add a third checkbox unless you must)

**Do**

1. When leftover **unclear-side** jars exceed a small threshold (decide **N**, e.g. 10) **or** a large fraction of `mods/`, escalate the existing warning: user should expect start failures; Console / this plan’s P1 copy is the next place to look.
2. Still **CanContinue** for manual/jar-root (8.4 P9). `.mrpack` unclear rules unchanged.
3. Show capped examples (already R4 style). No MilesPack-specific text.
4. Guide: one sentence on the stronger warning.

**Decide if unclear:** Exact N and whether to use count vs percent (prefer count ≥ 10 **or** ≥ 50% unclear, whichever fires). Keep copy short.

**Test**

- Hybrid/Core test on summary text when unclear count is high vs low.

**Done when:** High-unclear packs get a louder warning; low-unclear unchanged; V1 **NEXT** → Step **8.8** P1.

**Changelog:** *(empty until implemented)*

---



## After this plan (do not do it here)

1. V1 dashboard: **8.7 DONE**, **NEXT = Step 8.8**. Set [`V1-Operator-Notes-Follow-On-Plan.md`](V1-Operator-Notes-Follow-On-Plan.md) **P1 = NEXT**.
2. `AGENTS.md` + `.cursor/rules/oci-mc-server-product.mdc` NEXT lines.
3. Do **not** start Pass 3.

---



## Plan changelog


| Date | Note |
|------|------|
| 2026-08-21 | **P1 DONE** (crash-aware readiness). Fail-fast on crash-loop/FATAL; stop unit; capped journal + implicated mod. **NEXT = P2.** Do not start 8.8, Pass 3, 8.6.1, or 9.1. |
| 2026-08-21 | Created (docs only). Operator: postpone Pass 3; informal Change pack tests 1/5. **NEXT = P1**. Layer 3 quarantine, jar-root confirm UI, CurseForge API parked (8.8 / 4.12). Do not implement in the creation session. |
