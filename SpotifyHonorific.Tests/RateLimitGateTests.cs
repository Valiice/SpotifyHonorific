using FluentAssertions;
using SpotifyHonorific.Core;

namespace SpotifyHonorific.Tests;

public class RateLimitGateTests
{
    private static readonly DateTime Now = new(2026, 7, 8, 22, 0, 0);

    [Fact]
    public void NewGate_IsNotActive()
    {
        var gate = new RateLimitGate();

        gate.IsActive(Now).Should().BeFalse();
    }

    [Fact]
    public void Activate_PausesForRetryAfterDuration()
    {
        var gate = new RateLimitGate();

        gate.Activate(TimeSpan.FromSeconds(30), Now);

        gate.IsActive(Now.AddSeconds(29)).Should().BeTrue();
        gate.IsActive(Now.AddSeconds(31)).Should().BeFalse();
    }

    [Fact]
    public void Activate_NonPositiveRetryAfter_FallsBackToDefaultPause()
    {
        // Spotify does not always send a Retry-After header; the gate must
        // still pause rather than letting the next poll fire 2 seconds later.
        var gate = new RateLimitGate();

        var pause = gate.Activate(TimeSpan.Zero, Now);

        pause.Should().Be(TimeSpan.FromSeconds(30));
        gate.IsActive(Now.AddSeconds(29)).Should().BeTrue();
    }

    [Fact]
    public void Activate_ExcessiveRetryAfter_IsCappedSoPollingEventuallyResumes()
    {
        // Retry-After can be hours on badly rate-limited apps; cap it so the
        // plugin re-checks within a reasonable time instead of going silent.
        var gate = new RateLimitGate();

        var pause = gate.Activate(TimeSpan.FromHours(6), Now);

        pause.Should().Be(TimeSpan.FromMinutes(15));
        gate.IsActive(Now.AddMinutes(14)).Should().BeTrue();
        gate.IsActive(Now.AddMinutes(16)).Should().BeFalse();
    }

    [Fact]
    public void Activate_ReturnsTheChosenPause()
    {
        var gate = new RateLimitGate();

        var pause = gate.Activate(TimeSpan.FromSeconds(90), Now);

        pause.Should().Be(TimeSpan.FromSeconds(90));
    }
}
