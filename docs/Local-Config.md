# Local operator config (gitignored)

Avalonia manage MVP seeds connectivity from **local JSON**, not from lab private markdown at runtime.

## Files

| Path | Git | Role |
|------|-----|------|
| [`config.local.example.json`](../config.local.example.json) | Tracked | Schema template (placeholders) |
| [`friends.local.example.json`](../friends.local.example.json) | Tracked | Whitelist template |
| `data/config.local.json` | **Ignored** | Live OCIDs / SSH / Object Storage / budgets |
| `data/friends.local.json` | **Ignored** | Live Desired List seed |
| `data/setup-wizard.local.json` | **Ignored** | Setup wizard resume (step index + fields; **no** Auth Token, **no** SSH private key) |

Copy examples into `data/` and fill values, or keep the operator-seeded files already present on this machine.

## Sources of truth when refreshing seeds

1. Lab `data/config.json` — Manager day-2 settings the Python app uses  
2. Lab `data/Infrastructure-Deployment-Private.md` — full OCIDs (reserved IP id, private IP ids, VCN, bucket, …)  
3. Lab `data/friends.json` — whitelist  

Do **not** copy Auth Tokens into `config.local.json` (OCIR only; Manager uses `~/.oci` API key). Setup stores an optional OCIR Auth Token in **Windows Credential Manager** (`McManager/ocir`), not in wizard JSON.

## Setup wizard resume

`McManager.Core.Config.SetupWizardStore` reads/writes `data/setup-wizard.local.json` (same data directory as manage config). Saved on each Next/Back/Close.

Included: current step, Always Free / residual / capacity flags, OCI profile + region, compartment strategy, alert email, SSH **public** path/line/fingerprint (Generate creates `%USERPROFILE%\.ssh\mcmgr_ed25519_yyyyMMdd_HHmmss`, not a reused default name), Vanilla + version **id**, EULA flag, whether a token was stored, **admin `/32` CIDR**, **admin Minecraft username** (Vanilla whitelist), **`apply_stage`**, optional Function image after OCIR push.

**Not** included: Auth Token secret, SSH private key, tenancy OCID, jar URL/sha1.

**Step 3.3 writes:**

- `data/config.local.json` after a successful (non-dry-run) Deploy — **replaces** an existing manage seed in that data directory (wizard confirms first). Prefer `MCMANAGER_CONFIG_DIR` pointing at a **new empty folder** so the lab Manager config stays intact.
- OpenTofu `terraform.tfvars` + `terraform.tfstate` under `%LOCALAPPDATA%\McManager\tofu\<stack-id>\` (not the repo, not the shared bucket). **Never** writes [`infra/terraform.tfvars`](../infra/terraform.tfvars). Manual `tofu import` / `plan` for that stack must `-state`/`-var-file` those LocalAppData files while the working directory is repo `infra/` (PowerShell: quote `-state="$state"`).
- `friends.local.json` with the admin `/32` **only if that file is empty**.
- Guest netplan (`/etc/netplan/99-mcmgr-play.yaml`) for the secondary play IP; Vanilla whitelist from **admin Minecraft username**.

**Re-Deploy:** if `apply_stage` is already `vm1` (or later), Deploy re-runs guest repair (netplan, door env, whitelist) and can start a STOPPED VM1 — it does **not** re-`tofu apply`. Players use `play.reserved_public_ip`, not `vm1.ssh_host` / `door.ssh_host`.

Dry-run: set `MCMANAGER_TOFU_DRY_RUN=1` so Deploy uses a fake tofu runner and does **not** create OCI resources or overwrite `config.local.json`. Agents must use this (or not click Deploy). A real apply creates another Always Free A1 (product MVP **4 OCPU / 24 GB**; **TEMPORARY test default 2 / 12** — revert after 3.3). In the **same** tenancy as the live lab that competes for Ampere hours; a **separate** test tenancy does not.

First-run: if `config.local.json` is missing, the app opens a chooser (Setup vs “I already have a stack”) instead of MainWindow. With a valid manage config, Setup is **Advanced → Deploy / repair infrastructure**. To walk first-run while a real config exists, point `MCMANAGER_CONFIG_DIR` at an empty directory.

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

When Connect existing / auto-detect lands, these should be hydratable from Object Storage **`meta/infra.json`** (full OCID set in lab `PRODUCT-IDEAS.md` / product [`Contracts-Object-Storage.md`](Contracts-Object-Storage.md)). Local file still holds SSH private key path, OCI profile, and RCON — not Object Storage.

**Manage MVP (Step 2.2):** Advanced tab can **Publish infra meta from local config** to write nested `meta/infra.json` v2 (and migrate a legacy flat v1 object). Refresh reads the bucket object back. Game fields (`server_kind` / `minecraft_version`) are editable on that tab until Setup owns them.

## Shape note

Lab private doc targets **4 OCPU / 24 GB**. Lab `config.json` may show a different live shape (e.g. after resize / detect). Prefer values from the live Manager config when seeding; VM1 agent also stamps per-interval shape on the ledger.

## Sync discipline

When you change OCI resources in Console or lab config, update `data/config.local.json` (and lab private markdown) in the same sitting so Avalonia and Python stay aligned.

## Later (after v1): deployment profiles

MVP/v1 assume **one** connected stack: a single `data/config.local.json` (+ friends / wizard resume beside it). Lab `PRODUCT-IDEAS.md` **Multi-deploy profiles (after v1)** adds connecting an *additional* infrastructure deployment from Advanced (OCI API config + VM SSH keys → auto-detect/validate → profile switcher).

When that ships, local data should become **per-profile folders** (each with that deployment’s config, friends list, and paths to keys — still gitignored; still no secrets in Object Storage). Do **not** change the current flat `data/` layout until that feature is implemented. Connect existing in MVP remains one stack.
