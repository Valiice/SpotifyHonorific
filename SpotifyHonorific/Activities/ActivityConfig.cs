using Newtonsoft.Json;
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
        },
        new() {
            Name = "Spotify Simple",
            TypeName = "Spotify",
            FilterTemplate = """
{{ true }}
""",
            TitleTemplate = """
♪{{ Activity.Name | string.truncate 28 }}♪
"""
        }
    ];

    public string Name { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
    public string FilterTemplate { get; set; } = string.Empty;
    public string TitleTemplate { get; set; } = string.Empty;
    public bool IsPrefix { get; set; }
    public bool RainbowMode { get; set; }
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

    public string ExportToJson()
    {
        return JsonConvert.SerializeObject(this, Formatting.Indented);
    }

    public static bool TryImportFromJson(string json, out ActivityConfig? config, out string? error)
    {
        config = null;
        error = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            error = "JSON string is empty.";
            return false;
        }

        try
        {
            config = JsonConvert.DeserializeObject<ActivityConfig>(json);
            if (config == null)
            {
                error = "Failed to deserialize JSON.";
                return false;
            }

            return true;
        }
        catch (JsonException ex)
        {
            error = $"Invalid JSON format: {ex.Message}";
            return false;
        }
        catch (Exception ex)
        {
            error = $"Unexpected error: {ex.Message}";
            return false;
        }
    }
}
