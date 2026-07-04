using System.Text.RegularExpressions;

namespace SpotifyHonorific.Utils;

public static partial class TitleTextCleaner
{
    // Keeps letters, numbers, combining marks, whitespace, and punctuation
    // that commonly appears inside real track/artist names (hyphen, apostrophe,
    // ampersand, parentheses, comma, period). Everything else, decorative
    // symbols like music notes, stars, guillemets, hearts, is stripped, since
    // those come from arbitrary user-configured Scriban templates and carry
    // no search-relevant meaning.
    [GeneratedRegex(@"[^\p{L}\p{N}\p{M}\s'\-&(),.]")]
    private static partial Regex DecorativeSymbolPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex RepeatedWhitespacePattern();

    public static string Clean(string rawTitle)
    {
        var stripped = DecorativeSymbolPattern().Replace(rawTitle, "");
        var collapsed = RepeatedWhitespacePattern().Replace(stripped, " ");
        return collapsed.Trim();
    }
}
