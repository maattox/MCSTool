# V1 Function image (living)

**Status:** **COMPLETE** (P1–P2). Created 2026-08-27 (docs only). **Rewritten** 2026-08-27 — no GitHub Actions.  
**Parent:** [`V1-Implementation-Plan.md`](V1-Implementation-Plan.md) Phase **8.6** / Step **8.6.1** — **DONE**.  
**Why now:** Phase **8.5** exited. Remaining product path was derive OCIR username, Deploy/repair digest converge, Guide, TESTING verify that **users** copy without Docker. Step **8.4 P13** already copies a pre-built tar into OCIR. The operator pre-builds that tar with **Docker Desktop** (already done for TESTING: gitignored `artifacts/mcmgr-fn-softstop-linux-arm64.tar`).

**Cost:** $0. TESTING profile only. Never `DEFAULT` / live Forge lab. Never Minecraft `0.0.0.0/0`.  
**Functions / tofu:** do **not** `tofu apply` / `destroy` unless that section says to **ask first**. Do not SoftStop the door. Do not add paid Function memory, extra OCIR repos, or extra Function apps.  
**Hybrid config dir:** `MCMANAGER_CONFIG_DIR` = `mcmgr-blank-test` — **not** repo `data/config.local.json`.  
**Hosts / OCIDs:** do **not** paste live OCIDs, IPs, Auth Tokens, or key material into tracked docs.

**Do not start Step 9.1** (Windows installer) or GitHub Releases / public launch from this plan. **9.1** is what bundles the tar next to the `.exe`.

---

## How agents must use this file

1. Read **this protocol**, the [Progress dashboard](#progress-dashboard), [Scrutiny](#scrutiny-plan-decisions), and **only the NEXT section**.
2. Implement only that section. Do not start neighbors “while you are here.”
3. After finishing: mark **DONE**, set the next incomplete section to **NEXT**, changelog line, update V1 plan Step **8.6.1** + dashboard, update [`NEXT.md`](NEXT.md), **stop**.
4. Git: commits allowed per `git-policy`; never push/PR unless the operator asks.
5. Do **not** start Step **9.1**. Do not add GitHub Actions. Do not create a Pass 3 bug-fix plan. Do not “fix” parked **S0-01**.
6. If this plan disagrees with [`PRODUCT-IDEAS.md`](PRODUCT-IDEAS.md), **follow this plan** and note drift. Do not rewrite PRODUCT-IDEAS except the Function-image table if this chat already corrected it.
7. User-visible Setup/Deploy copy: patch [`Guide.md`](Guide.md) in **P1** (and **P2** if OCIR username form changes).

Vague notes: **decide** inside the section **using Scrutiny**. **Stop and ask** for spend, `tofu apply` / `destroy`, `DEFAULT`, or parked items.

### Context budget

This header + **one** P-section + the files listed there. Blueprint / Automated-Infrastructure: **named §§ only**. Do not load Pass 3, the full V1 plan, or PRODUCT-IDEAS unless a heading is named.

### Operator entry

New Agent chat → `/next-step`. Fresh chat per P-section.

### PARALLEL-OK

None. **P2** needs **P1** code and owns TESTING. **SEQUENTIAL.**

---

## Progress dashboard

| ID | Title | Status | Parallel | Cursor |
|----|-------|--------|----------|--------|
| P1 | OCIR username + digest converge + Guide | **DONE** | SEQUENTIAL — Core + Guide | agent |
| P2 | TESTING verify without Docker + **prove OCIR login** | **DONE** | SEQUENTIAL — owns TESTING; needs P1 | agent |

---

## What already exists (do not rediscover)

- **P13 copy path (DONE):** Setup prefers `mcmgr-fn-softstop-linux-arm64.tar` (`MCMANAGER_FUNCTION_IMAGE_TAR`, next to the app, or gitignored `artifacts/`). `OcirRegistryPusher` copies via Registry HTTP API **without a Docker daemon**. No artifact → `docker buildx linux/arm64` fallback; missing artifact+Docker → skip. Auth Token (Windows Credential Manager `McManager/ocir`) still required.
- **Artifact contract:** `FunctionImageArtifact.FileName` = `mcmgr-fn-softstop-linux-arm64.tar`. `DockerArchiveFunctionImage.Prepare` already understands docker-archive **and** OCI layout and exposes blob/manifest digests.
- **Developer pre-build (DONE for TESTING):** P12 produced the gitignored ARM tar with Docker Desktop + `buildx`. The operator **may** rebuild that way whenever `functions/shutdown_vm/` changes. Do not commit the tar. **9.1** bundles it with the installer.
- **Staging / env-rewrite:** `OcirFunctionPublisher.StageFunctionSources` copies `func.py` / `requirements.txt` / `func.yaml`, rewrites `INSTANCE_OCIDS` to env-driven, writes the FDK Python **3.12** Dockerfile. Function config (VM1 OCID, bucket, lock key) stays **tofu-owned**. `func.yaml` **0.0.12**.
- **OCIR coordinates:** `{region}.ocir.io/{namespace}/mcmgr-fn/softstop:setup`. Repo/app names stay `mcmgr-fn/softstop` / `mcmgr-fn-app`.
- **Username (P2, proven on TESTING):** Setup derives OCIR login as `{object-storage-namespace}/{identity-domain}/{IAM user name}` (`OcirUsername.Derive` + `OcirUsernameLookup`: IAM `GetUser` name + `ListDomains` DEFAULT display name). Classic (no domain listed) is `{namespace}/{IAM user name}`. `MCMANAGER_OCIR_USERNAME` is an optional override. Auth Token still required. P1’s `{namespace}/{user= OCID}` **401s**.
- **Digest converge (P1):** Deploy / repair copies when a bundled tar exists and its digest differs from the live OCIR tag (including `apply_stage` already `function` / `config_written`). A skipped push does **not** persist the Function stage.
- **TESTING fill-in (P12):** Function + Events already exist on TESTING (OCI CLI; tofu `function_image` may still be empty). Do **not** `tofu apply` a second Function/app. Synthetic invoke SoftStops **VM1 only** + lock PUT; door stays RUNNING.
- **crane / oras / GitHub Actions:** not required. C# registry push is the product copy client.

---

## Scrutiny (plan decisions)

Locked by the operator 2026-08-27. Do not reopen in an implementation chat.

| Topic | Decision |
|-------|----------|
| Who needs Docker | **Users never.** Setup copies a pre-built tar. **Developer (operator) Docker Desktop is OK** and is how the tar is produced. |
| GitHub Actions | **Out** of this plan. Do not add `.github/workflows` for the Function image. |
| P13 copy | **Keep.** Same tar name/layout. Do not invent a second copy path. |
| Live Function image | **User’s OCIR only.** Do not point the Function at public GHCR / Docker Hub. |
| Installer / Releases | **Out.** Do not start 9.1. Bundling the tar next to the `.exe` is 9.1. Do not commit the tarball. |
| Username | P1 shipped `<object-storage-namespace>/<oci-config-user>` (`~/.oci` `user=`). Drop the `MCMANAGER_OCIR_USERNAME` **requirement**; env override stays as an escape hatch. **Format is not proven.** Operator 2026-08-27: P2 must test which login works consistently (including identity-domain `Default`). Do not treat the P1 concat as locked if TESTING 401s. |
| Digest converge | Deploy / repair copies + sets `function_image` when bundled tar digest **≠** live Function image, even if `apply_stage` is `function` / `config_written`. Config-only changes (VM1 OCID, bucket, lock) stay Function **config**, no new image. |
| Docker fallback | Keep `docker buildx` for from-source **without** a tar (developer/lab). The **user** path with a bundled tar must not need Docker / `fn` / Cloud Shell. |
| TESTING P2 | Verify **copy** + digest + synthetic invoke **without Docker on that run**. Prefer `oci fn function update --image` if the Function already exists. **Ask** before `tofu apply`. **Also:** prove the OCIR login username (P1 derivation vs identity-domain `/Default/` vs Console username vs OCID). |
| S0-01 | **Parked OK** (operator 2026-08-27). Stale idle-chroma assert vs intended red `overlay-offline`. Not this plan. |
| `reconcile_usage` image | **Parked.** Same tar-copy channel later; this plan is `shutdown_vm` only. |

---

## Parked (not this plan)

| Item | Where |
|------|--------|
| GitHub Actions Function-image workflow | Operator 2026-08-27 — not needed |
| Windows installer bundling the tar | Step **9.1** |
| GitHub Releases update check | Step **9.2** |
| Pass 3 bug-fix plan / S0-01 test change | Operator skipped triage; parked OK |
| Pack-corpus `mr-fabric-cobblemon-1.7.3` re-run | Separate `/pack-test-one` chat |
| `reconcile_usage` image tar | After `shutdown_vm` channel ships |
| Extra OCIR repos / extra Function apps | Never unless operator accepts spend |
| `DEFAULT` / live Forge lab Function push | Not this plan |

---

## P1 — OCIR username + digest converge + Guide

**Status:** DONE  
**Parallel:** SEQUENTIAL — Core + Guide  
**Cursor mode:** agent

**Read first**
- This section + [Scrutiny](#scrutiny-plan-decisions)
- `src/McManager.Core/Setup/OcirFunctionPublisher.cs`
- `src/McManager.Core/Setup/SetupDeployOrchestrator.cs` (Function stage only)
- `src/McManager.Core/Setup/OciConfigProfiles.cs` (`User`)
- `src/McManager.Core/Setup/DockerArchiveFunctionImage.cs` (`PreparedFunctionImage` / digests)
- [`Guide.md`](Guide.md) Auth Token / Deploy Function paragraphs
- [`Local-Config.md`](Local-Config.md) OCIR / artifact paragraph
- `functions/shutdown_vm/README.md` (add a short developer rebuild recipe)

**Do**
1. **Username:** derive OCIR login user as `<object-storage-namespace>/<~/.oci user>` from tofu outputs + the wizard OCI profile. Remove the hard requirement on `MCMANAGER_OCIR_USERNAME`. Auth Token stays in Credential Manager. Unit-test derivation (namespace + user; reject blank).
2. **Digest converge:** on Deploy **and** Advanced → Deploy / repair, if a bundled tar exists, compare its image digest to the live Function image. If missing, different, or Function not created yet: copy into OCIR, set `function_image`, apply (or update the existing Function image). **Do this even when** `apply_stage` is already `function` / `config_written`. A skipped push must **not** mark the brake installed.
3. Config-only Function updates (VM1 OCID, bucket, lock object) remain tofu Function **config** — no new image.
4. Keep docker buildx fallback when the tar is absent (developer/lab). Product log/copy: Auth Token is required; **users** do not need Docker / `fn` / Cloud Shell when the tar is present; skipping the token skips Function+Events (budget email can still exist).
5. **Guide + Local-Config:** users copy a pre-built tar (Auth Token only). Developer may rebuild with Docker Desktop into gitignored `artifacts/`. Drop `MCMANAGER_OCIR_USERNAME` and “CI builds the image” / “no Docker on the admin PC.” Deploy/repair updates the image when the bundled digest changes. Short paragraphs — do not rewrite the Guide.
6. **Developer recipe:** a short `functions/shutdown_vm/README.md` section: `buildx linux/arm64` + env-rewrite (same as `StageFunctionSources`) + `docker save` to `artifacts/mcmgr-fn-softstop-linux-arm64.tar`. Do not commit the tar. Do not add GitHub Actions.
7. Do **not** `tofu apply` on TESTING from this section. Do **not** start 9.1.

**Test**
- `dotnet test` for username derivation + “stage already function but digest differs → still copy” (orchestrator/unit; fake digest/live image). Do not treat parked S0-01 as in-scope.
- Dry-run still skips real push.

**Done when:** Product Setup/repair copies without `MCMANAGER_OCIR_USERNAME` and without Docker when the tar is present, and converges digest. Guide + developer recipe match. **NEXT = P2**.

**Changelog:** 2026-08-27 — Derived OCIR username from Object Storage namespace + `~/.oci` `user=`; `MCMANAGER_OCIR_USERNAME` is an optional override. Deploy / repair copies when the bundled tar digest differs even if `apply_stage` is already `function` / `config_written`; skipped push no longer marks Function complete. Guide + Local-Config + developer rebuild recipe. **NEXT = P2**.

---

## P2 — TESTING verify without Docker (user copy path)

**Status:** DONE  
**Parallel:** SEQUENTIAL — owns TESTING; needs P1  
**Cursor mode:** agent

**Read first**
- This section + [Scrutiny](#scrutiny-plan-decisions) + [OCIR login — P2 must prove](#ocir-login--p2-must-prove)
- `src/McManager.Core/Setup/OcirUsername.cs` (what P1 concatenates)
- `src/McManager.Core/Setup/OcirFunctionPublisher.cs` (Resolve + copy)
- [`Agent-Deploy-Pitfalls.md`](Agent-Deploy-Pitfalls.md) (before any SSH)
- [`OCI-API-Usage.md`](OCI-API-Usage.md) (429 / waiters)
- Pass 3 S2-16 / S2-17 notes in [`V1-QA-Pass-3-Results.md`](V1-QA-Pass-3-Results.md) (Function already exists; do not duplicate)

**Do**
1. **OCIR login first** (do not skip). P1’s derived username is **unverified**. Investigate and **test which login works consistently** on TESTING before treating copy as green. Details in [OCIR login — P2 must prove](#ocir-login--p2-must-prove). If the working form ≠ P1 concat, **fix Core + unit tests + Guide / Local-Config** in this same P2 chat, then continue the copy path. `MCMANAGER_OCIR_USERNAME` may be used only as a temporary probe; the product path after P2 must not require it.
2. Use the existing gitignored ARM tar (P12) where `FunctionImageArtifact` looks. Confirm **Docker is not used for the copy** (`docker` unused in the Setup/repair log). The operator’s Docker Desktop may stay installed; it must not be on the copy path.
3. Auth Token + API key only (TESTING `mcmgr-blank-test`). No `MCMANAGER_OCIR_USERNAME` **requirement** once the derived login is proven.
4. Copy into existing TESTING OCIR `mcmgr-fn/softstop:setup`. If the Function already exists, update its image (CLI or product repair). **Ask** before `tofu apply`. Do not create a second app/repo. Do not SoftStop the door.
5. Confirm live image digest matches the bundled tar. Synthetic ACTUAL: VM1 SoftStop + lock PUT; door **RUNNING**. DELETE lock. Restore idle if VM1 was started.
6. Missing-token skip: record that the deploy log is explicit and VMs are untouched (do not wipe the operator’s stored token without asking).
7. Do **not** start 9.1. Do not fire a real $1 budget alert.

**Test**
- **Login:** at least two successful Registry authentications (or Setup copy attempts) with the **same** chosen username form; 401 on the rejected forms recorded in the P2 changelog (no secrets). One lucky 200 is not enough.
- Copy log: pre-built tar, no Docker on the copy path. Digest matches. Invoke: VM1 only + lock; door up. Idle re-enabled if this chat started VM1.

**Done when:** TESTING user path works without Docker / `fn` / Cloud Shell / `MCMANAGER_OCIR_USERNAME`, **and** the OCIR login form is the one that works consistently (code + Guide match). Plan **COMPLETE**. V1 Step **8.6.1** **DONE**. **Do not** point NEXT at **9.1** unless the operator asks.

**Changelog:** 2026-08-27 — TESTING login: `{ns}/{user OCID}` and `{ns}/Default/{user OCID}` **401** twice; `{ns}/oracleidentitycloudservice/{Console username}` **401** twice. `{ns}/Default/{Console username}` **200** twice (product form). Two-part `{ns}/{Console username}` also **200** twice (classic fallback). Core now `GetUser` + `ListDomains` (DEFAULT type); env override only. Product copy: pre-built tar, no Docker, digest matched live `mcmgr-fn/softstop:setup` (copy skipped). Did not `tofu apply`. Synthetic ACTUAL: `SUCCESS` SoftStop **VM1 only** + lock PUT; door **RUNNING**; DELETE lock. Idle left **15+on** on-disk before SoftStop. Missing-token skip left as the explicit Credential Manager message (token not wiped). Plan **COMPLETE**. Do not start 9.1.

---

## OCIR login — P2 must prove

Operator 2026-08-27 (after P1 shipped). **Investigate on TESTING in P2.** Do not assume P1 is correct. Do not paste live OCIDs, usernames, Auth Tokens, or `MCMANAGER_OCIR_USERNAME` values into tracked docs.

### What P1 actually sends

`OcirUsername.Derive` concatenates:

```text
{object-storage-namespace}/{value of user= in ~/.oci/config for the wizard profile}
```

That `user=` field is a **user OCID** (`ocid1.user.oc1..…`), not the Console display name. `OcirFunctionPublisher` uses `OcirUsername.Resolve` (env override first, else Derive). Auth Token stays in Credential Manager (`McManager/ocir`).

### Why that may 401

Oracle Container Registry docker-login / Registry HTTP Basic user is **not** documented as `{namespace}/{user OCID}`. Typical forms:

| Tenancy style | Username |
|---------------|----------|
| Classic IAM (no identity domains) | `{tenancy-namespace}/{username}` |
| **Identity domains** (current default; **this product’s tenancies**) | `{tenancy-namespace}/{domain}/{username}` |
| Old IDCS federation | `{tenancy-namespace}/oracleidentitycloudservice/{username}` |

The Object Storage namespace **is** the OCIR tenancy namespace (that half of P1 is fine).

**Operator memory (manual login that worked):** `{Object Storage namespace}/Default/{user}` where **`Default` is the identity domain**, not a compartment. New OCI tenancies get a domain named `Default`. This repo already treats TESTING / the 3.3 blank tenancy as identity-domain (door DG note; Resource Manager dumps of `identity_domains`).

**Unknown:** whether the third segment is the Console **username**, an **email**, or the **user OCID**. `{namespace}/Default/{ocid}` may or may not work. Prove it; do not guess.

### How to test (TESTING only)

Stay **$0**. Profile **TESTING**. Hybrid `MCMANAGER_CONFIG_DIR` = `mcmgr-blank-test`. Do not SoftStop the door. Do not log the Auth Token.

1. Read TESTING Object Storage namespace from tofu outputs / gitignored config (do not copy it into this plan).
2. Read `user=` from the TESTING `~/.oci` profile (OCID). Note the Console username for that user **without** writing it into git.
3. Against existing OCIR `mcmgr-fn/softstop` (Registry `GET /v2/` or product copy — same credentials Setup uses), try these **in order**, Auth Token unchanged, until one form authenticates **twice in a row**:
   1. P1: `{namespace}/{user OCID}`
   2. Operator: `{namespace}/Default/{user OCID}`
   3. `{namespace}/Default/{Console username}` (and email if that is what the Console shows)
   4. Two-part `{namespace}/{Console username}` (no domain)
   5. Only if still 401: `{namespace}/oracleidentitycloudservice/{username}`
4. Rejected forms: 401 / OCIR token failure in the Setup or probe log. Do not treat a single success as the product form.
5. If the winner ≠ P1 concat: change `OcirUsername` (and tests) to that form; keep `MCMANAGER_OCIR_USERNAME` as override only; patch Guide / Local-Config in the same P2 session (short). If identity-domain name is not always `Default`, resolve it (Identity API / config) rather than hard-coding unless TESTING + the operator confirm Default is the v1 product rule.
6. Then continue P2 copy / digest / synthetic invoke **without** leaving the env override set as the real product path.

---

## After this plan

When **P1–P2** are **DONE**:

- Mark V1 Step **8.6.1** **DONE**; Phase **8.6** **DONE**.
- [`NEXT.md`](NEXT.md) → **blocked** on operator (Phase **9** / Step **9.1**) — do **not** auto-start the installer.
- Guide / Local-Config / VM-Software Function-image sentences should already match P1/P2.
- TESTING Function blanket in V1 can stay: agents may still `fn`/`docker` on TESTING; the **user** path is tar + copy.

---

## Plan changelog

| Date | Note |
|------|------|
| 2026-08-27 | **P2 DONE / plan COMPLETE.** OCIR login proven; Core uses IAM user name + identity domain (not user OCID). TESTING copy without Docker; digest match; synthetic ACTUAL VM1 only. **NEXT blocked** on operator (Phase **9** / **9.1**). Do not start 9.1. |
| 2026-08-27 | **P2 note (docs only).** Operator: P1 `{namespace}/{user=}` may 401 on identity-domain tenancies; last manual login was `{namespace}/Default/{user}`. P2 must prove which OCIR login works consistently and fix derivation if needed. Do not start 9.1. |
| 2026-08-27 | **P1 DONE.** Derived OCIR username; digest converge on Deploy/repair; Guide + developer recipe. Living **NEXT = P2**. Do not start 9.1. |
| 2026-08-27 | **Rewritten.** Developer Docker Desktop pre-build is OK; **users** must not need Docker. Dropped GitHub Actions (old P1). Living **NEXT = P1** (username + digest + Guide). **P2** = TESTING user-copy verify. Do not start 9.1. |
| 2026-08-27 | **Created** (docs only). Phase **8.5** closed. Original P1 was GitHub Actions (superseded the same day). |
