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
using SpotifyHonorific.Activities;
using System.Numerics;

namespace SpotifyHonorific.Updaters;

public class Updater : IDisposable
{
    private const ushort MAX_TITLE_LENGTH = 32;
    private const double POLLING_INTERVAL_SECONDS = 2.0;
    private const uint AFK_THRESHOLD_MS = 30000;

    private IChatGui ChatGui { get; init; }
    private Config Config { get; init; }
    private IFramework Framework { get; init; }
    private IPluginLog PluginLog { get; init; }
    private ICallGateSubscriber<int, string, object> SetCharacterTitleSubscriber { get; init; }
    private ICallGateSubscriber<int, object> ClearCharacterTitleSubscriber { get; init; }

    public bool IsPlayerAfk { get; private set; } = false;

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

    private readonly Dictionary<string, Template> _templateCache = [];

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
        GC.SuppressFinalize(this);
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (HandleAfkStatus()) return;

        ProcessTitleUpdate(framework.UpdateDelta.TotalSeconds);
        HandlePolling(framework.UpdateDelta.TotalSeconds);
    }

    private bool HandleAfkStatus()
    {
        try
        {
            IsPlayerAfk = NativeMethods.IdleTimeFinder.GetIdleTime() > AFK_THRESHOLD_MS;
        }
        catch (Exception e)
        {
            PluginLog.Warning(e, "Could not get system idle time.");
            IsPlayerAfk = false;
        }

        if (IsPlayerAfk && !_isMusicPlaying)
        {
            if (!_hasLoggedAfk)
            {
                PluginLog.Debug("Player is AFK and no music is playing, stopping polling.");
                _hasLoggedAfk = true;
            }
            ClearTitle();
            _pollingTimer = 0.0;
            return true;
        }

        _hasLoggedAfk = false;
        return false;
    }

    private void ProcessTitleUpdate(double deltaSeconds)
    {
        if (UpdateTitle == null) return;

        UpdaterContext.SecsElapsed += deltaSeconds;
        try
        {
            UpdateTitle();
        }
        catch (Exception e)
        {
            PluginLog.Error(e.ToString());
        }
    }

    private void HandlePolling(double deltaSeconds)
    {
        if (!Config.Enabled)
        {
            if (UpdatedTitleJson != null)
            {
                ClearTitle();
            }
            return;
        }

        _pollingTimer += deltaSeconds;

        if (_pollingTimer < POLLING_INTERVAL_SECONDS || Config.SpotifyRefreshToken.IsNullOrWhitespace() || _isPolling)
        {
            return;
        }

        if (Config.EnableDebugLogging)
        {
            PluginLog.Debug($"POLLING NOW. Timer: {_pollingTimer:F2}/{POLLING_INTERVAL_SECONDS}s | IsPlaying: {_isMusicPlaying}");
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
            var spotify = await GetSpotifyClient().ConfigureAwait(false);
            if (spotify == null)
            {
                HandleSpotifyError(null, "Spotify client is null, likely not authenticated.");
                return;
            }

            var currentlyPlaying = await spotify.Player.GetCurrentlyPlaying(new PlayerCurrentlyPlayingRequest()).ConfigureAwait(false);
            if (currentlyPlaying != null && currentlyPlaying.IsPlaying && currentlyPlaying.Item is FullTrack track)
            {
                _isMusicPlaying = true;
                ProcessCurrentlyPlayingTrack(track);
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
            HandleSpotifyError(e, "Error polling Spotify. Token may be expired.");
        }
        catch (Exception e)
        {
            HandleSpotifyError(e, "Unhandled error during Spotify poll");
        }
        finally
        {
            _isPolling = false;
        }
    }

    private void ProcessCurrentlyPlayingTrack(FullTrack track)
    {
        if (track.Id == CurrentTrackId)
        {
            return;
        }

        CurrentTrackId = track.Id;

        var activityConfig = Config.ActivityConfigs.Where(c => c.Enabled).OrderByDescending(c => c.Priority).FirstOrDefault();
        if (activityConfig == null)
        {
            ClearTitle();
            return;
        }

        UpdaterContext.SecsElapsed = 0;
        UpdateTitle = CreateTitleUpdateAction(activityConfig, track);
    }

    private Action CreateTitleUpdateAction(ActivityConfig activityConfig, FullTrack track)
    {
        return () =>
        {
            if (!Config.Enabled || !activityConfig.Enabled)
            {
                ClearTitle();
                return;
            }

            RenderAndSetTitle(activityConfig, track);
        };
    }

    private void RenderAndSetTitle(ActivityConfig activityConfig, FullTrack track)
    {
        if (!_templateCache.TryGetValue(activityConfig.TitleTemplate, out var titleTemplate))
        {
            titleTemplate = Template.Parse(activityConfig.TitleTemplate);
            _templateCache[activityConfig.TitleTemplate] = titleTemplate;
        }

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

        Vector3? colorToUse = activityConfig.Color;

        if (activityConfig.RainbowMode)
        {
            float hue = (float)(UpdaterContext.SecsElapsed * 0.5) % 1.0f;
            colorToUse = HsvToRgb(hue, 1.0f, 1.0f);
        }

        var data = new Dictionary<string, object>() {
            {"Title", title},
            {"IsPrefix", activityConfig.IsPrefix},
            {"Color", colorToUse!},
            {"Glow", activityConfig.Glow!}
        };

        var serializedData = JsonConvert.SerializeObject(data, Formatting.Indented);
        if (serializedData == UpdatedTitleJson) return;

        if (Config.EnableDebugLogging)
        {
            PluginLog.Debug($"Call Honorific SetCharacterTitle IPC with:\n{serializedData}");
        }
        SetCharacterTitleSubscriber.InvokeAction(0, serializedData);
        UpdatedTitleJson = serializedData;
    }

    private static Vector3 HsvToRgb(float h, float s, float v)
    {
        var i = (int)(h * 6);
        var f = h * 6 - i;

        var p = v * (1 - s);
        var q = v * (1 - f * s);
        var t = v * (1 - (1 - f) * s);

        return (i % 6) switch
        {
            0 => new(v, t, p),
            1 => new(q, v, p),
            2 => new(p, v, t),
            3 => new(p, q, v),
            4 => new(t, p, v),
            _ => new(v, p, q)
        };
    }

    private void HandleSpotifyError(Exception? e, string message)
    {
        if (e != null)
        {
            PluginLog.Warning(e, message);
        }
        else
        {
            PluginLog.Warning(message);
        }

        CurrentAccessToken = null;
        Spotify = null;
        CurrentTrackId = null;
        _isMusicPlaying = false;
        ClearTitle();
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
            ).ConfigureAwait(false);

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
