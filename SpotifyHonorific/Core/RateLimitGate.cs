using System;

namespace SpotifyHonorific.Core;

/// <summary>
/// Tracks the pause window after Spotify returns 429 Too Many Requests.
/// While active, polling is skipped so the rate limit can actually clear
/// instead of being extended by continued traffic.
/// </summary>
public sealed class RateLimitGate
{
    private static readonly TimeSpan DefaultPause = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaxPause = TimeSpan.FromMinutes(15);

    private DateTime _pausedUntil = DateTime.MinValue;

    public bool IsActive(DateTime now) => now < _pausedUntil;

    /// <summary>
    /// Time left until polling resumes; zero when the gate is not active.
    /// </summary>
    public TimeSpan Remaining(DateTime now) => IsActive(now) ? _pausedUntil - now : TimeSpan.Zero;

    /// <summary>
    /// Starts a pause based on Spotify's Retry-After value and returns the
    /// pause actually applied (defaulted when absent, capped when excessive).
    /// </summary>
    public TimeSpan Activate(TimeSpan retryAfter, DateTime now)
    {
        var pause = retryAfter > TimeSpan.Zero ? retryAfter : DefaultPause;
        if (pause > MaxPause)
        {
            pause = MaxPause;
        }

        _pausedUntil = now + pause;
        return pause;
    }
}
