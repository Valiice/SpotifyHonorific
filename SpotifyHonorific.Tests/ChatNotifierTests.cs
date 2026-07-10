using Dalamud.Plugin.Services;
using NSubstitute;
using SpotifyHonorific.Core;

namespace SpotifyHonorific.Tests;

public class ChatNotifierTests
{
    private static IFramework MakeInlineFramework()
    {
        var framework = Substitute.For<IFramework>();
        framework.RunOnFrameworkThread(Arg.Any<Action>())
            .Returns(ci => { ci.Arg<Action>()(); return Task.CompletedTask; });
        return framework;
    }

    [Fact]
    public void Print_RunsOnFrameworkThread()
    {
        var chatGui = Substitute.For<IChatGui>();
        var framework = MakeInlineFramework();
        var notifier = new ChatNotifier(chatGui, framework);

        notifier.Print("hello");

        framework.Received(1).RunOnFrameworkThread(Arg.Any<Action>());
        chatGui.Received(1).Print("hello", Arg.Any<string?>(), Arg.Any<ushort?>());
    }

    [Fact]
    public void PrintError_RunsOnFrameworkThread()
    {
        var chatGui = Substitute.For<IChatGui>();
        var framework = MakeInlineFramework();
        var notifier = new ChatNotifier(chatGui, framework);

        notifier.PrintError("oops");

        framework.Received(1).RunOnFrameworkThread(Arg.Any<Action>());
        chatGui.Received(1).PrintError("oops", Arg.Any<string?>(), Arg.Any<ushort?>());
    }

    [Fact]
    public void Print_NeverTouchesChatOffTheFrameworkThread()
    {
        // An unconfigured framework substitute does NOT run the action;
        // chat must then never be reached.
        var chatGui = Substitute.For<IChatGui>();
        var framework = Substitute.For<IFramework>();
        var notifier = new ChatNotifier(chatGui, framework);

        notifier.Print("hello");

        chatGui.DidNotReceive().Print(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<ushort?>());
    }
}
