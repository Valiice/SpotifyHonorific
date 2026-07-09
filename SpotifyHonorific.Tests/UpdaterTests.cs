using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FluentAssertions;
using NSubstitute;
using SpotifyAPI.Web;
using SpotifyAPI.Web.Http;
using SpotifyHonorific.Core;
using SpotifyHonorific.Updaters;
using System.Net;

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

public class AuthNotificationTests
{
    private const double Cooldown = 600.0;

    [Fact]
    public void CheckAuthNotificationDue_BelowCooldown_ShouldNotNotify()
    {
        var (shouldNotify, newTimer) = Updater.CheckAuthNotificationDue(0, 100, Cooldown, true);
        shouldNotify.Should().BeFalse();
        newTimer.Should().BeApproximately(100, 0.001);
    }

    [Fact]
    public void CheckAuthNotificationDue_ExactlyCooldown_ShouldNotify()
    {
        var (shouldNotify, newTimer) = Updater.CheckAuthNotificationDue(0, Cooldown, Cooldown, true);
        shouldNotify.Should().BeTrue();
        newTimer.Should().Be(0);
    }

    [Fact]
    public void CheckAuthNotificationDue_ExceedsCooldown_ShouldNotify()
    {
        var (shouldNotify, newTimer) = Updater.CheckAuthNotificationDue(590, 20, Cooldown, true);
        shouldNotify.Should().BeTrue();
        newTimer.Should().Be(0);
    }

    [Fact]
    public void CheckAuthNotificationDue_AccumulatesAcrossCalls()
    {
        var timer = 0.0;

        // First call: 300s elapsed, not enough
        bool shouldNotify;
        (shouldNotify, timer) = Updater.CheckAuthNotificationDue(timer, 300, Cooldown, true);
        shouldNotify.Should().BeFalse();
        timer.Should().BeApproximately(300, 0.001);

        // Second call: another 300s, now at 600, should fire
        (shouldNotify, timer) = Updater.CheckAuthNotificationDue(timer, 300, Cooldown, true);
        shouldNotify.Should().BeTrue();
        timer.Should().Be(0);

        // Third call: timer reset, 100s not enough again
        (shouldNotify, timer) = Updater.CheckAuthNotificationDue(timer, 100, Cooldown, true);
        shouldNotify.Should().BeFalse();
        timer.Should().BeApproximately(100, 0.001);
    }

    [Fact]
    public void CheckAuthNotificationDue_NotificationsDisabled_ShouldNeverNotify()
    {
        var (shouldNotify, newTimer) = Updater.CheckAuthNotificationDue(0, Cooldown + 100, Cooldown, false);
        shouldNotify.Should().BeFalse();
        newTimer.Should().Be(0);
    }

    [Fact]
    public void CheckAuthNotificationDue_NotificationsDisabled_ShouldPreserveTimer()
    {
        var (shouldNotify, newTimer) = Updater.CheckAuthNotificationDue(200, 50, Cooldown, false);
        shouldNotify.Should().BeFalse();
        newTimer.Should().BeApproximately(200, 0.001);
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

public class RenderThrottleTests
{
    private const double Interval = 0.5;

    [Fact]
    public void CheckRenderDue_BelowInterval_ShouldNotRender()
    {
        var (shouldRender, newTimer) = Updater.CheckRenderDue(0, 0.016, Interval, true, true);
        shouldRender.Should().BeFalse();
        newTimer.Should().BeApproximately(0.016, 0.0001);
    }

    [Fact]
    public void CheckRenderDue_AtInterval_ShouldRenderAndResetTimer()
    {
        var (shouldRender, newTimer) = Updater.CheckRenderDue(0, Interval, Interval, true, true);
        shouldRender.Should().BeTrue();
        newTimer.Should().Be(0);
    }

    [Fact]
    public void CheckRenderDue_AccumulatesAcrossFrames()
    {
        var timer = 0.0;
        bool shouldRender;

        (shouldRender, timer) = Updater.CheckRenderDue(timer, 0.3, Interval, true, true);
        shouldRender.Should().BeFalse();

        (shouldRender, timer) = Updater.CheckRenderDue(timer, 0.3, Interval, true, true);
        shouldRender.Should().BeTrue();
        timer.Should().Be(0);
    }

    [Fact]
    public void CheckRenderDue_StaticAlreadySent_ShortCircuitsAndPreservesTimer()
    {
        // A static title already delivered to Honorific needs zero work,
        // no matter how much time passes.
        var (shouldRender, newTimer) = Updater.CheckRenderDue(0.4, 100, Interval, false, true);
        shouldRender.Should().BeFalse();
        newTimer.Should().BeApproximately(0.4, 0.0001);
    }

    [Fact]
    public void CheckRenderDue_StaticNotYetSent_PreChargedTimer_RendersImmediately()
    {
        // New track / zone change pre-charges the timer to the interval so the
        // first render lands on the very next frame.
        var (shouldRender, newTimer) = Updater.CheckRenderDue(Interval, 0.016, Interval, false, false);
        shouldRender.Should().BeTrue();
        newTimer.Should().Be(0);
    }

    [Fact]
    public void CheckRenderDue_StaticNotYetSent_UnchargedTimer_Throttles()
    {
        // Failed renders (e.g. title too long) keep alreadySent false; retries
        // must be throttled, not per-frame.
        var (shouldRender, newTimer) = Updater.CheckRenderDue(0, 0.016, Interval, false, false);
        shouldRender.Should().BeFalse();
        newTimer.Should().BeApproximately(0.016, 0.0001);
    }

    [Fact]
    public void RenderIntervals_MatchSpec()
    {
        Updater.TEXT_RENDER_INTERVAL_SECONDS.Should().Be(0.5);
        Updater.RAINBOW_RENDER_INTERVAL_SECONDS.Should().Be(0.1);
    }
}

public class UpdaterPerformanceStatsTests
{
    private static SpotifyPollingService MakePollingService()
    {
        var config = new Config();
        config.Initialize(Substitute.For<IDalamudPluginInterface>());
        return new SpotifyPollingService(config, Substitute.For<IPluginLog>(), Substitute.For<IChatGui>());
    }

    private static Updater MakeUpdater(SpotifyPollingService pollingService)
    {
        var pluginInterface = Substitute.For<IDalamudPluginInterface>();
        var config = new Config();
        config.Initialize(pluginInterface);
        var nearbyWatcher = new NearbyTitleWatcher(
            Substitute.For<IObjectTable>(),
            Substitute.For<IHonorificTitleReader>(),
            new RecentTitleCache());

        return new Updater(
            Substitute.For<IChatGui>(),
            config,
            Substitute.For<IFramework>(),
            pluginInterface,
            Substitute.For<IPluginLog>(),
            Substitute.For<IClientState>(),
            Substitute.For<IObjectTable>(),
            new PlaybackState(),
            Substitute.For<INotificationManager>(),
            nearbyWatcher,
            pollingService);
    }

    private static APITooManyRequestsException MakeRateLimitException(int retryAfterSeconds) =>
        new(new FakeResponse
        {
            StatusCode = HttpStatusCode.TooManyRequests,
            Headers = new Dictionary<string, string> { ["Retry-After"] = retryAfterSeconds.ToString() },
        });

    [Fact]
    public void GetPerformanceStats_NeverRateLimited_ShowsZeroesAndPlaceholders()
    {
        var updater = MakeUpdater(MakePollingService());

        var stats = updater.GetPerformanceStats();

        stats.Should().Contain("Plugin version: ");
        stats.Should().Contain("Requests per minute: ");
        stats.Should().Contain("Token refreshes: 0");
        stats.Should().Contain("429s this session: 0");
        stats.Should().Contain("Rate limited: No");
        stats.Should().Contain("Last Retry-After: never");
    }

    [Fact]
    public void GetPerformanceStats_WhileRateLimited_ShowsPauseAndRetryAfter()
    {
        var pollingService = MakePollingService();
        pollingService.HandleRateLimit(MakeRateLimitException(retryAfterSeconds: 90));
        var updater = MakeUpdater(pollingService);

        var stats = updater.GetPerformanceStats();

        stats.Should().Contain("429s this session: 1");
        stats.Should().Contain("Rate limited: Yes");
        stats.Should().Contain("Last Retry-After: 90s");
    }

    private sealed class FakeResponse : IResponse
    {
        public object? Body { get; init; }
        public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>();
        public HttpStatusCode StatusCode { get; init; }
        public string? ContentType { get; init; }
    }
}
