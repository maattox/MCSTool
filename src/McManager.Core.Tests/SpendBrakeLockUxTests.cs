using McManager.Core.Services;
using McManager.Core.Usage;
using Xunit;

namespace McManager.Core.Tests;

public sealed class SpendBrakeLockUxTests
{
    [Fact]
    public void Overlay_shows_when_get_object_returns_the_flag()
    {
        var present = new SpendBrakeLockReadResult { Present = true };
        Assert.True(SpendBrakeLockUx.ShouldShowOverlay(present));
        Assert.True(SpendBrakeLockUx.ShouldShowOverlay(new SpendBrakeLockReadResult
        {
            Present = true,
            ParseWarning = "malformed",
        }));
    }

    [Fact]
    public void Overlay_does_not_show_when_flag_is_absent()
    {
        Assert.False(SpendBrakeLockUx.ShouldShowOverlay(null));
        Assert.False(SpendBrakeLockUx.ShouldShowOverlay(new SpendBrakeLockReadResult { Present = false }));
    }

    [Fact]
    public void Start_is_blocked_when_flag_is_present_or_get_failed()
    {
        Assert.True(SpendBrakeLockUx.BlocksStart(null));
        Assert.True(SpendBrakeLockUx.BlocksStart(
            ServiceResult<SpendBrakeLockReadResult>.Fail("GetObject failed: TooManyRequests (429).")));
        Assert.True(SpendBrakeLockUx.BlocksStart(
            ServiceResult<SpendBrakeLockReadResult>.Ok(new SpendBrakeLockReadResult { Present = true })));
        Assert.False(SpendBrakeLockUx.BlocksStart(
            ServiceResult<SpendBrakeLockReadResult>.Ok(new SpendBrakeLockReadResult { Present = false })));
    }

    [Fact]
    public void Confirmation_matches_exactly_including_trailing_newline_trim()
    {
        Assert.True(SpendBrakeLockUx.MatchesConfirmation(SpendBrakeLockUx.ConfirmationSentence));
        Assert.True(SpendBrakeLockUx.MatchesConfirmation("  " + SpendBrakeLockUx.ConfirmationSentence + "\r\n"));
        Assert.False(SpendBrakeLockUx.MatchesConfirmation(""));
        Assert.False(SpendBrakeLockUx.MatchesConfirmation(null));
        Assert.False(SpendBrakeLockUx.MatchesConfirmation(
            SpendBrakeLockUx.ConfirmationSentence.ToLowerInvariant()));
        Assert.False(SpendBrakeLockUx.MatchesConfirmation(
            SpendBrakeLockUx.ConfirmationSentence[..^1]));
        Assert.False(SpendBrakeLockUx.MatchesConfirmation(
            SpendBrakeLockUx.ConfirmationSentence.Replace("card", "  card")));
    }

    [Fact]
    public void Confirmation_sentence_is_the_product_freeze()
    {
        Assert.Equal(
            "I confirm that we have entered a new calendar month and that my free monthly usage limits have been reset. I understand that if I ignore these warnings and turn on my server before a new month has started, the card I created my Oracle Cloud account with will automatically be charged for the excess usage.",
            SpendBrakeLockUx.ConfirmationSentence);
    }
}
