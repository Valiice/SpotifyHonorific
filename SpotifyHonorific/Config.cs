using Dalamud.Configuration;
using SpotifyHonorific.Activities;
using System;
using System.Collections.Generic;

namespace SpotifyHonorific;

[Serializable]
public class Config : IPluginConfiguration
{
    public int Version { get; set; } = 0;
    public bool Enabled { get; set; } = true;

    public string SpotifyClientId { get; set; } = string.Empty;
    public string SpotifyClientSecret { get; set; } = string.Empty;
    public string SpotifyRefreshToken { get; set; } = string.Empty;
    public DateTime LastSpotifyAuthTime { get; set; } = DateTime.MinValue;

    public List<ActivityConfig> ActivityConfigs { get; set; } = [];

    public Config() { }

    public Config(List<ActivityConfig> activityConfigs)
    {
        ActivityConfigs = activityConfigs;
    }

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
