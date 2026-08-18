# MVP Implementation Plan

**Status:** **Archive for Phases 0–7 (DONE).** Agents implementing product features must follow [`V1-Implementation-Plan.md`](V1-Implementation-Plan.md) (**NEXT = Step 1.1**). Packaging (this file’s Phase 8 / Step 8.1) is **deferred** until V1 Phase 9.  
**Product intent authority:** lab [`PRODUCT-IDEAS.md`](../../OCI-mc-server-manager/PRODUCT-IDEAS.md) (MVP section). When this plan and PRODUCT-IDEAS disagree on *what* MVP means, **PRODUCT-IDEAS wins** — update this file.  
**Suggested narrative order:** lab [`docs/Development-Steps.md`](../../OCI-mc-server-manager/docs/Development-Steps.md).  
**Live infra / on-box SoT:** lab repo (`Infrastructure-Information.md`, `door_vm/`, `vm_agent/`, `docs/VM-Software.md`).  
**Code SoT for Manager:** **this repo** (`OCI-mc-server`).

**Cost rule:** keep OCI spend at **$0** (Always Free–eligible) unless the operator explicitly accepts paid changes.

**OCI API:** follow [`OCI-API-Usage.md`](OCI-API-Usage.md) and Oracle [Using the API](https://docs.oracle.com/en-us/iaas/Content/API/Concepts/usingapi.htm) — **429** exponential backoff (≤60s), lifecycle waiters (≤30s between polls, ~20 min), list pagination, modest Object Storage chatter (~50k requests/month). Prefer Get-by-OCID from local config over chatty List discovery.

---

## How agents must use this file

**Living execution is [`V1-Implementation-Plan.md`](V1-Implementation-Plan.md).** This file stays as the MVP as-built record (Phases 0–7). Frozen step bodies below that say “do not implement v1” are **historical** — superseded by the V1 plan.

1. **Read [`V1-Implementation-Plan.md`](V1-Implementation-Plan.md) first** (protocol + dashboard + the single NEXT step). Do not implement from this MVP file unless the operator is explicitly fixing an MVP regression.  
2. Step **7.2** is **DONE**; [`E2E-7.2-Findings.md`](E2E-7.2-Findings.md) is historical. Do **not** start this file’s Step **8.1** (installer) — that work is V1 plan **Phase 9**.  
3. **Never create git commits** (operator commits in Visual Studio). You may suggest a commit message.
4. Implement v1 features **only** as the V1 plan NEXT step. Do **not** implement **after v1** / later PRODUCT-IDEAS items.
5. Do **not** put Manager UI in the lab repo. Phase B (Blazor Hybrid) is **DONE**; do not re-open Avalonia.
6. **Fix the product path, not only the test VM.** If troubleshooting the blank-tenancy / Setup deploy shows a bug caused by OpenTofu, IAM matching rules, cloud-init, SSH bootstrap, `onbox/mcmgr/`, `door_vm/`, or `vm_agent/` install: file it in lab `docs/Issues.md` **and** change the automated-deploy code in the same effort so the next greenfield run does not repeat it. Patching only the live test instance is not done. Example: SETUP-ISSUE-2 (door DG tag match + compartment-only `manage public-ips`) had to land in product HCL, not just a Console tweak.
7. **`ubuntu` often cannot read/write the files you need.** Recurring pitfall (lab `docs/Agent-Deploy-Pitfalls.md`): `/etc/mccontrol/oci.env` is mode 600 root; `/opt/mcmgr/`, `/etc/mcmgr/`, `/etc/mc-manager/`, systemd units, and many scripts are root-owned. Before operating on a path as `ubuntu`, check permissions; use `sudo` or fix ownership/mode. Do not burn a session rediscovering `Permission denied`.
8. **UI sketches in PRODUCT-IDEAS are not locked; operator UI notes override.** See [Phase 6](#phase-6--ui-polish), [Phase B](#phase-b--blazor-hybrid-ui), and lab `PRODUCT-IDEAS.md` → Manager UI. Do **not** build a mini-terminal / console status panel. Novice Status is Running/Stopped; technical VM/door status is on Advanced. For UI-design work, use or offer the `find-skills` skill unless the operator already asked; also look at panels such as Pterodactyl for feature reference. **NuGet is allowed** on the Manager UI project (`McManager.Hybrid`). Search for themes, icons, controls, fonts, or other UI needs. Do not add Avalonia / Semi / Material.Icons.Avalonia packages. Keep OCI SDK on Core; prefer OSS licenses; ask before paid/commercial packages; confirm large IA redesigns with the operator.

### Agent stop protocol

**Use [`V1-Implementation-Plan.md`](V1-Implementation-Plan.md)** for new work. Historical MVP protocol (Phases 0–7):

Between **large steps** (Phase / Step headings below), always stop for operator feedback.  
**Small sub-bullets** inside one large step may be completed together in one session if they are required to make that step testable.

If blocked (missing OCIDs, unclear UX, cost risk), stop and ask — do not guess in a way that opens `0.0.0.0/0` or accrues spend.

**Cloud apply is operator-only.** Agents must not run `tofu apply` / `tofu plan` / `tofu destroy` against any tenancy, must not `docker push` / `fn push` to OCIR, and must not SSH-bootstrap the live **lab** (Forge) VMs. Allowed checks: `dotnet build`, `tofu validate` in `infra/`, wizard Deploy only with `MCMANAGER_TOFU_DRY_RUN=1` (fake runner). A real apply creates a second Always Free A1 (Setup default **4/24**, or **2/12** if chosen). Same-tenancy as the lab competes for hours; a blank test tenancy does not.

**Historical — Step 7.2 findings (DONE):** that work used OCI CLI/API profile **`TESTING`** (not `DEFAULT`) and SSH to the **test** VMs. Agents still must not `tofu apply` / `tofu destroy` / OCIR push unless the operator explicitly authorizes it.

### Operator prompt (copy-paste for a new agent)

Use the prompt in [`V1-Implementation-Plan.md`](V1-Implementation-Plan.md). Historical MVP prompt (do not use):

```text
Read docs/V1-Implementation-Plan.md in OCI-mc-server. Implement only the step marked NEXT.
MVP Phases 0–7 are DONE. Packaging (old Step 8.1) is deferred until V1 Phase 9. Phase B (Blazor Hybrid UI) is DONE — do not re-open Avalonia.
You MAY use OCI CLI/API with profile TESTING (not DEFAULT) and SSH both test VMs with %USERPROFILE%\.ssh\mcmgr_ed25519_20260817_125552. Stay at $0. If you change a test VM or TESTING cloud resource, make the same change in the local deployment SoT.
If you need VM1 and it is STOPPED, START it, then disable idle. If VM1 is already RUNNING, confirm idle is off. When you finish, turn idle back on.
When done: update the V1 plan statuses, stop, tell me what you did, how to test, what’s next, and ask if I want to continue or adjust.
Do not commit. Do not start the following large step unless I say so.
Do not tofu apply / OCIR push unless I explicitly authorize it.
```

---

## MVP goal (from PRODUCT-IDEAS)

> Non-expert admin follows the guide, installs one app, deploys a **private Vanilla** stack, friends use the reserved IP with door wake, idle/budget stops protect free tier, worlds are backed up, admin manages whitelist and basic power from Manager.

**MVP success criteria**

- [x] Friend can wake and play on reserved play IP  
- [x] Empty / budget SoftStop works (no players **or** Minecraft not running, after idle timeout)  
- [x] Door refuses wake when daily budget exhausted (clear MOTD/kick) — live in lab `door_vm/`  
- [x] Admin can whitelist and repair SSH allow IP without Console  
- [x] In-game Minecraft `white-list` is **off**; OCI Security List is the allowlist  
- [x] Operator can recover a stuck reserved play IP / doorbell from Manager (Troubleshooting one-shots, Step 4.4)  
- [x] World backups under ~9.5 GB Object Storage policy  
- [x] Setup survives capacity wait and can resume (wizard + Guide)  
- [ ] Single Windows installer → one Manager app (Setup integrated) — **deferred to V1 plan Phase 9**  
- [ ] App can check GitHub Releases for updates + show release notes — **deferred to V1 plan Phase 9**  

**Explicitly out of MVP:** public game access, paid/spend mode, modded UI / Optimized Vanilla (Paper) / pack analyze, per-day budget sculpting, usage-API 48h reconcile Function, rich MOTD editor, interactive PTY console, event-driven door handback, macOS/Linux Manager, VPN / Distant Horizons engineering, silent OCI probing on startup, notification-center / settings / overflow chrome, oversized-world SSH download UX, Players tab, **$1 spend-brake lock UX** (Function OS flag + full-window warning + typed confirmation — v1), **Start progress checklist** (after v1). Guide/Setup **must** still disclose the possible ~$1–$2 residual if the $1 Function fires.

---

## Progress dashboard

| Phase | Focus | Status |
|-------|--------|--------|
| **0** | Operator infra + dual-repo foundation | **DONE** |
| **1** | Avalonia manage MVP (existing stack) | **DONE** |
| **2** | On-box / contract freeze for product | **DONE** |
| **3** | Setup wizard + OpenTofu greenfield | **DONE** |
| **4** | Stabilize test stack + operator repair | **DONE** |
| **5** | Connect-existing (auto-detect + meta) | **DONE** |
| **6** | UI polish (novice-ready) | **DONE** — Avalonia vehicle abandoned; goals transfer to Phase B |
| **B** | Blazor Hybrid UI (replace Avalonia) | **DONE** |
| **7** | Guide + greenfield E2E proof | **DONE** — 7.1 + 7.2 |
| **8** | Packaging, updates, closed beta | **DEFERRED** — moved to [`V1-Implementation-Plan.md`](V1-Implementation-Plan.md) Phase 9 |
| **9** | MVP exit review | **DEFERRED** — folded into V1 plan Step 9.5 (v1 exit) after v1 features |

**Current NEXT (product work):** [`V1-Implementation-Plan.md`](V1-Implementation-Plan.md) **Step 1.1**. This file has **no** active NEXT. Step **7.2** is **DONE**. Do **not** start this file’s Step **8.1**.

Phases **1–3 are frozen** (do not rewrite those step bodies). Historical step changelogs that said “NEXT = Phase 4” meant Connect-existing at the time; that work is now **Phase 5**. Phase **6** stays DONE (Avalonia polish shipped, then operator rejected the stack as the UI vehicle). Phase **B** stays DONE (Blazor Hybrid is the WinExe).

---

## Phase 0 — Foundation (DONE)

Operator Always Free stack + product repo bootstrap. Do not re-do unless something regressed.

| Item | Status | Notes |
|------|--------|-------|
| Dual-VM doorbell (reserved IP, door MOTD/wake, reconcile) | DONE | Lab live — see VM-Software |
| VM1 idle/budget SoftStop + ledger/lease + shape detect | DONE | `vm_agent/` empty + budget path live; SoftStop when Minecraft is **down** is Step **4.1** |
| Object Storage Phases 1–5 + world backup soft cap | DONE | Lab |
| $1 budget → Function SoftStop | DONE | Lab — SoftStops **VM1 and VM2**; copy in lab `functions/shutdown_vm/` |
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
  - Tabs: **Whitelist**, **Usage**, **Server Management** (backups / Download World Save), **Advanced / Danger Zone** (one tab in MVP; PRODUCT-IDEAS **v1** splits Advanced vs Danger Zone — do not split during MVP)
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
- **Out of this step (v1):** adaptive SSH download when oversized-world flag is set; bell notification for that flag. If the flag object already exists in the bucket, UI may show a simple status string, but full notification center is v1. **Wipe world** and Server Management **modding** inspect are also v1 (`PRODUCT-IDEAS.md`) — not this step.

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

**Status:** DONE  

**Read first:** [`Automated-Infrastructure-Deployment.md`](Automated-Infrastructure-Deployment.md) (locked IaC decisions) and [`Lab-Reference-Stack-Notes.md`](Lab-Reference-Stack-Notes.md) (sanitized lab dump: `mcmgr-…` names, 3 dynamic groups, skip NAT/private subnet, Events → Function). Do not apply or import the discovery pack.

**Do**

- Add `infra/` (or `tofu/`) OpenTofu HCL: VCN, subnet, Security List, VM1 A1 Flex, VM2 Micro, reserved public IP + secondaries, NSG/SL rules for private Minecraft/SSH/door admin, Object Storage bucket, IAM dynamic groups/policies (least privilege where practical), budget + **Events → Function** placeholders as needed (do not copy the lab’s unused ONS topic).
- **Product naming** per lab PRODUCT-IDEAS / [`Lab-Reference-Stack-Notes.md`](Lab-Reference-Stack-Notes.md): compartment `mcmgr`; `mcmgr-vcn`, `mcmgr-subnet-public`, `mcmgr-sl`, `mcmgr-vm1`, `mcmgr-door`, `mcmgr-play-ip`, bucket `mcmgr-shared-data`, DGs `mcmgr-dg-instances` / `mcmgr-dg-door` / `mcmgr-dg-fn`, etc. Freeform tag on compartment: `mcmgr-domain=mc-server-compartment`; instances `mcmgr-role=vm1|door`.
- Do not clone ad-hoc Console names from the first manual deploy.
- Outputs → values Manager needs for local config + `meta/infra.json`.
- **Game-layer boundary** (blueprint §13.1): OpenTofu / cloud-init may create the `mcmgr` user/group, empty `/opt/mcmgr/` + `/etc/mcmgr/` / `/var/lib/mcmgr/` tree, baseline OS packages, and Adoptium apt **repo registration** — but must **not** install Minecraft, Java majors chosen in the wizard, loaders, or mod packs (version-sensitive work stays in SSH bootstrap from Step 2.3 / 3.3).

**Test**

- `tofu validate` / plan against empty test compartment (operator must approve any apply). Prefer plan-only until Step 3.3.

**Done when:** Validatable OpenTofu root module exists with documented variables.

**Changelog:** 2026-08-12 — Linked [`Automated-Infrastructure-Deployment.md`](Automated-Infrastructure-Deployment.md) + [`Lab-Reference-Stack-Notes.md`](Lab-Reference-Stack-Notes.md); no HCL written yet.  
2026-08-12 — Added [`infra/`](../infra/) OpenTofu root (`oracle/oci` 8.27.0); `tofu validate` passed; plan skipped (no `terraform.tfvars`); **no apply**. NEXT = Step 3.2.

---

### Step 3.2 — Setup wizard UX (no apply yet)

**Status:** DONE  

**Do**

- Integrated Setup (not a second exe): collect alert email, region/compartment strategy, **Minecraft version picker** (from Mojang manifest; default `latest.release`; releases-only unless Advanced), EULA accept, Vanilla confirm, Always Free docs confirmation link, **$1 last-resort brake + possible ~$1–$2 residual-charge disclosure**, capacity-handling consent, SSH key creation/import.
- See [`Minecraft-Server-Deployment-Blueprint.md`](Minecraft-Server-Deployment-Blueprint.md) §13 for which parts of this belong to the wizard UI vs the SSH bootstrap module vs OpenTofu — the wizard fetches version metadata **read-only for display**; the actual bootstrap module re-resolves it on-box at execution time. Manager must **not** re-implement authoritative jar URL/hash resolution in C# (§13.3).
- Persist wizard state for resume-later (including selected version id).
- Auth Token: collect when OCIR/Function push needed; prefer Windows Credential Manager for storage (not long-term plaintext); gitignored local OK only as temporary operator aid.
- Show plan summary before apply.
- **Out of MVP:** Vanilla vs Modded / Optimized Vanilla (Paper) / modpack upload-analyze (v1 — PRODUCT-IDEAS Setup game types). Durable rule (not just “later”): **no in-app mod/modpack catalog** (blueprint §2.4) — pack input is file picker / drag-and-drop of an already-exported archive only.

**Test**

- Walk wizard offline/mocked; state resumes after app restart; version list loads from manifest (or fixture).

**Done when:** Wizard UI complete without requiring live apply.

**Changelog:** 2026-08-12 — `SetupWizardWindow` (9 steps), first-run chooser when `config.local.json` is missing, Advanced **Deploy / repair infrastructure**. Resume in `data/setup-wizard.local.json`. Mojang catalog live + fixture fallback. Auth Token → Credential Manager `McManager/ocir`. Deploy disabled; **no** `tofu` / **no** `terraform.tfvars` write. NEXT = Step 3.3.

---

### Step 3.3 — Apply + bootstrap + capacity wait

**Status:** DONE  

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
- Agents: `dotnet build`; optional `MCMANAGER_TOFU_DRY_RUN=1` wizard Deploy. **No** live `tofu apply`.

**Done when:** Greenfield deploy from app reaches manageable stack.

**Changelog:** 2026-08-14 — Blank-tenancy operator test: OS seed 404=create, door OS env, guest netplan, Vanilla whitelist, tenancy `mcmgr-door-ip`, door DG by instance OCID, `wait_forge` `set -u`, `ip_to_vm1 --force`. 2026-08-13 — Wired Setup Deploy to LocalAppData OpenTofu (`%LOCALAPPDATA%\McManager\tofu`), SSH bootstrap (`door_vm` install.sh + `onbox/mcmgr` + `vm_agent`), Object Storage seed + `config.local.json` write, best-effort OCIR push. Fake runner / `MCMANAGER_TOFU_DRY_RUN`. Agents did not apply. **TEMPORARY:** VM1 OpenTofu defaults **2/12** for the blank-tenancy test — revert to **4/24** after. NEXT = Phase 4.

---

## Phase 4 — Stabilize test stack + operator repair

**Goal:** The blank-tenancy Setup stack actually meets MVP success criteria (wake → play → idle/budget SoftStop) before Connect-existing or polish. Pull a **subset** of PRODUCT-IDEAS v1 Door/IP Repair into MVP as one-shot Manager repair actions. Keep the operator SSH runbook current.

**Why this sits before Connect-existing:** Phase 3.3 is code-complete, but 2026-08-15 dogfood still shows idle SoftStop not firing as expected, door wake **DEGRADED** (`wait_forge.sh` timed out), and **`minecraft.service` crash-looping on `CHDIR` / Permission denied**. Those block “friend can wake and play” and “empty SoftStop works.”

Phases 0–3 stay **DONE**; do not rewrite them. Bootstrap/HCL fixes that belong in Setup still land **here** (and in product code), not by editing frozen Phase 3 step text.

---

### Step 4.1 — Idle SoftStop when Minecraft is down + wait_forge

**Status:** DONE  
**Depends on:** Phase 3.3 operator test stack (blank PAYG tenancy)

**Do**

- **Product decision (operator 2026-08-15):** the idle agent **must SoftStop VM1 when Minecraft is not running**, using the **same idle timeout** as the empty-server path (default 15 minutes). Today’s `idle_watch.py` early-return (`Minecraft inactive; nothing to do.`) is **wrong** for product intent — not merely a log-line curiosity.
- **Implement in lab SoT and on the test VM — both required:**
  1. Change lab [`vm_agent/idle_watch.py`](../../OCI-mc-server-manager/vm_agent/idle_watch.py) (tracked SoT). Door Phase 4 deploy **does not** push VM1.
  2. **Redeploy the idle agent** to the blank-tenancy test VM1 (`/opt/mc-manager` + timer) so live behavior matches git. Updating only the PC checkout is **not done.** Updating only the test VM without `vm_agent/` is **not done.**
- **Semantics to implement:**
  - SoftStop after `idle_timeout_minutes` if **either** (a) Minecraft is `active` and RCON `list` shows no players, **or** (b) the `minecraft_unit` is **not** `active` (stopped, failed, crash-loop / CHDIR storm).
  - **Do not** SoftStop on the first oneshot tick. A normal wake/boot can take minutes in `activating` before `active` — start the same idle clock, then **clear it** when the unit becomes `active` **and** players are present (existing empty-server logic). If it never becomes a healthy `active` server, the timeout must still fire (this is how a CHDIR loop stops burning Ampere hours).
  - When the game is already down: skip RCON (it will fail); skip `systemctl stop` if already inactive; still attempt **cold world backup** if `world_path` exists, then ledger/lease close + OCI SoftStop (same stop path as empty-server, minus flush/`list`).
  - Budget **soft cap** must still SoftStop if VM1 is up even when Minecraft is down (hours are burning). Skip in-game `say` warnings when RCON cannot connect.
  - Do **not** change OS-ISSUE-7 (boot still force-enables idle).
- **CHDIR / wait_forge (same session context, do not chmod here):**
  - Operator turned VM1 on; idle timeout 15 minutes; VM did **not** SoftStop because of the old early-return. Timer **was** firing.
  - Manager Start did **not** bring Minecraft up. `journalctl -u minecraft` is a restart storm (`status=200/CHDIR`). That is SETUP-ISSUE-4 — product permission fix is Step **4.2**, not a one-off `chmod` in 4.1.
  - Door `wait_forge` TCP FAIL is expected until Minecraft listens. After 4.2, re-check VCN SL / firewalld only if TCP still fails while `minecraft` is `active`.
- Confirm CHDIR with lab [`docs/Operator-Troubleshooting.md`](../../OCI-mc-server-manager/docs/Operator-Troubleshooting.md) (`systemctl cat`, `namei -l`). File leftover findings in [`docs/Issues.md`](../../OCI-mc-server-manager/docs/Issues.md).
- Do **not** implement Manager repair buttons (Step 4.4). Do **not** start Connect-existing.

**Test**

- On the **test VM1** after redeploy: with Minecraft **stopped** (or still crash-looping), wait `idle_timeout_minutes` → VM1 SoftStops. Logs must **not** be only `Minecraft inactive; nothing to do.`
- After 4.2 (game can start): empty running server still SoftStops on the same timeout; a successful Start that reaches `active` within the timeout must **not** SoftStop mid-boot just because the unit was `activating` for a few minutes.
- Lab `vm_agent/` diff matches what is on `/opt/mc-manager`.

**Done when:** SoT + test VM1 both run the new idle rule; IDLE-ISSUE-1 marked fixed or “fixed in agent, pending 4.2 for play path”; wait_forge/CHDIR remaining work is Step 4.2.

**Changelog:** 2026-08-15 — Operator: idle agent must SoftStop when Minecraft is **not running** (same timeout). Implement in `vm_agent/` **and** redeploy test VM1. CHDIR still Step 4.2.  
2026-08-15 — **DONE.** `idle_watch.py` idle clock when unit not `active`; skip RCON/stop if already down; cold backup + SoftStop. Redeployed test VM1. Proof (timeout=2 min, Minecraft stopped): first tick started clock; ~2 min later `Stopped instance after: Minecraft not running for 2 minutes.`; VM1 STOPPED. SETUP-ISSUE-4 confirmed (`ubuntu:ubuntu` `0750` on `/opt/mcmgr`); DOOR-ISSUE-5 expected until 4.2. NEXT = Step 4.2.

---

### Step 4.2 — Comprehensive on-box permission model (bootstrap)

**Status:** DONE  
**Depends on:** 4.1 (CHDIR / `mcmgr` user confirmed)

**Do**

- **Problem class, not a one-path chmod.** Recurring theme: SSH/`ubuntu`, systemd `User=mcmgr`, root-owned `/etc` files, and Setup-created directories do not agree. SETUP-ISSUE-4 (`WorkingDirectory` CHDIR) is one symptom. A fix that only `chmod`s `/opt/mcmgr/server` on the test VM **is not done.**
- Audit and **encode in product bootstrap** (`onbox/mcmgr/` `layout.sh` / unit template / driver, and Setup SSH that creates dirs) a single ownership/mode contract that systemd, the game, idle-agent (root), and Manager-over-SSH (`ubuntu` + `sudo`) can all use. Start from blueprint **§5** (`root:mcmgr` `0750` on `/opt/mcmgr`, `mcmgr:mcmgr` `0750` on `server/`, etc.) and verify it is **actually applied** after every stage (including resume/Re-Deploy — a skipped `layout` stage plus a later `mkdir` as root `0700` is a likely bug).
- Cover at least:
  - Every parent of systemd `WorkingDirectory=` must be traversable by `User=mcmgr` (`x` bit + group). `status=200/CHDIR` means this failed.
  - `server.jar`, `server.properties`, `eula.txt`, `world/`, `ReadWritePaths=`, backups-work, `/opt/mcmgr/bin/` (ExecStop helper).
  - Java: `mcmgr` must be able to exec the Temurin JRE (normally world-executable under `/usr/lib/jvm/`; do not copy the JRE into a 0700 tree).
  - `/etc/mcmgr/` (manifest `0640` `root:mcmgr`; `rcon.secret` stays `0600` root — ExecStop/idle agent run as root).
  - `/etc/mc-manager/` idle-agent config (root).
  - systemd sandbox: `ProtectSystem=strict` / `ProtectHome=true` / `ReadWritePaths` must not contradict the tree. If sandboxing is the CHDIR cause, fix the unit **and** the tree together.
  - **Restart storm:** unit was at restart **30** with `Restart=on-failure` / `RestartSec=10`. Confirm `StartLimitBurst` in the **installed** unit actually stops the loop; if the live unit diverges from `minecraft.service.in`, that is part of this bug.
  - Door VM: same class of “created as ubuntu/root, later run as another user” — do not expand into a door rewrite, but do not ignore an identical pattern if bootstrap creates `/opt/mccontrol` wrong.
- **Do not** “fix” by running Minecraft as `ubuntu` or `chmod 0777`. Keep `User=mcmgr`.
- Add a bootstrap **verify** step (or `namei -l` / `systemd-analyze` check) that fails the install if `mcmgr` cannot `chdir` + exec Java, so this cannot ship green again.
- Update lab [`docs/Operator-Troubleshooting.md`](../../OCI-mc-server-manager/docs/Operator-Troubleshooting.md) + [`docs/Agent-Deploy-Pitfalls.md`](../../OCI-mc-server-manager/docs/Agent-Deploy-Pitfalls.md) with the contract (not only the CHDIR symptom).
- Fix **product code** (`onbox/mcmgr/`, Setup bootstrap if it mkdirs outside layout.sh). A Console/`chmod` on the test VM is only a temporary operator workaround.

**Test**

- After Re-Deploy or a documented permission-repair script from bootstrap: `systemctl start minecraft` stays `active` (no CHDIR loop); `ss` shows `:25565`; door `diagnose_wait_forge.sh` TCP OK.
- `namei -l` on WorkingDirectory is traversable by `mcmgr` on a **fresh** layout, not only the patched test box.
- Dry-run/fixtures still pass.

**Done when:** Next greenfield/Re-Deploy starts Minecraft as `mcmgr` without a manual permission chase; sibling paths in §5 are in the same contract.

**Changelog:** 2026-08-15 — **DONE.** Encoded blueprint §5 in `onbox/mcmgr/common/layout.sh` (`layout_ensure_accounts` / `layout_apply` / fail-closed `layout_verify`) + `repair-permissions.sh`. Driver never skips apply/verify; resume preserves `stages_completed`. Unit `ExecStop=+` + `RestartPreventExitStatus=200`. Cloud-init per-path owners (no `chown -R`). Setup whitelist seed + Manager world-replace re-apply the contract. Test VM1: repair script (not ad-hoc chmod) → `systemctl start minecraft` **active**, no CHDIR, `:25565` listen, door `diagnose_wait_forge.sh` TCP **OK**. Idle timer left **disabled** (start triggered `mc-boot-ledger` OS-ISSUE-7 force-enable; disabled again). NEXT = Step 4.3.

---

### Step 4.3 — Bootstrap: disable Minecraft in-game whitelist

**Status:** DONE  
**Depends on:** 4.2 preferred first (game should actually start) but can proceed in parallel.

**Do**

- Product access control for MVP is **OCI Security List `/32`s only**. Vanilla `white-list` / `enforce-whitelist` must be **off** on automated Setup so friends are not also gated by `whitelist.json`.
- Operator already turned it off **manually** on the current test deploy; that is not enough — change:
  - Product `onbox/mcmgr/common/server_properties.sh` managed defaults
  - [`Minecraft-Server-Deployment-Blueprint.md`](Minecraft-Server-Deployment-Blueprint.md) §7.3 (intent already updated 2026-08-15; keep code in sync)
  - Setup seed of `whitelist.json` from admin Minecraft username (SETUP-ISSUE-1 workaround) — stop requiring it for join; username may remain in wizard for later MOTD/ops
- Re-Deploy / bootstrap resume must write `white-list=false` and `enforce-whitelist=false` (read-modify-write managed keys only). Never set `online-mode=false`.
- Lab Forge stack: do not rip the operator’s live lab unless asked; Connect-existing reads whatever the stack actually has.

**Test**

- Fresh or Re-Deploy bootstrap: `server.properties` has whitelist off; an allowlisted IP can join without a Minecraft `whitelist.json` entry.

**Done when:** Next greenfield/Re-Deploy does not require a manual whitelist toggle.

**Changelog:** 2026-08-15 — **DONE.** `server_properties.sh` writes `white-list=false` / `enforce-whitelist=false` (never `online-mode=false`). Driver always re-applies managed keys on resume. `repair-server-properties.sh` + Setup `EnsureGuestRuntime` Re-Deploy the same writer. Admin Minecraft username optional (not a join gate). Dry-run asserts the three keys. Test VM1: product script flipped leftover `white-list=true` → `false`; Minecraft left stopped; idle left disabled. SETUP-ISSUE-3 fixed. NEXT = Step 4.4.

---

### Step 4.4 — Manager troubleshooting / one-shot repair actions

**Status:** DONE  
**Depends on:** 4.1–4.2 (know which repairs actually help; game must be able to start)

**Do**

- Add a **Troubleshooting** section on the existing combined **Advanced / Danger Zone** tab, **or** a dedicated **Troubleshooting** tab if that stays clearly “repair, not Danger Zone.” Do **not** split Advanced vs Danger Zone (that remains **v1**). PRODUCT-IDEAS v1 “Door/IP Repair” is **partially pulled forward** into MVP; full v1 recovery-after-$1-lock UX stays v1.
- Each action is a **one-shot**, confirm-gated, with a result log in the UI (SSH/OCI output the operator can paste). Prefer wrapping existing `door_vm/` scripts over new door Python.
- **Minimum — park reserved play IP (operator-requested):**
  - Preferred behavior: if VM1 is **RUNNING**, assign the reserved public IP to **VM1’s secondary**; if VM1 is **not** RUNNING, assign it to the **door secondary**. Start the door first if it is stopped and the IP should live there.
  - Alternate (also acceptable if cleaner): ensure VM1 is off, door is on, then assign the reserved IP to the door (hard reset to idle doorbell). Document which variant shipped.
  - Covers stuck/wrong-VM IP and FN-ISSUE-1 leftover after the `$1` Function SoftStops both VMs.
- **Also implement (or explicitly defer with operator OK) one-shots for other known failure modes** — design from lab `docs/Issues.md` + Step 4.1 findings. Candidate set:

  | Problem | Repair idea |
  |---------|-------------|
  | Door stuck STARTING/DEGRADED after game is actually up | Wrap `reset_door_state.sh` / `unstick_after_forge_ready.sh` |
  | `wait_forge` timeout / private `:25565` closed | Read-only **Diagnose wait_forge** (run `diagnose_wait_forge.sh`, show output); optional “ensure VCN→25565 + host listen” if 4.1 proved it |
  | Idle timer disabled / “Minecraft inactive” confusion | Show unit + timer status; **Force-enable idle timer** (warn: boot already force-enables — OS-ISSUE-7) |
  | Door stopped (`$1` Function) | **Start door VM** then park IP on door |
  | Stale door OS budget cache | Door `/api/os-refresh` / `pull_os_budget.sh --force` |
  | Open ledger after failed SoftStop | Door heal **only when VM1 STOPPED** (Phase 5 heal rules) |
  | Guest missing secondary play IP | Re-apply netplan `99-mcmgr-play.yaml` (SETUP-ISSUE-1) |
  | Sticky mccontrol / Minecraft | Restart `mccontrol` / `minecraft` (Restart already exists — don’t duplicate without a reason) |
  | `minecraft.service` `200/CHDIR` / restart storm | Show journal + `namei`; optional **repair game tree permissions** that runs the **same** bootstrap layout contract as Step 4.2 — not an ad-hoc chmod |
  | Guest ACPI SoftStop hang (OS-ISSUE-5) | **Not** a silent button — copy pointing at Console reset |

- Keep Always Free: no extra paid APIs; waiter/429 habits; do not open `0.0.0.0/0`.
- Update lab [`docs/Operator-Troubleshooting.md`](../../OCI-mc-server-manager/docs/Operator-Troubleshooting.md) so each new button maps to the SSH/OCI commands it runs.

**Test**

- Operator can recover a stuck reserved IP without Console.
- At least one other high-value repair (door reset or diagnose) works on the test stack.
- Confirm dialogs prevent accidental VM stop / IP move.

**Done when:** Operator can recover doorbell IP + the Step 4.1 failure modes without asking an agent for ad-hoc SSH.

**Changelog:** 2026-08-15 — **DONE.** Dedicated **Troubleshooting** tab (not Advanced/Danger split). **Preferred park-IP shipped:** VM1 `RUNNING` → door `ip_to_vm1.sh`; else start door if needed + `ip_to_vm2.sh` (instance principal; already-on-target is success). Also: diagnose/reset/unstick, `POST /api/os-refresh` + SSH `--force` fallback, heal only when VM1 STOPPED, idle status + force-enable (no Minecraft start), netplan re-apply, CHDIR diagnosis, `repair-permissions.sh` (4.2 contract). OS-ISSUE-5 Console copy only; top-bar Restart not duplicated. Operator-Troubleshooting button map. NEXT = Phase **5**. Do not start Phase 5 in this session.

---

## Phase 5 — Connect existing (MVP-light)

**Status:** DONE  

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

**Changelog:** 2026-08-15 — **DONE.** Button-gated Auto-detect (first-run + Advanced). Sequential `~/.oci` profiles; compartments named `mcmgr` **or** tagged `mcmgr-domain=mc-server-compartment`; bucket `mcmgr-shared-data` and/or any bucket with `meta/infra.json`; hydrate `config.local.json` from meta (SSH key / RCON / OCI profile stay local); overwrite confirm; chooser on multiple matches; soft `infra_schema` warn+confirm (no meta mutate). “I already have a stack” still skips detect. No launch-time OCI probe. NEXT = Phase **6**. Do not start Phase 6 in this session.

---

## Phase 6 — UI polish

**Status:** DONE  

**Do**

- Novice-first copy, hover explanations via **?** icons (not jargon paragraphs), disabled-state clarity, error toasts, consistent layout.
- **Status card is not a terminal / console.** Novice fields: **Status** (`Running` / `Stopped` — Minecraft joinable), **Play IP**, **Players**. Door / VM1 / doorbell technical status belongs on **Advanced**.
- Fill unused top-bar space with **pinned usage cards** (hours, not a gimmick chrome). HTML mockup is a density/hierarchy reference, not a feature-subset spec.
- Start/Stop/Restart: grey only until first status load, or while a power action is in flight. Tab Object Storage polls must **not** disable those buttons.
- Still Always Free–first messaging; no paid-mode UI.
- Do **not** add a full bell / settings / overflow notification center unless operator pulls v1 chrome forward. A compact title-bar placeholder icon is OK.
- **UI is not locked.** PRODUCT-IDEAS tab/layout notes are starting ideas; **operator notes override**. Use UI/software-design skills (`find-skills` unless the operator already directed it) and look at similar products (e.g. **Pterodactyl panel**) for what a server Manager should surface. Ask the operator before a large visual redesign (removing/renaming/reordering tabs, sidebar, splitting Advanced vs Danger Zone). **Search and add NuGet packages** as needed (Avalonia themes, icon packs, extra controls, fonts, etc.) — do not stay on Fluent or Semi just because they are already referenced. Keep OCI SDK on Core; prefer OSS licenses; ask before paid/commercial packages.
- **Setup Deploy log:** auto-scroll to the bottom on new text **unless** the user scrolled up; resume auto-scroll when they scroll back to the bottom.
- **Setup deploy progress (if it can be implemented cleanly):** progress bar + **percent** from known stages (`apply_stage` / bootstrap-state). Add **timestamps** on deploy-log lines so later timed test deploys can feed an ETA. **Minutes remaining** is **not** required in the first polish pass — operator will time a few deploys (or hand timestamped logs to an agent) before a useful estimate.
- **Setup lock after Deploy starts:** **Deploy** is not clickable once apply/bootstrap has started **or** after it has finished. Disable Back / previous wizard pages and any other control that could mutate the in-flight or completed deploy. Resume-later / Re-Deploy remains a **separate** explicit action (existing `apply_stage` behavior), not a second click of Deploy on the same finished page.

**Test**

- Operator (or a friend) can use Manager without reading lab docs.
- During a dry-run or real deploy: log stick-to-bottom works; Deploy/Back stay disabled after start; progress percent moves if implemented.

**Done when:** Operator accepts polish bar for MVP.

**Changelog:** 2026-08-17 — Operator Hybrid layout polish: tab-body scrollbar in the right window gutter (thin overlay-style thumb); tab cards stay chrome-width; `MinWidth` remeasures WebView2 client. NEXT remains **Step 7.2**. Do not start 7.2 unless asked.  
2026-08-16 — Operator Hybrid polish (post-Phase-B): twilight-granite + copper theme (light warm-gray rejected); equal-height pinned stats; filled Start/Stop/Restart; Running=green / Stopped=red; status-field `?` removed; DEBUG probes on Advanced; default/`MinWidth` hugs chrome, extra width centers the shell. NEXT remains **Step 7.2**. Do not start 7.2 unless asked.  
2026-08-15 — **DONE.** Semi.Avalonia Dark + Material icons; mini-terminal top bar; Start/Stop/Restart hover + disabled reasons + toasts; novice tab/Setup copy. Setup deploy log timestamps + stick-to-bottom unless scrolled up; stage percent bar; Deploy/Back lock after start (Re-Deploy = new wizard from Advanced). Dry-run persist still leaves `apply_stage` unchanged. NEXT = Phase **7**. Do not start Phase 7 in this session.  
2026-08-15 — Operator rejected mini-terminal look. Redesign (still Phase 6 polish, not Phase 7): Running/Stopped status card, pinned usage hours, Advanced technical VM/door status, custom title bar (Avalonia 12 `WindowDecorations=Full` + extended client area), power buttons no longer flash-disable on tab polls, Whitelist list-row overlap fix. NEXT remains Phase **7**. Do not start Phase 7 in this session.  
2026-08-15 — Operator chose **Blazor Hybrid** before Phase 7. Phase 6 stays **DONE**; Avalonia polish is **abandoned as the UI vehicle**. Novice Status / pinned hours / Setup log-lock **goals transfer** to [Phase B](#phase-b--blazor-hybrid-ui). Do **not** start Phase 7.

---

## Phase B — Blazor Hybrid UI

**Status:** DONE  
**Living checklist (SoT for B0–B13):** [`Blazor-UI-Migration-Plan.md`](Blazor-UI-Migration-Plan.md)

**Why this sits before Phase 7:** Avalonia visual iteration was slow and buggy (custom title bar, clipping, Semi hover/disabled, pinned-stat layout). The operator chose a **WPF + BlazorWebView** WinExe (HTML/CSS/Razor, existing `McManager.Core`) **before** Guide + greenfield E2E. Phase 6 Avalonia polish is abandoned as the UI vehicle; its product goals (Running/Stopped, pinned hours, Setup log behavior, feature parity) transfer here.

**Do not implement Phase B from this section’s bullets.** Implement **only the step marked NEXT** in [`Blazor-UI-Migration-Plan.md`](Blazor-UI-Migration-Plan.md). Stop for operator feedback between B0–B13. Agents never commit. **B0–B13 are all DONE.**

**Host (locked):** WPF + `Microsoft.AspNetCore.Components.WebView.Wpf`, `net8.0-windows`, native OS chrome, Evergreen WebView2. Not Blazor Server, not a browser, not MAUI, not Photino. Scaffold with `dotnet new wpf` (no VS wizard unless that template is missing). Avalonia `McManager.App` was **removed at B13**. The only WinExe is `McManager.Hybrid` (not renamed).

**Current NEXT inside Phase B:** none. **B0–B13 are DONE.** One WinExe: `McManager.Hybrid`. Manage + first-run + Setup wizard are live (dry-run Deploy; no live `tofu apply`).

**Out of this phase:** Phase 7 Guide/E2E, installer packaging, v1 PRODUCT-IDEAS, live `tofu apply`.

**Changelog:** 2026-08-15 — **B13 DONE.** Phase B complete. Removed Avalonia `McManager.App` from slnx and deleted `src/McManager.App/`. One WinExe: `McManager.Hybrid` (not renamed). README / Local-Config / AGENTS / rules updated. `dotnet build` clean. **NEXT = Phase 7** (TODO). Do not start Phase 7 in this session.  
2026-08-17 — Operator Hybrid layout polish (not a new B-step / not Step 7.2): tab-body scrollbar in the right window gutter so overflowing tabs stay chrome-width; thin overlay-style thumb; `MinWidth` remeasures WebView2 client for even left/right pads. **NEXT remains Step 7.2.** Do not start 7.2 unless asked.  
2026-08-16 — Operator Hybrid polish (not a new B-step): twilight-granite + copper theme; equal-height pins; filled power buttons; Running/Stopped colors; status `?` removed; DEBUG probes on Advanced; window default/`MinWidth` hugs chrome, extra width centers the shell. **NEXT remains Step 7.2.** Do not start 7.2 unless asked.  
2026-08-15 — **B12 DONE.** Hybrid Setup wizard (9 steps, resume JSON, Credential Manager token, deploy log timestamps/stick-to-bottom/percent, Deploy/Back lock, capacity wait). First-run/Advanced use the real wizard. Dry-run only. Avalonia App still in slnx. **NEXT = B13** (SEQUENTIAL). Do not start B13 in this session. Do not start Phase 7.  
2026-08-15 — **B11 DONE.** Hybrid first-run + Connect-existing (button-gated Auto-detect; chooser; overwrite confirm; preserve SSH/RCON). Shared `ConnectExistingFlow` with Advanced. Avalonia App still in slnx. **NEXT = B12** (SEQUENTIAL). Do not start B12–B13 in this session. Do not start Phase 7.  
2026-08-15 — **B10 DONE.** Hybrid Advanced / Danger Zone: technical VM/door status, break-glass Compute, idle OS-ISSUE-7, infra meta Refresh/Publish, Auto-detect, Setup stub (no tofu). Avalonia App still in slnx. **NEXT = B11** (SEQUENTIAL). Do not start B11–B13 in this session. Do not start Phase 7.  
2026-08-15 — parent pasted B6–B9 DI + MainLayout; tabs visible; **NEXT = B10**. Do not start B10 in this session. Do not start Phase 7.  
2026-08-15 — **B9 DONE.** Hybrid Troubleshooting tab: all Step 4.4 one-shots with Avalonia confirm gating; result log + `IClipboard` copy; OS-ISSUE-5 Console copy only; own `IsBusy` only. DI/layout snippets left for parent paste (did not edit `App.xaml.cs` / MainLayout). Avalonia App still in slnx. Do not start B10–B13 in this session. Do not start Phase 7.  
2026-08-15 — **B7 DONE.** Hybrid Usage: dashboard + all Avalonia budget fields; remaining-in-month on this tab; dirty-gated Save/Publish with `IUiDialogs`; ~2 min poll while selected. DI/layout snippets left for parent paste (did not edit `App.xaml.cs` / MainLayout). Avalonia App still in slnx. Do not start B10–B13 or Phase 7 in this session.
2026-08-15 — **B8 DONE.** Hybrid Server Management: four info cards, Object Storage list/download/upload (native `IFilePicker`), SSH replace when VM1 RUNNING, soft-cap messaging. No Wipe/Modding/Delete. DI/layout snippets left for parent paste (did not edit `App.xaml.cs` / MainLayout). Avalonia App still in slnx. Do not start B10–B13 or Phase 7 in this session.
2026-08-15 — **B6 DONE.** Hybrid Whitelist: friends CRUD + Security List apply; Add-IP popup; hover row actions; dirty-gated Save; Detect/Update admin IP. DI/layout snippets left for parent paste (did not edit `App.xaml.cs` / MainLayout). Avalonia App still in slnx. **NEXT = B7 / B8 / B9** (PARALLEL-OK remaining; B10 sequential). Do not start B7–B13 in this session. Do not start Phase 7.  
2026-08-15 — **B5 DONE.** Hybrid manage chrome live: novice Running/Stopped, door-aware Start/Stop, SSH Restart, pinned `UsageMath` leftover bank, toast, copy IP, door/OCI poll via `IUiClock`; Door/Compute/OciSession DI. Avalonia App still in slnx. **NEXT = B6** (B6–B9 PARALLEL-OK among themselves; B10 sequential). Do not start B6–B9 in this session. Do not start Phase 7.  
2026-08-15 — **B4 DONE.** Hybrid loads `LocalConfigStore` / `ManagerLocalConfig` and shows reserved play IP; stub first-run when no manage config; no OCI on launch. Avalonia App still in slnx. **NEXT = B5.** Do not start B5 in this session. Do not start Phase 7.  
2026-08-15 — **B3 DONE.** Hybrid `Ui/` host services (dialogs, pickers, clipboard, clock/dispatcher); WPF STA impls; Razor modal host; DEBUG probes. Avalonia App still in slnx. **NEXT = B4.** Do not start B4 in this session. Do not start Phase 7.  
2026-08-15 — **B2 DONE.** Light warm-gray Hybrid layout shell (mockup chrome, placeholders, self-hosted fonts/icons, native WPF caption). Avalonia App still in slnx. **NEXT = B3.** Do not start B3 in this session. Do not start Phase 7.  
2026-08-15 — **B1 DONE.** `McManager.Hybrid` WPF + BlazorWebView WinExe references Core; missing-WebView2 MessageBox; Avalonia App still in slnx. **NEXT = B2.** Do not start B2 in this session. Do not start Phase 7.  
2026-08-15 — **B0 DONE.** Docs/agent rules retargeted to Blazor Hybrid (WPF + WebView2); historical Wails→Avalonia kept. **NEXT = B1.** Do not scaffold Hybrid in the B0 session. Do not start Phase 7.  
2026-08-15 — Inserted before Phase 7. Living checklist created. **NEXT = B0.** Do not scaffold in the plan-creation session. Do not start Phase 7.

---

## Phase 7 — Guide + greenfield E2E

Phase B cutover is **DONE**. Step **7.1** is **DONE**. Step **7.2** is **DONE**. Destroy UI for teardown is **in Manager**. Packaging (this file’s Phase 8) is **deferred** to [`V1-Implementation-Plan.md`](V1-Implementation-Plan.md) Phase 9. Product **NEXT = V1 Step 1.1**.

### Step 7.1 — Happy-path guide

**Status:** DONE  

**Do**

- Short guide: OCI account / PAYG as needed, API key + Auth Token under `%USERPROFILE%\.oci\`, Always Free confirmation, installer → Setup → play.
- Disclose plainly: the stack is built to stay at **$0**, but if the **$1 last-resort budget** ever fires, Function latency can leave a **~$1–$2** charge for that month, then no further charges while the brake holds. (Full-window Manager lock UX is **v1**; this honesty is **MVP** guide/Setup copy.) See lab `PRODUCT-IDEAS.md` Always Free mode + $1 spend-brake lock.
- Optional deep appendix (SSH, door, Object Storage).

**Test**

- Someone other than the author can follow the short path (or operator role-plays cleanly).

**Done when:** Guide checked into repo (e.g. `docs/Guide.md`).

**Changelog:** 2026-08-15 — **DONE.** Added [`docs/Guide.md`](Guide.md): short Windows happy path (PAYG as needed, `%USERPROFILE%\.oci` API key + Auth Token, Always Free docs gate, **$1 brake + possible ~$1–$2 residual**, installer-or-`dotnet run` → Setup → whitelist → play). Appendix: SSH vs API key, doorbell/play IP, Object Storage, Connect-existing, Troubleshooting. Until Phase 8, run `McManager.Hybrid` from source. **NEXT = Step 7.2** (SEQUENTIAL). Do not start 7.2 in this session.

---

### Step 7.2 — Full greenfield E2E proof

**Status:** DONE  

**Child checklist (implement from here, one section per agent):** [`E2E-7.2-Findings.md`](E2E-7.2-Findings.md)

**Do**

- Destroy/recreate or second-compartment proof of Setup → manage → friend wake → idle stop → backup.
- Record results / gaps in this plan **and** the findings child file.
- Work remaining after the first E2E is **only** the findings sections (Setup UX, config reload, power buttons, door/play-IP, idle handback, flaky Start, docs). Do not treat “operator deployed once” as DONE.

**Teardown (shipped 2026-08-17, not the E2E itself):** Manager **Advanced / Danger Zone → Delete infrastructure**. Typed lowercase `confirm`; popup stays open with log + percent until `tofu destroy` returns (OpenTofu waits for OCI). Deletes OpenTofu-managed product resources only (LocalAppData state), then `config.local.json` + wizard resume + tofu workspace. Does **not** delete the Oracle tenancy, `friends.local.json`, `~/.oci`, or SSH keys. Destroy also wipes the product bucket (`ledger/usage.json` + world backups); a new Setup seeds a zero ledger while Oracle’s monthly Always Free hours already include the old VMs. Agents must not click Delete / run `tofu destroy`. See [`Guide.md`](Guide.md) → Tear down and redeploy, [`Automated-Infrastructure-Deployment.md`](Automated-Infrastructure-Deployment.md) §12.4.

**Test**

- All MVP success criteria checkboxes exercised on the fresh stack.
- Before that: Delete infrastructure on the existing test tenancy stack → window reports completion → Console shows product VMs/VCN/bucket gone → Setup can deploy again.

**Done when:** Operator signs off E2E.

**Changelog:** 2026-08-18 — **DONE.** Second greenfield E2E (operator play path) + Stop timeout fix (**DOOR-ISSUE-9**, async idle-empty) + host firewall persist (**SETUP-ISSUE-7**, mask `netfilter-persistent` so firewalld keeps 25565 after SoftStop reboot). **NEXT = Step 8.1**. Do not start Phase 8 unless asked.  
2026-08-17 — Findings **F9 DONE** (Start-after-idle: wait for VM1 RUNNING before wait_forge; DEGRADED recover/retry). See [`E2E-7.2-Findings.md`](E2E-7.2-Findings.md). Do not mark 7.2 DONE. Do not start Phase 8.  
2026-08-17 — Findings **F8 DONE** (idle/equivalent SoftStop parks reserved play IP on a listening door; `stop_vm1` already-down no-op). See [`E2E-7.2-Findings.md`](E2E-7.2-Findings.md). **NEXT = F9.** Do not mark 7.2 DONE. Do not start Phase 8.  
2026-08-17 — Findings **F7 DONE** (Setup parks reserved play IP on VM1 when the game is already up; wake START-on-RUNNING no-op; mcdoor I/O timeouts). See [`E2E-7.2-Findings.md`](E2E-7.2-Findings.md). **NEXT = F8.** Do not mark 7.2 DONE. Do not start Phase 8.  
2026-08-17 — Findings **F6 DONE** (Guide + destroy/contract docs: destroy+redeploy resets the usage ledger mid-month; Oracle Always Free hours do not). See [`E2E-7.2-Findings.md`](E2E-7.2-Findings.md). **NEXT = F7.** Do not mark 7.2 DONE. Do not start Phase 8.  
2026-08-17 — Findings **F5 DONE** (Start disabled when VM1 is already on; Status/buttons show Starting… / Stopping… / Restarting… in flight). See [`E2E-7.2-Findings.md`](E2E-7.2-Findings.md). **NEXT = F6.** Do not mark 7.2 DONE. Do not start Phase 8.  
2026-08-17 — Findings **F4 DONE** (Setup Close / Connect-existing reload `config.local.json` and rebuild manage clients without restart). See [`E2E-7.2-Findings.md`](E2E-7.2-Findings.md). **NEXT = F5.** Do not mark 7.2 DONE. Do not start Phase 8.  
2026-08-17 — Findings **F3 DONE** (Setup VM1 2/12 vs 4/24 picker; Minecraft username removed; HCL defaults 4/24). See [`E2E-7.2-Findings.md`](E2E-7.2-Findings.md). **NEXT = F4.** Do not mark 7.2 DONE. Do not start Phase 8.  
2026-08-17 — Findings **F2 DONE** (time-weighted Setup deploy % + remaining-time range from the timed E2E log). See [`E2E-7.2-Findings.md`](E2E-7.2-Findings.md). **NEXT = F3.** Do not mark 7.2 DONE. Do not start Phase 8.  
2026-08-17 — Findings **F1 DONE** (Setup deploy elapsed, duration copy, plan/log spacing, slim scrollbar). See [`E2E-7.2-Findings.md`](E2E-7.2-Findings.md). **NEXT = F2.** Do not mark 7.2 DONE. Do not start Phase 8.  
2026-08-17 — Operator E2E: Deploy finished after SETUP-ISSUE-5 resume. Gaps split into [`E2E-7.2-Findings.md`](E2E-7.2-Findings.md) (**NEXT = F1**). Agents implement findings sections, not “mark 7.2 DONE.” Findings agents may SSH/OCI on the **test** stack (`TESTING` profile). Do not start Phase 8.  
2026-08-17 — SETUP-ISSUE-5: Setup cloud-init wait was a false WAIT (`ubuntu` cannot `test -f` `/etc/mcmgr/cloud-init-done` under `0750 root:mcmgr`). Product waiter now `sudo -n test -f`. **Do not wait longer or reboot this stack.** Resume from Advanced → Deploy / repair after rebuilding Manager (`apply_stage=tofu_applied` skips tofu apply). **7.2 E2E is still the operator test.** NEXT remains 7.2. Do not start Phase 8.  
2026-08-17 — Prerequisite only: Danger Zone **Delete infrastructure** (typed `confirm`, log + percent, `tofu destroy`, local stack files). **7.2 E2E is still the operator test.** NEXT remains 7.2. Do not start Phase 8.

---

## Phase 8 — Packaging, updates, closed beta (**DEFERRED**)

**Moved to [`V1-Implementation-Plan.md`](V1-Implementation-Plan.md) Phase 9.** Do not implement from this heading. Step bodies below are kept for history.

### Step 8.1 — Windows installer

**Status:** TODO  

**Do**

- Single installer → one app (Setup integrated).
- Code-signing strategy documented (even if deferred purchase); SmartScreen notes.

**Test**

- Clean Windows VM/user install; app runs; config locations documented.

**Done when:** Installer artifact builds reproducibly.

**Changelog:** _(empty)_

---

### Step 8.2 — GitHub Releases update check

**Status:** TODO  

**Do**

- On launch: check latest GitHub Release; prompt + **release notes**.
- Respect MVP “Updates” row in PRODUCT-IDEAS.

**Test**

- Mock or real release; prompt appears; dismiss works offline.

**Done when:** Update check ships in app.

**Changelog:** _(empty)_

---

### Step 8.3 — Closed beta with friends

**Status:** TODO  

**Do**

- Dogfood with real friends on reserved IP; fix blockers only.
- Keep $0 discipline.

**Test**

- Multi-friend play session; wake from cold; idle stop.

**Done when:** No MVP-blocking bugs open (or deferred with operator OK).

**Changelog:** _(empty)_

---

## Phase 9 — MVP exit review (**DEFERRED**)

**Status:** DEFERRED — v1 exit is [`V1-Implementation-Plan.md`](V1-Implementation-Plan.md) Step 9.5. Installer/update success criteria stay unchecked here until that phase.  

**Do**

- Tick all [MVP success criteria](#mvp-goal-from-product-ideas).
- Confirm out-of-MVP items were not accidentally scoped in.
- Point forward to Development-Steps / PRODUCT-IDEAS **v1** (not started here).
- Update `README.md` + lab `VM-Software.md` to “MVP complete” (or “MVP closed beta”).
- **Operator (not agents):** after declaring MVP complete, run the clean-room acceptance test in lab `PRODUCT-IDEAS.md` (new account + installer + full Setup + $1 budget stress). Prefer a local VM / spare PC. This test may incur the documented ~$1–$2 residual — do not run it on the long-lived lab tenancy unless that spend is explicitly accepted.

**Done when:** Operator declares MVP achieved for product purposes.

**Changelog:** _(empty)_

---

## Reference map

| Need | Where |
|------|--------|
| **Living v1 execution checklist** | [`V1-Implementation-Plan.md`](V1-Implementation-Plan.md) |
| Happy-path user guide | [`Guide.md`](Guide.md) |
| Step 7.2 E2E findings (split agent work) | [`E2E-7.2-Findings.md`](E2E-7.2-Findings.md) |
| MVP / v1 intent | Lab `PRODUCT-IDEAS.md` |
| Minecraft server install/upgrade mechanism (Vanilla/Paper/Fabric/NeoForge/Forge/Quilt/modpacks) | [`Minecraft-Server-Deployment-Blueprint.md`](Minecraft-Server-Deployment-Blueprint.md) |
| Automated cloud infra (OpenTofu, Resource Manager reference capture, VM images, config hosting) | [`Automated-Infrastructure-Deployment.md`](Automated-Infrastructure-Deployment.md) |
| Suggested order narrative | Lab `docs/Development-Steps.md` |
| What’s live on VMs today | Lab `docs/VM-Software.md` |
| OCI layout | Lab `Infrastructure-Information.md` |
| Door behavior | Lab `docs/Door-VM-Control-Plane.md` |
| Operator SSH/OCI troubleshooting commands | Lab [`docs/Operator-Troubleshooting.md`](../../OCI-mc-server-manager/docs/Operator-Troubleshooting.md) |
| Known bugs / quirks | Lab `docs/Issues.md` |
| Deploy pitfalls (SSH/sudo; `ubuntu` permissions) | Lab `docs/Agent-Deploy-Pitfalls.md` |
| Local Manager config | `docs/Local-Config.md` |
| Blazor Hybrid UI migration (before Phase 7) | [`Blazor-UI-Migration-Plan.md`](Blazor-UI-Migration-Plan.md) |
| OCI API usage (429, waiters, thrift) | `docs/OCI-API-Usage.md` (lab twin: `OCI-mc-server-manager/docs/OCI-API-Usage.md`) |
| Secrets / OCIDs (gitignored) | `data/config.local.json`; lab private markdown |

---

## Out of scope reminders (do not implement under **this** MVP file)

v1 items below are **in** [`V1-Implementation-Plan.md`](V1-Implementation-Plan.md) — implement them only as that plan’s NEXT step, not from this archive.

- Public `0.0.0.0/0` Minecraft **except** V1 plan Step 3.2 (confirm-gated)  
- Paid / spend mode **except** V1 plan Phase 8  
- Modded / Optimized Vanilla (Paper) Setup + pack analyze/install — V1 plan Phase 4; still **no in-app catalog** (blueprint §2.4)  
- Per-day budget calendar tool (**after v1**)  
- Full PTY console (**after v1**; V1 Step 7.5 is RCON+logs only)  
- macOS / Linux Manager  
- Replacing door reconcile polling with events-as-primary  
- Silent OCI tenancy probing on every startup  
- Notification center / settings / overflow — V1 plan Phase 6  
- Oversized-world SSH download + bell — V1 plan Step 6.3  
- Players tab / Kick·Op·Ban (**after v1**)  
- **$1 spend-brake lock UX** — V1 plan Phase 2  
- Start-from-Manager **progress checklist** (**after v1**)  
- Migrating the operator’s live Forge lab off `/home/ubuntu/minecraft/server` as a prerequisite for Step 2.3 (greenfield `/opt/mcmgr/` only; Connect-existing reads actual `world_path`)  

---

## Plan changelog

| 2026-08-17 | Operator: **v1 features before packaging.** This file is the MVP archive (0–7 DONE). Living checklist: [`V1-Implementation-Plan.md`](V1-Implementation-Plan.md) **NEXT = Step 1.1**. Phase 8–9 deferred to V1 Phase 9. |
| 2026-08-18 | **Step 7.2 DONE.** Second greenfield E2E signed off after **DOOR-ISSUE-9** (async Stop) and **SETUP-ISSUE-7** (firewalld vs netfilter-persistent). **NEXT = Step 8.1**. Do not start Phase 8 unless asked. |
| 2026-08-17 | Findings **F8 DONE** (idle SoftStop parks reserved IP on a listening door). [`E2E-7.2-Findings.md`](E2E-7.2-Findings.md) **NEXT = F9.** Do not mark 7.2 DONE. Do not start Phase 8. |
| 2026-08-17 | Findings **F7 DONE** (Setup parks reserved play IP on VM1 when the game is already up; wake START-on-RUNNING no-op; mcdoor I/O timeouts). [`E2E-7.2-Findings.md`](E2E-7.2-Findings.md) **NEXT = F8.** Do not mark 7.2 DONE. Do not start Phase 8. |
| 2026-08-17 | Findings **F6 DONE** (destroy+redeploy resets usage ledger; Oracle monthly hours do not). [`E2E-7.2-Findings.md`](E2E-7.2-Findings.md) **NEXT = F7.** Do not mark 7.2 DONE. Do not start Phase 8. |
| 2026-08-17 | Findings **F5 DONE** (Start disabled when VM1 is already on; Starting… / Stopping… / Restarting… in flight). [`E2E-7.2-Findings.md`](E2E-7.2-Findings.md) **NEXT = F6.** Do not mark 7.2 DONE. Do not start Phase 8. |
| 2026-08-17 | Findings **F4 DONE** (Setup Close reloads manage clients without restart). [`E2E-7.2-Findings.md`](E2E-7.2-Findings.md) **NEXT = F5.** Do not mark 7.2 DONE. Do not start Phase 8. |
| 2026-08-17 | Findings **F3 DONE** (Setup VM1 2/12 vs 4/24 picker; username removed; HCL defaults 4/24). [`E2E-7.2-Findings.md`](E2E-7.2-Findings.md) **NEXT = F4.** Do not mark 7.2 DONE. Do not start Phase 8. |
| 2026-08-17 | Findings **F2 DONE** (time-weighted Setup deploy % + remaining-time range). [`E2E-7.2-Findings.md`](E2E-7.2-Findings.md) **NEXT = F3.** Do not mark 7.2 DONE. Do not start Phase 8. |
| 2026-08-17 | Findings **F1 DONE** (Setup deploy elapsed / duration copy / plan-log spacing / slim scrollbar). [`E2E-7.2-Findings.md`](E2E-7.2-Findings.md) **NEXT = F2.** Do not mark 7.2 DONE. Do not start Phase 8. |
| 2026-08-17 | Step **7.2** operator E2E ran; remaining work split in [`E2E-7.2-Findings.md`](E2E-7.2-Findings.md) (**NEXT = F1**). Do not mark 7.2 DONE. Do not start Phase 8. |
| 2026-08-17 | Step **7.2** in progress: SETUP-ISSUE-5 (cloud-init `Last: WAIT` = ubuntu vs `/etc/mcmgr` 0750). Waiter now `sudo -n test -f`. **Not DONE.** NEXT remains 7.2. Do not start Phase 8. |
| 2026-08-17 | Step **7.2 prerequisite:** Manager Danger Zone **Delete infrastructure** (typed `confirm`; log + percent until `tofu destroy` finishes; OpenTofu-managed resources only; then local config/wizard/tofu workspace). **7.2 E2E still operator-owned.** NEXT remains 7.2. Do not start Phase 8. |
| 2026-08-17 | Manage tab-body scrollbar in the right window gutter (thin overlay-style thumb); tab cards stay aligned to the chrome row; `MinWidth` remeasures WebView2 client. **NEXT remains Step 7.2.** |
| 2026-08-16 | Hybrid accent → cobalt; Usage tab hero metrics + grouped budget; “game computer” → “server” in UI help copy. |
| 2026-08-16 | Window default/`MinWidth` hugs the chrome row; extra width centers the fixed Hybrid shell (no stretch). |
| 2026-08-16 | Operator UI polish on `McManager.Hybrid` (not Step 7.2): twilight-granite + copper theme (light warm-gray rejected); equal-height pinned stats; filled power buttons; Running/Stopped colors; status `?` icons removed; DEBUG probes moved to Advanced. **NEXT remains Step 7.2.** Do not start 7.2 unless asked. |
| 2026-08-15 | **Step 7.1 DONE.** Happy-path guide: [`docs/Guide.md`](Guide.md) (PAYG / `~/.oci` API key + Auth Token, Always Free confirmation, **$1 brake + ~$1–$2 residual**, Setup → play; SSH/door/OS appendix). **NEXT = Step 7.2** (SEQUENTIAL). Do not start 7.2 in this session. |
| 2026-08-15 | **B13 DONE.** Phase B complete. Removed Avalonia `McManager.App` from slnx and deleted the project tree. One WinExe: `McManager.Hybrid` (not renamed). Docs/rules updated. `dotnet build` clean. **NEXT = Phase 7** (TODO). Do not start Phase 7 in this session. |
| 2026-08-15 | **B12 DONE.** Hybrid Setup wizard (9 steps, resume JSON, Credential Manager token, deploy log timestamps/stick-to-bottom/percent, Deploy/Back lock, capacity wait). First-run/Advanced use the real wizard. Dry-run only. Avalonia App still builds. **NEXT = B13** (SEQUENTIAL). Do not start B13 in this session. Do not start Phase 7. |
| 2026-08-15 | **B11 DONE.** Hybrid first-run + Connect-existing (button-gated Auto-detect; chooser; overwrite confirm; preserve SSH/RCON). Shared `ConnectExistingFlow` with Advanced. Avalonia App still builds. **NEXT = B12** (SEQUENTIAL). Do not start B12–B13 in this session. Do not start Phase 7. |
| 2026-08-15 | **B10 DONE.** Hybrid Advanced / Danger Zone: technical VM/door status, break-glass Compute, idle OS-ISSUE-7, infra meta Refresh/Publish, Auto-detect, Setup stub (no tofu). Avalonia App still builds. **NEXT = B11** (SEQUENTIAL). Do not start B11–B13 in this session. Do not start Phase 7. |
| 2026-08-15 | Parent pasted B6–B9 DI + MainLayout; tabs visible; **NEXT = B10**. Do not start B10 in this session. Do not start Phase 7. |
| 2026-08-15 | **B8 DONE.** Hybrid Server Management: four info cards, Object Storage list/download/upload (native `IFilePicker`), SSH replace when VM1 RUNNING, soft-cap messaging. No Wipe/Modding/Delete. DI/layout snippets for parent paste. Avalonia App still builds. Do not start B10–B13 or Phase 7 in this session. |
| 2026-08-15 | **B7 DONE.** Hybrid Usage dashboard/edit/publish (remaining-in-month on this tab; 2 min poll; dirty-gated Save). DI/layout snippets for parent paste. Avalonia App still builds. Do not start B10–B13 or Phase 7 in this session. |
| 2026-08-15 | **B9 DONE.** Hybrid Troubleshooting one-shots (dedicated tab; confirm-gated; result log + copy). DI/layout snippets for parent paste. Avalonia App still builds. Do not start B10–B13 in this session. Do not start Phase 7. |
| 2026-08-15 | **B6 DONE.** Hybrid Whitelist CRUD + Security List sync (Add-IP popup, hover actions, dirty-gated Save). DI/layout snippets for parent paste. Avalonia App still builds. **NEXT = B7 / B8 / B9** (PARALLEL-OK remaining; B10 sequential). Do not start B7–B13 in this session. Do not start Phase 7. |
| 2026-08-15 | **B5 DONE.** Hybrid manage chrome live (status, power, pins, poll, toast); Door/Compute/OciSession DI. Avalonia App still builds. **NEXT = B6** (B6–B9 PARALLEL-OK among themselves; B10 sequential). Do not start B6–B9 in this session. Do not start Phase 7. |
| 2026-08-15 | **B4 DONE.** Hybrid loads local config and shows reserved play IP; stub first-run when no manage config; no OCI on launch. Avalonia App still builds. **NEXT = B5.** Do not start B5 in this session. Do not start Phase 7. |
| 2026-08-15 | **B3 DONE.** Hybrid `Ui/` host services (dialogs, pickers, clipboard, clock/dispatcher); WPF STA impls; Razor modal host; DEBUG probes. Avalonia App still builds. **NEXT = B4.** Do not start B4 in this session. Do not start Phase 7. |
| 2026-08-15 | **B2 DONE.** Light warm-gray Hybrid layout shell (mockup chrome, placeholders, self-hosted fonts/icons). Avalonia App still builds. **NEXT = B3.** Do not start B3 in this session. Do not start Phase 7. |
| 2026-08-15 | **B1 DONE.** `McManager.Hybrid` WPF + BlazorWebView host references Core; WebView2-missing MessageBox; Avalonia App still builds. **NEXT = B2.** Do not start B2 in this session. Do not start Phase 7. |
| 2026-08-15 | **B0 DONE.** Agent rules + PRODUCT-IDEAS retargeted: Manager UI vehicle is Blazor Hybrid (WPF + WebView2). Historical Wails→Avalonia kept. **NEXT = B1.** Do not scaffold Hybrid. Do not start Phase 7. |
| 2026-08-15 | Inserted **Phase B** (Blazor Hybrid UI: WPF + WebView2) **before** Phase 7. Avalonia polish abandoned as the UI vehicle; goals transfer to [`Blazor-UI-Migration-Plan.md`](Blazor-UI-Migration-Plan.md). **NEXT = B0.** Do not start Phase 7. Do not scaffold in the plan-creation session. |
| 2026-08-15 | Phase **6** redesign (operator rejected mini-terminal): Running/Stopped novice status; pinned usage hours; Advanced technical VM/door status; custom title bar; no power-button flash on tab polls. NEXT remains Phase **7**. Do not start Phase 7 in this session. |
| 2026-08-15 | Phase **6 DONE:** novice-ready UI polish (Semi Dark, mini-terminal, Setup log/progress/lock). NEXT = Phase **7** (Guide + greenfield E2E). Do not start Phase 7 in this session. |
| 2026-08-15 | Phase **5 DONE:** Connect-existing auto-detect (button-gated; meta hydrate; chooser; local-only SSH/RCON). NEXT = Phase **6** (UI polish). Do not start Phase 6 in this session. |
| 2026-08-15 | Step **4.3 DONE:** bootstrap/Re-Deploy write `white-list=false`; username optional; test VM1 product repair flipped leftover `true`. NEXT = Step **4.4**. |
| 2026-08-15 | UI work: agents may search for and add NuGet packages (themes, icons, controls, etc.) — not restricted to Fluent / already-referenced libraries. |
| 2026-08-15 | Step **4.2 DONE:** §5 permission contract in `onbox/mcmgr` layout+verify; test VM1 Minecraft `active` without CHDIR; door TCP OK. NEXT = Step **4.3**. |
| 2026-08-15 | Step **4.1 DONE:** idle SoftStop when Minecraft is not running (`vm_agent/` + test VM1 proof). SETUP-ISSUE-4 confirmed (`ubuntu:ubuntu` 0750). NEXT = Step **4.2**. |
| 2026-08-15 | SETUP-ISSUE-4: `minecraft.service` `200/CHDIR` Permission denied (game never starts). Inserted **Step 4.2** comprehensive on-box permission model; whitelist → 4.3; Manager repairs → 4.4. NEXT remains 4.1. |
| 2026-08-15 | Inserted **Phase 4** (stabilize test stack: idle/`wait_forge` investigation, in-game whitelist off, Manager one-shot repairs). Former Phases 4–8 → **5–9**. NEXT = Step 4.1. Phases 1–3 unchanged. Operator runbook: lab `docs/Operator-Troubleshooting.md`. |
| 2026-08-14 | Step 3.3 blank-tenancy test lessons (IAM tenancy IP policy, door DG instance.id, OS seed, netplan, whitelist). NEXT remains Phase 4. |
| 2026-08-13 | TEMPORARY: VM1 OpenTofu defaults 2/12 for blank-tenancy 3.3 test (revert `infra/variables.tf` to 4/24 after). |
| 2026-08-13 | PRODUCT-IDEAS only: v1 will split Advanced vs Danger Zone; MVP Step 1.2 tab list unchanged (one combined tab). |
| 2026-08-12 | Step 3.2 DONE: Setup wizard UX (resume JSON, Mojang picker, Credential Manager token, static plan summary). No apply / no tfvars write. NEXT = Step 3.3. |
| 2026-08-12 | Step 3.1 DONE: product `infra/` OpenTofu skeleton (`mcmgr-…` names, cloud-init baseline, 3 DGs); `tofu validate` OK; no apply. NEXT = Step 3.2. |
| 2026-08-12 | Lab `$1` Function confirmed SoftStops VM1 **and** VM2; source copy in lab `functions/shutdown_vm/`. |
| 2026-08-12 | Budget wiring: Events → Function is live; unused ONS topic is leftover. NEXT remains Step 3.1. |
| 2026-08-12 | Pre-3.1: sanitized lab RM dump; [`Lab-Reference-Stack-Notes.md`](Lab-Reference-Stack-Notes.md) (naming, 3 DGs). NEXT remains Step 3.1. |
| 2026-08-12 | Pre-3.1: added [`Automated-Infrastructure-Deployment.md`](Automated-Infrastructure-Deployment.md) (OpenTofu-on-PC, RM discovery as reference only). NEXT remains Step 3.1. |
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
