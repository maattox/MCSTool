# Lab Resource Manager dump — notes for product OpenTofu

**Status:** Digest of the 2026-08-12 lab discovery capture. Authoritative for **what to copy vs rewrite** when implementing MVP Step 3.1.  
**Do not apply** the lab dump. **Do not import** the live Forge lab into product state.

**Related**

| Doc | Role |
|-----|------|
| [`Automated-Infrastructure-Deployment.md`](Automated-Infrastructure-Deployment.md) | Locked IaC decisions (OpenTofu on the admin PC, images, state, bootstrap split) |
| `PRODUCT-IDEAS.md` → Product resource naming | `mcmgr-…` display names |
| [`Lab-IAM-Reference.md`](Lab-IAM-Reference.md) | Sanitized lab IAM statements (do not copy matching rules) |
| [`Infrastructure-Information.md`](Infrastructure-Information.md) | Architecture (placeholders) |

---

## 1. What was captured

The operator created a Resource Manager stack from **Existing compartment**, all services **except Identity**, downloaded configuration (+ state), and wrote IAM notes by hand.

**Identity still leaked.** Discovery emitted `identity_domains.tf` (users, API public-key PEM, auth-token resources) even with Identity unchecked. That file was wiped from the lab pack. Do not ask for a second tenancy-root identity scan. IAM for product comes from the sanitized [`Lab-IAM-Reference.md`](Lab-IAM-Reference.md) statements plus the 3-group model below.

No need to re-run Resource Manager for Phase 3.

---

## 2. Lab → product names

Lab Console names were ad-hoc. Product OpenTofu **must** use the right-hand column.

| Lab display name | Role | Product name |
|------------------|------|----------------|
| Default (tenancy root) | stack compartment | dedicated compartment `mcmgr` + tag `mcmgr-domain=mc-server-compartment` |
| `minecraft-vcn` | VCN `10.0.0.0/16` | `mcmgr-vcn` |
| `public subnet-minecraft-vcn` | public subnet `10.0.0.0/24` | `mcmgr-subnet-public` |
| Internet gateway-minecraft-vcn | IGW | `mcmgr-igw` |
| Default Security List for minecraft-vcn | Minecraft / SSH / door :8080 | dedicated `mcmgr-sl` (not the VCN default SL) |
| `minecraft-vm3` | VM1 A1 Flex 4 OCPU / 24 GB | `mcmgr-vm1` |
| `minecraft-vm-door` | VM2 E2.1.Micro | `mcmgr-door` |
| hostname `minecraft-vm3` / `10.0.0.167` | VM1 primary (ephemeral public IP) | let OCI assign or document; SSH/admin |
| `vm3-play` / `10.0.0.168` | VM1 secondary | `mcmgr-vm1-play` — reserved-IP target when playing |
| hostname `door` / `10.0.0.92` | door primary | `mcmgr-door` hostname; ephemeral SSH |
| `door-play` / `10.0.0.236` | door secondary | `mcmgr-door-play` — reserved-IP target when idle |
| `PrimaryConnection` | reserved play public IP | `mcmgr-play-ip` |
| `minecraft-shared-data` | Object Storage bucket | `mcmgr-shared-data` |
| `dollar-limit` | $1 monthly budget | `mcmgr-budget-1usd` |
| `BudgetControlApp` | Functions application (`GENERIC_ARM`) | `mcmgr-fn-app` |
| `shutdown_vm` | Function 256 MB / 30s | `mcmgr-fn-softstop` |
| `budget-repo/shutdown_vm` | private OCIR repo | `mcmgr-fn/softstop` |
| `Budget-Alerts` | ONS topic (unused leftover) | **do not create** |
| `AutoShutdownOnBudgetAlert` | Events rule → Function | `mcmgr-events-budget-alert` |
| `mc-instances-dg` / `mc-instance-dg` | both VMs → Object Storage | `mcmgr-dg-instances` |
| `mc-door` | door → IP move + start/stop VM1 | `mcmgr-dg-door` |
| `mc-server-instances` | VM1 self SoftStop | **drop** (folded into `mcmgr-dg-instances`) |
| `BudgetFunctionsDynamicGroup` | all `fnfunc` in tenancy | `mcmgr-dg-fn` (compartment-scoped) |

Freeform tags at create:

| Tag | Resource | Value |
|-----|----------|--------|
| `mcmgr-domain` | compartment | `mc-server-compartment` |
| `mcmgr-role` | VM1 | `vm1` |
| `mcmgr-role` | door | `door` |

---

## 3. Dynamic-group consolidation (4 → 3)

Lab pins **instance OCIDs**. Product matches **compartment** for all instances and the Function; **door DG is pinned by door instance OCID** (hyphenated `mcmgr-role` tag matching did not enroll the door on the identity-domain 3.3 test).

| Product DG | Match | Grants |
|------------|-------|--------|
| `mcmgr-dg-instances` | `ALL {instance.compartment.id = '<mcmgr>'}` | Object access on `mcmgr-shared-data`; `use instance-family` in `mcmgr` (idle SoftStop + door start/stop VM1) |
| `mcmgr-dg-door` | `ALL {instance.id = '<door OCID>'}` | reserved-IP verbs via tenancy policy `mcmgr-door-ip` — **not** on every instance |
| `mcmgr-dg-fn` | `ALL {resource.type = 'fnfunc', resource.compartment.id = '<mcmgr>'}` | `use instance-family` in `mcmgr` + **object write on the product bucket** (v1 lock PUT; unused in MVP Function code) |

Do **not** copy:

- Tenancy-wide `manage buckets` / `manage objects`
- `ALL {resource.type = 'fnfunc'}` with no compartment predicate
- `manage instances` for the Function (SoftStop only needs `use instance-family`)
- Instance-OCID matching for **all-instances** or Function groups (door is the exception)

`oci_identity_dynamic_group.compartment_id` **must be the tenancy OCID**. That is not the matching rule.

Prefer `in compartment mcmgr` on every policy statement except reserved-IP move: `manage public-ips`, `use private-ips`, and `use virtual-network-family` must be **in tenancy** (`mcmgr-door-ip`). Compartment-only public-ip statements 404 on `UpdatePublicIp`.

---

## 4. Copy vs skip

### Create in product IaC (rewrite, don’t paste)

- Dedicated compartment `mcmgr`
- VCN `10.0.0.0/16`, public subnet `10.0.0.0/24`, IGW, default route `0.0.0.0/0` → IGW
- Dedicated Security List: ICMP as in the lab; subnet `10.0.0.0/24` → 25565 TCP (door `wait_forge`); admin `/32` for 22, 25565 TCP+UDP, 8080 TCP. **No** `0.0.0.0/0` on 22 / 25565 / 8080
- VM1 `VM.Standard.A1.Flex` 4/24, Ubuntu 22.04 **aarch64** via `data.oci_core_images`
- Door `VM.Standard.E2.1.Micro`, Ubuntu 22.04 **x86_64**, `assign_public_ip = true`
- Secondary private IP on each VNIC; reserved public IP attached to **door secondary** at create (idle)
- Private Standard bucket, no versioning, no object events
- $1 ACTUAL ABSOLUTE monthly budget: **CreateBudget `compartment_id` is the tenancy OCID**; `targets` is the mcmgr compartment. Email from wizard. (Lab dump used tenancy-root `compartment_ocid` because the lab stack lived in Default.)
- Functions app `GENERIC_ARM` + function 256 MB / 30s + private OCIR repo
- Events rule on `com.oraclecloud.budgets.createtriggeredalert` with a **Functions** action to `mcmgr-fn-softstop` (Resource Manager omitted the action in the dump; Console has it)
- Three dynamic groups + compartment/bucket-scoped policies
- `ignore_changes` on instance `metadata` after first boot; Security List ingress owned by Manager after Setup

### Do not create

| Lab leftover | Why |
|--------------|-----|
| NAT gateway + private subnet `10.0.1.0/24` + service gateway + private SL | VCN wizard leftovers. Both VMs are on the public subnet. NAT is not a separate OCI billing SKU, but it is unused surface. |
| IPv6 GUA on the public subnet | Extra attack surface; product is IPv4-only unless later decided |
| `oci_objectstorage_object` / PARs | Runtime (ledger, backups, meta). Setup seeds JSON after apply |
| Identity Domains / users / API keys / auth tokens | Tenancy furniture + secrets |
| Cloud Guard, License Manager, Recovery, NoSQL, metering, Oracle-Tags, VCN DNS extras | Tenancy defaults / unused |
| ONS topic `Budget-Alerts` + Function subscription | Abandoned attempt to have the budget call the Function via Notifications. The `$1` budget is **not** linked to that topic (email recipients only). Do not create an ONS topic for the spend brake. |
| Lab plugin list (Custom Logs Monitoring, Cloud Guard Workload Protection enabled) | Do not enable extras that look like paid/log-ingest |
| Friend `/32` laundry list | Day-2 Manager whitelist |

---

## 5. Budget → Function wiring

**Live lab path** (operator-confirmed in Console):

```text
$1 budget alert (email recipients only)
  → OCI Events emits com.oraclecloud.budgets.createtriggeredalert
    → rule AutoShutdownOnBudgetAlert
      → Functions action → BudgetControlApp / shutdown_vm
```

Resource Manager discovery found that **same** Events rule but left `actions { #action = <<Optional value not found in discovery>> }`. That is an export gap, not a missing Console action. Private notes already listed Target Service Type = Functions / `shutdown_vm`.

**ONS is leftover, not the trigger.** Topic `Budget-Alerts` exists and has an `ORACLE_FUNCTIONS` subscription to `shutdown_vm`, but the `dollar-limit` budget is **not** linked to that topic. It was an abandoned Console attempt; the Function is not invoked via Notifications. Product OpenTofu must **not** create an ONS topic for this. Optional lab cleanup: delete `Budget-Alerts` in Console (does not affect the Events path).

Function stop list stays a **variable** defaulting to both VMs (lab). Whether the Always Free Micro should stay up is still open in PRODUCT-IDEAS. Spend-brake **lock object** is v1 runtime state — not a tofu resource — but IAM object-write for `mcmgr-dg-fn` is granted in MVP tofu.

---

## 6. Other product IaC notes

- Lab `compartment_id` on many resources pointed at a License Manager configuration data source. That is a discovery artifact. Use the real compartment OCID.
- `are_legacy_imds_endpoints_disabled = true` is fine to keep.
- Boot volume VPUs were `10` (balanced). Stay on default/Always Free–safe; do not add extra volumes.
- Door and VM1 used different fault domains (FD-3 vs FD-2). Nice-to-have, not required. Product Setup no longer pins the door (`FAULT-DOMAIN-3` was causing Micro `Out of host capacity` while other FDs could still place).
- Security List descriptions in the lab are player names (OCI Minecraft rule ownership). Keep that convention in Manager sync, not in tofu friend rules.

---

## 7. Open items (not 3.1 blockers)

These can stay decisions inside Step 3.1 / later; they do not require another discovery dump.

1. Confirm IP-move verbs can be compartment-scoped.
2. Door-stop vs Micro-always-free (PRODUCT-IDEAS open; variable default = both VMs).
3. Exact v1 lock object key (do not invent it in tofu).
4. Optional: operator may delete the idle Resource Manager **stack object** in Console (do not Destroy resources), and may delete unused ONS topic `Budget-Alerts`.

---

## Changelog

| Date | Note |
|------|------|
| 2026-08-14 | Product IAM: door DG by instance OCID; reserved-IP verbs in tenancy (`mcmgr-door-ip`). Tag matching did not enroll the door on the identity-domain 3.3 test. |
| 2026-08-12 | Initial digest from sanitized lab discovery pack. |
