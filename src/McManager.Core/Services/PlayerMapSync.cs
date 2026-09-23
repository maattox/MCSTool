namespace McManager.Core.Services;

/// <summary>
/// VCN tile pull: door fetches a tar from VM1's primary private IP.
/// Not player HTTP (that is <see cref="SecurityListIngressPlanner.PlayerHttpPort"/>).
/// </summary>
public static class PlayerMapSync
{
    /// <summary>VM1 static HTTP for the door pull. Subnet-only; never the play path.</summary>
    public const int TilePort = 8765;

    public const string IngressDescription = "Door player-map tile pull";

    /// <summary>Matches tofu <c>var.subnet_cidr</c> default. SSH repair uses this constant.</summary>
    public const string DefaultSubnetCidr = "10.0.0.0/24";
}
