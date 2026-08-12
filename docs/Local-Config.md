# Local operator config (gitignored)

Avalonia manage MVP seeds connectivity from **local JSON**, not from lab private markdown at runtime.

## Files

| Path | Git | Role |
|------|-----|------|
| [`config.local.example.json`](../config.local.example.json) | Tracked | Schema template (placeholders) |
| [`friends.local.example.json`](../friends.local.example.json) | Tracked | Whitelist template |
| `data/config.local.json` | **Ignored** | Live OCIDs / SSH / Object Storage / budgets |
| `data/friends.local.json` | **Ignored** | Live Desired List seed |

Copy examples into `data/` and fill values, or keep the operator-seeded files already present on this machine.

## Sources of truth when refreshing seeds

1. Lab `data/config.json` — Manager day-2 settings the Python app uses  
2. Lab `data/Infrastructure-Deployment-Private.md` — full OCIDs (reserved IP id, private IP ids, VCN, bucket, …)  
3. Lab `data/friends.json` — whitelist  

Do **not** copy Auth Tokens into `config.local.json` (OCIR only; Manager uses `~/.oci` API key).

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
