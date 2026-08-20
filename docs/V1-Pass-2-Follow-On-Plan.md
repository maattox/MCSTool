# V1 Pass-2 follow-on — operator notes (living)

**Status:** Living. Created 2026-08-20 (docs only). **NEXT = P2.**  
**Parent:** [`V1-Implementation-Plan.md`](V1-Implementation-Plan.md) Step **8.4**.  
**Why now:** operator 2026-08-20 — Pass 2 closed early after greenfield Modded + join + Modding panel. Pause Step **8.5.2** and implement these notes **before** QA Pass 3.

This file’s creation session **must not implement code**. Later agents implement **only the single section marked NEXT**.

**Cost:** $0. TESTING profile only. Never `DEFAULT` / live Forge lab. Never Minecraft `0.0.0.0/0`.  
**Functions:** agents **may** `fn build` / `fn push` / invoke **product** Functions on TESTING without asking, still $0 — no real $1 budget fire; do not SoftStop the door.  
**Tofu:** do **not** `tofu apply` / `destroy` in this plan unless a section says to **stop and ask**. Pass 3 still owns any later greenfield destroy. Keep the Pass 2 TESTING stack.  
**SSH:** `%USERPROFILE%\.ssh\mcmgr_ed25519_20260817_125552` (Pass 2 **reused** the Pass 1 key). Confirm the path in the TESTING `config.local.json` before SSH.  
**Hybrid config dir:** `MCMANAGER_CONFIG_DIR` = `mcmgr-blank-test` — **not** repo `data/config.local.json` (Forge / `DEFAULT`).  
**Hosts / OCIDs:** TESTING `config.local.json` and `%LOCALAPPDATA%\McManager\tofu\<stack-id>\` (`outputs.json`). **Do not paste live OCIDs, IPs, Auth Tokens, or key material into tracked docs or chat dumps.**

---

## How agents must use this file

1. Read **this protocol**, the [Progress dashboard](#progress-dashboard), and **only the NEXT section**.  
2. Implement only that section. Do not start neighbors “while you are here.”  
3. After finishing: mark **DONE**, set the next incomplete section to **NEXT**, changelog line, update V1 plan Step **8.4** + dashboard, **stop**.  
4. If you change a test VM or TESTING cloud resource, make the **same** change in local SoT (`onbox/`, `infra/`, `door_vm/`, `vm_agent/`, `functions/shutdown_vm/`, Manager/Setup). File lab [`docs/Issues.md`](../../OCI-mc-server-manager/docs/Issues.md) for on-box/Setup/door bugs.  
5. Never create git commits. Suggest a message.  
6. Do **not** start Step **8.5.2** (Pass 3), **8.6.1** (CI Function pipeline), or **9.1**. P13 is a **Setup lookup** for a pre-built image, not the CI/Release work in 8.6.1.  
7. If this plan disagrees with lab `PRODUCT-IDEAS.md`, **follow this plan** and note drift (do not rewrite PRODUCT-IDEAS to match).  
8. VM1: START if needed, **disable idle** while working, **re-enable** when finished (re-disable after Minecraft start — OS-ISSUE-7).  
9. UI-heavy sections (P3, P4, P8) **must** read the named UI skills before changing CSS/Razor. Do not invent a third visual language.

### Context budget

This header + **one** P-section + the files listed there. Blueprint: **named §§ only**. Do not load Pass 3, the full V1 plan, or PRODUCT-IDEAS unless a heading is named.

### Operator prompt (copy-paste for the next agent)

```text
Read docs/V1-Pass-2-Follow-On-Plan.md in OCI-mc-server. Implement only the section marked NEXT (or the PARALLEL-OK section I named).
You MAY use OCI CLI/API with profile TESTING (not DEFAULT) and SSH both test VMs with %USERPROFILE%\.ssh\mcmgr_ed25519_20260817_125552 (confirm path in the TESTING config). You MAY fn build/push/invoke product Functions on TESTING. Stay at $0. Do not tofu apply/destroy unless I authorize it in this chat. Do not commit. Do not start Step 8.5.2 (Pass 3), 8.6.1 (CI), or 9.1.
Use MCMANAGER_CONFIG_DIR for mcmgr-blank-test, not repo data/config.local.json (Forge / DEFAULT).
If you need VM1, START it, disable idle, re-enable when finished. Minecraft boot force-enables idle (OS-ISSUE-7) — disable again after a game start.
When done: update this plan’s statuses and V1 Step 8.4, file Issues.md if on-box/Setup/door, stop, tell me what you did, how to test, what’s next, and ask if I want to continue.
Prompt sequential steps in Agent mode (not Plan mode). Use Build in Parallel / Plan mode only if the NEXT step is marked PARALLEL-OK. Include this same Agent-vs-Plan instruction in the prompt you give me for the following step.
```

### PARALLEL-OK

Only when two sections **do not** edit the same files **and** do not both own the TESTING stack. Hybrid Razor/CSS is sequential by default. P9 (Core pack analyze) does not overlap Hybrid tabs. P12 (TESTING Function fill-in) must not run in parallel with any other SSH/OCI chat.

---

## What already happened (do not rediscover)

- Pass 1: Vanilla manage UI, doorbell, idle, spend-brake invoke on the **old** stack. Bug-fix P1–P8 **DONE**.  
- Pass 2: Delete + greenfield Modded (Fabulously Optimized 6.5.0, Fabric, shape **4/24** operator override). S6-01/S6-02/S7-04/S3-05/S4-11 **Pass**. SETUP-ISSUE-9 and SETUP-ISSUE-10 fixed in product. Function image **skipped** (Docker daemon was down). Remaining Pass 2 Phase B–D IDs were **not run**. No Pass 2 bug-fix plan.  
- Step **4.13** / R1–R4 **DONE** (itzg exclude lists). Manual/jar-root **install** keeps leftover unclear jars after the list; Setup **analyze** still **blocks** the whole zip when `UnclearSideCount > 0` (`SetupPackImport.UnclearSideRefusal`).  
- Top-bar **Start** (`MainViewModel.UpdateCommandFlags`): `CanStart` is true when VM1 is not RUNNING and not STARTING/PROVISIONING. **STOPPING is not gated** — that is P1. Novice Status is door-playable, not OCI lifecycle.  
- **Players** pin is never filled (`PlayersDisplay` stays placeholder). There is no RCON `list` poll.  
- Spend-brake overlay confirm (`ConfirmSpendBrakeStartAsync`) parks the play IP, DELETEs the lock, refreshes OS budget, **then calls `WakeGameServerAsync()`**. Operator wants the overlay to **unlock** only; Start stays on the top bar.  
- Existing `mcm-toast` auto-hides. Tab `StatusMessage` (e.g. Server Management wipe copy) sits at the **bottom** of the scrolling tab. That is P4.  
- Danger Zone is its **own tab**. Idle **timeout** is on Advanced; idle **enable** is on Danger Zone. `--bg-danger: #2a1c1c` is the dark red.  
- Setup last step is still the Deploy log. There is no “Deployment Complete” + reserved-IP copy block.  
- Console shows `journalctl -u minecraft` (includes RCON listener / RCON client thread noise). No simple/full toggle.  
- One shared `main.mcm-tab-body` scrollbar — tab switch keeps `scrollTop`.  
- Usage tab has month/today/rollover heroes; no per-day breakdown. Lab Python Manager has a day/interval tree — **do not clone it**.  
- PRODUCT-IDEAS **Modpack replace (after v1)** and blueprint **§28.1** exist. Operator pulled this into **v1** (this plan). **Light swap is parked**; v1 path is **full re-setup**, keep world unless the user also wipes.  
- Step **8.6.1** (CI-built ARM image + crane/oras copy, no Docker on the user’s PC) remains **after QA exit**. This plan only fills TESTING (P12) and teaches Setup to **prefer a pre-built artifact if present** (P13). Do not start GitHub Actions / Releases / the installer.

---

## Drift vs PRODUCT-IDEAS (follow this plan)

| Topic | PRODUCT-IDEAS / older V1 | This plan |
|-------|--------------------------|-----------|
| Danger Zone **tab** | Separate tab from Advanced (v1) | Merge into **bottom of Advanced**; tab label stays **Advanced** |
| Idle timeout | Advanced | **Danger Zone heading** only (with enable/disable) |
| “game computer” | Novice phrasing | User-visible copy → **server** |
| Pack replace | After v1; light vs full | **v1 now**; **full re-setup only** (light swap parked) |
| Jar-root unclear side | Fail / do not guess (`.mrpack` rule) | **Manual zip / jar-root:** continue; rely on exclude lists. **`.mrpack` still fails** on unclear `env.server` |
| Spend-brake overlay confirm | Overlay Start (park + clear + wake) | Confirm **clears the lock** (and doorbell recover as today) but **does not Start**; user clicks top-bar Start |
| Function image | 8.6.1 CI after QA | P12 TESTING fill-in now; P13 Setup looks for a pre-built artifact; **CI/Release still 8.6.1** |

Do **not** rewrite PRODUCT-IDEAS to match. Note the drift in the implementing section’s changelog / Guide.

---

## Parked (not this plan)

| Item | Why |
|------|-----|
| Console **tab completion** (commands + online names) | Operator: only if easy. Needs a command dictionary, live `list` parse, and Blazor autocomplete. Too large for this pause. After-v1 unless a later agent finds a tiny path — do not start it here. |
| Pack replace **light swap** (same MC + loader, converge `mods/` only) | Blueprint §28.1 later path. v1 = full re-setup (P10–P11). |
| Step **8.6.1** CI (`linux/arm64` in GitHub Actions), GHCR/Release asset, `crane`/`oras` in the installer | Required before official release; **not** this pause. P13 must not invent a second product path that contradicts 8.6.1. |
| Committing a Function image tarball to git | Images are large. `artifacts/` is **gitignored**. |
| CurseForge API (4.12), Quilt Setup, Players tab, paid/spend mode, in-app pack browser | Unchanged. |
| `tofu destroy` / second greenfield | Pass 3 / later. Keep this TESTING stack. |

---

## Progress dashboard

| ID | Section | Status | Parallel? | Live SSH/OCI? |
|----|---------|--------|-----------|----------------|
| **P1** | Top-bar Start STOPPED gate + spend-brake confirm + player count | **DONE** | SEQUENTIAL | Yes (player count) |
| **P2** | Setup “Deployment Complete” + reserved IP copy | **NEXT** | SEQUENTIAL | No |
| **P3** | Merge Danger Zone into Advanced + idle only there + vibrant red | TODO | SEQUENTIAL | No |
| **P4** | Window-locked dismissible action banners | TODO | SEQUENTIAL | No |
| **P5** | “game computer” → “server” (Setup + Manager + Guide) | TODO | SEQUENTIAL | No |
| **P6** | Console simple vs full log | TODO | SEQUENTIAL | Yes (optional) |
| **P7** | Per-tab vertical scroll memory | TODO | SEQUENTIAL | No |
| **P8** | Usage by day (collapsed “Detailed usage”) | TODO | SEQUENTIAL | No |
| **P9** | Manual / jar-root unclear-side: continue + exclude lists | TODO | PARALLEL-OK vs Hybrid-only | No |
| **P10** | Pack replace — on-box full re-setup | TODO | SEQUENTIAL | Yes |
| **P11** | Pack replace — Server Management UI | TODO | SEQUENTIAL | Yes |
| **P12** | TESTING spend-brake Function fill-in (Docker) | TODO | SEQUENTIAL (owns stack) | Yes |
| **P13** | Setup prefers a pre-built Function image artifact | TODO | SEQUENTIAL | No |

When **P13** is DONE: point V1 **NEXT** at Step **8.5.2** Pass 3 ([`V1-QA-Pass-3-Scope.md`](V1-QA-Pass-3-Scope.md)). Do not start Pass 3 until the operator says so.

---

## P1 — Top-bar Start STOPPED gate + spend-brake confirm + player count

**Status:** DONE  
**Catalog IDs:** S3-01 (overlay confirm), S4-01 (Players pin)

**Read first**

- `src/McManager.Hybrid/ViewModels/MainViewModel.cs` (`UpdateCommandFlags`, `ConfirmSpendBrakeStartAsync`, `PlayersDisplay`, `Vm1IsComingUp`)  
- `src/McManager.Hybrid/Components/Layout/MainLayout.razor` (Players pin + Start)  
- `src/McManager.Hybrid/Components/Layout/SpendBrakeLockOverlay.razor`  
- `src/McManager.Core/Services/MinecraftConsoleRemote.cs` (reuse RCON helper; do not open 25575 on the Security List)  
- `src/McManager.Core/Usage/SpendBrakeLockUx.cs`

**Do**

1. **Start gate.** `CanStart` must be false unless VM1 lifecycle is **STOPPED** (not `STOPPING`, `STARTING`, `PROVISIONING`, `RUNNING`, empty/unknown). Tooltip: wait until the server has fully stopped. Do not treat “Minecraft off / novice Stopped” as enough. If it already waits for STOPPED, keep that and still add the STOPPING/unknown guards + a unit/VM test if cheap.  
2. **Spend-brake confirm.** Typed confirm still parks the play IP, DELETEs the lock, refreshes door OS cache (same recover as today). **Do not** call `WakeGameServerAsync()`. Overlay dismisses; user uses top-bar **Start**. Rename the overlay button so it is not “Start”. Update overlay copy.  
3. **Players pin.** While novice Status is Running, poll RCON `list` over SSH (localhost on VM1) on the existing status refresh cadence — **not** a 1s loop. Parse the vanilla “There are X of a max of Y players online” line (Fabric/FO still uses this). Show **X** (and max if cheap). When Stopped, show `0` or `—` (pick one and use it consistently). Never put the RCON password in logs.

**Test**

- After Stop: Start stays disabled through OCI **STOPPING**, then enables at **STOPPED**.  
- Overlay confirm: lock gone, overlay gone, VM1 **not** woken; top-bar Start works.  
- Join 1 player on TESTING: Players pin shows `1` (or `1 / N`) without focusing a hidden tab.

**Done when:** The three behaviors above work; Guide one-liners for overlay and Players; Core/Hybrid tests for the Start gate and overlay-does-not-wake.

**Changelog:** 2026-08-20 — Start only when VM1 is STOPPED (not STOPPING/unknown). Overlay **Clear lock** parks IP + DELETE lock + OS refresh, does **not** Wake. Players pin: `0` Stopped, RCON `list` `X / Y` while Running. Catalog S3-01 / S4-01 expected updated. Guide one-liners. Core tests.

---

## P2 — Setup Deployment Complete + reserved IP

**Status:** NEXT  
**Catalog IDs:** S6-01 (finish page)

**Read first**

- `src/McManager.Hybrid/Components/Setup/SetupWizard.razor` (last / Deploy step + footer Close)  
- `src/McManager.Hybrid/ViewModels/SetupWizardViewModel.cs` (`IsLastStep`, deploy success, play IP fields)  
- [`Guide.md`](Guide.md) Setup Deploy / first connect paragraphs

**Do**

When Deploy **succeeds**, the last step must clearly show:

1. Heading **Deployment Complete** (not only a log line).  
2. The **reserved play IP** friends use, with a small **Copy** button (clipboard).  
3. Nearby copy: close the Setup wizard to continue to the Manager app (the existing Close button is enough — do not add a second wizard).

Keep the deploy log available (below or collapsed). Do not dump OCIDs. Use “server” not “game computer” if P5 has not run yet — prefer “server” in **new** strings.

**Test**

- Operator: finish (or resume a finished) Setup page shows the three items; Copy puts the IP on the clipboard.

**Done when:** Success UI exists; Guide mentions the IP + Close.

**Changelog:** *(empty)*

---

## P3 — Merge Danger Zone into Advanced

**Status:** TODO  
**Catalog IDs:** S4-02 (update **expected** — product change)

**Read first**

- `C:\Users\matto\.agents\skills\impeccable\SKILL.md`  
- `C:\Users\matto\.agents\skills\frontend-design\SKILL.md`  
- `src/McManager.Hybrid/Components/Layout/MainLayout.razor` (tab strip)  
- `src/McManager.Hybrid/Components/Tabs/Advanced/AdvancedTab.razor`  
- `src/McManager.Hybrid/Components/Tabs/Advanced/DangerZoneTab.razor`  
- `src/McManager.Hybrid/wwwroot/css/app.css` (`--bg-danger`, `--fill-danger`, `--border-danger`, `.mcm-danger-card`)  
- [`Guide.md`](Guide.md) Advanced vs Danger Zone  
- [`V1-QA-Catalog.md`](V1-QA-Catalog.md) S4-02 **Expected** (update it)

**Do**

1. Remove the **Danger Zone** tab. Tab name **Advanced** stays.  
2. Put today’s Danger Zone content at the **bottom of Advanced** under a clear **Danger Zone** heading (idle enable/disable **and** idle timeout minutes, shape scale, Delete infrastructure).  
3. Remove idle timeout from the upper Advanced body so it is **not** duplicated. Break-glass VM power, technical status, Deploy/repair stay in Advanced **above** Danger Zone.  
4. Restyle Danger Zone: `--bg-danger: #2a1c1c` is too dark. Use the UI skills for a **vibrant** danger treatment that still reads on the cobalt Hybrid theme (section background, heading, buttons). Do not make Wipe World / other non-DZ buttons louder than this heading.

**Test**

- No Danger Zone tab. Idle controls only under the Advanced → Danger Zone heading. Delete still typed-`confirm`. Catalog S4-02 expected updated.

**Done when:** Guide + catalog S4-02 match; CSS tokens updated.

**Changelog:** *(empty)*

---

## P4 — Window-locked dismissible action banners

**Status:** TODO  
**Catalog IDs:** Server Management wipe path (S3-07 adjacent)

**Read first**

- `C:\Users\matto\.agents\skills\impeccable\SKILL.md`  
- `C:\Users\matto\.agents\skills\frontend-design\SKILL.md`  
- `src/McManager.Hybrid/Components/Layout/MainLayout.razor` (`mcm-toast`)  
- `src/McManager.Hybrid/ViewModels/MainViewModel.cs` (`ShowToast`)  
- `src/McManager.Hybrid/Components/Tabs/ServerManagement/ServerManagementTab.razor` (bottom `StatusMessage`)  
- Other tab `StatusMessage` footers (Usage, Console, Advanced, Whitelist, Setup wizard footer)

**Do**

Important action results must be **visible without scrolling the tab**. Operator lean: a banner **locked to the bottom of the Manager window** with an **X** to dismiss after reading (not auto-hide for long/error copy).

Decide with the UI skills (sticky bottom banner vs modal). Constraints:

- Long copy (Wipe world while stopped) must be readable. Auto-hide `mcm-toast` is **not** enough for that.  
- Do not only post to the bell.  
- Tab-embedded grey `StatusMessage` under a long page **cannot** be the only channel for button results.  
- Setup wizard may keep a footer status **if** it stays on-screen; Manager tabs must not.

Reuse one Hybrid mechanism; migrate Server Management first, then other manage tabs that hide status at the bottom.

**Test**

- Wipe world while VM1/Minecraft off: the running-server warning appears in the window-locked banner even if Server Management is scrolled to the top. X dismisses it.

**Done when:** At least Server Management + other top-of-tab actions use the banner; Guide one-liner if user-visible.

**Changelog:** *(empty)*

---

## P5 — “game computer” → “server”

**Status:** TODO  

**Read first**

- Grep `game computer` / `Game computer` under `src/McManager.Hybrid`, `src/McManager.Core` (user-visible strings only), `docs/Guide.md`  
- Tests that assert the old copy (`Vm1ShapeScaleUxTests`, Setup pack warning, etc.)

**Do**

Replace user-visible **game computer** with **server** in the Setup wizard, Manager Hybrid UI, and Guide. That includes shape-picker copy, Console hints, spend-brake/overlay adjacent copy, Danger Zone size, pack-import warning (`SetupPackImport.OverrideListMisdeclarationCopy`).

**Keep** Advanced technical labels that name VMs (`VM1` / `VM2` / door). Prefer **Minecraft VM (VM1)** over **Game VM** if you touch that row.

Do not rewrite the whole blueprint or PRODUCT-IDEAS. Do not change OCI shape names.

**Test**

- Grep the Hybrid/Setup UI projects: no remaining user-visible “game computer”. Tests updated.

**Done when:** Guide uses “server” on those paths.

**Changelog:** *(empty)*

---

## P6 — Console simple vs full log

**Status:** TODO  
**Catalog IDs:** S4-13

**Read first**

- `src/McManager.Hybrid/Components/Tabs/Console/ConsoleTab.razor`  
- `src/McManager.Hybrid/ViewModels/ConsoleViewModel.cs`  
- `src/McManager.Core/Services/MinecraftConsoleRemote.cs`

**Do**

Default view: **simplified** Minecraft log (game chat, joins/leaves, commands the user sent, crash/error lines). Hide RCON plumbing (RCON listener bind, “Thread RCON Client”, auth chatter, similar).

A small **Full** / **Advanced** control on the console (corner) shows the unfiltered `journalctl` buffer. Do not add a PTY. Do not open RCON on the Security List.

**Test**

- With Minecraft up: simple view is readable; Full shows the noisy RCON lines; `list` still works.

**Done when:** Guide Console paragraph mentions simple vs full.

**Changelog:** *(empty)*

---

## P7 — Per-tab vertical scroll memory

**Status:** TODO  

**Read first**

- `src/McManager.Hybrid/Components/Layout/MainLayout.razor` (`mcm-tab-body` / `mcm-slim-scroll`)  
- `src/McManager.Hybrid/wwwroot/css/app.css` (tab body overflow)

**Do**

Each Manager tab remembers its own vertical scroll. Switching away and back restores it. A tab the user has not opened yet starts at the **top**.

Do **not** keep one scrollbar on the shared `main` that all tabs share. Saving `scrollTop` per tab id, or giving each panel its own overflow (hidden inactive panels), are both fine — pick the smaller change. Console special-case (`is-console`) must not break.

**Test**

- Scroll Server Management to the bottom → Whitelist (top) → back to Server Management (still at bottom).

**Done when:** Behavior works across the tab strip (including Advanced after P3).

**Changelog:** *(empty)*

---

## P8 — Usage by day (collapsed)

**Status:** TODO  
**Catalog IDs:** S4-09

**Read first**

- `C:\Users\matto\.agents\skills\impeccable\SKILL.md`  
- `C:\Users\matto\.agents\skills\frontend-design\SKILL.md`  
- `src/McManager.Hybrid/Components/Tabs/Usage/UsageTab.razor`  
- `src/McManager.Hybrid/ViewModels/UsageViewModel.cs`  
- `src/McManager.Core` ledger day/interval helpers already used by the Usage tab  
- Lab Python Manager day tree is **reference only** (`OCI-mc-server-manager/app/ui.py`) — do not clone ttk columns

**Do**

Add a **collapsed** section on Usage, opened by a control like **Detailed usage** / **Usage by day**. Closed by default.

Inside: hours **by UTC day** for the current month, readable for a novice (one row per day, used hours, optional still-running hint). Not the Python interval editor. No paid-mode UI. Stay on existing ledger math.

**Test**

- Closed by default; expand shows days; matches Usage heroes well enough to be trustworthy.

**Done when:** Guide Usage paragraph mentions the expander.

**Changelog:** *(empty)*

---

## P9 — Manual / jar-root unclear-side may continue

**Status:** TODO  
**Catalog IDs:** S6-02

**Read first**

- `src/McManager.Core/Setup/SetupPackImport.cs` (`FromManual`, `UnclearSideRefusal`)  
- `src/McManager.Core/Setup/ManualServerPackAnalyzer.cs` / installer  
- `src/McManager.Core.Tests/SetupPackImportTests.cs`  
- [`V1-Modpack-Robustness-Plan.md`](V1-Modpack-Robustness-Plan.md) R3 (unclear jars **kept** after the list)  
- Tracked fixture `tests/fixtures/packs/jar-root.zip` if present

**Do**

User-made zips (jars at archive **root**, unstructured `mods/`, filled Server Files) must **not** hard-block Setup solely because some jars have unclear server/client metadata. Continue; strip via itzg/product exclude lists + in-jar `client`; **keep** remaining unclear jars (R3). Show a **warning** in the confirmable summary (not a third checkbox).

**Still hard-block:** CurseForge client export / jar-less / mixed ID-only (P7); Quilt; unknown loader; `.mrpack` with unclear `env.server` (do not guess Modrinth index).

If a jar-root zip still has **no** detectable Minecraft version or loader after in-jar metadata, **stop and ask** — do not invent a version.

**Test**

- `dotnet test` pack-import tests: jar-root / unclear manual → `CanContinue`; `.mrpack` unclear still blocked; P7 still blocked.

**Done when:** Guide Modded analyze note matches.

**Changelog:** *(empty)*

---

## P10 — Pack replace, on-box full re-setup

**Status:** TODO  
**Catalog IDs:** new check (Pass 3); blueprint §28.1 **full** path only

**Read first**

- Blueprint **§28.1** (full re-setup row only) + **§12.2 / §12.3** if named in that section  
- `onbox/mcmgr/` bootstrap / repair  
- `src/McManager.Core/Setup/` pack install + game-manifest write  
- [`docs/Agent-Deploy-Pitfalls.md`](Agent-Deploy-Pitfalls.md) (`User=mcmgr`, sudo chains, world path)  
- Do **not** load the whole blueprint

**Do**

Operator pulled **change pack** into v1. Implement the **full re-setup** path only (light swap parked):

1. Stop Minecraft.  
2. Keep the **world** unless the user also chose Wipe (P11 confirm).  
3. Clear the previous game install enough that bootstrap is clean (loader, `mods/`, `config/` from the old pack, unit, manifest) — **not** `/opt/mcmgr` identity/RCON/world unless the contract requires it. Prefer the existing Setup bootstrap modules over a one-off SSH novel.  
4. Install the new local pack the same way Setup does (analyze → retain original under `data/imported-packs/` → on-box install → manifest → unit).  
5. Start + health check. Warn if Minecraft/loader change may not load the old save.

**Test**

- On TESTING VM1 (idle **off**): replace is not wired in UI yet; exercise the Core/SSH API or a DEBUG probe if that is the smallest hook. Do not leave Minecraft/world destroyed. Re-enable idle.

**Done when:** A documented Core/on-box entry point can full-replace a pack and keep the world; product SoT updated (not only the live VM).

**Changelog:** *(empty)*

---

## P11 — Pack replace, Server Management UI

**Status:** TODO  
**Depends on:** P10  
**Catalog IDs:** S4-11 adjacent; add a catalog ID in a gap if needed (do not renumber)

**Read first**

- `src/McManager.Hybrid/Components/Tabs/ServerManagement/ServerManagementTab.razor`  
- `src/McManager.Hybrid/ViewModels/ServerManagementViewModel.cs`  
- P10 entry point  
- [`Guide.md`](Guide.md) Modding section  
- Same file-picker / drag-and-drop rules as Setup (**no catalog**)

**Do**

Server Management **Change pack** (wording up to you): pick / drop a `.mrpack` or server-pack zip. Strong confirm (this reinstalls Minecraft on the server; world kept unless they also wipe). Reuse Setup analyze + client-pack checkboxes. Disable while VM1 is not up (or Start-first copy). P4 banner for progress/errors.

**Not** a per-mod IDE. **Not** an in-app browser.

**Test**

- Operator: on TESTING, change to a **small** sample pack (or back to FO) only if they agree in the chat. Otherwise DEBUG/temp-dir + inspect. Do not download mega-packs.

**Done when:** Guide Modding documents Change pack; catalog expected updated.

**Changelog:** *(empty)*

---

## P12 — TESTING spend-brake Function fill-in

**Status:** TODO  
**Catalog IDs:** S2-16, S2-17 (Pass 3)

**Read first**

- V1 plan [Product Functions on TESTING](V1-Implementation-Plan.md#product-functions-on-testing-blanket)  
- `src/McManager.Core/Setup/OcirFunctionPublisher.cs`  
- `src/McManager.Core/Setup/SetupDeployOrchestrator.cs` (Function stage + skip)  
- `functions/shutdown_vm/` (`func.yaml` **0.0.12**, VM1 only + lock PUT)  
- TESTING `config.local.json` / tofu `outputs.json` (do not paste secrets)  
- Operator Docker image id **hint:** `rpgo24yh5lizi9tj4h5m8drh0` (identify whether this is local Docker or OCIR; do not assume)

**Do**

Pass 2 skipped the Function (Docker daemon was down). Docker Desktop is **running** now. On **TESTING** only:

1. Build/push **product** `shutdown_vm` `linux/arm64` (v1 image: SoftStop **VM1 only** + lock PUT). Reuse the existing OCIR repo `mcmgr-fn/softstop` if Setup created it.  
2. Create or update the Function + Events on this stack so S2-17 can run later. Prefer `fn` / OCI CLI under the Functions blanket.  
3. If Function **application** / Events resources were never applied (skip left tofu without a Function), **stop and ask** before `tofu apply`. Do not `tofu destroy`. Do not create extra paid apps/repos.  
4. Optionally `docker save` a local tarball under gitignored `artifacts/` for P13 — **do not commit it**.  
5. Synthetic invoke only. Do not fire a real $1 alert. Do not SoftStop the door. DELETE the lock after tests unless the next chat needs it.

**Test**

- Function present on TESTING; synthetic invoke SoftStops **VM1** and PUTs the lock (then restore: START VM1 if you stopped it, DELETE lock, idle on).

**Done when:** S2-16 would be Pass on this stack; notes in Pass 2 results additional-problems can stay historical.

**Changelog:** *(empty)*

---

## P13 — Setup prefers a pre-built Function image

**Status:** TODO  
**Depends on:** P12 (artifact path known)  
**Does not finish 8.6.1**

**Read first**

- V1 [Step 8.6.1](V1-Implementation-Plan.md#step-861--ci-built-arm-image--setup-copy-into-ocir) (constraints only — do not implement CI)  
- `src/McManager.Core/Setup/OcirFunctionPublisher.cs`  
- `src/McManager.Core/Setup/SetupDeployOrchestrator.cs`  
- [`Guide.md`](Guide.md) Auth Token / Deploy Function paragraphs  
- [`docs/Local-Config.md`](Local-Config.md) repair/Function skip note

**Do**

Teach Setup to **prefer a pre-built ARM image artifact** if present (path next to the app or gitignored `artifacts/`, documented). Then copy/push that into **the user’s** OCIR and continue the existing Function tofu stage.

If no artifact: keep today’s Docker buildx path; if Docker/token missing, **skip** as today (explicit log).

**Do not:** GitHub Actions, GHCR as the live Function image, `crane`/`oras` CI, installer bundling, committing the tarball, removing Auth Token (OCIR still needs it). Those stay **8.6.1**.

Derive OCIR username from namespace + OCI user **if that is a small change**; otherwise leave `MCMANAGER_OCIR_USERNAME` and note it for 8.6.1.

**Test**

- With a local artifact and no Docker daemon: Setup Function stage attempts copy/push (TESTING or dry-run). Without artifact and without Docker: skip still works.

**Done when:** Guide says a bundled/pre-built image is used when present; Docker is not required if the artifact exists. V1 **NEXT** → Step **8.5.2** Pass 3. Do not start Pass 3 unless the operator says so.

**Changelog:** *(empty)*

---

## After this plan

1. V1 dashboard: **8.4 DONE**, **NEXT = Step 8.5.2** (Pass 3).  
2. Follow [`V1-QA-Pass-3-Scope.md`](V1-QA-Pass-3-Scope.md) — leftovers from Pass 1/2 **plus** tests for files this plan changed. Do not `tofu destroy` unless that prompt says so.  
3. Do not start 8.6.1 or 9.1.

---

## Plan changelog

| Date | Note |
|------|------|
| 2026-08-20 | **P1 DONE.** Start STOPPED gate; overlay unlock-only; Players pin. **NEXT = P2.** Do not start Pass 3, 8.6.1, or 9.1. |
| 2026-08-20 | **Created** (docs only). **NEXT = P1.** Pass 2 closed early; Step **8.5.2** paused. Pack replace pulled into v1 (full re-setup). Tab completion, light-swap, and 8.6.1 CI parked. Do not start Pass 3, 8.6.1, or 9.1. |
