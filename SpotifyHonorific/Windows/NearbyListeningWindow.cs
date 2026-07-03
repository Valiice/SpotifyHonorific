using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using SpotifyHonorific.Core;
using System.Numerics;

namespace SpotifyHonorific.Windows;

public class NearbyListeningWindow : Window
{
    private readonly NearbyTitleWatcher _nearbyTitleWatcher;

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
        if (_nearbyTitleWatcher.History.Count == 0)
        {
            ImGui.TextUnformatted("No nearby Honorific titles seen yet.");
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

        foreach (var entry in _nearbyTitleWatcher.History)
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
