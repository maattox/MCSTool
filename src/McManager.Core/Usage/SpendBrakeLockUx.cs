using McManager.Core.Services;

namespace McManager.Core.Usage;

/// <summary>
/// Manager spend-brake overlay rules (v1). Typed confirmation text is frozen from
/// PRODUCT-IDEAS — copy-paste is allowed; partial / fuzzy match is not.
/// </summary>
public static class SpendBrakeLockUx
{
    /// <summary>
    /// Overlay typed confirm parks the play IP, DELETEs the lock, and refreshes the
    /// doorbell OS cache. It does not Start / wake VM1 — the admin uses top-bar Start
    /// after the overlay dismisses.
    /// </summary>
    public const bool OverlayConfirmStartsServer = false;

    /// <summary>
    /// Exact sentence the admin must type before Manager will clear the lock.
    /// Do not rephrase. Clearing the lock does not Start the server.
    /// </summary>
    public const string ConfirmationSentence =
        "I confirm that we have entered a new calendar month and that my free monthly usage limits have been reset. I understand that if I ignore these warnings and turn on my server before a new month has started, the card I created my Oracle Cloud account with will automatically be charged for the excess usage.";

    /// <summary>
    /// Outer trim only (paste often adds a trailing newline). Internal spacing and
    /// case must match <see cref="ConfirmationSentence"/> exactly.
    /// </summary>
    public static bool MatchesConfirmation(string? typed)
    {
        if (typed is null)
            return false;
        return string.Equals(typed.Trim(), ConfirmationSentence, StringComparison.Ordinal);
    }

    /// <summary>
    /// Full-window overlay only when the lock object was observed (present).
    /// Transport errors are not "observed" — those block Start without the overlay.
    /// </summary>
    public static bool ShouldShowOverlay(SpendBrakeLockReadResult? read) =>
        read is { Present: true };

    /// <summary>
    /// Fail closed for chrome Start: lock present, or Get failed / missing value.
    /// Unlocked (404 / not present) does not block.
    /// </summary>
    public static bool BlocksStart(ServiceResult<SpendBrakeLockReadResult>? getResult)
    {
        if (getResult is null || !getResult.Succeeded || getResult.Value is null)
            return true;
        return getResult.Value.Present;
    }
}
