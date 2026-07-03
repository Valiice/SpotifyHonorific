using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;
using FluentAssertions;
using NSubstitute;
using SpotifyHonorific.Core;

namespace SpotifyHonorific.Tests;

public class NearbyTitleWatcherTests
{
    private static IPlayerCharacter MakePlayer(string name)
    {
        var player = Substitute.For<IPlayerCharacter>();
        player.Name.Returns(new SeString(new TextPayload(name)));
        return player;
    }

    [Fact]
    public void Update_BelowTickInterval_DoesNotPoll()
    {
        var objectTable = Substitute.For<IObjectTable>();
        var titleReader = Substitute.For<IHonorificTitleReader>();
        var watcher = new NearbyTitleWatcher(objectTable, titleReader);

        watcher.Update(1.0);

        _ = objectTable.DidNotReceive().Length;
    }

    [Fact]
    public void Update_AtTickInterval_PollsNearbyPlayersIntoHistory()
    {
        var objectTable = Substitute.For<IObjectTable>();
        objectTable.Length.Returns(2);
        objectTable[0].Returns((IGameObject?)null); // local player slot, always skipped
        var nearbyPlayer = MakePlayer("Va Li");
        objectTable[1].Returns(nearbyPlayer);

        var titleReader = Substitute.For<IHonorificTitleReader>();
        titleReader.TryGetTitle(1, out Arg.Any<string>())
            .Returns(x => { x[1] = "♪ Track - Artist ♪"; return true; });

        var watcher = new NearbyTitleWatcher(objectTable, titleReader);
        watcher.Update(3.0);

        watcher.History.Should().ContainSingle();
        watcher.History[0].CharacterName.Should().Be("Va Li");
        watcher.History[0].RawTitle.Should().Be("♪ Track - Artist ♪");
    }

    [Fact]
    public void Update_ReaderReturnsFalse_SkipsThatCharacter()
    {
        var objectTable = Substitute.For<IObjectTable>();
        objectTable.Length.Returns(2);
        objectTable[0].Returns((IGameObject?)null);
        var nearbyPlayer = MakePlayer("No Honorific Title");
        objectTable[1].Returns(nearbyPlayer);

        var titleReader = Substitute.For<IHonorificTitleReader>();
        titleReader.TryGetTitle(1, out Arg.Any<string>()).Returns(false);

        var watcher = new NearbyTitleWatcher(objectTable, titleReader);
        watcher.Update(3.0);

        watcher.History.Should().BeEmpty();
    }

    [Fact]
    public void Update_NonPlayerObject_IsSkipped()
    {
        var objectTable = Substitute.For<IObjectTable>();
        objectTable.Length.Returns(2);
        objectTable[0].Returns((IGameObject?)null);
        var npc = Substitute.For<IGameObject>(); // not an IPlayerCharacter
        objectTable[1].Returns(npc);

        var titleReader = Substitute.For<IHonorificTitleReader>();

        var watcher = new NearbyTitleWatcher(objectTable, titleReader);
        watcher.Update(3.0);

        watcher.History.Should().BeEmpty();
        titleReader.DidNotReceive().TryGetTitle(Arg.Any<int>(), out Arg.Any<string>());
    }
}
