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
}
