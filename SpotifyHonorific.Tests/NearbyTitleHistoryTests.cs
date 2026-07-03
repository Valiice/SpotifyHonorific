using FluentAssertions;
using SpotifyHonorific.Core;

namespace SpotifyHonorific.Tests;

public class NearbyTitleHistoryTests
{
    [Fact]
    public void Upsert_NewCharacter_AddsEntry()
    {
        var history = new NearbyTitleHistory();
        var seenAt = new DateTime(2026, 7, 3, 12, 0, 0);

        history.Upsert("Va Li", "♪ Track ♪", seenAt);

        history.Entries.Should().ContainSingle();
        history.Entries[0].CharacterName.Should().Be("Va Li");
        history.Entries[0].RawTitle.Should().Be("♪ Track ♪");
        history.Entries[0].LastSeen.Should().Be(seenAt);
    }

    [Fact]
    public void Upsert_ExistingCharacter_UpdatesInPlaceWithoutDuplicating()
    {
        var history = new NearbyTitleHistory();
        history.Upsert("Va Li", "♪ Old Track ♪", new DateTime(2026, 7, 3, 12, 0, 0));

        history.Upsert("Va Li", "♪ New Track ♪", new DateTime(2026, 7, 3, 12, 0, 5));

        history.Entries.Should().ContainSingle();
        history.Entries[0].RawTitle.Should().Be("♪ New Track ♪");
        history.Entries[0].LastSeen.Should().Be(new DateTime(2026, 7, 3, 12, 0, 5));
    }

    [Fact]
    public void Upsert_AtCap_EvictsOldestBySeenTime()
    {
        var history = new NearbyTitleHistory();
        var baseTime = new DateTime(2026, 7, 3, 12, 0, 0);

        for (var i = 0; i < 100; i++)
        {
            history.Upsert($"Player{i}", "title", baseTime.AddSeconds(i));
        }
        history.Entries.Should().HaveCount(100);

        // Player0 has the oldest LastSeen (baseTime) — adding one more new
        // character beyond the cap should evict it specifically.
        history.Upsert("Player100", "title", baseTime.AddSeconds(100));

        history.Entries.Should().HaveCount(100);
        history.Entries.Should().NotContain(e => e.CharacterName == "Player0");
        history.Entries.Should().Contain(e => e.CharacterName == "Player100");
    }

    [Fact]
    public void Upsert_UpdatingExistingAtCap_DoesNotEvictAnyone()
    {
        var history = new NearbyTitleHistory();
        var baseTime = new DateTime(2026, 7, 3, 12, 0, 0);

        for (var i = 0; i < 100; i++)
        {
            history.Upsert($"Player{i}", "title", baseTime.AddSeconds(i));
        }

        history.Upsert("Player50", "updated title", baseTime.AddSeconds(200));

        history.Entries.Should().HaveCount(100);
        history.Entries.Should().Contain(e => e.CharacterName == "Player0");
        history.Entries.First(e => e.CharacterName == "Player50").RawTitle.Should().Be("updated title");
    }
}
