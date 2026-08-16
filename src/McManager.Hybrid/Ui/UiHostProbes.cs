namespace McManager.Hybrid.Ui;

internal static class UiHostProbes
{
    public const bool Enabled =
#if DEBUG
        true;
#else
        false;
#endif
}
