using FluentAssertions;
using NSubstitute;
using Dalamud.Plugin;
using SpotifyHonorific;
using SpotifyHonorific.Activities;

namespace SpotifyHonorific.Tests;

/// <summary>
/// Tests for the Config class, focusing on thread safety and locking mechanisms
/// </summary>
public class ConfigTests
{
    [Fact]
    public void Config_WithLock_ShouldExecuteActionSafely()
    {
        // Arrange
        var mockInterface = Substitute.For<IDalamudPluginInterface>();
        var config = new Config();
        config.Initialize(mockInterface);
        var counter = 0;

        // Act
        config.WithLock(() => counter++);

        // Assert
        counter.Should().Be(1);
    }

    [Fact]
    public void Config_WithLock_ShouldReturnValue()
    {
        // Arrange
        var mockInterface = Substitute.For<IDalamudPluginInterface>();
        var config = new Config();
        config.Initialize(mockInterface);
        var testValue = 42;

        // Act
        var result = config.WithLock(() => testValue);

        // Assert
        result.Should().Be(42);
    }

    [Fact]
    public async Task Config_WithLock_ShouldHandleMultipleThreads()
    {
        // Arrange
        var mockInterface = Substitute.For<IDalamudPluginInterface>();
        var config = new Config();
        config.Initialize(mockInterface);
        var counter = 0;
        var tasks = new List<Task>();
        const int threadCount = 100;

        // Act - Simulate multiple threads accessing config
        for (var i = 0; i < threadCount; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                config.WithLock(() =>
                {
                    counter++;
                    Thread.Sleep(1);
                });
            }));
        }

        await Task.WhenAll(tasks);

        // Assert - All increments should have succeeded without race conditions
        counter.Should().Be(threadCount);
    }

    [Fact]
    public void Config_DefaultValues_ShouldBeSetCorrectly()
    {
        // Arrange
        var mockInterface = Substitute.For<IDalamudPluginInterface>();

        // Act
        var config = new Config();
        config.Initialize(mockInterface);

        // Assert
        config.Version.Should().Be(0);
        config.Enabled.Should().BeTrue();
        config.SpotifyClientId.Should().BeEmpty();
        config.SpotifyClientSecret.Should().BeEmpty();
        config.SpotifyRefreshToken.Should().BeEmpty();
        config.LastSpotifyAuthTime.Should().Be(DateTime.MinValue);
        config.EnableDebugLogging.Should().BeFalse();
        config.ActiveConfigName.Should().BeEmpty();
        config.ActivityConfigs.Should().BeEmpty();
    }

    [Fact]
    public void Config_WithActivityConfigs_ShouldInitializeCorrectly()
    {
        // Arrange
        var mockInterface = Substitute.For<IDalamudPluginInterface>();
        var activityConfigs = new List<ActivityConfig>
        {
            new() { Name = "Test Config 1" },
            new() { Name = "Test Config 2" }
        };

        // Act
        var config = new Config(activityConfigs);
        config.Initialize(mockInterface);

        // Assert
        config.ActivityConfigs.Should().HaveCount(2);
        config.ActivityConfigs[0].Name.Should().Be("Test Config 1");
        config.ActivityConfigs[1].Name.Should().Be("Test Config 2");
    }

    [Fact]
    public void Config_WithLock_ShouldAllowNestedReads()
    {
        // Arrange
        var mockInterface = Substitute.For<IDalamudPluginInterface>();
        var config = new Config();
        config.Initialize(mockInterface);
        config.SpotifyClientId = "test_id";

        // Act
        var result = config.WithLock(() =>
        {
            return config.WithLock(() => config.SpotifyClientId);
        });

        // Assert - Should not deadlock and return correct value
        result.Should().Be("test_id");
    }

    [Fact]
    public async Task Config_ConcurrentReadWrite_ShouldBeSafe()
    {
        // Arrange
        var mockInterface = Substitute.For<IDalamudPluginInterface>();
        var config = new Config();
        config.Initialize(mockInterface);
        var readValues = new List<string>();
        var writeCount = 0;

        // Act - Simulate concurrent reads and writes
        var tasks = new List<Task>();

        // Writer tasks
        for (var i = 0; i < 10; i++)
        {
            var value = $"value_{i}";
            tasks.Add(Task.Run(() =>
            {
                config.WithLock(() =>
                {
                    config.SpotifyClientId = value;
                    writeCount++;
                    Thread.Sleep(1);
                });
            }));
        }

        // Reader tasks
        for (var i = 0; i < 20; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                var value = config.WithLock(() => config.SpotifyClientId);
                lock (readValues)
                {
                    readValues.Add(value);
                }
            }));
        }

        await Task.WhenAll(tasks);

        // Assert
        writeCount.Should().Be(10);
        readValues.Should().HaveCount(20);
        readValues.Should().OnlyContain(v => string.IsNullOrEmpty(v) || v.StartsWith("value_"));
    }
}
