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

public class SpotifyPollingService
{
    private const int API_TIMEOUT_MS = 5000;
    private const int TOKEN_REFRESH_WINDOW_MINUTES = 55;
    private const int MAX_RETRY_ATTEMPTS = 3;
    private const int MAX_RESPONSE_TIME_SAMPLES = 100;

    private readonly Config _config;
    private readonly IPluginLog _pluginLog;
    private readonly IChatGui _chatGui;

    private SpotifyClient? _spotify;
    private string? _currentAccessToken;
    private bool _rateLimitAnnounced;
    // Serializes the check-then-refresh below. The service is shared between
    // the poll loop and on-demand queue actions; two concurrent refreshes
    // would race with the same single-use PKCE refresh token, and the loser's
    // 400 would wipe the token the winner just saved.
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private const int MAX_EVENTS = 100;

    private int _apiCallCount;
    private int _apiErrorCount;
    private readonly Queue<long> _apiResponseTimes = new(MAX_RESPONSE_TIME_SAMPLES);
    private readonly Queue<PollEvent> _events = new(MAX_EVENTS);

    public int ApiCallCount => _apiCallCount;
    public int ApiErrorCount => _apiErrorCount;
    public double AverageResponseTime => _apiResponseTimes.Count > 0 ? _apiResponseTimes.Average() : 0;

    public int RateLimit429Count { get; private set; }
    public TimeSpan? LastRetryAfter { get; private set; }
    public int TokenRefreshCount { get; private set; }

    internal RateLimitGate RateLimitGate { get; } = new();

    public SpotifyPollingService(Config config, IPluginLog pluginLog, IChatGui chatGui)
    {
        _config = config;
        _pluginLog = pluginLog;
        _chatGui = chatGui;
    }

    public async Task<SpotifyPollResult?> GetCurrentlyPlayingTrackAsync()
    {
        if (RateLimitGate.IsActive(DateTime.Now))
        {
            return null;
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            using var clientCts = new CancellationTokenSource(API_TIMEOUT_MS);
            var spotify = await RetryAsync(
                () => GetSpotifyClientAsync(clientCts.Token),
                maxRetries: MAX_RETRY_ATTEMPTS
            ).ConfigureAwait(false);

            if (spotify == null)
            {
                HandleError(null, "Spotify client is null, likely not authenticated.");
                return null;
            }

            using var pollCts = new CancellationTokenSource(API_TIMEOUT_MS);
            var currentlyPlaying = await RetryAsync(
                () => spotify.Player.GetCurrentlyPlaying(new PlayerCurrentlyPlayingRequest(), pollCts.Token),
                maxRetries: MAX_RETRY_ATTEMPTS
            ).ConfigureAwait(false);

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
            HandleError(null, "Spotify API request timed out after 5 seconds.");
            return null;
        }
        catch (APITooManyRequestsException e)
        {
            _apiErrorCount++;
            HandleRateLimit(e);
            return null;
        }
        catch (APIException e)
        {
            _apiErrorCount++;
            RecordEvent("apiError", e.GetType().Name);
            HandleError(e, "Error polling Spotify. Token may be expired.");
            return null;
        }
        catch (Exception e)
        {
            _apiErrorCount++;
            RecordEvent("apiError", e.GetType().Name);
            HandleError(e, "Unhandled error during Spotify poll");
            return null;
        }
    }

    public void ResetClient()
    {
        _currentAccessToken = null;
        _spotify = null;
    }

    public async Task<SpotifyClient?> GetAuthenticatedClientAsync(CancellationToken cancellationToken = default)
    {
        using var clientCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        clientCts.CancelAfter(API_TIMEOUT_MS);
        return await RetryAsync(
            () => GetSpotifyClientAsync(clientCts.Token),
            maxRetries: MAX_RETRY_ATTEMPTS
        ).ConfigureAwait(false);
    }

    private async Task<SpotifyClient?> GetSpotifyClientAsync(CancellationToken cancellationToken = default)
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

            if (_spotify != null && _currentAccessToken != null && lastAuthTime.AddMinutes(TOKEN_REFRESH_WINDOW_MINUTES) > DateTime.Now)
            {
                return _spotify;
            }

            _pluginLog.Debug("Spotify token expired or missing, requesting new one...");

            var response = await new OAuthClient().RequestToken(
                new PKCETokenRefreshRequest(clientId, refreshToken),
                cancellationToken
            ).ConfigureAwait(false);

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

            _spotify = new SpotifyClient(_currentAccessToken);
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
        _apiResponseTimes.Enqueue(milliseconds);

        if (_apiResponseTimes.Count > MAX_RESPONSE_TIME_SAMPLES)
        {
            _apiResponseTimes.Dequeue();
        }
    }

    private void RecordEvent(string kind, string? detail = null)
    {
        _events.Enqueue(new PollEvent(DateTime.Now, kind, detail));

        if (_events.Count > MAX_EVENTS)
        {
            _events.Dequeue();
        }
    }

    internal IReadOnlyList<PollEvent> GetEventSnapshot() => _events.ToList();

    internal IReadOnlyList<long> GetResponseTimeSnapshot() => _apiResponseTimes.ToList();

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
            _chatGui.PrintError(
                "SpotifyHonorific: Spotify is rate limiting this app's requests. " +
                "This is a Spotify-side limit on your Spotify application, often after very long continuous listening. " +
                $"It is not a plugin error. Polling is paused for about {FormatPause(pause)} and will resume automatically.");
        }
        else if (_config.EnableDebugLogging)
        {
            _chatGui.PrintError($"SpotifyHonorific: {message}");
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
            _chatGui.Print(
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
        _chatGui.Print("SpotifyHonorific: Spotify rate limit cleared, polling resumed.");
    }

    internal static string FormatPause(TimeSpan pause)
    {
        if (pause < TimeSpan.FromSeconds(90)) return $"{pause.TotalSeconds:0}s";
        if (pause < TimeSpan.FromMinutes(90)) return $"{pause.TotalMinutes:0}m";
        return $"{(int)pause.TotalHours}h {pause.Minutes}m";
    }

    private void HandleError(Exception? e, string message)
    {
        if (e != null)
        {
            _pluginLog.Warning(e, message);
        }
        else
        {
            _pluginLog.Warning(message);
        }

        if (_config.EnableDebugLogging)
        {
            _chatGui.PrintError($"SpotifyHonorific: {message}");
        }

        ResetClient();
    }
}
