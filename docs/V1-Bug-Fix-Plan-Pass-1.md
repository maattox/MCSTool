# V1 bug-fix plan — Pass 1

**Status:** Living. Created 2026-08-19 from [`V1-QA-Pass-1-Results.md`](V1-QA-Pass-1-Results.md) after **operator early triage** (paused after S2). **P1–P4 DONE** 2026-08-19. Catalog **S0–S7 DONE** (S7-04 Skipped). Operator **confirmed remaining severities** 2026-08-19 (including S3-07 auto-start). **NEXT = P5.** Do not start 8.6.1 or 9.1.  
**Parent:** [`V1-Implementation-Plan.md`](V1-Implementation-Plan.md) Step **8.5.2** (stays NEXT until Phase 8.5 exits).  
**Catalog:** [`V1-QA-Catalog.md`](V1-QA-Catalog.md) — do not edit expected steps.

This file’s creation / triage sessions **did not implement code**. Later agents implement **only the single section marked NEXT**.

**Cost:** $0. TESTING profile only. Never `DEFAULT` / live Forge lab. Never Minecraft `0.0.0.0/0`.  
**Functions:** agents **may** `fn build` / `fn push` / invoke **product** Functions on TESTING without asking, still $0 — no real $1 budget fire; do not SoftStop the door.  
**Tofu:** `tofu apply` / `destroy` only if the operator authorizes that command in the session.

Hosts/OCIDs: `%LOCALAPPDATA%\McManager\tofu\mcmgr\outputs.json`. SSH: `ubuntu` + `%USERPROFILE%\.ssh\mcmgr_ed25519_20260817_125552`. Hybrid: `MCMANAGER_CONFIG_DIR` = `mcmgr-blank-test`. **Do not** use product `data/config.local.json` (live Forge lab).

---

## How agents must use this file

1. Read **this protocol**, the [Progress dashboard](#progress-dashboard), and **only the NEXT section**.  
2. Implement only that section. Do not start neighbors “while you are here.”  
3. After finishing: mark **DONE**, set the next incomplete section to **NEXT**, changelog line, **stop**.  
4. If you change a test VM or TESTING cloud resource, make the **same** change in local SoT (`onbox/`, `infra/`, `door_vm/`, `vm_agent/`, `functions/shutdown_vm/`, Manager/Setup). File lab [`docs/Issues.md`](../../OCI-mc-server-manager/docs/Issues.md) for on-box/Setup/door bugs.  
5. Never create git commits. Suggest a message.  
6. Do not start V1 Step **8.6.1** or **9.1**. Do not implement after-v1 PRODUCT-IDEAS items unless the operator asks. Catalog S3–S7 already ran.  
7. VM1: START if needed, **disable idle** while working, **re-enable** when finished (re-disable after Minecraft start — OS-ISSUE-7).  
8. **Operator will:** this plan is the operator-requested execution doc for Pass 1 remaining fixes. If it disagrees with lab `PRODUCT-IDEAS.md`, **follow this plan** and note the drift (do **not** rewrite this file to match PRODUCT-IDEAS). Stop and ask only if you cannot tell which document the operator meant.

### Context budget

Read this header + **one** section + the files listed there. Do not load the full V1 plan, blueprint, or PRODUCT-IDEAS unless a heading is named.

### Operator prompt (copy-paste for the next agent)

```text
Read docs/V1-Bug-Fix-Plan-Pass-1.md in OCI-mc-server. Implement only the section marked NEXT (or the PARALLEL-OK section I named).
You MAY use OCI CLI/API with profile TESTING (not DEFAULT) and SSH both test VMs. You MAY fn build/push/invoke product Functions on TESTING. Stay at $0. Do not tofu apply/destroy unless I authorize it in this chat. Do not commit. Do not start Step 8.6.1 or 9.1.
If you need VM1, START it, disable idle, re-enable when finished.
When done: update this plan’s statuses, file Issues.md if on-box/Setup/door, stop, tell me what you did, how to test, what’s next, and ask if I want to continue.
Prompt sequential steps in Agent mode (not Plan mode). Use Build in Parallel / Plan mode only if the NEXT step is marked PARALLEL-OK. Include this same Agent-vs-Plan instruction in the prompt you give the operator for the following step.
```

### PARALLEL-OK

Only when two sections **do not** edit the same files. Hybrid Razor/CSS is sequential by default. P4 (Core planner) does not overlap P5 (messages apply) or P6 (door wake / Manager Start) if those stay in the files listed.

---

## Confirmed triage (operator 2026-08-19)

Operator confirmed the suggested severities except **S3-07** (must auto-start after wipe; do not keep leave-stopped). **This plan is operator will** for these items. Lab `PRODUCT-IDEAS.md` **Wipe world (v1)** still says the next Start creates a world — **do not rewrite P8 back to that**. Note the drift; update Guide / catalog expected in P8.

| Catalog / item | Keep as v1 fix? | Severity | Plan | Notes |
| -------------- | --------------- | -------- | ---- | ----- |
| **S3-04** leftover Minecraft **prefix** CIDR after allowlist revert | **Yes** | **Minor** | **P4 DONE** | `IsManagedRule` was `/32`-only. SL already restored on TESTING. Do not strip door `wait_forge` VCN TCP 25565. |
| **S4-12** name / icon / MOTD never appeared in Java list | **Yes** | **Major** | **P5** | Save + start/restart while stopped, then save-again while running, still old identity. Door MOTD out of scope. |
| **S5-05** Manager Start refused when daily exhausted | **Yes** | **Major** | **P6** | Door refuses **player** wake; **admin Start from Manager** must still work. Spend-brake lock must still block Start (S3-01). |
| S5-05 distinct daily vs spend-brake copy | No | — | — | Eventual Pass after one connect. Do not re-fix. |
| S5-05 MOTD lag until first connect | **Known** | — | OS-ISSUE-4 | Door OS pull is wake + `/api/os-refresh`, not every tick. |
| S5-05 no in-game chat on **sudden** cap drop | **Won't-fix** this pass | — | — | Warnings are remaining-time ticks (30/5 min), not “cap just rewritten.” |
| S5-05 MOTD reset **PT midnight vs UTC day** | **Parked** | — | — | Documented door-vs-UTC gap in Contracts. Not a P-section this pass. |
| S6-02 incomplete CurseForge zip allowed continue | **Yes** | **Minor** | **P7** | Hard-block until jars are in the zip. Not CurseForge API (4.12 stays deferred). S6-02 catalog row was Pass. |
| **S3-07** wipe auto-start | **Yes** | **Minor** | **P8** | Operator override: Minecraft **starts again** after wipe. PRODUCT-IDEAS / Pass 1 catalog expected leave-stopped — follow this plan. |
| S4-11 live Modding inspect | **Defer** | — | later **modded** redeploy | Vanilla empty-state Pass. |
| S5-02 first-kick copy (“wake already triggered”) | **Known** | Nit | DOOR-ISSUE-1 | S5-02 Pass. |
| S5-04 Status stale until window focus | **Won't-fix** / Known | Nit | — | Unfocused poll is **2 min** on purpose. |
| S0-01 SSH.NET NU1903 | **After-v1** | — | Phase **9** packaging | Tests passed. |
| S6-03 lab not listed / `DEFAULT` in Setup picker | **No** | — | — | TESTING Connect Pass. |
| S7-04 Delete + greenfield | **Skip** | — | — | Needs explicit `tofu destroy`/`apply` later. |
| `data/config.local.json` still Forge / `DEFAULT` | **No** | — | — | Use `mcmgr-blank-test`. |

---

## What already happened (do not re-fix)

Pass 1 **S0–S7** are recorded in [`V1-QA-Pass-1-Results.md`](V1-QA-Pass-1-Results.md). Do not re-run the full catalog.

- **Pass (leave):** S0 (S0-05 Skipped), S1, S2-01–10, S2-16–18, S2-20–22, S2-28, S3-01–03, S3-05–07 (wipe path Pass; **P8** adds auto-start), S4 except S4-12, S5-01–04, S6, S7-02/S7-03.  
- **P1 DONE:** OS-ISSUE-9 firewalld/cloud-init/dbus cycle; SoftStop ~43s. S2-05 Pass after fix.  
- **P2 DONE:** S2-11 lock GET + DOOR-ISSUE-10 CLI 3.90 404.  
- **P3 DONE:** TESTING `shutdown_vm` 0.0.12; FN-ISSUE-1 gone on TESTING.  
- **Known / by design:** OS-ISSUE-7 idle re-enable; DOOR-ISSUE-1 first-kick; OS-ISSUE-4 wake-only OS pull; OS-ISSUE-6 heal-only-when-STOPPED.  
- **S3-04 leftover `/24`:** agent already stripped TEST-NET from TESTING SL. **P4 DONE** — planner now strips leftover Minecraft prefixes on revert.

---

## Progress dashboard

| ID | Section | Status | Parallel? | Live SSH/OCI? |
|----|---------|--------|-----------|----------------|
| **P1** | Guest ACPI SoftStop stuck STOPPING + UFW/firewalld/dbus | **DONE** | SEQUENTIAL | Yes |
| **P2** | Door spend-brake lock GET on TESTING (S2-11) | **DONE** | SEQUENTIAL | Yes |
| **P3** | TESTING `shutdown_vm` Function image (S2-16–18) | **DONE** | SEQUENTIAL | Yes (fn/Docker) |
| **P4** | Allowlist leftover Minecraft prefix CIDR (S3-04) | **DONE** | PARALLEL-OK vs P5/P6/P7 | Unit tests; no tofu |
| **P5** | Server name / icon / MOTD not applied (S4-12) | **NEXT** | SEQUENTIAL vs VM1 work | Yes |
| **P6** | Manager Start after daily exhaustion (S5-05) | TODO | SEQUENTIAL vs door/Hybrid Start | Yes |
| **P7** | Incomplete CurseForge zip hard-block (S6-02 UX) | TODO | PARALLEL-OK vs P4 | No |
| **P8** | Wipe world auto-starts Minecraft (S3-07) | TODO | SEQUENTIAL vs Server Management | Yes |

**NEXT = P5.** Do not start 8.6.1 or 9.1.

---

## Parked / Known / after-v1

Do **not** open a new P-section for these unless the operator overrides.

| Item | Disposition |
| ---- | ----------- |
| OS-ISSUE-4 MOTD lag until connect / OS-refresh | Known (by design). Optional Nit: Usage publish calls `POST /api/os-refresh`. |
| Sudden daily-cap drop → no 30/5 chat | Won't-fix this pass. Product warnings are remaining-time ticks. |
| Door LA midnight vs UTC day windows | **Parked** (operator 2026-08-19). Documented in [`Contracts-Object-Storage.md`](Contracts-Object-Storage.md). |
| DOOR-ISSUE-1 / S5-02 idle kick on first connect | Known. Copy nit (“wake already triggered”) parks with it. |
| Hybrid Status until focus (S5-04) | Intended 2 min background poll (`MainViewModel.BackgroundPollInterval`). |
| S4-11 modded Modding panel | Later modded TESTING redeploy. |
| SSH.NET GHSA-q939-rpr3-3284 | After-v1 / Phase 9. |
| S7-04 greenfield | Operator-authorized tofu later. |
| Step 4.12 CurseForge API | Stays **deferred**. P7 is file-import block only. |

---

## P1 — Guest ACPI SoftStop stuck STOPPING + UFW/firewalld/dbus

**Status:** DONE  
**Catalog IDs:** S2-05 (related), S2-08 notes, additional problem #3 → [OS-ISSUE-9](../../OCI-mc-server-manager/docs/Issues.md)  
**Severity:** Blocker (QA pause; operator 2026-08-19)

**Read first**

- [`V1-QA-Pass-1-Results.md`](V1-QA-Pass-1-Results.md) — Failures expanded **S2-05** and **OS-ISSUE-9 STOPPING timeline** only  
- Lab [`docs/Issues.md`](../../OCI-mc-server-manager/docs/Issues.md) — **OS-ISSUE-5**, **OS-ISSUE-9**, **SETUP-ISSUE-7**  
- [`infra/cloud-init/vm1.yaml.tftpl`](../infra/cloud-init/vm1.yaml.tftpl)  
- [`docs/Agent-Deploy-Pitfalls.md`](Agent-Deploy-Pitfalls.md) (firewalld / netfilter)  
- Lab [`docs/Operator-Troubleshooting.md`](../../OCI-mc-server-manager/docs/Operator-Troubleshooting.md) — VM1 STOPPING / firewalld blocks  

Do **not** load the Minecraft blueprint or PRODUCT-IDEAS.

**Do**

1. START VM1 if STOPPED (Always Free). Disable idle. Confirm you are on TESTING tofu outputs, not Forge `config.local.json`.  
2. **Investigate** guest firewall + shutdown, not only the last boot:
   - `systemctl is-enabled` / `is-active` / `cat` for `firewalld`, `dbus.socket`, `dbus.service`, **`ufw`**, `netfilter-persistent`.  
   - `ufw status verbose` (sudo). Unit `Conflicts=` / `Conflicts=` reverse.  
   - `journalctl --list-boots` and the **S1 START** boot if still on disk (~2026-08-19 19:10 UTC): `firewalld`, `dbus`, `ufw`, `minecraft`, shutdown/`final.target`.  
   - If you catch **STOPPING** live: `systemctl list-jobs`, `systemctl status minecraft`. Do not issue a second STOP while 409 “being modified” — wait STOPPED.  
   - **Research online** how Ubuntu **UFW** interacts with **firewalld** (both own nftables; typically must not both be active), and ACPI SoftStop hangs when **dbus** is down.  
3. **Hypothesis to prove or discard:** UFW (or leftover UFW/nft) prevents firewalld/dbus from coming up on some boots; a dbus-down boot then stalls ACPI SoftStop so OCI stays **STOPPING** ~15–17 min; waking during that window races IP handback (S2-08 first wake). SETUP-ISSUE-7 (netfilter-persistent) is already masked — this is a **different** fight if UFW is involved.  
4. **Fix the product path** (`infra/cloud-init`, Setup guest repair / `EnsureVm1HostFirewall` if that is where firewalld is enabled, `onbox/` as needed). Disable/mask UFW if it should never run on VM1, or document why firewalld-only is the SoT. Do not only patch the live test VM.  
5. **Reproduce:** at least two SoftStops after a Compute START and, if practical, one after a door wake. Target: **STOPPED in a few minutes**, not ~17. Confirm dbus+firewalld active after those boots; 25565 in firewalld.  
6. Update OS-ISSUE-9 (and S2-05 notes if the UFW finding is real). Re-enable idle when finished. Prefer leaving VM1 **STOPPED**, play IP on door, lock absent.

**Test**

- Catalog IDs to re-run after the fix (same chat or a short delta): **S2-05**, a **SOFTSTOP → STOPPED** timing check (S2-08 wait), optionally S2-09 2-minute idle if you already have Minecraft up. Do **not** start S3.

**Done when:** Root cause is written in OS-ISSUE-9 (UFW confirmed, discarded, or replaced with a better cause). Product SoT matches the guest. SoftStop no longer routinely sits in STOPPING past ~5–10 minutes on TESTING. Operator can resume Pass 1 at S3.

**Changelog:** 2026-08-19 — Root cause is firewalld/cloud-init/dbus **ordering cycle** (Debian #1025618), not UFW nft fight. S1 19:10 boot: systemd deleted `dbus.service`/`dbus.socket`; no logind ACPI; STOPPING ~17 min. UFW was systemd-enabled but `ENABLED=no` (cousin; masked anyway). Product: full `/etc/systemd/system/firewalld.service` override (`infra/cloud-init/firewalld-mcmgr.service`; drop-ins cannot reset `Before=`), mask UFW. `EnsureVm1HostFirewall` matches. TESTING: three SoftStops → STOPPED in **43s**; post-START boots had dbus+firewalld+25565, no dbus job deleted. Door wake not used for timing (lock GET fail-closed on 404 — P2). Idle re-enabled. Left VM1 STOPPED, play IP on door, lock absent.

---

## P2 — Door spend-brake lock GET on TESTING (S2-11)

**Status:** DONE  
**Catalog IDs:** S2-11  
**Severity:** Major (suggested)

**Do (later):** Redeploy TESTING door from product `door_vm/` so live `pull_os_budget.sh` GETs `meta/spend-brake-triggered.json` (product SoT already has this). Re-run S2-11. Do not implement in the P1 session.

**P1 note:** A wake during P1 showed live `pull_os_budget.sh` **does** GET the lock now, but treats OCI CLI **404** as fail-closed (`ERROR: spend-brake lock GET failed (not 404)`), so wake did not START. Confirm 404 = absent during P2.

**Changelog:** 2026-08-19 — TESTING redeployed product `pull_os_budget.sh` + rebuilt `mccontrol`. Root remaining bug after the script landed: OCI CLI 3.90+ 404 is `"status": 404` / `error code 404` (`code` null), not `ObjectNotFound` (DOOR-ISSUE-10). Absent lock → `SPEND_BRAKE_LOCK=0`. PUT v1 lock → `POST /api/wake` → VM1 stayed STOPPED, door `SPEND_BRAKE`, journal `SPEND_BRAKE_LOCK=1` (no `start_vm1`). DELETE + `/api/os-refresh` → `SPEND_BRAKE_LOCK=0`, `DOOR_IDLE`. S2-11 Pass. VM1 left STOPPED; lock absent. Did not start VM1 (idle unchanged). Did not start S3 or 9.1.

---

## P3 — TESTING `shutdown_vm` Function image (S2-16–18)

**Status:** DONE  
**Catalog IDs:** S2-16, S2-17, S2-18  
**Severity:** Blocked (environment) → cleared

**Do (later):** Until V1 Step **8.6.1** ships, `fn` CLI + Docker engine (or interim Setup OCIR publisher) onto existing `mcmgr-fn-app` / `mcmgr-fn/softstop`. That is a **TESTING** fill-in, not the installer story. Product path: CI-built ARM image copied into OCIR (no Docker on the admin PC). Stay $0; do not SoftStop the door. Do not implement in the P1 session.

**Changelog:** 2026-08-19 — No rebuild. Live TESTING already had `mcmgr-fn-app` / `mcmgr-fn-softstop` + private OCIR `mcmgr-fn/softstop:setup` (`func.yaml` **0.0.12**, env-driven `INSTANCE_OCIDS`, RESET skip + lock PUT). Function config is VM1 only (door not on the list). Docker Desktop was running; `fn` CLI still absent — used `oci fn function invoke`. **S2-16** Pass (image inspect). **S2-17** Pass: synthetic ACTUAL → VM1 SoftStop **STOPPED ~57s**, lock v1 `source=budget_function`, door **RUNNING**, play IP stayed on door secondary. **S2-18** Pass: RESET → `SKIPPED`; VM1 stayed RUNNING; lock absent. Extra: PUT lock + RESET left the object in place. DELETE lock. Idle re-enabled. Left VM1 STOPPED, play IP on door, lock absent. Did not `tofu apply`. Did not start S3 or 9.1. This is **not** Step 8.6.1.

---

## P4 — Allowlist leftover Minecraft prefix CIDR (S3-04)

**Status:** DONE  
**Catalog IDs:** S3-04  
**Severity:** **Minor** (operator 2026-08-19)

**Read first**

- [`V1-QA-Pass-1-Results.md`](V1-QA-Pass-1-Results.md) — Failures expanded **S3-04** only  
- [`src/McManager.Core/Services/SecurityListIngressPlanner.cs`](../src/McManager.Core/Services/SecurityListIngressPlanner.cs)  
- [`src/McManager.Core/Config/FriendRules.cs`](../src/McManager.Core/Config/FriendRules.cs)  
- [`src/McManager.Core.Tests/SecurityListIngressPlanTests.cs`](../src/McManager.Core.Tests/SecurityListIngressPlanTests.cs)  

Do **not** load PRODUCT-IDEAS or the Minecraft blueprint.

**Do**

1. Repro in unit tests (no live SL required): existing ingress has named Minecraft TCP+UDP **`192.0.2.0/24`** (or any `/9`–`/31`); Desired List is admin `/32` only. After `Build`, the leftover prefix must **not** be in `Preserved`. ICMP stays. World-open Minecraft still stripped.  
2. **Must preserve** the door `wait_forge` VCN rule: Minecraft **TCP** 25565 from the subnet CIDR (TESTING `10.0.0.0/24`), which is **not** a friend prefix. That rule is TCP-only; friend leftovers were TCP+UDP. Do not invent a second allowlist.  
3. Fix `IsManagedRule` / owned-description so Advanced CIDR rows are rewritten from Desired List and **removed on revert**, not kept because they are not `/32`. Friend `McDescription` is the **name**, not `mc-whitelist:`.  
4. Do not `tofu apply`. Do not open `0.0.0.0/0`. TESTING SL was already cleaned; optional GetSecurityList confirm only.

**Test**

- New/extended `SecurityListIngressPlanTests` for leftover `/24` gone + `wait_forge` TCP preserved.  
- Catalog **S3-04** / **S4-08** on a later Pass 2 delta (add `/32` + `/24`, Save, remove both, Save).

**Done when:** Revert of an Advanced Minecraft prefix does not leave that CIDR on 25565. VCN `wait_forge` TCP 25565 and ICMP still present.

**Changelog:** 2026-08-19 — `IsManagedRule` treated leftover Minecraft CIDRs as unmanaged unless `/32`. Friend Advanced rows use `McDescription` = name (not `mc-whitelist:`), so revert preserved TCP+UDP `192.0.2.0/24`. Fix: strip allowlist-width (`/9`–`/31`) Minecraft leftovers when they are UDP or TCP with a matching UDP sibling; keep TCP-only subnet `wait_forge`. Tests: leftover `/24`+`/16` gone, ICMP + `10.0.0.0/24` TCP 25565 preserved, world-open still stripped. Did not `tofu apply`. Did not touch TESTING SL (already cleaned). No Issues.md (Manager Core only).

---

## P5 — Server name / icon / MOTD not applied (S4-12)

**Status:** NEXT  
**Catalog IDs:** S4-12  
**Severity:** Major (operator 2026-08-19)

**Read first**

- [`V1-QA-Pass-1-Results.md`](V1-QA-Pass-1-Results.md) — Failures expanded **S4-12** only  
- [`docs/Local-Config.md`](Local-Config.md) — Server identity paragraph  
- [`docs/Contracts-Object-Storage.md`](Contracts-Object-Storage.md) — `messages/chat.json` / `messages/server-icon.png`  
- [`src/McManager.Core/Services/ChatMessagesStore.cs`](../src/McManager.Core/Services/ChatMessagesStore.cs)  
- [`vm_agent/os_publish.py`](../vm_agent/os_publish.py) — `pull_messages_if_dirty` / `_apply_identity`  
- [`vm_agent/record_boot.py`](../vm_agent/record_boot.py) (messages force-pull on boot only)

Door MOTD/`mcdoor` is **out of scope**. Do not load the full blueprint.

**Do**

1. START VM1 if STOPPED; **disable idle**. Use TESTING `mcmgr-blank-test`, not Forge `config.local.json`.  
2. **Investigate** (expected vs actual), do not guess a one-line patch:
   - Object Storage: `messages/chat.json` has the saved `server_name` / `description`; optional `messages/server-icon.png`; `meta/flags.json` `messages.vm1`.  
   - VM1: `journalctl -u mc-boot-ledger` / `record_boot` notes for messages pull; `/opt/mcmgr/server/server.properties` `motd=`; `server-icon.png` owner/mode.  
   - Timing: apply is **on Minecraft start** (`record_boot`); a restart that races before pull will keep the old list identity.  
3. Fix the **product path** (Manager PUT + dirty flag, and/or VM1 apply). Redeploy idle agent / boot unit if `vm_agent` changes. Do not only patch the live `server.properties`.  
4. Re-enable idle when finished. Prefer VM1 **STOPPED**, play IP on door, lock absent.

**Test**

- Catalog **S4-12**: Save name + description + optional 64×64 PNG; Start or Restart Minecraft; Java multiplayer list on the **play IP while VM1 holds it** shows the new identity. Door-off MOTD unchanged.

**Done when:** One save + one Minecraft start/restart applies name, MOTD/list text, and icon. Root cause written in the changelog (Manager write vs VM1 apply vs client cache).

**Changelog:** _(empty)_

---

## P6 — Manager Start after daily exhaustion (S5-05)

**Status:** TODO  
**Catalog IDs:** S5-05 (Manager Start half only)  
**Severity:** Major (operator 2026-08-19)

**Read first**

- [`V1-QA-Pass-1-Results.md`](V1-QA-Pass-1-Results.md) — Failures expanded **S5-05** points **1** and **4** only  
- Lab `PRODUCT-IDEAS.md` heading **Usage budget behavior (MVP+)** — one paragraph: *After daily exhaustion stop: only admin via Manager; door refuses player wake*  
- [`src/McManager.Hybrid/ViewModels/MainViewModel.cs`](../src/McManager.Hybrid/ViewModels/MainViewModel.cs) — `WakeGameServerAsync`  
- [`src/McManager.Core/Services/DoorClient.cs`](../src/McManager.Core/Services/DoorClient.cs) — `WakeAsync`  
- Door wake gate: `door_vm/` `do_wake` / budget refuse (named files in that tree; do not load all of `mcdoor.c` unless required)

Do **not** load the full PRODUCT-IDEAS or Minecraft blueprint. Do **not** weaken the **spend-brake** lock (S3-01 / S2-11).

**Do**

1. Player `POST /api/wake` / client connect must still refuse when daily is exhausted (distinct copy from `MONTHLY SPEND BRAKE FIRED` — already seen after one connect).  
2. Hybrid **Start** must **not** share that daily refuse. Admin Start is the documented override. Spend-brake present → still block Start (overlay).  
3. Implement the smallest product path (door admin-authenticated wake **or** Manager Compute+IP path that still parks/reconciles). Stay $0; do not SoftStop the door.  
4. Restore daily cap after any TESTING fixture. Re-enable idle. Leave lock absent.

**Out of this section (unless operator promoted them):** MOTD lag (OS-ISSUE-4), sudden-cap chat, PT vs UTC reset copy/gate.

**Test**

- Lower daily cap below used hours (TESTING, restore after). Client connect refused with **daily** copy. Manager **Start** succeeds (unlocked). With lock PUT, Start still blocked.

**Done when:** Daily exhaustion blocks friends, not the admin Start button. Spend-brake still blocks both.

**Changelog:** _(empty)_

---

## P7 — Incomplete CurseForge zip hard-block (optional)

**Status:** TODO  
**Catalog IDs:** Additional problem #11 (S6-02 row was **Pass**)  
**Severity:** Minor (operator 2026-08-19)

**Read first**

- [`V1-QA-Pass-1-Results.md`](V1-QA-Pass-1-Results.md) — Additional problem **#11** only  
- [`src/McManager.Core/Setup/ManualServerPackAnalyzer.cs`](../src/McManager.Core/Setup/ManualServerPackAnalyzer.cs) — `looksLikeCfExport` / `CanInstall`  
- [`src/McManager.Core/Setup/SetupPackImport.cs`](../src/McManager.Core/Setup/SetupPackImport.cs) — `FromManual` `canContinue`  
- V1 Step **4.12** stays **deferred** (no CurseForge API)

**Do**

- If a CurseForge `manifest.json` lists files but the zip has **no** pre-downloaded mod jars, **block** continue (stronger copy: download Server Files / filled zip). Zero-jar client export is already refused; this is the libraries/installer-without-jars hole.  
- Tests in `McManager.Core.Tests` / Setup pack import tests.

**Test**

- Incomplete CF zip: `CanContinue=false`. Complete Server Files / `.mrpack` still continue. No API.

**Done when:** Wizard cannot proceed on a jar-less CurseForge manifest zip.

**Changelog:** _(empty)_

---

## P8 — Wipe world auto-starts Minecraft (S3-07)

**Status:** TODO  
**Catalog IDs:** S3-07 (Pass 1 **Pass** for leave-stopped; operator 2026-08-19 overrode)  
**Severity:** Minor (operator: must change; not Won't-fix)

**PRODUCT-IDEAS drift:** lab [`PRODUCT-IDEAS.md`](../../OCI-mc-server-manager/PRODUCT-IDEAS.md) **Wipe world (v1)** step 4 still says stop, then the *next* Start creates a world. Pass 1 catalog / Guide / V1 Step 1.3 said leave stopped. **Operator will (this plan):** after wipe, Minecraft **starts again automatically**. Do **not** “correct” this section back to PRODUCT-IDEAS. Follow this plan; note the drift in the reply. Update Guide + catalog S3-07 expected in this same P8 session.

**Read first**

- [`V1-QA-Pass-1-Results.md`](V1-QA-Pass-1-Results.md) — S3-07 row + Additional problem **#6** only  
- [`src/McManager.Hybrid/ViewModels/ServerManagementViewModel.cs`](../src/McManager.Hybrid/ViewModels/ServerManagementViewModel.cs) — wipe action only  
- [`docs/Guide.md`](Guide.md) — Wipe world paragraph  

Do **not** load the full PRODUCT-IDEAS or blueprint except §11.3 if needed for `world_path`.

**Do**

1. After confirmed wipe (live save gone; backups / mods / `server.properties` kept), **start** `minecraft` again (same product Start/Restart path Server Management already uses). VM1 is already RUNNING for wipe.  
2. Guide wipe copy and catalog **S3-07 Expected** were updated 2026-08-19 to auto-start. **Do not revert** them to leave-stopped. Leave Pass 1 results as historical (leave-stopped then override).  
3. Do not delete Object Storage backups. Disable idle while working; Minecraft start force-enables idle (OS-ISSUE-7) — disable again if more work, re-enable at session end.

**Test**

- Catalog **S3-07**: Wipe → Minecraft **running**; join (or journal `Done`) shows a new world. Not left stopped.

**Done when:** Wipe auto-starts Minecraft. Guide + catalog expected match this plan. PRODUCT-IDEAS may still say next-Start — that is noted drift, not a revert.

**Changelog:** _(empty)_

---

## Plan changelog

| Date | Note |
|------|------|
| 2026-08-19 | Created from Pass 1 S0–S2. Operator paused before S3. **NEXT = P1** (STOPPING + UFW/firewalld/dbus). P2 = S2-11 door lock GET. P3 = Function image. Do not implement in the creation session. Do not start S3 or 9.1. |
| 2026-08-19 | **P1 DONE.** OS-ISSUE-9 = firewalld/cloud-init/dbus cycle (not UFW nft). Full firewalld unit override + mask UFW. SoftStop 43s ×3. **NEXT = P2**. Do not start S3 or 9.1. |
| 2026-08-19 | **P2 DONE.** DOOR-ISSUE-10: CLI 3.90 404 = unlocked in `pull_os_budget.sh`; TESTING door script + `mccontrol` rebuilt. S2-11 Pass (wake refuses while locked; 404 absent → `SPEND_BRAKE_LOCK=0`). **NEXT = P3**. Do not start S3 or 9.1. |
| 2026-08-19 | **P3 DONE.** TESTING `shutdown_vm` already 0.0.12 on `mcmgr-fn-softstop` / `mcmgr-fn/softstop:setup`. S2-16–18 Pass (ACTUAL SoftStops VM1 + lock; RESET skipped; door up). FN-ISSUE-1 gone on TESTING (Forge lab still 0.0.11). Pass 1 bug-fix plan has **no further P-sections**. Operator may resume catalog **S3**. Do not start 9.1. |
| 2026-08-19 | **Docs-only triage** of remaining Pass 1 Fails after S3–S7. Added **P4** (S3-04 Minor, confirmed), **P5** (S4-12 Major suggested), **P6** (S5-05 Manager Start Major suggested), optional **P7** (S6-02 UX). Parked Known / Won't-fix / after-v1 in the triage table. **NEXT = none** until operator confirms severity. Do not start 8.6.1 or 9.1. No product code this session. |
| 2026-08-19 | Operator **confirmed** severities: P4 Minor, P5 Major, P6 Major, P7 Minor, timezone parked. **S3-07** overridden to auto-start (**P8** Minor). Authority: operator will + this plan; do not rewrite P8 to match PRODUCT-IDEAS. **NEXT = P4**. Do not start 8.6.1 or 9.1. Docs-only; no product code. |
| 2026-08-19 | **P4 DONE.** Leftover Minecraft prefix CIDRs (TCP+UDP `/9`–`/31`) are managed and stripped; door `wait_forge` TCP-only subnet 25565 + ICMP preserved. `SecurityListIngressPlanTests` cover S3-04. **NEXT = P5**. Do not start 8.6.1 or 9.1. |
