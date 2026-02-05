using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using SpotifyAPI.Web;
using SpotifyHonorific.Activities;
using SpotifyHonorific.Core;
using SpotifyHonorific.Utils;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SpotifyHonorific.Updaters;

public class Updater : IDisposable
{
    private const double POLLING_INTERVAL_SECONDS = 2.0;
    private const uint AFK_THRESHOLD_MS = 30000;

    private readonly IChatGui _chatGui;
    private readonly Config _config;
    private readonly IFramework _framework;
    private readonly IPluginLog _pluginLog;
    private readonly ICallGateSubscriber<int, string, object> _setCharacterTitleSubscriber;
    private readonly ICallGateSubscriber<int, object> _clearCharacterTitleSubscriber;

    private readonly SpotifyPollingService _pollingService;
    private readonly TitleRenderingService _renderingService;
    private readonly TemplateCache _templateCache;

    public bool IsPlayerAfk { get; private set; }

    private Action? _updateTitle;
    private string? _updatedTitleJson;
    private readonly UpdaterContext _updaterContext = new();

    private double _pollingTimer;
    private bool _isPolling;
    private bool _isMusicPlaying;
    private string? _currentTrackId;
    private bool _hasLoggedAfk;

    private readonly HashSet<string> _tracksPlayedToday = new(100);
    private readonly DateTime _sessionStartTime;

    public Updater(IChatGui chatGui, Config config, IFramework framework, IDalamudPluginInterface pluginInterface, IPluginLog pluginLog)
    {
        _chatGui = chatGui;
        _config = config;
        _framework = framework;
        _pluginLog = pluginLog;

        _setCharacterTitleSubscriber = pluginInterface.GetIpcSubscriber<int, string, object>("Honorific.SetCharacterTitle");
        _clearCharacterTitleSubscriber = pluginInterface.GetIpcSubscriber<int, object>("Honorific.ClearCharacterTitle");

        // Initialize services
        _templateCache = new TemplateCache(pluginLog);
        _pollingService = new SpotifyPollingService(config, pluginLog, chatGui);
        _renderingService = new TitleRenderingService(_templateCache, pluginLog, chatGui);

        _framework.Update += OnFrameworkUpdate;
        _sessionStartTime = DateTime.Now;
    }

    public string GetPerformanceStats()
    {
        var sessionDuration = DateTime.Now - _sessionStartTime;

        return $"""
            === SpotifyHonorific Performance Stats ===
            Session Duration: {sessionDuration:hh\:mm\:ss}

            API Statistics:
            • Total API calls: {_pollingService.ApiCallCount}
            • API errors: {_pollingService.ApiErrorCount}
            • Average response time: {_pollingService.AverageResponseTime:F0}ms

            Template Cache:
            • Cache hits: {_templateCache.CacheHits}
            • Cache misses: {_templateCache.CacheMisses}
            • Hit rate: {_templateCache.HitRate:F1}%
            • Cached templates: {_templateCache.CachedTemplateCount}

            Music:
            • Unique tracks today: {_tracksPlayedToday.Count}
            • Currently playing: {(_isMusicPlaying ? "Yes" : "No")}
            • Player AFK: {(IsPlayerAfk ? "Yes" : "No")}
            """;
    }

    public void Dispose()
    {
        _framework.Update -= OnFrameworkUpdate;
        _framework.RunOnFrameworkThread(() =>
        {
            _clearCharacterTitleSubscriber.InvokeAction(0);
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
            _pluginLog.Warning(e, "Could not get system idle time.");
            IsPlayerAfk = false;
        }

        if (IsPlayerAfk && !_isMusicPlaying)
        {
            if (!_hasLoggedAfk)
            {
                _pluginLog.Debug("Player is AFK and no music is playing, stopping polling.");
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
        if (_updateTitle == null) return;

        _updaterContext.SecsElapsed += deltaSeconds;
        try
        {
            _updateTitle();
        }
        catch (Exception e)
        {
            _pluginLog.Error(e, "Failed to update title");
            _chatGui.PrintError($"SpotifyHonorific: Failed to update title. Check /xllog for details.");
            _updateTitle = null;
        }
    }

    private void HandlePolling(double deltaSeconds)
    {
        if (!_config.Enabled)
        {
            if (_updatedTitleJson != null)
            {
                ClearTitle();
            }
            return;
        }

        _pollingTimer += deltaSeconds;

        if (_pollingTimer < POLLING_INTERVAL_SECONDS || _config.SpotifyRefreshToken.IsNullOrWhitespace() || _isPolling)
        {
            return;
        }

        if (_config.EnableDebugLogging)
        {
            _pluginLog.Debug($"POLLING NOW. Timer: {_pollingTimer:F2}/{POLLING_INTERVAL_SECONDS}s | IsPlaying: {_isMusicPlaying}");
        }

        _pollingTimer = 0.0;
        _ = PollSpotifyAsync();
    }

    private async Task PollSpotifyAsync()
    {
        if (_isPolling) return;
        _isPolling = true;

        try
        {
            var track = await _pollingService.GetCurrentlyPlayingTrackAsync().ConfigureAwait(false);

            if (track != null)
            {
                _isMusicPlaying = true;
                _tracksPlayedToday.Add(track.Id);
                ProcessCurrentlyPlayingTrack(track);
            }
            else
            {
                _isMusicPlaying = false;
                _currentTrackId = null;
                ClearTitle();
            }
        }
        finally
        {
            _isPolling = false;
        }
    }

    private void ProcessCurrentlyPlayingTrack(FullTrack track)
    {
        if (track.Id == _currentTrackId)
        {
            return;
        }

        _currentTrackId = track.Id;

        var activityConfig = _config.WithLock(() =>
            ValidationHelper.FindActiveConfig(_config.ActivityConfigs, _config.ActiveConfigName));

        if (activityConfig == null)
        {
            ClearTitle();
            return;
        }

        _updaterContext.SecsElapsed = 0;
        _updateTitle = CreateTitleUpdateAction(activityConfig, track);
    }

    private Action CreateTitleUpdateAction(ActivityConfig activityConfig, FullTrack track)
    {
        return () =>
        {
            if (!_config.Enabled)
            {
                ClearTitle();
                return;
            }

            RenderAndSetTitle(activityConfig, track);
        };
    }

    private void RenderAndSetTitle(ActivityConfig activityConfig, FullTrack track)
    {
        var title = _renderingService.RenderTitle(activityConfig, track, _updaterContext);
        if (title == null) return;

        var serializedData = _renderingService.SerializeTitleData(title, activityConfig, _updaterContext);
        if (serializedData == _updatedTitleJson) return;

        if (_config.EnableDebugLogging)
        {
            _pluginLog.Debug($"Call Honorific SetCharacterTitle IPC with:\n{serializedData}");
        }

        _setCharacterTitleSubscriber.InvokeAction(0, serializedData);
        _updatedTitleJson = serializedData;
    }

    private void ClearTitle()
    {
        if (_updatedTitleJson == null) return;

        _pluginLog.Debug("Call Honorific ClearCharacterTitle IPC");
        _framework.RunOnFrameworkThread(() =>
        {
            _clearCharacterTitleSubscriber.InvokeAction(0);
        });
        _updaterContext.SecsElapsed = 0;
        _updateTitle = null;
        _updatedTitleJson = null;
        _currentTrackId = null;
    }
}
