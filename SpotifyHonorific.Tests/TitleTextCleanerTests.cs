using FluentAssertions;
using SpotifyHonorific.Utils;

namespace SpotifyHonorific.Tests;

public class TitleTextCleanerTests
{
    [Fact]
    public void Clean_StripsMusicNoteSymbols()
    {
        TitleTextCleaner.Clean("♪BLADETEKK - angelhard♪").Should().Be("BLADETEKK - angelhard");
    }

    [Fact]
    public void Clean_StripsGuillemets()
    {
        TitleTextCleaner.Clean("«DEMON»").Should().Be("DEMON");
    }

    [Fact]
    public void Clean_CollapsesRepeatedWhitespaceAndTrims()
    {
        TitleTextCleaner.Clean("★  Dreaming   ♡ ").Should().Be("Dreaming");
    }

    [Fact]
    public void Clean_KeepsHyphensApostrophesAndAmpersands()
    {
        TitleTextCleaner.Clean("♪Don't Stop - Fleetwood Mac & Friends♪")
            .Should().Be("Don't Stop - Fleetwood Mac & Friends");
    }

    [Fact]
    public void Clean_EmptyInput_ReturnsEmpty()
    {
        TitleTextCleaner.Clean("").Should().Be("");
    }

    [Fact]
    public void Clean_OnlyDecorativeSymbols_ReturnsEmpty()
    {
        TitleTextCleaner.Clean("♪★♡«»").Should().Be("");
    }
}
