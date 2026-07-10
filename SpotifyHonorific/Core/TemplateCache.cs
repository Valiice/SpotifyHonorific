using Dalamud.Plugin.Services;
using Scriban;
using System.Collections.Generic;

namespace SpotifyHonorific.Core;

/// <summary>
/// Manages caching of compiled Scriban templates for efficient reuse.
/// </summary>
public class TemplateCache
{
    internal const int MAX_CACHED_TEMPLATES = 64;

    private readonly Dictionary<string, Template> _cache = new(16);
    private readonly IPluginLog _pluginLog;

    private int _cacheHits;
    private int _cacheMisses;

    public int CacheHits => _cacheHits;
    public int CacheMisses => _cacheMisses;
    public int CachedTemplateCount => _cache.Count;
    public double HitRate => (_cacheHits + _cacheMisses) > 0
        ? (double)_cacheHits / (_cacheHits + _cacheMisses) * 100
        : 0;

    public TemplateCache(IPluginLog pluginLog)
    {
        _pluginLog = pluginLog;
    }

    /// <summary>
    /// Gets or creates a compiled template from the cache.
    /// Returns null if the template has parsing errors.
    /// </summary>
    public Template? GetOrCreate(string templateSource, out string? errorMessage)
    {
        errorMessage = null;

        if (_cache.TryGetValue(templateSource, out var cachedTemplate))
        {
            _cacheHits++;
            return cachedTemplate;
        }

        _cacheMisses++;
        var template = Template.Parse(templateSource);

        if (template.HasErrors)
        {
            errorMessage = $"Template parsing failed: {string.Join(", ", template.Messages)}";
            _pluginLog.Error(errorMessage);
            return null;
        }

        // Template editing renders every intermediate string once; without a
        // cap the dictionary grows for the whole session. Clearing is cheap
        // and the active templates re-cache on their next render.
        if (_cache.Count >= MAX_CACHED_TEMPLATES)
        {
            _cache.Clear();
        }

        _cache[templateSource] = template;
        return template;
    }

    /// <summary>
    /// Clears all cached templates.
    /// </summary>
    public void Clear()
    {
        _cache.Clear();
        _cacheHits = 0;
        _cacheMisses = 0;
    }
}
