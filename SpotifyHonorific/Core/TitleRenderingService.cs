using Dalamud.Plugin.Services;
using Newtonsoft.Json;
using SpotifyAPI.Web;
using SpotifyHonorific.Activities;
using SpotifyHonorific.Gradient;
using SpotifyHonorific.Updaters;
using System.Collections.Generic;
using System.Numerics;

namespace SpotifyHonorific.Core;

public class TitleRenderingService
{
    private const ushort MAX_TITLE_LENGTH = 32;
    private const float RAINBOW_HUE_SPEED = 0.5f;

    private readonly TemplateCache _templateCache;
    private readonly IPluginLog _pluginLog;
    private readonly IChatGui _chatGui;

    private bool _warnedAboutTruncation;

    public TitleRenderingService(TemplateCache templateCache, IPluginLog pluginLog, IChatGui chatGui)
    {
        _templateCache = templateCache;
        _pluginLog = pluginLog;
        _chatGui = chatGui;
    }

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
            title = TruncateToLimit(title);
            WarnAboutTruncationOnce();
        }

        return title;
    }

    // Honorific rejects titles over the limit outright. Dropping the render
    // used to leave the previous song's title on screen with no visible
    // update, which read as the plugin being broken. Cutting the tail keeps
    // the title moving; the one-time notice tells the user how to control
    // where the cut lands.
    private static string TruncateToLimit(string title) => title[..MAX_TITLE_LENGTH].TrimEnd();

    private void WarnAboutTruncationOnce()
    {
        if (_warnedAboutTruncation) return;
        _warnedAboutTruncation = true;

        var message = $"A rendered title exceeded {MAX_TITLE_LENGTH} characters and was cut short. Use 'string.truncate' in your template to choose where it is trimmed.";
        _pluginLog.Warning(message);
        _chatGui.Print(message, "SpotifyHonorific");
    }

    public string SerializeTitleData(string title, ActivityConfig activityConfig, UpdaterContext context, bool isHonorificSupporter)
    {
        var colorToUse = activityConfig.Color;

        if (activityConfig.RainbowMode)
        {
            var hue = (float)(context.SecsElapsed * RAINBOW_HUE_SPEED) % 1.0f;
            colorToUse = HsvToRgb(hue, 1.0f, 1.0f);
        }

        var data = new Dictionary<string, object?>(6)
        {
            { "Title", title },
            { "IsPrefix", activityConfig.IsPrefix },
            { "Color", colorToUse },
        };

        var hasGradient = isHonorificSupporter && activityConfig.GradientColourSet != null;
        if (hasGradient)
        {
            data["GradientColourSet"] = activityConfig.GradientColourSet;
            data["GradientAnimationStyle"] = (int?)activityConfig.GradientAnimationStyle;
            if (activityConfig.GradientColourSet == -1)
            {
                data["Glow"] = activityConfig.Glow;
                data["Color3"] = activityConfig.Color3;
            }
        }
        else
        {
            data["Glow"] = activityConfig.Glow;
        }

        return JsonConvert.SerializeObject(data, Formatting.None);
    }

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
