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
    private double _pollingTimer = 0.0;
    private bool _isPolling = false;
    private bool _isMusicPlaying = false;
    private string? CurrentTrackId { get; set; }
    private bool _hasLoggedAfk = false;

    public Updater(IChatGui chatGui, Config config, IFramework framework, IDalamudPluginInterface pluginInterface, IPluginLog pluginLog)
    {
        ChatGui = chatGui;
        Config = config;
        Framework = framework;
        PluginLog = pluginLog;

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

        if (this.IsPlayerAfk && !this._isMusicPlaying)
        {
            if (!_hasLoggedAfk)
            {
                PluginLog.Debug("[SpotifyHonorific] Player is AFK and no music is playing, stopping polling.");
                _hasLoggedAfk = true;
            }
            ClearTitle();
            _pollingTimer = 0.0;
            return;
        }
        else
        {
            _hasLoggedAfk = false;
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

        _pollingTimer += framework.UpdateDelta.TotalSeconds;

        double currentInterval = POLLING_INTERVAL_SECONDS;

        if (_pollingTimer < currentInterval || Config.SpotifyRefreshToken.IsNullOrWhitespace() || _isPolling)
        {
            return;
        }

        if (Config.EnableDebugLogging)
        {
            PluginLog.Debug($"[SpotifyHonorific] POLLING NOW. Timer: {_pollingTimer:F2}/{currentInterval}s | IsPlaying: {_isMusicPlaying}");
        }

        _pollingTimer = 0.0;

        _ = PollSpotify();
    }

    private async Task PollSpotify()
    {
        if (_isPolling) return;
        _isPolling = true;

        try
        {
            var spotify = await GetSpotifyClient();
            if (spotify == null)
            {
                _isMusicPlaying = false;
                ClearTitle();
                _isPolling = false;
                return;
            }

            var currentlyPlaying = await spotify.Player.GetCurrentlyPlaying(new PlayerCurrentlyPlayingRequest());
            if (currentlyPlaying != null && currentlyPlaying.IsPlaying && currentlyPlaying.Item is FullTrack track)
            {
                _isMusicPlaying = true;
                if (track.Id == CurrentTrackId)
                {
                    _isPolling = false;
                    return;
                }

                CurrentTrackId = track.Id;

                var activityConfig = Config.ActivityConfigs.Where(c => c.Enabled).OrderByDescending(c => c.Priority).FirstOrDefault();
                if (activityConfig == null)
                {
                    ClearTitle();
                    _isPolling = false;
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
                _isMusicPlaying = false;
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
            _isMusicPlaying = false;
            ClearTitle();
        }
        catch (Exception e)
        {
            PluginLog.Error(e, "Unhandled error during Spotify poll");
            CurrentTrackId = null;
            _isMusicPlaying = false;
            ClearTitle();
        }
        finally
        {
            _isPolling = false;
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