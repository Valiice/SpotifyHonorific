using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using SpotifyHonorific.Core;
using SpotifyHonorific.Utils;
using System;
using System.Linq;

namespace SpotifyHonorific.Windows;

// The Nearby Listening UI content, shared between the standalone window
// (/spotifyhonorific nearby) and the Nearby tab in the config window so
// both render the same list with the same filter state.
public class NearbyListeningView
{
    private const ushort SEARCH_MAX_LENGTH = 100;

    private readonly NearbyTitleWatcher _nearbyTitleWatcher;
    private readonly RecentTitleCache _recentTitleCache;
    private readonly TrackQueueService _trackQueueService;
    private readonly IChatGui _chatGui;
    private string _searchFilter = string.Empty;
    private bool _onlyShowLikelySongs = true;

    public NearbyListeningView(NearbyTitleWatcher nearbyTitleWatcher, RecentTitleCache recentTitleCache, TrackQueueService trackQueueService, IChatGui chatGui)
    {
        _nearbyTitleWatcher = nearbyTitleWatcher;
        _recentTitleCache = recentTitleCache;
        _trackQueueService = trackQueueService;
        _chatGui = chatGui;
    }

    public void Draw()
    {
        ImGui.InputTextWithHint("##nearbySearch", "Search by song title...", ref _searchFilter, SEARCH_MAX_LENGTH);
        ImGui.Checkbox("Only show likely song titles", ref _onlyShowLikelySongs);
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Best-effort guess based on music symbols (♪) or a \"Track - Artist\" pattern in the title. Not reliable — Honorific titles are free text with no way to tell what set them.");
        }

        var entries = _nearbyTitleWatcher.History
            .Where(e => !_onlyShowLikelySongs
                || SongTitleHeuristic.LooksLikeSong(e.RawTitle)
                || _recentTitleCache.IsKnownSpotifyListener(e.CharacterName))
            .Where(e => string.IsNullOrEmpty(_searchFilter) || e.RawTitle.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (entries.Count == 0)
        {
            ImGui.TextUnformatted(_nearbyTitleWatcher.History.Count == 0
                ? "No nearby Honorific titles seen yet."
                : "No titles match your filters.");
            return;
        }

        if (!ImGui.BeginTable("nearbyListeningTable", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
        {
            return;
        }

        ImGui.TableSetupColumn("Player");
        ImGui.TableSetupColumn("Title");
        ImGui.TableSetupColumn("Last Seen");
        ImGui.TableSetupColumn("");
        ImGui.TableHeadersRow();

        foreach (var entry in entries)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(entry.CharacterName);
            ImGui.TableNextColumn();
            DrawTitleCell(entry);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(entry.LastSeen.ToString("T"));
            ImGui.TableNextColumn();
            if (ImGui.Button($"Queue###queueBtn{entry.CharacterName}"))
            {
                QueueForCharacter(entry.CharacterName);
            }
            if (ImGui.IsItemHovered())
            {
                var query = _recentTitleCache.BuildSearchQuery(entry.CharacterName, DateTime.Now);
                ImGui.SetTooltip(query != null
                    ? $"Spotify will search for: {query}"
                    : "Nothing usable seen yet — wait a few seconds for their title to cycle.");
            }
        }

        ImGui.EndTable();
    }

    private void DrawTitleCell(NearbyPlayerEntry entry)
    {
        // Mid-cycle no-info phases ("Listening to Spotify", playback timers)
        // aren't worth displaying — show the last real song text we cached
        // for this character instead, when we have it.
        if (SpotifyPlaceholderDetector.IsNoInfoPhase(TitleTextCleaner.Clean(entry.RawTitle)))
        {
            var lastKnown = _recentTitleCache.BuildSearchQuery(entry.CharacterName, DateTime.Now);
            if (lastKnown != null)
            {
                ImGui.TextDisabled($"{lastKnown} (last seen)");
                return;
            }
        }

        ImGui.TextUnformatted(entry.RawTitle);
    }

    private void QueueForCharacter(string characterName)
    {
        var now = DateTime.Now;
        var query = _recentTitleCache.BuildSearchQuery(characterName, now);
        if (query == null)
        {
            _chatGui.Print("SpotifyHonorific: Haven't seen their song info yet — wait a few seconds and try again.");
            return;
        }

        _ = _trackQueueService.QueueTrackFromTitleAsync(query, _recentTitleCache.GetFreshSamples(characterName, now));
    }
}
