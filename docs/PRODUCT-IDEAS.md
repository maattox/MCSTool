# Product vision & staged roadmap (ideas / planning)

**Status:** Living product vision and staged feature plan (MVP → v1 → later).  
**Execution:** implement **v1 features** before Windows installer / GitHub Releases / public launch. **Paid / spend mode is not v1** (later / far future). Living checklist: [`V1-Implementation-Plan.md`](V1-Implementation-Plan.md) (**NEXT = Step 8.5.2** Pass 3, **blocked** until the operator says so). Pack-import contract: [`Pack-Import-Intended-Design.md`](Pack-Import-Intended-Design.md) (**implemented**, Step **8.9**). Do not start Step 9.1 until QA exits **and** Step **8.6.1** is DONE.  
**Not** an implementation checklist by itself — agents follow the V1 plan’s NEXT step (and must not implement **after v1** / later items from this file).  
**Not** a substitute for architecture docs. Doc map: [`README.md`](README.md).

| Doc | Role |
|-----|------|
| [`Infrastructure-Information.md`](Infrastructure-Information.md) | **Live / target infra** as deployed or intended on OCI (placeholders) |
| `data/config.local.json` | Live OCIDs / secrets (gitignored) |
| **This file** | Product principles, UX vision, **MVP / v1 / later** scope, open concerns |
| [`V1-Implementation-Plan.md`](V1-Implementation-Plan.md) | **Living v1 execution checklist** (status + stop-for-feedback) |
| [`archive/MVP-Implementation-Plan.md`](archive/MVP-Implementation-Plan.md) | **MVP archive** (Phases 0–7 DONE) |
| [`Minecraft-Server-Deployment-Blueprint.md`](Minecraft-Server-Deployment-Blueprint.md) | Game install **mechanism** (named §§ only) |
| [`Automated-Infrastructure-Deployment.md`](Automated-Infrastructure-Deployment.md) | Greenfield IaC **mechanism** |
| [`OCI-API-Usage.md`](OCI-API-Usage.md) | OCI SDK/REST usage (throttling, waiters, Always Free request thrift) |
| [`VM-Software.md`](VM-Software.md) | What is built/live on VM1/VM2 today |
| [`../AGENTS.md`](../AGENTS.md) | Day-to-day agent notes |

Agents: prefer infra docs for “what exists now.” Use this file for “what we are building toward” and which stage a feature belongs to — **unless** a newer operator-requested plan disagrees (then follow that plan and note drift). **Execute** v1 items only via the V1 plan’s NEXT step (or a bug-fix plan NEXT). Do **not** scaffold Avalonia; the current vehicle is **.NET + Blazor Hybrid** (WPF + WebView2).

**UI in this file is not locked.** Tab names, top-bar layout, wizard pages, and similar sketches are **starting ideas** for later polish — not a pixel-perfect spec. When doing UI-design work, agents should use (or offer) the `find-skills` skill unless the operator already asked, and should look at similar products (especially **Pterodactyl panel**) for what a game-server Manager should include. Confirm large visual changes with the operator. **NuGet is allowed** on **`McManager.Hybrid`**. Do not add Avalonia themes. Keep OCI SDK on `McManager.Core`. Prefer well-known OSS licenses; ask before paid or commercial packages.

### Document authority (important)

**The operator’s will is the source of truth.** This file is a living vision/roadmap, **not infallible**. Newer operator-requested planning documents (V1 implementation plan, QA bug-fix plans, this chat) often match current will more closely.

When a **current** planning/execution document disagrees with this file: do **not** silently rewrite that document to match PRODUCT-IDEAS. Either **stop and ask** which document to follow (then update the other), **or follow the current document** and **note** in the reply (and a changelog if relevant) that this file disagrees and may drift from what was implemented.

| Source | Authority |
|--------|-----------|
| **The operator** (this chat, confirmed notes) | **Wins.** |
| [`V1-Implementation-Plan.md`](V1-Implementation-Plan.md), QA bug-fix plans | **Living execution.** Implement only NEXT. |
| **This file (`PRODUCT-IDEAS.md`)** | Vision, UX sketches, MVP/v1/later staging. May lag operator will. |
| This repo (`src/`, `door_vm/`, `vm_agent/`, `onbox/`, `infra/`, `functions/`) | Implementation SoT. |
| [`Infrastructure-Information.md`](Infrastructure-Information.md) | Live / near-live OCI layout (placeholders). Update when the running tenancy changes. |
| [`Door-VM-Control-Plane.md`](Door-VM-Control-Plane.md) | How the **current** door implementation works. Not a license to keep product-contradicting behavior forever. |

A withdrawn `IMPLEMENTATION-PLAN.md` must not be treated as authoritative.

**Doc style:** Prefer complete intention over ultra-terse summaries so humans and future agents keep the full picture.

---

## Main principles

| Principle | Meaning |
|-----------|---------|
| **Always Free ($0) by default** | Default product mode stays inside OCI Always Free–eligible usage and hard brakes (soft OCPU caps, door wake gate, $1 budget SoftStop). Users may later opt into paid spend (v1)—never by accident. |
| **Simple components (not “lightweight” absolutism)** | Prefer making **individual** components as simple as practical so they are easier to understand and change. This is a **guideline**, not an absolute. Do **not** make something fragile, incomplete, or harder to maintain just to keep it “light.” Reliability and clarity win when they conflict with minimalism. The **overall** product can still be multi-component (door + VM1 + storage + app). |
| **Reliable** | Consistency and boring recovery matter more than cleverness. Users should get a working Vanilla private server without becoming OCI experts. Polling/reconcile for critical truth; clear degraded states; repair paths. |
| **Polling-first for shared truth** | Disconnected actors (door Micro, main VM, local Manager) should **poll** authoritative state when acting on it matters (VM lifecycle, budgets before wake, infra meta). Do **not** rely on events alone. Optional “nudge” events that only say “poll now” may be considered later for latency; they never replace the poller. See [Sync model](#sync-model-polling-events-central-storage). |
| **Self-contained desktop app** | One **installer** → one **Manager** application with an **integrated Setup wizard**. Users should rarely edit config files by hand. |
| **Minimal ongoing babysitting** | After setup: idle auto-stop, door wake-on-connect, budgets, backups, shared state—so the admin is not manually starting the cloud every session. |
| **Novice-first UI, power-user escape hatches** | Main UI may conflate “server” (VM + game) into simple Start/Stop/Restart; Advanced explains VM vs Minecraft process. **v1** splits **Advanced** (power-user tools) from **Danger Zone** ($0-brake bypass + delete infrastructure). Layout/copy here is **not frozen** — see [Manager UI](#manager-ui-behavior-detail). |
| **No in-app mod/modpack catalog (rejected — will not be implemented)** | The Manager/Setup is a server host/manager, not a modpack marketplace or launcher. **Do not build** an in-app mod browser, pack search, trending list, “download this pack,” slug/URL/ID box, or any UI that fetches packs from Modrinth/CurseForge/FTB for the user to pick. Users **create or download their own pack files** on those platforms, then **select the local file** in Setup or Manager (file picker / drag-and-drop only). This is **rejected**, not deferred — not v1, not after v1. See [Modded branch](#modded-branch) and blueprint [§2.4](Minecraft-Server-Deployment-Blueprint.md#24-no-in-app-modmodpack-catalog--architecture-decision). |

---

## Architecture intent (product)

Aligned with the doorbell design in `Infrastructure-Information.md`:

```text
Friends → reserved play IP
  idle:  Door Micro (MOTD / wake / budget gate) holds reserved IP
  play:  reserved IP on VM1 → Vanilla (MVP) Minecraft

Admin PC → single installed Manager app (Setup wizard + day-to-day manage)
  OCI APIs + SSH when needed
  Object Storage = shared source of truth (ledger, budgets, meta, backups)

Brake: $1 (or later user-set) budget → Function SoftStop **VM1** and PUT `meta/spend-brake-triggered.json` (product v1; `functions/shutdown_vm/`). **Do not SoftStop the Always Free door Micro** (AMD Micro is a separate Always Free allowance, not Ampere OCPU-hours). Live **TESTING** image is v1; an older Forge-lab image may still SoftStop both VMs. v1 also adds a Manager full-window warning until typed confirmation — see [$1 spend-brake lock (v1)](#1-spend-brake-lock-v1).
```

| Piece | Role |
|-------|------|
| **VM1** | Ampere A1 Flex — Minecraft host; often STOPPED when idle |
| **VM2 (door)** | Always Free AMD Micro (**~1/8 OCPU**) — always on; MOTD; wake; **reads** budget/ledger for wake gate; reserved IP parking. Implement door software primarily in **C** for performance on the tiny shape. |
| **Reserved public IP** | Stable address friends use |
| **Security List** | Primary (MVP: only) network allowlist — private `/32`s; **v1** also optional CIDR ranges for dynamic IPs |
| **Object Storage** | Central SoT: ledger, budgets, messages, infra meta, world backups |
| **OpenTofu** | Declarative greenfield deploy. Resource Manager / manual Terraform exports are **reference only**—when writing product IaC, choose **clear, consistent resource names** (do not preserve ad-hoc Console names from the operator’s first manual deploy). |
| **OCI SDK/API** | Day-2: IP management, power, storage, etc. |

**Budget ownership (product intent):**  
**Object Storage + VM1-originated start/stop ledger** (and Manager budget config) are the long-term authorities. The door **reads** shared ledger/budget state before wake and may refuse wake when exhausted. The current prototype door’s local Phase A “45 OCPU-h/day” ledger is an **interim** implementation detail—not the final product story. Align door code with this file in a later pass.

**MOTD** = Minecraft **Message Of The Day** (server-list status text / ping response). The door answers MOTD on the reserved IP while idle.

**Desktop stack (current):** **.NET + Blazor Hybrid** (WPF + `BlazorWebView` / WebView2) for the Manager (including Setup wizard). Layout and visual choices in this file remain **not locked**.

**On-box languages:**

| Where | Preference |
|-------|------------|
| **Door Micro (VM2)** | **C** (as today with `mccontrol`) — Micro is ~1/8 OCPU; keep the control plane lean. Avoid putting heavy Python runtimes / agents on the door unless there is no practical alternative. |
| **VM1 / Functions** | **Python is fine** (idle agent, Functions, glue) if it stays simple and reliable. |

**Compartment:** Setup creates a **dedicated compartment** for all product resources (not tenancy root). Display name for greenfield: **`mcmgr`**. Discovery also accepts a freeform tag on the stack compartment: key **`mcmgr-domain`**, value **`mc-server-compartment`** (used on the operator’s existing Default-compartment lab so Connect existing / auto-detect works without renaming live infra).

**Host firewall:** Prefer **Security List only**; host firewalld off/simplified to avoid dual sync (accept thinner defense-in-depth).

**Resource naming + discovery tags:** See [Product resource naming & discovery](#product-resource-naming--discovery). Greenfield uses consistent `mcmgr-…` display names. Connect-existing prefers reading Object Storage **`meta/infra.json`** (full OCIDs) after locating the compartment + bucket; optional per-resource tags are a secondary aid, not required once meta is present.

---

## Sync model (polling, events, central storage)

### Polling is the reliability backbone

For anything that can soft-lock play (door IP handback, “is VM1 stopped?”, “may we wake?”, ledger before wake):

- Prefer a **timer + explicit check** (e.g. door reconcile ~1 min), as in the current door design.
- **Do not** plan to replace reconcile polling with a purely event-driven handback. That idea is **dropped** as a primary future architecture.

### Optional “nudge” events (not required)

An event/Function that only tells a poller “run your check **now**” (e.g. VM1 beginning SoftStop → door polls immediately instead of waiting up to 60s) is a **small latency optimization**. It must never be the only signal. Default product stance: **polling alone is enough for MVP/v1**; nudges are optional later if the delay actually bothers operators.

### Central Object Storage — source of truth

**Problem:** Manager, VM1, and door need the same ledger, budgets, and meta without fragile “SSH pull only” as the long-term design.

**Rules (agreed):**

1. Object Storage holds the **authoritative** copies of shared documents (ledger, budget config, infra meta, message packs, backup index).
2. Use **version / generation / etag / conditional writes** so concurrent updates do not silently clobber each other.
3. **Writer roles (preferred):**
   - **VM1** — primary writer for **start/stop ledger intervals** (and backup uploads).
   - **Manager** — primary writer for **budget config**, IP **allowlist**, message customization, infra upgrades triggered from UI. **Only clearer** of the v1 $1 spend-brake lock flag (after typed confirmation).
   - **Door** — **read** budget/ledger/meta for wake decisions; **read** the v1 spend-brake lock flag and refuse VM1 wake while it is set; avoid becoming a competing ledger writer (prototype local ledger is interim—see document authority).
   - **$1 budget Function** — **writer** of the v1 spend-brake lock flag (set on trigger); not a ledger writer.
4. **Dirty flags** per data category × consumer (Manager / door / VM1) remain useful so UIs know to refresh—but flags are helpers, not a substitute for versioned SoT.
5. **Safety:** Door should still **re-read** budget/ledger (and, in v1, the spend-brake lock flag) before wake (and keep reconcile for VM lifecycle), not trust a single stale cache.
6. Manager Usage view: check flag/version on open; while open, refresh ~every 2 minutes; stop aggressive refresh when leaving the tab.

### OCI API client behavior (Manager / Setup / agents)

Oracle’s [Using the API](https://docs.oracle.com/en-us/iaas/Content/API/Concepts/usingapi.htm) rules apply to all OCI SDK/REST/CLI usage. Product digest: [`OCI-API-Usage.md`](OCI-API-Usage.md).

**Must-follow for $0 / Always Free:**

- Prefer official SDKs (signed requests, TLS 1.2). Client clock skew **> 5 minutes** → 401 — fix time sync before chasing bad keys.  
- On **429 TooManyRequests**: exponential back-off from a few seconds up to **60s**; never tight-loop.  
- Lifecycle waits (instance start/stop, etc.): exponential poll backoff up to **30s** between attempts, ~**20 min** timeout (SDK waiter defaults) — **not** 1s polling.  
- Paginate List calls (`opc-next-page`; Object Storage `nextStartWith`).  
- Use `opc-retry-token` on supported creates; ETags / `if-match` for optimistic updates when available.  
- Keep Object Storage and Compute chatter modest (cache OCIDs in meta/local config; button-gated auto-detect; UI poll intervals in the OCI-API-Usage doc). The **~50k Object Storage requests/month** free allowance is easy to burn with naïve refresh loops.

### Object Storage Always Free limits (paid / PAYG tenancy)

Per OCI Always Free Object Storage notes for **paid accounts** (confirm against current docs when implementing):

- **10 GB** Standard tier  
- **10 GB** Infrequent Access  
- **10 GB** Archive  
- **50,000** Object Storage API requests / month  

**Product decision:**

- Use **Standard tier only** for ledger, meta, and **world backups**. Do **not** depend on Infrequent Access / Archive for backups (minimum retention / early-delete rules fight “large modded worlds rotate backups often”).
- Soft cap backups (+ related backup data) at about **9.5 GB** to leave headroom under 10 GB Standard.
- **Before upload:** VM1 (or backup job) estimates resulting total Standard usage; if over cap, **delete oldest backup(s) first**, then upload (never upload-then-hope-delete if that can breach the free cap).
- **Single zip larger than soft cap:** cannot upload without breaching Always Free headroom. VM1 must **not** upload; it sets a durable Object Storage flag and skips further automatic OS backups until cleared. Manager notifies the admin and offers **SSH direct download** when VM1 is up — see [Oversized world backup (v1)](#oversized-world-backup-v1). Thin on-box “set flag + skip upload” can land at contract-freeze time; full Manager notification + SSH path is **v1**.
- Keep API chatter modest (flag checks, small JSON); stay aware of the **50k requests/month** free allowance — see [OCI API client behavior](#oci-api-client-behavior-manager--setup--agents) and [`OCI-API-Usage.md`](OCI-API-Usage.md).

**Manual next step on operator tenancy (before Manager / full IaC productization):** stand up bucket/prefix layout for `ledger`, `budget`, `meta`, `backups/` and prove read/write from VM1 + door instance principals + admin API key.

---

## Delivery packaging

- User downloads **one installer** and installs **one Manager application**.
- **Setup wizard** is integrated (first-run and “Deploy / repair infrastructure” from the app)—not a second product `.exe`.
- Shared libraries/services inside one codebase (OCI session, storage, whitelist, wizard state).
- Naming: **Installer** = what you download; **Setup wizard** = in-app deploy flow; **Manager** = day-to-day UI.

### Spend-brake Function image (v1, before release)

Oracle Functions need a container image in **the user’s** OCIR (same region, `GENERIC_ARM`). The product path is **not** “install Docker Desktop and build it” and **not** “open Cloud Shell / Code Editor.”

| Piece | Product rule |
|-------|----------------|
| **Build** | CI builds `linux/arm64` from product `functions/shutdown_vm/` (same channel later for `reconcile_usage`). |
| **First Setup** | Manager **copies** that versioned artifact into the user’s `mcmgr-fn/softstop` repo (bundled registry client or equivalent). Then OpenTofu creates the Function + Events rule. |
| **Admin PC** | API key + **Auth Token** (OCIR login). **No** Docker Desktop, **no** `fn` CLI, **no** Cloud Shell. OCIR username is derived (not an extra env var). |
| **Updates** | New Manager / GitHub Release carries a new image digest. Deploy / repair **converges** when bundled ≠ live. Config (VM1 OCID, bucket, lock key) stays Function config — no rebuild. Users do not rebuild in Cloud Shell to pick up a code fix. |
| **Not the live image** | Do not point the Function at a public GHCR/Docker Hub image; OCI Functions expect OCIR in that region/tenancy. |
| **Lab only** | Cloud Shell / `fn push` / Docker on a developer PC remain break-glass and TESTING-agent paths until/alongside V1 Step **8.6.1**. They are **not** the installer story. |

This must ship **before any official release** (V1 plan Phase **8.6**, then Phase 9 installer). The current Setup publisher that `docker buildx`s on the admin PC is **interim** and must not ship.

### App version vs infrastructure version

Track separately (MVP meta file; stronger use in v1 connect-existing):

| Version | Meaning |
|---------|---------|
| **App version** | Manager release (GitHub Releases / auto-update + release notes). |
| **Infra / stack version** | Schema of deployed cloud + on-box contracts (`infra_schema` / `stack_version` in Object Storage **meta**). |

Uses: agent-only fixes without app release; block or guide **Connect existing** when Manager expects a newer/older infra schema; support diagnostics.

Canonical meta object: **`meta/infra.json`**. It must carry enough identifiers that Connect existing can hydrate local Manager config **without further OCI hunting** — see [Infra meta contract (`meta/infra.json`)](#infra-meta-contract-metainfrajson).

---

## Cost awareness & modes

### Always Free mode (default; **MVP**)

- Soft monthly OCPU target (e.g. ~1400–1450 of ~1500 free Ampere hours—exact default TBD).
- Daily budgets + rollover; door blocks wake when exhausted (admin-only restart).
- **$1 compartment budget** → alert → Function SoftStop as last resort. **MVP:** stop compute to halt spend. **v1:** also set a durable Object Storage lock flag and block Manager start until the admin types an explicit confirmation — see [$1 spend-brake lock (v1)](#1-spend-brake-lock-v1).
- **Honest residual-charge copy (MVP guide + Setup, not only v1):** the stack is designed to stay at **$0**, but if this last-resort budget ever fires, Oracle bills when **actual spend reaches $1**, and the Function can take several minutes to run. The user may see a **~$1–$2** charge for that month, then **no further charges** while the brake holds. Say this plainly in the public guide and in Setup — do not imply the brake is a perfect $0 guarantee.
- Setup should push users to confirm free compute still matches expectations (Always Free docs gate below).

**Always Free docs gate (Setup):** Prefer automated check that free compute policy still looks like ~1500 OCPU-h; if not reliably automatable, first wizard page links [Always Free compute resources](https://docs.oracle.com/en-us/iaas/Content/FreeTier/freetier_topic-Always_Free_Resources.htm#compute) and asks the user to confirm before deploy. Same page (or an adjacent Setup screen) must disclose the **$1 last-resort budget** and the possible **~$1–$2** residual charge if that brake fires — see [Always Free mode](#always-free-mode-default-mvp).

### Paid / spend mode (**later / far future**, not v1)

**Not v1.** Operator 2026-08-18: skip entirely for this product generation. Keep the idea; if it is ever implemented, it will be far in the future. Do **not** build Setup or Danger Zone paid-mode UI, live cost binding, or a Cost Estimator JSON import under the V1 plan.

For users okay spending past free envelopes (or if Oracle shrinks free tier) — **later sketch only:**

- Selectable in Setup and in **Danger Zone**.
- User enters willingness to spend; UI fields **Average daily server uptime**, **Total monthly server uptime**, **Estimated cost**—editing any one updates the others (needs rate assumptions / estimator).
- Cap drives OCI **Budget** amount (replace or supplement the $1 Always Free brake): email on thresholds (e.g. every ~10%); SoftStop Function should act on the **terminal** threshold only (e.g. 100% / over-amount)—not every intermediate alert.
- Show projected cost from VM shape + expected uptime; label as **estimates**.
- Never infer paid mode from PAYG tenancy status.

**Official Cost Estimator:** [Oracle Cloud Cost Estimator](https://www.oracle.com/cloud/costestimator.html)

**Preferred (when/if this ships):** If a **reliable automated** estimate can be done from code/API, Setup/Manager should do it in-wizard.

**Fallback sketch:** Ship a **preset Cost Estimator configuration JSON** (all stack resources) inside the installer. Wizard instructs: open Cost Estimator → import JSON → confirm **$0** for Always Free mode, or that the estimate **matches** the in-app estimate for paid mode.

---

## Staged roadmap

### MVP — “Vanilla doorbell that doesn’t lose money”

**Goal:** Non-expert admin follows the guide, installs one app, deploys a **private Vanilla** stack, friends use the reserved IP with door wake, idle/budget stops protect free tier, worlds are backed up, admin manages whitelist and basic power from Manager.

| Area | In MVP |
|------|--------|
| Guide | Account, PAYG as needed, API key + Auth Token under `%USERPROFILE%\.oci\`, Always Free doc confirmation / link, **$1 last-resort brake + possible ~$1–$2 residual charge** (Function latency), short happy path + optional deep appendix |
| App | Single installer → Manager + **integrated Setup wizard**; first-run Setup or later deploy; **Connect existing** MVP-light: local config seed **or** button-gated **Auto-detect infrastructure** (see [Connect existing / auto-detect](#connect-existing--auto-detect-mvp)) |
| Deploy | **New compartment** display name `mcmgr` + freeform tag `mcmgr-domain=mc-server-compartment`; OpenTofu (Terraform-compatible HCL; Resource Manager stack export as reference); capacity failure → explain + Retry; optional **leave open & poll capacity** every 5–10 min with consent + **resume later** from saved wizard state |
| Compute / net | VM1 A1 + VM2 Micro + reserved play IP + Security List; **private only** (friend `/32` Minecraft; admin `/32` SSH + door admin if exposed); product display names per [naming](#product-resource-naming--discovery) |
| Game | **Vanilla only** (official Mojang `server.jar`); Setup lets the user **pick the Minecraft version**; EULA accept in wizard; bootstrap downloads jar via Mojang version manifest (see [Vanilla server bootstrap](#vanilla-server-bootstrap-mvp)); install matching Java; systemd enable so wake boots the game |
| Door | MOTD / wake-on-connect; **reconcile polling**; **budget gate** before wake; refuse wake when day exhausted with clear kick/MOTD |
| Idle / budget | Idle timeout (default 15m) when **no players are connected or Minecraft is not running** (same timeout; do not SoftStop on the first tick of a normal start). Soft monthly + simple daily budget + **rollover**; in-game warnings (30 min / 5 min; rollover every 10 min) when RCON works; admin-only restart after daily exhaustion. **Idle agent always starts with VM1** (see Danger Zone)—disable is testing-only and does not survive reboot. Ledger/config should record **shape (OCPUs / memory) per usage interval** so later scaling does not invalidate historical math (forward-compatible for v1 VM resize). |
| Storage | Object Storage SoT: ledger, budget, **infra meta**, backups; Standard tier; ~**9.5 GB** backup soft cap; delete-oldest-first before upload; on-box may set **oversized-world flag** when a single zip exceeds soft cap (Manager UX for that flag is v1) |
| Backups | Automatic world backup on sensible stop/schedule; Manager **Server Management** list + **Download World Save** from Object Storage; upload/replace world via storage+flag (SSH fallback when up OK). **Wipe world** is v1. |
| Whitelist / IP mgmt | Tab for allowlisted IPs → **OCI Security List** (the real allowlist); IP required, name optional; auto-repair SSH allow rule when SSH fails due to admin IP change; Advanced manual SSH IPs. **Minecraft in-game `white-list` is off** in automated Setup (OCI SL only). **Private only** (no public toggle; public/blacklist are **rejected**). Single IPv4 `/32` only (CIDR ranges are **v1**). Tab title: **Whitelist**. |
| Main UI | Top bar: compact **status** card (**Running** / **Stopped** for whether Minecraft itself is joinable — not door/VM lifecycle) + **Play IP** + copy + **Players**; **Start/Stop** (door-aware) and **Restart** (Minecraft process only). Fill remaining top width with **pinned usage cards** (today’s uptime, this month, daily average, rollover bank — wall-clock hours). Status / Play IP / Players do **not** use `?` help (self-explanatory). Pinned cards keep `?` for extra explanation (especially rollover ≠ hours left in the month). **Do not** style status as a terminal/console. **Running** is green, **Stopped** is red. Power buttons are filled and more prominent than the status/stat cards. Door / VM1 / doorbell technical status belongs on **Advanced**. Debug host probes (if present) belong on Advanced, not a global overlay. Power buttons stay enabled after first status load except while a Start/Stop/Restart is actually in flight (tab polls must not grey them). Novice copy outside Advanced / Troubleshooting / Danger Zone: no flags, cloud firewall, VM1, door, OCI, Object Storage, Security List, tofu, or issue IDs in body copy. **Sketches in this file are ideas, not a locked UI** — operator notes override. |
| Troubleshooting / repair | Combined Advanced tab (or a Troubleshooting section/tab — not a v1 Advanced/Danger split): **one-shot** confirm-gated repairs (park reserved play IP on VM1 if RUNNING else on door; door state reset; diagnose `wait_forge`; start door after `$1` Function stop; etc.). See MVP plan Phase **4.4**. This is a **subset** of v1 Door/IP Repair pulled forward. |
| Danger Zone | **Same tab as Advanced in MVP.** Disable idle / daily guardrails with strong warnings + confirm (**testing / troubleshooting only**). **Safety:** on every VM1 boot, the idle agent is started regardless of the Object Storage config flag; if that flag was off, VM1 also **rewrites shared config to enabled** so a forgotten disable cannot leave the free-tier brake off after a restart. **Delete all cloud infrastructure** is on this combined tab in MVP (typed `confirm`; log + percent until OCI finishes). **v1** splits Advanced vs Danger Zone. |
| Brake | $1 budget SoftStop Function (stop spend). Full-window Manager lock + typed confirmation to restart is **v1**. Guide/Setup must still disclose the possible ~$1–$2 residual charge in MVP. |
| Updates | App checks GitHub Releases on launch; prompt + **release notes** |
| Stack | .NET + Blazor Hybrid (WPF + WebView2); **C on door**; Python OK on VM1 / Functions |

| Explicitly **out** of MVP |
|---------------------------|
| Public `0.0.0.0/0` game access |
| Paid / spend mode & live cost binding |
| Modded servers / loaders / pack UI / Optimized Vanilla (Paper) in Setup |
| Per-day budget sculpting (redistribute / unbudgeted calendar tool) |
| Usage API 48h ledger reconcile Function |
| Rich MOTD message editor / full customization suite |
| Full interactive Java PTY console (RCON+logs can wait for v1) |
| Replacing door polling with event-driven handback |
| macOS / Linux Manager |
| VPN, Distant Horizons engineering, multi-deploy profiles |
| Silent OCI tenancy probing on every app startup (auto-detect is **button-gated** only) |
| In-app notification center / settings gear / overflow menu chrome (v1) |
| Oversized-world SSH download UX + bell notifications (v1; on-box flag OK earlier) |
| **$1 spend-brake lock UX** (Function writes OS flag; full-window warning; typed confirmation to restart) — **v1**; MVP Function still SoftStops |
| Start-from-Manager **progress checklist** (VM start → game start → mods load, …) — **after v1** |
| Players tab / IP↔username association / Kick·Op·Ban from UI (after v1) |
| Split **Advanced** vs **Danger Zone** into two tabs (v1; MVP keeps one combined tab) |
| Wipe world / Server Management **modding** inspect + re-download pack (v1) |
| Allowlist **CIDR ranges** (v1; MVP is single IPv4 `/32` only) |
| **Delete all cloud infrastructure** UI (v1 Danger Zone; **MVP pulled this onto the combined Advanced / Danger Zone tab** for Step 7.2 E2E teardown — still one tab, still typed `confirm`) |
| Maintenance / reserved-IP assignment controls + “start VM1 without moving the play IP” (after v1) |
| Connect an **additional** infrastructure deployment / multi-profile switcher (after v1; MVP Connect existing is one stack) |
| Day-2 **change/replace modpack** with light-swap vs full re-setup detection (after v1) |

**MVP success criteria:** Friend can wake and play on reserved IP; empty/budget stops work (**including when the Minecraft process is down**); door won’t wake when exhausted; admin can whitelist (OCI Security List) and fix SSH IP without Console; in-game Minecraft whitelist is off; operator can recover a stuck play IP from Manager; world backups exist under 9.5 GB policy; Setup can survive capacity waits and resume.

---

### v1 — “Flexible product”

Builds on MVP. Still novice-first. **Execution:** [`V1-Implementation-Plan.md`](V1-Implementation-Plan.md) (Manager UX first, then spend-brake lock, private allowlist + CIDR, Setup game types, remaining v1, **CI Function image**, **then packaging**). **Paid / spend mode is not v1** (later / far future). Do not start Windows installer work until that plan’s Phase 9, and do not start Phase 9 until Step **8.6.1** is DONE.

| Add in v1 |
|-----------|
| **Split Advanced vs Danger Zone** into two tabs — see [Advanced vs Danger Zone (v1)](#advanced-vs-danger-zone-v1). Danger Zone holds anything that can leave Always Free / disable $0 guardrails, plus **delete all cloud infrastructure**. Other power-user tools stay in Advanced. |
| **Allowlist CIDR ranges** — see [Allowlist CIDR ranges (v1)](#allowlist-cidr-ranges-v1). Add-IP dialog stays simple (single IPv4); an **Advanced** control reveals a CIDR field for friends on dynamic/CGNAT prefixes (e.g. `172.56.0.0/16`). |
| **Wipe world** — see [Wipe world (v1)](#wipe-world-v1). Server Management button near Download World Save; confirm popup before deleting the live save. |
| **Server Management modding (inspect + re-download)** — see [Server Management modding (v1)](#server-management-modding-v1). Show the mods currently on VM1; button to download a copy of the imported pack (for when the admin deleted their local file). **Not** an in-app catalog. Changing the pack is [after v1](#modpack-replace-after-v1). |
| **Delete all cloud infrastructure** — see [Delete all infrastructure](#delete-all-infrastructure-v1). Danger Zone (combined tab in MVP); warning popup; user must type `confirm` before Delete enables. Deletes product OCI resources only — **not** the Oracle tenancy. |
| **VM1 shape scaling (Danger Zone)** — see [VM1 shape scaling (v1)](#vm1-shape-scaling-v1) below. Show current size; scale up/down with hard warnings; update shared config + per-interval ledger shape fields; stop VM1/Minecraft first; recalculate how much monthly playtime remains under the free OCPU-hour envelope. Prefer locking ledger/config schema early (MVP forward-compat) so resize does not force another format break. |
| **Always-on-capable small shape UX** (if operator perf tests confirm): when VM1 is at a size that can stay up ~24/7 inside Always Free (e.g. **2 OCPU / 12 GB** for Vanilla), still track usage, but soften MOTD / budget-forward copy that implies scarcity the user may not hit. |
| **Setup game-type flow** — see [Setup game types (v1)](#setup-game-types-v1): **Vanilla** vs **Modded**; Vanilla → **Default Vanilla** (Mojang jar) or **Optimized Vanilla** (candidate: Paper via Fill API); then version picker; Modded → upload pack (zip / `.mrpack` / other supported), analyze, confirm detected loader/version/Java, install server-side only (strip client-only mods) |
| Stronger central storage discipline (conditional writes, clear writers, flags) as proven on operator tenancy |
| **Infra vs app version** enforced on **Connect existing** (optional: discover resources via **tags** when meta is missing/incomplete) |
| Usage API reconciliation for ledger days **older than ~48 hours**; write back + dirty/version bump |
| Server Management / customization: name, icon, description, automated chat messages in storage; RCON + log **console** tab |
| Door/IP **Repair / Reset** remaining UX (MVP already pulls **one-shot park-IP / diagnose / door-reset** onto Advanced — see MVP plan 4.4). v1 still owns recovery after the **$1 spend-brake lock** (full-window warning + typed confirmation) — see [$1 spend-brake lock (v1)](#1-spend-brake-lock-v1) |
| **Top-bar right chrome:** bell (**notification center**), cog (**program settings**), overflow / “hamburger” menu (About, extras) |
| **Oversized world backup UX** — see [Oversized world backup (v1)](#oversized-world-backup-v1): detect OS flag; notify via bell; adapt **Download World Save** to SSH pull when Object Storage backups are disabled for size |
| **$1 spend-brake lock** — see [$1 spend-brake lock (v1)](#1-spend-brake-lock-v1): Function sets a durable Object Storage flag when the $1 budget fires; Manager shows a full-window warning; Start is blocked until the admin types an exact confirmation that a new calendar month has begun; then Manager starts the stack into a valid IP state and clears the flag. Door must honor the flag if it is left running. |
| **Spend-brake Function image (before release)** — see [Spend-brake Function image](#spend-brake-function-image-v1-before-release): CI-built ARM image copied into the user’s OCIR. No Docker Desktop / Cloud Shell on the admin PC. |

| Moved **after v1** (was tempting for v1) |
|------------------------------------------|
| **Paid / spend mode** — see [Paid / spend mode](#paid--spend-mode-later--far-future-not-v1). Operator 2026-08-18: **not v1**; far future if ever. |
| **Full per-day budget tool** (set individual days, redistribute evenly / to selected days / park unbudgeted hours) |
| **Players tab** (IP↔username association, skins, Kick / Op / Ban) — see [Players tab (after v1)](#players-tab-after-v1) |
| **Start progress checklist** (staged VM / game / mods-load UI) — see [Start progress checklist (after v1)](#start-progress-checklist-after-v1) |
| **Maintenance / reserved-IP assignment**, start-VM1-without-moving-play-IP, VM Info, door maintenance MOTD — see [Maintenance / reserved-IP control (after v1)](#maintenance--reserved-ip-control-after-v1) |
| **Connect additional deployment** / multi-profile switcher — see [Multi-deploy profiles (after v1)](#multi-deploy-profiles-after-v1) |
| **Change/replace modpack** (light swap vs full re-setup) — see [Modpack replace (after v1)](#modpack-replace-after-v1) |

| **Rejected — will not be implemented** |
|----------------------------------------|
| **In-app mod / modpack browser** (browse, search, trending, “download a pack from the internet,” pick-by-name/URL/ID). Users must obtain the pack file themselves and import it. Not an after-v1 item. See [Modded branch](#modded-branch). |
| **Public Minecraft / public-private toggle / blacklist** — see [IP Management (v1)](#ip-management-v1). Private allowlist only. Not after-v1. |

---

### Later (after v1)

- **Paid / spend mode** (far future) — Danger Zone opt-in past Always Free; max monthly spend; uptime ↔ estimated cost; Cost Estimator JSON / automated estimate. **Not v1.** See [Paid / spend mode](#paid--spend-mode-later--far-future-not-v1).  
- Full day-budget calendar / redistribute / unbudgeted-hours UX  
- **Players tab** — IP↔username/UUID association from VM1 joins → Object Storage; skins via public APIs; Kick / Op / Ban (RCON + optional Security List IP removal) — see [Players tab (after v1)](#players-tab-after-v1)  
- **Start progress checklist** — when the admin starts the server from Manager, show staged steps and check them off as they complete (e.g. VM start, Minecraft process start, load mods). Add more granular stages if they can be observed reliably (boot logs, door wake phases, world ready). See [Start progress checklist (after v1)](#start-progress-checklist-after-v1).  
- Deeper modding day-2 UX: **change/replace the imported pack** from Server Management (**file picker**, same rule as Setup). Detect **light swap** vs **full Minecraft re-setup** — see [Modpack replace (after v1)](#modpack-replace-after-v1). Still **full pack replace**, not a per-mod IDE. An **in-app mod/pack browser is rejected** and is not part of this later work.  
- **Maintenance / reserved-IP controls** — Advanced: see which VM holds the reserved play IP; move it; start VM1 + Minecraft **without** taking the play IP off the door; **VM Info** (reserved + ephemeral public IPs). Door MOTD/kick while in that mode: maintenance copy so friends are not woken into an admin-only session. See [Maintenance / reserved-IP control (after v1)](#maintenance--reserved-ip-control-after-v1).  
- **Connect an additional deployment** / multi-profile switcher — Advanced: pick that stack’s OCI API config + VM SSH keys, auto-detect + validate, then switch profiles from a dropdown. Local data becomes per-profile folders. See [Multi-deploy profiles (after v1)](#multi-deploy-profiles-after-v1).  
- Distant Horizons engineering mitigations (product stance until then: **recommend against** under multiplayer load—see pre-v1 guide work)  
- VPN / harder admin access patterns  
- Optional poll-nudge events for latency  
- Multi-admin (second PC / second person sharing one stack) — related to, but not the same as, multi-deploy profiles  
- Advanced console PTY; cross-platform Manager  
- Cross-region capacity shopping  
- Larger A1 shapes in UI beyond what Always Free comfortably supports (e.g. toward **8 OCPU / 48 GB**) if PAYG + free-hour math is confirmed safe—still warn hard; 48 GB may be overkill for typical packs  

### Pre-v1 release work (operator; not product features)

Do before shipping v1 (feeds Setup guide + shape defaults / messaging):

1. **Vanilla perf matrix** on VM1 at **2 OCPU / 12 GB** and **4 OCPU / 24 GB**, with varying player counts. If Vanilla is solid at 2/12, that shape can be the “Vanilla / always-on-capable” recommendation (still meter usage; reduce scare-copy about hitting monthly limits).  
2. **Modded perf sampling** across a few pack sizes/weights on the same shapes (and note where 4/24 is the practical floor).  
3. **Setup / ops guide — mod recommendations:** include an explicit **avoid Distant Horizons** (or equivalent LOD mods) under multiplayer on this Ampere shape—already observed to degrade badly with multiple players. Other pack tips as learned from (2).  
4. Confirm with current Oracle Always Free / Ampere docs whether the **~1500 OCPU-hours/month** envelope applies across Flex sizes the product might offer (including larger than 4/24), not only a fixed catalog of shapes.

### Operator acceptance tests (post-v1 packaging)

Not product features — operator work after **v1 features and the installer** (V1 plan Phase 9) are complete. Prefer a **clean environment** (new OCI account and/or a local VM / spare Windows machine) so the test is not contaminated by lab config.

Until the installer exists, informal dogfood may use `dotnet run` on `McManager.Hybrid` (not a substitute for the clean-room test).

**After v1 + packaging:**

1. Create a **new OCI account** (PAYG as the product requires).  
2. Run the **program installer** as a first-time user (no pre-seeded `config.local.json`).  
3. Run the **full Setup wizard** through greenfield deploy (including Paper/Modded if those shipped).  
4. Exercise the happy path (whitelist, wake/play, idle stop, backups) plus v1 paths that shipped (CIDR, spend-brake **lock** UX if the $1 test fires).  
5. **Stress-test the $1 last-resort budget** and confirm SoftStop, **lock flag**, full-window warning, typed confirmation to restart, door honoring the flag (if the door stays up), and recovery into a valid IP state. Accept that this test may incur the documented **~$1–$2** residual — that is the point of the test; do not run it on the operator’s long-lived lab tenancy unless that spend is explicitly accepted.  
6. Record gaps and fix before calling v1 “shippable.”

---

## Setup wizard (behavior detail)

Asks (grows by stage): alert email; (v1) Always Free vs paid; **MVP:** Vanilla + **Minecraft version picker** + EULA (always deploy **private**; no public/private Setup choice — public/blacklist are **rejected**); **Always Free residual-charge disclosure** (stack is built to stay at $0; if the $1 last-resort budget fires, Function latency can leave a **~$1–$2** charge for that month — see [Always Free mode](#always-free-mode-default-mvp)); **v1:** Vanilla vs Modded / Optimized Vanilla flow — see [Setup game types (v1)](#setup-game-types-v1); server name/icon as available; capacity handling consent; etc.

Creates SSH keypair for instances; applies OpenTofu with **product naming**; bootstraps over SSH (Vanilla jar + Java + systemd + idle agent + door); writes local app config + Object Storage meta. Automated bootstrap sets Minecraft **`white-list=false`** (OCI Security List is the allowlist).

**Capacity:** On out-of-capacity, explain; offer Retry; offer **background check every 5–10 minutes** while app left open (user confirms); **Stop checking**; persist all choices so next launch resumes at “ready to deploy.”

**While Deploy is running or after it has finished:** the **Deploy** button is not clickable again; **Back** / previous wizard pages and any other control that could mutate the in-flight or completed apply are disabled. Resume-later / Re-Deploy is a separate explicit action, not a second click of Deploy on the same finished page.

**Deploy log / progress (UI polish):** the log auto-scrolls to the bottom unless the user scrolled up (resume when they scroll back to the bottom). If it can be implemented cleanly: a progress bar + percent from known stages; timestamps on log lines so later timed deploys can feed a minutes-remaining estimate (ETA is **not** required until the operator has timed a few runs).

**VM1 shape (Setup):** let the admin pick the Ampere A1 Flex size **before** apply. Two Always Free–eligible choices only:

| Choice | OCPUs | Memory | When to recommend |
|--------|-------|--------|-------------------|
| Smaller | **2** | **12 GB** | Vanilla / lighter use; ~24/7 possible inside the monthly OCPU-hour envelope |
| Default | **4** | **24 GB** | Product MVP target; headroom for more players / later modded |

Wire `vm1_ocpus` / `vm1_memory_gb` from the wizard into OpenTofu. Confirm copy and plan summary must match the chosen size. Distinct from **[VM1 shape scaling (v1)](#vm1-shape-scaling-v1)** (day-2 resize in Danger Zone after the stack exists).

HCL defaults are **4 / 24** (2026-08-17: Setup picker shipped; the Step 3.3 temporary 2/12 defaults were reverted).

---

## Vanilla server bootstrap (MVP)

**Full mechanism authority:** [`Minecraft-Server-Deployment-Blueprint.md`](Minecraft-Server-Deployment-Blueprint.md) — this section is now a short summary; the blueprint has the complete game-manifest schema, directory/systemd/RCON design, and step-by-step bootstrap/upgrade/rollback procedures (also covering the future Paper/Fabric/NeoForge/Forge/Quilt/Modrinth/CurseForge paths so infrastructure contracts do not hard-code "Vanilla"). Read it before implementing or changing on-box Minecraft install code.

**Executable SoT:** [`onbox/mcmgr/`](../onbox/mcmgr/) — shared bootstrap driver + Vanilla piston-meta module + post-manifest idle-agent config sync (§10.2). Door + idle-agent SoT: [`door_vm/`](../door_vm/) and [`vm_agent/`](../vm_agent/).

**Goal:** Setup (and any repair/reinstall path) can install an official **Vanilla** Java server for the version the user selected, without hard-coding a jar URL.

**Preferred approach (verified 2026-08-11):** use Mojang’s **piston-meta** version manifests — **not** a bare `GET https://mojang.com` (that is not the version API).

### Download flow

1. **Version list:** `GET https://piston-meta.mojang.com/mc/game/version_manifest_v2.json`  
   - Prefer **v2** (`sha1` of each version JSON is included). Legacy `version_manifest.json` / `launchermeta.mojang.com` still redirect/work but piston-meta is current.  
   - `latest.release` / `latest.snapshot` give the current tips; UI should default to **`latest.release`**.  
   - Each entry has `id` (e.g. `"1.21.1"`), `type` (`release` / `snapshot` / …), and `url` to that version’s metadata JSON.
2. **Resolve selection:** find the object where `id` equals the user’s choice (e.g. `1.21.1`).  
   - MVP UI: list **`type == "release"`** by default; optional Advanced toggle for snapshots.  
   - Note: Mojang version ids evolve (e.g. manifest `latest.release` may be a newer scheme such as `26.2`) — always drive the picker from the live manifest, never a hard-coded id list in the installer.
3. **Version metadata:** `GET` the `url` from step 2.  
   - Server jar: `downloads.server.url`  
   - Integrity: `downloads.server.sha1` (verify after download)  
   - Java hint: `javaVersion.majorVersion` / `javaVersion.component` (e.g. 1.21.1 → major **21**) — install matching **aarch64** JRE/JDK on VM1 (Ampere), not an x86 build.
4. **Fetch jar:** download `downloads.server.url` (hosts such as `piston-data.mojang.com`) onto VM1 during bootstrap (Manager may download then SFTP, or VM1 may curl with instance egress — either is fine; prefer verifying sha1 before enabling the unit).

### Bootstrap checklist (MVP)

- Write `eula.txt` = accepted only after wizard EULA step.  
- `server.properties` / systemd `minecraft.service` pointing at the jar; `Restart=` policy so door wake brings the game up.  
- **`white-list=false`** and **`enforce-whitelist=false`** — friends are gated by **OCI Security List `/32`s**, not `whitelist.json`. Never set `online-mode=false`.  
- Do **not** use the ancient disabled `s3.amazonaws.com/Minecraft.Download/...` URL pattern.  
- Record chosen version id (+ jar sha1) in local config / `meta/infra.json` for support and Connect existing.

**Example (1.21.1, checked 2026-08-11):** metadata package URL under piston-meta → `downloads.server.url` on piston-data → sha1 `59353fb40c36d304f2035d51e7d6e6baa98dc05c`; `javaVersion.majorVersion` = 21.

---

## Setup game types (v1)

**Full mechanism authority:** [`Minecraft-Server-Deployment-Blueprint.md`](Minecraft-Server-Deployment-Blueprint.md) §§16–29 — Paper/Fabric/NeoForge/Forge/Quilt research, Modrinth/CurseForge/manual-upload pack pathways, ARM64/native-mod risk, and the MVP/v1/later/unsupported classification table. This section stays a product-staging summary; do not re-derive API/mechanism details here — link to the blueprint instead.

MVP Setup stays **official Mojang Vanilla only** (+ version picker). **v1** expands the Setup game path:

```text
Server type:  Vanilla  |  Modded
                 │            │
                 ▼            ▼
        Default Vanilla    Upload modpack
        Optimized Vanilla  (.zip / .mrpack / …)
                 │            │
                 ▼            ▼
           MC version      Analyze → confirm
           picker          name / MC version /
                           loader / Java / …
```

### Vanilla branch

| Choice | Intent | Install approach (research direction) |
|--------|--------|----------------------------------------|
| **Default Vanilla** | Stock Mojang server | Same as MVP [Vanilla bootstrap](#vanilla-server-bootstrap-mvp) |
| **Optimized Vanilla** | Better multiplayer performance without a full modded pack | **Primary candidate: Paper** (Bukkit-plugin compatible server). **Not** the same as a Fabric/Forge “modpack”—Paper is a server implementation. Alternative later: a curated Fabric performance set (Lithium, etc.) if we want true mods without plugins. |

**Paper download API (prefer Fill v3 — do not hard-code old v2 URL builders long-term):**

- Docs: [Paper downloads service](https://docs.papermc.io/misc/downloads-service/); Swagger: `https://fill.papermc.io/swagger-ui/index.html`  
- Base: `https://fill.papermc.io/v3/projects/paper`  
- List builds for a game version: `…/versions/{minecraftVersion}/builds`  
- Use download **URL + checksums from the JSON response** (e.g. `downloads["server:default"].url` on `fill-data.papermc.io`) — **do not** hand-assemble legacy  
  `https://api.papermc.io/v2/projects/paper/versions/…/builds/…/downloads/…` paths; Fill **v2** (`api.papermc.io`) is deprecated and scheduled for shutdown (announced sunset **2026-07-01**).  
- Send a descriptive **User-Agent** (app name + contact) per Paper guidance.  
- Prefer **STABLE** channel builds when the API exposes channels.

After Default vs Optimized, show the **Minecraft version** picker (manifest for Mojang; Paper’s version list for Optimized—versions Paper does not build yet should be disabled or hidden).

### Modded branch

**In-app mod browser / pack catalog — rejected. Will not be implemented** (not open research, not “after v1”). The Manager and Setup wizard **must not** let users browse, search, download, or pick a modpack (or individual mods) from the internet inside the app — no trending list, no marketplace, no pack name/URL/ID box. Users **create or download the pack themselves** on Modrinth, CurseForge, FTB, or another tool, then **choose that local file** in Setup or Manager. The only pack-selection UI is a **file picker** and/or **drag-and-drop** of an already-exported archive (`.mrpack`, CurseForge export zip, or another supported format). Fetching files **already named inside** that uploaded archive is import plumbing, not a catalog. This keeps the product a server host/manager and avoids CurseForge “competing product” ToS. Mechanism: [`Minecraft-Server-Deployment-Blueprint.md` §2.4](Minecraft-Server-Deployment-Blueprint.md#24-no-in-app-modmodpack-catalog--architecture-decision).

1. User picks **Modded**.  
2. Import pack via **file picker** and/or **drag-and-drop** into the wizard — the file must already be a complete, exported modpack (supported formats: Modrinth `.mrpack`, CurseForge **Server Files** / filled server-pack zip, other supported layouts). There is no other way to specify a pack. CurseForge **client** exports (manifest IDs, no jars) are **not** imported in v1.
3. **Analyzing modpack** progress UI while the Manager inspects the archive.  
4. Show detected summary for confirmation before continuing Setup: pack name, Minecraft version, mod loader (+ version if known), required Java, file counts / warnings.  
5. On confirm, continue wizard; bootstrap installs loader + **server-side** mods only.

**Pack import intended design (operator 2026-08-23):** [`Pack-Import-Intended-Design.md`](Pack-Import-Intended-Design.md) is the living contract for formats, automatic vs **assisted** homemade zips, skip order, dependency freeze, review UI, and crash follow-up. Homemade client-jar zips **stay supported**; unattended “just works” is **not** the promise. **Implemented** as Step **8.9** ([`V1-Pack-Import-Assisted-Review-Plan.md`](V1-Pack-Import-Assisted-Review-Plan.md)). Live pointer [`NEXT.md`](NEXT.md). Pass 3 stays blocked until Step **8.10** exits and the operator starts it.

**Format hints (more research at implementation time):**

| Source | Typical marker | Notes |
|--------|----------------|-------|
| CurseForge | Root `manifest.json` | **v1:** import only when the zip **already contains the jars** (CurseForge **Server Files** / filled server pack) via the manual adapter. **Do not** ship a CurseForge API key or resolve client-export `projectID`/`fileID` lists. Client exports: download **Server Files** from that pack’s CurseForge page, or use a Modrinth `.mrpack` if one exists. API import is **deferred** (ToS: non-transferable key, no cache, no proxy; open-source WinExe cannot hold a product key; $0 forbids a relay). Revisit only with an **operator-owned** key in Credential Manager and downloads on the admin PC only. Not a catalog. |
| Modrinth | `modrinth.index.json` (mrpack) | The `.mrpack` the user uploads already embeds each file's CDN download URL — fetching those files is a plain download, not a Modrinth "browse" API call. |
| FTB / others | Varies | Add adapters as needed |

**Hard requirements:**

- **No in-app catalog/browse/search/download UI for mods or modpacks** — **rejected**, will not be implemented. Users supply a file they already have. Do not “just add a convenience picker.”  
- **Exclude client-only mods** (e.g. Sodium, Iris, many UI/minimaps) from what is installed on VM1 — use pack metadata `side` / environment fields and known client-only lists. **`.mrpack`:** still fail when `env.server` is unclear after filters. **Homemade / unstructured zip:** assisted review (default-keep unknowns; operator may skip) plus **never skip a required dependency of a kept jar** — see [`Pack-Import-Intended-Design.md`](Pack-Import-Intended-Design.md). Do not treat “warn and install anyway” as the long-term contract.  
- Download **mod loaders** (Forge / NeoForge / Fabric / Quilt / …) via their official APIs/metadata, not scraped launcher pages.  
- Design **adapter interfaces** per pack platform + per loader so CurseForge/Modrinth/loader URL churn does not require rewriting the whole Setup pipeline (version-pin HTTP clients; integration tests against sample packs). Operator-local archives: [`Sample-Packs.md`](Sample-Packs.md) (gitignored `data/sample-packs/`; CI stays on `tests/fixtures/`). Agents must **not** download kitchen-sink packs on their own — if a format is missing, pause and ask the operator.  
- Legal: do not redistribute paid modpack contents; user supplies the pack; EULA / third-party ToS copy in wizard.  
- Ampere: all JVMs and native loader bits must be **aarch64**-capable or pure Java.

**Open research (v1 implementation, non-blocking for MVP):** FTB formats, whether “Optimized Vanilla” stays Paper-only or also offers a Fabric performance preset, and how pack analysis runs (local Manager vs brief VM job). **Closed for v1:** CurseForge API key / client-export URL resolve — deferred (ToS / key custody); Server Files zip stays. **Rejected / not open:** an in-app mod or modpack browser — users always import a file they obtained outside the app.

---

## Manager UI (behavior detail)

**These sketches are not locked.** Treat tab names, top-bar chrome, and wizard page order as **ideas** to iterate with the operator during UI work (MVP plan **Phase B** / Blazor Hybrid; Phase 6 Avalonia polish is abandoned as the UI vehicle). Agents doing UI-design work should:

1. Use or **offer** the `find-skills` skill to look for UI / software-design skills unless the operator already told them to.  
2. Look at similar products — especially **[Pterodactyl](https://pterodactyl.io/)** panel — for what a game-server Manager typically exposes (console, files, backups, players, schedules), then map that to **this** product’s MVP/v1/later staging rather than copying a paid hosting panel wholesale.  
3. **Search nuget.org and add packages** to **`McManager.Hybrid`** when they help (CSS, fonts, icons, extra Blazor/WPF host needs). Do **not** add Avalonia themes to Hybrid. Keep OCI SDK references on `McManager.Core`. Prefer well-known OSS licenses; ask before paid or commercial packages.  
4. Confirm large visual or information-architecture changes with the operator before implementing.

### Top bar (novice)

**Left — status cluster**

- Compact **status card** (not a terminal/console): **Status** (`Running` / `Stopped` — Minecraft joinable at the play IP), **Play IP** + copy, **Players** (`n/max` when known, otherwise `—`). No `?` help on those three fields. **Running** text is green; **Stopped** text is red.
- Door / VM1 / doorbell technical state is **not** shown here; it belongs on **Advanced**.
- Immediately under that card: **Start/Stop** (door-aware wake / idle-empty) and **Restart** (Minecraft process only; greyed only until first status load, or while a Start/Stop/Restart is in flight). These buttons are filled (not the same card chrome as status/pins). **v1:** if the $1 spend-brake lock flag is set, Start is blocked by a full-window warning and a typed-confirmation popup — see [$1 spend-brake lock (v1)](#1-spend-brake-lock-v1).
- Remaining top width: **pinned usage cards** (today’s uptime vs daily allowance, this month vs cap, daily average, rollover bank) in a **fixed** 2×2 — they do not grow with the window. Extra window width **centers** the whole manage shell. Prefer wall-clock **hours** on the novice pins; OCPU-hours stay on the Usage tab with a help icon. Do not print the rollover caption on the card — the `?` already explains unused hours from earlier days this month. Long tab pages scroll **inside the tab body**; the status/pins/tab strip stay put so a window scrollbar cannot cover the pins. The tab-body scrollbar lives in the **right window gutter** (thin overlay-style thumb) so overflowing tabs do not shift cards left of the chrome row.
- Small **?** hover icons for extra explanation on **pinned usage** (and on Advanced / Usage / Troubleshooting where a field is not obvious). Status / Play IP / Players do not get them.
- Body copy outside Advanced / Troubleshooting / Danger Zone stays novice (no flags, cloud firewall, VM1, door, OCI, Object Storage, Security List, tofu, issue IDs).
- Advanced: separate raw VM power vs Minecraft systemd controls, plus a **technical status** panel (game VM + door VM lifecycle, door service state). Debug-only host probes (confirm / clipboard / picker) live here, not as a global bar.
- Live Hybrid theme: **twilight granite + cobalt accent** (dark). Operator rejected the B2 light warm-gray remap, then the copper accent.

**Right — chrome (v1)**

- **Bell** — notification center (e.g. oversized-world backup warning; later other product notices). MVP may show a **placeholder icon** in a custom title bar without a notification center.
- **Cog** — program settings.
- **Overflow / hamburger** (three horizontal bars) — About and other secondary actions.

MVP ships the status cluster, power buttons, copy IP, and pinned usage cards. Full notification-center / settings / overflow is **v1**. The Blazor Hybrid host uses **native OS window chrome**; a custom caption is not required unless the operator later asks. This is not a locked visual spec.

### Tabs (conceptual)

| Tab | MVP | v1+ |
|-----|-----|-----|
| **Usage** | Ledger + dashboard (monthly allowance, MTD usage, avg hours/day, rollover; unbudgeted when that concept exists later) | + Usage reconcile visibility. Paid projections are **later / far future**, not v1. |
| **Whitelist** | CRUD allowlist IPs; Security List; private only; single IPv4 `/32` | **CIDR ranges** via add-IP Advanced — see below. **No** public toggle or blacklist (rejected). |
| **Server Management** | Backups list; **Download World Save** from Object Storage; upload/replace | + customization, messages; oversized-world adaptive download (SSH); **Wipe world**; **Modding** inspect + re-download pack; pack *replace* is after v1 |
| **Advanced / Danger Zone** | One combined tab: idle timeout; disable guardrails (**reboot re-enables idle**—see MVP Danger Zone); break-glass VM vs Minecraft; **Troubleshooting one-shots** (park play IP, door reset, diagnose `wait_forge`, …) | **Split into two tabs** (see [Advanced vs Danger Zone (v1)](#advanced-vs-danger-zone-v1)) |
| **Advanced** | *(same combined tab)* | Power-user tools that do **not** disable $0 brakes or delete the stack: idle *timeout*; manual SSH IPs; raw VM vs Minecraft controls; **Troubleshooting one-shots** (MVP); remaining Door/IP Repair UX in v1; infra meta; Setup re-entry. After v1: reserved-IP / VM Info / extra-deploy profiles. |
| **Danger Zone** | *(same combined tab)* | Disable idle/guardrails; **VM1 shape scale**; **delete all cloud infrastructure**. **Paid / spend mode** is later / far future, not v1. |
| **Console** | — | RCON + logs |
| **Players** | — | — (after v1) |

Tab name stays **Whitelist**. Do not rename to IP Management for a public/blacklist feature that will not ship.

### VM1 shape scaling (v1)

Lives in **Danger Zone** after the v1 tab split (not the novice top bar). Showing the current size in Advanced or VM Info is fine; **applying** a resize belongs in Danger Zone because it changes Always Free OCPU-hour burn rate.

**Show:** current VM1 shape (OCPUs + memory).

**Scale up / down:** Manager updates shared **config / meta** so usage math, door wake budget gate, and UI all use the new size. Ledger intervals must carry the **shape used during that interval** (OCPUs and/or memory—exact fields TBD) so hours before a resize stay accurate when recomputed.

**Hard requirements before apply:**

- Clear warning of what the user is doing (Always Free OCPU-hour burn rate changes).  
- VM1 **and** Minecraft must be **stopped** first (OCI shape change).  
- Explain that **available monthly playtime** changes: **less** wall-clock uptime if scaling **up**, **more** if scaling **down** (same ~1500 OCPU-h envelope ÷ more or fewer OCPUs).  
- Optionally preview estimated hours/day or hours/month at the new size.

**PAYG note (verify before shipping):** on a Pay As You Go tenancy, A1 Flex can often be resized past the common **4 OCPU / 24 GB** Always Free–comfortable target (e.g. toward **8 OCPU**, with **48 GB** possible but likely overkill), as long as monthly OCPU-hours stay under the free Ampere allowance—**only if** that allowance is not restricted to specific shapes. Confirm in Oracle docs during pre-v1 work; do not advertise oversized shapes until confirmed.

**Why v1 (and schema early):** resize touches ledger + config contracts. Prefer MVP ledger already storing per-interval shape so v1 does not require another breaking storage migration.

**Not the same as Setup:** initial size is chosen in the [Setup wizard](#setup-wizard-behavior-detail) (2/12 vs 4/24). This section is **changing** size after deploy.

### IP Management (v1)

**Rejected (will not be implemented, not after-v1):** public Minecraft (`0.0.0.0/0` on 25565), a Manager **Make server public / private** toggle, and a **blacklist**. OCI Security Lists (and NSGs) are **allow-only**; there is no simple, correct deny. The only Security List “blacklist” would be synthesizing “world minus these CIDRs,” which is error-prone and can hit the ~200-rule cap. Paid Network Firewall is out ($0).

The product is **private only**. Friends join only when the admin has allowlisted their IPv4 or CIDR. Setup always deploys private. There is no day-2 public mode.

**Keep:**

- **Allowlist (whitelist) panel** — always visible; add/edit/delete IPs (IP required; name optional). Persist locally and in Object Storage `ip/allowlist.json` when that object already exists. **v1:** Add-IP **Advanced** CIDR — see [Allowlist CIDR ranges (v1)](#allowlist-cidr-ranges-v1). MVP remains one IPv4 → `/32`.
- Tab name **Whitelist**.
- SSH admin rules and non-Minecraft ingress preserved on every Security List rewrite (full-replace caution).

**Do not keep / do not rebuild:** mode field, blacklist list, `ip/mode.json` as a product contract, Minecraft `0.0.0.0/0`. Leftover `mode` / `blacklist` keys in local JSON or a leftover `ip/mode.json` in a bucket are unused; Step **3.4** removed the Manager code path.

### Advanced vs Danger Zone (v1)

**MVP** ships one combined **Advanced / Danger Zone** tab (idle timeout, disable guardrails, break-glass VM vs Minecraft, **Troubleshooting one-shot repairs**). **v1** splits that into two tabs so novices are not one mis-click away from deleting the stack or turning off $0 brakes, while power-user tools stay reachable.

**Rule of thumb:**

| Tab | Put here |
|-----|----------|
| **Danger Zone** | Anything that can **leave Always Free / disable the “stay at $0” brakes**, and anything that **deletes cloud infrastructure**. Strong warnings + extra confirmation. |
| **Advanced** | Other power-user settings and diagnostics that do **not** turn off those brakes and do **not** destroy the stack. |

**Danger Zone (v1 inventory):**

- Disable idle agent / daily SoftStop guardrails (testing / troubleshooting only; VM1 boot still force-enables idle — same MVP safety).  
- **Apply VM1 shape scale** (changes OCPU-hour burn rate; current size may also be shown in Advanced).  
- **Delete all cloud infrastructure** — see [Delete all infrastructure (v1)](#delete-all-infrastructure-v1).  

**Not v1 (later / far future):** **Paid / spend mode** (willingness to spend past the free envelope) — see [Paid / spend mode](#paid--spend-mode-later--far-future-not-v1).

**Advanced (v1 inventory):**

- Idle **timeout** (minutes) — how long VM1 may stay up with **no players or with Minecraft not running** before SoftStop. Changing this is not the same as disabling the idle agent.  
- Manual SSH allow IPs / admin IP repair.  
- Separate raw **VM** power vs **Minecraft** systemd controls (break-glass; still warn that top-bar Start/Stop is the doorbell-aware path).  
- Door/IP **Repair / Reset** remaining UX (MVP already has one-shot park-IP / diagnose / door-reset on the combined tab — MVP plan 4.4).  
- Infra meta refresh / publish; **Deploy / repair infrastructure** (Setup re-entry).

After v1, Advanced also gains reserved-IP / VM Info / extra-deploy profiles — those stay Advanced (they do not delete the tenancy or opt into paid spend). Maintenance start-without-moving-the-play-IP still **burns VM1 OCPU hours**; it is not a Danger Zone “disable brakes” action, but the UI should make the usage cost obvious.

Do **not** split the tab during MVP. Do **not** put novice Start/Stop on Danger Zone.

### Allowlist CIDR ranges (v1)

**Problem:** some friends use **dynamic / CGNAT** addresses where the host part changes but a prefix is stable (operator example: always `172.56.x.x` → typically `172.56.0.0/16`). A single `/32` allow rule breaks every time the address changes.

**UX:** the add/edit-IP window stays a simple **single IPv4** field by default (MVP behavior). A small **Advanced** control on that window reveals an extra field for a **CIDR prefix** (e.g. `172.56.0.0/16`) used **instead of** a single address — not a start–end “range” widget. OCI Security Lists take CIDR; do not invent a second syntax.

**Behavior:**

- Persist the prefix (local friends list + Object Storage allowlist when that object exists). Security List sync writes that CIDR as the rule source (same description = player name).  
- Default: CIDR applies to **Minecraft 25565 TCP/UDP** only. **SSH / door `:8080` admin rules stay `/32`** unless the admin is explicitly editing *their own* admin entry (do not silently open SSH to a `/16`).  
- Warn that a prefix is wider than one host (`/16` is 65,536 addresses). Prefer the tightest prefix that actually matches the friend’s ISP; reject obviously reckless prefixes (implementation floor TBD — see Open questions).  
- IPv4 only unless v1+ later adds IPv6 (not required here).

**MVP:** single IPv4 → `/32` only. Do not expose CIDR in the MVP add-IP dialog.

### Wipe world (v1)

**Server Management**, near **Download World Save**.

Deletes the **live world save** on VM1 (`world_path`) so the next Minecraft start generates a **new** world. Mods, loader, `server.properties`, and Object Storage **backups are not deleted**.

**UX:**

1. Button (e.g. **Wipe world** / **Start over**).  
2. Popup explains: this deletes the current world on the server; it cannot be undone except by restoring a backup; Object Storage backups (if any) remain. Strongly point at **Download World Save** first.  
3. User must click **Confirm** (explicit approve — not a tiny “OK”). Then run the wipe.  
4. Minecraft should be **stopped** first (or the action stops it). After wipe, starting the game creates a fresh world.

**Operator override (2026-08-19, Pass 1 P8):** after wipe, Minecraft should **start again automatically** (not leave-stopped). Pass 1 bug-fix plan **P8** is operator will. Do not “correct” auto-start back to leave-stopped to match this list. This section may drift until P8 updates copy.

Do not recycle this button into “delete backups” or “reset the whole VM.” World import/replace already exists as a separate Server Management path.

Mechanism notes (world folder, backups): [`Minecraft-Server-Deployment-Blueprint.md`](Minecraft-Server-Deployment-Blueprint.md) §11.

### Server Management modding (v1)

For stacks that Setup installed as **Modded** (Vanilla/Paper: hide or show a short “not a modded server” empty state).

**Modding** section on **Server Management**:

- **What’s installed:** list (or summary) of server-side mods currently on VM1 — names / files from the live `mods/` tree and/or the game manifest. This is inspect-only in v1.  
- **Download pack:** copy of the **original imported archive** the admin gave Setup (`.mrpack` / CurseForge Server Files zip / other supported file) — for when they deleted their local copy and still need to share it with friends. **Do not** zip VM1’s `mods/` folder and call that the pack: Setup strips client-only mods, so a server-side zip is **not** a playable client pack.

**Where the archive lives (v1 intent):** keep the imported file on the **admin PC** in Manager local data (survives “I deleted the zip on my Desktop”). Optionally also keep a copy on VM1 outside the live `mods/` tree. Prefer **not** to put large pack zips in Object Storage competing with the ~9.5 GiB world-backup cap unless a later decision says otherwise. If the local copy is missing and there is no VM1 copy, tell the user we cannot reconstruct a client pack from server `mods/` alone.

**Rejected feature — in-app browser:** Download pack is “give me the file I already imported,” not “browse/download a new pack from Modrinth or CurseForge.” There is no in-app catalog at this stage or later.

Changing the pack is [after v1](#modpack-replace-after-v1).

### Delete all infrastructure (v1)

**Danger Zone** (MVP: same combined Advanced / Danger Zone tab; v1 still splits the tabs). Not on Server Management.

**Action:** tear down **all product cloud resources** this Manager deployment created (compartment contents: VMs, VCN, reserved IP, bucket/backups, Function/budget wiring, etc. — exact list = whatever OpenTofu manages, via `tofu destroy` / equivalent). See [`Automated-Infrastructure-Deployment.md`](Automated-Infrastructure-Deployment.md) §12.4.

**UX:**

1. User clicks the delete-infrastructure control.  
2. **Popup** explains what will be destroyed (compute, network, reserved play IP, Object Storage including **world backups**, IAM artifacts the product created). Worlds that exist only in the cloud are gone.  
3. Explain clearly: this **only deletes the resources**. It does **not** close the Oracle account. The user must still **sign in to the OCI Console in a browser** to delete the **tenancy** if they want the account itself gone.  
4. A text box; the user must type **`confirm`** (lowercase, exact). The **Delete** button stays disabled until that string matches.  
5. Then run destroy. The popup **stays open** with a log and percent until OpenTofu reports OCI deletion finished. Surface failures (partial destroy, missing tofu state, bucket not empty, etc.) rather than claiming success.

**MVP:** this UI shipped on the combined Advanced / Danger Zone tab for greenfield E2E teardown (Step 7.2). Bucket `prevent_destroy` still blocks stray CLI destroy until the confirmed Manager path writes a temporary override.

### Maintenance / reserved-IP control (after v1)

Lives on **Advanced** (not the novice top bar). Goal: the admin can run Minecraft on VM1 **without** inviting friends onto the reserved play IP — e.g. maintenance, testing, or “only I should join.”

**Panels / actions:**

- **Reserved IP assignment:** show which VM currently holds the reserved public play IP (door vs VM1); controls to assign it (same underlying `ip_to_vm1` / `ip_to_vm2` operations the door already uses).  
- **Start VM1 without moving the play IP:** start VM1 and the Minecraft process while the reserved IP **stays on the door**. Top-bar door-aware Start remains the normal “friends can play” path; this is the escape hatch. Lab Manager already has a similar break-glass start — productize it here with the MOTD behavior below.  
- **VM Info:** reserved play IP (the address friends use) plus each VM’s **ephemeral** public IP (SSH / admin / this maintenance join path). Copy buttons as useful.

**How the admin plays while friends cannot:** start VM1 without moving the reserved IP; connect the Minecraft client to **VM1’s ephemeral public IP** on 25565 (admin’s IP must already be allowlisted). Friends still target the reserved IP and hit the **door**.

**Door behavior in this mode:** do **not** wake VM1 on connect. MOTD / login kick should say the server is **under maintenance** (or equivalent — polish copy at implementation; meaning: “you cannot play right now; this is not a budget-exhausted or starting-up state”). Distinct from idle-wake and daily-budget-exhausted messages.

**Limitation to decide at implementation:** Security List allow rules are subnet-wide. A friend who already knows VM1’s ephemeral IP and is allowlisted could still reach Minecraft. Default product: **do not advertise** the ephemeral game address (VM Info is Advanced-only); friends use the reserved IP and see maintenance MOTD. Optional tighter lock (temporarily restrict 25565 to admin `/32`s) can be considered then — not required for the first version of this feature.

**Not the same as MVP Troubleshooting “park play IP”:** that button assigns the reserved IP to **whichever VM should hold it** (VM1 if RUNNING, else door) so a stuck doorbell recovers. Maintenance mode is the opposite: run Minecraft **without** moving the play IP off the door.

Idle/budget metering still counts VM1 RUNNING time. This mode is not a way around Always Free limits.

### Modpack replace (after v1)

Expands the v1 **Modding** section: a **Change / replace pack** button. File picker / drag-and-drop of an already-exported pack (same [no in-app catalog](#modded-branch) rule as Setup).

**Detect light swap vs full re-setup** (do not always re-run the entire Minecraft install):

| Situation | Path |
|-----------|------|
| Same Minecraft version + same loader (and loader version compatible); pack is mostly the same — e.g. config tweaks, one or two mods added/removed | **Light swap:** stop Minecraft → converge `mods/` + `config/` / overrides to the new desired set (delete stale jars, do not layer) → start. No Java/loader reinstall. |
| Different Minecraft version, different loader, large pack identity change, or analysis cannot prove it is a small delta | **Full re-setup:** same automated pipeline Setup uses (installer module, Java if needed, pack install, manifest, systemd). Keep the world unless the user also chose Wipe world. Warn if the new pack is unlikely to load the existing save. |

Reuse the blueprint converge-not-layer rule and upgrade pipeline rather than inventing a third installer. Exact “mostly the same” heuristic (file-diff threshold, manifest identity, etc.) is an implementation detail — see Open questions.

**Not** a per-mod IDE and **not** an in-app pack/mod browser (that idea is **rejected**). Same file-picker import rule as Setup.

### Multi-deploy profiles (after v1)

**MVP / v1 Connect existing** attaches this Manager install to **one** stack (button-gated auto-detect or seeded local config). **After v1**, Advanced can **connect an additional** infrastructure deployment (second tenancy, second compartment, lab vs “real” friends server, etc.).

**Add-deployment flow:**

1. User clicks **Connect additional infrastructure** (Advanced).  
2. Prompt for that deployment’s **OCI API key config** (`~/.oci` config file / profile) and the **SSH private keys** for its VMs.  
3. Run the same kind of **auto-detect + validate** as Connect existing (compartment tag/name → bucket → `meta/infra.json`, required OCIDs, infra schema).  
4. **Valid:** save as a new **profile** and offer to switch to it.  
5. **Not valid:** say so plainly; tell the user to check they picked the **correct API config file** and **correct VM SSH keys** (wrong key/tenancy is the usual failure, not “the product is broken”).

**Switcher:** dropdown (or equivalent) on Advanced to choose which profile the Manager is currently talking to. Switching reloads that profile’s config, friends list, and OCI/SSH session — do not silently mix OCIDs from profile A with keys from profile B.

**On-disk layout:** local data becomes **per-profile folders** (names TBD), each holding that deployment’s `config.local.json`, friends/allowlist, and other non-secret-or-path state. Secrets still stay out of git (API PEM path, SSH key path, RCON). A single flat `data/config.local.json` is the MVP/v1 shape; do not break that until this feature is implemented. See [`Local-Config.md`](Local-Config.md).

This is **not** the same as two admins sharing one stack (that remains a later multi-admin concern).

---

## Usage budget behavior (MVP+)

- Ledger from VM1 start/stop × **OCPUs for that interval** (and ideally memory / shape id); explain UI in **uptime hours** with tooltip for OCPU-hours. Shared via Object Storage; door **reads** for wake gate (product intent).  
- **Per-interval shape fields** are required for correct history once VM1 can be resized (v1); store them from MVP if the ledger format is being finalized anyway.  
- Shared **config / meta** holds the **current** VM1 size used for live burn-rate and remaining-uptime estimates.  
- Rollover from under-budget days.  
- Warnings: 30 minutes out → every 5 minutes until stop; on rollover, every 10 minutes with session + total rollover used.  
- After daily exhaustion stop: **only admin** via Manager (budget tools / Danger Zone)—door refuses player wake with clear message.  
- Soft monthly target configurable later; MVP ships a safe default under ~1500 OCPU-h.  
- When current shape can run ~24/7 inside that envelope (e.g. 2 OCPU → ~744 OCPU-h/month if always on), product may still meter usage but should not nag as if the user were on a scarce 4-OCPU budget (v1 messaging / MOTD nuance).  
- Prototype door-local Phase A daily ledger: align to this model in a later code pass (see document authority).

---

## $1 spend-brake lock (v1)

**MVP** still deploys the $1 compartment budget → Events → Function SoftStop path (halt spend). **v1** adds a durable lock so the admin cannot quietly turn the stack back on the same month, and so the door cannot wake VM1 while the lock is set.

How the Function **image** reaches a user’s tenancy: [Spend-brake Function image](#spend-brake-function-image-v1-before-release) (CI-built ARM, copy into OCIR — not Docker on their PC).

### What the Function must do (v1)

On a real threshold alert (ignore budget **RESET**):

1. SoftStop the Minecraft host (**VM1**) — required.  
2. Write a durable Object Storage **lock flag** — frozen key **`meta/spend-brake-triggered.json`** (product [`Contracts-Object-Storage.md`](Contracts-Object-Storage.md)). Presence of the flag means “$1 last-resort budget has fired this period.”  
3. **Door Micro (VM2) — leave running (product v1).** Oracle [Always Free Resources](https://docs.oracle.com/en-us/iaas/Content/FreeTier/freetier_topic-Always_Free_Resources.htm) (read 2026-08-17): AMD **`VM.Standard.E2.1.Micro`** is a separate Always Free allowance (up to two instances), **not** Ampere A1 OCPU-hours / GB-hours. PAYG upgrade still does not charge Always Free resources — only usage **above** those limits. One door Micro therefore does not accrue Ampere spend. Stopping it drops MOTD, blocks reconcile, and strands the play IP (FN-ISSUE-1). **Do not SoftStop VM2.** Live lab Function **image** may still SoftStop both until an authorized `fn push` of [`functions/shutdown_vm/`](../functions/shutdown_vm/README.md).

**If the door is left running:** door wake / reconcile / MOTD paths must **read this flag** (same polling discipline as the budget gate) and **never START VM1** until Manager clears it. MOTD/kick copy should say the monthly spend brake has fired and the admin must use Manager after a new calendar month.

**If both VMs are stopped:** the Function can interrupt the stack in **any** IP/lifecycle state (play IP on VM1, on door, or mid-move). Manager’s post-confirmation Start must not assume idle-or-playable; it must **start the needed VMs and reconcile to a valid doorbell state** (reserved IP parked on door, or a documented playable handoff). Reuse / extend v1 **Door/IP Repair / Reset** rather than inventing a second recovery path.

### Manager UX when the flag is set

Treat this as a **blocking safety overlay**, not a toast or a bell-only notice.

**On Manager open / whenever the flag is observed:** fill the **entire window** with a warning that explains what happened, what it means, and what to do. Intended copy (polish wording at implementation; keep the meaning):

> The $1 budget safety mechanism — in place to ensure you are not accidentally charged — has been triggered. This means your server has exceeded the allowed monthly free usage, and any further use will result in your account being billed. The safety mechanism has automatically shut down your VMs to prevent further charges. The mechanism can take several minutes to activate, so your account may have been charged somewhere between $1 and $2, but there will be no further charges. You should wait until the start of the next calendar month before attempting to turn your server back on. To turn your server back on, you must give explicit permission.

Adjust “your VMs” vs “your Minecraft VM” to match the actual stop policy (product v1: Minecraft VM only; door stays up).

**Any time the user tries to Start** while the flag is set: show a **large popup** with the same explanation, plus a text field. The user must type this statement **exactly** (copy-paste may be allowed; partial / fuzzy match must not):

> I confirm that we have entered a new calendar month and that my free monthly usage limits have been reset. I understand that if I ignore these warnings and turn on my server before a new month has started, the card I created my Oracle Cloud account with will automatically be charged for the excess usage.

Only after the field matches **exactly** is a **Start Server** button at the bottom of the popup enabled. Clicking it:

1. Starts the stack and **reconciles to a valid IP/door state** (see above).  
2. **Clears / deletes the lock flag** (Manager is the only clearer).  
3. Does **not** bypass idle/daily/monthly OCPU gates — those still apply in the new month.

Do **not** auto-clear the flag at calendar-month rollover. The admin must type the confirmation. The product may later add a “it looks like a new month has started” hint, but that hint must not skip the typed statement.

### Residual ~$1–$2 charge (guide + Setup — MVP copy, v1 lock UX)

Oracle’s budget fires when **actual spend reaches $1**; the Function is not instantaneous. Public guide and Setup must say: the product is built to stay **completely free**, but if this last-resort brake ever triggers, the user may be billed **about $1–$2** for that month, then nothing further while the brake holds. This honesty belongs in MVP guide/Setup even though the full-window lock is v1.

---

## Product resource naming & discovery

**Terminology:** OCI **compartments** (not “domains”). Docs and code should say compartment.

### Greenfield display names (OpenTofu / Setup)

| Resource | Display / product name |
|----------|------------------------|
| Stack compartment | `mcmgr` |
| VCN | `mcmgr-vcn` |
| Public subnet | `mcmgr-subnet-public` |
| Internet gateway | `mcmgr-igw` |
| Security List (Minecraft / SSH / door) | `mcmgr-sl` |
| VM1 (A1 Minecraft host) | `mcmgr-vm1` |
| VM2 (door Micro) | `mcmgr-door` |
| VM1 play secondary private IP | `mcmgr-vm1-play` |
| Door play secondary private IP | `mcmgr-door-play` |
| Reserved play public IP | `mcmgr-play-ip` |
| Object Storage bucket | `mcmgr-shared-data` |
| $1 spend budget | `mcmgr-budget-1usd` |
| Events rule (budget → Function) | `mcmgr-events-budget-alert` |
| Functions application | `mcmgr-fn-app` |
| Spend-brake Function | `mcmgr-fn-softstop` |
| OCIR repo for that Function | `mcmgr-fn/softstop` |
| Dynamic groups | `mcmgr-dg-instances`, `mcmgr-dg-door`, `mcmgr-dg-fn` |

Do **not** preserve ad-hoc Console names from the operator’s first manual deploy (`minecraft-vm3`, `PrimaryConnection`, `BudgetControlApp`, etc.) when writing product IaC.

**Dynamic groups (3, not 4):** lab had a both-VMs Object Storage group, a door group, a VM1-only group, and a tenancy-wide Functions group. Product consolidates to compartment-scoped `mcmgr-dg-instances` (Object Storage + `use instance-family`), tag-scoped `mcmgr-dg-door` (IP move), and compartment-scoped `mcmgr-dg-fn`. Details: product [`docs/Lab-Reference-Stack-Notes.md`](Lab-Reference-Stack-Notes.md).

### Compartment discovery tags

| Freeform tag | Value | Purpose |
|--------------|-------|---------|
| `mcmgr-domain` | `mc-server-compartment` | Marks the stack compartment for Connect existing / auto-detect |

**Match rule:** a candidate compartment is the product stack if **either**:

1. Display name equals **`mcmgr`** (greenfield), **or**
2. Freeform tag **`mcmgr-domain` = `mc-server-compartment`** (operator lab: tag on the compartment that holds the live stack, including when that is Default)

Greenfield also sets per-instance freeform **`mcmgr-role`** = `vm1` | `door` so `mcmgr-dg-door` can match the door without pinning an instance OCID. Other `mcmgr-role` values (`bucket`, …) remain optional once `meta/infra.json` exists.

### Object Storage object keys (product)

| Key / prefix | Role |
|--------------|------|
| `meta/infra.json` | Stack identity + OCIDs for Connect existing |
| `meta/` flags / dirty helpers | Incl. oversized-world backup flag (exact filename TBD at contract freeze) |
| `meta/spend-brake-triggered.json` | **v1:** durable “$1 budget fired” lock (Function sets, Manager clears, door reads) — see [$1 spend-brake lock (v1)](#1-spend-brake-lock-v1) |
| `ledger/` | Usage ledger + lease |
| `budget/` | Budget / idle config |
| `backups/` | World zip backups |
| `ip/`, `messages/` | As product needs |

Bucket name greenfield: **`mcmgr-shared-data`**. Lab may still use an older bucket name; meta records the live bucket so Connect existing does not assume the greenfield name.

---

## Infra meta contract (`meta/infra.json`)

**Goal:** After auto-detect finds compartment + bucket + this object, Manager can write local config and manage the stack **without listing more OCI resources**.

Suggested fields (align with Manager `config.local` / lab Manager config; exact JSON schema frozen in product `docs/Contracts-Object-Storage.md` during MVP Phase 2):

| Field group | Contents |
|-------------|----------|
| **Versions** | `infra_schema`, `stack_version`, `created_at`, `updated_at` |
| **Mode** | `mode`: `always_free` \| `paid`; `region`; `tenancy_id`; `compartment_id` |
| **Play** | `reserved_public_ip`, `reserved_public_ip_id` |
| **Game** | `minecraft_version` (manifest id), `server_kind` (`vanilla` for MVP; later `paper` / modded), optional `server_jar_sha1` |
| **Network** | `vcn_id`, `subnet_id`, `security_list_id`; ports (`minecraft_port`, `ssh_port`, door `http_port`) |
| **VM1** | `instance_id`, `display_name`, `shape`, `shape_ocpus`, `shape_memory_gb`, `primary_private_ip`, `secondary_private_ip`, `secondary_private_ip_id`, `ssh_user`, `world_path`, `minecraft_unit` |
| **Door** | `instance_id`, `display_name`, `primary_private_ip`, `secondary_private_ip`, `secondary_private_ip_id`, `ssh_user`, `http_port` |
| **Object Storage** | `namespace`, `bucket`, `bucket_id`, `soft_cap_gb`, prefix map |
| **Budget brake (optional)** | `$1` budget OCID / Function OCID if present |
| **SSH note** | Public key fingerprint or “key material lives only on admin PC” — **never** put private keys or RCON passwords in Object Storage meta |

Local Manager still stores **SSH private key path**, **OCI API profile**, and **RCON password** only on the admin PC (gitignored local config / credential store)—meta points at *which* hosts/OCIDs, not secrets.

---

## Connect existing / auto-detect (MVP)

**Not** silent on every startup. User clicks **Auto-detect infrastructure** (first-run choice or Settings / Setup entry).

**Flow:**

1. Read `%USERPROFILE%\.oci\config` and try each usable **profile** (multi-key / multi-profile supported).
2. For each profile: list compartments; keep those matching display name **`mcmgr`** **or** tag **`mcmgr-domain=mc-server-compartment`**.
3. In each candidate compartment, look for the product bucket (name `mcmgr-shared-data` and/or bucket containing `meta/infra.json`).
4. If `meta/infra.json` validates (required OCIDs present; optional soft `infra_schema` check in MVP):
   - **One** stack → prompt: “Existing infrastructure detected. Connect?” + short summary (region, compartment name, play IP, VM display names).
   - **Multiple** → chooser listing those summaries.
5. On Yes: hydrate local Manager config from meta; proceed to manage UI.
6. On No / none found: offer Setup wizard or manual config import.

**MVP-light vs v1:** MVP may soft-warn on schema mismatch; **v1** enforces infra vs app version more strictly. Phase 1 manage work continues to use operator-seeded `data/config.local.json` until Phase 4 implements this flow.

Connecting a **second** stack from the same Manager install (profile folders + Advanced switcher) is **after v1** — see [Multi-deploy profiles (after v1)](#multi-deploy-profiles-after-v1). MVP/v1 remain one connected deployment.

---

## Oversized world backup (v1)

**Problem:** Soft-cap eviction works when *many* backups fill ~9.5 GiB, but a **single** world zip can exceed the soft cap (especially modded). Upload is impossible without breaching Always Free Standard headroom. Lab `world_backup.py` already refuses such uploads; product must surface that state.

**On-box (VM1):**

1. If local zip size **>** soft cap (~9.5 GiB): do **not** upload; set durable Object Storage flag (e.g. under `meta/`); skip further automatic Object Storage world backups until cleared.
2. Prefer not to leave SoftStop stuck retrying a doomed upload.

**Manager (v1):**

1. Detect flag at startup and whenever flags/meta are read for other reasons.
2. **Notification** (bell): world backup exceeds allowed size; automatic Object Storage backups stopped; admin should periodically use **Download World Save**.
3. **Download World Save** adapts:
   - Flag **clear:** download latest `backups/*.zip` from Object Storage (same as MVP).
   - Flag **set:** do **not** use Object Storage; if VM1 is **RUNNING**, initiate backup on VM1 and transfer to the admin PC over **SSH** (prefer streaming to avoid needing ~2× world free disk on VM1); if VM1 is not running, tell the admin to Start first.

Thin “set flag + skip upload” may land at contract freeze (MVP Phase 2); full notification + adaptive SSH download is **v1**.

---

## Players tab (after v1)

Associate connecting client IPs with Minecraft **username / UUID** on **VM1** (join/auth or log parse—not the door alone; door only sees pre-auth TCP). Publish associations to Object Storage for Manager.

**UI sketch:** list of players ever seen — username, skin head icon (public skin/UUID APIs; mind rate limits), online/offline; hover actions:

| Action | Behavior |
|--------|----------|
| **Kick** | Online only; RCON kick; prompt for reason shown to player |
| **Mod** | RCON `op <username>` |
| **Ban** | RCON `ban <player> [reason]` (UUID-based); optionally also remove associated IPs from Security List allow rules when in private mode. Use `ban-ip <ip>` only when banning by IP explicitly—**not** `ban-ip <username>` |

Requires RCON (v1 Console era) and careful private-mode Security List semantics.

---

## Start progress checklist (after v1)

When the admin starts the server from Manager (door-aware Start / wake), show a **staged checklist** and check items off as they complete. Goal: make a multi-minute OCI + Minecraft boot feel observable instead of a silent wait.

**Example stages** (exact labels TBD; skip stages that do not apply):

- VM start (OCI instance RUNNING)  
- Door / reserved-IP handoff (when coming from idle doorbell)  
- Minecraft process start (systemd unit up)  
- Load mods / datapacks (when a loader or pack is installed)  
- World ready / accepting players (port listening, or a log line that is reliable)

Add more granular stages only when they can be **observed** (OCI lifecycle, door HTTP status, SSH/systemd, game logs) — do not fake progress. MVP/v1 Start may keep a simpler spinner/status string; this checklist is **after v1**.

---

## IaC & day-2

- **OpenTofu** for greenfield (document as OpenTofu). OCI Resource Manager stack / Terraform exports from the operator’s manual tenancy are **learning references only**.  
- **Mechanism authority:** [`Automated-Infrastructure-Deployment.md`](Automated-Infrastructure-Deployment.md) — local `tofu` on the admin PC; Ubuntu 22.04 platform images; cloud-init = OS baseline only; SSH bootstrap for game/door/agent; hybrid bundled + GitHub-hosted HCL; do not embed Resource Manager or HashiCorp Terraform as the product apply engine.  
- **Naming / tags:** follow [Product resource naming & discovery](#product-resource-naming--discovery).  
- **OCI API / SDK / CLI** for ongoing changes (Security List / IP management, instance actions, Object Storage, etc.). Door continues instance-principal CLI/API for IP moves and start/stop as today.  
- Connect existing should prefer **`meta/infra.json`** over rediscovering every OCID via tags.

---

## Operator pre-coding checklist (manual tenancy)

Before building the Manager app / full product IaC loop:

1. Keep door + VM1 + reserved IP path working (current infra).  
2. Add **Object Storage** layout: meta, ledger, budget, backups (Standard); IAM for VM1, door, admin user.  
3. Prove backup upload + 9.5 GB eviction policy by hand/script. **Done (2026-08-10):** VM1 `world_backup.py` cold SoftStop + live `save-off`/`flush`/`save-on` path; soft-cap eviction of oldest `backups/*.zip`.  
4. Prove ledger write from VM1 + read from door for wake gate.  
5. Note any small infra adjustments forgotten in docs (`Infrastructure-Information.md` + private file).  
6. **Done (operator):** freeform tag `mcmgr-domain=mc-server-compartment` on the live stack compartment (Default) for auto-detect testing.  
7. Cost Estimator preset JSON: **not v1** (paid mode skipped; later / far future). Always Free docs gate in Setup stays.  
8. Operator research (anytime during v1): Vanilla + modded **perf tests**, Always Free shape/hour-envelope confirmation, and guide **mod recommendations** (including Distant Horizons)—see [Pre-v1 release work](#pre-v1-release-work-operator-not-product-features).  
9. **Done:** seed **`meta/infra.json`** (MVP Phase 5 Connect-existing).  
10. After v1 features **and** packaging (V1 plan Phase 9): **clean-room acceptance test** (new account + installer + Setup + $1 brake **including lock UX**) — see [Operator acceptance tests](#operator-acceptance-tests-post-v1-packaging).

**Suggested build order** (non-binding): [`V1-Implementation-Plan.md`](V1-Implementation-Plan.md).

---

## Potential concerns / things to consider later

Parked items—not blocking the staged plan, but should be revisited:

- Public product **support burden** (capacity, free-tier policy changes, ARM + mods).  
- Windows **SmartScreen / code signing** for the installer.  
- Always Free **Micro reclaim** if door is stopped by Oracle—recovery UX.  
- Object Storage **50k API requests/month** if clients poll too chatty — enforce intervals in [`OCI-API-Usage.md`](OCI-API-Usage.md).  
- OCI **429** throttling if Setup/Manager fire unbounded parallel calls — exponential backoff required (Oracle Using the API).  
- Egress vs ~10 TB narrative (backups, mod downloads, public servers).  
- Multi-admin / second PC sharing config safely.  
- Discord/webhook notifications.  
- Legal copy: Mojang EULA, Always Free **~$1–$2 residual** if the last-resort budget fires, no redistributing paid modpacks. (“You may be charged in paid mode” is later / far future copy only.)  
- Backup consistency (stop/save Minecraft before zip). **Mitigated for SoftStop (cold after stop) and live path (`save-off` / `save-all flush` / `save-on`).** Door/Console SoftStop alone still skips backup (OS-ISSUE-6).  
- Single world zip **> ~9.5 GiB** (modded): Object Storage path impossible under Always Free soft cap — see [Oversized world backup (v1)](#oversized-world-backup-v1); SSH transfer time/disk/streaming.  
- Exact default soft cap **1400 vs 1450** OCPU-h.  
- Whether door admin UI `:8080` stays long-term vs Manager-only.  
- Cost Estimator / price APIs accuracy vs static rate cards.  
- Free-tier Ampere limit churn (document in guide).  
- AMD Micro ~50 Mbps vs A1 bandwidth scaling—door is control plane only.
- Auto-detect false positives if other compartments reuse similar names without the `mcmgr-domain` tag (mitigate by requiring `meta/infra.json`).
- **$1 Function latency:** budget fires at $1 actual spend; Function may take minutes → possible **~$1–$2** bill. Disclose in guide/Setup; do not claim a perfect $0 guarantee.
- **$1 Function stop of both VMs:** **live lab deployed image (0.0.11) does this.** Product **v1 tracked source does not** — SoftStop VM1 + lock PUT; door stays up (Always Free AMD Micro ≠ Ampere hours, 2026-08-17). Door honor of the lock is Step 2.3. FN-ISSUE-1 remains for the unreployed live image.

---

## Open questions (non-blocking)

- Exact IAM policy text for product least-privilege (bucket/compartment scoped).  
- Default soft monthly OCPU-h number for MVP.  
- Whether automated cost APIs are good enough to avoid Cost Estimator JSON—or ship JSON if paid mode is ever built (far future, not v1).  
- Bringing prototype door budget code in line with Object Storage SoT (later pass on `MinecraftServerDeploy`).  
- Does Always Free **~1500 Ampere OCPU-h/month** apply to arbitrary A1 Flex sizes on PAYG (e.g. 8/48), or only certain configurations?  
- MVP default VM1 shape: ship **4/24** until Vanilla 2/12 is proven, **and** offer **2/12 vs 4/24 in Setup** ([Setup wizard](#setup-wizard-behavior-detail)). Temporary 3.3 test uses OpenTofu default 2/12 — revert to 4/24 after the test until the picker exists.  
- Exact ledger fields for per-interval shape (`ocpus` / `memory_gb` / shape string)—align with live `vm_agent` ledger if already recording them.  
- Exact oversized-world **flag object key** and clear/reset UX (admin downloaded offline copy? world shrunk?).  
- Optimized Vanilla: ship **Paper only** first, or also a curated Fabric performance preset?  
- Modpack adapters priority order (Modrinth `.mrpack` / CurseForge **Server Files** zip / …). CurseForge **API** client-export import is deferred (ToS / key custody), not rejected.  
- Where pack analysis runs (Manager-only vs helper on VM1) for large archives.  
- Allowlist CIDR: maximum prefix width to accept (reject `/8`? warn but allow `/16`?); whether an admin entry may ever use CIDR for SSH.  
- Wipe world: require Minecraft stopped only, or also refuse if players are online beyond that? (Stopped implies empty.)  
- Imported modpack archive retention: admin-PC profile cache vs VM1 copy vs (later) Object Storage; what to do if every copy is gone.  
- Maintenance mode: rely on unadvertised VM1 ephemeral IP + door MOTD, or also temporarily restrict Security List 25565 to admin IPs?  
- Modpack replace “light swap” heuristic: same loader + same MC version + file-diff under N changes, or manifest identity, or always ask the user after showing a diff?  
- Multi-deploy profile folder names and whether two profiles may share one `~/.oci` config file with different profiles vs requiring separate config files.

**Resolved (2026-08-11):**

- Resource naming / compartment discovery → [Product resource naming & discovery](#product-resource-naming--discovery) (`mcmgr` names; tag `mcmgr-domain=mc-server-compartment`).  
- Connect existing in MVP → button-gated auto-detect + `meta/infra.json`; Phase 1 still uses local config seed.  
- Meta OCID set → [Infra meta contract](#infra-meta-contract-metainfrajson).  
- Bucket/prefix product names → `mcmgr-shared-data` + standard prefixes; lab may differ, meta records live values.  
- Top-bar right chrome / oversized SSH download / Players tab staging → v1 / v1 / after v1 as documented.  
- MVP Vanilla jar install → Mojang **piston-meta** `version_manifest_v2.json` → version metadata → `downloads.server.url` + sha1 + `javaVersion` (not `mojang.com` HTML). User-selectable version in Setup.  
- Paper downloads for Optimized Vanilla research → prefer **Fill v3** (`fill.papermc.io`), not deprecated hand-built `api.papermc.io` v2 URLs.  
- Live lab `$1` Function **image** SoftStops both VM1 and VM2 (captured 2026-08-12, `func.yaml` **0.0.11**). **Product v1 (2026-08-17):** tracked source SoftStops **VM1 only** + PUTs `meta/spend-brake-triggered.json`; door Micro stays up (Always Free AMD Micro is not Ampere OCPU-hours).

---

## Old / superseded ideas

| Idea | Disposition |
|------|-------------|
| HTTP API Gateway wake as primary player wake | Superseded by door Micro + reserved IP |
| Gateway IP as Minecraft server list entry | Invalid protocol mismatch |
| Dual Desired List / OCI / firewalld user concepts | Superseded by Whitelist → Security List |
| No Micro / manual Force Start only | Superseded by door vision |
| Event-driven handback **as primary** | **Rejected**; polling/reconcile remains |
| “Lightweight” as absolute principle | Softened to simple-components guideline |
| Go + Wails as chosen stack | Replaced by **.NET + Avalonia** (historical; Avalonia later replaced as UI vehicle — see next row) |
| Avalonia as Manager UI vehicle | Replaced by **.NET + Blazor Hybrid** (WPF + WebView2) before MVP Phase 7 |
| Two product exes (Setup.exe + Manager.exe) | Replaced by **one installer / one app** |
| itzg Docker as default | Not in MVP/v1 plan; revisit only if needed |
| Docker Desktop / Cloud Shell / `fn deploy` on the user’s PC to install the spend-brake Function | **Rejected for the product path** — CI-built ARM image copied into the user’s OCIR (V1 Step **8.6.1**). Cloud Shell remains lab break-glass. |
| Root compartment default | Replaced by **dedicated compartment** |
| Full day-budget tool in v1 | Deferred to **after v1** |
| Setup wizard “public vs private” choice | **Rejected** — always private; no Manager public toggle |
| Public mode in MVP or v1; blacklist | **Rejected — will not be implemented** (not deferred). Private allowlist only. |
| Paid spend mode in MVP or v1 | **Not v1.** Later / far future (operator 2026-08-18). |
| Treating `MinecraftServerDeploy` README as product SoT | **Rejected** — product tree + living operator-requested plans win; prototype code updated later |
| Silent OCI tenancy probe on every Manager startup | **Rejected** — Connect existing auto-detect is **button-gated** only |
| Players tab / Kick·Op·Ban in MVP or v1 | Deferred to **after v1** |
| Download World Save only in v1 | **Rejected** — Object Storage download is **MVP**; oversized SSH adaptive path is **v1** |
| Hard-coded Minecraft server.jar URL / `Minecraft.Download` S3 | **Rejected** — use piston-meta version manifest + per-version `downloads.server` |
| Modded Setup only “after v1” with no v1 path | Softened — **v1** gains Setup Vanilla/Modded + Optimized Vanilla + pack analyze; deeper day-2 mod UX still later |
| In-app mod / modpack browser (browse, search, trending, download-a-pack, pick-by-name/URL/ID) | **Rejected — will not be implemented** (not deferred). Users create/download the pack themselves, then select the local file in Setup or Manager. |
| CurseForge API client-export import (resolve `projectID`/`fileID` with a product API key) | **Deferred** (ToS / key custody). v1 imports CurseForge **Server Files** / filled zips only, or a Modrinth `.mrpack`. Not rejected. |
| CurseForge **client-export refuse helper** (help panel when analyze refuses jar-less / mixed-ID export: Server Files vs `.mrpack` copy, outbound `curseforge.com/projects/{id}` links from IDs in the zip, optional Modrinth search ≤3 links — **no API key**, not a catalog) | **Maybe later** (operator 2026-08-23). Was Step 8.8 P11; **deferred**, not scheduled. Refuse + Guide copy remains today. |
| Assisted homemade-zip review UI + dependency freeze (unknown-side Keep/Skip, never skip a required dep of a kept jar) | **Done** 2026-08-24 as Step **8.9** ([`V1-Pack-Import-Assisted-Review-Plan.md`](V1-Pack-Import-Assisted-Review-Plan.md)). Spec: [`Pack-Import-Intended-Design.md`](Pack-Import-Intended-Design.md). |

---

## Changelog

| 2026-08-24 | **Step 8.10 scheduled** (density / MOTD / VM1 icon notes): [`V1-Operator-Notes-Follow-On-2-Plan.md`](V1-Operator-Notes-Follow-On-2-Plan.md). Living NEXT = P1. Rich MOTD editor pulled into v1 via that plan (this file’s after-v1 MOTD row may drift). Pass 3 still blocked. |
| 2026-08-24 | **Step 8.9 done** (assisted review UI + dep freeze). Living NEXT then moved to Step **8.10**. Pass 3 **blocked**. |
| 2026-08-23 | **Step 8.9 scheduled** (assisted review + dep freeze): [`V1-Pack-Import-Assisted-Review-Plan.md`](V1-Pack-Import-Assisted-Review-Plan.md). Living NEXT = P1. Pass 3 still blocked. |
| 2026-08-23 | **Pack import design lock** (no code): [`Pack-Import-Intended-Design.md`](Pack-Import-Intended-Design.md). Homemade zip kept; unattended success dropped; assisted review + dep freeze. Later scheduled as Step **8.9**. Pass 3 still blocked. |
| 2026-08-23 | **Step 8.8 closed** without P11. CurseForge refuse **helper panel** (links only, no API) parked as **maybe later** in deferred table. Pass 3 next via `docs/NEXT.md` (blocked until operator). Agent workflow + skills added. |
| 2026-08-21 | **Pass 3 postponed again:** informal Change pack tests + operator notes. Living **NEXT = Step 8.7 / P1**, then Step **8.8**. Layer 3 quarantine and Setup identity/icon variants pulled into v1 via 8.8 (this file’s parked Layer 3 / after-v1 identity headings may drift). Default MOTD names will **not** use Oracle™. Do not start Pass 3, 8.6.1 CI, or 9.1 until the V1 plan says so. |
| 2026-08-20 | **Pass-2 follow-on (v1):** operator notes after Pass 2 closed early. Living **NEXT = Step 8.4 / P1**. Pack replace **full re-setup** pulled into v1 (this file’s [Modpack replace (after v1)](#modpack-replace-after-v1) heading may drift). Danger Zone tab merge and “game computer”→“server” are operator will. Do not start Pass 3, 8.6.1 CI, or 9.1 until the V1 plan says so. |
| 2026-08-20 | **Modpack robustness detour (v1):** itzg exclude lists + mixed archives **before** Pass 2. Living **NEXT = Step 4.13 / R1**. Pass 2 paused. |
| 2026-08-19 | **Spend-brake Function image:** product path is CI-built `linux/arm64` copied into the user’s OCIR (Auth Token only). No Docker Desktop / `fn` / Cloud Shell on the admin PC. Required before official release (V1 Phase **8.6**). Living **NEXT** remains Step **8.5.2**. |
| 2026-08-19 | Pre-packaging QA inserted as V1 Phase 8.5. Living **NEXT = Step 8.5.2**. Do not start 9.1 until QA exits **and** Step **8.6.1** is DONE. TESTING agents may `fn push` / invoke product Functions at $0 (no real $1 alert). |
| 2026-08-18 | **Paid / spend mode removed from v1.** Operator: skip entirely; keep as a later / far-future idea. V1 plan Phase 8 SKIPPED; **NEXT = Step 9.1** (packaging). |
| 2026-08-18 | **CurseForge API client-export import deferred** (ToS / key custody): no product API key in v1. Keep CurseForge **Server Files** zip import. Client exports: Server Files from the pack page, or Modrinth `.mrpack`. Not rejected. V1 **NEXT = Step 5.1**. |
| 2026-08-18 | **Public Minecraft / public-private toggle / blacklist rejected** (will not be implemented): private allowlist only. V1 Step 3.3 cancelled; **NEXT = Step 3.4** removes the 3.1/3.2 code. CIDR allowlist (1.2) stays. |
| 2026-08-18 | In-app mod/modpack browser **rejected** (will not be implemented): users obtain pack files themselves and import via file picker. Not an after-v1 item. |
| 2026-08-18 | Operator-local sample modpacks for v1 pack import: gitignored `data/sample-packs/` + [`Sample-Packs.md`](Sample-Packs.md). Agents ask the operator if a pack type is missing. |
| 2026-08-17 | **V1 Step 2.2:** $1 Function **PUTs** `meta/spend-brake-triggered.json`; **do not SoftStop the door Micro** (Always Free AMD Micro is a separate envelope). Tracked `functions/shutdown_vm/` 0.0.12. No `fn push`. |
| 2026-08-17 | Hybrid manage tab-body scrollbar in the right window gutter (thin overlay-style thumb); tab cards stay chrome-width with or without overflow; min-width remeasures WebView2 so left/right pads stay even. |
| 2026-08-16 | Hybrid accent → cobalt; Usage tab redesigned for scanability; UI help copy says “server” not “game computer”. |
| 2026-08-16 | Hybrid window default/`MinWidth` hugs the status+pins row; extra width centers a fixed-size shell. |
| 2026-08-16 | Operator UI notes: reject light warm-gray; Hybrid twilight-granite + copper theme; equal-height pinned stats aligned to status+power; filled Start/Stop/Restart; Running=green / Stopped=red; no `?` on Status/Play IP/Players; DEBUG probes on Advanced only. |
| 2026-08-15 | Phase B **B13** cutover: Avalonia WinExe removed; only `McManager.Hybrid` remains. MVP **NEXT = Phase 7**. Layout/visual sketches remain **not locked**. |
| 2026-08-15 | Manager UI vehicle: **.NET + Avalonia** replaced by **.NET + Blazor Hybrid** (WPF + WebView2) before MVP Phase 7. Layout/visual sketches remain **not locked**. NuGet/UI packages go on `McManager.Hybrid`, not Avalonia themes. |
| 2026-08-15 | Operator UI SoT: **no mini-terminal**. Novice status = Running/Stopped (Minecraft joinable); door/VM technical status on Advanced; pinned usage hours; custom title bar OK; power buttons must not flash-disable on tab polls. |
| 2026-08-15 | Idle agent must SoftStop VM1 when Minecraft is **not running** (same idle timeout as empty server). Implement in lab `vm_agent/` and redeploy test VM1 (MVP Step 4.1). |
| 2026-08-15 | MVP: in-game Minecraft whitelist off (OCI SL only); Troubleshooting one-shot repairs pulled forward from v1 Door/IP Repair; Setup Deploy lock + log auto-scroll / progress polish. UI sketches are **not locked** — agents use `find-skills` + Pterodactyl-as-reference for UI work. |
| 2026-08-13 | Setup VM1 shape choice: 2 OCPU / 12 GB vs 4 OCPU / 24 GB (distinct from v1 day-2 resize). TEMPORARY: product OpenTofu defaults 2/12 for blank-tenancy 3.3 test — revert to 4/24 after. |
| 2026-08-12 | Budget wiring: live path is Events → Function; unused ONS topic is not a product resource. Events rule name `mcmgr-events-budget-alert`. |
| 2026-08-12 | Product naming table expanded (Function/Events/DGs); 4 lab DGs → 3 product DGs (`mcmgr-dg-instances`, `mcmgr-dg-door`, `mcmgr-dg-fn`). Digest: [`Lab-Reference-Stack-Notes.md`](Lab-Reference-Stack-Notes.md). |
| 2026-08-12 | Live lab `$1` Function confirmed SoftStops **both** VMs (not VM1-only); tracked copy at `functions/shutdown_vm/` (placeholders). Product v1 still open on whether to keep stopping the door Micro. |
| 2026-08-12 | Operator notes: v1 $1 spend-brake lock (Function OS flag, full-window warning, typed confirmation to restart, door-stop vs Micro-always-free open question, IP repair after Function stop); MVP guide/Setup ~$1–$2 residual-charge honesty; post-MVP and post-v1 clean-room acceptance tests; after-v1 Start progress checklist. |
| 2026-08-12 | IaC mechanism authority → [`Automated-Infrastructure-Deployment.md`](Automated-Infrastructure-Deployment.md) (local OpenTofu; RM stack export remains reference-only). |
| 2026-08-11 | **Durable product boundary:** no in-app mod/modpack catalog/browse/search UI, ever — Setup only accepts an already-exported pack file (file picker/drag-and-drop); users build/select packs on Modrinth/CurseForge/FTB's own site or launcher first. Added as a Main principle + Modded-branch hard requirement; full rationale/mechanism in the deep-dive doc's §2.4/§22–§24. |
| 2026-08-11 | OCI API usage guidance (throttling 429, lifecycle waiters, pagination, Always Free request thrift) — see `OCI-API-Usage.md`. |
| 2026-08-11 | MVP Vanilla bootstrap: user-selectable MC version via Mojang piston-meta `version_manifest_v2` → jar URL/sha1/Java; v1 Setup game types (Default/Optimized Vanilla, Paper Fill v3 note, Modded pack upload/analyze/adapters/client-only strip). |
| 2026-08-11 | Connect existing: button-gated auto-detect (multi-profile); compartment name `mcmgr` **or** freeform tag `mcmgr-domain=mc-server-compartment`; product resource naming table; `meta/infra.json` OCID contract; top-bar mini-terminal + copy IP (MVP) vs bell/settings/overflow (v1); Download World Save stays MVP; oversized-world flag + SSH download (v1); Players tab after v1; staging agreements from operator notes. |
| 2026-08-10 | MVP: idle agent forced on at every VM1 boot + rewrite shared config to enabled (Danger Zone disable = testing only). v1: Advanced VM1 shape scaling + always-on-capable small-shape messaging; per-interval ledger shape fields (prefer MVP forward-compat). Pre-v1 operator work: Vanilla/modded perf matrices, Always Free hour-envelope vs Flex size confirmation, guide recommendation against Distant Horizons. |
| 2026-08-10 | World backup MVP live on operator stack (cold SoftStop + live RCON quiesce path); operator checklist backup proof marked done. Suggested development order documented in `V1-Implementation-Plan.md`. |
| 2026-08-08 | Document authority (PRODUCT-IDEAS > MinecraftServerDeploy); door C-only language note; budget ownership clarification; IaC naming freedom; v1 IP Management UX (toggle, whitelist focus, blacklist, Security List research); optional resource tags; tab rename note. |
| 2026-08-07 | Major update: principles (simple components, polling-first); central storage SoT + Standard 9.5 GB backups; single installer/app; Avalonia; OpenTofu; dedicated compartment; MVP / v1 / later staging; cost estimator fallback; paid mode v1; public v1; day-budget tool after v1; operator pre-coding checklist; concerns parking lot. |
| 2026-08-06 | Initial brainstorm; later rewritten around early vision. |
