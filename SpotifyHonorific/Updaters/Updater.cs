using Dalamud.Interface.ImGuiNotification;
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
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace SpotifyHonorific.Updaters;

public class Updater : IDisposable
{
    private const double POLLING_INTERVAL_SECONDS = 2.0;
    internal const double PROTECTED_EDGE_POLL_SECONDS = 5.0;
    internal const double PROTECTED_MID_POLL_SECONDS = 10.0;
    internal const double PROTECTED_IDLE_POLL_SECONDS = 15.0;
    internal const int TRACK_START_WINDOW_MS = 30_000;
    internal const int TRACK_END_WINDOW_MS = 20_000;
    private const double AUTH_NOTIFICATION_COOLDOWN_SECONDS = 600.0;
    private const uint AFK_ONLINE_STATUS_ID = 17;
    private const double AFK_MUSIC_GRACE_SECONDS = 10.0;
    internal const double TEXT_RENDER_INTERVAL_SECONDS = 0.5;
    internal const double RAINBOW_RENDER_INTERVAL_SECONDS = 0.1;

    private readonly IChatGui _chatGui;
    private readonly Config _config;
    private readonly IFramework _framework;
    private readonly IPluginLog _pluginLog;
    private readonly INotificationManager _notificationManager;
    private readonly ICallGateSubscriber<int, string, object> _setCharacterTitleSubscriber;
    private readonly ICallGateSubscriber<int, object> _clearCharacterTitleSubscriber;

    private readonly SpotifyPollingService _pollingService;
    private readonly TitleRenderingService _renderingService;
    private readonly TemplateCache _templateCache;

    public bool IsPlayerAfk { get; private set; }

    private readonly TitleUpdateState _titleState = new();
    private readonly PlaybackState _playbackState;
    private readonly UpdaterContext _updaterContext = new();
    private readonly IClientState _clientState;
    private readonly IObjectTable _objectTable;
    private readonly NearbyTitleWatcher _nearbyTitleWatcher;

    private double _pollingTimer;
    private double _currentPollIntervalSeconds = POLLING_INTERVAL_SECONDS;

    private bool _isPolling;
    private bool _isMusicPlaying;
    private string? _currentTrackId;
    private bool _hasLoggedAfk;
    private double _authNotificationTimer;
    private double _musicOffSeconds;
    private double _renderTimer;
    private double _renderInterval = TEXT_RENDER_INTERVAL_SECONDS;
    private bool _isTitleTimeDependent;
    private int _lastSeenConfigRevision;

    private readonly HashSet<string> _tracksPlayedThisSession = new(100);
    private readonly DateTime _sessionStartTime;

    private static readonly string PluginVersion =
        typeof(Updater).Assembly.GetName().Version?.ToString(3) ?? "unknown";

    public Updater(IChatGui chatGui, Config config, IFramework framework, IDalamudPluginInterface pluginInterface, IPluginLog pluginLog, IClientState clientState, IObjectTable objectTable, PlaybackState playbackState, INotificationManager notificationManager, NearbyTitleWatcher nearbyTitleWatcher, SpotifyPollingService spotifyPollingService)
    {
        _chatGui = chatGui;
        _config = config;
        _framework = framework;
        _pluginLog = pluginLog;
        _clientState = clientState;
        _objectTable = objectTable;
        _playbackState = playbackState;
        _notificationManager = notificationManager;
        _nearbyTitleWatcher = nearbyTitleWatcher;

        _setCharacterTitleSubscriber = pluginInterface.GetIpcSubscriber<int, string, object>("Honorific.SetCharacterTitle");
        _clearCharacterTitleSubscriber = pluginInterface.GetIpcSubscriber<int, object>("Honorific.ClearCharacterTitle");

        _templateCache = new TemplateCache(pluginLog);
        _pollingService = spotifyPollingService;
        _renderingService = new TitleRenderingService(_templateCache, pluginLog, chatGui);

        _framework.Update += OnFrameworkUpdate;
        _clientState.TerritoryChanged += OnTerritoryChanged;
        _sessionStartTime = DateTime.Now;
        _lastSeenConfigRevision = config.Revision;
    }

    public string GetPerformanceStats()
    {
        var now = DateTime.Now;
        var sessionDuration = now - _sessionStartTime;
        var requestsPerMinute = (_pollingService.ApiCallCount + _pollingService.ApiErrorCount)
            / Math.Max(sessionDuration.TotalMinutes, 1.0 / 60.0);
        var rateLimitRemaining = _pollingService.RateLimitGate.Remaining(now);
        var rateLimitedText = rateLimitRemaining > TimeSpan.Zero
            ? $"Yes ({rateLimitRemaining.TotalSeconds:0}s remaining)"
            : "No";
        var lastRetryAfterText = _pollingService.LastRetryAfter is { } retryAfter
            ? $"{retryAfter.TotalSeconds:0}s"
            : "never";
        var protectionText = _config.RateLimitProtection
            ? $"On (current interval: {_currentPollIntervalSeconds:0}s)"
            : "Off";

        return $"""
            === SpotifyHonorific Performance Stats ===
            Plugin version: {PluginVersion}
            Session Duration: {sessionDuration:hh\:mm\:ss}

            API Statistics:
            • Total API calls: {_pollingService.ApiCallCount}
            • API errors: {_pollingService.ApiErrorCount}
            • Requests per minute: {requestsPerMinute:F1}
            • Average response time: {_pollingService.AverageResponseTime:F0}ms
            • Token refreshes: {_pollingService.TokenRefreshCount}

            Rate Limiting:
            • 429s this session: {_pollingService.RateLimit429Count}
            • Rate limited: {rateLimitedText}
            • Last Retry-After: {lastRetryAfterText}
            • Rate limit protection: {protectionText}

            Template Cache:
            • Cache hits: {_templateCache.CacheHits}
            • Cache misses: {_templateCache.CacheMisses}
            • Hit rate: {_templateCache.HitRate:F1}%
            • Cached templates: {_templateCache.CachedTemplateCount}

            Music:
            • Unique tracks this session: {_tracksPlayedThisSession.Count}
            • Currently playing: {(_isMusicPlaying ? "Yes" : "No")}
            • Player AFK: {(IsPlayerAfk ? "Yes" : "No")}
            """;
    }

    private static readonly JsonSerializerOptions ReportJsonOptions = new()
    {
        WriteIndented = true,
        // ActivityConfig colors are Vector4: its components are public FIELDS,
        // which System.Text.Json skips by default.
        IncludeFields = true,
    };

    public string GetDiagnosticReportJson()
    {
        var now = DateTime.Now;
        var sessionDuration = now - _sessionStartTime;

        var (authenticated, clientIdConfigured, lastAuthTime, configNode) = _config.WithLock(() =>
        {
            var node = JsonSerializer.SerializeToNode(_config, ReportJsonOptions)!.AsObject();
            // Secrets denylist: see the comment on these properties in Config.cs.
            node.Remove("SpotifyClientId");
            node.Remove("SpotifyRefreshToken");
            return (!_config.SpotifyRefreshToken.IsNullOrWhitespace(),
                    !_config.SpotifyClientId.IsNullOrWhitespace(),
                    _config.LastSpotifyAuthTime,
                    node);
        });

        var gate = _pollingService.RateLimitGate;
        var track = _playbackState.CurrentTrack;

        var report = new
        {
            meta = new
            {
                generatedAtLocal = now,
                generatedAtUtc = DateTime.UtcNow,
                pluginVersion = PluginVersion,
                dotnetRuntimeVersion = Environment.Version.ToString(),
                sessionStart = _sessionStartTime,
                sessionDurationSeconds = sessionDuration.TotalSeconds,
            },
            config = configNode,
            authentication = new
            {
                authenticated,
                clientIdConfigured,
                lastAuthTime,
                tokenAgeMinutes = (now - lastAuthTime).TotalMinutes,
            },
            api = new
            {
                totalCalls = _pollingService.ApiCallCount,
                errorCount = _pollingService.ApiErrorCount,
                requestsPerMinute = (_pollingService.ApiCallCount + _pollingService.ApiErrorCount)
                    / Math.Max(sessionDuration.TotalMinutes, 1.0 / 60.0),
                averageResponseMs = _pollingService.AverageResponseTime,
                tokenRefreshCount = _pollingService.TokenRefreshCount,
                responseTimesMs = _pollingService.GetResponseTimeSnapshot(),
            },
            rateLimit = new
            {
                count429 = _pollingService.RateLimit429Count,
                lastRetryAfterSeconds = _pollingService.LastRetryAfter?.TotalSeconds,
                gateActive = gate.IsActive(now),
                gateRemainingSeconds = gate.Remaining(now).TotalSeconds,
                fallbackEscalationCount = gate.FallbackEscalationCount,
                protectionEnabled = _config.RateLimitProtection,
                currentPollIntervalSeconds = _currentPollIntervalSeconds,
            },
            templateCache = new
            {
                hits = _templateCache.CacheHits,
                misses = _templateCache.CacheMisses,
                hitRatePercent = _templateCache.HitRate,
                cachedTemplateCount = _templateCache.CachedTemplateCount,
            },
            nearby = BuildNearbySection(_nearbyTitleWatcher.History, _nearbyTitleWatcher.RecentTitleCache, now, _config.Enabled),
            music = new
            {
                currentlyPlaying = _isMusicPlaying,
                uniqueTracksSession = _tracksPlayedThisSession.Count,
                currentTrackName = track?.Name,
                currentTrackArtists = track?.Artists.Select(a => a.Name).ToList(),
            },
            player = new
            {
                isAfk = IsPlayerAfk,
                isLoggedIn = _clientState.IsLoggedIn,
            },
            events = _pollingService.GetEventSnapshot(),
        };

        return JsonSerializer.Serialize(report, ReportJsonOptions);
    }

    // Character names must never leave the machine: each distinct character
    // becomes a stable per-report placeholder so history and cache entries can
    // still be correlated. Honorific title text (song/artist) is fine to keep.
    internal static object BuildNearbySection(IReadOnlyList<NearbyPlayerEntry> history, RecentTitleCache cache, DateTime now, bool watcherEnabled)
    {
        var placeholders = new Dictionary<string, string>();

        string Anon(string name)
        {
            if (!placeholders.TryGetValue(name, out var alias))
            {
                alias = $"player{placeholders.Count + 1}";
                placeholders[name] = alias;
            }
            return alias;
        }

        return new
        {
            watcherEnabled,
            historyCount = history.Count,
            history = history
                .Select(e => new { player = Anon(e.CharacterName), title = e.RawTitle, lastSeen = e.LastSeen })
                .ToList(),
            titleCache = cache.GetDiagnosticSnapshot(now)
                .Select(s => new { player = Anon(s.CharacterName), knownSpotifyListener = s.KnownListener, freshSampleCount = s.FreshSampleCount })
                .ToList(),
        };
    }

    public void Dispose()
    {
        _framework.Update -= OnFrameworkUpdate;
        _clientState.TerritoryChanged -= OnTerritoryChanged;
        _framework.RunOnFrameworkThread(() =>
        {
            _clearCharacterTitleSubscriber.InvokeAction(0);
        });
        GC.SuppressFinalize(this);
    }

    private void OnTerritoryChanged(uint territoryId)
    {
        _titleState.ForceResend();
        _renderTimer = _renderInterval; // pre-charge so the re-send lands on the next frame
    }

    // Config edits mutate the live ActivityConfig instances, and a render-once
    // static title would freeze those edits. Every edit path ends in
    // Config.Save(), so a revision bump invalidates the cached action; the next
    // poll cycle rebuilds it against the edited config. When the plugin is
    // disabled we must NOT clear _titleState here: HandlePolling's disabled
    // branch needs LastSentJson intact to know it still has to clear the title
    // on Honorific's side.
    private void CheckConfigRevision()
    {
        if (_config.Revision == _lastSeenConfigRevision) return;
        _lastSeenConfigRevision = _config.Revision;

        if (!_config.Enabled) return;

        _titleState.Clear();
        _currentTrackId = null;
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        var deltaSeconds = framework.UpdateDelta.TotalSeconds;
        UpdateMusicOffTimer(deltaSeconds);

        // Nearby scanning respects the plugin's on/off toggle like everything
        // else, a disabled plugin shouldn't keep firing IPC reads for every
        // nearby player.
        if (_config.Enabled)
        {
            _nearbyTitleWatcher.Update(deltaSeconds);
        }

        if (HandleAfkStatus()) return;

        CheckConfigRevision();
        ProcessTitleUpdate(deltaSeconds);
        HandlePolling(deltaSeconds);
    }

    private void UpdateMusicOffTimer(double deltaSeconds)
    {
        _musicOffSeconds = _isMusicPlaying ? 0.0 : _musicOffSeconds + deltaSeconds;
    }

    private bool HandleAfkStatus()
    {
        IsPlayerAfk = IsLocalPlayerAfk();

        if (ShouldPauseForAfk())
        {
            EngageAfkPause();
            return true;
        }

        _hasLoggedAfk = false;
        return false;
    }

    private bool IsLocalPlayerAfk()
        => _objectTable.LocalPlayer?.OnlineStatus.RowId == AFK_ONLINE_STATUS_ID;

    private bool ShouldPauseForAfk()
        => IsPlayerAfk && !_isMusicPlaying && _musicOffSeconds > AFK_MUSIC_GRACE_SECONDS;

    private void EngageAfkPause()
    {
        if (!_hasLoggedAfk)
        {
            _pluginLog.Debug("Player is AFK and no music is playing, stopping polling.");
            _hasLoggedAfk = true;
        }
        ClearTitle();
        _pollingTimer = 0.0;
    }

    private void ProcessTitleUpdate(double deltaSeconds)
    {
        if (_titleState.UpdateAction == null) return;

        // Always advance the template clock so throttling never skews timing,
        // only how often the output is recomputed.
        _updaterContext.SecsElapsed += deltaSeconds;

        var (shouldRender, newTimer) = CheckRenderDue(
            _renderTimer, deltaSeconds, _renderInterval,
            _isTitleTimeDependent, _titleState.LastSentJson != null);
        _renderTimer = newTimer;
        if (!shouldRender) return;

        try
        {
            _titleState.UpdateAction();
        }
        catch (Exception e)
        {
            _pluginLog.Error(e, "Failed to update title");
            _chatGui.PrintError($"SpotifyHonorific: Failed to update title. Check /xllog for details.");
            _titleState.Clear();
        }
    }

    private void HandlePolling(double deltaSeconds)
    {
        if (!_config.Enabled)
        {
            if (_titleState.LastSentJson != null)
            {
                ClearTitle();
            }
            _authNotificationTimer = 0;
            return;
        }

        _pollingTimer += deltaSeconds;

        if (_config.SpotifyRefreshToken.IsNullOrWhitespace())
        {
            ShowAuthNotificationIfDue(deltaSeconds);
            return;
        }

        _authNotificationTimer = 0;

        if (_pollingTimer < _currentPollIntervalSeconds || _isPolling)
        {
            return;
        }

        if (_config.EnableDebugLogging)
        {
            _pluginLog.Debug($"POLLING NOW. Timer: {_pollingTimer:F2}/{_currentPollIntervalSeconds:F0}s | IsPlaying: {_isMusicPlaying}");
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
            var result = await _pollingService.GetCurrentlyPlayingTrackAsync().ConfigureAwait(false);
            await _framework.RunOnFrameworkThread(() => ProcessPollResult(result)).ConfigureAwait(false);
        }
        finally
        {
            _isPolling = false;
        }
    }

    private void ProcessPollResult(SpotifyPollResult? result)
    {
        var track = result?.Track;
        _playbackState.CurrentTrack = track;

        _currentPollIntervalSeconds = ComputeNextPollInterval(
            _config.RateLimitProtection, track != null, result?.ProgressMs, track?.DurationMs);

        if (track != null)
        {
            _isMusicPlaying = true;
            _tracksPlayedThisSession.Add(track.Id);
            ProcessCurrentlyPlayingTrack(track);
        }
        else
        {
            _isMusicPlaying = false;
            _currentTrackId = null;
            ClearTitle();
        }
    }

    // Rate limit protection trades title responsiveness for fewer API
    // requests: quick polls near a track's start (catches skips) and end
    // (catches the transition), relaxed polls mid-track and while idle.
    // Failed or gate-skipped polls also land in the idle interval on purpose.
    internal static double ComputeNextPollInterval(bool protectionOn, bool hasTrack, int? progressMs, int? durationMs)
    {
        if (!protectionOn) return POLLING_INTERVAL_SECONDS;
        if (!hasTrack) return PROTECTED_IDLE_POLL_SECONDS;
        if (progressMs == null || durationMs is null or <= 0) return PROTECTED_EDGE_POLL_SECONDS;
        if (progressMs < TRACK_START_WINDOW_MS) return PROTECTED_EDGE_POLL_SECONDS;
        if (durationMs - progressMs <= TRACK_END_WINDOW_MS) return PROTECTED_EDGE_POLL_SECONDS;
        return PROTECTED_MID_POLL_SECONDS;
    }

    internal static bool ShouldSkipTrackProcessing(string? currentTrackId, string newTrackId, Action? updateTitle)
        => currentTrackId == newTrackId && updateTitle != null;

    private void ProcessCurrentlyPlayingTrack(FullTrack track)
    {
        if (ShouldSkipTrackProcessing(_currentTrackId, track.Id, _titleState.UpdateAction))
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
        _isTitleTimeDependent = TemplateHelper.IsTimeDependent(activityConfig);
        _renderInterval = activityConfig.RainbowMode
            ? RAINBOW_RENDER_INTERVAL_SECONDS
            : TEXT_RENDER_INTERVAL_SECONDS;
        _renderTimer = _renderInterval; // pre-charge: first render lands on the next frame
        _titleState.UpdateAction = CreateTitleUpdateAction(activityConfig, track);
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

        var serializedData = _renderingService.SerializeTitleData(title, activityConfig, _updaterContext, _config.IsHonorificSupporter);
        if (!_titleState.ShouldSend(serializedData)) return;

        if (_config.EnableDebugLogging)
        {
            _pluginLog.Debug($"Call Honorific SetCharacterTitle IPC with:\n{serializedData}");
        }

        _setCharacterTitleSubscriber.InvokeAction(0, serializedData);
        _titleState.LastSentJson = serializedData;
    }

    internal static (bool ShouldRender, double NewTimer) CheckRenderDue(
        double renderTimer, double deltaSeconds, double renderInterval,
        bool isTimeDependent, bool alreadySent)
    {
        if (!isTimeDependent && alreadySent)
            return (false, renderTimer);

        var newTimer = renderTimer + deltaSeconds;
        if (newTimer < renderInterval)
            return (false, newTimer);

        return (true, 0);
    }

    internal static (bool ShouldNotify, double NewTimer) CheckAuthNotificationDue(
        double currentTimer, double deltaSeconds, double cooldownSeconds, bool notificationsEnabled)
    {
        if (!notificationsEnabled)
            return (false, currentTimer);

        var newTimer = currentTimer + deltaSeconds;
        if (newTimer < cooldownSeconds)
            return (false, newTimer);

        return (true, 0);
    }

    private void ShowAuthNotificationIfDue(double deltaSeconds)
    {
        var (shouldNotify, newTimer) = CheckAuthNotificationDue(
            _authNotificationTimer, deltaSeconds, AUTH_NOTIFICATION_COOLDOWN_SECONDS, _config.EnableNotifications);

        _authNotificationTimer = newTimer;
        if (!shouldNotify) return;

        _notificationManager.AddNotification(new Notification
        {
            Title = "SpotifyHonorific",
            Content = "Spotify authentication required. Use /spotifyhonorific config to set up.",
            Type = NotificationType.Warning,
            Minimized = false,
        });
    }

    private void ClearTitle()
    {
        if (_titleState.LastSentJson == null) return;

        _pluginLog.Debug("Call Honorific ClearCharacterTitle IPC");
        _framework.RunOnFrameworkThread(() =>
        {
            _clearCharacterTitleSubscriber.InvokeAction(0);
        });
        _updaterContext.SecsElapsed = 0;
        _titleState.Clear();
        _currentTrackId = null;
    }
}
