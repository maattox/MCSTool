# Local operator config (gitignored)

The Manager WinExe (`McManager.Hybrid`, WPF + BlazorWebView) seeds connectivity from **local JSON**, not from lab private markdown at runtime. Same schema regardless of UI host.

## Files

| Path | Git | Role |
|------|-----|------|
| [`config.local.example.json`](../config.local.example.json) | Tracked | Schema template (placeholders) |
| [`friends.local.example.json`](../friends.local.example.json) | Tracked | Whitelist template (`ip` = IPv4 or IPv4 CIDR) |
| `data/config.local.json` | **Ignored** | Live OCIDs / SSH / Object Storage / budgets |
| `data/friends.local.json` | **Ignored** | Live Desired List seed |
| `data/setup-wizard.local.json` | **Ignored** | Setup wizard resume (step index + fields; **no** Auth Token, **no** SSH private key) |
| `data/sample-packs/` | **Ignored** | Operator-local sample `.mrpack` / CurseForge zips for Phase 4 pack-import work — see [`Sample-Packs.md`](Sample-Packs.md). **Not** CI fixtures. |
| `%LOCALAPPDATA%\McManager\app-settings.json` | **Ignored** (outside the repo) | Program settings for this PC: update-check toggle (Phase 9 honors it). Not stack OCIDs. |

Copy examples into `data/` and fill values, or keep the operator-seeded files already present on this machine.

`friends.local.json` is the local allowlist (friends + admin flags). Public mode and blacklist are **rejected** — do not add `mode` or `blacklist` as product fields. Leftover keys in an existing file are ignored; Save strips them. The Manager writes `ip/allowlist.json` when that object already exists. There is no live writer for `ip/mode.json`.

## Sources of truth when refreshing seeds

1. Lab `data/config.json` — Manager day-2 settings the Python app uses  
2. Lab `data/Infrastructure-Deployment-Private.md` — full OCIDs (reserved IP id, private IP ids, VCN, bucket, …)  
3. Lab `data/friends.json` — whitelist  

Do **not** copy Auth Tokens into `config.local.json` (OCIR only; Manager uses `~/.oci` API key). Setup stores an optional OCIR Auth Token in **Windows Credential Manager** (`McManager/ocir`), not in wizard JSON.

## Setup wizard resume

`McManager.Core.Config.SetupWizardStore` reads/writes `data/setup-wizard.local.json` (same data directory as manage config). Saved on each Next/Back/Close.

Included: current step, Always Free / residual / capacity flags, OCI profile + region, compartment strategy, alert email, SSH **public** path/line/fingerprint (Generate creates `%USERPROFILE%\.ssh\mcmgr_ed25519_yyyyMMdd_HHmmss`, not a reused default name), **server type** (`vanilla` / `modded`), Vanilla flavor (`default` Mojang vs `optimized` Paper) + version **id**, Modded pack path/kind/name/loader + confirm flags (no catalog URL), EULA flag, whether a token was stored, **admin `/32` CIDR**, **VM1 OCPUs / memory** (`2`/`12` or `4`/`24`; default **4 / 24**). In-game `white-list` is **off**; OCI Security List is the allowlist. Also **`apply_stage`**, optional Function image after OCIR push.

**Not** included: Auth Token secret, SSH private key, tenancy OCID, jar URL/sha1.

**Step 3.3 writes:**

- `data/config.local.json` after a successful (non-dry-run) Deploy — **replaces** an existing manage seed in that data directory (wizard confirms first). Prefer `MCMANAGER_CONFIG_DIR` pointing at a **new empty folder** so the lab Manager config stays intact.
- OpenTofu `terraform.tfvars` + `terraform.tfstate` under `%LOCALAPPDATA%\McManager\tofu\<stack-id>\` (not the repo, not the shared bucket). **Never** writes [`infra/terraform.tfvars`](../infra/terraform.tfvars). Manual `tofu import` / `plan` for that stack must `-state`/`-var-file` those LocalAppData files while the working directory is repo `infra/` (PowerShell: quote `-state="$state"`).
- `friends.local.json` with the admin `/32` **only if that file is empty**.
- Guest netplan (`/etc/netplan/99-mcmgr-play.yaml`) for the secondary play IP; managed `server.properties` with **`white-list=false`** / **`enforce-whitelist=false`** (OCI Security List is the allowlist). Setup does **not** seed `whitelist.json` from a Minecraft username.

**Re-Deploy:** if `apply_stage` is already `vm1` (or later), Deploy re-runs guest repair (netplan, door env, managed `server.properties` whitelist-off) and can start a STOPPED VM1 — it does **not** re-`tofu apply`. Players use `play.reserved_public_ip`, not `vm1.ssh_host` / `door.ssh_host`.

**Delete infrastructure (Danger Zone):** typed `confirm`, then OpenTofu `destroy` of the LocalAppData stack only. After success the app removes `config.local.json`, `setup-wizard.local.json`, and `%LOCALAPPDATA%\McManager\tofu\<stack-id>\`. It keeps `friends.local.json`, `~/.oci`, SSH keys, and Credential Manager. Close Manager and reopen before a fresh Setup. No tofu state on this PC → destroy refuses (it will not scan-and-wipe the tenancy). The product Object Storage bucket (including `ledger/usage.json` and world backups) is destroyed with the stack; a later Setup seeds a new empty ledger. Oracle Always Free hours for the current month are not reset — see [`Guide.md`](Guide.md) → Tear down and redeploy.

Dry-run: set `MCMANAGER_TOFU_DRY_RUN=1` so Deploy uses a fake tofu runner and does **not** create OCI resources or overwrite `config.local.json`. Agents must use this (or not click Deploy). A real apply creates another Always Free A1 (Setup default **4 OCPU / 24 GB**, or **2 / 12** if chosen). In the **same** tenancy as the live lab that competes for Ampere hours; a **separate** test tenancy does not.

First-run: if `config.local.json` is missing, the app opens a chooser (Setup vs **Auto-detect infrastructure** vs “I already have a stack”) instead of MainWindow. Auto-detect is **button-gated** — the app does **not** probe OCI on launch. “I already have a stack” opens the manage UI without scanning (hand-seeded config). With a valid manage config, Setup and Auto-detect are on **Advanced**. To walk first-run while a real config exists, point `MCMANAGER_CONFIG_DIR` at an empty directory.

## Connect existing (hydrate from `meta/infra.json`)

Button-gated discovery (`ConnectExistingService`):

1. Read `%USERPROFILE%\.oci\config` and try each usable profile (region + tenancy + loadable key). Sequential; 429 backoff via `OciSession`.
2. List compartments; keep display name **`mcmgr`** **or** freeform tag **`mcmgr-domain=mc-server-compartment`**.
3. In each candidate, look for `meta/infra.json` (prefer bucket `mcmgr-shared-data`, then other buckets in that compartment). Lab buckets may use another name — the object records the live bucket.
4. Validate required OCIDs. Soft `infra_schema` / document-version mismatch → warn + extra confirm; Connect does **not** publish or mutate meta. Multiple matches → chooser (never first-hit-wins).
5. On confirm: write `data/config.local.json` from meta (world_path / unit as recorded — lab may still be `/home/ubuntu/minecraft/server`; greenfield `/opt/mcmgr/`).
6. If that file already exists: confirm overwrite first. Existing **SSH key path** and **RCON** on this PC are preserved; the operator is prompted to browse a private key only when none is present. OCI profile is the one that found the stack.

**Never in meta / Object Storage:** SSH private key path, OCI config file path, RCON password.

**Never invent:** RCON password, SSH key path. Cancel / none-found does not delete an existing seed.

Optional targeted GetInstance/VNIC refresh fills a missing `ssh_host` from the recorded instance OCID — no tenancy-wide List of instances.

## Load path

Canonical location is the **product repo root** `data/` (next to `AGENTS.md` / `config.local.example.json`), **not** under `src/`.

`McManager.Core.Config.LocalConfigStore` walks upward from the app base directory / cwd and:

1. Uses the first existing `data/config.local.json` it finds  
2. Else creates `data/` at the directory that contains `AGENTS.md` or `config.local.example.json`  
3. Else falls back to `data/` next to `McManager.slnx` (only if no repo-root config exists)

Override with environment variable:

```text
MCMANAGER_CONFIG_DIR=C:\path\to\OCI-mc-server
```

(Expected layout: `{dir}/data/config.local.json`.)

## OCI API credentials & clock

- API signing uses `oci.config_file` + `oci.profile` (and the PEM referenced there). Tenancy OCID (`oci.tenancy_id`) is required for signing / some IAM ops.  
- If OCI returns **401 NotAuthenticated** with a valid key, check **Windows clock skew**: Oracle rejects clients skewed **more than 5 minutes** from the API servers ([Using the API](https://docs.oracle.com/en-us/iaas/Content/API/Concepts/usingapi.htm)).  
- Call-pattern rules (429 backoff, waiters, pagination, Always Free request thrift): [`OCI-API-Usage.md`](OCI-API-Usage.md).

## Manage MVP fields (minimum)

Required for early API work:

- `oci.region`, `oci.compartment_id`, `oci.config_file`, `oci.profile`
- `network.security_list_id`
- `vm1.instance_id`, `vm1.ssh_*`
- `door.instance_id`, `door.ssh_*`, `door.http_port`
- `play.reserved_public_ip` (+ `reserved_public_ip_id` for IP move / diagnostics)
- `object_storage.namespace`, `object_storage.bucket`

These fields are hydratable from Object Storage **`meta/infra.json`** (full OCID set in lab `PRODUCT-IDEAS.md` / product [`Contracts-Object-Storage.md`](Contracts-Object-Storage.md)). Local file still holds SSH private key path, OCI profile, and RCON — not Object Storage.

**Connect existing (Phase 5):** First-run **Auto-detect infrastructure** and Advanced **Auto-detect infrastructure** hydrate `config.local.json` from `meta/infra.json` after a confirm (and overwrite confirm if a seed already exists). See [Connect existing](#connect-existing-hydrate-from-metainfrajson) above.

**Manage MVP (Step 2.2):** Advanced tab can **Publish infra meta from local config** to write nested `meta/infra.json` v2 (and migrate a legacy flat v1 object). Refresh reads the bucket object back. Game fields (`server_kind` / `minecraft_version`) are editable on that tab until Setup owns them.

## Shape note

Lab private doc targets **4 OCPU / 24 GB**. Lab `config.json` may show a different live shape (e.g. after resize / detect). Prefer values from the live Manager config when seeding; VM1 agent also stamps per-interval shape on the ledger.

## Sync discipline

When you change OCI resources in Console or lab config, update `data/config.local.json` (and lab private markdown) in the same sitting so the Manager app and Python stay aligned.

## Sample modpacks (Phase 4)

Gitignored [`data/sample-packs/`](../data/sample-packs/) holds homemade parser fixtures plus a few real published exports on this PC. Tracked instructions, gotchas, and the “pause and ask the operator to download a pack” rule: [`Sample-Packs.md`](Sample-Packs.md). Do not commit those archives. CI stays on `tests/fixtures/`.

Imported packs the Manager actually installed are copied to **`data/imported-packs/<pack>_<version>/original.mrpack`** (or `original.zip`, plus `archive.json`). **Server Management → Download pack** copies that original archive — never a zip of VM1 `mods/`. The product cannot reconstruct a client pack from server `mods/` (Setup strips client-only files). Gitignored with the rest of `data/`.

**Program settings (gear):** resolved paths for the data folder, `config.local.json`, `%LOCALAPPDATA%\McManager\tofu`, and the Oracle API config file. The update-check checkbox writes `%LOCALAPPDATA%\McManager\app-settings.json` (`check_for_updates`, default on). No GitHub request runs until Phase 9.

**Notifications (bell):** in-memory this session only (not a file). Later steps post into the same list; Debug builds can post a sample from Advanced → DEBUG host probes.

## Later (after v1): deployment profiles

MVP/v1 assume **one** connected stack: a single `data/config.local.json` (+ friends / wizard resume beside it). Lab `PRODUCT-IDEAS.md` **Multi-deploy profiles (after v1)** adds connecting an *additional* infrastructure deployment from Advanced (OCI API config + VM SSH keys → auto-detect/validate → profile switcher).

When that ships, local data should become **per-profile folders** (each with that deployment’s config, friends list, and paths to keys — still gitignored; still no secrets in Object Storage). Do **not** change the current flat `data/` layout until that feature is implemented. Connect existing in MVP remains one stack.
