using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FluentAssertions;
using NSubstitute;
using SpotifyAPI.Web;
using SpotifyAPI.Web.Http;
using SpotifyHonorific.Core;
using System.Net;

namespace SpotifyHonorific.Tests;

// QueueTrackFromTitleAsync drives two live Spotify calls and had no coverage
// at all before the auth-retry rework touched both of them.
public class QueueTrackFromTitleTests
{
    private const string TrackUri = "spotify:track:abc123";

    private static ChatNotifier MakeChatNotifier(IChatGui chatGui)
    {
        var framework = Substitute.For<IFramework>();
        framework.RunOnFrameworkThread(Arg.Any<Action>())
            .Returns(ci => { ci.Arg<Action>()(); return Task.CompletedTask; });
        return new ChatNotifier(chatGui, framework);
    }

    private static SpotifyPollingService MakeSeamedService(Queue<ISpotifyClient> clients, IChatGui chatGui)
    {
        var config = new Config();
        config.Initialize(Substitute.For<IDalamudPluginInterface>());
        config.SpotifyClientId = "client-id";
        config.SpotifyRefreshToken = "refresh-token";

        return new SpotifyPollingService(
            config,
            Substitute.For<IPluginLog>(),
            MakeChatNotifier(chatGui),
            (_, _, _) => Task.FromResult(new PKCETokenResponse
            {
                AccessToken = "access-token",
                RefreshToken = "refresh-token",
                ExpiresIn = 3600,
                CreatedAt = DateTime.Now,
            }),
            _ => clients.Dequeue());
    }

    // searchFails/queueFails make that call throw 401 once per client, which is
    // how a stale token behaves: the request is rejected until a fresh client
    // replaces this one.
    private static ISpotifyClient MakeClient(bool searchFails = false, bool queueFails = false, IReadOnlyList<FullTrack>? results = null)
    {
        var unauthorized = () => new APIUnauthorizedException(
            new FakeResponse { StatusCode = HttpStatusCode.Unauthorized });

        var search = Substitute.For<ISearchClient>();
        search.Item(Arg.Any<SearchRequest>()).Returns(_ => searchFails
            ? throw unauthorized()
            : Task.FromResult(new SearchResponse
            {
                Tracks = new Paging<FullTrack, SearchResponse> { Items = [.. results ?? [MakeTrack("Song", TrackUri)]] },
            }));

        var player = Substitute.For<IPlayerClient>();
        player.AddToQueue(Arg.Any<PlayerAddToQueueRequest>()).Returns(_ => queueFails
            ? throw unauthorized()
            : Task.FromResult(true));

        var client = Substitute.For<ISpotifyClient>();
        client.Search.Returns(search);
        client.Player.Returns(player);
        return client;
    }

    private static FullTrack MakeTrack(string name, string uri) =>
        new() { Name = name, Uri = uri, Artists = [new SimpleArtist { Name = "Artist" }] };

    private static TrackQueueService MakeQueueService(Queue<ISpotifyClient> clients, IChatGui chatGui) =>
        new(MakeSeamedService(clients, chatGui), Substitute.For<IPluginLog>(), MakeChatNotifier(chatGui));

    [Fact]
    public async Task HappyPath_SearchesAndQueuesTheMatch()
    {
        var client = MakeClient();
        var chatGui = Substitute.For<IChatGui>();

        await MakeQueueService(new Queue<ISpotifyClient>([client]), chatGui)
            .QueueTrackFromTitleAsync("♪Song♪");

        await client.Player.Received(1).AddToQueue(
            Arg.Is<PlayerAddToQueueRequest>(r => r.Uri == TrackUri));
        chatGui.Received(1).Print(
            Arg.Is<string>(s => s.Contains("Queued \"Song\"")),
            Arg.Any<string?>(), Arg.Any<ushort?>());
    }

    [Fact]
    public async Task SearchRejectedWith401_RecoversAndStillQueues()
    {
        // Before the auth retry this printed "re-click Authenticate with
        // Spotify" for a transient rejection that re-authenticating cannot fix.
        var stale = MakeClient(searchFails: true);
        var fresh = MakeClient();
        var chatGui = Substitute.For<IChatGui>();

        await MakeQueueService(new Queue<ISpotifyClient>([stale, fresh]), chatGui)
            .QueueTrackFromTitleAsync("♪Song♪");

        await fresh.Player.Received(1).AddToQueue(Arg.Any<PlayerAddToQueueRequest>());
        chatGui.DidNotReceive().PrintError(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<ushort?>());
    }

    [Fact]
    public async Task SearchRecovered_QueuesOnTheFreshClientWithoutASecondRefresh()
    {
        // Only two clients are queued: if the queue call restarted from the
        // rejected client it would need a third, and the dequeue would throw.
        var stale = MakeClient(searchFails: true);
        var fresh = MakeClient();
        var chatGui = Substitute.For<IChatGui>();

        await MakeQueueService(new Queue<ISpotifyClient>([stale, fresh]), chatGui)
            .QueueTrackFromTitleAsync("♪Song♪");

        await stale.Player.DidNotReceive().AddToQueue(Arg.Any<PlayerAddToQueueRequest>());
        chatGui.Received(1).Print(
            Arg.Is<string>(s => s.Contains("Queued")),
            Arg.Any<string?>(), Arg.Any<ushort?>());
    }

    [Fact]
    public async Task QueueCallRejectedWith401_RecoversAndStillQueues()
    {
        var stale = MakeClient(queueFails: true);
        var fresh = MakeClient();
        var chatGui = Substitute.For<IChatGui>();

        await MakeQueueService(new Queue<ISpotifyClient>([stale, fresh]), chatGui)
            .QueueTrackFromTitleAsync("♪Song♪");

        await fresh.Player.Received(1).AddToQueue(Arg.Any<PlayerAddToQueueRequest>());
        chatGui.DidNotReceive().PrintError(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<ushort?>());
    }

    [Fact]
    public async Task PersistentlyUnauthorized_StillTellsTheUserToReauthenticate()
    {
        // A token rejected even after a fresh mint is a real scope problem, and
        // the original guidance must survive the retry rework.
        var chatGui = Substitute.For<IChatGui>();
        var clients = new Queue<ISpotifyClient>([MakeClient(queueFails: true), MakeClient(queueFails: true)]);

        await MakeQueueService(clients, chatGui).QueueTrackFromTitleAsync("♪Song♪");

        chatGui.Received(1).PrintError(
            Arg.Is<string>(s => s.Contains("queue permission")),
            Arg.Any<string?>(), Arg.Any<ushort?>());
    }

    [Fact]
    public async Task NoSearchResults_TellsTheUserAndDoesNotQueue()
    {
        var client = MakeClient(results: []);
        var chatGui = Substitute.For<IChatGui>();

        await MakeQueueService(new Queue<ISpotifyClient>([client]), chatGui)
            .QueueTrackFromTitleAsync("♪Nothing♪");

        await client.Player.DidNotReceive().AddToQueue(Arg.Any<PlayerAddToQueueRequest>());
        chatGui.Received(1).Print(
            Arg.Is<string>(s => s.Contains("No track found for")),
            Arg.Any<string?>(), Arg.Any<ushort?>());
    }

    private sealed class FakeResponse : IResponse
    {
        public object? Body { get; init; }
        public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>();
        public HttpStatusCode StatusCode { get; init; }
        public string? ContentType { get; init; }
    }
}

public class TrackQueueServiceTests
{
    private static FullTrack MakeTrack(string name) => new() { Name = name };

    [Fact]
    public void PickBestMatch_HintExactlyMatchesLaterResult_PrefersItOverTopHit()
    {
        // Spotify ranked the romanized release first, but the player's title
        // showed the Japanese one, the exact hint match must win.
        var tracks = new[] { MakeTrack("Usseewa"), MakeTrack("うっせぇわ") };

        var picked = TrackQueueService.PickBestMatch(tracks, ["うっせぇわ", "Ado"]);

        picked.Name.Should().Be("うっせぇわ");
    }

    [Fact]
    public void PickBestMatch_NoExactMatch_HintContainingResultNameWins()
    {
        var tracks = new[] { MakeTrack("Some Remix"), MakeTrack("FATE") };

        var picked = TrackQueueService.PickBestMatch(tracks, ["FATE - Alan Walker Ava Max"]);

        picked.Name.Should().Be("FATE");
    }

    [Fact]
    public void PickBestMatch_NothingMatches_FallsBackToTopHit()
    {
        var tracks = new[] { MakeTrack("Top Hit"), MakeTrack("Second Hit") };

        var picked = TrackQueueService.PickBestMatch(tracks, ["completely different text"]);

        picked.Name.Should().Be("Top Hit");
    }

    [Fact]
    public void PickBestMatch_ExactMatchIsCaseInsensitive()
    {
        var tracks = new[] { MakeTrack("Other"), MakeTrack("The Phoenix") };

        var picked = TrackQueueService.PickBestMatch(tracks, ["the phoenix"]);

        picked.Name.Should().Be("The Phoenix");
    }
}
