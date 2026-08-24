# Pack-corpus (Change-pack regression)

Expected-to-work packs that should **boot on TESTING VM1 after Change pack**. Headless harness (P2) + agent skills (P3). Not Hybrid UI QA. Not QA Pass 3 catalog IDs.

**Not** [`docs/Sample-Packs.md`](../docs/Sample-Packs.md) / `data/sample-packs/` (those are parser/UI refuse samples). Do not point this harness at that folder.

Protocol SoT: [`PROTOCOL.md`](PROTOCOL.md). Plan: [`docs/Pack-Corpus-Test-Plan.md`](../docs/Pack-Corpus-Test-Plan.md).

Config dir: `MCMANAGER_CONFIG_DIR` = `mcmgr-pack-test` (TESTING only). Not repo `data/config.local.json`. Not `mcmgr-blank-test`.

## Add a pack

1. Operator copies the archive into `packs/` (gitignored). Agents must not download kitchen-sink packs.
2. Add a row to `catalog.yaml` (`id`, `filename`, `sha256` before a live test, platform/format/loader/MC/Java/`size_class`).
3. Optional: after verifying client-only jars, add `client-only/<id>.yaml`. Install still default-Keeps; sidecars are for analysis only.
4. Put `id` on a phase `manifest.yaml` `queue[]`.
5. One TESTING VM1. Sequential. Never commit pack bytes or full journals.
