using McManager.Core.Config;

namespace McManager.Core.Setup;

/// <summary>
/// Environment exports for <c>onbox/mcmgr/common/driver.sh</c> (Setup bootstrap and Change pack).
/// </summary>
internal static class OnboxDriverExports
{
    /// <summary>
    /// Shell <c>export …</c> prefix for driver.sh. Includes <c>JAVA_MAJOR</c> when known from
    /// pack analyze or the Minecraft version floor table.
    /// </summary>
    public static string Build(SetupWizardState state, int? analyzedJavaMajor = null)
    {
        ArgumentNullException.ThrowIfNull(state);

        var minecraftVersion = state.MinecraftVersion.Trim();
        var dist = SetupPackImport.ToDistribution(state);
        var loaderPin = SetupPackImport.LoaderPin(state.PackLoader, state.PackLoaderVersion);
        var pinExport = loaderPin is { } pin
            ? $" {pin.Name}={ShQuote(pin.Value)}"
            : "";

        int? javaMajor = state.PackJavaMajor ?? analyzedJavaMajor;
        if (javaMajor is null && MinecraftJavaFloor.TryGet(minecraftVersion, out var mapped))
            javaMajor = mapped;

        var javaExport = javaMajor is { } j
            ? $" JAVA_MAJOR={j.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
            : "";

        return
            $"export EULA_ACCEPTED=true MINECRAFT_VERSION={ShQuote(minecraftVersion)} "
            + $"DISTRIBUTION={ShQuote(dist)}{pinExport}{javaExport} "
            + "HOME=\"${HOME:-/home/ubuntu}\"";
    }

    private static string ShQuote(string value) => "'" + value.Replace("'", "'\\''") + "'";
}
