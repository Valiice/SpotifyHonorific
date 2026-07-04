using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Plugin.Services;
using SpotifyHonorific.Utils;
using System;

namespace SpotifyHonorific.Core;

public sealed class NearbyTrackContextMenu : IDisposable
{
    private readonly IContextMenu _contextMenu;
    private readonly IHonorificTitleReader _titleReader;
    private readonly RecentTitleCache _recentTitleCache;
    private readonly TrackQueueService _trackQueueService;
    private readonly IPluginLog _pluginLog;
    private readonly IChatGui _chatGui;

    public NearbyTrackContextMenu(IContextMenu contextMenu, IHonorificTitleReader titleReader, RecentTitleCache recentTitleCache, TrackQueueService trackQueueService, IPluginLog pluginLog, IChatGui chatGui)
    {
        _contextMenu = contextMenu;
        _titleReader = titleReader;
        _recentTitleCache = recentTitleCache;
        _trackQueueService = trackQueueService;
        _pluginLog = pluginLog;
        _chatGui = chatGui;

        _contextMenu.OnMenuOpened += OnMenuOpened;
    }

    public void Dispose()
    {
        _contextMenu.OnMenuOpened -= OnMenuOpened;
    }

    private void OnMenuOpened(IMenuOpenedArgs args)
    {
        if (args.MenuType != ContextMenuType.Default) return;
        if (args.Target is not MenuTargetDefault target) return;
        if (target.TargetObject is not IPlayerCharacter playerCharacter) return;

        var objectIndex = playerCharacter.ObjectIndex;
        if (!_titleReader.TryGetTitle(objectIndex, out _)) return;

        var characterName = playerCharacter.Name.TextValue;
        args.AddMenuItem(new MenuItem
        {
            Name = "Queue their track (SpotifyHonorific)",
            OnClicked = _ => HandleQueueClicked(objectIndex, characterName)
        });
    }

    private void HandleQueueClicked(int objectIndex, string characterName)
    {
        // Prefer the cache of recent non-placeholder samples over a single
        // fresh read — the title may be mid-cycle showing "Listening to
        // Spotify" or just one of track/artist at the exact click moment,
        // while the cache can combine both phases into a better query.
        var query = _recentTitleCache.BuildSearchQuery(characterName, DateTime.Now);
        if (query != null)
        {
            _ = _trackQueueService.QueueTrackFromTitleAsync(query);
            return;
        }

        if (_titleReader.TryGetTitle(objectIndex, out var title))
        {
            var cleaned = TitleTextCleaner.Clean(title);
            if (!SpotifyPlaceholderDetector.IsNoInfoPhase(cleaned))
            {
                _ = _trackQueueService.QueueTrackFromTitleAsync(title);
                return;
            }
        }

        _pluginLog.Debug("No usable song title seen yet for context menu target.");
        _chatGui.Print("SpotifyHonorific: Haven't seen their song info yet — wait a few seconds and try again.");
    }
}
