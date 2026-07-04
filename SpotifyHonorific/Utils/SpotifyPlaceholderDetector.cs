using System;
using System.Linq;

namespace SpotifyHonorific.Utils;

public static class SpotifyPlaceholderDetector
{
    // Matches the "Listening to Spotify" filler phase of the default
    // SpotifyHonorific templates (ActivityConfig.cs), shown while cycling
    // between that placeholder, the track name, and the artist name. It
    // carries no song info and must never be used as a search query or
    // counted as a usable title sample.
    private const string PlaceholderText = "Listening to Spotify";

    public static bool IsPlaceholder(string cleanedTitle) =>
        string.Equals(cleanedTitle.Trim(), PlaceholderText, StringComparison.OrdinalIgnoreCase);

    // A phase with no searchable song info: the placeholder, or letterless
    // text like a playback timer ("02:56 / 04:04" cleans to "0256 0404").
    // Searching Spotify for bare digits matches essentially random songs.
    public static bool IsNoInfoPhase(string cleanedTitle) =>
        IsPlaceholder(cleanedTitle) || !cleanedTitle.Any(char.IsLetter);
}
