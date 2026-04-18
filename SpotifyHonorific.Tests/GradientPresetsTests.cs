using FluentAssertions;
using SpotifyHonorific.Gradient;

namespace SpotifyHonorific.Tests;

public class GradientPresetsTests
{
    [Fact]
    public void NumPresets_ShouldBePositive()
    {
        GradientPresets.NumPresets.Should().BeGreaterThan(0);
    }

    [Fact]
    public void GetName_ValidIndex_ShouldReturnNonEmptyString()
    {
        var name = GradientPresets.GetName(0);
        name.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GetName_InvalidIndex_ShouldReturnFallbackString()
    {
        var name = GradientPresets.GetName(999);
        name.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void AllNames_ShouldContainPrideRainbow()
    {
        var names = Enumerable.Range(0, GradientPresets.NumPresets)
            .Select(GradientPresets.GetName);
        names.Should().Contain(n => n.Contains("Pride Rainbow"));
    }
}
