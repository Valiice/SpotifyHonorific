using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using SpotifyHonorific.Core;
using SpotifyHonorific.Utils;
using System;
using System.Linq;
using System.Numerics;

namespace SpotifyHonorific.Windows;

public class NearbyListeningWindow : Window
{
    private const ushort SEARCH_MAX_LENGTH = 100;

    private readonly NearbyTitleWatcher _nearbyTitleWatcher;
    private readonly RecentTitleCache _recentTitleCache;
    private readonly TrackQueueService _trackQueueService;
    private readonly IChatGui _chatGui;
    private string _searchFilter = string.Empty;
    private bool _onlyShowLikelySongs = true;

    public NearbyListeningWindow(NearbyTitleWatcher nearbyTitleWatcher, RecentTitleCache recentTitleCache, TrackQueueService trackQueueService, IChatGui chatGui) : base("Nearby Listening##nearbyListeningWindow")
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(420, 220),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };
        Size = new Vector2(520, 320);
        SizeCondition = ImGuiCond.FirstUseEver;

        _nearbyTitleWatcher = nearbyTitleWatcher;
        _recentTitleCache = recentTitleCache;
        _trackQueueService = trackQueueService;
        _chatGui = chatGui;
    }

    public override void Draw()
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
            ImGui.TextUnformatted(entry.RawTitle);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(entry.LastSeen.ToString("T"));
            ImGui.TableNextColumn();
            if (ImGui.Button($"Queue###queueBtn{entry.CharacterName}"))
            {
                QueueForCharacter(entry.CharacterName);
            }
        }

        ImGui.EndTable();
    }

    private void QueueForCharacter(string characterName)
    {
        var query = _recentTitleCache.BuildSearchQuery(characterName, DateTime.Now);
        if (query == null)
        {
            _chatGui.Print("SpotifyHonorific: Haven't seen their song info yet — wait a few seconds and try again.");
            return;
        }

        _ = _trackQueueService.QueueTrackFromTitleAsync(query);
    }
}
