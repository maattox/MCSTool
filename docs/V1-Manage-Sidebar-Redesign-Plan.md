# V1 Manage sidebar redesign (living)

**Status:** NEXT = P2. Created 2026-08-25 (docs only). **Live NEXT:** [`NEXT.md`](NEXT.md).  
**Parent:** [`V1-Implementation-Plan.md`](V1-Implementation-Plan.md) Step **8.12**.  
**Branch:** `UI-redesign` — keep this work here until the operator likes it. Do not merge to `main` from an agent chat.  
**Why now:** operator 2026-08-25 — rearrange Manage chrome into a left sidebar (status, power, pins, vertical tabs) and a large content pane. Topology from [`assets/UI-design-mockup.png`](../assets/UI-design-mockup.png). **Not** a new color/type world. Vague spacing: agents **decide inside each section’s bounds** (and [Scrutiny](#scrutiny-plan-decisions)). Stop and ask for spend, `tofu destroy`, `DEFAULT`, a donation URL, or parked after-v1 items.

**Cost:** $0. TESTING profile only. Never `DEFAULT` / live Forge lab. Never Minecraft `0.0.0.0/0`.  
**Functions / tofu:** do **not** `tofu apply` / `destroy`. Do not SoftStop the door.  
**Hybrid config dir:** `MCMANAGER_CONFIG_DIR` = `mcmgr-blank-test` — **not** repo `data/config.local.json`.  
**SSH / VM1:** not required.  
**Hosts / OCIDs:** do **not** paste live OCIDs, IPs, Auth Tokens, or key material into tracked docs.

Applies to **Manage** (`MainLayout`). Setup and FirstRun keep their wizard layout; they share the same WPF window so they inherit the new default size and must still fill width when resized.

---

## How agents must use this file

1. Read **this protocol**, the [Progress dashboard](#progress-dashboard), [Scrutiny](#scrutiny-plan-decisions), and **only the NEXT section**.
2. Implement only that section. Do not start neighbors “while you are here.”
3. After finishing: mark **DONE**, set the next incomplete section to **NEXT**, changelog line, update V1 plan Step **8.12** + dashboard, update [`NEXT.md`](NEXT.md), **stop**.
4. Mirror TESTING / guest fixes into local SoT. File [`Issues.md`](Issues.md) for on-box/Setup/door bugs.
5. Git: commits allowed per `git-policy`; never push/PR unless the operator asks.
6. Do **not** start Step **8.5.2** (Pass 3), **8.6.1**, or **9.1**.
7. If this plan disagrees with [`PRODUCT-IDEAS.md`](PRODUCT-IDEAS.md), **follow this plan** and note drift. Do not rewrite PRODUCT-IDEAS.
8. **UI-heavy sections** must read the named UI skills **before** changing CSS/Razor. Reuse existing granite + cobalt tokens. **NuGet** on `McManager.Hybrid` only. No Avalonia.
9. User-visible Setup/manage changes: add a **short** paragraph to [`Guide.md`](Guide.md) in the same step (P5 may fold Guide if an earlier step only shipped a placeholder).

Vague notes: **decide** (sidebar width, pin density, icon picks) inside the section **using Scrutiny**. **Stop and ask** for legal/ToS, spend, or scope listed in [Parked](#parked-not-this-plan).

### Context budget

This header + **one** P-section + the files listed there. Blueprint: **named §§ only**. Do not load Pass 3, the full V1 plan, or PRODUCT-IDEAS unless a heading is named.

### UI skills (required where the section says **UI skill**)

Read **before** CSS/Razor:

- `C:\Users\matto\.agents\skills\impeccable\SKILL.md`
- `C:\Users\matto\.agents\skills\web-design-guidelines\SKILL.md`

Optional visual pass: `C:\Users\matto\.agents\skills\frontend-design\SKILL.md`. Treat the mockup as **topology**, not pixels. Keep `--bg` / `--caption-bg` / `--fill-accent` / existing card language.

### Operator entry

New Agent chat → `/next-step`. Fresh chat per P-section. **P4** is **plan-first** (switch to Plan mode, or post a short Overview card list and wait).

### PARALLEL-OK

None. Every section edits `MainLayout.razor` and/or `app.css` manage chrome.

---

## Progress dashboard

| ID | Title | Status | Parallel | Cursor |
|----|-------|--------|----------|--------|
| P1 | Two-column Manage shell | **DONE** | SEQUENTIAL — shell grid | agent |
| P2 | Combined Start/Stop + condensed pins | **NEXT** | SEQUENTIAL — same sidebar chrome | agent |
| P3 | About tab; remove caption overflow | TODO | SEQUENTIAL — nav + caption | agent |
| P4 | Overview tab | TODO | SEQUENTIAL — after shell exists | plan-first |
| P5 | Resize polish + Guide | TODO | SEQUENTIAL — after P4 landing default | agent |

---

## What already exists (do not rediscover)

- Manage (P1): full-bleed caption, then `.mcm-sidebar` (status, three power buttons, six pins in 2×3, vertical `.mcm-tabs`) | `.mcm-tab-body`. CSS `.mcm-app-manage` is pad | `--manage-sidebar-width` (296px) | pad | fluid content | pad. Content pane grows on resize; sidebar scrolls if the window is short.
- Window: default **1280** CSS px (`MainWindow.AppShellWidthDip`), min **920** (`AppShellMinWidthDip`), Height 720, MinHeight 560, `ResizeMode=CanResize`. `FitWidthToWebView` adds non-client thickness to both. Setup wizard fills the padded width; FirstRun stays a centered 560px column.
- Tabs: Overview, Whitelist, Server, Console, Usage, Advanced, Troubleshooting, About. Default `_tab = "whitelist"`. Overview/About are one-line placeholders until P4/P3. Server is keep-alive (`_serverTabCreated`). Inner `.mcm-subtabs` stay horizontal at the top of the content pane.
- Caption: bell, gear, **overflow ☰** (About modal + GitHub). `ChromeViewModel.GitHubUrl` / `OpenGitHub`. About copy lives in the modal in `MainLayout.razor`.
- Power: separate Start / Stop / Restart. Rules in `ManagePowerUx` (`CanStart` only when VM1 is fully STOPPED, etc.). Labels `Starting…` / `Stopping…` / `Restarting…`.
- Pins (8.11 P2): six cards from `PinnedUsageSnapshot` — Today's uptime, This month, Daily average, Rollover bank, Hours left this month, Idle timeout. Usage tab still has the full figures.
- Icons: Tabler (`.ti`) already in Hybrid. Copy play IP is a labeled button today (`Vm.CopyPlayIpLabel`).
- Toasts bottom-left; Change-pack `ProgressDock`; action banner. Setup/FirstRun use the same window, not this sidebar.

---

## Scrutiny (plan decisions)

Implementing agents follow these unless the operator overrides in chat.

**Visual world.** Rearrange only. Keep twilight granite + cobalt. Do not restyle Setup/FirstRun beyond filling the wider/resized window. Do not pixel-match the Photoshop file.

**Shell (P1).** Below the existing full-bleed caption, Manage is two columns: **sidebar left**, **tab content right**. Maximize the content pane without a cramped sidebar. Sidebar holds (top → bottom): status card, power row, pins, then the vertical tab list. Content pane is the current tab pages (Whitelist, Server, …) plus Overview/About placeholders until P3/P4. Caption stays full-width; sidebar starts **under** it. Bell + gear + min/max/close stay; P1 may leave the ☰ overflow until P3.

**Window (P1).** Default width **noticeably wider than 1040** (~1280 CSS px is the intended ballpark; UI skill picks the exact value). Default height may stay ~720. **Min-width** must be **lower than the default** so resize is real — pick the smallest width that still fits the sidebar plus a usable content pane (not below ~900). Height min can stay 560. CSS must **fill** extra width/height (no centered 1008px chrome column). Setup/FirstRun inherit size and should use the extra width instead of a skinny centered column.

**Nav (P1).** Vertical list, icon + label, Tabler icons (agent picks; mockup is a hint). Order: Overview, Whitelist, Server, Console, Usage, Advanced, Troubleshooting, About. Active state is a full-width highlight in the sidebar, not the old segmented control. Keep existing Server keep-alive and per-tab scroll restore. Inner Server/Advanced subtabs stay **horizontal at the top of the content pane**. Do not add new inner subtabs in P1. Default tab until P4: **Whitelist** (matches the mockup’s example pane). Overview and About in P1 are placeholders with one line of copy, not fake editors.

**Copy IP (P1).** Icon only (existing copy glyph). `aria-label` / `title` still explain Copy play IP. Status card width follows the sidebar.

**Power (P2).** One primary button + Restart beside it. The primary control **is Start when `CanStart`**, **Stop when `CanStop`**, and **disabled** when neither (keep current tooltips / in-flight labels: `Starting…` / `Stopping…`). Do not change `ManagePowerUx` allow/deny rules — only the chrome. Start keeps the green start treatment; Stop uses the existing stop/danger treatment. Restart unchanged.

**Pins (P2).** Four sidebar cards: **Today's uptime**, **Rollover bank**, **This month** (% of monthly cap), **Idle timeout**. Drop Daily average and Hours left from the **chrome** (they remain on Usage). If four cards still crowd the sidebar, collapse to **three single-line** stats: Today's uptime, Rollover bank, This month — still no editors. Prefer four if they fit.

**About (P3).** Replace the About **modal** and caption ☰ with an **About tab**. Reuse existing app name, version, short product sentence, and GitHub button (`ChromeViewModel.OpenGitHub`). No donation link unless the operator supplies a URL. Remove overflow menu markup and unused overflow state from the caption.

**Overview (P4).** Read-only snapshot + buttons that **switch tabs** (no inline editors). After P4, default landing tab is **Overview**. Suggested cards (P4 may tighten after the plan-first post): live status / play IP / players (no Start here — sidebar has it); whitelist names + count + **Manage IPs** → Whitelist; usage snapshot + **Open Usage**; server identity name + **Open Server**. Do not add Players-tab features.

**P5.** Verify Console, Change-pack dock, toasts, and banners in the new pane. Add extra inner subtabs **only** if a page actually overflows after the shell. Guide: sidebar, combined Start/Stop, pin set, Overview/About, caption without ☰.

---

## Drift vs PRODUCT-IDEAS (follow this plan)

| Topic | PRODUCT-IDEAS | This plan |
|-------|---------------|-----------|
| Manage tabs | Horizontal top segmented tabs (sketch) | **Vertical sidebar** |
| About | Overflow / chrome sketch | **About tab**; overflow gone |
| Overview | Not a v1 tab | **Read-only Overview** (operator 2026-08-25) |
| Pin row | Six cards in the top chrome (8.11) | **Four** (or three compact) in the sidebar |

Do **not** rewrite PRODUCT-IDEAS to match.

---

## Parked (not this plan)

| Item | Where |
|------|--------|
| Donation / sponsor link | No URL from the operator |
| Players tab, Kick·Op·Ban | After v1 |
| New visual world (palette, type, card language) | Operator: rearrange only |
| Setup wizard step rewrite | Inherit window size only |
| Pass 3, 8.6.1, 9.1 | Existing V1 parking |
| Light pack swap, in-app catalog | Rejected / after-v1 |

---

## After this plan

When P1–P5 are **DONE**: [`NEXT.md`](NEXT.md) → Step **8.5.2** Pass 3 (**blocked** until the operator says so). Do **not** start Pass 3, **8.6.1**, or **9.1** from this plan.

---

## P1 — Two-column Manage shell

**Status:** DONE  
**Parallel:** SEQUENTIAL — manage grid + `MainWindow` size  
**Cursor mode:** agent  
**UI skill:** yes

**Read first**

- This section + Scrutiny (Visual world, Shell, Window, Nav, Copy IP)
- [`assets/UI-design-mockup.png`](../assets/UI-design-mockup.png)
- `src/McManager.Hybrid/Components/Layout/MainLayout.razor`
- `src/McManager.Hybrid/wwwroot/css/app.css` (`.mcm-app-manage`, `.mcm-chrome`, `.mcm-tabs`, `.mcm-tab-body`, caption)
- `src/McManager.Hybrid/MainWindow.xaml` + `MainWindow.xaml.cs`
- `src/McManager.Hybrid/Components/Setup/SetupWizard.razor` (layout class only — must still fill width)
- `src/McManager.Hybrid/Components/FirstRun/FirstRun.razor` (same)

**Do**

1. Rewrite Manage layout to sidebar | content under the existing caption. Status, current three power buttons, current six pins, then vertical tabs with Tabler icons in the order in Scrutiny.
2. Widen default window; lower min-width so the operator can resize; make the shell **fluid** (content pane grows). Setup/FirstRun must not clip or stay a 1040-centered strip.
3. Copy play IP = icon only. Keep Server keep-alive, tab scroll restore, dock, toasts, banners.
4. Overview and About nav targets: one-line placeholders. Default tab **Whitelist**. Leave ☰ overflow until P3.

**Test**

- `dotnet run` Hybrid (`mcmgr-blank-test`): sidebar + content; all six existing tabs still work; resize wider/taller fills; Setup/FirstRun still usable at the new default size.
- `dotnet test` Core tests still pass (no behavior change expected).

**Done when**

- Manage is two columns; window default is wider; resize fills; existing tabs unchanged in function.

**Changelog:** 2026-08-25 — two-column Manage shell (sidebar | content), default window 1280 / min 920, icon-only Copy play IP, Overview/About placeholders. Default tab still Whitelist.

---

## P2 — Combined Start/Stop + condensed pins

**Status:** NEXT  
**Parallel:** SEQUENTIAL — same sidebar as P1  
**Cursor mode:** agent  
**UI skill:** yes

**Read first**

- This section + Scrutiny (Power, Pins)
- `src/McManager.Hybrid/Components/Layout/MainLayout.razor` (power + pins markup)
- `src/McManager.Hybrid/ViewModels/MainViewModel.cs` (Start/Stop/Restart labels and commands)
- `src/McManager.Core/Services/ManagePowerUx.cs` + `src/McManager.Core.Tests/ManagePowerUxTests.cs`
- `src/McManager.Hybrid/ViewModels/PinnedUsageSnapshot.cs`
- `src/McManager.Hybrid/wwwroot/css/app.css` (`.mcm-action-btn*`, `.mcm-statcard`, `.mcm-pins*`)

**Do**

1. Replace separate Start and Stop with one primary control per Scrutiny. Restart stays beside it. Do not loosen or tighten `CanStart` / `CanStop`.
2. Sidebar pins: four cards (or three compact lines if four will not fit). Drop Daily average and Hours left from chrome only.
3. Add or adjust unit tests if a small presentation helper is extracted; keep existing `ManagePowerUx` cases.

**Test**

- Hybrid: Stopped → primary is Start (green); Running → Stop; in-flight labels; Restart still separate. Pins match Scrutiny. Usage tab still shows dropped figures.
- `dotnet test` `ManagePowerUxTests`.

**Done when**

- One Start/Stop control + Restart; chrome pins are the reduced set.

**Changelog:** *(date when finished)*

---

## P3 — About tab; remove caption overflow

**Status:** TODO  
**Parallel:** SEQUENTIAL — caption + About placeholder from P1  
**Cursor mode:** agent  
**UI skill:** yes

**Read first**

- This section + Scrutiny (About)
- `src/McManager.Hybrid/Components/Layout/MainLayout.razor` (overflow, About modal, About tab placeholder)
- `src/McManager.Hybrid/Components/Layout/CaptionBar.razor`
- `src/McManager.Hybrid/ViewModels/ChromeViewModel.cs`

**Do**

1. Fill the About tab with the current About modal content (name, version, private-server sentence, GitHub). No donation link.
2. Remove the caption ☰ overflow (About + Source on GitHub). Bell, gear, and window buttons stay. Delete the About **modal** once the tab is the only About surface.
3. Clean unused overflow/`AboutOpen` chrome if nothing else uses it.

**Test**

- Hybrid: ☰ gone; About tab shows version + GitHub; gear and bell still work.

**Done when**

- About is a tab; overflow menu is gone.

**Changelog:** *(date when finished)*

---

## P4 — Overview tab

**Status:** TODO  
**Parallel:** SEQUENTIAL — after P1 placeholders  
**Cursor mode:** plan-first  
**UI skill:** yes

**Read first**

- This section + Scrutiny (Overview)
- `src/McManager.Hybrid/Components/Layout/MainLayout.razor` (tab switch + Overview placeholder)
- Existing tab VMs only as needed for **read-only** fields (`MainViewModel`, whitelist list, usage pins, server identity name)
- Do **not** load the full V1 plan or Pass 3

**Do**

1. **Plan-first:** post a short card list (what is shown, which buttons jump where) and wait for operator OK, **or** ask them to switch to Plan mode. Then implement.
2. Read-only Overview per approved list. Buttons only change `_tab` (e.g. Manage IPs → Whitelist). No editors, no power controls on this page.
3. Default landing tab = **Overview**.

**Test**

- Hybrid: launch lands on Overview; each jump button opens the right tab; no Overview field commits config.

**Done when**

- Overview is the home tab and is read-only with tab jumps.

**Changelog:** *(date when finished)*

---

## P5 — Resize polish + Guide

**Status:** TODO  
**Parallel:** SEQUENTIAL — after Overview is home  
**Cursor mode:** agent  
**UI skill:** yes

**Read first**

- This section + Scrutiny (P5)
- `src/McManager.Hybrid/Components/Layout/MainLayout.razor`
- `src/McManager.Hybrid/wwwroot/css/app.css` (manage shell, console, dock, toasts)
- `src/McManager.Hybrid/Components/Tabs/Console/ConsoleTab.razor` (full-height in the new pane)
- [`docs/Guide.md`](Guide.md) (Day-to-day in Manager + caption / pin / tab copy)

**Do**

1. Fix Console height, Change-pack dock, toasts, and banners in the two-column shell. Walk Whitelist, Server (inner tabs), Usage, Advanced (inner tabs), Troubleshooting at min and a large size.
2. Add inner subtabs **only** if a page overflows; otherwise leave Server/Advanced as they are.
3. Update Guide: sidebar, combined Start/Stop, pin set, Overview/About, no ☰, resize.

**Test**

- Hybrid at min-width and a wide/tall window: Console usable, dock reachable, no clipped caption. Guide matches the shipped chrome.

**Done when**

- Resize holds up; Guide is current. Then this plan is **COMPLETE** and NEXT returns to Pass 3 **blocked**.

**Changelog:** *(date when finished)*
