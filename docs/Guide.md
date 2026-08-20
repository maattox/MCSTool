# Happy-path guide

This is the short path for a **Windows** admin who wants a **private** Minecraft server for friends, hosted on **Oracle Cloud Infrastructure (OCI)** Always Free resources, managed from one desktop app.

Friends always connect to the same **play IP**. When nobody is playing, a small always-on “doorbell” answers Minecraft pings and can wake the server. Idle and budget stops are meant to keep you inside Always Free. Access is an **IP allowlist** — only addresses you add can join. This is not a public server.

**Windows only** for this Manager. There is no macOS or Linux Manager in MVP.

---

## Cost: built for $0, not a hard guarantee

This product is built to stay at **$0** using Oracle [Always Free resources](https://docs.oracle.com/en-us/iaas/Content/FreeTier/freetier_topic-Always_Free_Resources.htm#compute). There is **no paid mode** in the app.

Oracle still requires a **Pay As You Go (PAYG)** account for Ampere A1 capacity in many regions. PAYG means you *can* be billed if you leave Always Free. It is **not** permission to spend. Oracle’s docs say Always Free resources remain free after you upgrade; you are charged only for usage **above** those limits.

**Last-resort $1 brake:** Setup creates a **$1 monthly compartment budget**. If actual spend ever reaches $1, an Oracle Function **SoftStops the Minecraft computer** and writes a lock flag in Object Storage. The small always-on doorbell stays running (it is an Always Free AMD Micro and does not use Ampere hours). That Function is not instant. Oracle bills when spend hits $1, and the Function can take several minutes, so you may see a **~$1–$2** charge **for that month**, then **no further charges** while the brake holds. This is **not** a perfect $0 guarantee.

If that brake fires, friends who ping the play IP see a **MONTHLY SPEND BRAKE FIRED** message (not the daily budget one). Wait for the next calendar month, then open Manager. The app fills the window with a warning; Start stays blocked until you type the exact confirmation sentence (copy-paste is allowed). Confirming starts the doorbell if needed, parks the play IP, clears the lock, then tries a normal Start — idle and daily/monthly free-hour limits still apply. The lock is not cleared automatically at month rollover. Use **Troubleshooting** if the play IP is left on the wrong computer.

Do **not** add paid shapes, extra volumes, or load balancers. Setup never opens Minecraft to the whole internet. There is no public-server toggle.

---

## What you need

| Item | Notes |
|------|--------|
| Windows 10/11 PC | Manager is a desktop WinExe (WebView2). |
| [Evergreen WebView2](https://go.microsoft.com/fwlink/p/?LinkId=2124703) | The app tells you if this is missing. |
| Oracle Cloud account | PAYG as needed (see below). Prefer the **home region**. |
| API key files | `%USERPROFILE%\.oci\config` + PEM (not an SSH key). |
| Auth Token | **Needed** to install the $1 spend-brake Function image (Oracle Container Registry login). **Not** Docker Desktop — Setup copies a pre-built ARM image. Until [V1 Step 8.6.1](V1-Implementation-Plan.md#step-861--ci-built-arm-image--setup-copy-into-ocir) ships, from-source Setup may still skip the Function if Docker is missing. |
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

You do **not** install Docker Desktop, the `fn` CLI, or Oracle Cloud Shell to finish Setup. The product builds the ARM Function image in CI and Setup **copies** it into your tenancy. (Oracle Cloud Shell was how the lab prototype was first built; that is operator break-glass, not the user path.)

Official reference: [Getting an Auth Token](https://docs.oracle.com/en-us/iaas/Content/Registry/Tasks/registrygettingauthtoken.htm).

1. Profile menu → **User settings** / **My profile**.
2. **Tokens and keys** → **Auth Tokens** → **Generate token**.
3. Description example: `mc manager OCIR`.
4. **Copy the token immediately** — Oracle will not show it again.
5. You will paste it into Setup. You can skip Setup’s token page and finish later, but the last-resort Function is **not** installed until a token is stored and Deploy can copy the image into OCIR. Skipping Docker is expected; skipping the token is what leaves the brake uninstalled.

Each user may have at most **two** Auth Tokens. If you lose it, generate a new one.

**Until V1 Step 8.6.1 ships:** from-source Manager still tries to **build** the image with Docker on this PC and skips if Docker is not running. That is a gap, not the intended product. After 8.6.1, only the Auth Token is required for the copy.

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

Walk the wizard. You can close and resume later from **Advanced → Deploy / repair** (progress is saved locally; secrets are not).

| Step | What to do |
|------|------------|
| Always Free | Open the Always Free docs link. Check all three boxes: stay on Always Free–eligible compute, understand the **$1 brake and possible ~$1–$2 residual**, understand capacity wait. |
| OCI profile | Pick the profile from `%USERPROFILE%\.oci\config`. Confirm tenancy and **home region**. |
| Compartment | Default: create compartment named **`mcmgr`**. Do not point a first deploy at a compartment that already has unrelated resources. |
| Alert email | Where Oracle should email the $1 budget alert. |
| SSH | **Generate a new key** (recommended). This is **not** the API key. The private key stays on disk; Setup does not put it in the resume file. |
| Game | **Vanilla** or **Modded**. Vanilla: **Default Vanilla** (official Mojang) or **Optimized Vanilla** (Paper), then pick a **release**. Snapshots are Advanced and apply only to Default Vanilla. Paper’s list hides versions Paper does not build. Paper is a faster server, not a Forge/Fabric modpack. **Modded:** choose a local **`.mrpack` or server-pack zip** (file picker or drag-and-drop). Setup analyzes it and shows name, Minecraft version, loader, Java, and file counts. Confirm two checkboxes, including that you will give friends the **same exported pack**. There is no pack search box. Prefer a **Modrinth `.mrpack`**, or a CurseForge **Server Files** zip (the jars are already inside). CurseForge *client* exports (manifest IDs, no jars) are refused — download Server Files from that pack’s CurseForge page instead. Quilt packs are detected but not installable yet. Details: [Modded: friends need the client pack](#modded-friends-need-the-client-pack). |
| EULA | Open and accept the [Minecraft EULA](https://aka.ms/MinecraftEULA). Setup will not auto-accept it. |
| Auth Token | Paste the token and **Store token**. Needed to install the $1 spend-brake Function (copy a pre-built image into your registry). Skip only if you accept that the Function may not install this run. You do **not** need Docker Desktop. |
| Summary | Confirm **your public IPv4** as `x.x.x.x/32`. Pick the game computer size (**4 OCPU / 24 GB** recommended, or **2 OCPU / 12 GB**). Read the plan. Check the create-resources box. Click **Deploy**. |

**After Deploy starts:** Back and Deploy stay locked. Do not start a second Deploy. Resume / Re-Deploy is a separate Advanced action.

**If the log times out waiting for `/etc/mcmgr/cloud-init-done` with `Last: WAIT`:** cloud-init likely already finished; `ubuntu` cannot see that file (`0750`). Rebuild Manager and resume from **Advanced → Deploy / repair** (skips `tofu apply` if apply already succeeded). Do not wait longer, reboot, or chmod `/etc/mcmgr`.

**If Ampere A1 is out of capacity:** a window offers try again now, auto-retry every 5 minutes while the app stays open, or close and resume later. That wait does not spam Oracle’s API.

Deploy creates the compartment, network, reserved play IP, game VM, doorbell VM, shared storage, IAM, and (when the Auth Token is present) the $1 budget Function, then installs the chosen Default Vanilla, Paper, or Modded (loader + server-side mods) server on the game VM. It can take a while. Leave the app open until the log shows success.

The Function image is a **pre-built ARM** copy into your OCIR, not a Docker build on this PC (V1 Step 8.6.1). Until that step ships, from-source Deploy may log that the Function was skipped if Docker is missing.

---

## Modded: friends need the client pack

A **Modded** server is **not playable** until friends install the **same exported pack file** you chose in Setup. Vanilla Minecraft (and a different pack, or a different version of the same pack) cannot join.

- Keep that file. Manager also saves a copy on this PC; **Server Management → Download pack** copies that original archive (not a zip of server mods).
- Tell friends the pack **name**, **Minecraft version**, and **loader** (Fabric / Forge / NeoForge) shown in Setup, and give them the original export.
- This app **cannot** rebuild a client pack from the `mods/` folder on the game computer. Setup installs **server-side** mods only and skips client-only files, so a zip of the live server mods is **not** a playable pack for friends.

Some packs mark client-only mods as required on the server. Setup skips those known names automatically, shows a warning with examples, and still lets you continue. If the game later fails to start, check that skipped list first.

Next is not available in Setup until you check that you will give friends this same pack. The same reminder appears on the Review page before Deploy.

**CurseForge files:** if the zip is a *client* export (a `manifest.json` of project/file IDs and no mod jars), Setup will refuse it. On that pack’s CurseForge page, download **Server Files** (jars already inside) and import that zip — or use a Modrinth `.mrpack` when the pack exists there. This app does not call the CurseForge API and does not reconstruct missing jars.

---

## 5. Allow friends, then play

1. Open the **Whitelist** tab. Add each friend’s **current public IPv4** (name optional). Check **Admin** only for people who should also reach SSH / doorbell admin from that IP.
2. Click **Save changes** so the cloud firewall actually updates. Join is gated by this list, **not** Minecraft’s in-game whitelist (Setup leaves that off).

If a friend’s home address keeps changing but a **prefix** stays stable (for example they are always `172.56.x.x`), open **Add IP** → **Advanced** and enter a CIDR such as `172.56.0.0/16` instead of a single address. That prefix is written on the Minecraft (25565) rules only. SSH / doorbell admin stay a single `/32` unless you are editing **your own** admin row. Prefixes `/0`–`/8` are rejected as too wide; anything wider than one host shows a warning. IPv4 only.

The server is **private**. Join is allowlist-only: each friend needs an entry you Save. There is no public mode and no blacklist.
3. Copy the **Play IP** from the top bar. Give friends that address and the Minecraft version you chose. Port is the default Minecraft port (`25565`). **Modded:** also give them the **same exported pack file** from Setup — they cannot join with vanilla Minecraft. See [Modded: friends need the client pack](#modded-friends-need-the-client-pack).
4. Click **Start**. Status **Running** means the game itself is joinable (Modded friends still need the pack installed first). **Stopped** means they should wait or click Start again — first wake can take several minutes.
5. Friends add a server in Minecraft Java using the play IP. Modded friends must launch the matching pack (same loader and pack file), not a vanilla profile.

When everyone is done, click **Stop** (doorbell-aware). If you forget, idle timeout (default **15 minutes** with nobody online, or if Minecraft is not running) SoftStops the game VM. Daily/monthly budgets can also refuse wake with a clear Minecraft kick/MOTD when the day’s hours are exhausted.

**Your home IP changed?** Whitelist → detect or paste the new public IP → update the admin row.

---

## Day-to-day in Manager

| Want | Where |
|------|--------|
| See if friends can join | Top bar **Status** (`Running` / `Stopped`) |
| Copy the address | Top bar **Play IP** |
| Wake / park the server | **Start** / **Stop** (not raw Compute on Advanced) |
| Restart Minecraft only | **Restart** (game VM must already be up) |
| Hours vs budget | Pinned usage cards + **Usage** tab |
| World zip download / replace / wipe | **Server Management** (Object Storage; ~9.5 GB backup soft cap; SSH live copy if the world is too large) |
| Inspect mods / re-download imported pack | **Server Management** → **Modding** (original Setup file on this PC; not a zip of server mods) |
| Name, icon, description, idle chat lines | **Server Management** → **Name, icon, and messages** (plain text; Restart Minecraft to apply) |
| Send Minecraft commands / view logs | **Console** (not a live terminal) |
| Stuck play IP / doorbell | **Troubleshooting** (confirm-gated one-shots) |
| Technical VM / doorbell state | **Advanced** |
| Turn idle timer off / change game computer size / delete the stack | **Danger Zone** |
| Program settings / About / notifications | Top-right **bell**, **gear**, and **menu** (native Windows title bar stays) |

**Wipe world** on **Server Management** deletes only the live save on the game VM. Cloud backups, mods, and `server.properties` are not deleted. Download a world save first if you might want the current world back. The game VM must be running; Minecraft is stopped for the wipe and **started again** so a new world generates.

*(Pass 1 catalog recorded leave-stopped; operator 2026-08-19 overrode — bug-fix **P8**. Lab PRODUCT-IDEAS Wipe world step 4 may still say the next Start creates a world.)*

**World too large for cloud backup:** If a single world zip is bigger than the ~9.5 GB free cloud cap, automatic cloud backups stop. The top-right **bell** warns you. **Download latest world save** then copies the **live** world from the game VM over SSH (the VM must be Running). That file stays on this PC and is **not** uploaded to cloud storage. Older cloud backups in the list can still be downloaded.

**Modding** on the same tab: Vanilla and Paper show a short “not a modded server” note. On a Modded stack you can inspect the server-side files in `mods/` (game VM must be running) and **Download pack** — that copies the **original imported archive** saved on this PC, not a zip of the live server mods (that zip would not work for friends). If the original file is missing from this PC, Manager cannot rebuild a client pack.

**Name, icon, and messages** on Server Management: set the name and description friends see in their Minecraft server list (plain text, two lines), pick a **64×64 PNG** icon, and optionally edit the automated chat lines used before an idle or budget stop. Save writes the shared copy. **Restart** Minecraft (or **Start**) applies it. The doorbell message while the game computer is off is not edited here.

**Console** sends Minecraft commands (the same ones you would type in the server console) and shows recent logs from the game computer. Start the server first. A leading `/` is optional. This is not a live terminal, and the RCON port stays local on the game computer — it is not opened on the cloud firewall.

**Advanced vs Danger Zone:** Advanced is power-user tools (technical status, Deploy/repair, break-glass VM power, idle **timeout**, stack identity). **Danger Zone** is a separate tab for turning the idle timer **off** (testing only — boot / Minecraft start turns it back on), **changing the game computer size** (2 OCPU / 12 GB or 4 OCPU / 24 GB), and **Delete infrastructure**. Troubleshooting stays its own tab.

Do not disable the idle timer except for a short test. Booting the game VM turns it back on.

**Change game computer size** on Danger Zone is disabled until the game computer is **Stopped** (use top-bar **Stop** so Minecraft is down too). It updates Oracle A1 Flex OCPU/memory, then local config and shared budget/meta so usage math matches. Past usage rows keep the size they were recorded at. A larger size uses Always Free hours faster (less wall-clock left this month); a smaller size does the reverse. Sizes above 4 OCPU / 24 GB are not offered.

**Smaller size (2 OCPU / 12 GB):** hours are still counted, but Manager and the doorbell MOTD use calmer copy because this size can usually stay on all month inside Always Free. The 4 OCPU / 24 GB size still shows remaining-hours and “cap” language — those hours run out faster. Daily-budget-exhausted and spend-brake messages are the same on both sizes.

The top-right **bell** opens a notification list (empty until something posts; each item can be dismissed). The **gear** opens program settings for this PC: where stack config and OpenTofu files live, plus a **Check for updates** toggle (saved now; GitHub Releases checks start in a later release). The **menu** has **About** and a GitHub link. Tabs and Start / Stop are unchanged.

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

**Start** / **Stop** on the top bar are this doorbell-aware path. Advanced **Raw VM Start/Stop** do **not** move the play IP — friends will not follow that.

If the IP is stuck on the wrong VM after a Function stop or a failed wake, use **Troubleshooting → park reserved play IP** (if the game VM is running, park on it; otherwise start the doorbell if needed and park there).

---

## Appendix C — Object Storage

Setup creates a shared Standard-tier bucket (product name `mcmgr-shared-data`) used as the source of truth for:

- Usage ledger and budget config
- Stack identity (`meta/infra.json`) so another PC can connect
- World backup zips (`backups/world-*.zip`)

Always Free Object Storage on a paid/PAYG tenancy is small (**10 GB** Standard, **50,000 API requests/month** in Oracle’s current notes). The product keeps backups under about **9.5 GB** and avoids chatty refresh loops.

Manager updates to **budget config**, **stack identity** (`meta/infra.json`), and the shared **IP allowlist** use a conditional write (ETag). If the game VM or another Manager copy changed the object first, Save fails with a refresh-and-retry message instead of silently overwriting. `ip/mode.json` is not written.

A single world zip larger than the soft cap is **not** uploaded (an on-box flag is set). Manager then offers an SSH live-world download from Server Management when the game computer is up.

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

1. In Manager, open **Danger Zone**.
2. Click **Delete infrastructure**.
3. Read the warning. Type **`confirm`** (lowercase) to enable Delete. This does **not** close your Oracle account.
4. Keep the window open. The log and percent stay until Oracle finishes deleting (often several minutes). Close is disabled until it succeeds or fails.
5. After success: close Manager fully, reopen it, then run Setup.

Only resources this Manager deployed (OpenTofu state on **this PC**) are removed. Oracle default tenancy resources stay. The friends list on this PC, API key, and SSH keys stay.

**Usage hours vs a fresh Setup:** Delete also wipes the cloud bucket — world backups and the usage history Manager uses. A new Setup starts that history at **zero**. Oracle’s Always Free **OCPU-hours for this calendar month** were already used by the old computers while they were on; they do **not** reset when you delete. Until the next month, Manager’s leftover hours can look too high. If you delete and redeploy mid-month, trust Oracle’s monthly Always Free clock, not the new stack’s Usage tab.

If Delete says there is no OpenTofu state, this PC did not deploy the stack (or the `%LOCALAPPDATA%\McManager\tofu` folder is missing). Do not delete random compartments in the Console unless you know they are the product `mcmgr` stack.

Developer/operator SSH command dump (not required for the happy path): lab `docs/Operator-Troubleshooting.md` in the sibling tooling repo.

---

## Out of this guide (not MVP)

Public game access, paid/spend mode, and macOS/Linux Manager are **not** in this product. Paid mode is a far-future idea, not v1. The **$1 spend-brake lock** (full-window warning) **is** in this Manager. Setup offers Vanilla (Default or Paper) and **Modded** pack import (local file only).

**In-app modpack browse/download is rejected** and will not ship later either. Users obtain a pack file themselves (Modrinth `.mrpack`, CurseForge **Server Files** zip, etc.) and import it with the file picker or drag-and-drop. CurseForge client-export API import is not in this path.
