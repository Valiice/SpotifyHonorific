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
        var watcher = new NearbyTitleWatcher(objectTable, titleReader, new RecentTitleCache());

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

        var watcher = new NearbyTitleWatcher(objectTable, titleReader, new RecentTitleCache());
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

        var watcher = new NearbyTitleWatcher(objectTable, titleReader, new RecentTitleCache());
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

        var watcher = new NearbyTitleWatcher(objectTable, titleReader, new RecentTitleCache());
        watcher.Update(3.0);

        watcher.History.Should().BeEmpty();
        titleReader.DidNotReceive().TryGetTitle(Arg.Any<int>(), out Arg.Any<string>());
    }

    [Fact]
    public void Update_RealTitle_FeedsRecentTitleCacheWithCleanedText()
    {
        var objectTable = Substitute.For<IObjectTable>();
        objectTable.Length.Returns(2);
        objectTable[0].Returns((IGameObject?)null);
        var nearbyPlayer = MakePlayer("Xm'zora Tia");
        objectTable[1].Returns(nearbyPlayer);

        var titleReader = Substitute.For<IHonorificTitleReader>();
        titleReader.TryGetTitle(1, out Arg.Any<string>())
            .Returns(x => { x[1] = "♪ DECADANCE ♪"; return true; });

        var recentTitleCache = new RecentTitleCache();
        var watcher = new NearbyTitleWatcher(objectTable, titleReader, recentTitleCache);
        watcher.Update(3.0);

        recentTitleCache.BuildSearchQuery("Xm'zora Tia", DateTime.Now).Should().Be("DECADANCE");
    }

    [Fact]
    public void Update_PlaceholderTitle_DoesNotFeedRecentTitleCache()
    {
        var objectTable = Substitute.For<IObjectTable>();
        objectTable.Length.Returns(2);
        objectTable[0].Returns((IGameObject?)null);
        var nearbyPlayer = MakePlayer("Xm'zora Tia");
        objectTable[1].Returns(nearbyPlayer);

        var titleReader = Substitute.For<IHonorificTitleReader>();
        titleReader.TryGetTitle(1, out Arg.Any<string>())
            .Returns(x => { x[1] = "♪Listening to Spotify♪"; return true; });

        var recentTitleCache = new RecentTitleCache();
        var watcher = new NearbyTitleWatcher(objectTable, titleReader, recentTitleCache);
        watcher.Update(3.0);

        recentTitleCache.BuildSearchQuery("Xm'zora Tia", DateTime.Now).Should().BeNull();
        recentTitleCache.IsKnownSpotifyListener("Xm'zora Tia").Should().BeTrue();
    }

    private static IObjectTable MakeLargeObjectTable(int length)
    {
        var objectTable = Substitute.For<IObjectTable>();
        objectTable.Length.Returns(length);
        objectTable[0].Returns((IGameObject?)null); // local player slot, always skipped
        for (var i = 1; i < length; i++)
        {
            var player = MakePlayer($"Player{i}");
            objectTable[i].Returns(player);
        }
        return objectTable;
    }

    [Fact]
    public void Update_LargeTable_ProcessesTwentyFiveSlotsPerFrame()
    {
        var objectTable = MakeLargeObjectTable(60);
        var titleReader = Substitute.For<IHonorificTitleReader>();
        titleReader.TryGetTitle(Arg.Any<int>(), out Arg.Any<string>())
            .Returns(x => { x[1] = "♪ Track - Artist ♪"; return true; });

        var watcher = new NearbyTitleWatcher(objectTable, titleReader, new RecentTitleCache());

        watcher.Update(3.0);
        watcher.History.Should().HaveCount(25);

        watcher.Update(0.016);
        watcher.History.Should().HaveCount(50);

        watcher.Update(0.016);
        watcher.History.Should().HaveCount(59);
    }

    [Fact]
    public void Update_DuringPass_DoesNotStartAnotherPass()
    {
        var objectTable = MakeLargeObjectTable(60);
        var titleReader = Substitute.For<IHonorificTitleReader>();
        titleReader.TryGetTitle(Arg.Any<int>(), out Arg.Any<string>())
            .Returns(x => { x[1] = "♪ Track - Artist ♪"; return true; });

        var watcher = new NearbyTitleWatcher(objectTable, titleReader, new RecentTitleCache());

        watcher.Update(3.0);
        watcher.Update(3.0);

        watcher.History.Should().HaveCount(50);
        titleReader.Received(50).TryGetTitle(Arg.Any<int>(), out Arg.Any<string>());
    }

    [Fact]
    public void Update_AfterPassCompletes_TimerStartsFromZero()
    {
        var objectTable = Substitute.For<IObjectTable>();
        objectTable.Length.Returns(2);
        objectTable[0].Returns((IGameObject?)null);
        var nearbyPlayer = MakePlayer("Va Li");
        objectTable[1].Returns(nearbyPlayer);

        var titleReader = Substitute.For<IHonorificTitleReader>();
        titleReader.TryGetTitle(1, out Arg.Any<string>())
            .Returns(x => { x[1] = "♪ Track - Artist ♪"; return true; });

        var watcher = new NearbyTitleWatcher(objectTable, titleReader, new RecentTitleCache());

        watcher.Update(3.0); // completes the pass (table length 2, one slot to process)

        titleReader.ClearReceivedCalls();

        watcher.Update(2.9);
        titleReader.DidNotReceive().TryGetTitle(Arg.Any<int>(), out Arg.Any<string>());

        watcher.Update(0.2);
        titleReader.Received(1).TryGetTitle(Arg.Any<int>(), out Arg.Any<string>());
    }
}
