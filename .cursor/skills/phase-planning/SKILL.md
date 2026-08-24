---
name: phase-planning
description: Turns operator note dumps into a living implementation plan with grouped steps, SEQUENTIAL/PARALLEL-OK markers, Cursor mode hints, and docs/NEXT.md updates. Use when the operator invokes /phase-planning or asks to create or refresh a multi-agent phase plan from notes. Does not implement code.
disable-model-invocation: true
---

# Phase planning

Create or update a **living plan** from operator notes. **Do not implement code** in this session.

## Before writing

1. Read [`docs/NEXT.md`](../../docs/NEXT.md) for current work (do not advance it unless this session is closing a phase).
2. Read [`docs/Agent-Workflow.md`](../../docs/Agent-Workflow.md) for conventions.
3. Ask clarifying questions only for spend, `DEFAULT` profile, `tofu destroy`, or scope outside v1 — otherwise decide inside bounds.

## Intelligent grouping

- **Do not** map one bullet = one step by default.
- **Combine** related notes (e.g. three UI tweaks → one “Setup wizard polish” step).
- **Split** only when dependencies, file ownership, or risk differ (e.g. door deploy vs Hybrid-only CSS).
- Order steps by dependency; note what each step **reads** (context budget).

## Step template

Each section in the new/updated plan:

```markdown
## Pn — Short title

**Status:** NEXT | TODO | DONE | DEFERRED | SKIPPED
**Parallel:** SEQUENTIAL | PARALLEL-OK — one-line reason
**Cursor mode:** agent | plan-first | either

**Read first**
- (minimal file list)

**Do**
1. …

**Test**
- …

**Done when**
- …

**Changelog:** *(date when finished)*
```

### Parallel rules

- `PARALLEL-OK` only when sections **do not** edit the same files **and** do not both own the TESTING stack.
- Shared Razor/CSS wizard flows are usually **SEQUENTIAL**.
- Mark parallel groups in a short “Parallel groups” table if helpful.

### Cursor mode

- `agent` — implement in Agent mode.
- `plan-first` — design/tradeoffs before code; if the operator used Agent mode, **ask them to switch to Plan mode** or wait after posting a design.
- `either` — agent chooses.

## Plan document structure

New plans go under `docs/` with:

- Header: status, parent step, cost/TESTING reminders (short — details live in rules).
- **How agents must use this file** — read protocol + dashboard + **only the NEXT section**; update `docs/NEXT.md` when advancing.
- Progress dashboard table.
- Parked / out-of-scope table.
- **After this plan** — what updates when the plan completes.

Point agents at **`docs/NEXT.md`** as the live NEXT pointer; avoid duplicating NEXT in `AGENTS.md` or rules.

## Update `docs/NEXT.md`

When creating a new phase or closing one:

| Field | Set to |
|-------|--------|
| Plan | path to living plan |
| Step | V1 step or phase name |
| Sub-plan | plan file if applicable |
| Sub-step | first section id (e.g. P1) or unset |
| Status | `ready` or `blocked` with reason |
| Cursor mode | mode for the first sub-step |

## Finish

1. Write or update the living plan file.
2. Update `docs/NEXT.md`.
3. If parent is V1, add one line to V1 dashboard / step changelog (do not rewrite the whole V1 file).
4. Tell the operator: summary, first sub-step, how to test after it, and **“Start a new Agent chat and run `/next-step`.”**
5. Do **not** output a long copy-paste prompt unless the operator asks.
