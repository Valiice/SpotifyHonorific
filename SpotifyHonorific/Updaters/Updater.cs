using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using Newtonsoft.Json;
using Scriban;
using SpotifyAPI.Web;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SpotifyHonorific.Utils;

namespace SpotifyHonorific.Updaters;

public class Updater : IDisposable
{
    private static readonly ushort MAX_TITLE_LENGTH = 32;
    private const double POLLING_INTERVAL_SECONDS = 2.0;

    private IChatGui ChatGui { get; init; }
    private Config Config { get; init; }
    private IFramework Framework { get; init; }
    private IPluginLog PluginLog { get; init; }
    private IClientState ClientState { get; init; }

    private ICallGateSubscriber<int, string, object> SetCharacterTitleSubscriber { get; init; }
    private ICallGateSubscriber<int, object> ClearCharacterTitleSubscriber { get; init; }

    public bool IsPlayerAfk { get; private set; } = false;
    private const uint AfkThreshold = 30000; // 30 seconds in milliseconds

    private Action? UpdateTitle { get; set; }
    private string? UpdatedTitleJson { get; set; }
    private UpdaterContext UpdaterContext { get; init; } = new();
    private bool DisplayedMaxLengthError { get; set; } = false;

    private SpotifyClient? Spotify { get; set; }
    private string? CurrentAccessToken { get; set; }
    private double pollingTimer = 0.0;
    private bool isPolling = false;
    private bool IsMusicPlaying = false;
    private string? CurrentTrackId { get; set; }
    private bool hasLoggedAfk = false;

    public Updater(IChatGui chatGui, Config config, IFramework framework, IDalamudPluginInterface pluginInterface, IPluginLog pluginLog, IClientState clientState)
    {
        ChatGui = chatGui;
        Config = config;
        Framework = framework;
        PluginLog = pluginLog;
        ClientState = clientState;

        SetCharacterTitleSubscriber = pluginInterface.GetIpcSubscriber<int, string, object>("Honorific.SetCharacterTitle");
        ClearCharacterTitleSubscriber = pluginInterface.GetIpcSubscriber<int, object>("Honorific.ClearCharacterTitle");

        Framework.Update += OnFrameworkUpdate;
    }

    public void Dispose()
    {
        Framework.Update -= OnFrameworkUpdate;
        Framework.RunOnFrameworkThread(() =>
        {
            ClearCharacterTitleSubscriber.InvokeAction(0);
        });
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        try
        {
            this.IsPlayerAfk = NativeMethods.IdleTimeFinder.GetIdleTime() > AfkThreshold;
        }
        catch (Exception e)
        {
            PluginLog.Warning(e, "Could not get system idle time.");
            this.IsPlayerAfk = false;
        }

        if (this.IsPlayerAfk)
        {
            if (!hasLoggedAfk)
            {
                PluginLog.Debug("[SpotifyHonorific] Player is AFK, stopping polling.");
                hasLoggedAfk = true;
            }
            ClearTitle();
            pollingTimer = 0.0;
            return;
        }
        else
        {
            hasLoggedAfk = false;
        }

        if (UpdateTitle != null)
        {
            UpdaterContext.SecsElapsed += framework.UpdateDelta.TotalSeconds;
            try
            {
                UpdateTitle();
            }
            catch (Exception e)
            {
                PluginLog.Error(e.ToString());
            }
        }

        if (!Config.Enabled)
        {
            if (UpdatedTitleJson != null)
            {
                ClearTitle();
            }
            return;
        }

        pollingTimer += framework.UpdateDelta.TotalSeconds;

        double currentInterval = POLLING_INTERVAL_SECONDS;

        if (pollingTimer < currentInterval || Config.SpotifyRefreshToken.IsNullOrWhitespace() || isPolling)
        {
            return;
        }

        if (Config.EnableDebugLogging)
        {
            var localPlayer = ClientState.LocalPlayer;
            var statusListText = new StringBuilder();
            statusListText.AppendLine($"[SpotifyHonorific] POLLING NOW. Timer: {pollingTimer:F2}/{currentInterval}s | IsPlaying: {IsMusicPlaying}");
            if (localPlayer != null)
            {
                var statuses = localPlayer.StatusList.ToList();
                if (statuses.Count == 0)
                {
                    statusListText.AppendLine("    Status List: (None)");
                }
                else
                {
                    statusListText.AppendLine("    Status List:");
                    foreach (var s in statuses)
                    {
                        string statusName = s.GameData.Value.Name.ToString() ?? "Unknown Name";
                        if (string.IsNullOrWhiteSpace(statusName))
                        {
                            statusName = "Unknown Name (Blank)";
                        }
                        statusListText.AppendLine($"        - ID: {s.StatusId}, Name: '{statusName}'");
                    }
                }
            }
            else
            {
                statusListText.AppendLine("    Status List: LocalPlayer is null");
            }
            PluginLog.Debug(statusListText.ToString());
        }

        pollingTimer = 0.0;

        _ = PollSpotify();
    }

    private async Task PollSpotify()
    {
        if (isPolling) return;
        isPolling = true;

        try
        {
            var spotify = await GetSpotifyClient();
            if (spotify == null)
            {
                IsMusicPlaying = false;
                ClearTitle();
                isPolling = false;
                return;
            }

            var currentlyPlaying = await spotify.Player.GetCurrentlyPlaying(new PlayerCurrentlyPlayingRequest());
            if (currentlyPlaying != null && currentlyPlaying.IsPlaying && currentlyPlaying.Item is FullTrack track)
            {
                IsMusicPlaying = true;
                if (track.Id == CurrentTrackId)
                {
                    isPolling = false;
                    return;
                }

                CurrentTrackId = track.Id;

                var activityConfig = Config.ActivityConfigs.Where(c => c.Enabled).OrderByDescending(c => c.Priority).FirstOrDefault();
                if (activityConfig == null)
                {
                    ClearTitle();
                    isPolling = false;
                    return;
                }

                UpdaterContext.SecsElapsed = 0;
                UpdateTitle = () =>
                {
                    if (!Config.Enabled || !activityConfig.Enabled)
                    {
                        ClearTitle();
                        return;
                    }

                    var titleTemplate = Template.Parse(activityConfig.TitleTemplate);
                    var title = titleTemplate.Render(new { Activity = track, Context = UpdaterContext }, member => member.Name);

                    if (title.Length > MAX_TITLE_LENGTH)
                    {
                        if (!DisplayedMaxLengthError)
                        {
                            var message = $"Title '{title}' is longer than {MAX_TITLE_LENGTH} characters, it won't be applied by honorific. Trim whitespaces or truncate variables to reduce the length.";
                            PluginLog.Error(message);
                            ChatGui.PrintError(message, "DiscordActivityHonorific");
                            DisplayedMaxLengthError = true;
                        }
                        return;
                    }
                    DisplayedMaxLengthError = false;

                    var data = new Dictionary<string, object>() {
                        {"Title", title},
                        {"IsPrefix", activityConfig.IsPrefix},
                        {"Color", activityConfig.Color!},
                        {"Glow", activityConfig.Glow!}
                    };

                    var serializedData = JsonConvert.SerializeObject(data, Formatting.Indented);
                    if (serializedData == UpdatedTitleJson) return;

                    PluginLog.Debug($"Call Honorific SetCharacterTitle IPC with:\n{serializedData}");
                    SetCharacterTitleSubscriber.InvokeAction(0, serializedData);
                    UpdatedTitleJson = serializedData;
                };
            }
            else
            {
                IsMusicPlaying = false;
                CurrentTrackId = null;
                ClearTitle();
            }
        }
        catch (APIException e)
        {
            PluginLog.Warning(e, "Error polling Spotify. Token may be expired.");
            CurrentAccessToken = null;
            Spotify = null;
            CurrentTrackId = null;
            IsMusicPlaying = false;
            ClearTitle();
        }
        catch (Exception e)
        {
            PluginLog.Error(e, "Unhandled error during Spotify poll");
            CurrentTrackId = null;
            IsMusicPlaying = false;
            ClearTitle();
        }
        finally
        {
            isPolling = false;
        }
    }

    private async Task<SpotifyClient?> GetSpotifyClient()
    {
        if (Config.SpotifyRefreshToken.IsNullOrWhitespace() || Config.SpotifyClientId.IsNullOrWhitespace() || Config.SpotifyClientSecret.IsNullOrWhitespace())
        {
            return null;
        }

        if (Spotify != null && CurrentAccessToken != null && Config.LastSpotifyAuthTime.AddMinutes(55) > DateTime.Now)
        {
            return Spotify;
        }

        PluginLog.Debug("Spotify token expired or missing, requesting new one...");
        try
        {
            var response = await new OAuthClient().RequestToken(
                new PKCETokenRefreshRequest(Config.SpotifyClientId, Config.SpotifyRefreshToken)
            );

            CurrentAccessToken = response.AccessToken;

            if (!string.IsNullOrEmpty(response.RefreshToken))
            {
                Config.SpotifyRefreshToken = response.RefreshToken;
            }

            Config.LastSpotifyAuthTime = DateTime.Now;
            Config.Save();

            Spotify = new SpotifyClient(CurrentAccessToken);
            return Spotify;
        }
        catch (Exception e)
        {
            PluginLog.Error(e, "Failed to refresh Spotify token!");
            Config.SpotifyRefreshToken = string.Empty;
            Config.Save();
            return null;
        }
    }

    private void ClearTitle()
    {
        if (UpdatedTitleJson == null) return;

        PluginLog.Debug("Call Honorific ClearCharacterTitle IPC");
        Framework.RunOnFrameworkThread(() =>
        {
            ClearCharacterTitleSubscriber.InvokeAction(0);
        });
        UpdaterContext.SecsElapsed = 0;
        UpdateTitle = null;
        UpdatedTitleJson = null;
        CurrentTrackId = null;
    }
}