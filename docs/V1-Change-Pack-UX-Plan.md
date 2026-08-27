# V1 Change pack UX (living)

**Status:** P4 NEXT. Created 2026-08-27 (docs only). **Live NEXT:** [`NEXT.md`](NEXT.md).  
**Parent:** [`V1-Implementation-Plan.md`](V1-Implementation-Plan.md) Step **8.15**.  
**Why now:** operator 2026-08-27 — unstructured zip review is three separate scroll lists; Change pack copy/layout is long and stacked; pick is blocked when VM1 is stopped; the Change-pack bottom bar participates in layout and follows the user onto other tabs. Vague copy not named below and compact side-by-side layout: agents **decide inside each section’s bounds** (and [Scrutiny](#scrutiny-plan-decisions)). Stop and ask for spend, `tofu destroy`, `DEFAULT`, or parked after-v1 items.

**Cost:** $0. TESTING profile only. Never `DEFAULT` / live Forge lab. Never Minecraft `0.0.0.0/0`.  
**Functions / tofu:** do **not** `tofu apply` / `destroy`. Do not SoftStop the door.  
**Hybrid config dir:** `MCMANAGER_CONFIG_DIR` = `mcmgr-blank-test` — **not** repo `data/config.local.json`.  
**SSH / VM1:** not required except **P4** install-when-stopped (optional live check). Analyze/pick is local.  
**Hosts / OCIDs:** do **not** paste live OCIDs, IPs, Auth Tokens, or key material into tracked docs.

Applies to **Manage → Server → Change pack**. The shared `PackAssistedReviewPanel` also appears in Setup — **P1** list behavior applies there. Do **not** restyle the Setup/FirstRun wizard to match Change pack compactness (P2).

---

## How agents must use this file

1. Read **this protocol**, the [Progress dashboard](#progress-dashboard), [Scrutiny](#scrutiny-plan-decisions), and **only the NEXT section**.
2. Implement only that section. Do not start neighbors “while you are here.”
3. After finishing: mark **DONE**, set the next incomplete section to **NEXT**, changelog line, update V1 plan Step **8.15** + dashboard, update [`NEXT.md`](NEXT.md), **stop**.
4. Git: commits allowed per `git-policy`; never push/PR unless the operator asks.
5. Do **not** start Step **8.5.2** (Pass 3), **8.6.1**, or **9.1**.
6. If this plan disagrees with [`PRODUCT-IDEAS.md`](PRODUCT-IDEAS.md), **follow this plan** and note drift. Do not rewrite PRODUCT-IDEAS.
7. **UI-heavy sections** must read the named UI skills **before** changing CSS/Razor. Reuse existing granite + cobalt tokens. **NuGet** on `McManager.Hybrid` only. No Avalonia.
8. User-visible manage/Setup review changes: add a **short** paragraph (or patch the existing Change pack / assisted-review sentences) in [`Guide.md`](Guide.md) in the same section.

Vague notes: **decide** (side-by-side grouping, leftover copy) inside the section **using Scrutiny**. **Stop and ask** for legal/ToS, spend, or scope listed in [Parked](#parked-not-this-plan).

### Context budget

This header + **one** P-section + the files listed there. Blueprint: **named §§ only**. Do not load Pass 3, the full V1 plan, or PRODUCT-IDEAS unless a heading is named.

### UI skills (required where the section says **UI skill**)

Read **before** CSS/Razor:

- `C:\Users\matto\.agents\skills\impeccable\SKILL.md`
- `C:\Users\matto\.agents\skills\web-design-guidelines\SKILL.md`

Optional visual pass: `C:\Users\matto\.agents\skills\frontend-design\SKILL.md`. Keep `--bg` / `--caption-bg` / `--surface-1` / `--fill-accent` / existing card language. Do not invent a new type world.

### Operator entry

New Agent chat → `/next-step`. Fresh chat per P-section.

### PARALLEL-OK

None. P1–P4 share Change pack Razor, `app.css`, and `ServerManagementViewModel`. **SEQUENTIAL.**

---

## Progress dashboard

| ID | Title | Status | Parallel | Cursor |
|----|-------|--------|----------|--------|
| P1 | Single-list assisted review | **DONE** | SEQUENTIAL — shared review panel | agent |
| P2 | Change pack compactness | **DONE** | SEQUENTIAL — same tab + CSS | agent |
| P3 | Overlay dock + tab-scoped bars | **DONE** | SEQUENTIAL — same Manage grid CSS | agent |
| P4 | Pick/review when VM stopped | **NEXT** | SEQUENTIAL — same gates + install path | agent |

---

## What already exists (do not rediscover)

- **Contract:** [`Pack-Import-Intended-Design.md`](Pack-Import-Intended-Design.md) — skip order, dependency freeze, assisted vs automatic. Review UI is **one list** (operator 2026-08-27). Do not reopen skip-order or freeze rules.
- **Core grouping (8.9):** `PackAssistedReview` still has `WillSkip` / `NeedsYourCall` / `MustKeep` + `PackDependencyFreeze`. Reuse those buckets as **row state**; do not keep three scroll regions.
- **Shared panel:** `PackAssistedReviewPanel.razor` (Setup + Change pack). Skip persist: `PackAssistedReviewActions` + Layer 2 per-archive overlay. Unchecking an automatic skip already needs re-analyze (`NeedsReanalyze`).
- **Identity:** `PackIdentityFields.razor` — Minecraft, loader, loader version, Java. Change pack currently renders identity **below** the review panel.
- **Summary `<pre>`:** `Vm.PackSummary` in `ServerManagementTab.razor`. Shown for any preview; sits above assisted review.
- **Pick gate:** `PackReplaceUx.CanPick` requires `vm1Running && !busy`. Drop zone, Choose file, and Modding **Change pack** (opens the inner tab) all bind `CanPickPack`. Analyze is **local** (no SSH).
- **Install:** `PackReplaceUx.CanInstall` also requires VM1 running. `InstallPackReplaceAsync` is SSH full re-setup. World kept unless wipe is checked.
- **Dock:** `MainLayout.razor` hosts `ProgressDock` as a content-pane overlay (toast pattern; does not size tracks). Visible only on **Server → Change pack** while `ShowChangePackUi`. Toasts stay `z-index` above. Server tab is keep-alive (`mcm-tab-keep`). Inner pane lives on `ServerManagementViewModel.ServerPane`.

---

## Scrutiny (plan decisions)

Locked by the operator. Do not reopen in an implementation chat.

| Topic | Decision |
|-------|----------|
| Review lists | **One** scrollable list of all review jars. Checking client-only **must not** move the row to another list. |
| Checkbox | Checked = **client-only** (skip on the server). Unchecked + not auto-excluded = **server-compatible** (keep). |
| Auto-detected client-only | Start **checked**. **Editable** (not greyed) unless Must keep freeze applies. Uncheck = keep on the server (existing overlay / re-analyze path). |
| Must keep | Same list; checkbox **disabled**. Note in a left column, e.g. `required by tconstruct` (`RequiredByName`). |
| Identity vs list | Identity fields **above** the mod list. Not beside it. |
| Summary `<pre>` | **Hidden** when assisted review is showing (unstructured / unknown-side). **Shown** (and twice as tall) only when the pack does **not** need that confirm list. |
| Change pack title | Remove the inner-pane **Change pack** heading. Inner tabs already name the pane. |
| Friends warning | Compact one-liner only: **Players need this mod pack to join the server**. No body. Friend-pack **checkboxes** stay. |
| Reinstall blurb | **Delete** the paragraph that starts “This reinstalls Minecraft on the server…” |
| Drop zone | Title: **Drop a mod pack here**. Subtext: accepted formats, short (Modrinth `.mrpack`, CurseForge Server Pack `.zip`, unstructured `.jar` zip). Large packs / Choose file may stay as a short hint if still needed. |
| Skip warning | Body: **Known client-only mods will automatically be skipped. Check the list below and confirm that all client-only mods are correctly marked.** |
| Pronouns | On **Change pack**, drop “we” / “you”. Setup wizard copy is **not** this pass unless a **shared** constant would otherwise change Setup tone — then add a Change-pack-specific string. |
| Side-by-side | Compact leftover blocks and place **beside** each other to cut vertical scroll. **Exception:** identity stays above the list. |
| Dock overlay | Same idea as toasts: sits **on** the content pane; does **not** add a layout box / extra grid row. Toasts stay above the dock. |
| Dock tab scope | Visible only on **Server → Change pack**. Leaving that inner tab or the Server sidebar tab **hides** it; coming back restores it if a review is still in progress. Progress in the VM continues. |
| Stopped VM | Pick, drop, analyze, identity, and review work while VM1 is **stopped**. **Install this pack** starts VM1, then runs the existing replace. Do not require the operator to Start first. |
| Skip-order / freeze | Unchanged. Never skip a jar a kept jar requires. Force-skip of a required dep still **blocks** install. |

---

## Parked (not this plan)

| Item | Why |
|------|-----|
| Restyle Setup/FirstRun compactness | Operator asked for the **Change pack** page. P1 shared list only. |
| Light pack swap | After-v1. Full re-setup stays. |
| In-app pack catalog / public Minecraft | Rejected. |
| Pass 3 / 8.6.1 / 9.1 | Stay blocked. |
| Changing itzg lists or in-jar heuristics | Not requested. |
| Two lists (“confirmed client-only” vs “unknown”) | Explicitly rejected. |
| Setup wizard ProgressDock overlay | Different shell (in-flow wizard footer). Do not change unless it uses the Manage grid dock. |

---

## After this plan

1. [`NEXT.md`](NEXT.md) → Step **8.5.2** Pass 3 (**blocked** until the operator says so).
2. V1 dashboard Step **8.15** **DONE**.
3. [`Guide.md`](Guide.md) Change pack / assisted-review sentences match the single list, compactness, overlay dock, and start-then-install.
4. Do **not** start Pass 3, **8.6.1**, or **9.1**.

---

## P1 — Single-list assisted review

**Status:** DONE  
**Parallel:** SEQUENTIAL — shared `PackAssistedReviewPanel` + Change pack tab  
**Cursor mode:** agent  
**UI skill:** yes

**Read first**

- This section + [Scrutiny](#scrutiny-plan-decisions) + [What already exists](#what-already-exists-do-not-rediscover)
- [`Pack-Import-Intended-Design.md`](Pack-Import-Intended-Design.md) — **Review UI (assisted)** only
- `src/McManager.Hybrid/Components/Shared/PackAssistedReviewPanel.razor`
- `src/McManager.Hybrid/Components/Tabs/ServerManagement/ServerManagementTab.razor` (Change pack pane only)
- `src/McManager.Core/Setup/PackAssistedReview.cs`
- `src/McManager.Core/Setup/PackAssistedReviewActions.cs` (unskip / re-analyze — do not invent a second persist path)
- `src/McManager.Hybrid/wwwroot/css/app.css` (`.mcm-pack-review-*` only)
- UI skills listed in the protocol

**Do**

1. Replace the three scroll regions (**Needs your call** / **Must keep** / **Will skip**) with **one** `ul`. Every jar from those three buckets appears as a row. Checking client-only leaves the row in place.
2. Row UI: left **note** column (Must keep: `required by {name}`; auto-skip may show a short why; unknowns can be empty). Checkbox + filename. Must keep: checkbox **disabled**. Auto-detected client-only: **checked**, enabled. Unknowns: **unchecked**, enabled. Label the control **Client-only** (not “Skip on server”).
3. Keep search when the list is long (existing threshold). Filter the **single** list.
4. Freeze block copy: keep the hard disable; drop “you” / “We” on this panel (Change pack and Setup share the panel — pronoun-free here is OK).
5. Change pack: render `PackIdentityFields` **above** the list when identity confirm is shown. Hide `PackSummary` `<pre>` when `ShowPackAssistedReview` is true. Hide the panel lead / “Review unknown jars” explainer when the list is visible (the list + identity **are** the explanation). Setup may keep a short heading if needed; do not restore three lists there.
6. Prefer pack/analyzer order (`JarRecords` or equivalent) so neighbors stay together. Dedupe by path.
7. Do **not** change freeze rules, skip order, or Layer 3. Do **not** do P2 compactness (drop zone, friends box, side-by-side).
8. Patch [`Guide.md`](Guide.md) **Modded: friends need the client pack** (three-group sentences → one list + client-only checkbox).

**Test**

- `dotnet test` on Core tests that already cover freeze / skip persist (`PackDependencyFreezeTests`, assisted-review action tests if present).
- Hybrid: `MCMANAGER_CONFIG_DIR` = `mcmgr-blank-test`. Drop an unstructured jar-root zip. Confirm identity **above** one list; auto-skips pre-checked; Must keep greyed with “required by …”; toggling a row does **not** move it; uncheck still works. Setup Game step: same list, not three scrolls.

**Done when**

- One list in Setup + Change pack. Identity above the list on Change pack. Summary `<pre>` hidden during assisted review. Guide three-group copy gone. Freeze still blocks force-skip of a required dep.

**Changelog:** 2026-08-27 — One list (Client-only checkbox; must-keep greyed with `required by`; pack order). Identity above the list on Change pack and Setup. Summary `<pre>` hidden during assisted review. Guide three-group copy gone.

---

## P2 — Change pack compactness

**Status:** DONE  
**Parallel:** SEQUENTIAL — same tab + CSS as P1  
**Cursor mode:** agent  
**UI skill:** yes

**Read first**

- This section + Scrutiny
- `src/McManager.Hybrid/Components/Tabs/ServerManagement/ServerManagementTab.razor` (Change pack pane)
- `src/McManager.Core/Setup/PackReplaceUx.cs` (Change pack copy only)
- `src/McManager.Core/Setup/SetupPackImport.cs` — **only** if a string is actually shown on Change pack; prefer `PackReplaceUx` for Manage-only copy
- `src/McManager.Hybrid/ViewModels/ServerManagementViewModel.cs` (copy properties used by the pane)
- `src/McManager.Hybrid/wwwroot/css/app.css` (`.mcm-pack-*`, drop zone, warnings, summary)
- `docs/Guide.md` — **Day-to-day in Manager** Change pack sentences
- UI skills listed in the protocol

**Do**

1. Apply the locked copy in Scrutiny (title gone, friends one-liner, reinstall blurb gone, drop zone title/subtext, skip-warning body).
2. When the summary `<pre>` **is** shown (automatic packs, no assisted list): make it **twice** as tall as today.
3. Audit every other string on this pane that P1 / the operator did not name (confirm checkboxes, wipe, save-compat warning, file-name line, dock status is P3, choose-file hint, Prism hint). Shorten or remove redundant repeats. No “we” / “you”. Keep wipe **irreversible** meaning. Keep friend-pack checkboxes.
4. Compact and **side-by-side** leftover blocks (drop + choose; warnings; checkboxes vs summary) to cut vertical scroll. Identity stays **above** the P1 list.
5. Do **not** restyle Setup. Do **not** overlay the dock (P3). Do **not** change VM running gates (P4).
6. Short Guide patch: Change pack is a compact inner tab; friends one-liner; drop accepts the three formats.

**Test**

- Hybrid `mcmgr-blank-test`. Automatic `.mrpack` / Server Files: summary visible and taller; no assisted list; skip warning short if it appears.
- Unstructured zip: identity above list; no summary `<pre>`; no duplicate explainer box.
- Page is shorter; no stacked empty cards; inner tabs still identify the pane without a second “Change pack” heading.

**Done when**

- Locked copy is in the UI. Remaining Change pack strings are shorter and non-repeating. Side-by-side where Scrutiny allows. Guide updated.

**Changelog:** 2026-08-27 — Locked copy (no inner heading; friends one-liner; reinstall blurb gone; drop title/formats; skip-warning body). Summary twice as tall beside checkboxes. Side-by-side ingest + warnings. Setup copy unchanged.

---

## P3 — Overlay dock + tab-scoped bars

**Status:** DONE  
**Parallel:** SEQUENTIAL — Manage grid / `MainLayout` / dock CSS  
**Cursor mode:** agent

**Read first**

- This section + Scrutiny
- `src/McManager.Hybrid/Components/Layout/MainLayout.razor` (dock + toast host)
- `src/McManager.Hybrid/Components/Layout/ProgressDock.razor`
- `src/McManager.Hybrid/wwwroot/css/app.css` — `.mcm-action-banner` (toast overlay) **and** `.mcm-progress-dock` / `.mcm-app-manage` grid
- `src/McManager.Hybrid/ViewModels/ServerManagementViewModel.cs` (`ShowChangePackUi`, `ShowChangePackDock`)
- `src/McManager.Core/Notifications/ProgressDockUx.cs`
- `src/McManager.Hybrid/Components/Tabs/ServerManagement/ServerManagementTab.razor` (inner `_pane` — lift as needed)

**Do**

1. Make the Change-pack `ProgressDock` an **overlay** on the content pane, matching the toast pattern: it must **not** occupy a grid row or insert a backing box that covers/pushes the tab body. Toasts remain above it (`z-index`).
2. Show the dock **only** when the user is on **Server → Change pack** and a change-pack session is active (`ShowChangePackUi`). Hide on Overview, Whitelist, Console, Usage, Advanced, Troubleshooting, About, and on Server **Identity / World / Modding**. Returning to Change pack shows it again if the session is still open.
3. Lift Server inner pane (and whatever MainLayout needs for the sidebar tab) so dock visibility is not stuck true from keep-alive. Do not cancel the review just because the dock hid.
4. Audit other Manage “always-visible” bars that share this layout-participating pattern. Same overlay + relevant-tab rule. **Do not** change the Console command rail (it is inside the Console pane). **Do not** restyle the Setup wizard footer.
5. Short Guide patch: Install/Cancel overlay the Change pack pane; they do not follow other tabs; toasts still overlay above.

**Test**

- Hybrid: pick a pack → dock overlays the pane, content height unchanged (no extra card under the toast). Switch to Overview / Modding / Identity → dock gone. Back to Change pack → dock back, review intact. Toast + dock together: toast readable, no second backing box.

**Done when**

- Dock overlays like toasts. Tab-scoped. No leftover Manage grid-row card behind it. Guide updated.

**Changelog:** 2026-08-27 — Dock overlays the Change pack pane (no grid-row card). Hidden on other Manage tabs and Server Identity / World / Modding; session stays open. Toasts stay above. Guide updated.

---

## P4 — Pick/review when VM stopped

**Status:** NEXT  
**Parallel:** SEQUENTIAL — same gates + `InstallPackReplaceAsync`  
**Cursor mode:** agent

**Read first**

- This section + Scrutiny
- `src/McManager.Core/Setup/PackReplaceUx.cs`
- `src/McManager.Core.Tests/PackReplaceUxTests.cs`
- `src/McManager.Hybrid/ViewModels/ServerManagementViewModel.cs` — `CanPickPack`, `InstallPackReplaceAsync`, pick/drop early returns
- Existing VM **Start** path used by the sidebar power button (reuse; do not fork a second start stack)
- `docs/Agent-Deploy-Pitfalls.md` — only if the optional live install check SSHs
- `docs/Guide.md` — Change pack “VM must be Running”

**Do**

1. **Pick / drop / analyze / identity / review** do **not** require VM1 running. `CanPick` = not busy (and any other non-VM guards that already exist). Remove `StartFirstMessage` from pick-disabled reasons. Opening **Server → Change pack** (Modding button or inner tab) works while stopped.
2. **Install this pack** is enabled when the pack is ready (confirm checkboxes, identity, freeze) **even if VM1 is stopped**. Clicking it **starts VM1**, waits until the existing start path considers it ready, then runs the current pack-replace SSH flow. Disable idle while that work runs; existing install already turns idle back on when Minecraft starts (`IdleForceEnableNote`).
3. If start fails, do **not** begin replace; surface the start error on the existing toast/banner path. Cancel still aborts the review session.
4. Update `PackReplaceUxTests` (and install-disabled copy). Shorten leftover “start first” strings. No “you” / “we” on new copy.
5. Guide: Change pack can be prepared while stopped; Install starts the game VM then reinstalls.

**Test**

- Unit: `CanPick(false, false)` true; `CanInstall` no longer blocked solely by stopped VM; busy still blocks.
- Hybrid local: with VM stopped, drop a pack, confirm identity/list, see Install enabled (do not have to click Start first).
- Optional TESTING: idle off, Install from stopped VM1, replace succeeds or start failure is clean; idle back as today’s install does. Ask before `tofu`. Do not SoftStop the door.

**Done when**

- Stopped VM can pick and review. Install starts VM1 then replace. Tests + Guide updated. This plan **COMPLETE**. [`NEXT.md`](NEXT.md) → Step **8.5.2** Pass 3 **blocked**.

**Changelog:** *(date when finished)*
