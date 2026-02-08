# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build Commands

```bash
# Build (requires Dalamud SDK - CI downloads it automatically)
dotnet build --configuration Release SpotifyHonorific/SpotifyHonorific.csproj

# Restore dependencies
dotnet restore
```

**Local Development**: Set `DALAMUD_HOME` environment variable to your Dalamud installation path before building.

## Architecture

This is a **FFXIV Dalamud plugin** that updates a character's honorific title based on currently playing Spotify track.

### Core Components

- **Plugin.cs** - Entry point implementing `IDalamudPlugin`, registers `/spotifyhonorific config` command
- **Updater.cs** - Core logic: polls Spotify API every 2 seconds, renders titles via Scriban templates, communicates with Honorific plugin via IPC (`Honorific.SetCharacterTitle`, `Honorific.ClearCharacterTitle`)
- **ConfigWindow.cs** - ImGui configuration UI
- **ActivityConfig.cs** - Customizable activity display profiles with Scriban templates

### Key Patterns

- **Scriban templating** for dynamic title generation with variables: `Activity` (Spotify FullTrack), `Context.SecsElapsed`
- **IPC integration** with Honorific plugin for title display
- **OAuth PKCE flow** via local HTTP server on `http://127.0.0.1:5000/callback`
- **AFK detection** using Win32 P/Invoke to pause polling when idle

### Constraints

- **Max title length**: 32 characters
- **Token refresh**: Every 55 minutes (before 60-minute expiry)
- **AFK threshold**: 30 seconds idle pauses polling

## Code Style

Follow **SoC** (Separation of Concerns), **KISS** (Keep It Simple), and **DRY** (Don't Repeat Yourself). Extract focused methods per concern, avoid unnecessary abstractions, and keep solutions minimal. Prioritize readability — use clear, descriptive method and variable names, and break logic into well-named methods.

## Tech Stack

- C# / .NET 10.0 (Windows, x64)
- Dalamud.NET.Sdk 14.0.1
- SpotifyAPI.Web 7.2.1 (Spotify Web API client)
- Scriban 6.5.2 (templating)
