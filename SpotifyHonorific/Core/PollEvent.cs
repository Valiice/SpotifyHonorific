using System;

namespace SpotifyHonorific.Core;

/// <summary>
/// One entry in the diagnostic timeline of Spotify interactions.
/// Kinds: pollOk, timeout, rateLimited, apiError, tokenRefresh.
/// </summary>
public sealed record PollEvent(DateTime Time, string Kind, string? Detail);
