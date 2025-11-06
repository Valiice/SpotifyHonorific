using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace SpotifyHonorific.Activities;

[Serializable]
public class ActivityConfig
{
    public static readonly int DEFAULT_VERSION = 3;
    private static readonly List<ActivityConfig> DEFAULTS = [
        new() {
            Name = $"Spotify (V{DEFAULT_VERSION})",
            Priority = 1,
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
    public bool Enabled { get; set; } = true;
    public int Priority { get; set; } = 0;
    public string TypeName { get; set; } = string.Empty;
    public string FilterTemplate { get; set; } = string.Empty;
    public string TitleTemplate { get; set; } = string.Empty;
    public bool IsPrefix { get; set; } = false;
    public Vector3? Color { get; set; }
    public Vector3? Glow { get; set; }

    public ActivityConfig Clone()
    {
        return (ActivityConfig)MemberwiseClone();
    }

    public static List<ActivityConfig> GetDefaults()
    {
        return DEFAULTS.Select(c => c.Clone()).ToList();
    }
}
