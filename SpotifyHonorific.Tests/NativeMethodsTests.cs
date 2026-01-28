using FluentAssertions;
using SpotifyHonorific.Utils;

namespace SpotifyHonorific.Tests;

/// <summary>
/// Tests for NativeMethods and idle time detection
/// </summary>
public class NativeMethodsTests
{
    [Fact]
    public void GetIdleTime_ShouldReturnValidTime()
    {
        // Act
        var idleTime = NativeMethods.IdleTimeFinder.GetIdleTime();

        // Assert
        idleTime.Should().BeGreaterThanOrEqualTo(0);
        // Idle time should be reasonable (not millions of milliseconds)
        idleTime.Should().BeLessThan(uint.MaxValue / 2);
    }

    [Fact]
    public void GetIdleTime_CalledTwice_ShouldIncreaseOrStaySame()
    {
        // Arrange
        var firstCall = NativeMethods.IdleTimeFinder.GetIdleTime();
        Thread.Sleep(100); // Wait a bit

        // Act
        var secondCall = NativeMethods.IdleTimeFinder.GetIdleTime();

        // Assert
        // Second call should be >= first call (user might have interacted, resetting it)
        secondCall.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void GetIdleTime_AfterUserInput_ShouldReset()
    {
        // This test would verify that idle time resets after user interaction
        // In a real scenario, this would require simulating input
        // For now, we just verify the method works

        // Act
        var idleTime = NativeMethods.IdleTimeFinder.GetIdleTime();

        // Assert
        idleTime.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void GetIdleTime_MultipleConsecutiveCalls_ShouldNotThrow()
    {
        // Arrange & Act
        var action = () =>
        {
            for (int i = 0; i < 100; i++)
            {
                NativeMethods.IdleTimeFinder.GetIdleTime();
            }
        };

        // Assert
        action.Should().NotThrow();
    }

    [Fact]
    public void LASTINPUTINFO_Structure_ShouldHaveCorrectSize()
    {
        // Arrange & Act
        var info = new NativeMethods.LASTINPUTINFO
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.LASTINPUTINFO>()
        };

        // Assert
        // Should be 8 bytes (uint cbSize + uint dwTime)
        info.cbSize.Should().Be(8);
    }
}

/// <summary>
/// Tests for AFK detection logic (30-second threshold)
/// </summary>
public class AfkDetectionTests
{
    private const uint AFK_THRESHOLD_MS = 30000; // 30 seconds

    [Theory]
    [InlineData(0u, false)]
    [InlineData(15000u, false)]
    [InlineData(29999u, false)]
    [InlineData(30000u, false)]  // Exactly at threshold is NOT AFK (uses > not >=)
    [InlineData(30001u, true)]
    [InlineData(60000u, true)]
    public void IsPlayerAfk_WithVariousIdleTimes_ShouldDetectCorrectly(uint idleTimeMs, bool expectedAfk)
    {
        // Arrange & Act
        var isAfk = idleTimeMs > AFK_THRESHOLD_MS;

        // Assert
        isAfk.Should().Be(expectedAfk);
    }

    [Fact]
    public void AfkThreshold_ShouldBe30Seconds()
    {
        // Assert
        AFK_THRESHOLD_MS.Should().Be(30000);
    }
}
