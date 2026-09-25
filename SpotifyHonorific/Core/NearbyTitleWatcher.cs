using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin.Services;
using SpotifyHonorific.Utils;
using System;
using System.Collections.Generic;

namespace SpotifyHonorific.Core;

/// <summary>
/// Polls nearby players' Honorific titles on a timer. A full pass over the
/// object table is sliced across consecutive frames (<see cref="SlotsPerFrame"/>
/// slots per <see cref="Update"/> call) so the per-frame cost stays bounded
/// regardless of zone population. The <see cref="TickIntervalSeconds"/> cadence
/// is measured from the start of one pass to the start of the next, not from
/// when the previous pass finished.
/// </summary>
public sealed class NearbyTitleWatcher
{
    private const double TickIntervalSeconds = 3.0;
    private const int SlotsPerFrame = 25;

    private readonly IObjectTable _objectTable;
    private readonly IHonorificTitleReader _titleReader;
    private readonly RecentTitleCache _recentTitleCache;
    private readonly NearbyTitleHistory _history = new();

    private double _timer;
    private int _cursor; // 0 means no pass in progress; otherwise the next slot to process
    private DateTime _passStartedAt;

    public NearbyTitleWatcher(IObjectTable objectTable, IHonorificTitleReader titleReader, RecentTitleCache recentTitleCache)
    {
        _objectTable = objectTable;
        _titleReader = titleReader;
        _recentTitleCache = recentTitleCache;
    }

    public IReadOnlyList<NearbyPlayerEntry> History => _history.Entries;

    internal RecentTitleCache RecentTitleCache => _recentTitleCache;

    public void Update(double deltaSeconds)
    {
        if (_cursor == 0)
        {
            _timer += deltaSeconds;
            if (_timer < TickIntervalSeconds) return;
            _timer = 0;
            // Index 0 is always the local player (see Updater.cs character index 0
            // usage for Honorific.SetCharacterTitle/ClearCharacterTitle), skip it.
            _cursor = 1;
            _passStartedAt = DateTime.Now;
        }

        var length = _objectTable.Length;
        var end = Math.Min(_cursor + SlotsPerFrame, length);
        for (var i = _cursor; i < end; i++)
        {
            if (_objectTable[i] is not IPlayerCharacter playerCharacter) continue;
            if (!_titleReader.TryGetTitle(i, out var title)) continue;

            var characterName = playerCharacter.Name.TextValue;
            _history.Upsert(characterName, title, _passStartedAt);
            _recentTitleCache.Record(characterName, TitleTextCleaner.Clean(title), _passStartedAt);
        }

        _cursor = end >= length ? 0 : end;
    }
}
