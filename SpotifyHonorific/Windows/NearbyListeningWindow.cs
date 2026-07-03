using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using SpotifyHonorific.Core;
using System;
using System.Linq;
using System.Numerics;

namespace SpotifyHonorific.Windows;

public class NearbyListeningWindow : Window
{
    private const ushort SEARCH_MAX_LENGTH = 100;

    private readonly NearbyTitleWatcher _nearbyTitleWatcher;
    private string _searchFilter = string.Empty;

    public NearbyListeningWindow(NearbyTitleWatcher nearbyTitleWatcher) : base("Nearby Listening##nearbyListeningWindow")
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(420, 220),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        _nearbyTitleWatcher = nearbyTitleWatcher;
    }

    public override void Draw()
    {
        ImGui.InputTextWithHint("##nearbySearch", "Search by song title...", ref _searchFilter, SEARCH_MAX_LENGTH);

        var entries = _nearbyTitleWatcher.History
            .Where(e => string.IsNullOrEmpty(_searchFilter) || e.RawTitle.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (entries.Count == 0)
        {
            ImGui.TextUnformatted(_nearbyTitleWatcher.History.Count == 0
                ? "No nearby Honorific titles seen yet."
                : "No titles match your search.");
            return;
        }

        if (!ImGui.BeginTable("nearbyListeningTable", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
        {
            return;
        }

        ImGui.TableSetupColumn("Player");
        ImGui.TableSetupColumn("Title");
        ImGui.TableSetupColumn("Last Seen");
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
        }

        ImGui.EndTable();
    }
}
