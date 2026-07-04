using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;

namespace SpotifyHonorific.Core;

public interface IHonorificTitleReader
{
    bool TryGetTitle(int objectIndex, out string title);
}

public sealed class HonorificTitleReader : IHonorificTitleReader
{
    // Without a backoff, a missing Honorific plugin means every nearby player
    // triggers a thrown-and-caught IPC exception plus a log line every watcher
    // tick, forever — thousands per hour in a crowded zone for an operation
    // that can't succeed until the user installs Honorific and reloads.
    private const int IPC_FAILURE_BACKOFF_SECONDS = 60;

    private readonly ICallGateSubscriber<int, string> _getCharacterTitleSubscriber;
    private readonly IPluginLog _pluginLog;
    private DateTime _ipcRetryAt = DateTime.MinValue;

    public HonorificTitleReader(IDalamudPluginInterface pluginInterface, IPluginLog pluginLog)
    {
        _getCharacterTitleSubscriber = pluginInterface.GetIpcSubscriber<int, string>("Honorific.GetCharacterTitle");
        _pluginLog = pluginLog;
    }

    public bool TryGetTitle(int objectIndex, out string title)
    {
        title = string.Empty;

        if (DateTime.Now < _ipcRetryAt) return false;

        string rawJson;
        try
        {
            rawJson = _getCharacterTitleSubscriber.InvokeFunc(objectIndex);
        }
        catch (Exception e)
        {
            _ipcRetryAt = DateTime.Now.AddSeconds(IPC_FAILURE_BACKOFF_SECONDS);
            _pluginLog.Debug($"Honorific.GetCharacterTitle IPC unavailable or failed, backing off for {IPC_FAILURE_BACKOFF_SECONDS}s: {e.Message}");
            return false;
        }

        if (string.IsNullOrWhiteSpace(rawJson)) return false;

        return TryExtractCustomTitle(rawJson, out title);
    }

    // Honorific.GetCharacterTitle returns the full title-data JSON (the same
    // schema used to set a title), not plain text. IsOriginal:true means the
    // title is one of the character's real, unlocked in-game titles just
    // re-rendered by Honorific rather than custom text — those aren't song
    // titles, so they're excluded here.
    internal static bool TryExtractCustomTitle(string rawJson, out string title)
    {
        title = string.Empty;

        try
        {
            var data = JObject.Parse(rawJson);

            // ToObject<bool?> tolerates a literal JSON null (a non-null JValue
            // that ?. does not short-circuit on), which ToObject<bool> would
            // throw for — the payload comes from another plugin's IPC, so its
            // exact shape isn't under our control.
            if (data["IsOriginal"]?.ToObject<bool?>() == true) return false;

            var extracted = data["Title"]?.ToString();
            if (string.IsNullOrWhiteSpace(extracted)) return false;

            title = extracted;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
