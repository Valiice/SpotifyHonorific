using FluentAssertions;
using SpotifyHonorific.Core;

namespace SpotifyHonorific.Tests;

public class RecentTitleCacheTests
{
    private static readonly DateTime BaseTime = new(2026, 7, 4, 12, 0, 0);

    [Fact]
    public void Record_PlaceholderText_NotRecorded()
    {
        var cache = new RecentTitleCache();

        cache.Record("Xm'zora Tia", "Listening to Spotify", BaseTime);

        cache.BuildSearchQuery("Xm'zora Tia", BaseTime).Should().BeNull();
    }

    [Fact]
    public void Record_SingleRealValue_BuildSearchQueryReturnsThatValue()
    {
        var cache = new RecentTitleCache();

        cache.Record("Xm'zora Tia", "DECADANCE", BaseTime);

        cache.BuildSearchQuery("Xm'zora Tia", BaseTime).Should().Be("DECADANCE");
    }

    [Fact]
    public void Record_TwoDistinctRealValues_BuildSearchQueryJoinsBoth()
    {
        var cache = new RecentTitleCache();

        cache.Record("Xm'zora Tia", "DECADANCE", BaseTime);
        cache.Record("Xm'zora Tia", "MEJIBRAY", BaseTime.AddSeconds(10));

        cache.BuildSearchQuery("Xm'zora Tia", BaseTime.AddSeconds(10)).Should().Be("DECADANCE MEJIBRAY");
    }

    [Fact]
    public void Record_SameValueRepeated_DoesNotDuplicateInQuery()
    {
        var cache = new RecentTitleCache();

        cache.Record("Xm'zora Tia", "DECADANCE", BaseTime);
        cache.Record("Xm'zora Tia", "DECADANCE", BaseTime.AddSeconds(3));
        cache.Record("Xm'zora Tia", "DECADANCE", BaseTime.AddSeconds(6));

        cache.BuildSearchQuery("Xm'zora Tia", BaseTime.AddSeconds(6)).Should().Be("DECADANCE");
    }

    [Fact]
    public void Record_MoreThanTwoDistinctValues_KeepsOnlyMostRecentTwo()
    {
        var cache = new RecentTitleCache();

        cache.Record("Xm'zora Tia", "First", BaseTime);
        cache.Record("Xm'zora Tia", "Second", BaseTime.AddSeconds(10));
        cache.Record("Xm'zora Tia", "Third", BaseTime.AddSeconds(20));

        cache.BuildSearchQuery("Xm'zora Tia", BaseTime.AddSeconds(20)).Should().Be("Second Third");
    }

    [Fact]
    public void Record_ExistingSampleRefreshedBeforeEviction_EvictsOldestBySeenAtNotListPosition()
    {
        // Re-seeing a sample refreshes its SeenAt in place without moving it
        // in the list, so evicting the positional head would remove the
        // refreshed (newer) sample and keep a stale one, which then gets
        // combined into a corrupted cross-song query.
        var cache = new RecentTitleCache();

        cache.Record("Xm'zora Tia", "TrackA", BaseTime);
        cache.Record("Xm'zora Tia", "ArtistB", BaseTime.AddSeconds(5));
        cache.Record("Xm'zora Tia", "TrackA", BaseTime.AddSeconds(10));
        cache.Record("Xm'zora Tia", "TrackC", BaseTime.AddSeconds(15));

        cache.GetFreshSamples("Xm'zora Tia", BaseTime.AddSeconds(15))
            .Should().Equal("TrackA", "TrackC");
        cache.BuildSearchQuery("Xm'zora Tia", BaseTime.AddSeconds(15)).Should().Be("TrackA TrackC");
    }

    [Fact]
    public void BuildSearchQuery_NoSamplesRecorded_ReturnsNull()
    {
        var cache = new RecentTitleCache();

        cache.BuildSearchQuery("Xm'zora Tia", BaseTime).Should().BeNull();
    }

    [Fact]
    public void BuildSearchQuery_SamplesExpired_ReturnsNull()
    {
        var cache = new RecentTitleCache();

        cache.Record("Xm'zora Tia", "DECADANCE", BaseTime);

        cache.BuildSearchQuery("Xm'zora Tia", BaseTime.AddSeconds(41)).Should().BeNull();
    }

    [Fact]
    public void Record_DifferentCharacters_AreIndependent()
    {
        var cache = new RecentTitleCache();

        cache.Record("Xm'zora Tia", "DECADANCE", BaseTime);
        cache.Record("Va Li", "Traffic - Radio Edit", BaseTime);

        cache.BuildSearchQuery("Xm'zora Tia", BaseTime).Should().Be("DECADANCE");
        cache.BuildSearchQuery("Va Li", BaseTime).Should().Be("Traffic - Radio Edit");
    }

    [Fact]
    public void BuildSearchQuery_NewestSampleHasTrackArtistPattern_UsedAloneWithoutOlderSample()
    {
        // The person changed songs: an old bare sample is still cached when a
        // complete "Track - Artist" title arrives. Combining them corrupted
        // the search (queued a wrong song by the leftover artist).
        var cache = new RecentTitleCache();

        cache.Record("Maki Shimada", "AGORA", BaseTime);
        cache.Record("Maki Shimada", "FATE - Alan Walker Ava Max", BaseTime.AddSeconds(10));

        cache.BuildSearchQuery("Maki Shimada", BaseTime.AddSeconds(10)).Should().Be("FATE - Alan Walker Ava Max");
    }

    [Fact]
    public void BuildSearchQuery_SamplesFurtherApartThanOneCycle_OnlyNewestUsed()
    {
        // A gap larger than one template cycle (~15s between phases) likely
        // straddles a song change, so the older sample must not be joined in.
        var cache = new RecentTitleCache();

        cache.Record("Xm'zora Tia", "Old Song Leftover", BaseTime);
        cache.Record("Xm'zora Tia", "MEJIBRAY", BaseTime.AddSeconds(20));

        cache.BuildSearchQuery("Xm'zora Tia", BaseTime.AddSeconds(20)).Should().Be("MEJIBRAY");
    }

    [Fact]
    public void Record_PlaybackTimerJunk_NotRecorded()
    {
        // "02:56 / 04:04" cleans to "0256 0404"; caching it produced garbage
        // search queries that matched essentially random songs.
        var cache = new RecentTitleCache();

        cache.Record("Maki Shimada", "0256 0404", BaseTime);

        cache.BuildSearchQuery("Maki Shimada", BaseTime).Should().BeNull();
    }

    [Fact]
    public void Record_PlaybackTimerJunk_DoesNotPolluteExistingSamples()
    {
        var cache = new RecentTitleCache();

        cache.Record("Maki Shimada", "The Phoenix", BaseTime);
        cache.Record("Maki Shimada", "0256 0404", BaseTime.AddSeconds(10));

        cache.BuildSearchQuery("Maki Shimada", BaseTime.AddSeconds(10)).Should().Be("The Phoenix");
    }

    [Fact]
    public void Record_Placeholder_MarksCharacterAsKnownSpotifyListener()
    {
        var cache = new RecentTitleCache();

        cache.Record("Xm'zora Tia", "Listening to Spotify", BaseTime);

        cache.IsKnownSpotifyListener("Xm'zora Tia").Should().BeTrue();
    }

    [Fact]
    public void IsKnownSpotifyListener_NeverShowedPlaceholder_ReturnsFalse()
    {
        var cache = new RecentTitleCache();

        cache.Record("Maki Shimada", "FATE - Alan Walker Ava Max", BaseTime);

        cache.IsKnownSpotifyListener("Maki Shimada").Should().BeFalse();
    }

    [Fact]
    public void GetDiagnosticSnapshot_CoversSampledAndListenerOnlyCharacters()
    {
        var cache = new RecentTitleCache();
        cache.Record("Aya Brea", "Some Song Title", BaseTime);
        cache.Record("Kaine Mana", "Listening to Spotify", BaseTime); // placeholder: listener only

        var snapshot = cache.GetDiagnosticSnapshot(BaseTime);

        snapshot.Should().HaveCount(2);
        snapshot.Should().Contain(s => s.CharacterName == "Aya Brea" && !s.KnownListener && s.FreshSampleCount == 1);
        snapshot.Should().Contain(s => s.CharacterName == "Kaine Mana" && s.KnownListener && s.FreshSampleCount == 0);
    }
}
