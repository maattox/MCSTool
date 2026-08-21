# IAM reference (historical lab, sanitized)

Hand-copied from the original Always Free tenancy. **No OCIDs.** Matching rules below are the *lab* shape (instance-OCID pins). Product OpenTofu must **not** copy those rules — see [Recommended product model](#recommended-product-model-3-dynamic-groups) and [`Lab-Reference-Stack-Notes.md`](Lab-Reference-Stack-Notes.md).

Lab IAM lived in the **tenancy root / Default** compartment. Product greenfield uses a dedicated `mcmgr` compartment.

Display-name typo in the lab: the Object Storage policy refers to `mc-instances-dg` (with an **s**). The operator note called the same group `mc-instance-dg`. Treat them as one group.

---

## Lab dynamic groups (do not copy names or matching rules)

### 1. `mc-instances-dg` (also written `mc-instance-dg`)

| | |
|--|--|
| **Members** | Both VMs (door + VM1) |
| **Lab matching rule** | `Any {instance.id = '<DOOR_INSTANCE_OCID>', instance.id = '<VM1_INSTANCE_OCID>'}` |
| **Purpose** | Object Storage (buckets + objects) |

### 2. `mc-door`

| | |
|--|--|
| **Members** | Door Micro only |
| **Lab matching rule** | `All {instance.id = '<DOOR_INSTANCE_OCID>'}` |
| **Purpose** | Start/stop VM1; move reserved public IP + secondary private IPs |

### 3. `mc-server-instances`

| | |
|--|--|
| **Members** | VM1 only |
| **Lab matching rule** | `All {instance.id = '<VM1_INSTANCE_OCID>'}` |
| **Purpose** | Idle-agent self SoftStop |

### 4. `BudgetFunctionsDynamicGroup`

| | |
|--|--|
| **Members** | **Every** function in the tenancy |
| **Lab matching rule** | `ALL {resource.type = 'fnfunc'}` |
| **Purpose** | $1 budget Function SoftStop |

Too broad for the product. Scope to the `mcmgr` compartment.

---

## Lab policies (statements only)

Ignore `Tenant Admin Policy` (`ALLOW GROUP Administrators to manage all-resources IN TENANCY`). That is a tenancy default, not product IAM.

### `AllowVMsToAccessObjectStorage`

```text
Allow dynamic-group 'Default'/'mc-instances-dg' to manage buckets in tenancy
Allow dynamic-group 'Default'/'mc-instances-dg' to manage objects in tenancy
```

**Product:** do not grant tenancy-wide `manage buckets` / `manage objects`. Scope object write (and inspect/read as needed) to bucket `mcmgr-shared-data` in compartment `mcmgr`. VMs do not need `manage buckets` if Setup creates the bucket.

### `AllowDoorToOrchestratePrimary`

```text
Allow dynamic-group mc-door to use instance-family in tenancy
Allow dynamic-group mc-door to manage public-ips in tenancy
Allow dynamic-group mc-door to use private-ips in tenancy
Allow dynamic-group mc-door to use virtual-network-family in tenancy
```

**Product:** prefer `in compartment mcmgr`. Verify during Step 3.1 whether reserved-IP move still works when those verbs are compartment-scoped; if a verb is tenancy-only, document why in the module README.

### `AllowServerVMtoManageItself`

```text
Allow dynamic-group mc-server-instances to use instance-family in compartment id <TENANCY_OCID>
Allow dynamic-group mc-server-instances to read instances in compartment id <TENANCY_OCID>
```

Lab used the tenancy OCID as the compartment id because the stack lives in root. Product: `in compartment mcmgr`.

### `AllowFunctionsToManageInstances`

```text
Allow dynamic-group BudgetFunctionsDynamicGroup to manage instances in compartment id <TENANCY_OCID>
```

Lab is **SoftStop-only** and uses `manage instances` (broader than needed). Product: `use instance-family` in `mcmgr` is enough for SoftStop. Also grant **object write on `mcmgr-shared-data` only** (v1 spend-brake lock PUT; unused in MVP Function code).

---

## Recommended product model (3 dynamic groups)

Collapse the lab’s four groups. Match on **compartment** and **tags**, never on instance OCIDs (those change on recreate).

| Product name | Matching rule (sketch) | Policy intent |
|--------------|------------------------|---------------|
| `mcmgr-dg-instances` | `ALL {instance.compartment.id = '<mcmgr>'}` | Object Storage on `mcmgr-shared-data`; `use instance-family` in `mcmgr` (VM1 idle SoftStop; door can start/stop VM1) |
| `mcmgr-dg-door` | Tag `mcmgr-role=door` (or equivalent) **in** `mcmgr` | Extra verbs: `manage public-ips`, `use private-ips`, `use virtual-network-family` — **not** on every instance in the compartment |
| `mcmgr-dg-fn` | `ALL {resource.type = 'fnfunc', resource.compartment.id = '<mcmgr>'}` | SoftStop (`use instance-family` in `mcmgr`) + object write on `mcmgr-shared-data` |

Drop a separate VM1-only group: idle SoftStop is covered by `mcmgr-dg-instances`. Do **not** give the whole instances group IP-move rights.

Dynamic group `compartment_id` in the OCI provider **must be the tenancy OCID** (Oracle requirement). That is not the same as matching `instance.compartment.id`.

Freeform tags to set at create time:

| Tag | On | Value |
|-----|----|--------|
| `mcmgr-domain` | compartment `mcmgr` | `mc-server-compartment` |
| `mcmgr-role` | VM1 | `vm1` |
| `mcmgr-role` | door | `door` |
