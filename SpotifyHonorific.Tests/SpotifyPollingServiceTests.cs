using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FluentAssertions;
using NSubstitute;
using SpotifyAPI.Web;
using SpotifyAPI.Web.Http;
using SpotifyHonorific.Core;
using System.Net;

namespace SpotifyHonorific.Tests;

public class SpotifyPollingServiceTests
{
    private static ChatNotifier MakeChatNotifier(IChatGui chatGui)
    {
        var framework = Substitute.For<IFramework>();
        framework.RunOnFrameworkThread(Arg.Any<Action>())
            .Returns(ci => { ci.Arg<Action>()(); return Task.CompletedTask; });
        return new ChatNotifier(chatGui, framework);
    }

    private static SpotifyPollingService MakeService(out Config config, IPluginLog? pluginLog = null, IChatGui? chatGui = null)
    {
        config = new Config();
        config.Initialize(Substitute.For<IDalamudPluginInterface>());
        return new SpotifyPollingService(
            config,
            pluginLog ?? Substitute.For<IPluginLog>(),
            MakeChatNotifier(chatGui ?? Substitute.For<IChatGui>()));
    }

    private static SpotifyPollingService MakeService(IPluginLog? pluginLog = null, IChatGui? chatGui = null)
        => MakeService(out _, pluginLog, chatGui);

    private static APITooManyRequestsException MakeRateLimitException(int retryAfterSeconds) =>
        new(new FakeResponse
        {
            StatusCode = HttpStatusCode.TooManyRequests,
            Headers = new Dictionary<string, string> { ["Retry-After"] = retryAfterSeconds.ToString() },
        });

    [Fact]
    public async Task RetryAsync_RateLimited_ThrowsImmediatelyWithoutRetrying()
    {
        // Retrying a 429 in a tight loop keeps the app rate-limited forever;
        // it must surface after a single attempt, like a 401 does.
        var service = MakeService();
        var attempts = 0;

        Task<string> Operation()
        {
            attempts++;
            throw MakeRateLimitException(retryAfterSeconds: 30);
        }

        await Assert.ThrowsAsync<APITooManyRequestsException>(() => service.RetryAsync(Operation));

        attempts.Should().Be(1);
    }

    [Fact]
    public async Task RetryAsync_Timeout_ThrowsImmediatelyWithoutRetrying()
    {
        // The timeout CTS is shared across attempts, so retrying after a
        // timeout just burns the backoff delays re-throwing instantly.
        var service = MakeService();
        var attempts = 0;

        Task<string> Operation()
        {
            attempts++;
            throw new OperationCanceledException();
        }

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.RetryAsync(Operation));

        attempts.Should().Be(1);
    }

    [Fact]
    public async Task GetCurrentlyPlayingTrack_WhileRateLimited_SkipsThePollEntirely()
    {
        var pluginLog = Substitute.For<IPluginLog>();
        var service = MakeService(pluginLog);
        service.RateLimitGate.Activate(TimeSpan.FromSeconds(30), DateTime.Now);

        var result = await service.GetCurrentlyPlayingTrackAsync();

        result.Should().BeNull();
        // A skipped poll is not an error: no warning spam, no error count.
        service.ApiErrorCount.Should().Be(0);
        pluginLog.DidNotReceive().Warning(Arg.Any<string>());
    }

    [Fact]
    public void HandleRateLimit_FirstHit_AnnouncesInChatEvenWithoutDebugLogging()
    {
        var chatGui = Substitute.For<IChatGui>();
        var service = MakeService(chatGui: chatGui);

        service.HandleRateLimit(MakeRateLimitException(retryAfterSeconds: 30));

        chatGui.Received(1).PrintError(
            Arg.Is<string>(s => s.Contains("rate limit", StringComparison.OrdinalIgnoreCase)),
            Arg.Any<string?>(), Arg.Any<ushort?>());
    }

    [Fact]
    public void HandleRateLimit_RepeatedHits_AnnounceOnlyOnce()
    {
        // A long rate limit re-arms the gate every pause window; users must
        // not get a chat line per re-arm, that is the spam this fix removes.
        var chatGui = Substitute.For<IChatGui>();
        var service = MakeService(chatGui: chatGui);

        service.HandleRateLimit(MakeRateLimitException(retryAfterSeconds: 30));
        service.HandleRateLimit(MakeRateLimitException(retryAfterSeconds: 60));

        chatGui.Received(1).PrintError(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<ushort?>());
    }

    [Fact]
    public void HandleRateLimit_AfterSuccessfulPoll_AnnouncesTheNextEpisode()
    {
        var chatGui = Substitute.For<IChatGui>();
        var service = MakeService(chatGui: chatGui);

        service.HandleRateLimit(MakeRateLimitException(retryAfterSeconds: 30));
        service.RecordPollSuccess();
        service.HandleRateLimit(MakeRateLimitException(retryAfterSeconds: 30));

        chatGui.Received(2).PrintError(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<ushort?>());
    }

    [Fact]
    public void HandleRateLimit_ExplainsTheLimitAndDuration()
    {
        var chatGui = Substitute.For<IChatGui>();
        var service = MakeService(chatGui: chatGui);

        service.HandleRateLimit(MakeRateLimitException(retryAfterSeconds: 30));

        chatGui.Received(1).PrintError(
            Arg.Is<string>(s => s.Contains("Spotify-side limit") && s.Contains("30s")),
            Arg.Any<string?>(), Arg.Any<ushort?>());
    }

    [Fact]
    public void RecordPollSuccess_AfterAnnouncedEpisode_AnnouncesResumeOnce()
    {
        var chatGui = Substitute.For<IChatGui>();
        var service = MakeService(chatGui: chatGui);

        service.HandleRateLimit(MakeRateLimitException(retryAfterSeconds: 30));
        service.RecordPollSuccess();
        service.RecordPollSuccess();

        chatGui.Received(1).Print(
            Arg.Is<string>(s => s.Contains("polling resumed")),
            Arg.Any<string?>(), Arg.Any<ushort?>());
    }

    [Fact]
    public void RecordPollSuccess_WithoutEpisode_PrintsNothing()
    {
        var chatGui = Substitute.For<IChatGui>();
        var service = MakeService(chatGui: chatGui);

        service.RecordPollSuccess();

        chatGui.DidNotReceive().Print(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<ushort?>());
    }

    [Fact]
    public void RecordPollSuccess_AfterEpisode_ResetsGateEscalation()
    {
        var service = MakeService();
        service.RateLimitGate.Activate(TimeSpan.Zero, DateTime.Now);
        service.HandleRateLimit(MakeRateLimitException(retryAfterSeconds: 30));

        service.RecordPollSuccess();

        service.RateLimitGate.FallbackEscalationCount.Should().Be(0);
    }

    [Theory]
    [InlineData(30, "30s")]
    [InlineData(89, "89s")]
    [InlineData(90, "2m")]
    [InlineData(2700, "45m")]
    [InlineData(9000, "2h 30m")]
    public void FormatPause_FormatsHumanReadably(int seconds, string expected)
    {
        SpotifyPollingService.FormatPause(TimeSpan.FromSeconds(seconds)).Should().Be(expected);
    }

    [Fact]
    public void HandleRateLimit_RecordsARateLimitedEvent()
    {
        var service = MakeService();

        service.HandleRateLimit(MakeRateLimitException(retryAfterSeconds: 30));

        var events = service.GetEventSnapshot();
        events.Should().ContainSingle(e => e.Kind == "rateLimited" && e.Detail == "30s");
    }

    [Fact]
    public void EventRing_CapsAtOneHundredNewestKept()
    {
        var service = MakeService();

        for (var i = 1; i <= 120; i++)
        {
            service.HandleRateLimit(MakeRateLimitException(retryAfterSeconds: i));
        }

        var events = service.GetEventSnapshot();
        events.Should().HaveCount(100);
        events[0].Detail.Should().Be("21s");
        events[^1].Detail.Should().Be("120s");
    }

    [Fact]
    public void NewService_HasNoRateLimitHistory()
    {
        var service = MakeService();

        service.RateLimit429Count.Should().Be(0);
        service.LastRetryAfter.Should().BeNull();
        service.TokenRefreshCount.Should().Be(0);
    }

    [Fact]
    public void HandleRateLimit_CountsHitsAndRemembersRetryAfter()
    {
        var service = MakeService();

        service.HandleRateLimit(MakeRateLimitException(retryAfterSeconds: 30));
        service.HandleRateLimit(MakeRateLimitException(retryAfterSeconds: 90));

        service.RateLimit429Count.Should().Be(2);
        service.LastRetryAfter.Should().Be(TimeSpan.FromSeconds(90));
    }

    [Fact]
    public void HandleRateLimit_FirstHit_AutoEnablesRateLimitProtection()
    {
        var chatGui = Substitute.For<IChatGui>();
        var service = MakeService(out var config, chatGui: chatGui);

        service.HandleRateLimit(MakeRateLimitException(retryAfterSeconds: 30));

        config.RateLimitProtection.Should().BeTrue();
        chatGui.Received(1).Print(
            Arg.Is<string>(s => s.Contains("Rate limit protection")),
            Arg.Any<string?>(), Arg.Any<ushort?>());
    }

    [Fact]
    public void HandleRateLimit_RepeatedHits_PrintAutoEnableLineOnlyOnce()
    {
        // The flag is already on after the first hit, so later hits in the
        // same session must not repeat the auto-enable line.
        var chatGui = Substitute.For<IChatGui>();
        var service = MakeService(chatGui: chatGui);

        service.HandleRateLimit(MakeRateLimitException(retryAfterSeconds: 30));
        service.HandleRateLimit(MakeRateLimitException(retryAfterSeconds: 60));

        chatGui.Received(1).Print(
            Arg.Is<string>(s => s.Contains("Rate limit protection")),
            Arg.Any<string?>(), Arg.Any<ushort?>());
    }

    [Fact]
    public void HandleRateLimit_ProtectionAlreadyOn_DoesNotPrintAutoEnableLine()
    {
        var chatGui = Substitute.For<IChatGui>();
        var service = MakeService(out var config, chatGui: chatGui);
        config.RateLimitProtection = true;

        service.HandleRateLimit(MakeRateLimitException(retryAfterSeconds: 30));

        chatGui.DidNotReceive().Print(
            Arg.Is<string>(s => s.Contains("Rate limit protection")),
            Arg.Any<string?>(), Arg.Any<ushort?>());
    }

    [Fact]
    public void TokenRefreshTimeout_IsThirtySeconds()
    {
        // A refresh consumes the single-use rotating refresh token even when
        // the client gives up waiting; 30s absorbs slow post-login networks.
        SpotifyPollingService.TOKEN_REFRESH_TIMEOUT_MS.Should().Be(30000);
    }

    // The 401-recovery path is the whole point of the fix, so it is driven
    // through the real poll entry point with both network seams substituted:
    // a scripted token refresh, and a queue of clients handed out in order.
    private static SpotifyPollingService MakeSeamedService(Queue<ISpotifyClient> clients, IChatGui? chatGui = null)
    {
        var config = new Config();
        config.Initialize(Substitute.For<IDalamudPluginInterface>());
        config.SpotifyClientId = "client-id";
        config.SpotifyRefreshToken = "refresh-token";

        return new SpotifyPollingService(
            config,
            Substitute.For<IPluginLog>(),
            MakeChatNotifier(chatGui ?? Substitute.For<IChatGui>()),
            (_, _, _) => Task.FromResult(new PKCETokenResponse
            {
                AccessToken = "access-token",
                RefreshToken = "refresh-token",
                ExpiresIn = 3600,
                CreatedAt = DateTime.Now,
            }),
            _ => clients.Dequeue());
    }

    private static ISpotifyClient MakeClient(Func<CurrentlyPlaying> respond)
    {
        var player = Substitute.For<IPlayerClient>();
        player.GetCurrentlyPlaying(Arg.Any<PlayerCurrentlyPlayingRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(respond()));

        var client = Substitute.For<ISpotifyClient>();
        client.Player.Returns(player);
        return client;
    }

    private static ISpotifyClient MakeUnauthorizedClient()
        => MakeClient(() => throw new APIUnauthorizedException(
            new FakeResponse { StatusCode = HttpStatusCode.Unauthorized }));

    private static ISpotifyClient MakePlayingClient(string trackId)
        => MakeClient(() => new CurrentlyPlaying
        {
            IsPlaying = true,
            Item = new FullTrack { Id = trackId, Name = "Song", DurationMs = 200_000 },
            ProgressMs = 1000,
        });

    [Fact]
    public async Task Poll_SpuriousUnauthorized_RecoversWithoutSurfacingAnError()
    {
        // Spotify has been seen rejecting access tokens under a second old.
        // One silent refresh-and-retry keeps that off the user's screen.
        var clients = new Queue<ISpotifyClient>([MakeUnauthorizedClient(), MakePlayingClient("track1")]);
        var service = MakeSeamedService(clients);

        var result = await service.GetCurrentlyPlayingTrackAsync();

        result.Should().NotBeNull();
        result!.Track!.Id.Should().Be("track1");
        service.ApiErrorCount.Should().Be(0);
        service.AuthRetryCount.Should().Be(1);
        service.AuthRecoveredCount.Should().Be(1);
    }

    [Fact]
    public async Task Poll_SpuriousUnauthorized_MintsAFreshTokenBeforeRetrying()
    {
        // Retrying on the same rejected token would just 401 again.
        var clients = new Queue<ISpotifyClient>([MakeUnauthorizedClient(), MakePlayingClient("track1")]);
        var service = MakeSeamedService(clients);

        await service.GetCurrentlyPlayingTrackAsync();

        service.TokenRefreshCount.Should().Be(2);
        clients.Should().BeEmpty("the retry must use the newly built client, not the rejected one");
    }

    [Fact]
    public async Task Poll_SpuriousUnauthorized_ReportsAResultSoTheTitleIsNotCleared()
    {
        // A null return is what makes the Updater blank the title; recovery
        // must produce a real result instead.
        var clients = new Queue<ISpotifyClient>([MakeUnauthorizedClient(), MakePlayingClient("track1")]);
        var service = MakeSeamedService(clients);

        var result = await service.GetCurrentlyPlayingTrackAsync();

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task Poll_UnauthorizedTwice_SurfacesTheErrorWithItsStatusCode()
    {
        // A fresh token that is also rejected is a real problem, not a hiccup.
        var clients = new Queue<ISpotifyClient>([MakeUnauthorizedClient(), MakeUnauthorizedClient()]);
        var service = MakeSeamedService(clients);

        var result = await service.GetCurrentlyPlayingTrackAsync();

        result.Should().BeNull();
        service.ApiErrorCount.Should().Be(1);
        service.GetEventSnapshot().Should()
            .ContainSingle(e => e.Kind == "apiError" && e.Detail == "APIUnauthorizedException 401");
        // The retry was attempted but did not recover, so it must not be
        // counted as one or the chat notice would claim a recovery that
        // never happened.
        service.AuthRetryCount.Should().Be(1);
        service.AuthRecoveredCount.Should().Be(0);
    }

    [Fact]
    public async Task RunWithAuthRetry_IsNotPollSpecific()
    {
        // Queue actions hit the same spurious 401s. Without this they fall into
        // TrackQueueService's handler and tell the user to re-authenticate,
        // which does not help.
        var clients = new Queue<ISpotifyClient>([MakeUnauthorizedClient(), MakePlayingClient("track1")]);
        var service = MakeSeamedService(clients);
        var stale = await service.GetAuthenticatedClientAsync();
        var attempts = 0;

        var result = await service.RunWithAuthRetryAsync(stale!, client =>
        {
            attempts++;
            return client.Player.GetCurrentlyPlaying(new PlayerCurrentlyPlayingRequest(), CancellationToken.None);
        });

        attempts.Should().Be(2);
        result.Item.Should().BeOfType<FullTrack>();
        service.AuthRecoveredCount.Should().Be(1);
    }

    [Fact]
    public async Task Poll_Successful_DoesNotForceAnExtraRefresh()
    {
        var clients = new Queue<ISpotifyClient>([MakePlayingClient("track1")]);
        var service = MakeSeamedService(clients);

        var result = await service.GetCurrentlyPlayingTrackAsync();

        result!.Track!.Id.Should().Be("track1");
        service.TokenRefreshCount.Should().Be(1);
        service.AuthRetryCount.Should().Be(0);
    }

    [Fact]
    public async Task Poll_SecondPoll_ReusesTheCachedClient()
    {
        // Only one client is queued, so a second creation would throw.
        var clients = new Queue<ISpotifyClient>([MakePlayingClient("track1")]);
        var service = MakeSeamedService(clients);

        await service.GetCurrentlyPlayingTrackAsync();
        var result = await service.GetCurrentlyPlayingTrackAsync();

        result!.Track!.Id.Should().Be("track1");
        service.TokenRefreshCount.Should().Be(1);
    }

    [Fact]
    public void RecordAuthRetry_CountsRetriesAndRecordsEvents()
    {
        var service = MakeService();

        service.RecordAuthRetry();
        service.RecordAuthRetry();

        service.AuthRetryCount.Should().Be(2);
        service.GetEventSnapshot().Should().HaveCount(2).And.OnlyContain(e => e.Kind == "authRetry");
    }

    [Fact]
    public void RecordAuthRetry_NeverAnnouncesOnItsOwn()
    {
        // An attempt is not an outcome. Announcing here would claim recovery
        // for retries that went on to fail.
        var chatGui = Substitute.For<IChatGui>();
        var service = MakeService(chatGui: chatGui);

        for (var i = 0; i < SpotifyPollingService.TOKEN_CONFLICT_THRESHOLD + 3; i++)
        {
            service.RecordAuthRetry();
        }

        service.AuthRecoveredCount.Should().Be(0);
        chatGui.DidNotReceive().PrintError(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<ushort?>());
    }

    [Fact]
    public void RecordAuthRecovered_BelowThreshold_StaysQuiet()
    {
        // One-off 401s are recovered silently; only a pattern is worth saying.
        var chatGui = Substitute.For<IChatGui>();
        var service = MakeService(chatGui: chatGui);

        for (var i = 0; i < SpotifyPollingService.TOKEN_CONFLICT_THRESHOLD - 1; i++)
        {
            service.RecordAuthRecovered();
        }

        chatGui.DidNotReceive().PrintError(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<ushort?>());
    }

    [Fact]
    public void RecordAuthRecovered_AtThreshold_SaysItRecoveredWithoutBlamingACause()
    {
        // The cause is not knowable from inside the plugin, so the notice must
        // report what happened and that it was handled, not assert a diagnosis
        // the user has already ruled out.
        var chatGui = Substitute.For<IChatGui>();
        var service = MakeService(chatGui: chatGui);

        for (var i = 0; i < SpotifyPollingService.TOKEN_CONFLICT_THRESHOLD + 3; i++)
        {
            service.RecordAuthRecovered();
        }

        chatGui.Received(1).PrintError(
            Arg.Is<string>(s => s.Contains("recovered automatically") && s.Contains("no action is needed")),
            Arg.Any<string?>(), Arg.Any<ushort?>());
    }

    [Fact]
    public void RecordAuthRecovered_SurvivesSuccessfulPolls()
    {
        // The 401s are minutes apart with successful polls in between, so the
        // counter must be cumulative or the pattern is never detected.
        var chatGui = Substitute.For<IChatGui>();
        var service = MakeService(chatGui: chatGui);

        for (var i = 0; i < SpotifyPollingService.TOKEN_CONFLICT_THRESHOLD; i++)
        {
            service.RecordAuthRecovered();
            service.RecordPollSuccess();
        }

        service.AuthRecoveredCount.Should().Be(SpotifyPollingService.TOKEN_CONFLICT_THRESHOLD);
        chatGui.Received(1).PrintError(
            Arg.Is<string>(s => s.Contains("Client ID")),
            Arg.Any<string?>(), Arg.Any<ushort?>());
    }

    [Fact]
    public void ShouldAnnounceError_FirstOccurrence_Announces()
    {
        SpotifyPollingService.ShouldAnnounceError("boom", null, DateTime.MinValue, DateTime.Now, 5)
            .Should().BeTrue();
    }

    [Fact]
    public void ShouldAnnounceError_SameFailureRepeatingWithinCooldown_StaysQuiet()
    {
        // The reported spam: a 401 every few minutes, each one printing. The
        // failures alternate with successful polls, so only elapsed time can
        // end the episode.
        var now = DateTime.Now;

        SpotifyPollingService.ShouldAnnounceError("boom", "boom", now, now.AddMinutes(4), 5)
            .Should().BeFalse();
    }

    [Fact]
    public void ShouldAnnounceError_SameFailureAfterCooldown_AnnouncesAgain()
    {
        var now = DateTime.Now;

        SpotifyPollingService.ShouldAnnounceError("boom", "boom", now, now.AddMinutes(5), 5)
            .Should().BeTrue();
    }

    [Fact]
    public void ShouldAnnounceError_DifferentFailure_AnnouncesImmediately()
    {
        // A new kind of failure is news even mid-cooldown.
        var now = DateTime.Now;

        SpotifyPollingService.ShouldAnnounceError("timeout", "boom", now, now.AddSeconds(1), 5)
            .Should().BeTrue();
    }

    [Fact]
    public void DescribeApiError_IncludesTheStatusCode()
    {
        // The old report recorded only the type name, which could not tell a
        // 401 apart from a 502 for anything sharing the APIException base.
        var e = new APIException(new FakeResponse { StatusCode = HttpStatusCode.BadGateway });

        SpotifyPollingService.DescribeApiError(e).Should().Be("APIException 502");
    }

    [Fact]
    public void DescribeApiError_WithoutResponse_FallsBackToTheTypeName()
    {
        SpotifyPollingService.DescribeApiError(new APIException("boom"))
            .Should().Be("APIException");
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var service = MakeService();

        service.Dispose();
        var act = () => service.Dispose();

        act.Should().NotThrow();
    }

    private sealed class FakeResponse : IResponse
    {
        public object? Body { get; init; }
        public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>();
        public HttpStatusCode StatusCode { get; init; }
        public string? ContentType { get; init; }
    }
}
