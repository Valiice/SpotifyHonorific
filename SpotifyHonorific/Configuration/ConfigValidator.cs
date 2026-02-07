using Scriban;
using SpotifyHonorific.Activities;
using SpotifyHonorific.Utils;
using System.Collections.Generic;

namespace SpotifyHonorific.Configuration;

/// <summary>
/// Validates plugin configuration and activity configs.
/// Separated from Config class for better testability and SoC.
/// </summary>
public static class ConfigValidator
{
    /// <summary>
    /// Validates the entire plugin configuration.
    /// </summary>
    public static bool Validate(Config config, out List<string> errors)
    {
        errors = [];

        if (config.Enabled && string.IsNullOrWhiteSpace(config.SpotifyRefreshToken))
        {
            errors.Add("Spotify authentication required when plugin is enabled. Please authenticate with Spotify in the config.");
        }

        if (config.Enabled && string.IsNullOrWhiteSpace(config.SpotifyClientId))
        {
            errors.Add("Spotify Client ID is required. Please set up your Spotify app credentials.");
        }

        if (config.Enabled && string.IsNullOrWhiteSpace(config.SpotifyClientSecret))
        {
            errors.Add("Spotify Client Secret is required. Please set up your Spotify app credentials.");
        }

        if (config.Enabled && !string.IsNullOrWhiteSpace(config.ActiveConfigName))
        {
            if (!ValidationHelper.ConfigExists(config.ActivityConfigs, config.ActiveConfigName) && config.ActivityConfigs.Count > 0)
            {
                errors.Add($"Active config '{config.ActiveConfigName}' not found. Please select a valid config.");
            }
        }

        foreach (var activityConfig in config.ActivityConfigs)
        {
            ValidateActivityConfig(activityConfig, errors);
        }

        return errors.Count == 0;
    }

    /// <summary>
    /// Validates a single activity configuration.
    /// </summary>
    public static void ValidateActivityConfig(ActivityConfig config, List<string> errors)
    {
        var prefix = string.IsNullOrWhiteSpace(config.Name) ? "Unnamed config" : $"'{config.Name}'";

        ValidateTitleTemplate(config, prefix, errors);
        ValidateFilterTemplate(config, prefix, errors);
        ValidateColors(config, prefix, errors);
    }

    private static void ValidateTitleTemplate(ActivityConfig config, string prefix, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(config.TitleTemplate))
        {
            errors.Add($"{prefix}: Title template is empty.");
            return;
        }

        var titleTemplate = Template.Parse(config.TitleTemplate);
        if (titleTemplate.HasErrors)
        {
            var templateErrors = TemplateHelper.GetTemplateErrors(titleTemplate);
            errors.Add($"{prefix}: Invalid title template syntax - {templateErrors}");
            return;
        }

        var hasTruncate = config.TitleTemplate.Contains("truncate", System.StringComparison.OrdinalIgnoreCase);
        if (!hasTruncate && config.TitleTemplate.Length > 100)
        {
            errors.Add($"{prefix}: Title template is very long ({config.TitleTemplate.Length} chars) and doesn't use 'truncate' filter. Rendered output may exceed 32 character limit.");
        }
    }

    private static void ValidateFilterTemplate(ActivityConfig config, string prefix, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(config.FilterTemplate))
        {
            return;
        }

        var filterTemplate = Template.Parse(config.FilterTemplate);
        if (filterTemplate.HasErrors)
        {
            var templateErrors = TemplateHelper.GetTemplateErrors(filterTemplate);
            errors.Add($"{prefix}: Invalid filter template syntax - {templateErrors}");
        }
    }

    private static void ValidateColors(ActivityConfig config, string prefix, List<string> errors)
    {
        if (config.Color.HasValue && !ValidationHelper.IsValidNormalizedRgb(config.Color.Value))
        {
            errors.Add($"{prefix}: Color values must be between 0 and 1 (RGB normalized).");
        }

        if (config.Glow.HasValue && !ValidationHelper.IsValidNormalizedRgb(config.Glow.Value))
        {
            errors.Add($"{prefix}: Glow values must be between 0 and 1 (RGB normalized).");
        }
    }
}
