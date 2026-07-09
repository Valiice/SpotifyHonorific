using Dalamud.Plugin.Services;

namespace SpotifyHonorific.Core;

// Dalamud's ChatGui buffers messages in a non-thread-safe queue drained on
// the framework thread, so printing from threadpool poll continuations races
// that queue. This wrapper marshals every print onto the framework thread;
// the returned task is fire-and-forget because chat output is best-effort.
public class ChatNotifier(IChatGui chatGui, IFramework framework)
{
    public void Print(string message)
        => _ = framework.RunOnFrameworkThread(() => chatGui.Print(message));

    public void PrintError(string message)
        => _ = framework.RunOnFrameworkThread(() => chatGui.PrintError(message));
}
