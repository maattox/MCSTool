# V1 bug-fix plan — Pass 1

**Status:** Living. Created 2026-08-19 from [`V1-QA-Pass-1-Results.md`](V1-QA-Pass-1-Results.md) after **operator early triage** (paused after S2). **P1–P3 DONE** 2026-08-19. Operator may resume catalog **S3**. Do not start 9.1.  
**Parent:** [`V1-Implementation-Plan.md`](V1-Implementation-Plan.md) Step **8.5.2** (stays NEXT until Phase 8.5 exits).  
**Catalog:** [`V1-QA-Catalog.md`](V1-QA-Catalog.md) — do not edit expected steps.

This file’s creation session **did not implement code**. Later agents implement **only the single section marked NEXT**.

**Cost:** $0. TESTING profile only. Never `DEFAULT` / live Forge lab. Never Minecraft `0.0.0.0/0`.  
**Functions:** agents **may** `fn build` / `fn push` / invoke **product** Functions on TESTING without asking, still $0 — no real $1 budget fire; do not SoftStop the door.  
**Tofu:** `tofu apply` / `destroy` only if the operator authorizes that command in the session.

Hosts/OCIDs: `%LOCALAPPDATA%\McManager\tofu\mcmgr\outputs.json`. SSH: `ubuntu` + `%USERPROFILE%\.ssh\mcmgr_ed25519_20260817_125552`. **Do not** use product `data/config.local.json` (live Forge lab).

---

## How agents must use this file

1. Read **this protocol**, the [Progress dashboard](#progress-dashboard), and **only the NEXT section**.  
2. Implement only that section. Do not start neighbors “while you are here.”  
3. After finishing: mark **DONE**, set the next incomplete section to **NEXT**, changelog line, **stop**.  
4. If you change a test VM or TESTING cloud resource, make the **same** change in local SoT (`onbox/`, `infra/`, `door_vm/`, `vm_agent/`, `functions/shutdown_vm/`, Manager/Setup). File lab [`docs/Issues.md`](../../OCI-mc-server-manager/docs/Issues.md) for on-box/Setup/door bugs.  
5. Never create git commits. Suggest a message.  
6. Do not start V1 Step 9.1. Do not start catalog **S3**. Do not implement after-v1 PRODUCT-IDEAS items.  
7. VM1: START if needed, **disable idle** while working, **re-enable** when finished (re-disable after Minecraft start — OS-ISSUE-7).

### Context budget

Read this header + **one** section + the files listed there. Do not load the full V1 plan, blueprint, or PRODUCT-IDEAS unless a heading is named.

### Operator prompt

```text
Read docs/V1-Bug-Fix-Plan-Pass-1.md in OCI-mc-server. Implement only the section marked NEXT (or the PARALLEL-OK section I named).
You MAY use OCI CLI/API with profile TESTING (not DEFAULT) and SSH both test VMs. You MAY fn build/push/invoke product Functions on TESTING. Stay at $0. Do not tofu apply/destroy unless I authorize it in this chat. Do not commit. Do not start Step 9.1. Do not start S3.
If you need VM1, START it, disable idle, re-enable when finished.
When done: update this plan’s statuses, file Issues.md if on-box/Setup/door, stop, tell me what you did, how to test, what’s next, and ask if I want to continue.
Prompt sequential steps in Agent mode (not Plan mode). Use Build in Parallel / Plan mode only if the NEXT step is marked PARALLEL-OK. Include this same Agent-vs-Plan instruction in the prompt you give the operator for the following step.
```

### PARALLEL-OK

Only when two sections **do not** edit the same files. Hybrid Razor/CSS is sequential by default.

---

## What already happened (do not re-fix)

Pass 1 **S0–S2** are recorded in [`V1-QA-Pass-1-Results.md`](V1-QA-Pass-1-Results.md). Do not re-run the full S2 suite in the P1 session.

- **Pass (leave):** S2-01–04, 06–10, 20–22, 28. Wake (second try) and 2-minute idle SoftStop worked when dbus/firewalld were up.  
- **S2-05 Fail:** one S1 START boot had dbus+firewalld never started; later wake/raw START boots were fine. `netfilter-persistent` is already **masked** (SETUP-ISSUE-7 fix present — do not re-solve that).  
- **S2-11 Fail:** live door `pull_os_budget.sh` has no spend-brake GET (product SoT does). **P2**, not this section.  
- **S2-16/17/18 Pass (P3):** TESTING already had `mcmgr-fn-softstop` + OCIR `mcmgr-fn/softstop:setup` (`func.yaml` **0.0.12**, VM1-only config). Synthetic ACTUAL SoftStops VM1 + lock PUT; RESET is `SKIPPED`; door stays RUNNING. **P3 DONE.**  
- **Known / by design:** OS-ISSUE-7 idle re-enable on Minecraft start (S2-28 Pass). DOOR-ISSUE-1 first-kick. OS-ISSUE-6 heal-only-when-STOPPED (S2-10 Pass).  
- **OS-ISSUE-5** ledger mitigations around hung SoftStop already shipped; the **guest ACPI STOPPING hang itself** was left open. That hang is **P1**.

---

## Progress dashboard

| ID | Section | Status | Parallel? | Live SSH/OCI? |
|----|---------|--------|-----------|----------------|
| **P1** | Guest ACPI SoftStop stuck STOPPING + UFW/firewalld/dbus | **DONE** | SEQUENTIAL | Yes |
| **P2** | Door spend-brake lock GET on TESTING (S2-11) | **DONE** | SEQUENTIAL | Yes |
| **P3** | TESTING `shutdown_vm` Function image (S2-16–18) | **DONE** | SEQUENTIAL | Yes (fn/Docker) |

**No further P-sections.** Operator may resume Pass 1 catalog **S3**. Do not start 9.1.

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

## Plan changelog

| Date | Note |
|------|------|
| 2026-08-19 | Created from Pass 1 S0–S2. Operator paused before S3. **NEXT = P1** (STOPPING + UFW/firewalld/dbus). P2 = S2-11 door lock GET. P3 = Function image. Do not implement in the creation session. Do not start S3 or 9.1. |
| 2026-08-19 | **P1 DONE.** OS-ISSUE-9 = firewalld/cloud-init/dbus cycle (not UFW nft). Full firewalld unit override + mask UFW. SoftStop 43s ×3. **NEXT = P2**. Do not start S3 or 9.1. |
| 2026-08-19 | **P2 DONE.** DOOR-ISSUE-10: CLI 3.90 404 = unlocked in `pull_os_budget.sh`; TESTING door script + `mccontrol` rebuilt. S2-11 Pass (wake refuses while locked; 404 absent → `SPEND_BRAKE_LOCK=0`). **NEXT = P3**. Do not start S3 or 9.1. |
| 2026-08-19 | **P3 DONE.** TESTING `shutdown_vm` already 0.0.12 on `mcmgr-fn-softstop` / `mcmgr-fn/softstop:setup`. S2-16–18 Pass (ACTUAL SoftStops VM1 + lock; RESET skipped; door up). FN-ISSUE-1 gone on TESTING (Forge lab still 0.0.11). Pass 1 bug-fix plan has **no further P-sections**. Operator may resume catalog **S3**. Do not start 9.1. |
