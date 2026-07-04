using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using System.Numerics;

namespace SpotifyHonorific.Windows;

public class NearbyListeningWindow : Window
{
    private readonly NearbyListeningView _view;

    public NearbyListeningWindow(NearbyListeningView view) : base("Nearby Listening##nearbyListeningWindow")
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(420, 220),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };
        Size = new Vector2(520, 320);
        SizeCondition = ImGuiCond.FirstUseEver;

        _view = view;
    }

    public override void Draw() => _view.Draw();
}
