# Blazor Hybrid UI migration plan

**Status:** Living checklist for agents and the operator.  
**Inserted before:** MVP [Phase 7 — Guide + greenfield E2E](MVP-Implementation-Plan.md#phase-7--guide--greenfield-e2e). Do **not** start Phase 7 until this phase is DONE.  
**Parent checklist:** [`MVP-Implementation-Plan.md`](MVP-Implementation-Plan.md) (progress dashboard **NEXT = Step 7.2**; Phase B is **DONE**; Step 7.1 guide is **DONE**).  
**Product intent:** lab [`PRODUCT-IDEAS.md`](../../OCI-mc-server-manager/PRODUCT-IDEAS.md). UI sketches are **not locked**; operator notes override. Do not document layout choices as final or confirmed.  
**Code SoT:** this repo (`OCI-mc-server`). Do **not** put Manager UI in the lab repo.

**Cost rule:** keep OCI spend at **$0** (Always Free–eligible) unless the operator explicitly accepts paid changes.

**OCI API:** follow [`OCI-API-Usage.md`](OCI-API-Usage.md) — **429** exponential backoff (≤60s), lifecycle waiters (≤30s between polls, ~20 min), list pagination, modest Object Storage chatter. Do not poll OCI in 1s loops.

**This file’s creation session must not scaffold code.** Later agents implement **only the step marked NEXT**.

---

## How agents must use this file

1. **Read this file first** (especially [Progress dashboard](#progress-dashboard) and [Agent stop protocol](#agent-stop-protocol)). Also read the MVP plan dashboard so Phase 7 is not started by mistake.  
2. Implement **only the single next incomplete large step** marked **NEXT**.  
3. After finishing that large step:
   - Update **this file** and [`MVP-Implementation-Plan.md`](MVP-Implementation-Plan.md): mark the step **DONE**, set the following step to **NEXT**, note date + short notes in the changelog.
   - **Stop.** Do not start the next large step in the same session unless the operator explicitly says to continue.
4. In the chat reply to the operator:
   - Summarize **what was just done**
   - List **how to test** it
   - State **what the next step will be**
   - **Ask** whether to continue, pause, or adjust
5. **Never create git commits** (operator commits in Visual Studio). Suggest the commit message listed on the step.
6. Do **not** `tofu apply` / `tofu plan` / `tofu destroy` against any tenancy, do **not** `docker push` / `fn push` to OCIR, and do **not** SSH-bootstrap live VMs. Allowed: `dotnet build`, `tofu validate` in `infra/`, Setup Deploy only with `MCMANAGER_TOFU_DRY_RUN=1`.
7. Do **not** put Manager UI in the lab repo. Do **not** rewrite `McManager.Core` (OCI/SDK, usage math, backups, Setup apply, infra, config contracts).
8. **UI is not locked.** Copy the HTML mockup as a look/hierarchy reference; keep iterating. Operator notes override PRODUCT-IDEAS sketches.

### Agent stop protocol

Between **large steps** (B0–B13 headings), always stop for operator feedback.  
**Small sub-bullets** inside one large step may be completed together in one session if they are required to make that step testable.

If blocked (missing WebView2 workload, unclear UX, cost risk), stop and ask.

### Operator prompt (copy-paste for a new agent)

```text
Read docs/MVP-Implementation-Plan.md in OCI-mc-server. Implement only the step marked NEXT.
Phase B (Blazor Hybrid UI) is DONE — do not re-open Avalonia or this migration checklist except as archive.
When done: update the plan statuses, stop, tell me what you did, how to test, what’s next, and ask if I want to continue or adjust.
Do not commit. Do not start the following large step unless I say so.
Do not tofu apply / OCIR push / live SSH bootstrap.
Prompt sequential steps in Agent mode (not Plan mode). Use Build in Parallel / Plan mode only if the NEXT step is marked PARALLEL-OK. Phase 7 steps are SEQUENTIAL — prompt in Agent mode. Include this same Agent-vs-Plan instruction in the prompt you give the operator for the following step.
```

### Build in Parallel / worktrees / Best-of-N

**Do not** turn Build in Parallel on for this whole phase.

| When | What |
|------|------|
| B0–B5, B10–B13 | **SEQUENTIAL.** One new agent chat per step. No Build in Parallel. |
| B6–B9 | **PARALLEL-OK** only after B0–B5 are **DONE**. Parent chat may dispatch those four tab ports together. |
| B2 | Optional **Best-of-N** on the visual shell only (match [`mc-manager-ui-mockup.html`](mc-manager-ui-mockup.html) chrome + light tokens). Not for Core or Setup apply. |

Parallel agents **must not** edit:

- `src/McManager.Hybrid/App.xaml.cs` (or `Program.cs` if used)
- `MainWindow.xaml` / `MainWindow.xaml.cs`
- `Components/App.razor`
- `Components/Layout/MainLayout.razor`
- `wwwroot/css/**`
- `McManager.Hybrid.csproj`
- `src/McManager.slnx`

They own **only** the folders listed on their step and leave a **DI snippet** in the step changelog. A tiny sequential wire (parent agent or a follow-up chat) pastes those into the WPF service collection.

If two PARALLEL-OK agents would collide on the same working tree, the operator uses **git worktrees** (`/worktree`). Agents still **never commit**. Operator `/apply-worktree` and commits in Visual Studio.

---

## Host decision (locked for implementers)

**Pick: WPF + `BlazorWebView` (`Microsoft.AspNetCore.Components.WebView.Wpf`).** One Windows **WinExe**. In-process .NET talking to existing C#. **Not** Blazor Server, **not** hosting in a browser, **not** “open localhost” as the product UX. Stay on **net8.0-windows**.

Why this, not the others:

- **WPF vs WinForms:** Same WebView2 + same C# DI. Microsoft’s canonical tutorial is WPF ([Build a WPF Blazor app](https://learn.microsoft.com/en-us/aspnet/core/blazor/hybrid/tutorials/wpf)). Native OS caption (min/max/close) avoids the Avalonia custom title-bar bugs. DPI is slightly better than WinForms. UI is 100% Razor; WPF is a one-control host window.
- **Photino.Blazor:** Extra abstraction, community-maintained Blazor fork, unused cross-platform. Official `TryPhotino.VSCode.Project.Templates` short names are `photinoapp` / Angular / React / Vue — **no first-party `photinoblazor` ID**. File pickers/clipboard are less documented for this product.
- **MAUI Blazor Hybrid:** Rejected. This product is Windows-only; MAUI workloads add agent friction. Do not pick MAUI because it is a Microsoft template.
- **Blazor Server / WASM-in-browser:** Rejected (desktop-app constraint).

**Operator Visual Studio wizard: not required.** Agents scaffold unattended:

```text
dotnet new wpf -n McManager.Hybrid -o src/McManager.Hybrid -f net8.0
```

Then **hand-edit** (Microsoft Learn; do **not** install `VijayAnand.BlazorTemplates` / `wpf-blazor` unless official `dotnet new wpf` fails):

- SDK → `Microsoft.NET.Sdk.Razor`
- Override TFM to `net8.0-windows` (`UseWPF` true). Repo [`src/Directory.Build.props`](../src/Directory.Build.props) sets `net8.0` — the Hybrid csproj **must** override.
- NuGet: `Microsoft.AspNetCore.Components.WebView.Wpf`, `Microsoft.Extensions.DependencyInjection` (add `CommunityToolkit.Mvvm` when ViewModels move)
- `ProjectReference` → `McManager.Core`
- Add the project to [`src/McManager.slnx`](../src/McManager.slnx)
- Files from the Learn tutorial: `_Imports.razor`, `wwwroot/index.html` with `blazor.webview.js` (no Bootstrap), `App.razor`, `MainWindow.xaml` hosting `BlazorWebView` filling the window, `AddWpfBlazorWebView()` in code-behind or `App.xaml.cs`

Do **not** bump to net10 (Learn’s `WebView2CompositionControl` TFM) unless a later step proves net8 WebView2 is broken.

**Fallback operator gate (only if `dotnet new wpf` is missing):** install Visual Studio workload **“.NET desktop development”**. Template name if they must click: **WPF Application** (C#), location `OCI-mc-server/src/McManager.Hybrid`, then agents continue (Razor SDK, WebView package, Core reference). Not a Blazor-hybrid wizard.

Avalonia `McManager.App` was removed at **B13**. The only WinExe is `McManager.Hybrid` (not renamed).

```text
Friends' PCs  →  (unchanged cloud stack)
Admin PC      →  McManager.Hybrid WinExe
                 WPF Window (native OS chrome)
                   BlazorWebView (Evergreen WebView2)
                     Razor / HTML / CSS  →  McManager.Core (unchanged)
                     IUiDialogs / IFilePicker / IClipboard / IUiClock
```

---

## Hard constraints (the phase is invalid if it violates these)

1. **Desktop app, not a website.** One Windows WinExe. Friends do not use this. The admin PC keeps: `%USERPROFILE%\.oci`, SSH keys, gitignored `data/config.local.json`, native file pickers, Setup/OpenTofu on the admin PC.
2. **`McManager.Core` stays.** Do not rewrite OCI/SDK, usage math, backups, Setup apply, infra, or config contracts. No WebView/WPF types in Core.
3. **Feature parity** with the current manage + Setup app, not a mockup subset. Tabs stay: **Whitelist**, **Usage**, **Server Management**, **Advanced / Danger Zone** (one tab), **Troubleshooting**. Do not split Advanced vs Danger Zone; do not add a sidebar unless the operator later asks. First-run, Setup wizard (9 steps), Connect-existing / Auto-detect, break-glass, troubleshooting one-shots, world backup upload/download/replace — all in scope. The HTML mockup omitted some of these; that does **not** drop them.
4. **Visual reference:** [`mc-manager-ui-mockup.html`](mc-manager-ui-mockup.html) is the look to copy (status cluster, power buttons, pinned stats, whitelist Add-IP popup + hover row actions, Server Management four info cards + backup rows, section cards, tabs). Mockup is dark; ship a **light warm-gray** theme from B2 (remap CSS variables). Novice Status = **Running/Stopped** only; technical VM/door status on Advanced. Players = `—` when Minecraft is off. Power buttons that would not work are greyed and **must not hover-react**. Save-type buttons disabled until a change would actually push. Pinned stat cards must **not** grow with window width; mini-bars follow stat-text width. Rollover bank = unused hours from earlier days this month (Core `UsageMath` leftover), **not** hours left in the month (that remaining figure belongs on Usage).
5. **Mockup extras that are out of MVP** (do not port as features): bell/notification center, “update available”, hamburger About/Donate, “Add IP range” (v1 CIDR), mockup tab order. Keep **current product tab order**. Native OS chrome (no custom caption buttons unless a later operator ask).
6. **WebView2 Evergreen** (Win10/11 usually has it). First-run: if missing, show a **WPF** MessageBox with the Evergreen installer link (`https://go.microsoft.com/fwlink/p/?LinkId=2124703`). Do not bundle or pay for a runtime. Installer packaging is **Phase 8**, not this migration.
7. Prefer Blazor `@onclick` over JS interop. Native `OpenFileDialog` / `SaveFileDialog` for pickers (not HTML `<input type=file>`). Confirm/info/friend-edit as **Razor modals** matching mockup overlay CSS.
8. Self-host fonts/icons in `wwwroot` (Inter, JetBrains Mono, Tabler). No CDN at runtime. Do **not** add Bootstrap from the Learn sample (fights mockup CSS). Do **not** reference Avalonia / Semi / Material.Icons.Avalonia in Hybrid.

---

## Historical Avalonia surface (parity checklist)

Ported into Hybrid during B5–B12. The Avalonia tree (`src/McManager.App/`) was **removed at B13**. Table kept as archive:

| Area | Files | Avalonia glue to strip |
|------|--------|------------------------|
| Manage shell | `Views/MainWindow.axaml`, `ViewModels/MainViewModel.cs` | `DispatcherTimer` (door **15s** focused / **2 min** background; OCI **30s**); clipboard; toast |
| Tabs | Whitelist, Usage, ServerManagement, Advanced, Troubleshooting | `Application.Current` for dialogs; Usage 2 min poll |
| First-run | `FirstRunWindow`, `ConnectExistingFlow`, `StackChooserDialog` | `StorageProvider` for SSH key browse |
| Setup | `SetupWizardWindow`, `SetupWizardViewModel` (9 steps: Always Free → OCI → compartment → email → SSH → game → EULA → Auth Token → summary) | `Window? Host`, pickers, clipboard, log flush timer, capacity 5 min timer |
| Dialogs | Confirm, friend edit, info, capacity wait | `ShowDialog` |
| Local config | [`Local-Config.md`](Local-Config.md), gitignored `data/config.local.json` | Same Core `LocalConfigStore` / `MCMANAGER_CONFIG_DIR` |

Pinned usage: wall-clock hours; rollover = leftover from earlier days this month (`UsageMath`), not remaining-in-month.

Troubleshooting one-shots already in Avalonia (must all return): Park play IP, Diagnose wait_forge, Reset door state, Unstick after game is up, OS budget refresh, Heal open ledger (VM1 STOPPED only), Idle status, Force-enable idle, Re-apply netplan, Diagnose Minecraft CHDIR, Repair permissions, Copy result log.

---

## Progress dashboard

| Step | Focus | Kind | Status |
|------|--------|------|--------|
| **B0** | Docs / agent rules (stop writing Avalonia) | SEQUENTIAL | **DONE** |
| **B1** | WPF WinExe + BlazorWebView + Core | SEQUENTIAL | **DONE** |
| **B2** | Design tokens + layout shell (placeholders) | SEQUENTIAL | **DONE** |
| **B3** | UI-agnostic dialogs / pickers / clipboard / timer | SEQUENTIAL | **DONE** |
| **B4** | Spike: load local config / show play IP | SEQUENTIAL | **DONE** |
| **B5** | Manage chrome live (status, power, pins, poll) | SEQUENTIAL | **DONE** |
| **B6** | Whitelist tab | PARALLEL-OK after B5 | **DONE** |
| **B7** | Usage tab | PARALLEL-OK after B5 | **DONE** |
| **B8** | Server Management tab | PARALLEL-OK after B5 | **DONE** |
| **B9** | Troubleshooting tab | PARALLEL-OK after B5 | **DONE** |
| **B10** | Advanced / Danger Zone tab | SEQUENTIAL | **DONE** |
| **B11** | First-run + Connect-existing | SEQUENTIAL | **DONE** |
| **B12** | Setup wizard | SEQUENTIAL | **DONE** |
| **B13** | Cutover: remove Avalonia WinExe | SEQUENTIAL | **DONE** |

**Current NEXT step:** none in this file. Phase B is **DONE**. Return to [`MVP-Implementation-Plan.md`](MVP-Implementation-Plan.md) **Phase 7** (TODO — do not start in the cutover session).

**B0–B13 are DONE.** One WinExe: `McManager.Hybrid`. Avalonia `McManager.App` removed from the slnx and deleted. Manage + Setup live (dry-run Deploy; no live `tofu apply`).

**End state:** Avalonia project removed; one Blazor Hybrid WinExe; manage + Setup usable; `dotnet build` clean. MVP Phase 7 stays TODO.

---

## Sequential foundation (must finish before any PARALLEL-OK)

### B0 — Docs and agent rules (stack decision)

**Status:** DONE  
**Kind:** SEQUENTIAL  
**Depends on:** this file existing (DONE 2026-08-15)

**Goal:** Later agents stop scaffolding Avalonia. Historical “Go + Wails replaced by Avalonia” stays historical; add a **new** changelog line: Avalonia UI vehicle replaced by .NET + Blazor Hybrid (WPF WebView2) before Phase 7.

**Do**

- Lab [`PRODUCT-IDEAS.md`](../../OCI-mc-server-manager/PRODUCT-IDEAS.md): desktop-stack line currently “.NET + Avalonia” → .NET + Blazor Hybrid (WPF + WebView2). Changelog + “Old / superseded” row for Avalonia-as-UI-vehicle (keep the Wails→Avalonia row as history).
- Lab + product `AGENTS.md` and `.cursor/rules` that say Avalonia-only / “NEXT = Phase 7”:
  - product [`AGENTS.md`](../AGENTS.md)
  - lab [`AGENTS.md`](../../OCI-mc-server-manager/AGENTS.md)
  - [`.cursor/rules/oci-mc-server-product.mdc`](../.cursor/rules/oci-mc-server-product.mdc)
  - lab [`.cursor/rules/oci-minecraft-context.mdc`](../../OCI-mc-server-manager/.cursor/rules/oci-minecraft-context.mdc)
- Product [`README.md`](../README.md) stack sentence.
- Lab [`docs/Development-Steps.md`](../../OCI-mc-server-manager/docs/Development-Steps.md) and [`docs/VM-Software.md`](../../OCI-mc-server-manager/docs/VM-Software.md) only as needed for “what is built” / suggested order (Phase B before Guide/E2E).
- [`Local-Config.md`](Local-Config.md) “Avalonia” wording → Manager app (same JSON; not a schema change).
- NuGet guidance: UI packages go on **Hybrid** (CSS/fonts/icons), not Avalonia themes. OCI SDK stays on Core. Prefer OSS; ask before paid/commercial.
- Do **not** mark UI choices as final/confirmed.

**File ownership (this step only)**

- Owns: the markdown / rules listed above.
- Does **not** own: any `.cs` / `.razor` / `.csproj` / `slnx`.

**Test**

- Grep both repos for leftover **current** instructions that say “implement Avalonia” or “NEXT = Phase 7” without Phase B. Historical Phase 1 bodies in the MVP plan may stay frozen.

**Done when:** A new agent reading `AGENTS.md` would build Blazor Hybrid in `OCI-mc-server`, not Avalonia in the lab, and would not start Phase 7.

**Following step:** B1

**Suggested commit message:** `docs: retarget Manager UI from Avalonia to Blazor Hybrid (WPF WebView2)`

**Changelog:** 2026-08-15 — **DONE.** Retargeted PRODUCT-IDEAS, both AGENTS.md, product + lab `.cursor/rules` (incl. infrastructure-docs), product + lab README, Local-Config, Development-Steps, VM-Software; banner on `mc-manager-design-spec.md` (not an Avalonia brief; UI not locked). Historical Wails→Avalonia row kept; new superseded row + changelog for Avalonia→Blazor Hybrid. **NEXT = B1.** Do not scaffold Hybrid in this session. Do not start Phase 7.

---

### B1 — Host WinExe + WebView2 + Core

**Status:** DONE  
**Kind:** SEQUENTIAL  
**Depends on:** B0

**Goal:** `McManager.Hybrid` WinExe builds, a native window opens, Blazor root renders, references `McManager.Core`, `dotnet build src/McManager.slnx` is clean. Missing-WebView2 shows a WPF MessageBox. Avalonia `McManager.App` stays in the slnx.

**Do**

- `dotnet new wpf` as documented in [Host decision](#host-decision-locked-for-implementers); hand-edit Razor SDK, `net8.0-windows`, packages, Core reference, slnx entry.
- Root component hello (or “Loading…” then a Razor component). Catch WebView2 runtime missing (COM / WebView2 exception) → MessageBox + Evergreen link. Do not bundle the runtime.
- Keep `McManager.App` (Avalonia) buildable. Do not delete it.

**File ownership**

- Owns: `src/McManager.Hybrid/**` (new), `src/McManager.slnx` **only** to add the project.
- Does **not** own: Razor tab bodies, Core, Avalonia App sources.

**Test**

- `dotnet build src/McManager.slnx`
- `dotnet run --project src/McManager.Hybrid` — native window, **no** browser, **no** localhost as the UX.
- Confirm Avalonia still runs: `dotnet run --project src/McManager.App`

**Done when:** Hybrid window opens with a Razor root; solution builds; WebView2-missing path is documented in code or a short comment near the catch.

**Following step:** B2

**Suggested commit message:** `feat: add WPF BlazorWebView host referencing McManager.Core`

**Changelog:** 2026-08-15 — **DONE.** Scaffolded `McManager.Hybrid` (`dotnet new wpf`, Razor SDK, `net8.0-windows`, `Microsoft.AspNetCore.Components.WebView.Wpf` 8.0.100). Native WPF window hosts `BlazorWebView` + `Components/App.razor` hello (Core `LocalConfigStore` type referenced; no OCI/config load). Missing Evergreen WebView2 → WPF MessageBox + installer link (`WebView2RuntimeGuard`). Avalonia `McManager.App` still in slnx and builds. **NEXT = B2.** Do not start B2 in this session. Do not start Phase 7.

---

### B2 — Design tokens + layout shell (placeholders)

**Status:** DONE  
**Kind:** SEQUENTIAL (optional Best-of-N on this step only)  
**Depends on:** B1

**Goal:** Copy mockup chrome: title row, status cluster, power buttons, four pinned stats, tab strip with **five product tabs** (Whitelist, Usage, Server Management, Advanced / Danger Zone, Troubleshooting — **current product order**), placeholder tab bodies. Light warm-gray CSS variables (do not ship the mockup’s dark page as the product). Pinned cards fixed width (must not stretch with the window). Disabled button CSS: no hover fill. Native WPF chrome (no custom caption).

**Do**

- Port structure from [`mc-manager-ui-mockup.html`](mc-manager-ui-mockup.html) into `wwwroot/css` + `MainLayout.razor`.
- Self-host Inter, JetBrains Mono, Tabler icons under `wwwroot` (no CDN).
- Placeholder strings (`—`) for status / IP / players / pins. Power buttons visually disabled.
- Do **not** wire Core, ViewModels, or polling.

**File ownership**

- Owns: `wwwroot/css/**`, `wwwroot/index.html`, `wwwroot` font/icon assets, `Components/Layout/MainLayout.razor`, `Components/App.razor`, placeholder tab Razor if needed.
- Does **not** own: Core calls, ViewModels, DI beyond RootComponents.

**Test**

- Overlay mentally against the mockup: same hierarchy, **light** theme, tabs switch.
- Resize the window — pinned cards do not grow; mini-bars stay with stat-text width.
- Disabled Start/Stop look inert (no hover flash).

**Done when:** Operator can recognize the mockup layout in a light warm-gray WinExe with empty tab bodies.

**Following step:** B3

**Suggested commit message:** `feat: port Manager chrome from HTML mockup to Blazor layout (light theme)`

**Changelog:** 2026-08-15 — **DONE.** Ported mockup chrome into `MainLayout.razor` + light warm-gray tokens (`wwwroot/css/app.css`). Self-hosted Inter, JetBrains Mono, Tabler webfont under `wwwroot/fonts` (no CDN). Status/IP/players/pins are `—`; Start/Stop/Restart disabled with no hover fill. Native WPF caption. Five tabs in product order with placeholder bodies. No Core/ViewModel/poll wiring. **NEXT = B3.** Do not start B3 in this session. Do not start Phase 7.

---

### B3 — UI-agnostic host services

**Status:** DONE  
**Kind:** SEQUENTIAL  
**Depends on:** B2

**Goal:** Interfaces with **no** WPF/WebView/Avalonia types so ViewModels are not host-specific and Core is not infected with WebView types: `IUiDialogs`, `IFilePicker`, `IClipboard`, `IUiClock` (or `PeriodicTimer` + `IUiDispatcher`). WPF implementations for pickers/clipboard/clock (STA). Razor modal host for confirm / info / chooser (mockup overlay CSS).

**Do**

- Put interfaces in `src/McManager.Hybrid/Ui/` (not Core).
- Throwaway shell buttons to prove: confirm modal, copy clipboard, native file picker.
- Remove the throwaway buttons at the end of the step **or** hide them behind DEBUG — do not ship them into later tabs as product chrome.

**File ownership**

- Owns: `src/McManager.Hybrid/Ui/**`, WPF picker/clipboard/dispatcher classes, `ModalHost.razor` (or equivalent).
- Does **not** own: tab Razor, Core.

**Test**

- Those three actions work. Grep new files: no `Avalonia` usings, no `Microsoft.AspNetCore.Components.WebView` on the interfaces.

**Done when:** ViewModels that move later can depend only on `Ui/` interfaces.

**Following step:** B4

**Suggested commit message:** `feat: add UI-agnostic dialogs, file pickers, clipboard, and timers`

**Changelog:** 2026-08-15 — **DONE.** `Ui/` interfaces (`IUiDialogs`, `IFilePicker`, `IClipboard`, `IUiClock`, `IUiDispatcher`) with no WPF/WebView types. WPF STA pickers/clipboard/clock/dispatcher; Razor `ModalHost` confirm/info/chooser (mockup overlay CSS, light theme). DEBUG-only probe bar (confirm / clipboard / native picker). DI in `App.xaml.cs`: `WpfUiDispatcher`, `WpfUiClock`, `WpfClipboard`, `WpfFilePicker`, `UiDialogs` as `IUiDialogs`. **NEXT = B4.** Do not start B4 in this session. Do not start Phase 7.

---

### B4 — Spike: one real Core call (runnable checkpoint)

**Status:** DONE  
**Kind:** SEQUENTIAL  
**Depends on:** B3

**Goal:** Shell shows **play IP** (or “—” / config error) from `LocalConfigStore` + `ManagerLocalConfig` play reserved public IP. If no manage config, show a **stub** first-run chooser page (full first-run is B11) instead of crashing. **Not** a big-bang rewrite.

**Do**

- Same config discovery as Core (`MCMANAGER_CONFIG_DIR`, repo `data/`). See [`Local-Config.md`](Local-Config.md).
- No OCI probe on launch. No power/poll yet.

**File ownership**

- Owns: Hybrid `App.xaml.cs` DI for config load, a small shell status component or MainLayout `@code` for play IP / stub first-run.
- Does **not** own: OCI polling, power buttons, tab bodies.

**Test**

- With existing `data/config.local.json`: play IP matches Avalonia.
- `MCMANAGER_CONFIG_DIR` pointing at an empty directory: stub first-run, no OCI calls.

**Done when:** Operator can run Hybrid and see the reserved play IP in the status cluster.

**Following step:** B5

**Suggested commit message:** `feat: Blazor host loads local config and shows play IP`

**Changelog:** 2026-08-15 — **DONE.** Hybrid `LocalConfigHost` loads `LocalConfigStore` / `ManagerLocalConfig` at WPF startup (same `MCMANAGER_CONFIG_DIR` + repo `data/` discovery as Core). Status cluster shows `play.reserved_public_ip` (or `—`). Missing/unreadable manage config → stub first-run chooser (`FirstRunStub`; buttons disabled; no OCI). No power/poll. Avalonia App still in slnx. **NEXT = B5.** Do not start B5 in this session. Do not start Phase 7.

---

## Feature ports (manage, then Setup)

### B5 — Manage chrome live (status, power, pins, poll, toast)

**Status:** DONE  
**Kind:** SEQUENTIAL (touches MainLayout + DI + MainViewModel)  
**Depends on:** B4

**Goal:** Port [`MainViewModel.cs`](../src/McManager.App/ViewModels/MainViewModel.cs) off Avalonia: novice Running/Stopped, Players `—` when the game is off, door-aware Start/Stop, SSH Restart, pinned hours via existing `PinnedUsageSnapshot` / `UsageMath` leftover bank, toast, copy IP, poll door ~15s focused / 2 min background and OCI ~30s, 429/waiters unchanged. Grey power buttons with **no hover** when `CanStart` / `CanStop` / `CanRestart` is false. Tab Object Storage polls must **not** disable power buttons.

**Do**

- Copy ViewModel into Hybrid and strip Avalonia; use B3 interfaces + `IUiClock`.
- Wire Door/Compute/`OciSession` in the WPF service collection.

**File ownership**

- Owns: `ViewModels/MainViewModel.cs` (Hybrid copy), `Components/Layout/**` status/pins/power, `App.xaml.cs` DI for Door/Compute/OciSession.
- Does **not** own: tab folders (B6–B10).

**Test**

- Copy IP; Start from idle; Stop; unfocused poll slows; pins match Avalonia Usage math (rollover = unused earlier-day hours).
- Do not leave VM1 running accidentally after tests (cost).

**Done when:** Operator can wake/stop/restart from Hybrid on the live stack (same caveats as Avalonia).

**Following step:** B6–B9 may start (PARALLEL-OK among themselves). B10 stays sequential after those (or after B5 if tabs are not started yet).

**Suggested commit message:** `feat: port manage status, power, and pinned usage to Blazor`

**Changelog:** 2026-08-15 — **DONE.** Hybrid `MainViewModel` (no Avalonia): novice Running/Stopped, Players `—` when off, door-aware Start/Stop, SSH Restart, pinned hours via `PinnedUsageSnapshot` / `UsageMath` leftover bank, toast, copy IP, door ~15s focused / 2 min background + OCI ~30s (`IUiClock`). Grey power buttons with no hover when `CanStart`/`CanStop`/`CanRestart` is false. Pin Object Storage warmup does not disable power. DI: `OciSession` / `IComputeService` / `IDoorClient` / `UsageBudgetStore` via `ManageCloudServices` in `App.xaml.cs`. Avalonia App still in slnx. **NEXT = B6** (B6–B9 PARALLEL-OK among themselves; B10 sequential). Do not start B6–B9 in this session. Do not start Phase 7.

---

### B6 — Whitelist tab

**Status:** DONE  
**Kind:** PARALLEL-OK (after B5)  
**Depends on:** B5

**Goal:** CRUD + Security List sync matching Avalonia Step 1.3. Add-IP **popup** (mockup), hover row actions, Save disabled until dirty. **No** “Add IP range” (v1).

**File ownership**

- Owns **only:** `Components/Tabs/Whitelist/**`, `ViewModels/WhitelistViewModel.cs`, `ViewModels/FriendRowViewModel.cs`.
- Leave a **DI snippet** in the changelog for the parent to paste. Do not edit `App.xaml.cs` / layout / CSS / csproj.

**Test**

- Add/remove friend IP; confirm Security List ingress in Console; non-managed rules (ICMP, VCN, etc.) survive.

**Done when:** Operator can maintain the private allowlist from Hybrid without Python Manager / Console for normal edits.

**Following step:** B10 after B6–B9 siblings (or continue siblings).

**Suggested commit message:** `feat: port Whitelist tab to Blazor`

**Changelog:** 2026-08-15 — **DONE.** Hybrid Whitelist: friends CRUD + `LocalConfigStore.SaveFriends` + `SecurityListService.ApplyFriendsAsync` (shared `OciSession`, not disposed). Add-IP / Update Razor overlay (mockup; Update gated until dirty). Hover row actions. Save disabled until dirty. Your IP changed? Detect/Update my IP. No Add IP range. Avalonia App still in slnx. **Did not edit** `App.xaml.cs` / `MainLayout.razor` / CSS / csproj. Tab is not visible until parent pastes the snippets below. **NEXT = B7 / B8 / B9** (PARALLEL-OK remaining; B10 sequential after those). Do not start B7–B9 in this session. Do not start Phase 7.

Parent-paste **DI** (`RegisterManageServices` in `App.xaml.cs`, after `MainViewModel`):

```csharp
services.AddSingleton<WhitelistViewModel>();
```

Parent-paste **layout** (`MainLayout.razor`): add `@using McManager.Hybrid.Components.Tabs.Whitelist` and replace the whitelist `PlaceholderTab` with `<WhitelistTab />`.

---

### B7 — Usage tab

**Status:** DONE  
**Kind:** PARALLEL-OK (after B5)  
**Depends on:** B5

**Goal:** Dashboard/edit/publish matching Avalonia Step 1.5. Poll ~2 minutes while the tab is selected. Save gated on dirty. Remaining-in-month figure stays on **this** tab, not the rollover pin.

**File ownership**

- Owns **only:** `Components/Tabs/Usage/**`, `ViewModels/UsageViewModel.cs`.
- Leave a **DI snippet** in the changelog for the parent to paste. Do not edit `App.xaml.cs` / layout / CSS / csproj.

**Test**

- Refresh matches Avalonia Usage tab / bucket objects; Save dirties door/vm1 flags as designed.

**Done when:** Usage/budget day-2 works from Hybrid.

**Suggested commit message:** `feat: port Usage tab to Blazor`

**Changelog:** 2026-08-15 — **DONE.** Hybrid Usage: Avalonia Step 1.5 dashboard (month, targets, soft caps, used MTD, **Hours left this month** / `RemainingDisplay`, avg/day, rollover bank, today vs daily, soft-cap hit) + all budget edit fields (monthly OCPU/GB, soft caps, idle timeout, budget warn, shape OCPUs/memory, idle-agent checkbox). Save/Publish gated on dirty && !busy && store present; confirm via `IUiDialogs` (“Save usage budget?” / “Publish”). ~2 min poll via `IUiClock.CreatePeriodicTimer` while the tab component is alive; Refresh on `OnInitialized`; timer stopped on `Dispose`. Pins copied from `PinnedUsageSnapshot.FromReport` onto `MainViewModel` after refresh (remaining-in-month stays on this tab, not the rollover pin). Local fallback when Object Storage is missing. Own `IsBusy` only (does not grey Start/Stop/Restart; does not dispose `OciSession`). Avalonia App still in slnx. **Did not edit** `App.xaml.cs` / `MainLayout.razor` / CSS / csproj. Tab is not visible until parent pastes the snippets below.

Parent-paste **DI** (`RegisterManageServices` in `App.xaml.cs`, after `MainViewModel`):

```csharp
services.AddSingleton<UsageViewModel>();
```

Parent-paste **layout** (`MainLayout.razor`): add `@using McManager.Hybrid.Components.Tabs.Usage` and replace the usage `PlaceholderTab` with `<UsageTab />`.


---

### B8 — Server Management tab

**Status:** DONE  
**Kind:** PARALLEL-OK (after B5)  
**Depends on:** B5

**Goal:** Match Avalonia Step 1.6. Four info cards + backup rows + hover actions (mockup). Native pickers via `IFilePicker`. List/download/upload-replace; SSH replace when VM1 RUNNING. Soft-cap messaging. No Wipe world / Modding (v1).

**File ownership**

- Owns **only:** `Components/Tabs/ServerManagement/**`, `ViewModels/ServerManagementViewModel.cs`.
- Leave a **DI snippet** in the changelog for the parent to paste. Do not edit `App.xaml.cs` / layout / CSS / csproj.

**Test**

- List matches bucket; download to an operator-chosen directory; small test zip upload if needed.

**Done when:** Backups tab is usable for restore/replace via Object Storage (and SSH replace when RUNNING).

**Following step:** B10 after B6–B9 siblings (or continue siblings).

**Suggested commit message:** `feat: port Server Management backups to Blazor`

**Changelog:** 2026-08-15 — **DONE.** Hybrid Server Management: four info cards (name / Minecraft version from `InfraMetaStore` / last backup / `BackupStorageDisplay`); list + hover-row Download; native `IFilePicker` save/open (`.zip`); upload-replace confirm; SSH `ReplaceWorldAsync` when `MainViewModel.Vm1Lifecycle` is RUNNING; `EvaluateUpload` + `FormatSoftCapLine`. No Wipe world / Modding / mockup Delete. Own `IsBusy` only (does not grey Start/Stop/Restart; does not dispose `OciSession`). Avalonia App still in slnx. **Did not edit** `App.xaml.cs` / `MainLayout.razor` / CSS / csproj. Tab is not visible until parent pastes the snippets below.

Parent-paste **DI** (`RegisterManageServices` in `App.xaml.cs`, after `MainViewModel`):

```csharp
services.AddSingleton<ServerManagementViewModel>();
```

Parent-paste **layout** (`MainLayout.razor`): add `@using McManager.Hybrid.Components.Tabs.ServerManagement` and replace the server `PlaceholderTab` with `<ServerManagementTab />`.


---

### B9 — Troubleshooting tab

**Status:** DONE  
**Kind:** PARALLEL-OK (after B5)  
**Depends on:** B5

**Goal:** Match Avalonia Step 4.4. All current one-shots (see [parity checklist](#current-avalonia-surface-to-port-parity-checklist)). Confirm-gated. Result log + copy. Dedicated **Troubleshooting** tab (not merged into Advanced).

**File ownership**

- Owns **only:** `Components/Tabs/Troubleshooting/**`, `ViewModels/TroubleshootingViewModel.cs`.
- DI snippet for parent.

**Test**

- Copy log; Park IP confirm **Cancel** does nothing. Operator can recover a stuck reserved IP without Console when they choose to run a live one-shot.

**Done when:** One-shots are reachable from Hybrid with the same confirms as Avalonia.

**Suggested commit message:** `feat: port Troubleshooting one-shots to Blazor`

**Changelog:** 2026-08-15 — **DONE.** Hybrid Troubleshooting tab (dedicated; not merged into Advanced). Core `TroubleshootingService` from `LocalConfigHost` + `cloud.Ssh` / `Compute` / `Door` (shared session, not disposed). All Step 4.4 one-shots with Avalonia confirm gating (Park IP, netplan, reset, unstick, OS budget, heal, force-enable idle, repair permissions YES; diagnose wait_forge, idle status, CHDIR, copy log NO). Confirm Cancel no-ops. Result log trim 80k/60k; Copy via `IClipboard`. OS-ISSUE-5 Console copy only (no Manager button). Own `IsBusy` only — does not grey Start/Stop/Restart. Avalonia App still in slnx. **Did not edit** `App.xaml.cs` / `MainLayout.razor` / CSS / csproj. Tab is not visible until parent pastes the snippets below. Do not start B10–B13 in this session. Do not start Phase 7.

Parent-paste **DI** (`RegisterManageServices` in `App.xaml.cs`, after `MainViewModel`):

```csharp
services.AddSingleton<TroubleshootingViewModel>();
```

Parent-paste **layout** (`MainLayout.razor`): add `@using McManager.Hybrid.Components.Tabs.Troubleshooting` and replace the troubleshooting `PlaceholderTab` with `<TroubleshootingTab />`.

---

### B10 — Advanced / Danger Zone tab

**Status:** DONE  
**Kind:** SEQUENTIAL (do not parallelize with B11 or B12)  
**Depends on:** B5 (B6–B9 preferred first so the tab strip is real)

**Goal:** Match Avalonia Advanced tab. Technical VM/door status, break-glass Compute, idle timeout/enable/disable (OS-ISSUE-7 copy: boot force-enables idle), Publish/Refresh infra meta, Auto-detect button, Deploy/repair → Setup. Save gated on dirty. Still **one** combined Advanced / Danger Zone tab.

**File ownership**

- Owns: `Components/Tabs/Advanced/**`, `ViewModels/AdvancedViewModel.cs`, DI registration for this tab (sequential, so editing `App.xaml.cs` is allowed here).
- Does **not** own: Setup wizard pages (B12). Opening Setup may navigate to a stub until B12.

**Test**

- Meta refresh; idle save; opening Setup does **not** `tofu apply`.

**Done when:** Advanced/Danger Zone is usable from Hybrid minus living inside the wizard window.

**Following step:** B11

**Suggested commit message:** `feat: port Advanced / Danger Zone tab to Blazor`

**Changelog:** 2026-08-15 — **DONE.** Hybrid Advanced / Danger Zone (one combined tab): technical VM1/door VM/door-service status from MainViewModel + door GetInstance; break-glass Compute Start/SoftStop (no IP move); idle timeout/warn/enable with OS-ISSUE-7 disable confirm (boot force-enables); dirty-gated idle Save + infra meta Refresh/Publish; Auto-detect via Core `ConnectExistingService` + `IUiDialogs` / native SSH picker (restart after connect); Deploy/repair → Setup stub (no tofu apply). Own `IsBusy` only. DI: `HybridShell` + `AdvancedViewModel` in `App.xaml.cs`; `AdvancedTab` in MainLayout. Avalonia App still in slnx. **NEXT = B11** (SEQUENTIAL). Do not start B11–B13 in this session. Do not start Phase 7.

---

### B11 — First-run + Connect-existing

**Status:** DONE  
**Kind:** SEQUENTIAL  
**Depends on:** B10 (Auto-detect also lives on Advanced)

**Goal:** Match Avalonia Phase 5. Startup: `LocalConfigStore.HasManageConfig()` → main vs first-run. Button-gated Auto-detect; **no** launch-time OCI probe. Choices: Setup, Auto-detect infrastructure, “I already have a stack”. Multiple matches → chooser modal. Overwrite confirm. Preserve local SSH key path / RCON.

**File ownership**

- Owns: `Components/FirstRun/**`, `ConnectExistingFlow` port, StackChooser modal, `App.xaml.cs` startup branch.

**Test**

- Empty `MCMANAGER_CONFIG_DIR`: chooser, no silent probe.
- Auto-detect against lab tag (operator); Cancel / none-found does not delete an existing seed.

**Done when:** A new PC can attach to an existing deployment from Hybrid without full Setup.

**Following step:** B12

**Suggested commit message:** `feat: port first-run and Connect-existing to Blazor`

**Changelog:** 2026-08-15 — **DONE.** Hybrid first-run chooser (Setup stub / Find existing / I already have a stack). Startup: `LocalConfigStore.HasManageConfig()` → manage vs first-run; no OCI on launch. Shared `ConnectExistingFlow` used by first-run and Advanced (chooser modal, overwrite confirm, preserve SSH key path / RCON). Successful first-run connect reloads local config then enters manage. Avalonia App still in slnx. **NEXT = B12** (SEQUENTIAL). Do not start B12–B13 in this session. Do not start Phase 7.

---

### B12 — Setup wizard

**Status:** DONE  
**Kind:** SEQUENTIAL (large; **do not** parallelize with itself, B10, or B11)  
**Depends on:** B11

**Goal:** Port [`SetupWizardViewModel.cs`](../src/McManager.App/ViewModels/SetupWizardViewModel.cs) (strip `Window Host`). Nine steps: Always Free → OCI profile/region → compartment → alert email → SSH → Vanilla version/EULA path → Auth Token → summary. Resume `data/setup-wizard.local.json`. Auth Token → Windows Credential Manager `McManager/ocir`. Deploy log: timestamps; stick-to-bottom unless the user scrolled up; stage percent; **Deploy/Back locked** after start (Re-Deploy is a separate Advanced action). Capacity wait dialog. Agents only `MCMANAGER_TOFU_DRY_RUN=1`.

**File ownership**

- Owns: `Components/Setup/**`, `ViewModels/SetupWizardViewModel.cs`, related DI.

**Test**

- Walk 9 steps offline; resume after app restart; dry-run Deploy log scroll/lock; SSH import file picker.
- **No** live `tofu apply` from an agent session.

**Done when:** Wizard is complete without requiring live apply; dry-run path works.

**Following step:** B13

**Suggested commit message:** `feat: port Setup wizard to Blazor Hybrid`

**Changelog:** 2026-08-15 — **DONE.** Hybrid Setup wizard (9 steps: Always Free → OCI profile/region → compartment → alert email → SSH → Vanilla → EULA → Auth Token → summary). Resume `data/setup-wizard.local.json`. Auth Token → Credential Manager `McManager/ocir`. Deploy log timestamps + stick-to-bottom unless scrolled up; stage percent; Deploy/Back locked after start (Re-Deploy = Advanced opens a new wizard). Capacity wait dialog. First-run and Advanced open the real wizard (B10 stub removed). Agents: `MCMANAGER_TOFU_DRY_RUN=1`. Avalonia App still in slnx. **NEXT = B13** (SEQUENTIAL). Do not start B13 in this session. Do not start Phase 7.

---

### B13 — Cutover: remove Avalonia WinExe

**Status:** DONE  
**Kind:** SEQUENTIAL  
**Depends on:** B12 + operator dogfood that Hybrid is usable for manage + Setup

**Goal:** One WinExe. Remove `McManager.App` (Avalonia) from the slnx (delete or leave the folder unreferenced). Optionally rename Hybrid → `McManager.App` **only if** README / `dotnet run` paths are updated in the **same** step. `dotnet build` clean.

**Do**

- Update README, Local-Config, AGENTS “Hybrid vs App” names if renaming.
- Confirm no Avalonia package references remain in the solution.

**File ownership**

- Owns: `src/McManager.slnx`, README, leftover Avalonia project tree, Hybrid csproj name if renaming.

**Test**

- `dotnet build src/McManager.slnx`
- `dotnet run` using the documented project
- Manage + first-run smoke (no live apply)

**Done when:** Operator daily-drives Blazor; Avalonia is not in the slnx; build is clean.

**Following step:** Operator dogfood. MVP **Phase 7** stays TODO — **do not start Phase 7 in the cutover session.**

**Suggested commit message:** `chore: remove Avalonia Manager UI; Blazor Hybrid is the WinExe`

**Changelog:** 2026-08-15 — **DONE.** One WinExe: `McManager.Hybrid` (not renamed). Removed Avalonia `McManager.App` from `McManager.slnx` and deleted `src/McManager.App/`. README / Local-Config / AGENTS / product + lab rules updated (Hybrid is the Manager UI; no Avalonia package refs in the slnx). `dotnet build src/McManager.slnx` clean. **NEXT = MVP Phase 7** (TODO). Do not start Phase 7 in this session.

---

## Risks

| Risk | Mitigation |
|------|------------|
| WebView2 runtime missing | B1 WPF MessageBox + Evergreen link. Do not bundle. |
| Title-bar / WebView overlap | Native WPF chrome. Do **not** re-implement Avalonia extended client area. |
| JS interop vs Blazor events | Prefer `@onclick`. JS only if copy/scroll has no C# path (Setup log stick-to-bottom). |
| Semi / Material leftover | Do not reference Avalonia packages in Hybrid. |
| Setup log scrolling | Port Avalonia stick-to-bottom (`@ref` + `scrollTop`, or small JS). |
| File pickers | WPF dialogs on STA via `IUiDispatcher`. |
| `Directory.Build.props` `net8.0` | Hybrid csproj overrides `net8.0-windows`. |
| Dual WinExe confusion | Resolved at B13: only `dotnet run --project src/McManager.Hybrid`. |
| Parallel DI collisions | `App.xaml.cs` / csproj / layout / CSS are SEQUENTIAL-owned. Parallel steps leave DI snippets. |
| Custom caption buttons | Out of scope unless the operator later asks. Native OS chrome is acceptable. |

---

## Out of scope (whole Phase B)

- Phase 7 Guide / greenfield E2E  
- Installer signing / packaging (Phase 8)  
- v1 PRODUCT-IDEAS: split Advanced vs Danger Zone, CIDR ranges, bell/notification center, Players tab, $1 lock UX, modded Setup, in-app pack catalog  
- Live `tofu apply` / OCIR push / SSH-bootstrap  
- Rewriting `McManager.Core`  
- Putting Manager UI in the lab repo  
- MAUI, Photino, Blazor Server, or browser-hosted UX  
- Marking visual/layout decisions as final  

---

## Plan changelog

| Date | Notes |
|------|--------|
| 2026-08-15 | **B13 DONE.** Phase B complete. Removed Avalonia `McManager.App` from slnx and deleted the project tree. One WinExe: `McManager.Hybrid` (not renamed). Docs/rules updated. `dotnet build` clean. **NEXT = MVP Phase 7** (TODO). Do not start Phase 7 in this session. |
| 2026-08-15 | **B12 DONE.** Hybrid Setup wizard (9 steps, resume JSON, Credential Manager token, deploy log timestamps/stick-to-bottom/percent, Deploy/Back lock, capacity wait). First-run/Advanced use the real wizard. Dry-run only. Avalonia App still builds. **NEXT = B13** (SEQUENTIAL). Do not start B13 in this session. Do not start Phase 7. |
| 2026-08-15 | **B11 DONE.** Hybrid first-run + Connect-existing (button-gated Auto-detect; chooser; overwrite confirm; preserve SSH/RCON). Shared `ConnectExistingFlow` with Advanced. Avalonia App still builds. **NEXT = B12** (SEQUENTIAL). Do not start B12–B13 in this session. Do not start Phase 7. |
| 2026-08-15 | **B10 DONE.** Hybrid Advanced / Danger Zone: technical VM/door status, break-glass Compute, idle OS-ISSUE-7, infra meta Refresh/Publish, Auto-detect, Setup stub (no tofu). Avalonia App still builds. **NEXT = B11** (SEQUENTIAL). Do not start B11–B13 in this session. Do not start Phase 7. |
| 2026-08-15 | Parent pasted B6–B9 DI + MainLayout; tabs visible; **NEXT = B10**. Do not start B10 in this session. Do not start Phase 7. |
| 2026-08-15 | **B8 DONE.** Hybrid Server Management: four info cards, Object Storage list/download/upload (native `IFilePicker`), SSH replace when VM1 RUNNING, soft-cap messaging. No Wipe/Modding/Delete. DI/layout snippets left for parent paste (did not edit `App.xaml.cs` / MainLayout). Avalonia App still builds. Do not start B10–B13 or Phase 7 in this session. |
| 2026-08-15 | **B9 DONE.** Hybrid Troubleshooting one-shots (dedicated tab; confirm-gated; result log + copy; OS-ISSUE-5 Console copy only). DI/layout snippets in the B9 changelog — parent must paste; this step did not edit `App.xaml.cs` / MainLayout. Avalonia App still builds. Do not start B10–B13 in this session. Do not start Phase 7. |
| 2026-08-15 | **B7 DONE.** Hybrid Usage dashboard/edit/publish (Avalonia Step 1.5 fields; remaining-in-month on this tab; 2 min poll; dirty-gated Save). DI/layout snippets in the B7 changelog — parent must paste; this step did not edit `App.xaml.cs` / MainLayout. Avalonia App still builds. Do not start B10–B13 or Phase 7 in this session. |
| 2026-08-15 | **B6 DONE.** Hybrid Whitelist CRUD + Security List sync (Add-IP popup, hover actions, dirty-gated Save). DI/layout snippets in the B6 changelog — parent must paste; this step did not edit `App.xaml.cs` / MainLayout. Avalonia App still builds. **NEXT = B7 / B8 / B9** (PARALLEL-OK remaining; B10 sequential). Do not start B7–B13 in this session. Do not start Phase 7. |
| 2026-08-15 | **B5 DONE.** Manage chrome live (status, power, pins, poll, toast) in Hybrid; Door/Compute/OciSession DI; Avalonia App still builds. **NEXT = B6** (B6–B9 PARALLEL-OK among themselves; B10 sequential). Do not start B6–B9 in this session. Do not start Phase 7. |
| 2026-08-15 | **B4 DONE.** Hybrid loads local config and shows reserved play IP in the status cluster; stub first-run when no manage config (no OCI on launch). Avalonia App still builds. **NEXT = B5.** Do not start B5 in this session. Do not start Phase 7. |
| 2026-08-15 | **B3 DONE.** UI-agnostic `Ui/` host services (dialogs, pickers, clipboard, clock/dispatcher); WPF STA impls; Razor modal host; DEBUG probes. **NEXT = B4.** Do not start B4 in this session. Do not start Phase 7. |
| 2026-08-15 | **B2 DONE.** Light warm-gray layout shell (title row, status, disabled power, fixed-width pins, five product tabs, placeholders). Self-hosted Inter / JetBrains Mono / Tabler. Native WPF chrome. **NEXT = B3.** Do not start B3 in this session. Do not start Phase 7. |
| 2026-08-15 | **B1 DONE.** `McManager.Hybrid` WPF + BlazorWebView WinExe references Core; WebView2-missing MessageBox; Avalonia App still builds. **NEXT = B2.** Do not start B2 in this session. Do not start Phase 7. |
| 2026-08-15 | **B0 DONE.** Agent rules and product intent retargeted to Blazor Hybrid (WPF + WebView2). Historical Wails→Avalonia kept. **NEXT = B1.** Do not scaffold in the B0 session. Do not start Phase 7. |
| 2026-08-15 | Plan created. Operator chose Blazor Hybrid (WPF + WebView2) **before** MVP Phase 7. Avalonia polish (Phase 6) is abandoned as the UI vehicle; goals transfer here. **NEXT = B0.** Do not scaffold in the plan-creation session. Do not start Phase 7. |
