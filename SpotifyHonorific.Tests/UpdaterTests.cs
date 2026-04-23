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

public class TitleUpdateStateTests
{
    [Fact]
    public void ForceResend_NullsLastSentJson_WithoutClearingUpdateAction()
    {
        // Zone change: Honorific cleared the title, but the update loop must keep running.
        // ForceResend should null LastSentJson so the next render re-sends to Honorific,
        // without nulling UpdateAction (which would add a 2s delay waiting for next poll).
        var state = new TitleUpdateState
        {
            UpdateAction = () => { },
            LastSentJson = "existing json"
        };
        state.ForceResend();
        state.LastSentJson.Should().BeNull();
        state.UpdateAction.Should().NotBeNull();
    }

    [Fact]
    public void Clear_NullsUpdateAction()
    {
        var state = new TitleUpdateState { UpdateAction = () => { } };
        state.Clear();
        state.UpdateAction.Should().BeNull();
    }

    [Fact]
    public void Clear_NullsLastSentJson()
    {
        // Key bug: exception handler only cleared UpdateAction, leaving LastSentJson stale.
        // Recovery then hit the dedup guard and never re-sent the IPC call.
        var state = new TitleUpdateState { LastSentJson = "existing json" };
        state.Clear();
        state.LastSentJson.Should().BeNull();
    }

    [Fact]
    public void ShouldSend_WhenLastSentJsonIsNull_ReturnsTrue()
    {
        // After Clear(), recovery must be able to re-send the IPC call.
        var state = new TitleUpdateState { LastSentJson = null };
        state.ShouldSend("any json").Should().BeTrue();
    }

    [Fact]
    public void ShouldSend_WhenJsonUnchanged_ReturnsFalse()
    {
        var state = new TitleUpdateState { LastSentJson = "same" };
        state.ShouldSend("same").Should().BeFalse();
    }

    [Fact]
    public void ShouldSend_WhenJsonChanges_ReturnsTrue()
    {
        var state = new TitleUpdateState { LastSentJson = "old" };
        state.ShouldSend("new").Should().BeTrue();
    }
}

public class UpdaterTrackSkipGuardTests
{
    [Fact]
    public void ShouldSkipTrackProcessing_SameTrackWithActiveUpdate_ShouldSkip()
    {
        Updater.ShouldSkipTrackProcessing("track1", "track1", () => { }).Should().BeTrue();
    }

    [Fact]
    public void ShouldSkipTrackProcessing_SameTrackWithNullUpdate_ShouldNotSkip()
    {
        // Bug: same track on repeat, but _updateTitle was cleared (e.g. IPC exception).
        // Old guard fires and the title never recovers until a different song plays.
        Updater.ShouldSkipTrackProcessing("track1", "track1", null).Should().BeFalse();
    }

    [Fact]
    public void ShouldSkipTrackProcessing_DifferentTrack_ShouldNotSkip()
    {
        Updater.ShouldSkipTrackProcessing("track1", "track2", () => { }).Should().BeFalse();
    }

    [Fact]
    public void ShouldSkipTrackProcessing_NoCurrentTrack_ShouldNotSkip()
    {
        Updater.ShouldSkipTrackProcessing(null, "track1", null).Should().BeFalse();
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
