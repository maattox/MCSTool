# V1 Implementation Plan

**Status:** Living checklist for agents and the operator.  
**Product intent authority:** lab [`PRODUCT-IDEAS.md`](../../OCI-mc-server-manager/PRODUCT-IDEAS.md) (v1 table). When this plan and PRODUCT-IDEAS disagree on *what* v1 means, **PRODUCT-IDEAS wins** — update this file.  
**MVP archive:** [`MVP-Implementation-Plan.md`](MVP-Implementation-Plan.md) — Phases **0–7 DONE**. Packaging (old Phase 8 / Step 8.1) is **deferred** to [Phase 9](#phase-9--packaging-updates-launch) of **this** file.  
**Suggested narrative:** lab [`docs/Development-Steps.md`](../../OCI-mc-server-manager/docs/Development-Steps.md).  
**Live infra docs:** lab repo (`Infrastructure-Information.md`, `docs/VM-Software.md`).  
**On-box SoT:** **this repo** (`door_vm/`, `vm_agent/`, `functions/shutdown_vm/`, `onbox/mcmgr/`).  
**Code SoT for Manager:** **this repo** (`OCI-mc-server`).

**Cost rule:** keep OCI spend at **$0** (Always Free–eligible) unless the operator explicitly accepts paid changes. **Paid / spend mode is Phase 8** — last product feature, not a side quest.

**OCI API:** follow [`OCI-API-Usage.md`](OCI-API-Usage.md) — **429** exponential backoff (≤60s), lifecycle waiters (≤30s between polls, ~20 min), list pagination, modest Object Storage chatter (~50k requests/month). Prefer Get-by-OCID from local config over chatty List discovery.

**Execution order (operator 2026-08-17):** finish **v1 features** before Windows installer, GitHub Releases, public launch. Informal dogfood with friends (run from source) is allowed any time; it is not a plan step.

---

## How agents must use this file

1. **Do not read this whole file in one session.** Read [this protocol](#how-agents-must-use-this-file), the [Progress dashboard](#progress-dashboard), and **only the single NEXT step body**.  
2. Implement **only** that NEXT step. Do not start “the rest of the phase.”  
3. After finishing:
   - Mark the step **DONE**, set the following step to **NEXT**, add date + short notes on the step changelog line **and** the [Plan changelog](#plan-changelog).
   - **Stop.** Do not start the next large step unless the operator says to continue.
4. In the chat reply: what was done, how to test, what the next step will be, ask whether to continue / pause / adjust.
5. **Never create git commits** (operator commits in Visual Studio). You may suggest a commit message.
6. Do **not** implement **after v1** / **later** PRODUCT-IDEAS items (Players tab, start checklist, maintenance IP, multi-deploy, pack replace, Quilt Setup entry, Purpur, PTY console, macOS/Linux Manager). An **in-app mod/modpack browser** is **rejected** (not after-v1) — users import a local pack file only; do not build it. **Public Minecraft / public-private toggle / blacklist** is **rejected** (not after-v1) — private allowlist only; do not rebuild it.
7. Do **not** put Manager UI in the lab repo. On-box source (`door_vm/`, `vm_agent/`, `functions/shutdown_vm/`) lives **in this repo**. Lab changes are OK for lab docs / Python Manager only. Phase B (Blazor Hybrid) is **DONE**; do not re-open Avalonia.
8. **Fix the product path, not only the test VM.** If you change a test VM or a **TESTING** cloud resource, make the **same** change in the local deployment SoT in the same session (`onbox/mcmgr/`, `infra/`, `door_vm/`, `vm_agent/`, `functions/shutdown_vm/`, Manager/Setup code here). The next greenfield Setup must pick it up. Patching only the live test instance is not done.
9. **`ubuntu` Permission denied** — `sudo` or fix owner/mode ([`docs/Agent-Deploy-Pitfalls.md`](Agent-Deploy-Pitfalls.md)).
10. **UI sketches are not locked; operator notes override.** For UI-design work, use or offer `find-skills` unless already asked. **NuGet is allowed** on `McManager.Hybrid`. Do not add Avalonia packages. Keep OCI SDK on Core.
11. If this step changes a user-visible Setup or manage path, add a **short** paragraph to [`Guide.md`](Guide.md) in the same step. Do not rewrite the whole Guide.
12. **Test-stack OCI + SSH is allowed** for V1 work — see [Test stack access](#test-stack-access-oci--ssh). Stay at **$0**. Do **not** use the `DEFAULT` OCI profile or the live Forge lab. If you use VM1, start it when STOPPED, **disable idle** while you work, and **re-enable idle** when you finish.

### Context budget (256K window)

Each step is sized for **one** agent session (~256K tokens) after workspace rules.

- **Read first** lists are a **hard cap**, not a suggestion. Do not open the full [`Minecraft-Server-Deployment-Blueprint.md`](Minecraft-Server-Deployment-Blueprint.md), the full MVP plan, or every Hybrid tab “for context.”
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

| Item | Value |
|------|--------|
| OCI config | `%USERPROFILE%\.oci\config` (normal location) |
| OCI profile | **`TESTING` only** — test tenancy. **Never `DEFAULT`** (that is the operator’s other / live Forge lab tenancy). |
| OCI CLI | Always pass `--profile TESTING` (or the equivalent .NET `OciSession` profile from local config). Example: `oci compute instance get --profile TESTING --instance-id <from local config>` |
| SSH user | `ubuntu` |
| SSH private key | `%USERPROFILE%\.ssh\mcmgr_ed25519_20260817_125552` — **same key for both** test VMs |
| Hosts / OCIDs / IPs | Gitignored `data/config.local.json` (lab private markdown only if needed). **Do not copy live OCIDs, IPs, Auth Tokens, or key material into tracked docs or chat dumps.** |
| `ubuntu` permissions | Recurring `Permission denied` on `/etc/mcmgr`, `/etc/mccontrol/oci.env`, systemd units. Use `sudo` or fix mode/owner. Read [`docs/Agent-Deploy-Pitfalls.md`](Agent-Deploy-Pitfalls.md) before SSH deploy edits. |

**Allowed**

- OCI SDK / REST / CLI against the **TESTING** tenancy for the current step (Compute get/start/stop, VNIC / reserved IP, Security List, Object Storage, IAM reads, etc.), with [`OCI-API-Usage.md`](OCI-API-Usage.md) 429 backoff and modest Object Storage chatter.
- SSH to **both** test VMs (VM1 and door). Anything on-box the step needs: test, install or edit scripts/services, `systemctl`, journals, firewalld, Minecraft/door paths, redeploy from the local SoT.
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

More idle copy-paste: lab [`docs/Operator-Troubleshooting.md`](../../OCI-mc-server-manager/docs/Operator-Troubleshooting.md) (VM1 idle agent).

**Not allowed**

- `tofu apply` / `tofu plan` / `tofu destroy` / deleting the compartment / `docker push` / `fn push` to OCIR unless the operator **explicitly** authorizes that command in the session.
- Using **`DEFAULT`**, touching the live **Forge lab** tenancy, or SSH with any key other than the one named above.
- Opening `0.0.0.0/0` on Minecraft, SSH, or door admin.
- Committing secrets, filled `oci.env`, or live OCIDs.
- Wizard Deploy that would `tofu apply` (keep `MCMANAGER_TOFU_DRY_RUN=1` unless the operator authorizes a real apply).

`dotnet build`, `tofu validate` in `infra/`, and dry-run Setup remain always OK.

### Operator prompt (copy-paste for a new agent)

```text
Read docs/V1-Implementation-Plan.md in OCI-mc-server. Implement only the step marked NEXT.
MVP Phases 0–7 are DONE. Packaging (old Step 8.1) is deferred until V1 Phase 9. Phase B (Blazor Hybrid UI) is DONE — do not re-open Avalonia.
You MAY use OCI CLI/API with profile TESTING (not DEFAULT) and SSH both test VMs with %USERPROFILE%\.ssh\mcmgr_ed25519_20260817_125552. Stay at $0. If you change a test VM or TESTING cloud resource, make the same change in the local deployment SoT (onbox/, infra/, door_vm/, vm_agent/, functions/shutdown_vm/).
If you need VM1 and it is STOPPED, START it, then disable the idle agent so it does not SoftStop while you work. If VM1 is already RUNNING, confirm idle is off before other work. When you finish, turn the idle agent back on. Minecraft boot force-enables idle (OS-ISSUE-7) — disable again after a game start.
When done: update the V1 plan statuses, stop, tell me what you did, how to test, what’s next, and ask if I want to continue or adjust.
Do not commit. Do not start the following large step unless I say so.
Do not tofu apply / OCIR push unless I explicitly authorize it.
Prompt sequential steps in Agent mode (not Plan mode). Use Build in Parallel / Plan mode only if the NEXT step is marked PARALLEL-OK. Include this same Agent-vs-Plan instruction in the prompt you give the operator for the following step.
```

---

## V1 goal (from PRODUCT-IDEAS)

> Flexible product on the same Always Free doorbell: **private allowlist only**, CIDR prefixes, spend-brake lock, Paper + file-imported modpacks, Danger Zone isolated from power-user Advanced, then **one** installer and GitHub updates.

**Explicitly out of this plan (after v1 / later):** Players tab, Start progress checklist, maintenance / reserved-IP controls, multi-deploy profiles, change/replace modpack, full per-day budget calendar, Quilt as a Setup entry point, Purpur/Folia, interactive PTY console, macOS/Linux Manager.

**Rejected (will not be implemented, not after-v1):** in-app mod / modpack browser (browse, search, trending, download-a-pack, pick-by-name/URL/ID). Users create or download pack files themselves and select them in Setup or Manager.

**Rejected (will not be implemented, not after-v1):** public Minecraft (`0.0.0.0/0`), a public/private Manager toggle, and a blacklist. Private allowlist only (CIDR from Step 1.2 stays).

**Already shipped in MVP (do not rebuild):** Delete-infrastructure UI (typed `confirm`); Troubleshooting one-shots; Vanilla Setup; Connect-existing; Hybrid WinExe.

---

## Progress dashboard

| Phase | Focus | Status |
|-------|--------|--------|
| **1** | Manager shell (Advanced/Danger split, CIDR, wipe world) | **DONE** |
| **2** | $1 spend-brake lock (Function flag, door, Manager overlay) | **DONE** |
| **3** | Remove public/blacklist (was IP Management public mode) | **DONE** |
| **4** | Setup game types (Paper, loaders, pack import) | **NEXT** = Step **4.2** |
| **5** | Server Management modding inspect + re-download pack | TODO |
| **6** | Top-bar chrome + oversized-world SSH UX | TODO |
| **7** | Remaining v1 (resize, console, storage, Connect version) | TODO |
| **8** | Paid / spend mode (**last** product feature) | TODO |
| **9** | Packaging, updates, launch (old MVP Phase 8–9) | TODO — **do not start** until Phases 1–8 are DONE or the operator skips 8 |

**Current NEXT step:** [Step 4.2](#step-42--on-box-paper-installer-module). **Do not start Step 4.2** until the operator asks.

---

## Phase 1 — Manager shell

**Why first:** small, testable Hybrid-only slices; no OpenTofu; no live Function deploy. Unblocks later Danger Zone features (resize, paid mode, spend-brake recovery).

### Step 1.1 — Split Advanced vs Danger Zone

**Status:** DONE  
**Depends on:** MVP Phase B (DONE)

**Read first**

- Lab `PRODUCT-IDEAS.md` → heading **Advanced vs Danger Zone (v1)** only  
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

- Lab `PRODUCT-IDEAS.md` → **Allowlist CIDR ranges (v1)** only  
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

- Lab `PRODUCT-IDEAS.md` → **Wipe world (v1)** only  
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

**Changelog:** 2026-08-17 — Server Management **Wipe world** next to Download latest: confirm popup (live save only; cloud backups / mods / `server.properties` kept; Minecraft stopped then left stopped). SSH wipe via `WorldWipe` path guard (`/opt/mcmgr/server/<world>` only). Core unit tests for path construction. Follow-up: wipe no longer calls `repair-permissions.sh` (SETUP-ISSUE-8 same-file `cp`); layout helper skips copy when src is dest.

---

## Phase 2 — $1 spend-brake lock

Split so Function, door, and Manager each get their own window.

### Step 2.1 — Lock-flag Object Storage contract

**Status:** DONE  
**Depends on:** 1.1 (Danger Zone exists; overlay comes in 2.4)

**Read first**

- Lab `PRODUCT-IDEAS.md` → **$1 spend-brake lock (v1)** only  
- [`Contracts-Object-Storage.md`](Contracts-Object-Storage.md) — existing `meta/` objects + reserved spend-brake row  
- Lab `functions/shutdown_vm/` README (placeholders only)

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
- Lab `PRODUCT-IDEAS.md` → **What the Function must do (v1)** only  
- Lab `functions/shutdown_vm/` source (product copy)  
- Oracle Always Free page (Micro vs Ampere) — re-read; **prefer leaving the door running** if AMD Micro does not accrue Ampere OCPU-hour spend

**Do**

- On a real threshold alert (ignore budget RESET): SoftStop **VM1**; **PUT** the lock object.  
- Product decision in the same step: stop door or not. Default recommendation: **do not SoftStop VM2** if Micro stays Always Free; document the choice in the Function README + lab `Infrastructure-Information.md` (placeholders).  
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

- Lab `docs/Agent-Deploy-Pitfalls.md` (before any door script/C change)  
- `Contracts-Object-Storage.md` spend-brake section  
- Lab `door_vm/src/control.c` — budget-gate / wake paths only  
- Lab `door_vm/scripts/` wake-pull script(s) that already read budget/ledger (open only those)

**Do**

- Door must **read** the lock flag (same poll discipline as the budget gate) and **never START VM1** while it is set.  
- MOTD/kick: monthly spend brake fired; admin must use Manager after a new calendar month. Distinct from daily-budget-exhausted copy.  
- `HOME` default on systemd scripts; no Python on the Micro.

**Test**

- `make test` in lab `door_vm/` (MOTD/kick + `SPEND_BRAKE` state names). This Windows session had no gcc; run that in WSL/Linux.
- After **redeploying the door** from `door_vm/` (Testing2 Phase 3+4 or Setup `install.sh`): SSH the door as `ubuntu`, then `sudo bash /opt/mccontrol/oci/pull_os_budget.sh --force` — expect `SPEND_BRAKE_LOCK=0` while the object is absent (wake must not DEGRADE).
- Optional refuse check (delete the object when done): PUT a tiny `meta/spend-brake-triggered.json`, `POST /api/wake`, confirm VM1 stays STOPPED and MOTD/kick contains `MONTHLY SPEND BRAKE FIRED` (not `DAILY BUDGET`). Then DELETE the object and `/api/os-refresh`.

**Done when:** Wake path refuses START when the flag is present.

**Changelog:** 2026-08-17 — Door wake GETs `meta/spend-brake-triggered.json` on every `pull_os_budget.sh` (404 = unlocked; other GET errors fail closed). Presence → `SPEND_BRAKE`; **never** `start_vm1.sh`. MOTD/kick: `MONTHLY SPEND BRAKE FIRED — the admin must use Manager after a new calendar month.` (distinct from daily). Reconcile parks IP like idle. `make test` MOTD/state. Live door needs redeploy from `door_vm/`.

---

### Step 2.4 — Manager full-window lock UX

**Status:** DONE  
**Depends on:** 2.1, 1.1

**Read first**

- Lab `PRODUCT-IDEAS.md` → **Manager UX when the flag is set** only (keep the typed sentence exact)  
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

- Lab `PRODUCT-IDEAS.md` → **Rejected** table (public/blacklist row) + heading **IP Management (v1)** only  
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
- Do **not** start Phase 4. Lab Python seed of `ip/mode.json` may stay as an unused leftover unless a one-line comment is cheap.

**Test**

- `dotnet build src/McManager.slnx` and Core tests that still apply (planner private + CIDR; friends file without requiring mode/blacklist).  
- Whitelist tab: add `/32` and CIDR, Save; no public toggle; no blacklist UI.

**Done when:** Manager is private-only; no public SL code path; CIDR allowlist still works; `ip/mode.json` is not a live writer.

**Changelog:** 2026-08-18 — Removed Hybrid public/private toggle, public notices, and Blacklist panel. Save strips leftover `mode`/`blacklist` keys; no `ip/mode.json` PUT. Planner kept for private CIDR/`/32` allowlist; public `0.0.0.0/0` Minecraft branch deleted. TESTING GetSecurityList: no world-open 25565 (no apply). **NEXT = Step 4.1**. Do not start 4.1 unless asked.

---

## Phase 4 — Setup game types

**Order:** Paper (Optimized Vanilla) first, then loader modules, then pack import. **No in-app catalog** (blueprint §2.4). Quilt = detected loader value only, not a Setup radio. CurseForge is its **own** step (4.12).

Each installer step: Core metadata client + `onbox/mcmgr/` module + generic unit/manifest — **one platform per step**.

**Sample packs:** CI uses tiny tracked fixtures under `tests/fixtures/` (blueprint §15). Operator-local real/homemade archives live in gitignored `data/sample-packs/` — see [`Sample-Packs.md`](Sample-Packs.md) (gotchas + which file for 4.7–4.12). If a needed format/loader is missing, **pause and ask the operator to download it**. **Do not** add an in-app pack browser (that feature is **rejected**).

### Step 4.1 — Paper Fill v3 client + fixtures (Core only)

**Status:** DONE  
**Depends on:** Phase 1 (no hard code dep)

**Read first**

- Blueprint **§17** only  
- Lab `PRODUCT-IDEAS.md` → **Setup game types (v1)** → Vanilla branch / Paper bullets  
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

**Status:** NEXT  
**Depends on:** 4.1

**Read first**

- Blueprint **§17**, **§6.3** (jar launch), **§13.2** (SSH modules)  
- Lab `docs/Agent-Deploy-Pitfalls.md`  
- Existing Vanilla on-box installer under `onbox/mcmgr/` (open the Vanilla module + shared layout/unit generator only)

**Do**

- Installer module: download Paper jar via URLs the Manager already resolved (or Fill v3 from the module if MVP Vanilla already downloads on-box — match that pattern). Write `game-manifest.json` (`distribution`/Paper fields per §4.2 fixture). Generic systemd unit — do not add a Paper-specific unit file.  
- No wizard UI yet.

**Test**

- Script/layout unit tests if present; `tofu validate` not required. Test-stack SSH is OK ([Test stack access](#test-stack-access-oci--ssh)); do **not** SSH the live Forge lab.

**Done when:** On-box Paper module exists and writes a valid manifest + unit args for a jar.

**Changelog:** _(empty)_

---

### Step 4.3 — Setup wizard: Default vs Optimized Vanilla

**Status:** TODO  
**Depends on:** 4.1, 4.2

**Read first**

- Lab `PRODUCT-IDEAS.md` → **Setup game types (v1)** diagram + Vanilla branch  
- `src/McManager.Hybrid/Components/Setup/SetupWizard.razor`  
- `src/McManager.Hybrid/ViewModels/SetupWizardViewModel.cs`  
- Vanilla version-picker code (open only that)

**Do**

- After server type **Vanilla**: **Default Vanilla** (unchanged Mojang path) vs **Optimized Vanilla** (Paper). Then the version picker (Mojang list vs Paper’s list; hide versions Paper does not build).  
- Wire bootstrap to the 4.2 module. Short Guide.md note.

**Test**

- `dotnet build`; wizard dry-run (`MCMANAGER_TOFU_DRY_RUN=1`) shows both vanilla paths. No live apply.

**Done when:** Operator can choose Paper in Setup UI; Default Vanilla still works.

**Changelog:** _(empty)_

---

### Step 4.4 — Fabric installer module

**Status:** TODO  
**Depends on:** 4.2 (shared module shape)

**Read first**

- Blueprint **§18** only  
- Shared on-box installer interface from 4.2 (not the Paper Fill client)

**Do**

- Fabric meta client + on-box module + manifest `loader: fabric`. Single runnable jar launch shape. No pack import yet. No Setup Modded radio yet.

**Test**

- Fixtures/tests for meta resolve; `dotnet build`.

**Done when:** Fabric can be installed as a loader module in isolation.

**Changelog:** _(empty)_

---

### Step 4.5 — NeoForge installer module

**Status:** TODO  
**Depends on:** 4.4 (module pattern)

**Read first**

- Blueprint **§19** only (argfile / XML metadata)  
- Shared module interface; **§6.3** argfile rendering if not already implemented

**Do**

- NeoForge server installer module + manifest. Steer “current version” modded to NeoForge (UI copy in 4.11). No Forge yet.

**Test**

- Fixtures; `dotnet build`.

**Done when:** NeoForge module writes manifest + unit args (argfile).

**Changelog:** _(empty)_

---

### Step 4.6 — Forge installer module (legacy packs)

**Status:** TODO  
**Depends on:** 4.5

**Read first**

- Blueprint **§20** only  
- NeoForge module from 4.5 (shared argfile mechanics)

**Do**

- Forge module for packs that **declare Forge** (esp. 1.12.2-era and 1.20.1). Do **not** present Forge as a current-version alternative to NeoForge in Setup.

**Test**

- Fixtures for a pinned legacy version; `dotnet build`.

**Done when:** Forge module exists; UI does not offer Forge vs NeoForge as equal current choices.

**Changelog:** _(empty)_

---

### Step 4.7 — Modrinth `.mrpack` analyze (Manager, no install)

**Status:** TODO  
**Depends on:** 4.4–4.6 (need to *detect* loader)

**Read first**

- Blueprint **§22** and **§2.4** only  
- Lab `PRODUCT-IDEAS.md` → **Modded branch** (file picker / no catalog)  
- [`Sample-Packs.md`](Sample-Packs.md) (operator-local archives; gotcha: FO/`env.server`)

**Do**

- Parse an uploaded `.mrpack` locally: pack name, Minecraft version, loader + version, Java, file counts, `env.server` vs client-only.  
- No HTTP catalog/search. No CurseForge. No wizard page yet if cheaper as a Core API + small test harness — a hidden/dev button is OK; 4.11 wires the wizard.

**Test**

- Offline fixture `.mrpack` (tiny) in repo tests (`tests/fixtures/`). Optional: `data/sample-packs/homemade/fabric-strip.mrpack` on this PC (correct Sodium `unsupported` tag — do **not** use Fabulously Optimized / OptiFine for Fabric as the strip test).

**Done when:** Analyzer returns a confirmable summary without installing.

**Changelog:** _(empty)_

---

### Step 4.8 — Modrinth `.mrpack` install (server-side only)

**Status:** TODO  
**Depends on:** 4.7 + matching loader module

**Read first**

- Blueprint **§22**, **§25** (client-pack communication — install a copy reminder / keep original archive on the admin PC)  
- On-box pack converge-not-layer notes in **§12** only as needed (do not implement pack *replace*)

**Do**

- Download files using URLs **already in** the mrpack index (plain GET, not a Modrinth browse API). Strip client-only (`env.server` / side). Keep the original archive in Manager local data for later re-download (Phase 5).  
- Fail/warn loudly when side is unclear.

**Test**

- Fixture pack install into a temp dir (no live VM required). Prefer `data/sample-packs/homemade/fabric-strip.mrpack` (real CDN URLs) when that folder exists; see [`Sample-Packs.md`](Sample-Packs.md).

**Done when:** Server-side mods land; client-only jars do not; original archive is retained locally.

**Changelog:** _(empty)_

---

### Step 4.9 — Manual server-pack zip import

**Status:** TODO  
**Depends on:** 4.8

**Read first**

- Blueprint **§24** only

**Do**

- File picker for a generic server pack zip (mods/ + loader already present or documented layout). Same no-catalog rule. Same client-only strip where metadata exists.

**Test**

- Fixture zip; `dotnet build`. Operator-local: `data/sample-packs/homemade/manual-server.zip` ([`Sample-Packs.md`](Sample-Packs.md)). If a CurseForge Server Files zip is needed and missing, pause and ask the operator.

**Done when:** Manual zip is a second import adapter, not a rewrite of 4.8.

**Changelog:** _(empty)_

---

### Step 4.10 — Setup wizard Modded branch UI

**Status:** TODO  
**Depends on:** 4.7–4.9

**Read first**

- Lab `PRODUCT-IDEAS.md` → **Modded branch**  
- Setup wizard files from 4.3  
- Analyzer/install APIs from 4.7–4.9

**Do**

- Server type **Modded**: file picker / drag-and-drop only. Analyzing progress. Confirm summary. On confirm, continue wizard + bootstrap.  
- Required client-pack copy: tell the admin friends must install the **same exported pack** they uploaded (keep file / re-download later). Short Guide.md note.  
- **No** pack name/URL/ID search box.

**Test**

- `dotnet build`; dry-run wizard. No live apply.

**Done when:** Modded Setup path is selectable and confirmable without a catalog.

**Changelog:** _(empty)_

---

### Step 4.11 — Client-pack communication polish + Guide

**Status:** TODO  
**Depends on:** 4.10

**Read first**

- Blueprint **§25** only  
- [`Guide.md`](Guide.md) — existing Setup / play sections

**Do**

- Dedicated, novice-readable copy in wizard + Guide: server is not playable for friends until they have the client pack; product cannot reconstruct a client pack from `mods/` on VM1.

**Test**

- Read the Guide section; wizard strings exist.

**Done when:** First-time Modded Setup cannot miss the client-pack requirement.

**Changelog:** _(empty)_

---

### Step 4.12 — CurseForge pack import (gated)

**Status:** TODO  
**Depends on:** 4.10

**Read first**

- Blueprint **§23** only (ToS, API key custody, no cache/proxy, no competing catalog)  
- Lab `PRODUCT-IDEAS.md` → Modded branch CurseForge row

**Do**

- Import a **user-supplied** CurseForge export zip. Use the API **only** to resolve download URLs for files **already named** in that manifest.  
- Stop and ask if key custody / ToS still blocks shipping. Do not embed a user-extractable API key on VM1.  
- Client-only heuristic (CurseForge has no per-file server marker) — warn loudly.

**Test**

- Fixture manifest resolve with a mocked API; no catalog UI. Operator-local synthetic zip + real FO export: [`Sample-Packs.md`](Sample-Packs.md). Infinite Horizons is a valid 1.20.1 Forge *shape* but too large for routine tests.

**Done when:** CurseForge file-import works **or** this step is explicitly deferred in the changelog with the ToS blocker.

**Changelog:** _(empty)_

---

## Phase 5 — Server Management modding

### Step 5.1 — Inspect mods + re-download imported pack

**Status:** TODO  
**Depends on:** 4.10

**Read first**

- Lab `PRODUCT-IDEAS.md` → **Server Management modding (v1)** only  
- `ServerManagementTab.razor` + `ServerManagementViewModel.cs`  
- Local pack-archive path from 4.8

**Do**

- For Modded stacks: list/summary of server-side mods on VM1 (live `mods/` and/or manifest). Inspect-only.  
- **Download pack** = the original imported archive from admin-PC local data (optional VM1 copy outside `mods/`). Never zip VM1 `mods/` and call it the client pack.  
- Vanilla/Paper: short “not a modded server” empty state. **No** change/replace pack (after v1).

**Test**

- `dotnet build`; empty state on Vanilla config; download disabled with a clear message if the local archive is missing.

**Done when:** Inspect + original-archive download exist; no catalog.

**Changelog:** _(empty)_

---

## Phase 6 — Top-bar chrome + oversized world

### Step 6.1 — Overflow menu + settings gear

**Status:** TODO  
**Depends on:** 1.1

**Read first**

- Lab `PRODUCT-IDEAS.md` → v1 table row **Top-bar right chrome** + Manager UI top-bar notes (not the whole UI chapter)  
- `MainLayout.razor` header/chrome only  
- `find-skills` / existing Hybrid CSS — do not add Avalonia packages

**Do**

- Right-side **overflow** (About, extras) and **settings** (program settings: paths, update-check toggle placeholder for Phase 9). Native OS chrome stays. No mini-terminal.  
- Do **not** build the notification list yet.

**Test**

- `dotnet build`; buttons open panels; manage tabs unchanged.

**Done when:** Gear + overflow exist without a bell list.

**Changelog:** _(empty)_

---

### Step 6.2 — Notification center (bell)

**Status:** TODO  
**Depends on:** 6.1

**Read first**

- Same PRODUCT-IDEAS chrome row  
- `MainLayout.razor` + a new small ViewModel (keep it small)

**Do**

- Bell + notification list shell (empty states, dismiss). One in-app channel later steps can post to. No Start checklist. No Players.

**Test**

- `dotnet build`; can post a fake notification in DEBUG.

**Done when:** Bell shows a dismissible list.

**Changelog:** _(empty)_

---

### Step 6.3 — Oversized-world SSH download + bell

**Status:** TODO  
**Depends on:** 6.2

**Read first**

- Lab `PRODUCT-IDEAS.md` → **Oversized world backup (v1)** heading (search that title)  
- [`Contracts-Object-Storage.md`](Contracts-Object-Storage.md) `meta/oversized-world-backup.json`  
- `ServerManagementViewModel.cs` Download World Save path  
- Blueprint **§11.2** only if needed

**Do**

- Detect the OS flag; bell notification; **Download World Save** uses SSH pull when OS backups are blocked for size. Do not upload the zip to Object Storage.

**Test**

- `dotnet build`; with a fixture flag, UI switches to SSH path messaging.

**Done when:** Flag → bell + SSH download offer when VM1 is up.

**Changelog:** _(empty)_

---

## Phase 7 — Remaining v1 Manager / infra

### Step 7.1 — VM1 shape scaling (Danger Zone)

**Status:** TODO  
**Depends on:** 1.1

**Read first**

- Lab `PRODUCT-IDEAS.md` → **VM1 shape scaling (v1)** only  
- Danger Zone tab from 1.1  
- Core Compute instance update API usage (open existing Compute facade only)  
- [`OCI-API-Usage.md`](OCI-API-Usage.md) waiters

**Do**

- Show current OCPU/memory. Apply scale only from Danger Zone with hard warnings. VM1 **and** Minecraft must be stopped first. Update shared config/meta; per-interval ledger shape fields already exist — do not break them. Preview remaining monthly playtime (same ~1500 OCPU-h envelope).  
- Do not advertise shapes beyond 4/24 until Oracle Always Free envelope confirmation (operator research). Optional 8 OCPU only if the operator already confirmed docs.

**Test**

- `dotnet build`; UI disabled unless VM1 STOPPED. No live resize unless operator authorizes test tenancy.

**Done when:** Scale apply path exists with warnings; ledger history still valid.

**Changelog:** _(empty)_

---

### Step 7.2 — Always-on-capable 2/12 messaging

**Status:** TODO  
**Depends on:** 7.1 (or Setup 2/12 picker already shipped in MVP)

**Read first**

- Lab `PRODUCT-IDEAS.md` v1 row **Always-on-capable small shape UX**  
- Usage tab copy + door MOTD budget strings **only if** this step must change them (grep; do not rewrite door C unless copy is actually wrong)

**Do**

- When VM1 is 2 OCPU / 12 GB (or another shape that can stay up ~24/7 inside Always Free), soften MOTD / Usage scare-copy. Still meter usage.

**Test**

- Copy review at 2/12 vs 4/24.

**Done when:** 2/12 users are not nagged as if they were on a scarce 4-OCPU budget.

**Changelog:** _(empty)_

---

### Step 7.3 — Infra vs app version on Connect existing

**Status:** TODO  
**Depends on:** MVP Phase 5 (DONE)

**Read first**

- Lab `PRODUCT-IDEAS.md` → **App version vs infrastructure version**  
- `ConnectExistingFlow.cs`  
- `docs/Local-Config.md` (schema fields only)

**Do**

- Enforce or strongly warn on `infra_schema` / `stack_version` mismatch during Connect existing. Optional tag discovery only when meta is missing — keep auto-detect **button-gated**.

**Test**

- `dotnet build`; fixture meta with wrong schema shows confirm/block as designed.

**Done when:** Connect existing does not silently attach to an incompatible stack.

**Changelog:** _(empty)_

---

### Step 7.4 — Conditional Object Storage writes (etag)

**Status:** TODO  
**Depends on:** existing Core Object Storage client

**Read first**

- Lab `PRODUCT-IDEAS.md` → **Central Object Storage — source of truth** writer rules  
- [`Contracts-Object-Storage.md`](Contracts-Object-Storage.md) version/etag notes  
- Core Object Storage client only

**Do**

- Use etag / if-match (or generation) on Manager writes for budget, meta, and IP mode/allowlist. Do not redesign flag categories. Do not add a `backups` dirty-flag category.

**Test**

- `dotnet build`; conflict path returns a clear error instead of clobbering.

**Done when:** Those Manager writers are conditional.

**Changelog:** _(empty)_

---

### Step 7.5 — RCON + log console tab (not PTY)

**Status:** TODO  
**Depends on:** existing SSH/RCON helpers

**Read first**

- Lab `PRODUCT-IDEAS.md` v1 row **Server Management / customization** (console part)  
- Core RCON/SSH helpers (open only those)  
- `MainLayout.razor` tab strip

**Do**

- A **Console** tab: send RCON commands; show recent Minecraft logs via SSH. **Not** an interactive Java PTY. Not a mini-terminal on the status card.

**Test**

- `dotnet build`; RCON localhost-only still not exposed on the Security List.

**Done when:** Operator can send RCON and view logs from Manager.

**Changelog:** _(empty)_

---

### Step 7.6 — Server name / icon / description / chat messages

**Status:** TODO  
**Depends on:** 7.5 optional (can land without console)

**Read first**

- Lab `PRODUCT-IDEAS.md` same customization row (name, icon, description, automated chat in storage)  
- Existing `messages/` Object Storage sketch in contracts

**Do**

- Persist MOTD-scale customization (name, icon, description, scheduled chat JSON) in Object Storage. Wire what the door/VM1 already consume; do not build a rich MOTD visual editor.

**Test**

- `dotnet build`; objects round-trip.

**Done when:** Those fields save to shared storage; no PTY.

**Changelog:** _(empty)_

---

### Step 7.7 — Usage API 48h ledger reconcile Function (code only)

**Status:** TODO  
**Depends on:** ledger contract

**Read first**

- Lab `PRODUCT-IDEAS.md` v1 row **Usage API reconciliation**  
- Lab `functions/shutdown_vm/` only as a **pattern** for a second function (do not modify the $1 function in this step)  
- [`OCI-API-Usage.md`](OCI-API-Usage.md)

**Do**

- Tracked Function source: for ledger days **older than ~48 hours**, reconcile from OCI Usage API, write back, bump dirty/version. Placeholders, no OCIR push.  
- Do not run it against the lab tenancy.

**Test**

- Unit with a mocked usage payload.

**Done when:** Source + README exist; not deployed unless the operator later asks.

**Changelog:** _(empty)_

---

## Phase 8 — Paid / spend mode

**Last product feature.** Do not start this phase until the operator explicitly continues after Phase 7. Default product remains Always Free / $0.

### Step 8.1 — Paid mode model + Danger Zone UI

**Status:** TODO  
**Depends on:** 1.1, 2.4

**Read first**

- Lab `PRODUCT-IDEAS.md` → **Paid / spend mode (v1)** only  
- Danger Zone tab  
- Budget config DTO in Core / contracts (`mode=always_free` vs `paid`)

**Do**

- Danger Zone opt-in with hard warnings. Max monthly spend; daily/monthly uptime ↔ estimated cost fields; SoftStop on final alert only (wire to existing budget machinery — do not create new paid OCI services).  
- Never infer paid mode from PAYG tenancy status.

**Test**

- `dotnet build`; default stays `always_free`.

**Done when:** Explicit opt-in exists; Always Free remains default.

**Changelog:** _(empty)_

---

### Step 8.2 — Cost Estimator JSON fallback

**Status:** TODO  
**Depends on:** 8.1

**Read first**

- Lab `PRODUCT-IDEAS.md` → Cost Estimator fallback paragraph under paid mode  
- Setup wizard Always Free confirmation page

**Do**

- Ship a **preset Cost Estimator configuration JSON** in the repo (no secrets). Wizard/Danger Zone: open Cost Estimator → import JSON → confirm $0 for Always Free or that the estimate matches in-app paid estimates. Do not call paid billing APIs in a loop.

**Test**

- JSON is valid for the current Oracle Cost Estimator import format (operator can confirm).

**Done when:** Preset exists and Setup/Danger Zone point at it.

**Changelog:** _(empty)_

---

## Phase 9 — Packaging, updates, launch

Former MVP Phase **8–9**. **Do not start** until Phases **1–7** are DONE and Phase **8** is DONE or the operator **skips** paid mode.

### Step 9.1 — Windows installer

**Status:** TODO  

**Do**

- Single installer → one app (Setup integrated). Document code-signing strategy (purchase may be deferred); SmartScreen notes.

**Test**

- Clean Windows user install; app runs; config locations documented.

**Done when:** Installer artifact builds reproducibly.

**Changelog:** _(empty)_

---

### Step 9.2 — GitHub Releases update check

**Status:** TODO  
**Depends on:** 9.1 (or can ship against `dotnet run` if the operator wants it earlier — still this step)

**Do**

- On launch: check latest GitHub Release; prompt + **release notes**. Honor the settings-gear update toggle from 6.1 if present. Offline dismiss works.

**Test**

- Mock or real release; prompt appears; dismiss works offline.

**Done when:** Update check ships in the app.

**Changelog:** _(empty)_

---

### Step 9.3 — Guide + README v1 pass

**Status:** TODO  
**Depends on:** Phases 1–8 feature work

**Read first**

- [`Guide.md`](Guide.md)  
- [`README.md`](../README.md)

**Do**

- One consistency pass: Paper/Modded Setup, private allowlist (no public mode), spend-brake lock, installer vs run-from-source. Do not invent features.

**Test**

- Read-through as a first-time admin.

**Done when:** Guide matches shipped v1 behavior.

**Changelog:** _(empty)_

---

### Step 9.4 — Closed beta / dogfood

**Status:** TODO  

**Do**

- Dogfood with real friends on reserved IP; fix blockers only. Keep $0 discipline. Installer preferred if 9.1 exists; source is OK.

**Test**

- Multi-friend play; wake from cold; idle stop; at least one Modded or Paper path if those shipped.

**Done when:** No v1-blocking bugs open (or deferred with operator OK).

**Changelog:** _(empty)_

---

### Step 9.5 — V1 exit review

**Status:** TODO  

**Do**

- Tick v1 table in PRODUCT-IDEAS against this plan. Confirm **later** items were not scoped in. Update `README.md` + lab `VM-Software.md`.  
- **Operator (not agents):** clean-room test in PRODUCT-IDEAS (new account + installer + Setup + $1 brake including **lock UX**). Prefer a local VM / spare PC. May incur ~$1–$2 residual — not on the long-lived lab tenancy unless spend is accepted.

**Done when:** Operator declares v1 ready to publish.

**Changelog:** _(empty)_

---

## Reference map

| Need | Where |
|------|--------|
| This checklist | **this file** |
| MVP archive (Phases 0–7) | [`MVP-Implementation-Plan.md`](MVP-Implementation-Plan.md) |
| Happy-path user guide | [`Guide.md`](Guide.md) |
| MVP / v1 / later intent | Lab `PRODUCT-IDEAS.md` |
| Game install mechanism | [`Minecraft-Server-Deployment-Blueprint.md`](Minecraft-Server-Deployment-Blueprint.md) — **named §§ only** |
| Object Storage contracts | [`Contracts-Object-Storage.md`](Contracts-Object-Storage.md) |
| Suggested order narrative | Lab `docs/Development-Steps.md` |
| What’s live on VMs | Lab `docs/VM-Software.md` |
| Deploy pitfalls | Lab `docs/Agent-Deploy-Pitfalls.md` |
| OCI API usage | [`OCI-API-Usage.md`](OCI-API-Usage.md) |

---

## Out of scope (do not implement under this plan)

- Players tab / Kick·Op·Ban  
- Start-from-Manager **progress checklist**  
- Maintenance / reserved-IP assignment + start-VM1-without-moving-play-IP  
- Connect an **additional** deployment / multi-profile switcher  
- Day-2 **change/replace modpack**  
- Full per-day budget calendar  
- Quilt as a Setup entry point (detect-only is OK in 4.7)  
- Purpur / Folia / hybrids  
- **Rejected:** in-app Modrinth/CurseForge/FTB **catalog / browse / search / download-a-pack** (users import a local file; this is not an after-v1 feature)  
- **Rejected:** public Minecraft / public-private toggle / blacklist (private allowlist only; this is not an after-v1 feature)  
- Interactive Java **PTY** console  
- macOS / Linux Manager  
- Event-driven door handback as primary  
- Silent OCI probing on startup  
- Public game access (`0.0.0.0/0` on 25565 / 22 / 8080)  
- Paid OCI services / spend **except** Phase **8** when the operator continues  

---

## Plan changelog

| Date | Note |
|------|------|
| 2026-08-18 | **Step 4.1 DONE.** Core Fill v3 client + offline fixtures (STABLE resolve, SHA-256 URL from JSON, no v2 URL builder). No Setup UI / on-box. **NEXT = Step 4.2**. Do not start 4.2 unless asked. |
| 2026-08-18 | **Step 3.4 DONE.** Manager private-only: no public toggle/blacklist UI; no `ip/mode.json` writer; planner keeps CIDR allowlist and strips leftover world-open Minecraft. TESTING SL was already private. **NEXT = Step 4.1**. Do not start 4.1 unless asked. |
| 2026-08-18 | **Public/blacklist rejected.** Step **3.3 CANCELLED**. Steps **3.1–3.2 WITHDRAWN**. Docs updated. **NEXT = Step 3.4** (remove 3.1/3.2 code; keep CIDR). Do not start 3.4 unless asked. |
| 2026-08-18 | In-app mod/modpack browser marked **rejected** (not after-v1). Users import a local pack file only. **NEXT remains Step 3.3.** |
| 2026-08-18 | Operator-local sample packs: gitignored `data/sample-packs/` + tracked [`Sample-Packs.md`](Sample-Packs.md). CI stays on `tests/fixtures/`. Agents missing a pack format **pause and ask the operator**. **NEXT remains Step 3.3.** |
| 2026-08-18 | **On-box SoT moved** into this repo: `door_vm/`, `vm_agent/`, `functions/shutdown_vm/`, plus `docs/Agent-Deploy-Pitfalls.md`. Lab trees are pointer READMEs. Setup `ProductPaths` no longer requires a lab checkout. **NEXT remains Step 3.3.** |
| 2026-08-17 | **Step 3.2 DONE.** One-list rewrite: public Minecraft `0.0.0.0/0` TCP/UDP; SSH never world-open; private restores allowlist; 3.1 confirm before public apply. Planner unit tests; no live SL apply. **NEXT = Step 3.3**. Do not start 3.3 unless asked. |
| 2026-08-17 | **Step 3.1 DONE.** Persist `private`/`public` + blacklist locally (`friends.local.json`) and `ip/mode.json` when present; public confirm; Apply-public stub; SL unchanged. **NEXT = Step 3.2**. Do not start 3.2 unless asked. |
| 2026-08-17 | **Step 2.4 DONE.** Manager full-window spend-brake overlay; exact typed confirm; park-IP + DELETE lock + OS-refresh + Wake (gates still apply). Core `SpendBrakeLockUx` tests. **NEXT = Step 3.1**. Do not start 3.1 unless asked. |
| 2026-08-17 | **Step 2.3 DONE.** Door GETs `meta/spend-brake-triggered.json` on wake pull; presence refuses START (`SPEND_BRAKE` MOTD/kick distinct from daily). Fail closed on non-404 GET. No extra Python. Live door still needs redeploy. **NEXT = Step 2.4**. Do not start 2.4 unless asked. |
| 2026-08-17 | **Step 2.2 DONE.** Tracked Function PUTs `meta/spend-brake-triggered.json` on real threshold alerts (ignore RESET); SoftStop **VM1 only**; door Micro left running (Always Free AMD Micro ≠ Ampere hours). HCL stop-list default VM1 + OS config. No `fn push`. **NEXT = Step 2.3**. Do not start 2.3 unless asked. |
| 2026-08-17 | **Step 2.1 DONE.** Frozen Object Storage lock: `meta/spend-brake-triggered.json` v1; Function writer, Manager-only DELETE clearer, door+Manager readers; fail closed. Core DTO + get/put/delete. No live Function deploy. **NEXT = Step 2.2**. Do not start 2.2 unless asked. |
| 2026-08-17 | **Step 1.3 DONE.** Wipe world: Server Management button + confirm; SSH deletes only `world_path` under `/opt/mcmgr/server/`; Minecraft stopped first; Object Storage backups / mods / `server.properties` untouched. **NEXT = Step 2.1**. Do not start 2.1 unless asked. |
| 2026-08-17 | **Step 1.2 DONE.** Allowlist CIDR: Add-IP Advanced field; persist prefix locally + `ip/allowlist.json` when present; Minecraft SL rules use the CIDR; SSH/door stay `/32` except own admin entry; reject `/0`–`/8`. **NEXT = Step 1.3**. Do not start 1.3 unless asked. |
| 2026-08-17 | **Step 1.1 DONE.** Hybrid **Advanced** vs **Danger Zone** tabs (idle-disable + Delete infrastructure only on Danger Zone; timeout stays on Advanced). **NEXT = Step 1.2**. Do not start 1.2 unless asked. |
| 2026-08-17 | VM1 may be STOPPED: START if needed, **disable idle** while working, **re-enable idle** when finished. If already RUNNING, confirm idle is off first. OS-ISSUE-7: re-disable after Minecraft start. |
| 2026-08-17 | Test-stack access: OCI CLI/API with **`TESTING`** (never `DEFAULT`); SSH both test VMs with `mcmgr_ed25519_20260817_125552`; $0 only; mirror VM/cloud edits into local SoT. `tofu apply` / OCIR still operator-authorized. |
| 2026-08-17 | Created. Operator chose **v1 features before packaging**. Manager UX first (Phase 1), then spend-brake, IP mode, Setup game types, remaining v1, paid mode last, packaging last. **NEXT = Step 1.1**. Do not start 1.1 unless asked. |
