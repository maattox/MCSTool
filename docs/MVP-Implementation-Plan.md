# MVP Implementation Plan

**Status:** Living checklist for agents and the operator.  
**Product intent authority:** lab [`PRODUCT-IDEAS.md`](../../OCI-mc-server-manager/PRODUCT-IDEAS.md) (MVP section). When this plan and PRODUCT-IDEAS disagree on *what* MVP means, **PRODUCT-IDEAS wins** — update this file.  
**Suggested narrative order:** lab [`docs/Development-Steps.md`](../../OCI-mc-server-manager/docs/Development-Steps.md).  
**Live infra / on-box SoT:** lab repo (`Infrastructure-Information.md`, `door_vm/`, `vm_agent/`, `docs/VM-Software.md`).  
**Code SoT for Manager:** **this repo** (`OCI-mc-server`).

**Cost rule:** keep OCI spend at **$0** (Always Free–eligible) unless the operator explicitly accepts paid changes.

**OCI API:** follow [`OCI-API-Usage.md`](OCI-API-Usage.md) and Oracle [Using the API](https://docs.oracle.com/en-us/iaas/Content/API/Concepts/usingapi.htm) — **429** exponential backoff (≤60s), lifecycle waiters (≤30s between polls, ~20 min), list pagination, modest Object Storage chatter (~50k requests/month). Prefer Get-by-OCID from local config over chatty List discovery.

---

## How agents must use this file

1. **Read this file first** (especially [Progress dashboard](#progress-dashboard) and [Agent stop protocol](#agent-stop-protocol)).  
2. Implement **only the single next incomplete large step** marked **NEXT** (or the first **TODO** in the current phase if none is marked NEXT).  
3. After finishing that large step:
   - Update this file: mark the step **DONE**, set the following step to **NEXT**, note date + short notes in the step’s changelog line.
   - **Stop.** Do not start the next large step in the same session unless the operator explicitly says to continue.
4. In the chat reply to the operator:
   - Summarize **what was just done**
   - List **how to test** it
   - State **what the next step will be**
   - **Ask** whether to continue, pause, or adjust the plan
5. **Never create git commits** (operator commits in Visual Studio). You may suggest a commit message.
6. Do **not** implement v1 / later features from PRODUCT-IDEAS unless the operator asks.
7. Do **not** put Avalonia product code in the lab repo. Lab changes are OK only when a step explicitly requires on-box / door / idle-agent / infra-doc updates.

### Agent stop protocol

Between **large steps** (Phase / Step headings below), always stop for operator feedback.  
**Small sub-bullets** inside one large step may be completed together in one session if they are required to make that step testable.

If blocked (missing OCIDs, unclear UX, cost risk), stop and ask — do not guess in a way that opens `0.0.0.0/0` or accrues spend.

### Operator prompt (copy-paste for a new agent)

```text
Read docs/MVP-Implementation-Plan.md in OCI-mc-server. Implement only the step marked NEXT.
When done: update the plan statuses, stop, tell me what you did, how to test, what’s next, and ask if I want to continue or adjust.
Do not commit. Do not start the following large step unless I say so.
```

---

## MVP goal (from PRODUCT-IDEAS)

> Non-expert admin follows the guide, installs one app, deploys a **private Vanilla** stack, friends use the reserved IP with door wake, idle/budget stops protect free tier, worlds are backed up, admin manages whitelist and basic power from Manager.

**MVP success criteria**

- [ ] Friend can wake and play on reserved play IP  
- [ ] Empty / budget SoftStop works  
- [ ] Door refuses wake when daily budget exhausted (clear MOTD/kick)  
- [ ] Admin can whitelist and repair SSH allow IP without Console  
- [ ] World backups under ~9.5 GB Object Storage policy  
- [ ] Setup survives capacity wait and can resume  
- [ ] Single Windows installer → one Manager app (Setup integrated)  
- [ ] App can check GitHub Releases for updates + show release notes  

**Explicitly out of MVP:** public game access, paid/spend mode, modded UI / Optimized Vanilla (Paper) / pack analyze, per-day budget sculpting, usage-API 48h reconcile Function, rich MOTD editor, interactive PTY console, event-driven door handback, macOS/Linux Manager, VPN / Distant Horizons engineering, silent OCI probing on startup, notification-center / settings / overflow chrome, oversized-world SSH download UX, Players tab.

---

## Progress dashboard

| Phase | Focus | Status |
|-------|--------|--------|
| **0** | Operator infra + dual-repo foundation | **DONE** |
| **1** | Avalonia manage MVP (existing stack) | **DONE** |
| **2** | On-box / contract freeze for product | **DONE** |
| **3** | Setup wizard + OpenTofu greenfield | **TODO** — next: Step 3.1 |
| **4** | Connect-existing (auto-detect + meta) | **TODO** |
| **5** | UI polish (novice-ready) | **TODO** |
| **6** | Guide + greenfield E2E proof | **TODO** |
| **7** | Packaging, updates, closed beta | **TODO** |
| **8** | MVP exit review | **TODO** |

**Current NEXT step:** [3.1 — OpenTofu module skeleton](#step-31--opentofu-module-skeleton-product-names)

---

## Phase 0 — Foundation (DONE)

Operator Always Free stack + product repo bootstrap. Do not re-do unless something regressed.

| Item | Status | Notes |
|------|--------|-------|
| Dual-VM doorbell (reserved IP, door MOTD/wake, reconcile) | DONE | Lab live — see VM-Software |
| VM1 idle/budget SoftStop + ledger/lease + shape detect | DONE | `vm_agent/` |
| Object Storage Phases 1–5 + world backup soft cap | DONE | Lab |
| $1 budget → Function SoftStop | DONE | Lab |
| Dual-repo (lab vs `OCI-mc-server`) | DONE | 2026-08-10 |
| Avalonia scaffold (net8, App + Core) | DONE | |
| Local config seed (`data/config.local.json`, friends) | DONE | See `docs/Local-Config.md` |
| `LocalConfigStore` + shell status load | DONE | |
| Agent rules (no commits, product vs lab) | DONE | |

**Exit:** Operator can still run the stack with Python Manager; Avalonia opens and reports config OK.

---

## Phase 1 — Avalonia manage MVP (existing stack)

**Goal:** Day-to-day manage the **already deployed** operator stack without relying on the Python tkinter app for normal sessions. **No** Setup/OpenTofu in this phase.

**Repo:** `OCI-mc-server` only (unless a bug forces a lab on-box fix).

**UI bar:** Functionality + basic layout; visual polish is Phase 5.

### Step 1.1 — OCI session + Core service skeleton

**Status:** DONE  
**Depends on:** Phase 0

**Do**

- Add OCI .NET SDK packages needed for Core (at least Compute, Object Storage, Virtual Network / Security List as required by later steps).
- Implement `OciSession` (or equivalent) from `ManagerLocalConfig`: load `~/.oci` config file + profile + region.
- Thin facades / interfaces: Security List, Compute instance get/action, Object Storage get/put/list, Door HTTP client (wake / idle-empty / status).
- Wire retry / waiter habits early where practical (see [`OCI-API-Usage.md`](OCI-API-Usage.md)): 429 backoff; paginated lists; log `opc-request-id` on failures.
- No full UI yet beyond proving session construction (optional debug status).

**Test**

- App starts; can fetch VM1 lifecycle state and/or Security List display name via API using local config.
- Failure modes: missing PEM / wrong region → clear error string.

**Done when:** One read-only OCI call works from Core, surfaced in UI or debug status.

**Changelog:** 2026-08-10 — Added `OciSession`, Compute/SecurityList/ObjectStorage/Door facades; UI probes VM1 lifecycle via OCI API.  
2026-08-11 — Backfill before 1.4: `RetryConfiguration` (429), `opc-request-id` in errors, removed silent startup OCI probe (OCI-API-Usage).

---

### Step 1.2 — Shell layout: top bar + tabs (empty)

**Status:** DONE  
**Depends on:** 1.1

**Do**

- Main window structure per PRODUCT-IDEAS Manager UI:
  - Top bar **left:** status panel placeholders (playability, play IP, players, today’s usage vs budget), **copy play IP** control (can wire clipboard even if other fields are “—”), Start/Stop, Restart
  - Mini-terminal *visual* styling may wait for Phase 5; structure and fields are this step / 1.4
  - Top bar **right** (bell / settings / overflow): **out of MVP** — leave empty or omit until v1
  - Tabs: **Whitelist**, **Usage**, **Server Management** (backups / Download World Save), **Advanced / Danger Zone**
- Wire play IP from local config; other fields “—” until later steps.
- Keep Fluent theme; no heavy design polish.

**Test**

- Window opens; tabs switch; play IP visible from config.

**Done when:** Navigation shell exists and is stable.

**Changelog:** 2026-08-10 — Top bar (status, play IP, placeholders, disabled power buttons) + four tab placeholders.  
2026-08-11 — Plan note: mini-terminal polish → Phase 5; copy IP in left cluster; right chrome deferred to v1.

---

### Step 1.3 — Whitelist → Security List sync

**Status:** DONE  
**Depends on:** 1.2

**Do**

- Load/save `data/friends.local.json`.
- CRUD UI (IP required, name optional; admin flag for SSH rule ownership — match lab semantics).
- Sync to OCI Security List:
  - Preserve ICMP / non-owned rules (full replace caution — same as Python Manager).
  - Minecraft `:25565` TCP+UDP allow `/32`s; SSH `:22` for admins; door admin `:8080` for admins if product keeps door UI exposure.
- MVP: **Security List primary**; host firewalld sync is optional/deferred (product lean is Security List–only) — document choice in step notes.
- Advanced: manual SSH IP repair when admin public IP changed.

**Test**

- Add/remove friend IP; confirm Security List ingress in Console.
- Confirm non-managed rules (ICMP, VCN→25565, etc.) survive sync.

**Done when:** Operator can maintain private allowlist without Python Manager / Console for normal edits.

**Changelog:** 2026-08-10 — Friends CRUD + save; Security List–only sync (MC TCP/UDP, admin SSH + door :8080); preserves non-/32 rules; manual Update admin IP.

---

### Step 1.4 — Status polling + door-aware Start/Stop/Restart

**Status:** DONE  
**Depends on:** 1.2, 1.1

**Do**

- Poll VM1 lifecycle + door `/api/status` (door ephemeral).
- Top-bar status panel: door-ish / playable / starting / degraded as available; players when known; play IP + **copy to clipboard**.
- Status refresh: use modest intervals per [`OCI-API-Usage.md`](OCI-API-Usage.md) (e.g. **15–60s** while focused; **not** 1s OCI loops). After Start/Stop, wait for lifecycle with **exponential backoff** (SDK waiter style: few seconds → ≤30s between polls, ~20 min timeout). Retry **429** with backoff up to 60s.
- **Start** = door-aware wake (`POST /api/wake` or documented equivalent), not bare Compute start alone.
- **Stop** = door-aware idle-empty / graceful path (prefer door handback; align with lab Force Stop + reconcile behavior; cold backup remains on-box).
- **Restart** = Minecraft systemd restart only (SSH); disabled if VM not running.
- Advanced: separate raw VM SoftStop / start if needed for break-glass (warn).
- Do **not** implement notification center / settings gear / overflow menu (v1).

**Test**

- From idle: Start → wake → play IP serves Forge/Vanilla; Stop → IP returns to door.
- Restart while up cycles Minecraft only.
- Copy IP places reserved play address on clipboard.
- Do not leave VM1 running accidentally after tests (cost).

**Done when:** Operator can power the play path without Python Manager.

**Changelog:** 2026-08-11 — Door wake/idle-empty Start/Stop; SSH Restart; Copy IP; door ~15s / OCI ~30s poll (slow when unfocused); lifecycle waiter; Advanced break-glass Compute; Step 1.1 OCI-API backfill. Door stop skips cold backup (OS-ISSUE-6).

---

### Step 1.5 — Usage / budget view (Object Storage)

**Status:** DONE  
**Depends on:** 1.1, 1.2

**Do**

- Pull `ledger/usage.json`, `budget/config.json`, dirty flags (match lab Phase 1 contracts).
- Dashboard: monthly allowance, MTD usage, avg hours/day, rollover / leftover bank as data allows.
- Edit + publish budget config (soft caps, idle timeout, targets) with confirmations.
- Refresh on tab open; while open, poll ~2 minutes (PRODUCT-IDEAS sync model).
- Display today’s uptime vs budget on top bar.

**Test**

- Refresh matches Python Manager Usage tab / Console objects.
- Save budget dirties door/vm1 flags as designed.

**Done when:** Usage/budget day-2 no longer needs Python Manager.

**Changelog:** 2026-08-11 — Core `UsageMath`/`UsageBudgetStore` + Usage tab dashboard/edit; 2 min poll while selected; top-bar Today. Lease apply / interval editor deferred.

---

### Step 1.6 — Backups list / download / upload-replace

**Status:** DONE  
**Depends on:** 1.1, 1.2

**Do**

- List `backups/world-*.zip` from Object Storage; show size/date.
- **Download World Save** — download chosen/latest backup to an operator-chosen directory on the PC.
- Upload/replace world via Object Storage + dirty/meta flag; SSH fallback when VM1 up (document).
- Respect soft-cap messaging (eviction is on-box upload path; UI should not encourage breeching 9.5 GB).
- **Out of this step (v1):** adaptive SSH download when oversized-world flag is set; bell notification for that flag. If the flag object already exists in the bucket, UI may show a simple status string, but full notification center is v1.

**Test**

- List matches bucket; download opens; upload appears in bucket (use a small test zip if needed).

**Done when:** Server Management backups tab is usable for restore/replace workflows via Object Storage.

**Changelog:** 2026-08-11 — List/download/upload streaming OS APIs; Server Management UI + soft-cap messaging; **Replace via SSH when RUNNING** (no on-box dirty-flag apply yet — deferred Phase 2). Manager does not evict oldest zips.

---

### Step 1.7 — Advanced / Danger Zone (idle)

**Status:** DONE  
**Depends on:** 1.5

**Do**

- Idle timeout control; enable/disable idle agent + daily guardrails with **strong warnings + confirm**.
- Document/safety copy: disable is testing-only; **VM1 boot force-enables** idle and rewrites OS config (OS-ISSUE-7 / PRODUCT-IDEAS).
- Push agent-relevant settings via Object Storage budget config and/or SSH config push (match lab behavior as closely as practical).

**Test**

- Toggle disable → confirm warning; reboot/Minecraft restart restores idle (as designed).
- Idle timeout change reflected in on-box or OS config after sync path.

**Done when:** Danger Zone matches MVP safety story.

**Changelog:** 2026-08-11 — Danger Zone idle timeout/warn/enable; strong disable confirm; OS budget publish + SSH patch `/etc/mc-manager/config.json` + `mc-idle-watch.timer` when RUNNING; OS-ISSUE-7 banner. No separate daily-only disable flag.

---

### Step 1.8 — Manage MVP exit gate

**Status:** DONE  
**Depends on:** 1.3–1.7

**Do**

- Operator dogfood session: whitelist, wake/play/stop, usage refresh, backup list, danger zone — **without** Python Manager.
- Update lab `docs/VM-Software.md` Avalonia row to “manage MVP usable”.
- Fix blockers only; no Setup yet.

**Test**

- Written smoke checklist in step notes all pass.

**Done when:** Operator signs off that Python Manager is optional for daily ops.

**Changelog:** 2026-08-11 — Phase 1 exit: operator-passed smoke across 1.3–1.7; Avalonia **manage MVP usable** in lab `VM-Software.md`; Python Manager optional for daily ops. Build clean; no blockers. NEXT = Step 2.1.

**Phase 1 exit:** Core manage flows work on the live stack.

---

## Phase 2 — Contract freeze + product-shaped on-box

**Goal:** Freeze Object Storage / agent contracts and align remaining on-box gaps so Setup does not encode a moving target. Prefer **Vanilla** path for product MVP even if operator lab still runs Forge. On-box game install follows [`Minecraft-Server-Deployment-Blueprint.md`](Minecraft-Server-Deployment-Blueprint.md) (game manifest + `/opt/mcmgr/` layout + generic systemd) — MVP only *writes* a Vanilla manifest; shared contracts stay loader-ready.

### Step 2.1 — Document OS / meta / ledger contracts

**Status:** DONE  
**Do**

- Tracked doc in this repo (e.g. `docs/Contracts-Object-Storage.md`): object keys, JSON shapes, writers, dirty flags, `infra_schema` / `stack_version` fields.
- Align with live lab phases; note per-interval `ocpus` / `memory_gb` (MVP forward-compat for v1 resize).
- Include **`meta/infra.json`** field list from lab PRODUCT-IDEAS (Connect existing OCID set).
- Document **oversized-world backup flag** key + semantics (VM1 sets when zip > soft cap; skip further OS uploads). Full Manager bell + SSH download remains **v1**; contract freeze should still name the flag.

**Test**

- Doc reviewed against live bucket sample objects.

**Done when:** Avalonia and future OpenTofu/bootstrap share one written contract.

**Changelog:** 2026-08-11 — Added [`Contracts-Object-Storage.md`](Contracts-Object-Storage.md): live keys/shapes/writers/readers, dirty protocol, ledger v2 + per-interval shape, canonical `meta/infra.json` v2 (`infra_schema` / `stack_version`), oversized-world + restore/backup-lock contracts, and explicit deployed conformance gaps. Reviewed read-only against 14 live bucket objects; no objects changed.

---

### Step 2.2 — Infra meta object (`meta/infra.json` or equivalent)

**Status:** DONE  

**Do**

- Define + write/read infra meta per PRODUCT-IDEAS contract: region, tenancy, compartment, play IP + OCID, VCN/subnet/Security List, VM1 + door instance/secondary private IP OCIDs, Object Storage namespace/bucket/bucket_id, shape, mode `always_free`, app/infra schema versions.
- Manager can publish meta when managing existing stack (seed from local config) so Phase 4 auto-detect has something to find.
- Do **not** put SSH private keys or RCON passwords in meta.

**Test**

- Object exists in bucket; app round-trips fields.

**Done when:** Meta is readable by Manager and usable by Phase 4.

**Changelog:** 2026-08-11 — Added `InfraMetaDocument` (nested v2) + `InfraMetaStore` get/publish; Advanced tab Refresh/Publish from local config (no secrets); migrated live legacy flat v1 → nested v2; round-trip validated (version/infra_schema=2, OCIDs match local, secret scan clean). Dirty `meta.door`/`meta.vm1`.

---

### Step 2.3 — Vanilla on-box path readiness (lab or bootstrap scripts)

**Status:** DONE  

**Read first:** [`Minecraft-Server-Deployment-Blueprint.md`](Minecraft-Server-Deployment-Blueprint.md) — authoritative for on-box game install mechanism. Map for this step (blueprint §30): **§3 / §4.1** (game manifest), **§5** (`/opt/mcmgr/` + `mcmgr` user), **§6** (generic systemd unit), **§7** (`server.properties` + EULA), **§8** (RCON secret), **§9** (Java / ARM64), **§16** (Vanilla piston-meta). Implement **that** design — do not re-derive a simpler “jar + `java -jar`” Vanilla-only path. Manifest schema + unit generator must be **generic from day one** so Paper/Fabric/NeoForge/Forge (v1) are new installer modules + new manifest values, not a rewrite.

**Do**

- PRODUCT-IDEAS MVP game = **Vanilla only** with **user-selected version** (releases-oriented; snapshots optional later in Setup Advanced).
- Ship a concrete bootstrap recipe (scripts in this repo and/or lab) that Phase 3 can invoke over SSH. Prefer the blueprint §13.2 split: a **shared common driver** (layout, Java, properties/EULA/RCON, unit, final manifest write) plus a **Vanilla installer module** (version resolve → download → verify → place artifact) — even though only the Vanilla module exists yet.
- **Directory / user (§5):** create `mcmgr` system user/group and product tree under `/opt/mcmgr/` (`server/`, `backups-work/`, `bin/`) and `/etc/mcmgr/`, `/var/lib/mcmgr/`. New automated bootstrap must **not** special-case the lab’s `/home/ubuntu/minecraft/server` path. Do **not** rip the operator’s live Forge lab unless asked — Connect-existing / local config continue to read whatever `world_path` a stack actually has.
- **Java (§9):** install matching **aarch64** Temurin **JRE headless** via Adoptium apt repo (REST API archive only as fallback); pin `java_major` from Mojang version metadata; record `java` object fields in the manifest.
- **Vanilla artifact (§16):** `GET https://piston-meta.mojang.com/mc/game/version_manifest_v2.json` → resolve `id` → version metadata → `downloads.server.url` + `sha1` + `javaVersion`; download and verify SHA-1 before enabling the unit.
- **EULA + `server.properties` (§7):** write `eula.txt` only for an operator-accepted EULA; read-modify-write managed keys only (`enable-rcon`, `rcon.port`, `rcon.password`, whitelist defaults, etc. per §7.3) — never clobber unknown keys; never set `online-mode=false`.
- **RCON (§8):** generate `/etc/mcmgr/rcon.secret` (0600, root); same value into `server.properties` and (when idle-agent config exists) `/etc/mc-manager/config.json`. Never put the password in `game-manifest.json`, Object Storage, or logs (manifest stores `password_secret_ref` only). Include `/opt/mcmgr/bin/rcon-graceful-stop.sh` for unit `ExecStop=` (same safe-stop semantics as today’s idle-agent graceful stop).
- **systemd (§6):** generate **one generic** `minecraft.service` from `launch_command` (executable + args + working_directory) — no per-distribution unit templates; run as `User=mcmgr`; enable so door wake boots the game.
- **Game manifest (§3 / §4.1):** write authoritative `/etc/mcmgr/game-manifest.json` **once at successful end** (fixture shape §4.1). Do not leave a partial “success” manifest mid-install (blueprint §14.2). Full Object Storage `meta/infra.json` `game` mirror can wait for Setup success in Phase 3; local/support recording of version + jar sha1 is enough for this step’s dry-run.
- May live as scripts until OpenTofu user-data is ready. Product path must not assume Forge UI.
- Do **not** implement Paper / Modded / pack installers here (v1). Do **not** hard-code Vanilla-only assumptions into shared manifest/unit/layout code.

**Test**

- On a test VM or documented dry-run: chosen release (e.g. `latest.release` or pinned id) installs under `/opt/mcmgr/server`, writes a valid Vanilla `game-manifest.json`, starts under systemd on boot / after `systemctl start minecraft`, and RCON localhost stop path works.

**Done when:** Setup Phase 3 has a concrete Vanilla bootstrap recipe that matches the blueprint Part A contract (generic layout/manifest/unit + Vanilla module), driven by Mojang piston-meta — not a one-off jar URL script.

**Changelog:** 2026-08-11 — Added product SoT [`onbox/mcmgr/`](../onbox/mcmgr/): shared `common/driver.sh` + `modules/bootstrap-vanilla.sh`, generic unit template, RCON/EULA/properties helpers, `/var/lib/mcmgr/bootstrap-state.json` staging. Offline proof via `dry-run/run-dry-run.sh` + [`tests/fixtures/game-metadata/`](../tests/fixtures/game-metadata/) (1.21.1 / 1.21.11). No live VM / no Forge lab migration. NEXT = Step 2.4.

---

### Step 2.4 — Door / agent product gaps that block MVP

**Status:** DONE  

**Do**

- Triage lab `docs/Issues.md` for MVP blockers only (e.g. door SoftStop skipping world backup OS-ISSUE-6 if success criteria require backup on all stop paths).
- Align door wake gate with Object Storage budget SoT if any interim Phase A-only path remains.
- Keep door **C-first**; no heavy Python on Micro.
- **Idle agent ↔ game manifest seam** (blueprint §10 / §10.2): confirm bootstrap (or a thin post-install step) read-modify-writes `/etc/mc-manager/config.json` `world_path`, `minecraft_unit`, `rcon_port`, `rcon_password` from `/etc/mcmgr/game-manifest.json` whenever the manifest is written/updated. Idle agent code itself should stay config-driven (no Vanilla/Paper/Modded branches required).
- If practical: VM1 world backup path sets oversized-world **flag** when zip > soft cap (lab already refuses upload)—so SoftStop does not keep failing loudly. Manager UX for the flag is v1.

**Test**

- Blocker issues closed or explicitly deferred with operator OK in this plan.
- After a Step 2.3-style install (or fixture), idle-agent config keys match the on-box game manifest / RCON secret.

**Done when:** No known on-box blocker for MVP success criteria (or deferred with operator initials in changelog).

**Changelog:** 2026-08-11 — Triaged Issues: **OS-ISSUE-6 deferred** (idle SoftStop still backs up; door Stop skip OK for MVP soft-cap criterion). Door `do_wake`/`os-refresh` now `pull_os_budget.sh --force` (OS-ISSUE-8 fixed); redeployed door from `door_vm/` (resolve order prefers SoT). Product `idle_agent_sync.sh` after `manifest_write` + dry-run §10.2 assert. `vm_agent/world_backup.py` sets/skips `meta/oversized-world-backup.json`; agent redeployed; synthetic set/skip smoke on VM1. NEXT = Step 3.1.

---

## Phase 3 — Setup wizard + OpenTofu

**Goal:** Fresh **dedicated compartment** deploy from the app: private Vanilla doorbell stack + IAM + Object Storage + $1 budget Function + bootstrap.

### Step 3.1 — OpenTofu module skeleton (product names)

**Status:** NEXT  

**Do**

- Add `infra/` (or `tofu/`) OpenTofu HCL: VCN, subnet, Security List, VM1 A1 Flex, VM2 Micro, reserved public IP + secondaries, NSG/SL rules for private Minecraft/SSH/door admin, Object Storage bucket, IAM dynamic groups/policies (least privilege where practical), budget + Events + Function placeholders as needed.
- **Product naming** per lab PRODUCT-IDEAS: compartment display name `mcmgr`; resources `mcmgr-vcn`, `mcmgr-subnet-public`, `mcmgr-sl`, `mcmgr-vm1`, `mcmgr-door`, `mcmgr-play-ip`, bucket `mcmgr-shared-data`, etc. Freeform tag on compartment: `mcmgr-domain=mc-server-compartment`.
- Do not clone ad-hoc Console names from the first manual deploy.
- Outputs → values Manager needs for local config + `meta/infra.json`.
- **Game-layer boundary** (blueprint §13.1): OpenTofu / cloud-init may create the `mcmgr` user/group, empty `/opt/mcmgr/` + `/etc/mcmgr/` / `/var/lib/mcmgr/` tree, baseline OS packages, and Adoptium apt **repo registration** — but must **not** install Minecraft, Java majors chosen in the wizard, loaders, or mod packs (version-sensitive work stays in SSH bootstrap from Step 2.3 / 3.3).

**Test**

- `tofu validate` / plan against empty test compartment (operator must approve any apply). Prefer plan-only until Step 3.3.

**Done when:** Validatable OpenTofu root module exists with documented variables.

**Changelog:** _(empty)_

---

### Step 3.2 — Setup wizard UX (no apply yet)

**Status:** TODO  

**Do**

- Integrated Setup (not a second exe): collect alert email, region/compartment strategy, **Minecraft version picker** (from Mojang manifest; default `latest.release`; releases-only unless Advanced), EULA accept, Vanilla confirm, Always Free docs confirmation link, capacity-handling consent, SSH key creation/import.
- See [`Minecraft-Server-Deployment-Blueprint.md`](Minecraft-Server-Deployment-Blueprint.md) §13 for which parts of this belong to the wizard UI vs the SSH bootstrap module vs OpenTofu — the wizard fetches version metadata **read-only for display**; the actual bootstrap module re-resolves it on-box at execution time. Manager must **not** re-implement authoritative jar URL/hash resolution in C# (§13.3).
- Persist wizard state for resume-later (including selected version id).
- Auth Token: collect when OCIR/Function push needed; prefer Windows Credential Manager for storage (not long-term plaintext); gitignored local OK only as temporary operator aid.
- Show plan summary before apply.
- **Out of MVP:** Vanilla vs Modded / Optimized Vanilla (Paper) / modpack upload-analyze (v1 — PRODUCT-IDEAS Setup game types). Durable rule (not just “later”): **no in-app mod/modpack catalog** (blueprint §2.4) — pack input is file picker / drag-and-drop of an already-exported archive only.

**Test**

- Walk wizard offline/mocked; state resumes after app restart; version list loads from manifest (or fixture).

**Done when:** Wizard UI complete without requiring live apply.

**Changelog:** _(empty)_

---

### Step 3.3 — Apply + bootstrap + capacity wait

**Status:** TODO  

**Do**

- Run OpenTofu apply from wizard; stream errors.
- Out-of-capacity: explain; Retry; optional poll every 5–10 min while app open; Stop checking; persist resume point.
- SSH bootstrap (blueprint §13 + Step **2.3** recipe — do not invent a shorter Vanilla path):
  - Door (`door_vm` productized tree)
  - VM1: invoke the shared bootstrap driver + **Vanilla** installer module (piston-meta → jar + sha1 verify → `/opt/mcmgr/` layout → Temurin aarch64 JRE → EULA + managed `server.properties` → RCON secret → generic systemd unit → final `/etc/mcmgr/game-manifest.json`) + idle agent deploy with §10.2 config sync from the manifest
  - Firewall/Security List baseline; write local config + Object Storage `meta/infra.json` **game** summary from the on-box manifest (version / identifying fields only — no secrets, no download URLs)
- Resumability (blueprint §14): track per-stage completion in `/var/lib/mcmgr/bootstrap-state.json`; skip completed stages on retry; write `game-manifest.json` only after success — not just wizard capacity-wait resume state.
- Wire Function image push to OCIR (Auth Token / credential store) + `$1` budget brake.

**Test**

- **Operator-approved** apply to a disposable compartment (or carefully chosen empty compartment).
- Full wake/play/stop + backup + budget gate smoke.
- Destroy or stop resources after test to protect Always Free hours / avoid clutter (operator decision).

**Done when:** Greenfield deploy from app reaches manageable stack.

**Changelog:** _(empty)_

---

## Phase 4 — Connect existing (MVP-light)

**Status:** TODO  

**Do**

- First-run: Setup **or** “I already have a stack” **or** button **Auto-detect infrastructure** (never silent probe on every launch).
- Auto-detect flow (PRODUCT-IDEAS):
  - Read `%USERPROFILE%\.oci\config`; try each usable profile.
  - List compartments; match display name **`mcmgr`** **or** freeform tag **`mcmgr-domain=mc-server-compartment`** (operator lab uses the tag on Default).
  - Find product bucket / `meta/infra.json`; validate required OCIDs.
  - One match → confirm connect with summary (region, compartment, play IP, VM names); multiple → chooser.
- Hydrate local Manager config from meta (SSH key path / RCON stay local-only).
- Soft `infra_schema` check OK for MVP; strong version enforcement can wait for v1.
- MVP must not brick the operator if detect finds nothing — fall back to Setup or manual config.

**Test**

- Connect to operator lab stack via auto-detect (tagged compartment) and/or meta/local config; manage features work.

**Done when:** New PC can attach to existing deployment without full Setup.

**Changelog:** _(empty)_

---

## Phase 5 — UI polish

**Status:** TODO  

**Do**

- Novice-first copy, hover explanations (VM vs Minecraft), disabled-state clarity, error toasts, consistent layout.
- **Mini-terminal** visual styling for the top-bar status panel (structure already from Phase 1).
- Still Always Free–first messaging; no paid-mode UI.
- Do **not** add bell / settings / overflow chrome here unless operator pulls v1 chrome forward — default remains **v1**.

**Test**

- Operator (or a friend) can use Manager without reading lab docs.

**Done when:** Operator accepts polish bar for MVP.

**Changelog:** _(empty)_

---

## Phase 6 — Guide + greenfield E2E

### Step 6.1 — Happy-path guide

**Status:** TODO  

**Do**

- Short guide: OCI account / PAYG as needed, API key + Auth Token under `%USERPROFILE%\.oci\`, Always Free confirmation, installer → Setup → play.
- Optional deep appendix (SSH, door, Object Storage).

**Test**

- Someone other than the author can follow the short path (or operator role-plays cleanly).

**Done when:** Guide checked into repo (e.g. `docs/Guide.md`).

**Changelog:** _(empty)_

---

### Step 6.2 — Full greenfield E2E proof

**Status:** TODO  

**Do**

- Destroy/recreate or second-compartment proof of Setup → manage → friend wake → idle stop → backup.
- Record results / gaps in this plan.

**Test**

- All MVP success criteria checkboxes exercised on the fresh stack.

**Done when:** Operator signs off E2E.

**Changelog:** _(empty)_

---

## Phase 7 — Packaging, updates, closed beta

### Step 7.1 — Windows installer

**Status:** TODO  

**Do**

- Single installer → one app (Setup integrated).
- Code-signing strategy documented (even if deferred purchase); SmartScreen notes.

**Test**

- Clean Windows VM/user install; app runs; config locations documented.

**Done when:** Installer artifact builds reproducibly.

**Changelog:** _(empty)_

---

### Step 7.2 — GitHub Releases update check

**Status:** TODO  

**Do**

- On launch: check latest GitHub Release; prompt + **release notes**.
- Respect MVP “Updates” row in PRODUCT-IDEAS.

**Test**

- Mock or real release; prompt appears; dismiss works offline.

**Done when:** Update check ships in app.

**Changelog:** _(empty)_

---

### Step 7.3 — Closed beta with friends

**Status:** TODO  

**Do**

- Dogfood with real friends on reserved IP; fix blockers only.
- Keep $0 discipline.

**Test**

- Multi-friend play session; wake from cold; idle stop.

**Done when:** No MVP-blocking bugs open (or deferred with operator OK).

**Changelog:** _(empty)_

---

## Phase 8 — MVP exit review

**Status:** TODO  

**Do**

- Tick all [MVP success criteria](#mvp-goal-from-product-ideas).
- Confirm out-of-MVP items were not accidentally scoped in.
- Point forward to Development-Steps / PRODUCT-IDEAS **v1** (not started here).
- Update `README.md` + lab `VM-Software.md` to “MVP complete” (or “MVP closed beta”).

**Done when:** Operator declares MVP achieved for product purposes.

**Changelog:** _(empty)_

---

## Reference map

| Need | Where |
|------|--------|
| MVP / v1 intent | Lab `PRODUCT-IDEAS.md` |
| Minecraft server install/upgrade mechanism (Vanilla/Paper/Fabric/NeoForge/Forge/Quilt/modpacks) | [`Minecraft-Server-Deployment-Blueprint.md`](Minecraft-Server-Deployment-Blueprint.md) |
| Suggested order narrative | Lab `docs/Development-Steps.md` |
| What’s live on VMs today | Lab `docs/VM-Software.md` |
| OCI layout | Lab `Infrastructure-Information.md` |
| Door behavior | Lab `docs/Door-VM-Control-Plane.md` |
| Deploy pitfalls (SSH/sudo) | Lab `docs/Agent-Deploy-Pitfalls.md` |
| Local Avalonia config | `docs/Local-Config.md` |
| OCI API usage (429, waiters, thrift) | `docs/OCI-API-Usage.md` (lab twin: `OCI-mc-server-manager/docs/OCI-API-Usage.md`) |
| Secrets / OCIDs (gitignored) | `data/config.local.json`; lab private markdown |

---

## Out of scope reminders (do not implement under this plan)

- Public `0.0.0.0/0` Minecraft  
- Paid / spend mode  
- Modded / Optimized Vanilla (Paper) Setup + pack analyze/install (v1) — when that lands, still **no in-app Modrinth/CurseForge/FTB browse/search/catalog** (blueprint §2.4; file-import only)  
- Per-day budget calendar tool  
- Full PTY console  
- macOS / Linux Manager  
- Replacing door reconcile polling with events-as-primary  
- Silent OCI tenancy probing on every startup  
- Notification center / settings gear / overflow menu (v1)  
- Oversized-world SSH **Download World Save** path + bell UX (v1; on-box flag OK in Phase 2)  
- Players tab / Kick·Op·Ban (after v1)  
- Migrating the operator’s live Forge lab off `/home/ubuntu/minecraft/server` as a prerequisite for Step 2.3 (greenfield `/opt/mcmgr/` only; Connect-existing reads actual `world_path`)  

---

## Plan changelog

| Date | Note |
|------|------|
| 2026-08-11 | Step 2.4 DONE: door wake `--force` OS pull; idle-agent §10.2 sync; oversized-world flag set/skip; OS-ISSUE-6 deferred with operator OK. Phase 2 complete. NEXT = Step 3.1. |
| 2026-08-11 | Step 2.3 DONE: product `onbox/mcmgr/` Vanilla bootstrap (generic driver + piston-meta module) + offline dry-run/fixtures. NEXT = Step 2.4. |
| 2026-08-11 | Aligned Phase 2/3 steps with [`Minecraft-Server-Deployment-Blueprint.md`](Minecraft-Server-Deployment-Blueprint.md) §30: Step 2.3 Do list covers §3–§9/§16 (layout, RCON, properties, generic unit, final manifest); Step 2.4 adds idle-agent §10.2 sync; Steps 3.1–3.3 encode §13/§14 OpenTofu-vs-game split + bootstrap resume; out-of-scope notes no in-app pack catalog. NEXT remains Step 2.3. |
| 2026-08-11 | Step 1.8 / Phase 1 DONE: manage MVP exit gate; Avalonia usable for daily ops. NEXT = Step 2.1. |
| 2026-08-11 | Step 1.7 DONE: Danger Zone idle apply (OS budget + SSH timer); OS-ISSUE-7 safety copy. NEXT = Step 1.8. |
| 2026-08-11 | Step 1.6 DONE: Server Management backups list/download/upload + SSH replace; soft-cap UI. NEXT = Step 1.7. |
| 2026-08-11 | Step 1.5 DONE: Usage/budget Object Storage pull+publish, dashboard, 2 min tab poll, top-bar Today. NEXT = Step 1.6. |
| 2026-08-11 | Step 1.4 DONE: door-aware power + Always Free–aware polling; Step 1.1 OCI-API backfill. NEXT = Step 1.5. |
| 2026-08-11 | Document OCI API usage (throttling/waiters/Always Free request thrift) in `OCI-API-Usage.md`; Steps 1.1/1.4 notes. NEXT remains Step 1.4. |
| 2026-08-11 | PRODUCT-IDEAS: MVP Vanilla version picker via Mojang piston-meta; v1 Setup game types (Paper Fill v3 / modpacks). Updated Steps 2.3, 3.2, 3.3 + out-of-MVP notes. NEXT remains Step 1.4. |
| 2026-08-11 | Align with PRODUCT-IDEAS notes: button-gated auto-detect + naming/tag/`meta/infra.json` (Phase 4 / 2 / 3.1); top-bar copy IP + mini-terminal polish split; Download World Save stays Step 1.6; oversized SSH + chrome deferred to v1; Players after v1. NEXT remains Step 1.4. |
| 2026-08-10 | Step 1.3 DONE: whitelist CRUD + Security List sync (SL-only; door :8080; safer VCN preserve). NEXT = Step 1.4. |
| 2026-08-10 | Step 1.2 DONE: manage shell (top bar + four empty tabs). NEXT = Step 1.3. |
| 2026-08-10 | Step 1.1 DONE: OCI session + Core facades; VM1 lifecycle probe in status. NEXT = Step 1.2. |
| 2026-08-10 | Initial MVP implementation plan created from PRODUCT-IDEAS + Development-Steps + current dual-repo foundation. Phase 0 DONE; NEXT = Step 1.1. |
