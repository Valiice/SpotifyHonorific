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
    // Phases of the default template are ~10s apart within one ~30s cycle;
    // two samples further apart than this likely straddle a song change.
    private const double SameCycleWindowSeconds = 15.0;

    private sealed class Sample
    {
        public required string Text { get; set; }
        public required DateTime SeenAt { get; set; }
    }

    private readonly Dictionary<string, List<Sample>> _samplesByCharacter = new();
    private readonly HashSet<string> _knownSpotifyListeners = new();

    public void Record(string characterName, string cleanedTitle, DateTime seenAt)
    {
        if (string.IsNullOrWhiteSpace(cleanedTitle)) return;

        if (SpotifyPlaceholderDetector.IsPlaceholder(cleanedTitle))
        {
            // The placeholder phase carries no song info, but seeing it at all
            // proves this character runs SpotifyHonorific — remember that so
            // the window's song filter can pass them regardless of what
            // symbols their template uses.
            _knownSpotifyListeners.Add(characterName);
            return;
        }

        if (SpotifyPlaceholderDetector.IsNoInfoPhase(cleanedTitle)) return;

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
            // Evict by SeenAt, not list position: re-seeing an existing sample
            // refreshes its timestamp in place without moving it, so the list
            // head is not necessarily the oldest sample.
            samples.Remove(samples.MinBy(s => s.SeenAt)!);
        }
    }

    public bool IsKnownSpotifyListener(string characterName) => _knownSpotifyListeners.Contains(characterName);

    // The individual fresh sample texts, oldest first — used as match hints
    // so the queue action can prefer a search result whose track name equals
    // something we actually saw in the player's title.
    public IReadOnlyList<string> GetFreshSamples(string characterName, DateTime now)
    {
        if (!_samplesByCharacter.TryGetValue(characterName, out var samples)) return [];

        return samples
            .Where(s => (now - s.SeenAt).TotalSeconds <= MaxAgeSeconds)
            .OrderBy(s => s.SeenAt)
            .Select(s => s.Text)
            .ToList();
    }

    public string? BuildSearchQuery(string characterName, DateTime now)
    {
        if (!_samplesByCharacter.TryGetValue(characterName, out var samples)) return null;

        var fresh = samples
            .Where(s => (now - s.SeenAt).TotalSeconds <= MaxAgeSeconds)
            .OrderBy(s => s.SeenAt)
            .ToList();

        if (fresh.Count == 0) return null;

        var newest = fresh[^1];

        // A sample already containing a "Track - Artist" pattern is complete
        // on its own; appending an older sample only risks polluting the
        // query with text left over from a previous song.
        if (SongTitleHeuristic.HasTrackArtistPattern(newest.Text)) return newest.Text;

        if (fresh.Count == 1) return newest.Text;

        var previous = fresh[^2];
        if ((newest.SeenAt - previous.SeenAt).TotalSeconds > SameCycleWindowSeconds) return newest.Text;

        return $"{previous.Text} {newest.Text}";
    }
}
