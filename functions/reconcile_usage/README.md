# `reconcile_usage` — Usage API 48h ledger reconcile

**Status:** Product **v1** source (V1 Step **7.7**). `func.yaml` version **0.0.1**.  
**TESTING** agents **may** `fn push` / invoke without asking (prefer `{"dry_run": true}` first). Stay at **$0**. Do **not** invoke against the live Forge lab (`DEFAULT`). `tofu apply` still needs operator authorization.

This is a **second** Function. It does **not** change [`shutdown_vm`](../shutdown_vm/README.md) ($1 spend-brake SoftStop).

## What it does

Oracle’s Usage API lags about **48 hours**. For UTC calendar days whose end is older than that window:

1. `request_summarized_usages` (`query_type=USAGE`, `granularity=DAILY`) filtered to the product compartment.  
2. Keep **Ampere A1** OCPU / memory SKUs only (`B93113` / `B93114`, or sku/shape text). **Ignore** the Always Free AMD door Micro and Object Storage. Optional `VM1_INSTANCE_OCID` drops other `resourceId`s when present. Skip forecasts.  
3. GET `ledger/usage.json`. For each eligible day with non-zero API quantity:  
   - If a `daily_overrides` row exists and its `note` is **not** `usage_api_reconcile` (manual correction), **leave it**.  
   - If API OCPU-h / GB-h already match interval-derived totals, **skip** (no extra override).  
   - Otherwise write `daily_overrides[YYYY-MM-DD]` (`uptime_hours` from API OCPU-h ÷ that day’s interval shape, `ocpu_hours` / `gb_hours` from the API, `note=usage_api_reconcile`) and bump **`revision`**. Intervals and other ledger fields are preserved. **Never** write a zero-API override over interval hours (Always Free rows can be missing from Usage API).  
4. If anything was written: PUT the ledger with **If-Match** (412 → one refresh-and-retry), then dirty **`ledger.manager` / `ledger.door` / `ledger.vm1`** on `meta/flags.json` (Function is not a consumer). No new dirty-flag category.

Optional invoke JSON `{"dry_run": true}` runs the same reads and returns the planned changes without PUT.

## Function config

| Key | Required | Notes |
|-----|----------|--------|
| `OS_NAMESPACE` | yes | Object Storage namespace. |
| `OS_BUCKET` | yes | Shared bucket (product `mcmgr-shared-data`). |
| `OS_LEDGER_OBJECT` | no | Default `ledger/usage.json`. |
| `OS_FLAGS_OBJECT` | no | Default `meta/flags.json`. |
| `TENANCY_OCID` | yes | Usage API `tenant_id` (tenancy OCID). |
| `COMPARTMENT_OCID` | yes | Filter to the product compartment. |
| `VM1_INSTANCE_OCID` | no | When Usage rows include `resourceId`, keep VM1 only. |
| `AGE_HOURS` | no | Default `48`. |

Placeholders starting with `<` are treated as unset. Live OCIDs stay in Function config / the private file.

## IAM (when later deploying)

The Functions dynamic group already needs object read/write on the product bucket (spend-brake PUT). Usage API also needs a **tenancy-level** statement such as:

```text
allow dynamic-group <BUDGET_FUNCTIONS_DYNAMIC_GROUP> to read usage-reports in tenancy
```

Do **not** add this policy or a timer in OpenTofu in this step.

## Deploy later (operator-authorized)

- Same **pre-built ARM tar + copy into the user’s OCIR** channel as [`shutdown_vm`](../shutdown_vm/README.md) (V1 Step **8.6.1**). Do **not** require Docker Desktop on the **user’s** PC. Developer Docker Desktop is OK. TESTING `fn push` is a lab/agent path only.  
- Prefer the existing Functions application (`GENERIC_ARM`) rather than a paid second app.  
- Timeout in `func.yaml` is **120s** (Usage API + GET/PUT). Memory 256 MiB.

## Files

| File | Role |
|------|------|
| `func.py` | FDK handler + testable SKU/day/override helpers |
| `test_func.py` | Mocked Usage API rows / ledger JSON (no OCI) |
| `func.yaml` | Fn project metadata |
| `requirements.txt` | `fdk`, `oci` |

## Tests

From this directory (stdlib `unittest`; does not import OCI/FDK at collection time beyond `func.py` helpers):

```bash
python -m unittest test_func.py
```

No live Usage API call. No lab-tenancy run.
