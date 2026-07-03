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
}
