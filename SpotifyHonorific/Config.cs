using Dalamud.Configuration;
using Dalamud.Plugin;
using SpotifyHonorific.Activities;
using SpotifyHonorific.Configuration;
using System;
using System.Collections.Generic;
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
        => ConfigValidator.Validate(this, out errors);
}