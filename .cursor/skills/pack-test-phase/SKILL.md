---
name: pack-test-phase
description: Runs a sequential pack-corpus phase on TESTING VM1 — lock, disable idle, one pack-test-one at a time, abort after two consecutive infra_fail. Use when the operator invokes /pack-test-phase or asks to run the pack-test queue. Does not write the executive summary.
disable-model-invocation: true
---

# Pack test phase

Parent orchestrator. SoT: [`pack-tests/PROTOCOL.md`](../../../pack-tests/PROTOCOL.md) — **Phase parent**, **Abort**, **Ready-gate**. Do not duplicate them. Do not write `EXECUTIVE-SUMMARY.md`.

## Invariants

- OCI **TESTING** only. `MCMANAGER_CONFIG_DIR` = **`mcmgr-pack-test`**. Never `DEFAULT` / live Forge lab. Never repo `data/config.local.json`. Never `mcmgr-blank-test`.
- One TESTING VM1. Never parallel `ReplacePackAsync` / never two `pack-test-one` at once.
- No `git push`. No pack downloads. No `tofu apply` / `destroy`. Do not SoftStop the door.
- Never read full `logs/` journals or paste consoles into markdown.
- Re-enable idle **only** at phase end (complete or abort).

## Lock

Take or create `pack-tests/.lock` (gitignored). If it already exists and is **not** this phase → **stop** (Pass 3 / another chat owns VM1). Do not steal.

Write a single line the harness can match (phase directory name must appear):

```text
pack-test-phase:<phase_id>
```

Copy `pack-tests/phases/_template/` to `pack-tests/phases/<phase_id>/` if needed. Set manifest `status: running`.

## Idle (whole phase)

Read [`docs/Agent-Deploy-Pitfalls.md`](../../../docs/Agent-Deploy-Pitfalls.md) before SSH. START VM1 if STOPPED. Disable idle for the **whole** phase (`idle_agent_enabled=false`, stop+disable `mc-idle-watch.timer`). Copy-paste: `docs/V1-Implementation-Plan.md` heading **VM1 power + idle agent** — that snippet only. The harness re-disables after each live replace (OS-ISSUE-7). Re-enable only when releasing the lock.

## Queue

For each `queue[]` id **in order**:

1. Spawn **one** `/pack-test-one` (Composer 2.5 OK, not Fast). Wait until `results/<id>.yaml` exists **and** `ready_for_next: true`.
2. If verdict is `infra_fail`, increment `consecutive_infra_fails`; else reset to `0`. If `>= 2` → PROTOCOL **Abort** (`status: aborted`). Stop spawning.
3. Do not abort on `product_fail` / `blocked_freeze` / `timeout` / `pass` / `pass_quarantined`.

On empty remaining queue: `status: complete`. Release `pack-tests/.lock`. Re-enable idle. **Stop.** Tell the operator to run `/pack-test-analyze` in a **new** chat (Grok, not Composer). Do not start that analyze yourself. Do not `/phase-planning`. Do not start QA Pass 3.
