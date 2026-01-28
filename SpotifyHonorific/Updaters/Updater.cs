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
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using SpotifyHonorific.Activities;
using SpotifyHonorific.Utils;

namespace SpotifyHonorific.Updaters;

public class Updater : IDisposable
{
    private const ushort MAX_TITLE_LENGTH = 32;
    private const double POLLING_INTERVAL_SECONDS = 2.0;
    private const uint AFK_THRESHOLD_MS = 30000;
    private const int API_TIMEOUT_MS = 5000;
    private const int TOKEN_REFRESH_WINDOW_MINUTES = 55;
    private const float RAINBOW_HUE_SPEED = 0.5f;
    private const int MAX_RETRY_ATTEMPTS = 3;

    private IChatGui ChatGui { get; init; }
    private Config Config { get; init; }
    private IFramework Framework { get; init; }
    private IPluginLog PluginLog { get; init; }
    private ICallGateSubscriber<int, string, object> SetCharacterTitleSubscriber { get; init; }
    private ICallGateSubscriber<int, object> ClearCharacterTitleSubscriber { get; init; }

    public bool IsPlayerAfk { get; private set; }

    private Action? UpdateTitle { get; set; }
    private string? UpdatedTitleJson { get; set; }
    private UpdaterContext UpdaterContext { get; init; } = new();
    private bool DisplayedMaxLengthError { get; set; }

    private SpotifyClient? Spotify { get; set; }
    private string? CurrentAccessToken { get; set; }
    private double _pollingTimer;
    private bool _isPolling;
    private bool _isMusicPlaying;
    private string? CurrentTrackId { get; set; }
    private bool _hasLoggedAfk;

    private readonly Dictionary<string, Template> _templateCache = new(16);

    private int _apiCallCount;
    private int _apiErrorCount;
    private int _cacheHits;
    private int _cacheMisses;
    private readonly Queue<long> _apiResponseTimes = new();
    private readonly HashSet<string> _tracksPlayedToday = new(100);
    private readonly DateTime _sessionStartTime;
    private const int MAX_RESPONSE_TIME_SAMPLES = 100;

    public Updater(IChatGui chatGui, Config config, IFramework framework, IDalamudPluginInterface pluginInterface, IPluginLog pluginLog)
    {
        ChatGui = chatGui;
        Config = config;
        Framework = framework;
        PluginLog = pluginLog;

        SetCharacterTitleSubscriber = pluginInterface.GetIpcSubscriber<int, string, object>("Honorific.SetCharacterTitle");
        ClearCharacterTitleSubscriber = pluginInterface.GetIpcSubscriber<int, object>("Honorific.ClearCharacterTitle");

        Framework.Update += OnFrameworkUpdate;
        _sessionStartTime = DateTime.Now;
    }

    public string GetPerformanceStats()
    {
        var sessionDuration = DateTime.Now - _sessionStartTime;
        var avgResponseTime = _apiResponseTimes.Count > 0 ? _apiResponseTimes.Average() : 0;
        var cacheHitRate = (_cacheHits + _cacheMisses) > 0
            ? (double)_cacheHits / (_cacheHits + _cacheMisses) * 100
            : 0;

        return $"""
            === SpotifyHonorific Performance Stats ===
            Session Duration: {sessionDuration:hh\:mm\:ss}

            API Statistics:
            • Total API calls: {_apiCallCount}
            • API errors: {_apiErrorCount}
            • Average response time: {avgResponseTime:F0}ms

            Template Cache:
            • Cache hits: {_cacheHits}
            • Cache misses: {_cacheMisses}
            • Hit rate: {cacheHitRate:F1}%
            • Cached templates: {_templateCache.Count}

            Music:
            • Unique tracks today: {_tracksPlayedToday.Count}
            • Currently playing: {(_isMusicPlaying ? "Yes" : "No")}
            • Player AFK: {(IsPlayerAfk ? "Yes" : "No")}
            """;
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
            PluginLog.Error(e, "Failed to update title");
            ChatGui.PrintError($"SpotifyHonorific: Failed to update title. Check /xllog for details.");
            UpdateTitle = null;
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

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            using var cts = new CancellationTokenSource(API_TIMEOUT_MS);

            var spotify = await RetryAsync(
                () => GetSpotifyClient(cts.Token),
                maxRetries: MAX_RETRY_ATTEMPTS
            ).ConfigureAwait(false);

            if (spotify == null)
            {
                HandleSpotifyError(null, "Spotify client is null, likely not authenticated.");
                return;
            }

            var currentlyPlaying = await RetryAsync(
                () => spotify.Player.GetCurrentlyPlaying(new PlayerCurrentlyPlayingRequest(), cts.Token),
                maxRetries: MAX_RETRY_ATTEMPTS
            ).ConfigureAwait(false);

            stopwatch.Stop();
            _apiCallCount++;
            RecordResponseTime(stopwatch.ElapsedMilliseconds);

            if (currentlyPlaying != null && currentlyPlaying.IsPlaying && currentlyPlaying.Item is FullTrack track)
            {
                _isMusicPlaying = true;
                _tracksPlayedToday.Add(track.Id);
                ProcessCurrentlyPlayingTrack(track);
            }
            else
            {
                _isMusicPlaying = false;
                CurrentTrackId = null;
                ClearTitle();
            }
        }
        catch (OperationCanceledException)
        {
            _apiErrorCount++;
            HandleSpotifyError(null, "Spotify API request timed out after 5 seconds.");
        }
        catch (APIException e)
        {
            _apiErrorCount++;
            HandleSpotifyError(e, "Error polling Spotify. Token may be expired.");
        }
        catch (Exception e)
        {
            _apiErrorCount++;
            HandleSpotifyError(e, "Unhandled error during Spotify poll");
        }
        finally
        {
            _isPolling = false;
        }
    }

    private void RecordResponseTime(long milliseconds)
    {
        _apiResponseTimes.Enqueue(milliseconds);

        if (_apiResponseTimes.Count > MAX_RESPONSE_TIME_SAMPLES)
        {
            _apiResponseTimes.Dequeue();
        }
    }

    private async Task<T?> RetryAsync<T>(Func<Task<T>> operation, int maxRetries = MAX_RETRY_ATTEMPTS)
    {
        for (var attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                return await operation().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (attempt == maxRetries - 1)
                {
                    throw;
                }

                if (ex is APIException apiEx && apiEx.Response?.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    throw;
                }

                var delayMs = (int)Math.Pow(2, attempt) * 1000;

                if (Config.EnableDebugLogging)
                {
                    PluginLog.Debug($"Retry attempt {attempt + 1}/{maxRetries} after {delayMs}ms delay. Error: {ex.Message}");
                }

                await Task.Delay(delayMs).ConfigureAwait(false);
            }
        }

        return default;
    }

    private void ProcessCurrentlyPlayingTrack(FullTrack track)
    {
        if (track.Id == CurrentTrackId)
        {
            return;
        }

        CurrentTrackId = track.Id;

        var activityConfig = Config.WithLock(() =>
        {
            if (string.IsNullOrEmpty(Config.ActiveConfigName))
            {
                return Config.ActivityConfigs.Count > 0 ? Config.ActivityConfigs[0] : null;
            }

            foreach (var config in Config.ActivityConfigs)
            {
                if (config.Name == Config.ActiveConfigName)
                {
                    return config;
                }
            }

            return Config.ActivityConfigs.Count > 0 ? Config.ActivityConfigs[0] : null;
        });

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
            if (!Config.Enabled)
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
            _cacheMisses++;
            titleTemplate = Template.Parse(activityConfig.TitleTemplate);
            if (titleTemplate.HasErrors)
            {
                var errorMessage = $"Template parsing failed: {string.Join(", ", titleTemplate.Messages)}";
                PluginLog.Error(errorMessage);
                ChatGui.PrintError($"SpotifyHonorific: {errorMessage}");
                return;
            }
            _templateCache[activityConfig.TitleTemplate] = titleTemplate;
        }
        else
        {
            _cacheHits++;
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

        var colorToUse = activityConfig.Color;

        if (activityConfig.RainbowMode)
        {
            var hue = (float)(UpdaterContext.SecsElapsed * RAINBOW_HUE_SPEED) % 1.0f;
            colorToUse = HsvToRgb(hue, 1.0f, 1.0f);
        }

        var data = new Dictionary<string, object?>(4) {
            {"Title", title},
            {"IsPrefix", activityConfig.IsPrefix},
            {"Color", colorToUse},
            {"Glow", activityConfig.Glow}
        };

        var serializedData = JsonConvert.SerializeObject(data, Formatting.None);
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
        var f = (h * 6) - i;

        var p = v * (1 - s);
        var q = v * (1 - (f * s));
        var t = v * (1 - ((1 - f) * s));

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

        if (Config.EnableDebugLogging)
        {
            ChatGui.PrintError($"SpotifyHonorific: {message}");
        }

        CurrentAccessToken = null;
        Spotify = null;
        CurrentTrackId = null;
        _isMusicPlaying = false;
        ClearTitle();
    }

    private async Task<SpotifyClient?> GetSpotifyClient(CancellationToken cancellationToken = default)
    {
        var (refreshToken, clientId, clientSecret, lastAuthTime) = Config.WithLock(() =>
            (Config.SpotifyRefreshToken, Config.SpotifyClientId, Config.SpotifyClientSecret, Config.LastSpotifyAuthTime));

        if (refreshToken.IsNullOrWhitespace() || clientId.IsNullOrWhitespace() || clientSecret.IsNullOrWhitespace())
        {
            return null;
        }

        if (Spotify != null && CurrentAccessToken != null && lastAuthTime.AddMinutes(TOKEN_REFRESH_WINDOW_MINUTES) > DateTime.Now)
        {
            return Spotify;
        }

        PluginLog.Debug("Spotify token expired or missing, requesting new one...");
        try
        {
            var response = await new OAuthClient().RequestToken(
                new PKCETokenRefreshRequest(clientId, refreshToken),
                cancellationToken
            ).ConfigureAwait(false);

            CurrentAccessToken = response.AccessToken;

            Config.WithLock(() =>
            {
                if (!string.IsNullOrEmpty(response.RefreshToken))
                {
                    Config.SpotifyRefreshToken = response.RefreshToken;
                }
                Config.LastSpotifyAuthTime = DateTime.Now;
                Config.Save();
            });

            Spotify = new SpotifyClient(CurrentAccessToken);
            return Spotify;
        }
        catch (Exception e)
        {
            PluginLog.Error(e, "Failed to refresh Spotify token!");
            Config.WithLock(() =>
            {
                Config.SpotifyRefreshToken = string.Empty;
                Config.Save();
            });
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
