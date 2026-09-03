using McManager.Core.Setup;

namespace McManager.Core.Services;

/// <summary>
/// SSH prelude for Wipe: write or clear <c>level-seed</c> in guest
/// <c>server.properties</c> without touching other keys.
/// </summary>
public static class WorldSeedPatch
{
    public const string PropertiesPath = "/opt/mcmgr/server/server.properties";

    public static string BuildRemoteScript(string? levelSeed)
    {
        var seed = WorldSeed.Normalize(levelSeed);
        var mode = seed.Length == 0 ? "clear" : "set";
        // Double-quoted python -c so the outer sudo bash -c (single-quoted) is safe.
        // Python strings use only single quotes.
        const string py =
            "import sys;"
            + "path,mode,seed=sys.argv[1:4];"
            + "try:"
            + "\n lines=open(path,encoding='utf-8').read().splitlines()\n"
            + "except FileNotFoundError:\n lines=[]\n"
            + "out=[];seen=False\n"
            + "for line in lines:\n"
            + " raw=line\n"
            + " if (not line.strip()) or line.lstrip().startswith('#') or ('=' not in line):\n"
            + "  out.append(raw);continue\n"
            + " key,_,_val=line.partition('=')\n"
            + " if key.strip()!='level-seed':\n"
            + "  out.append(raw);continue\n"
            + " seen=True\n"
            + " if mode=='set':\n"
            + "  out.append('level-seed='+seed)\n"
            + "if mode=='set' and not seen:\n"
            + " out.append('level-seed='+seed)\n"
            + "open(path,'w',encoding='utf-8',newline='\\n').write(('\\n'.join(out)+('\\n' if out else '')))\n"
            + "print('OK seed='+mode)";

        return
            "set -euo pipefail; "
            + "HOME=\"${HOME:-/home/ubuntu}\"; "
            + "python3 -c \"" + py + "\" "
            + SshShell.Quote(PropertiesPath) + " "
            + SshShell.Quote(mode) + " "
            + SshShell.Quote(seed) + "; "
            + "if [ -f " + SshShell.Quote(PropertiesPath) + " ]; then "
            + "chown mcmgr:mcmgr " + SshShell.Quote(PropertiesPath) + " 2>/dev/null || true; "
            + "chmod 0640 " + SshShell.Quote(PropertiesPath) + " 2>/dev/null || true; fi";
    }
}
