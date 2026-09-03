using SpotifyAPI.Web;
using Dalamud.Plugin.Services;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NSubstitute;
using SpotifyHonorific.Activities;
using SpotifyHonorific.Core;
using SpotifyHonorific.Gradient;
using SpotifyHonorific.Updaters;
using System.Numerics;

namespace SpotifyHonorific.Tests;

public class TitleRenderingServiceTests
{
    private readonly TitleRenderingService _service;

    public TitleRenderingServiceTests()
    {
        var cache = new TemplateCache(Substitute.For<IPluginLog>());
        _service = new TitleRenderingService(cache, Substitute.For<IPluginLog>(), Substitute.For<IChatGui>());
    }

    private static UpdaterContext MakeContext() => new() { SecsElapsed = 0 };

    [Fact]
    public void SerializeTitleData_NoGradient_IncludesGlowAndNoGradientKeys()
    {
        var config = new ActivityConfig
        {
            IsPrefix = false,
            Glow = new Vector3(1f, 0f, 1f)
        };
        var json = _service.SerializeTitleData("Test", config, MakeContext(), true);
        var obj = JObject.Parse(json);
        obj.ContainsKey("Glow").Should().BeTrue();
        obj.ContainsKey("GradientColourSet").Should().BeFalse();
        obj.ContainsKey("GradientAnimationStyle").Should().BeFalse();
    }

    [Fact]
    public void SerializeTitleData_PresetGradient_IncludesGradientKeysAndOmitsGlow()
    {
        var config = new ActivityConfig
        {
            IsPrefix = false,
            GradientColourSet = 3,
            GradientAnimationStyle = GradientAnimationStyle.Wave,
        };
        var json = _service.SerializeTitleData("Test", config, MakeContext(), true);
        var obj = JObject.Parse(json);
        obj["GradientColourSet"]!.Value<int>().Should().Be(3);
        obj["GradientAnimationStyle"]!.Value<int>().Should().Be((int)GradientAnimationStyle.Wave);
        obj.ContainsKey("Glow").Should().BeFalse();
        obj.ContainsKey("Color3").Should().BeFalse();
    }

    [Fact]
    public void SerializeTitleData_TwoColorGradient_IncludesGlowAndColor3()
    {
        var color1 = new Vector3(0.8f, 0.35f, 0.82f);
        var color2 = new Vector3(1f, 0.84f, 0f);
        var config = new ActivityConfig
        {
            IsPrefix = false,
            GradientColourSet = -1,
            Glow = color1,
            Color3 = color2,
            GradientAnimationStyle = GradientAnimationStyle.Pulse,
        };
        var json = _service.SerializeTitleData("Test", config, MakeContext(), true);
        var obj = JObject.Parse(json);
        obj["GradientColourSet"]!.Value<int>().Should().Be(-1);
        obj["GradientAnimationStyle"]!.Value<int>().Should().Be((int)GradientAnimationStyle.Pulse);
        obj.ContainsKey("Glow").Should().BeTrue();
        obj.ContainsKey("Color3").Should().BeTrue();
    }

    [Fact]
    public void SerializeTitleData_AlwaysIncludesTitleAndIsPrefix()
    {
        var config = new ActivityConfig { IsPrefix = true };
        var json = _service.SerializeTitleData("My Title", config, MakeContext(), true);
        var obj = JObject.Parse(json);
        obj["Title"]!.Value<string>().Should().Be("My Title");
        obj["IsPrefix"]!.Value<bool>().Should().BeTrue();
    }
}

public class TitleRenderingServiceOverflowTests
{
    private readonly IChatGui _chatGui = Substitute.For<IChatGui>();
    private readonly TitleRenderingService _service;

    public TitleRenderingServiceOverflowTests()
    {
        var cache = new TemplateCache(Substitute.For<IPluginLog>());
        _service = new TitleRenderingService(cache, Substitute.For<IPluginLog>(), _chatGui);
    }

    private static UpdaterContext MakeContext() => new() { SecsElapsed = 0 };

    private static ActivityConfig ArtistSongTemplate() => new()
    {
        TitleTemplate = "{{ Activity.Artists[0].Name }} - {{ Activity.Name }}",
    };

    private static FullTrack MakeTrack(string artist, string name) => new()
    {
        Id = "track1",
        Name = name,
        Artists = [new SimpleArtist { Name = artist }],
    };

    [Fact]
    public void RenderTitle_LongerThanLimit_IsTruncatedToLimitInsteadOfDropped()
    {
        var track = MakeTrack("Some Fairly Long Artist Name", "A Song Title That Overflows");

        var title = _service.RenderTitle(ArtistSongTemplate(), track, MakeContext());

        title.Should().Be("Some Fairly Long Artist Name - A");
        title!.Length.Should().Be(32);
    }

    [Fact]
    public void RenderTitle_CutLandingOnSpace_DropsTrailingWhitespace()
    {
        var track = MakeTrack("Some Fairly Long Artist Names", "Overflowing Song");

        var title = _service.RenderTitle(ArtistSongTemplate(), track, MakeContext());

        title.Should().Be("Some Fairly Long Artist Names -");
    }

    [Fact]
    public void RenderTitle_WithinLimit_IsUnchanged()
    {
        var track = MakeTrack("Artist", "Song");

        var title = _service.RenderTitle(ArtistSongTemplate(), track, MakeContext());

        title.Should().Be("Artist - Song");
    }

    [Fact]
    public void RenderTitle_Overflow_WarnsInChatOncePerSession()
    {
        var first = MakeTrack("Some Fairly Long Artist Name", "A Song Title That Overflows");
        var second = MakeTrack("Another Fairly Long Artist Name", "Another Overflowing Song");

        _service.RenderTitle(ArtistSongTemplate(), first, MakeContext());
        _service.RenderTitle(ArtistSongTemplate(), second, MakeContext());

        _chatGui.Received(1).Print(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<ushort?>());
        _chatGui.DidNotReceive().PrintError(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<ushort?>());
    }
}
