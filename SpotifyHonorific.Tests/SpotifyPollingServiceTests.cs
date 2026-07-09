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
    private static SpotifyPollingService MakeService(IPluginLog? pluginLog = null, IChatGui? chatGui = null)
    {
        var config = new Config();
        config.Initialize(Substitute.For<IDalamudPluginInterface>());
        return new SpotifyPollingService(config, pluginLog ?? Substitute.For<IPluginLog>(), chatGui ?? Substitute.For<IChatGui>());
    }

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
    public async Task GetCurrentlyPlayingTrack_WhileRateLimited_SkipsThePollEntirely()
    {
        var pluginLog = Substitute.For<IPluginLog>();
        var service = MakeService(pluginLog);
        service.RateLimitGate.Activate(TimeSpan.FromSeconds(30), DateTime.Now);

        var track = await service.GetCurrentlyPlayingTrackAsync();

        track.Should().BeNull();
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

    private sealed class FakeResponse : IResponse
    {
        public object? Body { get; init; }
        public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>();
        public HttpStatusCode StatusCode { get; init; }
        public string? ContentType { get; init; }
    }
}
