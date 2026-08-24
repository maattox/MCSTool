# Pack-corpus (Change-pack regression)

Expected-to-work packs that should **boot on TESTING VM1 after Change pack**. Headless harness (P2) + agent skills (P3). Not Hybrid UI QA. Not QA Pass 3 catalog IDs.

**Not** [`docs/Sample-Packs.md`](../docs/Sample-Packs.md) / `data/sample-packs/` (those are parser/UI refuse samples). Do not point this harness at that folder.

Protocol SoT: [`PROTOCOL.md`](PROTOCOL.md). Plan: [`docs/Pack-Corpus-Test-Plan.md`](../docs/Pack-Corpus-Test-Plan.md).

Config dir: `MCMANAGER_CONFIG_DIR` = `mcmgr-pack-test` (TESTING only). Not repo `data/config.local.json`. Not `mcmgr-blank-test`.

## Seed `mcmgr-pack-test`

Copy TESTING `config.local.json` from `mcmgr-blank-test` (keep the same SSH key path). Same stack, isolated Layer 2 / derived-pack data dir. Do **not** commit the copy.

```powershell
$src = "$env:LOCALAPPDATA\McManager\mcmgr-blank-test"
$dst = "$env:LOCALAPPDATA\McManager\mcmgr-pack-test"
New-Item -ItemType Directory -Force -Path $dst | Out-Null
# Match blank-test layout: config at the folder root, or under data\
if (Test-Path "$src\data\config.local.json") {
  New-Item -ItemType Directory -Force -Path "$dst\data" | Out-Null
  Copy-Item "$src\data\config.local.json" "$dst\data\config.local.json"
} else {
  Copy-Item "$src\config.local.json" "$dst\config.local.json"
}
$env:MCMANAGER_CONFIG_DIR = $dst
```

Confirm `oci.profile` is `TESTING`. Then:

```powershell
dotnet run --project src/McManager.PackTestHarness -- `
  --pack <id> --catalog pack-tests/catalog.yaml --phase pack-tests/phases/<phase> --analyze-only
```

Omit `--analyze-only` only when a live Change pack on TESTING VM1 is intended (wipe world always). Never repo Forge `data/config.local.json`. Never `mcmgr-blank-test`.

## Add a pack

1. Operator copies the archive into `packs/` (gitignored). Agents must not download kitchen-sink packs.
2. Add a row to `catalog.yaml` (`id`, `filename`, `sha256` before a live test, platform/format/loader/MC/Java/`size_class`).
3. Optional: after verifying client-only jars, add `client-only/<id>.yaml`. Install still default-Keeps; sidecars are for analysis only.
4. Put `id` on a phase `manifest.yaml` `queue[]`.
5. One TESTING VM1. Sequential. Never commit pack bytes or full journals.
