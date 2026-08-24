# Pack-corpus Change-pack test system (living)

**Status:** COMPLETE (P1–P3). Created 2026-08-24 (docs only). **Live NEXT:** [`NEXT.md`](NEXT.md) — Step **8.5.2** Pass 3 (**blocked** until the operator says so). Operator may seed `pack-tests/` and run `/pack-test-phase` separately.
**Parent:** tooling / agent QA harness (not a V1 product step). Informal ancestor: [`Mod-Pack-Tests.md`](Mod-Pack-Tests.md).  
**Why now:** operator 2026-08-24 — automate expected-to-work pack boots on TESTING VM1 via a **headless** Change-pack path so agents can run a sequential corpus without clicking Hybrid.

Implement **only the single section marked NEXT**.

**Cost:** $0. TESTING profile only. Never `DEFAULT` / live Forge lab. Never Minecraft `0.0.0.0/0`.  
**Functions / tofu:** do **not** `tofu apply` / `destroy`. Do not SoftStop the door.  
**Hybrid / harness config dir:** `MCMANAGER_CONFIG_DIR` = **`mcmgr-pack-test`** (TESTING OCI/SSH copied from `mcmgr-blank-test`). **Not** repo `data/config.local.json` (Forge / `DEFAULT`). **Not** `mcmgr-blank-test` for Layer 2 overlays (keep interactive Manager sessions clean).  
**SSH / VM1:** not required for **P1** or **P3**. **P2** may use RUNNING TESTING VM1 only if the operator authorizes it in that chat; default P2 test is `--analyze-only` / `dotnet build`.  
**Hosts / OCIDs:** do **not** paste live OCIDs, IPs, Auth Tokens, or key material into tracked docs.

---

## How agents must use this file

1. Read **this protocol**, the [Progress dashboard](#progress-dashboard), [Scrutiny](#scrutiny-plan-decisions), and **only the NEXT section**.
2. Implement only that section. Do not start neighbors “while you are here.”
3. After finishing: mark **DONE**, set the next incomplete section to **NEXT**, changelog line, update [`NEXT.md`](NEXT.md), **stop**.
4. Do **not** change Step **8.10** (already COMPLETE). Pass 3 stays blocked until this plan exits **and** the operator starts it.
5. Do **not** start Step **8.5.2** (Pass 3), **8.6.1**, or **9.1**. Do **not** run a live pack-test **phase** from this plan (that is after P3, operator-started).
6. If this plan disagrees with [`PRODUCT-IDEAS.md`](PRODUCT-IDEAS.md), **follow this plan** and note drift. Do not rewrite PRODUCT-IDEAS.
7. Git: commits allowed per `git-policy` when the operator asks or when finishing a section; never push/PR unless asked. Never commit pack bytes, full journals, filled `*.local.json`, or OCIDs.
8. VM1 (P2 live smoke only): START if needed, **disable idle** while working, **re-enable** when finished (OS-ISSUE-7). After `vm_agent/` edits, redeploy idle agent — this plan should **not** edit `vm_agent/`.

### Context budget

This header + **one** P-section + the files listed there. Blueprint: **named §§ only**. Do not load Pass 3, the full V1 plan, or PRODUCT-IDEAS unless a heading is named.

### Operator entry

New Agent chat → `/next-step`. Fresh chat per P-section.

### PARALLEL-OK

None. P2 consumes P1 paths/schemas. P3 skills wrap P1 `PROTOCOL.md` + P2 harness CLI. All **SEQUENTIAL**.

---

## What already exists (do not rediscover)

- Manager **Change pack** is `ServerManagementViewModel.InstallPackReplaceAsync`: confirm → optional `DerivedPackWorkflow.BuildAndRetain` → `SetupBootstrapService.ReplacePackAsync` (`PackReplaceRequest` + wipe flag + data directory).
- Analyze / continue / freeze: `SetupPackImport.AnalyzeFile`, `PackReplacePlanner.TryCreate` / `ToWizardState` (already sets EULA + both friend-pack flags).
- Assisted review default **Keep**; operator Skip persists via `PackAssistedReviewActions` → `Layer2LocalOverlay` per archive SHA. **This harness must not** apply the client-only sidecar as Skip.
- Identity + derived zip: `DerivedPackIdentity`, `DerivedPackArchive`, `DerivedPackWorkflow.BuildAndRetain` (unstructured / jar-root).
- Health: RCON `list` wait; crash-loop / FATAL fail-fast (Step **8.7** P1); Layer 3 quarantine notice on `PackReplaceResult.QuarantineNotice` (Step **8.8** P10).
- Operator samples: gitignored [`data/sample-packs/`](../data/sample-packs/) + [`Sample-Packs.md`](Sample-Packs.md). **Not** this corpus. Includes packs expected to **refuse** (UI tests).
- Informal live results: [`Mod-Pack-Tests.md`](Mod-Pack-Tests.md) (full consoles — **do not** copy that shape).
- Agents **cannot** drive the WPF window ([`V1-QA-Pass-3-Scope.md`](V1-QA-Pass-3-Scope.md)). One owner of the TESTING stack at a time.
- `data/` is gitignored. `MCMANAGER_CONFIG_DIR` documented in [`Local-Config.md`](Local-Config.md).

---

## Scrutiny (plan decisions)

Implementing agents follow these unless the operator overrides in chat.

**Purpose.** Regression **corpus** for “this pack should boot on the server after Change pack,” not Hybrid UI QA and not Pass 3 catalog IDs.

**Same Core path as a user.** The harness is a console. It must not reimplement on-box bootstrap or skip order. After analyze (+ identity + derived zip when Hybrid would), call `ReplacePackAsync`. Friend-pack checkboxes = acknowledged (`ToWizardState` already does this). Wipe world = **always true** for this suite.

**Assisted review during install.** Default **Keep** all `NeedsYourCall`. Do **not** read or apply `pack-tests/client-only/*.yaml` in the harness. If freeze sets `CanContinue = false`, verdict `blocked_freeze` — no SSH replace. Automatic skips (`env.server`, exclude lists, in-jar) still run.

**Client-only sidecars.** Operator-verified. Analysis skill only (diff vs kept jars / crash blame). Separate files, not inline in the catalog.

**Dedicated pack folder.** Expected-to-work archives live under gitignored `pack-tests/packs/`. Do **not** point the harness at `data/sample-packs/`. Operator copies curated files in; agents must not download kitchen-sink packs.

**Queue of one.** One TESTING VM1. Parent starts the next subagent only after the previous result has `ready_for_next: true`. Never parallel `ReplacePackAsync`.

**Ready-gate** (harness sets `ready_for_next`; parent must not spawn the next `pack-test-one` without it):

1. Result YAML written.
2. VM1 **RUNNING**, SSH probe OK.
3. No in-progress replace.
4. On non-pass: `minecraft.service` **stopped** (no inherited crash-loop).
5. Idle stays **disabled for the whole phase**; re-enable only when the phase ends (and after any Minecraft start that force-enables it — OS-ISSUE-7).
6. Short cooldown (file locks).
7. `pack-tests/.lock` held by the phase parent; refuse to start if lock is foreign (another chat / Pass 3).

**Success (`pass`) — all required:**

1. Analyze allowed Continue.
2. Install finished.
3. `minecraft.service` active, not crash-looping.
4. RCON `list` succeeded.
5. No FATAL / hard crash after start.
6. Layer 3 did **not** have to save the boot.

`pass_quarantined` = RCON OK only after Layer 3. Not `pass`.

**Verdicts:** `pass` | `pass_quarantined` | `blocked_freeze` | `product_fail` | `timeout` | `infra_fail`.

**Phase abort:** stop spawning pack tests after **≥2 consecutive `infra_fail`**. Do not abort on product/pack verdicts. Cap wall-clock / remaining queue in the manifest if a replace hangs.

**Logs.** Result YAML: fail one-liner + short excerpt path. Full journal **gitignored**. Do not append consoles to markdown (no second `Mod-Pack-Tests.md`).

**Config.** Seed `mcmgr-pack-test` by copying TESTING `config.local.json` (and SSH key path) from `mcmgr-blank-test`. Same stack, isolated Layer 2 / derived-pack data dir.

**Skills.** Thin wrappers around `pack-tests/PROTOCOL.md` + harness CLI. `pack-test-analyze` (stronger model) writes `EXECUTIVE-SUMMARY.md`. Parent does **not** write that summary. Do **not** auto-start `/phase-planning`.

**Harness project.** `src/McManager.PackTestHarness/` — `ProjectReference` to Core only. **No new NuGet.** Add to `src/McManager.slnx`. Exit codes: `0` pass / pass_quarantined; `1` product_fail / blocked_freeze / timeout; `2` infra_fail; `3` usage.

---

## Parked (not this plan)

| Item | Why |
|------|-----|
| Filling the corpus / first live phase | Operator copies packs + sidecars; starts `pack-test-phase` after P3 |
| Applying client-only lists as Skip during install | Explicitly not this harness |
| Driving Hybrid / WPF Change pack | Agents cannot; UI stays Pass 3 / operator |
| `data/sample-packs/` as the corpus | UI refuse / format samples stay there |
| Full journal dumps in git | Forbidden |
| QA Pass 3 / Step **8.6.1** / **9.1** | Separate queues |
| Parallel subagents on one VM1 | Forbidden |
| `DEFAULT` / live Forge lab | Forbidden |
| `tofu apply` / `destroy` | Forbidden |
| In-app pack catalog / downloading packs | **Rejected** product |
| Pack replace light swap | After-v1 |

---

## Progress dashboard

| ID | Title | Status | Parallel | Cursor |
|----|-------|--------|----------|--------|
| **P1** | Layout, schemas, gitignore, `PROTOCOL.md` | **DONE** | SEQUENTIAL | agent |
| **P2** | Headless `PackTestHarness` (same Core Change-pack path) | **DONE** | SEQUENTIAL — consumes P1 paths | agent |
| **P3** | Skills wrapping protocol + harness | **DONE** | SEQUENTIAL — consumes P1+P2 CLI | agent |

**After this plan:** operator may seed `pack-tests/packs/` + sidecars and run `/pack-test-phase`. Do **not** auto-start that from this changelog. [`NEXT.md`](NEXT.md) → Step **8.5.2** Pass 3 (**blocked** until the operator says so).

---

## Parallel groups

None.

---

## P1 — Layout, schemas, gitignore, protocol

**Status:** DONE  
**Parallel:** SEQUENTIAL — P2/P3 read these paths  
**Cursor mode:** agent

**Read first**

- This file’s protocol + Scrutiny
- [`Local-Config.md`](Local-Config.md) (config dir + `data/` ignore)
- [`Sample-Packs.md`](Sample-Packs.md) (do **not** reuse as corpus; one “see also” sentence)
- Repo root `.gitignore` (`data/` already ignored)

**Do**

1. Create tracked `pack-tests/` (repo root) with this layout:

   ```text
   pack-tests/
     PROTOCOL.md              # SoT for skills (write in this step)
     catalog.yaml             # schema + packs: [] (no live pack rows required)
     client-only/_example.yaml
     phases/_template/manifest.yaml
     packs/.gitkeep           # binaries gitignored
     README.md                # one screen: purpose, not sample-packs, how to add a pack
   ```

2. Gitignore (root `.gitignore`): `pack-tests/packs/*` except `.gitkeep`; `pack-tests/**/logs/`; `pack-tests/.lock`. Keep result YAML and `PROTOCOL.md` tracked.

3. **`catalog.yaml` schema** (`schema_version: 1`). Each pack row:

   | Field | Role |
   |-------|------|
   | `id` | Stable slug (result filename) |
   | `filename` | Exact file under `pack-tests/packs/` |
   | `sha256` | Required before a live test; P1 may leave empty with a comment |
   | `platform` | `modrinth` / `curseforge` / `homemade` |
   | `format` | `mrpack` / `cf-server` / `unstructured` / `jar-root` |
   | `loader` | `fabric` / `forge` / `neoforge` |
   | `loader_version` | Used when Hybrid would ask identity |
   | `minecraft` | Expected MC version |
   | `java_major` | Expected Java major |
   | `size_class` | `small` / `medium` / `large` (operator hint) |
   | `client_only_sidecar` | Path under `pack-tests/client-only/` |

   Do **not** put client-jar lists in the catalog.

4. **Sidecar schema** (`client-only/<id>.yaml`): `pack_id`, `verified_utc`, `jars: []` (filenames as installed on disk / in the archive). Example file only in P1.

5. **Result schema** (`phases/<phase>/results/<id>.yaml`):

   - `schema_version`, `pack_id`, `filename`, `sha256`
   - `started_utc`, `finished_utc`
   - `verdict` (Scrutiny enum)
   - `ready_for_next` (bool)
   - `fail_message` (Manager/harness one-liner, not a stack)
   - `identity.expected` / `detected` / `applied` (MC, loader, loader version, Java)
   - `skip_counts` (automatic client, unknown kept) — short; no full jar dump
   - `health` (`rcon_list`, `crash_loop`, `fatal`, `quarantine`)
   - `log_excerpt_path` (tracked or under `logs/` — prefer `logs/<id>.excerpt.txt` gitignored if it can still be large; keep excerpt **≤80 lines** FATAL/ERROR)
   - `infra` (ssh, vm1, minecraft_unit, idle_disabled)
   - `notes` (optional string list)

6. **Phase `manifest.yaml`:** `phase_id`, `status` (`pending` / `running` / `aborted` / `complete`), `abort_reason`, `consecutive_infra_fails`, `idle: disabled-for-phase`, `queue[]` (`id`, `status`, `result` path), pointers to catalog + PROTOCOL. Template only in P1.

7. Write **`pack-tests/PROTOCOL.md`**: single-pack procedure (run harness, write result, ready-gate, stop); phase parent (lock, sequential spawn, abort on ≥2 infra_fail, never load full logs); analyze (read results + sidecars → `EXECUTIVE-SUMMARY.md`; do not implement product fixes). Cheap-model checklist style.

8. Short pointer in [`Local-Config.md`](Local-Config.md) (pack-test config dir + gitignored packs) and one sentence in [`Sample-Packs.md`](Sample-Packs.md) that expected-to-work Change-pack corpus is `pack-tests/`, not this folder. No Guide.md (not user-visible product UX).

**Test**

- `git check-ignore` on a fake `pack-tests/packs/foo.mrpack` and `pack-tests/phases/x/logs/j.txt`.
- Catalog / template YAML parse as YAML (no live packs required).
- PROTOCOL names verdicts + ready-gate + abort rule matching Scrutiny.

**Done when**

- Layout + schemas + gitignore + PROTOCOL exist. No C#, no skills bodies, no live SSH, no pack bytes committed.

**Changelog:** 2026-08-24 — layout, schemas, gitignore, `PROTOCOL.md`. No C# / skills / SSH.

---

## P2 — Headless PackTestHarness

**Status:** DONE  
**Parallel:** SEQUENTIAL — consumes P1 paths  
**Cursor mode:** agent

**Read first**

- This file’s protocol + Scrutiny
- `pack-tests/PROTOCOL.md` + `pack-tests/catalog.yaml` schema
- `src/McManager.Core/Setup/SetupBootstrapService.cs` (`ReplacePackAsync`)
- `src/McManager.Core/Setup/PackReplacePlanner.cs`
- `src/McManager.Core/Setup/DerivedPackWorkflow.cs`
- `src/McManager.Hybrid/ViewModels/ServerManagementViewModel.cs` (`InstallPackReplaceAsync` — **mirror order**, do not copy UI)
- [`docs/Agent-Deploy-Pitfalls.md`](Agent-Deploy-Pitfalls.md) before any SSH

**Do**

1. Add `src/McManager.PackTestHarness/` (console) to `src/McManager.slnx`. `ProjectReference` **Core only**. No new NuGet. No Hybrid/WPF reference.

2. CLI (keep flags few): `--pack <id>` `--catalog <path>` `--phase <phase-dir>` `--wipe-world` (default on; this suite always wipes) `--analyze-only` (no SSH). `MCMANAGER_CONFIG_DIR` must resolve `mcmgr-pack-test` with TESTING `config.local.json`; refuse repo Forge `data/config.local.json`.

3. For one catalog id:

   1. Resolve `pack-tests/packs/<filename>`; verify SHA if catalog has one (mismatch → `infra_fail` / usage, do not install).
   2. `SetupPackImport.AnalyzeFile`.
   3. If Hybrid would confirm identity (`DerivedPackIdentity.NeedsIdentityConfirm`): apply catalog `minecraft` / `loader` / `loader_version` / `java_major`, then `DerivedPackWorkflow.BuildAndRetain` into the pack-test data dir. Original archive untouched.
   4. Default-Keep assisted review. **Do not** load client-only sidecars. If `!CanContinue` / freeze → write result `blocked_freeze`, run ready-gate, exit `1`.
   5. Unless `--analyze-only`: `ReplacePackAsync(vm1, new PackReplaceRequest(installPath, wipeWorld: true, dataDirectory), log)`.
   6. Map Core fail copy to `product_fail` vs `timeout` vs `infra_fail` (SSH/connect/VM STOPPED → infra). Quarantine notice + RCON OK → `pass_quarantined`.
   7. Collect journal **excerpt** (capped); full journal only under gitignored `logs/`.
   8. Ready-gate (Scrutiny). Stop Minecraft on non-pass. Write `results/<id>.yaml`. Exit codes per Scrutiny.

4. Do **not** persist sidecar jars as Layer 2 excludes. Do **not** start idle-agent redeploy. Do **not** SoftStop the door.

5. Seed doc in `pack-tests/README.md`: how to copy TESTING config into `mcmgr-pack-test` (no secrets in git).

**Test**

- `dotnet build` the harness + Core tests still pass (do not expand pack-import product rules).
- `--analyze-only` on a missing file / SHA mismatch → non-zero, result YAML if phase dir exists.
- Optional live Change pack **only** if the operator authorizes VM1 in that chat (one small catalog pack). Default: no live replace.

**Done when**

- Harness mirrors Hybrid Change-pack Core order; wipe-world default; sidecars unused; result schema filled; analyze-only works without SSH.

**Changelog:** 2026-08-24 — `src/McManager.PackTestHarness/`; Core Change-pack order; `--analyze-only`; wipe default; sidecars unused.

---

## P3 — Skills

**Status:** DONE  
**Parallel:** SEQUENTIAL — wraps P1 PROTOCOL + P2 CLI  
**Cursor mode:** agent

**Read first**

- This file’s protocol + Scrutiny
- `pack-tests/PROTOCOL.md`
- `.cursor/skills/next-step/SKILL.md` (frontmatter / length — stay thin)
- P2 harness `--help` / flags (after P2)

**Do**

1. Add three project skills (`disable-model-invocation: true`), each pointing at `pack-tests/PROTOCOL.md` instead of duplicating it:

   | Skill | Who | Does |
   |-------|-----|------|
   | `pack-test-one` | Composer 2.5 OK | One catalog id: run harness, do not interpret sidecar, stop after result + ready-gate |
   | `pack-test-phase` | Parent | Take/create lock; disable idle for phase; spawn **one** `pack-test-one` at a time; abort on ≥2 consecutive `infra_fail`; never read full logs; do not write the executive summary |
   | `pack-test-analyze` | Grok (not Composer) | After queue complete/abort: results + sidecars → `phases/<id>/EXECUTIVE-SUMMARY.md` (clusters: infra vs client-jar kept vs Java vs overlay leftover vs RCON-timeout-with-Done vs quarantine). Do **not** `/phase-planning` unless the operator asks |

2. Skills must: TESTING only; `mcmgr-pack-test`; refuse if `.lock` is foreign or Pass 3 / another chat owns VM1; re-enable idle only at **phase** end; no `git push`; no pack downloads.

3. One line in [`docs/Agent-Workflow.md`](Agent-Workflow.md) skills table. When P3 finishes, update [`NEXT.md`](NEXT.md) to Pass 3 **blocked** (do not start Pass 3).

**Test**

- Skills exist and name the harness CLI + PROTOCOL headings. No live phase.

**Done when**

- Three skills + Agent-Workflow pointer. Protocol remains SoT.

**Changelog:** 2026-08-24 — `pack-test-one` / `pack-test-phase` / `pack-test-analyze`; Agent-Workflow pointer; `.cursor/skills/` tracked. Protocol remains SoT. Pass 3 blocked.

---

## Changelog (plan file)

| Date | Note |
|------|------|
| 2026-08-24 | **P3 DONE** — skills `pack-test-one` / `pack-test-phase` / `pack-test-analyze` wrap PROTOCOL + harness CLI; Agent-Workflow pointer; `.cursor/skills/` tracked. Plan **COMPLETE**. Pass 3 **blocked** until the operator says so. |
| 2026-08-24 | **P2 DONE** — `McManager.PackTestHarness` (Core-only console): analyze → identity/derived zip → default-Keep → `ReplacePackAsync`; `--analyze-only`; result YAML + ready-gate. Living **NEXT = P3**. Pass 3 still blocked. |
| 2026-08-24 | **P1 DONE** — `pack-tests/` layout, catalog/sidecar/manifest schemas, gitignore, `PROTOCOL.md`. Living **NEXT = P2**. Pass 3 still blocked. |
| 2026-08-24 | Operator pointed live [`NEXT.md`](NEXT.md) here (P1 `ready`). Pass 3 still blocked. |
| 2026-08-24 | Created (docs only). P1 NEXT. |
