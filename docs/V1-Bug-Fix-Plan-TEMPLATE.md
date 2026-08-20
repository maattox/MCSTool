# V1 bug-fix plan — template (copy to Pass N)

**Status:** Copy this file to `docs/V1-Bug-Fix-Plan-Pass-N.md`. Do **not** implement from the template.  
**Created from:** filled [`V1-QA-Pass-N-Results.md`](V1-QA-Pass-1-Results.md) after **operator triage**.  
**Parent:** [`V1-Implementation-Plan.md`](V1-Implementation-Plan.md) Phase 8.5.  
**Catalog:** [`V1-QA-Catalog.md`](V1-QA-Catalog.md).

This file’s creation session **must not implement code**. Later agents implement **only the single section marked NEXT**.

**Cost:** $0. TESTING profile only. Never `DEFAULT` / live Forge lab. Never Minecraft `0.0.0.0/0`.  
**Functions:** agents **may** `fn build` / `fn push` / invoke **product** Functions on TESTING without asking, still $0 — no real $1 budget fire; do not SoftStop the door.  
**Tofu:** `tofu apply` / `destroy` only if the operator authorizes that command in the session.

---

## How agents must use this file

1. Read **this protocol**, the [Progress dashboard](#progress-dashboard), and **only the NEXT section**.  
2. Implement only that section. Do not start neighbors “while you are here.”  
3. After finishing: mark **DONE**, set the next incomplete section to **NEXT**, changelog line, **stop**.  
4. If you change a test VM or TESTING cloud resource, make the **same** change in local SoT (`onbox/`, `infra/`, `door_vm/`, `vm_agent/`, `functions/shutdown_vm/`, Manager/Setup). File lab [`docs/Issues.md`](../../OCI-mc-server-manager/docs/Issues.md) for on-box/Setup/door bugs.  
5. Never create git commits. Suggest a message.  
6. Do not start V1 Step 9.1. Do not implement after-v1 PRODUCT-IDEAS items.  
7. VM1: START if needed, **disable idle** while working, **re-enable** when finished (re-disable after Minecraft start — OS-ISSUE-7).

### Context budget

Read this header + **one** section + the files listed there. Do not load the full V1 plan, blueprint, or PRODUCT-IDEAS unless a heading is named.

### Operator prompt

```text
Read docs/V1-Bug-Fix-Plan-Pass-N.md in OCI-mc-server. Implement only the section marked NEXT (or the PARALLEL-OK section I named).
You MAY use OCI CLI/API with profile TESTING (not DEFAULT) and SSH both test VMs. You MAY fn build/push/invoke product Functions on TESTING. Stay at $0. Do not tofu apply/destroy unless I authorize it in this chat. Do not commit. Do not start Step 9.1.
If you need VM1, START it, disable idle, re-enable when finished.
When done: update this plan’s statuses, file Issues.md if on-box/Setup/door, stop, tell me what you did, how to test, what’s next, and ask if I want to continue.
Prompt in Agent mode (not Plan mode) unless the section is marked PLAN-FIRST.
```

### PARALLEL-OK

Only when two sections **do not** edit the same files. Hybrid Razor/CSS is sequential by default.

---

## What already happened (do not re-fix)

_(Pass writer: 5–10 lines from the results file. Link catalog IDs. List Known/parked items that must not be reopened.)_

---

## Progress dashboard

| ID | Section | Status | Parallel? | Live SSH/OCI? |
|----|---------|--------|-----------|----------------|
| **P1** | _(title)_ | **NEXT** | SEQUENTIAL | Yes/No |
| **P2** | | TODO | | |

Renumber **P1…** per pass. One user-visible fix per section when possible.

---

## P1 — _(title)_

**Status:** NEXT  
**Catalog IDs:**  
**Severity:**  

**Read first**

- _(≤ ~8 files or named doc headings)_

**Do**

- 

**Test**

- Catalog IDs to re-run:  

**Done when:**  

**Changelog:** _(empty)_

---

## Plan changelog

| Date | Note |
|------|------|
| | Created from Pass N results. **NEXT = P1**. Do not implement in the creation session. |
