using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using System;

namespace SpotifyHonorific.Core;

public interface IHonorificTitleReader
{
    bool TryGetTitle(int objectIndex, out string title);
}

public sealed class HonorificTitleReader : IHonorificTitleReader
{
    private readonly ICallGateSubscriber<int, string> _getCharacterTitleSubscriber;
    private readonly IPluginLog _pluginLog;

    public HonorificTitleReader(IDalamudPluginInterface pluginInterface, IPluginLog pluginLog)
    {
        _getCharacterTitleSubscriber = pluginInterface.GetIpcSubscriber<int, string>("Honorific.GetCharacterTitle");
        _pluginLog = pluginLog;
    }

    public bool TryGetTitle(int objectIndex, out string title)
    {
        try
        {
            title = _getCharacterTitleSubscriber.InvokeFunc(objectIndex);
            return !string.IsNullOrWhiteSpace(title);
        }
        catch (Exception e)
        {
            _pluginLog.Debug($"Honorific.GetCharacterTitle IPC unavailable or failed: {e.Message}");
            title = string.Empty;
            return false;
        }
    }
}
