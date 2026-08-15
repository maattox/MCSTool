# MVP Implementation Progress — Avalonia Manager

**Purpose:** Living notes on what has been **implemented** in the Avalonia product (`OCI-mc-server`) and its MVP contract/on-box phases, and how that work behaves today.  
**Not authority for intent:** product goals still live in lab `PRODUCT-IDEAS.md` and the checklist in [`MVP-Implementation-Plan.md`](MVP-Implementation-Plan.md). This file records **as-built/as-frozen** behavior so agents and the operator do not rediscover completed steps.  
**Update policy:** Append or revise sections when an MVP plan step completes. Broader docs (README, lab `VM-Software.md`, etc.) wait until the relevant exit gate / operator asks.

**As of:** 2026-08-14 (Phase **1 DONE**; Phase **2 DONE**; Phase **3 DONE** through Step **3.3** apply/bootstrap + blank-tenancy operator test; **NEXT = Phase 4** Connect-existing).

---

## Snapshot

| Step | Title | Status |
|------|--------|--------|
| 0 | Foundation (scaffold, local config) | DONE (pre-session) |
| **1.1** | OCI session + Core service skeleton | **DONE** (+ 2026-08-11 OCI-API backfill) |
| **1.2** | Shell layout: top bar + tabs | **DONE** |
| **1.3** | Whitelist → Security List sync | **DONE** (operator-tested) |
| **1.4** | Status polling + door-aware Start/Stop/Restart | **DONE** (operator-tested) |
| **1.5** | Usage / budget (Object Storage) | **DONE** (operator-tested) |
| **1.6** | Backups list / download / upload-replace | **DONE** (operator-tested) |
| **1.7** | Advanced / Danger Zone (idle) | **DONE** (operator-tested) |
| **1.8** | Manage MVP exit gate | **DONE** (operator signoff) |
| **2.1** | Document OS / meta / ledger contracts | **DONE** (live-bucket reviewed) |
| **2.2** | Infra meta object (`meta/infra.json`) | **DONE** (live migrated + round-trip) |
| **2.3** | Vanilla on-box path readiness | **DONE** (offline dry-run + fixtures) |
| **2.4** | Door / agent product gaps | **DONE** (force OS pull; §10.2 sync; oversized flag; OS-ISSUE-6 deferred) |
| **3.1** | OpenTofu module skeleton | **DONE** (`tofu validate`; no apply) |
| **3.2** | Setup wizard UX (no apply) | **DONE** (walkable) |
| **3.3** | Apply + bootstrap + capacity wait | **DONE** (code + blank-tenancy operator test 2026-08-14) |
| 4+ | Connect-existing | NEXT |

**Run:**

```powershell
dotnet run --project "C:\Users\matto\Desktop\Minecraft Server\OCI-mc-server\src\McManager.App"
```

**Config:** gitignored `data/config.local.json` + `data/friends.local.json` at the **product repo root** (next to `AGENTS.md` / `config.local.example.json`), not under `src/`. See [`Local-Config.md`](Local-Config.md).

---

## Architecture (as built)

```text
McManager.App (Avalonia 12, Fluent, CommunityToolkit.Mvvm)
  MainWindow + MainViewModel          top bar, poller, power commands, Today usage
  FirstRunWindow                      missing-config chooser (Setup vs existing stack)
  SetupWizardWindow                   9-step collect / resume / Deploy (LocalAppData tofu + SSH bootstrap)
  WhitelistViewModel                  friends CRUD / Save / Sync
  UsageViewModel                      OS ledger/budget pull + publish; 2 min tab poll
  ServerManagementViewModel           backups list / download / upload / SSH replace
  AdvancedViewModel                   break-glass Compute + Danger Zone idle + infra meta publish + Setup entry
  SetupWizardViewModel                9-step greenfield collect / resume / static plan (no tofu)
        |
        v
McManager.Core
  Config/     LocalConfigStore, SetupWizardStore, ManagerLocalConfig, FriendRules, FriendsLocalFile
  Oci/        OciSession (auth + clients + RetryConfiguration)
  Setup/      MojangVersionCatalog, OciConfigProfiles, SshKeyHelper, WindowsCredentialStore, InfraPlanSummary
  Usage/      Ledger/Budget/Flags/InfraMeta DTOs + UsageMath.BudgetReport
  Services/   Compute, SecurityList, ObjectStorage (+ stream), UsageBudgetStore, InfraMetaStore, BackupStore, DoorClient, SshService (restart/replace/idle), …
```

**Auth:** Desktop user API key via `~/.oci` config file + PEM (`oci.config_file` / `oci.profile` / `oci.region` from local JSON). Not instance principals.

**Cost / thrift:** Follow [`OCI-API-Usage.md`](OCI-API-Usage.md). No silent OCI Get on app launch. Background OCI GetInstance ~30s focused / ~2 min unfocused; door HTTP ~15s / ~2 min. Usage Object Storage: refresh on tab open + ~2 min while Usage selected (stop on leave); flag-aware ledger pull. Server Management: list on tab open only. Advanced: budget pull + `meta/infra.json` refresh on tab open (Refresh buttons; no background OS poll).

**Shared contracts (Phase 2):** [`Contracts-Object-Storage.md`](Contracts-Object-Storage.md) is the frozen Object Storage SoT for Avalonia, VM1, door, Setup, and Connect existing.

---

## Step 1.1 — OCI session + Core skeleton

### What shipped

| Piece | Path / type | Role |
|-------|-------------|------|
| Packages | `McManager.Core.csproj` | `OCI.DotNetSDK.Common`, `.Core`, `.Objectstorage` (144.0.0); later + `SSH.NET` in 1.4 |
| Session | `Oci/OciSession.cs` | `ConfigFileAuthenticationDetailsProvider`, region from config, `ComputeClient` / `VirtualNetworkClient` / `ObjectStorageClient` |
| Retry | Same | `RetryConfiguration`: exponential backoff, retry **429 TooManyRequests** + 5xx families, `TotalElapsedTimeInSecs = 60` |
| Errors | `Services/OciErrorFormatter.cs` | Appends **`opc-request-id`** when present; rate-limit wording |
| Compute | `Services/ComputeService.cs` | Originally `GetLifecycleStateAsync`; expanded in 1.4 |
| Security List | `Services/SecurityListService.cs` | `GetDisplayNameAsync`; expanded in 1.3 |
| Object Storage | `Services/ObjectStorageService.cs` | Get/Put/List; later ListDetailed + stream download/upload (**1.6**) |
| Door HTTP | `Services/DoorClient.cs` | `GetStatusAsync`, `WakeAsync`, `IdleEmptyAsync` against `DoorAdminBaseUrl` |
| Results | `Services/ServiceResult.cs` | `Ok` / `Fail` wrappers |

### 2026-08-11 backfill (before 1.4)

- Wired SDK retry on all three clients (Step 1.1 plan text + OCI-API-Usage).
- Centralized OCI error formatting with `opc-request-id`.
- **Removed** silent startup `GetInstance` from UI init (MVP: no silent OCI probing on startup). Status comes from the Step 1.4 poller after the window is up.

### Config discovery fix (same era)

`LocalConfigStore.TryFindDataDirectory` used to stop at `src/McManager.slnx` and create empty `src/data/`, missing repo-root `data/config.local.json`. It now:

1. Prefers existing `data/config.local.json` while walking up  
2. Else creates `data/` at repo root marked by `AGENTS.md` or `config.local.example.json`  
3. Else falls back next to the `.slnx`  

Override: `MCMANAGER_CONFIG_DIR`.

---

## Step 1.2 — Shell layout

### UI structure

[`MainWindow.axaml`](../src/McManager.App/Views/MainWindow.axaml):

- **Top bar:** status text, action feedback, **Start / Stop / Restart**, Play IP (+ **Copy** from 1.4), Players (`—`), Today (Usage refresh from **1.5**)
- **Tabs:** Whitelist | Usage | Server Management | Advanced / Danger Zone  
- No right-side bell / settings / overflow (deferred to v1)

Placeholder tabs until later steps (all filled through 1.7):

| Tab | File | Filled in |
|-----|------|-----------|
| Whitelist | `Views/Tabs/WhitelistView.*` | **1.3** |
| Usage | `Views/Tabs/UsageView.*` | **1.5** |
| Server Management | `Views/Tabs/ServerManagementView.*` | **1.6** |
| Advanced | `Views/Tabs/AdvancedView.*` | Break-glass **1.4**; Danger Zone idle **1.7**; infra meta **2.2** |

Theme: Fluent (`App.axaml`). Window ~960×640.

**Tab selection wiring (1.5–1.7):** `MainWindow` `TabControl.SelectionChanged` → `MainViewModel.OnMainTabChanged(index)` only when the **tab index actually changes** (nested `ListBox` selection bubbles SelectionChanged — guard with `_lastMainTabIndex` added during 1.6 fixes). Indices: 0 Whitelist, 1 Usage, 2 Server Management, 3 Advanced.

---

## Step 1.3 — Whitelist → Security List sync

### Local friends

- Load: optional `data/friends.local.json` via `LocalConfigStore.Load()`  
- Save: `LocalConfigStore.SaveFriends(...)`  
- DTO: `FriendsLocalFile` / `FriendEntry` (`id`, `name`, `ip`, `is_admin`)  
- UI rows: mutable `FriendRowViewModel`  

### Ownership helpers

`Config/FriendRules.cs` — IPv4 normalize / `/32`, descriptions:

| Rule | Description |
|------|-------------|
| Minecraft TCP+UDP | Friend **name** (or bare IP if name empty) |
| Admin SSH `:22` | `"{name} SSH access"` |
| Admin door `:8080` | `"{name} door access"` (product emits; lab Python only preserved) |
| Legacy | `mc-whitelist:…`, `mc-ssh-admin` |

### Sync algorithm (`SecurityListService.ApplyFriendsAsync`)

1. `GetSecurityList` by OCID from config  
2. **Drop** owned descriptions + legacy **only** when dest is MC/SSH/door port **and** source is a `/32` (preserves VCN non-`/32` Minecraft rules and ICMP / other ports)  
3. Append rebuilt owned rules for each friend  
4. `UpdateSecurityList` with **full** new ingress list (egress untouched)  

**Product choice:** Security List **only** — no firewalld sync (matches PRODUCT-IDEAS lean).

### Whitelist UI

- List + Add / Update / Remove  
- **Save** (local JSON)  
- **Sync to OCI** (short-lived `OciSession`)  
- **Update admin IP:** detect via ipify / ifconfig.me / icanhazip (`PublicIpDetector`) or paste → update admin friend (`admin_name` or first `IsAdmin`) → save + sync  

### Related

`WhitelistViewModel` held on `MainViewModel.Whitelist`; tab `DataContext` bound to it.

---

## Step 1.4 — Status polling + door-aware power

### Door status model

`DoorStatus` parses `GET /api/status` (`door`, `wake_in_progress`, `last_error`, budget fields as available).

| `door` value | Top-bar label |
|--------------|----------------|
| `DOOR_IDLE` | Idle (doorbell) |
| `STARTING` / `wake_in_progress` | Starting |
| `PLAYABLE` | Playable |
| `BUDGET_EXHAUSTED` | Budget exhausted |
| `DEGRADED` | Degraded (+ `last_error`) |

Cross-check: door `PLAYABLE` but VM1 lifecycle `STOPPED`/`STOPPING` → “Degraded / recovering…”.  
**Players** stay `—` (door admin API does not expose Minecraft online count; Players tab out of MVP).

### Power commands (top bar)

| Control | Behavior |
|---------|----------|
| **Start** | `POST /api/wake` on door ephemeral (`http://{door.ssh_host}:{door.http_port}`), then poll door until Playable / Degraded / Budget exhausted |
| **Stop** | `POST /api/idle-empty` (SoftStop + IP handback to door), then poll until Idle / Degraded / Exhausted |
| **Restart** | SSH `sudo systemctl restart {minecraft_unit}` via `SshService` (`vm1.ssh_*`); enabled only when VM1 lifecycle is `RUNNING` |
| **Copy** | Clipboard ← `play.reserved_public_ip` |

Enable rules (approx.): Start when not already Starting/Playable; Stop when Playable/Starting or VM RUNNING; Restart when RUNNING; all gated by `IsBusy`.

### Polling (`MainViewModel`)

| Source | Focused | Unfocused / minimized |
|--------|---------|------------------------|
| Door `GET /api/status` | ~**15s** | ~**2 min** |
| OCI `GetInstance` lifecycle | ~**30s** | ~**2 min** |

- Long-lived `OciSession` + `DoorClient` + `ComputeService` after config load; disposed on window close  
- Focus tracked via window Activated / Deactivated / Minimized (`MainWindow.axaml.cs` → `SetWindowFocused`)  
- After Start/Stop, dedicated door wait loop (backoff, ~20 min cap), not only the slow background timer  
- Compute also has `WaitForLifecycleAsync` for Advanced break-glass (few sec → ≤30s between polls, ~20 min)

### Advanced break-glass (minimal in 1.4; expanded in 1.7)

`AdvancedViewModel` + `AdvancedView`:

- **Raw VM Start** / **Raw VM SoftStop** via OCI `InstanceAction`  
- Does **not** move reserved play IP — UI warns to prefer top-bar door path  
- Idle / Danger Zone controls added in Step **1.7** (same tab)

### Important operational notes

- Door `idle-empty` SoftStop does **not** run the Manager/idle-agent cold world backup path (lab **OS-ISSUE-6**). Prefer door Stop for correct IP handback; backups still rely on on-box SoftStop / agent paths when those run.  
- Lab Python Force Start/Stop are bare Compute and also do not move the play IP; Avalonia top bar intentionally uses the door.  
- After testing power flows, use top-bar **Stop** so VM1 does not keep burning Always Free hours with IP stranded on the wrong host.

---

## File map (Core + App, Phase 1 through 2.2)

### McManager.Core

| Path | Notes |
|------|--------|
| `Config/ManagerLocalConfig.cs` | Full local JSON schema; budget + OS prefixes; `DoorAdminBaseUrl` |
| `Config/LocalConfigStore.cs` | Load config/friends; `SaveFriends`; path expand/validate |
| `Config/FriendsLocalFile.cs` | Friends DTO |
| `Config/FriendRules.cs` | IP + owned-rule description helpers |
| `Oci/OciSession.cs` | Auth, clients, retry |
| `Usage/UsageLedgerDocument.cs` | `ledger/usage.json` intervals + `daily_overrides` |
| `Usage/BudgetConfigDocument.cs` | `budget/config.json`; `FromLocal` / `StampUpdated` / LA daily derive |
| `Usage/MetaFlagsDocument.cs` | `meta/flags.json`; dirty mark/clear/normalize |
| `Usage/InfraMetaDocument.cs` | Nested `meta/infra.json` v2 + nested sections; `FromLocal` / validate (**2.2**) |
| `Usage/BudgetReport.cs` / `UsageMath.cs` | Lab-parity budget math (UTC month) |
| `Services/ServiceResult.cs` | Result type |
| `Services/OciErrorFormatter.cs` | OCI error + opc-request-id; `IsNotFound*` helpers |
| `Services/ComputeService.cs` | Get lifecycle, Start, SoftStop, WaitForLifecycle |
| `Services/SecurityListService.cs` | GetDisplayName, ApplyFriends |
| `Services/ObjectStorageService.cs` | Get/Put/List; **ListDetailed**; **stream** Download/Upload |
| `Services/ObjectStorageObject.cs` | Name / SizeBytes / TimeCreated |
| `Services/IObjectStorageService.cs` | Interface including stream APIs |
| `Services/UsageBudgetStore.cs` | Pull flags/budget/ledger; PublishBudget dirty protocol |
| `Services/InfraMetaStore.cs` | Get/publish `meta/infra.json`; legacy detect; dirty `meta` (**2.2**) |
| `Services/BackupStore.cs` | World zip list / soft-cap check / upload naming |
| `Services/DoorClient.cs` / `DoorStatus.cs` | Door admin HTTP |
| `Services/SshService.cs` | Restart MC; ReplaceWorld; ApplyIdleSettings |
| `Services/PublicIpDetector.cs` | Admin IP detect |
| `Services/SecurityListApplyResult.cs` | Sync summary |

### McManager.App

| Path | Notes |
|------|--------|
| `ViewModels/MainViewModel.cs` | Shell, poller, power, Copy IP; owns Whitelist/Usage/ServerManagement/Advanced; wires `InfraMetaStore` |
| `ViewModels/WhitelistViewModel.cs` | Friends CRUD / Save / Sync / Update admin IP |
| `ViewModels/FriendRowViewModel.cs` | Editable friend row |
| `ViewModels/UsageViewModel.cs` | Usage dashboard + budget edit/publish; 2 min poll |
| `ViewModels/ServerManagementViewModel.cs` | Backups list / download / upload / replace |
| `ViewModels/AdvancedViewModel.cs` | Break-glass Compute + Danger Zone idle + infra meta Refresh/Publish |
| `Dialogs/ConfirmDialog.cs` | Shared confirm window (custom OK label) |
| `Views/MainWindow.axaml(.cs)` | Layout; focus/dispose; **tab-index change guard** |
| `Views/Tabs/*` | Tab user controls |

### Product docs added in Phase 2

| Path | Notes |
|------|--------|
| [`Contracts-Object-Storage.md`](Contracts-Object-Storage.md) | Frozen OS/meta/ledger contracts (**2.1**) |
| [`Automated-Infrastructure-Deployment.md`](Automated-Infrastructure-Deployment.md) | Phase 3 IaC authority (2026-08-12): OpenTofu on admin PC; RM discovery = operator reference only |
| [`Lab-Reference-Stack-Notes.md`](Lab-Reference-Stack-Notes.md) | Sanitized lab dump digest: `mcmgr-…` names, 3 DGs, skip NAT, Events→Function (ONS leftover) |
| [`Local-Config.md`](Local-Config.md) | Local seeds + Step 2.2 publish note (updated with infra meta) |
| [`OCI-API-Usage.md`](OCI-API-Usage.md) | 429 / waiter / pagination thrift (Phase 1 era; still current) |
| [`../infra/`](../infra/) | Step **3.1** OpenTofu root (`oracle/oci` 8.27.0); see [`infra/README.md`](../infra/README.md) |

---

## Step 1.5 — Usage / budget view (Object Storage)

**Status:** DONE (operator-tested).

### Object keys (config prefixes)

| Object | Default key | Access |
|--------|-------------|--------|
| Flags | `{meta}flags.json` → `meta/flags.json` | GET / PUT |
| Budget | `{budget}config.json` → `budget/config.json` | GET / PUT |
| Ledger | `{ledger}usage.json` → `ledger/usage.json` | GET (manager does not publish ledger in 1.5) |

`ledger/lease.json` is **not** used in this step.

### Core DTOs (`McManager.Core/Usage/`)

**`UsageLedgerDocument`** — v2 ledger: `intervals[]` (`id`, `started_at`, `stopped_at`, `ocpus`, `memory_gb`, sources), `daily_overrides` map, `idle_since`, `last_budget_warn_at`.

**`BudgetConfigDocument`** — shared budget SoT fields:

| Field | Role |
|-------|------|
| `monthly_ocpu_target` / `monthly_gb_target` | Monthly allowances |
| `soft_ocpu_cap` / `soft_gb_cap` | Soft monthly caps |
| `idle_timeout_minutes` / `budget_warn_minutes` | Idle agent timing |
| `idle_agent_enabled` | Flag in OS (Danger Zone is preferred apply path in 1.7) |
| `shape_ocpus` / `shape_memory_gb` | Shape metadata |
| `daily_ocpu_limit_phase_a` | Door wake daily share; stamped as monthly ÷ LA-month days |
| `mode` | `"always_free"` |

**`MetaFlagsDocument`** — categories `ledger` / `budget` / `meta` / `ip` / `messages` × consumers `manager` / `door` / `vm1`. Helpers: `Normalize`, `IsDirty`, `MarkDirty`, `ClearFlag`, `SummarizeBudgetFlags`.

### Math (`UsageMath.ComputeBudgetReport`)

Port of lab `app/usage.py` `compute_budget_report` (**UTC** calendar month):

- Daily OCPU/GB allowance = monthly target ÷ days-in-month  
- Per-day totals from intervals (clipped to day window; open intervals use `now`) or `daily_overrides`  
- MTD OCPU-h / GB-h / instance-h; today vs daily; leftover bank = sum of **prior** days’ unused daily allowance  
- Soft-cap hit when MTD OCPU or GB ≥ soft cap  
- Avg hours/day = `month_uptime / day_of_month`  
- Top bar string: `{today:F1}/{daily:F1} OCPU-h` (label “Today:” is already in the shell)

### `UsageBudgetStore`

- **`PullAsync(forceLedger, previousLedger)`**  
  1. GET flags (missing → empty normalized)  
  2. GET budget (missing → null; UI falls back to local)  
  3. GET ledger if `forceLedger` **or** `ledger.manager` dirty; else keep cached ledger  
  4. After successful ledger pull when dirty/forced: clear `ledger.manager` and PUT flags  
- **`PublishBudgetAsync(doc)`** — stamp `updated_at` + `daily_ocpu_limit_phase_a`; PUT budget; `MarkDirty(budget, door+vm1, clear manager)`; PUT flags  

Uses shared long-lived `ObjectStorageService` + session retry / `OciErrorFormatter`.

### Usage UI

`UsageViewModel` + `UsageView.axaml`, `DataContext` from `MainViewModel.Usage`.

**Dashboard (read-only):** month label; monthly targets; soft caps; MTD OCPU/GB/instance-h; avg h/day; leftover bank; today vs daily; soft-cap hit; last refresh time; status/errors.

**Editable form:** monthly OCPU/GB targets, soft caps, idle timeout, budget warn, shape OCPU/memory, idle_agent_enabled checkbox. Seed from OS budget; fallback `config.local.json` `budget` + `vm1` shape.

**Actions:**

| Control | Behavior |
|---------|----------|
| Refresh | Force ledger pull + recompute |
| Save / Publish | Confirm (“publishes to Object Storage and notifies door + VM1”) → `PublishBudgetAsync` → show dirty-flag summary → force refresh |
| Tab open (index 1) | Force pull + start **120s** `DispatcherTimer` |
| Tab leave | Stop timer |

**Top bar:** `MainViewModel.TodayUsageDisplay` updated on each successful Usage refresh via callback.

### Out of scope (1.5)

Interval editor / manual `daily_overrides`; SSH ledger push/pull; lease heartbeat apply; Danger Zone idle apply UX (1.7).

### Operator test (passed)

1. Open Usage — numbers align with Python Manager / Console objects.  
2. Stay on tab ~2 min — refresh fires; leave tab — no further Usage OS calls.  
3. Edit soft cap or idle timeout → confirm → Publish → Console budget updated; `meta/flags.json` `budget.door`/`budget.vm1` true.  
4. Top bar Today updates after refresh.

---

## Step 1.6 — Backups list / download / upload-replace

**Status:** DONE (operator-tested), including post-ship fixes for selection, soft-cap refuse, and multi‑GiB download.

### Context (lab gap)

VM1 uploads automatic SoftStop/live backups as `backups/world-{UTC}.zip` (`vm_agent/world_backup.py`). There is **no** on-box consumer that applies a pending world from Object Storage via dirty flags. Avalonia MVP therefore:

| Action | Path |
|--------|------|
| List / Download | Object Storage only |
| Upload | New `backups/world-{yyyyMMddTHHmmssZ}.zip` |
| Replace live world | **SSH** when VM1 `RUNNING` |
| Flag-driven OS→VM1 apply | Deferred (Phase 2 contract freeze) |

### Object Storage API extensions

`ObjectStorageService` / `IObjectStorageService`:

| Method | Role |
|--------|------|
| `ListDetailedAsync(prefix)` | Paginated list with `name`, `size`, `timeCreated` (`Fields = name,size,timeCreated`) |
| `DownloadToFileAsync` | Stream `GetObject` → local file; **`HttpCompletionOption.ResponseHeadersRead`** (avoids HttpClient ~2 GiB buffer limit / OCI SDK issue) |
| `UploadFromFileAsync` | Stream local file → `PutObject` with optional progress |

Existing `GetBytesAsync` / `PutBytesAsync` / `ListAsync` remain for small JSON (Usage).

### `BackupStore`

- Prefix from `object_storage.prefixes.backups` (default `backups/`)  
- Soft cap from `object_storage.soft_cap_gb` (default **9.5**)  
- `ListWorldBackupsAsync` — keep `world-*.zip`; skip `.keep` / `index.json`; newest first  
- Soft-cap line: `Backups ~X / 9.5 GiB (soft cap)` (sums listed zip sizes only)  
- `EvaluateUpload` — **refuse** if zip alone > soft cap **or** current backups + zip would exceed; message: `Upload failed: selected file would exceed storage limit of {N} GB.` (no “upload anyway” — Always Free safety)  
- `UploadNewBackupAsync` — `world-{stamp}Z.zip` under prefix  
- Manager does **not** evict oldest zips (on-box SoftStop still owns eviction)

### SSH replace (`SshService.ReplaceWorldAsync`)

1. Require absolute `vm1.world_path`  
2. `sudo systemctl stop {minecraft_unit}`  
3. SFTP zip to `/tmp/mc-manager-world-replace.zip` as ubuntu  
4. `sudo bash -c`: move existing world → `world_path.bak.<stamp>`; `mkdir` + `unzip -q` **into** `world_path` (lab zips are **contents-relative**, not a nested `world/` root); `chown ubuntu:ubuntu`; remove temp zip  
5. `sudo systemctl start`  
6. On failure after stop: best-effort start again  

### Server Management UI

`ServerManagementViewModel` + `ServerManagementView.axaml` (tab index **2**).

| Control | Behavior |
|---------|----------|
| Soft-cap line | Sum of listed world zips vs config soft cap |
| List | File name, size, created UTC; **no default selection** |
| Refresh | Re-list OS |
| Download World Save | Requires selection; save picker (“ZIP files”); stream download + progress |
| Upload backup | Open picker; soft-cap gate; confirm if allowed; stream upload; refresh list |
| Replace world on VM1 | Requires lifecycle `RUNNING`; strong confirm; SSH replace |
| Tab open | List once (no 2‑min poll) |

File pickers use type label **ZIP files** (`*.zip`).

### Post-ship fixes (same step)

1. **No auto-select** first row; Download requires explicit selection.  
2. **Selection snap-back** — TabControl was refreshing on every ListBox click (bubbled SelectionChanged); fixed via tab-index guard in `MainWindow`.  
3. **Soft-cap overage** — hard refuse (no override confirm).  
4. **~2.65 GiB download** — `ResponseHeadersRead` streaming (Console PAR path is unrelated).

### Out of scope (1.6)

Oversized-world adaptive SSH download / bell (v1); on-box dirty-flag world apply; Manager-side eviction.

### Operator test (passed)

1. List matches Console `backups/world-*.zip`.  
2. Select second entry — selection sticks.  
3. Download large (~2.65 GiB) backup → valid zip on disk.  
4. Upload small zip → appears in bucket + list.  
5. Replace on RUNNING VM1 works (large zip slow over SSH is expected).  
6. Upload that would exceed soft cap → refused with clear message (no override).

---

## Step 1.7 — Advanced / Danger Zone (idle)

**Status:** DONE (operator-tested).

### Product / lab semantics

- Idle settings live in OS `budget/config.json` **and** VM1 `/etc/mc-manager/config.json`.  
- Lab enable/disable applies via SSH + `mc-idle-watch.timer`; OS publish dirties door/vm1.  
- **No** separate “disable daily guardrails only” flag — daily SoftStop is part of the idle agent. Danger Zone **idle disable** is the MVP testing switch (PRODUCT-IDEAS).  
- **OS-ISSUE-7:** every VM1 boot / Minecraft start **force-enables** idle (`record_boot.py`); rewrites local + OS budget to enabled if it was off. Disable does not survive restart by design.

### SSH (`SshService.ApplyIdleSettingsAsync`)

1. `sudo cat /etc/mc-manager/config.json` (fail clearly if missing — agent not deployed)  
2. JSON merge-patch only: `idle_agent_enabled`, `idle_timeout_minutes`, `budget_warn_minutes` (preserve other keys)  
3. SFTP write `/tmp/mc-manager-config-patch.json` → `sudo cp` + `chown root:root` + `chmod 600` → remove temp  
4. Enabled: `systemctl enable` + `start mc-idle-watch.timer`  
5. Disabled: `systemctl stop` + `disable` that timer  

### UI (same Advanced tab as break-glass)

Danger Zone section in `AdvancedView`:

- Always-visible **safety banner** (testing-only disable; boot force-enable / OS rewrite)  
- Fields: idle timeout (min), budget warn (min), idle agent enabled  
- **Refresh from Object Storage** — pull budget (no forced ledger); seed form; fallback local  
- **Apply idle settings**  
  - Disable → strong confirm (empty + daily SoftStop stop until next Minecraft boot)  
  - Enable / timeout-only → milder confirm  
  - Then `PublishBudgetAsync` (reuse Usage store; dirty door+vm1)  
  - If `Vm1Lifecycle == RUNNING` → SSH apply; else OS-only + status (“Start then Apply again…”)  
- Tab index **3** open → auto Refresh from OS  

Break-glass Raw VM Start / SoftStop unchanged.

### Overlap with Usage tab

Usage can still edit/publish idle fields inside budget JSON. **Prefer Danger Zone** for enable/disable + apply-to-VM with strong warnings.

### Out of scope (1.7)

Separate daily-only disable flag; spend mode / VM resize; redeploy idle agent installer; changing `record_boot.py`.

### Operator test (passed)

1. Open Advanced — Danger Zone seeds from OS; banner visible.  
2. Change idle timeout → Apply (RUNNING) → on-box config + OS budget updated; timer active.  
3. Disable idle → strong confirm → timer stopped/disabled; OS `idle_agent_enabled: false`.  
4. Restart Minecraft / reboot → idle force-enabled again (as designed).  
5. Apply while STOPPED → OS updated; SSH skipped with clear status.

---

## Step 1.8 — Manage MVP exit gate

**Status:** DONE (operator signoff 2026-08-11).

### Purpose

Close Phase 1: prove the Avalonia Manager can replace the Python tkinter Manager for **normal day-to-day** ops on the already-deployed Always Free stack. Setup / OpenTofu / Connect existing stay later phases.

### What this step produced (docs / status — no new product features)

| Deliverable | Detail |
|-------------|--------|
| Operator dogfood | Re-ran core flows from Steps **1.3–1.7** without opening the Python Manager for those tasks |
| Smoke checklist | Written and signed off in this section (below) |
| Lab status row | `OCI-mc-server-manager/docs/VM-Software.md` Avalonia Manager row → **manage MVP usable** |
| Plan dashboard | Phase **1 DONE**; **NEXT = Step 2.1** |
| Build audit | `dotnet build` of `McManager.App` — 0 warnings / 0 errors at exit |

No new Core services or UI tabs were added in 1.8; it is an exit gate over the wiring already shipped in 1.1–1.7.

### Phase 1 product surface (as of exit)

| Area | Avalonia capability |
|------|---------------------|
| Config | Repo-root gitignored `data/config.local.json` + `friends.local.json` |
| Whitelist | CRUD / Save; Security List–only sync; Update admin IP |
| Power | Door wake Start / idle-empty Stop; SSH Restart; Copy play IP; focus-aware poll |
| Usage | OS ledger/budget pull + publish; Today bar; ~2 min tab poll |
| Backups | List / stream download / upload; soft-cap refuse; SSH world replace when RUNNING |
| Advanced | Break-glass Compute; Danger Zone idle OS publish + SSH timer apply |

### Closeout audit

- `MainViewModel` owns Whitelist / Usage / ServerManagement / Advanced with a shared long-lived `OciSession`.
- Object Storage: `UsageBudgetStore` + `BackupStore` share one `ObjectStorageService`.
- Tab selection uses `_lastMainTabIndex` so nested `ListBox` SelectionChanged does not re-enter tab refresh.
- Door-aware top-bar power remains the play path; Advanced Raw Start/SoftStop still warn they do not move the reserved IP.
- Known operational quirks left as-is (not blockers for exit): door Stop skips cold world backup (**OS-ISSUE-6**); idle disable does not survive boot (**OS-ISSUE-7**, by design).

### Phase 1 smoke checklist (operator-passed)

| Area | Checks | Result |
|------|--------|--------|
| Config | Repo-root `data/config.local.json` + friends load | **Pass** |
| Whitelist (1.3) | CRUD / Save; Sync to Security List; non-managed rules preserved; Update admin IP | **Pass** |
| Power / status (1.4) | Idle → Start → Playable on reserved IP; Stop → Idle + IP handback; Restart MC; Copy play IP | **Pass** |
| Usage (1.5) | Dashboard matches OS ledger/budget; publish dirties door/vm1; Today bar; ~2 min poll while tab open | **Pass** |
| Backups (1.6) | List matches bucket; explicit selection; multi‑GiB download; under-cap upload; over-cap refuse; SSH replace when RUNNING | **Pass** |
| Danger Zone (1.7) | Idle timeout apply; strong disable confirm; OS + SSH timer; boot/Minecraft restart force-enables | **Pass** |

### Signoff

- **Python Manager optional for daily ops:** yes (operator confirmed Steps 1.3–1.7).
- Lab Testing2 / Python Manager remain useful for **on-box redeploy**, door Phase scripts, and diagnostics — not required for normal whitelist / power / usage / backup / idle sessions.
- **No Setup / OpenTofu / installer** in this exit — Phase 3+.
- **No** notification center / settings gear / overflow chrome (v1).

### Explicitly still out after Phase 1

Lease apply UI; interval editor; flag-driven OS→VM1 world apply; oversized-world bell + adaptive SSH download; Connect existing; Vanilla Setup bootstrap; firewalld sync; public IP mode.

### Next (at time of exit)

**Step 2.1** — Document OS / meta / ledger contracts. **Completed below.**

---

## Step 2.1 — Object Storage contract freeze

**Status:** DONE (2026-08-11). Documentation-only step (no Avalonia feature code).

### Purpose

Freeze one written Object Storage contract so Avalonia, VM1 (`vm_agent`), door (`door_vm`), Setup/OpenTofu, and Connect existing share the same keys, JSON shapes, writers, dirty-flag rules, and version fields before more on-box automation lands.

### What shipped

Tracked product doc: [`Contracts-Object-Storage.md`](Contracts-Object-Storage.md).

Authority chain recorded in that doc:

1. Product intent → lab `PRODUCT-IDEAS.md`
2. Live implementation → lab `vm_agent/` / `door_vm/` / Object Storage Phase 1–5 docs + product `McManager.Core` DTOs
3. This contract freezes the **target** shapes and explicitly labels **deployed deviations**

### Contract contents (as frozen)

#### Bucket / encoding / compatibility

- Standard tier only; greenfield bucket name `mcmgr-shared-data` (existing stacks record live name in meta).
- UTF-8 JSON + trailing newline when practical; UTC timestamps `YYYY-MM-DDTHH:MM:SSZ`.
- Per-document integer `version`; breaking shape → bump document `version`; infra compatibility → bump `infra_schema`; deployed software → change `stack_version` (independent of Manager app release).
- Prefer ETag / `If-Match` for RMW documents; ledger already uses `revision` + best-effort conditional put in lab.

#### Object index (keys frozen)

| Object | Version / status |
|--------|------------------|
| `meta/infra.json` | Canonical nested **v2** (live after Step 2.2) |
| `meta/flags.json` | v1 dirty hints — live |
| `meta/oversized-world-backup.json` | v1 reserved — VM1 set/skip = Step 2.4; Manager UX = v1 |
| `meta/world-restore-request.json` | v1 reserved — flag-driven restore; SSH fallback is current MVP |
| `meta/backup-upload-lock.json` | v1 reserved — aggregate soft-cap concurrency (not implemented) |
| `ledger/usage.json` | **v2** — live; per-interval `ocpus` / `memory_gb` required |
| `ledger/lease.json` | v1 heartbeat — live; does **not** dirty ledger flags |
| `budget/config.json` | v1 — live; Manager primary + VM1 shape/idle safety patch |
| `ip/allowlist.json` / `ip/mode.json` | v1 seeded; Avalonia Phase 1 still Security List–direct |
| `messages/chat.json` | v1 seeded; rich editor deferred |
| `backups/.keep` + `backups/world-<UTC>.zip` | Live |

#### Dirty-flag protocol (`meta/flags.json`)

Categories: `ledger`, `budget`, `meta`, `ip`, `messages`.  
Consumers: `manager`, `door`, `vm1`.

Rules: writer updates authoritative object first → sets other consumers dirty → clears own bit; consumer clears **only** its bit after successful pull. No `backups` category in v1 (normalizers would drop unknown categories).

#### Ledger / lease / budget highlights

- **Ledger v2:** `revision`, intervals with shape fields, `daily_overrides`, heal fields (`stop_uncertain`, `uncertain_reason`, …).
- **Lease v1:** active session + `last_heartbeat_at`; door heal may clear after STOPPED-only orphan close.
- **Budget v1:** monthly targets/soft caps, idle fields, `daily_ocpu_limit_phase_a` (compatibility), `mode=always_free`.
- **Canonical accounting target:** UTC day/month, honor overrides, OCPU **and** GB gates — door deviations listed as Step 2.4 work.

#### `meta/infra.json` (canonical nested v2 field groups)

Versions (`version`, `infra_schema`, `stack_version`, timestamps), `stack_name`, `mode`, region/tenancy/compartment, nested `play` / `game` / `network` / `vm1` / `door` / `object_storage` / optional `budget_brake` / `ssh` (fingerprint + `private_key_location=admin_pc_only`). **No secrets.**

#### Reserved oversized-world flag (exact key)

`meta/oversized-world-backup.json` — `status: "blocked"` means automatic OS world backups must stop until cleared. Full Manager bell + adaptive SSH download remains **v1**.

### Live bucket review (2026-08-11, read-only)

OCI SDK list/get against the operator bucket (no PUTs in this step):

| Observation | Result |
|-------------|--------|
| Object count | 14 total |
| Control objects | Nine seeded layout/JSON objects present |
| Backups | Two `backups/world-*.zip` (+ `.keep`) |
| Smoke | Three non-contract `smoke/*` artifacts |
| `ledger/usage.json` | v2; revision present; 79 intervals; 3 daily overrides; per-interval shape + uncertainty fields |
| `ledger/lease.json` | v1 complete field set |
| `meta/flags.json` | v1; five categories × three consumers |
| Budget / IP / messages | Match v1 seed shapes |
| `meta/infra.json` | **Legacy flat v1** at review time (`infra_schema: 1`) — migration deferred to **2.2** |
| Oversized / restore / lock | Absent (consumers not built) |

No live OCIDs/secrets copied into the tracked contract.

### Conformance gaps explicitly recorded (not silently blessed)

1. Door wake trusts clear dirty bits / existing cache (no force-validate) — target vs live; Step **2.4**.
2. Door uses LA day windows, ignores `daily_overrides`, OCPU-only gate vs Manager/VM1 UTC + OCPU/GB.
3. Budget shared writers without consistent ETag / preserve-unknown; Manager DTO can clobber VM1 shape patches.
4. Unsupported document versions / invalid safety fields not consistently rejected by all actors.
5. Phase 1 Manager upload soft-cap uses listed backup ZIP totals, not fresh whole-bucket bytes; Manager/VM1 upload can race.
6. Current VM1 eviction accepts any `.zip` under `backups/`, not only canonical `world-*.zip`.
7. Current SSH restore lacks archive preflight + rollback; flag-driven restore not implemented.
8. Oversized-world set/skip on-box remains Step **2.4**; Manager bell/adaptive SSH UX remains **v1**.

A final cross-repo audit of the contract against PRODUCT-IDEAS, live lab actors, and product DTOs found **no remaining Step 2.1 blockers** once target-vs-live gaps were explicit.

### Out of scope (2.1)

No C# DTO/UI changes; no live object writes; no door/VM1 code changes.

### Next (at time of 2.1 exit)

**Step 2.2** — implement nested `meta/infra.json` v2 read/write and migrate the live flat v1. **Completed below.**

---

## Step 2.2 — Infra meta object

**Status:** DONE (2026-08-11). Live nested v2 published; round-trip + secret scan passed.

### Purpose

Make `meta/infra.json` the Connect-existing / Setup hydration SoT: nested v2 with full OCID set, readable and publishable from the Avalonia Manager using local manage config — **without** putting secrets in Object Storage.

### What shipped

| Piece | Path | Role |
|-------|------|------|
| DTO + nested sections | `src/McManager.Core/Usage/InfraMetaDocument.cs` | Canonical nested v2; `FromLocal`; `ValidateForPublish`; `FormatSummary` |
| Store | `src/McManager.Core/Services/InfraMetaStore.cs` | `GetAsync` (missing / legacy / unsupported / v2); `PublishFromLocalAsync` + dirty `meta` flags |
| UI | `AdvancedViewModel` + `AdvancedView` | Infra meta section: summary, editable game/stack fields, Refresh / Publish |
| Wiring | `MainViewModel.Initialize` | Builds `InfraMetaStore` beside `UsageBudgetStore` / `BackupStore` on the shared OS client |

Constants: `DocumentVersion = 2`, `InfraSchema = 2`, default `stack_version = "0.1.0"`, `stack_name = "mcmgr"`, `mode = "always_free"`.

### `InfraMetaDocument` nested shape (as implemented)

| Section | Fields seeded from local config |
|---------|----------------------------------|
| Top-level | `version`, `infra_schema`, `stack_version`, `created_at`, `updated_at`, `stack_name`, `mode`, `region`, `tenancy_id`, `compartment_id` |
| `play` | `reserved_public_ip`, `reserved_public_ip_id` |
| `game` | `server_kind`, `minecraft_version`, optional `server_jar_sha1` |
| `network` | `vcn_id`, `subnet_id`, `security_list_id`, `minecraft_port`, `ssh_port` |
| `vm1` | instance/display/shape/OCPU/memory, primary+secondary private IPs + secondary OCID, `ssh_host` (nullable), `ssh_user`, `world_path`, `minecraft_unit` |
| `door` | instance/display, primary+secondary private IPs + secondary OCID, `ssh_host` (nullable), `ssh_user`, `http_port` |
| `object_storage` | `namespace`, `bucket`, `bucket_id`, `soft_cap_gb`, `backup_enabled`, full `prefixes` map |
| `budget_brake` | optional; omitted when unset |
| `ssh` | optional `public_key_fingerprint`; `private_key_location` always `"admin_pc_only"` |

### Secrets policy (`FromLocal` / publish)

**Never copied into meta:**

- `vm1.ssh_key_path` / `door.ssh_key_path`
- `oci.config_file` / API PEM material
- `rcon.password`
- Auth Tokens / private key PEM bodies

`ValidateForPublish` also rejects a `ssh.private_key_location` that looks like a local path (must stay `admin_pc_only`).

### `InfraMetaStore` behaviors

**`GetAsync`:**

1. Missing object → `Missing=true` (prompt to publish).
2. `version` or `infra_schema` **newer** than Manager supports → hard fail (do not mutate).
3. Flat / incomplete / older than nested v2 → `IsLegacy=true` + human summary (migration input only).
4. Nested v2 with supported schema → deserialize `InfraMetaDocument`.

**`PublishFromLocalAsync`:**

1. Read existing (preserve `created_at` and prior game/stack fields when UI leaves them blank).
2. Build via `InfraMetaDocument.FromLocal(...)`; `StampUpdated`.
3. `ValidateForPublish` — fail closed with field list if incomplete.
4. PUT `meta/infra.json`.
5. Load `meta/flags.json` (or empty); `MarkDirty("meta", door+vm1, clearWriter: manager)`; PUT flags.
6. Return message including whether this was a legacy/missing migration.

### Advanced UI (tab index 3)

Section **Infrastructure meta (Object Storage)** below Danger Zone:

| Control | Behavior |
|---------|----------|
| Summary line | Monospace `FormatSummary()` (or legacy/missing guidance) |
| `stack_version` | Editable; default `0.1.0` |
| `server_kind` | Editable; default `vanilla` |
| `minecraft_version` | Editable; default `unspecified` until Setup records a Mojang id |
| Refresh infra meta | `GetAsync` → seed summary + edit fields |
| Publish infra meta from local config | Confirm → `PublishFromLocalAsync` with edit fields |
| Tab open | Sequentially refresh idle settings **then** infra meta (`RefreshAdvancedTabAsync`) |

Confirm copy states explicitly that SSH keys / OCI paths / RCON are not included.

### Live migration + round-trip (passed 2026-08-11)

| Step | Result |
|------|--------|
| GET before | Legacy flat v1 detected (`infra_schema=1`) |
| Publish from local config | Nested v2 written; `meta.door`/`meta.vm1` dirty, manager clear |
| GET after | `version=2`, `infra_schema=2`; play / VM1 / door / SL / bucket fields match local config |
| Secret scan | Local key paths, OCI config path, and RCON password **not** present in published JSON |
| Build | `dotnet build` McManager.App — 0 warnings / 0 errors |

### Notes / limits

- Game defaults (`vanilla` / `unspecified`) are product-MVP placeholders; the operator lab may still run Forge until Setup records the real install.
- Phase **4** Connect existing hydrate-from-meta is **not** implemented yet — the object is now ready for it.
- Ephemeral `vm1.ssh_host` / `door.ssh_host` are cached connectivity hints and may go stale; targeted Get-by-OCID refresh remains allowed later.
- Lab Python `build_infra_meta()` remains a thinner flat seed helper; Avalonia nested v2 is the product SoT going forward.

### Out of scope (2.2)

Connect-existing auto-detect UI; writing wizard game fields from Mojang bootstrap; budget brake OCID discovery; rewriting lab `build_infra_meta` seed path.

### Next

**Step 2.3** — Vanilla on-box path readiness (Mojang manifest → jar + aarch64 Java + EULA + systemd), without requiring a Forge lab rip. **Completed below.**

---

## Step 2.3 — Vanilla on-box bootstrap

**Status:** DONE (2026-08-11) — offline-provable; no live VM install this pass.

### What shipped

Product SoT tree [`onbox/mcmgr/`](../onbox/mcmgr/):

| Piece | Role |
|-------|------|
| `common/driver.sh` | Shared stages: layout → Vanilla module → Java → EULA/RCON/properties → generic unit → final manifest |
| `modules/bootstrap-vanilla.sh` | piston-meta resolve → download/sha1 (live) or fixture placeholder (dry-run) → `server.jar` |
| `common/unit_gen.sh` + `templates/minecraft.service.in` | Generic `minecraft.service` from `launch_command` (`User=mcmgr`, `ExecStop=rcon-graceful-stop.sh`) |
| `common/rcon.sh` / `rcon-graceful-stop.sh` | `/etc/mcmgr/rcon.secret`; password never in manifest |
| `common/bootstrap-state.sh` | `/var/lib/mcmgr/bootstrap-state.json` stage tracking |
| `dry-run/run-dry-run.sh` + `assert-dry-run.sh` | Temp `MCMGR_ROOT` + fixture-backed proof |
| `tests/fixtures/game-metadata/` | Trimmed `version_manifest_v2` + `1.21.1` / `1.21.11` metadata |

Lab keeps only a doc pointer (no script duplicate) — see PRODUCT-IDEAS Vanilla bootstrap + `docs/VM-Software.md`.

### Offline proof (passed)

```bash
# Git Bash from onbox/mcmgr
MINECRAFT_VERSION=1.21.1 bash dry-run/run-dry-run.sh
MINECRAFT_VERSION=1.21.11 bash dry-run/run-dry-run.sh
```

Asserts §4.1-shaped `game-manifest.json` (`distribution=vanilla`, `loader=null`, sha1 hash, no password leak) and unit with `User=mcmgr` / `nogui` / graceful-stop.

### Limits / deferred

- No live aarch64 VM install this step (operator greenfield E2E later).
- Full idle-agent `world_path` / `minecraft_unit` sync completed in Step **2.4**.
- Paper/modded modules, OpenTofu, Setup wizard → Phase 3 / v1.

### Next

**Step 2.4** — Door / agent product gaps (incl. blueprint §10.2 idle-agent config sync from game-manifest). **Completed below.**

---

## Step 2.4 — Door / agent product gaps

**Status:** DONE (2026-08-11).

### Triage

| Item | Outcome |
|------|---------|
| OS-ISSUE-6 door SoftStop skips backup | **Deferred** with operator OK — idle SoftStop still backs up; MVP criterion is soft-cap policy |
| Door wake stale OS cache | **Fixed** — `pull_os_budget.sh --force` on wake + `/api/os-refresh` (OS-ISSUE-8) |
| Door accounting LA/OCPU-only vs Manager UTC | Deferred (not MVP blocker for wake gate with OS SoT) |
| DOOR-ISSUE-1 MOTD / OS-ISSUE-3 dual-write / OS-ISSUE-7 | Deferred / by design |

### What shipped

| Piece | Repo | Role |
|-------|------|------|
| `door_vm/src/control.c` `run_script_args(..., "--force")` | lab | Wake + os-refresh always re-pull OS ledger/budget |
| `app/door_deploy.py` prefer `door_vm/` | lab | Avoid stale `development/` deploy tree |
| `onbox/mcmgr/common/idle_agent_sync.sh` | product | After `manifest_write`, RMW idle `world_path` / `minecraft_unit` / `rcon_port` / `rcon_password` |
| `vm_agent/world_backup.py` oversized flag | lab | PUT `meta/oversized-world-backup.json` + skip while blocked (no hard SoftStop fail) |

### Verification

- Offline dry-run assert includes §10.2 idle config match.
- Live door: Phase 3 redeploy + `run_door_pull(force=True)` → `force=1` + `os-refresh {"ok":true}`.
- Live VM1: idle agent redeployed; synthetic mock set/skip for oversized path OK.

### Next

**Step 3.1** — OpenTofu module skeleton (product names). **Completed below.**

---

## Step 3.1 — OpenTofu module skeleton

**Status:** DONE (2026-08-12). Validatable HCL only; **no apply**, no live-lab import, no Setup wizard.

### What shipped

Product tree [`infra/`](../infra/) (OpenTofu, provider `oracle/oci` **8.27.0** locked):

| Piece | Role |
|-------|------|
| Root `versions.tf` / `providers.tf` / `variables.tf` / `main.tf` / `outputs.tf` | `config_file_profile` from `~/.oci`; documented wizard-bound variables |
| `modules/compartment` | Create `mcmgr` + tag `mcmgr-domain=mc-server-compartment`, or use `existing_compartment_id` |
| `modules/network` | VCN `10.0.0.0/16`, public subnet, IGW, dedicated `mcmgr-sl` (no NAT/IPv6/NSG) |
| `modules/compute` | VM1 A1 Flex (product 4/24; **TEMPORARY test default 2/12**) aarch64 + door E2.1.Micro x86; secondaries; reserved IP on door secondary |
| `modules/storage` | Private Standard `mcmgr-shared-data`; `prevent_destroy`; no objects |
| `modules/iam` | 3 DGs (compartment/tag match) + bucket-scoped policies |
| `modules/budget_brake` | $1 budget + email; Functions app + OCIR repo; Function/Events gated on `function_image` |
| `cloud-init/*.yaml.tftpl` | OS baseline only (blueprint §13.1) |
| `manifest.json` | `infra_schema=2`, `stack_version=0.1.0` |
| `README.md` + `terraform.tfvars.example` | Operator how-to; SL vs Manager split; IAM notes |

SL ingress uses Manager description conventions, then `ignore_changes = [ingress_security_rules]`. Instance `metadata` is also `ignore_changes` after create. No ONS topic. `softstop_instance_ids` defaults to both VMs. Greenfield `world_path` output is `/opt/mcmgr/server/world`. `output.infra_meta_skeleton` matches nested `meta/infra.json` v2 (no secrets).

### Verification

```powershell
cd infra
tofu init
tofu validate   # Success
```

`tofu plan` skipped — no gitignored `terraform.tfvars` yet. **Do not apply** on the live lab tenancy.

### Out of scope (3.1)

Setup wizard; apply; SSH bootstrap; OCIR image push; seeding Object Storage JSON; writing `config.local.json`.

### Next

**Step 3.2** — Setup wizard UX (collect variables, plan summary, resume state; still no apply). **Completed below.**

---

## Step 3.2 — Setup wizard UX (no apply)

**Status:** DONE (2026-08-12). Walkable Avalonia wizard; **no** `tofu plan`/`apply`, **no** `infra/terraform.tfvars` write, **no** `config.local.json` overwrite.

### What shipped

| Piece | Role |
|-------|------|
| `SetupWizardState` / `SetupWizardStore` | Gitignored `data/setup-wizard.local.json` resume (step + fields; version **id** only) |
| `MojangVersionCatalog` | GET piston-meta `version_manifest_v2.json` for display; embedded fixture fallback |
| `OciConfigProfiles` | Profile + region from `~/.oci/config` (no API) |
| `SshKeyHelper` | Generate `%USERPROFILE%\.ssh\mcmgr_ed25519_yyyyMMdd_HHmmss` (unique; no overwrite) or import `.pub` |
| `WindowsCredentialStore` | Optional OCIR Auth Token → Credential Manager target `McManager/ocir` |
| `InfraPlanSummary` | Static ~20-create list aligned with the 3.1 plan |
| `SetupWizardWindow` | Fluent ~720×560, 9 steps, Back/Next, Deploy **disabled** |
| `FirstRunWindow` | Shown only when `config.local.json` is missing (Setup vs existing stack) |
| Advanced tab | **Deploy / repair infrastructure** (does not hijack launches that already have manage config) |

`infra/README.md` troubleshooting notes that tofu never overwrites `terraform.tfvars` (unsaved editor buffer vs disk; do not copy the example over a filled file).

### Out of scope (3.2)

Writing `infra/terraform.tfvars`; invoking tofu; SSH bootstrap; seeding Object Storage; overwriting `config.local.json`; Phase 4 auto-detect beyond the first-run stub; notification chrome.

### Next

**Phase 4** — Connect-existing (auto-detect + hydrate from `meta/infra.json`). **Do not start until the operator asks.**

---

## Step 3.3 — Apply + bootstrap + capacity wait

**Status:** DONE (2026-08-13) as **product code**. Live OCI apply is **operator-only**; agents did not `tofu apply`, OCIR push, or SSH the lab VMs. **Blank PAYG tenancy operator test 2026-08-14:** apply + bootstrap succeeded; follow-up fixes below are in tree.

### What shipped

| Piece | Role |
|-------|------|
| `OpenTofuLocator` / `OpenTofuRunner` | Find `tofu.exe` (PATH, then WinGet Links). `init` / `apply -auto-approve` / `output -json` with `-chdir=infra` and `-state`/`-var-file` under `%LOCALAPPDATA%\McManager\tofu\<stack-id>\` |
| `RecordingOpenTofuRunner` | Fake runner: records argv, canned outputs, **never starts tofu** |
| `TfvarsWriter` / `TofuWorkspace` | Writes LocalAppData `terraform.tfvars` from wizard + `tenancy=` from `~/.oci`. **Never** repo `infra/terraform.tfvars` |
| `SetupDeployOrchestrator` | Resumable pipeline: apply → wait RUNNING + cloud-init markers → door → VM1 → OS/meta → optional Function → `config.local.json` |
| `SetupBootstrapService` | SSH: lab `door_vm/install.sh --yes` (gcc/OCI CLI/make; stop mccontrol first), `onbox/mcmgr/common/driver.sh` with `EULA_ACCEPTED` + `MINECRAFT_VERSION`, lab `vm_agent/` + timer. CRLF strip, `/tmp` staging, `sudo bash -c` chains |
| `OcirFunctionPublisher` | Best-effort linux/arm64 `docker buildx --push`; skip without token/Docker/`MCMANAGER_OCIR_USERNAME`. Stages a temp Dockerfile; Function config `INSTANCE_OCIDS` (does not bake lab placeholders) |
| Wizard summary | Detected admin `/32` (editable), Deploy enabled, confirm (second A1 + overwrite `config.local.json`), log panel, capacity Retry / 7 min poll / Stop |
| `LocalConfigStore.SaveConfig` | Maps tofu outputs: SSH hosts = ephemeral primaries; play IP = reserved; key = private sibling of wizard `.pub`; RCON from VM1 only (never meta) |
| `MCMANAGER_TOFU_DRY_RUN=1` | Uses fake runner; skips wait/SSH/OS/config write |

`apply_stage` in wizard JSON: `not_started` → `tofu_applied` → `cloud_init` → `door` → `vm1` → `os_meta` → `function` → `config_written`. Re-Deploy skips completed tofu stages. At `vm1` or later, Re-Deploy re-runs guest repair (netplan, door `oci.env` OS vars, Vanilla whitelist) and can start a STOPPED VM1 without `tofu apply`. On-box Vanilla resume remains `/var/lib/mcmgr/bootstrap-state.json`.

### Blank-tenancy test lessons (2026-08-14)

| Symptom | Cause | Handling |
|---------|--------|----------|
| Setup stuck at `apply_stage=vm1`; bucket missing `meta/infra.json` | Greenfield GET 404 treated as publish failure; seed errors not in the deploy log | `PublishFromLocalAsync` treats missing meta as create; seed empty ledger; log seed failures |
| Door MOTD **Control plane degraded** on first wake | `oci.env` lacked Object Storage namespace/bucket; `pull_os_budget.sh` failed closed on missing ledger | `install.sh` / Setup persist OS vars; ledger 404 is OK on first pull |
| Reserved play IP connect timeout; ephemeral SSH worked | Guest OS had no secondary play address; reserved public IP maps to that secondary | Setup writes `/etc/netplan/99-mcmgr-play.yaml` on both VMs |
| Vanilla “you are not white-listed” | `white-list=true` with empty `whitelist.json` | Wizard **admin Minecraft username** → seed whitelist |
| `wait_forge.sh`: `POLL_INTERVAL_SEC: unbound variable` | `set -u` + `${UNSET//$'\r'/}` before `:-10` | Default optional env vars **before** CR-strip (`door_vm/oci/wait_forge.sh`) |
| `ip_to_vm1.sh failed` / `UpdatePublicIp` 404 | Compartment-only public-ip policy; door DG tag match did not enroll the instance | Tenancy policy `mcmgr-door-ip`; door DG `instance.id`; scripts `--force` + already-on-target no-op |
| `ubuntu` cannot source `/etc/mccontrol/oci.env` | File is mode 600 root — expected | Diagnose as root; do not treat this as a misdeploy |
| Manual `tofu import` “no configuration files” / literal `$infra` | LocalAppData has state only; PowerShell `-flag=$var` does not expand | `cd` repo `infra/`; `-state="$state"` `-var-file="$vars"`; see `infra/README.md` |
| Guest “restart required” / 22.04 vs 24.04 | cloud-init `package_update` only | **Do not** `apt upgrade` / `do-release-upgrade` |

Players join the **reserved play IP**, not the ephemeral SSH addresses. **TEMPORARY** VM1 OpenTofu default remains **2/12** until reverted to **4/24**.

### Out of scope (3.3)

GitHub infra zip pull; tofu state encryption; `remote-exec`; copying `door_vm/` / `vm_agent/` into this repo; agent live apply; Phase 4 auto-detect.

### Next

**Phase 4** — Connect-existing.

---

## Operator test coverage (confirmed)

Through Step **1.8** Phase 1 exit:

- Config loads from repo-root `data/`
- Whitelist save + Security List sync (non-managed rules preserved)
- Idle → Start → Playable on reserved play IP
- Stop → Idle; IP returns to door
- Restart while up cycles Minecraft only
- Copy play IP
- Usage dashboard / publish / Today bar / 2 min tab poll
- Server Management list/download (incl. multi‑GiB)/upload/replace; soft-cap refuse; selection behavior
- Danger Zone idle apply (OS + SSH) and boot force-enable story
- Exit gate: Python Manager optional for normal day-to-day manage

Through Steps **2.1–2.4**:

- Contract doc reviewed against live bucket sample objects (read-only in 2.1)
- `meta/infra.json` legacy → nested v2 publish + GET round-trip
- Published meta contains no SSH private key paths / OCI config path / RCON password
- Advanced Refresh/Publish infra meta wired for operator re-test
- Offline Vanilla bootstrap dry-run (`onbox/mcmgr`) for 1.21.1 / 1.21.11 — §4.1 manifest + generic unit + §10.2 idle sync asserts
- Door wake force OS pull redeployed; oversized-world flag set/skip on VM1 agent

Through Step **3.1**:

- `infra/` OpenTofu root `tofu validate` (oracle/oci 8.27.0)
- No `tofu apply`; no live-lab import

Through Step **3.2**:

- Solution builds; Setup is Advanced **Deploy / repair infrastructure** when `config.local.json` exists
- First-run chooser when config is missing (`MCMANAGER_CONFIG_DIR` at an empty dir)
- Wizard resume file is `data/setup-wizard.local.json`; Deploy does not call OCI/tofu
- Version list from live piston-meta **or** embedded fixture

---

Through Step **3.3** (code + operator blank-tenancy test 2026-08-14):

- Solution builds; Deploy is enabled on the summary step
- State/tfvars path is `%LOCALAPPDATA%\McManager\tofu` (repo `infra/terraform.tfvars` untouched by Setup)
- Dry-run via `MCMANAGER_TOFU_DRY_RUN=1` does not create OCI resources or overwrite `config.local.json`
- Operator apply on a **separate PAYG test tenancy** reached Vanilla up, idle SoftStop, Object Storage seed, door **PLAYABLE** on the reserved play IP (after IAM/netplan/whitelist/seed fixes)
- **TEMPORARY:** `infra/variables.tf` VM1 defaults are **2 OCPU / 12 GB** for the blank-tenancy 3.3 test — **revert to 4 / 24** after the test. Wizard plan/confirm copy matches. Product MVP shape remains 4/24 until Setup offers a picker (PRODUCT-IDEAS).

---

## Explicitly not built yet (Phase 4+)

- Connect-existing hydrate UI, installer, update checks
- Door SoftStop pre-stop world backup (OS-ISSUE-6 — deferred)
- Door UTC/override/GB accounting parity with Manager
- firewalld sync, notification center / settings gear
- Usage: lease apply, interval editor, SSH ledger push/pull
- Flag-driven Object Storage → VM1 world apply; oversized-world SSH download / Manager clear UX (v1)
- Separate “disable daily guardrails only” flag

---

## Changelog (this file)

| 2026-08-14 | Step 3.3 blank-tenancy operator test: OS seed 404=create, door OS env, netplan, Vanilla whitelist, tenancy `mcmgr-door-ip`, door DG by instance OCID, wait_forge `set -u`, ip_to_vm1 `--force`. NEXT remains Phase 4. |
| 2026-08-12 | Step 3.2: Setup wizard UX (resume JSON, Mojang picker, Credential Manager, static plan). No apply. NEXT = Step 3.3. |
| 2026-08-12 | Step 3.1: `infra/` OpenTofu skeleton; `tofu validate` OK; no apply. NEXT = Step 3.2. |
| 2026-08-12 | Budget wiring correction: Events → Function is live; ONS topic unlinked leftover. NEXT remains Step 3.1. |
| 2026-08-12 | Sanitized lab RM dump; added [`Lab-Reference-Stack-Notes.md`](Lab-Reference-Stack-Notes.md). NEXT remains Step 3.1 (do not start until operator says so). |
| 2026-08-12 | Pre-3.1 research: added [`Automated-Infrastructure-Deployment.md`](Automated-Infrastructure-Deployment.md). NEXT remains Step 3.1 (do not start until operator says so). |
| 2026-08-11 | Step 2.4: door `--force` OS pull; §10.2 idle sync; oversized-world flag; Phase 2 DONE; NEXT = 3.1. |
| 2026-08-11 | Step 2.3: `onbox/mcmgr/` Vanilla bootstrap + fixtures/dry-run; NEXT = Step 2.4. |
| 2026-08-11 | Expanded as-built docs for Steps **1.8**, **2.1**, and **2.2** (exit gate / contract freeze / infra meta) to match 1.5–1.7 depth; refreshed architecture + file map through 2.2. |
| 2026-08-11 | Step 2.2: nested `meta/infra.json` v2 DTO/store + Advanced publish; live legacy v1 migrated; round-trip + secret scan OK; NEXT = Step 2.3. |
| 2026-08-11 | Step 2.1: frozen Object Storage/meta/ledger contracts; reviewed 14 live bucket objects read-only; documented target-vs-live gaps; NEXT = Step 2.2. |
| 2026-08-11 | Step 1.8 / Phase 1 exit: smoke checklist + signoff; NEXT = Step 2.1. |
| 2026-08-11 | Expanded Steps 1.5–1.7 as-built documentation (operator-validated); refreshed file map + thrift/tab wiring notes. |
| 2026-08-11 | Step 1.7 as-built: Danger Zone idle OS publish + SSH timer apply. |
| 2026-08-11 | Step 1.6 as-built: BackupStore, stream OS, Server Management UI, SSH replace (+ selection/soft-cap/large-download fixes). |
| 2026-08-11 | Step 1.5 as-built: UsageMath, UsageBudgetStore, Usage tab + Today bar. |
| 2026-08-11 | Initial as-built doc covering Steps 1.1–1.4 after operator-validated power testing. |
