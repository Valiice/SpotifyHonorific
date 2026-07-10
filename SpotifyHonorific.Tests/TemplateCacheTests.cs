using Dalamud.Plugin.Services;
using FluentAssertions;
using NSubstitute;
using SpotifyHonorific.Core;

namespace SpotifyHonorific.Tests;

public class TemplateCacheTests
{
    private static TemplateCache MakeCache() => new(Substitute.For<IPluginLog>());

    [Fact]
    public void GetOrCreate_CachesCompiledTemplates()
    {
        var cache = MakeCache();

        cache.GetOrCreate("hello {{ 1 + 1 }}", out _);
        cache.GetOrCreate("hello {{ 1 + 1 }}", out _);

        cache.CacheHits.Should().Be(1);
        cache.CacheMisses.Should().Be(1);
        cache.CachedTemplateCount.Should().Be(1);
    }

    [Fact]
    public void GetOrCreate_PastTheCap_ClearsAndKeepsWorking()
    {
        var cache = MakeCache();

        for (var i = 0; i < TemplateCache.MAX_CACHED_TEMPLATES + 10; i++)
        {
            var template = cache.GetOrCreate($"template {i} {{{{ 1 + {i} }}}}", out var error);
            template.Should().NotBeNull();
            error.Should().BeNull();
        }

        cache.CachedTemplateCount.Should().BeLessThanOrEqualTo(TemplateCache.MAX_CACHED_TEMPLATES);
        // Counters keep accumulating across the internal clear.
        cache.CacheMisses.Should().Be(TemplateCache.MAX_CACHED_TEMPLATES + 10);
    }
}
