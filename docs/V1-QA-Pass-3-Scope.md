# V1 QA Pass 3 — scope (gap-close)

**Pass:** 3  
**Status:** **CLOSED** — Phases A–C **DONE**. Pass 3 **filled**. Operator 2026-08-27 **skipped** triage; S0-01 Nit **parked OK**. Phase **8.5** **DONE**. Living queue: [`NEXT.md`](NEXT.md) → Step **8.6.1**.  
**Catalog:** [`V1-QA-Catalog.md`](V1-QA-Catalog.md) — IDs and expected steps stay there. Do **not** regenerate the catalog.  
**Results:** fill [`V1-QA-Pass-3-Results.md`](V1-QA-Pass-3-Results.md) as you go.  
**Prior:** [`V1-QA-Pass-2-Results.md`](V1-QA-Pass-2-Results.md) (greenfield Modded; closed early). [`V1-QA-Pass-1-Results.md`](V1-QA-Pass-1-Results.md) (Vanilla; historical).

This is a **delta**, not a second encyclopedia. Pass 1 Passed most Vanilla chrome. Pass 2 Passed Delete + greenfield Modded, live Setup Deploy, modded join, and Modding/Download pack. Operator 2026-08-27 confirmed the post-Pass-2 Manager / Setup / pack-import surface (checklist **17–21**, **23–24**, **25–92**). Pass 3 now runs only what that confirmation **did not** cover: **Phase A** agent on-box/cloud leftovers, plus three Hybrid IDs.

**Cost:** $0. TESTING only. Never `DEFAULT` / live Forge lab. Never Minecraft `0.0.0.0/0`.  
**Do not start** Step **9.1**. Pass 3 bug-fix plan: **not created** (triage skipped).  
**Tofu:** do **not** `tofu destroy` / second greenfield unless the operator authorizes it. Prefer the **existing** Pass 2 TESTING stack (`mcmgr-blank-test`).

---

## Why this pass is different

| | Pass 1 | Pass 2 | Pass 3 |
|---|--------|--------|--------|
| Stack | Existing TESTING (old) | **Destroy + Setup** Modded | **Keep** Pass 2 stack unless operator says otherwise |
| Game | Vanilla | Modded (FO 6.5.0 Fabric) | Still Modded |
| Catalog | Full S0–S7 (S7-04 skipped) | Delta include-list; **closed early** | Agent leftovers + 3 Hybrid IDs |
| Tofu | Forbidden | Phase A authorized destroy-then-apply | **Forbidden** unless operator says so |

---

## How agents must use this file

1. Read **this protocol**, the [Progress](#progress) line, and **only the phase you were asked to run**.  
2. Catalog expected steps: open **named IDs** in [`V1-QA-Catalog.md`](V1-QA-Catalog.md). Do not load the Minecraft blueprint, PRODUCT-IDEAS, or the whole V1 file.  
3. Fill [`V1-QA-Pass-3-Results.md`](V1-QA-Pass-3-Results.md). Out-of-scope and operator-preconfirmed rows stay as recorded — do **not** re-run them.  
4. **One agent chat owns the TESTING stack at a time.**  
5. Mirror guest/cloud fixes into local SoT. File [`Issues.md`](Issues.md) for Setup/HCL/bootstrap/door bugs.  
6. Git: commits allowed; never push/PR unless the operator asks.  
7. Hybrid: agents **cannot** drive the WPF window. Operator clicks; agent stages/verifies OCI/SSH.  
8. VM1: START if needed, **disable idle** while working, **re-enable** when the phase ends (re-disable after Minecraft start — OS-ISSUE-7).  
9. SSH with the key in TESTING `config.local.json` (Pass 2 reused `mcmgr_ed25519_20260817_125552`).  
10. **Never** use product repo `data/config.local.json` (live Forge / `DEFAULT`).

### Context budget

This file + the named catalog IDs + [`Guide.md`](Guide.md) for changed copy.

---

## Progress

| Phase | Focus | Status |
|-------|--------|--------|
| **A** | S0 smoke + Pass 2 leftover on-box inspect (S1/S2) | **DONE** |
| **B** | Remaining Hybrid: overlay, sidebar Start, optional daily-exhaust | **DONE** |
| **C** | Setup leftovers (jar-root, Deploy Complete) | **DONE** — operator pre-confirmed 2026-08-27 (checklist 23–24). Do not re-run. |

**Pass 3 filled. Phase 8.5 closed.** S0-01 Nit parked OK. Do not start 9.1. Do not create a Pass 3 bug-fix plan.

---

## Include vs skip

### In scope (run these)

**Phase A — agent** (checklist **1–14**)

Suggested order: S0 → S1 preflight → inspect S2-01–S2-07 / S2-21–S2-22 while RUNNING → S2-08 wake → S2-09 idle SoftStop → S2-10 heal → S2-11 lock refuse → S2-16/S2-17 Function → S1-05 restore.

| ID | Why |
|----|-----|
| **S0-01** | Follow-on Core tests; QA-exit smoke |
| **S0-04** | Cheap; skipped in Pass 2 |
| **S1-01–S1-05** | Pass 2 never snapshotted the new stack |
| **S2-01–S2-11** | Pass 2 never inspected greenfield SoT (cloud-init, door, wake, idle, heal, lock GET) |
| **S2-16, S2-17** | Function should exist after follow-on **P12**; skip S2-17 only if still absent |
| **S2-21, S2-22** | Cheap while Minecraft is up |

**Phase B — Hybrid / operator** (checklist **15**, **16**, **22**)

| ID | Why |
|----|-----|
| **S3-01** | Overlay typed confirm must **not** Start (follow-on P1). Agent PUT lock; operator clicks; agent verifies VM1 stayed down. |
| **S3-02** | Pass 2 did not record doorbell **Start**. Click **Start** in the **left sidebar** (not a top bar). Status in-flight then **Running**. |
| **S5-05** | Optional. Temporarily lower daily cap; Manager sidebar Start / kick/MOTD is daily, **not** spend-brake. Restore the cap. Skip if the operator does not want to touch budgets — record `Skipped`. |

**S8:** fill as you hit known issues.

### Operator pre-confirmed (do not re-run)

Recorded as **Pass** in [`V1-QA-Pass-3-Results.md`](V1-QA-Pass-3-Results.md) on 2026-08-27. Do **not** re-click these unless a Phase A/B Fail shows a regression.

| Checklist | Catalog / topic |
|-----------|-----------------|
| 17 | **S3-03** sidebar Stop |
| 18 | **S3-04**, **S4-08** allowlist Save + CIDR |
| 19 | **S3-07** wipe world auto-start |
| 20 | **S5-01** door MOTD when VM1 stopped |
| 21 | **S5-02** wake from client connect |
| 23 | **S6-02** jar-root / homemade zip continue |
| 24 | Setup **Deployment Complete** + reserved IP Copy |
| 25–92 | Manage chrome (caption, sidebar, pins, Overview/About, toasts, Server/Advanced inner tabs, MOTD WYSIWYG, Change pack overlay/stopped-VM pick/assisted review, Setup pages, SSH key paths, Console Simple/Full, Usage, Danger, World backups, Layer 3 UI if seen) |

Those 25–92 rows cover the original Phase B chrome IDs **S4-01**, **S4-02**, **S4-09**, **S4-11**, **S4-12**, **S4-13**, **S4-18** and the 8.7–8.15 follow-on that never got separate catalog IDs.

### Out of scope (do not re-run)

- **S7-04** — Pass 2 already destroyed + deployed. Do not Delete again.  
- **S7-02, S7-03** — Pass 1 Pass; no resize/world-replace unless a Fail requires it.  
- **S4-03, S4-10, S4-14–S4-17, S4-19–S4-22** — Pass 1 Pass and/or operator checklist **25–92**.  
- **S6-01 live Deploy**, **S6-03–S6-05** — Pass 1/2 already covered; do not greenfield.  
- **S0-02, S0-03, S0-05** — unchanged / optional gcc.  
- **S3-05** — Pass 2 Pass (modded join).  
- **S3-06** — Pass 1 Pass oversized-world bell.  
- Pack-corpus harness, unfinished Cobblemon re-run, CurseForge API helper (deferred), installer / CI Function image, real `$1` budget fire.

If a skipped ID **regresses**, record it under Additional problems or promote to Fail.

---

## Restore (every mutating session)

DELETE spend-brake lock; restore idle timeout; re-enable idle unless the next Hybrid chat needs it off. Do not leave VM1 RUNNING overnight without asking.

---

## Operator prompts (copy-paste)

Phases A–C are **DONE**. Pass 3 is **filled**. Phase **8.5** is **closed**. Living **NEXT = 8.6.1**.

### Phase A

```text
Read docs/V1-QA-Pass-3-Scope.md in OCI-mc-server (protocol + Phase A only) and the named catalog IDs in docs/V1-QA-Catalog.md. Fill docs/V1-QA-Pass-3-Results.md. TESTING only. Stay at $0. Do not tofu apply/destroy. Do not commit. Do not start 8.6.1 or 9.1.
Use MCMANAGER_CONFIG_DIR = mcmgr-blank-test, not repo data/config.local.json. SSH key from that config (Pass 2 reused mcmgr_ed25519_20260817_125552). Disable idle while VM1 is up; re-enable when you finish.
Run S2-17 only if the Function exists (follow-on P12). Stop after Phase A. Prompt sequential steps in Agent mode (not Plan mode). Include this same Agent-vs-Plan instruction in the prompt for the following phase.
```

### Phase B

```text
Read docs/V1-QA-Pass-3-Scope.md Phase B only and those catalog IDs (S3-01, S3-02, optional S5-05). I will click Hybrid / Minecraft. Fill Pass 3 results. TESTING only. Stay at $0. Do not tofu apply. Do not commit. Do not Delete infrastructure. Do not re-run operator-preconfirmed IDs.
Use MCMANAGER_CONFIG_DIR = mcmgr-blank-test, not repo data/config.local.json. Prompt sequential steps in Agent mode (not Plan mode). Include this same Agent-vs-Plan instruction in the prompt for the following phase.
```

### After Pass 3 (closed)

Phase 8.5 exited 2026-08-27. Do not run triage or a Pass 3 bug-fix plan. See [`NEXT.md`](NEXT.md) for **8.6.1**.
