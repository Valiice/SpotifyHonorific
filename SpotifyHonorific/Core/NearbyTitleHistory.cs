using System;
using System.Collections.Generic;
using System.Linq;

namespace SpotifyHonorific.Core;

public sealed class NearbyTitleHistory
{
    private const int MaxEntries = 100;

    private readonly List<NearbyPlayerEntry> _entries = new();
    private readonly Dictionary<string, NearbyPlayerEntry> _byName = new(StringComparer.Ordinal);

    public IReadOnlyList<NearbyPlayerEntry> Entries => _entries;

    public void Upsert(string characterName, string rawTitle, DateTime seenAt)
    {
        if (_byName.TryGetValue(characterName, out var existing))
        {
            existing.RawTitle = rawTitle;
            existing.LastSeen = seenAt;
            return;
        }

        if (_entries.Count >= MaxEntries)
        {
            EvictOldest();
        }

        var entry = new NearbyPlayerEntry
        {
            CharacterName = characterName,
            RawTitle = rawTitle,
            LastSeen = seenAt
        };
        _entries.Add(entry);
        _byName.Add(characterName, entry);
    }

    private void EvictOldest()
    {
        var oldest = _entries.OrderBy(e => e.LastSeen).First();
        _entries.Remove(oldest);
        _byName.Remove(oldest.CharacterName);
    }
}
