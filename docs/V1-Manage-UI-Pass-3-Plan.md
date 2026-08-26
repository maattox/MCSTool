# V1 Manage UI pass 3 (living)

**Status:** COMPLETE (P1–P4). Created 2026-08-25 (docs only). **Live NEXT:** [`NEXT.md`](NEXT.md) → Step **8.5.2** Pass 3 (**blocked** until the operator says so).  
**Parent:** [`V1-Implementation-Plan.md`](V1-Implementation-Plan.md) Step **8.14**.  
**Branch:** `UI-redesign` — keep this work here until the operator likes it. Do not merge to `main` from an agent chat.  
**Why now:** operator 2026-08-25 — third UI redesign pass after [`V1-Manage-Sidebar-Polish-Plan.md`](V1-Manage-Sidebar-Polish-Plan.md) (Step **8.13**). Window-edge chrome still reads as a 6px frame; sidebar density (gutter, padding, equal power buttons, pin text cutoff); Ctrl+scroll still zooms the WebView; Overview is spare and whitelist names hide IPs. Vague spacing and exact layout: agents **decide inside each section’s bounds** (and [Scrutiny](#scrutiny-plan-decisions)). Stop and ask for spend, `tofu destroy`, `DEFAULT`, or parked after-v1 items.

**Cost:** $0. TESTING profile only. Never `DEFAULT` / live Forge lab. Never Minecraft `0.0.0.0/0`.  
**Functions / tofu:** do **not** `tofu apply` / `destroy`. Do not SoftStop the door.  
**Hybrid config dir:** `MCMANAGER_CONFIG_DIR` = `mcmgr-blank-test` — **not** repo `data/config.local.json`.  
**SSH / VM1:** not required.  
**Hosts / OCIDs:** do **not** paste live OCIDs, IPs, Auth Tokens, or key material into tracked docs.

Applies to **Manage** plus the **shared WPF window** (Setup/FirstRun inherit edge strips and zoom lock). Do **not** restyle the Setup/FirstRun wizard to match Manage panel fills.

---

## How agents must use this file

1. Read **this protocol**, the [Progress dashboard](#progress-dashboard), [Scrutiny](#scrutiny-plan-decisions), and **only the NEXT section**.
2. Implement only that section. Do not start neighbors “while you are here.”
3. After finishing: mark **DONE**, set the next incomplete section to **NEXT**, changelog line, update V1 plan Step **8.14** + dashboard, update [`NEXT.md`](NEXT.md), **stop**.
4. Git: commits allowed per `git-policy`; never push/PR unless the operator asks.
5. Do **not** start Step **8.5.2** (Pass 3), **8.6.1**, or **9.1**.
6. If this plan disagrees with [`PRODUCT-IDEAS.md`](PRODUCT-IDEAS.md), **follow this plan** and note drift. Do not rewrite PRODUCT-IDEAS.
7. **UI-heavy sections** must read the named UI skills **before** changing CSS/Razor. Reuse existing granite + cobalt tokens. **NuGet** on `McManager.Hybrid` only. No Avalonia.
8. User-visible manage changes: add a **short** paragraph to [`Guide.md`](Guide.md) in **P4** (P1–P3 may skip Guide).

Vague notes: **decide** (strip colors, pin internal layout, Overview cards) inside the section **using Scrutiny**. **Stop and ask** for legal/ToS, spend, or scope listed in [Parked](#parked-not-this-plan).

### Context budget

This header + **one** P-section + the files listed there. Blueprint: **named §§ only**. Do not load Pass 3, the full V1 plan, or PRODUCT-IDEAS unless a heading is named.

### UI skills (required where the section says **UI skill**)

Read **before** CSS/Razor:

- `C:\Users\matto\.agents\skills\impeccable\SKILL.md`
- `C:\Users\matto\.agents\skills\web-design-guidelines\SKILL.md`

Optional visual pass: `C:\Users\matto\.agents\skills\frontend-design\SKILL.md`. Keep `--bg` / `--caption-bg` / `--surface-1` / `--fill-accent` / existing card language. Do not invent a new type world.

### Operator entry

New Agent chat → `/next-step`. Fresh chat per P-section. **P1** and **P4** are **plan-first**.

### PARALLEL-OK

| Group | Sections | Why |
|-------|----------|-----|
| A | P1 | WPF host only (`MainWindow` / chrome service) |
| B | P2 | Manage CSS/Razor — **PARALLEL-OK with P1** (different files) |

P3 waits on P2 (pin width depends on chrome padding). P4 waits on P2 (shared `app.css`; Overview sits in the content pane). P4 is **PARALLEL-OK with P1** only. Do not run P2 and P4 in the same chat.

---

## Progress dashboard

| ID | Title | Status | Parallel | Cursor |
|----|-------|--------|----------|--------|
| P1 | Window edge chrome + disable Ctrl+scroll zoom | **DONE** | PARALLEL-OK with P2 — WPF host vs CSS | plan-first |
| P2 | Sidebar density: gutter, padding, equal power | **DONE** | PARALLEL-OK with P1 — different files | agent |
| P3 | Compact pin redesign | **DONE** | SEQUENTIAL — after P2 width | agent |
| P4 | Overview enrich + Guide | **DONE** | SEQUENTIAL after P2; PARALLEL-OK with P1 | plan-first |

---

## What already exists (do not rediscover)

- **WPF edge (P1):** `WindowStyle=None` + `WindowChrome` `ResizeBorderThickness="10"`. BlazorWebView **fills** the client (painted 6px strips removed). Inner resize is a DPI-scaled ~10 DIP `WM_NCHITTEST` hook in `WpfWindowChromeService` (skipped when maximized) because the WebView2 HWND swallows chrome hit-tests. Transparency / DWM glass next to WebView2 is not viable. Default window **1280** / min **920** CSS px (`AppShellWidthDip` / `AppShellMinWidthDip`); `FitWidthToWebView` adds remaining non-client thickness. Do not change those sizes unless a named section says so.
- **Manage shell (8.13 + P2):** flush 244px sidebar (`--manage-sidebar-width`). Chrome band `--bg`, nav `--surface-1`, content `--manage-content-bg`. Sidebar `border-right: 1px`. `.mcm-tab-body` padding `14px 16px 16px 0` (flush to the 1px edge). `.mcm-sidebar-chrome` padding **6px**. Power wraps share the row (`flex: 1 1 0`).
- **Pins (P3):** equal 2×2 `.mcm-statcard` with `grid-auto-rows: 1fr`. Labels wrap (no ellipsis); value stacked above hint; compact 10px label/hint; 16px pin help. Mini-bars kept (hidden spacer on cards without one). Pin set stays: today, rollover, this month %, idle timeout.
- **Overview (P4):** `OverviewTab.razor` — Live status strip; Server (icon + MOTD + pack) beside Usage (today / month / remaining / rollover / idle); whitelist table with name + IP + Admin; tab-jump buttons.
- **Zoom (P1):** `CoreWebView2Settings.IsZoomControlEnabled = false` on `BlazorWebViewInitialized` (Ctrl+wheel, Ctrl++, Ctrl+−, Ctrl+0). `ZoomFactor` reset to 1 at init.
- Setup/FirstRun share this WPF window. They stay on global `--bg`. Flush edges and zoom lock apply to them too.

---

## Scrutiny (plan decisions)

Implementing agents follow these unless the operator overrides in chat.

**Visual world.** Not a new palette, typeface, or card language. Keep twilight granite + cobalt. Setup/FirstRun stay on global `--bg`. Do not pixel-match the Photoshop file.

**Window edge (P1) — investigate, then pick.** Operator wants the 6px frame to stop reading as a separate gutter. Preferred outcomes, in order:

1. **Invisible / transparent** outer strip (or no visible strip) so the window looks flush like other desktop apps, **and**
2. **Easier resize grab** — other apps let the cursor start the resize when it is still ~10px off the edge; today the hit target is the painted 6px strip only.

If true transparency is not viable (WebView2 is typically opaque; `WindowChrome` / DWM glass often fails next to BlazorWebView), fall back to **per-edge color matching** so each strip matches the panel it sits next to (left: caption / chrome `--bg` / nav `--surface-1` as needed; right + bottom: Manage content `--manage-content-bg`; top already caption). The 6px **thickness can stay**; the problem is that it is visually distinct.

**Shared window constraint.** The strips are WPF, not CSS. Setup and FirstRun use the same window. A Manage-only paint (right/bottom = `#3c434c`) will look wrong on the wizard. The investigation must pick a strategy that is acceptable on **both** Manage and Setup (dynamic recolor by current page, true transparency, or extending WebView to the outer edge with a different resize hit-test). Document the chosen approach in the P1 changelog.

Do **not** drop `ResizeMode=CanResize`. Do not change 1280 / 920 unless `FitWidthToWebView` must follow a thickness change.

**Ctrl+scroll (P1).** Disable WebView zoom from Ctrl+wheel. Also lock Ctrl++ / Ctrl+− / Ctrl+0 if the host exposes them. Do not disable other Ctrl shortcuts. Prefer the WebView2 host setting if it exists; JS `preventDefault` alone is a fallback, not the first try.

**Sidebar | content gutter (P2).** Remove the large gap between the left panel and the main content box. The leftover strip is the content-pane fill / `.mcm-tab-body` left padding, not a second sidebar. Flush the work surface to the sidebar. A 1px `--border` is OK as a hard edge; a 14–16px gutter is not. Do not reintroduce the old 16px left window gutter.

**Chrome padding (P2).** `.mcm-sidebar-chrome` is 10px today. Reduce to **6px** (all sides unless a 6/6/8 split looks better). Status / Play IP / Players should use the extra horizontal room. Do **not** change `--manage-sidebar-width` (244px) in this plan unless 6px padding still leaves the status card looking padded-empty — then stop and say so, do not silently widen.

**Power buttons (P2).** Start/Stop and Restart **same width** (equal flex shares of the row). Keep combined Start/Stop rules and labels.

**Pins (P3).** Redesign the four cards so labels and values are **fully visible** at 244px minus chrome padding. Ellipsis-on-everything is the bug, not the goal. Allowed: shorter labels, wrapping, dropping or collapsing mini-bars, stacking value vs hint, tighter type. Keep the four metrics and help buttons. Cells stay equal 2×2. Do not change which stats are pinned.

**Overview (P4).** Whitelist must show **name and IP** per friend (not name-only). Then fill the spare home: read the UI skills and look at similar server-manager / game-server dashboards (Pterodactyl-style ops home, Discord bot dashboards, etc. — topology and density only). Stay granite + cobalt. Read-only snapshot + existing tab jumps. Use data already on the ViewModels (status, play IP, players, whitelist, usage pins, server identity). No Players tab, no new OCI calls, no public Minecraft.

**Not this plan.** Pin set, Start/Stop rules, caption buttons, Setup wizard steps, window default/min size (unless P1’s non-client math requires it).

---

## Drift vs PRODUCT-IDEAS (follow this plan)

| Topic | PRODUCT-IDEAS | This plan |
|-------|---------------|-----------|
| Manage chrome | Horizontal top tabs (sketch) | **Keep** 8.12 vertical sidebar |
| Pin row | Six top-chrome cards | **Four** compact cards (8.12 set) |

Do **not** rewrite PRODUCT-IDEAS to match.

---

## Parked (not this plan)

| Item | Where |
|------|--------|
| New visual world (type, Setup restyle, caption redesign) | Operator asked for edge + density + Overview only |
| Change which four stats are pinned | 8.12 lock |
| Window default / min size | 8.12 lock (1280 / 920) unless P1 non-client math |
| Pass 3, 8.6.1, 9.1 | Existing V1 parking |
| Players tab, donation URL, light pack swap | After-v1 / parked |

---

## After this plan

When P1–P4 are **DONE**: [`NEXT.md`](NEXT.md) → Step **8.5.2** Pass 3 (**blocked** until the operator says so). Do **not** start Pass 3, **8.6.1**, or **9.1** from this plan.

---

## P1 — Window edge chrome + disable Ctrl+scroll zoom

**Status:** DONE  
**Parallel:** PARALLEL-OK with P2 — WPF host vs Manage CSS  
**Cursor mode:** plan-first  
**UI skill:** no (WPF / WebView2 host)

**Read first**

- This section + Scrutiny (Window edge, Ctrl+scroll, Shared window constraint)
- `src/McManager.Hybrid/MainWindow.xaml`
- `src/McManager.Hybrid/MainWindow.xaml.cs`
- `src/McManager.Hybrid/Ui/Wpf/WpfWindowChromeService.cs`
- WebView2 / WPF `WindowChrome` docs as needed (transparency, `ResizeBorderThickness`, zoom lock)

**Do**

1. **Plan-first (chrome):** Investigate whether the 6px outer strip can be invisible/transparent and whether the resize hit-test can start ~10px off the painted edge. Post a short design in chat (what works, what does not, Setup/FirstRun impact). If this chat is in Agent mode, **ask the operator to switch to Plan mode** or wait for approval. Then implement the approved approach. Color-match per edge only if transparency / flush-WebView is not viable. Keep 6px thickness unless a slightly thicker invisible hit-test is the approved pattern.
2. **Zoom:** Disable Ctrl+wheel zoom (and Ctrl++ / Ctrl+− / Ctrl+0 if the host exposes them). Prefer the WebView2 setting over JS-only. Same session as the chrome change after the chrome choice is locked.
3. Keep `ResizeMode=CanResize`. Do not restyle Setup/FirstRun fills. Do not change 1280 / 920 unless `FitWidthToWebView` must follow a real thickness change.

**Test**

- `dotnet run` Hybrid (`mcmgr-blank-test`): Manage — the 6px frame is no longer a distinct wrong-color gutter (or is gone). Resize still works from the edges; if the design promised an earlier grab, verify it. Setup/FirstRun still look acceptable at the window edge. Ctrl+wheel (and Ctrl++ if previously zooming) does **not** scale the UI. Min/max/close and caption drag still work.

**Done when**

- Window edge is flush or color-matched per the approved design on Manage and Setup; Ctrl+scroll no longer zooms.

**Changelog:** 2026-08-25 — **DONE.** Transparency / DWM glass next to WebView2 is not viable (opaque HWND). Color-match per edge would still leave a 6px dead strip and needs page-aware paints for Setup. **Chosen:** flush WebView + `ResizeBorderThickness=10` + DPI-scaled inner `WM_NCHITTEST` (~10 DIP; skip maximized). Setup/FirstRun inherit flush edges (they already paint `--bg`). Zoom lock via `IsZoomControlEnabled=false`. 1280 / 920 unchanged.

---

## P2 — Sidebar density: gutter, padding, equal power

**Status:** DONE  
**Parallel:** PARALLEL-OK with P1 — different files  
**Cursor mode:** agent  
**UI skill:** yes

**Read first**

- This section + Scrutiny (Sidebar \| content gutter, Chrome padding, Power buttons)
- `src/McManager.Hybrid/Components/Layout/MainLayout.razor` (sidebar chrome + power row)
- `src/McManager.Hybrid/wwwroot/css/app.css` (`.mcm-app-manage`, `.mcm-tab-body`, `.mcm-sidebar-chrome`, `.mcm-power-*`)

**Do**

1. Remove the large gap between the sidebar and the main content box (flush work surface; 1px border OK).
2. Reduce `.mcm-sidebar-chrome` padding from 10px to **6px**. Let status / Play IP fill the extra width.
3. Make Start/Stop and Restart **equal width**.
4. Do **not** redesign pin internals (that is P3). Do not change 244px, tab type size, or power behavior.

**Test**

- Hybrid: no awkward gutter between sidebar and content; chrome band is less horizontally padded; the two power buttons match in width at default 1280 and min 920. Setup/FirstRun still fill the window.

**Done when**

- Gutter gone; chrome padding ~6px; power buttons equal width.

**Changelog:** 2026-08-25 — **DONE.** Tab-body left padding 0 (1px sidebar border is the edge). Chrome padding 6px. Start/Stop and Restart equal `flex: 1 1 0` shares. Sidebar width 244px unchanged.

---

## P3 — Compact pin redesign

**Status:** DONE  
**Parallel:** SEQUENTIAL — after P2 (uses the new chrome width)  
**Cursor mode:** agent  
**UI skill:** yes

**Read first**

- This section + Scrutiny (Pins)
- `src/McManager.Hybrid/Components/Layout/MainLayout.razor` (four `.mcm-statcard`)
- `src/McManager.Hybrid/wwwroot/css/app.css` (`.mcm-pins*`, `.mcm-statcard*`, `.mcm-stat-label*`, `.mcm-mini-bar`)

**Do**

1. Redesign the four pin cards so **Today's uptime** (and the other three) show their full labels and values — no `Today's u...` / `0... / 11.3h all...`. Compact the internal layout; do not make the sidebar wider.
2. Keep equal 2×2 cells, the four metrics, and help buttons. Mini-bars may shrink or drop if they force cutoff.

**Test**

- Hybrid: all four pins fully readable at 1280 and at min 920, Stopped and with live usage strings (including today's `0h / 11.3h allowed`-style hint). Cards still equal height/width.

**Done when**

- Pin text is fully visible; cards stay equal and inside the 244px rail.

**Changelog:** 2026-08-25 — **DONE.** Dropped pin ellipsis. Labels wrap; value stacked above hint; 10px label/hint; 16px pin help. Mini-bars kept. Sidebar 244px unchanged.

---

## P4 — Overview enrich + Guide

**Status:** DONE  
**Parallel:** SEQUENTIAL after P2; PARALLEL-OK with P1  
**Cursor mode:** plan-first  
**UI skill:** yes (also search similar app dashboards)

**Read first**

- This section + Scrutiny (Overview)
- `src/McManager.Hybrid/Components/Tabs/Overview/OverviewTab.razor`
- `src/McManager.Hybrid/wwwroot/css/app.css` (`.mcm-overview*` only)
- [`docs/Guide.md`](Guide.md) (Manage Overview sentence only)
- UI skills listed above; optional web examples of server-manager home dashboards (density / information architecture, not a skin copy)

**Do**

1. **Plan-first:** Post a short Overview layout (what cards, what each shows, how whitelist lists name+IP). If this chat is in Agent mode, **ask the operator to switch to Plan mode** or wait for approval. Must include whitelist **name and IP**. Fill the spare page with already-available snapshot data; keep tab-jump actions.
2. Implement the approved layout. Do not add a Players tab or new backend calls.
3. Short Guide note covering this pass (window edge, denser sidebar, readable pins, Overview). Do not rewrite the Guide.

**Test**

- Hybrid: Overview whitelist rows show name and IP; the page no longer looks empty at 1280. Tab jumps still work. Whitelist / Usage / Server tabs unchanged in behavior.

**Done when**

- Overview is a useful home snapshot with name+IP whitelist; Guide has one short paragraph.

**Changelog:** 2026-08-25 — **DONE.** Status strip (status / play IP / players). Server card: icon + name + pack line + list MOTD preview. Usage: today, month, remaining, rollover, idle. Whitelist table: name + IP + Admin. Guide covers flush window, denser sidebar, readable pins, Overview. No Players tab; no new OCI.
