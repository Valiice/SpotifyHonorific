namespace SpotifyHonorific.Updaters;

/// <summary>
/// What a poll means for the displayed title. A failed poll is deliberately
/// distinct from a successful one that reports nothing playing: only the
/// latter is evidence that the music actually stopped.
/// </summary>
public enum PollOutcome
{
    /// <summary>Spotify reported a track: render it.</summary>
    Playing,

    /// <summary>Nothing is playing, or the outage outlasted the grace period: clear the title.</summary>
    Stopped,

    /// <summary>The poll failed but the outage is still short: keep showing the last title.</summary>
    HoldTitle,
}
