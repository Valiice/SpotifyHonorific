using System;

namespace SpotifyHonorific.Core;

/// <summary>
/// Tracks the pause window after Spotify returns 429 Too Many Requests.
/// While active, polling is skipped so the rate limit can actually clear
/// instead of being extended by continued traffic. Retry-After from the
/// server is honored exactly; without it, pauses back off exponentially.
/// </summary>
public sealed class RateLimitGate
{
    private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromHours(24);
    private static readonly TimeSpan FallbackBasePause = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan FallbackMaxPause = TimeSpan.FromHours(1);

    private DateTime _pausedUntil = DateTime.MinValue;
    private int _consecutiveFallbacks;

    public int FallbackEscalationCount => _consecutiveFallbacks;

    public bool IsActive(DateTime now) => now < _pausedUntil;

    /// <summary>
    /// Time left until polling resumes; zero when the gate is not active.
    /// </summary>
    public TimeSpan Remaining(DateTime now) => IsActive(now) ? _pausedUntil - now : TimeSpan.Zero;

    /// <summary>
    /// Starts a pause and returns the pause actually applied. A positive
    /// Retry-After from Spotify is honored exactly (sanity-capped at 24h);
    /// probing earlier than the server asked is a guaranteed 429. Without
    /// Retry-After the pause escalates per consecutive fallback: 30s
    /// doubling up to 1h, reset by the first successful poll.
    /// </summary>
    public TimeSpan Activate(TimeSpan retryAfter, DateTime now)
    {
        TimeSpan pause;
        if (retryAfter > TimeSpan.Zero)
        {
            pause = retryAfter > MaxRetryAfter ? MaxRetryAfter : retryAfter;
        }
        else
        {
            // Shift capped at 7 (30s * 2^7 = 64m, already past the cap) so
            // the counter can never overflow into a negative pause.
            var shift = Math.Min(_consecutiveFallbacks, 7);
            pause = TimeSpan.FromTicks(FallbackBasePause.Ticks << shift);
            if (pause > FallbackMaxPause)
            {
                pause = FallbackMaxPause;
            }
            _consecutiveFallbacks++;
        }

        _pausedUntil = now + pause;
        return pause;
    }

    public void ResetEscalation() => _consecutiveFallbacks = 0;
}
