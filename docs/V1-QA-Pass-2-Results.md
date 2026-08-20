# V1 QA Pass 2 — results

**Pass:** 2  
**Status:** **PAUSED** — do not fill until Step **4.13** / robustness R4 is DONE and the operator starts Phase A.  
**Catalog:** [`V1-QA-Catalog.md`](V1-QA-Catalog.md)  
**Scope:** [`V1-QA-Pass-2-Scope.md`](V1-QA-Pass-2-Scope.md) — **run in-scope IDs only**. Pre-filled `Skipped` rows are Pass 1 Pass / out of this delta; do not re-run them.  
**Prior:** [`V1-QA-Pass-1-Results.md`](V1-QA-Pass-1-Results.md) (Vanilla, no greenfield). Pass 1 bug-fix **P1–P8 DONE**.  
**Dates:** *(fill)*  
**Stack:** TESTING greenfield (`dotnet run` Hybrid). Do **not** paste OCIDs, Auth Tokens, RCON passwords, or friend IPs.

**Game (Phase A):** Modded — pack file: *(filename)* · loader: *(neoforge/fabric/…)* · Minecraft: *(version)* · VM1 shape: **2/12** (unless overridden)  
**Config dir:** `MCMANAGER_CONFIG_DIR` = *(e.g. mcmgr-blank-test or mcmgr-pass-2)* — **not** repo `data/config.local.json`  
**SSH key:** *(path only, after Setup — not the Pass 1 key unless config still names it)*  
**Function:** present / Setup-skipped *(record after S7-04)*

**How to fill:** For each **in-scope** ID, set **Result** to `Pass` / `Fail` / `Blocked` / `Skipped` / `Known`. On Fail, set **Severity** (`Blocker` / `Major` / `Minor` / `Nit` / `After-v1` / `Won't-fix`) and write expected vs actual under [Failures expanded](#failures-expanded). `Known` needs an Issues.md id.

Suggested fill text for a clean test: `Pass` with notes empty. You do **not** need to type “No issues. Works as expected.”

Do not start 8.6.1 or 9.1. Do not create a Pass 2 bug-fix plan until this file is filled and the operator asks for triage.

---

## Session log


| When | Who | Suites | Idle left | Notes |
| ---- | --- | ------ | --------- | ----- |
|      |     |        |           |       |


**Preflight snapshot (S1-03):** *(after Phase A; no OCIDs / no friend IPs)*

- **VM1 lifecycle:**
- **Door lifecycle:**
- **Play IP holder:**
- **Spend-brake lock:**
- **minecraft.service:**
- **Idle:**
- **Door control plane:**
- **Security List 25565 `0.0.0.0/0`:** must be **no**
- **game-manifest:** `distribution=` · `loader=` · `minecraft_version=`

---

## S0 — Automated


| ID    | Result  | Severity | Notes |
| ----- | ------- | -------- | ----- |
| S0-01 |         |          | **In scope (Phase A).** |
| S0-02 | Skipped |          | Pass 1 Pass; Function unit tests unchanged this pass. |
| S0-03 | Skipped |          | Pass 1 Pass; `reconcile_usage` units unchanged this pass. |
| S0-04 |         |          | **In scope (Phase A).** `tofu validate` only — no apply here. |
| S0-05 | Skipped |          | Optional. Pass 1 Skipped (no gcc/WSL bash). |


---

## S1 — Preflight


| ID    | Result | Severity | Notes |
| ----- | ------ | -------- | ----- |
| S1-01 |        |          | **In scope (Phase B).** TESTING, not DEFAULT. |
| S1-02 |        |          | **In scope (Phase B).** New Setup SSH key. |
| S1-03 |        |          | **In scope (Phase B).** QA-exit smoke. See snapshot above. |
| S1-04 |        |          | **In scope (Phase B).** |
| S1-05 |        |          | **In scope (Phase B).** Restore lock/idle. |


---

## S2 — Agent on-box / cloud


| ID     | Result  | Severity | Notes |
| ------ | ------- | -------- | ----- |
| S2-01  |         |          | **In scope (Phase B).** Greenfield `mcmgr` layout. |
| S2-02  |         |          | **In scope (Phase B).** Expect **modded** + chosen loader (not Vanilla). |
| S2-03  |         |          | **In scope (Phase B).** |
| S2-04  |         |          | **In scope (Phase B).** |
| S2-05  |         |          | **In scope (Phase B).** P1 must land from cloud-init. |
| S2-06  |         |          | **In scope (Phase B).** |
| S2-07  |         |          | **In scope (Phase B).** |
| S2-08  |         |          | **In scope (Phase B).** QA-exit smoke. |
| S2-09  |         |          | **In scope (Phase B).** QA-exit smoke. 2-minute timeout; restore after. |
| S2-09b | Skipped |          | Optional 15-min clock. Skip if S2-09 Pass. |
| S2-10  |         |          | **In scope (Phase B).** New ledger. |
| S2-11  |         |          | **In scope (Phase B).** Fresh door lock GET (P2 product path). |
| S2-12  | Skipped |          | Optional. Pass 1 skipped; not this delta. |
| S2-16  |         |          | **In scope (Phase B).** Pass if v1 Function present; **Skipped** if Setup skipped Function (not a Fail of 8.6.1). |
| S2-17  |         |          | **In scope (Phase B) only if Function exists.** Else Skipped. QA-exit smoke. Do not SoftStop the door. DELETE lock after. |
| S2-18  | Skipped |          | Pass 1 Pass; RESET path unchanged. |
| S2-19  | Skipped |          | Optional daily MOTD. Use S5-05 for P6 Manager Start only. |
| S2-20  | Skipped |          | Pass 1 Pass; raw Compute vs door Start. |
| S2-21  |         |          | **In scope (Phase B).** Cheap while Minecraft is up. |
| S2-22  |         |          | **In scope (Phase B).** |
| S2-26  | Skipped |          | Optional `reconcile_usage`. Pass 1 Skipped. |
| S2-28  | Skipped |          | OS-ISSUE-7 by design. Pass 1 Pass. |


---

## S3 — Hybrid


| ID    | Result  | Severity | Notes |
| ----- | ------- | -------- | ----- |
| S3-01 |         |          | **In scope (Phase C).** QA-exit overlay smoke. |
| S3-02 |         |          | Skip if Phase A S7-04 already proved door-aware Start; note the pointer. |
| S3-03 |         |          | Skip if Phase A S7-04 already proved Stop + IP on door; note the pointer. |
| S3-04 |         |          | **In scope (Phase C).** P4 leftover `/24` on revert. |
| S3-05 |         |          | **In scope (Phase C).** Join with **same pack**. Vanilla client **must fail** (that is Pass). |
| S3-06 | Skipped |          | Pass 1 Pass; oversized-world bell unchanged. |
| S3-07 |         |          | **In scope (Phase C).** P8: wipe **auto-starts** Minecraft. |


---

## S4 — Operator Manager UI


| ID    | Result  | Severity | Notes |
| ----- | ------- | -------- | ----- |
| S4-01 |         |          | **In scope (Phase D).** QA-exit smoke. |
| S4-02 | Skipped |          | Pass 1 Pass; tab split unchanged. |
| S4-03 | Skipped |          | Pass 1 Pass; `/32` Save covered by S3-04/S4-08 this pass. |
| S4-08 |         |          | **In scope (Phase D)** with S3-04. Restore test prefix. |
| S4-09 | Skipped |          | Pass 1 Pass; Usage chrome. |
| S4-10 | Skipped |          | Pass 1 Pass; backups UI. New stack may have no zips yet — do not fail S4-10. |
| S4-11 |         |          | **In scope (Phase D).** Live `mods/` inspect + **Download pack** = original archive (Pass 1 deferred). |
| S4-12 |         |          | **In scope (Phase D).** P5 identity on Setup-installed agent. |
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
| S5-01 |         |          | **In scope (Phase D).** Door MOTD; matching **modded** client list ping. |
| S5-02 |         |          | **In scope (Phase D).** One wake-from-client. First-kick = Known DOOR-ISSUE-1 unless worse. |
| S5-03 | Skipped |          | Skip if S2-09 Pass (catalog). |
| S5-04 | Skipped |          | Skip if S2-09 Pass (catalog). |
| S5-05 |         |          | **In scope (Phase D).** P6 only: player refuse + **Manager Start succeeds**; spend-brake still blocks. Restore daily cap. Park MOTD lag / sudden-cap chat / PT vs UTC. |


---

## S6 — Setup / Connect


| ID    | Result  | Severity | Notes |
| ----- | ------- | -------- | ----- |
| S6-01 |         |          | **In scope (Phase A).** Live Setup **including Deploy** (Pass 1 did not click Deploy). |
| S6-02 |         |          | **In scope (Phase A).** Chosen `.mrpack` summary; jar-less CF zip **hard-block** (P7). |
| S6-03 | Skipped |          | Pass 1 Pass; Connect-existing. |
| S6-04 | Skipped |          | Pass 1 Pass; Deploy/repair resume. |
| S6-05 | Skipped |          | Pass 1 Pass; dry-run. Live apply is S7-04. |


---

## S7 — Destructive


| ID    | Result  | Severity | Notes |
| ----- | ------- | -------- | ----- |
| S7-02 | Skipped |          | Pass 1 Pass; live 2/12 ↔ 4/24. Do not resize this pass unless a Fail requires it. |
| S7-03 | Skipped |          | Pass 1 Pass; world replace. |
| S7-04 |         |          | **In scope (Phase A).** Delete + greenfield Modded. Destroy **before** apply. |


---

## S8 — Known-issue checks


| ID    | Result | Severity | Notes |
| ----- | ------ | -------- | ----- |
| S8-01 |        |          | DOOR-ISSUE-1 first-kick (S5-02). |
| S8-02 |        |          | FN-ISSUE-1 on TESTING after greenfield (Function may be skipped). |
| S8-03 |        |          | OS-ISSUE-7 docs. |
| S8-04 |        |          | SETUP-ISSUE-7 / P1 firewalld after SoftStop reboot (S2-05/S2-09). |


---

## Failures expanded

Copy one block per **Fail** (or Blocked that should become a fix):

### *(ID)* — *(short title)*

- **Severity:** *(suggested; operator confirms in triage)*
- **Expected:**
- **Actual:**
- **Repro:**
- **Evidence:** *(no OCIDs / secrets)*

---

## Additional problems

Anything not tied to a catalog ID: confusing copy, slow UI, “I also noticed…”, questions about intended behavior. Questions are **not** bugs until triage.

1. 

---

## Triage notes (operator + agent, docs-only session)

Fill **after** Phase A–D are recorded. Then copy [`V1-Bug-Fix-Plan-TEMPLATE.md`](V1-Bug-Fix-Plan-TEMPLATE.md) → `V1-Bug-Fix-Plan-Pass-2.md`. Do not implement in the triage chat.


| Catalog ID | Keep as fix? | Plan section id | Notes |
| ---------- | ------------ | --------------- | ----- |
|            |              |                 |       |
