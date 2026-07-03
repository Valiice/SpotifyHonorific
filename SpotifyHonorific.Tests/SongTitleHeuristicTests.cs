using FluentAssertions;
using SpotifyHonorific.Utils;

namespace SpotifyHonorific.Tests;

public class SongTitleHeuristicTests
{
    [Fact]
    public void LooksLikeSong_ContainsMusicNoteSymbol_ReturnsTrue()
    {
        SongTitleHeuristic.LooksLikeSong("♪ Track - Artist ♪").Should().BeTrue();
    }

    [Fact]
    public void LooksLikeSong_ContainsEmojiMusicSymbol_ReturnsTrue()
    {
        SongTitleHeuristic.LooksLikeSong("🎵 Track 🎵").Should().BeTrue();
    }

    [Fact]
    public void LooksLikeSong_TrackArtistDashPattern_ReturnsTrue()
    {
        SongTitleHeuristic.LooksLikeSong("BLADETEKK - angelhard").Should().BeTrue();
    }

    [Fact]
    public void LooksLikeSong_PlainRoleplayTitle_ReturnsFalse()
    {
        SongTitleHeuristic.LooksLikeSong("Once and Future King").Should().BeFalse();
    }

    [Fact]
    public void LooksLikeSong_RandomCustomTitle_ReturnsFalse()
    {
        SongTitleHeuristic.LooksLikeSong("trash, scum and unnecessary").Should().BeFalse();
    }

    [Fact]
    public void LooksLikeSong_EmptyString_ReturnsFalse()
    {
        SongTitleHeuristic.LooksLikeSong("").Should().BeFalse();
    }

    [Fact]
    public void LooksLikeSong_BareLeadingDash_ReturnsFalse()
    {
        SongTitleHeuristic.LooksLikeSong("- leading dash").Should().BeFalse();
    }
}
