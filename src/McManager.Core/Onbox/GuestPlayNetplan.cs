using System.Net;

namespace McManager.Core.Onbox;

/// <summary>
/// Guest secondary play-IP netplan (Setup + Troubleshooting). Writes
/// <c>/etc/netplan/99-mcmgr-play.yaml</c> for the default interface.
/// </summary>
public static class GuestPlayNetplan
{
    public static string BuildApplyScript(string secondaryPrivateIp)
    {
        if (string.IsNullOrWhiteSpace(secondaryPrivateIp)
            || !IPAddress.TryParse(secondaryPrivateIp, out _))
        {
            throw new ArgumentException(
                "Secondary private IP is missing or invalid.",
                nameof(secondaryPrivateIp));
        }

        return
            "set -euo pipefail\n"
            + $"IP={secondaryPrivateIp}\n"
            + "IFACE=$(ip -o -4 route show default | awk '{print $5}' | head -1)\n"
            + "if [ -z \"$IFACE\" ]; then echo 'ERROR: no default interface' >&2; exit 1; fi\n"
            + "umask 077\n"
            + "cat > /etc/netplan/99-mcmgr-play.yaml <<EOF\n"
            + "network:\n"
            + "  version: 2\n"
            + "  ethernets:\n"
            + "    ${IFACE}:\n"
            + "      addresses:\n"
            + "        - ${IP}/24\n"
            + "EOF\n"
            + "chmod 600 /etc/netplan/99-mcmgr-play.yaml\n"
            + "netplan apply\n"
            + "echo \"play netplan: $IFACE $IP/24\"\n"
            + "ip -4 addr show dev \"$IFACE\" | grep -F \"$IP\" || true\n";
    }
}
