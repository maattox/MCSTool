# Agent workflow

How operators and agents run multi-step work in this repo. **Current work** always starts at [`NEXT.md`](NEXT.md).

## Daily loop

1. **Note dump** → new Agent chat → `/phase-planning` (+ paste notes).
2. Review the generated or updated living plan; approve.
3. **New Agent chat** per implementation step → `/next-step` (or “implement NEXT”).
4. Operator tests; repeat step 3 until the phase is done.
5. New note dump → back to step 1.

Use a **fresh chat** per step to stay under the context window. Do not rely on one long chat for an entire phase.

## Skills (project)

| Skill | When |
|-------|------|
| `/phase-planning` | Turn operator notes into a living plan; group related items; mark SEQUENTIAL / PARALLEL-OK; set `docs/NEXT.md`. **No code.** |
| `/next-step` | Read `docs/NEXT.md`; implement only the active section; update docs; stop. |
| `/pack-test-one` | One catalog id via PackTestHarness; `pack-tests/PROTOCOL.md` + ready-gate; stop. Composer 2.5 OK. |
| `/pack-test-phase` | Lock, sequential `/pack-test-one`, abort ≥2 `infra_fail`; no full logs; no executive summary. |
| `/pack-test-analyze` | After phase complete/abort: results + sidecars → `EXECUTIVE-SUMMARY.md`. Grok, not Composer. |

Skills live in `.cursor/skills/`. Rules in `.cursor/rules/` hold short invariants (cost, git, models, deploy pitfalls).

## Living plans

- [`V1-Implementation-Plan.md`](V1-Implementation-Plan.md) — top-level V1 checklist.
- Follow-on files (`V1-*-Follow-On-Plan.md`, QA scope) — section detail for a step.
- **`docs/NEXT.md`** — the only live **NEXT** pointer. Plan headers may say “see NEXT.md”.

### Section metadata (new plans)

Each step should include:

| Field | Values |
|-------|--------|
| **Parallel** | `SEQUENTIAL` or `PARALLEL-OK` (+ one-line reason) |
| **Cursor mode** | `agent` · `plan-first` · `either` |

- **`plan-first`:** design or tradeoffs before code. Agent must ask the operator to switch to **Plan mode**, or produce a short design and wait for approval.
- **`PARALLEL-OK`:** only when file sets and TESTING stack ownership do not overlap.

`/phase-planning` should **group** related notes (e.g. several UI bullets → one step), not map one note = one step.

### Subagents

Even on `SEQUENTIAL` steps, the implementer should spawn subagents for:

- codebase search → `explore`
- builds, tests, SSH → `shell`

Parent keeps context small; subagents use cheaper models when appropriate.

## OCI and git (agents)

- Default **TESTING** profile and `mcmgr-blank-test` Hybrid config — not repo `data/config.local.json` unless the step says otherwise.
- **`DEFAULT`:** only when the operator explicitly allows it in that chat.
- **`tofu apply` / `destroy`:** ask first; explain cost/risk; stay $0 unless spend is accepted.
- **Commits:** allowed; **push / PR:** never unless explicitly asked.

See also [`Agent-Deploy-Pitfalls.md`](Agent-Deploy-Pitfalls.md) before SSH/SFTP deploy.
