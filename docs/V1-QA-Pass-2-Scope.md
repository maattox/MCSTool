# V1 QA Pass 2 — scope (greenfield + modded)

**Pass:** 2  
**Status:** **PAUSED** until Step **4.13** / [`V1-Modpack-Robustness-Plan.md`](V1-Modpack-Robustness-Plan.md) **R4** is DONE. Do **not** run Phase A or `tofu destroy` until then. Living execution slice of [Step 8.5.2](V1-Implementation-Plan.md#step-852--execute-qa-passes). Docs created 2026-08-19.  
**Catalog:** [`V1-QA-Catalog.md`](V1-QA-Catalog.md) — IDs and expected steps stay there. Do **not** regenerate the catalog.  
**Results:** fill [`V1-QA-Pass-2-Results.md`](V1-QA-Pass-2-Results.md) as you go.  
**Prior pass:** [`V1-QA-Pass-1-Results.md`](V1-QA-Pass-1-Results.md) (Vanilla on the **existing** TESTING stack; **S7-04 Skipped**). Bug-fix [`V1-Bug-Fix-Plan-Pass-1.md`](V1-Bug-Fix-Plan-Pass-1.md) **P1–P8 DONE**.

This is a **delta pass**, not a second encyclopedia. Pass 1 already proved Vanilla manage UI, doorbell, idle, spend-brake Function invoke, and most chrome on the old stack. Pass 2 exists to prove what that pass never did: **Delete + greenfield Setup** and a **live Modded** server from a sample pack — plus re-checks of Pass 1 fixes that only make sense on a **new** install.

**Cost:** $0. TESTING only. Never `DEFAULT` / live Forge lab. Never Minecraft `0.0.0.0/0`.  
**Do not start** Step **8.6.1** or **9.1**. Do not create [`V1-Bug-Fix-Plan-Pass-2.md`](V1-Bug-Fix-Plan-TEMPLATE.md) until this pass is filled and the operator asks for triage.

---

## Why this pass is different

| | Pass 1 | Pass 2 |
|---|--------|--------|
| Stack | Existing TESTING VMs (`mcmgr-blank-test`) | **Destroy, then Setup Deploy** (S7-04) |
| Game | Vanilla (`distribution=vanilla`, MC 26.2) | **Modded** from one gitignored sample pack |
| Catalog | Full S0–S7 (S7-04 skipped) | **Include list below only** |
| Tofu | Forbidden unless operator said so | **Authorized in the Phase A prompt** (TESTING destroy-then-apply only) |

---

## How agents must use this file

1. Read **this protocol**, the [Progress](#progress) line, and **only the phase you were asked to run**.  
2. Catalog expected steps: open **named IDs** in [`V1-QA-Catalog.md`](V1-QA-Catalog.md). Do not load the Minecraft blueprint, PRODUCT-IDEAS, or the whole V1 plan.  
3. Fill [`V1-QA-Pass-2-Results.md`](V1-QA-Pass-2-Results.md). Out-of-scope rows are already `Skipped` — do **not** re-run them “while you are here.”  
4. **One agent chat owns the TESTING stack at a time.**  
5. If you change a test VM or TESTING cloud resource, make the **same** change in local SoT (`onbox/`, `infra/`, `door_vm/`, `vm_agent/`, `functions/shutdown_vm/`, Manager/Setup). File lab [`docs/Issues.md`](../../OCI-mc-server-manager/docs/Issues.md) for Setup/HCL/bootstrap/door bugs.  
6. Never create git commits.  
7. Hybrid: agents **cannot** drive the WPF window. Operator clicks; agent stages/verifies OCI/SSH.  
8. VM1: START if needed, **disable idle** while working, **re-enable** when the phase ends (re-disable after Minecraft start — OS-ISSUE-7).  
9. After Setup, SSH with the **new** key the wizard generated (path in the new `config.local.json`). Do **not** assume Pass 1’s `mcmgr_ed25519_20260817_125552` still matches.  
10. **Never** use product repo `data/config.local.json` (live Forge / `DEFAULT`).

### Context budget

This file + the named catalog IDs + [`Guide.md`](Guide.md) **Tear down and redeploy** + [`Sample-Packs.md`](Sample-Packs.md) pack row. Setup/bootstrap Fail → also [`Agent-Deploy-Pitfalls.md`](Agent-Deploy-Pitfalls.md).

---

## Progress

| Phase | Focus | Status |
|-------|--------|--------|
| **A** | S0 smoke + **S7-04 greenfield Modded Setup** | **PAUSED** (wait for 4.13) |
| **B** | S1 + selected S2 on the **new** stack | TODO — after A |
| **C** | Hybrid delta (Pass 1 Fail retests + modded join) | TODO — after B |
| **D** | Remaining operator UI / play-path in-scope IDs | TODO — after C |

**NEXT = Phase A** after Step **4.13** exits. Do not start until the operator says Pass 2 may run. Stop after each phase unless the operator says continue.

---

## Pack (pick one before Deploy)

**Paused:** do not Deploy until 4.13 is DONE. After that, this row may prefer a pack that exercises override-list stripping (S6-02 warning). Until the 4.13 session updates this row:

Live VM1: **at most one** small Fabric or NeoForge pack ([`Sample-Packs.md`](Sample-Packs.md)). Files live in gitignored `data/sample-packs/` (exact names in `data/sample-packs/README.txt`). Do not commit packs. Do not download mega-packs.

**Recommended (Pass 2 default):** Modrinth NeoForge **BlockFront**  
`data/sample-packs/real/modrinth-neoforge-BlockFront - Official Mod Pack 0.9.0.27b.mrpack`

- Tiny real pack (loader **NeoForge**, Minecraft **1.21.1**).  
- Pass 1 never installed a loader or pack on VM1.  
- Sodium tagged `unsupported` correctly (unlike Fabulously Optimized).

**Fallback** if BlockFront is missing or Deploy is too heavy: `data/sample-packs/homemade/fabric-strip.mrpack` (Fabric 1.21.1, real CDN URLs, good `env.server` strip).

**Do not Deploy with:** Fabulously Optimized / OptiFine-for-Fabric / MMC3 (heavy or all-`required` until 4.13 lands); CurseForge **client** exports (no jars — Setup must **hard-block**, P7); Infinite Horizons (~305 mods); **MilesPack** (~300 MB jar-root zip).

Record the chosen filename + loader + Minecraft version in the results header. Friends (and the operator’s client) need **that same exported file** — vanilla Java cannot join (S3-05).

**P7 during Setup (cheap):** before picking BlockFront, drop `homemade/curseforge-synthetic.zip` (or any jar-less CF zip) and confirm the wizard **cannot continue**. Then pick the real `.mrpack`. That is the S6-02 incomplete-CF retest — do not call the CurseForge API (Step 4.12 stays deferred).

---

## Shape, hours, Function

- Setup default VM1 is **4 OCPU / 24 GB**. Pass 2 **must choose 2 OCPU / 12 GB** unless the operator overrides. Mid-month Always Free Ampere hours **do not reset** on Delete ([`Guide.md`](Guide.md) tear-down). A second 4/24 would burn the envelope faster.  
- New ledger starts at **zero**; do not treat Usage leftover hours as Oracle’s clock.  
- Until Step **8.6.1**, Setup may **skip** the spend-brake Function (no Docker / no CI image copy). That is **not** a Pass 2 Fail of 8.6.1. Record S2-16 as Pass (Function present) or Skipped (Setup skipped Function). Run S2-17 only if the Function exists.  
- Auth Token still belongs in Credential Manager when the operator has one; never print it.

---

## Tofu (Phase A only)

The Phase A operator prompt **is** authorization to `tofu destroy` then `tofu apply` on profile **TESTING** for this stack only.

**Hard rules**

1. **Destroy first.** Never create a second Always Free A1 beside the old one.  
2. Danger Zone **Delete infrastructure**, type `confirm`, keep the window open until tofu returns ([`Guide.md`](Guide.md) tear-down).  
3. Close Manager fully, reopen, then Setup.  
4. Profile **`TESTING`** in the wizard — the picker also lists `DEFAULT`; do not select it.  
5. Hybrid `MCMANAGER_CONFIG_DIR` = the TESTING folder (Pass 1: `mcmgr-blank-test`). After Delete, that folder’s `config.local.json` is gone. Reuse it **or** a new empty dir (e.g. `mcmgr-pass-2`) so first-run Setup appears. **Never** the repo `data/` Forge seed.  
6. Compartment default `mcmgr`. Do not delete the Oracle tenancy.  
7. Agents still must not `tofu apply` / `destroy` **outside** Phase A, and never on `DEFAULT`.

---

## Include vs skip

Catalog rule: Pass 2+ = last-pass **Fails** + **smoke** + tests for **changed files** + what Pass 1 never covered. Full re-run only at QA exit.

### In scope (run these)

**Phase A — greenfield**

| ID | Why |
|----|-----|
| **S0-01** | Core tests changed in P4–P8; QA-exit smoke |
| **S0-04** | `tofu validate` before a real apply |
| **S6-01** | Live Setup pages **with Deploy** (Pass 1 walked pages and did **not** Deploy) |
| **S6-02** | Analyze the chosen pack; P7 jar-less CF **hard-block** |
| **S7-04** | Delete + greenfield — the point of this pass |

Phase A **Done when:** TESTING stack is a playable doorbell; game-manifest is **modded** with the chosen loader; 25565 not world-open; VM1 shape **2/12** (unless overridden); new ledger; lock absent; idle on at session end (or say why not). Record whether Setup installed the Function.

**Phase B — new-stack inspect** (proves greenfield SoT, including P1 cloud-init and P2 door scripts)

| ID | Why |
|----|-----|
| **S1-01–S1-05** | New hosts / new SSH key / snapshot |
| **S2-01** | `User=mcmgr` / `/opt/mcmgr` on a **Setup** box (not the old hand-patched VM) |
| **S2-02** | Manifest `distribution=modded` + loader (not Vanilla) |
| **S2-03, S2-04, S2-06** | RCON/SL/whitelist contract on a new SL |
| **S2-05** | P1 firewalld override + mask UFW must land from **cloud-init**, not a live patch |
| **S2-07** | Door `mccontrol` + play secondary from Setup |
| **S2-08, S2-09** | QA-exit smoke wake + short idle |
| **S2-10** | Heal on a new ledger (STOPPED) |
| **S2-11** | Fresh door must GET the lock (P2 product path, not a later SSH patch) |
| **S2-16** | Function present vs Setup-skipped |
| **S2-17** | QA-exit smoke **only if** Function exists |
| **S2-21, S2-22** | Cheap while Minecraft is up |

**Phase C — Hybrid delta**

| ID | Why |
|----|-----|
| **S3-01** | QA-exit overlay smoke on the new Manager config |
| **S3-04** | P4 leftover prefix CIDR |
| **S3-05** | **Modded join** with the same pack; vanilla client **fail** is Pass |
| **S3-07** | P8 wipe **auto-starts** Minecraft (Pass 1 was leave-stopped) |

Skip **S3-02 / S3-03** if Phase A already recorded door-aware Start (play IP on VM1, joinable) and Stop (IP back on door). Note that in S3-02/S3-03 as Skipped with pointer to S7-04.

**Phase D — operator leftovers**

| ID | Why |
|----|-----|
| **S4-01** | QA-exit novice chrome smoke |
| **S4-08** | With S3-04 (Advanced CIDR) |
| **S4-11** | Live **Modding** inspect + Download pack (Pass 1 deferred — Vanilla empty-state only) |
| **S4-12** | P5 identity apply on a stack that got `vm_agent` from Setup |
| **S5-01, S5-02** | Client MOTD / wake once on **modded** (not a second idle-clock study) |
| **S5-05** | P6 **Manager Start while daily exhausted** only (player refuse + admin Start; spend-brake still blocks). Restore cap. Skip the sudden-cap chat / PT-vs-UTC parked items. |

**S8:** fill as you hit known issues. Do not file duplicates unless worse than documented.

### Out of scope (already `Skipped` in the results file)

Do **not** re-run:

- **S0-02, S0-03, S0-05** — Function/door unit tests unchanged; optional `make test` still no gcc.  
- **S2-09b, S2-12, S2-18, S2-19, S2-20, S2-26, S2-28** — Pass 1 Pass / optional / by-design.  
- **S3-06** — oversized-world bell; Pass 1 Pass.  
- **S4-02, S4-03, S4-09, S4-10, S4-13–S4-22** — chrome / Console / Troubleshooting / Danger Zone dialogs; Pass 1 Pass. (S4-20 still means **do not Delete again** in Phase D.)  
- **S5-03, S5-04** — occupied idle / empty idle from the player view; skip if **S2-09** Pass.  
- **S6-03, S6-04, S6-05** — Connect-existing / repair-resume / dry-run; Pass 1 Pass. Live Deploy replaces dry-run.  
- **S7-02, S7-03** — live shape scale and world-replace; Pass 1 Pass.

If a skipped ID **regresses** during an in-scope test, record it under [Additional problems](V1-QA-Pass-2-Results.md#additional-problems) or promote to Fail — do not silently ignore.

---

## Phase A procedure (first chat)

Agent: S0-01, S0-04, then watch OCI/SSH. Operator: Hybrid clicks.

1. **S0-01** `dotnet test src\McManager.slnx` and **S0-04** `tofu validate` in `infra/` (no apply). Stop on Fail.  
2. Confirm OCI profile **TESTING** (`oci os ns get --profile TESTING`). Confirm you will **not** touch `DEFAULT`.  
3. Launch Hybrid with `MCMANAGER_CONFIG_DIR` = the TESTING manage folder that still has tofu state (`mcmgr-blank-test` from Pass 1).  
4. Operator: Danger Zone → Delete infrastructure → type `confirm`. Wait until destroy succeeds.  
5. Close Manager. Reopen (same folder or a new empty `mcmgr-pass-2`). First-run → **Setup**.  
6. Wizard: Always Free checkboxes, profile **TESTING**, compartment `mcmgr`, **Modded**, file = chosen pack, client-pack checkboxes, EULA, admin `/32`, shape **2/12**. Optional: jar-less CF zip first (S6-02 P7). **Deploy.**  
7. Agent: waiter-style polls (not 1s). One A1 + one Micro only. Mirror any guest fix into SoT.  
8. **S7-04 expected:** doorbell (play IP on door when idle/stopped; wake moves it); private SL; `User=mcmgr`; manifest matches the pack; Minecraft eventually listen on 25565. New Object Storage ledger.  
9. Restore: DELETE lock if created; idle **on**; prefer VM1 **STOPPED**, play IP on door. Record the **new SSH key** path in the session log (**no** key material).  
10. **Stop.** Do not start Phase B unless the operator says so.

**Blocked:** destroy with no LocalAppData tofu state (this PC did not deploy the stack). Stop and ask — do not Console-wipe random compartments.

---

## Operator prompts (copy-paste)

### Phase A (first chat) — greenfield + modded

```text
Read docs/V1-QA-Pass-2-Scope.md in OCI-mc-server (protocol + Phase A only) and the named catalog IDs in docs/V1-QA-Catalog.md. Fill docs/V1-QA-Pass-2-Results.md as you go. Do not re-run Pass 1 rows already marked Skipped in that file.
Pass 1 is done (Vanilla, no greenfield). This chat is Pass 2 Phase A only: S0-01, S0-04, S6-01/S6-02 as live Setup, then S7-04 Delete + greenfield.
You MAY tofu destroy then tofu apply on profile TESTING only for this Phase A stack. Destroy the existing TESTING product stack first. Never a second Always Free A1. Never DEFAULT / live Forge lab. Never Minecraft 0.0.0.0/0. Stay at $0.
I will click Hybrid. Use MCMANAGER_CONFIG_DIR for the TESTING folder (Pass 1: mcmgr-blank-test), not repo data/config.local.json (Forge / DEFAULT). After Delete, close Manager, reopen, Setup.
Setup: profile TESTING, Modded, sample pack BlockFront .mrpack (or homemade/fabric-strip.mrpack if we say so in chat), VM1 shape 2 OCPU / 12 GB, client-pack confirmations. Before the real pack, try a jar-less CurseForge zip and confirm the wizard hard-blocks (P7).
You MAY fn build/push/invoke product Functions on TESTING. Setup may skip the Function until Step 8.6.1 — record that; it is not a Fail of 8.6.1. Do not fire a real $1 budget alert. Do not SoftStop the door.
If Setup/bootstrap is wrong, file lab docs/Issues.md and fix product onbox/infra/door_vm/vm_agent — do not only patch the new VMs.
After Setup, SSH with the new wizard key in the new config, not the Pass 1 key unless config still points there. If you need VM1, disable idle while working; re-enable when you finish (OS-ISSUE-7 after Minecraft start).
When Phase A is done: update Pass 2 results + V1 plan 8.5.2 changelog, stop, tell me what failed, how to test the doorbell, what’s next (Phase B), and ask if I want to continue.
Do not commit. Do not start Phase B, Step 8.6.1, or 9.1 unless I say so.
Prompt sequential steps in Agent mode (not Plan mode). Use Build in Parallel / Plan mode only if the NEXT step is marked PARALLEL-OK. Include this same Agent-vs-Plan instruction in the prompt you give me for the following phase.
```

### Phase B — agent inspect

```text
Read docs/V1-QA-Pass-2-Scope.md Phase B only and those catalog IDs in docs/V1-QA-Catalog.md. Fill docs/V1-QA-Pass-2-Results.md. TESTING only. Stay at $0. Do not tofu apply/destroy. Do not commit. Do not start 8.6.1 or 9.1.
Use the new Setup SSH key from the Pass 2 config dir. Disable idle while VM1 is up; re-enable when you finish. Run S2-17 only if the Function exists.
Stop after Phase B. Prompt sequential steps in Agent mode (not Plan mode). Include this same Agent-vs-Plan instruction in the prompt for the following phase.
```

### Phase C — Hybrid delta

```text
Read docs/V1-QA-Pass-2-Scope.md Phase C only and those catalog IDs. Stage fixtures; wait for me to click Hybrid / Minecraft (matching the deployed pack). Fill S3 rows in docs/V1-QA-Pass-2-Results.md. TESTING only. Stay at $0. Do not tofu apply. Do not commit.
Skip S3-02/S3-03 if Phase A already proved doorbell Start/Stop. Prompt sequential steps in Agent mode (not Plan mode). Include this same Agent-vs-Plan instruction in the prompt for the following phase.
```

### Phase D — operator leftovers

```text
Read docs/V1-QA-Pass-2-Scope.md Phase D only. I will click the in-scope S4/S5 IDs (Modding panel, identity, CIDR with S3-04, MOTD/wake, daily-exhaust Manager Start). Fill docs/V1-QA-Pass-2-Results.md. Do not Delete infrastructure again. Do not start 8.6.1 or 9.1.
```

### Triage (after the pass is filled)

```text
Read docs/V1-QA-Catalog.md (protocol only) and the filled docs/V1-QA-Pass-2-Results.md. Do not write product code. Create docs/V1-Bug-Fix-Plan-Pass-2.md from the template. Triage Fail vs Known vs after-v1. Stop and ask me to confirm severity before any agent implements fixes.
```

---

## Restore (every mutating session)

Same as the catalog: DELETE spend-brake lock; restore idle timeout; re-enable idle unless the next Hybrid chat needs it off (say so in the results file). Do not leave VM1 RUNNING overnight without asking. After greenfield, the “original” timeout is whatever Setup wrote (often 15).
