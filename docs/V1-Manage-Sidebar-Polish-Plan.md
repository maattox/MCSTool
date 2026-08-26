# V1 Manage sidebar polish (living)

**Status:** COMPLETE (P1–P2 DONE). Created 2026-08-25. **Live NEXT:** [`NEXT.md`](NEXT.md) → Step **8.14** ([`V1-Manage-UI-Pass-3-Plan.md`](V1-Manage-UI-Pass-3-Plan.md)). Pass 3 stays **blocked**.  
**Parent:** [`V1-Implementation-Plan.md`](V1-Implementation-Plan.md) Step **8.13**.  
**Branch:** `UI-redesign` — keep this work here until the operator likes it. Do not merge to `main` from an agent chat.  
**Why now:** operator 2026-08-25 — after Step **8.12**, Manage still reads as one flat `--bg` field. Make the status/power/pins band, the vertical tab list, and the content pane look like three distinct panels; equalize and compact the pin cards; make the tab list taller with larger buttons; narrow the whole sidebar. Topology still [`assets/UI-design-mockup.png`](../assets/UI-design-mockup.png). Vague spacing and exact hex: agents **decide inside each section’s bounds** (and [Scrutiny](#scrutiny-plan-decisions)). Stop and ask for spend, `tofu destroy`, `DEFAULT`, or parked after-v1 items.

**Cost:** $0. TESTING profile only. Never `DEFAULT` / live Forge lab. Never Minecraft `0.0.0.0/0`.  
**Functions / tofu:** do **not** `tofu apply` / `destroy`. Do not SoftStop the door.  
**Hybrid config dir:** `MCMANAGER_CONFIG_DIR` = `mcmgr-blank-test` — **not** repo `data/config.local.json`.  
**SSH / VM1:** not required.  
**Hosts / OCIDs:** do **not** paste live OCIDs, IPs, Auth Tokens, or key material into tracked docs.

Applies to **Manage** (`MainLayout`) only. Setup and FirstRun keep their wizard layout and global `--bg`; do not restyle them to match the new Manage content pane.

---

## How agents must use this file

1. Read **this protocol**, the [Progress dashboard](#progress-dashboard), [Scrutiny](#scrutiny-plan-decisions), and **only the NEXT section**.
2. Implement only that section. Do not start neighbors “while you are here.”
3. After finishing: mark **DONE**, set the next incomplete section to **NEXT**, changelog line, update V1 plan Step **8.13** + dashboard, update [`NEXT.md`](NEXT.md), **stop**.
4. Git: commits allowed per `git-policy`; never push/PR unless the operator asks.
5. Do **not** start Step **8.5.2** (Pass 3), **8.6.1**, or **9.1**.
6. If this plan disagrees with [`PRODUCT-IDEAS.md`](PRODUCT-IDEAS.md), **follow this plan** and note drift. Do not rewrite PRODUCT-IDEAS.
7. **UI-heavy sections** must read the named UI skills **before** changing CSS/Razor. Reuse existing granite + cobalt tokens. **NuGet** on `McManager.Hybrid` only. No Avalonia.
8. User-visible manage changes: add a **short** paragraph to [`Guide.md`](Guide.md) in **P2** (P1 may skip Guide if the bands are still getting their final density).

Vague notes: **decide** (exact sidebar px, pin min-height, tab type size) inside the section **using Scrutiny**. **Stop and ask** for legal/ToS, spend, or scope listed in [Parked](#parked-not-this-plan).

### Context budget

This header + **one** P-section + the files listed there. Blueprint: **named §§ only**. Do not load Pass 3, the full V1 plan, or PRODUCT-IDEAS unless a heading is named.

### UI skills (required where the section says **UI skill**)

Read **before** CSS/Razor:

- `C:\Users\matto\.agents\skills\impeccable\SKILL.md`
- `C:\Users\matto\.agents\skills\web-design-guidelines\SKILL.md`

Optional visual pass: `C:\Users\matto\.agents\skills\frontend-design\SKILL.md`. Treat the mockup as **panel contrast + density**, not pixels. Keep `--bg` / `--caption-bg` / `--surface-1` / `--fill-accent` / existing card language. Do not invent a new type world.

### Operator entry

New Agent chat → `/next-step`. Fresh chat per P-section.

### PARALLEL-OK

None. Every section edits `MainLayout.razor` and/or `app.css` manage chrome.

---

## Progress dashboard

| ID | Title | Status | Parallel | Cursor |
|----|-------|--------|----------|--------|
| P1 | Three-zone chrome + narrower sidebar | **DONE** | SEQUENTIAL — shell grid + sidebar wrap | agent |
| P2 | Compact equal pins + larger tabs + Guide | **DONE** | SEQUENTIAL — same sidebar chrome | agent |

---

## What already exists (do not rediscover)

- Manage (8.12): full-bleed caption, then `.mcm-sidebar` (status card, combined Start/Stop + Restart, four pins in 2×2, vertical `.mcm-tabs`) | `.mcm-tab-body`. CSS `.mcm-app-manage` is pad | `--manage-sidebar-width` (**296px**) | pad | fluid content | pad. Sidebar and content share page `--bg` (`#161a21`), so the three regions do not read as separate panels. Content pane has no distinct fill.
- Status card: `.mcm-srow` is a **64px** label column + fluid value. Play IP is icon-only copy. Operator: lots of empty space to the right of that card at 296px.
- Pins: four `.mcm-statcard` in `.mcm-pins` (`repeat(2, minmax(0, 1fr))`). **Today's uptime** and **This month** include a mini-bar; **Rollover bank** and **Idle timeout** do not. Cards size to content, so the two rows (and the two cells in a row) are not equal. Pin set stays: today, rollover, this month %, idle timeout. Daily average and Hours left stay on Usage.
- Tabs: `.mcm-tabs` `flex: 1 1 auto`, buttons `padding: 7px 10px`, `font-size: 13px`, icons 16px. Operator: list should be taller; buttons and labels bigger. Compact the pins to free that height.
- Window: default **1280** CSS px, min **920**. Do not change those unless a named section says so. Setup/FirstRun inherit the window; they must still fill width.
- Power rules, tab order, Overview/About, caption (bell/gear/minmax/close), toasts, Change-pack dock: **unchanged**.

---

## Scrutiny (plan decisions)

Implementing agents follow these unless the operator overrides in chat.

**Visual world.** This pass **is** allowed to give Manage three stepped panel fills. It is **not** a new palette, typeface, or card language. Keep twilight granite + cobalt. Setup/FirstRun stay on global `--bg`. Do not pixel-match the Photoshop file. Mockup hexes (`#161A21`, `#1E242E`, `#3C434C`) are **contrast hints**, not a required dump into `:root`.

**Three zones (P1).**

1. **Chrome band** — status card, power row, pins. Darkest of the three. Prefer existing `--bg` (`#161a21`, already the mockup’s chrome fill).
2. **Nav band** — vertical tab list. One step lighter. Prefer existing `--surface-1` (`#1e242e`, already the mockup’s tab fill).
3. **Content pane** — tab pages. Lightest of the three so it reads as the work surface. Global `--bg` is too close to the chrome band; `--surface-2` (`#262d38`) may still be too dark vs the mockup’s `#3C434C`. Add a **Manage-scoped** token (e.g. `--manage-content-bg` on `.mcm-app-manage`) if existing tokens do not step clearly. Do **not** change `:root --bg` (that would restyle Setup/FirstRun).

The two sidebar bands must look like **panels**, not tinted padding on the same field. Flush the sidebar to the **left window edge** under the caption (drop the 16px left gutter that currently shows the same `--bg`). Internal padding stays on the controls. Content pane fills the rest; a 1px `--border` between sidebar and content is OK if the fills need a hard edge. Caption stays full-bleed and `--caption-bg`.

**Sidebar width (P1).** 296px is too wide. Target ballpark **232–248px** (`--manage-sidebar-width`); UI skill picks the exact value. Tighten the status card so Status / Play IP / Players occupy the column (shrink the 64px label track; reduce card padding). Power buttons and pins **follow** that narrower column — do not leave a wide card sitting in a skinny rail. Tab labels (especially **Troubleshooting**) must stay **one line**. Do not change default window 1280 or min 920 in this plan.

**Pins (P2).** All four cards **same width and same height**. Use the 2×2 grid (`grid-auto-rows: 1fr` or a shared `min-height`) so a mini-bar on two cards cannot make one row taller. Compact: smaller padding, tighter type, single-line labels if they still fit. Keep the four metrics and help buttons. Mini-bars may stay if they fit inside the equal cell; drop or collapse them only if they force the cards taller than the compact target. Do not change which four stats are pinned.

**Tabs (P2).** The nav band should **eat leftover sidebar height** (taller tab region after pins shrink). Individual `.mcm-tab` buttons: larger hit target and **larger text** than 13px (ballpark 15–16px, icons ~18px; skill picks). Active highlight stays full-width in the nav band. Keep Tabler icons and the 8.12 order. Inner `.mcm-subtabs` in the content pane stay as they are.

**Not this plan.** Pin set, Start/Stop rules, Overview/About copy, caption buttons, Setup wizard steps, window default/min size.

---

## Drift vs PRODUCT-IDEAS (follow this plan)

| Topic | PRODUCT-IDEAS | This plan |
|-------|---------------|-----------|
| Manage chrome | Horizontal top tabs (sketch) | **Keep 8.12 vertical sidebar**; three panel fills |
| Pin row | Six top-chrome cards | **Four** equal compact cards (8.12 set) |

Do **not** rewrite PRODUCT-IDEAS to match.

---

## Parked (not this plan)

| Item | Where |
|------|--------|
| New visual world (type, Setup restyle, caption redesign) | Operator asked for Manage panel contrast only |
| Change which four stats are pinned | 8.12 lock; mockup “Hours left” stays on Usage |
| Window default / min size | 8.12 lock (1280 / 920) |
| Pass 3, 8.6.1, 9.1 | Existing V1 parking |
| Players tab, donation URL, light pack swap | After-v1 / parked |

---

## After this plan

When P1–P2 are **DONE**: follow-on is Step **8.14** ([`V1-Manage-UI-Pass-3-Plan.md`](V1-Manage-UI-Pass-3-Plan.md)). Do **not** start Pass 3, **8.6.1**, or **9.1** from this plan.

---

## P1 — Three-zone chrome + narrower sidebar

**Status:** DONE  
**Parallel:** SEQUENTIAL — manage grid + sidebar wrap  
**Cursor mode:** agent  
**UI skill:** yes

**Read first**

- This section + Scrutiny (Visual world, Three zones, Sidebar width)
- [`assets/UI-design-mockup.png`](../assets/UI-design-mockup.png) (panel contrast only)
- `src/McManager.Hybrid/Components/Layout/MainLayout.razor` (`.mcm-sidebar` structure)
- `src/McManager.Hybrid/wwwroot/css/app.css` (`.mcm-app-manage` grid, `--manage-sidebar-width`, `.mcm-sidebar`, `.mcm-status-card`, `.mcm-srow`, `.mcm-tab-body`)
- `src/McManager.Hybrid/Components/Setup/SetupWizard.razor` (layout class only — still fills width; still `--bg`)

**Do**

1. Wrap sidebar into two bands: **chrome** (status + power + pins) and **nav** (`.mcm-tabs`). Paint the three zones per Scrutiny. Sidebar flush to the left edge under the caption.
2. Narrow `--manage-sidebar-width` into the 232–248px ballpark. Tighten the status card (label column + padding) so it is not a short text block in a wide well. Power and pins use the same column width.
3. Do **not** restyle Setup/FirstRun fills. Do not change pin heights, tab type size, window 1280/920, or power/tab behavior.

**Test**

- `dotnet run` Hybrid (`mcmgr-blank-test`): Manage shows three distinct panel fills (chrome / tabs / content). Sidebar is visibly narrower; status/play IP still fit; **Troubleshooting** does not wrap. Resize still fills; Setup/FirstRun still usable on `--bg`.
- `dotnet test` Core tests still pass (no behavior change expected).

**Done when**

- Chrome, nav, and content read as three panels; sidebar is narrower; status card is not swimming in empty width.

**Changelog:** 2026-08-25 — **DONE**. Flush 244px sidebar (chrome `--bg` + nav `--surface-1`); content pane `--manage-content-bg` (`#3c434c`). Status card label track is auto; Setup/FirstRun still `--bg`.

---

## P2 — Compact equal pins + larger tabs + Guide

**Status:** DONE  
**Parallel:** SEQUENTIAL — same sidebar as P1  
**Cursor mode:** agent  
**UI skill:** yes

**Read first**

- This section + Scrutiny (Pins, Tabs)
- `src/McManager.Hybrid/Components/Layout/MainLayout.razor` (pins + `.mcm-tabs` markup)
- `src/McManager.Hybrid/wwwroot/css/app.css` (`.mcm-pins*`, `.mcm-statcard*`, `.mcm-tabs`, `.mcm-tab`)
- [`docs/Guide.md`](Guide.md) (Manage sidebar paragraph only)

**Do**

1. Make all four pin cards the **same dimensions**. Compact them (padding/type) so the nav band can grow.
2. Enlarge tab buttons and labels; let the nav band fill leftover sidebar height. Keep one-line labels at the P1 width.
3. Short Guide note: three Manage panels + narrower sidebar + equal compact pins. Do not rewrite the Guide.

**Test**

- Hybrid: all four pins match in size (Stopped and with live usage numbers / wrapping hints). Tab list is taller; buttons are easier to hit and read. Min-width 920: tabs still one line; content pane still usable. Overview / Whitelist / Console still switch.

**Done when**

- Pins are equal and compact; tabs are larger and the nav band is taller; Guide has one short paragraph.

**Changelog:** 2026-08-25 — **DONE**. Equal 2×2 pins (`grid-auto-rows: 1fr`, reserved mini-bar slot, compact type); tabs 15px / 18px icons and `flex: 1` to fill leftover nav height. Guide note added.
