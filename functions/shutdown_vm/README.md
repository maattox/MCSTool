# `shutdown_vm` — $1 budget emergency Function

**Status:** Product **v1** source (V1 Step **2.2**). `func.yaml` version **0.0.13**.  
**0.0.13 (2026-09-02):** SoftStop only on a confirmed **$1 actual-spend** breach. Official Events `CreateTriggeredAlert` JSON has **no** `triggeredAlertType` (monthly RESET uses the same event type). The Function GETs the budget and **skips** when `actual_spend` is below the amount; also skips parsed `RESET` / `FORECAST`. Unconfirmed envelopes (no type, spend unknown) skip instead of fail-closed. **Forge lab / DEFAULT** image is `budget-repo/shutdown_vm:0.0.13`. Stay at **$0**, do **not** fire a real $1 budget alert, **do not SoftStop the door**.

**Product path (required before official release — V1 Step 8.6.1):** developer pre-builds `linux/arm64` with Docker Desktop; Setup **copies** the tarball into the user’s OCIR. **Users** do not install Docker Desktop, `fn`, or use Cloud Shell. GitHub Actions is not required. Cloud Shell / Code Editor remain lab break-glass only (`oci fn` never builds an image). Later code fixes: rebuild the tar, ship with a new Manager / installer; Deploy / repair converges digest. Function **config** (VM1 OCID, bucket, lock key) stays tofu-owned — no rebuild.

Tracked placeholders only — resolve OCIDs from Function config / gitignored `data/config.local.json`. Do not bake live OCIDs into git.

## Product decision (door Micro)

**Do not SoftStop VM2.** Oracle [Always Free Resources](https://docs.oracle.com/en-us/iaas/Content/FreeTier/freetier_topic-Always_Free_Resources.htm) (read 2026-08-17):

- AMD **`VM.Standard.E2.1.Micro`** is its own Always Free allowance (up to **two** instances). It is **not** drawn from the Ampere A1 OCPU-hour / GB-hour envelope.
- After a PAYG upgrade, Oracle still does not charge **Always Free** resources; only usage **above** those limits is billed.

The product door is one Always Free Micro. Leaving it running does not accrue Ampere spend, and it keeps MOTD / reconcile / IP parking alive. Stopping it caused FN-ISSUE-1 (no handback while the door is down).

Override only if a future Always Free change makes Micro billable, or if the operator explicitly accepts stopping the door. Product OpenTofu default stop-list is **VM1 only** (`softstop_instance_ids`).

## What it does

Triggered by the compartment **$1 actual-spend** budget alert (Events: `Budgets: TriggeredAlert - Create`). Monthly RESET uses the **same** event type.

1. Parse the event JSON for an alert type (any `triggeredAlertType` / `triggered_alert_type`) and a budget OCID (`additionalDetails.budgetId`, else Function config `BUDGET_ID`).  
2. **Skip** (no SoftStop, **no lock PUT**, **no lock DELETE**) when:  
   - the parsed type is **`RESET`** or **`FORECAST`**, or  
   - `get_budget` shows `actual_spend` below the budget amount, or  
   - the envelope is unconfirmed (no type and spend unknown).  
3. **Act** only when spend has reached the amount, or when the parsed type is **`ACTUAL`** and spend could not be read: using a **resource-principals** signer:  
   - `SOFTSTOP` every OCID in `INSTANCE_OCIDS` (product default: **VM1 only**). Already STOPPED / STOPPING is success (`SKIPPED`).  
   - **PUT** Object Storage **`meta/spend-brake-triggered.json`** (v1 JSON: `version`, `triggered_at`, `updated_at`, `source=budget_function`, optional `alert_type`, `reason=compartment_budget_threshold`). Idempotent replace.  
4. Overall `ERROR` if any SoftStop **or** the lock PUT fails (so OCI can retry). SoftStop still runs if the lock PUT will fail, and the lock is still written if SoftStop failed.

It does **not**:

- SoftStop the door Micro (product v1)  
- DELETE or auto-clear the lock at month rollover  
- Move the reserved play IP (door reconcile can, because the door stays up)  
- Stop Minecraft gracefully or take a world backup (OS-ISSUE-6)

## Function config

| Key | Required | Notes |
|-----|----------|--------|
| `INSTANCE_OCIDS` | yes | Comma-separated instance OCIDs to SoftStop. Product HCL passes **VM1 only**. |
| `OS_NAMESPACE` | yes | Object Storage namespace (for the lock PUT). |
| `OS_BUCKET` | yes | Shared bucket (product `mcmgr-shared-data`). |
| `OS_LOCK_OBJECT` | no | Default `meta/spend-brake-triggered.json`. |
| `BUDGET_ID` | yes | Budget OCID for `get_budget` when the Events payload omits `budgetId`. Product HCL passes `oci_budget_budget.one_usd.id`. |

IAM: Functions dynamic group needs `use instance-family` (SoftStop), **object write on the product bucket** (lock PUT), and **`read usage-budgets` in tenancy** (`get_budget` spend gate). Product tofu grants all three.

## Files

| File | Role |
|------|------|
| `func.py` | FDK handler + testable event/lock helpers |
| `test_func.py` | Mocked Events payloads / lock JSON (no OCI) |
| `func.yaml` | Fn project metadata (memory 256 MiB, Python 3.12) |
| `requirements.txt` | `fdk`, `oci` |

`INSTANCE_OCIDS` placeholders in git must stay placeholders. The shipped image must read `INSTANCE_OCIDS` from Function config/env (Setup’s publisher rewrites the baked list when it still builds locally). A developer rebuild must produce the same env-driven image (recipe below).

## Developer rebuild (Docker Desktop)

Users never run this. Produce the gitignored ARM tarball Setup copies into OCIR (`FunctionImageArtifact.FileName`). Do not commit the tar. Do not add GitHub Actions.

From the product repo, with Docker Desktop running:

1. Stage like Setup (`OcirFunctionPublisher.StageFunctionSources`): copy `func.py`, `requirements.txt`, and `func.yaml` into a temp directory; rewrite the baked `INSTANCE_OCIDS = [...]` list to read from the `INSTANCE_OCIDS` env var (skip placeholder OCIDs); write the FDK Python **3.12** Dockerfile Setup uses.
2. Build ARM and write a docker-archive tarball (no registry push). Prefer `-o type=docker,dest=...` so a Windows/amd64 Docker Desktop does not need to `--load` arm64:

```bash
mkdir -p artifacts
docker buildx build --platform linux/arm64 --provenance=false --sbom=false \
  -t mcmgr-fn/softstop:setup \
  -o type=docker,dest=artifacts/mcmgr-fn-softstop-linux-arm64.tar \
  <staging-dir>
```

`docker save mcmgr-fn/softstop:setup -o artifacts/mcmgr-fn-softstop-linux-arm64.tar` after a `--load` is equivalent when the engine can load arm64.

Setup copies that file into the user’s OCIR. `MCMANAGER_FUNCTION_IMAGE_TAR` can point at another path. Rebuild whenever `functions/shutdown_vm/` changes; Deploy / repair converges digest. Function **config** (VM1 OCID, bucket, lock key) stays tofu-owned — no rebuild.

## Tests

From this directory (stdlib `unittest`; does not import OCI/FDK at collection time beyond `func.py` helpers):

```bash
python -m unittest test_func.py
```

No live budget fire.
