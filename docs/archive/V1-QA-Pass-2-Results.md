# V1 QA Pass 2 — results

**Pass:** 2  
**Status:** **DONE** — operator closed the pass after Phase A greenfield + a modded join + Modding panel. Remaining in-scope Phase B–D IDs were **not run**. No Pass 2 bug-fix plan (operator: issues already fixed in-pass; no triage).  
**Catalog:** [`V1-QA-Catalog.md`](V1-QA-Catalog.md)  
**Scope:** [`V1-QA-Pass-2-Scope.md`](V1-QA-Pass-2-Scope.md) — **run in-scope IDs only**. Pre-filled `Skipped` rows are Pass 1 Pass / out of this delta; do not re-run them.  
**Prior:** [`V1-QA-Pass-1-Results.md`](V1-QA-Pass-1-Results.md) (Vanilla, no greenfield). Pass 1 bug-fix **P1–P8 DONE**.  
**Dates:** 2026-08-20  
**Stack:** TESTING greenfield (`dotnet run` Hybrid). Do **not** paste OCIDs, Auth Tokens, RCON passwords, or friend IPs.

**Game (Phase A):** Modded — pack file: `modrinth-fabric-Fabulously.Optimized-v6.5.0.mrpack` · loader: **fabric** · Minecraft: *(from that pack)* · VM1 shape: **4 OCPU / 24 GB** (operator override of Pass 2 default 2/12)  
**Config dir:** `MCMANAGER_CONFIG_DIR` = `mcmgr-blank-test` — **not** repo `data/config.local.json`  
**SSH key:** reused Pass 1 key `%USERPROFILE%\.ssh\mcmgr_ed25519_20260817_125552` (imported in wizard; not a new wizard-generated key)  
**Function:** Setup-skipped — `docker.exe` present; daemon pipe `dockerDesktopLinuxEngine` missing. Not a Fail of 8.6.1.

**How to fill:** For each **in-scope** ID, set **Result** to `Pass` / `Fail` / `Blocked` / `Skipped` / `Known`. On Fail, set **Severity** (`Blocker` / `Major` / `Minor` / `Nit` / `After-v1` / `Won't-fix`) and write expected vs actual under [Failures expanded](#failures-expanded). `Known` needs an Issues.md id.

Suggested fill text for a clean test: `Pass` with notes empty. You do **not** need to type “No issues. Works as expected.”

Do not start 8.6.1 or 9.1. Do not create a Pass 2 bug-fix plan until this file is filled and the operator asks for triage.

---

## Session log


| When       | Who            | Suites                                      | Idle left | Notes                                                                                                                                                                                                                                                                                                 |
| ---------- | -------------- | ------------------------------------------- | --------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 2026-08-20 | Operator + agent | Phase A S6-01/S6-02/S7-04; S3-05 join; S4-11 | *(not recorded)* | Delete then Setup TESTING. Mid-apply **SETUP-ISSUE-9** then **SETUP-ISSUE-10** (both product-fixed). Final Deploy finished; Function image skipped (Docker daemon down). MultiMC join on reserved IP. Modding panel listed mods + Download pack. Operator marked Pass 2 **DONE**; no triage. |


**Preflight snapshot (S1-03):** *(not taken — Phase B not run)*

- **VM1 lifecycle:** RUNNING after Setup (play path left PLAYABLE)
- **Door lifecycle:** *(not recorded)*
- **Play IP holder:** VM1 after Setup (`promote_playable`)
- **Spend-brake lock:** *(not recorded)*
- **minecraft.service:** joinable (modded)
- **Idle:** *(not recorded)*
- **Door control plane:** PLAYABLE after Setup
- **Security List 25565 `0.0.0.0/0`:** must be **no** *(not re-checked this fill)*
- **game-manifest:** `distribution=` modded · `loader=` fabric · `minecraft_version=` *(from FO 6.5.0 pack)*

---

## S0 — Automated


| ID    | Result  | Severity | Notes |
| ----- | ------- | -------- | ----- |
| S0-01 | Skipped |          | Not run this pass. |
| S0-02 | Skipped |          | Pass 1 Pass; Function unit tests unchanged this pass. |
| S0-03 | Skipped |          | Pass 1 Pass; `reconcile_usage` units unchanged this pass. |
| S0-04 | Skipped |          | Not run as a dedicated pre-apply step. Agent `tofu validate` during SETUP-ISSUE-9 fix succeeded. |
| S0-05 | Skipped |          | Optional. Pass 1 Skipped (no gcc/WSL bash). |


---

## S1 — Preflight


| ID    | Result  | Severity | Notes |
| ----- | ------- | -------- | ----- |
| S1-01 | Skipped |          | Not run; operator closed Pass 2 after Phase A + join + S4-11. |
| S1-02 | Skipped |          | Not run. Wizard **imported** Pass 1 key `mcmgr_ed25519_20260817_125552`. |
| S1-03 | Skipped |          | Not run. Partial snapshot above from Setup/join only. |
| S1-04 | Skipped |          | Not run; operator closed Pass 2. |
| S1-05 | Skipped |          | Not run; operator closed Pass 2. |


---

## S2 — Agent on-box / cloud


| ID     | Result  | Severity | Notes |
| ------ | ------- | -------- | ----- |
| S2-01  | Skipped |          | Not run; operator closed Pass 2. |
| S2-02  | Skipped |          | Not run as SSH inspect. Live game was **modded Fabric** (FO join). |
| S2-03  | Skipped |          | Not run; operator closed Pass 2. |
| S2-04  | Skipped |          | Not run; operator closed Pass 2. |
| S2-05  | Skipped |          | Not run. Greenfield cloud-init **did not** apply OS baseline until SETUP-ISSUE-10 fix + guest repair (this VM was repaired). |
| S2-06  | Skipped |          | Not run; operator closed Pass 2. |
| S2-07  | Skipped |          | Not run; operator closed Pass 2. |
| S2-08  | Skipped |          | Not run; operator closed Pass 2. |
| S2-09  | Skipped |          | Not run; operator closed Pass 2. |
| S2-09b | Skipped |          | Optional 15-min clock. Skip if S2-09 Pass. |
| S2-10  | Skipped |          | Not run; operator closed Pass 2. |
| S2-11  | Skipped |          | Not run; operator closed Pass 2. |
| S2-12  | Skipped |          | Optional. Pass 1 skipped; not this delta. |
| S2-16  | Skipped |          | Setup skipped Function (Docker daemon not running). **Not a Fail of 8.6.1.** |
| S2-17  | Skipped |          | Function not installed. |
| S2-18  | Skipped |          | Pass 1 Pass; RESET path unchanged. |
| S2-19  | Skipped |          | Optional daily MOTD. Use S5-05 for P6 Manager Start only. |
| S2-20  | Skipped |          | Pass 1 Pass; raw Compute vs door Start. |
| S2-21  | Skipped |          | Not run; operator closed Pass 2. |
| S2-22  | Skipped |          | Not run; operator closed Pass 2. |
| S2-26  | Skipped |          | Optional `reconcile_usage`. Pass 1 Skipped. |
| S2-28  | Skipped |          | OS-ISSUE-7 by design. Pass 1 Pass. |


---

## S3 — Hybrid


| ID    | Result  | Severity | Notes |
| ----- | ------- | -------- | ----- |
| S3-01 | Skipped |          | Not run; operator closed Pass 2. |
| S3-02 | Skipped |          | Manager Start/Stop not recorded. After Setup, reserved IP was on VM1 (`promote_playable`) and joinable. |
| S3-03 | Skipped |          | Stop + IP handback to door not recorded. |
| S3-04 | Skipped |          | Not run; operator closed Pass 2. |
| S3-05 | Pass    |          | MultiMC joined on the **reserved play IP** with the deployed FO pack. Vanilla-client **fail** not recorded. |
| S3-06 | Skipped |          | Pass 1 Pass; oversized-world bell unchanged. |
| S3-07 | Skipped |          | Not run; operator closed Pass 2. |


---

## S4 — Operator Manager UI


| ID    | Result  | Severity | Notes |
| ----- | ------- | -------- | ----- |
| S4-01 | Skipped |          | Not run; operator closed Pass 2. |
| S4-02 | Skipped |          | Pass 1 Pass; tab split unchanged. |
| S4-03 | Skipped |          | Pass 1 Pass; `/32` Save covered by S3-04/S4-08 this pass. |
| S4-08 | Skipped |          | Not run; operator closed Pass 2. |
| S4-09 | Skipped |          | Pass 1 Pass; Usage chrome. |
| S4-10 | Skipped |          | Pass 1 Pass; backups UI. New stack may have no zips yet — do not fail S4-10. |
| S4-11 | Pass    |          | Server Management listed the mods; **Download pack** worked. |
| S4-12 | Skipped |          | Not run; operator closed Pass 2. |
| S4-13 | Skipped |          | Pass 1 Pass; Console. |
| S4-14 | Skipped |          | Pass 1 Pass; Troubleshooting. |
| S4-15 | Skipped |          | Pass 1 Pass; Advanced technical status. |
| S4-16 | Skipped |          | Pass 1 Pass; gear/overflow. |
| S4-17 | Skipped |          | Pass 1 Pass; bell shell. |
| S4-18 | Skipped |          | Pass 1 Pass; idle disable UI. OS-ISSUE-7 still applies. |
| S4-19 | Skipped |          | Pass 1 Pass; no live resize this pass. |
| S4-20 | Skipped |          | Pass 1 Pass. **Do not Delete again** in Phase D (that was Phase A). |
| S4-21 | Skipped |          | Pass 1 Pass; no public/blacklist. |
| S4-22 | Skipped |          | Pass 1 Pass; break-glass vs Start copy. |


---

## S5 — Play path


| ID    | Result  | Severity | Notes |
| ----- | ------- | -------- | ----- |
| S5-01 | Skipped |          | Not run as door-off MOTD. Join was while Setup left the stack **PLAYABLE**. |
| S5-02 | Skipped |          | Wake-from-client not recorded (game already up). |
| S5-03 | Skipped |          | Skip if S2-09 Pass (catalog). |
| S5-04 | Skipped |          | Skip if S2-09 Pass (catalog). |
| S5-05 | Skipped |          | Not run; operator closed Pass 2. |


---

## S6 — Setup / Connect


| ID    | Result  | Severity | Notes |
| ----- | ------- | -------- | ----- |
| S6-01 | Pass    |          | Live Setup **with Deploy** on TESTING. Compartment `mcmgr`. Profile TESTING. Modded. Shape 4/24. Client-pack warning shown. |
| S6-02 | Pass    |          | FO `.mrpack` via **drag-and-drop**; client-only skip warning (mis-declared mods). P7 jar-less CF zip **not** recorded this session. |
| S6-03 | Skipped |          | Pass 1 Pass; Connect-existing. |
| S6-04 | Skipped |          | Pass 1 Pass; Deploy/repair resume. Resume after failed apply was used to finish Deploy. |
| S6-05 | Skipped |          | Pass 1 Pass; dry-run. Live apply is S7-04. |


---

## S7 — Destructive


| ID    | Result  | Severity | Notes |
| ----- | ------- | -------- | ----- |
| S7-02 | Skipped |          | Pass 1 Pass; live 2/12 ↔ 4/24. Do not resize this pass unless a Fail requires it. |
| S7-03 | Skipped |          | Pass 1 Pass; world replace. |
| S7-04 | Pass    |          | Delete then greenfield Modded FO. Destroy succeeded (Function delete ~4 min, not a Fail). First two applies hit SETUP-ISSUE-9 then SETUP-ISSUE-10 (**fixed** in product). Final Deploy finished; reserved IP joinable (MultiMC). Function image skipped (Docker daemon). |


---

## S8 — Known-issue checks


| ID    | Result  | Severity | Notes |
| ----- | ------- | -------- | ----- |
| S8-01 | Skipped |          | S5-02 not run. |
| S8-02 | Skipped |          | Function not installed this pass. |
| S8-03 | Skipped |          | Not run. |
| S8-04 | Skipped |          | Not run. SETUP-ISSUE-10: first-boot cloud-init skipped OS baseline; repaired. |


---

## Failures expanded

None open. Mid-pass apply failures were filed and **fixed** before this fill:

- **SETUP-ISSUE-9** — budget description >200 chars + OCIR 404-DENIED on a new compartment.
- **SETUP-ISSUE-10** — VM1 `#cloud-config` invalid (`indent()` / `[Unit]`); marker never written.

---

## Additional problems

1. Setup Function skip: `docker.exe` on PATH, login to OCIR succeeded, `buildx --push` failed because Docker Desktop **daemon** was not running (`dockerDesktopLinuxEngine` named pipe missing). Interim Docker publisher; product path remains Step **8.6.1** (no Docker on the admin PC). Not a Fail of 8.6.1.
2. Danger Zone Delete sat ~4 minutes on deleting the budget-brake Function. Completed; note only.
3. Pass 2 default VM1 **2/12** was overridden to **4/24**. Pack was FO 6.5.0 (mis-declaration warning), not BlockFront.

---

## Triage notes (operator + agent, docs-only session)

**Not held.** Operator: issues already fixed in-pass; do not create `V1-Bug-Fix-Plan-Pass-2.md`.


| Catalog ID | Keep as fix? | Plan section id | Notes |
| ---------- | ------------ | --------------- | ----- |
| —          | No           | —               | SETUP-ISSUE-9 / SETUP-ISSUE-10 already in product SoT. Function skip is 8.6.1, not a Pass 2 Fail. |
