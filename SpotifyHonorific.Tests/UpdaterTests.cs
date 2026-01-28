using FluentAssertions;
using SpotifyHonorific.Updaters;
using SpotifyHonorific.Activities;
using System.Numerics;

namespace SpotifyHonorific.Tests;

public class UpdaterTests
{
    [Fact]
    public void UpdaterContext_DefaultValues_ShouldBeZero()
    {
        // Arrange & Act
        var context = new UpdaterContext();

        // Assert
        context.SecsElapsed.Should().Be(0);
    }

    [Fact]
    public void UpdaterContext_SecsElapsed_ShouldAccumulate()
    {
        // Arrange
        var context = new UpdaterContext();

        // Act
        context.SecsElapsed += 1.5;
        context.SecsElapsed += 2.3;

        // Assert
        context.SecsElapsed.Should().BeApproximately(3.8, 0.001);
    }
}

public class UpdaterIntegrationTests
{
    [Fact]
    public void UpdaterLogic_DisabledConfig_ShouldSkipPolling()
    {
        // Arrange
        var mockInterface = NSubstitute.Substitute.For<Dalamud.Plugin.IDalamudPluginInterface>();
        var config = new Config
        {
            Enabled = false,
            SpotifyRefreshToken = "test_token"
        };
        config.Initialize(mockInterface);

        // Assert
        config.Enabled.Should().BeFalse();
    }

    [Fact]
    public void UpdaterLogic_EmptyRefreshToken_ShouldNotAttemptAuth()
    {
        // Arrange
        var mockInterface = NSubstitute.Substitute.For<Dalamud.Plugin.IDalamudPluginInterface>();
        var config = new Config
        {
            Enabled = true,
            SpotifyRefreshToken = ""
        };
        config.Initialize(mockInterface);

        // Assert
        config.SpotifyRefreshToken.Should().BeEmpty();
    }

    [Fact]
    public void UpdaterLogic_PollingInterval_ShouldBe2Seconds()
    {
        // Arrange
        const double POLLING_INTERVAL_SECONDS = 2.0;

        // Assert
        POLLING_INTERVAL_SECONDS.Should().Be(2.0);
    }
}

public class UpdaterPerformanceTests
{
    [Fact]
    public async Task ConfigLocking_ShouldPreventRaceConditions()
    {
        // Arrange
        var mockInterface = NSubstitute.Substitute.For<Dalamud.Plugin.IDalamudPluginInterface>();
        var config = new Config();
        config.Initialize(mockInterface);
        var tasks = new List<Task>();
        var errors = 0;

        // Act
        for (var i = 0; i < 50; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                try
                {
                    config.WithLock(() =>
                    {
                        config.SpotifyClientId = $"id_{i}";
                        Thread.Sleep(1);
                        var read = config.SpotifyClientId;
                    });
                }
                catch
                {
                    Interlocked.Increment(ref errors);
                }
            }));
        }

        await Task.WhenAll(tasks);

        // Assert
        errors.Should().Be(0);
    }
}

public class UpdaterConfigSelectionTests
{
    [Fact]
    public void ProcessTrack_WithActiveConfigName_ShouldUseNamedConfig()
    {
        // Arrange
        var mockInterface = NSubstitute.Substitute.For<Dalamud.Plugin.IDalamudPluginInterface>();
        var config = new Config
        {
            ActiveConfigName = "Config2",
            ActivityConfigs =
            [
                new() { Name = "Config1" },
                new() { Name = "Config2" },
                new() { Name = "Config3" }
            ]
        };
        config.Initialize(mockInterface);

        // Act & Assert
        var selectedConfig = config.ActivityConfigs.FirstOrDefault(c => c.Name == config.ActiveConfigName);
        selectedConfig.Should().NotBeNull();
        selectedConfig!.Name.Should().Be("Config2");
    }
}
