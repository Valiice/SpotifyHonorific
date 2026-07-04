using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using SpotifyHonorific.Authentication;
using SpotifyHonorific.Core;
using SpotifyHonorific.Windows;
using SpotifyHonorific.Updaters;
using SpotifyHonorific.Activities;
using System;

namespace SpotifyHonorific;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IPluginLog PluginLog { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static INotificationManager NotificationManager { get; private set; } = null!;
    [PluginService] internal static IContextMenu ContextMenu { get; private set; } = null!;

    private const string CommandName = "/spotifyhonorific";
    private const string CommandHelpMessage = $"Use {CommandName} config to open the settings window, {CommandName} nearby to see nearby players' titles, or {CommandName} stats to view performance statistics.";

    public Config Config { get; init; }

    public readonly WindowSystem WindowSystem = new("SpotifyHonorific");
    private ConfigWindow ConfigWindow { get; init; }
    private NearbyListeningWindow NearbyListeningWindow { get; init; }
    private Updater Updater { get; init; }
    private PlaybackState PlaybackState { get; init; }
    private NearbyTitleWatcher NearbyTitleWatcher { get; init; }
    private RecentTitleCache RecentTitleCache { get; init; }
    private SpotifyPollingService SpotifyPollingService { get; init; }
    private HonorificTitleReader HonorificTitleReader { get; init; }
    private SpotifyAuthenticator SpotifyAuthenticator { get; init; }
    private TrackQueueService TrackQueueService { get; init; }
    private NearbyTrackContextMenu NearbyTrackContextMenu { get; init; }

    public Plugin()
    {
        Config = PluginInterface.GetPluginConfig() as Config ?? new Config(ActivityConfig.GetDefaults());
        Config.Initialize(PluginInterface);

        if (string.IsNullOrEmpty(Config.ActiveConfigName) && Config.ActivityConfigs.Count > 0)
        {
            Config.ActiveConfigName = Config.ActivityConfigs[0].Name;
            Config.Save();
        }

        PlaybackState = new PlaybackState();
        HonorificTitleReader = new HonorificTitleReader(PluginInterface, PluginLog);
        RecentTitleCache = new RecentTitleCache();
        NearbyTitleWatcher = new NearbyTitleWatcher(ObjectTable, HonorificTitleReader, RecentTitleCache);
        SpotifyPollingService = new SpotifyPollingService(Config, PluginLog, ChatGui);
        Updater = new(ChatGui, Config, Framework, PluginInterface, PluginLog, ClientState, ObjectTable, PlaybackState, NotificationManager, NearbyTitleWatcher, SpotifyPollingService);
        TrackQueueService = new TrackQueueService(SpotifyPollingService, PluginLog, ChatGui);
        NearbyTrackContextMenu = new NearbyTrackContextMenu(ContextMenu, HonorificTitleReader, RecentTitleCache, TrackQueueService, PluginLog, ChatGui);
        SpotifyAuthenticator = new SpotifyAuthenticator(Config, PluginLog);
        var nearbyListeningView = new NearbyListeningView(NearbyTitleWatcher, RecentTitleCache, TrackQueueService, ChatGui);
        ConfigWindow = new ConfigWindow(Config, new(), Updater, SpotifyAuthenticator, PlaybackState, nearbyListeningView);
        NearbyListeningWindow = new NearbyListeningWindow(nearbyListeningView);

        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(NearbyListeningWindow);
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
        NearbyTrackContextMenu.Dispose();
        SpotifyAuthenticator.Dispose();
        Updater.Dispose();
    }

    private void OnCommand(string command, string args)
    {
        var trimmedArgs = args.AsSpan().Trim();

        if (trimmedArgs.Equals("config", StringComparison.OrdinalIgnoreCase))
        {
            ToggleConfigUI();
        }
        else if (trimmedArgs.Equals("nearby", StringComparison.OrdinalIgnoreCase))
        {
            NearbyListeningWindow.Toggle();
        }
        else if (trimmedArgs.Equals("stats", StringComparison.OrdinalIgnoreCase))
        {
            var stats = Updater.GetPerformanceStats();
            ChatGui.Print(stats);
        }
        else
        {
            ChatGui.Print(CommandHelpMessage);
        }
    }

    private void DrawUI() => WindowSystem.Draw();

    public void ToggleConfigUI() => ConfigWindow.Toggle();
}
