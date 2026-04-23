using System;

namespace SpotifyHonorific.Updaters;

/// <summary>
/// Holds the two rendering-state fields that must always be cleared together.
/// Grouping them prevents the bug where only one is cleared on exception,
/// leaving the dedup guard stale and silently blocking IPC recovery.
/// </summary>
internal sealed class TitleUpdateState
{
    public Action? UpdateAction { get; set; }
    public string? LastSentJson { get; set; }

    /// <summary>
    /// Clears both fields atomically — used by the exception handler and ClearTitle.
    /// </summary>
    public void Clear()
    {
        UpdateAction = null;
        LastSentJson = null;
    }

    /// <summary>
    /// Returns true when <paramref name="serializedData"/> should be sent to Honorific via IPC.
    /// Returns false when the data is identical to the last sent payload (no-op optimisation).
    /// </summary>
    public bool ShouldSend(string serializedData) => serializedData != LastSentJson;
}
