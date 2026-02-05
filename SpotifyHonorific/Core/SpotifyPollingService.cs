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

/// <summary>
/// Handles Spotify API communication, token management, and polling.
/// </summary>
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

    private int _apiCallCount;
    private int _apiErrorCount;
    private readonly Queue<long> _apiResponseTimes = new(MAX_RESPONSE_TIME_SAMPLES);

    public int ApiCallCount => _apiCallCount;
    public int ApiErrorCount => _apiErrorCount;
    public double AverageResponseTime => _apiResponseTimes.Count > 0 ? _apiResponseTimes.Average() : 0;

    public SpotifyPollingService(Config config, IPluginLog pluginLog, IChatGui chatGui)
    {
        _config = config;
        _pluginLog = pluginLog;
        _chatGui = chatGui;
    }

    /// <summary>
    /// Polls Spotify for the currently playing track.
    /// Returns null if not playing or on error.
    /// </summary>
    public async Task<FullTrack?> GetCurrentlyPlayingTrackAsync()
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            using var cts = new CancellationTokenSource(API_TIMEOUT_MS);

            var spotify = await RetryAsync(
                () => GetSpotifyClientAsync(cts.Token),
                maxRetries: MAX_RETRY_ATTEMPTS
            ).ConfigureAwait(false);

            if (spotify == null)
            {
                HandleError(null, "Spotify client is null, likely not authenticated.");
                return null;
            }

            var currentlyPlaying = await RetryAsync(
                () => spotify.Player.GetCurrentlyPlaying(new PlayerCurrentlyPlayingRequest(), cts.Token),
                maxRetries: MAX_RETRY_ATTEMPTS
            ).ConfigureAwait(false);

            stopwatch.Stop();
            _apiCallCount++;
            RecordResponseTime(stopwatch.ElapsedMilliseconds);

            if (currentlyPlaying?.IsPlaying == true && currentlyPlaying.Item is FullTrack track)
            {
                return track;
            }

            return null;
        }
        catch (OperationCanceledException)
        {
            _apiErrorCount++;
            HandleError(null, "Spotify API request timed out after 5 seconds.");
            return null;
        }
        catch (APIException e)
        {
            _apiErrorCount++;
            HandleError(e, "Error polling Spotify. Token may be expired.");
            return null;
        }
        catch (Exception e)
        {
            _apiErrorCount++;
            HandleError(e, "Unhandled error during Spotify poll");
            return null;
        }
    }

    /// <summary>
    /// Resets the client state, forcing re-authentication on next poll.
    /// </summary>
    public void ResetClient()
    {
        _currentAccessToken = null;
        _spotify = null;
    }

    private async Task<SpotifyClient?> GetSpotifyClientAsync(CancellationToken cancellationToken = default)
    {
        var (refreshToken, clientId, clientSecret, lastAuthTime) = _config.WithLock(() =>
            (_config.SpotifyRefreshToken, _config.SpotifyClientId, _config.SpotifyClientSecret, _config.LastSpotifyAuthTime));

        if (refreshToken.IsNullOrWhitespace() || clientId.IsNullOrWhitespace() || clientSecret.IsNullOrWhitespace())
        {
            return null;
        }

        if (_spotify != null && _currentAccessToken != null && lastAuthTime.AddMinutes(TOKEN_REFRESH_WINDOW_MINUTES) > DateTime.Now)
        {
            return _spotify;
        }

        _pluginLog.Debug("Spotify token expired or missing, requesting new one...");

        try
        {
            var response = await new OAuthClient().RequestToken(
                new PKCETokenRefreshRequest(clientId, refreshToken),
                cancellationToken
            ).ConfigureAwait(false);

            _currentAccessToken = response.AccessToken;

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
        catch (Exception e)
        {
            _pluginLog.Error(e, "Failed to refresh Spotify token!");
            _config.WithLock(() =>
            {
                _config.SpotifyRefreshToken = string.Empty;
                _config.Save();
            });
            return null;
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
