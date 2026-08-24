# V1 operator-notes follow-on 2 (living)

**Status:** Living. Created 2026-08-24. **Live NEXT:** [`NEXT.md`](NEXT.md).  
**Parent:** [`V1-Implementation-Plan.md`](V1-Implementation-Plan.md) Step **8.10**.  
**Why now:** operator 2026-08-24 — Manager/Setup density, toasts, pack UX, VM1 icon, and MOTD styling **before** QA Pass 3. Vague layout notes: agents **decide inside each section’s bounds** (UI skills) and record the choice. Stop and ask for spend, `tofu destroy`, `DEFAULT`, or pulling other parked after-v1 items.

**Cost:** $0. TESTING profile only. Never `DEFAULT` / live Forge lab. Never Minecraft `0.0.0.0/0`.  
**Functions / tofu:** do **not** `tofu apply` / `destroy`. Do not SoftStop the door.  
**Hybrid config dir:** `MCMANAGER_CONFIG_DIR` = `mcmgr-blank-test` — **not** repo `data/config.local.json`.  
**SSH / VM1:** P8 requires TESTING SSH + idle-agent redeploy. Other sections are Hybrid/`dotnet run` unless noted.  
**Hosts / OCIDs:** do **not** paste live OCIDs, IPs, Auth Tokens, or key material into tracked docs.

Applies to **both** Setup and Manager **Change pack** unless a section says otherwise.

---

## How agents must use this file

1. Read **this protocol**, the [Progress dashboard](#progress-dashboard), [Scrutiny](#scrutiny-plan-decisions), and **only the NEXT section**.
2. Implement only that section. Do not start neighbors “while you are here.”
3. After finishing: mark **DONE**, set the next incomplete section to **NEXT**, changelog line, update V1 plan Step **8.10** + dashboard, update [`NEXT.md`](NEXT.md), **stop**.
4. Mirror TESTING / guest fixes into local SoT. File [`Issues.md`](Issues.md) for on-box/Setup/door bugs.
5. Git: commits allowed per `git-policy`; never push/PR unless the operator asks.
6. Do **not** start Step **8.5.2** (Pass 3), **8.6.1**, or **9.1**.
7. If this plan disagrees with [`PRODUCT-IDEAS.md`](PRODUCT-IDEAS.md), **follow this plan** and note drift. Do not rewrite PRODUCT-IDEAS.
8. VM1: START if needed, **disable idle** while working, **re-enable** when finished (OS-ISSUE-7). After `vm_agent/` edits, **redeploy the idle agent** on RUNNING VM1.
9. **UI-heavy sections (P1, P2, P4, P6, P7, P9)** must read the named UI skills **before** changing CSS/Razor. Reuse existing tokens. **NuGet** on `McManager.Hybrid` only. No Avalonia.
10. User-visible Setup/manage changes: add a **short** paragraph to [`Guide.md`](Guide.md) in the same step.

Vague notes: **decide** (layout, copy, animation ms, width) inside the section. **Stop and ask** for legal/ToS, spend, or scope listed in [Parked](#parked-not-this-plan).

### Context budget

This header + **one** P-section + the files listed there. Blueprint: **named §§ only**. Contracts: named headings only. Do not load Pass 3, the full V1 plan, or PRODUCT-IDEAS unless a heading is named.

### UI skills (required where the section says **UI skill**)

Read **before** CSS/Razor:

- `C:\Users\matto\.agents\skills\impeccable\SKILL.md`
- `C:\Users\matto\.agents\skills\web-design-guidelines\SKILL.md`

Optional visual pass: `C:\Users\matto\.agents\skills\frontend-design\SKILL.md`.

### Operator entry

New Agent chat → `/next-step`. Fresh chat per P-section. **P2** and **P9** are **plan-first** (switch to Plan mode, or post a short design and wait).

### PARALLEL-OK

| Group | Sections | Why |
|-------|----------|-----|
| A | P1 → P2 → P3 → P4 → P5 → P6 → P7 | Shared Hybrid chrome / `app.css` / Server + Setup Razor |
| B | P8 | `vm_agent/` + TESTING SSH; no Hybrid overlap with P1–P7 |
| Then | P9 | After P4 (identity UI) and P8 (`os_publish` MOTD apply) |

P8 may run in a **separate operator chat** while Group A is on P1–P7. Default in one chat: sequential P1…P9 (do P8 before P9).

---

## Progress dashboard

| ID | Title | Status | Parallel | Cursor |
|----|-------|--------|----------|--------|
| P1 | Toasts: bottom-left + start success fade | **DONE** | SEQUENTIAL — toast CSS sits on the same manage chrome as P2 | agent |
| P2 | Custom title bar (preferred) or compact header | **NEXT** | SEQUENTIAL — owns `MainWindow` + title row | plan-first |
| P3 | Tab rename/order + Server tab snappiness | TODO | SEQUENTIAL — `MainLayout` + Server VM | agent |
| P4 | Manager tab density (sub-tabs, mods collapsed) | TODO | SEQUENTIAL — Server tab markup after P2 height | either |
| P5 | Pack identity version dropdowns | TODO | SEQUENTIAL — `PackIdentityFields` used by Setup P7 | agent |
| P6 | Setup sparse pages + Always Free copy | TODO | SEQUENTIAL — `SetupWizard.razor` | either |
| P7 | Setup Minecraft step layout | TODO | SEQUENTIAL — same wizard file as P6 | either |
| P8 | VM1 color server icon | TODO | PARALLEL-OK with P1–P7 | agent |
| P9 | MOTD formatting editor | TODO | SEQUENTIAL after P4 + P8 | plan-first |

---

## What already exists (do not rediscover)

- Compact toasts (8.8 P3): lower-**right**, above the progress dock. Short success auto-hides at **3.5s** with no fade. `ActionBanner.ShouldPersist` keeps errors/warnings/progress **and** success copy longer than 80 chars or with a newline — that is why some “started” toasts sit until X.
- Title row (`MainLayout` / Setup wizard): “mc manager” wordmark + “Always Free” + bell / settings / more. Stat pins sit in the next section. Native WPF caption is the default white bar (`MainWindow.xaml` `Title="MC Server Manager"`). Width is locked to `--app-shell-width` (~802 CSS px) via `MainWindow.FitWidthToShell`.
- Tabs today (left → right): Whitelist, Usage, **Server Management**, Console, Advanced, Troubleshooting. Internal tab id for Server is already `"server"`.
- Server tab `OnInitialized` always calls `RefreshAsync` (identity + oversized flag + backup list, sets `IsBusy`) and `RefreshMinecraftVersionAsync`. Clicking the tab therefore does a full storage round-trip on first paint.
- Change pack **Install this pack**: `DerivedPackWorkflow.BuildAndRetain` runs on the UI thread **before** `ConfirmAsync` — that is the freeze.
- Jar-root / unstructured identity: Minecraft version and loader **version** are `<input type="text">`; loader **kind** is already a `<select>`. Vanilla Setup already has `MojangVersionCatalog` + Fabric/Forge/NeoForge clients.
- Server icon: admin PC composes 64×64 color → `messages/server-icon.png`; door greyscale variants already work. VM1 apply is `vm_agent/os_publish.py` `_apply_identity` (`server-icon.png` + `.tmp` next to it under the server dir, then `_chown_mcmgr`). OS-ISSUE-10 (boot ordering) is **Fixed**; this dump is a **remaining** color-icon-on-VM1 failure.
- MOTD today: plain `server_name` + `description` joined with `\n` (`_build_motd`). Manager copy says “plain text — not a formatted MOTD editor.” PRODUCT-IDEAS parks a rich MOTD suite as **out of MVP / after v1** — this plan **pulls a bounded editor into v1**.

---

## Scrutiny (plan decisions)

Implementing agents follow these unless the operator overrides in chat.

**Custom title bar (P2).** **Prefer** a Discord-style custom caption: WPF `WindowChrome` / `WindowStyle="None"`, Blazor draws min / max / close **and** bell / settings / more, styled with the rest of the window. This is possible on WPF + BlazorWebView; it is not Electron. **Fallback** (only if drag, snap, maximize, or WebView hit-testing is broken): delete the inner “mc manager” / “Always Free” title row, shrink the four stat pins, put bell / settings / more in a vertical column on the right of the pins. Do not keep both a custom caption **and** a second inner title row.

**Window size (P2).** Consider a **slightly wider** default (~1000–1100 CSS px, still laptop-safe). Min width may stay near today’s shell if the custom caption needs it. Wider is a tool for P4/P6/P7, not a goal by itself.

**Server inner sub-tabs (P4).** **Accept.** Split Server into inner sub-tabs so the tab body fits without vertical scroll at the default window (Identity, World/backups, Modding, Change pack — names via UI skill). Mod **file list** starts **collapsed**. Other Manager tabs get the same density treatment only if they still overflow after P2. Console stays a log. Do not invent a second visual language.

**Setup sparse pages (P6).** **Accept** combining OCI profile + budget email **if** both fit without an inner tiny scroll at the default window. Always Free stays its **own** step (P6 adds short explainer copy there). UI skill may center or grow remaining sparse steps instead of combining everything.

**Minecraft step (P7).** Vanilla vs Modded is the **primary** choice (visual weight / spacing). Sub-options (vanilla flavor, pack drop, buttons) sit in a **side-by-side** group, not a full-width stack. UI skill picks exact columns.

**Pack identity dropdowns (P5).** Minecraft version = Mojang catalog `<select>` (same source as Vanilla Setup). Loader version = Fabric/Forge/NeoForge catalog for the selected loader + MC version. If the detected value is missing from the list, **keep it as an extra option**. Catalogs fail → text fallback, do not block. Java major may be a small known-majors select driven by the MC/loader floor (not free-typed).

**Install-this-pack freeze (P3).** Confirm **first**. Build the derived zip **after** confirm, on a background thread, with dock progress. Identity-incomplete still blocks before the dialog.

**Toasts (P1).** Bottom-**left**, above the dock. Start-success (and other short success) auto-hide at **4s** with a **fade**. Progress / warning / error still persist until X. Long success may still persist.

**MOTD (P9).** **Target:** in-app editor inspired by [fadehost MOTD generator](https://tools.fadehost.com/motd-generator/) — type, select a span, apply formatting, Minecraft-list preview. Paste of an external `motd=` string must work. Raw generated string lives in a **collapsed** details row with copy. Checkbox: **do not put the server name on the MOTD** (description-only). Hex / gradients: allow paste + best-effort preview; tell the user they need Paper/Spigot **1.16+** (Vanilla / Forge / Fabric ignore hex). Standard `§` color/format codes are the v1 baseline for all game types. Door idle/starting/exhausted MOTDs are **out of this step**.

**VM1 icon (P8).** Diagnose on TESTING first (operator suspects a `tmp` permission). Product write today is `server-icon.png.tmp` **in the server directory**, not `/tmp`. Fix the **product** `vm_agent` path (and on-box permissions if the agent cannot write as `ubuntu` / cannot `chown` `mcmgr`). Redeploy idle agent. Do not only chmod the live VM.

---

## Drift vs PRODUCT-IDEAS (follow this plan)

| Topic | PRODUCT-IDEAS / older V1 | This plan |
|-------|--------------------------|-----------|
| Rich MOTD editor | After-v1 / out of MVP | **v1 now** (P9), bounded (list MOTD + `§` + paste; hex noted Paper-only) |
| Tab title “Server Management” | Older copy | Rename **Server**; order Whitelist → Server → Console → Usage → Advanced → Troubleshooting |
| Window chrome | Inner title row + native caption | Custom caption preferred (P2) |

Do **not** rewrite PRODUCT-IDEAS to match.

---

## Parked (not this plan)

| Item | Where |
|------|--------|
| Pack-replace **light swap** | After-v1 |
| In-app pack catalog / CurseForge API | Rejected / deferred |
| Door MOTD copy editor (idle / starting / exhausted text) | Not requested |
| Players tab, paid/spend mode, Pass 3, 8.6.1, 9.1 | Existing V1 parking |
| Hex/gradient MOTD as a guaranteed Vanilla/Forge/Fabric feature | P9 notes Paper-only; do not fake it |

---

## P1 — Toasts: bottom-left + start success fade

**Status:** DONE  
**Parallel:** SEQUENTIAL — toast host/CSS is the same manage chrome P2 will restyle  
**Cursor mode:** agent  
**UI skill:** yes

**Read first**

- This section + Scrutiny (Toasts)
- `src/McManager.Hybrid/Components/Layout/MainLayout.razor` (toast host)
- `src/McManager.Hybrid/Components/Layout/ProgressDock.razor`
- `src/McManager.Hybrid/wwwroot/css/app.css` (`.mcm-action-banner`, `--mcm-toast-bottom`)
- `src/McManager.Hybrid/ViewModels/ActionBannerViewModel.cs`
- `src/McManager.Core/Notifications/ActionBanner.cs`
- `src/McManager.Hybrid/ViewModels/MainViewModel.cs` (start/stop toast copy) — only the toast call sites

**Do**

1. Move compact toasts to the **bottom-left**, still **above** the progress dock (raise `--mcm-toast-bottom` or equivalent so the dock does not cover them).
2. Short success auto-hide **4 seconds**. Add a CSS **fade-out** (UI skill picks duration, ~250–400ms) instead of an instant remove.
3. Start-success must auto-hide even if the copy is slightly over 80 characters. Keep persist for progress / warning / error.
4. Guide: one sentence if toast placement is user-visible.

**Test**

- `dotnet test` on any ActionBanner unit tests; `dotnet build` Hybrid.
- Operator: Start Minecraft → success toast bottom-left, fades after ~4s; an error toast stays until X; toast sits above the dock when a long job is running.

**Done when**

- Toasts are bottom-left above the dock; start success fades at 4s; persist rules still hold for errors/progress.

**Changelog:** 2026-08-24 — **DONE.** Compact toasts lower-**left** above the dock (`--mcm-toast-bottom` 20px). Short success auto-hides at **4s** with a **320ms** fade-out (`prefers-reduced-motion` skips motion). Start-success forces `AutoHide` even if copy is slightly over 80 chars; progress / warning / error still persist. Guide. **NEXT = P2.**

---

## P2 — Custom title bar (preferred) or compact header

**Status:** NEXT  
**Parallel:** SEQUENTIAL — owns `MainWindow` + inner title row; P4 needs the reclaimed height  
**Cursor mode:** plan-first  
**UI skill:** yes

**Read first**

- This section + Scrutiny (Custom title bar, Window size)
- `src/McManager.Hybrid/MainWindow.xaml`
- `src/McManager.Hybrid/MainWindow.xaml.cs`
- `src/McManager.Hybrid/Components/Layout/MainLayout.razor` (title row, stat pins, chrome buttons)
- `src/McManager.Hybrid/Components/Setup/SetupWizard.razor` (wizard title row)
- `src/McManager.Hybrid/wwwroot/css/app.css` (`.mcm-title-row`, `.mcm-wordmark`, `.mcm-always-free`, `.mcm-stat-*`, `--app-shell-width`)

**Do**

1. **Plan-first:** post a short design (custom `WindowChrome` vs fallback compact header) covering drag, aero snap, maximize restore, Setup **and** Manage, and how min/max/close plus bell/settings/more share one caption. Wait for operator if the design is uncertain; otherwise implement the preferred path in the same chat after the design is posted.
2. **Preferred:** custom caption styled with the app (not the default white bar). Remove inner “mc manager” and “Always Free”. Integrate notification / settings / more into that caption.
3. **Fallback** only if custom chrome is blocked: Scrutiny fallback (no second title row; pins + vertical icon column).
4. Consider a slightly **wider** default window (~1000–1100 CSS px). Update `FitWidthToShell` / `--app-shell-width` together so the WebView is not clipped.
5. Reclaim vertical space for the tab body (the point of this step).
6. Guide: one sentence on window chrome if user-visible.

**Test**

- `dotnet run` Hybrid: drag, snap, maximize, close; Setup wizard and Manage both look consistent; bell/settings/more still work; default size is usable on a 1366-wide laptop.

**Done when**

- Native white caption is gone **or** the documented fallback is in place; wordmark + Always Free text are gone; tab body is taller; width decision is recorded in the changelog.

**Changelog:** *(date when finished)*

---

## P3 — Tab rename/order + Server tab snappiness

**Status:** TODO  
**Parallel:** SEQUENTIAL — `MainLayout` tab strip + `ServerManagementViewModel`  
**Cursor mode:** agent

**Read first**

- This section + Scrutiny (Install-this-pack freeze)
- `src/McManager.Hybrid/Components/Layout/MainLayout.razor` (tab strip + `SelectTabAsync`)
- `src/McManager.Hybrid/Components/Tabs/ServerManagement/ServerManagementTab.razor` (`OnInitialized`)
- `src/McManager.Hybrid/ViewModels/ServerManagementViewModel.cs` (`RefreshAsync`, `ConfirmReplace` / `BuildAndRetain` path)
- `src/McManager.Core/Setup/DerivedPackWorkflow.cs`
- Grep user-visible “Server Management” (Guide, Hybrid copy) — do not rewrite the whole Guide

**Do**

1. Rename the tab label to **Server**. Internal id `"server"` may stay.
2. Left → right: **Whitelist, Server, Console, Usage, Advanced, Troubleshooting**.
3. Fix tab-click lag: do not block first paint on a full backup list + identity pull. Defer or cache; keep the tab instance if Blazor is tearing it down; never SSH-scan every mod jar just to open the tab (P4 will hide the list anyway).
4. Fix Install-this-pack freeze: **confirm first**, then build the derived zip off the UI thread with dock progress.
5. Guide: tab name/order if the happy-path mentions “Server Management.”

**Test**

- Click Server: first paint is immediate; backups/identity fill in without a multi-second freeze.
- Change pack → Install this pack: confirm dialog appears **immediately**; zip build happens after Yes.
- Tab order matches Scrutiny.

**Done when**

- Label, order, tab-open lag, and pre-confirm freeze are fixed.

**Changelog:** *(date when finished)*

---

## P4 — Manager tab density (sub-tabs, mods collapsed)

**Status:** TODO  
**Parallel:** SEQUENTIAL — Server markup after P2 height and P3 snappiness  
**Cursor mode:** either  
**UI skill:** yes

**Read first**

- This section + Scrutiny (Server inner sub-tabs)
- `src/McManager.Hybrid/Components/Tabs/ServerManagement/ServerManagementTab.razor`
- `src/McManager.Hybrid/wwwroot/css/app.css` (tab body, section cards)
- Other Manager tabs only if they still overflow at the **new** default window (Whitelist, Usage, Advanced, Troubleshooting) — do not open Console for restyle

**Do**

1. Server: inner sub-tabs (Identity / World / Modding / Change pack — UI skill may rename). Goal: the **active** sub-tab fits in the tab box without vertical scroll at default size.
2. Modding: the scrollable mod list is an **expander**, **collapsed by default**. Summary line (count / loader) stays visible.
3. If Whitelist / Usage / Advanced still overflow, apply the same density ideas (side-by-side, smaller gaps, inner sub-tabs). Do not shrink type below readable.
4. Leave Identity in a shape P9 can extend (name, description, icon, preview). Do not build the MOTD editor here.
5. Guide: one sentence if Server is now split into inner tabs.

**Test**

- Default window, default UI scale: open each Server sub-tab; interact without scrolling the tab body (high-DPI / large fonts may still scroll — do not clip).
- Mod list hidden until expanded.

**Done when**

- Server content is grouped so the operator rarely scrolls inside a control; mods start collapsed.

**Changelog:** *(date when finished)*

---

## P5 — Pack identity version dropdowns

**Status:** TODO  
**Parallel:** SEQUENTIAL — shared `PackIdentityFields` with P7  
**Cursor mode:** agent

**Read first**

- This section + Scrutiny (Pack identity dropdowns)
- `src/McManager.Hybrid/Components/Shared/PackIdentityFields.razor`
- `src/McManager.Core/Setup/MojangVersionCatalog.cs`
- `src/McManager.Core/Setup/FabricMetaClient.cs` / `ForgePromotionsClient.cs` / `NeoForgeMavenClient.cs` (existing list APIs only)
- `src/McManager.Hybrid/ViewModels/SetupWizardViewModel.cs` (Vanilla catalog load — reuse, do not duplicate HTTP)
- `src/McManager.Hybrid/ViewModels/ServerManagementViewModel.cs` (identity bind)

**Do**

1. Replace typed Minecraft version and loader version with `<select>`s backed by existing catalogs. Loader **kind** stays a select.
2. Include the detected value if it is not in the catalog.
3. Changing MC version refreshes compatible loader versions; Java major follows the floor (small select OK).
4. Offline / catalog fail → keep a text fallback so Setup is not blocked.
5. Same control on Setup and Change pack.

**Test**

- Unit tests for “detected value present as option”; Hybrid: jar-root confirm shows dropdowns, not empty text boxes.
- `dotnet test` Core tests touching identity / catalogs.

**Done when**

- Users pick versions from lists on Setup and Change pack; typing is only the fallback.

**Changelog:** *(date when finished)*

---

## P6 — Setup sparse pages + Always Free copy

**Status:** TODO  
**Parallel:** SEQUENTIAL — `SetupWizard.razor`  
**Cursor mode:** either  
**UI skill:** yes

**Read first**

- This section + Scrutiny (Setup sparse pages)
- `src/McManager.Hybrid/Components/Setup/SetupWizard.razor` (Always Free, OCI, email steps)
- `src/McManager.Hybrid/ViewModels/SetupWizardViewModel.cs` (step titles, help strings, step indices)
- `src/McManager.Core/Config/SetupWizardState.cs` / `SetupWizardStore.cs` (step numbering — if combining pages, migrate saved resume)
- `src/McManager.Hybrid/wwwroot/css/app.css` (`.mcm-wizard-profile-pre`)

**Do**

1. Always Free step: add **short** body copy (Always Free–eligible shapes, $0 target, $1 spend brake, possible ~$1–$2 residual). Checkboxes stay. Detail stays on `WizardHelp` icons.
2. OCI profile `<pre>`: grow to fit the profile text (no tiny inner scroll). Page scroll is OK if the window is short.
3. Combine OCI profile + budget email **if** they fit. Otherwise treat empty space (center / grow) — UI skill decides and records it.
4. Do not merge Always Free into another step.
5. Guide: only if step count / Always Free copy changes the happy path.

**Test**

- Step 1 has explainer text and still requires the three checkboxes.
- Profile details are readable without scrolling inside a cramped box.
- Saved wizard resume still lands on the right page if steps were merged.

**Done when**

- Always Free is explained; profile box is usable; sparse-page treatment is applied or pages are combined.

**Changelog:** *(date when finished)*

---

## P7 — Setup Minecraft step layout

**Status:** TODO  
**Parallel:** SEQUENTIAL — same wizard file as P6  
**Cursor mode:** either  
**UI skill:** yes

**Read first**

- This section + Scrutiny (Minecraft step)
- `src/McManager.Hybrid/Components/Setup/SetupWizard.razor` (game step only)
- `src/McManager.Hybrid/wwwroot/css/app.css` (wizard choice cards, pack drop)
- `src/McManager.Hybrid/Components/Shared/PackIdentityFields.razor` (already dropdowns after P5)
- `src/McManager.Hybrid/Components/Shared/PackAssistedReviewPanel.razor` — only if it forces a full-width stack on this step

**Do**

1. Vanilla vs Modded is visually **primary** (spacing, size, or grouping — UI skill). Sub-options are secondary.
2. Side-by-side: primary choice column + pack drop / Choose / Clear grouped, not a full-width vertical stack. Reduce control width so they sit in sections.
3. Keep assisted review and identity fields usable; they may stay below the two-column header if they need width.
4. Aim: no vertical scroll on this step at the P2 default window when Modded is selected **before** a pack is analyzed. After analyze, review lists may scroll.

**Test**

- Default window: Vanilla/Modded + pack drop visible together without scrolling.
- Switching Vanilla ↔ Modded does not clip Next / the dock.

**Done when**

- Minecraft step uses horizontal grouping; Vanilla/Modded reads as the main choice.

**Changelog:** *(date when finished)*

---

## P8 — VM1 color server icon

**Status:** TODO  
**Parallel:** PARALLEL-OK with P1–P7 (on-box / `vm_agent` only)  
**Cursor mode:** agent

**Read first**

- This section + Scrutiny (VM1 icon)
- [`Agent-Deploy-Pitfalls.md`](Agent-Deploy-Pitfalls.md)
- `vm_agent/os_publish.py` (`_apply_identity`, `_chown_mcmgr`, `_server_dir`)
- `vm_agent/record_boot.py` (force pull)
- `onbox/mcmgr/` permission helpers **only if** the diagnose shows `mcmgr` cannot read `server-icon.png`
- Contracts heading `messages/server-icon.png` in [`Contracts-Object-Storage.md`](Contracts-Object-Storage.md) — that heading only
- [`Issues.md`](Issues.md) OS-ISSUE-10 (historical; do not re-open the ordering bug unless it regressed)

**Do**

1. TESTING: START VM1 if STOPPED, disable idle, SSH as `ubuntu`. Confirm Object Storage has `messages/server-icon.png`, `messages.vm1` flag behavior, and whether `/opt/mcmgr/server/server-icon.png` exists / owner / mode. Reproduce the `.tmp` write (operator hypothesis).
2. Fix the **product** path so the process that applies identity can write the color PNG where Minecraft loads it (`server-icon.png` in the server dir, 64×64). If `ubuntu` cannot write `/opt/mcmgr/server/` or cannot `chown mcmgr`, stage then install with the existing permission contract — do not leave a chmod-only live patch.
3. Redeploy idle agent from product `vm_agent/` ([pitfalls](Agent-Deploy-Pitfalls.md)). Mirror any on-box script fix into `onbox/mcmgr/`.
4. File [`Issues.md`](Issues.md) if this is a new on-box bug (do not silently reopen OS-ISSUE-10 unless the old ordering bug is back).
5. Re-enable idle when finished.

**Test**

- Save icon in Manager → Restart Minecraft on VM1 (play IP on VM1) → Java list shows the **color** icon, not the door greyscale. Door icons still update when VM1 is down.
- `ls -l` on `server-icon.png` is readable by `mcmgr`.

**Done when**

- Color icon lands on VM1 from the product path; TESTING matches repo SoT.

**Changelog:** *(date when finished)*

---

## P9 — MOTD formatting editor

**Status:** TODO  
**Parallel:** SEQUENTIAL after P4 (identity UI) and P8 (`_build_motd` / apply)  
**Cursor mode:** plan-first  
**UI skill:** yes

**Read first**

- This section + Scrutiny (MOTD)
- `src/McManager.Core/Usage/ChatMessagesDocument.cs`
- `src/McManager.Core/Services/ChatMessagesStore.cs` / `ServerIdentityUx.cs`
- Manager Identity UI (post-P4 Server sub-tab) + Setup Name-and-icon page
- `vm_agent/os_publish.py` `_build_motd` / `_patch_properties_key` (escaping)
- Contracts `messages/chat.json` heading only
- Preview CSS `.mcm-motd-preview`

**Do**

1. **Plan-first:** short design for the editor (toolbar + selection, preview, collapsed raw string, omit-name checkbox, paste-from-fadehost, how `chat.json` stores the string vs name). Then implement.
2. In-app editor is the **target**. Paste of a fadehost `motd=` string must work even if the toolbar is simpler than the website.
3. Preview should look like the in-game list (two lines, `§` colors/formats). Hex/gradient: best-effort + Paper 1.16+ note.
4. Option: MOTD is **description only** (no server name line). Name field still exists for Manager display / defaults.
5. Apply path: VM1 `motd=` in `server.properties` must keep `§` / `\n` (and hex if Paper). Do not strip codes in `_build_motd`.
6. Door MOTD: **do not change**.
7. Guide: how to format the list MOTD (and the omit-name option).

**Test**

- Unit tests for MOTD build (name+desc, desc-only, `§` preserved, properties escaping).
- Operator: paste a fadehost string, preview, Save, Restart Minecraft, Java list matches; expandable raw + copy; omit-name hides the name line.

**Done when**

- Setup + Manager can produce a formatted list MOTD without a required external tool; paste still works; VM1 apply preserves codes.

**Changelog:** *(date when finished)*

---

## After this plan

When P1–P9 are **DONE**:

- Mark this file **COMPLETE**.
- [`NEXT.md`](NEXT.md) → Step **8.5.2** Pass 3 (**blocked** until the operator says so).
- V1 dashboard Step **8.10** **DONE**.
- Do **not** start **8.6.1** or **9.1**.

---

## Plan changelog

| Date | Note |
|------|------|
| 2026-08-24 | **P1 DONE** (toasts bottom-left, 4s fade, start-success AutoHide). Living **NEXT = P2** (plan-first). Pass 3 stays blocked. |
| 2026-08-24 | Created. Living **NEXT = P1**. Pass 3 stays blocked. |
