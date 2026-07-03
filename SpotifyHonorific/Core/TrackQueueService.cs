using Dalamud.Plugin.Services;
using SpotifyAPI.Web;
using SpotifyHonorific.Utils;
using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace SpotifyHonorific.Core;

public class TrackQueueService
{
    private readonly SpotifyPollingService _pollingService;
    private readonly IPluginLog _pluginLog;
    private readonly IChatGui _chatGui;

    public TrackQueueService(SpotifyPollingService pollingService, IPluginLog pluginLog, IChatGui chatGui)
    {
        _pollingService = pollingService;
        _pluginLog = pluginLog;
        _chatGui = chatGui;
    }

    public async Task QueueTrackFromTitleAsync(string rawTitle)
    {
        var query = TitleTextCleaner.Clean(rawTitle);
        if (string.IsNullOrWhiteSpace(query))
        {
            _chatGui.Print("SpotifyHonorific: No track found in their title.");
            return;
        }

        try
        {
            var spotify = await _pollingService.GetAuthenticatedClientAsync().ConfigureAwait(false);
            if (spotify == null)
            {
                _chatGui.PrintError("SpotifyHonorific: Not authenticated with Spotify. Use /spotifyhonorific config to log in.");
                return;
            }

            var searchResponse = await spotify.Search
                .Item(new SearchRequest(SearchRequest.Types.Track, query) { Limit = 1 })
                .ConfigureAwait(false);
            var track = searchResponse.Tracks?.Items?.FirstOrDefault();
            if (track == null)
            {
                _chatGui.Print($"SpotifyHonorific: No track found for \"{query}\".");
                return;
            }

            await spotify.Player.AddToQueue(new PlayerAddToQueueRequest(track.Uri)).ConfigureAwait(false);
            var artistNames = string.Join(", ", track.Artists.Select(a => a.Name));
            _chatGui.Print($"SpotifyHonorific: Queued \"{track.Name}\" by {artistNames}.");
        }
        catch (APIException e) when (e.Response?.StatusCode == HttpStatusCode.Forbidden)
        {
            _pluginLog.Warning(e, "Spotify rejected queue request due to insufficient scope.");
            _chatGui.PrintError("SpotifyHonorific: Queueing needs a Spotify permission this plugin didn't have before. Re-click 'Authenticate with Spotify' in /spotifyhonorific config, then try again.");
        }
        catch (Exception e)
        {
            _pluginLog.Error(e, "Failed to queue track from nearby title.");
            _chatGui.PrintError("SpotifyHonorific: Failed to queue track. Check /xllog for details.");
        }
    }
}
