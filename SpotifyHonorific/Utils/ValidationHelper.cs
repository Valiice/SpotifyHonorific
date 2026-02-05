using SpotifyHonorific.Activities;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace SpotifyHonorific.Utils;

public static class ValidationHelper
{
    /// <summary>
    /// Validates that a Vector3 contains normalized RGB values (0-1 range).
    /// </summary>
    public static bool IsValidNormalizedRgb(Vector3 color)
        => color.X >= 0 && color.X <= 1 &&
           color.Y >= 0 && color.Y <= 1 &&
           color.Z >= 0 && color.Z <= 1;

    /// <summary>
    /// Finds an ActivityConfig by name, or returns the first config if not found.
    /// </summary>
    public static ActivityConfig? FindActiveConfig(IReadOnlyList<ActivityConfig> configs, string? activeConfigName)
    {
        if (configs.Count == 0)
            return null;

        if (string.IsNullOrEmpty(activeConfigName))
            return configs[0];

        return configs.FirstOrDefault(c => c.Name == activeConfigName) ?? configs[0];
    }

    /// <summary>
    /// Finds the index of an ActivityConfig by name, or returns 0 if not found.
    /// </summary>
    public static int FindActiveConfigIndex(IReadOnlyList<ActivityConfig> configs, string? activeConfigName)
    {
        if (configs.Count == 0 || string.IsNullOrEmpty(activeConfigName))
            return 0;

        for (var i = 0; i < configs.Count; i++)
        {
            if (configs[i].Name == activeConfigName)
                return i;
        }

        return 0;
    }

    /// <summary>
    /// Checks if a config with the given name exists in the list.
    /// </summary>
    public static bool ConfigExists(IReadOnlyList<ActivityConfig> configs, string? configName)
        => !string.IsNullOrEmpty(configName) && configs.Any(c => c.Name == configName);
}
