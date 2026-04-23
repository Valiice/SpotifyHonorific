# Live Preview: Real Track Data via PlaybackState

**Date:** 2026-04-23
**Branch:** feature/live-preview-playback-state

## Problem

`DrawTemplatePreview` in `ConfigWindow` renders the title template against a hardcoded mock track. The filter template is not evaluated at all. Users cannot see what their title will actually look like with their current song, or whether a config would activate right now.

## Goal

When a track is playing, the preview panel renders against the real Spotify track and evaluates the filter template live. When nothing is playing, it falls back to mock data with a `(mock)` label — preserving the ability to test templates offline.

## Architecture

### New: `PlaybackState` (`SpotifyHonorific/Core/PlaybackState.cs`)

A single shared runtime-state object. Holds one nullable property:

```csharp
internal sealed class PlaybackState
{
    public FullTrack? CurrentTrack { get; set; }
}
```

No logic. Created once in `Plugin` at startup, injected into `Updater` and `ConfigWindow`.

### Modified: `Plugin.cs`

Creates the `PlaybackState` instance and passes it to both constructors.

### Modified: `Updater.cs`

- Accepts `PlaybackState` in constructor, stores as `_playbackState`.
- In `ProcessPollResult`: sets `_playbackState.CurrentTrack = track` when playing, `null` when not.

### Modified: `ConfigWindow.cs`

- Accepts `PlaybackState` in constructor, stores as `_playbackState`.
- Passes it to `DrawTemplatePreview`.

### Modified: `DrawTemplatePreview`

- Uses `_playbackState.CurrentTrack ?? CreateMockSpotifyTrack()` as the track.
- Shows `(mock)` label in the title line when falling back.
- Evaluates the filter template against the same track when non-empty:
  - `✓ Filter matches` (green) when the filter evaluates to true.
  - `✗ Filter skipped` (red) when it evaluates to false or errors.
  - Hidden when filter template is empty.

## Data Flow

```
Plugin (creates PlaybackState)
  └─► Updater.ProcessPollResult  ──writes──► PlaybackState.CurrentTrack
  └─► ConfigWindow.DrawTemplatePreview ──reads──► PlaybackState.CurrentTrack
```

All writes happen on the framework thread (Updater already dispatches via `RunOnFrameworkThread`). All reads happen on the framework draw thread. No synchronisation needed.

## Out of Scope

- `SecsElapsed` in the preview stays at 0 (consistent with current mock behaviour).
- No changes to how the filter template is stored or evaluated in the actual polling loop.

## Testing

`PlaybackState` has no logic — no unit tests needed. Existing 74 tests unaffected. Manual verification: load plugin with a track playing, open config, confirm preview shows real track name and filter result.
