# OCI MC Server

Windows desktop **Manager** for a private Minecraft stack on Oracle Cloud Infrastructure (OCI), aimed at **Always Free / $0** operation by default.

This is the **official product repository**. The Manager UI is **.NET 8 + Blazor Hybrid** (WPF + WebView2): one WinExe, `McManager.Hybrid`. Layout and visual choices are **not locked**.

## Status

Manage + Setup are usable on the Blazor Hybrid WinExe. **Phase B** (Avalonia → Blazor Hybrid) is **DONE**. Happy-path user guide: [`docs/Guide.md`](docs/Guide.md). Living execution checklist: [`docs/V1-Implementation-Plan.md`](docs/V1-Implementation-Plan.md) (**NEXT = Step 2.1**; do not start until asked). MVP Phases 0–7 are **DONE** ([`docs/MVP-Implementation-Plan.md`](docs/MVP-Implementation-Plan.md)); packaging is deferred to V1 Phase 9. Danger Zone can **Delete infrastructure** (`tofu destroy` of the product stack only). Phase B archive: [`docs/Blazor-UI-Migration-Plan.md`](docs/Blazor-UI-Migration-Plan.md).

Licensing is **TBD** (no `LICENSE` file yet).

## Solution

| Project | Role |
|---------|------|
| [`src/McManager.Hybrid`](src/McManager.Hybrid) | Manager UI WinExe (WPF + BlazorWebView) |
| [`src/McManager.Core`](src/McManager.Core) | Domain / OCI / shared logic |
| [`src/McManager.slnx`](src/McManager.slnx) | Solution |

## Build & run

Requirements: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```bash
dotnet restore src/McManager.slnx
dotnet build src/McManager.slnx
dotnet run --project src/McManager.Hybrid
```

`McManager.Hybrid` is a native WPF window (Evergreen WebView2; not a browser / not localhost). If WebView2 is missing, the app shows a MessageBox with the [Evergreen installer](https://go.microsoft.com/fwlink/p/?LinkId=2124703).

Open `src/McManager.slnx` in Visual Studio or Cursor/VS Code with the C# extension.

## Cost rule

Stay on **Always Free–eligible** OCI resources unless you explicitly accept spend. Do not add paid shapes, load balancers, or surprise billable services casually.

OCI SDK call patterns (throttling, waiters, request thrift): [`docs/OCI-API-Usage.md`](docs/OCI-API-Usage.md).

## Dual-repo layout

| Repo | Role |
|------|------|
| **This repo (`OCI-mc-server`)** | Official Manager (Blazor Hybrid UI + Core + Setup / OpenTofu) |
| Sibling **`OCI-mc-server-manager`** (lab) | Python day-2 tool, `door_vm/` / `vm_agent/` SoT, infra docs, product planning |

Deep infrastructure and product-intent docs live in the lab sibling (e.g. `Infrastructure-Information.md`, `PRODUCT-IDEAS.md`). Prefer that lab tree for live OCI/on-box truth while this product app is under construction.

## Secrets

**Never commit** OCIDs, API keys, SSH keys, RCON passwords, Auth Tokens, or filled local config.

- OCI API: `%USERPROFILE%\.oci\config` + PEM
- SSH: under `%USERPROFILE%\.ssh\`
- App seeds: gitignored `data/config.local.json` and `data/friends.local.json` (see [`docs/Local-Config.md`](docs/Local-Config.md))

## Local config (manage MVP)

```bash
# Already seeded on the operator machine (gitignored). To recreate:
copy config.local.example.json data\config.local.json
copy friends.local.example.json data\friends.local.json
# then fill OCIDs from the lab private deploy notes / lab data/config.json
```

On launch the shell status line loads this config (region, play IP, friend count).

## Git

Agents do **not** create commits — commit from Visual Studio when ready.

## Links

- Happy-path guide: [`docs/Guide.md`](docs/Guide.md)
- GitHub: [maattox/oci-mc-server](https://github.com/maattox/oci-mc-server)
