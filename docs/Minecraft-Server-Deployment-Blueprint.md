# Minecraft server deployment blueprint

**Status:** Living design authority for **how the Minecraft server itself gets installed, launched, upgraded, and backed up on VM1** — for Vanilla (MVP), Optimized Vanilla, and Modded (v1+). Researched and written 2026-08-11 against current (2026) upstream APIs; re-verify version-specific facts (Java majors, endpoint URLs, sunset dates) before relying on them in far-future work.

**Scope:** This document is the **single source of truth** for the on-box "game layer" contract described only briefly in lab [`PRODUCT-IDEAS.md`](../../OCI-mc-server-manager/PRODUCT-IDEAS.md#vanilla-server-bootstrap-mvp) and [`PRODUCT-IDEAS.md`](../../OCI-mc-server-manager/PRODUCT-IDEAS.md#setup-game-types-v1). Where this document and PRODUCT-IDEAS disagree on **staging** (MVP vs v1 vs later), PRODUCT-IDEAS wins and this document should be corrected. Where they disagree on **mechanism** (exact APIs, schema field names, directory layout), this document is authoritative and PRODUCT-IDEAS should be updated to link here instead of re-describing details.

**Audience:** developers and coding agents implementing Setup/OpenTofu, SSH bootstrap scripts, the Manager (`OCI-mc-server` / `McManager.Core`), and the on-box idle agent (`vm_agent/` in this repo).

**Non-goals:** this document does not decide Manager UI copy/flow (see PRODUCT-IDEAS "Setup game types (v1)"), does not re-litigate budget/idle/door design (see `Infrastructure-Information.md`, `Contracts-Object-Storage.md`), and does not implement anything beyond MVP Vanilla by itself — it is written so v1/later work does not have to re-research or redesign the contract.

---

## Table of contents

**Part A — Architecture (applies to all game types, MVP builds only the Vanilla instance of it)**

1. [Executive summary](#1-executive-summary)
2. [Architectural goal — the game manifest contract](#2-architectural-goal--the-game-manifest-contract)
3. [Game manifest schema — full field reference](#3-game-manifest-schema--full-field-reference)
4. [Concrete fixtures](#4-concrete-fixtures)
5. [Directory layout, users, ownership, permissions](#5-directory-layout-users-ownership-permissions)
6. [systemd lifecycle and launch command generation](#6-systemd-lifecycle-and-launch-command-generation)
7. [server.properties and EULA handling](#7-serverproperties-and-eula-handling)
8. [RCON configuration and secret handling](#8-rcon-configuration-and-secret-handling)
9. [Java version mapping and ARM64 packages](#9-java-version-mapping-and-arm64-packages)
10. [Idle agent integration](#10-idle-agent-integration)
11. [World import/replacement and backup compatibility](#11-world-importreplacement-and-backup-compatibility)
12. [First boot, upgrades, downgrades, rollback](#12-first-boot-upgrades-downgrades-rollback)
13. [Bootstrap responsibilities: OpenTofu/user-data vs SSH scripts vs Manager](#13-bootstrap-responsibilities-opentofuuser-data-vs-ssh-scripts-vs-manager)
14. [Failure recovery and resumability](#14-failure-recovery-and-resumability)
15. [Offline fixtures and tests for version metadata](#15-offline-fixtures-and-tests-for-version-metadata)

**Part B — Per-platform artifact acquisition (Vanilla is MVP; the rest is v1/later research, captured now)**

16. [Vanilla (Mojang piston-meta)](#16-vanilla-mojang-piston-meta)
17. [Paper / Optimized Vanilla (Fill v3)](#17-paper--optimized-vanilla-fill-v3)
18. [Fabric](#18-fabric)
19. [NeoForge](#19-neoforge)
20. [Forge, including legacy versions](#20-forge-including-legacy-versions)
21. [Quilt](#21-quilt)
22. [Modrinth modpacks and API](#22-modrinth-modpacks-and-api)
23. [CurseForge modpacks, API, and licensing](#23-curseforge-modpacks-api-and-licensing)
24. [Manual server-pack upload/import](#24-manual-server-pack-uploadimport)
25. [Required client-pack communication](#25-required-client-pack-communication)

**Part C — Cross-cutting v1+ concerns**

26. [Java / Minecraft / loader compatibility matrix](#26-java--minecraft--loader-compatibility-matrix)
27. [ARM64 / native-mod risk](#27-arm64--native-mod-risk)
28. [Update and migration behavior](#28-update-and-migration-behavior)
29. [Future game-platform matrix — classification](#29-future-game-platform-matrix--classification)
30. [Implementation roadmap / cross-references](#30-implementation-roadmap--cross-references)
31. [Reference links](#31-reference-links)
32. [Changelog](#32-changelog)

---

# Part A — Architecture

## 1. Executive summary

The product will eventually offer three "server type" experiences (per `PRODUCT-IDEAS.md`):

| Server type | What it means technically | Stage |
|---|---|---|
| **Vanilla** | Official Mojang `server.jar`, no loader | **MVP** |
| **Optimized Vanilla** | Paper (Bukkit/Spigot-API-compatible server implementation), no mod loader, optional plugins later | **v1** |
| **Modded** | A mod loader (Fabric / NeoForge / Forge / Quilt) plus a set of mod jars, usually sourced from a Modrinth `.mrpack` or CurseForge modpack, or a manually uploaded server pack | **v1** for Setup-driven install **and** Server Management inspect + re-download of the imported pack; **change/replace pack** (light swap vs full re-setup) is **later** |

The **mistake to avoid** is building the MVP Vanilla bootstrap as if "the server is a jar file downloaded from one URL and started with `java -jar server.jar`" — because that assumption breaks immediately for Paper (still one jar, but a different API and checksum algorithm), breaks harder for Fabric (a "launcher" jar that itself launches game code from Maven-hosted libraries), and breaks completely for Forge/NeoForge 1.17+ (there is no single server jar at all — the "launch command" is `java @user_jvm_args.txt @libraries/.../unix_args.txt`, a two-file **argument-file** invocation against a `libraries/` tree assembled by a one-time **installer** run).

This document's central recommendation is therefore: **model "how do I start this server" and "what did I install" as data (the game manifest), not as code that assumes a single jar.** Every on-box script, systemd unit, and Manager upgrade flow should read the manifest rather than special-case "vanilla" as the only shape. MVP still only ever *writes* a Vanilla manifest — but the schema, directory layout, and systemd unit generator are built generically from day one so v1 does not require a rewrite.

Key research findings that drove the design below:

- Mojang's version manifest (`piston-meta`) is stable, versioned (`v2`), and gives a **SHA-1** per server jar. Still current in 2026; `launchermeta.mojang.com` still redirects but `piston-meta.mojang.com` is canonical.
- PaperMC's **Fill v2 API (`api.papermc.io`) is fully sunset as of 2026-07-01** — it is **dead**, not just deprecated. Any new code must use **Fill v3** (`fill.papermc.io`), which uses **SHA-256** (not SHA-1), requires a descriptive `User-Agent` header, and now publishes per-version `minimumJavaVersion`, `recommendedJvmFlags`, and a support-status field directly in the API — useful signal our own manifest should carry through.
- Fabric's server "jar" is a **fat launcher jar** built on demand by `meta.fabricmc.net`, keyed by **three** versions (game, loader, installer) — omitting the installer version segment is a common integration bug.
- **NeoForge has no JSON version manifest at all.** The only authoritative version source is Maven `maven-metadata.xml`. NeoForge's real, supported floor is **Minecraft 1.20.2** (1.20.1 builds exist but are explicitly deprecated/removed by the NeoForge team, who recommend Forge for 1.20.1).
- **Forge** publishes a small, scrapable `promotions_slim.json` (latest/recommended per Minecraft version, all the way back to 1.1) — good for "known-good" version pinning — but the **installer/launch mechanics changed at 1.17** (single jar → installer + argfiles) and again the exact argfile path is versioned into the path string (`libraries/net/minecraftforge/forge/<mc>-<forge>/unix_args.txt`), so it cannot be hard-coded.
- **Quilt remains alive but niche** in 2026: it retired its own standard-library ecosystem (QSL/QFAPI) in favor of just being "a Fabric-compatible loader with different governance," cannot load Forge/NeoForge mods, and has no first-party modpack format of its own — packs targeting Quilt are almost always Fabric mrpacks with a `quilt-loader` dependency instead of `fabric-loader`.
- **Modrinth's `.mrpack`** is an open, well-specified ZIP+JSON format with explicit `env.server` per-file markers (`required`/`optional`/`unsupported`) — this is the best-behaved of the pack formats for **automatic server-side install**, because the format itself tells you which files are server-eligible.
- **CurseForge modpacks are the most legally and technically awkward** source: as of 2026 the CDN **requires an API key** for automated downloads (optional window closed mid-2026), the 3rd-party API Terms of Service forbid caching/redistributing API data and forbid building a "competing" product, and CurseForge manifests do **not** mark files client/server the way `.mrpack` does — server-side detection needs a maintained "known client-only mod" heuristic list (itzg's `mc-image-helper` is the best public reference for this).
- **This product will never let a user browse/search/pick a modpack from within the app** — modpacks are always built/selected on Modrinth/CurseForge/FTB's own site or launcher, exported there, and then imported into Setup as a complete file (file picker/drag-and-drop only). This is a firm product decision (§2.4), not a v1-only limitation, and it reframes how to read the Modrinth/CurseForge "API" research below: those APIs are used only to resolve files an already-uploaded pack references, never to power a picker.
- **ARM64 (Ampere A1) has no fundamental blocker for the dedicated server** because the server process never loads LWJGL (that is a client rendering library). The real ARM64 risk for modded servers is **mods that bundle their own native code** (voice chat plugins, some database/JNI mods, occasionally physics/audio libraries) — these fail with `UnsatisfiedLinkError`/glibc-version errors, not silently, but the failure is per-mod and must be surfaced clearly rather than discovered by an admin at 2am.

---

## 2. Architectural goal — the game manifest contract

### 2.1 Problem statement

MVP only needs to install a Mojang `server.jar`. But every future on-box actor — the idle agent (`vm_agent/`), the systemd unit, Setup's upgrade flow, and the Manager's "what is installed" display — needs to answer the same handful of questions regardless of what got installed:

- What Minecraft version is this?
- Is there a mod loader, and if so which one and which version?
- What Java major version does this need?
- What file(s) did we download, and how do we verify them?
- What is the exact command to start the process?
- Where is the world?

**Decision: represent these as a single structured document — the *game manifest* — written once by bootstrap and read by everything else.** This is the concrete implementation of the "future-neutral deployment model" the operator asked for; it is not a new abstract idea layered on top of a Vanilla-only implementation, it *is* the Vanilla implementation, generalized enough that adding Paper/Fabric/NeoForge later is a new **installer module** plus new **manifest values**, not a rewrite of systemd/idle-agent/Manager code.

### 2.2 Where the manifest lives (two copies, one authoritative)

| Copy | Path / object | Role | Written by | Read by |
|---|---|---|---|---|
| **On-box canonical** | `/etc/mcmgr/game-manifest.json` on VM1 | **Authoritative.** Full schema (Part A §3). Root-owned, `0640`, group `mcmgr` (see §5). | Bootstrap script (first install), upgrade/rollback script (subsequent changes) | systemd unit generator, idle agent (`world_path`, `minecraft_unit`), Manager over SSH (read), any future in-VM helper |
| **Object Storage summary** | `meta/infra.json` → `game` object (see [`Contracts-Object-Storage.md`](Contracts-Object-Storage.md#metainfrajson--canonical-infrastructure-metadata-v2)) | **Mirror, not authoritative.** A reduced projection for Connect-existing / offline display. Never contains download URLs, only identifying fields. | Setup / Manager, copying from the on-box manifest after a successful install | Manager (Connect existing, status display) |

This two-tier design deliberately avoids two failure modes: (a) putting the full manifest in Object Storage would mean re-freezing `infra_schema` every time a new loader ships fields (loader churn is frequent; infra schema churn should not be), and (b) relying on Object Storage as the *only* copy would mean the VM cannot even show what game it is running while offline/mid-boot before Object Storage is reachable.

The **on-box manifest is versioned independently** (`schema_version`) from `infra_schema` in `meta/infra.json`. A manifest schema bump does not require an infra schema bump unless the *summary* fields mirrored into `meta/infra.json` also change shape.

### 2.3 The seven "future-neutral" fields, as actually implemented

The operator's prompt listed a provisional field set. Research (Part B) confirmed it maps directly onto real upstream concepts, with two additions (`distribution` needed a `plugins`-vs-`mods` sibling concept for later, and every platform needed a *pack provenance* block once modpacks entered the picture). The final field set is in §3. Summary of the mapping to the original prompt:

| Prompt field | Kept as-is? | Notes |
|---|---|---|
| `game_type` | Yes | Always `"minecraft"` today; exists so the schema is not Minecraft-specific if this product ever hosts another game (not planned — just future-proofing the field name itself). |
| `minecraft_version` | Yes | Mojang version id (`"1.21.11"`, or `"26.2"` under Mojang's newer date-influenced numbering — see §16.4). |
| `distribution` | Yes | `"vanilla"` \| `"paper"` \| `"modded"`. Governs which installer module ran. |
| `loader` | Yes | `null` for vanilla/paper; `"fabric"` \| `"neoforge"` \| `"forge"` \| `"quilt"` for modded. |
| `loader_version` | Yes | e.g. `"0.17.2"` (Fabric), `"21.1.98"` (NeoForge), `"47.4.10"` (Forge). |
| `java_major` | Yes | Integer, e.g. `21`. Always resolved and pinned at install time — never "figure it out again" at every boot. |
| `server_artifact` | Expanded to an object | A single string couldn't capture "single jar" vs "installer jar" vs "launcher jar" vs "argfile-driven install tree" — see §3.4. |
| `artifact_hash` | Expanded to an object | Different platforms use different algorithms (SHA-1 vs SHA-256) and some (NeoForge) do not publish a hash for the *installer* at all — see §3.5. |
| `launch_command` | Expanded to an object | Needs `executable`, `args[]`, and `working_directory` because Forge/NeoForge launch via `@argfile` tokens that are themselves shell-like but must be passed as literal argv entries, not shell-expanded — see §3.6. |
| `world_path` | Yes | Absolute path. Already exists as a config key in `vm_agent/config.json` today — this manifest becomes its authoritative source once Setup exists. |

Additional fields not in the original prompt but required once modpacks are in scope: `modpack` (provenance: source marketplace, project/version id, pack name, required-on-client flag), `install` (audit trail: when/how installed, bootstrap tool version), and `plugins`/`mods` directory conventions (§5).

### 2.4 No in-app mod/modpack catalog — architecture decision

**This idea is rejected and will not be implemented.** It is not a v1 deferral and not an after-v1 item. The Manager/Setup wizard **must not** implement a Modrinth/CurseForge/FTB browse, search, "trending packs," "download this pack," or "type a pack name/URL/ID" UI. Users **create or download their own pack files** on those platforms (or another tool), then **select that local file** in Setup or Manager. This governs pack *selection* UX, and has one load-bearing consequence for how Part B's per-platform sections must be read and implemented:

**The only supported pack-input mechanism, for every source, is: the user selects/builds the modpack on that platform's own website or launcher, exports it there as a complete pack archive, and imports that already-complete file into Setup via a local file picker and/or drag-and-drop.** Setup never accepts a Modrinth project slug, a CurseForge pack/project ID, a pack name typed into a search box, or a pasted marketplace URL as a way to *choose* a pack. The wizard's modpack step has exactly one input widget: "choose a file" (or drop one).

**What this means for the Modrinth/CurseForge API usage described later in this document (§22–§23) — read carefully, this is a common point of confusion:** an uploaded `.mrpack` or CurseForge export archive frequently does **not** embed every mod's actual bytes inside the archive itself — Modrinth's `.mrpack` embeds only a JSON index plus (optionally) config/override files, with each mod referenced by a CDN **download URL already present in that index**; a CurseForge pack export's `manifest.json` commonly references mods by **project ID + file ID** rather than embedding the jars, because many CurseForge mod authors' distribution permissions require CurseForge's own client/API to serve the actual file bytes. **Fetching those already-referenced files after the user has uploaded the pack is not "browsing a catalog" — it is finishing the import of a pack the user already fully selected elsewhere**, exactly analogous to how downloading `downloads.server.url` after resolving a Mojang version id is not "browsing Mojang's game catalog." The architectural line is specifically: **no product UI surface lets the user discover, search, compare, or pick a pack by name/ID from within this app.** Whatever HTTP calls an installer module makes to Modrinth's CDN or the CurseForge API afterward are artifact-resolution plumbing for a pack the user already has in hand, not a violation of this decision — but that plumbing must never be exposed as, or grow into, a picker.

**Rationale:**

1. **Product scope discipline.** This product's job is hosting/managing a Minecraft server, not competing with Modrinth/CurseForge as a discovery/marketplace surface. Modrinth and CurseForge already do pack discovery well; duplicating it inside a Setup wizard is scope creep with no clear user benefit over "go pick your pack on the site you already know."
2. **Legal/ToS alignment (a direct, useful side effect, not the primary reason):** CurseForge's 3rd-party API Terms explicitly forbid using the API to "build any product or service that competes, directly or indirectly, with CF, CurseForge for Studios, or the Platform" (§23.2). A browse/search feature inside this product would sit uncomfortably close to that line; file-import-only avoids the question entirely, because the API is only ever invoked to resolve files for a pack the user already selected on CurseForge's own platform.
3. **Reduced maintenance surface.** A browse UI would need to track two marketplaces' search/listing APIs, pagination, images/thumbnails, category taxonomies, and rate limits indefinitely. File-import only needs to parse a small number of well-defined **archive/manifest formats** (§22–§24), which change far less often than a marketplace's browse API surface.
4. **No CurseForge API key in v1.** CurseForge **Server Files** / filled zips import through the manual adapter (§24) with no API. Resolving client-export `projectID`/`fileID` lists via a product API key is **deferred** (§23.4) — ToS key custody, not a catalog question.

**Consequence for the manifest schema (§3.7):** `modpack.source` values `"modrinth"` / `"curseforge"` describe **where the imported file's own embedded metadata says it came from**, populated by parsing the uploaded archive — never a record of "the user searched/picked this in our UI," because that UI does not exist. `modpack.project_id`/`version_id` are populated from the archive's own manifest (`modrinth.index.json`'s implicit provenance, or CurseForge's `manifest.json` project/file references), not from a selection made inside this product.

---

## 3. Game manifest schema — full field reference

**File:** `/etc/mcmgr/game-manifest.json`. **Encoding:** UTF-8 JSON, trailing newline. **Ownership:** `root:mcmgr`, mode `0640` (contains no secrets, but there is no reason to make it world-readable). **Compatibility rule:** additive optional fields are always safe; any field whose *meaning* changes must bump `schema_version` — same discipline as `Contracts-Object-Storage.md` uses for Object Storage documents.

```text
{
  schema_version:      integer                — manifest shape version, currently 1
  game_type:            "minecraft"            — reserved constant
  distribution:         "vanilla" | "paper" | "modded"
  minecraft_version:    string                 — Mojang version id, e.g. "1.21.11"
  loader:               string | null          — "fabric" | "neoforge" | "forge" | "quilt" | null
  loader_version:       string | null
  java_major:           integer                — resolved/pinned Java major version
  java:                 object                 — resolved Java runtime identity (see 3.3)
  server_artifact:      object                 — what got downloaded / how it launches (see 3.4)
  artifact_hash:        object | array | null  — integrity record(s) (see 3.5)
  launch_command:       object                 — how to start the process (see 3.6)
  world_path:           string                 — absolute path to the world save directory
  server_dir:           string                 — absolute path to the server's working/install directory
  minecraft_unit:       string                 — systemd unit name, e.g. "minecraft"
  server_properties_managed_keys: array<string> — keys Setup/Manager may rewrite on upgrade (see 7.3)
  eula:                 object                 — EULA acceptance record (see 7.2)
  rcon:                 object                 — non-secret RCON facts only (see 8)
  modpack:               object | null          — pack provenance when distribution == "modded" (see 3.7)
  install:               object                 — audit trail (see 3.8)
  previous:              object | null          — snapshot of the manifest before the last upgrade, for rollback (see 12.4)
}
```

### 3.1 `schema_version`

Integer, starts at `1`. Bump only for a breaking change to this document's contract (field removed, meaning changed, required field added without a safe default). Adding a new *optional* field with a documented default does not require a bump.

### 3.2 `game_type` / `distribution` / `loader` / `loader_version` / `minecraft_version`

Plain strings as described in §2.3. `loader` and `loader_version` are both `null` together or both set together — never one without the other. `distribution == "paper"` implies `loader == null` (Paper is a server *implementation*, not a mod loader — see §17); a future curated "Fabric performance preset" (mentioned as a possible Optimized-Vanilla alternative in PRODUCT-IDEAS) would instead be `distribution == "modded"`, `loader == "fabric"`, with a fixed, product-curated mod list rather than a user-supplied pack — this manifest schema already supports that without changes.

### 3.3 `java` object

```text
java: {
  major:         integer         — same as top-level java_major (kept here too for locality)
  vendor:        string          — "temurin" for MVP/v1 (Eclipse Adoptium); reserved for "corretto" etc. later
  package_type:  "jre" | "jdk"   — servers only need a JRE; JDK is not required
  install_path:  string          — e.g. "/usr/lib/jvm/temurin-21-jre-arm64" or the distro package's resolved path
  source:        "distro_package" | "adoptium_api_archive"
  resolved_at:   UTC timestamp
}
```

Rationale for keeping both a top-level `java_major` and a nested `java` object: `java_major` is the field every consumer actually needs for compatibility checks (matches the operator's original prompt exactly); the nested object is bootstrap/upgrade bookkeeping (so a future re-run of bootstrap can detect "Java 21 is already installed the same way we'd install it" instead of blindly re-installing).

### 3.4 `server_artifact` object

Different platforms produce fundamentally different on-disk shapes. Rather than force them into one string, the manifest records a `kind` discriminator plus the fields relevant to that kind:

```text
server_artifact: {
  kind:  "single_jar" | "installer_jar" | "launcher_jar" | "argfile_tree"
  # single_jar        — Vanilla, Paper: one jar you run directly with `java -jar`.
  # installer_jar      — Forge <1.17, NeoForge, Forge >=1.17 (before argfiles exist):
  #                       an installer jar is run once with --installServer and produces
  #                       either a runnable jar (legacy) or an argfile_tree (modern).
  # launcher_jar       — Fabric, Quilt: a small "launcher" jar downloaded pre-built for the
  #                       exact (game, loader, installer) version triple; classpath'd against
  #                       Maven-hosted libraries fetched by the launcher itself on first run.
  # argfile_tree       — Forge >=1.17, NeoForge: no single runnable jar; launch is
  #                       `java @user_jvm_args.txt @libraries/.../unix_args.txt`.

  filename:            string | null   — primary downloaded file name, when kind has one
  download_url:        string | null   — exact URL used (for audit / re-download on repair)
  installer_filename:  string | null   — set when kind == installer_jar or argfile_tree
  installer_download_url: string | null
  unix_args_path:       string | null   — relative path under server_dir, when kind == argfile_tree
                                          e.g. "libraries/net/neoforged/neoforge/21.1.98/unix_args.txt"
}
```

### 3.5 `artifact_hash`

```text
artifact_hash: {
  algorithm: "sha1" | "sha256" | "none_published"
  value:     string | null
  verified_at: UTC timestamp | null
}
```

Or, when more than one file needs a recorded hash (e.g. a modded install with both an installer jar and, if published, a resulting server jar), this field is an **array** of the same shape with an added `subject` field (`"installer"` / `"server_jar"` / `"pack_index"`). Consumers must accept either a single object or an array and treat a single object as a one-element array. `"none_published"` is a legitimate value (e.g. NeoForge's installer jar has no first-party published checksum — see §19) — bootstrap must not fabricate a hash or silently skip verification without recording that fact here.

### 3.6 `launch_command`

```text
launch_command: {
  working_directory: string          — absolute path, == server_dir
  executable:         string          — normally "java", but always the absolute
                                        interpreter path actually resolved at install time,
                                        e.g. "/usr/lib/jvm/temurin-21-jre-arm64/bin/java"
  args:                array<string>   — literal argv entries, IN ORDER, no shell expansion.
                                        "@file" tokens are literal argv strings understood by
                                        the `java` launcher itself, not by a shell.
  jvm_memory_args_source: "user_jvm_args_file" | "launch_args" | "none"
                                        — where -Xms/-Xmx live, so Setup/Manager knows where
                                        to edit memory settings later without re-deriving args
}
```

**Critical implementation note (confirmed by research, §20):** for `argfile_tree` installs, `args` must be passed to systemd's `ExecStart=` as **separate ordered tokens**, e.g. `ExecStart=/usr/.../bin/java @user_jvm_args.txt @libraries/net/neoforged/neoforge/21.1.98/unix_args.txt --nogui`. Do **not** build this as a shell one-liner and hand it to `bash -c` — systemd's own argv splitting on a plain (non-`bash -c`) `ExecStart=` line handles `@file` tokens correctly as literal strings; wrapping in a shell adds a needless failure surface (quoting, `$@` expansion) that upstream's own `run.sh` examples rely on but that a generated unit file does not need.

### 3.7 `modpack` (null unless `distribution == "modded"` and a pack, not a manual mod list, was installed)

**Provenance note (see §2.4):** every non-`product_curated` value of this object is populated by **parsing a file the user already uploaded**, never by an in-app selection — there is no Setup UI where a user "picks" a Modrinth/CurseForge pack by browsing; they always import an already-exported archive, and `source`/`project_id`/`version_id` below simply record what that archive's own embedded metadata says about itself.

```text
modpack: {
  source:              "modrinth" | "curseforge" | "manual_upload" | "product_curated"
  project_id:          string | null   — Modrinth project id / CurseForge modId
  version_id:          string | null   — Modrinth version id / CurseForge fileId
  pack_name:           string
  pack_version_label:  string | null   — human display version, e.g. "1.4.2"
  client_pack_required: boolean         — true unless every installed mod is server_only-safe
  excluded_client_only_files: array<string> — file paths present in the pack but NOT installed
                                              server-side because a pack declaration (Modrinth
                                              env.server) or a known override list (§24.3 Layer 1/2)
                                              said so BEFORE install was attempted
  quarantined_files:    array<object> | []  — files removed AFTER an install/upgrade crash-looped
                                              and the loader attributed the crash to them (§24.3
                                              Layer 3); distinct from excluded_client_only_files
                                              because these are provisional and must be surfaced
                                              for operator confirmation, not silently permanent.
                                              Each entry: { path, reason, detected_at,
                                              retry_succeeded, operator_acknowledged }
  imported_at:          UTC timestamp
}
```

### 3.8 `install` (audit trail; always present)

```text
install: {
  installed_at:        UTC timestamp
  installed_by:         "setup_wizard" | "manager_upgrade" | "manager_rollback" | "manual_ssh"
  bootstrap_tool_version: string   — version string of the bootstrap script/tool that ran, e.g. "mcmgr-bootstrap/0.3.0"
  os_arch:               "aarch64"
}
```

### 3.9 `eula`, `rcon`, `server_properties_managed_keys`

Covered in full in §7 and §8; summarized here for schema completeness:

```text
eula: {
  accepted: boolean,
  accepted_at: UTC timestamp | null,
  accepted_version_context: string   — the minecraft_version the acceptance was recorded against
}

rcon: {
  enabled: boolean,
  port: integer,            # default 25575
  bind_address: "127.0.0.1" # never anything else — see 8.1
  password_secret_ref: string   # e.g. "file:/etc/mcmgr/rcon.secret" — NEVER the password itself
}

server_properties_managed_keys: ["motd", "difficulty", "max-players", "pvp", "..."]
```

---

## 4. Concrete fixtures

These are complete, valid example documents — not truncated sketches. They double as the **offline test fixtures** referenced in §15.

### 4.1 Fixture: Vanilla (MVP)

```json
{
  "schema_version": 1,
  "game_type": "minecraft",
  "distribution": "vanilla",
  "minecraft_version": "1.21.11",
  "loader": null,
  "loader_version": null,
  "java_major": 21,
  "java": {
    "major": 21,
    "vendor": "temurin",
    "package_type": "jre",
    "install_path": "/usr/lib/jvm/temurin-21-jre-arm64",
    "source": "distro_package",
    "resolved_at": "2026-08-11T18:02:00Z"
  },
  "server_artifact": {
    "kind": "single_jar",
    "filename": "server.jar",
    "download_url": "https://piston-data.mojang.com/v1/objects/8fcd0dc27f6a1f5a2a03fe0b3d1c8fbc4a04d1c8/server.jar",
    "installer_filename": null,
    "installer_download_url": null,
    "unix_args_path": null
  },
  "artifact_hash": {
    "algorithm": "sha1",
    "value": "8fcd0dc27f6a1f5a2a03fe0b3d1c8fbc4a04d1c8",
    "verified_at": "2026-08-11T18:03:12Z"
  },
  "launch_command": {
    "working_directory": "/opt/mcmgr/server",
    "executable": "/usr/lib/jvm/temurin-21-jre-arm64/bin/java",
    "args": ["-Xms2G", "-Xmx4G", "-XX:+UseG1GC", "-jar", "server.jar", "nogui"],
    "jvm_memory_args_source": "launch_args"
  },
  "world_path": "/opt/mcmgr/server/world",
  "server_dir": "/opt/mcmgr/server",
  "minecraft_unit": "minecraft",
  "server_properties_managed_keys": ["motd", "difficulty", "max-players", "pvp", "white-list", "enforce-whitelist"],
  "eula": {
    "accepted": true,
    "accepted_at": "2026-08-11T18:01:00Z",
    "accepted_version_context": "1.21.11"
  },
  "rcon": {
    "enabled": true,
    "port": 25575,
    "bind_address": "127.0.0.1",
    "password_secret_ref": "file:/etc/mcmgr/rcon.secret"
  },
  "modpack": null,
  "install": {
    "installed_at": "2026-08-11T18:03:20Z",
    "installed_by": "setup_wizard",
    "bootstrap_tool_version": "mcmgr-bootstrap/0.1.0",
    "os_arch": "aarch64"
  },
  "previous": null
}
```

*(This SHA-1 is illustrative-shaped, not a value you should trust as real — always take the live value from `piston-meta` at install time; see §16.)*

### 4.2 Fixture: Optimized Vanilla / Paper (v1)

```json
{
  "schema_version": 1,
  "game_type": "minecraft",
  "distribution": "paper",
  "minecraft_version": "1.21.10",
  "loader": null,
  "loader_version": null,
  "java_major": 21,
  "java": {
    "major": 21,
    "vendor": "temurin",
    "package_type": "jre",
    "install_path": "/usr/lib/jvm/temurin-21-jre-arm64",
    "source": "distro_package",
    "resolved_at": "2026-08-11T18:10:00Z"
  },
  "server_artifact": {
    "kind": "single_jar",
    "filename": "paper-1.21.10-48.jar",
    "download_url": "https://fill-data.papermc.io/v1/objects/bfca155b4a6b45644bfc1766f4e02a83c736e45fcc060e8788c71d6e7b3d56f6/paper-1.21.10-48.jar",
    "installer_filename": null,
    "installer_download_url": null,
    "unix_args_path": null
  },
  "artifact_hash": {
    "algorithm": "sha256",
    "value": "bfca155b4a6b45644bfc1766f4e02a83c736e45fcc060e8788c71d6e7b3d56f6",
    "verified_at": "2026-08-11T18:11:02Z"
  },
  "launch_command": {
    "working_directory": "/opt/mcmgr/server",
    "executable": "/usr/lib/jvm/temurin-21-jre-arm64/bin/java",
    "args": ["-Xms4G", "-Xmx8G", "-XX:+UseG1GC", "-XX:+ParallelRefProcEnabled", "-jar", "paper-1.21.10-48.jar", "--nogui"],
    "jvm_memory_args_source": "launch_args"
  },
  "world_path": "/opt/mcmgr/server/world",
  "server_dir": "/opt/mcmgr/server",
  "minecraft_unit": "minecraft",
  "server_properties_managed_keys": ["motd", "difficulty", "max-players", "pvp", "white-list", "enforce-whitelist"],
  "eula": {
    "accepted": true,
    "accepted_at": "2026-08-11T18:09:00Z",
    "accepted_version_context": "1.21.10"
  },
  "rcon": {
    "enabled": true,
    "port": 25575,
    "bind_address": "127.0.0.1",
    "password_secret_ref": "file:/etc/mcmgr/rcon.secret"
  },
  "modpack": null,
  "install": {
    "installed_at": "2026-08-11T18:11:05Z",
    "installed_by": "setup_wizard",
    "bootstrap_tool_version": "mcmgr-bootstrap/0.4.0",
    "os_arch": "aarch64"
  },
  "previous": null
}
```

### 4.3 Fixture: NeoForge modpack from Modrinth (v1, hypothetical)

```json
{
  "schema_version": 1,
  "game_type": "minecraft",
  "distribution": "modded",
  "minecraft_version": "1.21.1",
  "loader": "neoforge",
  "loader_version": "21.1.98",
  "java_major": 21,
  "java": {
    "major": 21,
    "vendor": "temurin",
    "package_type": "jre",
    "install_path": "/usr/lib/jvm/temurin-21-jre-arm64",
    "source": "distro_package",
    "resolved_at": "2026-08-11T19:00:00Z"
  },
  "server_artifact": {
    "kind": "argfile_tree",
    "filename": null,
    "download_url": null,
    "installer_filename": "neoforge-21.1.98-installer.jar",
    "installer_download_url": "https://maven.neoforged.net/releases/net/neoforged/neoforge/21.1.98/neoforge-21.1.98-installer.jar",
    "unix_args_path": "libraries/net/neoforged/neoforge/21.1.98/unix_args.txt"
  },
  "artifact_hash": {
    "algorithm": "none_published",
    "value": null,
    "verified_at": null
  },
  "launch_command": {
    "working_directory": "/opt/mcmgr/server",
    "executable": "/usr/lib/jvm/temurin-21-jre-arm64/bin/java",
    "args": ["@user_jvm_args.txt", "@libraries/net/neoforged/neoforge/21.1.98/unix_args.txt", "--nogui"],
    "jvm_memory_args_source": "user_jvm_args_file"
  },
  "world_path": "/opt/mcmgr/server/world",
  "server_dir": "/opt/mcmgr/server",
  "minecraft_unit": "minecraft",
  "server_properties_managed_keys": ["motd", "difficulty", "max-players", "pvp", "white-list", "enforce-whitelist"],
  "eula": {
    "accepted": true,
    "accepted_at": "2026-08-11T18:55:00Z",
    "accepted_version_context": "1.21.1"
  },
  "rcon": {
    "enabled": true,
    "port": 25575,
    "bind_address": "127.0.0.1",
    "password_secret_ref": "file:/etc/mcmgr/rcon.secret"
  },
  "modpack": {
    "source": "modrinth",
    "project_id": "AABBCCDD",
    "version_id": "1a2b3c4d",
    "pack_name": "Example Tech & Exploration",
    "pack_version_label": "3.2.0",
    "client_pack_required": true,
    "excluded_client_only_files": [
      "mods/xaerominimap-fair-1.39.14-fabric_1.21.1.jar",
      "mods/entityculling-forge-1.7.3-mc1.21.1.jar"
    ],
    "imported_at": "2026-08-11T18:50:00Z"
  },
  "install": {
    "installed_at": "2026-08-11T19:02:10Z",
    "installed_by": "setup_wizard",
    "bootstrap_tool_version": "mcmgr-bootstrap/0.6.0",
    "os_arch": "aarch64"
  },
  "previous": null
}
```

*(`excluded_client_only_files` intentionally includes a client-only mod name like `entityculling` even though "entity culling" sounds server-relevant — see §25 for why client/server side detection cannot be done by name alone.)*

### 4.4 Fixture: Fabric, manually uploaded server pack (v1/later, hypothetical)

```json
{
  "schema_version": 1,
  "game_type": "minecraft",
  "distribution": "modded",
  "minecraft_version": "1.21.8",
  "loader": "fabric",
  "loader_version": "0.17.2",
  "java_major": 21,
  "java": {
    "major": 21,
    "vendor": "temurin",
    "package_type": "jre",
    "install_path": "/usr/lib/jvm/temurin-21-jre-arm64",
    "source": "distro_package",
    "resolved_at": "2026-08-11T19:20:00Z"
  },
  "server_artifact": {
    "kind": "launcher_jar",
    "filename": "fabric-server-mc.1.21.8-loader.0.17.2-launcher.1.1.0.jar",
    "download_url": "https://meta.fabricmc.net/v2/versions/loader/1.21.8/0.17.2/1.1.0/server/jar",
    "installer_filename": null,
    "installer_download_url": null,
    "unix_args_path": null
  },
  "artifact_hash": {
    "algorithm": "none_published",
    "value": null,
    "verified_at": null
  },
  "launch_command": {
    "working_directory": "/opt/mcmgr/server",
    "executable": "/usr/lib/jvm/temurin-21-jre-arm64/bin/java",
    "args": ["-Xms3G", "-Xmx6G", "-jar", "fabric-server-mc.1.21.8-loader.0.17.2-launcher.1.1.0.jar", "nogui"],
    "jvm_memory_args_source": "launch_args"
  },
  "world_path": "/opt/mcmgr/server/world",
  "server_dir": "/opt/mcmgr/server",
  "minecraft_unit": "minecraft",
  "server_properties_managed_keys": ["motd", "difficulty", "max-players", "pvp", "white-list", "enforce-whitelist"],
  "eula": {
    "accepted": true,
    "accepted_at": "2026-08-11T19:18:00Z",
    "accepted_version_context": "1.21.8"
  },
  "rcon": {
    "enabled": true,
    "port": 25575,
    "bind_address": "127.0.0.1",
    "password_secret_ref": "file:/etc/mcmgr/rcon.secret"
  },
  "modpack": {
    "source": "manual_upload",
    "project_id": null,
    "version_id": null,
    "pack_name": "Friend-supplied server pack.zip",
    "pack_version_label": null,
    "client_pack_required": true,
    "excluded_client_only_files": [],
    "imported_at": "2026-08-11T19:15:00Z"
  },
  "install": {
    "installed_at": "2026-08-11T19:21:00Z",
    "installed_by": "manager_upgrade",
    "bootstrap_tool_version": "mcmgr-bootstrap/0.6.0",
    "os_arch": "aarch64"
  },
  "previous": null
}
```

---

## 5. Directory layout, users, ownership, permissions

### 5.1 Decision: dedicated `mcmgr` system user and group, product-owned tree under `/opt/mcmgr`

The lab's current operator deployment uses `/home/ubuntu/minecraft/server` and runs the Minecraft process as `ubuntu` (see `Infrastructure-Information.md` §"Minecraft server (VM1)" and `vm_agent/config.example.json`'s `world_path` default). That is acceptable for the **hand-built lab stack** but is not what Setup should create on a greenfield product deploy, for two reasons: (1) running the game process as the same user that owns the SSH login and the idle-agent/Manager SSH session is not least-privilege — a bad mod or a compromised world file should not have write access to `~ubuntu` (SSH keys, shell history, agent code); (2) `/home/ubuntu/...` is not a stable, discoverable path for generic tooling to assume.

**Setup/OpenTofu-created (greenfield) layout:**

| Path | Owner:Group | Mode | Purpose |
|---|---|---|---|
| `/opt/mcmgr/` | `root:mcmgr` | `0750` | Root of all product-managed on-box state |
| `/opt/mcmgr/server/` | `mcmgr:mcmgr` | `0750` | `server_dir` — jar(s)/installer output, `libraries/`, `mods/`, `config/`, `world/` |
| `/opt/mcmgr/server/world/` | `mcmgr:mcmgr` | `0750` | `world_path` — the save; owned by the game user so vanilla saves work without extra chown steps |
| `/opt/mcmgr/backups-work/` | `mcmgr:mcmgr` | `0750` | Ephemeral zip staging (equivalent to today's `/var/tmp/mc-manager-backup/`) |
| `/opt/mc-manager/` | `root:root` | `0750` | Idle agent code/venv — **unchanged path**, kept as-is for continuity with `vm_agent/` today (see §10) |
| `/etc/mcmgr/` | `root:mcmgr` | `0750` | `game-manifest.json`, `rcon.secret`, any bootstrap state files |
| `/etc/mc-manager/config.json` | `root:root` | `0640` | **Unchanged** idle-agent config path/format (§10) |
| `/var/lib/mcmgr/` | `root:root` | `0750` | Bootstrap/upgrade state machine files (§14) |

**System accounts created by bootstrap (idempotent, first run only):**

- `mcmgr` — system user (`useradd --system --home-dir /opt/mcmgr/server --shell /usr/sbin/nologin mcmgr`), **no SSH access, no sudo**. This is the user the `minecraft` systemd unit runs as (`User=mcmgr`, `Group=mcmgr`).
- `mcmgr` group — shared so the idle agent (running as `root`, see below) and any future non-root helper can read `/etc/mcmgr/` without being world-readable.

**Why the idle agent still runs as `root` (unchanged):** today's `mc-idle-watch.service` unit runs `User=root` (see `vm_agent/systemd/mc-idle-watch.service`) because it must call `systemctl stop minecraft`, publish to Object Storage via instance-principal OCI CLI wrappers, and eventually call the OCI SoftStop API. Splitting that into a least-privilege non-root agent is worth doing eventually but is **out of scope for this document** — it is an idle-agent hardening task, not a game-deployment task, and changing it is not required to ship Vanilla/Paper/Modded support. What **is** in scope here: the idle agent's `systemctl stop <unit>` call must use `minecraft_unit` from the manifest (already true — `cfg.get("minecraft_unit", "minecraft")`), and `world_path` for backup must likewise come from the manifest-derived config value (already true today; §10 documents how the manifest becomes the source of that config value going forward). **When** it SoftStops: product intent (MVP Step 4.1) is after `idle_timeout_minutes` if the unit is empty **or not `active`** — implement in lab `vm_agent/` **and** redeploy VM1; do not invent a second timeout key.

**Migration note for the operator's existing hand-built stack:** this section describes what **Setup creates on a fresh deploy**. It is *not* a mandate to immediately migrate the operator's live lab VM1 off `/home/ubuntu/minecraft/server` — `Infrastructure-Information.md` explicitly says the world path "may change under Setup / Vanilla vs modded" and callers should read config rather than assume a path. Do not rip up the working lab stack to match this table; instead, make sure any new automated-bootstrap code path (Step 2.3+) targets `/opt/mcmgr/server`, and let Connect-existing read whatever `world_path` a given stack actually has.

**CHDIR / `User=mcmgr` (SETUP-ISSUE-4, fixed 2026-08-15):** systemd `status=200/CHDIR` means **`mcmgr` cannot traverse `WorkingDirectory=`**. Every parent of `/opt/mcmgr/server` must remain executable by that user after **every** bootstrap stage (including resume). Product enforcement is `onbox/mcmgr/common/layout.sh` (`layout_apply` + fail-closed `layout_verify`) and `repair-permissions.sh`. Do not run the game as `ubuntu` and do not `0777` the tree.

### 5.2 Sub-directory conventions inside `server_dir`, by distribution

| Distribution | Notable subpaths | Notes |
|---|---|---|
| `vanilla` | `server.jar`, `world/`, `server.properties`, `eula.txt` | Flat; nothing else required |
| `paper` | `paper-<mc>-<build>.jar`, `world/`, `plugins/`, `server.properties`, `eula.txt` | `plugins/` exists from first boot even if empty — Paper creates it; Setup should not need to |
| `modded` (Fabric/Quilt) | `fabric-server-mc.<mc>-loader.<loader>-launcher.<installer>.jar`, `mods/`, `config/`, `world/`, `server.properties`, `eula.txt` | `mods/` populated by the pack installer step, not by the loader installer itself |
| `modded` (Forge/NeoForge, modern) | `libraries/` (huge tree, versioned installer output), `mods/`, `config/`, `world/`, `run.sh`/`run.bat` (generated by installer — informational only, **the systemd unit does not call these scripts**, it replicates their one meaningful line — see §6.4), `user_jvm_args.txt`, `server.properties`, `eula.txt` | `libraries/` is regenerated by the installer and should be treated as **build output**, not something Setup/backup ever needs to touch by hand |

Backups (§11) **never** include `libraries/` for Forge/NeoForge — only `world/`, `server.properties`, `mods/`, `config/`, and the manifest itself need to survive a restore; `libraries/` can always be regenerated by re-running the recorded installer.

---

## 6. systemd lifecycle and launch command generation

### 6.1 Decision: a single generic unit template, populated from the manifest, not one unit-file-per-loader

Bootstrap (and any future upgrade) **generates** `/etc/systemd/system/minecraft.service` from the manifest's `launch_command` object — there is exactly one code path that writes this unit file, shared by all distributions. This directly implements the "avoid hard-coding Vanilla into infrastructure contracts" goal: the unit generator's input is `launch_command.executable` + `launch_command.args` + `launch_command.working_directory`, never an `if distribution == "vanilla"` branch.

### 6.2 Generic unit template

```ini
[Unit]
Description=Minecraft server (%i)
After=network-online.target
Wants=network-online.target
StartLimitIntervalSec=600
StartLimitBurst=3

[Service]
Type=simple
User=mcmgr
Group=mcmgr
WorkingDirectory={{server_dir}}
ExecStart={{executable}} {{args_joined}}
ExecStop=/opt/mcmgr/bin/rcon-graceful-stop.sh
TimeoutStopSec=120
Restart=on-failure
RestartSec=10
Environment=LANG=C.UTF-8
Environment=JAVA_TOOL_OPTIONS=-Djava.net.preferIPv4Stack=true
StandardOutput=journal
StandardError=journal
SyslogIdentifier=minecraft

# Light sandboxing — does not block legitimate mod/plugin file access under server_dir
NoNewPrivileges=true
ProtectSystem=strict
ReadWritePaths={{server_dir}}
ProtectHome=true

[Install]
WantedBy=multi-user.target
```

Notes:

- `{{args_joined}}` must be rendered as **properly whitespace-separated literal tokens**, not shell-quoted/escaped — systemd's own `ExecStart=` line parser splits on whitespace and understands `@file` tokens as opaque strings passed straight to `execve()`; this is exactly what the `java` launcher expects, so no additional shell layer is needed (confirmed against upstream Forge/NeoForge `run.sh`, which is only a convenience wrapper for *interactive* use, not a systemd requirement).
- `ExecStop=` is a graceful-stop helper script that does `rcon-cli save-all flush` then `save-off`/`stop` semantics identical to `vm_agent/graceful_stop.sh` today — kept as a separate script (not inlined) so both the idle agent and a plain `systemctl stop minecraft` from an admin's SSH session get the same safe shutdown behavior. `TimeoutStopSec=120` gives modded worlds with slow chunk saves room to flush before systemd sends `SIGKILL`.
- `ProtectSystem=strict` + `ReadWritePaths={{server_dir}}` is a cheap, low-risk hardening default; it does **not** interfere with mods writing inside `server_dir` (`config/`, `mods/`-adjacent state, `world/`). It does mean a mod that insists on writing outside `server_dir` (rare, and generally a mod bug/anti-pattern) would need an explicit `ReadWritePaths=` addition — record any such exception directly in the manifest's `install` notes if ever needed, do not silently loosen the unit template product-wide for one mod.
- The unit is deliberately named `minecraft.service` regardless of distribution/loader (`minecraft_unit` in the manifest exists so nothing hard-codes the literal string `"minecraft"` — see §10 — but bootstrap has no reason to ever choose a different unit name in practice).

### 6.3 Rendering `args_joined` for each `server_artifact.kind`

| `kind` | Rendered `ExecStart=` args (after `{{executable}}`) |
|---|---|
| `single_jar` | `-Xms<mem> -Xmx<mem> [gc flags] -jar {{filename}} nogui` (Vanilla) or `... -jar {{filename}} --nogui` (Paper — note the double-dash flag) |
| `launcher_jar` | `-Xms<mem> -Xmx<mem> -jar {{filename}} nogui` |
| `argfile_tree` | `@user_jvm_args.txt @{{unix_args_path}} --nogui` |
| `installer_jar` (should not remain this `kind` post-install; see §12.1) | not a valid steady-state launch shape — bootstrap must transition this to `argfile_tree` or `single_jar` before writing the unit |

`--nogui` vs `nogui`: Vanilla/Fabric accept the bare `nogui` argument (historical convention); Paper's own documented examples use `--nogui`. Both are accepted by most versions in practice but the generator must use the **upstream-documented** form per distribution rather than assuming they're interchangeable, because a version mismatch here is a easy, silent source of "why is the GUI trying to open on a headless box" bug reports.

### 6.4 Why the unit does not call `run.sh`

Forge/NeoForge ship a generated `run.sh` (see `server_files/run.sh` in Forge's own source, confirmed current at Forge 26.1.2) whose only functional line is:

```sh
java @user_jvm_args.txt @libraries/@MAVEN_PATH@/unix_args.txt "$@"
```

The systemd unit reproduces exactly this invocation as literal `ExecStart=` tokens (with `@MAVEN_PATH@` already substituted by the installer when it wrote the real `unix_args.txt` path on disk, and `"$@"` replaced by the fixed `--nogui`). This is safe and simpler than invoking `run.sh` under systemd because it avoids adding a shell process (and shell-quoting foot-guns) between systemd and the JVM, and it avoids depending on `run.sh` being executable/present at all (some hosts strip execute bits on upload — see lab `Agent-Deploy-Pitfalls.md` CRLF/permission lessons, which apply equally here).

---

## 7. server.properties and EULA handling

### 7.1 `server.properties` ownership model

Bootstrap writes a minimal `server.properties` on first install (Vanilla/Paper/modded all consume the same file format — this part of the ecosystem is blessedly uniform). Setup-controlled keys are limited and explicit (`server_properties_managed_keys` in the manifest, §3.9) — this is deliberately a **small, explicit allow-list**, not "Setup owns the whole file," because:

- Mods/plugins routinely add their own keys to `server.properties` on first boot (rare but happens) or expect the admin to hand-tune performance-sensitive keys (`view-distance`, `simulation-distance`, `network-compression-threshold`) that this product does not want to silently overwrite on every Setup/upgrade re-run.
- The idle agent and RCON already depend on two of these keys indirectly (`enable-rcon`, `rcon.port` — see §8) and must never be clobbered by an unrelated Setup action.

**Rule:** any code that rewrites `server.properties` (Setup, Manager "Server Management" customization, an upgrade script) must **read-modify-write**, touching only keys it is explicitly responsible for, and must leave all other lines untouched — the same "preserve unknown, only replace owned" discipline the existing Security List sync and `meta/flags.json` protocol already use elsewhere in this product (see `Contracts-Object-Storage.md`).

### 7.2 EULA acceptance

Mojang's EULA (`https://aka.ms/MinecraftEULA` as embedded in the generated `eula.txt` comment) must be accepted **by the human operator**, once, during the Setup wizard — this product must never auto-accept it on the user's behalf without an explicit UI step, because it is a legal acknowledgment, not a technical default. Bootstrap then writes:

```properties
#By changing the setting below to TRUE you are indicating your agreement to our EULA (https://aka.ms/MinecraftEULA).
#<UTC timestamp>
eula=true
```

and records the acceptance in the manifest's `eula` object (§3.9) so:

1. A later **downgrade or platform switch** (Vanilla → Modded, or a version change) does not need to re-prompt if the operator already accepted the (version-independent) EULA once for this stack — Mojang's EULA text does not change per Minecraft version, so `accepted_version_context` is recorded for audit only, not as a re-prompt trigger.
2. Manager can display "EULA accepted on `<date>`" for support/troubleshooting without SSHing in.

Every server type (Vanilla, Paper, Fabric, NeoForge, Forge, Quilt) reads the identical `eula.txt` file/key — no per-loader special case here.

### 7.3 Managed key defaults written by bootstrap

| Key | MVP default | Notes |
|---|---|---|
| `enable-rcon` | `true` | Required for idle-agent `list`/`save-all`/graceful stop (§8) |
| `rcon.port` | `25575` | Matches `rcon.port` used everywhere else in this product; never changed per-deploy |
| `rcon.password` | *(generated secret, not a "default" — see §8.2)* | |
| `white-list` | `false` | **OCI Security List is the allowlist.** In-game Vanilla whitelist is off so friends are not gated twice. Automated Setup must write this (MVP Step 4.3); do not rely on a manual `server.properties` edit. |
| `enforce-whitelist` | `false` | Follows `white-list`. Never a substitute for Security List `/32`s. |
| `motd` | product default string, later user-editable | |
| `difficulty` | `normal` | User-editable later; not a Setup-time question for MVP |
| `max-players` | `20` | Reasonable default for a small friend group; user-editable later |
| `online-mode` | `true` | **Never** set to `false` — disabling Mojang auth on a server exposed to any friend IP (even allowlisted) removes account verification and is a known griefing/impersonation vector; this key is intentionally *not* in `server_properties_managed_keys` so no future UI feature can toggle it by accident |

---

## 8. RCON configuration and secret handling

### 8.1 Network exposure — unchanged, restated for emphasis

RCON **always** binds `127.0.0.1:25575` only. It must never appear in the OCI Security List, never in VM1 `firewalld` rich rules, and never be reachable from the VCN CIDR the way the game port deliberately is for the door's `wait_forge` probe. This is already policy (`Infrastructure-Information.md`, `PRODUCT-IDEAS.md`) — restated here because RCON is the mechanism this entire deployment blueprint leans on for graceful stop/backup coordination (idle agent `save-off`/`save-all flush`/`save-on`, and the systemd `ExecStop=` helper in §6.2), so it is worth being explicit that **every consumer of RCON runs on VM1 itself**, never remotely.

### 8.2 Secret generation and storage

**Decision:** bootstrap generates a random RCON password (32 bytes of `/dev/urandom`, base64-encoded, no shell-unsafe characters) at first install and stores it in exactly one place on disk: `/etc/mcmgr/rcon.secret`, mode `0600`, owner `root:root`. `server.properties`' `rcon.password` key is written from this same value (server.properties itself ends up mode `0640`, owned `mcmgr:mcmgr`, so the game process can read its own config — this is unavoidable since `server.properties` is a first-class Minecraft config file the server itself reads, not a "secrets file"). The idle agent's `/etc/mc-manager/config.json` continues to hold its own copy of `rcon_password` as it does today (`vm_agent/config.example.json`) — bootstrap writes the **same generated value** into both files rather than the idle agent inventing/receiving a separate one.

**What must never happen, ever, per the existing product-wide rule (`Contracts-Object-Storage.md`, `PRODUCT-IDEAS.md` infra meta "prohibited fields"):** the RCON password never appears in `meta/infra.json`, never in any Object Storage object, never in `game-manifest.json` itself (the manifest only stores `password_secret_ref`, a *pointer* to where the secret lives on the box, per §3.9) and never in a log line. Manager reads/rotates it only over SSH, directly touching the two on-box files, exactly like it already patches `/etc/mc-manager/config.json` today for Danger Zone idle settings (`MVP-Implementation-Progress.md`, Step 1.7).

### 8.3 Rotation

Rotating the RCON password (support/hygiene action, not required for MVP) is: generate a new secret → write `/etc/mcmgr/rcon.secret` → read-modify-write `server.properties`'s `rcon.password` key (§7.1 discipline) → read-modify-write `/etc/mc-manager/config.json`'s `rcon_password` key → `systemctl restart minecraft` (RCON password is only re-read on server start, like most `server.properties` keys). Document this as a Manager "Advanced" action at v1 implementation time; not required for MVP.

### 8.4 Optional v1+ hardening: systemd credentials

Research surfaced `systemd`'s `LoadCredentialEncrypted=` mechanism (stable since systemd v254, which Ubuntu 22.04's systemd (v249) predates — **not usable on the current VM1 base image without an OS upgrade**) as the modern best-practice replacement for "secret sits in a mode-0600 file." Because Minecraft's own server process only ever reads its password from `server.properties` (it has no support for systemd credential files natively), this would only harden the **idle agent's** copy of the secret, not the Minecraft process's own file — and the current Ubuntu 22.04 base does not support it anyway. **Recommendation: do not adopt this for MVP or v1**; revisit only if/when the base OS image moves to a systemd version that supports it and there is a concrete threat model (e.g. multi-tenant hosting) that a mode-0600 root-owned file does not already address for this product's single-admin-operator use case.

### 8.5 Console-access alternatives researched and deliberately not adopted

The `itzg/docker-minecraft-server` project documents three ways to reach a running server's console, worth recording here so a future implementer does not "rediscover" them and wonder why this product doesn't use them:

1. **`rcon-cli` exec / stdin pipe** — attaches to a running container and issues RCON commands interactively, or (when RCON is disabled) pipes commands to the Minecraft process's own stdin via a helper script. Not applicable here in the "RCON disabled" branch specifically, because this product's design never disables RCON (§8.1 — the idle agent and graceful-stop path depend on it unconditionally).
2. **An SSH console on a dedicated port, authenticated with the RCON password**, giving full admin console access without needing the game's own RCON client.
3. **A WebSocket console** (`/console` endpoint) with log streaming and command injection, authenticated via a `Sec-WebSocket-Protocol` header password.

**Decision: adopt none of these.** VM1 already has a real admin SSH login (`ubuntu` user, OCI Security List–gated per-admin `/32`), which is a strictly more capable and already-audited access path than a bolt-on SSH-on-a-different-port or a WebSocket endpoint whose only auth is reusing the RCON password over a **new** listening socket. Adding either would mean a second network-exposed credential surface for no capability this product doesn't already have via existing SSH + RCON-over-localhost. The eventual v1 "Console" tab (RCON + logs, per `PRODUCT-IDEAS.md`) should be implemented as **Manager SSH tunnel to localhost RCON, plus `journalctl -u minecraft` tail over the same SSH session** — not a new always-listening service on VM1. This keeps VM1's attack surface exactly as documented in `Infrastructure-Information.md` (game port + SSH only) rather than growing it for a Manager convenience feature.

---

## 9. Java version mapping and ARM64 packages

### 9.1 Minecraft-version → Java-major table (confirmed current, 2026-08-11)

| Minecraft version range | Minimum Java major | Notes |
|---|---|---|
| Classic – 1.5.2 | 5 | Not a supported product target; listed for completeness only |
| 1.6.1 – 1.11.2 | 6 | Not a supported product target |
| 1.12 – 1.16.5 | 8 | Legacy Forge packs live here — see §20 |
| 1.17 – 1.17.1 | 16 | Narrow band; Forge's server launch mechanics also changed exactly here (§6.4, §20) |
| 1.18 – 1.20.4 | 17 | |
| 1.20.5 – 1.21.11 | 21 | **Current mainstream target as of 2026-08-11** |
| 26.1 and newer | 25 | Mojang's newer date-influenced version scheme (`26.1`, `26.2`, ...); confirmed via Minecraft Wiki changelog, independently by PaperMC/NeoForge docs already tracking Java 25 for `26.1+`, and by Mojang's own [announcement of the new version numbering system](https://www.minecraft.net/en-us/article/minecraft-new-version-numbering-system) (also linked from the `itzg/docker-minecraft-server` version-selection docs, which accept the new scheme's identifiers — e.g. `26.1`, `26.1-snapshot-1`, `26.1-pre-1`, `26.1-rc-1` — directly as `VERSION` values) |

**Product policy:** always install the **minimum required** Java major for the selected `minecraft_version`/loader combination, not reflexively "the latest LTS," because (a) it is the documented, supported baseline every upstream project tests against, and (b) it keeps `java_major` in the manifest meaningfully tied to compatibility rather than to "whatever was newest the day Setup ran." A newer major usually also works (Minecraft/Forge/Paper all state older MC versions can run on newer JVMs), but "usually works" is not a basis for a repair/rollback-safe product — pin to the documented floor. **This floor is a per-Minecraft-version default, not an unconditional rule — see §9.5 for confirmed real-world exceptions where a specific modpack/loader build needs an *older* Java than the Minecraft version alone would suggest.**

**Where the floor comes from, per distribution:**

- Vanilla: `javaVersion.majorVersion` in the Mojang per-version metadata JSON (§16.3) — authoritative, per-version, no guesswork.
- Paper: Fill v3's `builds` response now publishes `minimumJavaVersion` directly (confirmed in the Fill v3 changelog materials, §17) — prefer this field over the static table above once implementing; fall back to the table only if the field is absent for an older Paper version.
- Fabric/Quilt/Forge/NeoForge: none of these publish a machine-readable per-version Java floor API as of this research; use the static table above keyed by `minecraft_version`, and keep the table itself in one shared, well-commented constant so a future Minecraft Java-floor bump (like the `26.1` → Java 25 jump already observed) is a one-line change, not a scattered find-and-replace across installer modules.

### 9.2 ARM64 package acquisition — decision: Linux distro packages, not the Adoptium REST API, for MVP

Two viable sources were researched:

1. **Adoptium's own package repositories** (`apt`/`dnf`/`apk`, `packages.adoptium.net`) — e.g. `sudo apt install temurin-21-jre-headless` on Ubuntu. These are architecture-aware automatically (the repo serves the right `aarch64` package for an ARM64 apt client) and are kept current by OS-level `unattended-upgrades`/patch cadence if enabled.
2. **The Adoptium REST API** (`api.adoptium.net/v3/binary/latest/{major}/ga/linux/aarch64/{jre|jdk}/hotspot/normal/eclipse`) with a `.sha256.txt` companion for verification — a pure-download, no-package-manager path.

**Decision: prefer (1), distro packages via the Adoptium `apt` repository, for the actual bootstrap script**, because it integrates with the OS's own dependency/update mechanics and matches what most of the reference implementations found during research do (`temurin-<version>-jre-headless` is exactly the package itzg's tooling and several from-scratch bootstrap scripts install). Use (2), the REST API, only as a **fallback** when the apt repository is unreachable (e.g. transient network partition, or a future non-Debian-family base image) — record which `source` was actually used in the manifest's `java.source` field (`"distro_package"` vs `"adoptium_api_archive"`) so upgrade/repair logic can be consistent with how a given box was actually provisioned.

**Headless JRE, not JDK:** the server never compiles anything at runtime; install `temurin-<major>-jre-headless` (excludes AWT/GUI libraries the dedicated server never touches) rather than a full JDK, keeping the install footprint and attack surface smaller on both VM1 and, if this product ever runs a build step for mods, only bring in a JDK for that specific, narrow case.

### 9.3 Concrete bootstrap steps (aarch64)

```bash
# One-time repo setup (idempotent — check before re-adding)
wget -O /etc/apt/trusted.gpg.d/adoptium.asc https://packages.adoptium.net/artifactory/api/gpg/key/public
echo "deb https://packages.adoptium.net/artifactory/deb $(awk -F= '/^VERSION_CODENAME/{print$2}' /etc/os-release) main" \
  | tee /etc/apt/sources.list.d/adoptium.list
apt-get update

# Resolve and install the pinned major from the manifest
apt-get install -y "temurin-${JAVA_MAJOR}-jre-headless"

# Record the resolved interpreter path for the manifest's launch_command.executable
readlink -f "$(update-alternatives --list java | grep "temurin-${JAVA_MAJOR}")"
```

Multiple Java majors can coexist on the same VM1 (Debian/Ubuntu `update-alternatives` supports parallel installs) — this matters because a future in-place upgrade from e.g. a 1.20.4 Vanilla server (Java 17) to a 1.21 Vanilla server (Java 21) should not require uninstalling the old runtime before the new one is proven to work; only remove an old Java major during **cleanup**, well after a successful upgrade + smoke check (§12).

### 9.4 ARM64 checksum verification for the JVM itself

Because the JRE is installed via signed apt packages (Adoptium's repository is GPG-signed, and apt/dpkg verify packages against that key automatically), there is no separate manual checksum step needed for the Java runtime the way there is for the *game* artifact — apt's own integrity verification is already equivalent to (arguably stronger than) manually checking a `.sha256.txt` file. Do not add a redundant manual hash check here; do record the resolved package version string in `java.install_path`/`java.resolved_at` for audit.

### 9.5 Confirmed real-world Java/loader interaction hazards (must override the static floor when they apply)

Cross-checking against `itzg/docker-minecraft-server`'s Java-version documentation (a project old enough, and used at enough scale, to have accumulated years of these exact bug reports) surfaced concrete exceptions that a naive "Minecraft version → Java major" lookup would get wrong:

- **A specific Forge modpack can require an *older* Java than its Minecraft version's generic floor.** Confirmed real symptom on Java 21 with some Forge mods as recent as Minecraft 1.21: `ClassMetadataNotFoundException` from Sponge Mixin, resolved only by pinning to Java 17. **Consequence for this manifest's design:** `java_major` resolution for a **modded** install must let the pack's own declared/tested requirement (or an explicit Setup override) win over the generic per-Minecraft-version table in §9.1 when they conflict — the table is the right default for Vanilla/Paper (no third-party mod code involved) and a *starting point*, not an unconditional rule, for modded installs.
- **Forge below Minecraft 1.18 requires Java 8 specifically** — not "8 or newer." Confirmed symptom: `ClassCastException: class jdk.internal.loader.ClassLoaders$AppClassLoader cannot be cast to class java.net.URLClassLoader` on newer JVMs, because these Forge versions reach into JVM classloader internals that changed shape after Java 8. This reinforces §20.4's legacy Forge Java-8 pin — do not let a future "just use a newer JVM for safety" impulse apply to pre-1.18 Forge.
- **Forge does not support the OpenJ9 JVM implementation at all**, on any Minecraft version. Not a concern for this product today (the Java-acquisition decision in §9.2 already standardizes on Eclipse Temurin/HotSpot), but worth recording so nobody "optimizes" Java acquisition toward OpenJ9 later (e.g. for a memory-footprint win) without rediscovering this the hard way on a Forge/NeoForge stack.
- **Base-OS libc choice is a related but distinct compatibility axis from CPU architecture.** `itzg/docker-minecraft-server` explicitly documents that its Oracle GraalVM image variants (built on Oracle Linux, an RHEL derivative) break the **Forge installer itself** because of the installer's use of `zlib-ng`, and separately ships both glibc-based (Ubuntu) and musl-based (Alpine) image variants as distinct compatibility tiers. This product already standardizes on Ubuntu 22.04 (glibc) for VM1 (`Infrastructure-Information.md`), which is the safe choice this cross-check validates — but it means **any future proposal to move VM1 to a slimmer/Alpine-style base image must be re-validated against the Forge/NeoForge installer and against any mod that bundles native libraries (§27.2)**, not assumed compatible just because the CPU architecture (aarch64) is unchanged. Native-library compatibility has (at least) two independent axes — CPU architecture **and** C library — and this product's ARM64 research (§27) only speaks to the first one.

---

## 10. Idle agent integration

### 10.1 What already generalizes cleanly (no change needed)

`vm_agent/` already reads `world_path`, `minecraft_unit`, `rcon_port`, and `rcon_password` from `/etc/mc-manager/config.json` rather than hard-coding them (confirmed by reading `vm_agent/world_backup.py`, `vm_agent/idle_watch.py`, `vm_agent/graceful_stop.sh` during this research — every one of these already takes the path/unit from config, with `/home/ubuntu/minecraft/server/world` and `"minecraft"` only as *fallback defaults*, not hard-coded assumptions). This means **the idle agent itself needs no code changes to support Vanilla/Paper/Modded** — it already treats "what/where is the game" as configuration.

### 10.2 What bootstrap must do so the idle agent config stays correct

The one integration point that must be wired up: whenever bootstrap or an upgrade writes/updates `/etc/mcmgr/game-manifest.json`, it must **also** update the relevant keys in `/etc/mc-manager/config.json` (`world_path`, `minecraft_unit`, `rcon_port`, `rcon_password`) to match. This is a plain read-modify-write of a JSON file the idle agent already owns the schema for — no new idle-agent feature, just a bootstrap-side responsibility to keep two config files in sync. Concretely:

```text
game-manifest.json.world_path      → mc-manager/config.json["world_path"]
game-manifest.json.minecraft_unit  → mc-manager/config.json["minecraft_unit"]
game-manifest.json.rcon.port       → mc-manager/config.json["rcon_port"]
(rcon secret file)                 → mc-manager/config.json["rcon_password"]
```

Because both files already live on the same box and are both touched by the same bootstrap/upgrade tooling, this is a same-transaction write — there is no cross-actor race to design around here the way there is for shared Object Storage documents.

### 10.3 Shape/backup soft-cap fields are out of scope for this document

`shape_ocpus`/`shape_memory_gb` (live-detected), Object Storage soft-cap, and lease/ledger behavior are the idle agent's own domain (`Object-Storage-Phase5.md`, `Contracts-Object-Storage.md`) and are unaffected by which game distribution is installed — this blueprint does not change or restate that design, only confirms the one integration seam above.

---

## 11. World import/replacement and backup compatibility

### 11.1 Backups already work per-distribution with zero changes, with one caveat

`world_backup.py`'s cold/live zip logic operates purely on `world_path` and RCON `save-off`/`save-all flush`/`save-on` — none of that is Vanilla-specific, and it already works identically for Paper (same RCON commands, same world folder shape) and for Fabric/Forge/NeoForge servers whose `world_path` still just points at a single save directory. **No backup code changes are required by this blueprint** for world zip/upload/eviction.

**Caveat worth recording (Setup/Manager responsibility, not idle-agent code):** a modded world backup should logically also let the operator recover `mods/` and `config/` alongside `world/`, because restoring a save into a server whose mod set no longer matches what generated it can corrupt chunks or simply refuse to load. This blueprint's recommendation: keep `world/` as the only thing the **automatic** Object Storage backup zips (unchanged behavior, keeps zip sizes and the 9.5 GiB soft cap policy meaningful), but have the **manifest itself** (`loader`/`loader_version`/`modpack.version_id`) serve as the durable record of "what mod set does this save expect" — a restore procedure (§12.4/§25) must cross-check the *current* manifest against the *target* backup's recorded manifest snapshot (see `previous` field, §3, and §12.4) before restoring, and warn loudly on mismatch rather than silently loading a save under the wrong mod set.

### 11.2 Multi-GiB modded worlds and the existing 9.5 GiB soft cap

Nothing here overrides the existing Object Storage Always-Free soft-cap policy (`meta/oversized-world-backup.json`, `Contracts-Object-Storage.md`) — it already anticipates modded worlds being the common trigger case ("especially modded" is called out explicitly in `PRODUCT-IDEAS.md`). This blueprint adds no new policy; it simply confirms that the existing flag/skip contract is distribution-agnostic and needs no changes for Paper/Modded support.

### 11.3 Wipe world (v1 Manager, not Setup)

Lab `PRODUCT-IDEAS.md` **Wipe world (v1)** is a Server Management action: stop Minecraft, delete the live save at `world_path` (the world directory only — not `mods/`, not Object Storage backups), then the next start lets the server generate a **new** world. Confirmation UI is a PRODUCT-IDEAS concern.

**On-box:** delete the contents of `world_path` (or the directory itself and recreate an empty one with the same ownership as §5). Do **not** use this path to wipe `mods/` or `config/`. Restoring a previous world remains the existing backup download / world-replace flow. If Minecraft is running, stop the unit first (same stop discipline as a cold backup) so files are not rewritten while being deleted.

---

## 12. First boot, upgrades, downgrades, rollback

### 12.1 First boot (any distribution)

1. Resolve inputs (chosen `minecraft_version`, `distribution`, and if modded, `loader`/pack source) — comes from Setup wizard state for MVP/v1, or from a re-run bootstrap invocation for repair.
2. Resolve `java_major` (§9.1) and ensure the runtime is installed (§9.2–9.3).
3. Run the distribution-specific **installer module** (Part B) to produce the on-disk artifact(s). For `installer_jar`/`argfile_tree` kinds, this step transitions the manifest's `server_artifact.kind` from a **transient** `installer_jar` bootstrap state to the **steady-state** `argfile_tree` (or, for very old single-jar Forge, `single_jar`) once the installer run completes successfully and the expected output files are verified present.
4. Write `server.properties` (§7.1/§7.3) and `eula.txt` (§7.2).
5. Generate the RCON secret and wire it into `server.properties` + idle-agent config (§8.2, §10.2).
6. If `distribution == "modded"`: run the pack-install step (download pack index, filter to server-eligible files per §22/§23/§24, place into `mods/`+`config/`+overrides) **before** first server start, so the loader's own first-boot config generation sees the final mod set.
7. Write the full `game-manifest.json` (§3) with `install.installed_by = "setup_wizard"` (or `"manual_ssh"` for a documented dry-run/repair path).
8. Generate and enable the systemd unit (§6) — `systemctl enable --now minecraft`.
9. Health check: poll RCON (or the game port) for a bounded time (reuse the door's existing `wait_forge`-style bounded poll pattern — see `Infrastructure-Information.md` "Door control plane" — rather than inventing a new polling convention) to confirm the process actually reached a joinable state, not just "systemd says the process is running." A modded server can `Active: running` for a long time while still crash-looping on world generation; only a successful RCON `list` (or equivalent) response counts as "first boot succeeded."
10. On success, hand control back to whichever actor invoked bootstrap (Setup wizard step, or Manager's install/repair action) with a clear success payload; on failure, leave the manifest's `install` block **absent or marked failed** rather than writing a manifest that claims success — see §14.

### 12.2 Upgrades (same distribution, new Minecraft/loader/build version)

**General shape, for every distribution:** stop the service → snapshot the current manifest into `previous` (§12.4) → take a **safety world backup** (reuse `world_backup.py cold`, this is the same mechanism SoftStop already uses, invoked explicitly rather than via the idle-timer path) → run the new version's installer module against the existing `server_dir` → re-verify Java major (may need to change, e.g. 1.20.4 → 1.21 crosses the Java 17→21 line) → rewrite the manifest with the new values → regenerate the systemd unit if `launch_command` shape changed → start → health check (§12.1 step 9) → on success, clear `previous`... **or keep it** (see §12.4 — recommend keeping exactly one `previous` snapshot, not clearing it, so a rollback is always one step behind, not zero).

**Per-distribution upgrade specifics:**

- **Vanilla → Vanilla (new version):** delete/replace `server.jar`, re-resolve `downloads.server.url`+`sha1` from the manifest for the *new* version id (§16). World save format upgrades are handled by the vanilla server itself on next boot (Mojang guarantees forward data-version migration within Java Edition); this product does not need its own world-format migration logic.
- **Paper → Paper (new build, same or new MC version):** same as Vanilla but resolve via Fill v3 (§17); Paper explicitly supports being dropped onto an existing Vanilla-format world (it *is* a superset), so a **Vanilla → Paper** "upgrade" (really a distribution *switch*, see §12.3) is the same mechanical steps as a version upgrade, just with `distribution` also changing in the manifest.
- **Fabric/Quilt (new loader version, same MC version):** re-run `.../server/jar` resolution with the new `loader_version` (and current `installer_version` — always re-check this too, since it is a separate axis from the loader version per §18); replace the launcher jar; `mods/`/`config/` are untouched (loader upgrades do not need a pack re-resolve unless the pack itself pinned an incompatible loader range).
- **Forge/NeoForge (new loader version, same MC version):** re-run the new version's installer jar with `--installServer` **into the same directory** — this is exactly what upstream NeoForge docs describe as the supported update path ("simply download and run the installer for the new version... The installer will automatically replace uses of the old version where needed"); do not attempt to hand-patch the `libraries/` tree.
- **Any modded upgrade that crosses a Minecraft version boundary:** treat this as **higher risk** than a same-MC-version loader bump — most mods are pinned to an exact Minecraft version range and a modpack upgrade across MC versions almost always means the *entire pack* must be re-resolved from its source (new Modrinth/CurseForge version id), not just the loader. Bootstrap must refuse to "partially" upgrade (new MC version, old pack files left in `mods/`) — either the new pack version resolves cleanly for every currently-installed mod's server-required files, or the upgrade aborts before touching anything on disk.

### 12.3 Distribution switches (Vanilla ↔ Paper ↔ Modded)

A distribution switch is modeled as an upgrade whose `distribution` (and possibly `loader`) field changes, using the **same** stop → snapshot → backup → install-new → manifest-rewrite → unit-regenerate → start → health-check pipeline as §12.2. The two directions that need explicit call-outs:

- **Vanilla/Paper → Modded:** the existing world save is compatible (Forge/NeoForge/Fabric all load a vanilla-format save fine, gaining whatever new block/dimension data the mods add on first load) — but the operator must be warned that adding mods **after** a save already exists can occasionally cause mod-specific first-load migrations that are one-way; this is an inherent Minecraft modding property this product cannot engineer around, so the recommendation is: the safety world backup taken in step 2 of §12.2 is the rollback path, not a "we prevented this" guarantee.
- **Modded → Vanilla/Paper:** the save will very likely be missing blocks/items/dimensions that only existed because of the removed mods; Minecraft handles unknown blocks by substituting them but chunk data can still contain now-orphaned block-entity data. This direction should carry the **strongest** UI warning of any switch (v1 UI concern, not addressed further here) — the technical bootstrap steps are otherwise identical.

### 12.4 Downgrades and rollback

**Rollback (undo the most recent upgrade) is a first-class, cheap operation** because of the `previous` field (§3): rollback means stop → restore the world backup taken immediately before the upgrade that is being undone → restore `server_dir`'s non-world files from *before* the upgrade (this requires bootstrap to keep the pre-upgrade artifact files, not just the manifest, until a rollback window closes — see retention rule below) → write `game-manifest.json` back to the exact content of `previous` (with `previous.previous` set to `null`, since we do not support multi-level rollback chains) → regenerate the unit → start → health check.

**Retention rule:** bootstrap/upgrade tooling keeps the **immediately prior** artifact set (the old jar, or the old `libraries/` tree renamed to `libraries.rollback/`, or the old `mods/`+`config/` renamed to `mods.rollback/`+`config.rollback/`) for **one upgrade cycle only** — a second upgrade on top of an already-pending rollback window permanently discards the older-than-previous state. This bounds disk usage predictably (at most 2x the artifact tree at any time, never unbounded history) while still making "that upgrade just went badly, undo it" a fast, reliable action rather than "re-run the entire original install from scratch and hope the world save still loads."

**Downgrade to an arbitrary older version** (not just "undo the last upgrade") is **not** a first-class product feature — it is mechanically the same pipeline as an upgrade (§12.2), just choosing an older version id/loader version as the target, and it inherits every one of the version-compatibility risks in §12.3 in reverse. Treat it as an Advanced/support action (v1+), not something Setup exposes as a casual toggle, and always force the same mandatory safety world backup before attempting it.

---

## 13. Bootstrap responsibilities: OpenTofu/user-data vs SSH scripts vs Manager

Three actors could plausibly "own" installing the game. This section assigns responsibility so future implementers do not duplicate or fight over this.

**Cloud-infra companion:** how OpenTofu is run (admin PC, not Resource Manager), which Ubuntu images to use, and how HCL is shipped/updated is defined in [`Automated-Infrastructure-Deployment.md`](Automated-Infrastructure-Deployment.md). This section only assigns the **game vs infra** boundary.

### 13.1 OpenTofu / cloud-init user-data — **infrastructure only, not the game**

**Decision: OpenTofu/user-data never installs Minecraft, a loader, Java, or a mod pack.** Its job stops at: create the VM, base OS, the `mcmgr` system user/group and directory tree (§5.1), install baseline OS packages (`unzip`, `curl`/`wget`, `jq`, the Adoptium apt repo registration), open the minimum firewalld rules matching the Security List, and drop an **empty** `/etc/mcmgr/` ready for a subsequent bootstrap step to populate. Reasoning: cloud-init/user-data runs once at instance creation and is awkward to safely re-run idempotently for anything version-sensitive (Mojang/Paper/loader version choice is a Setup-wizard-time decision, not a Terraform-apply-time constant); mixing "provision the VM" with "install the exact game version the user picked in the wizard 30 seconds ago" would also make user-data depend on live upstream API calls at `apply` time, which is fragile and hard to retry/resume (§14) compared to a dedicated SSH bootstrap step that Setup fully controls the timing of.

### 13.2 SSH bootstrap scripts — **the actual installer modules live here**

The per-distribution installer modules (Part B: `bootstrap-vanilla.sh`, `bootstrap-paper.sh`, `bootstrap-fabric.sh`, `bootstrap-neoforge.sh`, `bootstrap-forge.sh`, `bootstrap-quilt.sh`, `bootstrap-modpack-modrinth.sh`, `bootstrap-modpack-curseforge.sh`, `bootstrap-modpack-manual.sh`) are shell scripts (or a small Python helper reusing patterns already established in `vm_agent/`, if JSON/HTTP handling gets unwieldy in pure bash — either is acceptable per the product's existing "Python OK on VM1" stance) uploaded and executed over SSH by whichever orchestrator is driving Setup — matching the existing pattern documented in `Agent-Deploy-Pitfalls.md` (staging under a `ubuntu`-writable temp dir, LF-normalizing anything authored on Windows, one `sudo bash -c '...'` per privileged multi-step chain, stopping/starting the unit around any binary replace). Each module is responsible for exactly the "resolve version metadata → download/verify → place files → write the manifest fragment for its own fields" work described in Part B for its platform, and nothing else (it does not write `server.properties`, does not touch systemd, does not touch RCON — those are shared steps in the common bootstrap driver, run once regardless of which module executed).

### 13.3 Manager (`OCI-mc-server` / `McManager.Core`) — **orchestrates, never re-implements the modules**

The Manager's role is: collect the Setup wizard's choices, decide *which* bootstrap module to invoke and with what version/pack inputs, upload it (or a pinned version already baked into the installer as this product matures — see `docs/Agent-Deploy-Pitfalls.md` item 6 about stale `development/` trees for the cautionary tale of two copies of "the same" deploy code drifting), execute it over SSH, stream/relay progress and errors back into the wizard UI (mirroring the existing capacity-wait UX pattern already designed for OpenTofu apply — `PRODUCT-IDEAS.md` "Capacity" section), and on success, pull the resulting `game-manifest.json` back over SSH to populate the Object Storage `meta/infra.json` `game` summary block. **The Manager must never re-implement version resolution logic in C#** (e.g. re-parsing `piston-meta` itself with different logic than the on-box bootstrap script uses) — if the wizard needs a version *picker* before bootstrap even runs (MVP already needs this: "Setup lets the user pick the Minecraft version" per `PRODUCT-IDEAS.md`), the Manager calls the same upstream APIs documented in Part B **read-only**, for display purposes, and passes the user's chosen version id as a parameter into the bootstrap module, which re-resolves it independently on-box at execution time (so the authoritative URL/hash resolution always happens right before download, not from a possibly-stale value cached client-side minutes/hours earlier in the wizard session).

### 13.4 Summary table

| Actor | Owns | Never does |
|---|---|---|
| OpenTofu / user-data | VM, OS baseline, `mcmgr` user/dirs, firewalld baseline | Anything version-specific to a Minecraft/loader/pack choice |
| SSH bootstrap modules | Version resolution + download + verify + place files + manifest fragment, **per platform** | systemd unit writing, `server.properties`, RCON secret, Object Storage — those are shared/common steps or other actors' jobs |
| Manager | Wizard UX, invoking the right module with the right inputs, relaying progress, mirroring the resulting manifest into Object Storage | Re-implementing version-resolution HTTP/parsing logic that the on-box module already owns |

---

## 14. Failure recovery and resumability

### 14.1 Decision: an explicit bootstrap state file, not "re-run the whole script and hope it's idempotent"

`/var/lib/mcmgr/bootstrap-state.json` records, as a small ordered list, which **stages** of the current install/upgrade/rollback operation have completed:

```json
{
  "operation": "install",
  "started_at": "2026-08-11T18:00:00Z",
  "target_manifest_partial": { "distribution": "vanilla", "minecraft_version": "1.21.11" },
  "stages_completed": ["java_resolved", "artifact_downloaded", "artifact_verified"],
  "current_stage": "artifact_placed",
  "last_error": null,
  "updated_at": "2026-08-11T18:02:40Z"
}
```

Every module-level step in §12.1's first-boot sequence (and the equivalent upgrade/rollback sequences in §12.2/§12.4) corresponds to one `stages_completed` entry, appended **only after that stage's work is durably on disk and independently verifiable** (e.g. `artifact_verified` is only appended after the hash check in §3.5 passes — never optimistically before). A bootstrap driver re-invoked after a crash, a lost SSH session, or an operator-cancelled wizard step reads this file first and **skips already-completed stages**, re-attempting only from `current_stage` onward. This directly satisfies "Setup survives capacity wait and can resume" (already a stated MVP success criterion in `PRODUCT-IDEAS.md`/`MVP-Implementation-Plan.md`) extended to the game-install portion of Setup specifically, not just the OpenTofu-apply portion that criterion was originally written about.

### 14.2 Never leave a manifest that claims success prematurely

`game-manifest.json` itself is only written **once, at the very end**, after every stage in §14.1 has succeeded and the health check (§12.1 step 9) has passed. Any consumer that finds `game-manifest.json` present must be able to trust it completely — there is exactly one writer moment, not an incrementally-mutated-during-install document. This is why `bootstrap-state.json` is a **separate** file: it is allowed to be messy/partial/in-progress; the manifest is not.

### 14.3 Failure classification and operator-facing behavior

| Failure class | Example | Resumable automatically? | Operator-facing behavior |
|---|---|---|---|
| Transient network (upstream API/CDN unreachable) | `piston-meta` 5xx, Fill v3 rate-limited, Maven timeout (observed in NeoForge research, §19) | Yes — retry with backoff, same discipline as `docs/OCI-API-Usage.md` (few seconds → capped backoff, bounded attempt count) | Wizard shows "waiting on <upstream>, retrying" without abandoning wizard state |
| Hash/verification mismatch | SHA-1/SHA-256 does not match manifest metadata | No — must not silently retry-and-accept a mismatched file; delete the bad download and re-fetch **once** automatically, then surface an explicit error if it mismatches again | Clear "downloaded file failed integrity check" error, not a generic failure |
| Upstream metadata genuinely absent (e.g. no hash published at all for an installer, §3.5 `none_published`) | NeoForge installer jar | N/A — this is an expected, documented condition, not a failure | No error; recorded transparently in the manifest, not hidden |
| Pack resolution failure (missing/incompatible file for target loader+MC version) | Modrinth/CurseForge pack references a mod build that does not exist for the chosen loader version | No — abort before touching `server_dir` (§12.2's "no partial upgrades" rule) | Wizard/Manager shows exactly which pack entries failed to resolve |
| Health check failure (process starts but never becomes joinable) | Modded server crash-loops on world load | No — leave the **previous** working install/manifest in place (this is exactly what the `previous` snapshot + retention window in §12.4 is for), do not swap `game-manifest.json` to the new attempt | Surface the last N lines of `journalctl -u minecraft` (already a natural v1 "Console" tab feature, per `PRODUCT-IDEAS.md`) so the operator has an actionable crash reason, not just "install failed" |
| CurseForge/Modrinth ToS or licensing block | Pack marked non-redistributable / API key missing (§23) | No — this is a **policy** stop, not a technical retry case | Explicit legal/licensing message, distinct from a technical error |

### 14.4 Idempotency of each installer module

Every module in Part B is written so that **re-running it with the same inputs against an already-completed install is a safe no-op** (check "is the target file already present and hash-verified" before re-downloading; check "does `libraries/<mc>-<loader>/unix_args.txt` already exist" before re-running an installer jar). This property is what makes §14.1's "skip already-completed stages" resumability trustworthy — the state file is an optimization for skipping *redundant network calls*, but even without it, safely re-running a module from stage zero must never corrupt an already-good install. Both properties (state-file resumability *and* raw module idempotency) are required together — one masks bugs in the other if only one is tested.

---

## 15. Offline fixtures and tests for version metadata

### 15.1 Why offline fixtures matter here specifically

Every artifact-acquisition module in Part B depends on a live third-party HTTP API. Unit/integration tests for "does our manifest-building logic produce the right `launch_command` for a `argfile_tree` NeoForge install" must not require network access to Mojang/PaperMC/Fabric/NeoForge/Modrinth/CurseForge — both for CI reliability and because several of these APIs (CurseForge especially, §23) are access-controlled or rate-limited in ways a test suite should never stress.

### 15.2 Fixture set to check into the repo

Store recorded (and where necessary, hand-trimmed to remove irrelevant noise) upstream response bodies under a test-fixtures directory (e.g. `OCI-mc-server/tests/fixtures/game-metadata/`), one file per upstream call shape, **not** per Minecraft version (a handful of representative versions per platform is enough — the parsing logic being tested does not change per version):

| Fixture file | Represents |
|---|---|
| `mojang-version-manifest-v2.json` | Trimmed `piston-meta` `version_manifest_v2.json` (a handful of entries, not the full multi-hundred-version list) |
| `mojang-version-metadata-1.21.11.json` | One full per-version metadata document (§16.3 shape) |
| `paper-fill-v3-builds-1.21.10.json` | One `.../versions/{mc}/builds` response including a `STABLE` and a non-stable entry, to test channel filtering |
| `fabric-meta-loader-versions.json` | `/v2/versions/loader` response |
| `fabric-meta-installer-versions.json` | `/v2/versions/installer` response |
| `neoforge-maven-metadata.xml` | Trimmed `maven-metadata.xml` |
| `forge-promotions-slim.json` | Full real `promotions_slim.json` (small enough to keep whole; also doubles as a regression fixture for the Java-floor-by-version table in §9.1 since it is a good cross-check of "did this MC version ever have a Forge build") |
| `modrinth-index-neoforge-sample.json` | A representative `modrinth.index.json` with a mix of `env.server` = `required`/`optional`/`unsupported` entries |
| `curseforge-manifest-sample.json` | A representative CurseForge `manifest.json` (public sample data only — never a real, licensed pack's manifest) |

### 15.3 What the tests must assert

- **Parsing correctness:** given a fixture, the resolver produces the exact `server_artifact`/`artifact_hash`/`java_major` fields expected — this is a pure function test, no network, deterministic.
- **Manifest fixture round-trip:** every fixture in §4 (the four worked examples) parses back into the same schema without loss, and — critically — a **schema-validation test** (JSON Schema or equivalent) asserts every field in §3's reference is present with the right type, so a future accidental field rename is caught immediately rather than discovered when an on-box script reads a manifest written by a slightly different Manager version.
- **Negative cases:** a fixture with a missing/malformed hash field, a fixture representing a Fill v3 error payload (`{"ok": false, "message": "..."}` — confirmed real shape from the Downloads Service docs, §17), and a mrpack fixture where every file is `env.server: unsupported` (should resolve to zero server-side mods, not an error) all have dedicated tests.
- **Live smoke test, separate and explicitly optional:** a small, manually-triggered (not part of default CI) script that hits the real upstream APIs once and diffs their current shape against the checked-in fixtures, so upstream breaking changes (like the Fill v2→v3 migration this research had to account for) are caught by a deliberate, occasional check rather than by CI flakiness or, worse, silently by a confused operator's failed Setup run months later.

### 15.4 Operator-local sample packs (not CI)

Checked-in fixtures in §15.2 stay **synthetic / trimmed / public-sample** — never a real licensed pack's bytes or CurseForge API cache.

Separately, this operator PC may keep a **gitignored** folder `data/sample-packs/` (homemade tiny archives + a few real published `.mrpack` / CurseForge export zips) for Phase 4 analyze/install smoke. That folder is **not** part of the product tree and **must not** be committed (PRODUCT-IDEAS: do not redistribute pack contents). Agent instructions, gotchas (Fabulously Optimized / OptiFine for Fabric mis-tag `env.server`; Infinite Horizons is too large for routine tests), and the rule **pause and ask the operator if a needed pack type is missing**: [`Sample-Packs.md`](Sample-Packs.md). Default CI must keep working on a clone that does not have `data/sample-packs/`.

---

# Part B — Per-platform artifact acquisition

Every section below follows the same structure: **API/mechanism**, **integrity verification**, **launch shape**, **ARM64/Java notes specific to this platform**, and **staging classification**. Part A's architecture (manifest schema, directory layout, systemd generation, bootstrap responsibility split) applies uniformly; this part is the platform-specific research that feeds each installer module's inputs/outputs.

## 16. Vanilla (Mojang piston-meta)

**Stage: MVP.**

### 16.1 API

```text
GET https://piston-meta.mojang.com/mc/game/version_manifest_v2.json
```

Response shape (abridged):

```json
{
  "latest": { "release": "1.21.11", "snapshot": "26.2-snapshot-3" },
  "versions": [
    { "id": "1.21.11", "type": "release", "url": "https://piston-meta.mojang.com/v1/packages/<sha1>/1.21.11.json", "time": "...", "releaseTime": "...", "sha1": "<sha1-of-this-json>", "complianceLevel": 1 }
  ]
}
```

`piston-meta.mojang.com` is canonical in 2026; the legacy `launchermeta.mojang.com` host still redirects/works but new code should not target it. Prefer **v2** specifically because it carries `sha1` per version-JSON — useful for verifying the metadata document itself, distinct from the server jar's own hash below.

### 16.2 Version id format has changed shape over time — do not assume `N.N.N`

Mojang's own manifest already contains ids like `"26.2"`/`"26.1-snapshot-1"` (a year/month-influenced scheme) alongside legacy `"1.21.11"`-style ids. **The version picker (Manager/Setup UI) and every downstream string comparison must treat `id` as an opaque string, sourced live from the manifest, never pattern-matched or hard-coded** — this was already the right call in the existing PRODUCT-IDEAS guidance ("Mojang version ids evolve... always drive the picker from the live manifest") and this research reconfirms it is still true and, if anything, more important now that the scheme has visibly started shifting.

### 16.3 Resolve → download → verify

```text
GET <versions[i].url>              # per-version metadata JSON
```

Relevant fields:

```json
{
  "downloads": {
    "server": { "url": "https://piston-data.mojang.com/v1/objects/<sha1>/server.jar", "sha1": "<sha1>", "size": 12345678 }
  },
  "javaVersion": { "component": "jre-legacy", "majorVersion": 21 }
}
```

```text
GET <downloads.server.url>   →  server.jar
sha1sum server.jar           →  compare to downloads.server.sha1; abort + retry-once-then-error on mismatch (§14.3)
```

`javaVersion.majorVersion` feeds `java_major` directly (§9.1) — very old versions (pre-1.17ish) may omit this field; the static fallback table in §9.1 covers that case, though such old Vanilla versions are not a realistic product target anyway.

### 16.4 Launch shape

`server_artifact.kind = "single_jar"`; `launch_command.args = [-Xms.., -Xmx.., ...gc flags..., -jar, server.jar, nogui]`. First run without `eula=true` present will generate `eula.txt` and exit immediately — bootstrap must write `eula.txt` **before** the first real start (§7.2), not rely on the server's own generate-and-exit behavior, so the health check in §12.1 step 9 does not misinterpret an EULA-exit as a crash (both look like "process exited quickly," but a pre-written EULA turns this into a non-issue rather than a case bootstrap has to special-case).

---

## 17. Paper / Optimized Vanilla (Fill v3)

**Stage: v1** (provisional priority per the operator's prompt; confirmed reasonable by this research — see §29).

### 17.1 API — Fill v3 only; v2 is dead as of this writing

`api.papermc.io` (Fill v2) stopped receiving new builds 2025-12-31 and was **fully disabled 2026-07-01** — any code still targeting it is not "deprecated," it is **broken**, as of the date this document was written (2026-08-11). All new work must target `fill.papermc.io` (Fill v3) exclusively. `PRODUCT-IDEAS.md`'s existing Paper guidance already correctly pointed at Fill v3; this research upgrades that from "prefer v3" to "v2 no longer works at all, there is no fallback."

```text
GET https://fill.papermc.io/v3/projects/paper/versions/{minecraftVersion}/builds
Header: User-Agent: <product-name>/<version> (<contact-url-or-email>)
```

The `User-Agent` requirement is **enforced**, not a courtesy suggestion (confirmed from PaperMC's own docs: "All requests must now include a valid User-Agent header" that is not a generic default like `curl`/`wget` and includes contact info) — bootstrap's HTTP client must set this explicitly, e.g. `mcmgr-bootstrap/0.1.0 (https://github.com/<org>/<repo>)`, or requests may be rejected/rate-limited.

Response (array of build objects, newest typically first depending on query):

```json
[
  {
    "id": 48,
    "time": "2026-08-01T12:00:00.000Z",
    "channel": "STABLE",
    "commits": [{ "sha": "...", "time": "...", "message": "..." }],
    "downloads": {
      "server:default": {
        "name": "paper-1.21.10-48.jar",
        "url": "https://fill-data.papermc.io/v1/objects/<sha256>/paper-1.21.10-48.jar",
        "checksums": { "sha256": "<sha256>" },
        "size": 54185955
      }
    }
  }
]
```

**Channels** (overhauled in the v2→v3 migration): `ALPHA` (early/unstable) → `BETA` (feature-complete, may have bugs) → `STABLE` (production-ready) → `RECOMMENDED` (currently Velocity-only, not guaranteed present for Paper). **Always filter to `channel == "STABLE"`** and pick the highest `id` in that channel; never install `ALPHA`/`BETA` automatically. If the user's chosen Minecraft version has no `STABLE` build yet (can happen right after a fresh Minecraft release, before Paper has caught up), surface that clearly rather than silently falling back to an unstable channel — this is a real, documented possibility per the reference shell script in PaperMC's own docs, which explicitly handles "no stable build for this version" as a normal, expected branch, not an error.

Error responses use `{"ok": false, "message": "..."}` — check `.ok` before assuming the array shape.

### 17.2 Integrity: SHA-256, not SHA-1

Paper via Fill v3 publishes **SHA-256** checksums (a deliberate difference from Mojang's SHA-1) — the manifest's `artifact_hash.algorithm` field exists specifically so downstream verification code is not hard-coded to one algorithm; the installer module for Paper sets `algorithm: "sha256"`.

### 17.3 Version-level metadata worth carrying through

Fill v3 additionally publishes, per the v2→v3 migration announcement: version support status (`SUPPORTED`/`DEPRECATED`/`UNSUPPORTED`), a support end date, a **minimum required Java version**, and **recommended JVM flags** — all *per Minecraft version*, at the project level (not the individual build level). **Recommendation:** the Paper installer module should fetch this project/version metadata once, use `minimumJavaVersion` to override the static §9.1 table when present (Paper's own floor can be stricter than the generic Mojang floor — confirmed by research showing "PaperMC 1.21.8+ requires Java 21" messaging even where a slightly older Vanilla-only chart might have implied 17 was still viable), and store any `recommendedJvmFlags` as the **starting point** for `launch_command.args`' memory/GC flags rather than this product inventing its own G1GC tuning from scratch.

### 17.4 Launch shape

`server_artifact.kind = "single_jar"`; Paper's own documented examples use `--nogui` (double-dash) — confirm this exact flag form is what the unit generator emits (§6.3) rather than assuming it is interchangeable with Vanilla's bare `nogui`.

### 17.5 Plugins vs mods

Paper's extensibility mechanism is **plugins** (Bukkit/Spigot API, dropped into `plugins/`), not Forge/Fabric-style mods — this product's "Optimized Vanilla" framing in `PRODUCT-IDEAS.md` currently does not commit to shipping a plugin-management UI in v1, and this research does not recommend rushing one in; Setup installing bare Paper (zero plugins, just the performance/bug-fix benefit over Vanilla) is a complete, useful v1 feature on its own, matching the "Not the same as a Fabric/Forge modpack—Paper is a server implementation" framing already in PRODUCT-IDEAS.

### 17.6 The Paper fork ecosystem — named for completeness, Paper itself remains the recommendation

Paper is not the only "Optimized Vanilla" option; it is the **root** of an actively-maintained fork ecosystem confirmed current via `itzg/docker-minecraft-server`'s own server-type documentation (a good signal of real-world adoption, since that project only documents software people actually run at scale):

| Fork | Positioning | Notes for this product |
|---|---|---|
| **Purpur** | "A drop-in replacement for Paper... configurability and new, fun, exciting gameplay features" | The most broadly popular Paper fork; adds config-gated gameplay tweaks on top of Paper/Bukkit compatibility. **Worth evaluating as a second "Optimized Vanilla" option at v1 implementation time** — same `single_jar` launch shape and plugin model as Paper, so it fits this blueprint's architecture with no new `server_artifact.kind`. Its own version-resolution API was not part of this research pass; confirm before implementing. |
| **Pufferfish** | "Highly optimized Paper fork... for large servers requiring maximum performance, stability, and enterprise features" | Positioned for high player counts, less relevant to this product's small-friend-group scale — **later**, not v1. |
| **Leaf** | Paper fork focused on low-level performance optimization | Similar niche to Pufferfish; **later**. |
| **Folia** | Paper's own **regionized multithreading** fork — splits the world into independently-ticked regions to scale across cores | Explicitly **experimental only** as of this research (no stable release channel, per `itzg`'s docs) and a materially different runtime model (per-region threading changes how some plugins/mods behave). **Do not build on Folia** until it ships a stable channel — track it as a **later** re-evaluation candidate, not a v1 option, given this product's modest 2–4 OCPU Ampere target where multi-region threading benefit is unproven and added complexity is not currently justified. |

**Recommendation:** keep Paper as the sole "Optimized Vanilla" implementation for v1 (simplest, most conservative, best-documented API per §17.1–§17.3); revisit Purpur specifically as a v1-or-shortly-after addition once Paper's own path is proven, precisely because it shares Paper's exact launch/plugin model and would not require new architecture — just a second entry in the platform picker and its own version-resolution research.

---

## 18. Fabric

**Stage: v1 candidate** (per operator's provisional list; confirmed reasonable — see §29).

### 18.1 API — `meta.fabricmc.net`, three version axes

Fabric uniquely separates **game version**, **loader version**, and **installer version** — all three are required to build a server download URL, and it is a common, confirmed-in-the-wild integration bug to omit the third:

```text
GET https://meta.fabricmc.net/v2/versions/loader                       # all loader versions, newest first, "stable" flag
GET https://meta.fabricmc.net/v2/versions/installer                    # all installer versions, newest first, "stable" flag
GET https://meta.fabricmc.net/v2/versions/loader/{game_version}        # loader versions valid for a game version
GET https://meta.fabricmc.net/v2/versions/loader/{game_version}/{loader_version}/server/json   # launcher JSON (for inspection/testing only)
GET https://meta.fabricmc.net/v2/versions/loader/{game_version}/{loader_version}/{installer_version}/server/jar   # the actual downloadable launcher jar
```

Resolution algorithm for the installer module: fetch `/v2/versions/installer`, pick the first entry with `"stable": true` (list is newest-first); fetch `/v2/versions/loader/{game_version}`, pick the first `"stable": true` entry (or let the user/pack pin an exact `loader_version` — packs frequently do); then build the final `/server/jar` URL with all three resolved values.

### 18.2 Integrity

Fabric's meta API does **not** publish a checksum for the assembled server launcher jar in the response (confirmed by inspecting the documented response shapes — no `sha1`/`sha256` field is present anywhere in the loader/installer/server endpoints). `artifact_hash.algorithm = "none_published"` is the correct, honest manifest value here — do not fabricate a hash by computing one locally and calling it "verified" (that only proves the download did not get corrupted in transit, which TLS already provides; it is not upstream-attested integrity, and the manifest schema's distinction exists precisely so this difference is not lost).

### 18.3 Launch shape

`server_artifact.kind = "launcher_jar"`; the downloaded jar **is** directly runnable (`java -jar fabric-server-mc.<mc>-loader.<loader>-launcher.<installer>.jar nogui`) — Fabric deliberately kept the "one jar, one command" simplicity that Forge lost at 1.17, which is one reason Fabric is a strong v1 candidate from an implementation-effort standpoint, not just a mod-ecosystem standpoint.

### 18.4 Quilt is launched identically, from a different meta host

Quilt Loader intentionally mirrors Fabric's launcher-jar approach closely enough that the installer module structure is nearly identical, pointed at `meta.quiltmc.org` instead — see §21 for why Quilt itself is still classified as a **later**, not v1, priority despite the low *implementation* cost.

---

## 19. NeoForge

**Stage: v1 candidate** (per operator's provisional list; confirmed reasonable — see §29).

### 19.1 No JSON version manifest — Maven metadata is the only source of truth

Confirmed directly from a NeoForge maintainer (GitHub discussion, 2026): *"At the moment, we do not yet have a versions or metadata API with JSON information. We commonly recommend users and developers to check the Maven metadata instead as a singular source of truth."*

```text
GET https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml
```

This is a plain Maven XML document (`<versioning><versions><version>21.1.98</version>...</versions></versioning>`) — the installer module must parse XML here, not JSON, which is worth calling out explicitly since every *other* platform in this document is JSON-first. Versions are **not** simple semver against a Minecraft version string; NeoForge's own versioning scheme is `<mc_minor>.<mc_patch>.<build>[-beta]` (e.g. `21.1.98` for Minecraft `1.21.1`) — deriving "which NeoForge versions target Minecraft 1.21.1" means string-matching the `21.1.` prefix against the requested Minecraft version's minor/patch, which is workable but brittle across format changes; treat this parsing logic as a single, well-tested, isolated function precisely because it is the most upstream-format-fragile part of this entire document.

### 19.2 Minimum supported Minecraft version: **1.20.2**, not 1.20.1

Confirmed by a NeoForge maintainer directly: 1.20.1 NeoForge builds were published briefly, then the **public download for 1.20.1 NeoForge was removed**, and the team explicitly recommends **Forge** for 1.20.1 packs. NeoForge's real, supported floor for new work is **Minecraft 1.20.2**. **This document's installer module must refuse (or at minimum, strongly warn) an attempt to bootstrap NeoForge for Minecraft 1.20.1 or older** — a well-intentioned "just try the API" attempt will likely fail outright (metadata absent) since the 1.20.1 artifacts were removed from public download.

### 19.3 Download and install: installer jar, no published checksum

```text
GET https://maven.neoforged.net/releases/net/neoforged/neoforge/{version}/neoforge-{version}-installer.jar
java -jar neoforge-{version}-installer.jar --installServer
```

No first-party checksum is published alongside the installer jar in a machine-discoverable way (confirmed — no `.sha256`/`.sha1` sibling documented anywhere in the researched install flow). `artifact_hash.algorithm = "none_published"` again, same reasoning as §18.2. The **update** path is identical to the initial install path — re-run the new version's installer over the existing directory (§12.2); NeoForge's own docs state this explicitly ("simply download and run the installer for the new version... The installer will automatically replace uses of the old version where needed").

### 19.4 Launch shape

`server_artifact.kind = "argfile_tree"`; installer output includes `run.sh`/`run.bat` (informational, not executed by the systemd unit — §6.4), `user_jvm_args.txt`, and `libraries/net/neoforged/neoforge/{version}/unix_args.txt`. Java floor per §9.1's table (17 for 1.20.2–1.20.4, 21 for 1.20.5–1.21.11, 25 for 26.1+).

### 19.5 Network reliability caveat (observed directly in research)

A real, documented bug report (itzg's `mc-image-helper` issue tracker) shows a NeoForge Maven-metadata fetch timing out under poor network conditions, producing a confusing downstream error ("Unexpected format of id from Forge installer's version.json") rather than a clean "network timeout" message, and the maintainers' own guidance was "you have network quality issues." **Recommendation:** the NeoForge installer module should use a generous, explicit HTTP timeout and retry policy (matching the transient-failure handling already designed in §14.3) and should surface a *specific* "could not reach maven.neoforged.net" error rather than letting a raw parse failure propagate — this is exactly the class of "discovered failure mode we should design for up front, not rediscover after an operator hits it" this document exists to prevent (see the workspace's own `Agent-Deploy-Pitfalls.md` philosophy, applied here to a different upstream).

---

## 20. Forge, including legacy versions

**Stage: v1 candidate, retained specifically for legacy-version packs** (per operator's provisional list; confirmed by this research — see §29 for the exact reasoning on *why* "retained for older packs" rather than "retained generally").

**A note on automating around Forge's ad-supported download page:** the official Forge installer itself displays a request to third-party tooling authors: *"Please do not automate the download and installation of Forge. Our efforts are supported by ads from the download page. If you MUST automate this, please consider supporting the project [through Patreon]."* This product's entire value proposition depends on unattended install/upgrade, so full compliance (never automating) is not viable — the same tension every comparable tool (including `itzg/docker-minecraft-server`, which documents the request verbatim and "passes it along") already lives with. **This product should not pretend the tension doesn't exist.** Concrete, low-cost mitigations to carry into Setup implementation (v1, not a blocker for this document): (a) surface a courtesy link to Forge's Patreon/support page in Setup copy whenever a user selects Forge, mirroring `itzg`'s own approach; (b) prefer NeoForge by default for any Minecraft version where it is the maintained option (§19.2, §29) so Forge automation is scoped to the legacy catalog it is actually needed for, not used as a blanket default; (c) do not scrape the ad-supported HTML download page itself — `promotions_slim.json` (§20.1) and the Maven artifact URLs (§20.2) are Forge's own published machine-readable endpoints, not the ad-supported page, which is a meaningfully smaller ask than scraping page HTML would be, but is still automation the maintainer has asked tooling authors to reconsider.

### 20.1 Version discovery: `promotions_slim.json`

```text
GET https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json
```

This is a small, complete, scrapable JSON map from `"{mc_version}-latest"`/`"{mc_version}-recommended"` to a Forge version string, going all the way back to Minecraft `1.1`. **This research fetched the live document directly** (2026-08-11) and confirmed it is current, well-formed, and covers modern versions correctly (e.g. `"1.21.11-latest": "61.2.0"`, `"26.2-recommended": "65.1.0"`), including Mojang's newer `26.x` version-id scheme. **Always prefer `-recommended` over `-latest` when both exist** for anything Setup installs automatically without the user pinning an exact build — `-latest` can be a very recent, less-battle-tested build; `-recommended` is Forge's own "this is the one to use" signal. Some Minecraft versions only have a `-latest` entry (no `-recommended` published) — fall back to `-latest` only in that case, and record which one was used.

### 20.2 Download and install

```text
GET https://maven.minecraftforge.net/net/minecraftforge/forge/{mc}-{forge}/forge-{mc}-{forge}-installer.jar
java -jar forge-{mc}-{forge}-installer.jar --installServer
```

No first-party published checksum for the installer jar was found during this research (same situation as NeoForge, §19.3) — `artifact_hash.algorithm = "none_published"`.

**Known legacy-version gotcha (confirmed via the Minecraft Wiki's own Forge server tutorial):** some older Forge installer versions require a **pre-existing Vanilla `server.jar` already present** in the target directory before the Forge installer will run cleanly (the wiki explicitly documents an "invalid e-tag checksum" failure mode and the fix: install Vanilla first, accept its EULA, *then* run the Forge installer). The Forge installer module should therefore **always run the Vanilla installer module first** (§16) as a prerequisite step, unconditionally, for any Forge version — this is cheap (Vanilla installs in seconds) and sidesteps a whole class of legacy-version install failures rather than special-casing "which old Forge versions need this."

### 20.3 The 1.17 launch-mechanics discontinuity

**This is the single most important Forge-specific fact for this document's architecture.** Forge versions for Minecraft **1.16.5 and earlier** produce a `kind = "single_jar"` result — a directly runnable `forge-{mc}-{forge}.jar` (or, for very old 1.5-and-earlier versions, an even simpler standalone jar per the Wiki's "1.5 and prior" section). Forge versions for Minecraft **1.17 and later** produce a `kind = "argfile_tree"` result, identical in shape to NeoForge's (§19.4) — `java @user_jvm_args.txt @libraries/net/minecraftforge/forge/{mc}-{forge}/unix_args.txt "$@"`, because NeoForge inherited this exact mechanism when it forked from Forge in 2023. **The manifest schema's `kind` discriminator (§3.4) exists specifically because of this one platform's mid-lifecycle mechanics change** — any implementation that assumed "Forge = always a single jar" (a reasonable assumption if only looking at 1.12.2-era tutorials, which remain extremely common in search results and community guides) would silently break the moment a user picked a modern Minecraft version.

### 20.4 Legacy version support matrix (informational, confirms "retained for older packs")

| Minecraft range | Forge launch shape | Java floor | Product relevance |
|---|---|---|---|
| 1.1 – 1.6.4 | single jar, pre-`--installServer` era conventions | 6/8 | Not a realistic product target; listed only because `promotions_slim.json` covers it |
| 1.7 – 1.12.2 | single jar via `--installServer` | 8 | **This is where "retained for older packs" earns its keep** — a large fraction of long-lived CurseForge modpacks (classic "kitchen sink" packs, tech packs from the 2016-2019 era) are pinned to 1.12.2 specifically and have no NeoForge equivalent (NeoForge did not exist yet) |
| 1.13 – 1.16.5 | single jar via `--installServer` | 8 | Same reasoning as above, slightly newer packs |
| 1.17 – 1.17.1 | `argfile_tree` begins | 16 | Narrow, awkward band — Java 16 specifically, rarely targeted by modern packs |
| 1.18 – 1.20.1 | `argfile_tree` | 17 | **1.20.1 is the other place Forge is specifically still relevant** — this is the version NeoForge itself says to use Forge for (§19.2), because it was the last shared version before the NeoForge fork and still has an enormous, actively-played modpack catalog |
| 1.20.2+ | `argfile_tree`, but NeoForge is preferred here | 17/21/25 | Both loaders technically exist; product should default new modded installs at 1.20.2+ to **NeoForge**, only offering Forge here for a pack that specifically requires it |

### 20.5 ARM64/native risk specific to old Forge (see §27 for the general treatment)

Forge itself (the loader) bundles no native binaries for any version researched — the server-side ARM64 risk for legacy Forge packs is **entirely a function of which mods are in the pack**, not the loader. Old 1.7.10/1.12.2-era mods are, if anything, *lower* risk than modern ones for native-library issues (native/JNI-heavy mod patterns like bundled voice chat or physics engines are a more recent trend), but conversely these old packs are the ones most likely to have been abandoned by their authors, so there is no expectation of an aarch64-native fix ever shipping if one *is* found — a legacy pack that hits a native-library wall is closer to "permanently unsupported on this architecture" than "wait for an upstream fix" (§27.4).

---

## 21. Quilt

**Stage: later** (this research downgrades Quilt from the operator's provisional "v1" framing to **later** — see the reasoning below and the summary in §29).

### 21.1 Why "later" rather than "v1 candidate," despite low implementation cost

Implementation cost for Quilt is genuinely low — it mirrors Fabric's launcher-jar API shape closely (a `meta.quiltmc.org` equivalent of §18.1), and a Quilt installer module would be a near-copy of the Fabric one. The reason this research recommends **later** rather than matching Fabric's v1 priority is about **product value versus maintenance surface**, confirmed directly from Quilt's own 2026 messaging:

- Quilt **retired its own standard-library ecosystem** (Quilt Standard Libraries, Quilted Fabric API) in early 2026, citing exactly the maintenance burden this product should avoid taking on by proxy: "the work to maintain these libraries has largely fallen on one sole developer... QSL as incompatible with FAPI... has proven to be extremely difficult to maintain long-term." Quilt's own team chose to become "a Fabric-compatible loader with different governance" rather than a distinct technical ecosystem.
- Quilt's own FAQ confirms: *"Quilt can't load Forge or Neoforge mods and support is not planned,"* and *"a mod may be compatible with both Fabric and Quilt, but a mod which is made for Quilt won't work on Fabric"* — i.e. the compatibility direction is asymmetric and Quilt-exclusive content is rare.
- Every third-party 2026 comparison found during research (mod-loader comparison articles from hosting providers, kept as general market-sentiment signal only, not authoritative sources) converges on the same framing: choose Quilt only if a specific mod is Quilt-exclusive, or for governance/community preference — never for a capability Fabric lacks.
- There is no popular, well-known modpack *platform-wide default* built on Quilt the way there is for Fabric (performance-focused packs) or NeoForge/Forge (content-heavy packs) — so a Setup wizard offering "Quilt" as a peer choice next to Fabric/NeoForge/Forge would be offering a rarely-needed option at the cost of one more installer module, one more entry in every compatibility check (§26), and one more thing to keep working across upstream API changes, for a sliver of actual demand.

**Recommendation:** if a v1/later user-uploaded pack (manual upload path, §24) happens to declare a `quilt-loader` dependency in its `modrinth.index.json` (the format explicitly supports this — confirmed in §22.1's dependency-object fields), honor it by installing Quilt Loader rather than rejecting the pack — the *manual-upload* and *Modrinth-pack* pathways should support Quilt as a **detected loader value**, even before a dedicated "Quilt" button exists anywhere in Setup's UI. This is a cheap, low-risk way to not lock out Quilt-pack users while still not committing scarce v1 engineering time to promoting it as a first-class Setup choice.

---

## 22. Modrinth modpacks and API

**Stage: v1 candidate** (best-behaved of the pack-source options; confirmed by this research to be the right one to build first if only one pack source ships in v1).

**Reminder (§2.4): this is a file-import format, not a browse/search integration.** The user obtains a `.mrpack` from Modrinth (via its website's own download button, or the Modrinth App export, or a modpack-manager tool of their choosing) and drags/uploads that file into Setup. Nothing below implies or requires a "search Modrinth from inside our app" feature.

### 22.1 The `.mrpack` format

An `.mrpack` is a ZIP file (MIME type `application/x-modrinth-modpack+zip`) whose root contains `modrinth.index.json`:

```json
{
  "formatVersion": 1,
  "game": "minecraft",
  "versionId": "1.0.0",
  "name": "Example Tech & Exploration",
  "summary": "A sample modpack",
  "files": [
    {
      "path": "mods/examplemod.jar",
      "hashes": { "sha1": "93d6c1f9a0c18c8d1b6ae114f26fd1b2766e9ff4", "sha512": "..." },
      "env": { "client": "required", "server": "required" },
      "downloads": ["https://cdn.modrinth.com/data/AAAAAAA/versions/1.0.0/examplemod.jar"],
      "fileSize": 1234567
    },
    {
      "path": "mods/clientside-minimap.jar",
      "hashes": { "sha1": "...", "sha512": "..." },
      "env": { "client": "required", "server": "unsupported" },
      "downloads": ["..."],
      "fileSize": 456789
    }
  ],
  "dependencies": { "minecraft": "1.21.1", "neoforge": "21.1.98" }
}
```

`env.server` (and `env.client`) is one of `required` / `optional` / `unsupported` **per file** — this is the single most valuable property of this format for automated server-side install: **the pack itself tells you which files belong on the server.** The installer module's file-selection rule is simply: install every file where `env.server != "unsupported"` (i.e. `required` or `optional`); record every file where `env.server == "unsupported"` into the manifest's `modpack.excluded_client_only_files` (§3.7) for transparency.

**Correction from an earlier draft of this document, confirmed by cross-checking `itzg/docker-minecraft-server`'s Modrinth-modpack docs:** this format's `env.server` field is authoritative for *intent*, but pack authors still sometimes mis-declare a client-only file as server-compatible in practice — `itzg`'s tooling ships `MODRINTH_EXCLUDE_FILES`/`MODRINTH_FORCE_INCLUDE_FILES` override variables specifically to correct this, on top of trusting `env.server`. **So: Modrinth packs need meaningfully *less* heuristic guessing than CurseForge (§23.3) — trust `env.server` first — but still benefit from the same maintained override-list mechanism described in §24.3, not zero heuristics as an earlier draft of this section implied.**

`dependencies` gives the loader + loader version + Minecraft version directly (`fabric-loader`, `quilt-loader`, `forge`, or `neoforge` as keys) — this maps straight onto the manifest's `loader`/`loader_version`/`minecraft_version` fields with no guesswork.

**Overrides:** `overrides/` (applied to both client and server), `server-overrides/` (applied only server-side, layered on top of `overrides/`, "to eliminate the need for server packs" per Modrinth's own docs), and `client-overrides/` (client-only) are copied into the instance directory as plain files/config — the installer module copies `overrides/` then `server-overrides/` (in that order, so server-overrides wins on conflict) into `server_dir`, skipping `client-overrides/` entirely.

### 22.2 The project/version lookup API exists upstream but is intentionally not used for pack selection

Modrinth also exposes:

```text
GET https://api.modrinth.com/v2/project/{id_or_slug}
GET https://api.modrinth.com/v2/version/{version_id}       # returns file list incl. primary .mrpack download URL
GET https://api.modrinth.com/v2/project/{id_or_slug}/version   # list versions
```

This is the API a "browse/search/pick a pack by name" feature *would* be built on — and per §2.4, this product deliberately does not build that feature, so **the installer module never calls these endpoints to let a user choose a pack.** The only Modrinth-hosted content this product's installer module fetches is the individual mod/config files already referenced by URL inside a `.mrpack` the user uploaded (§22.1) — plain file downloads from `cdn.modrinth.com`, not catalog API calls. This section is recorded here only so a future implementer sees this endpoint, recognizes what it is for, and does not accidentally reintroduce a picker while "just adding a convenience feature."

Modrinth's API is unauthenticated for public read access (no API key required, confirmed by the reference scripts found during research using plain unauthenticated `wget`/`curl`) — worth noting for completeness, but not a reason to use it for selection; it only means that *if* this product ever needed metadata lookups for some other reason (e.g. displaying a "this pack has a newer version available" notice in Server Management, a **later**-stage day-2 feature, not Setup selection), there would be no API-key gate to design around, unlike CurseForge (§23).

### 22.3 Hash verification

Every file entry carries both `sha1` and `sha512` — prefer `sha512` when available (Modrinth's own format spec lists it, and it is the stronger guarantee); fall back to `sha1` if `sha512` happens to be absent for a particular file entry (both are optional per-file per the schema, though in practice CDN-hosted files carry both).

---

## 23. CurseForge modpacks, API, and licensing

**Stage: v1 ships CurseForge as a *file format* only (Server Files / filled zip via §24). API client-export resolve is deferred** (operator 2026-08-18). The research below still describes how an API path would have to work if it is ever reopened.

**Reminder (§2.4): this is file-import support, not a CurseForge browse/search integration.** The user exports/downloads a modpack (ideally its published "server pack," see §24.1) from CurseForge's own website/app and uploads that archive into Setup. v1 does **not** call the CurseForge API. If an API path is ever reopened, every call described below exists solely to resolve **download URLs for files that archive's own `manifest.json` already names** — this product still never calls a CurseForge search/listing endpoint, never presents CurseForge pack results in its UI, and never lets a user pick a pack by ID/name from inside this app.

### 23.1 API key is now mandatory, not optional

CurseForge announced API-key authentication for direct CDN file downloads (`edge.forgecdn.net`) as **optional starting mid-2026, becoming required from 2026-07-16 onward** — meaning as of this document's writing (2026-08-11), **unauthenticated CurseForge CDN downloads are already failing with `401 Unauthorized`.** Any implementation must apply for a CurseForge for Studios developer key (`x-api-key` header, preferred over the `?api-key=` query-parameter form specifically because query parameters leak into logs/referrers) before this integration can function at all — this is now a **hard prerequisite**, not a nice-to-have, and the application/approval process is manual (a form reviewed by Overwolf's team, considering "impact on Authors' earnings," "effect on CurseForge's servers/CDN," and "Authors' consent... for third party distribution").

**Prior art confirming the "one product-owned key" design (§23.2's last bullet) is workable:** `itzg/docker-minecraft-server` — a much larger-scale consumer of this API than this product will be for the foreseeable future — ships a **key it obtained itself, bundled into its own image**, with a documented escape hatch for a user to supply their own instead. This is a direct, real-world precedent for "the product applies for and holds exactly one key; individual deployments never need their own" (this product's own plan per §23.2), and a reminder that the request/approval process is a known, survivable step other real projects have already been through successfully.

### 23.2 Terms of Service restrictions that constrain this product's design

The CurseForge 3rd Party API Terms and Conditions (confirmed by reading the actual ToS text during this research, not a summary of it) impose several restrictions directly relevant to how this product may use the API:

- **No caching/saving API data:** *"shall not... save or cache any data obtained through the API or SDK."* This means the product **cannot** build a local mirror/cache of CurseForge pack metadata for offline browsing the way it reasonably could for Modrinth (whose API has no such restriction) — every CurseForge-sourced lookup must be a live call at the moment it is needed, which has real implications for Setup wizard UX (no "browse packs offline once fetched" convenience) and for the offline-fixture testing strategy in §15 (fixtures there are for **testing our own parsing code**, never for shipping a real cached CurseForge response inside the product).
- **No proxying/concealing origin:** *"shall not... conceal your identity or geographic location when accessing the API, including accessing the API through a proxy server or VPN."* Irrelevant to normal on-box bootstrap calls (which originate directly from VM1's own IP), but rules out any future design where a shared product-operated relay service fetches CurseForge data on behalf of many users' VMs from one central IP — each user's own VM1 must make its own direct call.
- **No building a competing product:** *"Developer shall not use the API or SDK... in order to build any product or service that competes, directly or indirectly, with CF, CurseForge for Studios, or the Platform."* This product is a **Minecraft server manager**, not a modpack distribution platform, so this should not be a practical conflict — but it is worth stating explicitly so a future feature idea (e.g. "let users browse and rate packs inside our app") gets a legal gut-check against this clause before being built, not after.
- **Non-transferable, confidential key:** the API key issued to this product must not be shared with end users or embedded in a way that lets it be trivially extracted and reused outside this product's own request flow — this has a concrete implementation consequence: **the API key belongs to the product's own backend/build process, not to each individual VM1** in the naive sense; the cleanest compliant shape is likely "the Manager (or a thin product-operated relay respecting the no-proxy-of-CDN-downloads rule for actual file bytes, but permissible for authenticated *metadata* calls under the developer's own key) holds the key, and VM1's installer module receives a short-lived, already-resolved download URL/manifest rather than the raw API key itself." This exact mechanism should be finalized at v1 implementation time with a legal/ToS re-read against whatever the terms say **then** (ToS terms are exactly the kind of fact that can silently change between this document's writing and actual v1 implementation).

### 23.3 CurseForge manifest format and the client/server detection problem

Unlike Modrinth's `.mrpack`, a CurseForge pack's `manifest.json` (root of the pack ZIP) does **not** mark files client-only or server-only — CurseForge's manifest lists mod project/file ids with no environment field at all. Confirmed concretely from CurseForge's own documented "unpublished modpack" manifest shape:

```json
{
  "minecraft": { "version": "1.20.4", "modLoaders": [{ "id": "fabric-0.15.3", "primary": true }] },
  "manifestType": "minecraftModpack",
  "manifestVersion": 1,
  "name": "Custom",
  "files": [
    { "projectID": 351725, "fileID": 4973035, "required": true },
    { "projectID": 306612, "fileID": 5010374, "required": true }
  ],
  "overrides": "overrides"
}
```

**Precision note (do not conflate with Modrinth's `env.server`):** `required` here means "installed by default vs. an opt-in extra within the CurseForge/client-launcher install flow" — it is **not** a client/server side marker. A file with `required: true` can still be entirely client-only (e.g. a UI mod every player is expected to have). This means **automated server-side install of a CurseForge pack requires a maintained heuristic** for "which of these mods are client-only and must be excluded" — there is no authoritative per-file signal to read the way there is for Modrinth. The best current public reference for this heuristic is itzg's `mc-image-helper` `install-curseforge` command (widely used, actively maintained, handles exactly this problem daily across a huge range of real packs) — **recommendation: do not attempt to build this heuristic list from scratch; study/reuse the *category* of logic (known client-only mod slugs/ids, side annotations where CurseForge's own newer API does expose per-file `gameVersions`/environment hints for some files) that mature open-source tooling already maintains, and budget ongoing maintenance time for this list specifically**, because it will drift as new mods appear — this is explicitly **not** a "solve once" problem the way Modrinth's explicit `env.server` field is. See §24.3 for the maintenance/detection design this document commits to for that ongoing drift.

### 23.3a Some CurseForge files cannot be resolved via the API at all, by the mod author's own choice

Confirmed directly from `itzg/docker-minecraft-server`'s own design: it maintains a dedicated `/downloads` manual-drop mechanism specifically because **a mod/file author can disable third-party API distribution for their file**, in which case **no valid API key changes anything** — the file is only obtainable by a human visiting the CurseForge website in a browser, downloading it there, and placing it somewhere the tooling can pick it up. This is not a hypothetical edge case; it is common enough that a project as mature as `itzg`'s dedicates a first-class, documented feature to it.

**Consequence for this product's design (a real gap in earlier drafts of this document, not just a footnote):** the CurseForge installer module (§23) must treat "the API refused to resolve this file's download URL because the author disabled third-party distribution" as an **expected, named outcome**, not a generic failure. When it occurs during Setup: halt before touching `server_dir` (same "no partial installs" discipline as §12.2), and tell the operator **exactly which mod(s)** could not be resolved, with a direct link to that mod's CurseForge page, and instruct them to download the file there and supply it back into Setup via the **same manual-upload primitive** already defined in §24 (a single extra jar is just a one-file case of the same "user hands over a file" mechanism, not a new pathway). This keeps the product's promise from §2.4 intact — the user is never asked to *browse/pick* anything new, only to fetch one specific, already-identified file the automated path could not legally reach.

### 23.4 Recommendation given all of the above

**Product decision 2026-08-18 (v1):** do **not** ship a CurseForge API key, and do **not** drop CurseForge as a file format.

- **In v1:** import CurseForge **Server Files** / any zip that already contains the jars (manual adapter, §24 / V1 Step 4.9). Refuse CurseForge **client** exports. Guide users to download Server Files from that pack’s CurseForge page, or a Modrinth `.mrpack` when one exists — not “Modrinth only.”
- **Do not** apply for or bundle a product-owned key in git, the WinExe, VM1, or an Always Free Function relay (non-transferable key + no-proxy + $0). `itzg` baking a key into a container image is not a model this open-source desktop app can copy.
- **Revisit later only** with an **operator-owned** key in Windows Credential Manager, API + CDN downloads on the **admin PC** only, no API JSON cache, no catalog UI, plus the heuristic and author-opt-out handling in §23.3 / §23.3a.

The research above remains valid if that later path is reopened. A large fraction of popular packs are CurseForge-primary; Server Files covers the legally simple subset without an API.

---

## 24. Manual server-pack upload/import

**Stage: v1 candidate**, and — given §2.4 — actually **the umbrella mechanism every pack pathway in this document funnels through, not a separate "fallback" option.** There is no Setup code path where a pack arrives any other way than the user handing over a file. "Modrinth" and "CurseForge" (§22–§23) are best understood as **two recognized, better-supported file formats** this same upload/import step knows how to parse (because their manifests carry rich, structured metadata: explicit `env.server` markers for Modrinth, project/file IDs for CurseForge) — not as alternate, API-driven selection paths. A raw, unrecognized zip is the same import step falling back to weaker heuristics (§24.1's third bullet) because it lacks that structured metadata, not because it arrived through a different UI.

### 24.1 What "manual" covers

- A user-supplied `.mrpack` file (parsed with the exact §22.1 logic, just sourced from a local file picker/drag-drop instead of a Modrinth API lookup — the parsing code path is identical either way, which is a good argument for implementing `.mrpack` parsing as one shared function regardless of *how* the file arrived).
- A user-supplied **CurseForge-exported "server pack" zip** — many CurseForge pack authors publish a pre-built "server files" download (via the pack's own CurseForge page, a separate download from the full client pack) that already excludes client-only content and often already includes a working `run.sh`/installer for the correct loader version. When this shape is detected (presence of a loader-installer jar or an already-populated `libraries/` tree plus a `manifest.json`, versus a raw client-pack CurseForge zip), the import step is closer to "unzip into `server_dir` and let the bundled `run.sh`'s one real command become this manifest's `launch_command`" than a full pack-resolution pipeline — confirmed as a common, well-documented real-world pattern by multiple independent guides found during research (e.g. the diengdoh.com Forge/NeoForge Linux setup guide walks through exactly this "if the pack ships a `run.sh`, use it" flow).
- A raw client-pack zip with no server-pack variant available — the fallback here is the same client-only-mod heuristic problem as §23.3, now with **no CurseForge API metadata available at all** to help (since the whole point is the user just handed over a zip, not a project/file id) — this is the **hardest and lowest-priority** manual-upload sub-case; product should clearly tell the user "this looks like a client pack; if a server-pack download is available for it, please upload that instead" rather than silently attempting a low-confidence heuristic strip on a raw client pack.

### 24.2 Why this pathway is legally simpler than CurseForge API integration

Because the *user* supplies the archive (already, presumably, having agreed to whatever license/ToS governed their own download of it), this product is not itself calling a gated API or redistributing third-party content — it is acting on a file the operator already possesses, the same legal posture as "the user pastes in a URL and our server downloads it," which is explicitly the existing precedent in this product's own MVP world-restore design (`Contracts-Object-Storage.md`'s `meta/world-restore-request.json`) and Server Management upload/replace flow. This does **not** remove all legal considerations (Setup should still show "you supplied this archive; you are responsible for having the right to install it" copy, per PRODUCT-IDEAS' existing "do not redistribute paid modpack contents" principle) but it is a materially simpler starting point than building CurseForge API compliance from zero.

### 24.3 Exclude/include override lists and mis-tagged-mod detection (applies to Modrinth §22 and CurseForge §23 imports)

This is a cross-cutting concern for both structured pack formats, because both can mis-declare a client-only mod as server-installable (§22.1's correction; §23.3's heuristic problem). This section commits to a concrete, three-layer design rather than leaving "maintain a list" as an unstated assumption.

**Layer 1 — vendor an actively-maintained upstream list, do not start from zero.** `itzg/docker-minecraft-server` maintains and ships exactly this kind of override data today — `files/modrinth-exclude-include.json` and `files/cf-exclude-include.json` in that project's repo, both using [the same documented JSON schema](https://github.com/itzg/mc-image-helper#excludeinclude-file-schema). A trimmed real excerpt (fetched during this research, [`modrinth-exclude-include.json`](modrinth-exclude-include.json) is checked into this repo alongside this document for reference):

```json
{
  "globalExcludes": [
    "sodium",
    "iris",
    "entityculling",
    "Xaeros_Minimap",
    "XaerosWorldMap",
    "chat_heads",
    "notenoughanimations"
  ],
  "globalForceIncludes": [],
  "modpacks": {
    "cobbleverse": {
      "excludes": ["cloth-config"]
    }
  }
}
```

`globalExcludes` is a flat list of mod slugs/names always treated as client-only regardless of a pack's own declaration; `globalForceIncludes` is the reverse (mods sometimes mis-tagged as client-only that must always be kept); `modpacks` allows a **per-pack-slug** override layer on top of the globals. **Recommendation: this product should track (vendor/refresh) `itzg`'s two files as its default data source, with attribution, rather than hand-building an equivalent list from scratch** — that list represents years of accumulated real-world corrections this product would otherwise have to rediscover mod-by-mod. Refresh the vendored copy on a regular cadence (e.g. tied to product releases), not live-fetched from GitHub at install time (keeps bootstrap's dependency surface limited to the platforms already in Part B, and avoids a new "what if GitHub is unreachable during Setup" failure mode).

**Layer 2 — a small product-owned supplemental file** for corrections specific to this product's own experience (e.g. an ARM64-specific exclusion that a generic x86-focused list would have no reason to carry — see §27.3's separate "known-problematic-on-ARM64" list, which is conceptually the same mechanism scoped to a different failure category and can share this implementation rather than becoming a second, redundant list format). Same schema, layered on top of (never replacing) the vendored Layer 1 file.

**Layer 3 — automatic, reversible, transparent quarantine when a crash is directly attributable to one mod, closing the "the list doesn't have this new mod yet" gap the operator raised.** Neither Layer 1 nor Layer 2 can ever be fully current — a newly published mod that mis-declares itself as server-compatible will not be in any list on day one. Rather than either (a) doing nothing and leaving the operator to debug a crash-looping modded server cold, or (b) trying to *fully automatically and silently* patch the exclude list from a single observed crash (rejected — too easy to misattribute a crash caused by a mod *interaction* to the wrong single mod, and a silent, permanent, unreviewed exclusion is exactly the kind of "the product changed my modpack without telling me" surprise this product should never produce):

1. During the health check after a modded first-boot/upgrade (§12.1 step 9 / §14.3's "health check failure" row), if the server crash-loops, inspect the crash output for the mod loader's **own** "problem mod" report — Forge, NeoForge, and Fabric all commonly print an explicit "the following mod(s) caused the server to crash" section in a modded crash report, which is a *much* more reliable attribution signal than free-text stack-trace scraping.
2. If **exactly one** mod is implicated this way (not zero, not several — ambiguous cases always fall through to a plain reported failure, no automatic action), retry **once**: move that mod's jar to a sibling `mods.quarantined/` directory (never delete it — fully reversible) and attempt the boot again.
3. Whether the retry succeeds or not, **always** write the outcome into a new manifest field, `modpack.quarantined_files` (extends §3.7): `{ "path": "mods/badmod-1.2.3.jar", "reason": "crash_attributed_by_loader_report", "detected_at": "<UTC timestamp>", "retry_succeeded": true, "operator_acknowledged": false }`. This is **never** silently folded into `excluded_client_only_files` (which is reserved for pack-declared/known-list exclusions decided *before* install) — quarantine is a distinct, provisional, must-be-surfaced state.
4. Surface every unacknowledged `quarantined_files` entry prominently wherever Server Management/Console (v1) shows modpack status, with one-click "keep excluded" (promotes it into the local Layer 2 override file, so future re-installs of the *same* pack do not repeat the crash-detect-quarantine cycle) or "put it back" (moves the jar back from `mods.quarantined/` and clears the entry, for when the operator determines the crash was actually caused by something else and this mod was wrongly blamed).

This turns "our list doesn't know about this brand-new mod yet" from a cold, unrecoverable Setup failure into a self-healing-with-consent recovery path that also **feeds Layer 2** over time from this product's own real operator base — which is exactly the same kind of empirically-grown list `itzg`'s project itself represents, just started later and scoped to what this product's own users actually hit.

---

## 25. Required client-pack communication

**Stage: v1 candidate** (a UX/communication requirement that must ship *alongside* the first modded Setup path, not be deferred — otherwise the first modded server this product deploys will confuse every friend who tries to connect with the vanilla launcher).

### 25.1 The problem

A modded server (any `distribution == "modded"` manifest) is **only joinable by a client running the same loader + the same server-required mod set** (plus any client-required-only mods, which the server never needed to install but which the *player* does need). Nothing about the server side of this product can make a vanilla client join a Fabric/Forge/NeoForge server — this is a fundamental Minecraft protocol-and-content property, not a bug to fix.

### 25.2 What the manifest already captures to make this solvable

The `modpack` object (§3.7) already records `client_pack_required` (a simple boolean: true unless the pack analysis found zero mods requiring the client, which in practice is essentially always true for any real modpack) and enough identity (`source`, `project_id`, `version_id`, `pack_name`) for a v1 Manager feature to generate operator-facing guidance such as: *"This server requires players to install the same modpack. Share this link with friends: `https://modrinth.com/modpack/<project_id>/version/<version_id>`"* (Modrinth) or an equivalent CurseForge pack page link, or, for a manually-uploaded pack with no public project id, a generated instruction to "share the same pack file you uploaded" plus the recorded `pack_name`/`pack_version_label` for the operator's own reference.

### 25.3 Recommendation for v1 scope

Do **not** attempt to build an in-product client-pack *installer* (e.g. generating a CurseForge-app-compatible or Prism-Launcher-compatible client profile) — that is a materially larger feature (client-side launcher integration is its own ecosystem problem, well outside "deploy the server") and is explicitly out of scope per the operator's prompt ("Required client-pack communication," not "required client-pack installation"). The v1 bar is: **the Manager must always be able to show the operator exactly what pack/version/loader their friends need, and a shareable reference to get it**, sourced directly from the manifest fields already being recorded for other reasons (§3.7) — this is a UI/copy feature built on data this document's schema already provides, not a new backend capability.

---

# Part C — Cross-cutting v1+ concerns

## 26. Java / Minecraft / loader compatibility matrix

Combining §9.1 (Minecraft→Java floor) with each platform's own minimum-supported-Minecraft-version constraint from Part B, into one table an installer module's pre-flight validation can check against before touching disk:

| Minecraft version | Java floor | Vanilla | Paper | Fabric | NeoForge | Forge | Quilt |
|---|---|---|---|---|---|---|---|
| 1.12 – 1.16.5 | 8 | Yes | Yes (as Spigot/Paper for these versions) | Yes | No (did not exist) | Yes — **primary recommendation for this range** | Yes (mirrors Fabric availability) |
| 1.17 – 1.17.1 | 16 | Yes | Yes | Yes | No | Yes (argfile mechanics begin) | Yes |
| 1.18 – 1.20.1 | 17 | Yes | Yes | Yes | No (1.20.1 builds removed by NeoForge team; use Forge) | Yes — **primary recommendation for 1.20.1 specifically** | Yes |
| 1.20.2 – 1.20.4 | 17 | Yes | Yes | Yes | Yes — **primary recommendation from here forward** | Yes (separate ecosystem from NeoForge as of 1.20.2) | Yes |
| 1.20.5 – 1.21.11 | 21 | Yes | Yes | Yes | Yes | Yes | Yes |
| 26.1+ | 25 | Yes | Confirm at implementation time (very new; Paper support lag is normal at the start of a Minecraft cycle) | Confirm at implementation time | Confirm at implementation time | Confirm at implementation time | Confirm at implementation time |

**Design implication:** the pre-flight check for a Setup/upgrade request is: (1) look up the Java floor for the requested `minecraft_version` (§9.1, or the platform's own richer metadata when available, e.g. Paper's `minimumJavaVersion`), (2) confirm the requested `loader`/`distribution` actually has *any* build for that exact `minecraft_version` (not just "the loader exists in general" — e.g. NeoForge existing does not mean it supports every Minecraft version), and (3) for a **modded** request specifically, confirm the *pack's own* declared `dependencies.minecraft` (Modrinth) or manifest-declared Minecraft version (CurseForge) matches the user's selected Minecraft version exactly — a pack pinned to 1.20.1 must not be silently installed against a server provisioned for 1.20.4, even though both "are Forge."

---

## 27. ARM64 / native-mod risk

### 27.1 The server process itself has no ARM64 blocker

The extensive LWJGL/native-library ARM64 issues surfaced repeatedly during research (`Platform/architecture mismatch detected for module: org.lwjgl`, `liblwjgl.so` `UnsatisfiedLinkError`, Mojang shipping no Linux-aarch64 LWJGL natives in the version metadata) are **exclusively a client-side rendering problem.** LWJGL exists to talk to OpenGL/GLFW for **drawing a window** — a dedicated server has no window, no renderer, and never loads LWJGL at all. This is confirmed both by first-principles (the `net.minecraft.server.dedicated.DedicatedServer` code path never touches `RenderSystem`/`Blaze3D`, the classes whose static initializers are what trigger the LWJGL native-load crashes seen in every researched issue report) and by the practical fact that this product's **existing lab VM1 already runs a Forge server on Ampere A1 today** without any LWJGL workaround — the architecture already proves this in production, this document is simply explaining *why* it works so a future implementer does not waste time "fixing" a non-problem.

### 27.2 The real risk: mods/plugins that bundle their own native code

A minority of mods/plugins link against **non-LWJGL** native libraries for their own functionality — confirmed real, current examples found during research:

| Example | Native dependency | Failure mode observed |
|---|---|---|
| Simple Voice Chat's Discord Bridge plugin | `libvoicechat_discord.so`, requires a specific glibc version | `UnsatisfiedLinkError: ... GLIBC_2.27' not found` on an older base OS — architecture-independent glibc-version issue, but the same *class* of native-loading fragility applies to an architecture mismatch. See §9.5 for the distinct-but-related finding that the base OS's **C library** (glibc vs musl, or even RHEL-derivative quirks like Oracle Linux's `zlib-ng`) is its own compatibility axis alongside CPU architecture — this product's Ubuntu 22.04/glibc choice is the validated-safe default on both axes. |
| `sqlite-jdbc` (used by some mods/plugins for local databases) | Bundles per-OS/per-arch native `.so` files inside the jar (`org/sqlite/native/Linux/aarch64/...`) | Modern releases (post ~3.40) **do** ship an aarch64 build and generally work; older pinned versions inside an old, unmaintained mod may not have bundled an aarch64 native at all, or `/tmp` execution restrictions (noexec mounts, some container runtimes) can break native loading even when the right architecture's `.so` **is** present in the jar |
| Any mod using JNA/JNI for OS-level integration (hardware info libraries, some anti-cheat/analytics mods, occasional physics/audio libraries) | Varies | Same general class — a native library that was only ever built/tested for x86_64 |

The common thread: **the failure is always an explicit, loud exception at plugin/mod load time** (`UnsatisfiedLinkError`, a stack trace naming the missing/mismatched `.so`), **never** silent data corruption or a hang — this is good news for this product's failure-handling design (§14.3): a per-mod native-library failure surfaces during the health-check phase of a modded install/upgrade as a crash-loop or an explicit plugin-load error in `journalctl`, and should be classified and surfaced exactly like the existing "health check failure" row in §14.3's table, with the specific mod/plugin name extracted from the log when feasible.

### 27.3 What this product should do about it (v1+ design guidance, not MVP-blocking)

- **Do not attempt to pre-scan every mod jar for bundled native libraries at install time** — this would require unpacking and inspecting every jar in a pack (expensive, and native libraries are not always trivially identifiable by filename convention alone) for a failure class that, per §27.2, already fails loudly and specifically on its own.
- **Do** surface the *specific* mod/plugin name from a crash-loop's log output prominently in whatever v1 "install failed" / Console-tab UI exists (§14.3), rather than a generic "server failed to start" message — this is the single highest-leverage mitigation, because it turns "mysterious ARM64 failure" into "this specific mod does not support ARM64, remove it or find an alternative," which the operator can then act on immediately.
- **Do** maintain a short, product-owned "known-problematic-on-ARM64" list (starting empty, or seeded with any mods this research or the operator's own future testing confirms) that Setup/Manager can warn about **before** install for a *known* offender, without needing to claim completeness — this is explicitly a "grows over time from real incidents" list, not a research deliverable this document can fully populate up front, because the space of "mods that happen to bundle x86_64-only natives" is large, changes as mod authors add aarch64 builds, and is best discovered empirically from this product's own real usage rather than guessed at exhaustively here. **Implementation note:** do not build this as a separate, third list format — it is the same Layer 2 product-owned override file already defined in §24.3 (which already layers on top of a vendored community list and already has a Layer-3 crash-quarantine feedback loop); an ARM64-native-load crash is simply another input that can promote a mod into that same file via the same §24.3 Layer 3 mechanism, since it produces the same loud, attributable, per-mod failure signature described above.
- **Distant Horizons** (already flagged in `PRODUCT-IDEAS.md` as a "recommend against under multiplayer load" item, and already observed by the operator to degrade badly with multiple players on this Ampere shape) is worth explicitly noting here too: its issues are **performance/multiplayer-load-related** based on the operator's own observation, not a documented native-ARM64-incompatibility per this research — do not conflate the two problem categories in guide copy; they warrant separate warnings for separate reasons.

### 27.4 Old/abandoned packs and permanent unsupportability

As already noted in §20.5: a legacy pack (pre-NeoForge era, often unmaintained) that hits a native-library ARM64 wall has, realistically, **no upstream fix coming.** Product guidance for this case (v1+, not MVP) should be honest about that rather than implying "try again later": if a specific mod is confirmed native-incompatible and its pack has had no updates in a long time, the operator's practical options are (a) remove/replace that one mod if the pack allows it, or (b) accept that this specific pack cannot run on this product's ARM64 hardware at all. This product's own value proposition (Always Free Ampere A1) is precisely the tradeoff that makes this an occasional real limitation, not a hidden one — say so plainly in whatever guide/UI copy eventually covers this, rather than let an operator discover it via a cryptic crash log with no context.

---

## 28. Update and migration behavior

This section consolidates the update/migration-relevant findings from Part B into one cross-platform view (the mechanics themselves are specified per-platform in Part B and the general upgrade pipeline is §12.2 — this section is the "what's different between platforms" summary a developer should read before writing the generic upgrade driver).

| Platform | Update mechanism | Idempotent re-run? | Version-pin friendliness |
|---|---|---|---|
| Vanilla | Re-resolve `piston-meta` for new version id, replace jar | Yes | Trivial — Mojang version ids are exact and stable |
| Paper | Re-query Fill v3 builds for (possibly new) Minecraft version, replace jar | Yes | Build numbers are monotonic per Minecraft version; pin to an exact `build.id` for reproducibility, not just "latest STABLE," when the operator wants a frozen version |
| Fabric/Quilt | Re-resolve loader+installer versions, replace launcher jar; `mods/`/`config/` untouched unless the pack itself is also being upgraded | Yes | Good — all three version axes (game/loader/installer) are explicit, discrete strings |
| NeoForge/Forge (1.17+/argfile) | Re-run new version's installer **over the existing directory** (upstream-documented, self-migrating) | Yes, per upstream's own claim — this research did not find a documented exception, but treat any Forge/NeoForge installer run as something the safety-backup-first discipline in §12.2 protects against regardless | Good — exact version strings, but remember NeoForge's non-obvious `<mc_minor>.<mc_patch>.<build>` scheme (§19.1) when displaying/choosing versions in UI |
| Modrinth pack | Re-resolve the pack's *new* version id; re-diff the full file list (added/removed/changed mods), not just "download whatever's new" | Yes, if implemented as "compute the desired file set for the new version id, converge `mods/`+`config/`+overrides to exactly that set" rather than "layer new files on top of old" | Good — explicit version ids; a pinned `version_id` is exactly reproducible |
| CurseForge pack | Re-resolve via the pack's file id / a new file id the operator selects; same "converge to desired set" discipline as Modrinth | Yes, with the same converge-not-layer implementation discipline | Weaker — CurseForge historically has looser version/file identity conventions than Modrinth in some community tooling, though the modern API does expose stable numeric file ids that are fine to pin against |
| Manual upload | Operator supplies a new archive; treat as a full pack replace, same converge discipline | Yes (each upload is a fresh desired-state computation) | N/A — there is no "version id" to speak of beyond whatever the operator's own file naming conveys |

**Cross-platform migration principle, restated from §12.2 for emphasis:** any pack/mod-set change (as opposed to a same-pack loader-only bump) should be implemented as **"compute the complete desired file set for the target state, then converge `mods/`/`config/`/overrides to exactly that set"** — deleting files that are no longer wanted, not just adding new ones — because Minecraft mod loaders generally do not gracefully ignore a stale, no-longer-referenced mod jar sitting in `mods/`; it will simply keep loading and can conflict with the new pack's intended set. A "just download the new files" upgrade implementation is a **known bug class**, not a hypothetical one, based on how many of the researched reference scripts (`gist.github.com` community modpack-updater scripts, §22) had to explicitly grapple with exactly this cleanup step.

### 28.1 Day-2 pack replace: light swap vs full re-setup (after v1)

Lab `PRODUCT-IDEAS.md` **Modpack replace (after v1)** is a Server Management file-picker flow (still **no in-app catalog** — §2.4). The Manager should **detect** whether the new archive can be a **light swap** or needs the **full** Setup-style Minecraft install pipeline:

| Detected situation | Mechanism |
|---|---|
| Same Minecraft version + same loader (compatible loader version); pack is mostly the same (config changes, one or two mods added/removed) | Stop the unit → converge `mods/` + `config/` / overrides to the new desired set (§28 principle) → start + health check. Do **not** re-run Java install or the Forge/NeoForge/Fabric installer. |
| Different Minecraft version, different loader, large pack identity change, or analysis cannot prove a small delta | Same pipeline as §12.2 / §12.3 (stop → snapshot → safety world backup → installer module → pack install → manifest → unit → start → health check). Keep the existing world unless the user also chose Wipe world (§11.3). Warn if the new pack is unlikely to load that save. |

**v1** only needs inspect-current-mods + **re-download the original imported archive** (client pack — not a zip of live server `mods/`). Retain that archive on the admin PC (and optionally on VM1 outside `mods/`); do not treat server-side jars as a reconstructable client pack.

Exact “mostly the same” heuristic is product-open (file-diff threshold vs manifest identity vs showing a diff and asking). This section only locks the **two mechanical paths** so UI work does not invent a third installer.

---

## 29. Future game-platform matrix — classification

This is the operator's requested classification, informed by every finding above.

| Platform / feature | Classification | Rationale (short form; full detail in Part B) |
|---|---|---|
| **Vanilla** (official Mojang jar) | **MVP** | Already the committed MVP scope; simplest artifact shape; stable, well-documented API |
| **Paper** (Optimized Vanilla) | **v1 candidate** | Confirmed low implementation risk (single jar, same `server.properties`/RCON/world model as Vanilla); Fill v3 is stable and current; strongest "bang for the buck" performance win for the least added complexity |
| **Purpur** (Paper fork with extra config/gameplay tweaks) | **v1-or-shortly-after candidate** (§17.6) | Same launch/plugin architecture as Paper (no new `server_artifact.kind`); confirmed as the most broadly adopted Paper fork via `itzg/docker-minecraft-server`'s own server-type coverage. Worth adding as a **second** "Optimized Vanilla" option once Paper itself ships, not a Paper replacement. |
| **Pufferfish / Leaf** (large-server-focused Paper forks) | **Later** (§17.6) | Positioned for player counts well beyond this product's small-friend-group Ampere target; no current justification over plain Paper/Purpur |
| **Folia** (Paper's regionized-multithreading fork) | **Later — explicitly not until it has a stable release channel** (§17.6) | Confirmed experimental-only as of this research (no stable channel); different-enough runtime model (per-region ticking) that plugin/mod compatibility assumptions elsewhere in this document would need re-validation before use |
| **Fabric** | **v1 candidate** | Confirmed via research: simplest modded launch shape (still a single runnable jar), large and modern-focused mod ecosystem, unauthenticated/simple meta API |
| **NeoForge** | **v1 candidate** | Confirmed via research: the de facto modern successor to Forge, "default choice for content-heavy modpacks on 1.20.2+" per multiple 2026 sources; more implementation complexity than Fabric (argfile mechanics, XML metadata, no published checksum) but this complexity is well-understood and documented, not a research gap |
| **Forge** | **v1 candidate, explicitly scoped to legacy-version packs (primarily 1.12.2-era and 1.20.1)** | Confirmed via research **and directly contradicted the "retained generally" framing**: for 1.20.2+, NeoForge is the recommended path and Forge is redundant; Forge's ongoing relevance is specifically the pre-1.20.2 catalog (huge, long-lived pack ecosystem with no NeoForge equivalent) and the 1.20.1 boundary version NeoForge's own team says to use Forge for. **Recommendation: implement the Forge installer module as part of the same v1 slice as NeoForge (they share the argfile launch mechanics, §20.3), but do not present Forge as a "current-version" alternative to NeoForge in any UI — steer new/current-version modded setups to NeoForge, and offer Forge specifically when a pack itself declares a Forge dependency (almost always implying an older Minecraft version).** |
| **Quilt** | **Downgraded from the operator's provisional v1 framing to *later*** | Confirmed via research: Quilt itself frames its 2026 direction as "Fabric-compatible loader, different governance," retired its own standard-library ecosystem, cannot load Forge/NeoForge content, and has no meaningful modpack-platform-wide default depending on it. Implementation cost is low (near-copy of the Fabric module) but there is no confirmed user-facing demand signal strong enough to justify it ahead of Paper/Fabric/NeoForge/Forge/Modrinth/manual-upload, all of which have clearer, larger value. **Still support Quilt as a *detected* loader value inside the Modrinth/manual-upload pack pathways from day one of modded support (cheap), just do not build a dedicated "Quilt" Setup entry point until there is a concrete reason to.** |
| **Modrinth `.mrpack` import** (file upload/drag-and-drop of an already-exported pack; **not** an in-app Modrinth browser) | **v1 candidate — recommended as the first pack-source integration** | Confirmed via research: best-specified format for automated server-side install (explicit `env.server` per file), files already carry embedded CDN URLs so no catalog API call is even needed, no caching/ToS restriction found |
| **CurseForge Server Files / filled zip import** (file upload; jars already in the archive; **not** an in-app CurseForge browser) | **v1** (via §24 / V1 Step 4.9) | Legally simple: the operator already has the archive. No API key. |
| **CurseForge API client-export import** (resolve `projectID`/`fileID` from `manifest.json`) | **Deferred** (V1 Step 4.12; ToS / key custody) | CDN requires `x-api-key` (enforcement 2026-07-16). 3rd Party API Terms: non-transferable key, no cache, no proxy, no competing product. Open-source WinExe cannot hold a product key; $0 forbids a relay. Not rejected (unlike catalog). Revisit only with operator-owned Credential Manager key and admin-PC downloads. |
| **In-app modpack/mod catalog, browse, search, or in-app download UI (Modrinth, CurseForge, or otherwise)** | **Rejected — will not be implemented** (not staged for any release) | Operator-confirmed (§2.4): this product is a server host/manager, not a modpack marketplace. Users always create/download packs on the source platform's own site/launcher and import the resulting **file**; Setup/Manager pack input is file picker/drag-and-drop only. |
| **Manual server-pack upload/import** | **v1 candidate — ship alongside Modrinth, not after it; also the umbrella mechanism the two rows above are specific manifest formats of (§24)** | Confirmed via research to be the most legally simple pathway (operator-supplied archive, same posture as existing world-restore/upload precedent) and the most resilient to any single upstream API's future ToS/uptime changes; also the *only* pathway available for packs with no server-pack variant published anywhere, or for formats (FTB, etc.) with no dedicated adapter yet |
| **Required client-pack communication** | **v1 candidate, must ship with the first modded Setup path** | Not optional/deferrable once any modded distribution ships — a modded server with no client-pack guidance is not a usable feature, it is a support-ticket generator |
| **Deeper day-2 mod UX** (swap individual mods, per-mod config UI) | **Later** for **change/replace pack** (file picker only); **rejected** for any in-app mod/pack browser | Inspect + re-download of the **already-imported** pack is **v1**. **Change/replace pack** (file picker; light swap vs full re-setup, §28.1) is **later**. Still **full pack replace**, not a per-mod IDE. An in-app catalog/browse/download UI remains **rejected** (§2.4) — do not sneak it in as “deeper modding.” |
| **A curated Fabric "performance preset" as an alternative Optimized-Vanilla path** | **Later** (design-compatible with this schema today, not yet a scoped feature) | The manifest schema already supports this without changes (`distribution: "modded"`, `loader: "fabric"`, a product-curated rather than user-supplied mod list) — explicitly noted so a future decision to build this is a product-scope choice, not a schema migration. **If built, consider distributing/versioning the curated preset as an OCI-registry artifact** (a real, current pattern confirmed via `itzg/docker-minecraft-server`'s `GENERIC_PACKS=oci://...` support) rather than re-hosting a bespoke zip format — lower-effort than it sounds since this product's own container registry tooling (if any exists by then) would already speak that protocol. |
| **Hybrid mod+plugin servers** (Magma, Magma Maintained, Ketting, Mohist, Youer, Banner, Arclight — combined Forge/NeoForge/Fabric **and** Bukkit-plugin support in one server) | **Explicitly unsupported** | Confirmed real but volatile ecosystem: the original Magma project is **terminated**, replaced by several independently-maintained forks with narrow per-Minecraft-version support matrices (each pinned to specific Forge/loader builds). No confirmed operator/product demand, and the instability itself (a whole project dying and needing multiple successor forks) is a concrete reason not to build install automation against any one of them without a specific, explicit future ask. Named here explicitly so a future agent finds this reasoning instead of wondering why hybrids are unaddressed. |
| **Explicitly unsupported: any other loader/platform not named above** (e.g. Bukkit/Spigot directly rather than via Paper — confirmed its official download provider no longer supports automated downloads at all, per `itzg`'s docs, which independently validates steering to Paper — Sponge, standalone Bedrock/PocketMine servers, proxy-only platforms like Velocity/BungeeCord as the primary server) | **Explicitly unsupported** | Out of scope for this product's "one Java Edition survival server for a friend group" mission (`PRODUCT-IDEAS.md`/`oci-minecraft-context.mdc`); Paper already supersedes plain Bukkit/Spigot for this product's purposes (Paper is a superset, and now also the *only* automatable option of the two), and a proxy-network architecture (Velocity/BungeeCord fronting multiple backend servers) is a different, larger product shape this workspace's mission does not call for |

---

## 30. Implementation roadmap / cross-references

This document is research + architecture, not an execution checklist — [`V1-Implementation-Plan.md`](V1-Implementation-Plan.md) is the living execution checklist (MVP archive: [`MVP-Implementation-Plan.md`](MVP-Implementation-Plan.md)). Agents must read **only the blueprint §§ named in the current V1 step**, not this whole file. This section maps sections onto plan steps.

| Plan step | What this document supplies |
|---|---|
| **MVP Step 2.3 — Vanilla on-box path readiness** (DONE) | §16 (Vanilla acquisition), §5 (directory layout — implementers should adopt the `/opt/mcmgr/` + `mcmgr` user layout described here rather than continuing to special-case the lab's `/home/ubuntu/minecraft/server` path in any new automated-bootstrap code), §6 (systemd unit generation — build the **generic** template now, even though only Vanilla feeds it today), §7 (server.properties/EULA), §8 (RCON secret generation), §3/§4.1 (write the actual `game-manifest.json` file, not just install the jar) |
| **MVP Step 2.4 — Door / agent product gaps** (DONE) | §10 (idle agent integration seam — confirm/patch the manifest-to-`/etc/mc-manager/config.json` sync step, §10.2) |
| **MVP Step 3.1–3.3 — OpenTofu + Setup wizard + apply/bootstrap** (DONE) | §13 (bootstrap responsibility split — keep OpenTofu/user-data limited to infra per §13.1), §14 (failure recovery/resumability — this directly extends the existing "Setup survives capacity wait and can resume" MVP criterion to the bootstrap portion, not just the OpenTofu-apply portion) |
| **V1 plan Phase 4 — Setup game types** ([`V1-Implementation-Plan.md`](V1-Implementation-Plan.md); **read only the §§ named in that step**) | Part B in full is **not** one agent session. Map: Step 4.1–4.3 → §17 Paper; 4.4 → §18 Fabric; 4.5 → §19 NeoForge; 4.6 → §20 Forge; 4.7–4.8 → §22 Modrinth; 4.9 → §24 manual zip (incl. CurseForge Server Files); 4.10–4.11 → §25 client-pack; 4.12 → §23 CurseForge API (**deferred**). §26–§29 for platform gating. Wipe world is V1 Step 1.3 / §11.3. Pack replace stays after v1 (§28.1). |

**Immediate, concrete recommendation for whoever picks up V1 plan Step 4.1:** do **not** re-read this entire document. Follow that step’s **Read first** list (Fill v3 §17 + existing Vanilla client). Manifest schema (§3), generic systemd unit (§6), and `/opt/mcmgr/` layout (§5) already shipped in MVP Step 2.3.

---

## 31. Reference links

**Vanilla / Mojang**
- Version manifest: https://piston-meta.mojang.com/mc/game/version_manifest_v2.json
- Format reference: https://minecraft.wiki/w/Version_manifest.json , https://wiki.vg/Game_files
- Java version history: https://minecraft.wiki/w/Tutorial:Update_Java
- New version numbering scheme, official Mojang announcement: https://www.minecraft.net/en-us/article/minecraft-new-version-numbering-system

**Java runtime**
- Adoptium: https://adoptium.net/ , API docs/cookbook: https://github.com/adoptium/api.adoptium.net , CI script guidance: https://adoptium.net/installation/ci-scripts , Linux packages: https://adoptium.net/installation/linux
- Real-world Java/loader interaction hazards (§9.5) cross-checked against: https://docker-minecraft-server.readthedocs.io/en/latest/versions/java/

**Paper (and forks, §17.6)**
- Downloads Service docs: https://docs.papermc.io/misc/downloads-service/
- Fill v3 base: https://fill.papermc.io/v3/projects/paper ; Swagger: https://fill.papermc.io/swagger-ui/index.html ; GraphQL: https://fill.papermc.io/graphiql?path=/graphql
- v2→v3 migration announcement (mirrored in multiple community threads): https://github.com/itzg/docker-minecraft-server/issues/3517
- Fork ecosystem (Purpur/Pufferfish/Leaf/Folia) surveyed via: https://docker-minecraft-server.readthedocs.io/en/latest/types-and-platforms/server-types/paper/
- Purpur: https://purpurmc.org/ ; Pufferfish: https://github.com/pufferfish-gg/Pufferfish ; Leaf: https://www.leafmc.one/ ; Folia: https://papermc.io/software/folia

**Bukkit/Spigot (explicitly unsupported, §29)**
- Automated-download-provider deprecation confirmed via: https://docker-minecraft-server.readthedocs.io/en/latest/types-and-platforms/server-types/bukkit-spigot/

**Fabric / Quilt**
- Fabric Meta: https://meta.fabricmc.net/ , source: https://github.com/FabricMC/fabric-meta/
- Quilt: https://quiltmc.org/en/ , FAQ: https://quiltmc.org/en/about/faq/ , 2026 direction post: https://quiltmc.org/en/blog/2026-02-03-non-obfuscated-updates/

**NeoForge**
- Server install docs: https://docs.neoforged.net/user/docs/server
- Maven metadata: https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml
- Version-manifest-does-not-exist discussion: https://github.com/neoforged/NeoForge/discussions/3108
- 1.20.1 removal discussion: https://github.com/neoforged/NeoForge/issues/2019

**Forge**
- Promotions: https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json
- Legacy server setup tutorial: https://minecraft.fandom.com/wiki/Tutorials/Setting_up_a_Minecraft_Forge_server
- `run.sh` source (argfile launch): https://github.com/MinecraftForge/MinecraftForge/blob/26.1.2/server_files/run.sh
- "Please do not automate" installer notice + alternatives (Cleanroom) + Hybrids survey (§29): https://docker-minecraft-server.readthedocs.io/en/latest/types-and-platforms/server-types/forge/ , https://docker-minecraft-server.readthedocs.io/en/latest/types-and-platforms/server-types/hybrids/

**Modrinth**
- `.mrpack` format: https://support.modrinth.com/en/articles/8802351-modrinth-modpack-format-mrpack
- API base: https://api.modrinth.com/v2/
- Real-world mis-tagging + exclude/include mechanism (§22.1, §24.3): https://docker-minecraft-server.readthedocs.io/en/latest/types-and-platforms/mod-platforms/modrinth-modpacks/

**CurseForge**
- API key rollout announcement: https://blog.curseforge.com/introducing-api-key-authentication-for-curseforge-file-downloads/
- API application process: https://support.curseforge.com/support/solutions/articles/9000208346-about-the-curseforge-api-and-how-to-apply-for-a-key
- 3rd party ToS: https://support.curseforge.com/en/support/solutions/articles/9000207405-curse-forge-3rd-party-api-terms-and-conditions
- API reference: https://docs.curseforge.com/rest-api/
- Bundled-key precedent, manual `/downloads` fallback for API-blocked files, unpublished-manifest shape (§23.1, §23.3, §23.3a): https://docker-minecraft-server.readthedocs.io/en/latest/types-and-platforms/mod-platforms/auto-curseforge/ , https://docker-minecraft-server.readthedocs.io/en/latest/types-and-platforms/mod-platforms/curseforge/

**Exclude/include override list schema (§24.3)**
- Schema doc: https://github.com/itzg/mc-image-helper#excludeinclude-file-schema
- Reference data files: https://github.com/itzg/docker-minecraft-server/blob/master/files/modrinth-exclude-include.json , https://github.com/itzg/docker-minecraft-server/blob/master/files/cf-exclude-include.json (a trimmed copy of the former is vendored at [`docs/modrinth-exclude-include.json`](modrinth-exclude-include.json) in this repo)

**Reference implementations studied (not dependencies of this product, used only to validate real-world install/URL patterns and operational decisions)**
- itzg/docker-minecraft-server + mc-image-helper: https://github.com/itzg/docker-minecraft-server , https://github.com/itzg/mc-image-helper , full docs: https://docker-minecraft-server.readthedocs.io/en/latest/
- ServerStarterJar (Forge/NeoForge same-process wrapper reference for argfile launch): https://github.com/neoforged/ServerStarterJar
- Console-access alternatives surveyed and deliberately not adopted (§8.5): https://docker-minecraft-server.readthedocs.io/en/latest/sending-commands/commands/ , https://docker-minecraft-server.readthedocs.io/en/latest/sending-commands/ssh/ , https://docker-minecraft-server.readthedocs.io/en/latest/sending-commands/websocket/
- Java/OS image-tag compatibility matrix (§9.5): https://docker-minecraft-server.readthedocs.io/en/latest/versions/java/
- `server.properties` environment-variable mapping surveyed for §7.3 cross-check: https://docker-minecraft-server.readthedocs.io/en/latest/configuration/server-properties/

---

## 32. Changelog

| Date | Note |
|---|---|
| 2026-08-18 | §2.4 / §29: in-app mod/modpack browser **rejected** (will not be implemented); removed “unless the operator revisits” hedge. Users import a local pack file only. |
| 2026-08-18 | §15.4: operator-local gitignored `data/sample-packs/` vs CI `tests/fixtures/`; pointer [`Sample-Packs.md`](Sample-Packs.md). |
| 2026-08-15 | Step **4.3:** `server_properties.sh` writes `white-list=false` / `enforce-whitelist=false` (code now matches §7.3). |
| 2026-08-15 | §5: SETUP-ISSUE-4 `CHDIR` **fixed** — `layout_apply` + `layout_verify` + `repair-permissions.sh` (MVP Step 4.2). §7.3 whitelist-off remains Step **4.3**. |
| 2026-08-15 | §7.3: `white-list` / `enforce-whitelist` default **false** — OCI Security List is the only MVP allowlist (SETUP-ISSUE-3 / MVP Step 4.3). |
| 2026-08-13 | PRODUCT-IDEAS staging: §11.3 wipe world (v1); §28.1 day-2 pack replace light-swap vs full re-setup (after v1); v1 inspect + re-download imported pack; §29 day-2 row updated. |
| 2026-08-12 | Full-document cross-check against `itzg/docker-minecraft-server`'s documentation (a large, actively-maintained, real-world implementation of most of what this blueprint designs). Added: §8.5 (console-access alternatives surveyed, deliberately not adopted); §9.5 (confirmed Java/loader interaction hazards — per-pack Java overrides, Forge/OpenJ9 incompatibility, base-OS libc as a compatibility axis distinct from CPU architecture) plus a Mojang-official citation for the new version-numbering scheme in §16.2/§9.1; §17.6 (Paper fork ecosystem — Purpur/Pufferfish/Leaf/Folia — and their staging); a Forge "please don't automate" ethical callout in §20; softened §22.1's Modrinth mis-tagging claim; §23.1 bundled-API-key precedent, §23.3's `required`-field precision fix, and new §23.3a (per-file API-download-permission blocks and their manual fallback); new §24.3 (three-layer exclude/include override-list design with crash-attributable automatic quarantine, extending §3.7's `modpack` schema with `quarantined_files`); a Hybrids row and a Purpur/Pufferfish/Leaf/Folia set of rows in §29's classification table, plus a Bukkit/Spigot automated-download-dead confirmation. Vendored `docs/modrinth-exclude-include.json` from the upstream project as a concrete schema/data reference for §24.3. |
| 2026-08-11 | Added §2.4 **"No in-app mod/modpack catalog"** as a firm, operator-confirmed architecture decision: Setup never browses/searches Modrinth or CurseForge; the only pack input is an already-exported file via file picker/drag-and-drop. Reframed §22 (Modrinth), §23 (CurseForge), and §24 (manual upload — now the umbrella mechanism) accordingly, updated the §29 classification table with an explicit "in-app catalog UI" = unsupported row, and clarified §3.7's `modpack` schema fields as file-provenance, not user-selection, data. |
| 2026-08-11 | Initial version. Full research pass across Mojang/Paper/Fabric/NeoForge/Forge/Quilt/Modrinth/CurseForge, Java/ARM64 packaging, and systemd/RCON/idle-agent integration. Defined the game-manifest contract (Part A) and the future-platform classification table (§29). Created as the authoritative companion to `PRODUCT-IDEAS.md`'s existing (now superseded-in-detail, still correct-in-intent) Vanilla bootstrap and Setup-game-types sections. |

