---
name: next-step
description: Implements the current work item from docs/NEXT.md and the active living-plan section only. Updates plan statuses and NEXT.md, uses subagents for explore/shell, then stops. Use when the operator invokes /next-step or asks to implement NEXT.
disable-model-invocation: true
---

# Next step

Implement **only** the current item from [`docs/NEXT.md`](../../docs/NEXT.md).

## Start

1. Read `docs/NEXT.md`. If `status` is `blocked`, **stop** and report — do not implement.
2. Open the plan named there; read **only** the active sub-step section (+ protocol header if first visit).
3. Check **Cursor mode** on that section:
   - `plan-first` and you are in Agent mode → ask the operator to switch to **Plan mode**, or post a short design and wait.
   - `agent` → proceed.
4. Read [`docs/Agent-Deploy-Pitfalls.md`](../../docs/Agent-Deploy-Pitfalls.md) before SSH/SFTP/sudo deploy.

## Context budget

- `docs/NEXT.md` + one plan section + files listed in **Read first**.
- Do not load full V1 plan, PRODUCT-IDEAS, or blueprint unless the section names them.

## Subagents

Parent orchestrates; spawn subagents for:

| Task | Subagent |
|------|----------|
| Find code, map codebase | `explore` |
| Build, test, SSH, scripts | `shell` |
| Narrow implement slice | `generalPurpose` (optional) |

Do not do large explore + implement + docs in one bloated parent thread if subagents can isolate work.

If using Composer 2.5 or Grok as the model for a subagent, do NOT use "Fast" mode. This wastes tokens.

## OCI / git / tofu

- Default OCI profile: **TESTING**. `DEFAULT` only if this chat explicitly allows.
- Hybrid: `MCMANAGER_CONFIG_DIR` = `mcmgr-blank-test` unless the step says otherwise.
- `tofu apply` / `destroy`: **ask the operator first**; state cost/risk; stay **$0** unless they accept spend.
- **Commits:** allowed when the step is done or when asked. **Never** push, open PRs, force-push, rebase, or reset unless explicitly asked.
- Stay at **$0**; never Minecraft `0.0.0.0/0`; do not SoftStop the door casually.

## While working

- Implement **only** this section — not neighbors “while you are here.”
- VM1: START if needed; **disable idle** while working; **re-enable** when finished (OS-ISSUE-7).
- UI sections: read impeccable + web-design-guidelines skills when the plan says so.
- Mirror TESTING fixes into local SoT; file [`docs/Issues.md`](../../docs/Issues.md) for on-box/Setup/door bugs.

## Finish

1. Mark section **DONE**; set next section **NEXT** (or close plan if last).
2. Update **`docs/NEXT.md`** (sub-step, status, date).
3. Update parent plan dashboard/changelog if applicable.
4. If user-visible UX changed, touch [`docs/Guide.md`](../../docs/Guide.md).
5. **Stop.** Reply with: what changed, how to test, what `docs/NEXT.md` says now.
6. Tell the operator to start a **new** Agent chat for the following step (`/next-step`).

Do **not** auto-start the next section in the same chat unless the operator explicitly asks to continue.
