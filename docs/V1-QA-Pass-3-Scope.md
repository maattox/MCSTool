# V1 QA Pass 3 — scope (gap-close + follow-on)

**Pass:** 3  
**Status:** **BLOCKED** — Steps **8.7** and **8.8** are **DONE**. Step **8.9** (pack-import assisted review) is **in front**. Do **not** run until **8.9** completes **and** the operator says Pass 3 may start. (Step **8.4** P1–P13 is already DONE.) Living execution slice of [Step 8.5.2](V1-Implementation-Plan.md#step-852--execute-qa-passes). **Live queue:** [`NEXT.md`](NEXT.md).  
**Catalog:** [`V1-QA-Catalog.md`](V1-QA-Catalog.md) — IDs and expected steps stay there. Do **not** regenerate the catalog. Implementing follow-on sections may **update expected** for product changes (S4-02 tabs, S3-01 overlay, S6-02 jar-root).  
**Results:** fill [`V1-QA-Pass-3-Results.md`](V1-QA-Pass-3-Results.md) as you go.  
**Prior:** [`V1-QA-Pass-2-Results.md`](V1-QA-Pass-2-Results.md) (greenfield Modded; closed early). [`V1-QA-Pass-1-Results.md`](V1-QA-Pass-1-Results.md) (Vanilla; historical).

This is a **delta + gap-close**, not a second encyclopedia. Pass 1 already Passed most Vanilla chrome. Pass 2 already Passed Delete + greenfield Modded, live Setup Deploy, modded join, and Modding/Download pack. Pass 3 exists to run what those passes **skipped**, plus tests for **Step 8.4**, **8.7**, and **8.8** follow-on changes.

**Cost:** $0. TESTING only. Never `DEFAULT` / live Forge lab. Never Minecraft `0.0.0.0/0`.  
**Do not start** Step **8.6.1** or **9.1**. Do not create a Pass 3 bug-fix plan until this pass is filled and the operator asks for triage.  
**Tofu:** do **not** `tofu destroy` / second greenfield unless the operator authorizes it. Prefer the **existing** Pass 2 TESTING stack (`mcmgr-blank-test`).

---

## Why this pass is different

| | Pass 1 | Pass 2 | Pass 3 |
|---|--------|--------|--------|
| Stack | Existing TESTING (old) | **Destroy + Setup** Modded | **Keep** Pass 2 stack unless operator says otherwise |
| Game | Vanilla | Modded (FO 6.5.0 Fabric) | Still Modded (may have changed pack in P11) |
| Catalog | Full S0–S7 (S7-04 skipped) | Delta include-list; **closed early** | Leftover include-list **plus** follow-on checks |
| Tofu | Forbidden | Phase A authorized destroy-then-apply | **Forbidden** unless operator says so |

---

## How agents must use this file

1. Read **this protocol**, the [Progress](#progress) line, and **only the phase you were asked to run**.  
2. Catalog expected steps: open **named IDs** in [`V1-QA-Catalog.md`](V1-QA-Catalog.md). Do not load the Minecraft blueprint, PRODUCT-IDEAS, or the whole V1 file.  
3. Fill [`V1-QA-Pass-3-Results.md`](V1-QA-Pass-3-Results.md). Out-of-scope rows stay `Skipped` — do **not** re-run Pass 1 chrome that already Passed unless a follow-on section changed those files.  
4. **One agent chat owns the TESTING stack at a time.**  
5. Mirror guest/cloud fixes into local SoT. File [`Issues.md`](Issues.md) for Setup/HCL/bootstrap/door bugs.  
6. Git: commits allowed; never push/PR unless the operator asks.  
7. Hybrid: agents **cannot** drive the WPF window. Operator clicks; agent stages/verifies OCI/SSH.  
8. VM1: START if needed, **disable idle** while working, **re-enable** when the phase ends (re-disable after Minecraft start — OS-ISSUE-7).  
9. SSH with the key in TESTING `config.local.json` (Pass 2 reused `mcmgr_ed25519_20260817_125552`).  
10. **Never** use product repo `data/config.local.json` (live Forge / `DEFAULT`).

### Context budget

This file + the named catalog IDs + [`Guide.md`](Guide.md) for changed copy. Follow-on Fail → also the named P-section in [`V1-Pass-2-Follow-On-Plan.md`](V1-Pass-2-Follow-On-Plan.md), [`V1-Modpack-Test-Follow-On-Plan.md`](V1-Modpack-Test-Follow-On-Plan.md), or [`V1-Operator-Notes-Follow-On-Plan.md`](V1-Operator-Notes-Follow-On-Plan.md).

---

## Progress

| Phase | Focus | Status |
|-------|--------|--------|
| **A** | S0 smoke + Pass 2 leftover on-box inspect (S1/S2) | **TODO** — after follow-on + operator |
| **B** | Hybrid leftovers + follow-on UI (S3/S4/S5) | TODO — after A |
| **C** | Setup leftovers (jar-root continue, Deploy Complete if re-opened) | TODO — after B |

**NEXT = Phase A** only after Steps **8.7** and **8.8** exit and the operator says Pass 3 may run.

---

## Include vs skip

### In scope (run these)

**Phase A — agent**

| ID | Why |
|----|-----|
| **S0-01** | Follow-on Core tests; QA-exit smoke |
| **S0-04** | Cheap; skipped in Pass 2 |
| **S1-01–S1-05** | Pass 2 never snapshotted the new stack |
| **S2-01–S2-11** | Pass 2 never inspected greenfield SoT (P1 cloud-init, door, wake, idle, heal, lock GET) |
| **S2-16, S2-17** | Function should exist after follow-on **P12**; skip S2-17 only if still absent |
| **S2-21, S2-22** | Cheap while Minecraft is up |

**Phase B — Hybrid / operator**

| ID | Why |
|----|-----|
| **S3-01** | Overlay confirm must **not** Start (follow-on P1) |
| **S3-02, S3-03** | Pass 2 did not record doorbell Start/Stop |
| **S3-04** | P4 leftover CIDR; skipped in Pass 2 |
| **S3-07** | Wipe auto-start; skipped in Pass 2 |
| **S4-01** | Novice chrome + **Players** pin (P1) |
| **S4-02** | Tabs: Danger Zone **merged into Advanced** (P3) — use **updated** catalog expected |
| **S4-08** | With S3-04 |
| **S4-09** | Usage + **Detailed usage** expander (P8) |
| **S4-11** | Modding + **Change pack** if P11 shipped |
| **S4-12** | Identity apply on Setup `vm_agent` |
| **S4-13** | Console simple vs full (P6) |
| **S4-18** | Idle controls now under Advanced → Danger Zone (P3) |
| **S5-01, S5-02** | MOTD / wake on **modded** (Pass 2 join was already PLAYABLE) |
| **S5-05** | Daily-exhaust Manager Start (P6 from Pass 1) |

**Phase C — Setup**

| ID | Why |
|----|-----|
| **S6-02** | Jar-root / user zip **continue** (P9). P7 jar-less CF still hard-block. Do not Deploy a second stack. |
| **Deploy Complete page** | Re-open Setup / use a finished Deploy page (P2) — reserved IP + Copy + close hint |

**S8:** fill as you hit known issues.

### Add when Steps 8.7 / 8.8 exit (do not run now)

Pass 3 writers should **add catalog expected / include-list rows** for these once those plans are DONE (do not invent IDs here until then):

- Crash-loop vs slow-start fail copy (8.7 P1); Java major applied on Change pack (8.7 P4)
- Console Simple is not a near-copy of Full (8.8 P1); no tab-open backup/infra toasts (8.8 P2)
- Compact toasts; progress dock on Deploy / Change pack (8.8 P3–P4)
- Setup: no Compartment step; identity page; taller deploy log; no “stack” in novice copy (8.8 P5–P7)
- Door idle/starting/exhausted favicons from user/default icon + overlays (8.8 P8)
- Jar-root confirm fields (8.8 P9); quarantine UI if a one-mod crash is available (8.8 P10)
- CF client-export still blocked **with** project links (8.8 P11)

### Out of scope (do not re-run)

- **S7-04** — Pass 2 already destroyed + deployed. Do not Delete again.  
- **S7-02, S7-03** — Pass 1 Pass; no resize/world-replace unless a Fail requires it.  
- **S4-03, S4-10, S4-14–S4-17, S4-19–S4-22** — Pass 1 Pass chrome, unless a Fail shows up while running an in-scope ID.  
- **S6-01 live Deploy**, **S6-03–S6-05** — Pass 1/2 already covered; do not greenfield.  
- **S0-02, S0-03, S0-05** — unchanged / optional gcc.  
- **S3-05** — Pass 2 Pass (modded join). Re-run only if P11 changed the pack.  
- **S3-06** — Pass 1 Pass oversized-world bell.

If a skipped ID **regresses**, record it under Additional problems or promote to Fail.

---

## Restore (every mutating session)

DELETE spend-brake lock; restore idle timeout; re-enable idle unless the next Hybrid chat needs it off. Do not leave VM1 RUNNING overnight without asking.

---

## Operator prompts (copy-paste)

Do **not** use these until Steps **8.7** and **8.8** are DONE.

### Phase A

```text
Read docs/V1-QA-Pass-3-Scope.md in OCI-mc-server (protocol + Phase A only) and the named catalog IDs in docs/V1-QA-Catalog.md. Fill docs/V1-QA-Pass-3-Results.md. TESTING only. Stay at $0. Do not tofu apply/destroy. Do not commit. Do not start 8.6.1 or 9.1.
Use MCMANAGER_CONFIG_DIR = mcmgr-blank-test, not repo data/config.local.json. SSH key from that config (Pass 2 reused mcmgr_ed25519_20260817_125552). Disable idle while VM1 is up; re-enable when you finish.
Run S2-17 only if the Function exists (follow-on P12). Stop after Phase A. Prompt sequential steps in Agent mode (not Plan mode). Include this same Agent-vs-Plan instruction in the prompt for the following phase.
```

### Phase B

```text
Read docs/V1-QA-Pass-3-Scope.md Phase B only and those catalog IDs. I will click Hybrid / Minecraft. Fill Pass 3 results. TESTING only. Stay at $0. Do not tofu apply. Do not commit. Do not Delete infrastructure.
```

### Phase C

```text
Read docs/V1-QA-Pass-3-Scope.md Phase C only. Analyze a jar-root / user zip (continue after P9). Re-open finished Setup for Deployment Complete + IP copy. Do not Deploy a second stack. Do not start 8.6.1 or 9.1.
```
