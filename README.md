# MC Manager

### Setup guide: [docs/Guide.md](docs/Guide.md)

A Windows app for a **private Minecraft server** (modded or vanilla) for you and your friends, hosted on [Oracle Cloud](https://www.oracle.com/cloud/). Built to run on Oracle’s Always Free resources.

**Open beta 0.9.1** — download it from [Releases](https://github.com/maattox/oci-mc-server/releases).

![MC Manager](assets/sample-image.png)

## What you get

- One app to create the server and manage it afterward
- Vanilla or modded server
- Players always join the same address
- When nobody is playing, the game server sleeps. A small always-on “doorbell” still answers Minecraft and can wake the server
- Only players whose IP you add in the manager can connect
- Start and stop, the friend list, usage, and world backups — all in the app

Windows only. There is no Mac or Linux app yet.

## Cost

The goal is **$0** using [Oracle Always Free](https://docs.oracle.com/en-us/iaas/Content/FreeTier/freetier_topic-Always_Free_Resources.htm#compute), and there are systems in place to automatically manage the server's uptime to ensure your account is not charged.

Oracle often requires a **Pay As You Go** account so the server can be created. That is for eligibility to create a VM, and does not require spending any money. If something ever goes wrong and a charge appears, a last-resort **$1 monthly limit** stops the game server. You might still see about **$1–$2 that month**. The [guide](docs/Guide.md) explains this in full.

## Get started

1. You need **Windows 10 or 11**, an [Oracle Cloud](https://cloud.oracle.com) account, and **Minecraft Java Edition**.
2. Download **MCManager-Setup-0.9.1.exe** from [Releases](https://github.com/maattox/oci-mc-server/releases).
3. Windows may say the publisher is unknown. That is expected for this beta. Choose **More info** → **Run anyway** only if you downloaded the file from this project’s Releases.
4. Open **MC Manager** and follow Setup. The app will tell you if a Microsoft component is missing.

Step-by-step: [docs/Guide.md](docs/Guide.md).

## License

[MIT](LICENSE)