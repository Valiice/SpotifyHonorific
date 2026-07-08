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
    private static SpotifyPollingService MakeService(IPluginLog? pluginLog = null)
    {
        var config = new Config();
        config.Initialize(Substitute.For<IDalamudPluginInterface>());
        return new SpotifyPollingService(config, pluginLog ?? Substitute.For<IPluginLog>(), Substitute.For<IChatGui>());
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

    private sealed class FakeResponse : IResponse
    {
        public object? Body { get; init; }
        public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>();
        public HttpStatusCode StatusCode { get; init; }
        public string? ContentType { get; init; }
    }
}
