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

    public void Clear()
    {
        UpdateAction = null;
        LastSentJson = null;
    }

    // Nulls LastSentJson without touching UpdateAction, so the next render frame
    // re-sends to Honorific without waiting for the next 2-second poll cycle.
    // Used when an external event (zone change) clears the title from Honorific's side.
    public void ForceResend()
    {
        LastSentJson = null;
    }

    public bool ShouldSend(string serializedData) => serializedData != LastSentJson;
}
