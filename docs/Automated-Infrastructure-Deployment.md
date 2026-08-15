# Automated infrastructure deployment blueprint

**Status:** Authoritative design for **how greenfield OCI infrastructure is created, updated, and versioned** by the Avalonia Manager Setup wizard. Researched 2026-08-12 against current Oracle Resource Manager, OpenTofu, HashiCorp BSL, Canonical Ubuntu, and Always Free docs. Re-verify version-specific facts (Resource Manager Terraform versions, OpenTofu releases, `oracle/oci` provider, Always Free envelopes) before relying on them in far-future work.

**Scope:** Cloud resources (compartment, VCN, compute, reserved IP, IAM, Object Storage, $1 budget Function) plus the **orchestration** that runs OpenTofu and then SSH-bootstraps the boxes. This document does **not** redefine how Minecraft itself is installed — that remains [`Minecraft-Server-Deployment-Blueprint.md`](Minecraft-Server-Deployment-Blueprint.md).

**Product intent authority:** lab [`PRODUCT-IDEAS.md`](../../OCI-mc-server-manager/PRODUCT-IDEAS.md). When this document and PRODUCT-IDEAS disagree on **staging** (MVP vs v1 vs later), PRODUCT-IDEAS wins. When they disagree on **IaC mechanism** (OpenTofu vs Resource Manager, image strategy, config hosting, state), **this document is authoritative** and PRODUCT-IDEAS should link here instead of re-describing details.

**Audience:** the operator (especially [§18](#18-operator-guide--what-to-capture-from-the-oci-console-for-ai-agents)), and coding agents implementing MVP Phase 3 (`infra/` OpenTofu, Setup wizard, apply + bootstrap).

**Cost rule:** keep OCI spend at **$0** (Always Free–eligible) unless the operator explicitly accepts paid changes.

---

## Table of contents

1. [Executive summary — locked decisions](#1-executive-summary--locked-decisions)
2. [Authority, related docs, and non-goals](#2-authority-related-docs-and-non-goals)
3. [Problem statement](#3-problem-statement)
4. [Why Resource Manager is not the product apply engine](#4-why-resource-manager-is-not-the-product-apply-engine)
5. [Chosen architecture](#5-chosen-architecture)
6. [Resource Manager Create-stack options — what each origin is for](#6-resource-manager-create-stack-options--what-each-origin-is-for)
7. [Recommended Resource Manager settings for a *reference* capture](#7-recommended-resource-manager-settings-for-a-reference-capture)
8. [How agents must treat a discovery dump](#8-how-agents-must-treat-a-discovery-dump)
9. [VM provisioning strategy](#9-vm-provisioning-strategy)
10. [Where bootstrap files live (not Object Storage as primary)](#10-where-bootstrap-files-live-not-object-storage-as-primary)
11. [OpenTofu module design](#11-opentofu-module-design)
12. [Idempotency and future updates](#12-idempotency-and-future-updates)
13. [Config distribution: installer vs GitHub](#13-config-distribution-installer-vs-github)
14. [State, secrets, and the admin PC](#14-state-secrets-and-the-admin-pc)
15. [Setup wizard orchestration](#15-setup-wizard-orchestration)
16. [Always Free constraints encoded in IaC](#16-always-free-constraints-encoded-in-iac)
17. [Rejected alternatives](#17-rejected-alternatives)
18. [Operator guide — what to capture from the OCI Console for AI agents](#18-operator-guide--what-to-capture-from-the-oci-console-for-ai-agents)
19. [Phase 3 implementation mapping](#19-phase-3-implementation-mapping)
20. [Reference links](#20-reference-links)
21. [Changelog](#21-changelog)

---

## 1. Executive summary — locked decisions

These are product decisions, not open research. Agents implementing Step 3.1+ must follow them.

| # | Decision | Why |
|---|----------|-----|
| **D1** | **Greenfield apply engine = OpenTofu on the admin PC**, invoked by the Manager. Document and ship it as OpenTofu (`tofu`), not HashiCorp Terraform. | PRODUCT-IDEAS already chose OpenTofu. OpenTofu is MPL 2.0 (Linux Foundation / CNCF). HashiCorp Terraform is BSL 1.1 since 2023; embedding `terraform.exe` in a product that *offers IaC to third parties* is a competitive-use gray area. Resource Manager itself runs HashiCorp Terraform 1.5.x, not OpenTofu. |
| **D2** | **OCI Resource Manager is a one-shot *reference capture* tool**, not the runtime the shipped app uses. Never `Apply` a discovery stack against the live lab. Never require end users to create Resource Manager stacks, GitHub PATs, or custom providers. | Discovery is explicitly “not a migration tool.” Generated HCL uses live Console names, omits secrets, misses tenancy-scoped IAM when pointed at a non-root compartment, and may include unrelated resources. Product IaC is **rewritten** with `mcmgr-…` names. |
| **D3** | **VMs launch from Canonical Ubuntu 22.04 *platform images*** (not custom images, not instance configurations). **VM1 = aarch64** (Ampere A1); **door = x86_64** (E2.1.Micro). | Matches the live lab OS. Platform images are free, patched monthly by Canonical/Oracle, and do not consume the 10 GB Object Storage or 200 GB Block Volume envelopes the way extra custom-image copies can. Two CPU architectures need two images anyway. |
| **D4** | **cloud-init / `user_data` = OS baseline only.** Game, Java major, door binary, idle agent, RCON, systemd unit, and Minecraft version stay in **SSH bootstrap** (`onbox/mcmgr/`, lab `door_vm/`, lab `vm_agent/`). | Already frozen in the Minecraft blueprint §13. cloud-init runs once at first boot, is capped at **32 KB** combined metadata, and must not call Mojang/Paper APIs at `tofu apply` time. |
| **D5** | **IaC lives in this repo** (`infra/` / `tofu/` as Step 3.1 creates). The Windows installer **bundles a pinned copy**. Setup **may pull a newer compatible GitHub Release** (free) and fall back to the bundle when offline. | Lets infra fix without a full app rebuild, costs $0, and still works without GitHub. Do not host the module in a paid CDN or in the user’s Object Storage as the *primary* source (chicken-and-egg + 50k request cap). |
| **D6** | **OpenTofu state stays on the admin PC** (gitignored, preferably encrypted). Outputs are also written to Object Storage `meta/infra.json` after success. Day-2 (whitelist, power, usage) stays **OCI SDK**, not a second `tofu apply`. | Avoids Resource Manager lock-in, avoids using the shared data bucket as a Terraform backend (request thrift + chicken-and-egg), and matches “one admin PC” MVP. |
| **D7** | **Do not import the operator’s live Forge lab into product OpenTofu state.** Greenfield = new `mcmgr` compartment. Connect-existing (MVP plan **Phase 5**) hydrates from `meta/infra.json` and never needs tofu state. Importing **one resource you created out of band in the same greenfield/test stack** into that stack’s LocalAppData state is OK (`infra/README.md`). | Importing a hand-built stack would freeze ad-hoc names (`minecraft-vm3`, …) and risk `tofu apply` rebuilding or destroying the working lab. |
| **D8** | **Custom Terraform providers = off** for both the reference stack and the product module. | The official `oracle/oci` provider from the OpenTofu / Terraform Registry is sufficient. Custom-provider buckets consume Object Storage and add a second binary-distribution problem. |
| **D9** | **`schema.yaml` is not required** for the product module. | Schema documents only customize the *Resource Manager Console* variable UI. The product UI is the Avalonia Setup wizard. |
| **D10** | **Bugs found on a test Setup deploy that come from HCL, IAM matching rules, cloud-init, or SSH bootstrap must be fixed in the product automated-deploy path**, not only on the live test VMs. File lab `docs/Issues.md` in the same effort. Example: SETUP-ISSUE-2 (door DG tag match + compartment-only `manage public-ips`) → product `infra/modules/iam`. | Otherwise the next greenfield run repeats the outage. |

---

## 2. Authority, related docs, and non-goals

| Doc | Role vs this file |
|-----|-------------------|
| Lab `PRODUCT-IDEAS.md` | MVP/v1 *intent* (OpenTofu, dedicated compartment, `mcmgr-…` names, Setup wizard). |
| [`MVP-Implementation-Plan.md`](MVP-Implementation-Plan.md) | Execution checklist. Phase 3 implements *this* design. |
| [`Minecraft-Server-Deployment-Blueprint.md`](Minecraft-Server-Deployment-Blueprint.md) §13–§14 | Game-layer vs infra-layer split; bootstrap resumability. |
| [`Contracts-Object-Storage.md`](Contracts-Object-Storage.md) | `meta/infra.json`, flags, ledger — Setup must write these after apply. |
| Lab `Infrastructure-Information.md` | Live lab layout (placeholders). Discovery dump is a *snapshot* of that, not a better SoT. |
| [`Lab-Reference-Stack-Notes.md`](Lab-Reference-Stack-Notes.md) | Sanitized 2026-08-12 dump digest: naming table, 3-DG model, copy vs skip, Events→Function (ONS leftover). **Read this before writing `infra/`.** |
| Lab `docs/Agent-Deploy-Pitfalls.md` | SSH/sudo/SFTP rules the Setup bootstrap must obey. |
| [`OCI-API-Usage.md`](OCI-API-Usage.md) | 429 backoff, waiters, Object Storage thrift — apply/bootstrap must follow. |

**Non-goals of this document**

- Manager UI copy for the wizard (Phase 3.2).
- Rewriting door C or idle-agent Python.
- Migrating the live Forge lab off `/home/ubuntu/minecraft/server`.
- Publishing an Oracle Marketplace / Resource Manager public template.
- Multi-admin shared tofu state (later).
- Paid/spend-mode shapes (v1).

---

## 3. Problem statement

The operator’s Always Free stack was built by hand (Console + SSH). The product must let a **non-expert** click through Setup and get the same *kind* of stack: private Vanilla doorbell, reserved play IP, idle/budget brakes, Object Storage SoT.

Three properties are mandatory:

1. **Idempotent.** Re-running Setup after a partial failure, or a later “repair infrastructure” action, must not duplicate VCNs/VMs or destroy a working stack. OpenTofu state + bootstrap-state.json are how that is achieved — not “hope the scripts are careful.”
2. **Updatable.** Networking/IAM/Function fixes must be shippable without forcing every user to download a new installer *and* without requiring the operator to hand-edit live Console resources. That implies versioned HCL, a plan-before-apply UX, and a split between **infra version** (`stack_version` / `infra_schema`) and **app version**.
3. **$0.** No paid Resource Manager extras (RM itself is free, but see §4), no extra boot volumes, no custom-image Object Storage bloat, no GitHub-PAT-gated cloud runner the user has to keep.

The tempting shortcut — “export my tenancy as a Resource Manager stack and have an agent apply that zip for every customer” — fails all three. This document replaces that shortcut with a real plan.

---

## 4. Why Resource Manager is not the product apply engine

[OCI Resource Manager](https://docs.oracle.com/en-us/iaas/Content/ResourceManager/Concepts/resourcemanager.htm) is Oracle’s hosted Terraform runner. Stacks, jobs, and state hosting are **free**; you pay only for the OCI resources a job creates. That is attractive, and it is the right tool for **one** job we will actually do: capturing the live lab as a learning reference (§7, §18).

It is the **wrong** tool to embed as the product’s deploy engine.

### 4.1 It runs HashiCorp Terraform, not OpenTofu

As of the 2026-08-12 docs, Resource Manager supported Terraform **1.5.x** (CLI 1.5.7) as the current line, with **1.2.x / 1.1.x / 1.0.x** deprecated. On **2026-04-30** Oracle stopped allowing *new* stacks and *new jobs* on versions earlier than 1.5.x. See [Supported Terraform Versions](https://docs.oracle.com/en-us/iaas/Content/ResourceManager/Reference/terraformversions.htm).

OpenTofu 1.12.x (current line at research time) is a **fork**. HCL is still largely compatible, but:

- Resource Manager will not run `tofu`.
- State-file format has already started to diverge (OpenTofu added [state encryption](https://opentofu.org/docs/language/state/encryption/) in 1.7; HashiCorp OSS Terraform does not have the same feature).
- A module written and tested under OpenTofu on Windows is the artifact we ship. Asking Resource Manager to apply “the same” zip later is an untested second engine.

### 4.2 HashiCorp BSL vs a shippable installer

HashiCorp moved Terraform to [Business Source License 1.1](https://www.hashicorp.com/bsl) in August 2023. The Additional Use Grant allows production use **unless** you offer Terraform to third parties on a **hosted or embedded** basis in order to compete with HashiCorp’s paid products. “Embedded” includes shipping the binary *or* packaging a product so Terraform must be downloaded for it to operate. Products that are **not sold** are currently called out as non-competitive — but this product may later have paid support, and the legal line is exactly the kind of ambiguity OpenTofu exists to avoid.

OpenTofu is [MPL 2.0](https://github.com/opentofu/opentofu) under the Linux Foundation. Redistributing `tofu.exe` (with source-offer obligations typical of MPL) is the clean path for a Windows installer.

### 4.3 Extra moving parts the novice admin should not need

To use Resource Manager *as the apply engine* every user would need:

| Extra requirement | Why it hurts MVP |
|-------------------|------------------|
| IAM policies for `orm-family` / Resource Manager | Another tenancy-level permission the guide must teach; easy to get wrong |
| A stack object in *their* tenancy | Discovery/connect-existing would have to find it; two sources of truth (stack vs `meta/infra.json`) |
| GitHub **Personal Access Token** if origin is Git | Secrets in OCI; token rotation; private-repo vs public-repo confusion |
| Custom-provider bucket if we ever needed one | Burns Always Free Object Storage |
| Console-oriented `schema.yaml` | Duplicates the Avalonia wizard |
| `provider "oci" { region = … }` only (RM injects identity) | Different provider block than local OpenTofu, which must use `~/.oci/config` |

The Manager already authenticates with the user’s API key via `OciSession`. Running `tofu` locally with `config_file_profile` is the same credential story as day-2 manage. Resource Manager would be a *second* identity path.

### 4.4 What Resource Manager *is* good for (this project)

- **Once**, on the operator tenancy: resource discovery → download generated `.tf` zip (+ optional state) → hand to agents as a **labeled reference**.
- Optional later (not MVP): operator-only break-glass “recreate this compartment in another region” experiments. Still not what the installer runs.

Oracle’s own discovery docs: *“Resource discovery is not a migration tool. When cloning or migrating resources, configurations generated by resource discovery are a starting point. They may require changes.”* ([Resource Discovery](https://docs.oracle.com/en-us/iaas/Content/ResourceManager/Concepts/resource-discovery.htm))

---

## 5. Chosen architecture

```text
Admin PC  (Windows Manager)
  │
  ├─ Setup wizard  (Avalonia)
  │     collect region, SSH key, MC version, EULA, alert email, capacity consent
  │
  ├─ OpenTofu  (bundled tofu.exe + pinned oracle/oci provider)
  │     source: bundled infra/  OR  GitHub Release zip (verified)
  │     state:  %LocalAppData%/McManager/tofu/<stack>/  (gitignored, encrypted)
  │     apply:  compartment, VCN, SL, VM1, door, reserved IP, bucket, IAM, budget+Fn
  │     user_data: cloud-init baseline only
  │
  ├─ SSH bootstrap  (existing SshService + onbox/mcmgr + door_vm + vm_agent)
  │     Vanilla module, door binary, idle agent, RCON, unit, game-manifest
  │
  └─ Write local config + Object Storage meta/infra.json

Friends → reserved play IP → door or VM1  (unchanged doorbell)
```

**Two layers, two idempotency mechanisms:**

| Layer | Tool | Idempotency |
|-------|------|-------------|
| Cloud resources | `tofu plan` / `tofu apply` | State file; re-apply is a no-op when config matches |
| On-box software | SSH scripts | `/var/lib/mcmgr/bootstrap-state.json` + module-level “already verified” checks (blueprint §14) |
| Day-2 | OCI .NET SDK | Not tofu. Whitelist/power/usage must not require re-apply |

**Game-layer boundary (restated from blueprint §13.1, now also an infra rule):** OpenTofu may create the `mcmgr` user/group, empty `/opt/mcmgr/` + `/etc/mcmgr/` + `/var/lib/mcmgr/` tree, baseline packages (`curl`, `jq`, `unzip`, `firewalld` as needed), and Adoptium apt **repo registration**. It must **not** install a Minecraft jar, a Java major chosen in the wizard, a loader, or a pack.

---

## 6. Resource Manager Create-stack options — what each origin is for

This section answers the Console **Create stack** form so the operator and agents do not have to reverse-engineer it. Official overview: [Terraform Configurations for Resource Manager](https://docs.oracle.com/en-us/iaas/Content/ResourceManager/Concepts/terraformconfigresourcemanager.htm).

### 6.1 Origin: “My configuration” (upload zip / folder)

**What it is:** You already have `.tf` files. RM stores a copy and can Plan/Apply them.

**Zip rules (Oracle):** working directory must contain at least one `.tf`; **must not** contain `.terraform/` or `terraform.tfstate`. RM owns state separately.

**Use for this project:** **Not for the live-lab capture** (we do not yet have product HCL). **Not for the shipped product** (users should not open Resource Manager). Could be used later by the *operator* to test a zip of `infra/` in RM as a curiosity — still not the product path, and RM would run Terraform 1.5 not OpenTofu.

**Related fields:** folder vs `.zip`; optional working directory if the HCL is in a subfolder.

### 6.2 Origin: “Template” (Oracle-provided or private)

**What it is:** Marketplace / Oracle sample stacks (VCNs, OKE, …) or a private template the tenancy saved.

**Use for this project:** **None.** There is no Oracle template that is a dual-VM Minecraft doorbell. Do not start from a generic “compute + VCN” template and try to grow it into the product — we would inherit names, shapes, and public-ingress habits we do not want.

### 6.3 Origin: “Source code control system” (GitHub, GitLab, Bitbucket, DevOps)

**What it is:** A [configuration source provider](https://docs.oracle.com/en-us/iaas/Content/ResourceManager/Tasks/create-stack-git.htm) stores a PAT/app password. Each job clones the **latest commit** of a branch (or a specified commit, depending on job options).

**Use for this project:** **Not the product runtime** (PAT + RM IAM + HashiCorp Terraform). **Not the operator reference capture** (the live lab is not already HCL in git).

GitHub *is* how we **host** the product module for the Manager to download (§13). That download is a GitHub Releases / zipball fetch from the desktop app, not an RM Git source provider.

### 6.4 Origin: “Existing compartment” (resource discovery) — **the one we use for reference**

**What it is:** RM lists supported resources in one compartment + region and **generates** Terraform configuration (and a state file representing those resources). Docs: [Creating a Stack from an Existing Compartment](https://docs.oracle.com/en-us/iaas/Content/ResourceManager/Tasks/create-stack-compartment.htm), [Resource Discovery](https://docs.oracle.com/en-us/iaas/Content/ResourceManager/Concepts/resource-discovery.htm).

**Critical Oracle constraints:**

- **Not a migration tool.** Starting point only.
- **Single compartment.** Nested child compartments are **not** included.
- **Scope follows which compartment you pick:**
  - **Root / tenancy** → tenancy-scoped resources (users, groups, dynamic groups, many policies).
  - **Non-root** (e.g. Default, or a future `mcmgr`) → compartment-scoped resources (instances, VCNs, buckets, Functions, …).
- Only **active/usable** resources; terminated instances are omitted.
- Some attributes are **not discoverable** (especially secrets). RM inserts placeholders and often adds `lifecycle { ignore_changes = … }` so Plan does not immediately fail.
- Resource Terraform names default to the **display name**.
- **Service filter cannot be changed later** on that stack — choose filters carefully at create time.

**Supported types that matter to us** (non-exhaustive; full list is on the discovery page): `oci_core_instance`, VCN/subnet/IGW/route table/security list/public IP/private IP, `oci_objectstorage_bucket` (and **objects**), `oci_functions_application` / `oci_functions_function`, `oci_events_rule`, `oci_budget_budget` / `oci_budget_alert_rule`, `oci_identity_*` (including **api_key, auth_token, customer_secret_key** — dangerous), `oci_core_instance_configuration` / instance pools (we will not use these in product IaC).

### 6.5 “Use custom terraform providers”

**What it is:** Point the stack at an Object Storage bucket whose keys look like `linux_amd64/terraform-provider-<TYPE>_v<MAJOR.MINOR.PATCH>` (and/or `linux_arm64/…`). Docs: [Using Custom Providers with a Stack](https://docs.oracle.com/en-us/iaas/Content/ResourceManager/Tasks/update-stack-custom-providers.htm).

**Use for this project:** **Leave unchecked / off.** We only need `oracle/oci` (and possibly `hashicorp/cloudinit` or built-in `cloudinit` / `archive` providers from the public registry). A custom-provider bucket would:

- Consume Always Free Object Storage.
- Need both amd64 and arm64 binaries if RM’s runner architecture ever matters.
- Solve a problem we do not have.

If a stack is old enough to predate Terraform Registry sourcing, Oracle says to update it to Registry **before** custom providers work. New stacks already fetch from the Registry. Irrelevant if we never apply this stack.

### 6.6 Name, description, compartment, Terraform version, tags

| Field | Meaning | Our reference-capture choice |
|-------|---------|------------------------------|
| **Name** | Stack display name. Not confidential per Oracle, but still do not put OCIDs/IPs. | `mcmgr-lab-discovery-reference` (compartment capture) and optionally `mcmgr-lab-discovery-iam-root` if a second, filtered identity capture is ever done — see §18 for why we prefer **not** to. |
| **Description** | Free text. | Explicitly: “REFERENCE ONLY. Do not Apply. Generated for product IaC authors.” |
| **Create in compartment** | Where the *stack object* lives (not necessarily the compartment being scanned). | Same compartment as the lab compute (Default today) so it is easy to find. Stacks are free. |
| **Terraform version** | RM runner version. | **1.5.x** — the only non-deprecated line; required for new stacks/jobs after 2026-04-30. Do not pick 1.2/1.1/1.0. |
| **Tags** | Optional freeform/defined. | Optional: `mcmgr-role=discovery-reference` so it is obvious in lists. Do **not** put `mcmgr-domain=mc-server-compartment` on the *stack* — that tag marks the **compute compartment** for Connect-existing. |
| **Run apply on create** | Some origins offer this. | **Never** for discovery. Discovery’s “job” is configuration *generation*, not Apply. After the stack exists, do not click Apply. |

### 6.7 Apply jobs (so nobody clicks them by accident)

[Creating an Apply Job](https://docs.oracle.com/en-us/iaas/Content/ResourceManager/Tasks/create-job-apply.htm): Apply provisions or **modifies** resources from the configuration. Plan-resolution can be “latest plan” or **Automatically approve**.

For a discovery stack, Apply would try to *manage* the already-existing lab resources using generated HCL that is incomplete (placeholders, ignore_changes, missing IAM). That can no-op, drift, or **destroy/recreate**. **Do not Apply. Do not Destroy.** Download the config (and optionally state) and leave the stack idle, or delete the *stack object* later (deleting a stack does not delete the discovered compute — confirm the Console warning; if unsure, leave the stack).

---

## 7. Recommended Resource Manager settings for a *reference* capture

This is the short form of §18. Use **Existing compartment**.

### 7.1 Primary stack — compartment-scoped lab resources

| Setting | Value |
|---------|--------|
| Origin | **Existing compartment** |
| Compartment to scan | The compartment that actually holds VM1, door, VCN, bucket, Functions (today: **Default**, tagged `mcmgr-domain=mc-server-compartment`) |
| Region | Lab home region (typically `us-sanjose-1` — confirm private file; do not type it into tracked docs) |
| Services to discover | **Selected**, not all. Include: `core`, `objectstorage`, `functions`, `events`, `budget`, `artifacts` (OCIR). **Exclude `identity`.** |
| Custom providers | **Off** |
| Name | `mcmgr-lab-discovery-reference` |
| Description | `REFERENCE ONLY — do not Plan/Apply/Destroy against live lab. For OCI-mc-server infra authors.` |
| Stack compartment | Same as scanned compartment |
| Terraform version | **1.5.x** |
| Tags | optional `mcmgr-role=discovery-reference` |
| After create | Wait for the generate job to **Succeed**. Download **Terraform configuration**. Optionally download **state**. **Do not Apply.** |

**Why exclude identity here:** Identity resources (dynamic groups, tenancy policies, users, **API keys, auth tokens**) are **tenancy-scoped**. A Default-compartment discovery will **not** reliably capture the door/VM1/Functions dynamic groups. Including identity on a *root* scan is worse — see §7.2.

**Why filter services at all:** An unfiltered Default-compartment scan can still pick up unrelated leftovers (old instance configurations, extra buckets, console history, boot volume backups). Filters keep the zip small and on-topic. The filter **cannot be edited later**.

**Object Storage warning:** Discovery supports `oci_objectstorage_object`. A naive capture can try to represent **every object** in the bucket, including multi-gigabyte `backups/world-*.zip`. That makes a huge, useless, possibly secret-adjacent zip. After download, **delete any generated `oci_objectstorage_object` resources** for backup zips / ledger JSON before zipping a copy for agents. Keep the **bucket** resource only.

### 7.2 Do **not** discovery-scan the tenancy root for identity

Root-compartment discovery would include `oci_identity_user`, `oci_identity_api_key`, `oci_identity_auth_token`, `oci_identity_customer_secret_key`, `oci_identity_smtp_credential`, `oci_identity_ui_password`, and **all** policies/groups in the tenancy — far beyond this Minecraft stack, and explicitly the kind of material we must not put in git or paste into chat.

**Instead, capture IAM by hand (still for agents, still gitignored):**

1. Console → Identity → **Dynamic groups** used by VM1, door, both-VMs Object Storage, Functions — copy **name + matching rule** (OCIDs ok in the gitignored pack).
2. Console → Identity → **Policies** that mention those groups — copy **statement text**.
3. Or: copy the already-redacted conceptual statements from lab `Infrastructure-Information.md` plus the exact statements from gitignored `data/Infrastructure-Deployment-Private.md`.

Product IaC will **rewrite** matching rules to `instance.compartment.id = '<mcmgr compartment>'` (or equivalent tag match) rather than pinning today’s instance OCIDs.

### 7.3 What *not* to create

- No second stack with origin **My configuration** until product `infra/` exists — and even then, do not make RM the SoT.
- No GitHub configuration source provider for this capture.
- No Oracle template.
- No “Run apply.”
- No “Upgrade provider versions” / parallel-operations experiments on this stack.

---

## 8. How agents must treat a discovery dump

When the operator provides a zip (see §18.6 for the exact pack):

1. **Read it as a field-level encyclopedia**, not as a module to check in.
2. **Extract:** resource *types*, relationships (VNIC → secondary private IP → reserved public IP), Security List rule *shapes* (ports, ICMP, preserve-non-owned-rules lesson), instance shapes/OCPU/memory, subnet CIDR, IGW/route patterns, Function/Events/Budget wiring, OCIR repo names.
3. **Discard:** display names (`minecraft-vm3`, …), hardcoded OCIDs, public IPs, tenancy OCID, user OCID, `ignore_changes` placeholders, any `oci_objectstorage_object` bodies, anything that looks like a key/token.
4. **Rewrite** into product HCL under `infra/` with names from PRODUCT-IDEAS (`mcmgr-vcn`, `mcmgr-vm1`, …).
5. **Never** `tofu import` the **live Forge lab** into product state as part of Phase 3. Importing a single resource you created **out of band in the same greenfield/test stack** into that stack’s `%LOCALAPPDATA%\McManager\tofu\<stack-id>\` state is OK (see `infra/README.md` IAM import). Do not `cd` into LocalAppData — that folder has no `.tf` files.
6. **Never** commit the dump. Place it only under a gitignored path the operator chooses (suggested: lab `data/reference-stack/` or product `data/reference-stack/`).
7. **Identity can leak even when unchecked.** The 2026-08-12 lab capture still emitted Identity Domains (users, API public-key PEM, auth tokens). Delete that file; do not copy it.

The lab pack was sanitized in place. Phase 3 agents should start from [`Lab-Reference-Stack-Notes.md`](Lab-Reference-Stack-Notes.md), not from any `ocid1.ormstack…` folder.

If discovery HCL and lab `Infrastructure-Information.md` disagree, **believe the live Console / private file**, then update the public infra doc. Discovery can omit attributes.

---

## 9. VM provisioning strategy

### 9.1 Launch from Canonical Ubuntu 22.04 platform images

| VM | Shape (MVP) | Image |
|----|-------------|--------|
| **VM1** `mcmgr-vm1` | `VM.Standard.A1.Flex`, **4 OCPU / 24 GB** (Always Free–comfortable product target). **TEMPORARY (Step 3.3 blank-tenancy test):** OpenTofu defaults are **2 / 12** — revert [`infra/variables.tf`](../infra/variables.tf) after the test. Setup should later let the admin pick 2/12 vs 4/24 (PRODUCT-IDEAS). | Canonical Ubuntu **22.04 aarch64** (not Minimal unless we later prove Minimal has every package we need) |
| **Door** `mcmgr-door` | `VM.Standard.E2.1.Micro` (~1/8 OCPU, Always Free AMD Micro) | Canonical Ubuntu **22.04 x86_64** |

Do **not** hard-code image OCIDs in git (they are regional and rotate monthly). Use `data "oci_core_images"` filtered by `operating_system = "Canonical Ubuntu"`, `operating_system_version = "22.04"`, and `display_name` regex for `aarch64` vs non-aarch64. Take the latest image in the list (Oracle returns newest first in the usual pattern used by [Ampere’s example](https://github.com/AmpereComputing/terraform-oci-ampere-a1/blob/main/ubuntu2204.tf)). Pin the **resolved** image OCID into `meta/infra.json` after apply for support, not into the module as a constant.

Stay on **22.04 for MVP** even though 24.04 images exist (Oracle image catalog listed Canonical Ubuntu 22.04 and 24.04 builds as of 2026-07). The lab, door C build, and idle agent are proven on 22.04. Jumping LTS is a later, tested change.

SSH user remains **`ubuntu`**. Inject the Setup-generated (or imported) public key via instance `metadata.ssh_authorized_keys`.

Boot volume: **default 50 GB** each. Two instances = 100 GB of the **200 GB** Always Free block-volume envelope ([Always Free resources](https://docs.oracle.com/en-us/iaas/Content/FreeTier/freetier_topic-Always_Free_Resources.htm)). Do not attach extra block volumes. Do not grow boot volumes “just in case.”

### 9.2 cloud-init / `user_data` — baseline only

OCI instances accept cloud-init via `metadata.user_data` (**base64**). Combined `metadata` + `extended_metadata` size limit: **32,000 bytes** ([oci_core_instance](https://docs.oracle.com/en-us/iaas/tools/terraform-provider-oci/latest/docs/r/core_instance.html)). Keep the payload small: `#cloud-config` YAML, not a tarball of `door_vm/`.

**Allowed in user_data (both VMs, parameterized):**

- Create system group/user `mcmgr` (VM1) — door may skip `mcmgr` if it has no game tree.
- `mkdir` product dirs with documented ownership (`/opt/mcmgr`, `/etc/mcmgr`, `/var/lib/mcmgr` on VM1).
- `apt-get` baseline: `jq`, `unzip`, `curl`, `ca-certificates`, `firewalld` (VM1) as required.
- Register Adoptium apt **repository** (not a specific `temurin-21` package — Java major is wizard-time).
- Optional: disable password SSH; ensure `ubuntu` sudo; set hostname `mcmgr-vm1` / `mcmgr-door`.
- Write a tiny `/etc/mcmgr/cloud-init-done` marker so SSH bootstrap can wait.

**Forbidden in user_data:**

- `curl` of Mojang piston-meta / Paper Fill / Forge installers.
- Embedding `door_vm` binaries or `vm_agent` wheels (size + update problem).
- Writing RCON passwords or API keys.
- Opening `0.0.0.0/0` in firewalld.

Prefer `#cloud-config` `packages:` / `runcmd:` / `users:` over a giant `#!/bin/bash` blob. If multiple files are needed, use the `cloudinit` provider’s `cloudinit_config` data source (gzip+base64) rather than concatenating scripts into one metadata key by hand.

**Do not use Terraform/OpenTofu `remote-exec` provisioners** for the game or door. They are notoriously brittle (SSH race, Windows runner, no good resume). The Manager already has `SshService`; Phase 3.3 waits until the instance is RUNNING (OCI waiter, ≤30 s backoff, ~20 min) **and** cloud-init finished, then runs bootstrap over SSH.

### 9.3 Custom images — do not use for product distribution

[Creating a custom image](https://docs.oracle.com/en-us/iaas/Content/Compute/Tasks/custom-images-create.htm) snapshots a boot volume (instance should be stopped). That is useful for *operator* golden-image experiments; it is a poor product vehicle:

| Issue | Detail |
|-------|--------|
| **Two architectures** | A1 vs Micro cannot share one image. |
| **Stale goldens** | Every door/`vm_agent`/Ubuntu patch needs a new image bake + test matrix. |
| **Always Free storage** | Custom images count against **custom-image service limits**; import-from-Object-Storage uses the **10 GB Standard** envelope if you stage qcow2 there. Forum reports of `custom-image-count` quota **0** on some free tenancies. |
| **Secrets** | Easy to bake RCON, `~/.oci`, or authorized_keys into an image and then clone them to strangers. |
| **First-boot uniqueness** | Host keys, machine-id, and instance principals must be regenerated; easy to get wrong. |
| **Does not replace IAM/VCN** | An image is not a stack. |

**Product rule:** never require users to import our custom image. Never bake Minecraft into an image.

### 9.4 Instance configurations — do not use

[Instance configurations](https://docs.oracle.com/en-us/iaas/Content/Compute/Tasks/creatinginstanceconfig.htm) are launch *templates* for **instance pools / autoscaling**. Oracle documents that creating a configuration *from an instance* does **not** copy boot-volume contents (installed software). To include software you must custom-image first, then wrap that image in a configuration.

We have **two unique VMs**, not a homogeneous pool, and we do not want autoscaling (that would burn Always Free hours and possibly paid capacity). OpenTofu `oci_core_instance` resources *are* the template.

### 9.5 Object Storage as a script depot — not primary

It is technically possible to `PutObject` bootstrap tarballs and have cloud-init `curl` them with instance principals. Problems:

1. **Chicken-and-egg:** the bucket is created in the same apply as the VMs. user_data cannot assume the bucket exists unless apply is split into two stages (extra wizard complexity).
2. **50k Object Storage requests/month** Always Free cap — do not add boot-time Gets on every VM start (VM1 starts often).
3. **10 GB Standard** envelope is for ledger + world backups, not copies of `door_vm`.
4. We already have a **free** file host: GitHub (this repo + Releases).

Optional **later** (not MVP): after the bucket exists, Setup may upload a copy of bootstrap scripts for *on-box repair when GitHub is unreachable from the VM* — still secondary to Manager-driven SFTP.

---

## 10. Where bootstrap files live (not Object Storage as primary)

| Tree | Repo | How it reaches the VM |
|------|------|------------------------|
| OpenTofu HCL | product `infra/` | Runs on admin PC only |
| Vanilla/common bootstrap | product `onbox/mcmgr/` | Manager SFTP + `sudo bash` (Phase 3.3) |
| Door C + scripts | lab `door_vm/` until productized copy | Same SFTP pattern as lab `door_deploy.py` (obey Agent-Deploy-Pitfalls) |
| Idle agent | lab `vm_agent/` | Same |

CRLF stripping, `/tmp` as `ubuntu`, `sudo bash -c 'a && b'`, stop `mccontrol` before replacing the binary, `HOME` for systemd oneshots — all still apply.

---

## 11. OpenTofu module design

### 11.1 Layout (Step 3.1)

Suggested root (exact folder name may be `infra/` or `tofu/`; pick one in 3.1 and stick to it):

```text
infra/
  versions.tf          # tofu >= 1.6, required_providers oracle/oci
  providers.tf         # config_file_profile from ~/.oci
  variables.tf
  outputs.tf
  main.tf              # thin root — calls modules
  modules/
    compartment/
    network/           # VCN, subnet, IGW, route, SL
    compute/           # VM1 + door + secondaries + reserved IP
    storage/           # bucket mcmgr-shared-data
    iam/               # dynamic groups (tenancy) + policies
    budget_brake/      # $1 budget, Events, Function, OCIR (may stub in 3.1)
  cloud-init/
    vm1.yaml.tftpl
    door.yaml.tftpl
```

**Provider block (local OpenTofu, not RM):**

```hcl
terraform {
  required_version = ">= 1.6.0"
  required_providers {
    oci = {
      source  = "oracle/oci"
      version = "~> 8.0"  # pin exact in .terraform.lock.hcl; 8.23.0 was current 2026-07-14
    }
  }
}

provider "oci" {
  config_file_profile = var.oci_profile
  region              = var.region
}
```

**Never** use implied `hashicorp/oci` — OpenTofu Registry does not serve that name; the source is **`oracle/oci`**. Every module that needs the provider must repeat `required_providers` or inherit carefully (see common OpenTofu failure: child modules still requesting `hashicorp/oci`).

Commit the **dependency lock file** (`.terraform.lock.hcl`) so Setup does not float providers. Run `tofu init` during development on Windows; the lock file is cross-platform for provider *versions* (binaries are fetched per OS).

### 11.2 Variables (wizard → tofu)

| Variable | Source |
|----------|--------|
| `tenancy_ocid` | `~/.oci` / user |
| `region` | wizard (default lab region) |
| `oci_profile` | wizard / local config |
| `ssh_public_key` | wizard generate or import |
| `admin_cidr` | detected public IP `/32` |
| `alert_email` | wizard |
| `compartment_name` | default `mcmgr` |
| `vcn_cidr` | default `10.0.0.0/16` (or documented equivalent) |
| `vm1_ocpus` / `vm1_memory_gb` | Product MVP **4 / 24**. **TEMPORARY test defaults 2 / 12** in `infra/variables.tf` (revert after Step 3.3). Setup does not yet collect this — see PRODUCT-IDEAS Setup VM1 shape choice. |
| `bucket_name` | `mcmgr-shared-data` (must be unique per namespace — if taken, suffix; record actual name in meta) |

No Minecraft version variable in tofu.

### 11.3 Resources the module must create (MVP)

Aligned with PRODUCT-IDEAS naming and lab `Infrastructure-Information.md` behavior:

- Compartment `mcmgr` + freeform tag `mcmgr-domain=mc-server-compartment`
- VCN, public subnet, Internet Gateway, route `0.0.0.0/0` → IGW. **No** NAT gateway, private subnet, service gateway, or IPv6 (lab VCN-wizard leftovers — see [`Lab-Reference-Stack-Notes.md`](Lab-Reference-Stack-Notes.md))
- Security List `mcmgr-sl`: ICMP as needed; **no** `0.0.0.0/0` for 25565/22/8080; admin `/32` for SSH 22 and door 8080; Minecraft 25565 TCP+UDP only for friend `/32`s (Setup may start with admin only)
- VM1 A1 Flex + door Micro; **ephemeral public IP on primary**; **secondary private IP** on each VNIC
- Reserved public IP `mcmgr-play-ip` attached to **door secondary** initially (idle)
- Private Standard bucket `mcmgr-shared-data` (no versioning, no emit events, Oracle-managed keys)
- Dynamic groups (tenancy OCID as `compartment_id` — Oracle requires this): `mcmgr-dg-instances` (compartment), `mcmgr-dg-door` (**door instance OCID** — hyphenated `mcmgr-role` tag matching did not enroll the door on the identity-domain 3.3 test), `mcmgr-dg-fn` (fnfunc in compartment)
- Policies **scoped to the product compartment / bucket** where the API allows (lab today is tenancy-wide `manage`; product should tighten)
- $1 monthly budget on the compartment + alert (email) + **Events rule → Function** (`mcmgr-events-budget-alert`; RM dump omitted the action — Console has Functions → `shutdown_vm`). Do **not** create the lab’s unused ONS topic `Budget-Alerts`. Phase 3.3 may complete Function image push; 3.1 may placeholder. Behavior is staged in lab `PRODUCT-IDEAS.md` ([$1 spend-brake lock (v1)](../../OCI-mc-server-manager/PRODUCT-IDEAS.md#1-spend-brake-lock-v1)); IaC must not freeze the wrong split:
  - **MVP Function code:** SoftStop to halt spend. Live lab (`functions/shutdown_vm/`) SoftStops **VM1 and VM2**. Keep the stop list a **variable** (default both, matching the lab) until PRODUCT-IDEAS settles whether the Always Free Micro can stay up; do not hard-code “VM1 only” as if that were already the product rule.
  - **v1 Function code (not MVP):** also **PUT** a durable Object Storage lock flag (exact key TBD at v1 contract freeze; sketch `meta/spend-brake-triggered.json`). Manager is the only clearer; door reads it before wake. **Do not create or delete that object in OpenTofu** — it is runtime state, like ledger JSON.
  - **IAM now (MVP tofu):** grant the Functions dynamic group SoftStop on the product compartment **and** object write **scoped to the product bucket**. The extra statement is unused until v1 Function code lands; putting it in greenfield avoids a later IAM-only apply. Do **not** grant tenancy-wide `manage objects`.

**Outputs:** every OCID/IP the Manager needs for `config.local.json` and `meta/infra.json` (see Contracts-Object-Storage nested v2). No secrets in outputs.

### 11.4 IAM special case

`oci_identity_dynamic_group.compartment_id` **must be the tenancy OCID**. Matching rule example:

```text
ALL {instance.compartment.id = '<mcmgr compartment ocid>'}
```

`mcmgr-dg-door` matches **`instance.id = <door OCID>`** (tofu passes `module.compute.door_instance_id`). Compartment + `freeform-tag.mcmgr-role = 'door'` did not enroll the door on the identity-domain test tenancy (hyphen in the tag key is a likely parser issue). Lab SoT also pins the door by instance OCID.

Door needs `use instance-family` in the product compartment, plus **`manage public-ips`, `use private-ips`, and `use virtual-network-family` in tenancy** (`mcmgr-door-ip` at the root). Compartment-only public-ip statements return `NotAuthorizedOrNotFound` on `UpdatePublicIp` (verified on the blank-tenancy 3.3 test). Lab SoT matches this tenancy set.

If `mcmgr-door-ip` already exists (created in Console/CLI during a test) and is **not** in Setup’s LocalAppData state, the next apply will try to create a duplicate name. Import it with config = repo `infra/`, `-state`/`-var-file` = LocalAppData, quoted PowerShell paths — see [`infra/README.md`](../infra/README.md) (Importing `mcmgr-door-ip`). Do not `cd` to `%LOCALAPPDATA%\McManager\tofu\…`.

Functions dynamic group `mcmgr-dg-fn` (resource principal, not the VM instance groups): match `fnfunc` **in the product compartment**, not all functions in the tenancy. Grant `use instance-family` in `mcmgr` (SoftStop), plus **object write on `mcmgr-shared-data` only** (v1 spend-brake lock PUT; see §11.3). Do not reuse the instances Object Storage group for the Function — different principal type.

The Setup user’s API key must be allowed to create compartments, policies, and dynamic groups (typically Administrators or a dedicated “McManager setup” policy). The happy-path guide (Phase 6) must say this; the module should fail with a readable IAM error if not.

### 11.5 Capacity

A1 Flex is frequently out of host capacity. Setup **probes** `CreateComputeCapacityReport` for `VM.Standard.A1.Flex` (same OCPU/memory as the module defaults) in the first availability domain **before** `tofu apply`. That call does not need a VCN or instance. If the report is `OUT_OF_HOST_CAPACITY`, apply is skipped so a retry does not keep expanding a partial stack.

The report is a **snapshot, not a reservation** — apply can still lose a race. The wizard still treats apply-time `Out of host capacity` as the same wait path (Retry / auto-retry every 5 min; no 1 s loop). Auto-retry re-runs the probe first. Do **not** silently retry in a 1 s loop (OCI-API-Usage).

`CreateBudget.compartmentId` must be the **tenancy** OCID; `targets` is the stack compartment. A child-compartment `compartment_id` on the budget resource returns `400 Invalid compartmentId`.

---

## 12. Idempotency and future updates

### 12.1 Greenfield apply

- First successful apply writes state on the admin PC and `meta/infra.json` in the bucket.
- Re-running apply with the same module version and variables: **no replacements** of VMs (watch `user_data` — if cloud-init is inlined and changes, OCI may force a new instance; put stable cloud-init in files and only change it when we *intend* a recreate, or `ignore_changes` on `metadata` after first boot).
- **Prefer `ignore_changes` on `metadata` / `user_data` after initial create** so later module tweaks do not destroy VM1 (world lives on that boot volume). Document that OS-baseline fixes for *new* deploys do not retrofit old VMs — those use SSH repair.

### 12.2 What tofu should not manage day-to-day

| Concern | Owner |
|---------|--------|
| Security List friend `/32`s | Manager whitelist sync (already shipped) |
| Start/stop / reserved IP move | Door + Manager |
| Ledger / budget JSON | VM1 / Manager |
| **v1 `$1` spend-brake lock object** (e.g. `meta/spend-brake-triggered.json`) | Function **sets**; Manager **clears** after typed confirmation; door **reads**. Not a tofu resource. |
| Minecraft version upgrades | SSH bootstrap / future upgrade flow |
| Idle timeout | Danger Zone |

If tofu also owned Security List ingress, every whitelist edit would fight state. **Pattern:** tofu creates the SL with *structural* rules (ICMP, maybe empty placeholder comments); Manager owns `/32` Minecraft/SSH/door rules. Use `lifecycle { ignore_changes = [ingress_security_rules] }` on the SL **or** split: tofu attaches a second SL we never touch. Prefer **ignore_changes on ingress** plus a documented baseline egress, matching today’s “preserve non-owned rules” lesson. Decide the exact split in Step 3.1 and write it in `infra/README.md`.

### 12.3 Infra upgrades (after MVP)

1. Bump module `stack_version`.
2. If `meta/infra.json` shape changes, bump `infra_schema`.
3. Manager “Repair / update infrastructure” (v1-ish; MVP Setup may only do first deploy): fetch newer HCL (§13), `tofu plan`, show the user, apply.
4. Connect-existing MVP-light may soft-warn on schema mismatch; v1 enforces.

App updates (GitHub Releases of the Manager) are independent. A newer app must still drive older infra until `infra_schema` says otherwise.

### 12.4 Destroy

**MVP:** no polished Destroy UI. Bucket modules may use `prevent_destroy` so a stray `tofu destroy` cannot wipe world backups.

**v1:** Manager **Danger Zone** exposes **delete all cloud infrastructure** (lab `PRODUCT-IDEAS.md`). `tofu destroy` (or the equivalent product-owned teardown of everything OpenTofu manages) is the only safe bulk delete. UX:

- Warning popup: VMs, network, reserved play IP, Object Storage **including backups**, and other product resources in the stack compartment go away.  
- This **does not** delete the Oracle **tenancy**. Copy must say the user has to log in to the **OCI Console in a browser** to delete the tenancy/account if they want that.  
- User types **`confirm`** in a text box before the Delete button enables.  
- Lift `prevent_destroy` on the bucket only as part of that confirmed path (or destroy the bucket explicitly after the typed confirmation) — never on a one-click control.

Worlds that exist only in Object Storage are deleted with the bucket; local Manager copies and any previously downloaded zips on the admin PC are unaffected.

---

## 13. Config distribution: installer vs GitHub

**Question:** pre-package HCL in the installer, or pull at Setup time so infra can update without a new installer?

**Answer: hybrid.**

| Channel | Role |
|---------|------|
| **Bundled `infra/`** inside the app/installer | Always present; checksummed; used offline; defines the minimum `infra_schema` that app version understands |
| **GitHub Release asset** (e.g. `mcmgr-infra-0.2.0.zip`) or repo zipball of tag `infra-v0.2.0` | Optional newer module; Setup checks on wizard start (not every Manager launch — no silent probe) |
| **User Object Storage / RM Git source** | Not used as the module source |

GitHub is **free** for public repos. This product repo is already public (`maattox/oci-mc-server`). Unauthenticated GitHub API is rate-limited (~60 req/hr/IP) — one check per Setup session is fine.

**Compatibility gate:** a pulled zip must declare `infra_schema` / `min_app_version` (file `infra/manifest.json` in the zip). The app refuses a zip that is newer than it understands, and refuses a zip older than a breaking schema it already wrote to a tenancy.

**Supply chain:** pin SHA-256 of Release assets in the app *or* verify GitHub artifact attestations if we add them later. Do not `tofu apply` a zip the user pasted from a random URL in MVP (too easy to get wrong); Advanced “load from folder” can wait.

**OpenTofu binary:** ship a **pinned** `tofu.exe` (Windows amd64) from [OpenTofu Releases](https://github.com/opentofu/opentofu/releases) (e.g. 1.12.x line at research time), or download it once into `%LocalAppData%` with checksum. MPL 2.0 redistribution: keep license text and a pointer to source. Do **not** download HashiCorp `terraform.exe`.

**Providers:** `tofu init` on first Setup (needs network) caches `oracle/oci` into the local data dir. Offline Setup: vendor providers in the installer if we want a fully offline apply — **nice-to-have**, not MVP-blocking if the guide says “internet required for Setup.”

---

## 14. State, secrets, and the admin PC

| Item | Location |
|------|----------|
| OpenTofu state | `%LocalAppData%\McManager\tofu\<compartment-or-stack-id>\terraform.tfstate` (path TBD in 3.1). Gitignored. **Not** in the shared bucket. |
| State encryption | OpenTofu `encryption` block with PBKDF2 passphrase from Windows Credential Manager (or skip encryption in first 3.1 skeleton, but **do not** commit plaintext state). See [State and Plan Encryption](https://opentofu.org/docs/language/state/encryption/). Losing the passphrase = losing the ability to manage that stack with tofu (Connect-existing via meta still works for *day-2*). |
| SSH private key | Admin PC only; fingerprint may go in `meta/infra.json` |
| API key | `~/.oci` |
| Auth Token (OCIR) | Windows Credential Manager when Function push needs it |
| RCON | `/etc/mcmgr/rcon.secret` on VM1; local config copy; **never** meta, **never** tofu state if we can avoid it (state will still see instance metadata — do not put RCON in user_data) |

**No Terraform Cloud / HCP / OCI RM remote backend** for MVP.

**Do not use the product bucket as a `backend "http"` / S3-compatible state store in MVP.** It competes with backup quota and request caps, and the bucket does not exist before apply.

---

## 15. Setup wizard orchestration

Order of operations (Phase 3.3; 3.1 only needs the module to be plan-able):

1. Wizard collects inputs; persist resume state (already planned).
2. Resolve infra zip (bundled vs GitHub).
3. `tofu init` → `tofu plan` → show summary → user confirms.
4. `tofu apply` with waiter-style handling of 429s (provider retry + our own).
5. Capacity failure → do not abandon variables; retry/poll/resume.
6. Wait instances RUNNING; wait cloud-init marker over SSH. **Do not** `apt upgrade` / `do-release-upgrade` on the guests (22.04 is the baseline; cloud-init already sets `package_upgrade: false`).
7. SSH: door deploy (`install.sh` must write Object Storage namespace/bucket into `oci.env` when OS wake is on), VM1 `onbox/mcmgr` Vanilla driver, idle agent, §10.2 config sync.
8. Guest repair (same SSH session or Re-Deploy at `apply_stage=vm1` without re-apply): `/etc/netplan/99-mcmgr-play.yaml` for the **secondary** play IP on both VMs (reserved public IP targets that address, not the ephemeral primary); seed Vanilla `whitelist.json` from the wizard **admin Minecraft username**.
9. Seed Object Storage layout if tofu did not (empty prefixes, initial budget JSON). Treat **missing** `meta/infra.json` / `ledger/usage.json` as create (greenfield GET 404 is not a fatal publish error). Log seed failures into the Setup deploy log.
10. Publish `meta/infra.json` from tofu outputs + on-box game manifest summary.
11. Write `data/config.local.json`. Players connect on the **reserved play IP**, not the SSH ephemeral.

Resumability: wizard state **and** tofu state **and** `bootstrap-state.json` are three different checkpoints. Document which step was last completed.

---

## 16. Always Free constraints encoded in IaC

| Constraint | How IaC honors it |
|------------|-------------------|
| A1 Flex free envelope (~1500 OCPU-h / ~9000 GB-h — confirm current docs) | Product default **4/24**; idle agent + $1 budget still deployed. **TEMPORARY test default 2/12** in `variables.tf` — revert after Step 3.3. |
| AMD Micro always-on door | E2.1.Micro only; no second Micro |
| 200 GB block volume | Two × 50 GB boots; no extra volumes |
| 10 GB Object Storage Standard | One bucket; no custom-provider bucket; no image staging bucket |
| ~50k Object Storage requests/month | No tofu refresh loops; no cloud-init polling of the bucket |
| No paid LB / NAT / extra reserved IPs | Module does not declare them |
| Security List private-only | No `0.0.0.0/0` on 22/25565/8080 |
| Home region | Wizard should prefer home region for Always Free eligibility |

If a resource would create spend, the module must not include it without a variable that defaults **off** and a wizard warning (v1 paid mode). MVP has no such variable.

---

## 17. Rejected alternatives

| Idea | Disposition |
|------|-------------|
| Ship the discovery zip as the product module | **Rejected** — ad-hoc names, secrets/placeholders, not idempotent for *new* tenancies, RM/Terraform 1.5 not OpenTofu |
| Resource Manager GitHub origin as Setup engine | **Rejected** — PAT, RM IAM, HashiCorp runner, novice Console steps |
| HashiCorp Terraform CLI in the installer | **Rejected** — BSL embedding risk; PRODUCT-IDEAS says OpenTofu |
| Custom images of lab VM1/VM2 | **Rejected** — arch split, staleness, quota, secrets, Always Free storage |
| Instance configuration + pool | **Rejected** — wrong abstraction; pools/autoscaling fight $0 |
| cloud-init installs Minecraft | **Rejected** — blueprint §13; 32 KB; not resumable |
| Object Storage as primary script host | **Rejected** for MVP — chicken-and-egg, request/quota |
| `remote-exec` provisioners | **Rejected** — brittle; Manager SSH is the orchestrator |
| Import live lab into tofu state | **Rejected** — risk to working Forge stack; Connect-existing does not need it |
| Oracle Marketplace template as UX | **Rejected** for MVP — we have an Avalonia wizard; Marketplace is a later distribution channel if ever |
| `schema.yaml` for RM Console | **Not needed** unless we publish an RM stack |
| Unfiltered tenancy identity discovery | **Rejected** — dumps users/keys/tokens |
| Ubuntu 24.04 by default | **Deferred** — lab is 22.04 |
| Minimal Ubuntu image | **Not default** — prove package set first |

---

## 18. Operator guide — what to capture from the OCI Console for AI agents

This section is for **you** (the operator). Agents should not click the Console for you. The goal is a **gitignored reference pack** that teaches resource *shape*, not a zip we apply.

### 18.1 What we already have (you may not need a huge dump)

Agents in this workspace can already read:

- Lab `Infrastructure-Information.md` (architecture)
- Gitignored `data/Infrastructure-Deployment-Private.md` (live OCIDs, policy text)
- Product `docs/Contracts-Object-Storage.md`
- This file

A discovery zip is still useful because it shows **exact Terraform resource arguments** (VNIC details, source_details, IP assignment, Function config) that prose docs skip. It is **not** a substitute for the private markdown.

### 18.2 Create the reference stack (Console)

1. OCI Console → **Developer Services** → **Resource Manager** → **Stacks**.
2. **Create stack**.
3. **Origin:** **Existing compartment** (resource discovery).  
   Do **not** choose My configuration, Template, or Source code control.
4. **Compartment to capture:** the compartment that contains VM1, the door, the VCN, and the shared bucket (today: **Default**).  
   **Region:** the lab home region.
5. **Services:** choose **Selected** (wording may be “Selected services” / a checklist). Enable at least:
   - **core** (compute, VCN, IPs, security lists, volumes)
   - **objectstorage**
   - **functions**
   - **events**
   - **budget**
   - **artifacts** (OCIR), if listed  
   **Do not enable identity.**  
   If the UI only offers “all services,” still create the stack, but when you sanitize the zip (§18.5) delete identity/user/key resources if any appear. **Even with Identity unchecked, the 2026-08-12 lab dump still emitted Identity Domains — always search the zip for `identity_domains`, `auth_token`, and PEM blocks.**
6. **Use custom terraform providers:** **unchecked**.
7. **Name:** `mcmgr-lab-discovery-reference`  
   **Description:** `REFERENCE ONLY. Do not Apply or Destroy. For writing product OpenTofu.`  
   Avoid confidential info in these fields (Oracle’s warning).
8. **Create in compartment:** same as the lab compute compartment.
9. **Terraform version:** **1.5.x** (or the Console’s 1.5.7 / 1.5 line). Do not pick 1.2, 1.1, or 1.0.
10. **Tags (optional):** freeform `mcmgr-role` = `discovery-reference`.  
    Do **not** set `mcmgr-domain=mc-server-compartment` on the stack object.
11. Review. Confirm there is **no** “Run apply” check (if present, leave it off).
12. **Create.** A work request runs, then a **job that generates configuration** (this is not Apply). Wait until the stack is **Active** and the generate job **Succeeded**.

Docs: [Creating a Stack from an Existing Compartment](https://docs.oracle.com/en-us/iaas/Content/ResourceManager/Tasks/create-stack-compartment.htm).

### 18.3 Download artifacts

On the stack details page:

1. **Terraform configuration → Download** (zip of generated `.tf` files).
2. **View state → Download Terraform state** (optional but useful so agents see attribute values that HCL omitted).
3. Optionally copy the **generate-job logs** if discovery skipped resources.

Do **not** click **Plan**, **Apply**, **Import state**, **Destroy**, or **Upgrade provider versions**.

### 18.4 Capture IAM without scanning the tenancy

In a text file `iam-reference.md` (gitignored pack only):

- Dynamic group **names**, **matching rules**, and OCIDs
- Policy **names** and **full statement text**
- Which group is door vs VM1 vs both-VMs vs Functions

You can paste from the private deployment markdown. **Strip Auth Tokens, API key PEMs, and RCON passwords** if they appear nearby.

### 18.5 Sanitize before any agent-facing copy

Unzip the configuration. Then:

1. Delete `identity_domains.tf` entirely (users, API keys, auth tokens). Delete or empty Terraform state if it was downloaded.
2. Delete or empty any `oci_objectstorage_object` resources (especially `backups/` zips and JSON bodies). Keep bucket-level HCL.
3. Search for `ocid1.credential`, `auth_token`, `private_key`, `password`, `rcon`, `ssh-rsa`, emails. Replace with `<REDACTED>`.
4. You may leave OCIDs in the **gitignored** pack (agents here already see them in private markdown). If you ever attach the zip to a **public** issue or commit, replace all OCIDs and public IPs with placeholders.
5. Re-zip. Suggested filename: `mcmgr-lab-discovery-reference-sanitized.zip`.

The 2026-08-12 lab capture is already sanitized under lab `data/reference-stack/` (raw OCID folder gitignored). Tracked digest: [`Lab-Reference-Stack-Notes.md`](Lab-Reference-Stack-Notes.md). **Do not capture another dump unless the live layout changes in a way that document does not cover.**

### 18.6 Pack to give agents

Create a gitignored folder, for example:

`OCI-mc-server/data/reference-stack/`  
or  
`OCI-mc-server-manager/data/reference-stack/`

Include:

| File | Required? |
|------|-----------|
| Sanitized discovery `.tf` zip | **Yes** |
| Discovery state file (redact if needed) | Recommended |
| `iam-reference.md` | **Yes** |
| A one-line `README.txt`: “REFERENCE ONLY. Do not apply. Product IaC is rewritten per docs/Automated-Infrastructure-Deployment.md.” | **Yes** |
| Optional: screenshots of VNIC secondary IPs / reserved IP attachment | Nice |

Then tell the agent, in chat:

```text
Reference pack is in data/reference-stack/ (gitignored).
Follow docs/Automated-Infrastructure-Deployment.md.
Do not import this stack. Do not apply it. Rewrite infra/ with mcmgr names.
```

### 18.7 After agents have used it

You may **delete the Resource Manager stack object** to reduce Console clutter. Deleting a stack should not delete the discovered VMs — read the Console confirmation. If the confirmation mentions destroying resources, **cancel**. Leaving the idle stack is also fine (free).

Do **not** keep applying jobs on it “to stay in sync.” Day-2 remains the Avalonia Manager.

### 18.8 CLI equivalent (optional)

If you prefer CLI instead of Console ([create-from-compartment](https://docs.oracle.com/en-us/iaas/Content/ResourceManager/Tasks/create-stack-compartment.htm)):

```text
oci resource-manager stack create-from-compartment
  --compartment-id <STACK_HOME_OCID>
  --config-source-compartment-id <LAB_COMPARTMENT_OCID>
  --config-source-region <HOME_REGION>
  --config-source-services-to-discover '["core","objectstorage","functions","events","budget","artifacts"]'
  --terraform-version 1.5.x
  --display-name mcmgr-lab-discovery-reference
  --description "REFERENCE ONLY. Do not Apply."
```

Then download config with the stack get-configuration APIs documented under [Getting a Stack’s Terraform Configuration](https://docs.oracle.com/en-us/iaas/Content/ResourceManager/Tasks/upgradingstacks.htm) (same download as Console).

---

## 19. Phase 3 implementation mapping

| MVP step | What this document requires |
|----------|-----------------------------|
| **3.1 OpenTofu skeleton** | Create `infra/` as §11. `tofu validate` / plan against an **empty** test compartment. **Read this file first.** Use discovery pack only as reference. No apply until operator approves. |
| **3.2 Wizard UX** | Collect variables in §11.2; show plan summary; persist resume; **no** RM UI. Infra zip resolve (bundled only is OK in 3.2; GitHub pull can be 3.3 or a small follow-up). |
| **3.3 Apply + bootstrap** | Local `tofu apply`; capacity wait; SSH bootstrap per Minecraft blueprint; write meta; Function/OCIR Auth Token. |
| **4 Connect existing** | Does **not** need tofu state. `meta/infra.json` only. |
| **7 Installer** | Bundle pinned `tofu.exe` + `infra/` + license; optional GitHub infra pull already designed. |

**Step 3.1 must not:** clone discovery HCL, add `schema.yaml` “just in case,” create custom images, or target Resource Manager as a backend.

---

## 20. Reference links

Oracle (retrieved 2026-08-12):

- [Resource Manager overview](https://docs.oracle.com/en-us/iaas/Content/ResourceManager/Concepts/resourcemanager.htm)
- [Terraform configurations for Resource Manager](https://docs.oracle.com/en-us/iaas/Content/ResourceManager/Concepts/terraformconfigresourcemanager.htm) (includes [schema documents](https://docs.oracle.com/en-us/iaas/Content/ResourceManager/Concepts/terraformconfigresourcemanager_topic-schema.htm))
- [Resource discovery](https://docs.oracle.com/en-us/iaas/Content/ResourceManager/Concepts/resource-discovery.htm)
- [Create stack from existing compartment](https://docs.oracle.com/en-us/iaas/Content/ResourceManager/Tasks/create-stack-compartment.htm)
- [Create stack from zip](https://docs.oracle.com/en-us/iaas/Content/ResourceManager/Tasks/create-stack-local.htm)
- [Create stack from Git](https://docs.oracle.com/en-us/iaas/Content/ResourceManager/Tasks/create-stack-git.htm)
- [Supported Terraform versions](https://docs.oracle.com/en-us/iaas/Content/ResourceManager/Reference/terraformversions.htm)
- [Upgrade stacks / auto-upgrade to 1.5.x](https://docs.oracle.com/en-us/iaas/Content/ResourceManager/Tasks/upgradingstacks.htm)
- [Create apply job](https://docs.oracle.com/en-us/iaas/Content/ResourceManager/Tasks/create-job-apply.htm)
- [Custom providers](https://docs.oracle.com/en-us/iaas/Content/ResourceManager/Tasks/update-stack-custom-providers.htm)
- [Always Free resources](https://docs.oracle.com/en-us/iaas/Content/FreeTier/freetier_topic-Always_Free_Resources.htm)
- [Create custom image](https://docs.oracle.com/en-us/iaas/Content/Compute/Tasks/custom-images-create.htm)
- [Instance configurations](https://docs.oracle.com/en-us/iaas/Content/Compute/Tasks/creatinginstanceconfig.htm)
- [oci_core_instance / user_data](https://docs.oracle.com/en-us/iaas/tools/terraform-provider-oci/latest/docs/r/core_instance.html)
- [oci_identity_dynamic_group](https://registry.terraform.io/providers/oracle/oci/latest/docs/resources/identity_dynamic_group)

OpenTofu / HashiCorp:

- [OpenTofu GitHub (MPL 2.0)](https://github.com/opentofu/opentofu)
- [OpenTofu Releases](https://github.com/opentofu/opentofu/releases)
- [oracle/oci on OpenTofu Registry](https://search.opentofu.org/provider/oracle/oci/latest)
- [State encryption](https://opentofu.org/docs/language/state/encryption/)
- [HashiCorp BSL 1.1](https://www.hashicorp.com/bsl)
- [HashiCorp licensing FAQ](https://www.hashicorp.com/license-faq)

Ubuntu on OCI:

- [Find Ubuntu images](https://ubuntu.com/docs/oracle/oracle-how-to/find-ubuntu-images/)
- [OCI image catalog](https://docs.oracle.com/en-us/iaas/images/index.htm)

---

## 21. Changelog

| Date | Note |
|------|------|
| 2026-08-15 | D7 Connect-existing = MVP plan Phase 5; **D10** test-deploy bugs must be fixed in product HCL/bootstrap, not only the live VM. Vanilla in-game whitelist off (SETUP-ISSUE-3 / Step 4.3). SETUP-ISSUE-4 CHDIR → Step 4.2 comprehensive permissions. |
| 2026-08-14 | Step 3.3 blank-tenancy test: tenancy `mcmgr-door-ip`; door DG by instance OCID; OS seed 404=create; door `oci.env` OS vars; guest netplan; Vanilla whitelist; no apt-upgrade. Out-of-band policy import uses repo `infra/` + LocalAppData `-state` (quoted PowerShell). |
| 2026-08-12 | Budget wiring correction: live path is Events → Function; ONS `Budget-Alerts` is unlinked leftover. RM dump omitted the Events action. |
| 2026-08-12 | Lab discovery dump sanitized; added [`Lab-Reference-Stack-Notes.md`](Lab-Reference-Stack-Notes.md) (naming, 3 DGs, skip NAT/private subnet). Identity Domains leaked despite Identity unchecked. |
| 2026-08-12 | PRODUCT-IDEAS v1 spend-brake lock: MVP tofu grants Functions DG bucket-scoped object write (forward-compat); Function code still SoftStop-only in MVP; lock object is not an OpenTofu resource. Stop-list stays a variable (lab default: VM1+VM2). |
| 2026-08-12 | Live lab Function SoftStops VM1 **and** VM2 (`functions/shutdown_vm/` in lab); v1 still open on door-stop vs Micro-always-free. |
| 2026-08-12 | Initial blueprint: local OpenTofu as apply engine; Resource Manager discovery as operator reference only; Ubuntu 22.04 platform images + cloud-init baseline + SSH bootstrap; hybrid GitHub/bundled HCL; no custom images / instance configs / RM Git runtime. |
