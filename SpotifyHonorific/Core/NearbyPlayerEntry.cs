using System;

namespace SpotifyHonorific.Core;

public sealed class NearbyPlayerEntry
{
    public required string CharacterName { get; set; }
    public required string RawTitle { get; set; }
    public required DateTime LastSeen { get; set; }
}
