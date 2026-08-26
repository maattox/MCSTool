# Happy-path guide

This is the short path for a **Windows** admin who wants a **private** Minecraft server for friends, hosted on **Oracle Cloud Infrastructure (OCI)** Always Free resources, managed from one desktop app.

Friends always connect to the same **play IP**. When nobody is playing, a small always-on “doorbell” answers Minecraft pings and can wake the server. Idle and budget stops are meant to keep you inside Always Free. Access is an **IP allowlist** — only addresses you add can join. This is not a public server.

**Windows only** for this Manager. There is no macOS or Linux Manager in MVP.

---

## Cost: built for $0, not a hard guarantee

This product is built to stay at **$0** using Oracle [Always Free resources](https://docs.oracle.com/en-us/iaas/Content/FreeTier/freetier_topic-Always_Free_Resources.htm#compute). There is **no paid mode** in the app.

Oracle still requires a **Pay As You Go (PAYG)** account for Ampere A1 capacity in many regions. PAYG means you *can* be billed if you leave Always Free. It is **not** permission to spend. Oracle’s docs say Always Free resources remain free after you upgrade; you are charged only for usage **above** those limits.

**Last-resort $1 brake:** Setup creates a **$1 monthly compartment budget**. If actual spend ever reaches $1, an Oracle Function **SoftStops the Minecraft computer** and writes a lock flag in Object Storage. The small always-on doorbell stays running (it is an Always Free AMD Micro and does not use Ampere hours). That Function is not instant. Oracle bills when spend hits $1, and the Function can take several minutes, so you may see a **~$1–$2** charge **for that month**, then **no further charges** while the brake holds. This is **not** a perfect $0 guarantee.

If that brake fires, friends who ping the play IP see a **MONTHLY SPEND BRAKE FIRED** message (not the daily budget one). Wait for the next calendar month, then open Manager. The app fills the window with a warning; Start stays blocked until you type the exact confirmation sentence (copy-paste is allowed). Confirming **clears the lock** (and recovers the doorbell / play IP) but does **not** start the server — use **Start** in the left sidebar when you are ready. Idle and daily/monthly free-hour limits still apply. The lock is not cleared automatically at month rollover. Use **Troubleshooting** if the play IP is left on the wrong computer.

Do **not** add paid shapes, extra volumes, or load balancers. Setup never opens Minecraft to the whole internet. There is no public-server toggle.

---

## What you need

| Item | Notes |
|------|--------|
| Windows 10/11 PC | Manager is a desktop WinExe (WebView2). |
| [Evergreen WebView2](https://go.microsoft.com/fwlink/p/?LinkId=2124703) | The app tells you if this is missing. |
| Oracle Cloud account | PAYG as needed (see below). Prefer the **home region**. |
| API key files | `%USERPROFILE%\.oci\config` + PEM (not an SSH key). |
| Auth Token | **Needed** to install the $1 spend-brake Function image (Oracle Container Registry login). **Not** Docker Desktop when a pre-built ARM tarball is present (`artifacts/mcmgr-fn-softstop-linux-arm64.tar` next to the app or in the repo). Without that artifact, from-source Setup still builds with Docker. CI / installer bundling remains [V1 Step 8.6.1](V1-Implementation-Plan.md#step-861--ci-built-arm-image--setup-copy-into-ocir). |
| Public IPv4 | Yours, and each friend’s, for the allowlist. Home IPs change. |
| Minecraft Java Edition | Same release Setup chooses: Vanilla/Paper picker, or the version declared in a Modded pack. **Modded:** friends also need **that same exported pack file** — vanilla Minecraft cannot join. See [Modded: friends need the client pack](#modded-friends-need-the-client-pack). |

Until a Windows installer ships (MVP packaging step), run Manager from this repo — see [Install the Manager](#3-install-the-manager).

---

## 1. Create the Oracle Cloud account (PAYG as needed)

1. Sign up at [cloud.oracle.com](https://cloud.oracle.com).
2. Complete identity / payment verification Oracle asks for.
3. If the account is **Free Tier only** and Ampere A1 will not create, **upgrade to Pay As You Go**. That is for **capacity eligibility**, not so you can spend.
4. Stay in the tenancy **home region**. Always Free Ampere A1 and the tiny AMD Micro doorbell are home-region entitlements.

Confirm current Always Free compute limits yourself:

[Always Free compute resources](https://docs.oracle.com/en-us/iaas/Content/FreeTier/freetier_topic-Always_Free_Resources.htm#compute)

Oracle can change the Ampere envelope. Setup will ask you to confirm you understand the limits and the $1 residual before Deploy.

---

## 2. Put an API key and Auth Token on this PC

Manager talks to Oracle with an **API signing key**. That is **not** the SSH key Setup generates later, and **not** the Auth Token.

Official reference: [Required Keys and OCIDs](https://docs.oracle.com/en-us/iaas/Content/API/Concepts/apisigningkey.htm).

### API signing key + `%USERPROFILE%\.oci\config`

1. Create the folder if it does not exist: `%USERPROFILE%\.oci\`  
   Example: `C:\Users\you\.oci\`
2. In the Console, open the **Profile** menu (top right) → **User settings** (or **My profile**).
3. Open **Tokens and keys** / **API Keys** → **Add API Key**.
4. Prefer **Generate API key pair**. Download the **private** key into `%USERPROFILE%\.oci\` (for example `oci_api_key.pem`). Move it there if the browser saved it in Downloads.
5. Click **Add**. Copy the **Configuration File Preview** snippet.
6. Create or edit `%USERPROFILE%\.oci\config` (no `.txt` extension). Paste the snippet. Set `key_file` to the real private-key path, for example:

```ini
[DEFAULT]
user=ocid1.user.oc1..<your-user>
fingerprint=12:34:56:78:90:ab:cd:ef:12:34:56:78:90:ab:cd:ef
key_file=C:\Users\you\.oci\oci_api_key.pem
tenancy=ocid1.tenancy.oc1..<your-tenancy>
region=us-sanjose-1
```

Use **your** home region, not a copy-paste from this example. `region` should match the region currently selected in the Console when the snippet was generated.

7. Restrict the PEM so only your Windows user can read it. Do not commit it, email it, or put it in chat.

If Oracle returns **401 NotAuthenticated** with a valid key, check that this PC’s clock is within **5 minutes** of real time.

### Auth Token (for the $1 Function image)

The Auth Token is a **separate** secret used to put the spend-brake Function image into **your** Oracle Container Registry. It is **not** stored in `%USERPROFILE%\.oci\config`. Setup can keep it in **Windows Credential Manager** (`McManager/ocir`).

You do **not** install Docker Desktop, the `fn` CLI, or Oracle Cloud Shell to finish Setup **when a pre-built ARM Function image is present**. Setup copies that tarball into **your** Oracle Container Registry. Look next to the app, or in repo `artifacts/mcmgr-fn-softstop-linux-arm64.tar` (gitignored; not committed). You can point at a file with `MCMANAGER_FUNCTION_IMAGE_TAR`. (Oracle Cloud Shell was how the lab prototype was first built; that is operator break-glass, not the user path.)

Official reference: [Getting an Auth Token](https://docs.oracle.com/en-us/iaas/Content/Registry/Tasks/registrygettingauthtoken.htm).

1. Profile menu → **User settings** / **My profile**.
2. **Tokens and keys** → **Auth Tokens** → **Generate token**.
3. Description example: `mc manager OCIR`.
4. **Copy the token immediately** — Oracle will not show it again.
5. You will paste it into Setup. You can skip Setup’s token page and finish later, but the last-resort Function is **not** installed until a token is stored and Deploy can copy the image into OCIR. Skipping Docker is expected; skipping the token is what leaves the brake uninstalled.

Each user may have at most **two** Auth Tokens. If you lose it, generate a new one.

**If a pre-built ARM image is present:** Docker is not required; Setup copies it. **If it is missing** (typical from-source checkout without `artifacts/`): Setup still tries to **build** with Docker on this PC and skips if Docker is not running. [V1 Step 8.6.1](V1-Implementation-Plan.md#step-861--ci-built-arm-image--setup-copy-into-ocir) is the CI / installer path that always ships the artifact. Auth Token is required for the copy either way. `MCMANAGER_OCIR_USERNAME` (`<namespace>/<username>`) is still required until 8.6.1 derives it.

---

## 3. Install the Manager

**When a Windows installer is available:** install it, then open **MC Manager**. That is the intended path (one app; Setup is inside it).

**Until the installer ships**, from a checkout of this repo (requires [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)):

```powershell
dotnet restore src\McManager.slnx
dotnet build src\McManager.slnx
dotnet run --project src\McManager.Hybrid
```

Or open `src\McManager.slnx` in Visual Studio and run **McManager.Hybrid**.

---

## 4. Setup → Deploy

On first launch with no local manage config, choose **Deploy a new stack (Setup)**.

(If you already deployed from another PC, use **Find an existing stack** instead — see [Connect an existing stack](#connect-an-existing-stack).)

Walk the wizard. Pages are short; hover the **info (i)** next to a label for the extra detail. You can close and resume later from **Advanced → Deploy / repair** (progress is saved locally; secrets are not).

| Step | What to do |
|------|------------|
| Always Free | Read the short Always Free summary (eligible shapes, $0 target, $1 brake, possible ~$1–$2 residual). Open the docs link if you want Oracle’s page. Check all three boxes. Extra detail is on the **info (i)** hover next to each box. |
| Oracle Cloud | Pick the profile from `%USERPROFILE%\.oci\config`. Confirm tenancy and **home region**. Enter the email Oracle should use for the $1 budget alert. Setup creates a compartment named **`mcmgr`**, or **`mcmgr-2`** / **`mcmgr-3`** if that name is already used. There is no Compartment page; Advanced **Auto-detect** is how you attach to an existing stack. |
| SSH | **Generate a new key** (recommended). This is **not** the API key. The private key stays on disk; Setup does not put it in the resume file. |
| Game | **Vanilla** or **Modded** is the main choice on the left. Beside it: Vanilla flavor and version, or the Modded pack drop with Choose / Clear (visible together without scrolling). Vanilla: **Default Vanilla** (official Mojang) or **Optimized Vanilla** (Paper), then pick a **release**. Snapshots are Advanced and apply only to Default Vanilla. Paper’s list hides versions Paper does not build. Paper is a faster server, not a Forge/Fabric modpack. **Modded:** choose a local **`.mrpack` or server-pack zip** (file picker or drag-and-drop). Prefer a **Modrinth `.mrpack`** or CurseForge **Server Files** (jars already inside) — those usually continue after the two friend-pack checkboxes. A homemade zip is the fallback and may open a three-group review (Will skip / Needs your call / Must keep). Default is **Keep**; optional **Skip on server**. There is no pack search box. CurseForge *client* exports (manifest IDs, no jars) are refused. Quilt packs are detected but not installable yet. After a pack is analyzed, review lists may scroll. Details: [Modded: friends need the client pack](#modded-friends-need-the-client-pack). |
| Name and icon | Friends see this in Minecraft’s server list. Defaults are **Vanilla Server**, **Paper Server**, or **Modded Server**, plus `made with github.com/maattox/oci-mc-server`. Changing Vanilla/Paper/Modded updates the default name until you edit it. Format the list MOTD in the **name and description**: highlight text and use the toolbar (the typing boxes show the look, not `§` codes). Click the same color or effect again to remove it. Counters under the Minecraft-font preview (`line 1: 41/59`) warn when a list line is too long. Check **Don’t put the server name on the MOTD** when the description already has both list lines. **Raw motd= string** is always visible: paste a generator string there (or type `§` codes) to fill the preview, or **Copy** to take the value written to the game VM. Hex/gradient colors need Paper or Spigot 1.16+ (Vanilla / Forge / Fabric ignore them). Icon is an optional **PNG** (Manager fits it to 64×64). Skipping it uses the product default. Offline / starting / unavailable copies show on the doorbell while the server is off. You can change all of this later on the **Server** tab. |
| EULA | Open and accept the [Minecraft EULA](https://aka.ms/MinecraftEULA). Setup will not auto-accept it. |
| Auth Token | Paste the token and **Store token**. Needed to install the $1 spend-brake Function (copy a pre-built image into your registry when the ARM tarball is present). Skip only if you accept that the Function may not install this run. You do **not** need Docker Desktop if that artifact exists. |
| Summary | Confirm **your public IPv4** as `x.x.x.x/32`. Pick the server size (**4 OCPU / 24 GB** recommended, or **2 OCPU / 12 GB**). Read the plan. Check the create-resources box. Click **Deploy**. |

**After Deploy starts:** percent, elapsed time, and a short English status stay in the **bottom bar** with Back and Deploy locked — they stay reachable if you scroll. The status is never a raw SSH command (those stay in the **Deploy log**, which grows to fill the page while Deploy runs). The plan summary is collapsed under **Plan summary**. Do not start a second Deploy. Resume / Re-Deploy is a separate Advanced action.

**If the log times out waiting for `/etc/mcmgr/cloud-init-done` with `Last: WAIT`:** cloud-init likely already finished; `ubuntu` cannot see that file (`0750`). Rebuild Manager and resume from **Advanced → Deploy / repair** (skips `tofu apply` if apply already succeeded). Do not wait longer, reboot, or chmod `/etc/mcmgr`.

**If Ampere A1 is out of capacity:** a window offers try again now, auto-retry every 5 minutes while the app stays open, or close and resume later. That wait does not spam Oracle’s API.

Deploy creates the compartment, network, reserved play IP, game VM, doorbell VM, shared storage, IAM, and (when the Auth Token is present) the $1 budget Function, then installs the chosen Default Vanilla, Paper, or Modded (loader + server-side mods) server on the game VM. It can take a while. Leave the app open until the log shows success.

When Deploy **succeeds**, Setup shows **Deployment Complete** and the **reserved play IP** friends should use, with a **Copy** button. Click **Close** (footer) to continue to the Manager app. The deploy log stays on that page, collapsed under **Deploy log**. Reopening a finished Setup from **Advanced → Deploy / repair** shows the same complete page — do not click Deploy again.

The Function image is copied into your OCIR from a **pre-built ARM** tarball when present (next to the app or gitignored `artifacts/mcmgr-fn-softstop-linux-arm64.tar`). Docker is not required for that copy. Without the artifact, from-source Deploy may log that the Function was skipped if Docker is missing. CI / installer bundling is V1 Step 8.6.1.

---

## Modded: friends need the client pack

A **Modded** server is **not playable** until friends install the **same exported pack file** you chose in Setup. Vanilla Minecraft (and a different pack, or a different version of the same pack) cannot join.

- Keep that file. Manager also saves a copy on this PC; **Server → Modding → Download pack** copies that original archive (not a zip of server mods).
- Tell friends the pack **name**, **Minecraft version**, and **loader** (Fabric / Forge / NeoForge) shown in Setup, and give them the original export.
- This app **cannot** rebuild a client pack from the `mods/` folder on the server. Setup installs **server-side** mods only and skips client-only files, so a zip of the live server mods is **not** a playable pack for friends.

Some packs mark client-only mods as required on the server. Setup skips those known names automatically (including Fabric loading-screen and GUI-loader **classes**, not only Sodium/Iris), shows a warning with examples, and still lets you continue. Leftover Fabric jars that declare themselves client-only in `fabric.mod.json` (or have only client entrypoints) are also skipped. If the game later fails to start, check that skipped list first. If Setup or **Change pack** fails because Minecraft crashed while starting, the error includes a short server log (and the loader’s blamed mod when it printed one). When the loader names **exactly one** mod, Manager moves that jar to `mods.quarantined` (never deletes it) and retries once. You then choose **Keep excluded** (skip it on future installs of this same pack file) or **Put back** on **Server → Modding**. Several blamed mods, or no loader report, stay a normal crash with the log — nothing is stripped automatically. A slow first world gen still waits for RCON; that is not the same as a crash-loop. **Change pack** installs the **Required Java** major from pack analyze before starting Minecraft (for example Java 25 for Minecraft 26.x); if Temurin for that major cannot be installed, Setup stops with a clear message instead of an RCON timeout.

User-made server zips, jar-root archives, and leftover unknowns on a Server Files zip go through **assisted review** when jars still have no client/server metadata after automatic skips. **Will skip** lists automatic skips and why. **Needs your call** defaults to **Keep**; you may mark **Skip on server** (saved for that same file on this PC). **Must keep** is a required dependency of a jar you are keeping — it is locked. If you force-skip a required dep, Next / Install this pack stays disabled until you unmark Skip. We skip obvious client mods; everything else stays unless you mark it. If the server crashes and the game names one mod, you can exclude it here (or after a crash via Layer 3 Keep excluded, once). Homemade zips and jar-root archives also let you **correct** detected Minecraft version, loader, loader version, and Java from version lists before install; Manager then saves a **confirmed copy** (with manifest) for **Download pack** — still not a zip of live server `mods/`. A long unknown list shows a search box. A Modrinth **`.mrpack`** with unclear `env.server` still **cannot** continue — fix the pack or pick a different export. Novices should prefer `.mrpack` or CurseForge **Server Files** so they can skip the review.

Next is not available in Setup until you check that you will give friends this same pack. The same reminder appears on the Review page before Deploy.

**CurseForge files:** if the zip is a *client* export (a `manifest.json` of project/file IDs and no mod jars), Setup will refuse it. On that pack’s CurseForge page, download **Server Files** (jars already inside) and import that zip — or use a Modrinth `.mrpack` when the pack exists there. This app does not call the CurseForge API and does not reconstruct missing jars.

---

## 5. Allow friends, then play

1. Open the **Whitelist** tab. Add each friend’s **current public IPv4** (name optional). Check **Admin** only for people who should also reach SSH / doorbell admin from that IP.
2. Click **Save changes** so the cloud firewall actually updates. Join is gated by this list, **not** Minecraft’s in-game whitelist (Setup leaves that off).

If a friend’s home address keeps changing but a **prefix** stays stable (for example they are always `172.56.x.x`), open **Add IP** → **Advanced** and enter a CIDR such as `172.56.0.0/16` instead of a single address. That prefix is written on the Minecraft (25565) rules only. SSH / doorbell admin stay a single `/32` unless you are editing **your own** admin row. Prefixes `/0`–`/8` are rejected as too wide; anything wider than one host shows a warning. IPv4 only.

The server is **private**. Join is allowlist-only: each friend needs an entry you Save. There is no public mode and no blacklist.
3. Copy the **Play IP** from the left sidebar. Give friends that address and the Minecraft version you chose. Port is the default Minecraft port (`25565`). **Modded:** also give them the **same exported pack file** from Setup — they cannot join with vanilla Minecraft. See [Modded: friends need the client pack](#modded-friends-need-the-client-pack).
4. Click **Start** (enabled only after the Minecraft VM is fully **Stopped** — wait if it is still shutting down). Status **Running** means the game itself is joinable (Modded friends still need the pack installed first). **Stopped** means they should wait or click Start again — first wake can take several minutes. **Players** in the sidebar is `0` while Stopped and the live count while Running.
5. Friends add a server in Minecraft Java using the play IP. Modded friends must launch the matching pack (same loader and pack file), not a vanilla profile.

When everyone is done, click **Stop** (doorbell-aware). If you forget, idle timeout (default **15 minutes** with nobody online, or if Minecraft is not running) SoftStops the game VM. Daily/monthly budgets can also refuse wake with a clear Minecraft kick/MOTD when the day’s hours are exhausted.

**Your home IP changed?** Whitelist → detect or paste the new public IP → update the admin row.

---

## Day-to-day in Manager

| Want | Where |
|------|--------|
| See if friends can join | Left sidebar **Status** (`Running` / `Stopped`) |
| How many are online | Left sidebar **Players** (`0` when Stopped; `X / Y` while Running) |
| Copy the address | Left sidebar **Play IP** (copy icon) |
| Wake / park the server | One **Start** / **Stop** button in the left sidebar (Start when the server is off, Stop when it is on; not raw Compute on Advanced) |
| Restart Minecraft only | **Restart** beside that button (game VM must already be up) |
| Hours vs budget | Three stacked pin strips in the left sidebar (today’s uptime, this month %, rollover bank) + **Usage** (**Hours** still has daily average, hours left, and idle timeout; expand **Detailed usage** for hours by UTC day; **Budget** to edit allowances) |
| World zip download / replace / wipe | **Server → World** (Object Storage; ~9.5 GB backup soft cap; SSH live copy if the world is too large) |
| Inspect mods / re-download imported pack | **Server → Modding** (mod list starts collapsed; original Setup file on this PC; not a zip of server mods) |
| Reinstall from a new pack | **Server → Change pack** |
| Name, icon, description, idle chat lines | **Server → Identity** (formatted list MOTD; Restart Minecraft to apply) |
| Send Minecraft commands / view logs | **Console** (not a live terminal) |
| Stuck play IP / doorbell | **Troubleshooting** (confirm-gated one-shots) |
| Technical VM / doorbell state | **Advanced** |
| Turn idle timer off / idle timeout / change server size / delete the stack | **Advanced → Danger** |
| Program settings / About / notifications | Top-right **bell** and **gear** (same bar as min / max / close). **About** is a sidebar tab |

The left sidebar holds **Status**, **Play IP**, **Players**, one **Start** / **Stop** button plus **Restart**, and **three** stacked pin strips: today’s uptime (hours used vs the daily slice), this month (percent used), and rollover bank. They refresh from the same hours budget as the **Usage** tab (no extra fetch). Daily average, hours left, and idle timeout stay on **Usage** (and Overview) — hours left is the month’s remaining cap, not the rollover bank. Idle timeout is the configured empty-server stop (edit it on **Usage → Budget** or **Advanced → Danger**). The large pane on the right is the current tab. **Overview** (the home tab) is a read-only snapshot: live status / play IP / players, the list MOTD and pack line, usage (including rollover and idle timeout), and the whitelist with **name and IP** per friend. **Manage IPs**, **Open Usage**, and **Open Server** switch tabs — they do not edit from Overview. **About** shows the app name, version, a short private-server sentence, and **Source on GitHub**.

Manage reads as three panels: a dark left band (status, power, and three stacked pin strips), a lighter tab list that uses the leftover sidebar height with larger buttons, and the work pane on the right. Ctrl+scroll does not zoom the UI. The sidebar is narrow and flush to the left edge; the work pane takes the rest of the window.

Each Manager tab **remembers its own scroll position** when you switch away and back. A tab you have not opened yet starts at the top. The sidebar list is **Overview**, **Whitelist**, **Server**, **Console**, **Usage**, **Advanced**, **Troubleshooting**, **About**. Manager opens on **Overview**. **Server** uses inner tabs (**Identity**, **World**, **Modding**, **Change pack**) so the active pane fits the window; the server-side mod list starts collapsed.

**Wipe world** on **Server → World** deletes only the live save on the game VM. Cloud backups, mods, and `server.properties` are not deleted. Download a world save first if you might want the current world back. The game VM must be running; Minecraft is stopped for the wipe and **started again** so a new world generates. If the server is off, that warning (and other button results) shows in a compact toast at the **lower-left of the content pane**, above the Change-pack bar if it is open — read it, then **X** to dismiss. Short successes (including Start) fade after a few seconds; errors and in-progress toasts stay until **X**. Setup keeps its footer status.

*(Pass 1 catalog recorded leave-stopped; operator 2026-08-19 overrode — bug-fix **P8**. [`PRODUCT-IDEAS.md`](PRODUCT-IDEAS.md) Wipe world step 4 may still say the next Start creates a world.)*

**World too large for cloud backup:** If a single world zip is bigger than the ~9.5 GB free cloud cap, automatic cloud backups stop. The top-right **bell** warns you. **Download latest world save** then copies the **live** world from the game VM over SSH (the VM must be Running). That file stays on this PC and is **not** uploaded to cloud storage. Older cloud backups in the list can still be downloaded.

**Modding** on **Server**: Vanilla and Paper show a short “not a modded server” note. **Change pack** is its own inner tab (it reinstalls Minecraft from a new `.mrpack` or server-pack zip). The game VM must be **Running**. Confirm the same two Setup checkboxes (use this pack; friends get the same file). Homemade zips with unknown-side jars show the same three-group review as Setup. **Install this pack** and **Cancel** stay in the **bottom bar** (with elapsed time while it runs) so they stay reachable if you scroll the summary. The world is **kept** unless you also check wipe. Friends need the new exported pack on their PCs — Manager cannot rebuild a client pack from server `mods/`. On a Modded stack you can inspect the server-side files in `mods/` (the list starts **collapsed**) and **Download pack** — that copies the **original imported archive** saved on this PC, not a zip of the live server mods. If the original file is missing from this PC, download is disabled. A crash while starting shows a short log in the error, not only an RCON timeout. If the loader blamed exactly one mod, that jar is set aside and listed here so you can **Keep excluded** or **Put back**.

**Identity** on the **Server** tab: set the name and description friends see in their Minecraft server list. Highlight text in either field and use the toolbar to apply a color or effect (you do not type `§` codes in those boxes). Click the same color or effect again to remove it. Counters under the Minecraft-font preview warn when a list line is over 59 characters. Paste a generator `motd=` string (or type codes) in **Raw motd= string** — the preview updates as you edit. Check **Don’t put the server name on the MOTD** when the description already has both list lines. Hex/gradient colors need Paper or Spigot 1.16+; Vanilla, Forge, and Fabric ignore them. **Copy** takes the value written to the game VM. Pick a **PNG** icon (fitted to 64×64; default if you skip), and optionally edit the automated chat lines used before an idle or budget stop. Save writes the shared copy and updates the doorbell list icons (offline / starting / unavailable). **Restart** Minecraft (or **Start**) applies the in-game name, MOTD, and **color** icon — the game VM installs that PNG in the Minecraft server folder as `mcmgr` (the doorbell keeps greyscale variants for when the game VM is down).

**Console** sends Minecraft commands (the same ones you would type in the server console) and shows recent logs from the server. **Simple** (default) hides RCON plumbing, modloader startup, and mixin debug noise so chat, joins, world-prep progress, and errors stay readable; **Full** shows the unfiltered service log. Start the server first. A leading `/` is optional. This is not a live terminal, and the RCON port stays local on the server — it is not opened on the cloud firewall. If a crash set a mod aside, use **Server → Modding** to keep it excluded or put it back.

**Advanced** uses inner tabs: **Status** (VM/door lifecycle and break-glass Compute), **Stack** (deploy/repair and stack identity), and **Danger** (idle timeout, turning the idle timer **off**, **changing the server size**, and **Delete infrastructure**). Troubleshooting stays its own tab.

Do not disable the idle timer except for a short test. Booting the game VM turns it back on.

**Change server size** on **Advanced → Danger** is disabled until the server is **Stopped** (use sidebar **Stop** so Minecraft is down too). It updates Oracle A1 Flex OCPU/memory, then local config and shared budget/meta so usage math matches. Past usage rows keep the size they were recorded at. A larger size uses Always Free hours faster (less wall-clock left this month); a smaller size does the reverse. Sizes above 4 OCPU / 24 GB are not offered.

**Smaller size (2 OCPU / 12 GB):** hours are still counted, but Manager and the doorbell MOTD use calmer copy because this size can usually stay on all month inside Always Free. The 4 OCPU / 24 GB size still shows remaining-hours and “cap” language — those hours run out faster. Daily-budget-exhausted and spend-brake messages are the same on both sizes.

The top-right **bell** opens a notification list (empty until something posts; each item can be dismissed). The **gear** opens program settings for this PC: where stack config and OpenTofu files live, plus a **Check for updates** toggle (saved now; GitHub Releases checks start in a later release). **About** (sidebar tab) has the app name, version, and a GitHub link. Bell and gear share the caption strip with min / max / close — there is no extra ☰ menu. The strip is a step darker than the rest of the window so it reads as chrome, not as more page background, and it spans the full window if you resize. Drag the empty left side of that bar to move the window. The window opens wide (~1280 CSS pixels) and can shrink to about 920. The sidebar stays a fixed width; the right pane grows and shrinks. **Console** keeps the command row at the bottom of the pane and lets the log fill leftover height. **Change pack** Install / Cancel stay in the bottom bar of that pane so they stay reachable while you scroll.

---

## Appendix A — SSH

Setup’s SSH key is how Manager (and you, if needed) log into the Ubuntu VMs as `ubuntu`. It lives under `%USERPROFILE%\.ssh\` when generated (name like `mcmgr_ed25519_yyyyMMdd_HHmmss`).

- **API key** (`%USERPROFILE%\.oci\*.pem`) = Oracle **control plane** (create VMs, firewall, storage).
- **SSH key** = **inside** the VMs (install, restart Minecraft, repairs).
- Never commit either. Never open SSH to `0.0.0.0/0`.

Most admins never need a terminal. Prefer Manager **Troubleshooting** buttons over ad-hoc SSH. If you do SSH, many on-box files are root-owned (`Permission denied` as `ubuntu` is common) — use `sudo` or fix ownership; do not chmod the world to 777, and do not run Minecraft as `ubuntu`.

---

## Appendix B — Door (doorbell)

Two computers share **one reserved public play IP**:

- **Idle:** the tiny always-on doorbell VM holds the play IP. Minecraft pings get a message (MOTD). A connect from an allowlisted IP can **wake** the game VM and move the play IP there.
- **Playable:** the Ampere game VM holds the play IP and runs Vanilla.
- **Stop / idle timeout:** the game VM SoftStops (world backup on that path) and the IP returns to the doorbell.

Wake reads the shared budget first. If the daily budget is exhausted, wake is refused with a clear kick/MOTD. The doorbell also reconciles “who should hold the IP” after crashes or a $1 Function stop.

**Start** / **Stop** in the left sidebar are this doorbell-aware path. Advanced **Raw VM Start/Stop** do **not** move the play IP — friends will not follow that.

If the IP is stuck on the wrong VM after a Function stop or a failed wake, use **Troubleshooting → park reserved play IP** (if the game VM is running, park on it; otherwise start the doorbell if needed and park there).

---

## Appendix C — Object Storage

Setup creates a shared Standard-tier bucket (product name `mcmgr-shared-data`) used as the source of truth for:

- Usage ledger and budget config
- Stack identity (`meta/infra.json`) so another PC can connect
- World backup zips (`backups/world-*.zip`)

Always Free Object Storage on a paid/PAYG tenancy is small (**10 GB** Standard, **50,000 API requests/month** in Oracle’s current notes). The product keeps backups under about **9.5 GB** and avoids chatty refresh loops.

Manager updates to **budget config**, **stack identity** (`meta/infra.json`), and the shared **IP allowlist** use a conditional write (ETag). If the game VM or another Manager copy changed the object first, Save fails with a refresh-and-retry message instead of silently overwriting. `ip/mode.json` is not written.

A single world zip larger than the soft cap is **not** uploaded (an on-box flag is set). Manager then offers an SSH live-world download from the **Server** tab when the server is up.

Do not put SSH private keys, API keys, Auth Tokens, or RCON passwords in the bucket.

[Deleting infrastructure](#tear-down-and-redeploy-greenfield-e2e) removes this bucket. A later Setup seeds a new empty usage ledger; Oracle’s Always Free hours for the current month are not reset.

---

## Connect an existing stack

On a **new PC** (or after reinstall):

1. Repeat [API key](#api-signing-key--userprofileociconfig) on that PC (same tenancy).
2. Open Manager with **no** local config → **Find an existing stack** (or Advanced **Auto-detect infrastructure**). The app does **not** scan Oracle on every launch.
3. Confirm the summary (region, compartment, play IP, infra schema / stack version). Multiple matches get a chooser.
4. If this Manager is **older** than the stack (`infra_schema` or document version newer than the app), Connect **refuses** — update Manager. If the stack is older, or `stack_version` differs, you get an extra warning; Connect still does not change the cloud stack.
5. Point at the SSH **private** key when asked. RCON stays local-only.

“I already have a stack” skips the scan — only use that if you already placed `config.local.json` by hand.

---

## If something is stuck

Try **Troubleshooting** in Manager first (each action asks for confirm and shows a pasteable log):

- Park reserved play IP
- Diagnose / reset doorbell state
- Start the doorbell VM (after a $1 Function stop)
- Force-refresh doorbell budget cache
- Repair game-tree permissions (CHDIR / Minecraft will not start)
- Re-apply guest play-IP network config

Guest ACPI SoftStop hang is **not** a silent button — use Oracle Console reset if the copy on that tab says so.

## Tear down and redeploy (greenfield E2E)

To wipe the **product stack** on a test tenancy and run Setup again:

1. In Manager, open **Advanced → Danger**.
2. Click **Delete infrastructure**.
3. Read the warning. Type **`confirm`** (lowercase) to enable Delete. This does **not** close your Oracle account.
4. Keep the window open. The log and percent stay until Oracle finishes deleting (often several minutes). Close is disabled until it succeeds or fails.
5. After success: close Manager fully, reopen it, then run Setup.

Only resources this Manager deployed (OpenTofu state on **this PC**) are removed. Oracle default tenancy resources stay. The friends list on this PC, API key, and SSH keys stay.

**Usage hours vs a fresh Setup:** Delete also wipes the cloud bucket — world backups and the usage history Manager uses. A new Setup starts that history at **zero**. Oracle’s Always Free **OCPU-hours for this calendar month** were already used by the old computers while they were on; they do **not** reset when you delete. Until the next month, Manager’s leftover hours can look too high. If you delete and redeploy mid-month, trust Oracle’s monthly Always Free clock, not the new stack’s Usage tab.

If Delete says there is no OpenTofu state, this PC did not deploy the stack (or the `%LOCALAPPDATA%\McManager\tofu` folder is missing). Do not delete random compartments in the Console unless you know they are the product `mcmgr` stack.

Developer/operator SSH command dump (not required for the happy path): [`Operator-Troubleshooting.md`](Operator-Troubleshooting.md).

---

## Out of this guide (not MVP)

Public game access, paid/spend mode, and macOS/Linux Manager are **not** in this product. Paid mode is a far-future idea, not v1. The **$1 spend-brake lock** (full-window warning) **is** in this Manager. Setup offers Vanilla (Default or Paper) and **Modded** pack import (local file only).

**In-app modpack browse/download is rejected** and will not ship later either. Users obtain a pack file themselves (Modrinth `.mrpack`, CurseForge **Server Files** zip, etc.) and import it with the file picker or drag-and-drop. CurseForge client-export API import is not in this path.
