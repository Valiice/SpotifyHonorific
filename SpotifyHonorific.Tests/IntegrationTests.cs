using FluentAssertions;
using NSubstitute;
using Dalamud.Plugin;
using SpotifyHonorific;
using SpotifyHonorific.Activities;
using System.Numerics;

namespace SpotifyHonorific.Tests;

/// <summary>
/// Integration tests that verify multiple components working together
/// </summary>
public class IntegrationTests
{
    [Fact]
    public void FullWorkflow_CreateConfigWithActivity_ShouldWork()
    {
        // Arrange
        var mockInterface = Substitute.For<IDalamudPluginInterface>();
        var activityConfig = new ActivityConfig
        {
            Name = "Test Spotify",
            TypeName = "Spotify",
            TitleTemplate = "♪ {{ Activity.Name }} ♪",
            IsPrefix = false,
            RainbowMode = false,
            Color = new Vector3(1.0f, 0.5f, 0.0f)
        };

        var config = new Config
        {
            Enabled = true,
            ActiveConfigName = "Test Spotify",
            ActivityConfigs = new List<ActivityConfig> { activityConfig }
        };
        config.Initialize(mockInterface);

        // Act & Assert
        config.Enabled.Should().BeTrue();
        config.ActiveConfigName.Should().Be("Test Spotify");
        config.ActivityConfigs.Should().HaveCount(1);
        config.ActivityConfigs[0].Name.Should().Be("Test Spotify");
    }

    [Fact]
    public void MultipleConfigs_SelectActive_ShouldWork()
    {
        // Arrange
        var mockInterface = Substitute.For<IDalamudPluginInterface>();
        var configs = new List<ActivityConfig>
        {
            new() { Name = "Config 1" },
            new() { Name = "Config 2" },
            new() { Name = "Config 3" }
        };

        var config = new Config
        {
            ActivityConfigs = configs,
            ActiveConfigName = "Config 2"
        };
        config.Initialize(mockInterface);

        // Act
        var activeConfig = config.ActivityConfigs.FirstOrDefault(c => c.Name == config.ActiveConfigName);

        // Assert
        activeConfig.Should().NotBeNull();
        activeConfig!.Name.Should().Be("Config 2");
    }

    [Fact]
    public void ThreadSafeConfig_WithConcurrentActivityConfigChanges_ShouldNotCorrupt()
    {
        // Arrange
        var mockInterface = Substitute.For<IDalamudPluginInterface>();
        var config = new Config
        {
            ActivityConfigs = ActivityConfig.GetDefaults()
        };
        config.Initialize(mockInterface);

        var tasks = new List<Task>();

        // Act - Simulate concurrent modifications
        for (int i = 0; i < 50; i++)
        {
            var index = i;
            tasks.Add(Task.Run(() =>
            {
                config.WithLock(() =>
                {
                    var newConfig = new ActivityConfig { Name = $"Config {index}" };
                    config.ActivityConfigs.Add(newConfig);
                });
            }));

            tasks.Add(Task.Run(() =>
            {
                config.WithLock(() =>
                {
                    config.ActiveConfigName = $"Config {index}";
                });
            }));
        }

        Task.WaitAll(tasks.ToArray());

        // Assert
        config.ActivityConfigs.Should().HaveCountGreaterThan(1);
        config.ActiveConfigName.Should().NotBeEmpty();
    }

    [Fact]
    public void DefaultsWorkflow_RecreateDefaults_ShouldAddConfigs()
    {
        // Arrange
        var mockInterface = Substitute.For<IDalamudPluginInterface>();
        var config = new Config();
        config.Initialize(mockInterface);

        // Act
        config.ActivityConfigs.AddRange(ActivityConfig.GetDefaults());

        if (string.IsNullOrEmpty(config.ActiveConfigName) && config.ActivityConfigs.Count > 0)
        {
            config.ActiveConfigName = config.ActivityConfigs[0].Name;
        }

        // Assert
        config.ActivityConfigs.Should().NotBeEmpty();
        config.ActiveConfigName.Should().NotBeEmpty();
        config.ActiveConfigName.Should().Be(config.ActivityConfigs[0].Name);
    }

    [Fact]
    public void ConfigCloning_AllActivityConfigs_ShouldBeIndependent()
    {
        // Arrange
        var original = ActivityConfig.GetDefaults();
        var originalCount = original.Count;
        var originalFirstName = original[0].Name;

        // Act - Clone and modify
        var cloned = new List<ActivityConfig>();
        foreach (var config in original)
        {
            cloned.Add(config.Clone());
        }

        cloned[0].Name = "MODIFIED";
        cloned.Add(new ActivityConfig { Name = "NEW" });

        // Assert
        original.Should().HaveCount(originalCount);
        original[0].Name.Should().Be(originalFirstName);
        cloned.Should().HaveCount(originalCount + 1);
        cloned[0].Name.Should().Be("MODIFIED");
    }
}

/// <summary>
/// Performance benchmark tests
/// </summary>
public class PerformanceBenchmarkTests
{
    [Fact]
    public void Benchmark_ConfigLocking_ShouldBeEfficient()
    {
        // Arrange
        var mockInterface = Substitute.For<IDalamudPluginInterface>();
        var config = new Config();
        config.Initialize(mockInterface);
        var iterations = 10000;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Act
        for (int i = 0; i < iterations; i++)
        {
            config.WithLock(() => config.Enabled);
        }

        stopwatch.Stop();

        // Assert - 10k lock operations should complete in under 100ms
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(100);
    }

    [Fact]
    public void Benchmark_ActivityConfigCloning_ShouldBeEfficient()
    {
        // Arrange
        var original = new ActivityConfig
        {
            Name = "Test",
            TitleTemplate = "Long template string with {{ variables }}",
            FilterTemplate = "{{ true }}",
            Color = new Vector3(1, 0, 0)
        };

        var iterations = 10000;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Act
        for (int i = 0; i < iterations; i++)
        {
            var clone = original.Clone();
        }

        stopwatch.Stop();

        // Assert - 10k clones should complete in under 50ms
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(50);
    }

    [Fact]
    public void Benchmark_GetDefaults_ShouldBeEfficient()
    {
        // Arrange
        var iterations = 1000;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Act
        for (int i = 0; i < iterations; i++)
        {
            var defaults = ActivityConfig.GetDefaults();
        }

        stopwatch.Stop();

        // Assert - 1k default generations should complete in under 50ms
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(50);
    }
}

/// <summary>
/// Edge case and boundary tests
/// </summary>
public class EdgeCaseTests
{
    [Fact]
    public void EmptyConfig_ShouldHandleGracefully()
    {
        // Arrange
        var mockInterface = Substitute.For<IDalamudPluginInterface>();

        // Act
        var config = new Config();
        config.Initialize(mockInterface);

        // Assert
        config.ActivityConfigs.Should().BeEmpty();
        config.ActiveConfigName.Should().BeEmpty();
    }

    [Fact]
    public void ConfigWithNullValues_ShouldHandleGracefully()
    {
        // Arrange
        var activityConfig = new ActivityConfig
        {
            Color = null,
            Glow = null
        };

        // Act & Assert
        activityConfig.Color.Should().BeNull();
        activityConfig.Glow.Should().BeNull();
    }

    [Fact]
    public void VeryLongConfigName_ShouldBeAccepted()
    {
        // Arrange
        var longName = new string('X', 1000);
        var config = new ActivityConfig { Name = longName };

        // Act & Assert
        config.Name.Should().Be(longName);
        config.Name.Length.Should().Be(1000);
    }

    [Fact]
    public void SpecialCharactersInName_ShouldBeAccepted()
    {
        // Arrange
        var specialName = "Config!@#$%^&*()_+-=[]{}|;':\"<>?,./ 你好";
        var config = new ActivityConfig { Name = specialName };

        // Act & Assert
        config.Name.Should().Be(specialName);
    }

    [Fact]
    public void ConcurrentConfigDeletion_ShouldNotThrow()
    {
        // Arrange
        var mockInterface = Substitute.For<IDalamudPluginInterface>();
        var config = new Config();
        config.Initialize(mockInterface);
        for (int i = 0; i < 100; i++)
        {
            config.ActivityConfigs.Add(new ActivityConfig { Name = $"Config {i}" });
        }

        var tasks = new List<Task>();

        // Act - Concurrent deletions
        for (int i = 0; i < 50; i++)
        {
            var index = i;
            tasks.Add(Task.Run(() =>
            {
                config.WithLock(() =>
                {
                    if (config.ActivityConfigs.Count > index)
                    {
                        config.ActivityConfigs.RemoveAt(0);
                    }
                });
            }));
        }

        // Assert - Should not throw
        var action = () => Task.WaitAll(tasks.ToArray());
        action.Should().NotThrow();
    }
}
