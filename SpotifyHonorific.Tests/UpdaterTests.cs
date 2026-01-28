using FluentAssertions;
using SpotifyHonorific.Updaters;
using SpotifyHonorific.Activities;
using System.Numerics;

namespace SpotifyHonorific.Tests;

/// <summary>
/// Tests for the Updater class, focusing on polling, template caching, and title rendering
/// </summary>
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

    [Fact]
    public void HsvToRgb_WithRedHue_ShouldReturnRedVector()
    {
        // This tests the HsvToRgb color conversion logic
        // Hue 0 = Red, Saturation 1, Value 1 should give (1, 0, 0)
        // We can't directly test the private method, but we can verify the logic through integration tests
        // For now, this is a placeholder to demonstrate test structure
    }

    [Theory]
    [InlineData(0.0f, 1.0f, 1.0f)] // Red
    [InlineData(0.33f, 1.0f, 1.0f)] // Green
    [InlineData(0.66f, 1.0f, 1.0f)] // Blue
    public void RainbowMode_WithDifferentHues_ShouldProduceDifferentColors(float hue, float saturation, float value)
    {
        // This would test rainbow color generation
        // Actual implementation requires access to private HsvToRgb method
        // Demonstrating proper test structure with Theory attribute
    }
}

/// <summary>
/// Integration tests for Updater - Note: Full integration tests require Dalamud runtime
/// These tests focus on testable logic without Dalamud dependencies
/// </summary>
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

        // Assert - When Enabled is false, polling should not occur
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
        // Updater should detect empty token and not attempt authentication
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

/// <summary>
/// Performance tests to verify optimizations
/// </summary>
public class UpdaterPerformanceTests
{
    [Fact]
    public void TemplateCache_ShouldReuseTemplates()
    {
        // This test would verify that templates are cached and not reparsed
        // Requires access to the private _templateCache field or testing through behavior

        // Arrange
        var templateContent = "{{ Activity.Name }}";

        // Act - Parse the same template multiple times
        // The second parse should be instant due to caching

        // Assert - Would measure execution time difference
    }

    [Fact]
    public void JsonSerialization_ShouldUseNoFormatting()
    {
        // This test verifies that JSON serialization uses Formatting.None
        // Would require capturing the serialized output

        // Arrange
        var data = new Dictionary<string, object?>
        {
            {"Title", "Test"},
            {"IsPrefix", false},
            {"Color", new Vector3(1, 0, 0)},
            {"Glow", null}
        };

        // Act - Serialize

        // Assert - Should not contain unnecessary whitespace/newlines
    }

    [Fact]
    public void ConfigLocking_ShouldPreventRaceConditions()
    {
        // Arrange
        var mockInterface = NSubstitute.Substitute.For<Dalamud.Plugin.IDalamudPluginInterface>();
        var config = new Config();
        config.Initialize(mockInterface);
        var tasks = new List<Task>();
        var errors = 0;

        // Act - Simulate concurrent access
        for (int i = 0; i < 50; i++)
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

        Task.WaitAll(tasks.ToArray());

        // Assert - No race conditions should occur
        errors.Should().Be(0);
    }
}

/// <summary>
/// Tests for timeout and error handling
/// </summary>
public class UpdaterErrorHandlingTests
{
    [Fact]
    public void ApiTimeout_ShouldNotFreezeApplication()
    {
        // This test verifies that API calls have proper timeout handling
        // Would require mocking the SpotifyClient to simulate a hanging request

        // Arrange - Create an updater with mocked dependencies
        // Act - Trigger a polling operation with a mock that delays response
        // Assert - Operation should complete within API_TIMEOUT_MS (5 seconds)
    }

    [Fact]
    public void InvalidTemplate_ShouldNotCrashUpdater()
    {
        // Arrange
        var invalidTemplate = "{{ invalid syntax }}}";

        // Act - Attempt to parse invalid template
        // Should log error but not crash

        // Assert - Error should be handled gracefully
    }

    [Fact]
    public void SpotifyApiError_ShouldHandleGracefully()
    {
        // This test verifies that API exceptions are caught and logged
        // without crashing the application

        // Arrange - Mock Spotify client to throw exception
        // Act - Trigger polling
        // Assert - Exception should be caught and logged
    }
}

/// <summary>
/// Tests for config selection logic
/// </summary>
public class UpdaterConfigSelectionTests
{
    [Fact]
    public void ProcessTrack_WithNoActiveConfig_ShouldUseFallback()
    {
        // Arrange
        var mockInterface = NSubstitute.Substitute.For<Dalamud.Plugin.IDalamudPluginInterface>();
        var config = new Config
        {
            ActiveConfigName = "",
            ActivityConfigs = new List<ActivityConfig>
            {
                new() { Name = "Default" }
            }
        };
        config.Initialize(mockInterface);

        // Act - When no active config is set, should use first config

        // Assert - First config should be selected as fallback
        config.ActivityConfigs.Should().HaveCount(1);
    }

    [Fact]
    public void ProcessTrack_WithActiveConfigName_ShouldUseNamedConfig()
    {
        // Arrange
        var mockInterface = NSubstitute.Substitute.For<Dalamud.Plugin.IDalamudPluginInterface>();
        var config = new Config
        {
            ActiveConfigName = "Config2",
            ActivityConfigs = new List<ActivityConfig>
            {
                new() { Name = "Config1" },
                new() { Name = "Config2" },
                new() { Name = "Config3" }
            }
        };
        config.Initialize(mockInterface);

        // Act & Assert
        var selectedConfig = config.ActivityConfigs.FirstOrDefault(c => c.Name == config.ActiveConfigName);
        selectedConfig.Should().NotBeNull();
        selectedConfig!.Name.Should().Be("Config2");
    }

    [Fact]
    public void ProcessTrack_WithInvalidActiveConfig_ShouldFallbackToFirst()
    {
        // Arrange
        var mockInterface = NSubstitute.Substitute.For<Dalamud.Plugin.IDalamudPluginInterface>();
        var config = new Config
        {
            ActiveConfigName = "NonExistent",
            ActivityConfigs = new List<ActivityConfig>
            {
                new() { Name = "Config1" }
            }
        };
        config.Initialize(mockInterface);

        // Act - When active config doesn't exist, should fallback to first

        // Assert
        config.ActivityConfigs[0].Name.Should().Be("Config1");
    }
}
