using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Plugin.Services;
using System;

namespace SpotifyHonorific.Core;

public sealed class NearbyTrackContextMenu : IDisposable
{
    private readonly IContextMenu _contextMenu;
    private readonly IHonorificTitleReader _titleReader;
    private readonly TrackQueueService _trackQueueService;
    private readonly IPluginLog _pluginLog;

    public NearbyTrackContextMenu(IContextMenu contextMenu, IHonorificTitleReader titleReader, TrackQueueService trackQueueService, IPluginLog pluginLog)
    {
        _contextMenu = contextMenu;
        _titleReader = titleReader;
        _trackQueueService = trackQueueService;
        _pluginLog = pluginLog;

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

        args.AddMenuItem(new MenuItem
        {
            Name = "Queue their track (SpotifyHonorific)",
            OnClicked = _ => HandleQueueClicked(objectIndex)
        });
    }

    private void HandleQueueClicked(int objectIndex)
    {
        if (!_titleReader.TryGetTitle(objectIndex, out var title))
        {
            _pluginLog.Debug("No Honorific title available for context menu target.");
            return;
        }

        _ = _trackQueueService.QueueTrackFromTitleAsync(title);
    }
}
