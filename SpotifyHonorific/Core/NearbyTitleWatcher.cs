using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin.Services;
using SpotifyHonorific.Utils;
using System;
using System.Collections.Generic;

namespace SpotifyHonorific.Core;

public sealed class NearbyTitleWatcher
{
    private const double TickIntervalSeconds = 3.0;

    private readonly IObjectTable _objectTable;
    private readonly IHonorificTitleReader _titleReader;
    private readonly RecentTitleCache _recentTitleCache;
    private readonly NearbyTitleHistory _history = new();

    private double _timer;

    public NearbyTitleWatcher(IObjectTable objectTable, IHonorificTitleReader titleReader, RecentTitleCache recentTitleCache)
    {
        _objectTable = objectTable;
        _titleReader = titleReader;
        _recentTitleCache = recentTitleCache;
    }

    public IReadOnlyList<NearbyPlayerEntry> History => _history.Entries;

    public void Update(double deltaSeconds)
    {
        _timer += deltaSeconds;
        if (_timer < TickIntervalSeconds) return;
        _timer = 0;

        var now = DateTime.Now;
        // Index 0 is always the local player (see Updater.cs character index 0
        // usage for Honorific.SetCharacterTitle/ClearCharacterTitle) — skip it.
        for (var i = 1; i < _objectTable.Length; i++)
        {
            if (_objectTable[i] is not IPlayerCharacter playerCharacter) continue;
            if (!_titleReader.TryGetTitle(i, out var title)) continue;

            var characterName = playerCharacter.Name.TextValue;
            _history.Upsert(characterName, title, now);
            _recentTitleCache.RecordIfUseful(characterName, TitleTextCleaner.Clean(title), now);
        }
    }
}
