using FluentAssertions;
using SpotifyHonorific.Activities;
using SpotifyHonorific.Utils;

namespace SpotifyHonorific.Tests;

public class TemplateHelperIsTimeDependentTests
{
    [Fact]
    public void IsTimeDependent_StaticTemplate_ReturnsFalse()
    {
        var config = new ActivityConfig
        {
            TitleTemplate = "♪{{ Activity.Name | string.truncate 28 }}♪",
            RainbowMode = false
        };
        TemplateHelper.IsTimeDependent(config).Should().BeFalse();
    }

    [Fact]
    public void IsTimeDependent_TemplateReferencesContext_ReturnsTrue()
    {
        var config = new ActivityConfig
        {
            TitleTemplate = "{{ if (Context.SecsElapsed % 30) < 10 }}A{{ else }}B{{ end }}",
            RainbowMode = false
        };
        TemplateHelper.IsTimeDependent(config).Should().BeTrue();
    }

    [Fact]
    public void IsTimeDependent_RainbowModeWithStaticTemplate_ReturnsTrue()
    {
        var config = new ActivityConfig
        {
            TitleTemplate = "♪{{ Activity.Name }}♪",
            RainbowMode = true
        };
        TemplateHelper.IsTimeDependent(config).Should().BeTrue();
    }

    [Fact]
    public void IsTimeDependent_EmptyTemplateNoRainbow_ReturnsFalse()
    {
        var config = new ActivityConfig { TitleTemplate = string.Empty, RainbowMode = false };
        TemplateHelper.IsTimeDependent(config).Should().BeFalse();
    }

    [Fact]
    public void IsTimeDependent_LowercaseContext_ReturnsFalse()
    {
        // Scriban exposes exact member names (renamer: member => member.Name),
        // so only the exact spelling "Context" is a time-varying reference.
        var config = new ActivityConfig
        {
            TitleTemplate = "{{ Activity.Name }} in context",
            RainbowMode = false
        };
        TemplateHelper.IsTimeDependent(config).Should().BeFalse();
    }
}
