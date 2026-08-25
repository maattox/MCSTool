# V1 operator-notes follow-on 3 (living)

**Status:** Living. Created 2026-08-24 (docs only). **Live NEXT:** [`NEXT.md`](NEXT.md).  
**Parent:** [`V1-Implementation-Plan.md`](V1-Implementation-Plan.md) Step **8.11**.  
**Why now:** operator 2026-08-24 — caption contrast, pin-row empty space after the wider window, and MOTD editor (selection wrap, WYSIWYG, Minecraft-font preview, per-line counters, format **name and description**) **before** QA Pass 3. Vague layout notes: agents **decide inside each section’s bounds** (and [Scrutiny](#scrutiny-plan-decisions)). Stop and ask for spend, `tofu destroy`, `DEFAULT`, or pulling other parked after-v1 items.

**Cost:** $0. TESTING profile only. Never `DEFAULT` / live Forge lab. Never Minecraft `0.0.0.0/0`.  
**Functions / tofu:** do **not** `tofu apply` / `destroy`. Do not SoftStop the door.  
**Hybrid config dir:** `MCMANAGER_CONFIG_DIR` = `mcmgr-blank-test` — **not** repo `data/config.local.json`.  
**SSH / VM1:** not required. Stored MOTD stays `§` codes; VM1 apply already preserves them (8.10 P9).  
**Hosts / OCIDs:** do **not** paste live OCIDs, IPs, Auth Tokens, or key material into tracked docs.

Applies to **both** Setup identity and Manager Server → Identity unless a section says otherwise.

---

## How agents must use this file

1. Read **this protocol**, the [Progress dashboard](#progress-dashboard), [Scrutiny](#scrutiny-plan-decisions), and **only the NEXT section**.
2. Implement only that section. Do not start neighbors “while you are here.”
3. After finishing: mark **DONE**, set the next incomplete section to **NEXT**, changelog line, update V1 plan Step **8.11** + dashboard, update [`NEXT.md`](NEXT.md), **stop**.
4. Mirror TESTING / guest fixes into local SoT. File [`Issues.md`](Issues.md) for on-box/Setup/door bugs.
5. Git: commits allowed per `git-policy`; never push/PR unless the operator asks.
6. Do **not** start Step **8.5.2** (Pass 3), **8.6.1**, or **9.1**.
7. If this plan disagrees with [`PRODUCT-IDEAS.md`](PRODUCT-IDEAS.md), **follow this plan** and note drift. Do not rewrite PRODUCT-IDEAS.
8. **UI-heavy sections (P1, P2, P4)** must read the named UI skills **before** changing CSS/Razor. Reuse existing tokens. **NuGet** on `McManager.Hybrid` only. No Avalonia.
9. User-visible Setup/manage changes: add a **short** paragraph to [`Guide.md`](Guide.md) in the same step.

Vague notes: **decide** (exact caption token, pin widths) inside the section **using Scrutiny**. **Stop and ask** for legal/ToS, spend, or scope listed in [Parked](#parked-not-this-plan).

### Context budget

This header + **one** P-section + the files listed there. Blueprint: **named §§ only**. Contracts: named headings only. Do not load Pass 3, the full V1 plan, or PRODUCT-IDEAS unless a heading is named.

### UI skills (required where the section says **UI skill**)

Read **before** CSS/Razor:

- `C:\Users\matto\.agents\skills\impeccable\SKILL.md`
- `C:\Users\matto\.agents\skills\web-design-guidelines\SKILL.md`

Optional visual pass: `C:\Users\matto\.agents\skills\frontend-design\SKILL.md`.

### Operator entry

New Agent chat → `/next-step`. Fresh chat per P-section. **P4** is **plan-first** (switch to Plan mode, or post a short design and wait).

### PARALLEL-OK

| Group | Sections | Why |
|-------|----------|-----|
| A | P1 → P2 | Shared `app.css` manage chrome (caption then pins) |
| B | P3 | Core MOTD wrap + visible-length only; no Hybrid CSS |
| Then | P4 | After P3 (wrap API) and after P1–P2 (`app.css` MOTD rules) |

P3 may run in a **separate operator chat** while Group A is on P1–P2. Default in one chat: sequential P1…P4.

---

## Progress dashboard

| ID | Title | Status | Parallel | Cursor |
|----|-------|--------|----------|--------|
| P1 | Caption bar contrast | **DONE** | SEQUENTIAL — same `app.css` as P2 | agent |
| P2 | Fill pin-row empty space | **DONE** | SEQUENTIAL — manage chrome after P1 | either |
| P3 | MOTD wrap-with-reset + line metrics | **NEXT** | PARALLEL-OK with P1–P2 | agent |
| P4 | MOTD WYSIWYG editor (name + description) | TODO | SEQUENTIAL after P3 and P1–P2 | plan-first |

---

## What already exists (do not rediscover)

- Custom caption (8.10 P2 + 8.11 P1): `CaptionBar.razor` + WPF `WindowChrome`. `.mcm-caption` uses `--caption-bg` (`#10141a`, darker than `--bg`) and a 1px `--border` bottom edge. Shared by Manage, Setup, and FirstRun. Min/max hover `--surface-2`; close hover danger. `.mcm-app` is full window width; Manage uses outer `1fr` grid tracks so the caption full-bleeds when the window is wider than 1040 CSS px. Chrome / tabs stay `--app-chrome-width`. Left/right 6px WPF resize strips keep a caption-colored top (`MainWindow.xaml`) so the title bar is flush with the window edge.
- Default window ~1040 CSS px / `--app-chrome-width: 1008px`. Status column is **330px**. Pins are a **3×2** that **fills remaining chrome** after the status column (`MainLayout` + `PinnedUsageSnapshot`): Today's uptime, This month, Daily average, Rollover bank, Hours left this month, Idle timeout. Remaining hours and idle minutes come from the same usage/budget refresh as the Usage tab (no extra Object Storage list / SSH).
- MOTD editor (8.10 P9): `MotdEditor.razor` + `window.mcmMotd.wrap` in `index.html`. Wrap inserts a **prefix only** (empty suffix) — that is why `test MOTD message` + bold on `MOTD` becomes `test §lMOTD message` and “message” stays bold. Typing surface is a **textarea that shows `§` codes**. Preview is HTML from `MotdFormatting.ToPreviewHtml` (not Minecraft font). Name is a **plain** `<input maxlength="40">`; only description uses `MotdEditor`. Omit-name checkbox + collapsed raw `motd=` + paste normalize already exist. `MaxNameLength = 40`, `MaxDescriptionLength = 512`. VM1 `_build_motd` already keeps `§`.
- Operator reference (gitignored `development/`): `development/motd-generator-files/MotdGeneratorTool.BCUg2Acy.js` and `Minecraft-Regular.otf`. Read for wrap/WYSIWYG behavior; **do not** copy Sunset/Ocean presets or a gradient designer.

---

## Scrutiny (plan decisions)

Implementing agents follow these unless the operator overrides in chat.

**Caption (P1).** Make the top bar read as window chrome, not as more `--bg`. Add `--caption-bg` a step **darker** than `--bg` (Discord-style strip) and a 1px bottom border `--border`. Full-bleed as today. Hover on min/max still uses `--surface-2`; close stays danger. Same bar on Setup and FirstRun.

**Pin row (P2).** **Six** cards in a **3×2** grid. Stretch the pin slot to **fill remaining chrome width** after the 330px status column (no empty gutter). Keep the four existing usage pins. Add two pins from data **already** on the usage/budget refresh — **no** extra Object Storage list and **no** SSH:

1. **Hours left this month** (`UsageViewModel` remaining-hours figure; lift onto `PinnedUsageSnapshot` / `MainViewModel`).
2. **Idle timeout** (configured minutes from budget; same refresh).

Do not pin world-backup GB or live world size (those need Server-tab fetches). Card chrome stays the existing `.mcm-statcard` language. UI skill picks exact column width so six cards fill the slot without looking sparse.

**MOTD wrap (P3).** Selection wrap must match the fadehost contract: highlight `MOTD` in `test MOTD message`, apply bold → `test §lMOTD§r message` (section char or `\u00A7` equivalent). Empty selection: insert `§code` + `§r` and leave the caret between them (do not format the rest of the line). If the selection sits inside a color/format run, restore that outer state after `§r` so the following text does not lose its color. Unit-test at least the operator’s bold example. Visible length **ignores** `§` / `§x` hex runs. Per-line limit **59** (Java server list). Counter copy: `line 1: 41/59` and `line 2: 60/59 — too long` when over.

**MOTD editor (P4).** Layout, top → bottom:

1. Formatting toolbar (vanilla colors + formats already in `MotdFormatting`; **one** toolbar, applies to the focused field).
2. WYSIWYG **server name** (effects visible; **no raw codes** in the typing surface).
3. WYSIWYG **description** (same).
4. Combined **Minecraft-font** live preview (the in-game list look).
5. Per-line counters for the **combined** list MOTD.
6. Omit-name checkbox (existing).
7. Expandable raw `motd=` + copy (existing).

Prefer contenteditable (or an equivalent run editor) that **stores** the `§` string. Do not ship the current “codes in the textarea” typing UX. Paste of `motd=` / `\u00a7` / `&` still goes through `NormalizePaste`. Hex: keep the Paper/Spigot 1.16+ note; **no** gradient UI and **no** named presets (Sunset/Ocean). Name MOTD line may use the **59** visible-char list limit (drop the 40-char name cap if it fights the counter). Description storage stays 512. Font: copy `development/motd-generator-files/Minecraft-Regular.otf` into Hybrid `wwwroot` (that folder is gitignored — read it from disk). If the file is missing, stop and ask; do not download a font. Door MOTD: **do not change**.

---

## Drift vs PRODUCT-IDEAS (follow this plan)

| Topic | PRODUCT-IDEAS / 8.10 P9 | This plan |
|-------|-------------------------|-----------|
| MOTD typing surface | Textarea showing `§` codes | **WYSIWYG**; codes only in collapsed raw |
| MOTD name field | Plain name + formatted description | **Both** name and description use the editor |
| MOTD wrap | Prefix-only wrap | Selection + **`§r` close** (restore outer run) |

Do **not** rewrite PRODUCT-IDEAS to match.

---

## Parked (not this plan)

| Item | Where |
|------|--------|
| Fadehost Sunset/Ocean presets, gradient designer | Operator: do not copy |
| Door MOTD copy editor (idle / starting / exhausted) | Not requested |
| Players tab, paid/spend mode, Pass 3, 8.6.1, 9.1 | Existing V1 parking |
| Pinning world-backup GB / live world size | Needs extra fetches; not P2 |
| Hex/gradient MOTD as a guaranteed Vanilla/Forge/Fabric feature | Paper-only note stays |

---

## P1 — Caption bar contrast

**Status:** DONE  
**Parallel:** SEQUENTIAL — `.mcm-caption` lives in the same `app.css` P2 will restyle  
**Cursor mode:** agent  
**UI skill:** yes

**Read first**

- This section + Scrutiny (Caption)
- `src/McManager.Hybrid/Components/Layout/CaptionBar.razor`
- `src/McManager.Hybrid/wwwroot/css/app.css` (`.mcm-caption*` and `:root` tokens only)

**Do**

1. Add `--caption-bg` darker than `--bg` and paint `.mcm-caption` with it plus a bottom border so the strip is distinct on Manage, Setup, and FirstRun.
2. Keep drag / min / max / close hit targets. Win-button hover must still read on the new bar.

**Test**

- `dotnet run` Hybrid (`mcmgr-blank-test`): caption is a different color from the window body on Manage and Setup; drag and window buttons still work.

**Done when**

- The top bar no longer blends into `--bg`.

**Changelog:** 2026-08-24 — **DONE.** `--caption-bg: #10141a` (one step darker than `--bg`) on `.mcm-caption` plus 1px `--border` bottom. Same strip on Manage, Setup, and FirstRun. Win-button hover unchanged (`--surface-2` / close danger). Bell badge ring matches the caption. Caption full-bleeds on window resize (outer `1fr` tracks); chrome stays 1040. WPF 6px side strips paint `--caption-bg` for the caption height so the title bar is flush. Guide. **NEXT = P2.**

---

## P2 — Fill pin-row empty space

**Status:** DONE  
**Parallel:** SEQUENTIAL — pin CSS/layout in the same chrome as P1  
**Cursor mode:** either  
**UI skill:** yes

**Read first**

- This section + Scrutiny (Pin row)
- `src/McManager.Hybrid/Components/Layout/MainLayout.razor` (`.mcm-pins` block)
- `src/McManager.Hybrid/wwwroot/css/app.css` (`.mcm-chrome`, `.mcm-pins*`, `.mcm-statcard`)
- `src/McManager.Hybrid/ViewModels/PinnedUsageSnapshot.cs`
- `src/McManager.Hybrid/ViewModels/UsageViewModel.cs` (remaining hours + idle timeout — named members only)
- `src/McManager.Hybrid/ViewModels/MainViewModel.cs` (pin apply path)

**Do**

1. Expand the pin slot to fill chrome width after the status column. **3×2** of six `.mcm-statcard`s.
2. Keep the four existing pins. Add **Hours left this month** and **Idle timeout** from the existing usage/budget refresh (`PinnedUsageSnapshot`). Help `title`s in the same voice as the current pins.
3. Do not add OCI/SSH round-trips. Do not change window default size unless six cards cannot fit — prefer filling width.

**Test**

- Manage chrome: six pins flush to the right padding; no empty gutter. Values update when Usage budget refresh runs. Always-on copy still makes sense on the new pins.

**Done when**

- The pin row fills the chrome; two new pins show real budget facts without extra fetches.

**Changelog:** 2026-08-24 — **DONE.** Pin slot `flex: 1` fills chrome after the 330px status column; **3×2** `minmax(0, 1fr)` cards (no empty gutter, window size unchanged). Four existing pins kept. Added **Hours left this month** (`RemainingHoursLabel` / remaining-hours math) and **Idle timeout** (budget minutes) on `PinnedUsageSnapshot` from the same refresh. Always-on copy on the new pins. Guide. **NEXT = P3.**

---

## P3 — MOTD wrap-with-reset + line metrics

**Status:** NEXT  
**Parallel:** PARALLEL-OK with P1–P2 — Core + tests only  
**Cursor mode:** agent  
**UI skill:** no

**Read first**

- This section + Scrutiny (MOTD wrap)
- `src/McManager.Core/Services/MotdFormatting.cs`
- `src/McManager.Core.Tests/MotdFormattingTests.cs`
- Optional (behavior only, do not vendor): `development/motd-generator-files/MotdGeneratorTool.BCUg2Acy.js` if present on disk

**Do**

1. Add a Core helper to wrap a `[start, end)` span with a vanilla color/format code and **close with `§r`**, restoring outer color/format so following text is unchanged. Empty range: `§code` + `§r` with caret conceptually between them (return indices if useful for P4).
2. Add visible-length + per-line counter helpers (limit **59**, copy `line N: used/59` and `— too long` when over). Combined MOTD lines follow `ServerIdentityUx.BuildMotd` (name line + description line; omit-name uses description’s `\n` split).
3. Unit tests: operator bold example; empty selection; wrap inside an existing color; visible length ignores codes; over-limit suffix.

**Test**

- `dotnet test` on `McManager.Core.Tests` (MOTD facts). No Hybrid required.

**Done when**

- Core wrap/reset and counters are tested; P4 can call them without re-deriving MOTD rules.

**Changelog:** *(date when finished)*

---

## P4 — MOTD WYSIWYG editor (name + description)

**Status:** TODO  
**Parallel:** SEQUENTIAL after P3 (API) and P1–P2 (`app.css`)  
**Cursor mode:** plan-first  
**UI skill:** yes

**Read first**

- This section + Scrutiny (MOTD editor)
- `src/McManager.Hybrid/Components/Shared/MotdEditor.razor`
- `src/McManager.Hybrid/wwwroot/index.html` (`window.mcmMotd`)
- Manager Identity (`ServerManagementTab.razor` name + `MotdEditor`) and Setup Name-and-icon (`SetupWizard.razor`)
- `src/McManager.Core/Services/ServerIdentityUx.cs` (`MaxNameLength` / `BuildMotd`)
- `src/McManager.Core.Tests/MotdFormattingTests.cs` (P3 helpers)
- Optional: `development/motd-generator-files/` JS + OTF on disk
- Preview CSS `.mcm-motd-*`

**Do**

1. **Plan-first:** short design for the WYSIWYG (contenteditable vs run editor), how one toolbar targets the focused field, and how the stored `chat.json` name/description strings stay `§` text. Then implement.
2. Replace the codes-showing textarea. Name and description both get the editor. Layout: toolbar → name WYSIWYG → description WYSIWYG → Minecraft-font combined preview → counters → omit-name → raw details.
3. Toolbar click uses P3 wrap on the **current selection in the focused field**. Paste still `NormalizePaste`.
4. Embed the operator OTF for the **preview only** (typing surface may use a clean UI font that still shows bold/italic/color). If the OTF is missing from `development/motd-generator-files/`, stop and ask.
5. Guide: how to highlight text, apply an effect, and read the counters. Hex/Paper note stays.

**Test**

- Setup + Manager: highlight `MOTD` in `test MOTD message`, Bold → following word is **not** bold; typing box shows bold “MOTD” without `§l`; Minecraft-font preview matches; raw details show `test §lMOTD§r message` (or `\u00A7` equivalent). Counters match fadehost copy at 41/59 and 60/59. Name formatting applies to list line 1. Omit-name and paste still work. `dotnet test` Core tests still pass.

**Done when**

- Setup and Manager identity can format **both** list lines in-app without seeing codes while typing; wrap closes; preview uses the Minecraft font; counters warn when a line is too long.

**Changelog:** *(date when finished)*

---

## After this plan

When P1–P4 are **DONE**:

- Mark this file **COMPLETE**.
- [`NEXT.md`](NEXT.md) → Step **8.5.2** Pass 3 (**blocked** until the operator says so).
- V1 dashboard Step **8.11** **DONE**.
- Do **not** start **8.6.1** or **9.1**.

---

## Plan changelog

| Date | Note |
|------|------|
| 2026-08-24 | **P2 DONE** (3×2 pin row fills chrome; Hours left + Idle timeout from existing budget refresh). Living **NEXT = P3**. Pass 3 stays blocked. |
| 2026-08-24 | P1 follow-up: 6px WPF side strips match caption at the top so the title bar is flush. Living **NEXT = P2**. |
| 2026-08-24 | P1 follow-up: caption full-bleeds on resize (outer `1fr` tracks). Living **NEXT = P2**. |
| 2026-08-24 | **P1 DONE** (caption `--caption-bg` + bottom border). Living **NEXT = P2**. Pass 3 stays blocked. |
| 2026-08-24 | Created. Living **NEXT = P1**. Pass 3 stays blocked. |
