# V1 QA Pass 1 — results

**Pass:** 1  
**Catalog:** `[V1-QA-Catalog.md](V1-QA-Catalog.md)`  
**Dates:** 2026-08-19 (S0, S1, S2, S3, S4, S5, S6, **S7**). **Paused after S2** for OS-ISSUE-9; **P1 DONE** 2026-08-19; **P2 DONE** 2026-08-19 (S2-11); **P3 DONE** 2026-08-19 (S2-16–18). **S3 DONE** 2026-08-19 (one **Minor** Fail: S3-04 leftover `/24`). **S4 DONE** 2026-08-19 (one **Major** Fail suggested: S4-12 name/icon/MOTD not applied). **S5 DONE** 2026-08-19 (one **Major** Fail suggested: S5-05 daily-exhaust path). **S6 DONE** 2026-08-19 (no catalog Fail; incomplete CurseForge zip UX in Additional problems). **S7 DONE** 2026-08-19 (S7-02/S7-03 Pass; S7-04 Skipped — no tofu this round). Pass 1 catalog suites S0–S7 filled. Do not start 8.6.1 or 9.1.  
**Stack:** TESTING (`dotnet run` Hybrid). Do **not** paste OCIDs, Auth Tokens, RCON passwords, or friend IPs.

**How to fill:** For each ID, set **Result** to `Pass` / `Fail` / `Blocked` / `Skipped` / `Known`. On Fail, set **Severity** (`Blocker` / `Major` / `Minor` / `Nit` / `After-v1` / `Won't-fix`) and write expected vs actual under [Failures expanded](#failures-expanded). `Known` needs an Issues.md id.

Suggested fill text for a clean test: `Pass` with notes empty. You do **not** need to type “No issues. Works as expected.”

---



## Session log


| When       | Who              | Suites        | Idle left                                                                    | Notes                                                                                                                                                                                                                                                                                                                                                                                                                                                                                           |
| ---------- | ---------------- | ------------- | ---------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 2026-08-19 | agent            | S0            | n/a (no VM)                                                                  | S0-01–S0-05 recorded; S1/S2 not run this chat                                                                                                                                                                                                                                                                                                                                                                                                                                                   |
| 2026-08-19 | agent            | S1            | **off** (left off for S2 inspect)                                            | STARTed VM1 (was STOPPED). Did not change lock or idle timeout (still 15). S2 chat should disable idle again after any Minecraft start (OS-ISSUE-7).                                                                                                                                                                                                                                                                                                                                            |
| 2026-08-19 | agent            | S2            | **on** (config+timer were force-enabled on last START; VM1 then SoftStopped) | Inspect S2-01–S2-07 while RUNNING. Wake/idle/lock/Function story. Lock **absent** at end (404). Timeout **15**. VM1 **STOPPED**; play IP on door; door `DOOR_IDLE`. TESTING tofu outputs + named SSH key only.                                                                                                                                                                                                                                                                                  |
| 2026-08-19 | operator + docs  | S2 triage     | n/a (VM1 left STOPPED)                                                       | **QA pause.** Do not start S3. Next agent: `[V1-Bug-Fix-Plan-Pass-1.md](V1-Bug-Fix-Plan-Pass-1.md)` **P1** (OS-ISSUE-9). S3 lock PUT waits until after P1.                                                                                                                                                                                                                                                                                                                                      |
| 2026-08-19 | agent            | P2 / S2-11    | n/a (VM1 never STARTed)                                                      | Redeployed TESTING door `pull_os_budget.sh` + rebuilt `mccontrol`. CLI 3.90 404 = `SPEND_BRAKE_LOCK=0` (DOOR-ISSUE-10). Lock fixture: wake refused, VM1 **STOPPED**, door `SPEND_BRAKE`. DELETE + OS-refresh: unlocked, `DOOR_IDLE`. Lock **absent**. Play IP still on door.                                                                                                                                                                                                                    |
| 2026-08-19 | agent            | P3 / S2-16–18 | **on** (re-enabled before SoftStop)                                          | Live Function already `mcmgr-fn-softstop` / `:setup` **0.0.12**. S2-17 ACTUAL SoftStop VM1 + lock PUT; door RUNNING; play IP on door. S2-18 RESET `SKIPPED`. Lock **absent**. VM1 **STOPPED**. Did not start S3.                                                                                                                                                                                                                                                                                |
| 2026-08-19 | agent            | S3 staging    | n/a (VM1 **STOPPED**; idle not touched)                                      | PUT v1 `meta/spend-brake-triggered.json` (S3-01; S2-17 lock was gone) + DEBUG `meta/oversized-world-backup.json` (S3-06). Door RUNNING; play IP on door secondary. **Paused for operator Hybrid clicks** (must use TESTING `MCMANAGER_CONFIG_DIR`, not repo Forge `data/config.local.json`).                                                                                                                                                                                                    |
| 2026-08-19 | operator + agent | S3            | **on** (boot force-enable; timeout 15)                                       | Operator clicked Hybrid against `mcmgr-blank-test`. Overlay/Start/Stop/join/wipe/bell as below. Agent: lock **404**; oversized **DELETED**; leftover `192.0.2.0/24` 25565 TCP+UDP **stripped** from SL (restore). Inspect START (raw Compute; play IP stayed on door) then SOFTSTOP ~67s → **STOPPED**. Door RUNNING; play IP on door.                                                                                                                                                          |
| 2026-08-19 | operator + agent | S4            | **on** (timeout 15)                                                          | Operator clicked Hybrid (`mcmgr-blank-test`). Agent did not watch rows (none require it). After clicks: VM1 was **RUNNING**, Minecraft active, play IP on VM1 secondary; idle already on (S4-18 disable did not survive later Minecraft start — OS-ISSUE-7). Lock **404**. SL: no `0.0.0.0/0` 25565; no leftover `192.0.2.0/24`. Stopped Minecraft; SoftStop → VM1 **STOPPED** (~1 min). Play IP on door secondary; door `DOOR_IDLE`; `mccontrol` + :25565 listen.                              |
| 2026-08-19 | operator + agent | S5            | **on** (timeout **15**; 2 min used for S5-03/S5-04 then restored)            | Operator Minecraft + Hybrid (`mcmgr-blank-test`). Agent did not watch rows (none require it). After clicks: VM1 **STOPPED**; play IP on door secondary; door `DOOR_IDLE`, `mccontrol` active; lock **404**. OS `budget/config.json` already original (monthly 1400, `daily_ocpu_limit_phase_a` ~45.16, idle 15, idle enabled). Door remaining ~41 OCPU-h (not exhausted). No lock PUT/DELETE. Did not START VM1. Did not start S6.                                                              |
| 2026-08-19 | operator + agent | S6            | **on** (timeout 15; not touched)                                             | Operator Hybrid Setup / Connect (`mcmgr-blank-test`). Agent did not watch S6-01–04 (none require OCI/SSH). Did not START VM1. Did not `tofu apply`/`destroy`. After: VM1 **STOPPED**; play IP on **door** secondary; door RUNNING; lock **404**. Idle left on (15). No lock PUT/DELETE. Did not start S7.                                                                                                                                                                                       |
| 2026-08-19 | operator + agent | S7            | **on** (timeout 15; not touched)                                             | Operator already ran S7-02/S7-03 in Hybrid (`mcmgr-blank-test`); S7-04 skipped (no tofu). Agent launched Hybrid with `MCMANAGER_CONFIG_DIR` = that folder (repo `data/config.local.json` unused). Did **not** START VM1. Did not `tofu apply`/`destroy`. Restore check: VM1 **STOPPED** A1.Flex **2/12**; play IP on **door** secondary; door `DOOR_IDLE` `ocpus=2.0`; lock **404**; idle 15 enabled; monthly 1400 / daily ~45.16. Ledger 29 intervals all 2/12 (past intervals not rewritten). |


**Preflight snapshot (S1-03):** 2026-08-19 after VM1 START (no OCIDs / no friend IPs)

- **VM1 lifecycle:** RUNNING (`mcmgr-vm1`; was STOPPED at session start)
- **Door lifecycle:** RUNNING (`mcmgr-door`)
- **Play IP holder:** reserved public IP ASSIGNED to the **door secondary private IP** (that address is not the VNIC primary IP; VNIC itself is the door **primary** VNIC)
- **Spend-brake lock:** **absent** (`meta/spend-brake-triggered.json` HEAD 404)
- **minecraft.service:** **active**
- **Idle:** `idle_agent_enabled=true`, timeout **15** min; `mc-idle-watch.timer` **enabled** + **active** (boot force-enable). S1-04 then disabled it.
- **Door control plane:** `mccontrol` **active**; `/api/status` OK, `door=DOOR_IDLE`, `wake_in_progress=false`. `mcdoor` unit **inactive** (listener is via mccontrol; not a Fail of this snapshot).
- **Security List 25565** `0.0.0.0/0`**:** **no**. Covering rules: admin `/32` TCP+UDP 25565; VCN `10.0.0.0/24` TCP 25565 (door `wait_forge` poll).

---



## S0 — Automated


| ID    | Result  | Severity | Notes                                                                                                                             |
| ----- | ------- | -------- | --------------------------------------------------------------------------------------------------------------------------------- |
| S0-01 | Pass    |          | 226 passed, 0 failed (`McManager.Core.Tests`). Restore warning NU1903: SSH.NET 2024.2.0 GHSA-q939-rpr3-3284 (not a test failure). |
| S0-02 | Pass    |          | 11 tests OK (`functions/shutdown_vm/test_func.py`).                                                                               |
| S0-03 | Pass    |          | 19 tests OK (`functions/reconcile_usage/test_func.py`).                                                                           |
| S0-04 | Pass    |          | `tofu validate` in `infra/`: configuration is valid (no apply).                                                                   |
| S0-05 | Skipped |          | Optional. No `gcc`/`make` on Windows PATH. WSL default distro is `docker-desktop` (Stopped); `wsl -e bash` failed (no bash).      |




## S1 — Preflight


| ID    | Result | Severity | Notes                                                                                                                                                                                                                                                                                     |
| ----- | ------ | -------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| S1-01 | Pass   |          | `oci os ns get --profile TESTING` succeeded. Namespace matches TESTING tofu outputs, not lab `config.local.json` (that file still has profile DEFAULT / live Forge). Region `us-sanjose-1` matches. `[TESTING]` is a distinct tenancy from `[DEFAULT]`.                                   |
| S1-02 | Pass   |          | VM1 was STOPPED; START + wait RUNNING (Always Free). SSH `ubuntu` + named key: both `hostname` OK (`mcmgr-vm1`, `mcmgr-door`). `sudo -n true` OK on both.                                                                                                                                 |
| S1-03 | Pass   |          | See preflight snapshot above. Door RUNNING; 25565 not world-open. Play IP on door secondary (idle doorbell). Minecraft active; idle was on at snapshot then disabled in S1-04.                                                                                                            |
| S1-04 | Pass   |          | After VM1 START: set `idle_agent_enabled=false`; `mc-idle-watch.timer` stop+disable. Verified: enabled=False, timer disabled/inactive. Timeout left at 15.                                                                                                                                |
| S1-05 | Pass   |          | Lock was already absent (not created this session). Idle timeout unchanged (15). **Idle left off** for the following S2 chat (inspect while RUNNING). VM1 left RUNNING. Re-enable idle at end of S2 unless S2-09 still needs it, or after any Minecraft start disable again (OS-ISSUE-7). |




## S2 — Agent on-box / cloud


| ID     | Result  | Severity | Notes                                                                                                                                                                                                                                                                                                                                                                                                   |
| ------ | ------- | -------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| S2-01  | Pass    |          | `User=mcmgr`, `WorkingDirectory=/opt/mcmgr/server`. `namei`: `/opt/mcmgr` `root:mcmgr` 0750; `server` `mcmgr:mcmgr` 0750. Matches `onbox/mcmgr` layout (SETUP-ISSUE-4 not recurring).                                                                                                                                                                                                                   |
| S2-02  | Pass    |          | `/etc/mcmgr/game-manifest.json` valid JSON. `distribution=vanilla`, `loader=null`, `java_major=25`, `minecraft_version=26.2`. Matches TESTING tofu `server_kind=vanilla`.                                                                                                                                                                                                                               |
| S2-03  | Pass    |          | Java listens `0.0.0.0:25575` (manifest claims `127.0.0.1`; vanilla `server.properties` has no bind). SL has **no** 25575 ingress. Equivalent not public.                                                                                                                                                                                                                                                |
| S2-04  | Pass    |          | `white-list=false` in `server.properties`.                                                                                                                                                                                                                                                                                                                                                              |
| S2-05  | Pass    |          | **P1 2026-08-19:** S1 19:10 boot was a firewalld/cloud-init **ordering cycle** — systemd deleted dbus (not UFW nft; UFW was enabled-as-unit / `ENABLED=no`). After product override + mask UFW: every Compute START this session had dbus+firewalld **active**, ports `25565/tcp 25565/udp`, `netfilter-persistent` masked, `ufw` masked/inactive, no `Job dbus.*.deleted`.                             |
| S2-06  | Pass    |          | No Minecraft `0.0.0.0/0`. SSH `/32` admin only. 25565: admin `/32` TCP+UDP; VCN `10.0.0.0/24` TCP (door `wait_forge`). ICMP type 3 code 4 world + type 3 VCN still present. No 25575.                                                                                                                                                                                                                   |
| S2-07  | Pass    |          | `mccontrol` active. `/api/status` JSON: `door=DOOR_IDLE`, `wake_in_progress=false`. Netplan `99-mcmgr-play.yaml`; guest secondary `10.0.0.102/24` on `ens3`. Reserved play IP ASSIGNED to door secondary private IP.                                                                                                                                                                                    |
| S2-08  | Pass    |          | Wake **eventually** OK on the **second** try. First SOFTSTOP hung STOPPING ~17 min — **OS-ISSUE-9** (not a Fail of the wake itself). See Failures expanded.                                                                                                                                                                                                                                             |
| S2-09  | Pass    |          | Saved timeout 15 → set 2, idle on. Empty Minecraft SoftStopped; VM1 **STOPPED** within ~3 min (this boot had dbus/firewalld up). Play IP on door secondary. Door RUNNING, `DOOR_IDLE`, `mccontrol` active, :25565 listen. Restored timeout 15 after S2-11 accidental START (SSH).                                                                                                                       |
| S2-09b | Skipped |          | Optional 15-min clock confirmation. S2-09 2-min path already Pass.                                                                                                                                                                                                                                                                                                                                      |
| S2-10  | Pass    |          | `sudo env HOME=/home/ubuntu /opt/mccontrol/oci/heal_os_ledger.sh` while STOPPED: `HEAL_SKIP no_open_intervals` / `HEAL_OS_OK closed=0`. No `HOME: unbound`. Idle path had already closed.                                                                                                                                                                                                               |
| S2-11  | Pass    |          | **P2 2026-08-19:** Product script already GETs the lock; live TESTING was stale then 404-mismatch (DOOR-ISSUE-10). After redeploy: PUT v1 lock → `POST /api/wake` → VM1 **STOPPED**, door `SPEND_BRAKE`, `last_error=monthly spend brake fired`, journal `SPEND_BRAKE_LOCK=1` (no `start_vm1`). DELETE + `/api/os-refresh` → `SPEND_BRAKE_LOCK=0`, `DOOR_IDLE`. Absent-lock pull: `SPEND_BRAKE_LOCK=0`. |
| S2-12  | Skipped |          | Optional. Same live-script gap as S2-11; another wake would START again. Not a separate repro.                                                                                                                                                                                                                                                                                                          |
| S2-16  | Pass    |          | **P3 2026-08-19:** Live `mcmgr-fn-softstop` ACTIVE; OCIR `mcmgr-fn/softstop:setup` (1 image). Pulled `linux/arm64`: `func.yaml` **0.0.12**, RESET skip + lock PUT, env `INSTANCE_OCIDS`. Function config: VM1 only (door not listed). Docker Desktop was up; `fn` CLI still absent — no rebuild. Did not `tofu apply`. Not Step 8.6.1.                                                                  |
| S2-17  | Pass    |          | Synthetic ACTUAL invoke: `SUCCESS` / SoftStop **one** instance (VM1) + lock PUT HTTP 200. VM1 **STOPPED ~57s**. Lock JSON v1 `source=budget_function` `reason=compartment_budget_threshold` `alert_type=ACTUAL`. Door **RUNNING**. Play IP stayed on door secondary. FN-ISSUE-1 gone on TESTING. DELETE lock after.                                                                                     |
| S2-18  | Pass    |          | STARTed VM1, idle off. RESET invoke: `SKIPPED` / `Monthly budget reset event`. After ~30s VM1 still **RUNNING**, lock still **absent** (404). Extra: PUT lock + RESET left object in place (`triggered_at` unchanged); then DELETE.                                                                                                                                                                     |
| S2-19  | Skipped |          | Optional. Too risky to exhaust shared daily cap on this stack.                                                                                                                                                                                                                                                                                                                                          |
| S2-20  | Pass    |          | After idle SoftStop, play IP on door. Raw `oci compute instance action START` (not door wake) → VM1 RUNNING; play IP **stayed on door** secondary. Boot force-enabled idle (left **on**). SoftStop at session end so doorbell matches.                                                                                                                                                                  |
| S2-21  | Pass    |          | SSH localhost RCON `list`: AUTH_OK, `There are 0 of a max of 20 players online`. No SL change.                                                                                                                                                                                                                                                                                                          |
| S2-22  | Pass    |          | After S2-28 boot ledger: `lease.json` `active=true`, `last_heartbeat_at=2026-08-19T19:24:20Z` (boot record). Shape 2 OCPU / 12 GB. Idle was off during inspect so no minute ticks until force-enable.                                                                                                                                                                                                   |
| S2-26  | Skipped |          | Optional. No `reconcile_usage` Function on TESTING (same empty Function app as S2-16). Did not invoke Forge lab.                                                                                                                                                                                                                                                                                        |
| S2-28  | Pass    |          | Idle was off. `systemctl restart minecraft` → `mc-boot-ledger` rewrote `idle_agent_enabled=true`; timer enabled+active. Documented OS-ISSUE-7 safety (not a bug).                                                                                                                                                                                                                                       |




## S3 — Hybrid


| ID    | Result | Severity | Notes                                                                                                                                                                                                                                                                                     |
| ----- | ------ | -------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| S3-01 | Pass   |          | Full-window overlay on open (TESTING `mcmgr-blank-test`). Typed confirm unblocked Start. After confirm: lock **404**. Play IP later parked on door (S3-03).                                                                                                                               |
| S3-02 | Pass   |          | Overlay confirm woke VM1. Status **Starting…** then **Running**. Door-aware Start (not raw Compute).                                                                                                                                                                                      |
| S3-03 | Pass   |          | Top-bar Stop did not hang. **Stopping…** ~20s then **Stopped**. After: VM1 **STOPPED**; play IP on door secondary; door `DOOR_IDLE`; `mccontrol` + :25565 listen.                                                                                                                         |
| S3-04 | Fail   | Minor    | Save of named `192.0.2.0/32` + `192.0.2.0/24` pushed both to SL (ICMP kept). Revert removed the `/32` but **left** `192.0.2.0/24` TCP+UDP 25565 (`description=test range`). Local friends were admin-only. Agent stripped the leftover `/24` after. See Failures expanded.                |
| S3-05 | Pass   |          | Java Vanilla join OK. Journal: player login ~23:03Z (pre-wipe spawn) then again after Restart on a **new** spawn.                                                                                                                                                                         |
| S3-06 | Pass   |          | Bell warned world too large for cloud backup. Server Management copy: cloud backups paused / latest download over **SSH**, not OS PUT. Fixture **DELETED** after (404).                                                                                                                   |
| S3-07 | Pass   |          | Wipe stopped Minecraft and deleted the live world. **Left stopped** is intentional ([Guide](Guide.md); V1 Step 1.3). Operator **Restart** then joined: fresh gen (~16s `Done`) and new spawn; `server.properties` kept; no `mods/` (Vanilla); Object Storage `backups/` still had 4 zips. |




## S4 — Operator Manager UI


| ID    | Result | Severity | Notes                                                                                                                                                                                                                                                                                                       |
| ----- | ------ | -------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| S4-01 | Pass   |          |                                                                                                                                                                                                                                                                                                             |
| S4-02 | Pass   |          |                                                                                                                                                                                                                                                                                                             |
| S4-03 | Pass   |          |                                                                                                                                                                                                                                                                                                             |
| S4-08 | Pass   |          | Agent restore check: no leftover TEST-NET `192.0.2.0/24` on 25565 (S3-04 leftover still gone).                                                                                                                                                                                                              |
| S4-09 | Pass   |          |                                                                                                                                                                                                                                                                                                             |
| S4-10 | Pass   |          |                                                                                                                                                                                                                                                                                                             |
| S4-11 | Pass   |          | Vanilla/Paper empty “not modded” note present. Live **Modding** inspect / Download pack deferred until a later **modded** redeploy (this TESTING stack is Vanilla). Not a product Fail of the vanilla expected.                                                                                             |
| S4-12 | Fail   | Major    | Name, description, and icon did not appear in the Minecraft client server browser after save + start, after a Minecraft restart, or after save-again while VM1/Minecraft were already running + another restart. Door MOTD not in scope. See Failures expanded. **Severity suggested — confirm at triage.** |
| S4-13 | Pass   |          |                                                                                                                                                                                                                                                                                                             |
| S4-14 | Pass   |          |                                                                                                                                                                                                                                                                                                             |
| S4-15 | Pass   |          |                                                                                                                                                                                                                                                                                                             |
| S4-16 | Pass   |          |                                                                                                                                                                                                                                                                                                             |
| S4-17 | Pass   |          |                                                                                                                                                                                                                                                                                                             |
| S4-18 | Pass   |          | Idle disable UI OK. On-box idle was **on** again at session restore (Minecraft start force-enable, OS-ISSUE-7). Timeout still 15.                                                                                                                                                                           |
| S4-19 | Pass   |          | no live resize                                                                                                                                                                                                                                                                                              |
| S4-20 | Pass   |          | do not delete                                                                                                                                                                                                                                                                                               |
| S4-21 | Pass   |          |                                                                                                                                                                                                                                                                                                             |
| S4-22 | Pass   |          |                                                                                                                                                                                                                                                                                                             |




## S5 — Play path


| ID    | Result | Severity | Notes                                                                                                                                                                                                                                                                                                                                 |
| ----- | ------ | -------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| S5-01 | Pass   |          | Door MOTD while VM1 stopped / IP on door.                                                                                                                                                                                                                                                                                             |
| S5-02 | Pass   |          | Wake from client connect. First connect showed idle kick **Server offline. Connect to wake the world.** (wake did start). Copy nit: that kick should say wake is already triggered. Not a Fail of this row. See Additional problems. First-kick still in the DOOR-ISSUE-1 neighborhood (S8-01 Known).                                 |
| S5-03 | Pass   |          | 2-minute idle timeout. Occupied server was not SoftStopped.                                                                                                                                                                                                                                                                           |
| S5-04 | Pass   |          | Player-view confirmation despite S2-09 Pass. 2-minute timeout. Hybrid Status did not update after idle SoftStop until the window was focused. See Additional problems (not a catalog Fail of idle itself).                                                                                                                            |
| S5-05 | Fail   | Major    | Optional daily-exhaust. Distinct **daily** copy (not spend-brake) eventually appeared after one connect. Manager Start was also refused; MOTD lagged until that connect; no in-game chat warnings on a sudden cap drop; MOTD reset time is PT midnight vs UTC day. See Failures expanded. **Severity suggested — confirm at triage.** |




## S6 — Setup / Connect


| ID    | Result | Severity | Notes                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                           |
| ----- | ------ | -------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| S6-01 | Pass   |          | Walked Setup pages. **Did not click Deploy.**                                                                                                                                                                                                                                                                                                                                                                                                                                                                   |
| S6-02 | Pass   |          | `.mrpack` / server-pack analyze + summary. Incomplete CurseForge zip (no pre-downloaded jars) **warned** but still allowed continue — see Additional problems (not a catalog Fail of this row).                                                                                                                                                                                                                                                                                                                 |
| S6-03 | Pass   |          | Auto-detect (button) found the **TESTING** stack. Forge **lab** was not listed (likely missing Object Storage `meta/infra.json` / outdated lab — Connect-existing only hydrates that object). Setup step 2 profile picker **does** list `DEFAULT`. Catalog has no Partial; this is Pass of the TESTING path, not a Fail that the live lab was skipped. Version-skew refuse/extra-confirm not exercised (no newer-than-app stack; lab never became a candidate). No silent probe on launch (S4-15 already Pass). |
| S6-04 | Pass   |          | Resume / Deploy-repair path. No second apply.                                                                                                                                                                                                                                                                                                                                                                                                                                                                   |
| S6-05 | Pass   |          | Documented `MCMANAGER_TOFU_DRY_RUN=1` fake runner ([Local-Config.md](Local-Config.md)). Operator: Pass (both vanilla flavors in plan). No live `tofu apply`.                                                                                                                                                                                                                                                                                                                                                    |




## S7 — Destructive


| ID    | Result  | Severity | Notes                                                                                                                                                                                                                                                                                                                                    |
| ----- | ------- | -------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| S7-02 | Pass    |          | Operator: 2/12 → 4/24 → back to 2/12, both applies worked. Agent restore: live `VM.Standard.A1.Flex` **2 OCPU / 12 GB**; `mcmgr-blank-test` `vm1.shape_`* 2/12; OS `budget/config.json` 2/12; `meta/infra.json` vm1 2/12. Ledger **29** intervals all **2/12** (no 4/24 rewrite — resize while STOPPED). Door `/api/status` `ocpus=2.0`. |
| S7-03 | Pass    |          | Operator world replace from a zip they own.                                                                                                                                                                                                                                                                                              |
| S7-04 | Skipped |          | Operator: no full Delete + greenfield this round. After first-round testing and triage, a fresh deployment for pass 2. No `tofu destroy`/`apply` this chat.                                                                                                                                                                              |




## S8 — Known-issue checks


| ID    | Result | Severity | Notes                                                                                                        |
| ----- | ------ | -------- | ------------------------------------------------------------------------------------------------------------ |
| S8-01 | Known  |          | DOOR-ISSUE-1 still parked. S5-02 first connect showed idle kick, not the custom “starting / try again” kick. |
| S8-02 |        |          | FN-ISSUE-1 on TESTING                                                                                        |
| S8-03 |        |          | OS-ISSUE-7 docs                                                                                              |
| S8-04 |        |          | SETUP-ISSUE-7                                                                                                |


---



## Failures expanded

Copy one block per **Fail** (or Blocked that should become a fix):

### S2-05 — firewalld 25565 after boot

- **Severity:** Major (suggested; operator confirms in triage)
- **Expected:** 25565 allowed in firewalld. `netfilter-persistent` not fighting firewalld after SoftStop reboot (SETUP-ISSUE-7).
- **Actual:** `netfilter-persistent` is **masked** (that half of SETUP-ISSUE-7 holds). After S1 START (boot 2026-08-19 19:10 UTC), `firewalld.service` was enabled but **inactive (dead)** with **no** journal that boot. `dbus.socket` / `dbus.service` also **inactive**; host `INPUT` policy **ACCEPT**. After S2-08 door wake and S2-20 raw Compute START, `dbus` and `firewalld` were **active** and `firewall-cmd` listed `25565/tcp 25565/udp`. Intermittent (saw it on the S1 START boot), not the documented netfilter fight.
- **P1 (2026-08-19):** UFW was **not** the job-deletion cause. `/etc/ufw/ufw.conf` `ENABLED=no`; `ufw.service` was still systemd-enabled (oneshot). Root cause: **firewalld** `Before=network-pre.target` **+** `After=dbus.service` **races cloud-init** (Debian #1025618). Boot -6 journal: `Job dbus.service/start deleted` and `Job dbus.socket/start deleted`. Product SoT: mask UFW; full `/etc/systemd/system/firewalld.service` override (drop-ins cannot reset `Before=`). Re-check after Compute START: dbus+firewalld active, 25565 listed, ufw masked.
- **Repro:** START VM1 from STOPPED (S1), SSH, `sudo systemctl is-active firewalld dbus.socket`; `sudo firewall-cmd --list-all`; also `systemctl is-enabled ufw; sudo ufw status verbose`.
- **Evidence:** `journalctl -b -u firewalld` empty on the S1 START boot; `iptables -S INPUT` → `-P INPUT ACCEPT`; `systemctl is-enabled netfilter-persistent` → `masked`. Wake boot: `firewall-cmd --list-all` showed `25565/tcp 25565/udp`. UFW state: **unknown** (not queried).



### OS-ISSUE-9 — VM1 SoftStop stuck STOPPING (QA interrupt)

- **Severity:** Blocker (operator: fix before S3)
- **Expected:** `SOFTSTOP` reaches **STOPPED** in a few minutes so wake/idle tests and heal (STOPPED-only) can proceed. Do not wake while still STOPPING.
- **Actual:** On the S1 START boot (dbus/firewalld **down**), SoftStop sat in **STOPPING ~17 min**. OCI waiter `--max-wait-seconds 600` exited **2**. Hard `STOP` while still STOPPING returned **409** “currently being modified”. A wake issued after API first showed STOPPED still overlapped leftover STOPPING (door PLAYABLE/TCP, then IP back on door; second STOPPING ~16 min). Later SoftStops on boots where dbus/firewalld were **up** finished in **~1–3 min**. S2-08 **Pass** is the second (clean) wake only.
- **P1 (2026-08-19):** Hypothesis replaced. dbus-down was systemd **deleting dbus** to break the firewalld/cloud-init cycle, so ACPI/logind never ran. UFW discarded as the cycle trigger (masked as firewalld-only SoT). After override: SoftStop → STOPPED in **43s** ×3. Do not start S3 until operator resumes; P2 is next.
- **Repro:** After a Compute START, confirm dbus/firewalld (and UFW). `oci compute instance action --action SOFTSTOP` (TESTING). Poll lifecycle with waiter-style backoff (not 1s). Do not START/wake until **STOPPED**.
- **Evidence:** Timeline below (UTC, 2026-08-19, TESTING VM1). Door stayed RUNNING. No OCIDs.

**Timeline (do not wake during STOPPING)**


| UTC                                | What                                                                                                                                                                                                             |
| ---------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| ~19:10                             | S1 `START` boot. `journalctl -b -u firewalld` **empty**. `dbus.socket` / `dbus.service` **inactive**. `netfilter-persistent` **masked**. Host `INPUT` **ACCEPT**. UFW **not checked**.                           |
| ~19:25                             | `SOFTSTOP` while that boot was still up. **STOPPING** for the entire 600s waiter (CLI **exit 2**). GETs after timeout still STOPPING.                                                                            |
| ~19:39                             | Hard `STOP` → **409** instance currently being modified.                                                                                                                                                         |
| ~19:42                             | Finally **STOPPED** (~**17 min**). Door reconcile: play IP already on door secondary; `HEAL_OS_OK`.                                                                                                              |
| ~19:43                             | First `POST /api/wake`. Door: STARTING → guest RUNNING in ~7s; `wait_forge` TCP OK ~33s; briefly **PLAYABLE**. Concurrent Compute GET still **STOPPING**. `ip_to_vm2` raced; play IP ended back on the **door**. |
| ~19:43–~20:00                      | Second **STOPPING** hang (~**16 min**).                                                                                                                                                                          |
| ~20:03                             | After ~30s settle, **second wake** clean: RUNNING, play IP on VM1 secondary, `PLAYABLE`. This boot: dbus+firewalld **active**.                                                                                   |
| S2-09                              | Idle timeout 2, dbus up → SoftStop in ~**3 min**.                                                                                                                                                                |
| S2-20 prep / session-end SoftStops | dbus up → STOPPED in ~**1–1.5 min**.                                                                                                                                                                             |
| S2-20 raw START                    | dbus+firewalld **active** (S2-05 was **not** every Compute START).                                                                                                                                               |


**Where to look next:** `journalctl --list-boots` and the 19:10 boot if retained; `shutdown` / `final.target` / `minecraft.service` stop; `systemctl status dbus ufw firewalld`; `systemctl cat ufw firewalld`; `ufw status verbose`; nft leftover; live `systemctl list-jobs` if STOPPING reproduces. Fix product SoT (`infra/cloud-init`, Setup guest firewall repair), not only the test VM.

### S2-11 — Lock fixture refuses wake (no Function)

- **Severity:** Major (suggested; operator confirms in triage)
- **Expected:** PUT v1 `meta/spend-brake-triggered.json`, `POST /api/wake`, VM1 **stays STOPPED**. Door RUNNING. MOTD/journal **MONTHLY SPEND BRAKE FIRED** (not daily). After DELETE + OS-refresh, unlocked.
- **Actual:** Lock PUT succeeded (etag present). Wake still ran `start_vm1` (`lifecycle=STARTING` then `VM1 RUNNING after 12s`). Live door script `/opt/mccontrol/oci/pull_os_budget.sh` header is still “ledger + budget” only — **no** `meta/spend-brake-triggered.json` GET and no `SPEND_BRAKE_LOCK=` lines (product `door_vm/oci/pull_os_budget.sh` has them). `PULL_OS_OK ledger=1 budget=1` then start. Door stayed RUNNING. Idle was force-enabled; tester disabled idle and restored timeout 15. Lock **DELETED** after.
- **Repro:** PUT contract JSON; `POST /api/wake` on TESTING door without redeploying `door_vm` 2.3 scripts.
- **Evidence:** Live script dump vs product SoT; mccontrol journal `start_vm1: VM1 RUNNING after 12s` while lock object existed.
- **P2 (2026-08-19):** Two stacked gaps. (1) Live script was pre-2.3 at S2-11. (2) After the GET landed, OCI CLI 3.90+ 404 is `"status": 404` / `error code 404` (`code` null), not `ObjectNotFound` — grep miss → fail-closed when unlocked (DOOR-ISSUE-10). Product SoT grep updated; TESTING script + `mccontrol` rebuilt. Re-run: lock present refuses START; 404 absent → `SPEND_BRAKE_LOCK=0`.



### S3-04 — Allowlist Save revert leftover `/24`

- **Severity:** Minor (operator: not major; overlapping CIDR edge case)
- **Expected:** After removing the test entries and Save, Security List Minecraft rules match Desired List. ICMP / non-owned rules kept. Test `/32` **and** Advanced `/24` gone.
- **Actual:** First Save pushed both `192.0.2.0/32` and `192.0.2.0/24` (TCP+UDP 25565; descriptions named). Revert + Save removed the `/32`. **Left** `192.0.2.0/24` TCP+UDP 25565 `description=test range`. Local `friends.local.json` was already admin-only. ICMP type 3 and VCN `wait_forge` 10.0.0.0/24 TCP 25565 still present (good). No `0.0.0.0/0` 25565.
- **Why:** `SecurityListIngressPlanner.IsManagedRule` strips leftover Minecraft/SSH/door only when `FriendRules.IsSingleHostCidr` (`/32`). Prefix leftovers are not “owned” once the friend name is gone (`McDescription` is the friend name, not `mc-whitelist:`), so they are **preserved**. Overlap with a `/32` of the same network is not required — any leftover prefix would stick.
- **Restore:** Agent `UpdateSecurityList` dropped the two `192.0.2.0/24` rules (TESTING). Ingress count 9 → 7. Do not leave TEST-NET on the play port.
- **Repro:** Whitelist add a named `/32` and a named `/24` (same TEST-NET), Save, remove both, Save. GetSecurityList: `/24` 25565 still present.
- **Evidence:** TESTING SL after operator revert (2026-08-19): `192.0.2.0/24` proto 6 and 17, desc `test range`. Planner: `IsManagedRule` returns false when source is not a single host.



### S4-12 — Name / icon / messages

- **Severity:** Major (suggested; operator confirms in triage)
- **Expected:** After Save, a Minecraft restart applies the Server Management name, description (plain-text MOTD / list name), and optional 64×64 PNG icon in the Java client server browser. Door-off MOTD is **not** edited here.
- **Actual:** Operator changed name, description, and icon while VM1 and Minecraft were off. After starting VM1 and Minecraft, the client server browser still showed the old identity. Restarting Minecraft did not apply it. Saving again while VM1 was on and Minecraft was running, then restarting Minecraft again, still did not apply it.
- **Repro:** TESTING Hybrid (`mcmgr-blank-test`). Server Management → change name + description + icon → Save. Start VM1/Minecraft if stopped, or Restart if already running. Add/refresh the play IP in the Java multiplayer list. Identity unchanged.
- **Evidence:** Operator client observation only this session (no Object Storage `messages/chat.json` dump). Product path is `messages/chat.json` + optional `messages/server-icon.png`; VM1 `record_boot.py` is supposed to apply on Minecraft start (V1 Step 7.6).



### S5-05 — Daily exhausted copy (optional)

- **Severity:** Major (suggested; operator confirms in triage)
- **Expected:** Temporarily lower daily cap. Kick/MOTD is **daily**, not spend-brake (`MONTHLY SPEND BRAKE FIRED`). Restore cap. PRODUCT-IDEAS: after daily exhaustion, **door refuses player wake**; **admin can still Start from Manager**.
- **Actual (operator, sudden cap drop on TESTING; natural approach not tried):**
  1. **Copy distinction (eventual Pass):** After one Minecraft connect while VM1 was down, wake was refused and the icon/MOTD became daily-usage-limit copy, not spend-brake. Catalog “distinct strings” half eventually matched.
  2. **MOTD lag:** While VM1 was already stopped, door MOTD did **not** mention daily exhaustion until after that first connect. Then it rejected start and updated icon/MOTD. Matches door pull-on-wake (OS-ISSUE-4), but the list ping stayed stale until a join attempt.
  3. **In-game chat:** Lowering the cap **while the server was on** produced **no** budget-limit chat messages. Operator notes the **lab** usage warning works on a natural approach; this pass only tested a sudden cap drop. Unproven whether TESTING would warn if remaining time crossed 30/5 min naturally.
  4. **Manager Start blocked (product-intent Fail):** With the daily limit reached, **Start from Manager was also rejected**. PRODUCT-IDEAS: *only admin via Manager* after daily exhaustion; player client wake stays refused. Manager Start currently shares the door wake gate.
  5. **Reset time:** MOTD says **12:00 AM PT**. Operator: daily reset is **00:00 UTC**. Door `/api/status` `reset_at_utc` was next **07:00Z** (midnight PDT). Frozen contract is UTC day windows; deployed door still uses America/Los_Angeles midnight for MOTD (`mcdoor.c` `format_la_reset_date`).
- **Restore:** Operator republished original budget (or equivalent). Agent 2026-08-20 UTC: OS `budget/config.json` already monthly 1400 / `daily_ocpu_limit_phase_a` ~45.16 / idle 15 / idle enabled. Door `DOOR_IDLE`, remaining ~41 OCPU-h, idle 15. Lock 404. VM1 **STOPPED**; play IP on door. No further PUT.
- **Repro:** TESTING Hybrid (`mcmgr-blank-test`). Usage publish a daily cap below today’s used hours while Minecraft is running (no chat). Stop VM1. Ping play IP (MOTD still idle). Connect once (refuse + daily MOTD/icon). Click Manager Start (also refused).
- **Evidence:** Operator client + Manager. Agent restore: door status JSON `daily_limit_ocpu_hours` ~45.16, `DOOR_IDLE`. No OCIDs.

---



## Additional problems

Anything not tied to a catalog ID: confusing copy, slow UI, “I also noticed…”, questions about intended behavior. Questions are **not** bugs until triage.

1. S0-01 restore printed NU1903: `SSH.NET` 2024.2.0 has GHSA-q939-rpr3-3284. Tests still passed. Not a catalog Fail; revisit before Phase 9 packaging.
2. Product `data/config.local.json` is still the live Forge lab seed (`oci.profile` DEFAULT). S1 used TESTING CLI + `%LOCALAPPDATA%\McManager\tofu\mcmgr\outputs.json` for OCIDs/SSH hosts. Later S2/S3 agents must not treat that JSON as the test stack.
3. **OS-ISSUE-9:** First SOFTSTOP after the S1 START boot sat in STOPPING ~17 min. Promoted to Failures expanded + bug-fix **P1**. Do not start S3 until P1 is DONE.
4. **P3 DONE:** TESTING Function is v1 (`mcmgr-fn-softstop` / `:setup` 0.0.12). S2-17 lock was **DELETED**. S3-01 overlay used a **fresh PUT** fixture. **P2 DONE:** TESTING door honors the lock (DOOR-ISSUE-10). Live Forge lab door/Function may still be pre-v1.
5. **S3 Hybrid must use** `MCMANAGER_CONFIG_DIR` **=** `mcmgr-blank-test`**.** Repo `data/config.local.json` is still Forge / `DEFAULT`.
6. **Wipe auto-start (operator 2026-08-19):** Operator wants Minecraft to start again after Wipe world. Pass 1 catalog / Guide / Step 1.3 recorded leave-stopped. Promoted to bug-fix **P8** (Minor). PRODUCT-IDEAS Wipe world step 4 may still say next-Start — follow P8; note drift.
7. **S4-11 modded Modding panel:** Vanilla “not modded” note Pass. Operator will redeploy a **modded** stack for a later pass to cover live `mods/` inspect and **Download pack** (original `data/imported-packs/` archive). Not a Fail of this Vanilla TESTING stack.
8. **S5-02 wake kick copy (nit, not a Fail):** First connect while stopped showed **Server offline. Connect to wake the world.** Wake had already started. Operator wants that kick to say the wake is already triggered. S5-02 Pass. Related parked **DOOR-ISSUE-1** (S8-01 Known).
9. **S5-04 Hybrid Status stale until focus (nit, not a Fail):** If the idle agent SoftStops VM1 while Manager is open, Status stays Running until the operator clicks back to the window (refresh on focus). S5-04 Pass for the idle/player-view path.
10. **S5-05 sudden vs natural daily cap:** This pass only suddenly lowered the daily cap. In-game 30/5 min warnings untested on TESTING; operator says they work on the live Forge lab. Do not treat “lab works” as a TESTING Pass of chat warnings.
11. **S6-02 incomplete CurseForge zip (P7 Minor):** A CurseForge `.zip` without pre-downloaded `.jar` files was detected and **warned**, but the wizard still allowed continue. Operator confirmed hard-block. Not CurseForge API (4.12 deferred).

---



## Triage notes (operator + agent, docs-only session)


| Catalog ID                 | Keep as fix?          | Plan section id         | Notes                                                                                                                                                                                                                                                                                                                                                                                                                                                        |
| -------------------------- | --------------------- | ----------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| S2-05                      | Yes                   | P1 **DONE**             | Ordering cycle, not UFW nft; firewalld unit override + mask UFW                                                                                                                                                                                                                                                                                                                                                                                              |
| OS-ISSUE-9 (STOPPING hang) | Yes                   | **P1 DONE**             | Firewalld/cloud-init/dbus cycle; SoftStop 43s after fix                                                                                                                                                                                                                                                                                                                                                                                                      |
| S2-11                      | Yes                   | **P2 DONE**             | Live door missing GET then CLI 3.90 404 mismatch (DOOR-ISSUE-10); TESTING redeployed                                                                                                                                                                                                                                                                                                                                                                         |
| S2-16–18                   | Yes (env)             | **P3 DONE**             | TESTING already had 0.0.12 image + Function; S2-16–18 Pass. FN-ISSUE-1 gone on TESTING                                                                                                                                                                                                                                                                                                                                                                       |
| S3-04                      | Yes (Minor)           | **P4 NEXT**             | Leftover Minecraft **prefix** CIDR after allowlist revert (`IsManagedRule` `/32`-only). SL restored.                                                                                                                                                                                                                                                                                                                                                         |
| S3-07 auto-start           | **Yes (Minor)**       | **P8**                  | Operator override: start Minecraft after wipe. PRODUCT-IDEAS / Pass 1 expected leave-stopped — follow the bug-fix plan.                                                                                                                                                                                                                                                                        |
| S4-11 modded half          | Defer                 | —                       | Vanilla empty-state Pass. Live Modding inspect waits for a later modded redeploy.                                                                                                                                                                                                                                                                                                                                                                            |
| S4-12                      | Yes (Major)           | **P5**                  | Name/description/icon never appeared in the Java server browser after save + start/restart. Operator confirmed Major 2026-08-19.                                                                                                                                                                                                                                                                                                                               |
| S5-02 first-kick copy      | Nit / Known           | DOOR-ISSUE-1            | Idle kick on first connect; operator wants “wake already triggered”. S5-02 Pass.                                                                                                                                                                                                                                                                                                                                                                             |
| S5-04 Status refresh       | Nit                   | —                       | Hybrid Status does not update after idle SoftStop until window focus. S5-04 Pass.                                                                                                                                                                                                                                                                                                                                                                            |
| S5-05                      | Yes (Major)           | **P6**                  | Manager Start refused when daily exhausted (admin Start must work). Distinct copy after one connect (leave). MOTD lag Known OS-ISSUE-4. Sudden-cap chat Won't-fix. Timezone parked.                                                                                                                                                                                                                                                                           |
| S6-02 incomplete CF zip    | Yes (Minor)           | **P7**                  | Warned but allowed continue without jars. Hard-block until jars are in the zip. Not CurseForge API. S6-02 catalog row was Pass.                                                                                                                                                                                                                                                                                                                                |
| S6-03 lab not listed       | No (not a Fail)       | —                       | TESTING detected. Lab skipped without `meta/infra.json`. `DEFAULT` still in profile picker.                                                                                                                                                                                                                                                                                                                                                                  |
| S7-02 / S7-03              | No (Pass)             | —                       | Shape scale restored to original 2/12; world replace Pass.                                                                                                                                                                                                                                                                                                                                                                                                   |
| S7-04                      | Skip                  | —                       | Operator deferred Delete + greenfield until after first-round triage.                                                                                                                                                                                                                                                                                                                                                                                        |


Pass 1 bug-fix plan: `[V1-Bug-Fix-Plan-Pass-1.md](V1-Bug-Fix-Plan-Pass-1.md)` (**P1–P3 DONE**). Remaining: **P4 NEXT** (S3-04 Minor), P5 Major, P6 Major, P7 Minor, **P8** wipe auto-start (operator override vs PRODUCT-IDEAS leave-stopped). Timezone parked. Do not start 8.6.1 or 9.1. Optional S8 rows still empty except S8-01.