# Developer notes

Users start at the [root README](../README.md) and the [guide](Guide.md). This file is for people building, packing, or changing MCSTool.

The app is a .NET 8 **Blazor Hybrid** app (WPF + WebView2): one WinExe, `McManager.Hybrid`. Setup is inside that same app.

## Status

**Open beta 0.9.1** is published on [GitHub Releases](https://github.com/maattox/MCSTool/releases). Users install the Windows installer from there. Pushing `master` does **not** cut a new Release — only a new tag + Release does.

Licensed under the [MIT License](../LICENSE).

## Solution

| Project | Role |
|---------|------|
| [`src/McManager.Hybrid`](../src/McManager.Hybrid) | Manager UI WinExe (WPF + BlazorWebView) |
| [`src/McManager.Core`](../src/McManager.Core) | Domain / OCI / shared logic |
| [`src/McManager.slnx`](../src/McManager.slnx) | Solution |

On-box source that Setup deploys lives in this repo (`door_vm/`, `vm_agent/`, `functions/`, `onbox/mcmgr/`, `infra/`).

## Build and run

Requirements: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
dotnet restore src\McManager.slnx
dotnet build src\McManager.slnx
dotnet run --project src\McManager.Hybrid
```

Or open `src/McManager.slnx` in Visual Studio and run **McManager.Hybrid**.

`McManager.Hybrid` is a native WPF window (Evergreen WebView2; not a browser / not localhost). If WebView2 is missing, the app shows a MessageBox with the [Evergreen installer](https://go.microsoft.com/fwlink/p/?LinkId=2124703).

Folder publish (no installer):

```powershell
dotnet publish src\McManager.Hybrid -c Release -r win-x64 --self-contained
```

The output is a product root (`infra/` and on-box trees sit next to the exe). The Function tar copies when `artifacts/mcmgr-fn-softstop-linux-arm64.tar` exists.

From-source checkouts **without** that tar still need Docker Desktop if you want Setup to build the spend-brake Function image. Users of the installer do **not** need Docker or the .NET SDK.

## Pack the installer

Install [Inno Setup 6](https://jrsoftware.org/isinfo.php), then from the repo root:

```powershell
powershell -ExecutionPolicy Bypass -File .\packaging\pack.ps1
```

That fails if the Function tar is missing (rebuild recipe: [`functions/shutdown_vm/README.md`](../functions/shutdown_vm/README.md)). The `.exe` lands in `packaging/out/` (gitignored).

## GitHub Releases

When you mean to ship a newer installer: bump Hybrid `<Version>`, pack with `packaging/pack.ps1`, tag the commit, then open [Releases](https://github.com/maattox/MCSTool/releases/new).

**Do not** mark it as a pre-release (the in-app updater uses `/releases/latest`, which ignores pre-releases). Do not attach the Function tar as a separate asset — it is already inside the installer. The GitHub **repository** must be **public** or the updater’s unauthenticated request 404s and stays quiet.

Optional:

```powershell
gh release create v0.9.1 .\packaging\out\MCSTool-Setup-0.9.1.exe --title "MCSTool 0.9.1" --notes "Paste the user-facing notes here."
```

## Cost

Stay on **Always Free–eligible** OCI resources unless you explicitly accept spend. Do not add paid shapes, load balancers, or surprise billable services casually. Danger Zone **Delete infrastructure** is `tofu destroy` of the product stack only.

## Secrets

**Never commit** OCIDs, API keys, SSH keys, RCON passwords, Auth Tokens, or filled local config.

- OCI API: `%USERPROFILE%\.oci\config` + PEM
- SSH: under `%USERPROFILE%\.ssh\`
- App seeds (gitignored): `%LOCALAPPDATA%\MCSTool\profiles\<slug>\` (`config.local.json`, `friends.local.json`, wizard, imported packs, tofu state). `app-settings.json` in `%LOCALAPPDATA%\MCSTool` lists this PC’s servers. Repo `data/` is leftover, not the from-source seed. `MCMANAGER_CONFIG_DIR` is a flat QA folder.

From a checkout:

```powershell
copy config.local.example.json data\config.local.json
copy friends.local.example.json data\friends.local.json
```

Then fill OCIDs from OCI Console / Setup tofu outputs (`%LOCALAPPDATA%\MCSTool\profiles\<slug>\tofu\<stack-id>\`). From-source with no env override uses that same LocalAppData layout (not repo `data/`).

## Docs

| Doc | Audience |
|-----|----------|
| [`../README.md`](../README.md) | Users (GitHub landing page) |
| [`Guide.md`](Guide.md) | Users (install + Setup + day-to-day) |
| This file | Developers |
| `docs/archive/` | Operator / agent notes — **gitignored**, not on the public tree |
