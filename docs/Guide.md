# Setup guide

This guide explains how to set up a Minecraft server with **MCSTool**. The server runs on Oracle Cloud Infrastructure (OCI) Always Free resources, and you manage it from one desktop app.

- **Windows only.** There is no macOS or Linux version of MCSTool yet.
- For modded servers, you must supply the modpack file. Supported formats are **Modrinth** `.mrpack`, **CurseForge Server Files**, and a **zip of** `.jar` **mods**. For a zip of mods, Setup asks you to confirm the loader, Minecraft version, and Java.
- Always Free can work at **$0**, but Oracle often **doesn't have enough free capacity to create the VMs**. Upgrading the account to **Pay As You Go (PAYG)** makes it more likely the VMs can be created. You still pay $0 if you stay inside Always Free limits.



## What you need

- Windows 10 or 11
- An [Oracle Cloud](https://www.oracle.com/cloud/) account (you will create one in Part 1)
- Minecraft Java Edition



## Cost

The goal is **$0** using Oracle [Always Free](https://docs.oracle.com/en-us/iaas/Content/FreeTier/freetier_topic-Always_Free_Resources.htm#compute) resources.

In many regions, Oracle still requires **Pay As You Go (PAYG)** before it will create the game VM. That is for **eligibility**, not permission to spend. Always Free resources stay free after the upgrade; you are charged only for usage **above** those limits. MCSTool tracks your hours and stops the server automatically so you stay inside those limits.

Setup also adds a **$1 spending limit**. If spending ever reaches $1, MCSTool stops the game VM. It is not instant, so you might still see about **$1–$2 that month**. After that, the game VM stays off until you turn it back on in MCSTool, so there are no further charges.

---



## Part 1 — Create an Oracle Cloud account and upgrade to PAYG



### 1. Create a free account

1. Sign up at [signup.oraclecloud.com](https://signup.oraclecloud.com/).

- For a detailed walkthrough, use Oracle’s [account creation guide](https://docs.oracle.com/en/learn/get-started-with-oci-and-oci-console/index.html#introduction).
- You start with a free account. The next step upgrades it to PAYG.

![Oracle Cloud Free Tier signup form (Account Information)](../assets/guide-images/1.1.png)

### 2. Upgrade to Pay As You Go (PAYG)

1. Open the navigation menu in the top left of the OCI Console.
2. Search for and select **Upgrade and Manage Payment**.

![OCI Console search results for Upgrade and Manage Payment](../assets/guide-images/1.2.png)

3. Under **Pay As You Go**, review your information and click **Upgrade your account**.
4. Review the confirmation and click **Upgrade account**.

- The upgrade can take a day or two. Oracle emails you when it is complete.

> **Credit-card authorization:** when you upgrade to PAYG, your card is authorized for **$100 USD** (or the equivalent in your country). Oracle reverses that authorization immediately on their side. Your bank decides how long the reversal takes to show up.

---



## Part 2 — Create an API key and Auth Token



### 1. Create and download the API key

1. Click the profile icon in the top right of the OCI Console.
2. Select **User settings**.

![OCI Console profile menu with User settings highlighted](../assets/guide-images/2.1.png)

3. Open the **Tokens and keys** tab and click **Add API key**.

![User settings Tokens and keys tab with Add API key highlighted](../assets/guide-images/2.2.png)

4. Select **Generate API key pair**.
5. On your PC, create a folder named `.oci` at `C:\Users\YourUser` (use your Windows user name).
6. In that folder, create a file named `config` with **no file extension**. To do that, enable **File name extensions** in File Explorer, then create and rename a text file and remove `.txt`.

![File Explorer View tab with File name extensions enabled](../assets/guide-images/2.3.png)

7. Download the **private** and **public** keys and move both files into `C:\Users\YourUser\.oci`.

- At this point, the `.oci` folder should contain the `config` file you created and both the public and private key you downloaded.

8. Back in the OCI Console, click **Add**.

![Add API key dialog with Generate API key pair selected and Add highlighted](../assets/guide-images/2.4.png)

9. Copy the configuration file preview.

![Configuration file preview with the Copy button highlighted](../assets/guide-images/2.5.png)

10. Open the `config` file in a text editor and paste the snippet.
11. Set the `key_file` line to the full path of the **private** key in your `.oci` folder. Your file name will be different from the example.

![Notepad config file with key_file path filled in](../assets/guide-images/2.6.png)

12. Save the file.



### 2. Create the Auth Token

1. On the same **Tokens and keys** page, under **Auth tokens**, click **Generate token**.

![Auth tokens section with Generate token highlighted](../assets/guide-images/2.7.png)

2. Enter any description and click **Generate token**.
3. **Copy the token immediately** and save it somewhere safe. You will paste it into the Setup wizard later. Oracle will not show it again.

![Generate token dialog with Copy highlighted](../assets/guide-images/2.8.png)

---



## Part 3 — Run the Setup wizard



### Download and install

1. On the [GitHub repo](https://github.com/maattox/MCSTool), open the latest **MCSTool** release under **Releases**.
2. Download and run the setup `.exe`.

- The installer is not signed. Windows Defender / SmartScreen may show **Windows protected your PC**. Choose **More info** → **Run anyway**.
- If MCSTool says a Microsoft component is missing, install [Evergreen WebView2](https://go.microsoft.com/fwlink/p/?LinkId=2124703), then open the app again.



### Walk through the wizard

3. Open **MCSTool**. Select **Set up a new server**.
4. Work through the pages:

**Step 1 — Always Free**  
Read the notes and check each box: stay on Always Free, the $1 spending limit (and a possible $1–$2 charge), and that Oracle may be out of free capacity.

**Step 2 — Oracle Cloud**  
Select your OCI profile and enter an email. The profile should be detected automatically if you finished Part 2. The email is only used for Oracle's $1 budget alert.

**Step 3 — SSH key**  
Generate a new key, or import an existing one. This is **not** the API key from Part 2. Setup can use one key for both VMs or a different key for the doorbell VM.

**Step 4 — Minecraft**  
Choose **Vanilla (Paper)** or **Modded**. Paper is the vanilla-like server (better multiplayer). Modded needs a modpack you already exported.

- Supported modpack formats: Modrinth `.mrpack`, CurseForge **Server Files**, or a zip of `.jar` mods (confirm the loader, versions, and that client-only mods are marked correctly).
- Supported loaders: Fabric, Forge, NeoForge.
- Large modpacks with heavy mods will lag on this VM. In particular, skip **Distant Horizons** (generating new chunks on this VM size causes significant lag).
- You can switch between Vanilla (Paper) and Modded later from **Server → Settings → Change server type**.
- Optional **World seed**. Leave it blank for a random world.

**Step 5 — Name and icon**  
Set the name, description, and icon players see in the Minecraft server list. You can change these later.

**Step 6 — Minecraft EULA**  
Open and accept the [Minecraft EULA](https://aka.ms/MinecraftEULA).

**Step 7 — Auth Token**  
Paste the Auth Token you saved in Part 2 and click **Store token**. MCSTool keeps **one** token on this PC (Windows Credential Manager). If you later add a second Oracle account, you may need to replace that token during Setup. You won't need it again after Setup.

**Step 8 — Review and deploy**  
Setup detects your public IP and adds it to the whitelist. Pick a VM size and server memory, look over **What Setup will create**, tick the confirmation box, and click **Deploy**.

- Deployment often takes **10–25 minutes**, depending on VM size and modpack. Leave the app open until it finishes.
- If Deploy is interrupted after the game VM already exists, that VM may stay on. Finish Setup, or stop it in the OCI Console (especially on the 4 OCPU / 24 GB size).
- The recommended size (**4 OCPU / 24 GB**) can only run about **11.5 hours a day** on average over a month. The **Usage** tab tracks this for you.
- The smaller size (**2 OCPU / 12 GB**) can usually stay on all month, with less room for mods and players.
- **Server memory** is the memory Minecraft can use, not the VM size:
  - Setup picks a starting value from the server type and modpack. You can change it.
  - Choices are **4G**, **6G**, and **8G**. The **24 GB** VM also offers **10G** and **12G** for heavier modpacks.
  - You can change it later on **Advanced → Danger Zone** (restarts Minecraft).
  - Changing the VM size from **24 GB** to **12 GB** on **Advanced → Danger Zone** lowers server memory above 8G to **8G** (applied when Minecraft next starts).
  - MCSTool can warn when the server is short on memory. It does not add memory by itself.

When you see **Setup complete**, click **Close** to open MCSTool.

---



## Start playing

The Minecraft server should now be up. Copy the **play IP** from MCSTool (Overview or the sidebar) and connect from Minecraft Java Edition.

### Whitelist

Setup adds your public IP to the whitelist for Minecraft **and** the player map. The whitelist works by IP address, not Minecraft username.

To let other players in, add each player’s **current public IPv4** on the **Whitelist** tab (or **Open Whitelist** on Overview) and click **Save changes**. The map page uses the same whitelist. Home IPs can change; update the whitelist when they do.

### Player map

Players can open a **2D map** in a browser. Copy **Player map** from Overview (or **Advanced → Setup**).

- The map uses the doorbell VM's address, **not** the play IP.
- It shows chunks the server has already generated.
- It opens on the Overworld. Switch between Overworld, Nether, and End on the page, plus other explored dimensions when the modpack has them. Extra dimensions show the same explored shape and pins, but their colors may look plain.
- Hover to see block **X** / **Z** next to the cursor. The same numbers stay in the corner.
- Double-click the map (or press and hold on a phone) to add a named pin. Any whitelisted player can rename or delete pins. Click a pin, then click the map or **×** to close it without changes.
- The map and pins still work when the game VM is off.
- Wiping the world clears the map and its pins. The map fills in again as the new world is explored. Replacing the world clears the map the same way.



### Players tab

On the **Players** tab:

- **Online now** shows who is connected (name and face). Start the server to see the list.
- Hover a row for **Kick** (optional reason), **Mod** / **Unmod**, and **Ban** (confirm + optional reason).
- **Banned** lists in-game banned players under **Online now**. Hover a row for **Unban** (`pardon`).
- **Ban** and **Unban** use Minecraft’s in-game ban list only. They do **not** change the **Whitelist**, and neither does an in-game `/ban` from another operator.



### Server tab

On the **Server** tab:

- **Identity** — name, description, and icon in the Minecraft server list.
- **Settings** — difficulty, default game mode, max players, view distance, simulation distance, PvP, spawn protection, hardcore, force game mode, and allow flight. **Save**, then **Restart** (or **Start**) so Minecraft picks up the changes. Below that, **Change server type** switches between Vanilla (Paper) and Modded and reinstalls Minecraft on the game VM. Wiping the world is optional (off by default). Modded needs a modpack file.
- **World** — cloud backups, **Replace world** from a zip, and **Wipe world**. Wipe deletes every dimension (Nether, End, and any others) and clears the player map and its pins. Replace world clears the player map too. Cloud backups are kept.
- **Mods** — drop a new modpack file (any supported format) to change the modpack. At the bottom of this menu, you can add or delete a single mod `.jar`.
- **Plugins** — only for a **Paper** server. List, upload, and delete plugin `.jar` files. Upload and delete **restart Minecraft**. Do **not** use `/reload`.




### Usage tab

Days and hours are counted in **UTC**, not your local time zone. **Hours** shows what you have used. **Calendar** sets how many hours the server can run each day. **Budget settings** holds the monthly allowance, monthly hours limit, idle warnings, and VM size.

- **Rollover** (also in the sidebar) is unused hours from past UTC days. The server can keep running on them after a day's hours run out, or you can put them on later days in **Calendar**.
- The default monthly allowance is about 1,400 CPU-hours and 8,800 memory-hours, which stays under Oracle's free limits. On the 4 OCPU / 24 GB size, that averages about **11.5 hours a day**.



### Idle stop and wake

- The server stops after **15 minutes** with no players. That is how MCSTool stays inside Oracle’s free monthly hours. The doorbell VM stays on and keeps the same play IP.
- When the server is off, start it from MCSTool, or a player can wake it by joining from Minecraft (unless that UTC day is **set to 0 hours** or today’s hours are used up). The doorbell VM then starts the game VM. Waking can take **2–5 minutes**, depending on the modpack and world.
- On the 4 OCPU / 24 GB size, the Minecraft server list shows about how many hours are left today (not CPU-hours). The 2 OCPU / 12 GB size doesn't show that number. Minecraft may cache the server list text; refresh or reconnect if it looks stale.

