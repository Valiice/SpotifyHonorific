using FluentAssertions;
using SpotifyHonorific.Utils;

namespace SpotifyHonorific.Tests;

public class SpotifyPlaceholderDetectorTests
{
    [Fact]
    public void IsPlaceholder_ExactMatch_ReturnsTrue()
    {
        SpotifyPlaceholderDetector.IsPlaceholder("Listening to Spotify").Should().BeTrue();
    }

    [Fact]
    public void IsPlaceholder_DifferentCasing_ReturnsTrue()
    {
        SpotifyPlaceholderDetector.IsPlaceholder("listening to spotify").Should().BeTrue();
    }

    [Fact]
    public void IsPlaceholder_WithSurroundingWhitespace_ReturnsTrue()
    {
        SpotifyPlaceholderDetector.IsPlaceholder(" Listening to Spotify ").Should().BeTrue();
    }

    [Fact]
    public void IsPlaceholder_RealTrackName_ReturnsFalse()
    {
        SpotifyPlaceholderDetector.IsPlaceholder("DECADANCE").Should().BeFalse();
    }

    [Fact]
    public void IsPlaceholder_EmptyString_ReturnsFalse()
    {
        SpotifyPlaceholderDetector.IsPlaceholder("").Should().BeFalse();
    }

    [Fact]
    public void IsNoInfoPhase_Placeholder_ReturnsTrue()
    {
        SpotifyPlaceholderDetector.IsNoInfoPhase("Listening to Spotify").Should().BeTrue();
    }

    [Fact]
    public void IsNoInfoPhase_CleanedPlaybackTimer_ReturnsTrue()
    {
        // "02:56 / 04:04" after TitleTextCleaner becomes digits and whitespace
        SpotifyPlaceholderDetector.IsNoInfoPhase("0256 0404").Should().BeTrue();
    }

    [Fact]
    public void IsNoInfoPhase_RealTrackName_ReturnsFalse()
    {
        SpotifyPlaceholderDetector.IsNoInfoPhase("The Phoenix").Should().BeFalse();
    }

    [Fact]
    public void IsNoInfoPhase_EmptyString_ReturnsTrue()
    {
        SpotifyPlaceholderDetector.IsNoInfoPhase("").Should().BeTrue();
    }
}
