# OCI MC Server

Windows desktop **Manager** for a private Minecraft stack on Oracle Cloud Infrastructure (OCI), aimed at **Always Free / $0** operation by default.

This is the **official product repository**. The Manager UI is **.NET 8 + Blazor Hybrid** (WPF + WebView2): one WinExe, `McManager.Hybrid`. Layout and visual choices are **not locked**.

## Status

Manage + Setup are usable on the Blazor Hybrid WinExe. Happy-path user guide: [`docs/Guide.md`](docs/Guide.md). Living execution: [`docs/V1-Implementation-Plan.md`](docs/V1-Implementation-Plan.md) (**NEXT = Step 8.5.2** Pass 3 — do not start until the operator says so; follow-on P1–P13 **DONE**; do not start 9.1 until QA exits **and** Step **8.6.1** is DONE). MVP Phases 0–7 are **DONE** ([`docs/archive/MVP-Implementation-Plan.md`](docs/archive/MVP-Implementation-Plan.md)); Phase **8.6** is the CI-built ARM Function image (no Docker on the admin PC); packaging is V1 Phase 9. **Paid / spend mode is skipped** (later / far future). Danger Zone can **Delete infrastructure** (`tofu destroy` of the product stack only). Doc map: [`docs/README.md`](docs/README.md).

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

## Docs

Architecture, vision, QA, and on-box maps live in [`docs/`](docs/README.md) (`PRODUCT-IDEAS.md`, `Infrastructure-Information.md`, `Issues.md`, and the V1 plan). On-box source that Setup deploys lives in this repo (`door_vm/`, `vm_agent/`, `functions/`, `onbox/mcmgr/`, `infra/`).

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
# then fill OCIDs from OCI Console / TESTING tofu outputs (`%LOCALAPPDATA%\McManager\tofu\<stack-id>\`)
```

On launch the shell status line loads this config (region, play IP, friend count).

## Git

Agents do **not** create commits — commit from Visual Studio when ready.

## Links

- Happy-path guide: [`docs/Guide.md`](docs/Guide.md)
- GitHub: [maattox/oci-mc-server](https://github.com/maattox/oci-mc-server)
