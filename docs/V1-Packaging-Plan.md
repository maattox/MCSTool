# V1 Packaging (living)

**Status:** Living. Created 2026-08-27 (docs only).  
**Parent:** [`V1-Implementation-Plan.md`](V1-Implementation-Plan.md) Phase **9** (Steps **9.1**–**9.5**).  
**Why now:** Phase **8.5** (QA) and Phase **8.6** (pre-built ARM Function image; users do not need Docker) are **DONE**. Operator unblocked Phase 9 on 2026-08-27.

**Cost:** $0. TESTING profile only. Never `DEFAULT` / live Forge lab. Never Minecraft `0.0.0.0/0`.  
**Functions / tofu:** do **not** `tofu apply` / `destroy` unless that section says to **ask first**. Do not SoftStop the door.  
**Hybrid config dir:** `MCMANAGER_CONFIG_DIR` = `mcmgr-blank-test` — **not** repo `data/config.local.json`.  
**Hosts / OCIDs:** do **not** paste live OCIDs, IPs, Auth Tokens, or key material into tracked docs.

**GitHub:** do **not** `git push`, create tags, or `gh release create` unless the operator asks in that chat. The operator does not need to change default branch or GitHub repo feature flags to start this plan — see [GitHub Releases (operator)](#github-releases-operator).

---

## How agents must use this file

1. Read **this protocol**, the [Progress dashboard](#progress-dashboard), [Scrutiny](#scrutiny-plan-decisions), and **only the NEXT section**.
2. Implement only that section. Do not start neighbors “while you are here.”
3. After finishing: mark **DONE**, set the next incomplete section to **NEXT**, changelog line, update V1 plan Step **9.1** (or the matching 9.x) + dashboard, update [`NEXT.md`](NEXT.md), **stop**.
4. Git: commits allowed per `git-policy`; never push/PR/tags/Releases unless the operator asks.
5. Do **not** add GitHub Actions. Do **not** buy or embed a code-signing certificate. Do **not** implement Velopack / silent in-app apply. Do **not** implement Setup “pull a newer `infra/` zip from GitHub” (§13 optional channel).
6. If this plan disagrees with [`PRODUCT-IDEAS.md`](PRODUCT-IDEAS.md), **follow this plan** and note drift. Do not rewrite PRODUCT-IDEAS except a one-line NEXT pointer if this chat already did.
7. User-visible install / update copy: patch [`Guide.md`](Guide.md) in the same section that ships the behavior.

Vague notes: **decide** inside the section **using Scrutiny**. **Stop and ask** for spend, `tofu apply` / `destroy`, `DEFAULT`, a paid code-signing cert, or parked items.

### Context budget

This header + **one** P-section + the files listed there. [`Automated-Infrastructure-Deployment.md`](Automated-Infrastructure-Deployment.md): **§13 only** when a section names it. Do not load the full V1 plan or PRODUCT-IDEAS unless a heading is named.

### Operator entry

New Agent chat → `/next-step`. Fresh chat per P-section.

### PARALLEL-OK

None. P1–P3 share publish layout and Hybrid csproj. P4 reads the same version + Guide. **SEQUENTIAL.**

---

## Progress dashboard

| ID | Title | Status | Parallel | Cursor |
|----|-------|--------|----------|--------|
| P1 | Publish layout: product tree next to the exe | **DONE** | SEQUENTIAL — ProductPaths + Hybrid publish | agent |
| P2 | OpenTofu on a clean PC (pinned download) | **DONE** | SEQUENTIAL — needs P1 layout; Setup path | agent |
| P3 | Windows installer (Inno) + Function tar + Release recipe | **DONE** | SEQUENTIAL — wraps P1 output | agent |
| P4 | GitHub Releases update check | **DONE** | SEQUENTIAL — Hybrid launch + settings toggle | agent |
| P5 | Guide + README v1 pass | **DONE** | SEQUENTIAL — docs after P1–P4 exist | agent |
| P6 | Closed beta / dogfood | **NEXT** | SEQUENTIAL — operator-led; installer preferred | either |
| P7 | V1 exit review | TODO | SEQUENTIAL — operator declares ready | either |

**Live NEXT:** [`NEXT.md`](NEXT.md) → **P6**.

---

## What already exists (do not rediscover)

- **One WinExe:** `McManager.Hybrid` (`net8.0-windows`, `Version` 0.1.0). Setup is inside the app. Do not add a second Setup.exe.
- **From-source run:** `dotnet run --project src/McManager.Hybrid`. Guide and README treat this as the **developer** path; users use the Windows installer.
- **ProductPaths:** walks up from `AppContext.BaseDirectory` looking for `infra/` or `config.local.example.json`, then resolves `infra/`, `onbox/mcmgr/`, `door_vm/`, `vm_agent/`, `functions/shutdown_vm/`. A published layout that puts those trees **next to the exe** is enough; do not invent a second resolver.
- **Function tar (8.6.1 DONE):** `FunctionImageArtifact` already looks next to the app, `artifacts/` next to the app, repo `artifacts/`, and `MCMANAGER_FUNCTION_IMAGE_TAR`. File name `mcmgr-fn-softstop-linux-arm64.tar` (gitignored; do **not** commit). Developer rebuild: Docker Desktop + `buildx` (Guide recipe). Users copy without Docker.
- **OpenTofu today:** `OpenTofuLocator` finds `tofu.exe` on PATH, WinGet Links, or `%LOCALAPPDATA%\McManager\tofu\tofu.exe`. If still missing, Setup/Destroy **downloads once** a pinned OpenTofu Windows amd64 zip (SHA-256), extracts `tofu.exe` there, and writes an MPL 2.0 pointer. Do **not** require WinGet. Do **not** ship HashiCorp `terraform.exe`. `tofu init` still fetches the OCI provider.
- **Update toggle (6.1 DONE; P4 honors it):** `%LOCALAPPDATA%\McManager\app-settings.json` `check_for_updates` (default **on**). Gear UI saves it. On launch, one unauthenticated `GET .../releases/latest`; newer tag → prompt with notes + download link. No silent apply.
- **WebView2:** Evergreen runtime is a **prerequisite** (MessageBox + Microsoft installer link). Do not bundle the runtime.
- **No `.github/workflows`:** GitHub Actions stayed **out** of 8.6. Stay out here too. Operator builds locally and uploads a Release by hand.
- **Repo:** public `maattox/oci-mc-server`. Product work is on branch `staging`. GitHub Releases are **tags + assets**, not a special branch.

---

## Scrutiny (plan decisions)

Locked for this plan. Do not reopen in an implementation chat.

| Topic | Decision |
|-------|----------|
| Who needs Docker | **Users never.** Installer / publish output includes the ARM Function tar. **Developer** Docker Desktop is how the tar is produced (already true). |
| GitHub Actions | **Out.** No `.github/workflows`. No CI publisher. |
| Installer tool | **Inno Setup 6**, one `.exe`. Per-user install (no admin / no Program Files). Start Menu shortcut. Uninstall entry. |
| Auto-apply updates (Velopack / Squirrel / MSIX) | **Out of v1.** 9.2 is **check + prompt + release notes**, then the user downloads the new installer. |
| Code signing | **Deferred.** Document SmartScreen “unknown publisher” in Guide. Do not buy a cert in this plan. Unsigned is OK for closed beta. |
| `tofu.exe` | **Download once** (pinned OpenTofu Windows amd64 + SHA-256) into `%LOCALAPPDATA%\McManager\tofu\tofu.exe`. Internet required for first Setup. Do **not** require WinGet. Do **not** ship HashiCorp `terraform.exe`. Keep MPL license text / source pointer. `tofu init` still fetches the OCI provider (already the Setup story). |
| Product tree | Publish/install layout is **app dir = product root**: `infra/`, `onbox/mcmgr/`, `door_vm/`, `vm_agent/`, `functions/shutdown_vm/`, plus the Function tar next to the exe (or `artifacts/`). Exclude `.terraform/`, tfstate, filled `terraform.tfvars`. |
| Self-contained | `dotnet publish -r win-x64 --self-contained` so users do **not** need the .NET SDK. WebView2 Evergreen stays a separate prereq. |
| Function tar in git | **Never commit.** Pack script copies from gitignored `artifacts/`. Missing tar → fail the pack with the developer rebuild recipe, do not ship an installer without it. |
| GitHub repo settings | **No change required** to start P1–P4. Releases need no extra feature flag. Do not switch default branch for this plan. Do not add a `release` branch. |
| First public Release | **Operator-only** when they ask (usually during/after P3). Agents write the recipe; they do not push tags. |
| Infra zip pull from GitHub | **Parked** (Automated-Infrastructure §13 optional channel). v1 ships **bundled** `infra/` only. |
| LICENSE file | **Parked** (README still TBD). Installer may omit a license page. |
| Clean-room $1 fire | **Operator**, Step 9.5 / P7. Not an agent session unless spend is accepted in that chat. |

---

## GitHub Releases (operator)

You do **not** need to change branches or GitHub settings to **start** this plan (P1 is local publish). When you actually cut a release (P3 recipe; you run it, not the agent unless you ask):

1. **No Settings toggle.** Public repos already have Releases. You do not enable Actions, Pages, or a GitHub App.
2. **No new branch.** Do not create `release`. A Release is a **git tag** (for example `v0.1.0`) plus notes and files. Tag the commit you intend to ship (today that is likely `staging`, or `master` after you merge — your call). Default branch can stay as it is.
3. **Do not use pre-release** for the build the app should see. `GET /repos/maattox/oci-mc-server/releases/latest` **ignores** drafts and pre-releases.
4. **Upload the Inno `.exe`** as a Release asset. Release notes (`body`) are what P4 shows in the prompt.
5. **Pushing the tag** is a `git push` (operator). Agents will not push unless you say so in that chat.
6. **SmartScreen** is Windows reputation, not a GitHub setting. Unsigned installs will warn until a purchased cert exists (deferred).

Unauthenticated GitHub API is enough (~60 req/hr/IP). The shipped app must **not** embed a PAT.

---

## Parked (not this plan)

| Item | Why |
|------|-----|
| GitHub Actions / CI installer build | Locked out (same as 8.6). |
| Velopack / silent in-app apply | v1 is prompt-then-download. |
| Code-signing certificate purchase | Deferred; SmartScreen notes only. |
| Setup pull of newer `infra/` from GitHub | §13 optional; bundled `infra/` is enough for v1. |
| Bundling WebView2 Evergreen | Existing prereq MessageBox stays. |
| macOS / Linux Manager | After-v1. |
| LICENSE file / SPDX | README TBD; not a packaging blocker. |
| Public launch marketing | After P7 operator “ready to publish.” |
| Pack-corpus Cobblemon re-run | Separate chat; `/pack-test-one`. |
| Paid / spend mode | Not v1. |

---

## After this plan

1. [`NEXT.md`](NEXT.md) — operator declares v1 ready (P7) or names follow-on.
2. V1 dashboard Phase **9** **DONE**.
3. Guide describes installer + GitHub update check; from-source remains a developer path.
4. Do **not** start after-v1 PRODUCT-IDEAS items from this plan.

---

## P1 — Publish layout: product tree next to the exe

**Status:** DONE  
**Parallel:** SEQUENTIAL — ProductPaths + Hybrid publish  
**Cursor mode:** agent

**Read first**

- This section + [Scrutiny](#scrutiny-plan-decisions) + [What already exists](#what-already-exists-do-not-rediscover)
- `src/McManager.Core/Setup/ProductPaths.cs`
- `src/McManager.Core/Setup/FunctionImageArtifact.cs`
- `src/McManager.Hybrid/McManager.Hybrid.csproj`
- [`Local-Config.md`](Local-Config.md) — Function tar lookup paragraph only

**Do**

1. Make `dotnet publish` of `McManager.Hybrid` (`win-x64`, self-contained) produce a folder whose **app directory is a product root**: `infra/` (with `main.tf`), `onbox/mcmgr/`, `door_vm/`, `vm_agent/`, `functions/shutdown_vm/`. Exclude `.terraform/`, `*.tfstate*`, filled `terraform.tfvars`.
2. Confirm `ProductPaths.FindInfraDirectory` / on-box / door / agent / Function sources resolve from that folder **without** a git checkout (typical test: publish to a temp dir, or unit-test candidate roots). Do not add a second path API unless the current walker cannot see `BaseDirectory`.
3. If gitignored `artifacts/mcmgr-fn-softstop-linux-arm64.tar` exists on the build PC, copy it next to the published exe (or `artifacts/` under that folder — both are already in `FunctionImageArtifact` candidates). If it is missing, publish may still succeed (developer from-source); **P3** will refuse to pack an installer without it.
4. Do **not** add Inno, GitHub Releases, tofu download, or Guide rewrite beyond one sentence if publish output paths need a developer note.
5. Do **not** commit the Function tar.

**Test**

- `dotnet publish` of Hybrid (`win-x64`, self-contained) succeeds.
- From the publish folder, `infra/main.tf` and `onbox/mcmgr/common/driver.sh` exist. `ProductPaths` finds them when `BaseDirectory` is that folder (unit test and/or a short probe).
- Existing Core tests still pass.

**Done when:** A published folder is a usable product root (no repo checkout). Function tar copies when present. No installer yet.

**Changelog:** 2026-08-27 — Hybrid `dotnet publish -r win-x64 --self-contained` copies `infra/` (no `.terraform` / tfstate / filled `terraform.tfvars`), `onbox/mcmgr/`, `door_vm/`, `vm_agent/`, `functions/shutdown_vm/` next to the exe. Function tar copies from gitignored `artifacts/` when present. `ProductPaths` treats that folder as a product root (no git checkout). Living **NEXT = P2**.

---

## P2 — OpenTofu on a clean PC (pinned download)

**Status:** DONE  
**Parallel:** SEQUENTIAL — Setup uses the published layout from P1  
**Cursor mode:** agent

**Read first**

- This section + Scrutiny `tofu.exe` row
- `src/McManager.Core/Setup/OpenTofuLocator.cs`
- `src/McManager.Core/Setup/SetupDeployOrchestrator.cs` (locator call only)
- [`Automated-Infrastructure-Deployment.md`](Automated-Infrastructure-Deployment.md) **§13** OpenTofu binary paragraph only
- [`Guide.md`](Guide.md) — Install the Manager / Setup Deploy tofu mentions only if present

**Do**

1. Keep PATH / WinGet / existing LocalAppData lookup.
2. If still missing, **download once** a **pinned** OpenTofu Windows amd64 zip from the official OpenTofu GitHub Releases, verify **SHA-256**, extract `tofu.exe` to `%LOCALAPPDATA%\McManager\tofu\tofu.exe`. Pin version + checksum in code or a small tracked JSON (not a live “latest” float).
3. Replace the WinGet-only missing message. Users of the installer must not be told to install WinGet/OpenTofu by hand. Include MPL 2.0 license text or a pointer next to the binary / in Guide.
4. Never download HashiCorp `terraform.exe`. Do not vendor the OCI provider in this step (`tofu init` on first Setup stays).
5. Unit-test: fake HTTP / temp dir; checksum mismatch refuses to run the file; success path finds the extracted exe.

**Test**

- Locator tests with mock download.
- `dotnet build` / Core tests.

**Done when:** A PC with no WinGet OpenTofu can obtain a checksummed `tofu.exe` into LocalAppData. Guide sentence matches.

**Changelog:** 2026-08-27 — Pinned OpenTofu **1.12.6** Windows amd64 zip (SHA-256) downloads once into `%LOCALAPPDATA%\McManager\tofu\tofu.exe` when PATH/WinGet/existing copy are missing. Checksum mismatch does not install. MPL 2.0 notice next to the binary + Guide. No WinGet. Living **NEXT = P3**.

---

## P3 — Windows installer (Inno) + Function tar + Release recipe

**Status:** DONE  
**Parallel:** SEQUENTIAL — wraps P1 publish output  
**Cursor mode:** agent

**Read first**

- This section + Scrutiny installer / signing / GitHub rows + [GitHub Releases (operator)](#github-releases-operator)
- P1 publish output (do not re-litigate layout)
- [`Guide.md`](Guide.md) § **3. Install the Manager**
- `src/McManager.Hybrid/WebView2RuntimeGuard.cs` (prereq copy; do not bundle)

**Do**

1. Add a tracked Inno Setup 6 script (and a small pack script, PowerShell OK) that: publishes Hybrid (P1), copies the Function tar from `artifacts/` (**fail if missing**), builds **one** per-user installer `.exe`. Install dir under `%LOCALAPPDATA%\Programs\` (or Inno’s per-user default). Start Menu shortcut named **MC Manager**. Uninstall registered.
2. Do not require admin. Do not install to Program Files. Do not bundle WebView2. Do not bundle `tofu.exe` (P2 download). Do not commit the Function tar or the built installer.
3. Document in Guide: install the `.exe`; WebView2 Evergreen if prompted; unsigned / SmartScreen “unknown publisher” is expected until a cert exists; Function tar is inside the install (no Docker).
4. Add a short **operator Release recipe** (Guide or `docs/Operator-Troubleshooting.md`): tag `vX.Y.Z`, GitHub → Releases → Draft, attach the Inno `.exe`, **not** pre-release, publish. No branch rename. No Actions. **Do not run** `git push` / `gh release create` in this step unless the operator asks.
5. Code-signing: a short Guide/Troubleshooting note only (purchase deferred).

**Test**

- Pack script fails clearly when the Function tar is absent.
- On a pack PC with the tar present: installer builds. Install on this Windows user (or a throwaway folder) → shortcut starts Manager; `infra/` and the Function tar exist under the install dir; Setup can see them (`ProductPaths` / artifact finder). Config still honors `MCMANAGER_CONFIG_DIR` for QA.
- Uninstall removes the Start Menu entry.

**Done when:** Reproducible installer artifact. Guide has install + SmartScreen + operator Release recipe. No GitHub Release created unless the operator asked.

**Changelog:** 2026-08-27 — Inno Setup 6 per-user installer (`packaging\McManager.iss` + `packaging\pack.ps1`). Pack publishes Hybrid, **fails** without `artifacts/mcmgr-fn-softstop-linux-arm64.tar`, writes `packaging\out\MCManager-Setup-<version>.exe` (gitignored). Start Menu **MC Manager**. No admin / Program Files / WebView2 / `tofu.exe`. Guide §3 + operator Release recipe (not pre-release; no Actions; no tag push). Living **NEXT = P4**.

---

## P4 — GitHub Releases update check

**Status:** DONE  
**Parallel:** SEQUENTIAL — Hybrid launch + existing settings toggle  
**Cursor mode:** agent

**Read first**

- This section + Scrutiny auto-apply row
- `src/McManager.Core/Config/AppSettingsDocument.cs` / `AppSettingsStore.cs`
- `src/McManager.Hybrid/Components/Layout/MainLayout.razor` (Updates settings block only)
- `src/McManager.Hybrid/ViewModels/ChromeViewModel.cs` (version + GitHub URL)
- `src/McManager.Hybrid/App.xaml.cs` (startup)
- [`Local-Config.md`](Local-Config.md) — `app-settings.json` row

**Do**

1. If `check_for_updates` is true, on launch (after UI is up; not a silent OCI probe) `GET https://api.github.com/repos/maattox/oci-mc-server/releases/latest` with a descriptive User-Agent. No token.
2. Compare latest **tag** (strip a leading `v`) to the running informational/assembly version. If newer: prompt with **release name + notes** (`body`) and an action that opens the Release HTML URL (or the installer asset URL). If equal/older, or toggle off: no prompt.
3. Offline / 404 / rate-limit: **no crash**; fail quiet or one dismissible note. Dismiss does not retry in a loop that chatters GitHub.
4. Replace the settings copy “checks are not live yet.” Keep the checkbox as the SoT.
5. Unit-test the compare + JSON parse with fixtures (including pre-release ignored because `/latest` already skips them; still fixture a draft-shaped body). Do not hit live GitHub from tests.

**Test**

- Fixture: newer tag → prompt payload; same tag → no prompt; toggle off → no HTTP (or no prompt).
- Manual: toggle on with airplane mode → app still opens.

**Done when:** Update check ships. Settings copy matches. No Velopack.

**Changelog:** 2026-08-27 — Unauthenticated `GET /repos/maattox/oci-mc-server/releases/latest` after UI is up when `check_for_updates` is on. Newer tag (strip `v`) → confirm with release name + notes and **Open download** (HTML URL). Equal/older, toggle off, 404, 429, and offline → no prompt, no retry loop. Settings copy matches. Fixture tests; no live GitHub. No Velopack. Living **NEXT = P5**.

---

## P5 — Guide + README v1 pass

**Status:** DONE  
**Parallel:** SEQUENTIAL — docs after P1–P4  
**Cursor mode:** agent

**Read first**

- [`Guide.md`](Guide.md)
- [`README.md`](../README.md)
- This plan Scrutiny + [GitHub Releases (operator)](#github-releases-operator)
- [`PRODUCT-IDEAS.md`](PRODUCT-IDEAS.md) → **Delivery packaging** only (do not invent features)

**Do**

1. One consistency pass: installer vs from-source; private allowlist (no public mode); spend-brake lock; Function image = pre-built tar copied into OCIR (**users do not need Docker**); update check + settings toggle; unsigned/SmartScreen; WebView2 prereq.
2. README status: packaging is in Phase 9 / installer exists once P3 is DONE; drop stale “do not start 9.1 until 8.6.1” if still present.
3. Do not invent later PRODUCT-IDEAS features. Do not rewrite the whole Guide.

**Test**

- Read-through as a first-time admin.

**Done when:** Guide and README match shipped v1 packaging behavior.

**Changelog:** 2026-08-28 — Guide + README match shipped v1 packaging (installer vs from-source, no Docker for users, allowlist, spend-brake lock, update prompt, SmartScreen, WebView2). PRODUCT-IDEAS Delivery packaging still says “auto-update” / “9.1 bundles” (vision tense; not rewritten). Living **NEXT = P6**.

---

## P6 — Closed beta / dogfood

**Status:** NEXT  
**Parallel:** SEQUENTIAL — operator-led  
**Cursor mode:** either

**Read first**

- This section
- [`Guide.md`](Guide.md) (as shipped in P5)
- [`NEXT.md`](NEXT.md)

**Do**

1. **Operator:** give friends the **installer** if P3 exists (`dotnet run` is OK only as a fallback). Play on the reserved IP. Keep **$0**. TESTING vs a friend’s tenancy is the operator’s call; agents stay on **TESTING** unless this chat authorizes otherwise.
2. **Agents:** fix **v1-blocking** bugs only. Do not start after-v1 features. Do not `tofu destroy`. Do not fire a real $1 budget (P7).
3. File on-box quirks in [`Issues.md`](Issues.md) when they are product bugs.

**Test**

- Multi-friend join; wake from cold; idle stop; at least one Modded or Paper path if that is what they run.

**Done when:** Operator says no v1-blocking bugs remain (or defers them in writing).

**Changelog:** *(empty)*

---

## P7 — V1 exit review

**Status:** TODO  
**Parallel:** SEQUENTIAL — operator declares ready  
**Cursor mode:** either

**Read first**

- [`PRODUCT-IDEAS.md`](PRODUCT-IDEAS.md) v1 table only
- [`VM-Software.md`](VM-Software.md)
- [`README.md`](../README.md)
- This plan parked table

**Do**

1. Tick the v1 table against what shipped. Confirm **later** items were not scoped in. Update `README.md` + `VM-Software.md` if build-vs-planned drifted.
2. **Operator (not agents):** clean-room test in PRODUCT-IDEAS (new account + installer + Setup + $1 brake **including lock UX**) only if they accept ~$1–$2 residual. Prefer a spare PC / local VM. Not on the long-lived lab tenancy unless they say so.
3. Operator declares v1 ready to publish (or names remaining blockers).

**Done when:** Operator says v1 is ready (or explicitly pauses). Living NEXT leaves this plan.

**Changelog:** *(empty)*

---

## Plan changelog

| Date | Note |
|------|------|
| 2026-08-28 | **P5 DONE** (Guide + README v1 pass: installer vs from-source, users no Docker, allowlist, spend-brake lock, update prompt, SmartScreen, WebView2). Living **NEXT = P6** (closed beta / dogfood). Do not start P7. |
| 2026-08-27 | **P4 DONE** (GitHub Releases update check: prompt + notes; no silent apply). Living **NEXT = P5** (Guide + README v1 pass). Do not start P6. |
| 2026-08-27 | **P2 DONE** (pinned OpenTofu 1.12.6 Windows amd64 download + SHA-256 into LocalAppData). Living **NEXT = P3** (Inno installer). Do not start P4. |
| 2026-08-27 | **P1 DONE** (publish layout: product tree + optional Function tar next to the exe). Living **NEXT = P2** (pinned OpenTofu download). Do not start P3. |
| 2026-08-27 | Created (docs only). Operator unblocked Phase 9. **NEXT = P1** (publish layout). GitHub Actions out; Inno + prompt-on-Release; tofu pinned download; signing deferred. |
