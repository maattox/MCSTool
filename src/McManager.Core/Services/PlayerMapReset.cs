using McManager.Core.Config;
using Renci.SshNet;

namespace McManager.Core.Services;

/// <summary>
/// Drops the game VM tile cache and the doorbell copy (tiles + pins) after a world
/// wipe or replace. Keeps the MinedMap viewer template on the game VM.
/// </summary>
public static class PlayerMapReset
{
    public const string VmMapRoot = "/var/lib/mcmgr-map";
    public const string DoorMapRoot = "/var/lib/mc-player-map";
    public const string DoorStampDir = "/var/lib/mccontrol";

    /// <summary>
    /// Commands for an already-<c>set -e</c> root script. Stops an in-flight render,
    /// then deletes tile output. Does not delete <c>viewer/</c>.
    /// </summary>
    public static string VmCacheClearBody() =>
        "systemctl stop mc-player-map.service >/dev/null 2>&1 || true; "
        + $"MAP={SshShell.Quote(VmMapRoot)}; "
        + "case \"$MAP\" in "
        + $"{VmMapRoot}) ;; "
        + "*) echo refusing map root; exit 2 ;; "
        + "esac; "
        + "sudo rm -rf -- \"$MAP/render\" \"$MAP/publish\" \"$MAP/publish.next\" \"$MAP/shim\" \"$MAP/http\"; "
        + "sudo mkdir -p -- \"$MAP\"; ";

    public static string VmCacheClearScript() =>
        "set -euo pipefail; " + VmCacheClearBody() + "echo MAP_OK";

    public static string DoorClearScript() =>
        "set -euo pipefail; "
        + "export HOME=\"${HOME:-/home/ubuntu}\"; "
        + $"MAP={SshShell.Quote(DoorMapRoot)}; "
        + $"STAMP={SshShell.Quote(DoorStampDir)}; "
        + "case \"$MAP\" in "
        + $"{DoorMapRoot}) ;; "
        + "*) echo refusing map root; exit 2 ;; "
        + "esac; "
        + "sudo rm -rf -- \"$MAP\" \"${MAP}.next\" \"${MAP}.prev\"; "
        + "sudo mkdir -p -- \"$MAP\" \"$STAMP\"; "
        + "sudo tee \"$MAP/index.html\" >/dev/null <<'MCS_MAP_STUB'\n"
        + EmptyMapHtml
        + "\nMCS_MAP_STUB\n"
        + "sudo chmod 0755 -- \"$MAP\"; "
        + "sudo chmod 0644 -- \"$MAP/index.html\"; "
        + "sudo rm -f -- \"$STAMP/player-map-markers.json\" \"$STAMP/player-map.sha256\"; "
        + "echo DOOR_MAP_OK";

    /// <summary>
    /// Null when the door was cleared, or when <paramref name="door"/> is null
    /// (caller did not ask). Otherwise a short sentence for the status line.
    /// </summary>
    public static string? TryClearDoor(DoorSettings? door, string failureLead)
    {
        if (door is null)
            return null;
        if (string.IsNullOrWhiteSpace(door.SshHost))
            return failureLead + " The doorbell VM address is missing.";

        var keyPath = LocalConfigStore.ExpandPath(door.SshKeyPath ?? "");
        if (string.IsNullOrWhiteSpace(keyPath) || !File.Exists(keyPath))
            return failureLead + " The doorbell VM key file is missing.";

        var user = string.IsNullOrWhiteSpace(door.SshUser) ? "ubuntu" : door.SshUser.Trim();
        SshClient? client = null;
        try
        {
            client = new SshClient(door.SshHost.Trim(), user, new PrivateKeyFile(keyPath));
            client.ConnectionInfo.Timeout = TimeSpan.FromSeconds(30);
            client.Connect();
            var cmd = client.CreateCommand("sudo bash -c " + SshShell.Quote(DoorClearScript()));
            cmd.CommandTimeout = TimeSpan.FromMinutes(2);
            var output = cmd.Execute() ?? "";
            if (cmd.ExitStatus != 0)
            {
                var err = string.IsNullOrWhiteSpace(cmd.Error) ? output : cmd.Error;
                var detail = err.Trim();
                return string.IsNullOrEmpty(detail) ? failureLead : failureLead + " " + detail;
            }

            return null;
        }
        catch (Exception ex)
        {
            return failureLead + " " + ex.Message;
        }
        finally
        {
            client?.Dispose();
        }
    }

    /// <summary>Same empty page the doorbell ships before the first tile pull.</summary>
    internal const string EmptyMapHtml =
        """
        <!DOCTYPE html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <meta name="theme-color" content="#161310">
          <title>Map</title>
          <style>
            :root {
              --void: #161310;
              --binding: #2A2218;
              --item: #E8DCC0;
              --quiet: #9A8E78;
              --well: #1A1510;
              --bevel-dark: #0A0907;
              --bevel-light: #5C4E3C;
              --font: ui-sans-serif, system-ui, "Segoe UI", sans-serif;
            }
            *, *::before, *::after { box-sizing: border-box; }
            html { color-scheme: dark; height: 100%; }
            body {
              margin: 0;
              min-height: 100%;
              min-height: 100dvh;
              background: var(--void);
              color: var(--item);
              font-family: var(--font);
            }
            .shell {
              display: grid;
              grid-template-rows: auto minmax(0, 1fr);
              min-height: 100dvh;
            }
            .bar {
              display: grid;
              grid-template-columns: auto minmax(0, 1fr) auto;
              grid-template-areas: "title tabs note";
              align-items: center;
              gap: 10px 16px;
              padding: 8px 14px;
              padding-top: max(8px, env(safe-area-inset-top));
              padding-left: max(14px, env(safe-area-inset-left));
              padding-right: max(14px, env(safe-area-inset-right));
              background: var(--binding);
              border-bottom: 1px solid var(--bevel-dark);
              box-shadow: inset 0 1px 0 #3D3428;
            }
            .brand { grid-area: title; margin: 0; font-size: 15px; font-weight: 600; color: var(--item); }
            .dims { grid-area: tabs; display: flex; gap: 4px; }
            .note { grid-area: note; margin: 0; font-size: 11px; font-weight: 400; color: var(--quiet); text-align: right; }
            .slot {
              display: inline-flex;
              align-items: center;
              justify-content: center;
              padding: 0 14px;
              height: 32px;
              background: var(--well);
              color: var(--quiet);
              font-size: 13px;
              font-weight: 500;
              box-shadow: inset 2px 2px 0 var(--bevel-dark), inset -2px -2px 0 var(--bevel-light);
            }
            .stage { display: grid; place-items: center; padding: 24px 16px; }
            .stage p { margin: 0; max-width: 36rem; font-size: 15px; line-height: 1.45; color: var(--item); text-align: center; }
          </style>
        </head>
        <body>
          <div class="shell">
            <header class="bar">
              <h1 class="brand">Map</h1>
              <nav class="dims" aria-label="Dimension">
                <span class="slot" aria-disabled="true">Overworld</span>
                <span class="slot" aria-disabled="true">Nether</span>
                <span class="slot" aria-disabled="true">End</span>
              </nav>
              <p class="note">Explored terrain only</p>
            </header>
            <main class="stage">
              <p>Map is not ready yet. Explored terrain appears here after the first update.</p>
            </main>
          </div>
        </body>
        </html>
        """;
}
