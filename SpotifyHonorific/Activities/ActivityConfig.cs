using System;
using System.Collections.Generic;
using System.Numerics;

namespace SpotifyHonorific.Activities;

[Serializable]
public class ActivityConfig
{
    private static readonly List<ActivityConfig> DEFAULTS = [
        new() {
            Name = "Spotify",
            TypeName = "Spotify",
            FilterTemplate = """
{{ true }}
""",
            TitleTemplate = """
♪{{- if (Context.SecsElapsed % 30) < 10 -}}
    Listening to Spotify
{{- else if (Context.SecsElapsed % 30) < 20 -}}
    {{ Activity.Name | string.truncate 30 }}
{{- else -}}
    {{ Activity.Artists[0].Name | string.truncate 30 }}
{{- end -}}♪
"""
        }
    ];

    public string Name { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
    public string FilterTemplate { get; set; } = string.Empty;
    public string TitleTemplate { get; set; } = string.Empty;
    public bool IsPrefix { get; set; } = false;
    public bool RainbowMode { get; set; } = false;
    public Vector3? Color { get; set; }
    public Vector3? Glow { get; set; }

    public ActivityConfig Clone()
    {
        return (ActivityConfig)MemberwiseClone();
    }

    public static List<ActivityConfig> GetDefaults()
    {
        var result = new List<ActivityConfig>(DEFAULTS.Count);
        foreach (var config in DEFAULTS)
        {
            result.Add(config.Clone());
        }
        return result;
    }
}
