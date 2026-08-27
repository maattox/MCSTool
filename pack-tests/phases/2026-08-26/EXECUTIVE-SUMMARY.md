# Pack-corpus executive summary — phase `2026-08-26`

**Status:** original queue `complete` (not aborted). **Parked follow-up:** `mr-fabric-cobblemon-1.7.3` re-run is **UNFINISHED** (operator aborted 2026-08-27 before analysis lock-in). Do **not** treat the later harness `pass` YAML as verified. Come back with `/pack-test-one` that id.

**11** queued. **10** `pass`. **1** unfinished cobblemon mrpack (first run `product_fail`; re-run not locked in). **0** `infra_fail` / `timeout` / `blocked_freeze` / `pass_quarantined`. Consecutive infra fails stayed **0**. Idle was re-enabled at original phase end, then again on 2026-08-27 wrap-up.

TESTING only. Sidecars were **not** applied as Skip (default Keep). Do not treat this file as a product-fix list.

## Unfinished — come back later

**`mr-fabric-cobblemon-1.7.3` (Modrinth mrpack)** is not closed out.

- First phase run: `product_fail` (~18 min). SSH died mid-replace (`connection was aborted`); VM1 was Stopping/STOPPED. Excerpt empty. Same Cobblemon line as CurseForge Server Files **did** boot (`cf-fabric-cobblemon-1.7.3` **pass**), so the mrpack was not shown to be unbootable.
- Later re-run: harness wrote `verdict: pass` / `rcon_list: true`, and the Manager console showed `Done` + RCON listener. The parent/subagent never finished ready-gate analysis. Idle SoftStop interrupted an earlier attempt (OS-ISSUE-7). Operator stopped the session 2026-08-27.
- **Next time:** TESTING + `mcmgr-pack-test`. Disable idle for the whole replace (harness `IdleHold` every ~15s). `/pack-test-one` `mr-fabric-cobblemon-1.7.3` only. Then `/pack-test-analyze` if you want this summary rewritten. Harness classifier now treats connection-abort / VM1 not RUNNING as `infra_fail` (uncommitted working tree).

## Scoreboard

| id | format | verdict | auto skip | unknown kept | sidecar jars | RCON | quarantine |
|----|--------|---------|-----------|--------------|--------------|------|------------|
| homemade-fabric-strip | mrpack | pass | 1 | 0 | 1 | yes | no |
| mr-neoforge-blockfront-0.9.0.27b | mrpack | pass | 1 | 0 | 1 | yes | no |
| mr-fabric-fabulously-optimized-6.5.0 | mrpack | pass | 17 | 0 | 41 | yes | no |
| homemade-mods-fabric-1.21.1 | unstructured | pass | 36 | 0 | 41 | yes | no |
| homemade-jar-root-fabric-1.21.1 | jar-root | pass | 36 | 0 | 41 | yes | no |
| mr-fabric-simply-optimized-continued-2.1-26.2 | mrpack | pass | 3 | 0 | 7 | yes | no |
| mr-fabric-stam1o-create-s2 | mrpack | pass | 16 | 0 | 30 | yes | no |
| mr-fabric-cobblemon-1.7.3 | mrpack | unfinished | 26 | 0 | 64 | no* | no |
| cf-fabric-cobblemon-1.7.3 | cf-server | pass | 0 | 1 | 0 | yes | no |
| cf-neoforge-cobblemon-1.7.3 | cf-server | pass | 0 | 9 | 0 | yes | no |
| cf-forge-cobblemon-1.5.2 | cf-server | pass | 1 | 11 | 0 | yes | no |

`automatic_client` = assisted-review **WillSkip**. `unknown_kept` = **NeedsYourCall** (default Keep). Sidecar jars are operator-verified client-only names; they were not Skip-during-install. `*` cobblemon RCON: first run never reached a healthy unit; later harness YAML says `rcon_list: true` but the re-run was **not locked in**.

---

## Infra

**The cobblemon mrpack is unfinished, not a locked `product_fail`.**

First run (~18 min): replace error *An established connection was aborted by the server.* Ready-gate then saw VM1 **not RUNNING** and SSH timeout. Parent STARTed VM1, stopped `minecraft`, disabled idle, then continued. Excerpt file is empty (replace never produced a boot journal).

Same Cobblemon line as CurseForge Server Files **did** boot (`cf-fabric-cobblemon-1.7.3` **pass** ~2 min). The mrpack content is **not** shown to be unbootable; the live SSH session died and the instance was Stopping/STOPPED.

Likely stack: previous pack left Minecraft **active**; OS-ISSUE-7 can force-enable idle on boot/start; a long wipe/upload then SoftStops VM1. Harness `LooksInfra` originally did not treat “connection aborted” as `infra_fail`, so the phase did not abort (abort is ≥2 consecutive `infra_fail`). Working-tree harness now classifies that as `infra_fail` and holds idle disabled every ~15s during replace (`IdleHold`).

No other pack lost SSH. Re-run was aborted 2026-08-27 before lock-in.

---

## Client-jar kept

Harness never applied sidecars. Default Keep. Several **passes** still installed jars the sidecar calls client-only (gap = sidecar − WillSkip, with `unknown_kept: 0` so they were **Must keep**, not NeedsYourCall).

| Pack | Sidecar | WillSkip | Likely installed vs sidecar |
|------|---------|----------|-----------------------------|
| homemade-fabric-strip | 1 (Sodium) | 1 | match |
| mr-neoforge-blockfront-0.9.0.27b | 1 (Sodium) | 1 | match |
| mr-fabric-fabulously-optimized-6.5.0 | 41 | 17 | ~24 sidecar jars treated as server |
| homemade-mods-fabric-1.21.1 | 41 (same FO set) | 36 | ~5; matcher/in-jar skipped more than mrpack `env.server` |
| homemade-jar-root-fabric-1.21.1 | 41 | 36 | same as unstructured FO |
| mr-fabric-simply-optimized-continued-2.1-26.2 | 7 | 3 | ~4 mistagged opt/client jars still kept; **still passed** on Java 25 / MC 26.2 |
| mr-fabric-stam1o-create-s2 | 30 | 16 | ~14 (maps/HUD/editor); **still passed** |
| mr-fabric-cobblemon-1.7.3 | 64 | 26 | unfinished; first install did not finish; re-run not locked in |
| cf-fabric-cobblemon-1.7.3 | 0 | 0 | 1 NeedsYourCall kept (sidecar says Server Files already stripped) |
| cf-neoforge-cobblemon-1.7.3 | 0 | 0 | 9 NeedsYourCall kept |
| cf-forge-cobblemon-1.5.2 | 0 | 1 | 11 NeedsYourCall kept; sidecar notes appleskin shipped in Server Files on purpose |

FO / Stam1o / Simply Optimized **booted with those extra client jars**. Cluster is “sidecar vs automatic skip,” not crash blame. CurseForge Server Files show the other shape: empty sidecar, **NeedsYourCall default Keep** (installer/unknown-side leftovers), still **pass**.

---

## Java

No Java-runtime failure.

- `mr-fabric-simply-optimized-continued-2.1-26.2`: expected/applied **Java 25** / **Minecraft 26.2**. Catalog warned it might `product_fail`. **pass.**
- Unstructured / jar-root FO: detected Minecraft **1.21** (no loader version); catalog **1.21.1** / Fabric **0.19.3** applied. Derived zip. **pass.**
- `cf-forge-cobblemon-1.5.2`: detected **1.20**; catalog **1.20.1** / Forge **47.2.17** applied. **pass.**
- CF Fabric/NeoForge Cobblemon: detected loader version empty; catalog versions applied. **pass.**

Identity confirm + catalog row did the job. No evidence the guest Java major was wrong.

---

## Overlay leftover

No sign a prior Layer 2 Skip overlay hijacked this phase. Every result notes sidecar not applied (default Keep). `mcmgr-pack-test` is the isolated config dir.

Derived zip (expected, not leftover) on: `homemade-mods-fabric-1.21.1`, `homemade-jar-root-fabric-1.21.1`, all three CurseForge Server Files packs. Original archives untouched.

CF `unknown_kept` is assisted-review Keep of NeedsYourCall on those zips, not a persisted Skip list.

---

## RCON-timeout-with-Done

**None on locked passes.** Every `pass` has `rcon_list: true`. Cobblemon mrpack first run never reached a healthy `minecraft.service` / RCON wait. A later harness write claimed `rcon_list: true` but that re-run is **unfinished**.

---

## Quarantine

**None.** `health.quarantine: false` on all 11. No `pass_quarantined`. Layer 3 did not save a boot.

---

## What this phase showed

Tiny tagged mrpacks (strip, BlockFront) skip exactly the sidecar Sodium and boot. Java 25 / MC 26.2 boots. FO client pack and homemade FO zips boot even when many HUD/render jars stay. Stam1o Create boots. Official Cobblemon **Server Files** (Fabric / NeoForge / Forge) boot. The Modrinth Cobblemon **mrpack** did not get a fair locked boot — first run lost SSH/VM1; the later re-run is **UNFINISHED**.

Come back: `/pack-test-one` `mr-fabric-cobblemon-1.7.3` with idle held off for the whole replace.
