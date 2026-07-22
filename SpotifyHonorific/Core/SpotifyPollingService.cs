using Dalamud.Plugin.Services;
using Dalamud.Utility;
using SpotifyAPI.Web;
using SpotifyAPI.Web.Auth;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace SpotifyHonorific.Core;

// Seams for the two things this service does over the network. The defaults
// are the real Spotify calls; substituting them is the only way to exercise
// token refresh and 401 recovery without HTTP.
internal delegate Task<PKCETokenResponse> TokenRefresher(string clientId, string refreshToken, CancellationToken cancellationToken);

internal delegate ISpotifyClient SpotifyClientFactory(string accessToken);

public class SpotifyPollingService : IDisposable
{
    private const int API_TIMEOUT_MS = 5000;
    internal const int TOKEN_REFRESH_TIMEOUT_MS = 30000;
    private const int TOKEN_REFRESH_WINDOW_MINUTES = 55;
    private const int MAX_RETRY_ATTEMPTS = 3;
    private const int MAX_RESPONSE_TIME_SAMPLES = 100;
    // Forced refreshes needed before we stop assuming it is a one-off and tell
    // the user something else is using their Spotify app.
    internal const int TOKEN_CONFLICT_THRESHOLD = 3;
    internal const double ERROR_ANNOUNCE_COOLDOWN_MINUTES = 5;

    private readonly Config _config;
    private readonly IPluginLog _pluginLog;
    private readonly ChatNotifier _chat;
    private readonly TokenRefresher _refreshToken;
    private readonly SpotifyClientFactory _createClient;

    private ISpotifyClient? _spotify;
    private string? _currentAccessToken;
    private bool _rateLimitAnnounced;
    private bool _tokenConflictAnnounced;
    private string? _lastAnnouncedError;
    private DateTime _lastAnnouncedErrorAt;
    // Serializes the check-then-refresh below. The service is shared between
    // the poll loop and on-demand queue actions; two concurrent refreshes
    // would race with the same single-use PKCE refresh token, and the loser's
    // 400 would wipe the token the winner just saved.
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly object _statsLock = new();

    private const int MAX_EVENTS = 100;

    private int _apiCallCount;
    private int _apiErrorCount;
    private readonly Queue<long> _apiResponseTimes = new(MAX_RESPONSE_TIME_SAMPLES);
    private readonly Queue<PollEvent> _events = new(MAX_EVENTS);

    public int ApiCallCount => _apiCallCount;
    public int ApiErrorCount => _apiErrorCount;
    public double AverageResponseTime
    {
        get
        {
            lock (_statsLock)
            {
                return _apiResponseTimes.Count > 0 ? _apiResponseTimes.Average() : 0;
            }
        }
    }

    public int RateLimit429Count { get; private set; }
    public TimeSpan? LastRetryAfter { get; private set; }
    public int TokenRefreshCount { get; private set; }
    public int AuthRetryCount { get; private set; }
    public int AuthRecoveredCount { get; private set; }

    internal RateLimitGate RateLimitGate { get; } = new();

    public SpotifyPollingService(Config config, IPluginLog pluginLog, ChatNotifier chat)
        : this(config, pluginLog, chat, null, null)
    {
    }

    internal SpotifyPollingService(Config config, IPluginLog pluginLog, ChatNotifier chat,
        TokenRefresher? tokenRefresher, SpotifyClientFactory? clientFactory)
    {
        _config = config;
        _pluginLog = pluginLog;
        _chat = chat;
        _refreshToken = tokenRefresher ?? RequestTokenFromSpotify;
        _createClient = clientFactory ?? (accessToken => new SpotifyClient(accessToken));
    }

    private static Task<PKCETokenResponse> RequestTokenFromSpotify(string clientId, string refreshToken, CancellationToken cancellationToken)
        => new OAuthClient().RequestToken(new PKCETokenRefreshRequest(clientId, refreshToken), cancellationToken);

    public async Task<SpotifyPollResult?> GetCurrentlyPlayingTrackAsync()
    {
        if (RateLimitGate.IsActive(DateTime.Now))
        {
            return null;
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            using var clientCts = new CancellationTokenSource(TOKEN_REFRESH_TIMEOUT_MS);
            var spotify = await RetryAsync(
                () => GetSpotifyClientAsync(clientCts.Token),
                maxRetries: MAX_RETRY_ATTEMPTS
            ).ConfigureAwait(false);

            if (spotify == null)
            {
                HandleError(null, "Spotify client is null, likely not authenticated.");
                return null;
            }

            var currentlyPlaying = await RunWithAuthRetryAsync(spotify, PollCurrentlyPlayingAsync).ConfigureAwait(false);

            stopwatch.Stop();
            _apiCallCount++;
            RecordResponseTime(stopwatch.ElapsedMilliseconds);
            RecordPollSuccess();
            RecordEvent("pollOk", $"{stopwatch.ElapsedMilliseconds}ms");

            if (currentlyPlaying?.IsPlaying == true && currentlyPlaying.Item is FullTrack track)
            {
                return new SpotifyPollResult(track, currentlyPlaying.ProgressMs);
            }

            return new SpotifyPollResult(null, null);
        }
        catch (OperationCanceledException)
        {
            _apiErrorCount++;
            RecordEvent("timeout");
            // A timeout says nothing about the token, so the client is kept:
            // discarding it would force a needless refresh on the next poll.
            HandleError(null, "Spotify API request timed out. Retrying next poll.");
            return null;
        }
        catch (APITooManyRequestsException e)
        {
            _apiErrorCount++;
            HandleRateLimit(e);
            return null;
        }
        catch (APIUnauthorizedException e)
        {
            _apiErrorCount++;
            RecordEvent("apiError", DescribeApiError(e));
            HandleError(e, "Spotify rejected a freshly refreshed access token. Re-authenticate in /spotifyhonorific config if this keeps happening.", resetClient: true);
            return null;
        }
        catch (APIException e)
        {
            _apiErrorCount++;
            RecordEvent("apiError", DescribeApiError(e));
            HandleError(e, $"Spotify API error ({DescribeApiError(e)}). Retrying next poll.");
            return null;
        }
        catch (Exception e)
        {
            _apiErrorCount++;
            RecordEvent("apiError", e.GetType().Name);
            // Nothing is known about what this left the client in, so discard
            // it rather than reuse a possibly faulted one until the 55-minute
            // refresh window happens to expire.
            HandleError(e, "Unhandled error during Spotify poll", resetClient: true);
            return null;
        }
    }

    // Spotify sometimes rejects an access token we minted moments earlier:
    // 401s have been observed on tokens well under a second old, which no
    // expiry window can prevent. Mint a fresh token and run the request again
    // before calling it an error.
    //
    // Every caller holding a client from this service needs this, not just the
    // poll loop, so it wraps one request at a time: a retry re-runs only the
    // rejected call, never the caller's surrounding logic or chat output.
    public async Task<T> RunWithAuthRetryAsync<T>(ISpotifyClient client, Func<ISpotifyClient, Task<T>> operation)
    {
        try
        {
            return await operation(client).ConfigureAwait(false);
        }
        catch (APIUnauthorizedException)
        {
            RecordAuthRetry();

            using var refreshCts = new CancellationTokenSource(TOKEN_REFRESH_TIMEOUT_MS);
            var refreshed = await GetSpotifyClientAsync(refreshCts.Token, forceRefresh: true).ConfigureAwait(false);
            if (refreshed == null)
            {
                throw;
            }

            var result = await operation(refreshed).ConfigureAwait(false);
            RecordAuthRecovered();
            return result;
        }
    }

    private async Task<CurrentlyPlaying?> PollCurrentlyPlayingAsync(ISpotifyClient spotify)
    {
        using var pollCts = new CancellationTokenSource(API_TIMEOUT_MS);
        return await RetryAsync(
            () => spotify.Player.GetCurrentlyPlaying(new PlayerCurrentlyPlayingRequest(), pollCts.Token),
            maxRetries: MAX_RETRY_ATTEMPTS
        ).ConfigureAwait(false);
    }

    // A rejected token we are about to refresh and retry. Counted separately
    // from the recoveries below because the retry can fail too, and a notice
    // claiming everything was handled must only fire when that is true.
    internal void RecordAuthRetry()
    {
        AuthRetryCount++;
        RecordEvent("authRetry");
    }

    // Repeated recoveries mean something outside this plugin keeps invalidating
    // its tokens. The cause is not knowable from here: Spotify rejects tokens
    // it minted seconds earlier, and a second app on the same Client ID is only
    // one way that happens. Report the observation and leave the diagnosis to
    // the report. Retries that did not recover surface through the normal error
    // path instead, so they are never described as handled.
    internal void RecordAuthRecovered()
    {
        AuthRecoveredCount++;
        RecordEvent("authRecovered");

        if (_tokenConflictAnnounced || AuthRecoveredCount < TOKEN_CONFLICT_THRESHOLD) return;

        _tokenConflictAnnounced = true;
        _chat.PrintError(
            $"SpotifyHonorific: Spotify has rejected this app's access token {AuthRecoveredCount} times. " +
            "Each one was recovered automatically and your title kept working, so no action is needed. " +
            "If it becomes constant, check that no other app is signed in with the same Spotify Client ID.");
    }

    internal static string DescribeApiError(APIException e)
        => e.Response?.StatusCode is { } status
            ? $"{e.GetType().Name} {(int)status}"
            : e.GetType().Name;

    public void ResetClient()
    {
        _currentAccessToken = null;
        _spotify = null;
    }

    public async Task<ISpotifyClient?> GetAuthenticatedClientAsync(CancellationToken cancellationToken = default)
    {
        using var clientCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        clientCts.CancelAfter(TOKEN_REFRESH_TIMEOUT_MS);
        return await RetryAsync(
            () => GetSpotifyClientAsync(clientCts.Token),
            maxRetries: MAX_RETRY_ATTEMPTS
        ).ConfigureAwait(false);
    }

    // forceRefresh discards the cached client from inside the lock, so a
    // concurrent refresh cannot be clobbered the way an unlocked ResetClient
    // followed by a re-acquire could.
    private async Task<ISpotifyClient?> GetSpotifyClientAsync(CancellationToken cancellationToken = default, bool forceRefresh = false)
    {
        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var (refreshToken, clientId, lastAuthTime) = _config.WithLock(() =>
                (_config.SpotifyRefreshToken, _config.SpotifyClientId, _config.LastSpotifyAuthTime));

            if (refreshToken.IsNullOrWhitespace() || clientId.IsNullOrWhitespace())
            {
                return null;
            }

            if (!forceRefresh && _spotify != null && _currentAccessToken != null && lastAuthTime.AddMinutes(TOKEN_REFRESH_WINDOW_MINUTES) > DateTime.Now)
            {
                return _spotify;
            }

            _pluginLog.Debug("Spotify token expired or missing, requesting new one...");

            var response = await _refreshToken(clientId, refreshToken, cancellationToken).ConfigureAwait(false);

            _currentAccessToken = response.AccessToken;
            TokenRefreshCount++;
            RecordEvent("tokenRefresh");

            _config.WithLock(() =>
            {
                if (!string.IsNullOrEmpty(response.RefreshToken))
                {
                    _config.SpotifyRefreshToken = response.RefreshToken;
                }
                _config.LastSpotifyAuthTime = DateTime.Now;
                _config.Save();
            });

            _spotify = _createClient(_currentAccessToken);
            return _spotify;
        }
        catch (APIException e) when (e.Response?.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
        {
            _pluginLog.Error(e, "Spotify rejected the refresh token, clearing authentication.");
            _config.WithLock(() =>
            {
                _config.SpotifyRefreshToken = string.Empty;
                _config.Save();
            });
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            _pluginLog.Warning(e, "Transient error refreshing Spotify token, will retry next poll cycle.");
            _spotify = null;
            _currentAccessToken = null;
            return null;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    internal async Task<T?> RetryAsync<T>(Func<Task<T>> operation, int maxRetries = MAX_RETRY_ATTEMPTS)
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

                // Retrying a 429 in a tight loop keeps the rate limit from
                // ever clearing; surface it immediately like a 401.
                if (ex is APITooManyRequestsException)
                {
                    throw;
                }

                if (ex is APIException apiEx && apiEx.Response?.StatusCode == HttpStatusCode.Unauthorized)
                {
                    throw;
                }

                // The CTS is shared across attempts; once it fires, every
                // retry re-throws instantly, so the delays are pure dead time.
                if (ex is OperationCanceledException)
                {
                    throw;
                }

                var delayMs = (int)Math.Pow(2, attempt) * 1000;

                if (_config.EnableDebugLogging)
                {
                    _pluginLog.Debug($"Retry attempt {attempt + 1}/{maxRetries} after {delayMs}ms delay. Error: {ex.Message}");
                }

                await Task.Delay(delayMs).ConfigureAwait(false);
            }
        }

        return default;
    }

    private void RecordResponseTime(long milliseconds)
    {
        lock (_statsLock)
        {
            _apiResponseTimes.Enqueue(milliseconds);

            if (_apiResponseTimes.Count > MAX_RESPONSE_TIME_SAMPLES)
            {
                _apiResponseTimes.Dequeue();
            }
        }
    }

    private void RecordEvent(string kind, string? detail = null)
    {
        lock (_statsLock)
        {
            _events.Enqueue(new PollEvent(DateTime.Now, kind, detail));

            if (_events.Count > MAX_EVENTS)
            {
                _events.Dequeue();
            }
        }
    }

    internal IReadOnlyList<PollEvent> GetEventSnapshot()
    {
        lock (_statsLock)
        {
            return _events.ToList();
        }
    }

    internal IReadOnlyList<long> GetResponseTimeSnapshot()
    {
        lock (_statsLock)
        {
            return _apiResponseTimes.ToList();
        }
    }

    internal void HandleRateLimit(APITooManyRequestsException e)
    {
        RateLimit429Count++;
        LastRetryAfter = e.RetryAfter;
        RecordEvent("rateLimited", $"{e.RetryAfter.TotalSeconds:0}s");

        var pause = RateLimitGate.Activate(e.RetryAfter, DateTime.Now);
        var message = $"Spotify rate limit hit (429). Pausing polling for {pause.TotalSeconds:0}s.";

        _pluginLog.Warning(e, message);

        // Announce once per episode so users know why their title stopped;
        // a long rate limit re-arms the gate repeatedly and must not spam chat.
        if (!_rateLimitAnnounced)
        {
            _rateLimitAnnounced = true;
            _chat.PrintError(
                "SpotifyHonorific: Spotify is rate limiting this app's requests. " +
                "This is a Spotify-side limit on your Spotify application, often after very long continuous listening. " +
                $"It is not a plugin error. Polling is paused for about {FormatPause(pause)} and will resume automatically.");
        }
        else if (_config.EnableDebugLogging)
        {
            _chat.PrintError($"SpotifyHonorific: {message}");
        }

        AutoEnableRateLimitProtection();

        // The token is fine; keep the client so no refresh traffic is added.
    }

    private void AutoEnableRateLimitProtection()
    {
        var enabledNow = _config.WithLock(() =>
        {
            if (_config.RateLimitProtection) return false;
            _config.RateLimitProtection = true;
            _config.Save();
            return true;
        });

        if (enabledNow)
        {
            _chat.Print(
                "SpotifyHonorific: Rate limit protection has been turned on automatically so this happens less. " +
                "You can change this in /spotifyhonorific config.");
        }
    }

    internal void RecordPollSuccess()
    {
        if (!_rateLimitAnnounced) return;

        // First successful poll after an announced episode: close the loop in
        // chat and let the fallback escalation start from scratch next time.
        _rateLimitAnnounced = false;
        RateLimitGate.ResetEscalation();
        _chat.Print("SpotifyHonorific: Spotify rate limit cleared, polling resumed.");
    }

    internal static string FormatPause(TimeSpan pause)
    {
        if (pause < TimeSpan.FromSeconds(90)) return $"{pause.TotalSeconds:0}s";
        if (pause < TimeSpan.FromMinutes(90)) return $"{pause.TotalMinutes:0}m";
        return $"{(int)pause.TotalHours}h {pause.Minutes}m";
    }

    // A recurring failure interleaved with successful polls must not print on
    // every occurrence: that is the "every few minutes" chat spam users hit.
    // Clearing the memo on success would not help, because the failures alternate
    // with successes; only elapsed time ends an episode.
    internal static bool ShouldAnnounceError(string message, string? lastMessage, DateTime lastAnnouncedAt, DateTime now, double cooldownMinutes)
        => message != lastMessage || now >= lastAnnouncedAt.AddMinutes(cooldownMinutes);

    // resetClient defaults to false: a timeout or a 5xx says nothing about the
    // token. Discarding the client for those forced a PKCE refresh on the next
    // poll, rotating the single-use refresh token and adding traffic for a
    // problem that was never token-related.
    private void HandleError(Exception? e, string message, bool resetClient = false)
    {
        if (e != null)
        {
            _pluginLog.Warning(e, message);
        }
        else
        {
            _pluginLog.Warning(message);
        }

        var now = DateTime.Now;
        if (_config.EnableDebugLogging &&
            ShouldAnnounceError(message, _lastAnnouncedError, _lastAnnouncedErrorAt, now, ERROR_ANNOUNCE_COOLDOWN_MINUTES))
        {
            _lastAnnouncedError = message;
            _lastAnnouncedErrorAt = now;
            _chat.PrintError($"SpotifyHonorific: {message}");
        }

        if (resetClient)
        {
            ResetClient();
        }
    }

    public void Dispose() => _refreshLock.Dispose();
}
