using System;
using System.Collections.Generic;
using System.Linq;

namespace SpotifyHonorific.Core;

public sealed class NearbyTitleHistory
{
    private const int MaxEntries = 100;

    private readonly List<NearbyPlayerEntry> _entries = new();

    public IReadOnlyList<NearbyPlayerEntry> Entries => _entries;

    public void Upsert(string characterName, string rawTitle, DateTime seenAt)
    {
        var existing = _entries.Find(e => e.CharacterName == characterName);
        if (existing != null)
        {
            existing.RawTitle = rawTitle;
            existing.LastSeen = seenAt;
            return;
        }

        if (_entries.Count >= MaxEntries)
        {
            EvictOldest();
        }

        _entries.Add(new NearbyPlayerEntry
        {
            CharacterName = characterName,
            RawTitle = rawTitle,
            LastSeen = seenAt
        });
    }

    private void EvictOldest()
    {
        var oldest = _entries.OrderBy(e => e.LastSeen).First();
        _entries.Remove(oldest);
    }
}
