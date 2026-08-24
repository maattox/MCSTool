---
name: pack-test-analyze
description: Writes pack-corpus EXECUTIVE-SUMMARY.md from result YAML and client-only sidecars after a phase completes or aborts. Use when the operator invokes /pack-test-analyze. Use Grok, not Composer. Does not implement product fixes or start /phase-planning unless asked.
disable-model-invocation: true
---

# Pack test analyze

**Grok, not Composer.** After the queue is `complete` or `aborted`. SoT: [`pack-tests/PROTOCOL.md`](../../../pack-tests/PROTOCOL.md) — **Analyze**. Parent does **not** write this file.

## Do

1. Confirm the phase manifest is `complete` or `aborted`. If still `running`, **stop**.
2. Read result YAML under `phases/<phase_id>/results/`, matching `pack-tests/client-only/<id>.yaml` sidecars, and catalog rows. Do **not** load full `logs/` journals.
3. Write `pack-tests/phases/<phase_id>/EXECUTIVE-SUMMARY.md` clustered by: infra vs client-jar kept vs Java vs overlay leftover vs RCON-timeout-with-Done vs quarantine.
4. TESTING notes only. No OCIDs, IPs, or secrets. No pack downloads. No `git push`.

## Do not

- Implement product fixes.
- `/phase-planning` unless the operator asks in this chat.
- Start `/pack-test-phase`, `/pack-test-one`, or QA Pass 3.
- Re-enable or disable idle (phase already ended).
- Treat sidecar lists as Skip-during-install (analysis only).
