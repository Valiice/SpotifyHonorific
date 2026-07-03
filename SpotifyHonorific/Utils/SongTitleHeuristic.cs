using System.Linq;
using System.Text.RegularExpressions;

namespace SpotifyHonorific.Utils;

public static partial class SongTitleHeuristic
{
    // Symbols the default SpotifyHonorific templates (and most similar
    // Honorific-driven music/activity templates) wrap the title in.
    private static readonly string[] MusicSymbols = ["♪", "♫", "♩", "♬", "🎵", "🎶"];

    // "Track - Artist" / "Artist - Track" style separator, requiring real
    // text on both sides of the dash so a bare leading/trailing "-" doesn't match.
    [GeneratedRegex(@".+ - .+")]
    private static partial Regex TrackArtistSeparatorPattern();

    // Best-effort only: Honorific titles are arbitrary user-configured text
    // with no metadata saying which plugin set them or what they represent,
    // so this can never be a reliable classifier — it just reduces noise.
    public static bool LooksLikeSong(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return false;

        if (MusicSymbols.Any(title.Contains)) return true;

        return TrackArtistSeparatorPattern().IsMatch(title);
    }
}
