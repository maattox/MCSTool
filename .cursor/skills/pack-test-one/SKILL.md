---
name: pack-test-one
description: Runs one pack-corpus catalog id through McManager.PackTestHarness (headless Change pack). Use when the operator invokes /pack-test-one or a pack-test-phase parent asks to test a single pack id. Composer 2.5 is OK. Stops after result YAML + ready-gate.
disable-model-invocation: true
---

# Pack test one

One catalog `id`. **Composer 2.5 OK** (not Fast). SoT: [`pack-tests/PROTOCOL.md`](../../../pack-tests/PROTOCOL.md) — **Single pack**. Do not duplicate it.

## Before the harness

1. Read `pack-tests/PROTOCOL.md` headings **Verdicts**, **Ready-gate**, **Single pack**.
2. `MCMANAGER_CONFIG_DIR` must be **`mcmgr-pack-test`** (TESTING). Refuse repo `data/config.local.json` and `mcmgr-blank-test`.
3. Confirm `pack-tests/.lock` names **this** phase (contents contain the phase directory name). Missing or foreign (Pass 3 / another chat) → **stop**. Do not steal the lock.
4. Do **not** read or apply `pack-tests/client-only/*.yaml`. Do not download packs. No `git push`. No `tofu apply` / `destroy`. Do not SoftStop the door.

## Run

```powershell
$env:MCMANAGER_CONFIG_DIR = "$env:LOCALAPPDATA\McManager\mcmgr-pack-test"
dotnet run --project src/McManager.PackTestHarness -- `
  --pack <id> --catalog pack-tests/catalog.yaml --phase pack-tests/phases/<phase_id>
```

`--wipe-world` is default on. Use `--analyze-only` only if the parent asked for no SSH.

Exit: `0` pass / pass_quarantined; `1` product_fail / blocked_freeze / timeout; `2` infra_fail; `3` usage.

## Stop

Wait until `phases/<phase>/results/<id>.yaml` exists. Check `ready_for_next`. **Stop.** Do not start the next pack. Do not write `EXECUTIVE-SUMMARY.md`. Do not `/phase-planning`. Do not re-enable idle (phase parent only).
