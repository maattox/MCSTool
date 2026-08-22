# V1 Implementation Plan

**Status:** Living checklist for agents and the operator.  
**Product intent:** **Operator will** is the source of truth. [`PRODUCT-IDEAS.md`](PRODUCT-IDEAS.md) is the living vision/roadmap (v1 table), **not infallible**. When **this plan** and PRODUCT-IDEAS disagree on *what* v1 means: do **not** silently rewrite this file to match PRODUCT-IDEAS. Either **stop and ask** the operator which document to follow (then update the other), **or follow this plan** (operator-requested execution) and **note** in the step changelog that PRODUCT-IDEAS disagrees and may drift. Newer operator-requested docs often match current will more closely.  
**MVP archive:** [`archive/MVP-Implementation-Plan.md`](archive/MVP-Implementation-Plan.md) — Phases **0–7 DONE**. Packaging (old Phase 8 / Step 8.1) is **deferred** to [Phase 9](#phase-9--packaging-updates-launch) of **this** file.  
**Infra / on-box:** [`Infrastructure-Information.md`](Infrastructure-Information.md), [`VM-Software.md`](VM-Software.md). On-box SoT is **this repo** (`door_vm/`, `vm_agent/`, `functions/shutdown_vm/`, `functions/reconcile_usage/`, `onbox/mcmgr/`). Doc map: [`README.md`](README.md).

**Cost rule:** keep OCI spend at **$0** (Always Free–eligible) unless the operator explicitly accepts paid changes. **Paid / spend mode is not v1** — skipped (operator 2026-08-18); the idea stays in PRODUCT-IDEAS as **later / far future**. Do not implement it under this plan.

**OCI API:** follow `[OCI-API-Usage.md](OCI-API-Usage.md)` — **429** exponential backoff (≤60s), lifecycle waiters (≤30s between polls, ~20 min), list pagination, modest Object Storage chatter (~50k requests/month). Prefer Get-by-OCID from local config over chatty List discovery.

**Execution order (operator 2026-08-17):** finish **v1 features** before Windows installer, GitHub Releases, public launch. **Pre-packaging QA is Phase 8.5** (catalog + passes + bug-fix plans) — do **not** start Phase 9 until 8.5 exits. **Phase 8.6** (CI-built ARM Function image; no Docker on the admin PC) must be **DONE** before Step **9.1** / any official release. Informal dogfood with friends (run from source) is allowed any time; it is not a plan step.

---



## How agents must use this file

1. **Do not read this whole file in one session.** Read [this protocol](#how-agents-must-use-this-file), the [Progress dashboard](#progress-dashboard), and **only the single NEXT step body**.
2. Implement **only** that NEXT step. Do not start “the rest of the phase.”
3. After finishing:
  - Mark the step **DONE**, set the following step to **NEXT**, add date + short notes on the step changelog line **and** the [Plan changelog](#plan-changelog).
  - **Stop.** Do not start the next large step unless the operator says to continue.
4. In the chat reply: what was done, how to test, what the next step will be, ask whether to continue / pause / adjust.
5. **Never create git commits** (operator commits in Visual Studio). You may suggest a commit message.
6. Do **not** implement **after v1** / **later** PRODUCT-IDEAS items (Players tab, start checklist, maintenance IP, multi-deploy, Quilt Setup entry, Purpur, PTY console, macOS/Linux Manager, **paid / spend mode**) unless the operator asks. **Pack replace** was after-v1 in PRODUCT-IDEAS; operator 2026-08-20 pulled **full re-setup** into v1 via [Step 8.4](#step-84--pass-2-follow-on-operator-notes) (light swap still parked). Blueprint **§24.3 Layer 3** quarantine is **v1** in [Step 8.8](#step-88--operator-notes-follow-on) (was parked in 4.13). An **in-app mod/modpack browser** is **rejected** (not after-v1) — users import a local pack file only; do not build it. **Public Minecraft / public-private toggle / blacklist** is **rejected** (not after-v1) — private allowlist only; do not rebuild it. If **this plan** disagrees with PRODUCT-IDEAS, follow this plan and note the drift (do not silently rewrite this file to match PRODUCT-IDEAS).
7. On-box source (`door_vm/`, `vm_agent/`, `functions/shutdown_vm/`) lives **in this repo**. Phase B (Blazor Hybrid) is **DONE**; do not re-open Avalonia.
8. **Fix the product path, not only the test VM.** If you change a test VM or a **TESTING** cloud resource, make the **same** change in the local deployment SoT in the same session (`onbox/mcmgr/`, `infra/`, `door_vm/`, `vm_agent/`, `functions/shutdown_vm/`, Manager/Setup code here). The next greenfield Setup must pick it up. Patching only the live test instance is not done.
9. `ubuntu` **Permission denied** — `sudo` or fix owner/mode (`[docs/Agent-Deploy-Pitfalls.md](Agent-Deploy-Pitfalls.md)`).
10. **UI sketches are not locked; operator notes override.** For UI-design work, use or offer `find-skills` unless already asked. **NuGet is allowed** on `McManager.Hybrid`. Do not add Avalonia packages. Keep OCI SDK on Core.
11. If this step changes a user-visible Setup or manage path, add a **short** paragraph to `[Guide.md](Guide.md)` in the same step. Do not rewrite the whole Guide.
12. **Test-stack OCI + SSH is allowed** for V1 work — see [Test stack access](#test-stack-access-oci--ssh). Stay at **$0**. Do **not** use the `DEFAULT` OCI profile or the live Forge lab. If you use VM1, start it when STOPPED, **disable idle** while you work, and **re-enable idle** when you finish. Product Function `fn build`/`push`/invoke on TESTING is allowed ([Product Functions](#product-functions-on-testing-blanket)); `tofu apply`/`destroy` is not unless the operator authorizes it.



### Context budget (256K window)

Each step is sized for **one** agent session (~256K tokens) after workspace rules.

- **Read first** lists are a **hard cap**, not a suggestion. Do not open the full `[Minecraft-Server-Deployment-Blueprint.md](Minecraft-Server-Deployment-Blueprint.md)`, the full MVP plan, or every Hybrid tab “for context.”
- Typical budget: this protocol + dashboard + **one** step; **one** PRODUCT-IDEAS heading; **named blueprint §§ only**; **≤ ~8** source files unless the step lists more.
- Do not implement adjacent steps “while you are here.”
- If the named files plus the required docs cannot fit, **stop and ask** to split the step — do not skim the blueprint.



### Agent stop protocol

Between **large steps** (Phase / Step headings below), always stop for operator feedback.  
**Small sub-bullets** inside one step may be completed together if they are required to make that step testable.

If blocked (missing OCIDs, unclear UX, cost risk, CurseForge ToS), stop and ask — do not guess in a way that opens `0.0.0.0/0` or accrues spend.

### Test stack access (OCI + SSH)

Agents **may** manage the **test** stack with OCI APIs and the OCI CLI, and **may SSH both test VMs**, when that is useful for the current NEXT step (inspect, reproduce, install, edit scripts/services, restart units, pull logs, exercise wake/idle, etc.).

**$0 is non-negotiable.** Do not take any action that would bill the tenancy: no paid shapes, extra block volumes, load balancers, extra reserved IPs, paid logging, leaving Always Free, or other spend. Always Free–eligible start/stop, Security List edits that stay **private** (allowlist CIDRs/`/32`s only — never Minecraft `0.0.0.0/0`), Object Storage of existing ledger/meta/backups, and SSH to the existing test VMs are in bounds. If an action might charge, **stop and ask**.


| Item                 | Value                                                                                                                                                                                                           |
| -------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| OCI config           | `%USERPROFILE%\.oci\config` (normal location)                                                                                                                                                                   |
| OCI profile          | `TESTING` **only** — test tenancy. **Never** `DEFAULT` (that is the operator’s other / live Forge lab tenancy).                                                                                                 |
| OCI CLI              | Always pass `--profile TESTING` (or the equivalent .NET `OciSession` profile from local config). Example: `oci compute instance get --profile TESTING --instance-id <from local config>`                        |
| SSH user             | `ubuntu`                                                                                                                                                                                                        |
| SSH private key      | `%USERPROFILE%\.ssh\mcmgr_ed25519_20260817_125552` — **same key for both** test VMs                                                                                                                             |
| Hosts / OCIDs / IPs  | Gitignored `data/config.local.json`. **Do not copy live OCIDs, IPs, Auth Tokens, or key material into tracked docs or chat dumps.**                                       |
| `ubuntu` permissions | Recurring `Permission denied` on `/etc/mcmgr`, `/etc/mccontrol/oci.env`, systemd units. Use `sudo` or fix mode/owner. Read [`Agent-Deploy-Pitfalls.md`](Agent-Deploy-Pitfalls.md) before SSH deploy edits. |


**Allowed**

- OCI SDK / REST / CLI against the **TESTING** tenancy for the current step (Compute get/start/stop, VNIC / reserved IP, Security List, Object Storage, IAM reads, etc.), with `[OCI-API-Usage.md](OCI-API-Usage.md)` 429 backoff and modest Object Storage chatter. Hosts/OCIDs for TESTING are in `%LOCALAPPDATA%\McManager\tofu\mcmgr\outputs.json`.
- SSH to **both** test VMs (VM1 and door). Anything on-box the step needs: test, install or edit scripts/services, `systemctl`, journals, firewalld, Minecraft/door paths, redeploy from the local SoT.
- Product Function **build / push / invoke** on TESTING — see [Product Functions on TESTING](#product-functions-on-testing-blanket).
- Mirror every test-VM or TESTING-cloud change into the **local deployment files** in the same session (see item 8 above).

**VM1 power + idle agent (every session that uses VM1)**

VM1 is often **STOPPED**. If the current step needs SSH, Minecraft, or on-box testing, start it first (`oci compute instance action --action START --profile TESTING` + waiter; OCID from local config). Do **not** SoftStop the door unless the step explicitly requires it.

Boot / Minecraft start **force-enables** the idle agent (OS-ISSUE-7 / `mc-boot-ledger.service`). Idle SoftStop will then halt VM1 after the timeout if the game is empty **or not running**. So:

1. **If you start VM1** (or start/restart Minecraft while working): wait until it is **RUNNING** and SSH works, **then disable idle** (config + timer below). If you start Minecraft later in the session, disable idle **again** after that start.
2. **If VM1 is already RUNNING** when you begin: **check idle is off** before doing other work; disable it if it is on.
3. **When you finish** the step (or stop for the day): **turn idle back on** (config + timer). Do not leave the timer disabled. You do not have to SoftStop VM1 yourself — with idle on, an empty/down game will SoftStop after the timeout.

Pure Hybrid UI steps that never contact the test stack may skip this.

Check:

```bash
sudo python3 -c 'import json; c=json.load(open("/etc/mc-manager/config.json")); print("enabled=", c.get("idle_agent_enabled"))'
sudo systemctl is-enabled mc-idle-watch.timer
sudo systemctl is-active mc-idle-watch.timer
```

Disable (session start, after VM1/Minecraft is up):

```bash
sudo python3 -c 'import json; p="/etc/mc-manager/config.json"; c=json.load(open(p)); c["idle_agent_enabled"]=False; json.dump(c, open(p,"w"), indent=2); print("enabled=", c["idle_agent_enabled"])'
sudo systemctl stop mc-idle-watch.timer
sudo systemctl disable mc-idle-watch.timer
```

Re-enable (session end):

```bash
sudo python3 -c 'import json; p="/etc/mc-manager/config.json"; c=json.load(open(p)); c["idle_agent_enabled"]=True; json.dump(c, open(p,"w"), indent=2); print("enabled=", c["idle_agent_enabled"])'
sudo systemctl enable --now mc-idle-watch.timer
```

More idle copy-paste: [`Operator-Troubleshooting.md`](Operator-Troubleshooting.md) (VM1 idle agent).

### Product Functions on TESTING (blanket)

Agents **may** without asking, on profile `TESTING` **only**:

- `fn build` / `fn deploy` / `fn push` of **product** Functions (`functions/shutdown_vm/`, `functions/reconcile_usage/`) onto the **existing** TESTING Function application / OCIR repository
- `fn invoke` / `oci fn function invoke` with **synthetic** Events / JSON payloads
- `docker login` + image push **only** as part of those product Function images

**Still $0:** do **not** fire a real Oracle **$1 compartment budget alert** (that can bill ~$1–$2). SoftStop **VM1** via the Function is OK (Always Free A1 hours). **Do not SoftStop the door Micro**; if an old image still stops both, START the door immediately and push v1 (`0.0.12`, VM1 only + lock PUT). Do not add paid Function memory, extra OCIR repos, or extra Function apps. After lock tests, DELETE `meta/spend-brake-triggered.json` unless the next test needs it. Never print Auth Tokens. **Never** `fn push` / invoke on `DEFAULT` / the live Forge lab.

This blanket is **TESTING / until Step 8.6.1 ships**. The **product** path is a CI-built ARM image copied into the user’s OCIR (no Docker on the admin PC). Do not treat `fn`/`docker buildx` on the operator’s Windows PC as the installer story.

**Not allowed**

- `tofu apply` / `tofu plan` / `tofu destroy` / deleting the compartment unless the operator **explicitly** authorizes that command in the session.
- Arbitrary `docker push` of non-product images, **or** `fn push` / invoke against `DEFAULT` / the live Forge lab. Product Function build/push/invoke on **TESTING** is allowed — see [Product Functions on TESTING](#product-functions-on-testing-blanket).
- Using `DEFAULT`, touching the live **Forge lab** tenancy, or SSH with any key other than the one named above.
- Opening `0.0.0.0/0` on Minecraft, SSH, or door admin.
- Committing secrets, filled `oci.env`, or live OCIDs.
- Wizard Deploy that would `tofu apply` (keep `MCMANAGER_TOFU_DRY_RUN=1` unless the operator authorizes a real apply).

`dotnet build`, `tofu validate` in `infra/`, and dry-run Setup remain always OK.

### Operator prompt (copy-paste for a new agent)

```text
Read docs/V1-Implementation-Plan.md in OCI-mc-server. Implement only the step marked NEXT.
When NEXT is Step 8.7, also read docs/V1-Modpack-Test-Follow-On-Plan.md and implement only the P-section marked NEXT there (not this whole V1 file).
When NEXT is Step 8.8, also read docs/V1-Operator-Notes-Follow-On-Plan.md and implement only the P-section marked NEXT there (not this whole V1 file).
When NEXT is Step 8.4, also read docs/V1-Pass-2-Follow-On-Plan.md (historical — P1–P13 DONE).
MVP Phases 0–7 are DONE. Paid/spend mode is skipped (far future, not v1). Pre-packaging QA is Phase 8.5 (Pass 3 after 8.7 + 8.8). Phase 8.6 is CI-built ARM Function image (no Docker on the admin PC) — required before any official release. Packaging is V1 Phase 9 — do not start 9.1 until Phase 8.5 exits AND Step 8.6.1 is DONE. Phase B (Blazor Hybrid UI) is DONE — do not re-open Avalonia.
You MAY use OCI CLI/API with profile TESTING (not DEFAULT) and SSH both test VMs with %USERPROFILE%\.ssh\mcmgr_ed25519_20260817_125552. Stay at $0. If you change a test VM or TESTING cloud resource, make the same change in the local deployment SoT (onbox/, infra/, door_vm/, vm_agent/, functions/shutdown_vm/).
You MAY fn build, fn push, and invoke product Functions (shutdown_vm, reconcile_usage) on TESTING without asking. Do not fire a real $1 budget alert. Do not SoftStop the door. Never DEFAULT / live Forge lab.
If you need VM1 and it is STOPPED, START it, then disable the idle agent so it does not SoftStop while you work. If VM1 is already RUNNING, confirm idle is off before other work. When you finish, turn the idle agent back on. Minecraft boot force-enables idle (OS-ISSUE-7) — disable again after a game start.
When done: update the V1 plan statuses, stop, tell me what you did, how to test, what’s next, and ask if I want to continue or adjust.
Do not commit. Do not start the following large step unless I say so.
Do not tofu apply / tofu destroy unless I explicitly authorize it.
Prompt sequential steps in Agent mode (not Plan mode). Use Build in Parallel / Plan mode only if the NEXT step is marked PARALLEL-OK. Include this same Agent-vs-Plan instruction in the prompt you give the operator for the following step.
```

---



## V1 goal (from PRODUCT-IDEAS)

> Flexible product on the same Always Free doorbell: **private allowlist only**, CIDR prefixes, spend-brake lock, Paper + file-imported modpacks, Danger Zone isolated from power-user Advanced, then **one** installer and GitHub updates. The spend-brake Function image is **CI-built ARM**, copied into the user’s OCIR — not built with Docker Desktop / Cloud Shell on their PC.

**Explicitly out of this plan (after v1 / later):** Players tab, Start progress checklist, maintenance / reserved-IP controls, multi-deploy profiles, pack-replace **light swap**, full per-day budget **calendar editor**, Quilt as a Setup entry point, Purpur/Folia, interactive PTY console, macOS/Linux Manager, **paid / spend mode** (far future). **Change/replace pack (full re-setup)** is **v1** in Step **8.4** (operator 2026-08-20; PRODUCT-IDEAS still says after-v1 — follow this plan). **Layer 3 quarantine** and Setup identity/icon variants are **v1** in Step **8.8**. Danger Zone as a **separate tab** is superseded by Step **8.4** P3 (merged into Advanced).

**Rejected (will not be implemented, not after-v1):** in-app mod / modpack browser (browse, search, trending, download-a-pack, pick-by-name/URL/ID). Users create or download pack files themselves and select them in Setup or Manager.

**Rejected (will not be implemented, not after-v1):** public Minecraft (`0.0.0.0/0`), a public/private Manager toggle, and a blacklist. Private allowlist only (CIDR from Step 1.2 stays).

**Already shipped in MVP (do not rebuild):** Delete-infrastructure UI (typed `confirm`); Troubleshooting one-shots; Vanilla Setup; Connect-existing; Hybrid WinExe.

---



## Progress dashboard


| Phase   | Focus                                                      | Status                                                |
| ------- | ---------------------------------------------------------- | ----------------------------------------------------- |
| **1**   | Manager shell (Advanced/Danger split, CIDR, wipe world)    | **DONE**                                              |
| **2**   | $1 spend-brake lock (Function flag, door, Manager overlay) | **DONE**                                              |
| **3**   | Remove public/blacklist (was IP Management public mode)    | **DONE**                                              |
| **4**   | Setup game types (Paper, loaders, pack import)             | **DONE** (Step **4.12** deferred)                     |
| **4.13** | Modpack robustness (itzg exclude lists, mixed archives)  | **DONE**                                              |
| **5**   | Server Management modding inspect + re-download pack       | **DONE**                                              |
| **6**   | Top-bar chrome + oversized-world SSH UX                    | **DONE**                                              |
| **7**   | Remaining v1 (resize, console, storage, Connect version)   | **DONE**                                              |
| **8**   | Paid / spend mode                                          | **SKIPPED** (operator 2026-08-18; far future, not v1) |
| **8.4** | Pass-2 follow-on (operator notes)                          | **DONE** (P1–P13)                                     |
| **8.7** | Modpack-test follow-on (Change pack failures)              | **DONE** — [`V1-Modpack-Test-Follow-On-Plan.md`](V1-Modpack-Test-Follow-On-Plan.md) P1–P5 |
| **8.8** | Operator-notes follow-on (Manager / Setup / pack UX)       | **NEXT** = [`V1-Operator-Notes-Follow-On-Plan.md`](V1-Operator-Notes-Follow-On-Plan.md) **P9** |
| **8.5** | Pre-packaging QA (catalog + passes + bug-fix plans)        | **PAUSED** Pass 3 until **8.7 + 8.8** exit — then [`V1-QA-Pass-3-Scope.md`](V1-QA-Pass-3-Scope.md); do not start until the operator says so |
| **8.6** | CI-built ARM spend-brake Function image (no Docker on admin PC) | TODO — after 8.5 exit; **required before 9.1 / official release** |
| **9**   | Packaging, updates, launch (old MVP Phase 8–9)             | TODO — do not start until Phase 8.5 **and** Step **8.6.1** are DONE |


**Current NEXT step:** [Step 8.8](#step-88--operator-notes-follow-on) ([`V1-Operator-Notes-Follow-On-Plan.md`](V1-Operator-Notes-Follow-On-Plan.md) **P9**). Step **8.7** / modpack-test follow-on **P1–P5** is **DONE**. **Do not start Pass 3** until 8.8 exits **and** the operator says so. **Do not start Step 8.6.1** until Phase 8.5 exits. **Do not start Step 9.1** until Phase 8.5 **and** Step **8.6.1** are DONE.

---



## Phase 1 — Manager shell

**Why first:** small, testable Hybrid-only slices; no OpenTofu; no live Function deploy. Unblocks later Danger Zone features (resize, spend-brake recovery).

### Step 1.1 — Split Advanced vs Danger Zone

**Status:** DONE  
**Depends on:** MVP Phase B (DONE)

**Read first**

- `PRODUCT-IDEAS.md` → heading **Advanced vs Danger Zone (v1)** only  
- `src/McManager.Hybrid/Components/Layout/MainLayout.razor` (tab strip + switch)  
- `src/McManager.Hybrid/Components/Tabs/Advanced/AdvancedTab.razor`  
- `src/McManager.Hybrid/ViewModels/AdvancedViewModel.cs`  
- `src/McManager.Hybrid/Components/Tabs/Advanced/DestroyInfrastructureDialog.razor`  
- `src/McManager.Hybrid/ViewModels/DestroyInfrastructureViewModel.cs`

**Do**

- Add a **Danger Zone** tab. Keep **Troubleshooting** as its own tab.  
- **Danger Zone:** disable idle agent (the Enabled checkbox + strong confirm already in `AdvancedViewModel`); **Delete infrastructure** (existing dialog). Leave placeholders only as comments for later paid mode / VM1 scale — do not implement those.  
- **Advanced:** technical status, Deploy/repair + Auto-detect, break-glass VM power, idle **timeout** + budget warning lead time (not the enable/disable brake), stack identity, DEBUG probes.  
- Rename the combined “Advanced / Danger Zone” tab label. Match existing Hybrid visual language; do not redesign the chrome.

**Test**

- `dotnet build src/McManager.slnx`  
- Click through: Advanced no longer contains Delete or idle-disable; Danger Zone does; timeout still saves from Advanced.

**Done when:** Two tabs exist; idle-disable and destroy are only on Danger Zone; timeout remains on Advanced.

**Changelog:** 2026-08-17 — Split Hybrid tabs: **Advanced** (technical status, Deploy/repair, break-glass, idle timeout/warn, stack identity, DEBUG probes) vs **Danger Zone** (idle-enable checkbox + strong confirm, Delete infrastructure). Troubleshooting unchanged. Paid mode / VM1 scale left as comments only. Usage tab no longer offers idle-disable.

---



### Step 1.2 — Allowlist CIDR ranges

**Status:** DONE  
**Depends on:** 1.1

**Read first**

- `PRODUCT-IDEAS.md` → **Allowlist CIDR ranges (v1)** only  
- `src/McManager.Hybrid/ViewModels/WhitelistViewModel.cs`  
- `src/McManager.Hybrid/Components/Tabs/Whitelist/WhitelistTab.razor`  
- `src/McManager.Hybrid/ViewModels/FriendRowViewModel.cs`  
- Core Security List apply type(s) used by the whitelist (grep `SecurityList` / friends apply — open only those files)

**Do**

- Add-IP dialog stays a single IPv4 by default. Small **Advanced** control reveals a CIDR field used **instead of** a host address.  
- Persist prefix on the friends list + Object Storage allowlist when that object is already written. Security List rule source = that CIDR; description = player name.  
- CIDR applies to **Minecraft 25565 TCP/UDP only**. SSH / door admin rules stay `/32` unless the admin is editing **their own** admin entry.  
- Warn that a prefix is wider than one host. Reject reckless prefixes (implementation floor: reject `/0`–`/8` for Minecraft; ask the operator if unsure). IPv4 only.

**Test**

- `dotnet build`; add a `/32` (unchanged) and a `/16`; Apply updates the Security List Minecraft rules only.

**Done when:** CIDR friends sync to Minecraft rules; SSH is not silently widened.

**Changelog:** 2026-08-17 — Add-IP **Advanced** reveals a CIDR field used instead of a host. `ip` in `friends.local.json` / `ip/allowlist.json` is IPv4 or IPv4 CIDR (hosts stored without `/32`). Minecraft 25565 TCP/UDP uses that CIDR; SSH/door stay `/32` unless editing the own admin row. Reject `/0`–`/8`; warn when wider than one host. OS allowlist PUT only if the object already exists.

---



### Step 1.3 — Wipe world

**Status:** DONE  
**Depends on:** 1.1

**Read first**

- `PRODUCT-IDEAS.md` → **Wipe world (v1)** only  
- Blueprint **§11.3** only (not the rest of §11)  
- `src/McManager.Hybrid/ViewModels/ServerManagementViewModel.cs`  
- `src/McManager.Hybrid/Components/Tabs/ServerManagement/ServerManagementTab.razor`  
- Existing SSH world-replace helper in Core (open only that file)

**Do**

- Server Management button near Download World Save. Confirm popup: deletes the **live** world on VM1; backups are kept; point at Download World Save first.  
- Stop Minecraft first (or the action stops it). Do not delete Object Storage backups, mods, or `server.properties`.

**Test**

- `dotnet build`; confirm dialog copy; dry-run / unit the remote path construction against `world_path` from local config. Live wipe only if the operator provides a disposable test VM.

**Done when:** Confirmed wipe path exists; backups are not deleted.

**Changelog:** 2026-08-17 — Server Management **Wipe world** next to Download latest: confirm popup (live save only; cloud backups / mods / `server.properties` kept; Minecraft stopped then left stopped). SSH wipe via `WorldWipe` path guard (`/opt/mcmgr/server/<world>` only). Core unit tests for path construction. Follow-up: wipe no longer calls `repair-permissions.sh` (SETUP-ISSUE-8 same-file `cp`); layout helper skips copy when src is dest. **2026-08-19:** operator overrode leave-stopped — Pass 1 **P8** auto-starts Minecraft after wipe (PRODUCT-IDEAS item 4 may still say next-Start).

---



## Phase 2 — $1 spend-brake lock

Split so Function, door, and Manager each get their own window.

### Step 2.1 — Lock-flag Object Storage contract

**Status:** DONE  
**Depends on:** 1.1 (Danger Zone exists; overlay comes in 2.4)

**Read first**

- `PRODUCT-IDEAS.md` → **$1 spend-brake lock (v1)** only  
- `[Contracts-Object-Storage.md](Contracts-Object-Storage.md)` — existing `meta/` objects + reserved spend-brake row  
- `functions/shutdown_vm/` README (placeholders only)

**Do**

- Freeze key + JSON shape (product sketch: `meta/spend-brake-triggered.json`). Writer = Function; only clearer = Manager after typed confirmation. Door + Manager are readers.  
- Update `Contracts-Object-Storage.md` and the reserved row. No runtime code except a small Core DTO + get/put/delete helpers if that stays under a few files.

**Test**

- `dotnet build` if Core helpers were added; contract doc names the key, fields, and writers.

**Done when:** Contract is named and documented; no live Function deploy.

**Changelog:** 2026-08-17 — Frozen key `meta/spend-brake-triggered.json` v1 (`triggered_at`, `source=budget_function`, optional `alert_type`). Function writes; Manager DELETEs after typed confirm; door+Manager read; fail closed on malformed/newer JSON. Core `SpendBrakeLockDocument` + `SpendBrakeLockStore` (get/put/delete). No Function deploy.

---



### Step 2.2 — Function writes the lock flag

**Status:** DONE  
**Depends on:** 2.1

**Read first**

- Step 2.1 contract (this file + `Contracts-Object-Storage.md` spend-brake section)  
- `PRODUCT-IDEAS.md` → **What the Function must do (v1)** only  
- `functions/shutdown_vm/` source (product copy)  
- Oracle Always Free page (Micro vs Ampere) — re-read; **prefer leaving the door running** if AMD Micro does not accrue Ampere OCPU-hour spend

**Do**

- On a real threshold alert (ignore budget RESET): SoftStop **VM1**; **PUT** the lock object.  
- Product decision in the same step: stop door or not. Default recommendation: **do not SoftStop VM2** if Micro stays Always Free; document the choice in the Function README + [`Infrastructure-Information.md`](Infrastructure-Information.md) (placeholders).  
- Do **not** `fn push` / OCIR. Code + docs only.

**Test**

- Review the handler against a mocked Events payload; no live budget fire.

**Done when:** Tracked Function source writes the flag; door-stop policy is written down.

**Changelog:** 2026-08-17 — Tracked Function PUTs `meta/spend-brake-triggered.json` on real alerts (ignore RESET). SoftStop **VM1 only**; **do not SoftStop the door Micro** (Always Free AMD Micro is a separate envelope, not Ampere OCPU-hours). HCL default stop-list = VM1; Function config gets OS namespace/bucket/lock key. Mocked Events unit tests. No `fn push` / OCIR.

---



### Step 2.3 — Door honors the lock flag

**Status:** DONE  
**Depends on:** 2.1 (2.2 code may still be undeployed)

**Read first**

- `Agent-Deploy-Pitfalls.md` (before any door script/C change)  
- `Contracts-Object-Storage.md` spend-brake section  
- `door_vm/src/control.c` — budget-gate / wake paths only  
- `door_vm/scripts/` wake-pull script(s) that already read budget/ledger (open only those)

**Do**

- Door must **read** the lock flag (same poll discipline as the budget gate) and **never START VM1** while it is set.  
- MOTD/kick: monthly spend brake fired; admin must use Manager after a new calendar month. Distinct from daily-budget-exhausted copy.  
- `HOME` default on systemd scripts; no Python on the Micro.

**Test**

- `make test` in `door_vm/` (MOTD/kick + `SPEND_BRAKE` state names). This Windows session had no gcc; run that in WSL/Linux.
- After **redeploying the door** from `door_vm/` (Testing2 Phase 3+4 or Setup `install.sh`): SSH the door as `ubuntu`, then `sudo bash /opt/mccontrol/oci/pull_os_budget.sh --force` — expect `SPEND_BRAKE_LOCK=0` while the object is absent (wake must not DEGRADE).
- Optional refuse check (delete the object when done): PUT a tiny `meta/spend-brake-triggered.json`, `POST /api/wake`, confirm VM1 stays STOPPED and MOTD/kick contains `MONTHLY SPEND BRAKE FIRED` (not `DAILY BUDGET`). Then DELETE the object and `/api/os-refresh`.

**Done when:** Wake path refuses START when the flag is present.

**Changelog:** 2026-08-17 — Door wake GETs `meta/spend-brake-triggered.json` on every `pull_os_budget.sh` (404 = unlocked; other GET errors fail closed). Presence → `SPEND_BRAKE`; **never** `start_vm1.sh`. MOTD/kick: `MONTHLY SPEND BRAKE FIRED — the admin must use Manager after a new calendar month.` (distinct from daily). Reconcile parks IP like idle. `make test` MOTD/state. Live door needs redeploy from `door_vm/`.

---



### Step 2.4 — Manager full-window lock UX

**Status:** DONE  
**Depends on:** 2.1, 1.1

**Read first**

- `PRODUCT-IDEAS.md` → **Manager UX when the flag is set** only (keep the typed sentence exact)  
- `src/McManager.Hybrid/Components/Layout/MainLayout.razor`  
- `src/McManager.Hybrid/ViewModels/MainViewModel.cs` (Start/Stop gating)  
- Core Object Storage helper from 2.1  
- Existing Troubleshooting park-IP / start-door one-shots (open `TroubleshootingViewModel.cs` only — reuse, do not rewrite)

**Do**

- When the flag is observed (open + Start): fill the **entire window** with the warning. Start is blocked until the operator types the **exact** confirmation sentence from PRODUCT-IDEAS.  
- On confirm: start needed VMs, reconcile to a valid doorbell state (reuse Troubleshooting park-IP / door start — do not invent a second recovery path), **clear** the flag. Manager is the only clearer. Do not auto-clear at month rollover.  
- Idle/daily/monthly OCPU gates still apply after unlock.

**Test**

- `dotnet build`; UI can be exercised with a local/fixture flag object if the operator has a bucket; otherwise a Core unit that the overlay shows when get-object returns the flag.

**Done when:** Overlay + exact typed confirm + clear + reconcile path exist.

**Changelog:** 2026-08-17 — Full-window overlay when `meta/spend-brake-triggered.json` is present (open + Start re-GET). Exact PRODUCT-IDEAS confirmation sentence; Start Server parks play IP (Troubleshooting `ParkPlayIp`), DELETEs the lock, door OS-refresh, then normal Wake (idle/daily/monthly gates still apply). No auto-clear at month rollover. Fail-closed Start when Get fails (no overlay). DEBUG fixture PUT/clear on Advanced probes. Core `SpendBrakeLockUx` unit tests.

---



## Phase 3 — Public / blacklist withdrawn

Operator 2026-08-18: **public Minecraft, the public/private toggle, and blacklist are rejected** (PRODUCT-IDEAS). Private allowlist + CIDR (Step **1.2**) stay. Never write Minecraft `0.0.0.0/0`. Preserve SSH / non-Minecraft rules on every Security List rewrite.

### Step 3.1 — Mode + blacklist persist (no SL rewrite yet)

**Status:** WITHDRAWN (code still present until 3.4)  
**Depends on:** 1.2 (friends list / allowlist objects)

Shipped 2026-08-17 then product-rejected. Historical notes only — do not extend.

**Changelog:** 2026-08-17 — Whitelist **Make server public** / **Make server private** with aggressive confirm before public. Mode + blacklist persist in `friends.local.json` and Object Storage `ip/mode.json` when present. Public notice + blacklist panel. **Apply public access** disabled; Security List not rewritten; no `0.0.0.0/0`. **2026-08-18 — WITHDRAWN.**

---



### Step 3.2 — Security List public / private rewrite

**Status:** WITHDRAWN (code still present until 3.4)  
**Depends on:** 3.1

Shipped 2026-08-17 then product-rejected. The **private** path in `SecurityListIngressPlanner` is still needed (CIDR allowlist). Do not delete the planner; 3.4 removes only the public branch.

**Changelog:** 2026-08-17 — Rewrite **one** Security List. Public: Minecraft 25565 TCP/UDP from `0.0.0.0/0`; SSH never world-open; private restores allowlist. Planner + unit tests. **No live test-tenancy apply.** **2026-08-18 — WITHDRAWN.**

---



### Step 3.3 — Blacklist in public mode

**Status:** CANCELLED  
**Depends on:** 3.2

Never implemented. OCI Security Lists have no deny. Do not ship a CIDR invert. Public/blacklist are **rejected** (not deferred).

**Changelog:** 2026-08-18 — **CANCELLED.** Research: SL/NSG allow-only; invert forbidden; paid Network Firewall out. Product is private allowlist only.

---



### Step 3.4 — Remove public mode + blacklist code

**Status:** DONE  
**Depends on:** docs already updated (PRODUCT-IDEAS rejected public/blacklist; this file)

**Read first**

- `PRODUCT-IDEAS.md` → **Rejected** table (public/blacklist row) + heading **IP Management (v1)** only  
- `src/McManager.Hybrid/Components/Tabs/Whitelist/WhitelistTab.razor`  
- `src/McManager.Hybrid/ViewModels/WhitelistViewModel.cs`  
- `src/McManager.Core/Services/SecurityListIngressPlanner.cs`  
- `src/McManager.Core/Config/FriendsLocalFile.cs`  
- Core `IpModeStore` (open only that file)

**Do**

- Remove the **Make server public / private** toggle, public notices, and the **Blacklist** panel from Hybrid.  
- Remove `mode` / `blacklist` persist from Manager save paths. Stop PUTting `ip/mode.json`. Ignore leftover `mode`/`blacklist` keys in `friends.local.json` (or strip them on save). Update `friends.local.example.json`.  
- Remove the **public** Minecraft `0.0.0.0/0` branch from `SecurityListIngressPlanner` / `ApplyFriendsAsync`. **Keep** the planner for **private** allowlist CIDR/`/32` apply (Step 1.2). Do not revert CIDR.  
- Delete leftover types/tests that exist only for public mode or blacklist (`BlacklistRowViewModel`, public planner tests, `IpAccessMode.Public`, etc.).  
- Optional **TESTING** check (no `tofu apply`): `GetSecurityList` — if Minecraft 25565 is `0.0.0.0/0`, apply the private allowlist and say so. 3.2 claimed no live apply; this is only a safety net.  
- Do **not** start Phase 4. An unused leftover `ip/mode.json` may stay unless a one-line comment is cheap.

**Test**

- `dotnet build src/McManager.slnx` and Core tests that still apply (planner private + CIDR; friends file without requiring mode/blacklist).  
- Whitelist tab: add `/32` and CIDR, Save; no public toggle; no blacklist UI.

**Done when:** Manager is private-only; no public SL code path; CIDR allowlist still works; `ip/mode.json` is not a live writer.

**Changelog:** 2026-08-18 — Removed Hybrid public/private toggle, public notices, and Blacklist panel. Save strips leftover `mode`/`blacklist` keys; no `ip/mode.json` PUT. Planner kept for private CIDR/`/32` allowlist; public `0.0.0.0/0` Minecraft branch deleted. TESTING GetSecurityList: no world-open 25565 (no apply). **NEXT = Step 4.1**. Do not start 4.1 unless asked.

---



## Phase 4 — Setup game types

**Order:** Paper (Optimized Vanilla) first, then loader modules, then pack import. **No in-app catalog** (blueprint §2.4). Quilt = detected loader value only, not a Setup radio. CurseForge **Server Files** zips (jars already in the archive) use the Step **4.9** manual adapter. CurseForge **API** client-export import is **not** a v1 code path — Step **4.12** is deferred.

Each installer step: Core metadata client + `onbox/mcmgr/` module + generic unit/manifest — **one platform per step**.

**Sample packs:** CI uses tiny tracked fixtures under `tests/fixtures/` (blueprint §15). Operator-local real/homemade archives live in gitignored `data/sample-packs/` — see `[Sample-Packs.md](Sample-Packs.md)` (gotchas + which file for 4.7–4.11). If a needed format/loader is missing, **pause and ask the operator to download it**. **Do not** add an in-app pack browser (that feature is **rejected**).

### Step 4.1 — Paper Fill v3 client + fixtures (Core only)

**Status:** DONE  
**Depends on:** Phase 1 (no hard code dep)

**Read first**

- Blueprint **§17** only  
- `PRODUCT-IDEAS.md` → **Setup game types (v1)** → Vanilla branch / Paper bullets  
- Existing Vanilla piston-meta client in Core (open only that file + its tests)  
- Blueprint **§15** (offline fixtures pattern) — only if adding test JSON

**Do**

- Fill v3 HTTP client: list versions/builds, STABLE when exposed, download URL + checksums from JSON, descriptive User-Agent. **Do not** build legacy Fill v2 URLs.  
- Offline fixtures + tests. No Setup UI. No on-box scripts.

**Test**

- `dotnet test` (or `dotnet build` + new fixture tests).

**Done when:** Core can resolve a Paper build from fixtures without touching the network in CI.

**Changelog:** 2026-08-18 — Core `PaperFillV3Client`: Fill v3 project/version/builds GETs with descriptive User-Agent; STABLE + highest build id; `server:default` URL + SHA-256 from JSON (reject `api.papermc.io` v2 URLs); no STABLE → fail, no ALPHA/BETA fallback. Offline fixtures under `tests/fixtures/game-metadata/` (`paper-fill-v3-project.json`, `paper-fill-v3-builds-1.21.10.json`, version + error). No Setup UI, no on-box scripts. **NEXT = Step 4.2**. Do not start 4.2 unless asked.

---



### Step 4.2 — on-box Paper installer module

**Status:** DONE  
**Depends on:** 4.1

**Read first**

- Blueprint **§17**, **§6.3** (jar launch), **§13.2** (SSH modules)  
- `Agent-Deploy-Pitfalls.md`  
- Existing Vanilla on-box installer under `onbox/mcmgr/` (open the Vanilla module + shared layout/unit generator only)

**Do**

- Installer module: download Paper jar via URLs the Manager already resolved (or Fill v3 from the module if MVP Vanilla already downloads on-box — match that pattern). Write `game-manifest.json` (`distribution`/Paper fields per §4.2 fixture). Generic systemd unit — do not add a Paper-specific unit file.  
- No wizard UI yet.

**Test**

- Script/layout unit tests if present; `tofu validate` not required. Test-stack SSH is OK ([Test stack access](#test-stack-access-oci--ssh)); do **not** SSH the live Forge lab.

**Done when:** On-box Paper module exists and writes a valid manifest + unit args for a jar.

**Changelog:** 2026-08-18 — On-box `bootstrap-paper.sh` + `paper_fill_v3.py`: Fill v3 STABLE resolve (SHA-256, `server:default` URL from JSON, no v2 builder, no ALPHA/BETA fallback). Driver `DISTRIBUTION=paper` writes §4.2 manifest + generic unit `--nogui` + Paper recommended JVM flags. Layout verify accepts Paper jar name. No wizard UI. **NEXT = Step 4.3**. Do not start 4.3 unless asked.

---



### Step 4.3 — Setup wizard: Default vs Optimized Vanilla

**Status:** DONE  
**Depends on:** 4.1, 4.2

**Read first**

- `PRODUCT-IDEAS.md` → **Setup game types (v1)** diagram + Vanilla branch  
- `src/McManager.Hybrid/Components/Setup/SetupWizard.razor`  
- `src/McManager.Hybrid/ViewModels/SetupWizardViewModel.cs`  
- Vanilla version-picker code (open only that)

**Do**

- After server type **Vanilla**: **Default Vanilla** (unchanged Mojang path) vs **Optimized Vanilla** (Paper). Then the version picker (Mojang list vs Paper’s list; hide versions Paper does not build).  
- Wire bootstrap to the 4.2 module. Short Guide.md note.

**Test**

- `dotnet build`; wizard dry-run (`MCMANAGER_TOFU_DRY_RUN=1`) shows both vanilla paths. No live apply.

**Done when:** Operator can choose Paper in Setup UI; Default Vanilla still works.

**Changelog:** 2026-08-18 — Setup game step: **Default Vanilla** (Mojang catalog + snapshots Advanced) vs **Optimized Vanilla (Paper)** (Fill v3 version list, hide Mojang versions Paper does not build). Bootstrap `DISTRIBUTION=paper|vanilla` to the 4.2 module; plan summary + `meta/infra.json` `server_kind` follow. Guide.md Game row. **NEXT = Step 4.4**. Do not start 4.4 unless asked.

---



### Step 4.4 — Fabric installer module

**Status:** DONE  
**Depends on:** 4.2 (shared module shape)

**Read first**

- Blueprint **§18** only  
- Shared on-box installer interface from 4.2 (not the Paper Fill client)

**Do**

- Fabric meta client + on-box module + manifest `loader: fabric`. Single runnable jar launch shape. No pack import yet. No Setup Modded radio yet.

**Test**

- Fixtures/tests for meta resolve; `dotnet build`.

**Done when:** Fabric can be installed as a loader module in isolation.

**Changelog:** 2026-08-18 — Core `FabricMetaClient` + on-box `bootstrap-fabric.sh` / `fabric_meta.py`: three-axis `meta.fabricmc.net` v2 resolve (first stable loader + installer; optional pins); launcher jar filename; `/server/jar` URL requires the installer segment; `artifact_hash.algorithm=none_published` (no local sha). Driver `DISTRIBUTION=fabric` writes `distribution=modded` `loader=fabric` + generic unit `nogui`. No pack import, no Setup Modded radio. **NEXT = Step 4.5**. Do not start 4.5 unless asked.

---



### Step 4.5 — NeoForge installer module

**Status:** DONE  
**Depends on:** 4.4 (module pattern)

**Read first**

- Blueprint **§19** only (argfile / XML metadata)  
- Shared module interface; **§6.3** argfile rendering if not already implemented

**Do**

- NeoForge server installer module + manifest. Steer “current version” modded to NeoForge (UI copy in 4.11). No Forge yet.

**Test**

- Fixtures; `dotnet build`.

**Done when:** NeoForge module writes manifest + unit args (argfile).

**Changelog:** 2026-08-18 — Core `NeoForgeMavenClient` + on-box `bootstrap-neoforge.sh` / `neoforge_meta.py`: Maven `maven-metadata.xml` (not JSON); highest non-beta matching MC (component match, not `21.1` vs `21.10`); refuse Minecraft ≤1.20.1; `none_published`; `--installServer` after Java; generic unit `@user_jvm_args.txt @unix_args --nogui`. No Forge, no pack import, no Setup Modded radio. **NEXT = Step 4.6**. Do not start 4.6 unless asked.

---



### Step 4.6 — Forge installer module (legacy packs)

**Status:** DONE  
**Depends on:** 4.5

**Read first**

- Blueprint **§20** only  
- NeoForge module from 4.5 (shared argfile mechanics)

**Do**

- Forge module for packs that **declare Forge** (esp. 1.12.2-era and 1.20.1). Do **not** present Forge as a current-version alternative to NeoForge in Setup.

**Test**

- Fixtures for a pinned legacy version; `dotnet build`.

**Done when:** Forge module exists; UI does not offer Forge vs NeoForge as equal current choices.

**Changelog:** 2026-08-18 — Core `ForgePromotionsClient` + on-box `bootstrap-forge.sh` / `forge_meta.py`: `promotions_slim.json` (prefer `-recommended`, Maven installer URL — not ad HTML); Vanilla `server.jar` first; `none_published`; 1.16.5-and-earlier `single_jar` / 1.17+ `argfile_tree`; Java 8 for pre-1.17; refuse Minecraft older than 1.7. Fixture pin 1.12.2 recommended `14.23.5.2854`. No Setup Forge radio (Vanilla flavor stays vanilla/paper). **NEXT = Step 4.7**. Do not start 4.7 unless asked.

---



### Step 4.7 — Modrinth `.mrpack` analyze (Manager, no install)

**Status:** DONE  
**Depends on:** 4.4–4.6 (need to *detect* loader)

**Read first**

- Blueprint **§22** and **§2.4** only  
- `PRODUCT-IDEAS.md` → **Modded branch** (file picker / no catalog)  
- `[Sample-Packs.md](Sample-Packs.md)` (operator-local archives; gotcha: FO/`env.server`)

**Do**

- Parse an uploaded `.mrpack` locally: pack name, Minecraft version, loader + version, Java, file counts, `env.server` vs client-only.  
- No HTTP catalog/search. No CurseForge. No wizard page yet if cheaper as a Core API + small test harness — a hidden/dev button is OK; 4.11 wires the wizard.

**Test**

- Offline fixture `.mrpack` (tiny) in repo tests (`tests/fixtures/`). Optional: `data/sample-packs/homemade/fabric-strip.mrpack` on this PC (correct Sodium `unsupported` tag — do **not** use Fabulously Optimized / OptiFine for Fabric as the strip test).

**Done when:** Analyzer returns a confirmable summary without installing.

**Changelog:** 2026-08-18 — Core `MrpackAnalyzer` parses a local `.mrpack` / `modrinth.index.json` (no HTTP, no install, no catalog). Summary: pack name, Minecraft, loader+version (fabric/quilt/forge/neoforge), Java floor (§9.1), file counts, `env.server` required/optional vs unsupported vs unclear. Tiny tracked fixture `tests/fixtures/packs/fabric-strip.mrpack`. DEBUG Advanced probe **Analyze .mrpack**. No wizard page. **NEXT = Step 4.8**. Do not start 4.8 unless asked.

---



### Step 4.8 — Modrinth `.mrpack` install (server-side only)

**Status:** DONE  
**Depends on:** 4.7 + matching loader module

**Read first**

- Blueprint **§22**, **§25** (client-pack communication — install a copy reminder / keep original archive on the admin PC)  
- On-box pack converge-not-layer notes in **§12** only as needed (do not implement pack *replace*)

**Do**

- Download files using URLs **already in** the mrpack index (plain GET, not a Modrinth browse API). Strip client-only (`env.server` / side). Keep the original archive in Manager local data for later re-download (Phase 5).  
- Fail/warn loudly when side is unclear.

**Test**

- Fixture pack install into a temp dir (no live VM required). Prefer `data/sample-packs/homemade/fabric-strip.mrpack` (real CDN URLs) when that folder exists; see `[Sample-Packs.md](Sample-Packs.md)`.

**Done when:** Server-side mods land; client-only jars do not; original archive is retained locally.

**Changelog:** 2026-08-18 — Core `MrpackInstaller` GETs URLs already in the index (no catalog API); strips `env.server=unsupported`; fails loudly on unclear side; copies `overrides/` then `server-overrides/` (skips `client-overrides/`); sha512 then sha1. Original archive retained under `data/imported-packs/`. DEBUG Advanced **Install .mrpack (temp dir)**. Homemade `fabric-strip.mrpack` CDN smoke. No wizard page. **NEXT = Step 4.9**. Do not start 4.9 unless asked.

---



### Step 4.9 — Manual server-pack zip import

**Status:** DONE  
**Depends on:** 4.8

**Read first**

- Blueprint **§24** only

**Do**

- File picker for a generic server pack zip (mods/ + loader already present or documented layout). Same no-catalog rule. Same client-only strip where metadata exists.

**Test**

- Fixture zip; `dotnet build`. Operator-local: `data/sample-packs/homemade/manual-server.zip` (`[Sample-Packs.md](Sample-Packs.md)`). If a CurseForge Server Files zip is needed and missing, pause and ask the operator.

**Done when:** Manual zip is a second import adapter, not a rewrite of 4.8.

**Changelog:** 2026-08-18 — Core `ManualServerPackAnalyzer` + `ManualServerPackInstaller` (second adapter, does not rewrite 4.8). Unstructured `mods/`+`config/` unzip; CurseForge Server Files layout when jars/libraries are already in the zip; strip jars whose fabric/quilt/Forge metadata is client-only; refuse `.mrpack`, CurseForge client exports, and launcher zips instead of heuristic-stripping. Retain original under `data/imported-packs/` as `original.zip`. Tracked fixture `tests/fixtures/packs/manual-server.zip`. DEBUG Advanced analyze/install probes. Homemade `manual-server.zip` smoke. No catalog, no wizard. **NEXT = Step 4.10**. Do not start 4.10 unless asked.

---



### Step 4.10 — Setup wizard Modded branch UI

**Status:** DONE  
**Depends on:** 4.7–4.9

**Read first**

- `PRODUCT-IDEAS.md` → **Modded branch**  
- Setup wizard files from 4.3  
- Analyzer/install APIs from 4.7–4.9

**Do**

- Server type **Modded**: file picker / drag-and-drop only. Analyzing progress. Confirm summary. On confirm, continue wizard + bootstrap.  
- Required client-pack copy: tell the admin friends must install the **same exported pack** they uploaded (keep file / re-download later). Short Guide.md note.  
- **No** pack name/URL/ID search box.

**Test**

- `dotnet build`; dry-run wizard. No live apply.

**Done when:** Modded Setup path is selectable and confirmable without a catalog.

**Changelog:** 2026-08-18 — Setup Game step: **Vanilla** vs **Modded**. Modded is file picker + drag-and-drop only (no name/URL/ID search). Analyzing progress, confirmable summary, client-pack copy + two confirm checkboxes. Wires 4.7–4.9 analyzers; Quilt / unclear side / CurseForge client exports cannot continue. Bootstrap uses detected Fabric/Forge/NeoForge module then copies server-side pack files. Guide + Local-Config note. **NEXT = Step 4.11**. Do not start 4.11 unless asked.

---



### Step 4.11 — Client-pack communication polish + Guide

**Status:** DONE  
**Depends on:** 4.10

**Read first**

- Blueprint **§25** only  
- `[Guide.md](Guide.md)` — existing Setup / play sections

**Do**

- Dedicated, novice-readable copy in wizard + Guide: server is not playable for friends until they have the client pack; product cannot reconstruct a client pack from `mods/` on VM1.

**Test**

- Read the Guide section; wizard strings exist.

**Done when:** First-time Modded Setup cannot miss the client-pack requirement.

**Changelog:** 2026-08-18 — Dedicated Setup notice (Game + Review): friends cannot join until they have the same exported pack; vanilla Minecraft is not enough; cannot rebuild a client pack from server `mods/`. Confirm checkbox + pack identity line. Guide section **Modded: friends need the client pack**. **NEXT = Step 4.12**. Do not start 4.12 unless asked.

---



### Step 4.12 — CurseForge pack import (gated)

**Status:** DEFERRED (ToS / API-key custody)  
**Depends on:** 4.10

**Read first**

- Blueprint **§23** only (ToS, API key custody, no cache/proxy, no competing catalog)  
- `PRODUCT-IDEAS.md` → Modded branch CurseForge row

**Decision (operator 2026-08-18)** — docs only; **do not implement** an API client.

- **Do not** apply for, bundle, or ship a CurseForge API key (not in git, not in the WinExe, not on VM1, not in an Always Free Function “relay”). A product-owned key in an open-source desktop app is extractable → sharing the key, which the [3rd Party API Terms](https://support.curseforge.com/en/support/solutions/articles/9000207405-curse-forge-3rd-party-api-terms-and-conditions) forbid. A shared proxy conflicts with the no-proxy clause and **$0**.
- **Do not** drop CurseForge as a *file format*. Step **4.9** already imports a zip whose jars are in the archive, including CurseForge **Server Files**.
- CurseForge **client** exports (`manifest.json` with `projectID`/`fileID` and no jars) stay **refused**. Guide copy: download **Server Files** from that pack’s CurseForge page, or use a Modrinth `.mrpack` if one exists. Do not tell users “Modrinth only.”
- Revisit only if the operator later wants an **operator-owned** key in Windows Credential Manager, with all API + CDN downloads on the **admin PC** (never VM1), no API JSON cache, no catalog UI.

Historical **Do** (not to be started): import a user-supplied CurseForge client export; API only to resolve URLs already named in that manifest; client-only heuristic; stop if key custody / ToS blocked. That last gate fired.

**Test**

- Docs: Guide + PRODUCT-IDEAS + this changelog match the decision. No live CurseForge API. No `tofu apply`.

**Done when:** CurseForge file-import works **or** this step is explicitly deferred in the changelog with the ToS blocker.

**Changelog:** 2026-08-18 — **DEFERRED.** No CurseForge API key in v1. Keep Server Files / filled-zip import (4.9). Refuse client exports; Guide points at Server Files or Modrinth `.mrpack`. Not rejected (unlike catalog). **NEXT = Step 5.1**. Do not start 5.1 unless asked.

---



### Step 4.13 — Modpack robustness (exclude lists)

**Status:** DONE (living: [`V1-Modpack-Robustness-Plan.md`](V1-Modpack-Robustness-Plan.md) R1–R4)  
**Depends on:** Phase 4 DONE; **paused** Step 8.5.2 until this step exited (historical — 4.13 is DONE)

**Read first**

- [`V1-Modpack-Robustness-Plan.md`](V1-Modpack-Robustness-Plan.md) protocol + **only the NEXT R-section**  
- Do **not** load Pass 2, the full blueprint, or this whole V1 file

**Do**

- Implement **only** the robustness plan section marked NEXT (R1 → R4). Stop after each R-section.  
- Blueprint **§24.3** Layers 1–2 (itzg lists + product overlay). Layer 3 crash quarantine is **parked**.  
- CurseForge API (Step **4.12**) stays deferred. Jar-less CF zips stay hard-blocked (P7).

**Test**

- Per the current R-section in the robustness plan.

**Done when:** R1–R4 **DONE** in the robustness plan; Setup warns on mis-declared client mods and still auto-strips them; mixed URL/embedded and jar-root zips install. Then point this plan’s **NEXT** at Step **8.5.2** and update Pass 2’s pack row.

**Changelog:** 2026-08-20 — **R4 DONE** (Setup mis-declaration warning + optional GitHub Layer 1 refresh + Guide). **R1–R4 complete.** Living **NEXT = Step 8.5.2** (do not start Pass 2 until the operator says so). Do not start 8.6.1 or 9.1. 2026-08-20 — **R3 DONE** (manual / jar-root / CF-with-jars use CF exclude list; jar-root → `mods/`; mixed CF still P7-style refuse; `jar-root.zip`). Living **NEXT = R4**. Pass 2 still paused. Do not start 8.5.2, 8.6.1, or 9.1. 2026-08-20 — **R2 DONE** (`.mrpack` matcher after `env.server`; mixed embedded+URL; override-jar filter; `fabric-mistag.mrpack`). Living **NEXT = R3**. Pass 2 still paused. Do not start 8.5.2, 8.6.1, or 9.1. 2026-08-20 — **R1 DONE** (Core matcher + embedded itzg JSON; installers unchanged). Living **NEXT = R2**. Pass 2 still paused. Do not start 8.5.2, 8.6.1, or 9.1. 2026-08-20 — **Inserted** (operator). Pause Pass 2. Living plan R1 = matcher + vendor itzg JSON. Do not start 8.5.2, 8.6.1, or 9.1.

---



## Phase 5 — Server Management modding



### Step 5.1 — Inspect mods + re-download imported pack

**Status:** DONE  
**Depends on:** 4.10

**Read first**

- `PRODUCT-IDEAS.md` → **Server Management modding (v1)** only  
- `ServerManagementTab.razor` + `ServerManagementViewModel.cs`  
- Local pack-archive path from 4.8

**Do**

- For Modded stacks: list/summary of server-side mods on VM1 (live `mods/` and/or manifest). Inspect-only.  
- **Download pack** = the original imported archive from admin-PC local data (optional VM1 copy outside `mods/`). Never zip VM1 `mods/` and call it the client pack.  
- Vanilla/Paper: short “not a modded server” empty state. **No** change/replace pack (after v1).

**Test**

- `dotnet build`; empty state on Vanilla config; download disabled with a clear message if the local archive is missing.

**Done when:** Inspect + original-archive download exist; no catalog.

**Changelog:** 2026-08-18 — Server Management **Modding** section: Vanilla/Paper empty state; Modded lists live `mods/` via SSH (inspect-only) and **Download pack** copies `data/imported-packs/` original archive (never a zip of VM1 `mods/`). Missing local archive disables download with a reconstruct warning. Guide note. **NEXT = Step 6.1**. Do not start 6.1 unless asked.

---



## Phase 6 — Top-bar chrome + oversized world



### Step 6.1 — Overflow menu + settings gear

**Status:** DONE  
**Depends on:** 1.1

**Read first**

- `PRODUCT-IDEAS.md` → v1 table row **Top-bar right chrome** + Manager UI top-bar notes (not the whole UI chapter)  
- `MainLayout.razor` header/chrome only  
- `find-skills` / existing Hybrid CSS — do not add Avalonia packages

**Do**

- Right-side **overflow** (About, extras) and **settings** (program settings: paths, update-check toggle placeholder for Phase 9). Native OS chrome stays. No mini-terminal.  
- Do **not** build the notification list yet.

**Test**

- `dotnet build`; buttons open panels; manage tabs unchanged.

**Done when:** Gear + overflow exist without a bell list.

**Changelog:** 2026-08-18 — Title-row **gear** (program settings: resolved paths + Open/Copy; update-check toggle persisted to `%LOCALAPPDATA%\McManager\app-settings.json`, no GitHub check yet) and **overflow** (About + Source on GitHub). Native OS chrome; no bell list. Guide + Local-Config notes. **NEXT = Step 6.2**. Do not start 6.2 unless asked.

---



### Step 6.2 — Notification center (bell)

**Status:** DONE  
**Depends on:** 6.1

**Read first**

- Same PRODUCT-IDEAS chrome row  
- `MainLayout.razor` + a new small ViewModel (keep it small)

**Do**

- Bell + notification list shell (empty states, dismiss). One in-app channel later steps can post to. No Start checklist. No Players.

**Test**

- `dotnet build`; can post a fake notification in DEBUG.

**Done when:** Bell shows a dismissible list.

**Changelog:** 2026-08-18 — Title-row **bell** + dismissible list (empty state; unread pip). Core `NotificationCenter` is the in-app channel (session-only, cap 50). DEBUG Advanced probe **Post fake notification**. No Start checklist, no Players. Guide + Local-Config notes. **NEXT = Step 6.3**. Do not start 6.3 unless asked.

---



### Step 6.3 — Oversized-world SSH download + bell

**Status:** DONE  
**Depends on:** 6.2

**Read first**

- `PRODUCT-IDEAS.md` → **Oversized world backup (v1)** heading (search that title)  
- `[Contracts-Object-Storage.md](Contracts-Object-Storage.md)` `meta/oversized-world-backup.json`  
- `ServerManagementViewModel.cs` Download World Save path  
- Blueprint **§11.2** only if needed

**Do**

- Detect the OS flag; bell notification; **Download World Save** uses SSH pull when OS backups are blocked for size. Do not upload the zip to Object Storage.

**Test**

- `dotnet build`; with a fixture flag, UI switches to SSH path messaging.

**Done when:** Flag → bell + SSH download offer when VM1 is up.

**Changelog:** 2026-08-18 — Detect `meta/oversized-world-backup.json` on Manager open and Server Management refresh; bell posts once while blocked. **Download latest world save** streams the live world over SSH (`world_backup.py --stream-stdout`) when VM1 is RUNNING and does not PUT the zip to Object Storage; Start-first copy when VM1 is down. Per-row cloud backups still download from Object Storage. DEBUG PUT/clear fixture. Core store + UX tests. **NEXT = Step 7.1**. Do not start 7.1 unless asked.

---



## Phase 7 — Remaining v1 Manager / infra



### Step 7.1 — VM1 shape scaling (Danger Zone)

**Status:** DONE  
**Depends on:** 1.1

**Read first**

- `PRODUCT-IDEAS.md` → **VM1 shape scaling (v1)** only  
- Danger Zone tab from 1.1  
- Core Compute instance update API usage (open existing Compute facade only)  
- `[OCI-API-Usage.md](OCI-API-Usage.md)` waiters

**Do**

- Show current OCPU/memory. Apply scale only from Danger Zone with hard warnings. VM1 **and** Minecraft must be stopped first. Update shared config/meta; per-interval ledger shape fields already exist — do not break them. Preview remaining monthly playtime (same ~1500 OCPU-h envelope).  
- Do not advertise shapes beyond 4/24 until Oracle Always Free envelope confirmation (operator research). Optional 8 OCPU only if the operator already confirmed docs.

**Test**

- `dotnet build`; UI disabled unless VM1 STOPPED. No live resize unless operator authorizes test tenancy.

**Done when:** Scale apply path exists with warnings; ledger history still valid.

**Changelog:** 2026-08-18 — Danger Zone **Change game computer size**: live GetInstance OCPU/memory; radios 4/24 and 2/12 only (no 8 OCPU); Apply disabled unless VM1 STOPPED; hard confirm with remaining playtime preview (~1500 OCPU-h envelope ÷ shape; stack budget target). UpdateInstance shapeConfig + waiter; writes `config.local.json`, `budget/config.json`, `meta/infra.json`; ledger intervals unchanged. Core `Vm1ShapeScaleUx` tests. No live test-tenancy resize. **NEXT = Step 7.2**. Do not start 7.2 unless asked.

---



### Step 7.2 — Always-on-capable 2/12 messaging

**Status:** DONE  
**Depends on:** 7.1 (or Setup 2/12 picker already shipped in MVP)

**Read first**

- `PRODUCT-IDEAS.md` v1 row **Always-on-capable small shape UX**  
- Usage tab copy + door MOTD budget strings **only if** this step must change them (grep; do not rewrite door C unless copy is actually wrong)

**Do**

- When VM1 is 2 OCPU / 12 GB (or another shape that can stay up ~24/7 inside Always Free), soften MOTD / Usage scare-copy. Still meter usage.

**Test**

- Copy review at 2/12 vs 4/24.

**Done when:** 2/12 users are not nagged as if they were on a scarce 4-OCPU budget.

**Changelog:** 2026-08-18 — Soften Usage / pin / idle MOTD copy when VM1 can stay ~24/7 inside Always Free (2 OCPU × 31d ≤ ~1500 OCPU-h). 4/24 keeps remaining-hours and cap language. Metering, daily-exhausted, and spend-brake copy unchanged. Core `AlwaysOnCapableCopy` tests. Live door needs redeploy from `door_vm/` for MOTD. **NEXT = Step 7.3**. Do not start 7.3 unless asked.

---



### Step 7.3 — Infra vs app version on Connect existing

**Status:** DONE  
**Depends on:** MVP Phase 5 (DONE)

**Read first**

- `PRODUCT-IDEAS.md` → **App version vs infrastructure version**  
- `ConnectExistingFlow.cs`  
- `docs/Local-Config.md` (schema fields only)

**Do**

- Enforce or strongly warn on `infra_schema` / `stack_version` mismatch during Connect existing. Optional tag discovery only when meta is missing — keep auto-detect **button-gated**.

**Test**

- `dotnet build`; fixture meta with wrong schema shows confirm/block as designed.

**Done when:** Connect existing does not silently attach to an incompatible stack.

**Changelog:** 2026-08-18 — Connect existing **blocks** when `infra_schema` or document `version` is newer than this Manager (hydrate refuses; no `config.local.json` write). Older schema, legacy meta, or `stack_version` drift → extra confirm. Chooser marks `(incompatible)` / `(version warning)`. Auto-detect stays button-gated; no tag rediscovery. Core fixture tests. Guide + Local-Config + contracts. **NEXT = Step 7.4**. Do not start 7.4 unless asked.

---



### Step 7.4 — Conditional Object Storage writes (etag)

**Status:** DONE  
**Depends on:** existing Core Object Storage client

**Read first**

- `PRODUCT-IDEAS.md` → **Central Object Storage — source of truth** writer rules  
- `[Contracts-Object-Storage.md](Contracts-Object-Storage.md)` version/etag notes  
- Core Object Storage client only

**Do**

- Use etag / if-match (or generation) on Manager writes for budget, meta, and IP mode/allowlist. Do not redesign flag categories. Do not add a `backups` dirty-flag category.

**Test**

- `dotnet build`; conflict path returns a clear error instead of clobbering.

**Done when:** Those Manager writers are conditional.

**Changelog:** 2026-08-18 — Manager `If-Match` on `budget/config.json`, `meta/infra.json`, `meta/flags.json` (those publishes), and `ip/allowlist.json`. GetObject returns ETag; 412 → refresh-and-retry instead of clobber. First create stays unconditional. `ip/mode.json` still has no writer. Core conflict tests. No dirty-flag category changes. **NEXT = Step 7.5**. Do not start 7.5 unless asked.

---



### Step 7.5 — RCON + log console tab (not PTY)

**Status:** DONE  
**Depends on:** existing SSH/RCON helpers

**Read first**

- `PRODUCT-IDEAS.md` v1 row **Server Management / customization** (console part)  
- Core RCON/SSH helpers (open only those)  
- `MainLayout.razor` tab strip

**Do**

- A **Console** tab: send RCON commands; show recent Minecraft logs via SSH. **Not** an interactive Java PTY. Not a mini-terminal on the status card.

**Test**

- `dotnet build`; RCON localhost-only still not exposed on the Security List.

**Done when:** Operator can send RCON and view logs from Manager.

**Changelog:** 2026-08-18 — Hybrid **Console** tab (after Server Management): SSH `journalctl -u minecraft` log well + Send to localhost RCON via on-box `/etc/mcmgr/rcon.secret` (command base64, never a Security List 25575). Leading `/` optional. Not a PTY; status card unchanged. Core `MinecraftConsoleRemote` tests + planner 25575 assertion. Guide + Local-Config notes.

---



### Step 7.6 — Server name / icon / description / chat messages

**Status:** DONE  
**Depends on:** 7.5 optional (can land without console)

**Read first**

- `PRODUCT-IDEAS.md` same customization row (name, icon, description, automated chat in storage)  
- Existing `messages/` Object Storage sketch in contracts

**Do**

- Persist MOTD-scale customization (name, icon, description, scheduled chat JSON) in Object Storage. Wire what the door/VM1 already consume; do not build a rich MOTD visual editor.

**Test**

- `dotnet build`; objects round-trip.

**Done when:** Those fields save to shared storage; no PTY.

**Changelog:** 2026-08-18 — Server Management **Name, icon, and messages**: persist `messages/chat.json` (name, description, `chat_messages`) + optional `messages/server-icon.png` (64×64 PNG). Manager If-Match; flags `messages.vm1`. VM1 `record_boot.py` force-pulls and applies motd/icon + idle templates. No rich MOTD editor; doorbell MOTD unchanged. Setup seeds chat.json. Core round-trip tests. **NEXT = Step 7.7**. Do not start 7.7 unless asked.

---



### Step 7.7 — Usage API 48h ledger reconcile Function (code only)

**Status:** DONE  
**Depends on:** ledger contract

**Read first**

- `PRODUCT-IDEAS.md` v1 row **Usage API reconciliation**  
- `functions/shutdown_vm/` only as a **pattern** for a second function (do not modify the $1 function in this step)  
- `[OCI-API-Usage.md](OCI-API-Usage.md)`

**Do**

- Tracked Function source: for ledger days **older than ~48 hours**, reconcile from OCI Usage API, write back, bump dirty/version. Placeholders, no OCIR push.  
- Do not run it against the lab tenancy.

**Test**

- Unit with a mocked usage payload.

**Done when:** Source + README exist; not deployed unless the operator later asks.

**Changelog:** 2026-08-18 — Tracked Function `functions/reconcile_usage/`: Usage API `USAGE` daily rows for UTC days older than ~48h; Ampere A1 OCPU/memory only (ignore door Micro); write `daily_overrides` (`note=usage_api_reconcile`), bump `revision`, dirty all three ledger consumers; preserve intervals and manual overrides; never plant a zero-API override. Mocked payload unit tests. No `fn push` / OCIR / live Usage API run. **2026-08-18 — paid mode skipped; NEXT = Step 9.1.** Do not start 9.1 unless asked.

---



## Phase 8 — Paid / spend mode

**Status: SKIPPED** (operator 2026-08-18). Not v1. Product remains Always Free / $0. The idea stays in `PRODUCT-IDEAS.md` as **later / far future** — if it is ever built, it will not be this plan. **Do not implement Steps 8.1–8.2.**

### Step 8.1 — Paid mode model + Danger Zone UI

**Status:** SKIPPED  
**Depends on:** — (withdrawn from v1)

Historical **Do** (not to be started): Danger Zone opt-in; max monthly spend; daily/monthly uptime ↔ estimated cost; SoftStop on final alert only; never infer paid mode from PAYG tenancy status.

**Changelog:** 2026-08-18 — **SKIPPED.** Operator removed paid mode from v1. Idea remains later / far future. **NEXT = Step 9.1**. Do not start 9.1 unless asked.

---



### Step 8.2 — Cost Estimator JSON fallback

**Status:** SKIPPED  
**Depends on:** 8.1 (also skipped)

Historical **Do** (not to be started): ship a preset Cost Estimator configuration JSON; Wizard/Danger Zone import to confirm $0 Always Free or match in-app paid estimates.

**Changelog:** 2026-08-18 — **SKIPPED** with 8.1.

---



## Phase 8.4 — Pass-2 follow-on (operator notes)

**Why this sits here:** QA Pass 2 closed early after greenfield Modded + join. Operator notes from that pass (UX, pack replace, Function fill-in, jar-root continue) land **before** Pass 3 so they are not tested twice.

### Step 8.4 — Pass-2 follow-on (operator notes)

**Status:** DONE (living: [`V1-Pass-2-Follow-On-Plan.md`](V1-Pass-2-Follow-On-Plan.md) **P1–P13 DONE**)  
**Depends on:** Pass 2 closed (S7-04 / S3-05 / S4-11 recorded)

**Read first**

- [`V1-Pass-2-Follow-On-Plan.md`](V1-Pass-2-Follow-On-Plan.md) protocol + **only the NEXT P-section**  
- Do **not** load Pass 3, the full blueprint, or this whole V1 file

**Do**

- Implement **only** the follow-on plan section marked NEXT (P1 → P13). Stop after each P-section.  
- Same TESTING permissions as this file’s [Test stack access](#test-stack-access-oci--ssh) + [Functions blanket](#product-functions-on-testing-blanket). No `tofu apply`/`destroy` unless a P-section says to stop and ask.  
- Do **not** start Pass 3, Step **8.6.1** CI, or **9.1**. P13 is Setup artifact lookup only.

**Test**

- Per the current P-section in the follow-on plan.

**Done when:** P1–P13 **DONE** in the follow-on plan. Historical: this step then pointed **NEXT** at Pass 3; operator 2026-08-21 inserted Steps **8.7** and **8.8** first.

**Changelog:** 2026-08-21 — **8.4 remains DONE.** Operator postponed Pass 3 again; living **NEXT = Step 8.7**. 2026-08-20 — **P13 DONE** (Setup prefers pre-built ARM Function tarball; copy into OCIR without Docker; buildx fallback). **8.4 complete.** (Then Pass 3; superseded 2026-08-21 by 8.7/8.8.) Do not start Pass 3, 8.6.1 CI, or 9.1. 2026-08-20 — **P12 DONE** (TESTING spend-brake Function fill-in: OCIR push + Function + Events via OCI CLI; synthetic RESET/ACTUAL; gitignored ARM tarball). Living **NEXT = P13**. Do not start Pass 3, 8.6.1 CI, or 9.1. 2026-08-20 — **P11 DONE** (Server Management **Change pack** UI; live FO pack not replaced). Living **NEXT = P12**. Do not start Pass 3, 8.6.1 CI, or 9.1. 2026-08-20 — **P10 DONE** (pack replace full re-setup: on-box prepare + Core `ReplacePackAsync`; keep world unless wipe). Living **NEXT = P11**. Do not start Pass 3, 8.6.1 CI, or 9.1. 2026-08-20 — **P9 DONE** (manual / jar-root unclear-side may continue; `.mrpack` unclear still blocked). Living **NEXT = P10**. Do not start Pass 3, 8.6.1 CI, or 9.1. 2026-08-20 — **P8 DONE** (Usage **Detailed usage** expander: UTC days, closed by default). Living **NEXT = P9**. Do not start Pass 3, 8.6.1 CI, or 9.1. 2026-08-20 — **P7 DONE** (per-tab vertical scroll memory). Living **NEXT = P8**. Do not start Pass 3, 8.6.1 CI, or 9.1. 2026-08-20 — **P6 DONE** (Console Simple vs Full log; RCON plumbing hidden in Simple). Living **NEXT = P7**. Do not start Pass 3, 8.6.1 CI, or 9.1. 2026-08-20 — **P5 DONE** (“game computer” → “server” in Setup, Manager, Guide). Living **NEXT = P6**. Do not start Pass 3, 8.6.1 CI, or 9.1. 2026-08-20 — **P4 DONE** (window-locked dismissible action banners). Living **NEXT = P5**. Do not start Pass 3, 8.6.1 CI, or 9.1. 2026-08-20 — **P3 DONE** (Danger Zone merged into Advanced; idle only under that heading; vibrant redstone). Living **NEXT = P4**. Do not start Pass 3, 8.6.1 CI, or 9.1. 2026-08-20 — **P2 DONE** (Setup Deployment Complete + reserved play IP Copy). Living **NEXT = P3**. Do not start Pass 3, 8.6.1 CI, or 9.1. 2026-08-20 — **P1 DONE** (Start STOPPED gate, overlay unlock-only, Players pin). Living **NEXT = P2**. Do not start Pass 3, 8.6.1 CI, or 9.1. 2026-08-20 — **Inserted** (docs only). Living **NEXT = P1**. Pass 2 closed early; Step **8.5.2** paused. Do not start Pass 3, 8.6.1 CI, or 9.1.

---



## Phase 8.7 — Modpack-test follow-on

**Why this sits here:** Operator informal **Change pack** tests ([`Mod-Pack-Tests.md`](Mod-Pack-Tests.md), 2026-08-21) failed **4 / 5**. Pause Pass 3. Fix **generalizable** start/strip/Java gaps before operator-notes UX (Step **8.8**) so Pass 3 is not testing a known-broken pack path twice.

### Step 8.7 — Modpack-test follow-on

**Status:** DONE (living: [`V1-Modpack-Test-Follow-On-Plan.md`](V1-Modpack-Test-Follow-On-Plan.md) P1–P5)  
**Depends on:** Step **8.4** DONE; Pass 3 not started

**Read first**

- [`V1-Modpack-Test-Follow-On-Plan.md`](V1-Modpack-Test-Follow-On-Plan.md) protocol + **only the NEXT P-section**  
- Do **not** load Pass 3, Step **8.8**, the full blueprint, or this whole V1 file

**Do**

- Implement **only** the follow-on section marked NEXT (P1 → P5). Stop after each P-section.  
- Same TESTING permissions as this file’s [Test stack access](#test-stack-access-oci--ssh) + [Functions blanket](#product-functions-on-testing-blanket). No `tofu apply`/`destroy`.  
- Do **not** start Step **8.8**, Pass 3, **8.6.1**, or **9.1**.  
- Do **not** denylist only the exact jars from the informal tests.

**Test**

- Per the current P-section in the modpack-test follow-on plan.

**Done when:** P1–P5 **DONE** in that plan. Then point this plan’s **NEXT** at Step **8.8** P1. Do not start 8.8 unless the operator says to continue.

**Changelog:** 2026-08-21 — **P5 DONE** (high-unclear analyze warning: ≥10 or ≥50% unclear mod jars). **8.7 complete.** Living **NEXT = Step 8.8 P1**. 2026-08-21 — **P4 DONE** (Java major on Setup + Change pack: `JAVA_MAJOR` → driver, Fabric 26.x → 25 on-box, clear Temurin install fail). Living **NEXT = P5**. 2026-08-21 — **P3 DONE** (Fabric / `.mrpack` leftover client: overlay classes `loading-screen` / `konkrete` / `titlebar` / `flatlaf`; leftover in-jar client skip; `fabric-gui-client.mrpack`). Living **NEXT = P4**. 2026-08-21 — **P2 DONE** (unstructured zip in-jar side: `InJarSideDetector` side fields, client entrypoints, high-confidence common mixin targets). Living **NEXT = P3**. 2026-08-21 — **P1 DONE** (crash-aware Setup/Change pack health). Living **NEXT = P2**. 2026-08-21 — **Inserted** (docs only). Living **NEXT = P1**. Informal Change pack tests 1/5. Clusters: crash-aware health, unstructured in-jar side, Fabric leftover clients, Java major on pack change, high-unclear warnings. Do not start 8.8, Pass 3, 8.6.1, or 9.1.

---



## Phase 8.8 — Operator-notes follow-on

**Why this sits here:** After pack-start gaps (8.7), implement Manager/Setup/pack UX notes **before** Pass 3. Notes are often vague; the living plan records **scrutiny decisions** (no Oracle™ on default names, Layer 3 in v1, CF helper = links only).

### Step 8.8 — Operator-notes follow-on

**Status:** NEXT (living: [`V1-Operator-Notes-Follow-On-Plan.md`](V1-Operator-Notes-Follow-On-Plan.md) **P9**)  
**Depends on:** Step **8.7** DONE

**Read first**

- [`V1-Operator-Notes-Follow-On-Plan.md`](V1-Operator-Notes-Follow-On-Plan.md) protocol + **only the NEXT P-section**  
- Do **not** load Pass 3, the full blueprint, or this whole V1 file

**Do**

- Implement **only** the follow-on section marked NEXT (P1 → P11). Stop after each P-section.  
- UI sections must read the skills named in that plan.  
- Same TESTING permissions as this file. No `tofu apply`/`destroy` unless a P-section says to stop and ask.  
- Do **not** start Pass 3, **8.6.1**, or **9.1**.

**Test**

- Per the current P-section in the operator-notes plan.

**Done when:** P1–P11 **DONE** in that plan. Then point this plan’s **NEXT** at Step **8.5.2** Pass 3 ([`V1-QA-Pass-3-Scope.md`](V1-QA-Pass-3-Scope.md)). Do not start Pass 3 until the operator says so.

**Changelog:** 2026-08-21 — **P8 DONE** (admin-PC icon variants: color 64×64 + door greyscale overlays; Object Storage + door pull). Living **NEXT = P9**. Do not start Pass 3, 8.6.1, or 9.1. 2026-08-21 — **P7 DONE** (Setup Name and icon page; type-based defaults, no Oracle™; seeds `messages/chat.json`). Living **NEXT = P8**. Do not start Pass 3, 8.6.1, or 9.1. 2026-08-21 — **P6 DONE** (auto compartment `mcmgr` / `mcmgr-2`…; Compartment wizard page removed). Living **NEXT = P7**. Do not start Pass 3, 8.6.1, or 9.1. 2026-08-21 — **P5 DONE** (Setup wizard copy/layout, taller deploy log, humanized dock status). Living **NEXT = P6**. Do not start Pass 3, 8.6.1, or 9.1. 2026-08-21 — **P4 DONE** (shared bottom progress dock for Setup Deploy + Change pack). Living **NEXT = P5**. Do not start Pass 3, 8.6.1, or 9.1. 2026-08-21 — **P3 DONE** (compact lower-right toasts; Start/Stop progress dismiss). Living **NEXT = P4**. Do not start Pass 3, 8.6.1, or 9.1. 2026-08-21 — **P2 DONE** (stop tab-open toasts: backup list, infra meta load). Living **NEXT = P3**. Do not start Pass 3, 8.6.1, or 9.1. 2026-08-21 — **P1 DONE** (Console Simple filter: RCON/journal/mixin/modloader noise). Living **NEXT = P2**. Do not start Pass 3, 8.6.1, or 9.1. 2026-08-21 — **Inserted** (docs only). Blocked on 8.7. Then **NEXT = P1**. Do not start Pass 3, 8.6.1, or 9.1.

---



## Phase 8.5 — Pre-packaging QA

**Why this sits before packaging:** v1 features (Phases 1–7) are **DONE**. Phase 8 is **SKIPPED**. Find and fix bugs on `dotnet run` + the TESTING stack **before** the Function-image product path (Step **8.6.1**) and the Windows installer (Step 9.1). Repeat catalog → results → bug-fix plan until the [QA exit](V1-QA-Catalog.md#qa-exit-phase-85-done) bar is met.

**Docs (do not implement product features from these files):**


| File                                                         | Role                                                                       |
| ------------------------------------------------------------ | -------------------------------------------------------------------------- |
| `[V1-QA-Catalog.md](V1-QA-Catalog.md)`                       | Stable tests, runners (`agent` / `hybrid` / `operator`), expected, restore |
| `[V1-QA-Pass-1-Results.md](V1-QA-Pass-1-Results.md)`         | Pass 1 fill-out (Vanilla, existing stack; **historical**)                  |
| `[V1-QA-Pass-2-Scope.md](V1-QA-Pass-2-Scope.md)`             | Pass 2 include/skip (**historical** — closed early after Phase A + join) |
| `[V1-QA-Pass-2-Results.md](V1-QA-Pass-2-Results.md)`         | Pass 2 fill-out (Modded greenfield; no Pass 2 bug-fix plan)                |
| `[V1-Pass-2-Follow-On-Plan.md](V1-Pass-2-Follow-On-Plan.md)` | **DONE (P1–P13).** Operator notes after Pass 2                            |
| `[V1-Modpack-Test-Follow-On-Plan.md](V1-Modpack-Test-Follow-On-Plan.md)` | Step **8.7**. Informal Change pack failures. **DONE** (P1–P5) |
| `[V1-Operator-Notes-Follow-On-Plan.md](V1-Operator-Notes-Follow-On-Plan.md)` | Step **8.8**. Manager / Setup / pack UX notes. **NEXT = P9** |
| `[V1-QA-Pass-3-Scope.md](V1-QA-Pass-3-Scope.md)`             | Pass 3 gap-close + follow-on tests. **Do not start** until **8.7 + 8.8** exit and the operator says so |
| `[V1-QA-Pass-3-Results.md](V1-QA-Pass-3-Results.md)`         | Pass 3 fill-out (do not start until operator says so)                      |
| `[V1-Bug-Fix-Plan-Pass-1.md](V1-Bug-Fix-Plan-Pass-1.md)`     | Pass 1 fixes; **P1–P8 DONE**. Do not re-open unless a regression.          |
| `[V1-Modpack-Robustness-Plan.md](V1-Modpack-Robustness-Plan.md)` | **DONE (R1–R4).** Exclude lists + mixed archives.                          |
| `[V1-Bug-Fix-Plan-TEMPLATE.md](V1-Bug-Fix-Plan-TEMPLATE.md)` | Copy to `V1-Bug-Fix-Plan-Pass-N.md` after triage                           |


Pass 2 is **closed early** (no triage). Do **not** regenerate the whole catalog each pass. Do **not** create `V1-Bug-Fix-Plan-Pass-2.md`. Pass 3 waits for Steps **8.7** and **8.8**, then the operator to start Step **8.5.2**.

**Not this phase:** installer, GitHub Releases, CI Function-image publisher (that is **8.6.1**), real **$1 budget fire** (clean-room / accepted spend), live Forge lab, after-v1 PRODUCT-IDEAS.

### Step 8.5.1 — QA catalog + agent runner protocol

**Status:** DONE  
**Depends on:** Phases 1–7 DONE; Phase 8 skipped

**Read first**

- This phase heading  
- `[V1-QA-Catalog.md](V1-QA-Catalog.md)` (created in this step)

**Do**

- Write the catalog (suites S0–S8, runners, $0 Function invoke vs real budget fire).  
- Write Pass 1 results skeleton + bug-fix plan template.  
- Grant TESTING **product Function** build/push/invoke without per-session ask; `tofu apply`/`destroy` still operator-authorized.

**Test**

- Operator can start S0–S2 with the catalog prompt; IDs match the results file.

**Done when:** Catalog + Pass 1 results file + template exist; this plan’s dashboard points at 8.5.2.

**Changelog:** 2026-08-19 — Catalog, Pass 1 results skeleton, bug-fix template. Function blanket on TESTING. **NEXT = Step 8.5.2**. Do not start 9.1.

---



### Step 8.5.2 — Execute QA passes

**Status:** TODO (Pass 3) — do not start until Steps **8.7** and **8.8** exit **and** the operator says so  
**Depends on:** 8.5.1 + Step **4.13** DONE + Step **8.4** DONE + Steps **8.7** and **8.8** (before Pass 3)

**Read first**

- **Pass 3 (after 8.4):** `[V1-QA-Pass-3-Scope.md](V1-QA-Pass-3-Scope.md)` protocol + **only the phase** you were asked to run  
- `[V1-QA-Catalog.md](V1-QA-Catalog.md)` — named IDs only  
- `[V1-QA-Pass-3-Results.md](V1-QA-Pass-3-Results.md)`  
- Pass 2 is **historical:** `[V1-QA-Pass-2-Results.md](V1-QA-Pass-2-Results.md)` (do not fill it). Pass 1 is historical.

**Do (not product features)**

**Pass 1 (DONE):** S0–S7 on the existing Vanilla TESTING stack (S7-04 Skipped). Bug-fix P1–P8 DONE. Do not re-run that catalog.

**Pass 2 (DONE, closed early):** Delete + greenfield **Modded** (FO; S6-01/S6-02/S7-04/S3-05/S4-11 Pass). Phase B–D not run. No Pass 2 bug-fix plan (in-pass SETUP-ISSUE-9/10). Do not `tofu destroy` again from this step.

**Pass 3 (blocked):** Follow `[V1-QA-Pass-3-Scope.md](V1-QA-Pass-3-Scope.md)` when the operator starts Pass 3 **after** Steps **8.7** and **8.8**. Gap-close + follow-on tests (including 8.4 / 8.7 / 8.8) on the **existing** TESTING stack. **Do not** `tofu destroy` unless that prompt says so. **One** agent chat on the test stack at a time.

1. **Phase A:** S0-01, S0-04, S1, leftover S2 (including S2-16/S2-17 if the Function exists after 8.4 P12).  
2. **Phase B:** Hybrid leftovers + follow-on UI (S3-01 does not Start; S4-02 merged Danger Zone; Players pin; console simple/full; usage-by-day).  
3. **Phase C:** jar-root continue (S6-02); Deployment Complete page. Do not greenfield.  
4. When Pass 3 is filled: **docs-only** triage only if the operator asks (`V1-Bug-Fix-Plan-Pass-3.md`).  
5. Repeat until [QA exit](V1-QA-Catalog.md#qa-exit-phase-85-done). Then Step 8.5.3.

Do **not** start Step **8.6.1** or Step **9.1** from this step. Do not rewrite the catalog each pass. Do not re-run Pass 1 chrome that already Passed unless follow-on changed those files.

**Test**

- Pass 3 Phase A: S0-01 recorded; S1 snapshot of the Pass 2 stack; leftover S2 filled.

**Done when:** Operator agrees a pass is ready for triage **or** QA exit is met (then 8.5.3). This step stays the QA executor across chats; living **NEXT** is Step **8.8** until 8.8 exits.

**Changelog:** 2026-08-21 — **PAUSED** again for Steps **8.7** (modpack tests) and **8.8** (operator notes). Do not start Pass 3, 8.6.1, or 9.1. 2026-08-20 — **PAUSED** for Step **8.4** follow-on. Pass 2 **closed early** (Modded greenfield + join; no triage). Next QA = Pass 3 after 8.4. Do not start 8.6.1 or 9.1. 2026-08-20 — **NEXT** (4.13 / R4 DONE). Do not start Pass 2 Phase A or `tofu destroy` until the operator says so. 2026-08-20 — **PAUSED** until Step **4.13** / robustness R1–R4 (itzg exclude lists). Do not start Pass 2 Phase A or `tofu destroy`. 2026-08-19 — **Pass 2 docs.** Scope + results files. Pass 1 complete (P1–P8 DONE). Do not start 8.6.1 or 9.1.

---



### Step 8.5.3 — QA exit

**Status:** TODO  
**Depends on:** 8.5.2 + bug-fix plans for each pass

**Do**

- Confirm catalog exit bar: no open Blocker/Major (or parked with operator OK); smoke IDs Pass.  
- Point V1 **NEXT** at Step **8.6.1** (CI-built ARM Function image). **Do not** skip 8.6 and point at 9.1. Update `AGENTS.md` NEXT lines if they still say 8.5.2.

**Done when:** Operator says pre-packaging QA is done. **NEXT** becomes **8.6.1**, not 9.1.

**Changelog:** *(empty)*

---



## Phase 8.6 — Spend-brake Function image (no Docker on the admin PC)

**Why this sits before the installer:** Oracle Functions need a **container image in the user’s OCIR** (same region, `GENERIC_ARM`). Today Setup’s `OcirFunctionPublisher` **builds** that image on the admin PC (`docker buildx linux/arm64`) and **skips** if Docker / Auth Token / `MCMANAGER_OCIR_USERNAME` is missing. That is a lab/developer path. PRODUCT-IDEAS is **one installer → one Manager**; users must not install Docker Desktop, the `fn` CLI, or use Cloud Shell / Code Editor to finish Setup.

**Decision (operator 2026-08-19):** **Pre-build `linux/arm64` in CI**, ship or Release-pull that artifact with the app, **copy** it into the user’s existing `mcmgr-fn/softstop` OCIR repo, then the existing second `tofu apply` with `function_image` set creates `mcmgr-fn-softstop` + Events. Cloud Shell remains **lab break-glass only** (it still uses Oracle’s Docker daemon; `oci fn` never builds an image). Do **not** point the live Function at a public GHCR/Docker Hub image — OCI Functions expect the image **in that tenancy’s OCIR**.

This phase is **required before any official release**. Do **not** start Step **9.1** until **8.6.1** is DONE.

**Interim (until this step ships):** from-source Setup may still skip the Function (TESTING S2-16). TESTING agents may still `fn build`/`push` under the [Product Functions blanket](#product-functions-on-testing-blanket). P3 on the Pass 1 bug-fix plan may use that TESTING path; it does **not** replace this product step.

### Step 8.6.1 — CI-built ARM image + Setup copy into OCIR

**Status:** TODO  
**Depends on:** Step 8.5.3 (QA exit). Do not start from 8.5.2 unless the operator asks to interleave.

**Read first**

- This phase heading  
- `PRODUCT-IDEAS.md` → **Delivery packaging** (Function image subsection)  
- `[Automated-Infrastructure-Deployment.md](Automated-Infrastructure-Deployment.md)` §10 Function row + §13 hybrid bundle/GitHub  
- `src/McManager.Core/Setup/OcirFunctionPublisher.cs`  
- `src/McManager.Core/Setup/SetupDeployOrchestrator.cs` (Function stage + skip-advances-stage)  
- `functions/shutdown_vm/` (`func.yaml` **0.0.12**, env-driven `INSTANCE_OCIDS`)  
- `[Guide.md](Guide.md)` Auth Token / Deploy paragraphs  

**Do**

- **CI:** build `linux/arm64` from `functions/shutdown_vm/` (same FDK/Python image Setup builds today). Apply the same env-rewrite so git placeholders never bake live OCIDs; Function config stays tofu-owned. Publish a **versioned** artifact (digest + `func.yaml` version) as a GitHub Release asset and/or GHCR image the app can copy.  
- **Setup:** copy that artifact into **the user’s** OCIR (`<region>.ocir.io/<namespace>/mcmgr-fn/softstop:<tag>`). Use a **bundled registry client** (`crane` / `oras`) or equivalent C# registry push — **not** Docker Desktop, **not** `fn`, **not** Cloud Shell. Stay on the existing Functions app + OCIR repo (no extra paid apps/repos).  
- **Auth Token** stays (OCIR login). **Derive** the OCIR username from Object Storage namespace + OCI config user. **Remove** the `MCMANAGER_OCIR_USERNAME` env requirement.  
- **Converge:** Deploy / repair must copy + set `function_image` + apply when the bundled digest **differs** from the live Function image, even if `apply_stage` is already `function` / `config_written`. Today a skipped push still marks Function complete — that must not ship. Config-only changes (VM1 OCID, bucket, lock key) remain tofu Function config, no new image.  
- **Updates:** later `shutdown_vm` (and the same channel for `reconcile_usage` when that Function is deployed) ship as a new image version with the app / GitHub Release; Manager copies and updates the Function. Users must not rebuild in Cloud Shell to pick up a lock-PUT fix.  
- **Guide + wizard:** Auth Token is needed for the brake; Docker / `fn` / Cloud Shell are **not**. Skipping the token still skips Function+Events (budget email can exist); do not imply the brake is installed.  
- Stay **$0**: existing 256 MB ARM Function, existing repo/app.

**Test**

- Setup on a machine **without** Docker/`fn` (Auth Token + API key only) copies the image, second apply creates Function + Events, synthetic invoke SoftStops **VM1 only** and PUTs the lock.  
- Deploy / repair with a newer bundled digest updates the live image without a greenfield redeploy.  
- Missing token → skip is explicit in the deploy log; Setup still finishes VMs.

**Done when:** Product path no longer requires Docker Desktop / `fn` / Cloud Shell / `MCMANAGER_OCIR_USERNAME`. Repair converges image digest. Guide matches. **Do not start 9.1** until this is DONE.

**Changelog:** *(empty)*

---



## Phase 9 — Packaging, updates, launch

Former MVP Phase **8–9**. Phases **1–7** are **DONE**. Phase **8** is **SKIPPED**. Phase **8.5** (pre-packaging QA) must **exit** before this phase. Step **8.6.1** (CI-built ARM Function image; no Docker on the admin PC) must be **DONE** before Step **9.1**. **Do not start Step 9.1** until [Step 8.5.3](#step-853--qa-exit) **and** [Step 8.6.1](#step-861--ci-built-arm-image--setup-copy-into-ocir) are DONE.

### Step 9.1 — Windows installer

**Status:** TODO  
**Depends on:** Phase 8.5 exit **and** Step **8.6.1**  

**Do**

- Single installer → one app (Setup integrated). Document code-signing strategy (purchase may be deferred); SmartScreen notes.  
- Bundle (or Release-pull) the **8.6.1** ARM Function image artifact the same way `infra/` is bundled — users must not need Docker Desktop to finish Setup.

**Test**

- Clean Windows user install; app runs; config locations documented.

**Done when:** Installer artifact builds reproducibly.

**Changelog:** *(empty)*

---



### Step 9.2 — GitHub Releases update check

**Status:** TODO  
**Depends on:** 9.1 (or can ship against `dotnet run` if the operator wants it earlier — still this step)

**Do**

- On launch: check latest GitHub Release; prompt + **release notes**. Honor the settings-gear update toggle from 6.1 if present. Offline dismiss works.  
- Function image updates (8.6.1) may ride the same Release channel; copying a newer digest into the user’s OCIR is Setup/repair, not a second installer product.

**Test**

- Mock or real release; prompt appears; dismiss works offline.

**Done when:** Update check ships in the app.

**Changelog:** *(empty)*

---



### Step 9.3 — Guide + README v1 pass

**Status:** TODO  
**Depends on:** Phases 1–7 feature work (Phase 8 skipped)

**Read first**

- `[Guide.md](Guide.md)`  
- `[README.md](../README.md)`

**Do**

- One consistency pass: Paper/Modded Setup, private allowlist (no public mode), spend-brake lock, **Function image = CI copy into OCIR (no Docker on the admin PC)**, installer vs run-from-source. Do not invent features.

**Test**

- Read-through as a first-time admin.

**Done when:** Guide matches shipped v1 behavior.

**Changelog:** *(empty)*

---



### Step 9.4 — Closed beta / dogfood

**Status:** TODO  

**Do**

- Dogfood with real friends on reserved IP; fix blockers only. Keep $0 discipline. Installer preferred if 9.1 exists; source is OK.

**Test**

- Multi-friend play; wake from cold; idle stop; at least one Modded or Paper path if those shipped.

**Done when:** No v1-blocking bugs open (or deferred with operator OK).

**Changelog:** *(empty)*

---



### Step 9.5 — V1 exit review

**Status:** TODO  

**Do**

- Tick v1 table in PRODUCT-IDEAS against this plan. Confirm **later** items were not scoped in. Update `README.md` + [`VM-Software.md`](VM-Software.md).  
- **Operator (not agents):** clean-room test in PRODUCT-IDEAS (new account + installer + Setup + $1 brake including **lock UX**). Prefer a local VM / spare PC. May incur ~$1–$2 residual — not on the long-lived lab tenancy unless spend is accepted.

**Done when:** Operator declares v1 ready to publish.

**Changelog:** *(empty)*

---



## Reference map


| Need                      | Where                                                                                                      |
| ------------------------- | ---------------------------------------------------------------------------------------------------------- |
| This checklist            | **this file**                                                                                              |
| MVP archive (Phases 0–7)  | [`archive/MVP-Implementation-Plan.md`](archive/MVP-Implementation-Plan.md)                                 |
| Happy-path user guide     | [`Guide.md`](Guide.md)                                                                                     |
| MVP / v1 / later intent   | [`PRODUCT-IDEAS.md`](PRODUCT-IDEAS.md)                                                                     |
| Game install mechanism    | [`Minecraft-Server-Deployment-Blueprint.md`](Minecraft-Server-Deployment-Blueprint.md) — **named §§ only** |
| Object Storage contracts  | [`Contracts-Object-Storage.md`](Contracts-Object-Storage.md)                                               |
| What’s live on VMs        | [`VM-Software.md`](VM-Software.md)                                                                         |
| Deploy pitfalls           | [`Agent-Deploy-Pitfalls.md`](Agent-Deploy-Pitfalls.md)                                                     |
| OCI API usage             | [`OCI-API-Usage.md`](OCI-API-Usage.md)                                                                     |
| Pre-packaging QA catalog  | [`V1-QA-Catalog.md`](V1-QA-Catalog.md)                                                                     |
| QA pass 1 results         | [`archive/V1-QA-Pass-1-Results.md`](archive/V1-QA-Pass-1-Results.md) (historical)                           |
| QA pass 2 scope           | [`archive/V1-QA-Pass-2-Scope.md`](archive/V1-QA-Pass-2-Scope.md)                                           |
| QA pass 2 results         | [`archive/V1-QA-Pass-2-Results.md`](archive/V1-QA-Pass-2-Results.md)                                       |
| Pass-2 follow-on (8.4)    | [`V1-Pass-2-Follow-On-Plan.md`](V1-Pass-2-Follow-On-Plan.md) (**DONE**)                                    |
| Modpack-test follow-on    | [`V1-Modpack-Test-Follow-On-Plan.md`](V1-Modpack-Test-Follow-On-Plan.md) (Step **8.7**)                     |
| Operator-notes follow-on  | [`V1-Operator-Notes-Follow-On-Plan.md`](V1-Operator-Notes-Follow-On-Plan.md) (Step **8.8**)                 |
| Informal pack tests       | [`Mod-Pack-Tests.md`](Mod-Pack-Tests.md) (input to 8.7; not a living NEXT)                                 |
| Modpack robustness (4.13) | [`archive/V1-Modpack-Robustness-Plan.md`](archive/V1-Modpack-Robustness-Plan.md)                           |
| Bug-fix plan template     | `[V1-Bug-Fix-Plan-TEMPLATE.md](V1-Bug-Fix-Plan-TEMPLATE.md)`                                               |


---



## Out of scope (do not implement under this plan)

- Players tab / Kick·Op·Ban  
- Start-from-Manager **progress checklist**  
- Maintenance / reserved-IP assignment + start-VM1-without-moving-play-IP  
- Connect an **additional** deployment / multi-profile switcher  
- Day-2 **change/replace modpack** **light swap** (full re-setup is v1 via 8.4)  
- Full per-day budget calendar  
- Quilt as a Setup entry point (detect-only is OK in 4.7)  
- **Deferred (ToS):** CurseForge **API** client-export import (project/file ID resolve). Server Files zip import (4.9) stays. Not rejected.  
- Purpur / Folia / hybrids  
- **Rejected:** in-app Modrinth/CurseForge/FTB **catalog / browse / search / download-a-pack** (users import a local file; this is not an after-v1 feature)  
- **Rejected:** public Minecraft / public-private toggle / blacklist (private allowlist only; this is not an after-v1 feature)  
- Interactive Java **PTY** console  
- macOS / Linux Manager  
- Event-driven door handback as primary  
- Silent OCI probing on startup  
- Public game access (`0.0.0.0/0` on 25565 / 22 / 8080)  
- Requiring **Docker Desktop**, the **`fn` CLI**, or **Cloud Shell / Code Editor** on the admin PC to install or update the spend-brake Function (product path is CI-built ARM image + copy into the user’s OCIR — Step **8.6.1**)  
- **Paid / spend mode** and paid OCI services / spend (far future; **not v1**. Phase 8 is SKIPPED.)

---



## Plan changelog


| Date       | Note                                                                                                                                                                                                                                                                                                                              |
| ---------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 2026-08-21 | **Step 8.8 P8 DONE.** Admin-PC icon variants (64×64 color + door greyscale overlays); Object Storage + door pull. Living **NEXT = Step 8.8 P9**. Do not start Pass 3, 8.6.1 CI, or 9.1. |
| 2026-08-21 | **Step 8.8 P7 DONE.** Setup Name and icon page (Vanilla/Paper/Modded Server defaults, no Oracle™); seeds `messages/chat.json`. Living **NEXT = Step 8.8 P8**. Do not start Pass 3, 8.6.1 CI, or 9.1. |
| 2026-08-21 | **Step 8.8 P6 DONE.** Auto compartment name (`mcmgr` / `mcmgr-2`…); Compartment wizard page removed. Living **NEXT = Step 8.8 P7**. Do not start Pass 3, 8.6.1 CI, or 9.1. |
| 2026-08-21 | **Step 8.8 P4 DONE.** Shared bottom progress dock for Setup Deploy and Change pack. Living **NEXT = Step 8.8 P5**. Do not start Pass 3, 8.6.1 CI, or 9.1. |
| 2026-08-21 | **Step 8.8 P3 DONE.** Compact lower-right toasts; Start/Stop progress dismiss on completion. Living **NEXT = Step 8.8 P4**. Do not start Pass 3, 8.6.1 CI, or 9.1. |
| 2026-08-21 | **Step 8.8 P2 DONE.** Stop tab-open toasts (backup list, infra meta load). Living **NEXT = Step 8.8 P3**. Do not start Pass 3, 8.6.1 CI, or 9.1. |
| 2026-08-21 | **Step 8.8 P1 DONE.** Console Simple filter stricter (RCON, journal, mixin/modloader boot). Living **NEXT = Step 8.8 P2**. Do not start Pass 3, 8.6.1 CI, or 9.1. |
| 2026-08-21 | **Step 8.7 DONE** (modpack-test P1–P5). High-unclear analyze warning (≥10 or ≥50%). Living **NEXT = Step 8.8 P1**. Do not start Pass 3, 8.6.1 CI, or 9.1. |
| 2026-08-21 | **Step 8.7 P4 DONE.** Java major on Setup + Change pack (`JAVA_MAJOR` → driver, Fabric 26.x → 25, clear Temurin fail). Living **NEXT = Step 8.7 P5**. Do not start 8.8, Pass 3, 8.6.1 CI, or 9.1. |
| 2026-08-21 | **Step 8.7 P3 DONE.** Fabric / `.mrpack` leftover client mods (overlay classes `loading-screen` / `konkrete` / `titlebar` / `flatlaf`; leftover in-jar client skip). Living **NEXT = Step 8.7 P4**. Do not start 8.8, Pass 3, 8.6.1 CI, or 9.1. |
| 2026-08-21 | **Step 8.7 P2 DONE.** Unstructured zip in-jar side detection (`InJarSideDetector`: Forge/Fabric side fields, client entrypoints, high-confidence common mixin targets). Living **NEXT = Step 8.7 P3**. Do not start 8.8, Pass 3, 8.6.1 CI, or 9.1. |
| 2026-08-21 | **Step 8.7 P1 DONE.** Crash-aware Setup/Change pack health (fail-fast crash-loop/FATAL, stop unit, capped journal). Living **NEXT = Step 8.7 P2**. Do not start 8.8, Pass 3, 8.6.1 CI, or 9.1. |
| 2026-08-21 | **Steps 8.7 + 8.8 inserted** (docs only). Informal Change pack tests 1/5 ([`Mod-Pack-Tests.md`](Mod-Pack-Tests.md)). Living **NEXT = Step 8.7 P1**. Then 8.8 operator notes. Pass 3 stays blocked until both exit. Do not start Pass 3, 8.6.1 CI, or 9.1. |
| 2026-08-20 | **Step 8.4 P13 DONE.** Setup prefers pre-built ARM Function tarball (`artifacts/mcmgr-fn-softstop-linux-arm64.tar` or next to the app); copy into OCIR without Docker. Fallback docker buildx / skip. **8.4 complete. NEXT = Step 8.5.2** Pass 3. Do not start Pass 3, 8.6.1 CI, or 9.1. |
| 2026-08-20 | **Step 8.4 P12 DONE.** TESTING spend-brake Function fill-in (OCIR `mcmgr-fn/softstop:setup` + Function + Events via OCI CLI; no tofu apply). Synthetic RESET skip + ACTUAL SoftStop VM1 / lock PUT. Artifact `artifacts/mcmgr-fn-softstop-linux-arm64.tar`. Living **NEXT = P13**. Do not start Pass 3, 8.6.1 CI, or 9.1. |
| 2026-08-20 | **Step 8.4 P11 DONE.** Server Management **Change pack** UI (analyze + confirm; world kept unless wipe). Live FO pack not replaced. Living **NEXT = P12**. Do not start Pass 3, 8.6.1 CI, or 9.1. |
| 2026-08-20 | **Step 8.4 P10 DONE.** Pack replace full re-setup (on-box prepare + Core `ReplacePackAsync`; keep world unless wipe). Living **NEXT = P11**. Do not start Pass 3, 8.6.1 CI, or 9.1. |
| 2026-08-20 | **Step 8.4 P9 DONE.** Manual / jar-root unclear-side may continue; `.mrpack` unclear still blocked. Living **NEXT = P10**. Do not start Pass 3, 8.6.1 CI, or 9.1. |
| 2026-08-20 | **Step 8.4 P8 DONE.** Usage **Detailed usage** expander (UTC days). Living **NEXT = P9**. Do not start Pass 3, 8.6.1 CI, or 9.1. |
| 2026-08-20 | **Step 8.4 P7 DONE.** Per-tab vertical scroll memory. Living **NEXT = P8**. Do not start Pass 3, 8.6.1 CI, or 9.1. |
| 2026-08-20 | **Step 8.4 P6 DONE.** Console Simple vs Full log toggle; RCON plumbing hidden in Simple. Living **NEXT = P7**. Do not start Pass 3, 8.6.1 CI, or 9.1. |
| 2026-08-20 | **Step 8.4 P5 DONE.** “game computer” → “server” (Setup + Manager + Guide). Living **NEXT = P6**. Do not start Pass 3, 8.6.1 CI, or 9.1. |
| 2026-08-20 | **Step 8.4 P4 DONE.** Window-locked dismissible action banners. Living **NEXT = P5**. Do not start Pass 3, 8.6.1 CI, or 9.1. |
| 2026-08-20 | **Step 8.4 P3 DONE.** Danger Zone merged into Advanced; idle only under that heading. Living **NEXT = P4**. Do not start Pass 3, 8.6.1 CI, or 9.1. |
| 2026-08-20 | **Step 8.4 P2 DONE.** Setup Deployment Complete + reserved play IP Copy. Living **NEXT = P3**. Do not start Pass 3, 8.6.1 CI, or 9.1. |
| 2026-08-20 | **Step 8.4 P1 DONE.** Start STOPPED gate; overlay unlock-only; Players pin. Living **NEXT = P2**. Do not start Pass 3, 8.6.1 CI, or 9.1. |
| 2026-08-20 | **Step 8.4 inserted** (docs only). [`V1-Pass-2-Follow-On-Plan.md`](V1-Pass-2-Follow-On-Plan.md) **NEXT = P1**. Pass 2 closed early (Modded greenfield + join). Step **8.5.2** paused until P13; then Pass 3. Pack replace (full re-setup) pulled into v1. Do not start Pass 3, 8.6.1 CI, or 9.1. |
| 2026-08-20 | **Step 4.13 R4 DONE.** Setup mis-declaration warning + optional GitHub Layer 1 refresh + Guide. **R1–R4 complete.** Living **NEXT = Step 8.5.2**. Do not start Pass 2 until the operator says so. Do not start 8.6.1 or 9.1. |
| 2026-08-20 | **Step 4.13 R3 DONE.** Manual / jar-root / CF-with-jars use the CF exclude list; jar-root installs to `mods/`; mixed CF still hard-blocks. Living **NEXT = R4**. Pass 2 still paused. Do not start 8.5.2, 8.6.1, or 9.1. |
| 2026-08-20 | **Step 4.13 R2 DONE.** `.mrpack` analyze/install applies itzg lists; mixed embedded+URL; override jars filtered. Living **NEXT = R3**. Pass 2 still paused. Do not start 8.5.2, 8.6.1, or 9.1. |
| 2026-08-20 | **Step 4.13 R1 DONE.** Core `ExcludeIncludeMatcher` + embedded itzg lists. Living **NEXT = R2**. Pass 2 still paused. Do not start 8.5.2, 8.6.1, or 9.1. |
| 2026-08-20 | **Step 4.13 inserted.** [`V1-Modpack-Robustness-Plan.md`](V1-Modpack-Robustness-Plan.md) **NEXT = R1**. Pass 2 / Step **8.5.2** paused until R4. itzg lists vendored in `docs/`. Layer 3 quarantine and CurseForge API still out. Do not start 8.6.1 or 9.1. |
| 2026-08-19 | **Phase 8.5** Pass 2 docs. [`V1-QA-Pass-2-Scope.md`](V1-QA-Pass-2-Scope.md) + [`V1-QA-Pass-2-Results.md`](V1-QA-Pass-2-Results.md). Pass 1 complete (Vanilla; S7-04 skipped; P1–P8 DONE). **NEXT = Pass 2 Phase A** (Delete + greenfield Modded, sample pack, 2/12). Do not start 8.6.1 or 9.1. |
| 2026-08-19 | **Phase 8.5** Pass 1 remaining Fails **confirmed**. Bug-fix **NEXT = P4**. P5 Major, P6 Major, P7 Minor, **P8** wipe auto-start (operator override vs PRODUCT-IDEAS leave-stopped). Timezone parked. **NEXT remains Step 8.5.2**. Do not start 8.6.1 or 9.1. |
| 2026-08-19 | **Phase 8.5** Pass 1 **S7 DONE**. S7-02/S7-03 Pass; S7-04 Skipped (no tofu this round). Restore: VM1 **STOPPED** A1.Flex **2/12**, play IP on door, idle on (15), lock absent, daily cap original. Next: docs-only triage of remaining Fails. **NEXT remains Step 8.5.2**. Do not start 8.6.1 or 9.1. |
| 2026-08-19 | **Phase 8.5** Pass 1 **S6 DONE**. Setup/Connect recorded (all Pass). Incomplete CurseForge zip warned but allowed continue (Additional problems). Restore: VM1 **STOPPED**, play IP on door, idle on (15), lock absent. Continue **S7** (optional; S7-04 needs explicit tofu). **NEXT remains Step 8.5.2**. Do not start 8.6.1 or 9.1. |
| 2026-08-19 | **Phase 8.5** Pass 1 **S5 DONE**. Play path recorded; S5-05 Fail (daily exhaust: Manager Start refused; MOTD lag/timezone; no chat on sudden cap). Restore: VM1 **STOPPED**, play IP on door, idle on (15), lock absent, daily cap original. Continue **S6**. **NEXT remains Step 8.5.2**. Do not start 8.6.1 or 9.1. |
| 2026-08-19 | **Phase 8.5** Pass 1 **S4 DONE**. Operator Manager UI recorded; S4-12 Fail (name/icon/MOTD not applied). Restore: VM1 **STOPPED**, play IP on door, idle on (15), lock absent. Continue **S5**. **NEXT remains Step 8.5.2**. Do not start 8.6.1 or 9.1. |
| 2026-08-19 | **Phase 8.6 added** (docs only): CI-built `linux/arm64` spend-brake Function image + Setup copy into the user’s OCIR. No Docker Desktop / `fn` / Cloud Shell on the admin PC. **Required before 9.1 / official release.** **NEXT remains Step 8.5.2.** Do not start 8.6.1 until QA exits (unless asked). |
| 2026-08-19 | **Phase 8.5** Pass 1 **paused after S2**. Bug-fix plan created: **P1** = OS-ISSUE-9 (ACPI STOPPING + UFW/firewalld/dbus). Do not start S3 or 9.1. **NEXT remains Step 8.5.2**. |
| 2026-08-18 | **Phase 8 SKIPPED.** Operator removed paid / spend mode from v1 (idea remains later / far future). **NEXT = Step 9.1**. Do not start 9.1 unless asked.                                                                                                                                                                            |
| 2026-08-18 | **Step 7.7 DONE.** Tracked Usage API 48h ledger reconcile Function (`functions/reconcile_usage/`): Ampere A1 daily_overrides + revision/flags bump; mocked tests; not deployed. **NEXT = Step 8.1**. Do not start 8.1 unless asked.                                                                                               |
| 2026-08-18 | **Step 7.6 DONE.** Server Management identity + automated chat: Object Storage `messages/chat.json` (+ optional 64×64 PNG); VM1 boot apply; no rich MOTD editor. **NEXT = Step 7.7**. Do not start 7.7 unless asked.                                                                                                              |
| 2026-08-18 | **Step 7.5 DONE.** Hybrid Console tab: SSH journalctl logs + localhost RCON Send (on-box secret, no Security List 25575). Not a PTY. **NEXT = Step 7.6**. Do not start 7.6 unless asked.                                                                                                                                          |
| 2026-08-18 | **Step 7.4 DONE.** Manager Object Storage writes for budget, meta (infra + flags on those publishes), and `ip/allowlist.json` use ETag `If-Match`; 412 is a clear conflict error. No `backups` dirty-flag category. **NEXT = Step 7.5**. Do not start 7.5 unless asked.                                                           |
| 2026-08-18 | **Step 7.3 DONE.** Connect existing blocks newer `infra_schema` / document version; extra-confirms older schema, legacy meta, or `stack_version` drift; hydrate refuses incompatible stacks. Auto-detect stays button-gated (no tag rediscovery). **NEXT = Step 7.4**. Do not start 7.4 unless asked.                             |
| 2026-08-18 | **Step 7.2 DONE.** Soften Usage / pin / idle MOTD copy for always-on-capable 2/12; 4/24 keeps scarce remaining-hours language; still meters. Live door MOTD needs redeploy. **NEXT = Step 7.3**. Do not start 7.3 unless asked.                                                                                                   |
| 2026-08-18 | **Step 7.1 DONE.** Danger Zone VM1 A1 Flex scale (2/12 or 4/24); STOPPED gate; playtime preview; local + budget/meta update; ledger intervals unchanged. No live resize. **NEXT = Step 7.2**. Do not start 7.2 unless asked.                                                                                                      |
| 2026-08-18 | **Step 6.3 DONE.** Oversized-world flag → bell + Server Management SSH live-world download (no OS PUT). DEBUG fixture. **NEXT = Step 7.1**. Do not start 7.1 unless asked.                                                                                                                                                        |
| 2026-08-18 | **Step 6.2 DONE.** Title-row bell + dismissible notification list; Core `NotificationCenter` channel (session-only); DEBUG fake post. **NEXT = Step 6.3**. Do not start 6.3 unless asked.                                                                                                                                         |
| 2026-08-18 | **Step 6.1 DONE.** Title-row gear (paths + update-check placeholder) and overflow (About, GitHub). No bell. Native OS chrome. **NEXT = Step 6.2**. Do not start 6.2 unless asked.                                                                                                                                                 |
| 2026-08-18 | **Step 5.1 DONE.** Server Management Modding: Vanilla/Paper empty state; inspect live `mods/` (SSH); **Download pack** = original `data/imported-packs/` archive (never zip VM1 `mods/`). Guide note. **NEXT = Step 6.1**. Do not start 6.1 unless asked.                                                                         |
| 2026-08-18 | **Step 4.12 DEFERRED** (ToS / key custody). No product CurseForge API key; keep Server Files zip via 4.9; client exports stay refused (Guide: Server Files or Modrinth `.mrpack`). **NEXT = Step 5.1**. Do not start 5.1 unless asked.                                                                                            |
| 2026-08-18 | **Step 4.11 DONE.** Dedicated client-pack notice in Setup (Game + Review) + Guide section: not playable until friends have the same exported pack; cannot rebuild from server `mods/`. **NEXT = Step 4.12**. Do not start 4.12 unless asked.                                                                                      |
| 2026-08-18 | **Step 4.10 DONE.** Setup Modded branch: Vanilla vs Modded radios; file picker + drop (no catalog); analyze/confirm; client-pack copy; bootstrap loader + server-side pack files. Guide note. **NEXT = Step 4.11**. Do not start 4.11 unless asked.                                                                               |
| 2026-08-18 | **Step 4.8 DONE.** Modrinth `.mrpack` server-side install: Core `MrpackInstaller` (plain GET of index URLs, strip client-only, fail on unclear side, overrides copy, hash verify); retain original under `data/imported-packs/`; DEBUG temp-dir probe. No catalog, no wizard. **NEXT = Step 4.9**. Do not start 4.9 unless asked. |
| 2026-08-18 | **Step 4.7 DONE.** Local `.mrpack` analyze: Core `MrpackAnalyzer` (no HTTP/install/catalog); `env.server` strip counts; tracked fixture `tests/fixtures/packs/fabric-strip.mrpack`; DEBUG Advanced probe. No wizard page. **NEXT = Step 4.8**. Do not start 4.8 unless asked.                                                     |
| 2026-08-18 | **Step 4.6 DONE.** Forge loader module: Core `promotions_slim.json` client + on-box installer; Vanilla jar first; 1.12.2 `single_jar` / 1.20.1 `argfile_tree`; `none_published`; no Setup Forge radio. **NEXT = Step 4.7**. Do not start 4.7 unless asked.                                                                        |
| 2026-08-18 | **Step 4.5 DONE.** NeoForge loader module: Core Maven XML client + on-box installer; `--installServer` argfile tree; `none_published`; refuse ≤1.20.1; generic unit `@user_jvm_args.txt @unix_args --nogui`. No Forge / pack import / Setup Modded radio. **NEXT = Step 4.6**. Do not start 4.6 unless asked.                     |
| 2026-08-18 | **Step 4.4 DONE.** Fabric loader module: Core meta client + on-box installer; three-axis `/server/jar` URL; `launcher_jar` + `none_published`; generic unit `nogui`. No pack import / Setup Modded radio. **NEXT = Step 4.5**. Do not start 4.5 unless asked.                                                                     |
| 2026-08-18 | **Step 4.3 DONE.** Setup Default Vanilla vs Optimized Vanilla (Paper): Mojang vs Fill v3 picker; bootstrap `DISTRIBUTION` to the 4.2 module; plan summary + infra `server_kind`. Guide note. **NEXT = Step 4.4**. Do not start 4.4 unless asked.                                                                                  |
| 2026-08-18 | **Step 4.2 DONE.** On-box Paper module (`bootstrap-paper.sh` + Fill v3 helper): STABLE jar + sha256 + §4.2 manifest + generic unit `--nogui`. No Setup UI. **NEXT = Step 4.3**. Do not start 4.3 unless asked.                                                                                                                    |
| 2026-08-18 | **Step 4.1 DONE.** Core Fill v3 client + offline fixtures (STABLE resolve, SHA-256 URL from JSON, no v2 URL builder). No Setup UI / on-box. **NEXT = Step 4.2**. Do not start 4.2 unless asked.                                                                                                                                   |
| 2026-08-18 | **Step 3.4 DONE.** Manager private-only: no public toggle/blacklist UI; no `ip/mode.json` writer; planner keeps CIDR allowlist and strips leftover world-open Minecraft. TESTING SL was already private. **NEXT = Step 4.1**. Do not start 4.1 unless asked.                                                                      |
| 2026-08-18 | **Public/blacklist rejected.** Step **3.3 CANCELLED**. Steps **3.1–3.2 WITHDRAWN**. Docs updated. **NEXT = Step 3.4** (remove 3.1/3.2 code; keep CIDR). Do not start 3.4 unless asked.                                                                                                                                            |
| 2026-08-18 | In-app mod/modpack browser marked **rejected** (not after-v1). Users import a local pack file only. **NEXT remains Step 3.3.**                                                                                                                                                                                                    |
| 2026-08-18 | Operator-local sample packs: gitignored `data/sample-packs/` + tracked `[Sample-Packs.md](Sample-Packs.md)`. CI stays on `tests/fixtures/`. Agents missing a pack format **pause and ask the operator**. **NEXT remains Step 3.3.**                                                                                               |
| 2026-08-18 | **On-box SoT moved** into this repo: `door_vm/`, `vm_agent/`, `functions/shutdown_vm/`, plus `Agent-Deploy-Pitfalls.md`. Lab trees are pointer READMEs. Setup `ProductPaths` no longer requires a lab checkout. **NEXT remains Step 3.3.**                                                                                   |
| 2026-08-17 | **Step 3.2 DONE.** One-list rewrite: public Minecraft `0.0.0.0/0` TCP/UDP; SSH never world-open; private restores allowlist; 3.1 confirm before public apply. Planner unit tests; no live SL apply. **NEXT = Step 3.3**. Do not start 3.3 unless asked.                                                                           |
| 2026-08-17 | **Step 3.1 DONE.** Persist `private`/`public` + blacklist locally (`friends.local.json`) and `ip/mode.json` when present; public confirm; Apply-public stub; SL unchanged. **NEXT = Step 3.2**. Do not start 3.2 unless asked.                                                                                                    |
| 2026-08-17 | **Step 2.4 DONE.** Manager full-window spend-brake overlay; exact typed confirm; park-IP + DELETE lock + OS-refresh + Wake (gates still apply). Core `SpendBrakeLockUx` tests. **NEXT = Step 3.1**. Do not start 3.1 unless asked.                                                                                                |
| 2026-08-17 | **Step 2.3 DONE.** Door GETs `meta/spend-brake-triggered.json` on wake pull; presence refuses START (`SPEND_BRAKE` MOTD/kick distinct from daily). Fail closed on non-404 GET. No extra Python. Live door still needs redeploy. **NEXT = Step 2.4**. Do not start 2.4 unless asked.                                               |
| 2026-08-17 | **Step 2.2 DONE.** Tracked Function PUTs `meta/spend-brake-triggered.json` on real threshold alerts (ignore RESET); SoftStop **VM1 only**; door Micro left running (Always Free AMD Micro ≠ Ampere hours). HCL stop-list default VM1 + OS config. No `fn push`. **NEXT = Step 2.3**. Do not start 2.3 unless asked.               |
| 2026-08-17 | **Step 2.1 DONE.** Frozen Object Storage lock: `meta/spend-brake-triggered.json` v1; Function writer, Manager-only DELETE clearer, door+Manager readers; fail closed. Core DTO + get/put/delete. No live Function deploy. **NEXT = Step 2.2**. Do not start 2.2 unless asked.                                                     |
| 2026-08-17 | **Step 1.3 DONE.** Wipe world: Server Management button + confirm; SSH deletes only `world_path` under `/opt/mcmgr/server/`; Minecraft stopped first; Object Storage backups / mods / `server.properties` untouched. **NEXT = Step 2.1**. Do not start 2.1 unless asked.                                                          |
| 2026-08-17 | **Step 1.2 DONE.** Allowlist CIDR: Add-IP Advanced field; persist prefix locally + `ip/allowlist.json` when present; Minecraft SL rules use the CIDR; SSH/door stay `/32` except own admin entry; reject `/0`–`/8`. **NEXT = Step 1.3**. Do not start 1.3 unless asked.                                                           |
| 2026-08-17 | **Step 1.1 DONE.** Hybrid **Advanced** vs **Danger Zone** tabs (idle-disable + Delete infrastructure only on Danger Zone; timeout stays on Advanced). **NEXT = Step 1.2**. Do not start 1.2 unless asked.                                                                                                                         |
| 2026-08-17 | VM1 may be STOPPED: START if needed, **disable idle** while working, **re-enable idle** when finished. If already RUNNING, confirm idle is off first. OS-ISSUE-7: re-disable after Minecraft start.                                                                                                                               |
| 2026-08-17 | Test-stack access: OCI CLI/API with `TESTING` (never `DEFAULT`); SSH both test VMs with `mcmgr_ed25519_20260817_125552`; $0 only; mirror VM/cloud edits into local SoT. `tofu apply` / OCIR still operator-authorized.                                                                                                            |
| 2026-08-17 | Created. Operator chose **v1 features before packaging**. Manager UX first (Phase 1), then spend-brake, IP mode, Setup game types, remaining v1, paid mode last, packaging last. **NEXT = Step 1.1**. Do not start 1.1 unless asked.                                                                                              |


