# Object Storage contracts — MVP stack

**Status:** Frozen target contract for MVP Phase 2 (2026-08-11); known deployed-code deviations are listed explicitly.  
**Scope:** Shared Object Storage data used by the Manager, VM1 idle/backup agent, door, Setup, and Connect existing.  
**Product intent authority:** lab [`PRODUCT-IDEAS.md`](../../OCI-mc-server-manager/PRODUCT-IDEAS.md).  
**Live implementation authority:** product `vm_agent/`, `door_vm/`, lab Object Storage Phase 1–5 docs, and this product's `McManager.Core` DTOs.

This document defines object names, JSON shapes, writer ownership, dirty flags, and compatibility rules. It contains placeholders only—never copy live OCIDs, public IPs, credentials, private keys, Auth Tokens, or RCON passwords into this tracked file.

---

## Goals and non-goals

The bucket is the shared source of truth for actors that are not always connected:

- **Manager** — budget/UI writer; ledger reader; future IP/message/meta writer; **v1:** only clearer of the $1 spend-brake lock flag.
- **VM1** — primary usage-ledger, lease, and backup writer.
- **Door** — budget/ledger reader before wake; rare ledger repair writer while VM1 is `STOPPED`; **v1:** read spend-brake lock flag and refuse VM1 wake while it is set.
- **Setup / Connect existing** — infrastructure metadata writer/reader.
- **$1 budget Function (v1)** — sole **writer** of `meta/spend-brake-triggered.json` on a real threshold alert (not RESET). Tracked Function source writes it (V1 Step 2.2); live deploy still requires an authorized `fn push`.

Dirty flags are pull hints, not the source of truth. The **target contract** requires a safety-critical action (especially door wake) to fetch or validate authoritative data rather than trusting only a stale flag/cache. The deployed door is currently flag-aware and can reuse a cache when bits are clear; closing that gap belongs to Step 2.4.

This contract does **not** define:

- OCI credentials, SSH private-key paths, Auth Tokens, or RCON passwords.
- Local-only Manager configuration.
- Local door cache files under `/var/lib/mccontrol/os-cache/`.
- Local VM1 files under `/etc/mc-manager/` or `/var/lib/mc-manager/`.
- A backup index object; MVP lists `backups/world-*.zip` directly.

---

## Bucket and encoding rules

- Storage tier: **Standard** only.
- Product greenfield bucket name: `mcmgr-shared-data`; existing stacks may use another name recorded in `meta/infra.json`.
- JSON: UTF-8 object with a trailing newline when practical.
- Timestamps: UTC ISO 8601, normally `YYYY-MM-DDTHH:MM:SSZ`.
- Object names are case-sensitive.
- JSON documents carry an integer `version`.
- Readers must reject a known document whose `version` is newer than they support when misreading it could affect cost, wake gating, restore, or infrastructure mutation.
- Missing optional fields use documented defaults; malformed required fields are an error.
- Never store secrets in the bucket.

### Compatibility rules

1. **Patch change:** documentation/behavior clarification with no JSON shape change.
2. **Additive field:** allowed only after all writers of that object preserve the field. Some current DTO-based writers serialize a known field set and can drop unknown fields.
3. **Breaking shape/meaning change:** increment that object's `version`.
4. **Infrastructure compatibility change:** increment `infra_schema`.
5. **Deployed software release:** change `stack_version`; this is independent of the Manager app version.
6. New dirty-flag categories require a coordinated reader/writer rollout. Current v1 normalizers intentionally emit only the five categories in this document and would drop unknown categories.

These are normative rules for new Phase 2+ work. Existing lab/product code does not yet enforce every rule—especially unsupported-version rejection and conditional writes. That is a tracked conformance gap, not permission for new code to repeat it.

### Concurrency

- Prefer OCI ETag + `If-Match` for read-modify-write documents.
- `ledger/usage.json` already uses a monotonic `revision`, remote/local merge, and best-effort `If-Match`.
- `ledger/lease.json` is a last-writer heartbeat singleton; it intentionally does not dirty ledger flags.
- IP, messages, and infra meta have one primary writer role. `budget/config.json` has a Manager primary writer **and** a narrow VM1 patch writer (shape and boot safety), so both must fetch the latest document, preserve fields, and use conditional writes.
- **Manager (V1 Step 7.4):** writes to `budget/config.json`, `meta/infra.json`, `meta/flags.json` (on those Manager publishes/clears), and `ip/allowlist.json` GET the current ETag and PUT with `If-Match`. HTTP 412 returns a refresh-and-retry error instead of clobbering. First create (object missing) is unconditional. `ip/mode.json` is withdrawn — no Manager writer.
- VM1 shape/idle patches of `budget/config.json` in `vm_agent/os_publish.py` still PUT without If-Match. A concurrent Manager publish now fails closed (412) rather than overwriting blindly.
- `meta/flags.json` is shared by all actors. A writer must fetch, modify only the intended bits, preserve all known categories, and PUT the result. A failed flags PUT does not invalidate the authoritative object that was already written; consumers must also support explicit/forced refresh.

---

## Object index

| Object key | Version | Authority / primary writer | Readers | Status |
|------------|---------|----------------------------|---------|--------|
| `meta/infra.json` | canonical **2** | Setup; Manager infra-publish/upgrade | Manager / Connect existing; diagnostics | **Live nested v2** after Step 2.2 migration |
| `meta/flags.json` | 1 | Shared protocol (last modifier) | Manager, VM1, door | Live |
| `meta/oversized-world-backup.json` | 1 | VM1 backup agent | Manager | **Live set/skip (Step 2.4)**; Manager bell + SSH download = **V1 Step 6.3 DONE**. Typed clear UX still later. |
| `meta/spend-brake-triggered.json` | 1 | $1 budget Function sets/replaces; Manager is the only clearer (DELETE) | Manager, door | **Frozen v1.** Function PUT = 2.2 (not live-pushed). Door honor = 2.3 (source). **Manager overlay = 2.4 DONE.** |
| `meta/world-restore-request.json` | 1 | Manager requests; VM1 updates outcome | VM1, Manager | Reserved contract for flag-driven restore; current MVP uses SSH fallback |
| `meta/backup-upload-lock.json` | 1 | Manager or VM1 active uploader | Manager, VM1 | Reserved coordination contract; not implemented |
| `ledger/usage.json` | 2 | VM1; door only for STOPPED orphan heal | Manager, door, VM1 boot | Live |
| `ledger/lease.json` | 1 | VM1 heartbeat; door clears after STOPPED heal | VM1 boot, door heal, diagnostics | Live |
| `budget/config.json` | 1 | Manager; VM1 may patch detected shape / boot safety | Door, VM1, Manager | Live |
| `ip/allowlist.json` | 1 | Manager | Future product consumers | Seeded/live; Hybrid Save updates when present. `ip` = IPv4 or IPv4 CIDR. **Always applied** (product is private-only). |
| `ip/mode.json` | 1 | — (withdrawn) | — | **Withdrawn 2026-08-18.** Public/blacklist rejected. Step 3.1 wrote it; Step **3.4** stopped the Manager writer. Leftover objects in buckets may remain unused. |
| `messages/chat.json` | 1 | Manager | VM1 agent | Seeded/live; rich editor deferred |
| `backups/.keep` | text | Setup/seed | None | Optional marker |
| `backups/world-<UTC>.zip` | ZIP | VM1 backup agent; Manager manual upload | Manager | Live |
| `smoke/*` | text | Test actors | Test actors | Non-contract test artifacts; safe to delete |

---

## `meta/infra.json` — canonical infrastructure metadata v2

### Purpose

After button-gated discovery finds the compartment and bucket, this object contains enough stable identifiers to hydrate local Manager configuration without listing every OCI resource. It must not contain local credential paths or secret material.

The operator bucket previously contained a **legacy flat v1** object (`infra_schema: 1`). Step 2.2 migrated it to the canonical nested v2 shape below via Manager publish-from-local.

### Version semantics

| Field | Type | Meaning |
|-------|------|---------|
| `version` | integer | JSON document shape; canonical value `2` |
| `infra_schema` | integer | Compatibility of deployed cloud/on-box contracts; canonical value `2` |
| `stack_version` | string | Deployed stack software/release version, independent of Manager app version |
| `created_at` | UTC timestamp | First creation of this stack metadata |
| `updated_at` | UTC timestamp | Last successful metadata update |

MVP Connect existing warned and required confirmation on an unsupported `infra_schema`; it did not silently mutate an incompatible stack. **v1 (Step 7.3):** Connect **refuses** when `infra_schema` or document `version` is **newer** than this Manager. Older schema, legacy meta, or a different `stack_version` gets an extra confirm. Connect does not publish or migrate meta. Auto-detect stays button-gated.

**Connect existing (Phase 5):** After the operator clicks **Auto-detect infrastructure** (first-run or Advanced — never on launch), Manager locates a product compartment + bucket, **reads** this object, and hydrates `data/config.local.json`. Prefer this object over rediscovering every OCID via tags. Targeted Get-by-OCID to refresh a stale `ssh_host` is allowed. Connect does not publish or migrate meta.

### Canonical shape

```json
{
  "version": 2,
  "infra_schema": 2,
  "stack_version": "0.1.0",
  "created_at": "2026-08-11T00:00:00Z",
  "updated_at": "2026-08-11T00:00:00Z",
  "stack_name": "mcmgr",
  "mode": "always_free",
  "region": "<OCI_REGION>",
  "tenancy_id": "<TENANCY_OCID>",
  "compartment_id": "<COMPARTMENT_OCID>",
  "play": {
    "reserved_public_ip": "<PLAY_IP>",
    "reserved_public_ip_id": "<PUBLIC_IP_OCID>"
  },
  "game": {
    "server_kind": "vanilla",
    "minecraft_version": "<MOJANG_VERSION_ID>",
    "server_jar_sha1": "<OPTIONAL_SHA1>"
  },
  "network": {
    "vcn_id": "<VCN_OCID>",
    "subnet_id": "<SUBNET_OCID>",
    "security_list_id": "<SECURITY_LIST_OCID>",
    "minecraft_port": 25565,
    "ssh_port": 22
  },
  "vm1": {
    "instance_id": "<VM1_INSTANCE_OCID>",
    "display_name": "mcmgr-vm1",
    "shape": "VM.Standard.A1.Flex",
    "shape_ocpus": 4.0,
    "shape_memory_gb": 24.0,
    "primary_private_ip": "<PRIVATE_IP>",
    "secondary_private_ip": "<SECONDARY_PRIVATE_IP>",
    "secondary_private_ip_id": "<PRIVATE_IP_OCID>",
    "ssh_host": "<OPTIONAL_CACHED_PUBLIC_IP>",
    "ssh_user": "ubuntu",
    "world_path": "/home/ubuntu/minecraft/server/world",
    "minecraft_unit": "minecraft"
  },
  "door": {
    "instance_id": "<DOOR_INSTANCE_OCID>",
    "display_name": "mcmgr-door",
    "primary_private_ip": "<PRIVATE_IP>",
    "secondary_private_ip": "<SECONDARY_PRIVATE_IP>",
    "secondary_private_ip_id": "<PRIVATE_IP_OCID>",
    "ssh_host": "<OPTIONAL_CACHED_PUBLIC_IP>",
    "ssh_user": "ubuntu",
    "http_port": 8080
  },
  "object_storage": {
    "namespace": "<OBJECT_STORAGE_NAMESPACE>",
    "bucket": "mcmgr-shared-data",
    "bucket_id": "<BUCKET_OCID>",
    "soft_cap_gb": 9.5,
    "backup_enabled": true,
    "prefixes": {
      "meta": "meta/",
      "ledger": "ledger/",
      "budget": "budget/",
      "ip": "ip/",
      "messages": "messages/",
      "backups": "backups/"
    }
  },
  "budget_brake": {
    "budget_id": "<OPTIONAL_BUDGET_OCID>",
    "function_id": "<OPTIONAL_FUNCTION_OCID>"
  },
  "ssh": {
    "public_key_fingerprint": "<OPTIONAL_FINGERPRINT>",
    "private_key_location": "admin_pc_only"
  }
}
```

### Required and optional fields

Required for a manageable stack: versions/timestamps, `stack_name`, `mode`, region/tenancy/compartment, both play fields, all network fields, all VM1/door fields shown (a `ssh_host` property may be JSON `null` while no public endpoint exists), all Object Storage fields, `server_kind`, and `minecraft_version`. Display names, shape, service paths, ports, `soft_cap_gb`, and `backup_enabled` are required because Manager local configuration and operator summaries use them.

Optional:

- `game.server_jar_sha1` before bootstrap completes.
- `budget_brake` fields when the last-resort brake is not yet deployed.
- `ssh.public_key_fingerprint`.

`vm1.ssh_host` / `door.ssh_host` are required **properties** but cached connectivity values, not resource identity. They may be `null` while an endpoint is absent, and ephemeral values may become stale. Connect existing hydrates stable resource identity without broad List discovery; targeted Get/VNIC refresh by recorded OCID remains an allowed operational lookup when an ephemeral address changes.

`mode` is `always_free` for MVP. `paid` is reserved for v1 and must never be inferred from PAYG tenancy status.

### Prohibited fields

Do not include:

- OCI API config paths or private API key material.
- SSH private keys or local private-key paths.
- RCON passwords.
- Auth Tokens.
- Filled `oci.env` content or other bearer credentials.

---

## `meta/flags.json` — dirty hints v1

```json
{
  "version": 1,
  "updated_at": "2026-08-11T00:00:00Z",
  "categories": {
    "ledger":  { "manager": false, "door": false, "vm1": false },
    "budget":  { "manager": false, "door": false, "vm1": false },
    "meta":    { "manager": false, "door": false, "vm1": false },
    "ip":      { "manager": false, "door": false, "vm1": false },
    "messages":{ "manager": false, "door": false, "vm1": false }
  },
  "help": "Writer sets consumers dirty; consumer clears only its own bit after a successful pull."
}
```

### Protocol

- A writer updates the authoritative object first.
- It then sets each consumer that must pull to `true`.
- It clears its own consumer bit because it already has the data.
- A consumer clears **only its own** bit and only after a successful pull/apply.
- `updated_at` changes on each flags PUT.
- A clear bit does not guarantee a local cache exists; missing cache or explicit force refresh still pulls.
- Door wake force/fail-closed behavior overrides flag thrift for budget/ledger safety.

The last rule is the target. The deployed `pull_os_budget.sh` currently fetches only when a door bit is dirty, a cache is missing, or `--force` is explicitly supplied; normal wake does not pass `--force`. Step 2.4 must make wake validate current ledger/budget even after a lost flags PUT.

### Current transitions

| Authoritative write | Resulting bits |
|---------------------|----------------|
| VM1 publishes ledger | `ledger.manager=true`, `ledger.door=true`, `ledger.vm1=false` |
| Door heals ledger while VM1 `STOPPED` | `ledger.manager=true`, `ledger.door=false`, `ledger.vm1=true` |
| Manager optional/manual ledger publish | `ledger.manager=false`, `ledger.door=true`, `ledger.vm1=true` |
| Manager publishes budget | `budget.manager=false`, `budget.door=true`, `budget.vm1=true` |
| VM1 patches detected shape or boot force-enables idle | `budget.manager=true`, `budget.door=true`, `budget.vm1=false` |
| Setup/Manager publishes infra meta | `meta.manager=false`, `meta.door=true`, `meta.vm1=true` |
| Manager publishes IP config | `ip.manager=false`; set only consumers implemented by that stack |
| Manager publishes messages | `messages.manager=false`, `messages.vm1=true`; door remains false unless it gains a message consumer |

There is no `backups` category in v1. Adding it now is unsafe because current normalizers drop unknown categories.

---

## `ledger/usage.json` — usage ledger v2

VM1 is the primary writer. Door writes only to close orphaned open intervals after OCI reports VM1 **`STOPPED`**. Manager Phase 1 is a reader; lab Testing2 supports manual publish.

**Destroy:** `tofu destroy` of the product stack deletes this bucket (including `ledger/usage.json`); a later Setup seeds a new empty ledger and does not restore prior intervals. Oracle Always Free OCPU-hours for the calendar month still include the destroyed VMs. MVP has no ledger import. See [`Guide.md`](Guide.md) → Tear down and [`Automated-Infrastructure-Deployment.md`](Automated-Infrastructure-Deployment.md) §12.4.

```json
{
  "version": 2,
  "revision": 42,
  "intervals": [
    {
      "id": "<UUID>",
      "started_at": "2026-08-11T00:00:00Z",
      "stopped_at": "2026-08-11T02:00:00Z",
      "ocpus": 2.0,
      "memory_gb": 12.0,
      "source": "boot",
      "stop_source": "idle_or_budget_stop",
      "stop_uncertain": false
    }
  ],
  "daily_overrides": {
    "2026-08-01": {
      "uptime_hours": 1.5,
      "ocpu_hours": 3.0,
      "gb_hours": 18.0,
      "note": "manual correction",
      "updated_at": "2026-08-11T00:00:00Z"
    }
  },
  "idle_since": null,
  "last_budget_warn_at": null
}
```

### Top-level fields

| Field | Type | Rules |
|-------|------|-------|
| `version` | integer | Required; `2` |
| `revision` | integer | Required; monotonic publish counter, starts `0` |
| `intervals` | array | Required; sort by `started_at` for display, identity by `id` |
| `daily_overrides` | object | Required; keys are UTC `YYYY-MM-DD`; override replaces interval-derived totals for that day |
| `idle_since` | timestamp/null | Current idle countdown state mirrored for diagnostics |
| `last_budget_warn_at` | timestamp/null | Last budget warning state when present |

### Interval fields

| Field | Type | Rules |
|-------|------|-------|
| `id` | UUID string | Required, stable merge identity |
| `started_at` | UTC timestamp | Required |
| `stopped_at` | UTC timestamp/null | `null` means open; at most one open interval after normalization |
| `ocpus` | number | Required and positive; shape for **this interval** |
| `memory_gb` | number | Required and positive; shape for **this interval** |
| `source` | string | Required; diagnostic origin such as boot/manual |
| `stop_source` | string | Required for a closed interval when known |
| `stop_uncertain` | boolean | Optional; true when stop is an estimate |
| `uncertain_reason` | string | Optional diagnostic explanation |
| `uncertain_repaired_at` | UTC timestamp | Optional; VM1 later verified/refined an estimate |

Per-interval shape is authoritative for OCPU-h/GB-h math and allows future resize without rewriting history. Do not calculate historical totals using the current VM shape.

### Daily override fields

`uptime_hours`, `ocpu_hours`, and `gb_hours` are non-negative numbers. `note` and `updated_at` are optional diagnostics. Overrides replace, rather than add to, interval-derived totals for that UTC day.

### Merge and repair rules

- Merge intervals by `id`.
- Prefer a definitive stop over an uncertain stop.
- When two valid stops differ, current boot reconciliation keeps the earlier defensible stop; never extend usage into a later boot.
- Normalize multiple open intervals by closing older opens at the next interval start.
- Door orphan heal:
  - only while OCI lifecycle is `STOPPED`, never `STOPPING`;
  - use lease `last_heartbeat_at` when available, otherwise heal time;
  - set `stop_uncertain` and diagnostic source/reason;
  - dirty Manager + VM1, clear door.
- VM1 boot force-pulls ledger and lease, merges local knowledge, repairs estimates from journal/list-boots where possible, then opens the new interval.

### Known product DTO constraint

The current Avalonia `UsageInterval` DTO reads fields needed for budget math but does not model all uncertainty diagnostics. Phase 1 never writes the ledger, so those fields remain safe. Any future Manager ledger writer must preserve all interval fields or extend the DTO before PUT.

---

## `ledger/lease.json` — active-session heartbeat v1

```json
{
  "version": 1,
  "active": true,
  "session_id": "<UUID>",
  "interval_id": "<LEDGER_INTERVAL_UUID>",
  "started_at": "2026-08-11T00:00:00Z",
  "last_heartbeat_at": "2026-08-11T00:05:00Z",
  "ocpus": 2.0,
  "memory_gb": 12.0,
  "updated_at": "2026-08-11T00:05:00Z",
  "cleared_at": null,
  "clear_reason": null
}
```

- VM1 writes an active lease on boot and refreshes it about every five minutes while active.
- Heartbeats do **not** dirty ledger flags.
- Clean stop writes `active=false`, retains last-known session/shape for diagnostics, and sets `cleared_at` / `clear_reason`.
- Door may clear the lease after a STOPPED-only ledger heal (`clear_reason: "door_heal"`).
- A lease is stale when missing/inactive or its heartbeat age exceeds the configured grace (currently normally 900 seconds).
- A stale lease is evidence for recovery, not permission to close a ledger while OCI says VM1 is running.

---

## `budget/config.json` — budget and idle configuration v1

```json
{
  "version": 1,
  "updated_at": "2026-08-11T00:00:00Z",
  "shape_ocpus": 2.0,
  "shape_memory_gb": 12.0,
  "monthly_ocpu_target": 1400.0,
  "monthly_gb_target": 8800.0,
  "soft_ocpu_cap": 1375.0,
  "soft_gb_cap": 8600.0,
  "idle_timeout_minutes": 15,
  "budget_warn_minutes": 5,
  "idle_agent_enabled": true,
  "daily_ocpu_limit_phase_a": 45.16,
  "mode": "always_free"
}
```

| Field | Type / constraints | Meaning |
|-------|--------------------|---------|
| `version` | integer `1` | Document schema |
| `updated_at` | UTC timestamp | Last writer time |
| `shape_ocpus` | number > 0 | Current detected/configured VM1 shape |
| `shape_memory_gb` | number > 0 | Current detected/configured VM1 memory |
| `monthly_ocpu_target` | number > 0 | Monthly OCPU-h target |
| `monthly_gb_target` | number > 0 | Monthly GB-h target |
| `soft_ocpu_cap` | number > 0 | Monthly OCPU-h SoftStop cap |
| `soft_gb_cap` | number > 0 | Monthly GB-h SoftStop cap |
| `idle_timeout_minutes` | integer >= 1 | Minutes before SoftStop when the server is empty **or** Minecraft is not `active` (same field; Step 4.1 does **not** add a second key) |
| `budget_warn_minutes` | integer >= 0 | Final warning grace |
| `idle_agent_enabled` | boolean | Testing switch; VM1 boot force-enables |
| `daily_ocpu_limit_phase_a` | number > 0 | Compatibility field for older door logic |
| `mode` | `always_free` | MVP only |

Manager is the primary writer. VM1 may patch:

- shape fields after `/proc` live detection; optional `shape_source: "vm1_proc_detect"`;
- `idle_agent_enabled=true` on boot; optional `idle_agent_enabled_source: "vm1_boot_force_enable"`.

Those `*_source` fields are advisory and are not currently preserved by every DTO writer; safety behavior must not depend on them.

Usage reporting uses UTC windows. The deployed Manager currently derives compatibility field `daily_ocpu_limit_phase_a` from the America/Los_Angeles month because the deployed door historically used that boundary. It is transitional input, not the frozen source of truth: the aligned door computes its daily allowance from `monthly_ocpu_target` using the canonical UTC month and uses the compatibility field only until that migration is complete.

Disabling idle is testing/troubleshooting only. VM1 boot starts the timer and rewrites this object to enabled so a forgotten disable cannot leave the free-tier brake off.

### Canonical accounting versus deployed door

The frozen product accounting contract is:

- UTC day/month windows, matching VM1 and `UsageMath`;
- `daily_overrides` replace interval-derived totals for that UTC day;
- wake/SoftStop decisions evaluate both OCPU-h and GB-h daily allowance/leftover and both monthly soft caps.
- all shape, target, and cap fields above must be finite and positive; invalid/zero safety configuration is rejected and wake fails closed rather than treating zero as “unlimited.”

The deployed door currently uses America/Los_Angeles day windows, ignores `daily_overrides`, gates OCPU only, and treats some non-positive values as unset while VM1 can treat a zero cap as immediately exhausted. Avalonia DTOs can also deserialize missing shape as zero. These can produce different decisions; Step 2.4 must align validation/accounting with the frozen contract or record an explicit operator-approved deferral in the plan.

---

## `ip/allowlist.json` — private allowlist v1

```json
{
  "version": 1,
  "updated_at": "2026-08-11T00:00:00Z",
  "mode_note": "Product is private-only. This allowlist is always applied. ip/mode.json is withdrawn.",
  "entries": [
    {
      "id": "<UUID>",
      "name": "Friend",
      "ip": "203.0.113.10",
      "is_admin": false
    },
    {
      "id": "<UUID>",
      "name": "CgnatFriend",
      "ip": "172.56.0.0/16",
      "is_admin": false
    }
  ]
}
```

- `ip` is a single IPv4 address **or** an IPv4 CIDR prefix (e.g. `172.56.0.0/16`). Hosts are stored without a `/32` suffix; prefixes `/9`–`/31` are stored as `network/prefix`. `/0`–`/8` are rejected.
- `name` is optional display/description text (Security List Minecraft rule description).
- `is_admin` controls SSH/door-admin rule ownership in the Manager. CIDR prefixes apply to **Minecraft 25565 TCP/UDP** only. SSH / door `:8080` stay `/32` unless the operator is editing **their own** admin entry.
- Manager is the intended writer. Hybrid Save updates this object **only when it already exists** in the bucket (does not create it).
- This is the shared IP SoT when present. Hybrid always applies the Security List from local `friends.local.json`.

## `ip/mode.json` — withdrawn

**Withdrawn 2026-08-18.** Public Minecraft, a public/private toggle, and blacklist are **rejected**. Do not treat this object as a product contract.

A leftover object may still exist in some buckets from V1 Step 3.1 (`version`, `updated_at`, `mode`, `blacklist`). Step **3.4** stops the Manager from reading or PUTting it. Actors must ignore it. Do not create it on greenfield Setup.

---

## `messages/chat.json` — VM1 message templates v1

```json
{
  "version": 1,
  "updated_at": "2026-08-11T00:00:00Z",
  "chat_messages": {
    "budget_warn_leftover": "Daily usage limit exceeded; using leftover hours (~{ocpu:.1f} OCPU-h / ~{gb:.1f} GB-h left).",
    "budget_final_warn": "Daily + leftover usage exhausted. Server will shut down soon.",
    "budget_stop": "Usage limits reached. Server shutting down.",
    "soft_cap_stop": "Monthly usage soft cap reached. Server shutting down.",
    "idle_stop": "No players for {minutes} minutes. Saving and shutting down.",
    "admin_stop": "Admin requested shutdown. Saving world…"
  }
}
```

- Manager is the intended writer; VM1 is the consumer.
- Unknown/missing template keys fall back to built-in defaults.
- Invalid format placeholders must not crash the agent; current formatter returns the unformatted template.
- RCON credentials never belong in this object.
- Rich message editing is outside MVP.

---

## Backups

### `backups/.keep`

Optional text marker used only so a newly seeded prefix is visible. It is not a backup and must be ignored by list/eviction logic.

### `backups/world-<UTC>.zip`

- Name format: `backups/world-YYYYMMDDTHHMMSSZ.zip`.
- ZIP entries are relative to the world directory; there is no required outer `world/` folder.
- VM1 is the primary writer. Manager manual upload uses the same naming convention.
- Manager lists objects by prefix and suffix; there is no authoritative `backups/index.json` in MVP.
- Soft cap: approximately 9.5 GiB of Standard storage headroom.
- The legacy setting name `soft_cap_gb` is interpreted as **GiB** (`value * 1024^3`) by current code.
- On-box automatic backup lists total bucket use, deletes oldest matching world ZIPs **before** upload when needed, and never deletes non-backup objects.
- A single ZIP larger than the cap must never be uploaded.
- The target Manager upload gate obtains a fresh **whole-bucket** byte total immediately before upload and fails closed if total + ZIP would exceed the cap. It still cannot make List+PUT atomic, so writer ownership and `If-None-Match` collision protection are also required.
- Manager Phase 1 currently hard-refuses based on its last `backups/world-*.zip` listing only. It does not count control/non-backup objects or re-list atomically, so it is protective UI but not yet the bucket-wide target guarantee.
- Second-resolution names can collide. A writer must create with `If-None-Match: *`; on collision retry as `backups/world-YYYYMMDDTHHMMSSZ-<8-hex>.zip`. Readers accept both forms.

### `meta/backup-upload-lock.json` — upload/eviction lock v1

List/evict/PUT is not atomic. Manager and VM1 must serialize any operation that can add or evict backup bytes:

```json
{
  "version": 1,
  "operation_id": "<UUID>",
  "owner": "vm1",
  "acquired_at": "2026-08-11T00:00:00Z",
  "expires_at": "2026-08-11T00:30:00Z",
  "expected_upload_bytes": 123456789
}
```

- Acquire by conditional create (`If-None-Match: *`). If a non-expired lock exists, fail/try later; do not upload concurrently.
- Recover an expired lock only with ETag-conditional delete/replacement so two actors cannot both steal it.
- While holding the lock: fresh-list the entire bucket, include the lock/control objects in total bytes, evict only eligible `backups/world-*.zip` objects, verify projected total, then conditionally create the new ZIP.
- Release by ETag-conditional delete in success/failure cleanup.
- A lock is not a reservation after `expires_at`; choose an expiry longer than the expected multi-GiB transfer and renew it conditionally when needed.
- This lock is not implemented in the deployed actors. Until it is, Manager/VM1 uploads must be treated as single-operator, non-concurrent operations rather than a hard concurrent guarantee.

### `meta/oversized-world-backup.json` — reserved durable block flag v1

Exact key frozen by this contract:

```json
{
  "version": 1,
  "status": "blocked",
  "detected_at": "2026-08-11T00:00:00Z",
  "updated_at": "2026-08-11T00:00:00Z",
  "archive_size_bytes": 12000000000,
  "soft_cap_bytes": 10200547328,
  "reason": "archive_exceeds_soft_cap",
  "backup_prefix": "backups/"
}
```

Semantics:

- **Existence** with `status: "blocked"` means automatic Object Storage world backups are blocked because one archive cannot fit safely.
- VM1 writes/replaces it before returning from the doomed upload path and skips later automatic OS backup attempts while it exists.
- Absence means no known oversized-world block.
- Clear operation is deletion after an operator resolves/accepts the condition; a dedicated typed-clear UX is still later. DEBUG Advanced can PUT/DELETE a fixture. A successful explicit on-box backup that proves the archive fits may also delete a stale object.
- Manager checks this key at startup (bell) and when Server Management refreshes. While blocked, **Download latest world save** streams the live world over SSH (`world_backup.py --stream-stdout`) and does **not** PUT the zip to Object Storage. Per-row downloads of existing `backups/world-*.zip` objects stay on the Object Storage path.
- No v1 dirty-flag category is added; consumers GET this small object at the relevant UI/action boundary.
- The flag must not include the world contents, SSH paths/keys, or credentials.

### `meta/spend-brake-triggered.json` — v1 $1 budget lock (frozen)

Exact key frozen by this contract (V1 Step 2.1). Product intent: lab `PRODUCT-IDEAS.md` ($1 spend-brake lock). Do **not** invent a second lock object.

```json
{
  "version": 1,
  "triggered_at": "2026-08-17T21:00:00Z",
  "updated_at": "2026-08-17T21:00:00Z",
  "source": "budget_function",
  "alert_type": "ACTUAL",
  "reason": "compartment_budget_threshold"
}
```

| Field | Required | Notes |
|-------|----------|--------|
| `version` | yes | Integer; current **1**. Readers that cannot parse a newer version still treat **presence** as locked. |
| `triggered_at` | yes (well-formed) | UTC ISO 8601 (`YYYY-MM-DDTHH:MM:SSZ`) when the Function wrote or last replaced the object. Used later for a “new month?” hint only — **never** auto-clear. |
| `updated_at` | no | Defaults to `triggered_at`. |
| `source` | no | Writer identity. Function must use `budget_function`. |
| `alert_type` | no | Optional copy of Events `triggeredAlertType` (e.g. `ACTUAL`). Must **not** be `RESET`. |
| `reason` | no | Machine-readable cause. Default `compartment_budget_threshold`. |

Semantics:

- **Existence** of the object means the $1 last-resort compartment budget has fired this period. That is the lock. Absence means no known spend-brake lock.
- **Fail closed:** a present object is locked even if JSON is malformed or `version` is newer than the reader supports. Transport / auth Get failures are **errors**, not unlocked.
- **Writer:** the $1 budget Function **sets/replaces** the object when handling a real threshold alert. Ignore budget **RESET** — do not write or delete the object on RESET. A second alert may overwrite (idempotent PUT). Tracked Function write is Step **2.2** (no live `fn push` in 2.2).
- **Clearer:** Manager **only**, via **DELETE**, after the admin types the exact confirmation statement from PRODUCT-IDEAS and Start/reconcile succeeds (Step **2.4**). Missing-object DELETE is success. Do **not** auto-clear at calendar-month rollover. Do **not** write `status: "cleared"` — unlocked = object gone.
- **Readers:** Manager (full-window warning; block Start until typed confirm — **DONE** Step **2.4**) and the door (refuse **START VM1** while present, same poll discipline as the budget gate). Door honor is **DONE** in lab `door_vm/` (Step **2.3**; live door needs redeploy).
- **Not** a dirty-flag category. Consumers GET this small object at Manager open / Start / door wake (same pattern as oversized-world).
- **Not** an OpenTofu / Setup seed object. Do not create or delete it from IaC.
- **Not** a field of `meta/infra.json`. Freezing this key does **not** bump `infra_schema`.
- This object does **not** encode whether the Function SoftStopped the door Micro. **Product v1 policy (Step 2.2):** the Function **does not** SoftStop the door (Always Free AMD Micro is a separate envelope). Live undeployed images may still stop both VMs.
- Must not contain secrets, card details, confirmation-sentence text, or live OCIDs (those stay in `meta/infra.json` if needed).

Core helpers: `SpendBrakeLockDocument` + `SpendBrakeLockStore` (get / put / delete) + `SpendBrakeLockUx` (exact confirmation sentence + overlay/Start rules). Manager production code must use PUT only for tests or DEBUG fixtures — never to *set* the live lock.

### `meta/world-restore-request.json` — reserved restore request v1

This exact singleton key is the Object Storage apply path referenced by the MVP restore design. Current Phase 1 replaces directly over SSH while VM1 is running; no current on-box consumer applies this object.

```json
{
  "version": 1,
  "request_id": "<UUID>",
  "status": "pending",
  "requested_at": "2026-08-11T00:00:00Z",
  "updated_at": "2026-08-11T00:00:00Z",
  "object_name": "backups/world-20260811T000000Z-1a2b3c4d.zip",
  "object_size_bytes": 123456789,
  "object_etag": "<OPTIONAL_ETAG>",
  "applied_at": null,
  "error": null
}
```

Semantics:

- Manager uploads the ZIP completely, then writes a new `pending` request and dirties `meta.vm1`.
- `object_name` must remain under the configured `backups/` prefix and end in `.zip`; VM1 validates size/ETag when supplied and rejects path traversal in the archive.
- VM1 stops Minecraft, takes the required safety backup, replaces the world, fixes ownership, starts Minecraft, then writes `status: "applied"` plus `applied_at`; on failure it writes `status: "failed"` plus a non-secret error.
- VM1 clears only `meta.vm1` after it has fetched and durably recorded/applied the request. Reprocessing the same `request_id` is idempotent.
- Manager reads the outcome and may delete an acknowledged terminal request. A new request must not overwrite an unacknowledged `pending`/`applying` request.
- SSH replacement remains the implemented MVP fallback. On-box request application must not be assumed until explicitly shipped/tested.

Current SSH fallback is happy-path tested but not yet equivalent to the target apply safety: it does not preflight archive paths/shape before moving the old world, and extraction failure does not roll back the `.bak.<timestamp>` world before attempting to restart Minecraft. Automated/novice-ready restore must add preflight and rollback before this gap can be considered closed.

---

## Writer/reader matrix

| Data | Manager | VM1 | Door | Setup / Connect |
|------|---------|-----|------|-----------------|
| Infra meta | Read; publish/upgrade in Step 2.2 | Optional read | Optional read | Primary write/read |
| Flags | Modify own writes; clear own bits | Modify own writes; clear own bits | Clear own bits; set heal consumers | Initialize/update meta bits |
| Usage ledger | Read (manual lab write only) | **Primary write** | Read; rare STOPPED heal write | Seed empty |
| Lease | Diagnostics | **Primary heartbeat/clear** | Read; clear after heal | Seed empty |
| Budget | **Primary write** | Read; shape/idle safety patch | Read | Seed |
| IP config | Intended primary write | Future read | Future read | Seed |
| Messages | Intended primary write | Read | Not currently | Seed |
| Backup ZIPs | Read; manual upload/SSH replace | **Primary upload/evict** | None | Marker seed |
| Backup upload lock | Acquire/release for upload | Acquire/release for backup/evict | None | None |
| Oversized backup flag | Read; future clear UX | **Write/block** | None | None |
| Spend-brake lock | **Only clearer** (DELETE after typed confirm — **2.4 DONE**) | None | Read; refuse VM1 wake (**Step 2.3 source**) | None (not a tofu/seed object; Function is the writer — Step 2.2 source) |
| World restore request | Write/read outcome; SSH fallback today | Future apply/outcome write | None | None |

---

## Live bucket review (2026-08-11)

Reviewed read-only through the OCI SDK against the operator bucket. Values/OCIDs were not copied into this document.

Observed:

- 14 objects total.
- Nine seeded control/layout objects: infra, flags, usage, lease, budget, allowlist, IP mode, messages, and backup marker.
- Two `backups/world-*.zip` objects.
- Three non-contract `smoke/*` artifacts.
- `ledger/usage.json`: v2, revision present, 79 intervals, three daily overrides, per-interval `ocpus` / `memory_gb`, uncertainty fields observed.
- `ledger/lease.json`: v1 with the complete active/heartbeat/clear field set.
- `meta/flags.json`: v1 with all five categories and all three consumers.
- Budget/IP/message documents match their v1 seed shapes.
- `meta/infra.json`: **nested v2** after Step 2.2 Manager publish (`infra_schema: 2`); legacy flat v1 remains a supported read/migration input only.
- `meta/oversized-world-backup.json` is absent, as expected before on-box support lands.

No live objects were modified during review.

---

## Known gaps after contract freeze

**Idle timeout meaning (MVP Step 4.1 — no schema change):** `budget/config.json` `idle_timeout_minutes` stays the same key. Product intent now includes SoftStop when Minecraft is **not running**, not only when RCON `list` is empty. Do **not** add a second timeout field.

1. **Step 2.2 (done):** canonical nested `meta/infra.json` v2 read/write + live legacy migration; unsupported newer schema rejected on **manage** read (`InfraMetaStore.GetAsync`). Connect existing uses a **lenient** parse (warn + confirm) and does not mutate the object.
2. **DONE (Step 2.4):** VM1 `meta/oversized-world-backup.json` set/skip behavior in `vm_agent/world_backup.py`. **DONE (V1 Step 6.3):** Manager bell + SSH live-world download (no OS PUT). Typed clear UX still later.
3. **Step 2.4:** make door wake force-refresh/validate authoritative ledger+budget rather than relying only on flags/cache.
4. **Step 2.4:** align door UTC/override/OCPU+GB accounting with Manager and VM1, or record an operator-approved deferral.
5. **DONE (V1 Step 7.4):** Manager conditional writes (`If-Match`) for `budget/config.json`, `meta/infra.json`, `meta/flags.json` on those Manager publishes, and `ip/allowlist.json`. Ledger already had best-effort If-Match. VM1 budget shape/idle patches still PUT unconditionally.
6. Budget is a shared-writer object. Manager If-Match stops a silent clobber when VM1 writes in the GET/PUT window; a closed DTO can still drop advisory `*_source` fields if there is no concurrent writer.
7. Current readers do not consistently reject unsupported versions or malformed required safety fields.
8. Avalonia uncertainty diagnostics are not fully represented in `UsageInterval`; safe while Manager remains ledger-read-only.
9. Avalonia manual backup upload counts only listed backup ZIPs, not fresh whole-bucket usage.
10. Flag-driven `meta/world-restore-request.json` apply is not implemented; SSH replacement is the current fallback.
11. The upload lock is not implemented; current Manager/VM1 List+PUT operations can race and exceed the aggregate cap. Current VM1 eviction accepts any `.zip` under `backups/`, not only canonical `world-*.zip`.
12. Current SSH world replace lacks archive preflight and rollback after extraction failure.
13. Hybrid whitelist writes local `friends.local.json` (friends only) and applies the Security List **allowlist**. `ip/allowlist.json` is updated on Save **when that object already exists** (V1 Step 1.2). `ip` may be a single IPv4 or an IPv4 CIDR prefix. `ip/mode.json` is **withdrawn** (public/blacklist rejected; Step 3.4 removed the writer).
14. Prefixes are fixed for `infra_schema: 2`; the prefix map is configuration/discovery data, not permission to change hardcoded deployed actors independently. A prefix change requires coordinated actor updates plus an `infra_schema` bump.
15. Lease fields are all required properties (nullable where shown). A VM1 helper implements the configured 900-second stale test, but no active caller was found; deployed door heal does not enforce age and uses the heartbeat only after OCI reports VM1 `STOPPED`.
16. **DONE (V1 Steps 2.1–2.4):** `meta/spend-brake-triggered.json` key + v1 JSON shape are frozen. Tracked Function PUTs the object and SoftStops VM1 only. Door wake GETs the object and refuses START while it is present. Manager shows a full-window overlay on open, blocks Start until the exact PRODUCT-IDEAS confirmation sentence, then parks the play IP (Troubleshooting path), DELETEs the lock, refreshes door OS cache, and Wakes (idle/daily/monthly gates still apply). Live Function image still does not write this object until an authorized `fn push`. Live door needs redeploy from `door_vm/`.

---

## Implementation references

Product:

- `src/McManager.Core/Usage/*`
- `src/McManager.Core/Services/UsageBudgetStore.cs`
- `src/McManager.Core/Services/InfraMetaStore.cs`
- `src/McManager.Core/Services/ConnectExistingService.cs`
- `src/McManager.Core/Services/ObjectStorageService.cs`
- `src/McManager.Core/Services/BackupStore.cs`
- `src/McManager.Core/Services/AllowlistStore.cs`
- `src/McManager.Core/Services/SpendBrakeLockStore.cs`
- `src/McManager.Core/Usage/SpendBrakeLockDocument.cs`
- `src/McManager.Core/Usage/SpendBrakeLockUx.cs`
- `src/McManager.Core/Config/FriendRules.cs`
- `docs/Local-Config.md`

Lab/on-box:

- Lab `app/object_storage.py`, `app/os_sync.py`, `app/usage.py`
- Product `vm_agent/ledger.py`, `vm_agent/lease.py`, `vm_agent/os_publish.py`, `vm_agent/world_backup.py`
- Product `door_vm/oci/pull_os_budget.sh`, `door_vm/oci/heal_os_ledger.sh`
- Lab `docs/Object-Storage-Phase1.md` through `Object-Storage-Phase5.md`
- `PRODUCT-IDEAS.md` — sync model, infra meta, oversized-world intent, v1 $1 spend-brake lock
