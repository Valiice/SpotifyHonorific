using FluentAssertions;
using SpotifyHonorific.Core;

namespace SpotifyHonorific.Tests;

public class RecentTitleCacheTests
{
    private static readonly DateTime BaseTime = new(2026, 7, 4, 12, 0, 0);

    [Fact]
    public void RecordIfUseful_PlaceholderText_NotRecorded()
    {
        var cache = new RecentTitleCache();

        cache.RecordIfUseful("Xm'zora Tia", "Listening to Spotify", BaseTime);

        cache.BuildSearchQuery("Xm'zora Tia", BaseTime).Should().BeNull();
    }

    [Fact]
    public void RecordIfUseful_SingleRealValue_BuildSearchQueryReturnsThatValue()
    {
        var cache = new RecentTitleCache();

        cache.RecordIfUseful("Xm'zora Tia", "DECADANCE", BaseTime);

        cache.BuildSearchQuery("Xm'zora Tia", BaseTime).Should().Be("DECADANCE");
    }

    [Fact]
    public void RecordIfUseful_TwoDistinctRealValues_BuildSearchQueryJoinsBoth()
    {
        var cache = new RecentTitleCache();

        cache.RecordIfUseful("Xm'zora Tia", "DECADANCE", BaseTime);
        cache.RecordIfUseful("Xm'zora Tia", "MEJIBRAY", BaseTime.AddSeconds(10));

        cache.BuildSearchQuery("Xm'zora Tia", BaseTime.AddSeconds(10)).Should().Be("DECADANCE MEJIBRAY");
    }

    [Fact]
    public void RecordIfUseful_SameValueRepeated_DoesNotDuplicateInQuery()
    {
        var cache = new RecentTitleCache();

        cache.RecordIfUseful("Xm'zora Tia", "DECADANCE", BaseTime);
        cache.RecordIfUseful("Xm'zora Tia", "DECADANCE", BaseTime.AddSeconds(3));
        cache.RecordIfUseful("Xm'zora Tia", "DECADANCE", BaseTime.AddSeconds(6));

        cache.BuildSearchQuery("Xm'zora Tia", BaseTime.AddSeconds(6)).Should().Be("DECADANCE");
    }

    [Fact]
    public void RecordIfUseful_MoreThanTwoDistinctValues_KeepsOnlyMostRecentTwo()
    {
        var cache = new RecentTitleCache();

        cache.RecordIfUseful("Xm'zora Tia", "First", BaseTime);
        cache.RecordIfUseful("Xm'zora Tia", "Second", BaseTime.AddSeconds(10));
        cache.RecordIfUseful("Xm'zora Tia", "Third", BaseTime.AddSeconds(20));

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

        cache.RecordIfUseful("Xm'zora Tia", "DECADANCE", BaseTime);

        cache.BuildSearchQuery("Xm'zora Tia", BaseTime.AddSeconds(41)).Should().BeNull();
    }

    [Fact]
    public void RecordIfUseful_DifferentCharacters_AreIndependent()
    {
        var cache = new RecentTitleCache();

        cache.RecordIfUseful("Xm'zora Tia", "DECADANCE", BaseTime);
        cache.RecordIfUseful("Va Li", "Traffic - Radio Edit", BaseTime);

        cache.BuildSearchQuery("Xm'zora Tia", BaseTime).Should().Be("DECADANCE");
        cache.BuildSearchQuery("Va Li", BaseTime).Should().Be("Traffic - Radio Edit");
    }
}
