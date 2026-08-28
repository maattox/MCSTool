# V1 QA Pass 3 — results

**Pass:** 3  
**Status:** **CLOSED** — Phase **8.5** exited. Pass 3 filled; triage **skipped** (operator 2026-08-27). S0-01 Nit **parked OK** (intended overlay design; not a bug-fix plan). Living **NEXT = 8.6.1**.  
**Catalog:** [`V1-QA-Catalog.md`](V1-QA-Catalog.md)  
**Scope:** [`V1-QA-Pass-3-Scope.md`](V1-QA-Pass-3-Scope.md) — **run in-scope IDs only**.  
**Prior:** [`V1-QA-Pass-2-Results.md`](V1-QA-Pass-2-Results.md) (Modded greenfield; closed early). Pass 1 is historical.  
**Dates:** pre-confirm 2026-08-27; Phase A started 2026-08-27; Phase B started 2026-08-27  
**Stack:** TESTING Pass 2 stack (`dotnet run` Hybrid). `MCMANAGER_CONFIG_DIR` = `mcmgr-blank-test`. Do **not** paste OCIDs, Auth Tokens, RCON passwords, or friend IPs.

**How to fill:** For each **in-scope** ID, set **Result** to `Pass` / `Fail` / `Blocked` / `Skipped` / `Known`. On Fail, set **Severity** and write expected vs actual under [Failures expanded](#failures-expanded). `Known` needs an Issues.md id.

Phase **8.5** is **DONE**. Do **not** start **9.1**. Do **not** create a Pass 3 bug-fix plan.

---

## Session log

| When | Who | Suites | Idle left | Notes |
|------|-----|--------|-----------|-------|
| 2026-08-27 | docs | — | — | Operator skipped triage; S0-01 parked OK; Phase 8.5 closed. Living NEXT = 8.6.1. |
| 2026-08-27 | agent + operator | Phase B S3-01 (complete) | **on** (timeout **15**; not mutated this slice) | Operator: overlay + typed **Clear lock** worked; did **not** Start. Agent after: lock **404**, VM1 **STOPPED**, door **RUNNING**. S3-01 Pass. Restore: lock absent; idle left on 15. Post-Pass overlay copy (DEBUG clear removed; prompt “Type this statement exactly”; “Minecraft server”) — **not re-tested**. |
| 2026-08-27 | agent | Phase B S3-01 staging; S3-02 / S5-05 | **on** (timeout **15** from Phase A restore; not re-checked this slice) | Operator already confirmed S3-02 and S5-05 in this chat — recorded Pass, not re-run. S3-01: VM1 **STOPPED**, door **RUNNING**, lock was 404 → PUT v1 `meta/spend-brake-triggered.json` (`source=budget_function`, `reason=compartment_budget_threshold`, `alert_type=QA_S3_01`). GET present. **Paused for operator Hybrid** (`mcmgr-blank-test`): full-window overlay, typed confirm **Clear lock** must **not** Start. Agent will verify lock 404 + VM1 still STOPPED after the click. |
| 2026-08-27 | agent | S2-08–S2-17 + S1-05 | **on** (timeout **15**); VM1 **STOPPED** | Resumed after PC crash. Restarted S2-08 from live inspect (VM1 RUNNING, idle off, timeout 15, lock absent, play IP still on door). All remaining Phase A IDs Pass. Restore: lock 404, idle 15+on, door RUNNING, play IP on door. Door never STOPPED. |
| 2026-08-27 | agent | S2-08+ | unknown (crash) | Operator PC crashed ~8 min into mutating S2 (S2-08 SoftStop/wake). Do not treat that runner as Pass. |
| 2026-08-27 | agent | S1 + S2 inspect | **off** (timeout **15**) | Idle timeout was **2** on arrival; restored to **15** before inspect. VM1 was STOPPED → START. S1-01–S1-04 and S2-01–S2-07 / S2-21 / S2-22 recorded. |
| 2026-08-27 | operator | S1/S2 | **unknown** (S2-09 may have set timeout **2** + idle **on**) | Operator aborted Phase A mid **S2-09**. S1/S2 rows not filled. VM1 had been woken; idle timeout may still be 2 until a later restore. Do not resume this runner. |
| 2026-08-27 | agent | S0 | (S1/S2 next) | S0-01 Fail (Nit: stale idle-chroma assert vs red `overlay-offline`). S0-04 Pass. Continuing S1/S2 — not a stack blocker. |
| 2026-08-27 | agent | Phase A | (in session) | Operator unblocked Pass 3. Starting S0 then S1/S2 leftovers on TESTING Pass 2 stack. |
| 2026-08-27 | docs | — | — | Operator pre-confirmed checklist **17–21**, **23–24**, **25–92**. Include-list narrowed to Phase A + **S3-01**, **S3-02**, optional **S5-05**. |
| 2026-08-21 | docs | — | — | Pass 3 still blocked. Living NEXT = Step **8.7** P1, then **8.8**. |
| 2026-08-20 | docs | — | — | Skeleton only. Follow-on plan **NEXT = P1**. |

**Preflight snapshot (S1-03):** 2026-08-27 (after START; idle still on until S1-04)

- **VM1 lifecycle:** STOPPED on arrival → START (Always Free) → **RUNNING**
- **Door lifecycle:** **RUNNING**
- **Play IP holder:** **door secondary**
- **Spend-brake lock:** **absent** (404)
- **minecraft.service:** **active** (enabled)
- **Idle:** timeout **15** (restored from **2**), `idle_agent_enabled=true`, timer enabled/active (disabled in S1-04)
- **Door control plane:** `mccontrol` **active**; GET `/api/status` **200**, `door=DOOR_IDLE`
- **Security List 25565 `0.0.0.0/0`:** **no** (private /32s + small CIDR)
- **game-manifest:** `distribution=modded`, `loader=fabric`, `java_major=21` (MC 1.21.1 / Fabric 0.19.3; FO 6.5.0-class)

---

## S0 — Automated

| ID | Result | Severity | Notes |
| ---- | ------ | -------- | ----- |
| S0-01 | Known | Nit | **Parked OK** (operator 2026-08-27). 638 passed, 1 failed: stale idle-chroma assert vs intended red `overlay-offline`. Not a product bug; no bug-fix plan. |
| S0-02 | Skipped | | Pass 1 Pass; Function unit tests unchanged this pass. |
| S0-03 | Skipped | | Pass 1 Pass; `reconcile_usage` units unchanged this pass. |
| S0-04 | Pass | | `tofu validate` in `infra/`: configuration is valid (no apply). |
| S0-05 | Skipped | | Optional gcc / WSL. |

---

## S1 — Preflight

| ID | Result | Severity | Notes |
| ---- | ------ | -------- | ----- |
| S1-01 | Pass | | `--profile TESTING` auth works (`oci iam region list` / `oci os ns get`). Not DEFAULT. Region matches blank-test config (`us-sanjose-1`). |
| S1-02 | Pass | | VM1 was STOPPED; START + waiter → RUNNING. SSH `ubuntu` + `sudo -n true` on `mcmgr-vm1` and `mcmgr-door`. |
| S1-03 | Pass | | Snapshot above. Door RUNNING. Minecraft 25565 not world-open. Idle timeout was 2; restored to 15 before other inspect. |
| S1-04 | Pass | | `idle_agent_enabled=false`; `mc-idle-watch.timer` stopped and disabled. Timeout left at 15. |
| S1-05 | Pass | | Lock 404. START VM1 to SSH: timeout **15**, `idle_agent_enabled=true`, timer enabled/active. SOFTSTOP after that; VM1 **STOPPED** ~26s. Door RUNNING. Did not leave VM1 RUNNING overnight. |

---

## S2 — Agent on-box / cloud

| ID | Result | Severity | Notes |
| ---- | ------ | -------- | ----- |
| S2-01 | Pass | | `User=mcmgr`, `WorkingDirectory=/opt/mcmgr/server`. `/opt/mcmgr` `root:mcmgr` 750; `/opt/mcmgr/server` `mcmgr:mcmgr` 750. No 0777. |
| S2-02 | Pass | | `distribution=modded`, `loader=fabric`, `java_major=21`. Not Vanilla. Fabric 0.19.3 / MC 1.21.1 (FO 6.5.0). Manifest `modpack` empty. |
| S2-03 | Pass | | Java listens `0.0.0.0:25575` (same as Pass 1 Vanilla). SL **no** 25575; firewalld does not open 25575. Equivalent not public. Localhost RCON works (S2-21). |
| S2-04 | Pass | | `white-list=false`. |
| S2-05 | Pass | | firewalld public: **25565/tcp and 25565/udp**. `netfilter-persistent` **masked** and inactive. |
| S2-06 | Pass | | No Minecraft `0.0.0.0/0`. SSH not world-open (private /32). 25565: private /32s + small CIDR. No 25575 ingress. ICMP/other rules present. |
| S2-07 | Pass | | `mccontrol` active. GET `/api/status` 200, `door=DOOR_IDLE`. Netplan `99-mcmgr-play.yaml` present; reserved play IP on door secondary. |
| S2-08 | Pass | | Lock 404. OCI SOFTSTOP RUNNING→STOPPED ~20s (not OS-ISSUE-9). Play IP on door. `POST /api/wake` 202 `STARTING`; VM1 RUNNING ~16s; play IP on VM1; TCP 25565 open; `minecraft.service` active; door `PLAYABLE`. Idle force-enabled (OS-ISSUE-7); disabled again. Crash leftover: IP had been on door while VM1 was already up. |
| S2-09 | Pass | | Saved timeout 15 → set 2, idle on. Empty RCON `list` (0 players). VM1 STOPPED ~181s (~3 min). Play IP on door. Door RUNNING, `mccontrol` active, `DOOR_IDLE`. Timeout restored to 15 on later SSH (S2-17). |
| S2-09b | Skipped | | Optional 15-min clock. Skip if S2-09 Pass. |
| S2-10 | Pass | | Door `sudo env HOME=/home/ubuntu /opt/mccontrol/oci/heal_os_ledger.sh` while VM1 STOPPED. Exit 0. `HEAL_SKIP no_open_intervals`, `HEAL_OS_OK closed=0`. No `HOME: unbound`. Not STOPPING. |
| S2-11 | Pass | | PUT v1 lock → `POST /api/wake` 202 then VM1 **stayed STOPPED**, door RUNNING, `SPEND_BRAKE`, `last_error=monthly spend brake fired` (not daily). Journal `SPEND_BRAKE_LOCK=1`. DELETE + `/api/os-refresh` → 404, `DOOR_IDLE`, `SPEND_BRAKE_LOCK=0`. |
| S2-12 | Skipped | | Optional. Pass 1 skipped; not this delta. |
| S2-16 | Pass | | TESTING Function present; no rebuild. `mcmgr-fn-softstop` ACTIVE, image `mcmgr-fn/softstop:setup` matches repo `func.yaml` **0.0.12**. Config: one instance (VM1 only, door not listed); lock object/bucket/namespace set. |
| S2-17 | Pass | | Door-wake VM1 RUNNING (first wake raced a concurrent `pull_os_budget.sh --force` / `NEED_LEDGER: unbound` — retry OK). Idle off, timeout 15. Synthetic ACTUAL: `SUCCESS` / SoftStop one instance + lock PUT 200. VM1 STOPPED; lock v1 `source=budget_function` `reason=compartment_budget_threshold` (no `alert_type`; invoke `alertType` null). Door **RUNNING**. DELETE lock. |
| S2-18 | Skipped | | Pass 1 Pass; RESET path unchanged. |
| S2-19 | Skipped | | Optional daily MOTD. S5-05 covers Manager Start copy if run. |
| S2-20 | Skipped | | Pass 1 Pass; raw Compute vs door Start. |
| S2-21 | Pass | | Localhost RCON `list` via on-box secret: 0 of 20 players online. No SL change. |
| S2-22 | Pass | | `/var/lib/mc-manager/lease.json` present, `active=true`, heartbeat ~7 min after this VM1 START. |
| S2-26 | Skipped | | Optional `reconcile_usage`. |
| S2-28 | Skipped | | OS-ISSUE-7 by design. Pass 1 Pass. |

---

## S3 — Hybrid

| ID | Result | Severity | Notes |
| ---- | ------ | -------- | ----- |
| S3-01 | Pass | | Operator 2026-08-27: full-window overlay; typed confirm **Clear lock** dismissed overlay and did **not** Start. Agent: after confirm lock **404**, VM1 **STOPPED**, door **RUNNING**. QA-exit smoke. Post-Pass overlay copy tweaks (DEBUG clear removed; prompt; “Minecraft server”) were applied after this Pass and **not re-tested**. |
| S3-02 | Pass | | Operator confirmed 2026-08-27 in this chat (sidebar **Start** works as expected). Not re-run. |
| S3-03 | Pass | | Operator pre-confirmed 2026-08-27 (checklist 17). Not re-run. |
| S3-04 | Pass | | Operator pre-confirmed 2026-08-27 (checklist 18). Not re-run. |
| S3-05 | Skipped | | Pass 2 Pass (modded join). |
| S3-06 | Skipped | | Pass 1 Pass; oversized-world bell. |
| S3-07 | Pass | | Operator pre-confirmed 2026-08-27 (checklist 19). Not re-run. |

---

## S4 — Operator Manager UI

| ID | Result | Severity | Notes |
| ---- | ------ | -------- | ----- |
| S4-01 | Pass | | Operator pre-confirmed 2026-08-27 (checklist 25–92 chrome). QA-exit smoke. Not re-run. |
| S4-02 | Pass | | Operator pre-confirmed 2026-08-27 (sidebar tab list + Server/Advanced inner tabs). Not re-run. |
| S4-03 | Skipped | | Pass 1 Pass; `/32` Save covered by S3-04. |
| S4-08 | Pass | | Operator pre-confirmed 2026-08-27 (checklist 18). Not re-run. |
| S4-09 | Pass | | Operator pre-confirmed 2026-08-27 (Usage Hours/Budget + detailed expander). Not re-run. |
| S4-10 | Pass | | Operator pre-confirmed 2026-08-27 (Server → World backups). Not re-run. |
| S4-11 | Pass | | Operator pre-confirmed 2026-08-27 (Modding + Change pack 8.15). Not re-run. |
| S4-12 | Pass | | Operator pre-confirmed 2026-08-27 (Identity / MOTD / icon). Not re-run. |
| S4-13 | Pass | | Operator pre-confirmed 2026-08-27 (Console Simple vs Full). Not re-run. |
| S4-14 | Pass | | Operator pre-confirmed 2026-08-27 (Troubleshooting two-column). Not re-run. |
| S4-15 | Pass | | Operator pre-confirmed 2026-08-27 (Advanced → Status). Not re-run. |
| S4-16 | Pass | | Operator pre-confirmed 2026-08-27 (caption gear). Not re-run. |
| S4-17 | Pass | | Operator pre-confirmed 2026-08-27 (caption bell). Not re-run. |
| S4-18 | Pass | | Operator pre-confirmed 2026-08-27 (Advanced → Danger idle). Not re-run. |
| S4-19 | Pass | | Operator pre-confirmed 2026-08-27 (shape picker STOPPED gate; no live apply). Not re-run. |
| S4-20 | Pass | | Operator pre-confirmed 2026-08-27 (Delete dialog; did not delete). Not re-run. |
| S4-21 | Skipped | | Pass 1 Pass; no public/blacklist. |
| S4-22 | Pass | | Operator pre-confirmed 2026-08-27 (raw start vs doorbell Start copy). Not re-run. |

---

## S5 — Play path

| ID | Result | Severity | Notes |
| ---- | ------ | -------- | ----- |
| S5-01 | Pass | | Operator pre-confirmed 2026-08-27 (checklist 20). Not re-run. |
| S5-02 | Pass | | Operator pre-confirmed 2026-08-27 (checklist 21). Not re-run. |
| S5-03 | Skipped | | Skip if S2-09 Pass (catalog). |
| S5-04 | Skipped | | Skip if S2-09 Pass (catalog). |
| S5-05 | Pass | | Operator confirmed 2026-08-27 in this chat (daily-exhausted copy / Start refuse as expected; optional ID). Not re-run. |

---

## S6 — Setup / Connect

| ID | Result | Severity | Notes |
| ---- | ------ | -------- | ----- |
| S6-01 | Pass | | Operator pre-confirmed 2026-08-27 (checklist 25–92 Setup pages, no second Deploy). Not re-run. |
| S6-02 | Pass | | Operator pre-confirmed 2026-08-27 (checklist 23). Not re-run. |
| S6-03 | Skipped | | Pass 1 Pass; Connect-existing. |
| S6-04 | Pass | | Operator pre-confirmed 2026-08-27 (checklist 24 Deployment Complete / Deploy-repair reopen). Not re-run. |
| S6-05 | Skipped | | Pass 1 Pass; dry-run. Do not greenfield. |

---

## S7 — Destructive

| ID | Result | Severity | Notes |
| ---- | ------ | -------- | ----- |
| S7-02 | Skipped | | Pass 1 Pass. Do not live-resize unless a Fail requires it. |
| S7-03 | Skipped | | Pass 1 Pass. No world-replace this pass. |
| S7-04 | Skipped | | Pass 2 already destroyed + deployed. Do not Delete again. |

---

## S8 — Known-issue checks

Fill if a known issue is exercised during Phase A/B.

| ID | Result | Severity | Notes |
| ---- | ------ | -------- | ----- |
| | | | |

---

## Failures expanded

### S0-01 — Core unit tests (parked OK)

- **Severity:** Nit (**parked OK**)
- **Expected:** `dotnet test src\McManager.slnx` all pass.
- **Actual:** 638 passed, 1 failed: `ServerIconComposerTests.Default_compose_is_64_with_overlay_variants` line 24 `Assert.False(HasChroma(IdlePng, ignoreNearBlack: true))`. Idle door icon is greyscale user art plus the red `overlay-offline.png` power glyph (asset update 2026-08-24). Composer still matches P8 (greyscale + colored overlays). Other overlay tests already expect chroma on starting/exhausted.
- **Repro:** `dotnet test src\McManager.Core.Tests --filter Default_compose_is_64_with_overlay_variants`
- **Operator 2026-08-27:** **Parked OK.** The test failed because the intended idle overlay design changed (red `overlay-offline` glyph). Not a product bug; **no** Pass 3 bug-fix plan. Optional later: relax the assert — not Phase 8.6.

---

## Additional problems

- **S0-01 stale idle-chroma assert** — **parked OK** (operator 2026-08-27). Intended overlay design; not an on-box/Setup/door bug; no `Issues.md` id; no bug-fix plan.
- **S2-17 wake vs concurrent pull** — first `POST /api/wake` after S2-11 raced `pull_os_budget.sh --force` (`NEED_LEDGER: unbound`); VM1 stayed STOPPED. Retry wake (no concurrent pull) succeeded. Not a catalog Fail of the Function path.
- **S2-17 lock JSON** — synthetic ACTUAL PUT omitted `alert_type` (`alertType` null on invoke). Still v1 `source=budget_function`. Door was never SoftStopped.
- **Spend-brake overlay copy (post S3-01)** — operator asked three nits after Pass: drop overlay **DEBUG: clear lock**; prompt “Type this statement exactly”; “Minecraft computer” → “Minecraft server”. Applied 2026-08-27; **not re-tested**. Not a catalog Fail.

---

## Triage notes

Pass 3 is **filled**. Operator 2026-08-27 **skipped** triage. **No** [`V1-Bug-Fix-Plan-Pass-3.md`](V1-Bug-Fix-Plan-TEMPLATE.md). **S0-01** Nit **parked OK** (intended idle overlay; stale assert). Overlay copy nits above are already applied. Phase **8.5** **DONE**. Living **NEXT = 8.6.1**. Do **not** start **9.1**.
