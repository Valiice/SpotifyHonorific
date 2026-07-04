using FluentAssertions;
using SpotifyAPI.Web;
using SpotifyHonorific.Core;

namespace SpotifyHonorific.Tests;

public class TrackQueueServiceTests
{
    private static FullTrack MakeTrack(string name) => new() { Name = name };

    [Fact]
    public void PickBestMatch_HintExactlyMatchesLaterResult_PrefersItOverTopHit()
    {
        // Spotify ranked the romanized release first, but the player's title
        // showed the Japanese one — the exact hint match must win.
        var tracks = new[] { MakeTrack("Usseewa"), MakeTrack("うっせぇわ") };

        var picked = TrackQueueService.PickBestMatch(tracks, ["うっせぇわ", "Ado"]);

        picked.Name.Should().Be("うっせぇわ");
    }

    [Fact]
    public void PickBestMatch_NoExactMatch_HintContainingResultNameWins()
    {
        var tracks = new[] { MakeTrack("Some Remix"), MakeTrack("FATE") };

        var picked = TrackQueueService.PickBestMatch(tracks, ["FATE - Alan Walker Ava Max"]);

        picked.Name.Should().Be("FATE");
    }

    [Fact]
    public void PickBestMatch_NothingMatches_FallsBackToTopHit()
    {
        var tracks = new[] { MakeTrack("Top Hit"), MakeTrack("Second Hit") };

        var picked = TrackQueueService.PickBestMatch(tracks, ["completely different text"]);

        picked.Name.Should().Be("Top Hit");
    }

    [Fact]
    public void PickBestMatch_ExactMatchIsCaseInsensitive()
    {
        var tracks = new[] { MakeTrack("Other"), MakeTrack("The Phoenix") };

        var picked = TrackQueueService.PickBestMatch(tracks, ["the phoenix"]);

        picked.Name.Should().Be("The Phoenix");
    }
}
