# V1 operator-notes follow-on (living)

**Status:** Living. Created 2026-08-21 (docs only). **NEXT = P9** (P1–P8 **DONE**). Step **8.7** P1–P5 are **DONE**.  
**Parent:** `[V1-Implementation-Plan.md](V1-Implementation-Plan.md)` Step **8.8**.  
**Why now:** operator 2026-08-21 — after modpack-test fixes, implement Manager / Setup / pack-UX notes **before** QA Pass 3. Many notes are vague; agents **decide inside each section’s bounds** and record the choice. Stop and ask for spend, tofu destroy, CurseForge **API keys**, or pulling parked after-v1 items.

This file’s creation session **must not implement code**. Later agents implement **only the single section marked NEXT**.

**Cost:** $0. TESTING profile only. Never `DEFAULT` / live Forge lab. Never Minecraft `0.0.0.0/0`.  
**Functions:** agents **may** `fn build` / `fn push` / invoke **product** Functions on TESTING without asking, still $0 — no real $1 budget fire; do not SoftStop the door.  
**Tofu:** do **not** `tofu apply` / `destroy` unless a section says to **stop and ask**. Keep the Pass 2 TESTING stack.  
**SSH:** `%USERPROFILE%\.ssh\mcmgr_ed25519_20260817_125552` (confirm in TESTING `config.local.json`).  
**Hybrid config dir:** `MCMANAGER_CONFIG_DIR` = `mcmgr-blank-test` — **not** repo `data/config.local.json`.  
**Hosts / OCIDs:** TESTING `config.local.json` and `%LOCALAPPDATA%\McManager\tofu\<stack-id>\`. **Do not paste live OCIDs, IPs, Auth Tokens, or key material into tracked docs or chat dumps.**

Pack-handling notes apply to **both** Setup and Manager **Change pack** unless a section says otherwise.

---



## How agents must use this file

1. Read **this protocol**, the [Progress dashboard](#progress-dashboard), and **only the NEXT section**.
2. Implement only that section. Do not start neighbors “while you are here.”
3. After finishing: mark **DONE**, set the next incomplete section to **NEXT**, changelog line, update V1 plan Step **8.8** + dashboard, **stop**.
4. Mirror TESTING / guest fixes into local SoT. File `[Issues.md](Issues.md)` for on-box/Setup/door bugs.
5. Never create git commits. Suggest a message.
6. Do **not** start Step **8.5.2** (Pass 3), **8.6.1**, or **9.1**. Do not start this file until **8.7** is DONE.
7. If this plan disagrees with `[PRODUCT-IDEAS.md](PRODUCT-IDEAS.md)`, **follow this plan** and note drift (do not rewrite PRODUCT-IDEAS).
8. VM1: START if needed, **disable idle** while working, **re-enable** when finished (OS-ISSUE-7).
9. **UI-heavy sections (P3, P4, P5, P7, P8)** must read the named UI skills **before** changing CSS/Razor. Do not invent a third visual language. Reuse existing tokens (`mcm-help` info hover, wizard footer, action banner chrome). **NuGet is allowed** on `McManager.Hybrid` only. No Avalonia.

Vague notes: **decide** (layout, copy, animation ms, color) inside the section. **Stop and ask** for legal/ToS, spend, or scope listed in [Parked](#parked-not-this-plan).

### Context budget

This header + **one** P-section + the files listed there. Blueprint: **named §§ only**. Do not load Pass 3, the full V1 plan, or PRODUCT-IDEAS unless a heading is named.

### UI skills (required where the section says so)

Read **before** CSS/Razor (paths on the operator PC):

- `C:\Users\matto\.agents\skills\impeccable\SKILL.md`
- `C:\Users\matto\.agents\skills\web-design-guidelines\SKILL.md`

Optional if the section is a visual pass: `C:\Users\matto\.agents\skills\frontend-design\SKILL.md`. Do not skip impeccable + web-design-guidelines when the section marks **UI skill**.

### Operator prompt (copy-paste for the next agent)

```text
Read docs/V1-Operator-Notes-Follow-On-Plan.md in OCI-mc-server. Implement only the section marked NEXT (or the PARALLEL-OK section I named).
You MAY use OCI CLI/API with profile TESTING (not DEFAULT) and SSH both test VMs with %USERPROFILE%\.ssh\mcmgr_ed25519_20260817_125552 (confirm path in the TESTING config). You MAY fn build/push/invoke product Functions on TESTING. Stay at $0. Do not tofu apply/destroy unless I authorize it in this chat. Do not commit. Do not start Step 8.5.2 (Pass 3), 8.6.1, or 9.1.
Use MCMANAGER_CONFIG_DIR for mcmgr-blank-test, not repo data/config.local.json (Forge / DEFAULT).
If you need VM1, START it, disable idle, re-enable when finished. Minecraft boot force-enables idle (OS-ISSUE-7) — disable again after a game start.
UI sections (P3, P4, P5, P7, P8): read the impeccable and web-design-guidelines skills listed in that plan before CSS/Razor.
When done: update this plan’s statuses and V1 Step 8.8, file Issues.md if on-box/Setup/door, stop, tell me what you did, how to test, what’s next, and ask if I want to continue.
Prompt sequential steps in Agent mode (not Plan mode). Use Build in Parallel / Plan mode only if the NEXT step is marked PARALLEL-OK. Include this same Agent-vs-Plan instruction in the prompt you give me for the following step.
```



### PARALLEL-OK

Only when two sections **do not** edit the same files **and** do not both own the TESTING stack. Setup wizard Razor/CSS is **SEQUENTIAL** (P4–P7). P1 (console filter) may run in parallel with P2 **if** they do not share files. Default: **SEQUENTIAL**.

---



## What already happened (do not rediscover)

- Console **Simple vs Full** (8.4 P6): `MinecraftConsoleRemote.FilterSimpleLog` drops only a handful of RCON listener/thread strings. Operator 2026-08-21: Simple still shows RCON and other noise. That is **P1**, not a new toggle.
- Action banners (8.4 P4): window-locked full-width bar; short success auto-hides (~3.5s); **progress / warning / error persist** until X. That is why “Stopping the game server…” stays up. Tab `StatusMessage` on Advanced **forwards** to the banner (`OnStatusMessageChanged`) — loading `meta/infra.json` posts `FormatSummary()` (play IP, bucket, …). Server Management posts `Listed N backup(s). Select one to download.` on tab open.
- Setup steps today: Always Free → OCI profile → budget email → SSH → Minecraft → Name and icon → EULA → Auth Token → Review/deploy. Compartment is auto-named `mcmgr` (or `mcmgr-2` / `mcmgr-3` if taken) — no wizard page. Connect-existing / paste OCID stays on Advanced auto-detect. Default window + wizard footer (Back / Next / Close) exist. Deploy log is short. Progress sits in the page, not a second dock.
- Server identity (Step 7.6): Manager **Name, icon, and messages** → `messages/chat.json` + optional 64×64 `messages/server-icon.png`. Door MOTD **favicons** are separate: `/opt/mccontrol/assets/icons/` (`idle.png`, `starting.png`, `exhausted.png`) from `door_vm/assets/icons/`. Operator overlays live in `assets/server-icons/`.
- Pack replace full re-setup is **v1**. Jar-root may continue with unclear jars (8.4 P9). Layer 3 quarantine was **parked** in 4.13; operator pulled it into **this** plan (P10).
- CurseForge **API** deferred (4.12, ToS). Client exports / mixed-ID zips **refused**. In-app pack **catalog** is **rejected**.
- Step **8.7** (must be DONE before this file): crash-aware health, in-jar unstructured side, Fabric leftover clients, Java major on pack change, high-unclear warnings.

---



## Scrutiny (operator asked — decisions)

These are **plan decisions**, not optional flavor. Implementing agents follow them unless the operator overrides in chat.

**Jar-root confirm + manifest (P9).** Detection of MC version / loader from jars already exists and is often wrong. **Accept:** show detected values and let the user **correct** Minecraft version, loader (Forge / Fabric / NeoForge), loader version, and Java major **before** deploy / Change pack. **Do not** mutate the user’s original file in place. Write a **derived** archive used for bootstrap + **Download pack**. Prefer a valid Modrinth `modrinth.index.json` (formatVersion 1, `dependencies`, files in `overrides/` with hashes) so MultiMC-style import **might** work. If a spec-compliant pack cannot be produced without download URLs, still write that index **plus** a small product sidecar (`mcmgr-pack.json`) with the confirmed fields; do not block Setup on MultiMC compatibility. **Do not** claim CurseForge `manifest.json` compatibility.

**Quarantine (P10).** **Accept**, bounded by blueprint **§24.3 Layer 3**: only when the loader attributes the crash to **exactly one** mod; move jar to `mods.quarantined/` (never delete); retry **once**; record `modpack.quarantined_files`; surface in Manager (keep excluded vs put back). Do **not** silently merge into Layer 2. Ambiguous / multi-mod crashes = P1-style failure, no auto-strip. Client-only guess in the notify copy is OK when metadata says client; do not promise every quarantined jar was client-only.

**CurseForge helper (P11).** **Reject** API key, CDN download, and in-app catalog. **Accept** a **help panel** when analyze refuses a client/mixed export: (1) existing Server Files / Modrinth `.mrpack` copy, (2) `https://www.curseforge.com/projects/{projectID}` links from IDs already in the zip (no slug required), (3) **optional** one Modrinth search by pack **name** from the CF manifest — show **at most 3** outbound links; user still downloads a file themselves. If search is flaky or empty, skip silently besides (1)(2).

**Oracle branding on the default server name.** **Drop** “Hosted by Oracle™” / “Powered by Oracle”. Oracle’s name and logo are trademarks; using them on a Minecraft MOTD/list name implies endorsement we do not have. Defaults: `Vanilla Server`, `Paper Server`, `Modded Server`. Description default: `made with github.com/maattox/oci-mc-server` (operator string).

**Compartment step (P6).** **Accept:** hide the wizard page; create `mcmgr`, or `mcmgr-2` / `mcmgr-3` if the name exists. Connect-existing / paste OCID stays on Advanced auto-detect — not a Setup radio. Do not `tofu apply` to test name collision unless the operator authorizes it (unit-test the namer).

**Progress dock vs toasts (P3–P4).** Two layers: **toasts** = compact, not full-width; **progress dock** = full-width bottom bar like the wizard footer / old action banner, for long jobs + primary actions (Deploy / Cancel). Toasts sit **above** the dock (or lower-left/right — **UI skill decides**). Do not put “Listed 3 backups” in either.

**Icon processing (P8).** **Admin PC** (Core/Hybrid), not VM1 (often STOPPED) and not the door Micro. Color 64×64 → existing `messages/server-icon.png` (Java list when VM1 holds the play IP). Greyscale + `assets/server-icons/overlay-*.png` → door favicon variants (map unavailable → `exhausted.png`). Default user art: `assets/server-icons/default-icon.png`. Examples in that folder are **reference only**.

**Setup no-scroll (P5).** **Aim** for the default Manager window with default UI scale. Split a step rather than shrink type below readable. High DPI / large fonts may still scroll — do not clip. Use `mcm-help` hover for extra copy.

---



## Drift vs PRODUCT-IDEAS (follow this plan)


| Topic              | PRODUCT-IDEAS / older V1        | This plan                                                                   |
| ------------------ | ------------------------------- | --------------------------------------------------------------------------- |
| Layer 3 quarantine | Parked after 4.13               | **v1 now** (P10), blueprint §24.3 bounds                                    |
| Setup identity     | Seed chat.json; edit in Manager | **Setup page** for name / description / icon (P7)                           |
| Door favicons      | Solid color gen_icons.py        | **User icon** + overlays (P8)                                               |
| Compartment        | Wizard step                     | **Hidden**; auto `mcmgr` (+ numeric suffix)                                 |
| Default MOTD name  | Operator / Manager              | Type-based defaults; **no Oracle™**                                         |
| CF client export   | Refuse + Guide                  | Refuse **plus** project links / optional Modrinth search (still no API key) |
| Jar-root           | Detect + continue               | Detect + **user correct** + derived archive                                 |


Do **not** rewrite PRODUCT-IDEAS to match.

---



## Parked (not this plan)


| Item                                                               | Why                                                   |
| ------------------------------------------------------------------ | ----------------------------------------------------- |
| CurseForge **API** (4.12) / downloading jars by file ID            | ToS + no product key. P11 is links only.              |
| In-app pack browser / catalog                                      | **Rejected**.                                         |
| Pack replace **light swap**                                        | After-v1.                                             |
| MultiMC-perfect round-trip as a hard gate                          | P9 best-effort `modrinth.index.json`; sidecar always. |
| Players tab, paid mode, PTY console, Quilt Setup, public Minecraft | Unchanged out of scope.                               |
| `tofu destroy` / second greenfield                                 | Pass 3 / later.                                       |
| Step **8.6.1** CI Function image                                   | After QA exit.                                        |


---



## Progress dashboard


| ID      | Section                                                    | Status   | Parallel?                            | Live SSH/OCI?   |
| ------- | ---------------------------------------------------------- | -------- | ------------------------------------ | --------------- |
| **P1**  | Console Simple: drop RCON + plumbing noise                 | **DONE** | PARALLEL-OK vs P2 if no shared files | Optional        |
| **P2**  | Stop tab-open toasts (backups, infra.json)                 | **DONE** | PARALLEL-OK vs P1                    | No              |
| **P3**  | Compact toasts + auto-dismiss completed progress           | **DONE** | SEQUENTIAL                           | No              |
| **P4**  | Bottom progress dock (Setup + Change pack)                 | **DONE** | SEQUENTIAL                           | No              |
| **P5**  | Setup wizard UX (copy, layout, log height, humanize)       | **DONE** | SEQUENTIAL                           | No              |
| **P6**  | Auto compartment name; drop wizard step                    | **DONE** | SEQUENTIAL                           | No              |
| **P7**  | Setup identity page (name / description / icon)            | **DONE** | SEQUENTIAL                           | No              |
| **P8**  | Icon state variants from overlays                          | **DONE** | SEQUENTIAL                           | Yes (door push) |
| **P9**  | Jar-root confirm + derived manifest                        | **NEXT** | SEQUENTIAL                           | Optional        |
| **P10** | Layer 3 crash quarantine                                   | TODO     | SEQUENTIAL                           | Yes             |
| **P11** | CurseForge refuse helper (links, optional Modrinth search) | TODO     | SEQUENTIAL                           | No              |


When **P11** is DONE: point V1 **NEXT** at Step **8.5.2** Pass 3 (`[V1-QA-Pass-3-Scope.md](V1-QA-Pass-3-Scope.md)`). Do **not** start Pass 3 until the operator says so.

---



## P1 — Console Simple filter

**Status:** DONE  
**Catalog IDs:** S4-13 (update expected if Simple is stricter)

**Read first**

- `src/McManager.Core/Services/MinecraftConsoleRemote.cs` (`FilterSimpleLog`, `IsSimpleLogNoiseLine`)
- `src/McManager.Core.Tests/MinecraftConsoleRemoteTests.cs`
- `src/McManager.Hybrid/ViewModels/ConsoleViewModel.cs`
- Real noise examples in `[Mod-Pack-Tests.md](Mod-Pack-Tests.md)` / live journal if VM1 is up — Full view must still show everything

**Do**

1. Simple = **player-facing**: joins/leaves, chat, commands the admin typed, `Done`, world-prep progress, WARN/ERROR/FATAL that are not RCON plumbing.
2. Drop: RCON listener/client/auth/connection threads (expand beyond today’s five substrings), systemd/journalctl wrappers if present, mixin/debug spam that is not an ERROR, Netty/RCON worker lines.
3. Keep `[Rcon:` **command echoes** if they are the user-visible result of a command the Console sent (already intended in P6). Drop “RCON running on” / “Thread RCON Client”.
4. Tests with fixture log chunks (including leftover RCON lines the operator still sees).

**Decide if unclear:** When unsure, **hide** in Simple (Full exists). Do not add a third verbosity.

**Test**

- `dotnet test`. Operator: Simple on a running modded server is readable; Full is a superset.

**Done when:** Simple is not a near-copy of Full; tests cover new noise; Guide one-liner if the toggle copy changes.

**Changelog:** 2026-08-21 — **P1 DONE.** Expanded `FilterSimpleLog`: RCON plumbing, journal wrappers, Netty INFO, mixin INFO/WARN boot spam, modloader startup INFO; keeps spawn progress, Done, joins/chat, `[Rcon:` echoes, ERROR/FATAL. Core tests + spawn-area fixture. Guide Console line. Catalog S4-13 expected. **NEXT = P2.**

---



## P2 — Stop tab-open toasts

**Status:** DONE  
**Catalog IDs:** S4-11 (backups), S4-02 (Advanced)

**Read first**

- `src/McManager.Hybrid/ViewModels/ServerManagementViewModel.cs` (`Listed {N} backup(s)`)
- `src/McManager.Hybrid/ViewModels/AdvancedViewModel.cs` (`OnStatusMessageChanged` → `_banner.ShowInferred`, `FormatSummary`)
- `src/McManager.Core/Notifications/ActionBanner.cs`

**Do**

1. **Do not** toast when Server Management opens / refreshes the backup list. The list UI is enough.
2. **Do not** toast `Loaded meta/infra.json: …` / `FormatSummary()` on Advanced tab select. Keep `InfraSummary` **on the page**.
3. Still toast **user-initiated** failures (publish failed, list failed) and break-glass progress.
4. Stop forwarding every `StatusMessage` change if that is what causes load dumps — gate banner posts instead of deleting in-page status.

**Test**

- Open Server Management: no backup-count toast. Open Advanced: no infra dump toast. Publish-meta failure still notifies.

**Done when:** Those two loads are silent; tests if cheap.

**Changelog:** 2026-08-21 — **P2 DONE.** Tab-open backup list + infra meta load no longer post action banners; `TabStatusBannerPolicy` gates forwarding; list/publish failures still toast explicitly. Core tests. **NEXT = P3.**

---



## P3 — Compact toasts + completed-progress dismiss

**Status:** DONE  
**UI skill:** required  
**Catalog IDs:** S4-01 chrome; 8.4 P4 expected

**Read first**

- UI skills (impeccable + web-design-guidelines)
- `ActionBanner` / `ActionBannerViewModel` / MainLayout banner markup + CSS
- 8.4 P4: persist rules (`ShouldPersist` — progress never auto-hides)

**Do**

1. Replace full-width notification bar with a **compact bubble** (width = content, max-width so long errors wrap). Still dismissible (X). Not the bell tray.
2. Color: **UI skill** — light **info/success = blue-ish**, **error/warning = red/amber**, distinct from page background. Match existing Hybrid tokens if a redstone/info pair already exists; do not introduce a third palette.
3. **Progress** (“Stopping the game server…”) **must clear when the action finishes** (success → short auto-hide toast or silent if P2-style; failure → error toast that persists). Do not leave the in-progress bubble up until the user hits X.
4. Short success may still auto-hide (~3–5s). Errors/warnings persist until X (unchanged intent).
5. Accessibility: do not rely on color alone; keep `aria-live` roles from today.

**Decide if unclear:** Corner vs centered-above-footer — prefer **lower area**, not covering the tab strip. Animation: short fade or slight rise is OK; keep under ~200ms.

**Test**

- Stop: “Stopping…” disappears when novice Status is Stopped. Error wipe still stays. `dotnet test` on persist rules.

**Done when:** Compact colored toasts; progress completes without a stuck bar; Guide not required unless copy changes.

**Changelog:** 2026-08-21 — **P3 DONE.** Full-width action banner → compact lower-right toast (cobalt info/success, amber warning, redstone error). Start/Stop progress clears when door wait finishes (`WaitForDoorAsync` toasts success). `SeverityName` sr-only label; progress loader spin. Core persist rules unchanged. Catalog S4-01/S3-07 + Guide. **NEXT = P4.**

---



## P4 — Bottom progress dock

**Status:** DONE  
**UI skill:** required  
**Catalog IDs:** S6-01 deploy; S4-11 Change pack

**Read first**

- UI skills
- Setup wizard footer (Back / Next / Close / Deploy) + deploy progress UI
- Change pack progress in Server Management
- P3 toast layout (dock must not collide)

**Do**

1. Long-running **Setup Deploy** and **Change pack**: **percent** (if known), **time elapsed**, short status, and primary actions (**Deploy** / **Cancel** when those exist) live in a **window-locked bottom dock** — same family as the wizard footer and the old full-width banner, **not** embedded only in the scrolling panel.
2. Show the dock with a **short slide-up** (~150–250ms). Do not block input to the page except as today (no Deploy double-start).
3. Setup: moving Deploy/Cancel into the dock is **in scope**. Back/Next/Close may stay on the same bar or the dock **while deploying** — **UI skill** picks one bar, not two competing footers.
4. Percent may be **indeterminate** when stages have no %. Elapsed time should still run.
5. Humanized **one-line** status on the dock (see P5 for the string table). The detailed log stays on the page (taller in P5).

**Decide if unclear:** Whether Change pack uses the same component as Setup (prefer **one** shared dock component).

**Test**

- During a dry or real deploy: dock visible while scrolled to top of the log; Cancel/Deploy reachable. Change pack shows the same pattern.

**Done when:** Dock exists for both flows; animation present; no second mystery progress bar in the panel **as the only** indicator.

**Changelog:** 2026-08-21 — **P4 DONE.** Shared window-locked progress dock for Setup Deploy and Change pack (percent when known, elapsed, one-line status, Deploy/Install/Cancel on the same bar). In-page progress is no longer the only indicator. Compact toasts stay above the dock for outcomes. Catalog S6-01/S4-11 + Guide. **NEXT = P5.**

---



## P5 — Setup wizard UX

**Status:** DONE  
**UI skill:** required (impeccable + web-design-guidelines; frontend-design optional)  
**Catalog IDs:** S6-01; Guide Setup chapter

**Read first**

- UI skills
- `SetupWizard.razor` + wizard CSS + `SetupWizardViewModel` step titles/copy
- Default window size (WPF `MainWindow`)
- `mcm-help` hover pattern (`MainLayout.razor`)

**Do**

1. **Less text.** Keep must-know facts; move explanations to **info-icon hover** (`mcm-help`). No walls of paragraphs.
2. **No “stack”** in user-visible Setup copy (say **server**, or **VMs** only when the sentence is actually about the two computers).
3. **Fit the default window** without vertical scroll when possible. If a step is too tall, **split into another step** rather than cramming. Measure; don’t guess.
4. **Deploy log** viewport at least **2.5×** current height (the operator number).
5. **Humanize** in-progress one-liners. Never show raw `> rm -rf /tmp/mcmgr-onbox && mkdir -p /tmp/mcmgr-onbox` as the “what is happening” line. Map SSH/on-box commands to short English (“Preparing files on the server…”). Keep the **raw log** in the log window for Advanced users.
6. Buttons/layout: **UI skill**. Do not redesign the Manager **tabs**; this section is the **wizard**.
7. Guide: short paragraph if step names/order change (P6/P7 may land right after — update Guide in the step that changes order, or here if only copy/layout).

**Decide if unclear:** Exact step splits after measuring. Do not remove EULA or Always Free education — shorten them.

**Test**

- Operator: each current step at default size; deploy log taller; status line never a raw `rm`.

**Done when:** Copy/layout pass landed; log taller; humanized status map for the noisiest bootstrap lines; Guide if needed.

**Changelog:** 2026-08-21 — **P5 DONE.** Short Setup copy + `mcm-help` hovers (no “stack”); Review log ≥2.5× during Deploy (form/plan hidden); humanized dock status (`ProgressDockUx.TryHumanizeLogLine`, never raw `rm`). Guide + S6-01. **NEXT = P6.**

---



## P6 — Auto compartment name

**Status:** DONE  
**Catalog IDs:** S6-01 (step list)

**Read first**

- `SetupWizardViewModel` / `SetupWizard.razor` compartment step
- OpenTofu / Setup code that creates the compartment
- Connect-existing (must keep working)

**Do**

1. Remove the Compartment **wizard step**. Always create name `mcmgr`. If that name is taken in the tenancy/parent, suffix `-2`, `-3`, … (decide delimiter; keep OCI name rules).
2. Do not ask the user to paste a compartment OCID in Setup. Advanced **Auto-detect** remains the escape hatch.
3. Renumber steps / persist keys so resume does not land on a deleted index.
4. Guide: one line.

**Decide if unclear:** Collision lookup API (ListCompartments) vs create-and-retry on 409 — prefer list then create; no extra spend.

**Test**

- Unit test namer. Do not `tofu apply` unless authorized. Wizard has no Compartment page; Next still validates.

**Done when:** Step gone; default `mcmgr`; suffix logic tested; Guide.

**Changelog:** 2026-08-21 — **P6 DONE.** Removed Compartment wizard page. Setup always creates `mcmgr`, or `mcmgr-2` / `mcmgr-3` … (hyphen; ListCompartments then create). Resume schema 2 remaps old step indexes. Connect-existing still matches name `mcmgr` / `mcmgr-N` or tag. Guide + S6-01. **NEXT = P7.**

---



## P7 — Setup identity page

**Status:** DONE  
**UI skill:** required  
**Catalog IDs:** S4-12 (identity still applied); new Setup step

**Read first**

- UI skills
- Server Management name/icon/messages (Step 7.6) — **reuse** store + 64×64 PNG rules
- Setup seed of `messages/chat.json` today
- Contracts `messages/chat.json` / `messages/server-icon.png` headings only

**Do**

1. Add a Setup step: **server name**, **description**, **icon upload** (PNG). Place it where the wizard flow is natural (after game type / pack confirm is known, before Review) — **decide** after P5/P6 step list.
2. Defaults (no Oracle wording):
  - Vanilla (Mojang): `Vanilla Server`
  - Paper: `Paper Server`
  - Modded: `Modded Server`
  - Description: `made with github.com/maattox/oci-mc-server`
3. Empty icon → P8 will use `assets/server-icons/default-icon.png` (if P8 not done yet, skip variants and still seed name/description).
4. Manager identity page remains the day-2 editor. Setup writes the same objects.

**Test**

- Changing game type updates the default **until** the user edits the name (then stop overwriting). Deploy/seed has name+description. Guide one-liner.

**Done when:** Step exists; defaults as above; no Oracle™; reuse existing persistence.

**Changelog:** 2026-08-21 — **P7 DONE.** Setup **Name and icon** after Minecraft (before EULA). Defaults Vanilla/Paper/Modded Server + `made with github.com/maattox/oci-mc-server` (no Oracle™); name default updates until the user edits. Optional 64×64 PNG. Seeds `messages/chat.json` via existing store (icon if chosen). Resume schema 3. Guide + S6-01/S4-12. **NEXT = P8.**

---



## P8 — Server icon state variants

**Status:** DONE  
**UI skill:** required for any in-app preview  
**Catalog IDs:** S4-12; door MOTD favicon

**Read first**

- UI skills (preview only)
- `assets/server-icons/` (`default-icon.png`, `overlay-offline.png`, `overlay-starting.png`, `overlay-unavailable.png`; examples are **not** shipped as live assets unless useful as test fixtures)
- `door_vm/assets/icons/` + `mcdoor` favicon load
- `ChatMessagesStore` / VM1 `server-icon.png`
- `[Contracts-Object-Storage.md](Contracts-Object-Storage.md)` messages heading

**Do**

1. On admin PC: user (or default) PNG → 64×64 color (Minecraft `server-icon.png` when the game is up).
2. Greyscale copy + overlay:
  - offline → `overlay-offline.png` → door **idle**
  - starting → `overlay-starting.png` → door **starting**
  - unavailable (usage / spend-brake) → `overlay-unavailable.png` → door **exhausted** (and spend-brake if that uses the same slot — **decide**, prefer one “cannot play” art)
3. Publish variants to the **door** (Setup + identity save). Prefer Object Storage + door pull if that avoids SSH-only drift; if pull is too large, SCP/SFTP from Manager like other door deploys — **decide**, mirror into `door_vm/` defaults for greenfield.
4. Do not process on the E2 Micro beyond writing files mcdoor already loads.
5. Image library: **Hybrid/Core** (e.g. ImageSharp if needed — ask only if the license is not OSS-friendly). Tests compare against `example-*.png` loosely (size/overlay presence), not pixel-perfect unless cheap.

**Decide if unclear:** Greyscale algorithm (luma). Stretch vs pad user art to 64×64 (prefer **contain** on a vanilla-ish background, not smash).

**Test**

- Upload / default: three variants exist; door ping while idle shows overlay-offline style; VM1 playable still uses color icon. Guide one-liner.

**Done when:** Pipeline + door/VM1 publish; defaults work with no upload; Issues.md if live TESTING door path was wrong.

**Changelog:** 2026-08-21 — **P8 DONE.** Admin-PC ImageSharp pipeline: contain-fit 64×64 color (`messages/server-icon.png`) plus greyscale+overlay door variants (`door-idle/starting/exhausted.png`). Default `assets/server-icons/default-icon.png` when none uploaded. Setup seed + identity Save PUT Object Storage and `messages.door`; door `pull_os_icons.sh` + mccontrol reload on os-refresh/wake. Unavailable and spend-brake share exhausted art. In-app preview strip. Core tests + greenfield `door_vm/assets/icons`. Guide + S4-12. **NEXT = P9.**

---



## P9 — Jar-root confirm + derived manifest

**Status:** NEXT  
**Catalog IDs:** S6-02; Change pack

**Read first**

- `ManualServerPackAnalyzer` / jar-root path
- Setup Game step + Change pack confirm UI
- Modrinth index format (public spec; do not load the whole blueprint — **§22** only if needed)
- Download pack (must point at the **derived** archive once created)

**Do**

1. When the pack is **jar-root / unstructured zip** (not a complete `.mrpack` / filled CF Server Files), show **detected** Minecraft version, loader, loader version, Java major as **editable** fields (dropdowns or validated text — **decide**). User confirms before deploy / Change pack.
2. Warn if the user’s loader/MC **disagrees** with in-jar peeks, but **allow** continue (operator asked to correct bad detection).
3. After confirm: build a **derived zip** (do not overwrite the source path). Include `modrinth.index.json` if possible (`overrides/mods/` + hashes + `dependencies`). Always include confirmed fields in a sidecar if the index is incomplete. Use the derived file for VM copy + Download pack.
4. Same UI idea in Setup and Change pack (shared component if cheap).
5. Tests: analyzer → user override → archive contains index/sidecar with overridden loader.

**Decide if unclear:** Loader version picker source (user text vs small catalog). Prefer text + validate non-empty.

**Test**

- Fixture jar-root: wrong autodetect, user sets Fabric/MC, derived index matches. Guide: unstructured packs can be corrected.

**Done when:** Confirm UI + derived archive on both Setup and Change pack; original file untouched.

**Changelog:** *(empty until implemented)*

---



## P10 — Layer 3 crash quarantine

**Status:** TODO  
**Catalog IDs:** new (Pass 3); depends on Step **8.7** P1

**Read first**

- Blueprint **§24.3 Layer 3 only** (exactly one blamed mod; `mods.quarantined/`; `quarantined_files`; never silent Layer 2)
- Health check from 8.7 P1
- Server Management / Console surfaces for pack status
- Manifest writer `onbox/mcmgr/common/manifest_write.sh`

**Do**

1. Implement Layer 3 as specified: inspect loader “problem mod” report; **exactly one** → move jar, retry once, record outcome (`retry_succeeded`, `operator_acknowledged`).
2. Notify the user: named mod removed from this boot; if metadata says client-only, say they likely do not need it on the server; if unknown, say it may be required and they can put it back.
3. Manager: **keep excluded** (promote to local Layer 2 overlay for **this** pack identity) vs **put back** (restore jar, clear entry).
4. Zero or several implicated mods: no quarantine; 8.7 P1 failure copy.
5. Tests: fixture crash report with one mod; ambiguous report does nothing.

**Decide if unclear:** Pack identity key for Layer 2 promote (hash of original archive vs name+MC+loader). Prefer archive hash.

**Test**

- Fixture + optional live only if operator provides a crashing pack. Guide: quarantined mods list.

**Done when:** On-box + Manager ack/restore; manifest field; Guide; Issues.md if TESTING bootstrap needed a repair script.

**Changelog:** *(empty until implemented)*

---



## P11 — CurseForge refuse helper

**Status:** TODO  
**Catalog IDs:** S6-02 (P7 jar-less still hard-block)

**Read first**

- `ManualServerPackAnalyzer` CF refusal strings
- Step **4.12** decision (no API key)
- Setup + Change pack refuse UI

**Do**

1. Keep **hard-block** for jar-less / mixed-ID CF exports. No downloads.
2. Help panel: Server Files vs `.mrpack` (existing intent); list **CurseForge project links** from IDs in the zip (`https://www.curseforge.com/projects/{id}`).
3. Optional: one Modrinth API **search** by pack name; **≤3** links; timeout ~5s; failure → omit search, keep (1)(2).
4. Not a catalog: no browse, no trending, no “install this result.”

**Test**

- Fixture CF client export: refuse + at least project links when IDs exist. Guide sentence.

**Done when:** Helper ships; still no CF key; V1 **NEXT** → Step **8.5.2** Pass 3 (blocked until the operator starts it).

**Changelog:** *(empty until implemented)*

---



## After this plan (do not do it here)

1. V1 dashboard: **8.8 DONE**, **NEXT = Step 8.5.2** Pass 3.
2. Update `[V1-QA-Pass-3-Scope.md](V1-QA-Pass-3-Scope.md)` include-list for 8.7/8.8 behaviors if not already listed.
3. `AGENTS.md` + product rule NEXT lines.
4. Do **not** start Pass 3 until the operator says so.

---



## Plan changelog


| Date       | Note                                                                                                                                                                                                                                                    |
| ---------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 2026-08-21 | **P8 DONE.** Admin-PC icon variants (color 64×64 + door greyscale overlays); Object Storage + door pull. **NEXT = P9.**                                                                                                                                              |
| 2026-08-21 | **P7 DONE.** Setup Name and icon page (type-based defaults, no Oracle™); seeds `messages/chat.json`. **NEXT = P8.**                                                                                                                                              |
| 2026-08-21 | **P6 DONE.** Auto compartment name (`mcmgr` / `mcmgr-2`…); Compartment wizard page removed. **NEXT = P7.**                                                                                                                                              |
| 2026-08-21 | **P5 DONE.** Setup wizard copy/layout/help hovers; taller deploy log; humanized dock status. **NEXT = P6.**                                                                                                                                              |
| 2026-08-21 | **P4 DONE.** Shared bottom progress dock (Setup Deploy + Change pack). **NEXT = P5.**                                                                                                                                                                    |
| 2026-08-21 | **P3 DONE.** Compact lower-right toasts; Start/Stop progress dismiss on completion. **NEXT = P4.**                                                                                                                                                      |
| 2026-08-21 | **P2 DONE.** Stop tab-open toasts (backup list, infra meta load). **NEXT = P3.**                                                                                                                                                                        |
| 2026-08-21 | **P1 DONE.** Console Simple filter stricter (RCON, journal, mixin/modloader boot noise). **NEXT = P2.**                                                                                                                                                 |
| 2026-08-21 | Created (docs only). Operator notes after informal pack tests. **Do not start until 8.7 DONE.** Then **NEXT = P1**. Oracle™ dropped from default names. Layer 3 pulled into v1 (P10). CF helper = links only. Do not implement in the creation session. |


