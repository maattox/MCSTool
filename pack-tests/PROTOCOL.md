# Pack-corpus protocol (agent SoT)

Cheap-model checklists. Skills wrap **this file** + the PackTestHarness CLI. Do not reimplement Change-pack. Do not load full journals.

**Config:** `MCMANAGER_CONFIG_DIR` = `mcmgr-pack-test` (TESTING `config.local.json` copied from `mcmgr-blank-test`). Never repo `data/config.local.json` (Forge / `DEFAULT`). Never `mcmgr-blank-test` for this harness (keep interactive Manager Layer 2 clean). **OCI:** TESTING only.

**Wipe world:** always `true` for this suite.

**Assisted review:** default **Keep** all `NeedsYourCall`. Do **not** read or apply `client-only/*.yaml` during install. Automatic skips (`env.server`, exclude lists, in-jar) still run.

**Queue of one:** never parallel `ReplacePackAsync`. Parent starts the next pack only after `ready_for_next: true`.

---

## Verdicts

Exactly one of:

| Verdict | Meaning |
|---------|---------|
| `pass` | Analyze Continue + install finished + `minecraft.service` active, not crash-looping + RCON `list` OK + no FATAL/hard crash + Layer 3 did **not** save the boot |
| `pass_quarantined` | RCON OK only after Layer 3. **Not** `pass` |
| `blocked_freeze` | Freeze / `CanContinue = false`. No SSH replace |
| `product_fail` | Product/pack failed (not infra) |
| `timeout` | Replace or health wait hung past cap |
| `infra_fail` | SSH / connect / VM1 STOPPED / lock / harness usage that is stack-side |

Harness exit codes (P2): `0` = `pass` / `pass_quarantined`; `1` = `product_fail` / `blocked_freeze` / `timeout`; `2` = `infra_fail`; `3` = usage (bad flags / missing catalog row).

---

## Ready-gate

Harness sets `ready_for_next`. Parent **must not** spawn the next `pack-test-one` unless **all** are true:

1. Result YAML written (`phases/<phase>/results/<id>.yaml`).
2. VM1 **RUNNING**, SSH probe OK.
3. No in-progress replace.
4. On non-pass: `minecraft.service` **stopped** (no inherited crash-loop).
5. Idle stays **disabled for the whole phase**; re-enable only when the phase ends (and after any Minecraft start that force-enables it — OS-ISSUE-7).
6. Short cooldown (file locks).
7. `pack-tests/.lock` held by the **phase parent**; refuse to start if the lock is foreign (another chat / Pass 3).

`pass` / `pass_quarantined` still require the gate (Minecraft may stay up; idle still disabled until phase end).

---

## Abort

Stop spawning pack tests after **≥2 consecutive `infra_fail`**. Do **not** abort on `product_fail` / `blocked_freeze` / `timeout` / `pass` / `pass_quarantined`. Cap wall-clock / remaining queue in the manifest if a replace hangs. Set manifest `status: aborted` and `abort_reason`.

---

## Single pack (`pack-test-one`)

One catalog `id`. Stop after result + ready-gate. Do **not** interpret sidecars. Do **not** start the next pack.

1. Confirm `pack-tests/.lock` is this phase (or refuse). TESTING + `mcmgr-pack-test` only.
2. Resolve `pack-tests/packs/<filename>` from `catalog.yaml`. If catalog `sha256` is set, verify it (mismatch → `infra_fail` / usage, do not install).
3. Run PackTestHarness (P2), same Core path as Hybrid Change pack: analyze → identity/derived zip when Hybrid would → default-Keep review → **before live replace:** stop `minecraft.service` and disable idle (OS-ISSUE-7; previous pack may have left the game up) → **during** `ReplacePackAsync`, re-disable idle every ~15s (Minecraft start force-enables it) unless `--analyze-only`. SSH abort / VM1 not RUNNING during replace is `infra_fail`.
4. Intended CLI (P2 lands the binary): `--pack <id>` `--catalog <path>` `--phase <phase-dir>` `--wipe-world` (default on) `--analyze-only` (no SSH).
5. Write `results/<id>.yaml` (schema below). Journal excerpt **≤80 lines** FATAL/ERROR at `logs/<id>.excerpt.txt` (gitignored). Full journal only under gitignored `logs/`.
6. Run ready-gate. Set `ready_for_next`. Exit with the code in [Verdicts](#verdicts).
7. **Stop.** Do not analyze the corpus. Do not `/phase-planning`.

---

## Phase parent (`pack-test-phase`)

1. Take or create `pack-tests/.lock`. If foreign → **stop** (do not steal VM1 from Pass 3 / another chat).
2. Disable idle for the **whole** phase. Re-enable only at phase end.
3. Copy `phases/_template/` to `phases/<phase_id>/` if needed. Set `status: running`.
4. For each `queue[]` entry **in order**: spawn **one** `pack-test-one`. Wait until result YAML exists **and** `ready_for_next: true`. Never parallel.
5. After each result: if verdict is `infra_fail`, increment `consecutive_infra_fails`; else reset to `0`. If `consecutive_infra_fails >= 2` → [Abort](#abort).
6. **Never** read full journals or paste consoles into markdown.
7. Do **not** write `EXECUTIVE-SUMMARY.md` (that is analyze).
8. On empty remaining queue: `status: complete`. Release lock. Re-enable idle.
9. No `git push`. No pack downloads. No `tofu apply` / `destroy`. Do not SoftStop the door.

---

## Analyze (`pack-test-analyze`)

After the queue is `complete` or `aborted`. Stronger model. Parent does **not** write this file.

1. Read result YAML files + `client-only/<id>.yaml` sidecars + catalog rows. Do **not** load full `logs/` journals.
2. Write `phases/<phase_id>/EXECUTIVE-SUMMARY.md` clustered by: infra vs client-jar kept vs Java vs overlay leftover vs RCON-timeout-with-Done vs quarantine.
3. Do **not** implement product fixes. Do **not** `/phase-planning` unless the operator asks.

---

## Result YAML schema

Path: `phases/<phase>/results/<id>.yaml` (tracked). Example:

```yaml
schema_version: 1
pack_id: example-slug
filename: example.mrpack
sha256: ""
started_utc: "2026-08-24T00:00:00Z"
finished_utc: "2026-08-24T00:10:00Z"
verdict: pass
ready_for_next: true
fail_message: ""
identity:
  expected:
    minecraft: "1.21.1"
    loader: fabric
    loader_version: ""
    java_major: 21
  detected:
    minecraft: ""
    loader: ""
    loader_version: ""
    java_major: 0
  applied:
    minecraft: ""
    loader: ""
    loader_version: ""
    java_major: 0
skip_counts:
  automatic_client: 0
  unknown_kept: 0
health:
  rcon_list: false
  crash_loop: false
  fatal: false
  quarantine: false
log_excerpt_path: logs/example-slug.excerpt.txt
infra:
  ssh: false
  vm1: ""
  minecraft_unit: ""
  idle_disabled: true
notes: []
```

`fail_message`: Manager/harness one-liner, not a stack. `skip_counts`: short; no full jar dump. `log_excerpt_path`: prefer gitignored `logs/<id>.excerpt.txt` (cap ≤80 lines).

---

## Forbidden

- Hybrid / WPF Change pack (agents cannot drive the window)
- `data/sample-packs/` as the corpus
- Applying sidecar jars as Layer 2 Skip during install
- Parallel pack tests on one VM1
- Appending consoles to markdown (no second `Mod-Pack-Tests.md`)
- Committing pack bytes, full journals, filled `*.local.json`, or OCIDs
- `DEFAULT` / live Forge lab; Minecraft `0.0.0.0/0`; SoftStop door; `tofu apply` / `destroy`
- Downloading packs; in-app catalog (rejected product)
