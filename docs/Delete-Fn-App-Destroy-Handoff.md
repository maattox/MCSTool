# Handoff — Delete infrastructure fails on Functions application

**For:** a fresh Agent chat. Implement this only. Do **not** start P6/P7 or other `docs/NEXT.md` work unless the operator says to continue after this fix.

**Created:** 2026-08-28 (operator wipe of TESTING; off-queue).  
**Cost:** `$0`. Profile **`TESTING` only**. Never `DEFAULT` / live Forge lab. Never Minecraft `0.0.0.0/0`. Do not SoftStop the door casually. Do not `git push`.

---

## Operator intent

Danger Zone **Delete infrastructure** failed mid-destroy. The operator wants the **product** path fixed so Delete succeeds when a spend-brake Function exists in `mcmgr-fn-app` even if that Function was **never imported into OpenTofu state**. Then they can finish wiping TESTING and run a clean installer Setup.

Do **not** only Console-patch TESTING. Mirror the fix into Manager Delete (`src/McManager.Core/Setup/`).

---

## Symptom

`tofu destroy` (from Manager Delete) ended with:

```text
Error: 400-InvalidParameter, Invalid Application cannot be deleted while it has associated functions
Service: Functions Application
Operation Name: DeleteApplication
tofu destroy failed. Local config was kept so you can retry. See the log.
```

Operator-local full log (gitignored, has live OCIDs — do **not** copy IDs into tracked docs):  
`development/tofu-destroy-fail.txt`

That run **did** destroy VMs, reserved play IP, IAM policies/DGs, `$1` budget + alert, shared bucket, and the OCIR repo `mcmgr-fn/softstop`. It **started** destroying `module.budget_brake.oci_functions_application.app` (`mcmgr-fn-app`) and failed there. Compartment, VCN, and remaining network were **not** deleted. Retry Delete; do not greenfield until destroy completes.

---

## Root cause

OpenTofu state for this TESTING stack has the **Functions application** (and budget, OCIR repo, compute, …) but **not**:

- `oci_functions_function.softstop`
- `oci_events_rule.budget_alert`

Those were created **outside tofu** (TESTING fill-in, OCI CLI). Product HCL still gates them on a non-empty `function_image`:

```113:130:infra/modules/budget_brake/main.tf
resource "oci_functions_function" "softstop" {
  count              = local.create_function ? 1 : 0
  application_id     = oci_functions_application.app.id
  ...
}

resource "oci_events_rule" "budget_alert" {
  count          = local.create_function ? 1 : 0
  ...
}
```

`local.create_function = trimspace(var.function_image) != ""`. Empty `function_image` → tofu never manages Function/Events, but Setup still creates the **app**. OCI refuses `DeleteApplication` while any Function remains in that app.

Destroy already special-cases leftovers tofu cannot delete:

- Empty Object Storage bucket
- Purge OCIR images (`OcirImagePurger`) so the container repo can go away

There is **no** equivalent for Functions (or Events) that exist in OCI but not in state. `InfrastructureDestroyOrchestrator` even documents that it does not delete resources never in tofu state — then tofu still tries to delete the empty-looking app.

A **clean Setup** that writes `function_image` and lets tofu create `oci_functions_function.softstop` should destroy Function then app via implicit deps. This hole still matters for:

- TESTING-style CLI fill-in (this failure)
- Any repair/copy path that creates/updates the Function via API without importing it into state
- Retry after a partial destroy (current TESTING)

---

## What to implement

**Product:** before `tofu destroy`, best-effort delete leftover **Functions inside the product application** (display name `mcmgr-fn-app`), same pattern as `OcirImagePurger` (continue on failure; log; do not abort the whole Delete unless you have a clear reason).

Also delete the product **Events rule** (`mcmgr-events-budget-alert`) if it is not in tofu state — otherwise compartment destroy can fail next with “not empty.”

Suggested shape:

1. `src/McManager.Core/Setup/InfrastructureDestroyOrchestrator.cs` — call a new helper after OCIR purge, before `tofu destroy` (same TESTING session / compartment as bucket + OCIR).
2. New helper next to `OcirImagePurger.cs` (Functions + Events). `OciSession` today has Compute, Identity, VCN, Object Storage, **Artifacts** — **no** Functions/Events clients yet; add them with the same retry/429 patterns as [`docs/OCI-API-Usage.md`](OCI-API-Usage.md).
3. Identify the app by product display name in the stack compartment (from tofu outputs / local config), not by pasting live OCIDs into code.
4. List functions in that app → delete each → then tofu can `DeleteApplication`. Delete the Events rule by display name in that compartment.
5. 404 / already gone = success. Pagination. No PAT. Profile from local config (`TESTING` in this chat).
6. Unit tests with fakes/fixtures if that is the local pattern; do not hit live GitHub; do not `tofu apply` a second stack.

**Do not:** `tofu apply` a second Always Free A1; import the live Function into state as the only fix; buy a signing cert; add GitHub Actions; fire a real `$1` budget.

---

## TESTING stack (this chat)

Partial destroy already happened. Operator may Console-delete `mcmgr-fn-softstop` + `mcmgr-events-budget-alert` and retry Delete **or** wait for this code and retry Delete from Manager (state + local config were kept).

If you verify on live TESTING: **ask the operator first** before another `tofu destroy`. Stay `$0`. Read [`docs/Agent-Deploy-Pitfalls.md`](Agent-Deploy-Pitfalls.md) before SSH. VM1/door may already be gone.

---

## Docs (same session as the code fix)

- File **SETUP-ISSUE-14** in [`docs/Issues.md`](Issues.md) (next SETUP-ISSUE id as of 2026-08-28).
- Operator copy-paste (delete Function + Events if Delete still fails): [`docs/Operator-Troubleshooting.md`](Operator-Troubleshooting.md).
- Short Guide note only if user-visible Delete copy should mention “retry if the Function was added outside Setup.”
- Do **not** rewrite the whole Guide. Do **not** advance `docs/NEXT.md` off P6 unless the operator asks.

---

## Done when

Manager Delete can destroy `mcmgr-fn-app` even when the spend-brake Function/Events exist in OCI but not in OpenTofu state. TESTING leftover is either cleaned by the new helper on retry, or documented as operator Console steps. No live OCIDs in tracked markdown.
