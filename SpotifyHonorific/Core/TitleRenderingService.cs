using Dalamud.Plugin.Services;
using Newtonsoft.Json;
using SpotifyAPI.Web;
using SpotifyHonorific.Activities;
using SpotifyHonorific.Updaters;
using System.Collections.Generic;
using System.Numerics;

namespace SpotifyHonorific.Core;

/// <summary>
/// Handles rendering of title templates and preparation of IPC data.
/// </summary>
public class TitleRenderingService
{
    private const ushort MAX_TITLE_LENGTH = 32;
    private const float RAINBOW_HUE_SPEED = 0.5f;

    private readonly TemplateCache _templateCache;
    private readonly IPluginLog _pluginLog;
    private readonly IChatGui _chatGui;

    private bool _displayedMaxLengthError;

    public TitleRenderingService(TemplateCache templateCache, IPluginLog pluginLog, IChatGui chatGui)
    {
        _templateCache = templateCache;
        _pluginLog = pluginLog;
        _chatGui = chatGui;
    }

    /// <summary>
    /// Renders a title from an activity config and track.
    /// Returns null if rendering fails or title exceeds max length.
    /// </summary>
    public string? RenderTitle(ActivityConfig activityConfig, FullTrack track, UpdaterContext context)
    {
        var template = _templateCache.GetOrCreate(activityConfig.TitleTemplate, out var errorMessage);

        if (template == null)
        {
            _chatGui.PrintError($"SpotifyHonorific: {errorMessage}");
            return null;
        }

        var title = template.Render(new { Activity = track, Context = context }, member => member.Name);

        if (title.Length > MAX_TITLE_LENGTH)
        {
            if (!_displayedMaxLengthError)
            {
                var message = $"Title '{title}' is longer than {MAX_TITLE_LENGTH} characters, it won't be applied by honorific. Trim whitespaces or truncate variables to reduce the length.";
                _pluginLog.Error(message);
                _chatGui.PrintError(message, "DiscordActivityHonorific");
                _displayedMaxLengthError = true;
            }
            return null;
        }

        _displayedMaxLengthError = false;
        return title;
    }

    /// <summary>
    /// Creates serialized JSON data for the Honorific IPC call.
    /// </summary>
    public string SerializeTitleData(string title, ActivityConfig activityConfig, UpdaterContext context)
    {
        var colorToUse = activityConfig.Color;

        if (activityConfig.RainbowMode)
        {
            var hue = (float)(context.SecsElapsed * RAINBOW_HUE_SPEED) % 1.0f;
            colorToUse = HsvToRgb(hue, 1.0f, 1.0f);
        }

        var data = new Dictionary<string, object?>(4)
        {
            { "Title", title },
            { "IsPrefix", activityConfig.IsPrefix },
            { "Color", colorToUse },
            { "Glow", activityConfig.Glow }
        };

        return JsonConvert.SerializeObject(data, Formatting.None);
    }

    /// <summary>
    /// Converts HSV color values to RGB Vector3.
    /// </summary>
    public static Vector3 HsvToRgb(float h, float s, float v)
    {
        var i = (int)(h * 6);
        var f = (h * 6) - i;

        var p = v * (1 - s);
        var q = v * (1 - (f * s));
        var t = v * (1 - ((1 - f) * s));

        return (i % 6) switch
        {
            0 => new(v, t, p),
            1 => new(q, v, p),
            2 => new(p, v, t),
            3 => new(p, q, v),
            4 => new(t, p, v),
            _ => new(v, p, q)
        };
    }
}
