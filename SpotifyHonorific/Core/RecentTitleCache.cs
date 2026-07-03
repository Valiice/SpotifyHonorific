using SpotifyHonorific.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SpotifyHonorific.Core;

// Tracks, per character, the last couple of distinct "real" (non-placeholder)
// title samples seen recently. The default SpotifyHonorific template cycles
// every ~10s between a "Listening to Spotify" placeholder, the track name,
// and the artist name — sampling this plugin's own 3s poll over one cycle
// naturally captures both the track and artist phases, which can then be
// combined into a much better Spotify search query than whatever single
// phase happens to be showing at the exact moment someone right-clicks.
public sealed class RecentTitleCache
{
    private const int MaxDistinctValues = 2;
    private const double MaxAgeSeconds = 40.0;

    private sealed class Sample
    {
        public required string Text { get; set; }
        public required DateTime SeenAt { get; set; }
    }

    private readonly Dictionary<string, List<Sample>> _samplesByCharacter = new();

    public void RecordIfUseful(string characterName, string cleanedTitle, DateTime seenAt)
    {
        if (string.IsNullOrWhiteSpace(cleanedTitle)) return;
        if (SpotifyPlaceholderDetector.IsPlaceholder(cleanedTitle)) return;

        if (!_samplesByCharacter.TryGetValue(characterName, out var samples))
        {
            samples = new List<Sample>();
            _samplesByCharacter[characterName] = samples;
        }

        samples.RemoveAll(s => (seenAt - s.SeenAt).TotalSeconds > MaxAgeSeconds);

        var existing = samples.FirstOrDefault(s => s.Text == cleanedTitle);
        if (existing != null)
        {
            existing.SeenAt = seenAt;
            return;
        }

        samples.Add(new Sample { Text = cleanedTitle, SeenAt = seenAt });
        if (samples.Count > MaxDistinctValues)
        {
            samples.RemoveAt(0);
        }
    }

    public string? BuildSearchQuery(string characterName, DateTime now)
    {
        if (!_samplesByCharacter.TryGetValue(characterName, out var samples)) return null;

        var fresh = samples
            .Where(s => (now - s.SeenAt).TotalSeconds <= MaxAgeSeconds)
            .Select(s => s.Text)
            .ToList();

        return fresh.Count == 0 ? null : string.Join(" ", fresh);
    }
}
