using SpotifyAPI.Web;

namespace SpotifyHonorific.Core;

// One successful poll of the Spotify player. Track is null when nothing is
// playing or playback is paused. ProgressMs comes from the same API response
// and feeds the adaptive poll interval when rate limit protection is on.
public sealed record SpotifyPollResult(FullTrack? Track, int? ProgressMs);
