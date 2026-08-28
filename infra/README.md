# OpenTofu module — MC Manager greenfield stack

Product IaC for a private Always Free doorbell (VM1 A1 Flex + door Micro + reserved play IP).  
**Engine:** OpenTofu (`tofu`), provider `oracle/oci`. Not HashiCorp Terraform. Not OCI Resource Manager.

Authority: [`docs/Automated-Infrastructure-Deployment.md`](../docs/Automated-Infrastructure-Deployment.md), [`docs/Lab-Reference-Stack-Notes.md`](../docs/Lab-Reference-Stack-Notes.md).

**Step 3.1:** skeleton is validatable / plan-able. **Do not `tofu apply` on the live lab tenancy.** Setup (Step 3.3) applies from the wizard using **LocalAppData state**, not this directory’s `terraform.tfvars`. Applying here would create a second Always Free stack that competes with the running lab for Ampere / Micro / reserved-IP envelopes.

Setup writes `vm1_ocpus` / `vm1_memory_gb` (**4 / 24** default, or **2 / 12**). HCL defaults match **4 / 24** if those variables are omitted.

---

## Prerequisites

1. OpenTofu **1.6+** (1.12.x recommended). `tofu version`
2. `~/.oci/config` + API key. The API user must be able to create compartments, dynamic groups, and policies (typically **Administrators**).
3. Copy [`terraform.tfvars.example`](terraform.tfvars.example) → `terraform.tfvars` (gitignored) and **replace every placeholder**. OpenTofu does **not** read `tenancy=` from `~/.oci/config` into variables — you must paste the OCID. `REPLACE_ME`, `AAAA... comment`, `203.0.113.10`, and `you@example.com` are documentation stubs and will fail plan or create the wrong Security List / budget email.

```powershell
cd infra
copy terraform.tfvars.example terraform.tfvars
# edit terraform.tfvars
tofu init
tofu validate
tofu plan
# tofu apply   # operator-only; never on the live Forge lab. Setup uses %LOCALAPPDATA%\McManager\tofu instead.
```

State for **manual** `tofu` in this folder stays here (`terraform.tfstate`, gitignored). **Setup** writes variables and state under `%LOCALAPPDATA%\McManager\tofu\<stack-id>\` and never overwrites this repo’s `terraform.tfvars`. Do not copy Setup’s LocalAppData tfvars over the 3.1 lab file. Encryption of tofu state is later (Phase 7).

---

## What this module creates

| Resource | Product name |
|----------|----------------|
| Compartment + tag `mcmgr-domain=mc-server-compartment` | `mcmgr` |
| VCN / public subnet / IGW | `mcmgr-vcn`, `mcmgr-subnet-public`, `mcmgr-igw` |
| Dedicated Security List | `mcmgr-sl` |
| VM1 A1 Flex (**4/24** default; Setup can pick **2/12**) Ubuntu 22.04 aarch64 | `mcmgr-vm1` |
| Door E2.1.Micro Ubuntu 22.04 x86_64 | `mcmgr-door` |
| Play secondaries + reserved public IP (idle: on door) | `mcmgr-vm1-play`, `mcmgr-door-play`, `mcmgr-play-ip` |
| Private Standard bucket | `mcmgr-shared-data` |
| Dynamic groups | `mcmgr-dg-instances`, `mcmgr-dg-door`, `mcmgr-dg-fn` |
| $1 budget + email alert | `mcmgr-budget-1usd` |
| Functions app + private OCIR repo | `mcmgr-fn-app`, `mcmgr-fn/softstop` |

**Not created:** NAT gateway, private subnet, service gateway, IPv6, NSGs, ONS topic, Object Storage objects (ledger/meta/backups), Minecraft/Java/door binaries.

**Gated on `function_image`:** Function `mcmgr-fn-softstop` and Events rule `mcmgr-events-budget-alert`. Leave the variable empty until Setup has copied the pre-built ARM image into OCIR (V1 Step **8.6.1**). Developer Docker Desktop may produce that tar; **users** do not need Docker.

---

## Security List vs Manager whitelist

Tofu writes **structural** ingress only:

- ICMP (type 3 code 4 from `0.0.0.0/0`; type 3 from the VCN)
- Subnet → 25565 TCP (door `wait_forge`)
- Admin `/32` → SSH 22, Minecraft 25565 TCP+UDP, door 8080

Descriptions match Manager ownership (`"{name} SSH access"`, name, `"{name} door access"`).

`lifecycle { ignore_changes = [ingress_security_rules] }` so day-2 Avalonia whitelist sync does not fight state. Friend `/32`s are **not** tofu resources. RCON 25575 is never opened. No `0.0.0.0/0` on 22 / 25565 / 8080.

---

## cloud-init vs SSH bootstrap

`user_data` is OS baseline only (Minecraft blueprint §13.1):

| VM1 | Door |
|-----|------|
| `mcmgr` user/group, empty `/opt/mcmgr` + `/etc/mcmgr` + `/var/lib/mcmgr` with blueprint **§5** owners (`root:mcmgr` `0750` on `/opt/mcmgr`, **not** `chown -R mcmgr`; SoT is `onbox/mcmgr/common/layout.sh`) | hostname + `jq`/`curl` |
| Adoptium **apt repo registration** (no `temurin-*` package) | no game tree, no firewalld |
| firewalld: SSH + 25565 tcp/udp **without** source IPs (Security List is the IP allowlist). Cloud-init **masks `netfilter-persistent`** (SETUP-ISSUE-7), **masks UFW**, and writes `/etc/systemd/system/firewalld.service` (no `network-pre`) so boot does not delete dbus (OS-ISSUE-9). | iptables comes with `door_vm` in 3.3 |
| marker `/etc/mcmgr/cloud-init-done` (0750 dir — Setup waiter uses `sudo -n test -f`, SETUP-ISSUE-5) | marker `/etc/mcmgr-door/cloud-init-done` |

Instance `metadata` is `ignore_changes` after create so later template tweaks do not recreate VM1 (world lives on that boot volume). OS-baseline fixes for *new* deploys do not retrofit old VMs — those use SSH repair.

**Never in user_data:** Mojang/Paper APIs, a Java major, jars, RCON, API keys, door binaries.

---

## IAM

Dynamic groups use `compartment_id = <tenancy OCID>` (Oracle requirement). `mcmgr-dg-instances` and `mcmgr-dg-fn` match the **product compartment**. `mcmgr-dg-door` matches the **door instance OCID** (hyphenated `mcmgr-role` tag matching did not enroll the door on the identity-domain 3.3 test).

| Group | Match | Grants |
|-------|--------|--------|
|-------|--------|-----------------------------------------------|
| `mcmgr-dg-instances` | all instances in the stack compartment | object read/write on the product bucket; `use instance-family` |
| `mcmgr-dg-door` | `instance.id = <door OCID>` (tag matching did not enroll the door on the identity-domain test tenancy) | reserved-IP verbs via `mcmgr-door-ip` (tenancy) |
| `mcmgr-dg-fn` | `fnfunc` in that compartment | `use instance-family`; object write on the product bucket (v1 spend-brake lock PUT) |

No tenancy-wide `manage buckets` / `manage objects` / `manage instances`.

**Reserved-IP verbs:** `UpdatePublicIp` returns `NotAuthorizedOrNotFound` when these verbs are only compartment-scoped. Policy `mcmgr-door-ip` lives at the **tenancy root**: `manage public-ips`, `use private-ips`, and `use virtual-network-family` **in tenancy**. Compartment `mcmgr-stack` still repeats the same door verbs for other VCN work.

Identity-domain tenancies: classic `oci iam dynamic-group list` may show `matching-rule: null` even when tofu set a rule. Policy text `dynamic-group mcmgr-dg-instances` (no `'Default'/` prefix) was enough for `use instance-family` on the 3.3 test; if plan/apply errors on unknown dynamic group, try `dynamic-group 'Default'/'mcmgr-dg-instances'`.

### Importing `mcmgr-door-ip` into Setup state

Greenfield `tofu apply` from current HCL **creates** this policy. Import is only needed if it was created **out of band** (CLI/Console) so the next apply does not collide on the name.

Setup state lives under `%LOCALAPPDATA%\McManager\tofu\<stack-id>\` (**no** `.tf` files). Config is the repo `infra/` directory. Do not `cd` into LocalAppData and run `tofu import`.

PowerShell does **not** expand `$var` in unquoted `-flag=$var` (tofu then looks for a directory literally named `$infra`). Quote: `-state="$state"`.

This OpenTofu `import` subcommand has **no** `-chdir` option (`-chdir` is global: `tofu -chdir="$infra" import …`). From `infra/`, omit chdir:

```powershell
$Env:OCI_CLI_SUPPRESS_FILE_PERMISSIONS_WARNING = "True"
$tenancy = (Get-Content '<config.local.json>' -Raw | ConvertFrom-Json).oci.tenancy_id
$pol = (oci --profile TESTING iam policy list --compartment-id $tenancy --name mcmgr-door-ip --all --output json | ConvertFrom-Json).data[0].id
$infra = '<repo>\infra'
$state = "$env:LOCALAPPDATA\McManager\tofu\mcmgr\terraform.tfstate"
$vars  = "$env:LOCALAPPDATA\McManager\tofu\mcmgr\terraform.tfvars"
Set-Location $infra
tofu import -input=false -state="$state" -var-file="$vars" 'module.iam.oci_identity_policy.door_ip' $pol
```

Never `tofu import` the **live Forge lab** into product state. Importing one resource you created in the **same** greenfield/test stack into that stack’s LocalAppData state is fine.

---

## $1 budget brake

- Budget + ACTUAL ABSOLUTE $1 alert (email). Residual-charge copy is in the budget description / alert message. OCI **CreateBudget `description` max 200 characters**. When this apply also creates the stack compartment, wait 2 min before OCIR `mcmgr-fn/softstop` (Artifacts 404-DENIED on a brand-new compartment; SETUP-ISSUE-9).
- Events → Function is the live path. **No ONS topic.**
- `softstop_instance_ids` defaults to **VM1 only**. Always Free AMD Micro stays up (does not use Ampere OCPU-hours). Function config also passes `OS_NAMESPACE` / `OS_BUCKET` / `OS_LOCK_OBJECT` for the lock PUT.
- The v1 lock object (`meta/spend-brake-triggered.json`) is **runtime state**, not a tofu resource. Tracked Function source writes it (`functions/shutdown_vm/`). **Product path (before release):** pre-built ARM tarball copied into the user’s OCIR (V1 Step **8.6.1**); **users** do not need Docker Desktop / Cloud Shell. Developer Docker Desktop is OK. TESTING `fn push` remains allowed for agents; do not `fn push` the live Forge lab unless the operator authorizes it.

---

## Outputs → Manager config / `meta/infra.json`

Root outputs cover every OCID/IP in [`docs/Local-Config.md`](../docs/Local-Config.md) and nested `meta/infra.json` v2. `output.infra_meta_skeleton` is the map Step 3.3 should PUT (game stays `vanilla` / `unspecified` until SSH bootstrap). Greenfield `world_path` is `/opt/mcmgr/server/world`. **No secrets** (no SSH private key, no RCON, no Auth Token).

If `mcmgr-shared-data` is taken in the namespace, set `bucket_name` to a suffix and record the actual name in meta.

---

## Always Free constraints encoded here

- A1 Flex product default **4 OCPU / 24 GB** (Setup may pick **2 / 12**); door `VM.Standard.E2.1.Micro` with no `shape_config`
- Two × 50 GB boot volumes; no extra block volumes
- One Standard bucket; no custom-provider / image-staging bucket
- No paid LB / NAT / extra reserved IPs
- Ubuntu **22.04** platform images via `data.oci_core_images` (no hardcoded image OCIDs)
- Compute plugins disabled (`are_all_plugins_disabled`) to avoid log-ingest extras

A1 host capacity is probed with `CreateComputeCapacityReport` before apply (no VCN required). If the AD is empty, apply is skipped. Apply-time `Out of host capacity` is still handled (Retry / 5 min auto-retry; no 1 s loop). Do not tight-loop plan/apply. Neither VM1 nor the door pins a fault domain (same FD is fine) so OCI can place Always Free A1 / Micro.

---

## Troubleshooting `tofu plan`

| Symptom | Cause |
|---------|--------|
| `400-InvalidParameter` / `Invalid compartmentId` on `oci_budget_budget` | `CreateBudget` `compartment_id` must be the **tenancy** OCID; `targets` is the mcmgr compartment. |
| Validation error mentioning `REPLACE_ME` or `AAAA...` | Example placeholders left in `terraform.tfvars`. |
| Plan proposes creating `mcmgr` + VMs but you did not apply | Expected. Plan is read-only. |
| File looks like it “reset” to the example | `tofu plan` / `validate` **never write** `terraform.tfvars`. OpenTofu reads the **saved disk** file, not an unsaved editor buffer. Do not `copy terraform.tfvars.example terraform.tfvars` over a filled file. The Setup wizard writes a **different** tfvars under `%LOCALAPPDATA%\McManager\tofu`, not this file. |

---

## Explicitly out of this module

Setup wizard UI, `tofu apply` orchestration, SSH bootstrap (`onbox/mcmgr`, `door_vm`, `vm_agent`), OCIR Auth Token / `fn push`, seeding Object Storage JSON, writing `data/config.local.json`, `schema.yaml`, custom images, instance configs, importing the live Forge lab.

## Destroy (Manager Danger Zone)

Manager **Advanced / Danger Zone → Delete infrastructure** runs `tofu destroy` against `%LOCALAPPDATA%\McManager\tofu\<stack-id>\` (never this folder’s `terraform.tfvars`). It empties the product bucket and `mcmgr-fn/softstop` images first, lifts bucket `prevent_destroy` via a gitignored override for that run only, then waits until OpenTofu reports OCI deletion finished.

That path does **not** delete the Oracle tenancy or resources that were never in tofu state. Manual CLI destroy from this directory is still operator-only and still blocked by `prevent_destroy` on the bucket until that override exists.
