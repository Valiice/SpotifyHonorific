using Dalamud.Configuration;
using Dalamud.Plugin;
using Scriban;
using SpotifyHonorific.Activities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;

namespace SpotifyHonorific;

[Serializable]
public class Config : IPluginConfiguration
{
    [field: NonSerialized]
    private Lock _syncLock = new();

    [field: NonSerialized]
    private IDalamudPluginInterface? _pluginInterface;

    public int Version { get; set; }
    public bool Enabled { get; set; } = true;

    public string SpotifyClientId { get; set; } = string.Empty;
    public string SpotifyClientSecret { get; set; } = string.Empty;
    public string SpotifyRefreshToken { get; set; } = string.Empty;
    public DateTime LastSpotifyAuthTime { get; set; } = DateTime.MinValue;

    public bool EnableDebugLogging { get; set; }

    public string ActiveConfigName { get; set; } = string.Empty;
    public List<ActivityConfig> ActivityConfigs { get; set; } = [];

    public Config() { }

    public Config(List<ActivityConfig> activityConfigs)
    {
        ActivityConfigs = activityConfigs;
    }

    [OnDeserialized]
    private void OnDeserialized(StreamingContext context)
    {
        _syncLock = new Lock();
    }

    public void Initialize(IDalamudPluginInterface pluginInterface)
    {
        _pluginInterface = pluginInterface;
    }

    public void Save()
    {
        lock (_syncLock)
        {
            var interfaceToUse = _pluginInterface ?? Plugin.PluginInterface;
            interfaceToUse.SavePluginConfig(this);
        }
    }

    public T WithLock<T>(Func<T> action)
    {
        lock (_syncLock)
        {
            return action();
        }
    }

    public void WithLock(Action action)
    {
        lock (_syncLock)
        {
            action();
        }
    }

    public bool Validate(out List<string> errors)
    {
        errors = [];

        if (Enabled && string.IsNullOrWhiteSpace(SpotifyRefreshToken))
        {
            errors.Add("Spotify authentication required when plugin is enabled. Please authenticate with Spotify in the config.");
        }

        if (Enabled && string.IsNullOrWhiteSpace(SpotifyClientId))
        {
            errors.Add("Spotify Client ID is required. Please set up your Spotify app credentials.");
        }

        if (Enabled && string.IsNullOrWhiteSpace(SpotifyClientSecret))
        {
            errors.Add("Spotify Client Secret is required. Please set up your Spotify app credentials.");
        }

        if (Enabled && !string.IsNullOrWhiteSpace(ActiveConfigName))
        {
            var activeExists = false;
            foreach (var config in ActivityConfigs)
            {
                if (config.Name == ActiveConfigName)
                {
                    activeExists = true;
                    break;
                }
            }

            if (!activeExists && ActivityConfigs.Count > 0)
            {
                errors.Add($"Active config '{ActiveConfigName}' not found. Please select a valid config.");
            }
        }

        foreach (var config in ActivityConfigs)
        {
            ValidateActivityConfig(config, errors);
        }

        return errors.Count == 0;
    }

    private static void ValidateActivityConfig(ActivityConfig config, List<string> errors)
    {
        var prefix = string.IsNullOrWhiteSpace(config.Name) ? "Unnamed config" : $"'{config.Name}'";

        if (string.IsNullOrWhiteSpace(config.TitleTemplate))
        {
            errors.Add($"{prefix}: Title template is empty.");
        }
        else
        {
            var titleTemplate = Template.Parse(config.TitleTemplate);
            if (titleTemplate.HasErrors)
            {
                var errorMessages = new List<string>(titleTemplate.Messages.Count);
                foreach (var msg in titleTemplate.Messages)
                {
                    errorMessages.Add(msg.Message);
                }
                var templateErrors = string.Join("; ", errorMessages);
                errors.Add($"{prefix}: Invalid title template syntax - {templateErrors}");
            }

            var hasTruncate = config.TitleTemplate.Contains("truncate", StringComparison.OrdinalIgnoreCase);

            if (!hasTruncate && config.TitleTemplate.Length > 100)
            {
                errors.Add($"{prefix}: Title template is very long ({config.TitleTemplate.Length} chars) and doesn't use 'truncate' filter. Rendered output may exceed 32 character limit.");
            }
        }

        if (!string.IsNullOrWhiteSpace(config.FilterTemplate))
        {
            var filterTemplate = Template.Parse(config.FilterTemplate);
            if (filterTemplate.HasErrors)
            {
                var errorMessages = new List<string>(filterTemplate.Messages.Count);
                foreach (var msg in filterTemplate.Messages)
                {
                    errorMessages.Add(msg.Message);
                }
                var templateErrors = string.Join("; ", errorMessages);
                errors.Add($"{prefix}: Invalid filter template syntax - {templateErrors}");
            }
        }

        if (config.Color.HasValue)
        {
            var color = config.Color.Value;
            if (color.X < 0 || color.X > 1 || color.Y < 0 || color.Y > 1 || color.Z < 0 || color.Z > 1)
            {
                errors.Add($"{prefix}: Color values must be between 0 and 1 (RGB normalized).");
            }
        }

        if (config.Glow.HasValue)
        {
            var glow = config.Glow.Value;
            if (glow.X < 0 || glow.X > 1 || glow.Y < 0 || glow.Y > 1 || glow.Z < 0 || glow.Z > 1)
            {
                errors.Add($"{prefix}: Glow values must be between 0 and 1 (RGB normalized).");
            }
        }
    }
}