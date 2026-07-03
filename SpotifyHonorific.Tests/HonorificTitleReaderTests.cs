using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using FluentAssertions;
using NSubstitute;
using SpotifyHonorific.Core;

namespace SpotifyHonorific.Tests;

public class HonorificTitleReaderTests
{
    [Fact]
    public void TryGetTitle_CustomTitleJson_ReturnsTrueWithExtractedTitleText()
    {
        var pluginInterface = Substitute.For<IDalamudPluginInterface>();
        var subscriber = Substitute.For<ICallGateSubscriber<int, string>>();
        subscriber.InvokeFunc(5).Returns("""{"Title":"♪ Track - Artist ♪","IsPrefix":false,"IsOriginal":false,"Color":null}""");
        pluginInterface.GetIpcSubscriber<int, string>("Honorific.GetCharacterTitle").Returns(subscriber);
        var pluginLog = Substitute.For<IPluginLog>();

        var reader = new HonorificTitleReader(pluginInterface, pluginLog);
        var result = reader.TryGetTitle(5, out var title);

        result.Should().BeTrue();
        title.Should().Be("♪ Track - Artist ♪");
    }

    [Fact]
    public void TryGetTitle_OriginalInGameTitleJson_ReturnsFalse()
    {
        // IsOriginal:true means this is a real unlocked in-game title just
        // rendered by Honorific, not custom text — not a song, filter it out.
        var pluginInterface = Substitute.For<IDalamudPluginInterface>();
        var subscriber = Substitute.For<ICallGateSubscriber<int, string>>();
        subscriber.InvokeFunc(5).Returns("""{"Title":"Winged Death","IsPrefix":true,"IsOriginal":true,"Color":null}""");
        pluginInterface.GetIpcSubscriber<int, string>("Honorific.GetCharacterTitle").Returns(subscriber);
        var pluginLog = Substitute.For<IPluginLog>();

        var reader = new HonorificTitleReader(pluginInterface, pluginLog);
        var result = reader.TryGetTitle(5, out var title);

        result.Should().BeFalse();
        title.Should().BeEmpty();
    }

    [Fact]
    public void TryGetTitle_EmptyTitleFieldInJson_ReturnsFalse()
    {
        var pluginInterface = Substitute.For<IDalamudPluginInterface>();
        var subscriber = Substitute.For<ICallGateSubscriber<int, string>>();
        subscriber.InvokeFunc(5).Returns("""{"Title":"","IsPrefix":false,"IsOriginal":false,"Color":null}""");
        pluginInterface.GetIpcSubscriber<int, string>("Honorific.GetCharacterTitle").Returns(subscriber);
        var pluginLog = Substitute.For<IPluginLog>();

        var reader = new HonorificTitleReader(pluginInterface, pluginLog);
        var result = reader.TryGetTitle(5, out var title);

        result.Should().BeFalse();
        title.Should().BeEmpty();
    }

    [Fact]
    public void TryGetTitle_MalformedJson_ReturnsFalse()
    {
        var pluginInterface = Substitute.For<IDalamudPluginInterface>();
        var subscriber = Substitute.For<ICallGateSubscriber<int, string>>();
        subscriber.InvokeFunc(5).Returns("not json");
        pluginInterface.GetIpcSubscriber<int, string>("Honorific.GetCharacterTitle").Returns(subscriber);
        var pluginLog = Substitute.For<IPluginLog>();

        var reader = new HonorificTitleReader(pluginInterface, pluginLog);
        var result = reader.TryGetTitle(5, out var title);

        result.Should().BeFalse();
        title.Should().BeEmpty();
    }

    [Fact]
    public void TryGetTitle_IpcReturnsEmptyString_ReturnsFalse()
    {
        var pluginInterface = Substitute.For<IDalamudPluginInterface>();
        var subscriber = Substitute.For<ICallGateSubscriber<int, string>>();
        subscriber.InvokeFunc(5).Returns("");
        pluginInterface.GetIpcSubscriber<int, string>("Honorific.GetCharacterTitle").Returns(subscriber);
        var pluginLog = Substitute.For<IPluginLog>();

        var reader = new HonorificTitleReader(pluginInterface, pluginLog);
        var result = reader.TryGetTitle(5, out var title);

        result.Should().BeFalse();
        title.Should().BeEmpty();
    }

    [Fact]
    public void TryGetTitle_IpcThrows_ReturnsFalseWithoutPropagating()
    {
        var pluginInterface = Substitute.For<IDalamudPluginInterface>();
        var subscriber = Substitute.For<ICallGateSubscriber<int, string>>();
        subscriber.InvokeFunc(5).Returns(_ => throw new InvalidOperationException("Honorific not installed"));
        pluginInterface.GetIpcSubscriber<int, string>("Honorific.GetCharacterTitle").Returns(subscriber);
        var pluginLog = Substitute.For<IPluginLog>();

        var reader = new HonorificTitleReader(pluginInterface, pluginLog);
        var result = reader.TryGetTitle(5, out var title);

        result.Should().BeFalse();
        title.Should().BeEmpty();
    }
}
