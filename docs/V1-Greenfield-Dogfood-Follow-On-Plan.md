# V1 greenfield dogfood follow-on (living)

**Status:** Living. Created 2026-08-28 (docs only).  
**Parent:** [`V1-Implementation-Plan.md`](V1-Implementation-Plan.md) Step **9.4** / [`V1-Packaging-Plan.md`](V1-Packaging-Plan.md) **P6** (closed beta / dogfood).  
**Why now:** operator 2026-08-28 — full greenfield on a **new PC** (Windows installer). Most of Setup worked; these notes are **v1-blocking** (especially local config after Deploy) plus installer/wizard polish found in that run.

**Cost:** $0. TESTING profile only. Never `DEFAULT` / live Forge lab unless this chat explicitly authorizes. Never Minecraft `0.0.0.0/0`.  
**Functions / tofu:** do **not** `tofu apply` / `destroy` unless that section says to **ask first**. Do not SoftStop the door.  
**Hybrid config dir:** `MCMANAGER_CONFIG_DIR` = `mcmgr-blank-test` for Hybrid QA — **not** repo `data/config.local.json`. P1 is specifically about the **installed** path (no env override, no repo).  
**Hosts / OCIDs:** do **not** paste live OCIDs, IPs, Auth Tokens, or key material into tracked docs.

The operator’s timed log is gitignored `development/fresh-deploy-log.txt` (~17 minutes). Use it as evidence. Do **not** commit secrets from it. Duration itself is **not** a speed-up task (see [Parked](#parked-not-this-plan)).

---

## How agents must use this file

1. Read **this protocol**, the [Progress dashboard](#progress-dashboard), [Scrutiny](#scrutiny-plan-decisions), and **only the NEXT section**.
2. Implement only that section. Do not start neighbors “while you are here.”
3. After finishing: mark **DONE**, set the next incomplete section to **NEXT**, changelog line, add one line to V1 Step **9.4** + dashboard, update [`NEXT.md`](NEXT.md), **stop**.
4. **Last section (P4):** after it is DONE, set [`V1-Packaging-Plan.md`](V1-Packaging-Plan.md) **P6** back to **NEXT**, and set [`NEXT.md`](NEXT.md) to packaging **P6** / Step **9.4**. Do **not** start packaging **P7**.
5. Git: commits allowed per `git-policy`; never push/PR/tags/Releases unless the operator asks.
6. User-visible Setup/install changes: patch [`Guide.md`](Guide.md) in the **same** section that ships the behavior.
7. If this plan disagrees with [`PRODUCT-IDEAS.md`](PRODUCT-IDEAS.md), **follow this plan** and note drift.

Vague notes: **decide** inside the section **using Scrutiny**. **Stop and ask** for spend, `tofu apply` / `destroy`, `DEFAULT`, or parked items.

### Context budget

This header + **one** P-section + the files listed there. Do not load the full V1 plan or PRODUCT-IDEAS unless a heading is named. [`Local-Config.md`](Local-Config.md): **Load path** + Setup write bullets only when a section names it.

### Operator entry

New Agent chat → `/next-step`. Fresh chat per P-section.

### PARALLEL-OK

None. P2–P3 share [`Guide.md`](Guide.md). P1 then P2 share wizard persist / Setup copy. P4 owns the TESTING stack. **SEQUENTIAL.**

---

## Progress dashboard

| ID | Title | Status | Parallel | Cursor |
|----|-------|--------|----------|--------|
| P1 | Greenfield local config (installed PC) | **DONE** | SEQUENTIAL — unblocks Close → Manage | agent |
| P2 | Setup wizard polish | **DONE** | SEQUENTIAL — Hybrid wizard after P1 | agent |
| P3 | Installer desktop shortcut + app icon | **NEXT** | SEQUENTIAL — Inno + Hybrid; Guide | agent |
| P4 | Park reserved play IP on VM1 after Deploy | TODO | SEQUENTIAL — last; restores packaging P6 | agent |

**Live NEXT:** [`NEXT.md`](NEXT.md).

---

## What already exists (do not rediscover)

- **Installer (packaging P3 DONE):** Inno Setup 6 per-user (`packaging/McManager.iss`). Start Menu **MC Manager** only — no desktop icon task. Publish layout is app dir = product root (`infra/`, `onbox/mcmgr/`, `door_vm/`, `vm_agent/`, Function tar). It does **not** copy `AGENTS.md`, `McManager.slnx`, or `config.local.example.json`.
- **Local config today:** `LocalConfigStore.TryFindDataDirectory` walks up looking for an existing `data/config.local.json`, else `AGENTS.md` / `config.local.example.json`, else `McManager.slnx`. If none match, it returns **null**. `SetupWizardStore.Save` then fails with *Could not locate data directory. Set MCMANAGER_CONFIG_DIR or ensure the product repo root is findable.* Deploy’s last stage is `SetupApplyStage.ConfigWritten` (“Saving local config…” ~98%): `LocalConfigStore.SaveConfig` uses the same finder. `ShowDeploySuccess` is true only when `apply_stage` is `config_written`. Close is enabled whenever the wizard is not busy — so a failed save looks stuck at 98% with Close still clickable, and Close cannot open Manage because there is no `config.local.json`.
- **Program settings already use LocalAppData:** `%LOCALAPPDATA%\McManager\app-settings.json` (`AppSettingsStore`). OpenTofu workspaces live under `%LOCALAPPDATA%\McManager\tofu\`. Uninstall does **not** delete that folder (Guide §3).
- **Default server icon:** `assets/server-icons/default-icon.png` is already an embedded Core resource (`ServerIconComposer.DefaultIconResource`). Hybrid has **no** `ApplicationIcon`. Inno has no `SetupIconFile`.
- **Wizard step 1 help:** Always Free checkboxes are `.mcm-checkbox-row` with `.mcm-check-hit { flex: 1 1 auto }` then a sibling `WizardHelp`. The help control is pushed to the **right edge**.
- **Pack skip copy:** `PackReplaceUx.SkipWarningBody` is *Known client-only mods will automatically be skipped. Check the list below…*. Setup shows that string when `ShowOverrideListWarning` (override-list skip count &gt; 0), **not** when `ShowPackAssistedReview`. A fully supported `.mrpack` often has override-list skips and **no** review list. The review list is for homemade / jar-root zips (`KindManualZip` / `NeedsAssistedReview`).
- **Auth Token:** wizard title *Optional Auth Token*; `StepIsValid` for that step is **always true**. Guide §2 already explains Console **Tokens and keys** → **Auth Tokens**. Guide Setup table still says skip is allowed.
- **Play IP after Deploy:** OpenTofu attaches the reserved public IP to the **door** secondary (idle doorbell). `SetupBootstrapService.EnsureGuestRuntimeAsync` is supposed to `promote_playable.sh` (`ip_to_vm1`) after VM1 Minecraft is up ([SETUP-ISSUE-6](Issues.md) marked Fixed 2026-08-17). Operator greenfield 2026-08-28 still left the reserved IP on the **door** after first startup. Treat that as a **regression** to fix in product Setup, not a Console workaround.

---

## Scrutiny (plan decisions)

Locked for this plan. Do not reopen in an implementation chat.

| Topic | Decision |
|-------|----------|
| Installed config location | When `MCMANAGER_CONFIG_DIR` is unset **and** no product-repo markers are findable, the data directory is **`%LOCALAPPDATA%\McManager`** (same folder as `app-settings.json`). Write `config.local.json`, `friends.local.json`, `setup-wizard.local.json` there. Do **not** create `data/` under the install dir (`%LOCALAPPDATA%\Programs\MC Manager`) — uninstall would wipe the stack seed. Do **not** ship `AGENTS.md` / example JSON as a finder hack. |
| From-source / QA | Unchanged: repo `data/` next to `AGENTS.md` / `config.local.example.json`; `MCMANAGER_CONFIG_DIR` still wins (`mcmgr-blank-test`, pack-test, etc.). |
| Wizard “data directory” copy | Users must **never** see MCMANAGER_CONFIG_DIR / “product repo root” copy. Fix the finder so save **succeeds**. If save still fails, show a short user-facing error (cannot write under LocalAppData), not a developer env hint. |
| Deploy Close | Successful Deploy must persist `config.local.json` and set `apply_stage=config_written` so Close opens Manage. Failed config write must **not** look like success; keep the error visible. |
| Auth Token | **Required** to continue Setup. Spend-brake Function copy is a v1 feature, not a skip. Guide already documents creating the token in the OCI Console; remove “optional” / “skip this run” wizard and Guide Setup-table language. |
| Skip “check the list” warning | Show `PackReplaceUx.SkipWarningBody` **only** when the assisted-review list is visible (`ShowPackAssistedReview`). Do **not** show it for a clean / fully handled `.mrpack` with no list. Same rule on Manage **Change pack** (shared copy). Do not invent a second warning. |
| Help icons | Place `WizardHelp` immediately after the checkbox (or radio) it explains — not `space-between` / not `flex: 1` on the label shoving it to the far right. Fix wizard checkbox rows (step 1 is the report; the same row pattern elsewhere in Setup should not keep the old gap). |
| Desktop shortcut | Inno **optional** additional-tasks checkbox (standard `desktopicon`). Default **checked**. Start Menu shortcut stays. |
| App icon | Use `assets/server-icons/default-icon.png` as the Manager identity. Convert/track a multi-size `.ico` if Win32/Inno need ICO; do not draw a new mark. Set exe `ApplicationIcon`, WPF window icon if needed, Inno `SetupIconFile` + shortcut icons. |
| Reserved IP after Deploy | After a successful first Deploy with Minecraft up, the reserved play IP must be on **VM1** (door `PLAYABLE`). Idle/SoftStop still parks it on the door — do not change that. Do not ask the user to run Troubleshooting **Park play IP** as the happy path. |
| TESTING vs operator’s new-PC tenancy | Agents stay on **TESTING**. Do not SSH the operator’s friend/new-PC stack unless this chat names it. P4 uses the deploy log + product code; live verify on TESTING only if a stack is already there, and **ask** before `tofu apply` / `destroy`. |
| Restore NEXT | P4 DONE → packaging **P6** is the live NEXT again. Do not start **P7**. |

---

## Parked (not this plan)

| Item | Why |
|------|-----|
| Make Deploy faster than ~17 minutes | Operator timed a full greenfield; that is context, not a perf project. |
| Code-signing certificate | Still deferred (packaging Scrutiny). |
| Packaging **P7** / $1 clean-room fire | Operator spend; not this follow-on. |
| GitHub Actions / Velopack | Locked out of Phase 9. |
| Pack-corpus Cobblemon re-run | Separate chat; `/pack-test-one`. |
| In-app pack browser / public Minecraft | Rejected. |

---

## After this plan

1. [`NEXT.md`](NEXT.md) → [`V1-Packaging-Plan.md`](V1-Packaging-Plan.md) **P6** (closed beta / dogfood).
2. Operator continues dogfood with a rebuilt installer (P1–P3) and a Deploy that saves config + parks the play IP (P1 + P4).
3. Do **not** start packaging P7 from this file.

---

## P1 — Greenfield local config (installed PC)

**Status:** DONE  
**Parallel:** SEQUENTIAL — unblocks Close → Manage after Deploy  
**Cursor mode:** agent

**Read first**

- This section + Scrutiny rows **Installed config location**, **From-source / QA**, **Wizard “data directory” copy**, **Deploy Close**
- `src/McManager.Core/Config/LocalConfigStore.cs`
- `src/McManager.Core/Config/SetupWizardStore.cs`
- `src/McManager.Core/Config/AppSettingsStore.cs` (LocalAppData folder only)
- `src/McManager.Core/Setup/SetupDeployOrchestrator.cs` (`ConfigWritten` / `SaveConfig` only)
- [`Local-Config.md`](Local-Config.md) — **Load path** + Setup write bullets
- [`Guide.md`](Guide.md) — §3 uninstall / LocalAppData sentence + Setup “resume is saved locally” if present

**Do**

1. Change `TryFindDataDirectory` so an **installed** Manager (no repo markers, no `MCMANAGER_CONFIG_DIR`) resolves to `%LOCALAPPDATA%\McManager`, creates the folder if needed, and `SaveConfig` / `SetupWizardStore.Save` / `SaveFriends` succeed there. Keep existing override + repo-root behavior for developers and QA.
2. Replace the developer “MCMANAGER_CONFIG_DIR / product repo root / data/” failure strings on the user path. Wizard persist and Deploy must not show that copy.
3. If `SaveConfig` fails after a live Deploy, return a hard Deploy failure with a visible error. Do not leave the bar at ~98% as the only signal. Success still requires `apply_stage=config_written` so Close opens Manage.
4. Unit-test: temp LocalAppData (or isolated folder) with **no** `AGENTS.md` / example JSON / `.slnx` → save + load `config.local.json`. Env override and repo-root cases still work. Existing Core tests still pass.
5. Patch [`Local-Config.md`](Local-Config.md) Load path and a short Guide sentence: installed seeds live under `%LOCALAPPDATA%\McManager` (survive uninstall); from-source still uses repo `data/`.

**Test**

- `dotnet test` on Core (include the new finder tests).
- Manual if available: `dotnet run` of Hybrid **without** `MCMANAGER_CONFIG_DIR` from a published folder that is **not** inside the git checkout → wizard Next persists; a dry-run or stub must not require OCI. Do **not** `tofu apply` in this step.

**Done when:** An installer-layout process can write and reload manage config with no repo checkout and no env var. Wizard no longer shows the repo-root error on a clean PC. Deploy’s config stage can succeed.

**Changelog:** 2026-08-28 — Installed Manager (no repo markers, no `MCMANAGER_CONFIG_DIR`) writes `config.local.json` / friends / wizard JSON under `%LOCALAPPDATA%\McManager`. User-facing save errors; Deploy config-stage failure stays a hard fail. Core finder tests.

---

## P2 — Setup wizard polish

**Status:** DONE  
**Parallel:** SEQUENTIAL — Hybrid Setup after P1  
**Cursor mode:** agent

**Read first**

- This section + Scrutiny rows **Help icons**, **Skip “check the list” warning**, **Auth Token**
- UI skills **before** CSS/Razor: `C:\Users\matto\.agents\skills\impeccable\SKILL.md`, `C:\Users\matto\.agents\skills\web-design-guidelines\SKILL.md`
- `src/McManager.Hybrid/Components/Setup/SetupWizard.razor` (Always Free checkboxes; pack warning aside; Auth Token page)
- `src/McManager.Hybrid/wwwroot/css/app.css` (`.mcm-checkbox-row` / `.mcm-check-hit` / `.mcm-help`)
- `src/McManager.Hybrid/ViewModels/SetupWizardViewModel.cs` (step title, `StepIsValid` Auth Token, `ShowOverrideListWarning` / `ShowPackAssistedReview`)
- `src/McManager.Core/Setup/PackReplaceUx.cs` (`SkipWarningBody`)
- `src/McManager.Hybrid/Components/Tabs/ServerManagement/ServerManagementTab.razor` (same skip aside)
- [`Guide.md`](Guide.md) — Setup table Auth Token row + Game pack-review sentence if it still implies a list on every `.mrpack`

**Do**

1. **Step 1 (Always Free):** move each **info (i)** so it sits next to its checkbox, not at the right edge of the page. Reuse `WizardHelp`. Fix the flex that grows `.mcm-check-hit` to full row width. Apply the same row rule anywhere Setup uses that pattern.
2. **Step 4 (pack):** show `SkipWarningBody` (*check the list below…*) **only** when `ShowPackAssistedReview` is true. A fully supported `.mrpack` with no review list must not show that warning. Mirror on Manage Change pack.
3. **Auth Token step:** required. Cannot Next until a token is stored in Windows Credential Manager (`McManager/ocir`). Drop “optional” from the heading, subtitle, and `CanAdvance`. Keep Store / paste UX; Guide Console steps stay the how-to. Update Guide Setup table (remove skip). `Local-Config.md` “optional OCIR Auth Token” sentence → required for Setup.
4. Do **not** change pack skip-order / freeze / installer.

**Test**

- `dotnet build` Hybrid + Core tests that mention `SkipWarningBody` / Auth Token validity if they exist; add or adjust fixtures so a no-list `.mrpack` does not require the list warning.
- Visual: Always Free page — (i) adjacent to each box. Auth Token — Next disabled until stored.

**Done when:** Three operator notes (help icons, mrpack warning, required Auth Token) match the UI and Guide.

**Changelog:** 2026-08-28 — Help icons sit next to their checkbox/radio (wizard `.mcm-check-hit` no longer grows). `SkipWarningBody` only when assisted review is visible (Setup + Change pack). Auth Token required to Next; heading/Guide/Local-Config drop optional/skip. Core `ShouldShowSkipListWarning` tests.

---

## P3 — Installer desktop shortcut + app icon

**Status:** NEXT  
**Parallel:** SEQUENTIAL — Inno + Hybrid after P2 Guide pass  
**Cursor mode:** agent

**Read first**

- This section + Scrutiny rows **Desktop shortcut**, **App icon**
- `packaging/McManager.iss`
- `packaging/pack.ps1` (only if the pack copies icons)
- `src/McManager.Hybrid/McManager.Hybrid.csproj`
- Hybrid WPF window (`MainWindow.xaml` / `App.xaml`) for `Icon`
- `assets/server-icons/default-icon.png`
- [`Guide.md`](Guide.md) § **3. Install the Manager**

**Do**

1. Add an Inno **additional tasks** checkbox to create a **desktop** shortcut to `McManager.Hybrid.exe`. Default checked. Start Menu entry unchanged. Uninstall removes both shortcuts.
2. Use `assets/server-icons/default-icon.png` as the Manager app image: tracked `.ico` (multi-size from that PNG) for the exe, installer, Start Menu, and desktop shortcut. Do not commission a new graphic.
3. Guide §3: mention the desktop-shortcut checkbox; uninstall still leaves `%LOCALAPPDATA%\McManager`.
4. Do **not** require admin, Program Files, WebView2 bundling, or a code-signing cert. Do **not** `git push` / cut a Release unless the operator asks.

**Test**

- Pack script still fails without the Function tar.
- If Inno is on this PC: rebuild the installer, install per-user, confirm optional desktop icon + Start Menu use the new icon, exe shows it in Explorer/taskbar.

**Done when:** Installer can create a desktop shortcut; Manager uses the default server icon.

**Changelog:** *(empty)*

---

## P4 — Park reserved play IP on VM1 after Deploy

**Status:** TODO  
**Parallel:** SEQUENTIAL — last; restores packaging P6  
**Cursor mode:** agent

**Read first**

- This section + Scrutiny rows **Reserved IP after Deploy**, **TESTING vs operator’s new-PC tenancy**, **Restore NEXT**
- `development/fresh-deploy-log.txt` (gitignored; look for `Parking reserved play IP` / `promote_playable` / `ip_to_vm1` / door state — do not paste secrets)
- [`Issues.md`](Issues.md) **SETUP-ISSUE-6**
- `src/McManager.Core/Setup/SetupBootstrapService.cs` (`EnsureGuestRuntimeAsync`, `PromotePlayableAfterVm1`)
- `door_vm/scripts/promote_playable.sh`
- `src/McManager.Core/Setup/SetupDeployOrchestrator.cs` (order: guest repair vs Function vs `ConfigWritten`)
- [`Guide.md`](Guide.md) — after-Deploy reserved IP paragraph only if copy is wrong

**Do**

1. Find why the 2026-08-28 greenfield left the reserved IP on the **door** after Minecraft was up, despite SETUP-ISSUE-6 / `promote_playable.sh`. Fix the **product** Setup path (bootstrap script, SSH, door state, or stage order). Do not change tofu’s idle default (IP on door when the game VM is stopped).
2. After a successful Deploy with the game running, door `/api/status` should be **PLAYABLE** and the reserved public IP should be **ASSIGNED to VM1’s secondary**. Troubleshooting **Park play IP** remains break-glass, not the first-run path.
3. File or reopen [`Issues.md`](Issues.md) if this is a new cause (do not only patch the test VM). Mirror guest/door script fixes into `door_vm/` / Setup in this repo.
4. Guide: one sentence if the after-Deploy story still implies friends join immediately on the reserved IP — that must be true without a manual park.
5. **When this section is DONE:** mark P4 **DONE**; set [`V1-Packaging-Plan.md`](V1-Packaging-Plan.md) **P6** to **NEXT**; set [`NEXT.md`](NEXT.md) Plan = V1 Phase **9** / Step **9.4**, Sub-plan = packaging plan, Sub-step = **P6**, status `ready`. Do **not** start P7.

**Test**

- Prefer evidence from the deploy log + a targeted Core/script fix. Live TESTING verify only if VM1/door already exist: **ask** before `tofu apply` / `destroy`. Do not SoftStop the door. Do not use profile `DEFAULT`.
- If live: after Deploy/repair with Minecraft up, reserved IP on VM1 secondary; door `PLAYABLE`.

**Done when:** Product Setup parks the play IP on VM1 at the end of a successful first Deploy. NEXT pointer is packaging **P6** again.

**Changelog:** *(empty)*

---

## Plan changelog

| Date | Note |
|------|------|
| 2026-08-28 | **P2 DONE** — Setup wizard polish (help icons, skip-list warning only with assisted review, required Auth Token). Living **NEXT = P3** (installer desktop shortcut + app icon). Do not start P4 or packaging P7. |
| 2026-08-28 | **P1 DONE** — installed local config under `%LOCALAPPDATA%\McManager`. Living **NEXT = P2** (Setup wizard polish). Do not start P3 or packaging P7. |
| 2026-08-28 | Created (docs only) from operator new-PC greenfield notes. Living **NEXT = P1** (installed local config). Packaging **P6** paused (TODO) until P4 restores it. Do not start P7. |
