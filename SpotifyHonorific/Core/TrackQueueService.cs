using Dalamud.Plugin.Services;
using SpotifyAPI.Web;
using SpotifyHonorific.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace SpotifyHonorific.Core;

public class TrackQueueService
{
    private const int SEARCH_RESULT_LIMIT = 5;

    private readonly SpotifyPollingService _pollingService;
    private readonly IPluginLog _pluginLog;
    private readonly IChatGui _chatGui;

    public TrackQueueService(SpotifyPollingService pollingService, IPluginLog pluginLog, IChatGui chatGui)
    {
        _pollingService = pollingService;
        _pluginLog = pluginLog;
        _chatGui = chatGui;
    }

    public async Task QueueTrackFromTitleAsync(string rawTitle, IReadOnlyList<string>? titleHints = null)
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
                .Item(new SearchRequest(SearchRequest.Types.Track, query) { Limit = SEARCH_RESULT_LIMIT })
                .ConfigureAwait(false);
            var tracks = searchResponse.Tracks?.Items;
            if (tracks == null || tracks.Count == 0)
            {
                _chatGui.Print($"SpotifyHonorific: No track found for \"{query}\".");
                return;
            }

            var track = PickBestMatch(tracks, titleHints ?? [query]);

            await spotify.Player.AddToQueue(new PlayerAddToQueueRequest(track.Uri)).ConfigureAwait(false);
            var artistNames = string.Join(", ", track.Artists.Select(a => a.Name));
            _chatGui.Print($"SpotifyHonorific: Queued \"{track.Name}\" by {artistNames}.");
        }
        catch (APIException e) when (e.Response?.StatusCode == HttpStatusCode.Forbidden)
        {
            _pluginLog.Warning(e, "Spotify rejected queue request due to insufficient scope.");
            _chatGui.PrintError("SpotifyHonorific: Queueing needs a Spotify permission this plugin didn't have before. Re-click 'Authenticate with Spotify' in /spotifyhonorific config, then try again.");
        }
        catch (APIException e) when (e.Response?.StatusCode == HttpStatusCode.NotFound)
        {
            // Spotify returns 404 NO_ACTIVE_DEVICE when nothing has played
            // recently — the queue needs an active playback session to attach to.
            _pluginLog.Warning(e, "Spotify queue request failed: no active device.");
            _chatGui.PrintError("SpotifyHonorific: No active Spotify device. Open Spotify and play or pause something first, then try again.");
        }
        catch (Exception e)
        {
            _pluginLog.Error(e, "Failed to queue track from nearby title.");
            _chatGui.PrintError("SpotifyHonorific: Failed to queue track. Check /xllog for details.");
        }
    }

    // Spotify's ranking sometimes puts a different release of the same song
    // first (e.g. a romanized "Usseewa" over the Japanese-titled "うっせぇわ"
    // the player's title actually showed). Prefer the result whose name
    // matches one of the title texts we actually saw, falling back to
    // Spotify's top hit.
    internal static FullTrack PickBestMatch(IReadOnlyList<FullTrack> tracks, IReadOnlyList<string> titleHints)
    {
        foreach (var hint in titleHints)
        {
            var exact = tracks.FirstOrDefault(t => string.Equals(t.Name, hint.Trim(), StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;
        }

        foreach (var hint in titleHints)
        {
            var partial = tracks.FirstOrDefault(t => hint.Contains(t.Name, StringComparison.OrdinalIgnoreCase));
            if (partial != null) return partial;
        }

        return tracks[0];
    }
}
