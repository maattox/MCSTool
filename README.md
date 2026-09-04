# MCSTool

Create a vanilla or modded Minecraft server using OCI Always Free resources.

### Setup guide: [docs/Guide.md](docs/Guide.md)

- Always Free *can* work at **$0**, but Oracle **capacity often blocks creating the VMs**. Upgrading the account to **Pay As You Go (PAYG)** raises scheduling priority. You can still stay at $0 if you stay inside Always Free limits.
- For modded servers, supported modpack formats are **Modrinth** `.mrpack`, **CurseForge Server Files**, and a **zip of** `.jar` **mods**. For a zip of jars, Setup asks you to confirm the loader, Minecraft version, and Java.
- At the recommended VM size (4 OCPUs), the server is only able to be running for about 12 hours a day over the course of a month. This is to stay within Oracle's Always Free resource limits. There are systems in place to automatically manage your server's uptime and resource usage to ensure you do not exceed Oracle's monthly free resources.

**1.1.0** — download it from [Releases](https://github.com/maattox/MCSTool/releases).

![MCSTool](assets/sample-image.png)

## What you get

- Easily create a modded, Paper, or vanilla Minecraft server for free
- One app to create the server and manage it afterward
- Players always join the same address
- When nobody is playing, the game server sleeps. A small always-on “doorbell” still answers Minecraft and can wake the server
- Only players whose IP you add in the app can connect
- Start and stop, the player list, usage, and world backups — all in the app

Windows only. There is no Mac or Linux app yet.

## Cost

The goal is **$0** using [Oracle Always Free](https://docs.oracle.com/en-us/iaas/Content/FreeTier/freetier_topic-Always_Free_Resources.htm#compute), and there are systems in place to automatically manage the server's uptime to ensure your account is not charged.

Oracle often requires a **Pay As You Go** account so the server can be created. That is for eligibility to create a VM, and does not require spending any money. If something ever goes wrong and a charge appears, a last-resort **$1 monthly limit** stops the game server. You might still see about **$1–$2 that month**. The [guide](docs/Guide.md) explains this in full.

## Get started

1. You need **Windows 10 or 11**, an [Oracle Cloud](https://cloud.oracle.com) account, and **Minecraft Java Edition**.
2. Download **MCSTool-Setup-1.1.0.exe** from [Releases](https://github.com/maattox/MCSTool/releases).
3. Windows may say the publisher is unknown. That is expected for this installer. Choose **More info** → **Run anyway** only if you downloaded the file from this project’s Releases.
4. Open **MCSTool** and follow Setup.

Step-by-step: [docs/Guide.md](docs/Guide.md).

## License

[MIT](LICENSE)