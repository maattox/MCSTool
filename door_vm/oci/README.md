# OCI CLI wrappers (VM2)

Thin shell scripts around the Oracle `oci` CLI for wake/stop orchestration.
The C control plane (Task 9+) invokes these with fixed paths; they do not source
`config.example.env` — export variables from your deployment env first.

## Scripts

| Script | Purpose |
|--------|---------|
| `start_vm1.sh` | `oci compute instance action --action START` |
| `stop_vm1.sh` | `SOFTSTOP`; skip if already STOPPED/STOPPING |
| `ip_to_vm1.sh` | Reassign reserved public IP to VM1 private IP |
| `ip_to_vm2.sh` | Reassign reserved public IP to VM2 private IP |
| `wait_forge.sh` | Poll `VM1_PRIVATE_IP:25565` until TCP accepts |
| `pull_os_budget.sh` | GET ledger/budget (flag-aware or `--force`) and **always** GET `meta/spend-brake-triggered.json` (404 = unlocked) |

See `config.example.env` for required variables.

## Auth and IAM

VM2 should use **instance principal** (dynamic group + policies) so the
control plane can call OCI without embedding user API keys.

Minimum policy shape (product SoT: `OCI-mc-server/infra/modules/iam`):

- `use instance-family` **in the product compartment** (often via the all-instances DG so START works even if the door DG is mis-matched)
- `manage public-ips`, `use private-ips`, `use virtual-network-family` **in tenancy** (`mcmgr-door-ip`) — compartment-only statements 404 on `UpdatePublicIp`
- Door DG membership: **`instance.id = <door OCID>`** (hyphenated `mcmgr-role` tag matching did not enroll the door on an identity-domain tenancy)

`/etc/mccontrol/oci.env` is mode **600 root**. Do not source it as `ubuntu`. `wait_forge.sh` must default optional vars (`POLL_INTERVAL_SEC`, `WAIT_TIMEOUT_SEC`) **before** CR-strip under `set -u`.

The instance principal must be able to:

1. **Start / SOFTSTOP** the VM1 compute instance (`INSTANCE_ID`).
2. **Update** the reserved public IP (`RESERVED_PUBLIC_IP_ID`) to point at
   either `VM1_PRIVATE_IP_ID` or `VM2_PRIVATE_IP_ID`.

Test from VM2 before wiring the daemon:

```sh
oci iam region get --auth instance_principal
oci compute instance get --instance-id "$INSTANCE_ID" --auth instance_principal
```

Add `--auth instance_principal` to wrapper invocations if your `~/.oci/config`
does not default to it.

## CLI version notes

Validate flags against the `oci` version on the target image (`oci -v`).
`public-ip update` uses `--force` when moving the reserved IP between door and VM1 secondaries. `ip_to_vm1.sh` / `ip_to_vm2.sh` no-op if the IP is already on the target private IP (avoids DEGRADED on a redundant Stop).

`oci os object get` of a missing key (CLI **3.90+**): ServiceError `"code": null`, message **The service returned error code 404**, JSON `"status": 404` — not `ObjectNotFound`. `pull_os_budget.sh` treats that as unlocked (`SPEND_BRAKE_LOCK=0`) and deletes the empty `--file` leftover. Other GET errors stay fail-closed.
