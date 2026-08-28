# Workflow state

**Updated:** 2026-08-27

Single source of truth for **what to work on next**. Living plan files keep section history; agents update **this file** when advancing work.

## Current

| Field | Value |
|-------|-------|
| **Plan** | [`V1-Implementation-Plan.md`](V1-Implementation-Plan.md) Phase **9** / Step **9.1** |
| **Step** | **9.1** Windows installer |
| **Sub-plan** | [`V1-Packaging-Plan.md`](V1-Packaging-Plan.md) |
| **Sub-step** | **P3** Windows installer (Inno) + Function tar + Release recipe |
| **Status** | `ready` |
| **Cursor mode** | `agent` |

Phase **8.6** is **DONE**. **P2 DONE** (2026-08-27): pinned OpenTofu 1.12.6 Windows amd64 zip downloads once into `%LOCALAPPDATA%\McManager\tofu` (SHA-256; no WinGet). Living **NEXT = P3** ([`V1-Packaging-Plan.md`](V1-Packaging-Plan.md)): Inno Setup 6 per-user installer + Function tar + Release recipe. Do **not** start P4–P7. GitHub Actions stays **out**. Users must not need Docker. **Parked:** `mr-fabric-cobblemon-1.7.3` re-run is **UNFINISHED** (operator aborted 2026-08-27). Come back in a **separate** chat: `/pack-test-one` that id (TESTING, `mcmgr-pack-test`; disable idle for the whole replace). Do not treat the later harness `pass` YAML as verified.

## Design lock (implementing)

Pack import contract: [`Pack-Import-Intended-Design.md`](Pack-Import-Intended-Design.md) — skip order + freeze **implemented** (Step **8.9**); **review UI** is one list (Step **8.15**). Manage sidebar topology: [`assets/UI-design-mockup.png`](../assets/UI-design-mockup.png) + 8.12 Scrutiny (**COMPLETE**). Steps **8.13**–**8.15** **COMPLETE**. Phase **8.5** **COMPLETE**. Step **8.6.1** **COMPLETE**. Phase **9** packaging: [`V1-Packaging-Plan.md`](V1-Packaging-Plan.md) Scrutiny (Inno installer, GitHub prompt-not-apply, no Actions).

## Completed recently

- **2026-08-27** — Step **9.1 P2 DONE** (pinned OpenTofu 1.12.6 download + SHA-256 into LocalAppData). Living **NEXT = P3** (Inno installer). Do not start P4.
- **2026-08-27** — Step **9.1 P1 DONE** (publish layout: product tree + optional Function tar next to the exe). Living **NEXT = P2** (pinned OpenTofu download). Do not start P3.
- **2026-08-27** — Phase **9 unblocked.** Living plan [`V1-Packaging-Plan.md`](V1-Packaging-Plan.md) created. **NEXT = P1** (publish layout). Do not start P2.
- **2026-08-27** — Step **8.6.1 P2 DONE** (TESTING OCIR login proven; IAM name + identity domain; copy without Docker; digest match; synthetic ACTUAL VM1 only). Plan **COMPLETE**. Phase **8.6** **DONE**. Living **NEXT = 9.1** **blocked**. Do not start 9.1 until asked.
- **2026-08-27** — Step **8.6.1 P1 DONE** (OCIR username derived from namespace + `~/.oci` user; Deploy/repair digest converge; Guide + developer recipe). Living **NEXT = P2** (TESTING user-copy verify). Do not start 9.1.
- **2026-08-27** — Step **8.6.1** plan rewritten: developer Docker Desktop pre-build OK; users must not need Docker; GitHub Actions dropped. Living **NEXT = P1** (username + digest + Guide). Do not start 9.1.
- **2026-08-27** — Phase **8.5** **CLOSED** (8.5.3). Pass 3 triage **skipped**. S0-01 Nit **parked OK** (operator: intended overlay design, not a product bug). Living **NEXT = 8.6.1 P1**. Do not start 9.1.
- **2026-08-27** — Pass 3 **Phase B DONE** / pass **filled**. S3-01 Pass (overlay Clear lock did not Start; lock 404; VM1 STOPPED). S3-02 / S5-05 operator-confirmed Pass. Restore: lock absent, VM1 STOPPED, door RUNNING, idle 15+on. Post-Pass overlay copy nits applied (not re-tested). Living **NEXT** = triage if the operator asks. Do not start 8.6.1 or 9.1.
- **2026-08-27** — Pass 3 **Phase A DONE**. S0–S1 and leftover S2 filled (S0-01 Fail Nit; remaining in-scope S1/S2 Pass). Restore: VM1 STOPPED, door RUNNING, play IP on door, lock absent, idle 15+on. Living **NEXT = Phase B**. Do not start Phase B, 8.6.1, or 9.1 until a new Agent chat.
- **2026-08-27** — Operator aborted Phase A S1/S2 mid **S2-09**. S0 is recorded. Do not resume that runner. A later chat can continue S1/S2 from the catalog order (re-check idle timeout; it may still be 2).
- **2026-08-27** — Operator unblocked Pass 3. Living **NEXT = 8.5.2 Phase A**. Do not start Phase B, 8.6.1, or 9.1.
- **2026-08-27** — Pass 3 include-list narrowed. Operator pre-confirmed checklist **17–21**, **23–24**, **25–92**. Remaining: Phase A (agent S0/S1/S2 leftovers) + **S3-01**, **S3-02**, optional **S5-05**.
- **2026-08-27** — Operator request (off-queue): Advanced → Stack can set **separate SSH private-key paths** for the game VM and doorbell (`vm1.ssh_key_path` / `door.ssh_key_path`). Local path only — does not rotate guest keys. Pass 3 **blocked**.
- **2026-08-27** — Step **8.15 P4 DONE** (pick/review while VM stopped; Install starts VM1 then replace). Plan **COMPLETE**. Living **NEXT = 8.5.2** Pass 3 **blocked**.
- **2026-08-27** — Step **8.15 P3 DONE** (Change-pack dock overlays the pane; tab-scoped to Server → Change pack). Living **NEXT = P4**. Pass 3 **blocked**.
- **2026-08-27** — Step **8.15 P2 DONE** (Change pack compactness: locked copy, taller summary, side-by-side ingest/warnings/checkboxes). Living **NEXT = P3**. Pass 3 **blocked**.
- **2026-08-27** — Step **8.15 P1 DONE** (single-list assisted review: Client-only checkbox, identity above the list, summary hidden during review). Living **NEXT = P2**. Pass 3 **blocked**.
- **2026-08-27** — Step **8.15** inserted (Change pack UX: single-list review, compactness, overlay dock, stopped-VM pick). Living **NEXT = P1**. Pass 3 **blocked**.
- **2026-08-26** — Sidebar **Status** now shows **Running** when the Minecraft VM is already on (same “already on” signal as **Stop**), so opening Manager on a live server no longer stays **Stopped**. Pass 3 **blocked**.

- **2026-08-27** — Pack-corpus cobblemon mrpack re-run **UNFINISHED** (session aborted). VM1 Minecraft stopped; idle re-enabled; `pack-tests/.lock` released. Phase `2026-08-26` stay `complete` except that id. Pass 3 **blocked**.
- **2026-08-26** — Sidebar shrink: chrome no longer clips status/power after pins tuck; tab gaps 8px→2px; extra height stays under the tab list. Pass 3 **blocked**.
- **2026-08-26** — Sidebar: no scrollbar; pins clip away before status/power; tab gap fixed at 12px; window min-height raised so tabs stay fully visible. Pass 3 **blocked**.
- **2026-08-26** — Operator pin pass (no new plan): three stacked full-width sidebar strips (Today's uptime, This month, Rollover bank). Idle timeout left on Usage / Overview / Advanced. Pass 3 **blocked**.
- **2026-08-26** — Step **8.14** operator review: kept zoom lock, equal power `flex: 1 1 0`, P3 pins, P4 Overview; restored painted 6px strips and pre-P2 gutter/chrome padding. Guide no longer describes flush edges. Pass 3 **blocked**.
- **2026-08-25** — Step **8.14 P4 DONE** (Overview name+IP whitelist, MOTD/pack snapshot, five usage metrics + Guide). Plan **COMPLETE**. Pass 3 **blocked** until the operator says so.
- **2026-08-25** — Step **8.14 P3 DONE** (pin labels/values fully visible; stacked hint; no ellipsis). Living **NEXT = P4** (plan-first). Pass 3 **blocked**.
- **2026-08-25** — Step **8.14 P2 DONE** (flush tab-body to sidebar; chrome padding 6px; equal-width power buttons). Living **NEXT = P3**. Pass 3 **blocked**.
- **2026-08-25** — Step **8.14 P1 DONE** (flush WebView + 10 DIP resize hit-test + Ctrl+scroll zoom lock). Living **NEXT = P2**. Pass 3 **blocked**.
- **2026-08-25** — Step **8.14** inserted (third UI pass: window edge, sidebar density, pin redesign, Overview). Living **NEXT = P1** (plan-first). Pass 3 **blocked**.
- **2026-08-25** — Step **8.13 P2 DONE** (equal compact pins + larger tabs + Guide). Plan **COMPLETE**. Pass 3 **blocked** until the operator says so.
- **2026-08-25** — Step **8.13 P1 DONE** (three-zone Manage chrome + 244px flush sidebar). Living **NEXT = P2**. Pass 3 **blocked**.
- **2026-08-25** — Step **8.13** inserted (Manage sidebar polish: three-zone panels, narrower rail, equal compact pins, larger tabs). Living **NEXT = P1**. Pass 3 **blocked**.
- **2026-08-25** — Step **8.12 P5 DONE** (resize polish + Guide). Plan **COMPLETE**. Pass 3 **blocked** until the operator says so.
- **2026-08-25** — Step **8.12 P4 DONE** (Overview home tab: read-only snapshot + tab jumps). Living **NEXT = P5**. Pass 3 **blocked**.
- **2026-08-25** — Step **8.12 P3 DONE** (About tab; caption ☰ and About modal removed). Living **NEXT = P4** (plan-first). Pass 3 **blocked**.
- **2026-08-25** — Step **8.12 P2 DONE** (combined Start/Stop primary + Restart; four sidebar pins). Living **NEXT = P3**. Pass 3 **blocked**.
- **2026-08-25** — Step **8.12 P1 DONE** (two-column Manage shell: sidebar | content, 1280 default / 920 min, icon-only Copy play IP, Overview/About placeholders). Living **NEXT = P2**. Pass 3 **blocked**.
- **2026-08-25** — Step **8.12** inserted (Manage sidebar redesign). Living **NEXT = P1**. Pass 3 **blocked**.
- **2026-08-24** — Step **8.11 P4 DONE** (MOTD WYSIWYG name + description; Minecraft-font preview; 59-char counters). Follow-on 3 **COMPLETE**. Pass 3 **blocked** until the operator says so.
- **2026-08-24** — Step **8.11 P3 DONE** (MOTD wrap-with-reset + 59-char line counters in Core). Living **NEXT = P4** (plan-first). Pass 3 **blocked**.
- **2026-08-24** — Step **8.11 P2 DONE** (pin row 3×2 fills chrome; Hours left + Idle timeout from existing budget refresh). Living **NEXT = P3**. Pass 3 **blocked**.
- **2026-08-24** — Step **8.11 P1 DONE** (caption `--caption-bg` + 1px `--border`; Manage / Setup / FirstRun). Living **NEXT = P2**. Pass 3 **blocked**.
- **2026-08-24** — Step **8.11** inserted (caption contrast, pin-row fill, MOTD WYSIWYG). Living **NEXT = P1**. Pass 3 **blocked**.
- **2026-08-24** — Pack-corpus **P3 DONE** (skills `pack-test-one` / `pack-test-phase` / `pack-test-analyze`; Agent-Workflow pointer). Plan **COMPLETE**. Pass 3 **blocked**.
- **2026-08-24** — Pack-corpus **P2 DONE** (`McManager.PackTestHarness`: same Core Change-pack path, `--analyze-only`, result YAML). Living **NEXT = P3**. Pass 3 stays **blocked**.
- **2026-08-24** — Pack-corpus **P1 DONE** (layout, schemas, gitignore, `PROTOCOL.md`). Living **NEXT = P2**. Pass 3 stays **blocked**.
- **2026-08-24** — Live **NEXT** → pack-corpus test system **P1** ([`Pack-Corpus-Test-Plan.md`](Pack-Corpus-Test-Plan.md)). Pass 3 stays **blocked**.
- **2026-08-24** — Step **8.10 P9 DONE** (MOTD formatting editor: `§` toolbar + paste, list preview, omit-name, collapsed raw; VM1 preserves codes). Follow-on 2 **COMPLETE**. Pass 3 **blocked** until the operator says so.
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
