# Workflow state

**Updated:** 2026-08-24

Single source of truth for **what to work on next**. Living plan files keep section history; agents update **this file** when advancing work.

## Current

| Field | Value |
|-------|-------|
| **Plan** | [`V1-Implementation-Plan.md`](V1-Implementation-Plan.md) |
| **Step** | **8.10** Operator-notes follow-on 2 |
| **Sub-plan** | [`V1-Operator-Notes-Follow-On-2-Plan.md`](V1-Operator-Notes-Follow-On-2-Plan.md) |
| **Sub-step** | **P9** MOTD formatting editor |
| **Status** | `ready` |
| **Cursor mode** | plan-first |

## Design lock (implementing)

Pack import contract: [`Pack-Import-Intended-Design.md`](Pack-Import-Intended-Design.md) — **implemented** (Step **8.9** P1–P2). Pass 3 stays **blocked** until Step **8.10** exits **and** the operator starts it.

## Completed recently

- **2026-08-24** — Step **8.10 P8 DONE** (VM1 color icon: `PrivateTmp` so ImageIO can encode the favicon; stage PNG then `install` as `mcmgr`). Living **NEXT = P9** (plan-first). Pass 3 **blocked**.
- **2026-08-24** — Step **8.10 P7 DONE** (Setup Minecraft step: Vanilla/Modded primary column; flavor/pack drop beside it). Living **NEXT = P8**. Pass 3 **blocked**.
- **2026-08-24** — Step **8.10 P6 DONE** (Setup Always Free explainer; OCI profile `<pre>` grows; OCI + budget email combined, schema v4). Living **NEXT = P7**. Pass 3 **blocked**.
- **2026-08-24** — Step **8.10 P5 DONE** (pack identity version dropdowns; detected extra option; catalog-fail text fallback). Living **NEXT = P6**. Pass 3 **blocked**.
- **2026-08-24** — Step **8.10 P4 DONE** (Server inner tabs; mods collapsed; Usage/Advanced/Troubleshooting density). Living **NEXT = P5**. Pass 3 **blocked**.
- **2026-08-24** — Step **8.10 P3 DONE** (tab **Server**, new order, keep-alive + deferred load, confirm-then-build derived zip). Living **NEXT = P4**. Pass 3 **blocked**.
- **2026-08-24** — Step **8.10 P2 DONE** (custom WindowChrome caption, 1040 CSS px, wordmark gone). Living **NEXT = P3**. Pass 3 **blocked**.
- **2026-08-24** — Step **8.10 P1 DONE** (toasts bottom-left, 4s fade, start-success AutoHide). Living **NEXT = P2** (plan-first). Pass 3 **blocked**.
- **2026-08-24** — Step **8.10** inserted (density / MOTD / VM1 icon operator notes). Living **NEXT = P1**. Pass 3 **blocked**.
- **2026-08-24** — Step **8.9 P2 DONE** (assisted review UI + persist Skip + Guide). Plan **COMPLETE**.
- **2026-08-24** — Step **8.9 P1 DONE** (Core skip order + dependency freeze + review grouping).
- **2026-08-23** — Pack-import **intended design** locked; Step **8.9** plan created (P1 NEXT).
- **Step 8.8** — Operator-notes follow-on **P1–P10 DONE** ([`V1-Operator-Notes-Follow-On-Plan.md`](V1-Operator-Notes-Follow-On-Plan.md) **COMPLETE**)
- **P11** (CurseForge refuse helper) — **DEFERRED** → [`PRODUCT-IDEAS.md`](PRODUCT-IDEAS.md) (maybe later; not scheduled)
- **Step 8.7** — Modpack-test follow-on **DONE**

## Agent entry

1. Read this file first.
2. If `status` is `blocked`, **stop** and tell the operator — do not implement.
3. Otherwise invoke `/next-step` or implement only the named sub-step.
4. After finishing a step: update this file, the living plan section statuses, and stop.

## Policy (summary)

- **OCI default:** profile `TESTING`. `DEFAULT` / live Forge lab **only** if this chat explicitly authorizes.
- **Git:** commits allowed when finishing work or when asked; **never** `git push`, `gh pr`, force-push, rebase, or reset unless explicitly asked.
- **Tofu:** `tofu apply` / `destroy` allowed **after asking** the operator; stay **$0** unless they accept spend.
- **Models:** never **Fast** on Grok 4.6 or Composer 2.5.
- **Subagents:** parent orchestrates; use `explore` / `shell` for search and tests.
- **Workflow:** [`Agent-Workflow.md`](Agent-Workflow.md) · skills: `/phase-planning`, `/next-step`
