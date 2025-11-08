using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using SpotifyHonorific.Windows;
using SpotifyHonorific.Updaters;
using SpotifyHonorific.Activities;

namespace SpotifyHonorific;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IPluginLog PluginLog { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;

    private const string CommandName = "/spotifyhonorific";
    private const string CommandHelpMessage = $"Use {CommandName} config to open the settings window.";

    public Config Config { get; init; }

    public readonly WindowSystem WindowSystem = new("SpotifyHonorific");
    private ConfigWindow ConfigWindow { get; init; }
    private Updater Updater { get; init; }


    public Plugin()
    {
        Config = PluginInterface.GetPluginConfig() as Config ?? new Config(ActivityConfig.GetDefaults());

        Updater = new(ChatGui, Config, Framework, PluginInterface, PluginLog);
        ConfigWindow = new ConfigWindow(Config, new(), Updater);

        WindowSystem.AddWindow(ConfigWindow);
        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = CommandHelpMessage
        });

        PluginInterface.UiBuilder.Draw += DrawUI;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUI;
        PluginInterface.UiBuilder.OpenMainUi += ToggleConfigUI;
    }

    public void Dispose()
    {
        WindowSystem.RemoveAllWindows();
        CommandManager.RemoveHandler(CommandName);
        Updater.Dispose();
    }

    private void OnCommand(string command, string args)
    {
        var subcommand = args.Split(" ", 2)[0];
        if (subcommand == "config")
        {
            ToggleConfigUI();
        }
        else
        {
            ChatGui.Print(CommandHelpMessage);
        }
    }

    private void DrawUI() => WindowSystem.Draw();

    public void ToggleConfigUI() => ConfigWindow.Toggle();
}