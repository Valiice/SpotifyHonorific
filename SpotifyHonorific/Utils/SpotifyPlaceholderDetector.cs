using System;

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
}
