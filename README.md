# OCI MC Server

Windows desktop **Manager** for a private Minecraft Java server on Oracle Cloud Infrastructure (OCI), aimed at **Always Free / $0** operation by default.

The Manager is a .NET 8 **Blazor Hybrid** app (WPF + WebView2): one WinExe, `McManager.Hybrid`. Setup is inside that same app.

## Status

**Open beta 0.9.0** (not 1.0.0). Manage + Setup are usable. Happy-path: [`docs/Guide.md`](docs/Guide.md) — **Windows installer** (per-user, no admin), private **IP allowlist** (no public Minecraft), **$1 spend-brake lock**, WebView2 Evergreen as a prerequisite, unsigned/SmartScreen expected for open-beta builds, and GitHub Releases **check** (prompt + notes; Manager never installs the update). **Users do not need Docker** or the .NET SDK: the installer bundles a pre-built ARM Function tarball that Setup copies into the user’s OCIR.

There is **no paid / spend mode**. Danger Zone can **Delete infrastructure** (`tofu destroy` of the product stack only).

Licensed under the [MIT License](LICENSE).

## Solution

| Project | Role |
|---------|------|
| [`src/McManager.Hybrid`](src/McManager.Hybrid) | Manager UI WinExe (WPF + BlazorWebView) |
| [`src/McManager.Core`](src/McManager.Core) | Domain / OCI / shared logic |
| [`src/McManager.slnx`](src/McManager.slnx) | Solution |

On-box source that Setup deploys lives in this repo (`door_vm/`, `vm_agent/`, `functions/`, `onbox/mcmgr/`, `infra/`).

## Install (users)

Run the Windows installer (`MCManager-Setup-<version>.exe`) from [GitHub Releases](https://github.com/maattox/oci-mc-server/releases) when a release exists, or pack one locally — see [`docs/Guide.md`](docs/Guide.md#3-install-the-manager). Per-user (no administrator prompt, not Program Files). Setup is inside that one app. You do **not** need Docker Desktop, WinGet, or the .NET SDK.

## Build & run (developers)

Requirements: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```bash
dotnet restore src/McManager.slnx
dotnet build src/McManager.slnx
dotnet run --project src/McManager.Hybrid
```

`McManager.Hybrid` is a native WPF window (Evergreen WebView2; not a browser / not localhost). If WebView2 is missing, the app shows a MessageBox with the [Evergreen installer](https://go.microsoft.com/fwlink/p/?LinkId=2124703). From-source checkouts without `artifacts/mcmgr-fn-softstop-linux-arm64.tar` still need Docker Desktop if you want Setup to build the spend-brake Function image.

Open `src/McManager.slnx` in Visual Studio or VS Code with the C# extension.

## Cost rule

Stay on **Always Free–eligible** OCI resources unless you explicitly accept spend. Do not add paid shapes, load balancers, or surprise billable services casually.

## Secrets

**Never commit** OCIDs, API keys, SSH keys, RCON passwords, Auth Tokens, or filled local config.

- OCI API: `%USERPROFILE%\.oci\config` + PEM
- SSH: under `%USERPROFILE%\.ssh\`
- App seeds (gitignored): `data/config.local.json` and `data/friends.local.json` when running from a checkout; an installed Manager writes the same files under `%LOCALAPPDATA%\McManager`

From a checkout:

```bash
copy config.local.example.json data\config.local.json
copy friends.local.example.json data\friends.local.json
```

Then fill OCIDs from OCI Console / Setup tofu outputs (`%LOCALAPPDATA%\McManager\tofu\<stack-id>\`).

## Docs

User guide: [`docs/Guide.md`](docs/Guide.md).

## Links

- Happy-path guide: [`docs/Guide.md`](docs/Guide.md)
- GitHub: [maattox/oci-mc-server](https://github.com/maattox/oci-mc-server)
