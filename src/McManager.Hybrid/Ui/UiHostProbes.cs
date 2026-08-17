namespace McManager.Hybrid.Ui;

internal static class UiHostProbes
{
    /// <summary>
    /// Debug-build host checks (confirm / clipboard / native picker). Shown on Advanced only.
    /// </summary>
    public const bool Enabled =
#if DEBUG
        true;
#else
        false;
#endif
}
