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
    public void Activate_LongRetryAfter_IsHonoredInFull()
    {
        // Spotify tells us exactly when to come back; probing earlier is a
        // guaranteed 429 that can extend the penalty.
        var gate = new RateLimitGate();

        var pause = gate.Activate(TimeSpan.FromHours(6), Now);

        pause.Should().Be(TimeSpan.FromHours(6));
        gate.IsActive(Now.AddHours(5)).Should().BeTrue();
        gate.IsActive(Now.AddHours(7)).Should().BeFalse();
    }

    [Fact]
    public void Activate_AbsurdRetryAfter_IsSanityCappedAt24Hours()
    {
        var gate = new RateLimitGate();

        var pause = gate.Activate(TimeSpan.FromHours(48), Now);

        pause.Should().Be(TimeSpan.FromHours(24));
    }

    [Fact]
    public void Activate_ConsecutiveFallbacks_DoubleFrom30Seconds()
    {
        // No Retry-After header: 30s matches Spotify's rolling window, and
        // repeated header-less 429s must back off instead of probing forever.
        var gate = new RateLimitGate();

        gate.Activate(TimeSpan.Zero, Now).Should().Be(TimeSpan.FromSeconds(30));
        gate.Activate(TimeSpan.Zero, Now).Should().Be(TimeSpan.FromMinutes(1));
        gate.Activate(TimeSpan.Zero, Now).Should().Be(TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void Activate_ManyConsecutiveFallbacks_CapAtOneHour()
    {
        var gate = new RateLimitGate();

        for (var i = 0; i < 12; i++)
        {
            gate.Activate(TimeSpan.Zero, Now);
        }

        gate.Activate(TimeSpan.Zero, Now).Should().Be(TimeSpan.FromHours(1));
    }

    [Fact]
    public void ResetEscalation_ReturnsFallbackToBase()
    {
        var gate = new RateLimitGate();
        gate.Activate(TimeSpan.Zero, Now);
        gate.Activate(TimeSpan.Zero, Now);

        gate.ResetEscalation();

        gate.FallbackEscalationCount.Should().Be(0);
        gate.Activate(TimeSpan.Zero, Now).Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void Activate_ExplicitRetryAfter_DoesNotAdvanceFallbackEscalation()
    {
        var gate = new RateLimitGate();
        gate.Activate(TimeSpan.Zero, Now);

        gate.Activate(TimeSpan.FromSeconds(45), Now);

        gate.FallbackEscalationCount.Should().Be(1);
        gate.Activate(TimeSpan.Zero, Now).Should().Be(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void Activate_ReturnsTheChosenPause()
    {
        var gate = new RateLimitGate();

        var pause = gate.Activate(TimeSpan.FromSeconds(90), Now);

        pause.Should().Be(TimeSpan.FromSeconds(90));
    }

    [Fact]
    public void Remaining_NewGate_IsZero()
    {
        var gate = new RateLimitGate();

        gate.Remaining(Now).Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Remaining_WhileActive_ReturnsTimeLeft()
    {
        var gate = new RateLimitGate();
        gate.Activate(TimeSpan.FromSeconds(30), Now);

        gate.Remaining(Now.AddSeconds(10)).Should().Be(TimeSpan.FromSeconds(20));
    }

    [Fact]
    public void Remaining_AfterExpiry_IsZero()
    {
        var gate = new RateLimitGate();
        gate.Activate(TimeSpan.FromSeconds(30), Now);

        gate.Remaining(Now.AddSeconds(31)).Should().Be(TimeSpan.Zero);
    }
}
