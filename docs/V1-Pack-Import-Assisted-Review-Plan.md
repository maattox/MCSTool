# V1 pack-import assisted review (living)

**Status:** **ACTIVE** — P1 **NEXT**. Created 2026-08-23. **Live NEXT:** [`NEXT.md`](NEXT.md).  
**Parent:** [`V1-Implementation-Plan.md`](V1-Implementation-Plan.md) Step **8.9**.  
**Spec:** [`Pack-Import-Intended-Design.md`](Pack-Import-Intended-Design.md) (operator 2026-08-23 design lock). This file is the **implementation queue**; the spec wins on product rules.  
**Why now:** operator asked to implement the locked design (homemade zip stays; unattended boot dropped; assisted review + dependency freeze) **before** QA Pass 3.

**Cost:** $0. TESTING profile only. Never `DEFAULT` / live Forge lab. Never Minecraft `0.0.0.0/0`.  
**Functions / tofu:** do **not** `tofu apply` / `destroy`. Do not SoftStop the door.  
**Hybrid config dir:** `MCMANAGER_CONFIG_DIR` = `mcmgr-blank-test` — **not** repo `data/config.local.json`.  
**SSH / live VM1:** not required for P1. P2 may use a RUNNING TESTING VM1 for Change pack if the operator is present; otherwise Hybrid `dotnet run` + unit tests.  
**Hosts / OCIDs:** do **not** paste live OCIDs, IPs, Auth Tokens, or key material into tracked docs.

Applies to **both** Setup and Manager **Change pack** unless a section says otherwise.

---

## How agents must use this file

1. Read **this protocol**, the [Progress dashboard](#progress-dashboard), [Scrutiny](#scrutiny-plan-decisions), and **only the NEXT section**.
2. Implement only that section. Do not start neighbors “while you are here.”
3. After finishing: mark **DONE**, set the next incomplete section to **NEXT**, changelog line, update V1 plan Step **8.9** + dashboard, update [`NEXT.md`](NEXT.md), **stop**.
4. Git: commits allowed per `git-policy`; never push/PR unless the operator asks.
5. Do **not** start Step **8.5.2** (Pass 3), **8.6.1**, or **9.1**.
6. If this plan disagrees with [`PRODUCT-IDEAS.md`](PRODUCT-IDEAS.md), **follow this plan** (and the pack-import spec) and note drift. Do not rewrite PRODUCT-IDEAS except the one scheduled-row already pointed here.
7. **P2 (UI)** must read the named UI skills **before** changing CSS/Razor. Reuse existing tokens. **NuGet** on `McManager.Hybrid` only. No Avalonia.

### Context budget

This header + **one** P-section + the files listed there. Spec: [`Pack-Import-Intended-Design.md`](Pack-Import-Intended-Design.md) (the NEXT section may name headings). Blueprint: **named §§ only**. Do not load Pass 3, the full V1 plan, or PRODUCT-IDEAS unless a heading is named.

### UI skills (P2)

Read **before** CSS/Razor:

- `C:\Users\matto\.agents\skills\impeccable\SKILL.md`
- `C:\Users\matto\.agents\skills\web-design-guidelines\SKILL.md`

### Operator entry

New Agent chat → `/next-step`. Fresh chat per P-section.

### PARALLEL-OK

None. P2 consumes P1 Core types. Setup + Change pack Razor share the review surface → **SEQUENTIAL**.

---

## What already exists (do not rediscover)

**Aligned with the spec today**

- Local file import only (picker / drag-and-drop). No catalog.
- `.mrpack`: skip order in `MrpackFileFilter.Decide` is force-include → `env.server=unsupported` → exclude lists → in-jar client. Unclear `env.server` **blocks**. Quilt / unknown loader / missing MC version refuse.
- Manual zip format detect: server layout, CurseForge Server Files, jar-root (`LooksLikeJarRootPack`), CF client/mixed refuse, launcher client zip refuse, unknown refuse.
- In-jar client (`InJarSideDetector`): Fabric/Quilt environment + client-only entrypoints; Forge/NeoForge `clientSideOnly` / `side=CLIENT`; common mixin **class** `@OnlyIn(CLIENT)` / `@Environment(CLIENT)`. **Not** `displayTest` / `IGNORE_SERVER_VERSION`. Dual-side lib with one client mixin **target** is kept when toml is present (CoFH class).
- Layer 2 local overlay per archive SHA: `{dataDirectory}/pack-lists/mcmgr-layer2-local.json` via `Layer2LocalOverlay.PromoteExclude` (crash **Keep excluded** today). Analyze already reads it.
- Jar-root / unstructured identity confirm + derived zip sidecar (`DerivedPackIdentity`, `PackIdentityFields.razor`).
- Friend-pack two checkboxes (Setup + Change pack).
- Layer 3 quarantine (exactly one blamed mod) already v1 — **do not change bounds**.

**Gaps this plan closes**

- **No dependency freeze.** `InJarSideDetector` reads `depends` only for Minecraft version, not inter-mod edges.
- **No assisted review UI.** Unknowns on manual zips are auto-kept with a warning (`UnclearSideKeepCopy` / `UnclearSideHighRiskCopy`); `CanContinue = true`. Test: `SetupPackImportTests.Manual_zip_with_unclear_side_jars_can_continue`.
- **No operator Skip-on-server at import.** Overlay writes are crash-only.
- **Manual skip order inverted.** `ManualPackFileFilter.Decide` runs in-jar **before** exclude lists. Target (and `MrpackFileFilter`) is exclude then in-jar.
- Setup and Change pack **duplicate** pack-step markup (summary `<pre>` + checkboxes). No three-group lists.

---

## Scrutiny (plan decisions)

Implementing agents follow these unless the operator overrides in chat.

1. **Assisted vs automatic.** `NeedsAssistedReview` is true when the **Needs your call** group is non-empty after freeze. Do **not** use the ≥10 / ≥50% high-unclear constants as a continue **gate**. Those constants may drive a **search box** when the unknown list is long (`UnclearSideHighCountThreshold`). Automatic packs (clean `.mrpack`, clean Server Files, homemade zip with zero Needs-your-call) keep today’s summary + friend-pack checkboxes and do **not** show the three-group review.
2. **Identity confirm stays** for all `ManualServerPackKind.UnstructuredServer` (homemade `mods/` **and** jar-root). CurseForge Server Files and `.mrpack` stay no-identity. This keeps Step **8.8 P9** (detection is often wrong). **Drift vs a narrow reading** of the spec formats table (“server layout → automatic”): homemade `mods/` still shows identity fields even with zero unknowns. Identity is not jar classification.
3. **`.mrpack` unclear `env.server` still refuses.** Do not send those files to assisted review.
4. **Persist Skip** as Layer 2 per-archive `excludes` via `Layer2LocalOverlay.PromoteExclude` (same file as crash Keep excluded). Default **Keep** writes nothing. Zip bytes change → new SHA → **new archive** (no “file changed” banner).
5. **Continue control** is existing Setup **Next** / Change pack **Install this pack**. Do **not** add a third acknowledgement checkbox. Do **not** require an explicit Keep on every row.
6. **Force-skip of a required dep** → `CanContinue = false` (or Hybrid equivalent) naming the kept depender. Unskip to proceed. Do not boot into “mod X requires Y”.
7. **Shared Razor** for the three-group review. Do not copy-paste the lists into both `SetupWizard.razor` and `ServerManagementTab.razor`.
8. **Reclassify in memory** after Skip ticks when P1 provides it. Full `AnalyzeFile` re-read of a large zip is a last resort, not the default.
9. **Quilt:** still not a Setup entry. Refuse install if the **confirmed** loader is Quilt. Identity dropdown stays Fabric/Forge/NeoForge only.
10. **Do not** grow in-jar heuristics, encode any one test-pack’s jar names into the denylist, turn Layer 3 into unbounded auto-strip, add a catalog, or port itzg `TYPE=` scripts.

---

## Drift vs PRODUCT-IDEAS / older copy

| Topic | Older / PRODUCT-IDEAS | This plan |
| ----- | --------------------- | --------- |
| Homemade zip unknown sides | Warn + auto-keep + continue (8.4 P9 / 8.7 P5) | Assisted review; default Keep; optional Skip; Next/Install is the ack |
| Homemade “just works” | Implied by continue-with-warning | **Dropped** (spec). Prefer `.mrpack` / Server Files for unattended |
| Assisted review + dep freeze | Design lock, not scheduled | **v1 now** (8.9) |
| Identity on homemade `mods/` | 8.8 P9: all unstructured | **Keep** (scrutiny #2) |
| Pass 3 | Next after 8.8 | Still blocked until **this plan completes** and the operator says so |

Do **not** rewrite PRODUCT-IDEAS sketches. The deferred-table row for this work should say **scheduled** (Step 8.9).

---

## Parked (not this plan)

| Item | Why |
| ---- | --- |
| QA Pass 3 | After this plan; operator must start it |
| CurseForge **API** / P11 refuse helper | Deferred / maybe later |
| In-app pack catalog | **Rejected** |
| Pack replace **light swap** | After-v1 |
| Quilt Setup radio | Out of spec / later |
| Growing in-jar heuristics until client dumps never crash | Spec non-goal |
| Unbounded Layer 3 strip-until-RCON | Spec non-goal |
| `tofu destroy` / second greenfield | Pass 3 / later |
| Step **8.6.1** / **9.1** | After QA exit |

---

## Progress dashboard

| ID | Section | Status | Parallel? | Live SSH/OCI? |
| -- | ------- | ------ | --------- | ------------- |
| **P1** | Core skip order + dependency freeze + review grouping | **NEXT** | SEQUENTIAL | No |
| **P2** | Assisted review UI (Setup + Change pack) + persist Skip + Guide | TODO | SEQUENTIAL | Optional |

**After this plan:** [`NEXT.md`](NEXT.md) → Step **8.5.2** Pass 3 (**blocked** until the operator says so). Do not start Pass 3 from a P2 changelog.

---

## Parallel groups

None. Run P1 then P2 in separate chats.

---

## P1 — Core skip order, dependency freeze, review grouping

**Status:** NEXT  
**Parallel:** SEQUENTIAL — P2 Hybrid needs these types  
**Cursor mode:** agent  

**Read first**

- [`Pack-Import-Intended-Design.md`](Pack-Import-Intended-Design.md) — **Skip order**, **Dependency freeze**, **Tiers** (not the whole file if already loaded)
- `src/McManager.Core/Setup/ManualPackFileFilter.cs`
- `src/McManager.Core/Setup/MrpackFileFilter.cs`
- `src/McManager.Core/Setup/InJarSideDetector.cs` (mod-id + `depends` / toml parse — do not load the whole mixin scanner unless needed)
- `src/McManager.Core/Setup/ManualServerPackAnalyzer.cs` (jar loop + unclear → `serverSide`)
- `src/McManager.Core/Setup/MrpackAnalyzer.cs` (classify loop)
- `src/McManager.Core/Setup/SetupPackImport.cs` (`SetupPackPreview`)
- `src/McManager.Core/Setup/Layer2LocalOverlay.cs`
- `src/McManager.Core/Setup/ExcludeIncludeLists.cs` (`PackFileSkipReason`)
- Tests: `ManualServerPackInstallerTests.cs`, `MrpackAnalyzerTests.cs`, `InJarSideDetectorTests.cs`, `SetupPackImportTests.cs`, `Layer2LocalOverlayTests.cs`

**Do**

1. **Align manual skip order** with the spec and `MrpackFileFilter`: force-include → exclude lists → high-confidence in-jar client. Update the `ManualPackFileFilter` remarks (they currently claim in-jar before exclude).
2. **Parse required dependencies** per jar from `fabric.mod.json` / `quilt.mod.json` `depends` (object keys that are other mods — not `minecraft` / `java` / loader ids as freeze edges), and from `mods.toml` / `neoforge.mods.toml` `[[dependencies.*]]` where mandatory/`type=required`. Map dep **mod id** → other jars in the same archive via each jar’s `id` / `modId`. Missing or unreadable metadata → **no edge** (jar may stay Needs your call). Optional / embedded / jar-in-jar do **not** force-keep a sibling already classified client-only.
3. **Dependency freeze** after automatic skips (steps 1–4), and **again** after operator Skip marks: never skip a jar that a **kept** jar declares as required. Put it in **Must keep** with “required by {B}”. If the operator force-skips that jar anyway, analysis is **blocked** (named B). Add `PackFileSkipReason.OperatorSkip` if useful; overlay excludes already apply as list skips.
4. **Review grouping DTO** (Core, no Razor): **Will skip** (automatic, with why: list / `env.server` / in-jar), **Needs your call** (unknown side **and** not required by a kept jar), **Must keep** (required dep of a kept jar, locked, short reason). Remaining unknown → Keep (server assumed) in Needs your call.
5. **Expose on `SetupPackPreview`** (and manual/mrpack analysis as needed): the three groups, `NeedsAssistedReview` (scrutiny #1), and a block reason when force-skip violates freeze. **P1 must not flip Hybrid:** keep `CanContinue = true` for manual zips with unknowns (existing tests). Set `NeedsAssistedReview` so P2 can gate. `.mrpack` unclear `env.server` still `CanContinue = false`.
6. **In-session reclassify API** so P2 can apply Skip terms without re-hashing the zip (e.g. `ApplyOperatorSkips` on the analysis + freeze). Persist helper: writing a Skip uses existing `PromoteExclude` (filename / id term consistent with crash Keep excluded). Provide a **remove** exclude for Unskip if missing today.
7. **Installers** honor freeze + overlay excludes (they already skip from analysis lists — keep that true after freeze un-skips a required dep).
8. **Unit tests** (synthetic jars in zip bytes, no kitchen-sink packs, no Hybrid):
   - Manual exclude-before-in-jar order.
   - Thermal-style: skipping CoFH while keeping Thermal is **refused** by freeze; CoFH lands in Must keep.
   - Optional/embedded does not resurrect a client-only sibling.
   - Unreadable metadata → Needs your call, no Must-keep edge.
   - Operator skip + freeze re-run; force-skip required dep blocks.
   - `.mrpack` unclear still fails after freeze.
   - Overlay SHA persist / unskip.

**Do not**

- Change Razor, ViewModels, or `Guide.md`.
- Change Layer 3 quarantine.
- Grow mixin heuristics.
- Flip `Manual_zip_with_unclear_side_jars_can_continue` to false (that is P2).

**Test**

- `dotnet test` (at least the listed test projects/classes).

**Done when:** freeze + grouping are covered by tests; `NeedsAssistedReview` is true iff Needs your call is non-empty; Hybrid still compiles and existing continue-with-warning tests pass.

**Changelog:** *(date when finished)*

---

## P2 — Assisted review UI, persist Skip, Guide

**Status:** TODO  
**Parallel:** SEQUENTIAL — same wizard Razor as identity/checkboxes  
**Cursor mode:** agent  
**UI skill:** yes (impeccable + web-design-guidelines)

**Read first**

- [`Pack-Import-Intended-Design.md`](Pack-Import-Intended-Design.md) — **Review UI**, **Tiers**, **Formats** copy
- P1 types on `SetupPackPreview` / reclassify API (do not re-implement freeze)
- `src/McManager.Hybrid/Components/Setup/SetupWizard.razor` (modded pack step)
- `src/McManager.Hybrid/Components/Tabs/ServerManagement/ServerManagementTab.razor` (Change pack panel)
- `src/McManager.Hybrid/ViewModels/SetupWizardViewModel.cs` (`StepIsValid` game step)
- `src/McManager.Hybrid/ViewModels/ServerManagementViewModel.cs` (`AnalyzePackPathAsync`, install gate)
- `src/McManager.Hybrid/Components/Shared/PackIdentityFields.razor`
- `src/McManager.Core/Setup/PackReplaceUx.cs` (`CanInstall`)
- `src/McManager.Hybrid/wwwroot/css/app.css` (`.mcm-pack-*`)
- `docs/Guide.md` — Modded / pack-import paragraphs only

**Do**

1. **Shared review component** (three groups): Will skip (read-only, why), Needs your call (default Keep, optional **Skip on server**, search if count ≥ `UnclearSideHighCountThreshold`), Must keep (locked, “required by B”). Show it when `NeedsAssistedReview`. Automatic packs: no three-group UI.
2. **Copy** on the review: *We skip obvious client mods. Everything else stays unless you mark it. If the server crashes and the game names one mod, you can exclude it here.* Drop-zone / help: non-experts should use a Modrinth `.mrpack` or CurseForge **Server Files**; homemade zip is the fallback. Optional one-liner if the zip looks like MultiMC/Prism (`instance.cfg` / `mmc-pack.json` — detect if cheap, skip if not): exporting a `.mrpack` from Prism is easier than reviewing dozens of unknown jars. Do not require that export.
3. **Wire Skip:** persist per SHA (`PromoteExclude`); Unskip removes the term; call P1 reclassify so groups update. Same file later → same answers.
4. **Gate Continue:** Setup `StepIsValid` and `PackReplaceUx.CanInstall` / Change pack install must **not** proceed on assisted packs until the review is on screen and freeze is not blocking. Identity complete + existing friend-pack checkboxes **remain**. Force-skip required dep: disable Next/Install and name the depender.
5. **Replace auto-keep copy.** `UnclearSideKeepCopy` / high-risk “Setup will keep them” is wrong once review exists. Update `SetupPackImport` strings, tests that assert the old warning (`Manual_zip_with_unclear_side_jars_can_continue` → assisted + `NeedsAssistedReview`, still default-keep unless Skip).
6. **`docs/Guide.md`** in this same step: automatic vs assisted, default-Keep + optional Skip, dep freeze / Must keep, `.mrpack` / Server Files for novices, crash follow-up still Layer 3 once. Do not document Pass 3.
7. Reuse `mcm-pack-*` / `mcm-help` tokens. Accessible labels, keyboard, don’t dump 60 unlabeled filenames.

**Do not**

- Change freeze rules (P1).
- Add a catalog, CF API, or Quilt Setup radio.
- Require Keep/Skip on every unknown row.
- Edit Layer 3.

**Test**

- `dotnet test` including updated SetupPackImport / PackReplaceUx tests.
- `dotnet run` Hybrid (`mcmgr-blank-test`): Setup modded pack step — clean `.mrpack` or Server Files = no review UI; homemade / jar-root with unknowns = three groups, default Keep, Next blocked only on freeze violation or incomplete identity/checkboxes; Skip persists after re-pick of the **same** file.
- Change pack panel: same review, same gates.
- Operator: if a named homemade zip is available under `data/sample-packs/`, use it; do not download kitchen-sink packs.

**Done when:** both flows show the spec review; assisted cannot skip the unknown list; Guide matches shipped UX; automatic packs are not forced through classification.

**Changelog:** *(date when finished)*

---

## After this plan

1. [`NEXT.md`](NEXT.md) → Step **8.5.2** Pass 3, **blocked** until the operator says so.
2. V1 dashboard Step **8.9** → **DONE**.
3. [`Pack-Import-Intended-Design.md`](Pack-Import-Intended-Design.md) changelog: implemented (P1–P2); keep as the contract.
4. Do **not** start Pass 3, 8.6.1, or 9.1 from P2.

---

## Plan changelog

| Date | Note |
| ---- | ---- |
| 2026-08-23 | Created. P1 NEXT (Core freeze). P2 Hybrid review + Guide. Pass 3 stays blocked. |
