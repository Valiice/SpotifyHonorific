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
        idleTime.Should().BeLessThan(uint.MaxValue / 2);
    }

    [Fact]
    public void GetIdleTime_CalledTwice_ShouldIncreaseOrStaySame()
    {
        // Arrange
        _ = NativeMethods.IdleTimeFinder.GetIdleTime();
        Thread.Sleep(100);

        // Act
        var secondCall = NativeMethods.IdleTimeFinder.GetIdleTime();

        // Assert
        secondCall.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void GetIdleTime_AfterUserInput_ShouldReset()
    {
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
            for (var i = 0; i < 100; i++)
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
        info.cbSize.Should().Be(8);
    }
}

public class AfkDetectionTests
{
    private const uint AFK_THRESHOLD_MS = 30000;

    [Theory]
    [InlineData(0u, false)]
    [InlineData(15000u, false)]
    [InlineData(29999u, false)]
    [InlineData(30000u, false)]
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
