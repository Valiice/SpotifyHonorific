# Live Preview: Real Track Data via PlaybackState

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the hardcoded mock track in `DrawTemplatePreview` with the real currently-playing Spotify track, and add live filter template evaluation.

**Architecture:** A new `PlaybackState` class holds `FullTrack? CurrentTrack` as shared runtime state. `Plugin` creates one instance and injects it into `Updater` (which writes) and `ConfigWindow` (which reads). `DrawTemplatePreview` falls back to mock data with a `(mock)` label when no track is playing.

**Tech Stack:** C# / .NET 10, Dalamud plugin framework, SpotifyAPI.Web (`FullTrack`), Scriban templating, ImGui via Dalamud.

---

## File Map

| File | Change |
|---|---|
| `SpotifyHonorific/Core/PlaybackState.cs` | **Create** — runtime state holder |
| `SpotifyHonorific/Updaters/Updater.cs` | **Modify** — accept + write `PlaybackState` |
| `SpotifyHonorific/Windows/ConfigWindow.cs` | **Modify** — accept + read `PlaybackState`, update preview |
| `SpotifyHonorific/Plugin.cs` | **Modify** — create instance, wire constructors |
| `SpotifyHonorific.Tests/ConfigWindowPreviewTests.cs` | **Create** — tests for filter evaluation logic |

---

## Task 1: Create branch

- [ ] **Step 1: Create and switch to feature branch**

```bash
git checkout -b feature/live-preview-playback-state
```

Expected: `Switched to a new branch 'feature/live-preview-playback-state'`

---

## Task 2: Create `PlaybackState`

**Files:**
- Create: `SpotifyHonorific/Core/PlaybackState.cs`

- [ ] **Step 1: Create the file**

```csharp
using SpotifyAPI.Web;

namespace SpotifyHonorific.Core;

internal sealed class PlaybackState
{
    public FullTrack? CurrentTrack { get; set; }
}
```

- [ ] **Step 2: Build to confirm it compiles**

```bash
dotnet build
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 3: Commit**

```bash
git add SpotifyHonorific/Core/PlaybackState.cs
git commit -m "feat: add PlaybackState runtime state holder"
```

---

## Task 3: Wire `PlaybackState` into `Updater`

**Files:**
- Modify: `SpotifyHonorific/Updaters/Updater.cs`

`Updater` currently constructs like:
```csharp
public Updater(IChatGui chatGui, Config config, IFramework framework, IDalamudPluginInterface pluginInterface, IPluginLog pluginLog)
```

And `ProcessPollResult` is:
```csharp
private void ProcessPollResult(FullTrack? track)
{
    if (track != null)
    {
        _isMusicPlaying = true;
        _tracksPlayedToday.Add(track.Id);
        ProcessCurrentlyPlayingTrack(track);
    }
    else
    {
        _isMusicPlaying = false;
        _currentTrackId = null;
        ClearTitle();
    }
}
```

- [ ] **Step 1: Add `PlaybackState` field and constructor parameter**

Add the field after the existing `private readonly TitleUpdateState _titleState = new();` line:

```csharp
private readonly PlaybackState _playbackState;
```

Change the constructor signature to:

```csharp
public Updater(IChatGui chatGui, Config config, IFramework framework, IDalamudPluginInterface pluginInterface, IPluginLog pluginLog, PlaybackState playbackState)
```

Add to the constructor body (after `_pluginLog = pluginLog;`):

```csharp
_playbackState = playbackState;
```

- [ ] **Step 2: Set `CurrentTrack` in `ProcessPollResult`**

Replace the existing `ProcessPollResult` body:

```csharp
private void ProcessPollResult(FullTrack? track)
{
    _playbackState.CurrentTrack = track;

    if (track != null)
    {
        _isMusicPlaying = true;
        _tracksPlayedToday.Add(track.Id);
        ProcessCurrentlyPlayingTrack(track);
    }
    else
    {
        _isMusicPlaying = false;
        _currentTrackId = null;
        ClearTitle();
    }
}
```

- [ ] **Step 3: Build (will fail until Plugin.cs is updated)**

```bash
dotnet build
```

Expected: error about wrong number of arguments to `new Updater(...)` in Plugin.cs — that is expected, fixed in Task 4.

---

## Task 4: Wire `PlaybackState` into `ConfigWindow`

**Files:**
- Modify: `SpotifyHonorific/Windows/ConfigWindow.cs`

Current constructor:
```csharp
public ConfigWindow(Config config, ImGuiHelper imGuiHelper, Updater updater, SpotifyAuthenticator spotifyAuthenticator)
```

Current `DrawTemplatePreview` signature:
```csharp
private static void DrawTemplatePreview(ActivityConfig activityConfig)
```

- [ ] **Step 1: Add `PlaybackState` field and constructor parameter**

Add field after `private SpotifyAuthenticator SpotifyAuthenticator { get; init; }`:

```csharp
private PlaybackState PlaybackState { get; init; }
```

Add `using SpotifyHonorific.Core;` to the using block at the top of the file if not already present.

Change constructor signature to:

```csharp
public ConfigWindow(Config config, ImGuiHelper imGuiHelper, Updater updater, SpotifyAuthenticator spotifyAuthenticator, PlaybackState playbackState) : base("Spotify Activity Honorific Config##configWindow")
```

Add to constructor body (after existing assignments):

```csharp
PlaybackState = playbackState;
```

- [ ] **Step 2: Pass `PlaybackState` to `DrawTemplatePreview`**

Change the call site at line ~417:

```csharp
DrawTemplatePreview(activityConfig, PlaybackState);
```

Change the method signature:

```csharp
private static void DrawTemplatePreview(ActivityConfig activityConfig, PlaybackState playbackState)
```

---

## Task 5: Update `Plugin.cs`

**Files:**
- Modify: `SpotifyHonorific/Plugin.cs`

Current construction:
```csharp
Updater = new(ChatGui, Config, Framework, PluginInterface, PluginLog);
ConfigWindow = new ConfigWindow(Config, new(), Updater, SpotifyAuthenticator);
```

- [ ] **Step 1: Create `PlaybackState` and pass to both constructors**

Add a field:
```csharp
private PlaybackState PlaybackState { get; init; }
```

Add `using SpotifyHonorific.Core;` to the using block.

Replace the two construction lines:

```csharp
PlaybackState = new PlaybackState();
Updater = new(ChatGui, Config, Framework, PluginInterface, PluginLog, PlaybackState);
ConfigWindow = new ConfigWindow(Config, new(), Updater, SpotifyAuthenticator, PlaybackState);
```

- [ ] **Step 2: Build to confirm everything compiles**

```bash
dotnet build
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 3: Run tests**

```bash
dotnet test
```

Expected: `Passed! Failed: 0, Passed: 74`

- [ ] **Step 4: Commit**

```bash
git add SpotifyHonorific/Core/PlaybackState.cs SpotifyHonorific/Updaters/Updater.cs SpotifyHonorific/Windows/ConfigWindow.cs SpotifyHonorific/Plugin.cs
git commit -m "feat: wire PlaybackState through Updater, ConfigWindow, and Plugin"
```

---

## Task 6: Update `DrawTemplatePreview` to use real track + filter evaluation

**Files:**
- Modify: `SpotifyHonorific/Windows/ConfigWindow.cs`
- Create: `SpotifyHonorific.Tests/ConfigWindowPreviewTests.cs`

Note: `FilterTemplate` exists on `ActivityConfig` and is validated for syntax, but is not currently evaluated in the polling loop. The preview evaluates it client-side only — useful for template authoring.

### 6a — Write failing test for filter evaluation first

- [ ] **Step 1: Add test file**

```csharp
using FluentAssertions;
using SpotifyHonorific.Windows;

namespace SpotifyHonorific.Tests;

public class ConfigWindowPreviewTests
{
    [Fact]
    public void EvaluateFilterTemplate_EmptyTemplate_ReturnsNull()
    {
        ConfigWindow.EvaluateFilterTemplate(string.Empty, new { }).Should().BeNull();
    }

    [Fact]
    public void EvaluateFilterTemplate_TrueTemplate_ReturnsTrue()
    {
        ConfigWindow.EvaluateFilterTemplate("{{ true }}", new { }).Should().BeTrue();
    }

    [Fact]
    public void EvaluateFilterTemplate_FalseTemplate_ReturnsFalse()
    {
        ConfigWindow.EvaluateFilterTemplate("{{ false }}", new { }).Should().BeFalse();
    }

    [Fact]
    public void EvaluateFilterTemplate_InvalidTemplate_ReturnsFalse()
    {
        ConfigWindow.EvaluateFilterTemplate("{{ !! invalid !! }}", new { }).Should().BeFalse();
    }

    [Fact]
    public void EvaluateFilterTemplate_TrackNameCondition_MatchesWhenTrue()
    {
        var track = new { Name = "Never Gonna Give You Up" };
        ConfigWindow.EvaluateFilterTemplate(
            """{{ Activity.Name == "Never Gonna Give You Up" }}""",
            track
        ).Should().BeTrue();
    }

    [Fact]
    public void EvaluateFilterTemplate_TrackNameCondition_NoMatchWhenFalse()
    {
        var track = new { Name = "Something Else" };
        ConfigWindow.EvaluateFilterTemplate(
            """{{ Activity.Name == "Never Gonna Give You Up" }}""",
            track
        ).Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run tests — confirm they fail**

```bash
dotnet test --filter "ConfigWindowPreviewTests"
```

Expected: compile error — `EvaluateFilterTemplate` does not exist yet.

### 6b — Add `EvaluateFilterTemplate` to `ConfigWindow`

- [ ] **Step 3: Add the static method to `ConfigWindow`**

Add after `CreateMockSpotifyTrack`:

```csharp
internal static bool? EvaluateFilterTemplate(string filterTemplate, object track)
{
    if (string.IsNullOrWhiteSpace(filterTemplate))
        return null;

    try
    {
        var template = Template.Parse(filterTemplate);
        if (template.HasErrors)
            return false;

        var result = template.Render(new { Activity = track }, member => member.Name).Trim();
        return bool.TryParse(result, out var b) ? b : (bool?)false;
    }
    catch
    {
        return false;
    }
}
```

- [ ] **Step 4: Run tests — confirm they pass**

```bash
dotnet test --filter "ConfigWindowPreviewTests"
```

Expected: `Passed! Failed: 0, Passed: 6`

### 6c — Update `DrawTemplatePreview`

- [ ] **Step 5: Replace the body of `DrawTemplatePreview`**

Replace the entire method body (keep the signature change from Task 4):

```csharp
private static void DrawTemplatePreview(ActivityConfig activityConfig, PlaybackState playbackState)
{
    ImGui.Separator();
    ImGui.Text("Live Preview:");
    ImGui.Spacing();
    ImGui.Indent(10);

    var isMock = playbackState.CurrentTrack == null;
    var track = isMock ? (object)CreateMockSpotifyTrack() : playbackState.CurrentTrack!;
    var mockContext = new UpdaterContext { SecsElapsed = 0 };

    try
    {
        var titleTemplate = Template.Parse(activityConfig.TitleTemplate);
        if (titleTemplate.HasErrors)
        {
            ImGuiHelper.TextError($"Template Error: {TemplateHelper.GetTemplateErrors(titleTemplate)}");
        }
        else
        {
            var renderedTitle = titleTemplate.Render(new { Activity = track, Context = mockContext }, member => member.Name);

            ImGui.Text("Result:");
            ImGui.SameLine();

            var colorToUse = activityConfig.Color ?? new Vector3(1, 1, 1);
            if (activityConfig.RainbowMode)
            {
                ImGui.TextColored(new Vector4(1, 0.5f, 0, 1), "🌈 ");
                ImGui.SameLine();
            }

            ImGui.TextColored(new Vector4(colorToUse.X, colorToUse.Y, colorToUse.Z, 1), renderedTitle);

            var length = renderedTitle.Length;
            var lengthColor = length <= 32 ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed;
            ImGui.SameLine();
            ImGui.TextColored(lengthColor, $"({length}/32)");

            if (isMock)
            {
                ImGui.SameLine();
                ImGui.TextDisabled("(mock)");
            }

            if (length > 32)
            {
                ImGuiHelper.TextWarning("⚠ Title exceeds 32 character limit and will be rejected by Honorific plugin.");
            }
        }

        if (!string.IsNullOrWhiteSpace(activityConfig.FilterTemplate))
        {
            ImGui.Spacing();
            var filterResult = EvaluateFilterTemplate(activityConfig.FilterTemplate, track);
            if (filterResult == true)
                ImGui.TextColored(ImGuiColors.HealerGreen, "✓ Filter matches");
            else
                ImGui.TextColored(ImGuiColors.DalamudRed, "✗ Filter skipped");
        }
    }
    catch (Exception ex)
    {
        ImGuiHelper.TextError($"Preview Error: {ex.Message}");
    }

    ImGui.Unindent();
}
```

- [ ] **Step 6: Build and run all tests**

```bash
dotnet test
```

Expected: `Passed! Failed: 0, Passed: 80`

- [ ] **Step 7: Commit**

```bash
git add SpotifyHonorific/Windows/ConfigWindow.cs SpotifyHonorific.Tests/ConfigWindowPreviewTests.cs
git commit -m "feat: use real track in preview, add filter template evaluation"
```

---

## Task 7: Push and open PR

- [ ] **Step 1: Push branch**

```bash
git push -u origin feature/live-preview-playback-state
```

- [ ] **Step 2: Open PR**

```bash
gh pr create --title "feat: live preview with real track and filter evaluation" --body "$(cat <<'EOF'
## Summary

- Introduces `PlaybackState` to share the currently-playing `FullTrack` between `Updater` and `ConfigWindow`
- `DrawTemplatePreview` renders against the real track when playing, falls back to mock data with a `(mock)` label when nothing is playing
- Adds live filter template evaluation in the preview panel: green checkmark when the filter matches, red X when it does not

## Test plan
- [ ] Open config while a track is playing — preview shows real song name
- [ ] Stop Spotify — preview switches to mock data with `(mock)` label
- [ ] Set a filter template to `{{ true }}` — preview shows green checkmark
- [ ] Set a filter template to `{{ false }}` — preview shows red X
- [ ] Set a filter template referencing track name — confirm it matches/doesn't match correctly
- [ ] `dotnet test` — all 80 tests pass
EOF
)"
```
