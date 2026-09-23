# MCSTool

Create a Vanilla (Paper) or modded Minecraft server on Oracle Cloud Always Free.

### Setup guide: [docs/Guide.md](docs/Guide.md)

- Always Free can work at **$0**, but Oracle often **doesn't have enough free capacity to create the VMs**. Upgrading the account to **Pay As You Go (PAYG)** makes it more likely the VMs can be created. You still pay $0 if you stay inside Always Free limits.
- For modded servers, supported modpack formats are **Modrinth** `.mrpack`, **CurseForge Server Files**, and a **zip of** `.jar` **mods**. For a zip of mods, Setup asks you to confirm the loader, Minecraft version, and Java.
- At the recommended VM size (4 OCPU / 24 GB), the server can run for about **11.5 hours a day** on average over a month. MCSTool tracks the hours and stops the server automatically so you stay inside Oracle's monthly free limits.

**1.1.2** — download it from [Releases](https://github.com/maattox/MCSTool/releases).

![MCSTool](assets/sample-image.png)

## What you get

- A free Vanilla (Paper) or modded Minecraft server
- One app to create the server and manage it afterward
- Players always join the same address
- When nobody is playing, the game VM sleeps. A small always-on doorbell VM still answers Minecraft and can wake it
- Only players whose IP address is on the whitelist can connect
- Start and stop, the player list, usage, and world backups — all in the app

Windows only. There is no Mac or Linux app yet.

## Cost

The goal is **$0** using [Oracle Always Free](https://docs.oracle.com/en-us/iaas/Content/FreeTier/freetier_topic-Always_Free_Resources.htm#compute).

If a charge ever appears, the **$1 spending limit** stops the game VM. It is not instant, so you might still see about **$1–$2 that month**. The [guide](docs/Guide.md) explains this in full.

## Get started

1. You need **Windows 10 or 11**, an [Oracle Cloud](https://cloud.oracle.com) account, and **Minecraft Java Edition**.
2. Download **MCSTool-Setup-1.1.2.exe** from [Releases](https://github.com/maattox/MCSTool/releases).
3. Windows may say the publisher is unknown. That is expected for this installer. Choose **More info** → **Run anyway** only if you downloaded the file from this project’s Releases.
4. Open **MCSTool** and follow Setup.

Step-by-step: [docs/Guide.md](docs/Guide.md).

## License

[MIT](LICENSE)
