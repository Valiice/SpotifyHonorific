using FluentAssertions;
using SpotifyHonorific.Activities;
using System.Numerics;

namespace SpotifyHonorific.Tests;

/// <summary>
/// Tests for ActivityConfig class, including cloning and default generation
/// </summary>
public class ActivityConfigTests
{
    [Fact]
    public void ActivityConfig_DefaultConstructor_ShouldInitializeProperties()
    {
        // Arrange & Act
        var config = new ActivityConfig();

        // Assert
        config.Name.Should().BeEmpty();
        config.TypeName.Should().BeEmpty();
        config.FilterTemplate.Should().BeEmpty();
        config.TitleTemplate.Should().BeEmpty();
        config.IsPrefix.Should().BeFalse();
        config.RainbowMode.Should().BeFalse();
        config.Color.Should().BeNull();
        config.Glow.Should().BeNull();
    }

    [Fact]
    public void ActivityConfig_Clone_ShouldCreateDeepCopy()
    {
        // Arrange
        var original = new ActivityConfig
        {
            Name = "Test Config",
            TypeName = "Spotify",
            FilterTemplate = "{{ true }}",
            TitleTemplate = "♪ {{ Activity.Name }} ♪",
            IsPrefix = true,
            RainbowMode = false,
            Color = new Vector3(1.0f, 0.5f, 0.0f),
            Glow = new Vector3(0.0f, 1.0f, 0.0f)
        };

        // Act
        var clone = original.Clone();

        // Assert
        clone.Should().NotBeSameAs(original);
        clone.Name.Should().Be(original.Name);
        clone.TypeName.Should().Be(original.TypeName);
        clone.FilterTemplate.Should().Be(original.FilterTemplate);
        clone.TitleTemplate.Should().Be(original.TitleTemplate);
        clone.IsPrefix.Should().Be(original.IsPrefix);
        clone.RainbowMode.Should().Be(original.RainbowMode);
        clone.Color.Should().Be(original.Color);
        clone.Glow.Should().Be(original.Glow);
    }

    [Fact]
    public void ActivityConfig_Clone_ModifyingClone_ShouldNotAffectOriginal()
    {
        // Arrange
        var original = new ActivityConfig
        {
            Name = "Original",
            IsPrefix = false
        };

        // Act
        var clone = original.Clone();
        clone.Name = "Modified";
        clone.IsPrefix = true;

        // Assert
        original.Name.Should().Be("Original");
        original.IsPrefix.Should().BeFalse();
        clone.Name.Should().Be("Modified");
        clone.IsPrefix.Should().BeTrue();
    }

    [Fact]
    public void ActivityConfig_GetDefaults_ShouldReturnConfigList()
    {
        // Act
        var defaults = ActivityConfig.GetDefaults();

        // Assert
        defaults.Should().NotBeNull();
        defaults.Should().NotBeEmpty();
        defaults.Should().HaveCountGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void ActivityConfig_GetDefaults_ShouldContainSpotifyConfig()
    {
        // Act
        var defaults = ActivityConfig.GetDefaults();

        // Assert
        var spotifyConfig = defaults.FirstOrDefault(c => c.Name.Contains("Spotify"));
        spotifyConfig.Should().NotBeNull();
        spotifyConfig!.TypeName.Should().Be("Spotify");
        spotifyConfig.TitleTemplate.Should().NotBeEmpty();
        spotifyConfig.FilterTemplate.Should().NotBeEmpty();
    }

    [Fact]
    public void ActivityConfig_GetDefaults_ShouldReturnIndependentCopies()
    {
        // Act
        var defaults1 = ActivityConfig.GetDefaults();
        var defaults2 = ActivityConfig.GetDefaults();

        // Assert
        defaults1.Should().NotBeSameAs(defaults2);
        defaults1[0].Should().NotBeSameAs(defaults2[0]);
    }

    [Fact]
    public void ActivityConfig_GetDefaults_ModifyingReturnedList_ShouldNotAffectSubsequentCalls()
    {
        // Arrange
        var firstCall = ActivityConfig.GetDefaults();
        var originalCount = firstCall.Count;
        var originalName = firstCall[0].Name;

        // Act
        firstCall[0].Name = "Modified Name";
        firstCall.Add(new ActivityConfig { Name = "Added Config" });

        var secondCall = ActivityConfig.GetDefaults();

        // Assert
        secondCall.Should().HaveCount(originalCount);
        secondCall[0].Name.Should().Be(originalName);
    }

    [Fact]
    public void ActivityConfig_ColorAndGlow_ShouldAcceptValidVectors()
    {
        // Arrange
        var config = new ActivityConfig();
        var testColor = new Vector3(0.5f, 0.7f, 0.9f);
        var testGlow = new Vector3(1.0f, 1.0f, 1.0f);

        // Act
        config.Color = testColor;
        config.Glow = testGlow;

        // Assert
        config.Color.Should().Be(testColor);
        config.Glow.Should().Be(testGlow);
    }

    [Fact]
    public void ActivityConfig_RainbowMode_ShouldToggle()
    {
        // Arrange
        var config = new ActivityConfig { RainbowMode = false };

        // Act
        config.RainbowMode = true;

        // Assert
        config.RainbowMode.Should().BeTrue();
    }

    [Fact]
    public void ActivityConfig_IsPrefix_ShouldToggle()
    {
        // Arrange
        var config = new ActivityConfig { IsPrefix = false };

        // Act
        config.IsPrefix = true;

        // Assert
        config.IsPrefix.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("Simple Name")]
    [InlineData("Complex Name With Spaces")]
    [InlineData("Name_With_Underscores")]
    [InlineData("123 Numbers")]
    public void ActivityConfig_Name_ShouldAcceptVariousStrings(string name)
    {
        // Arrange
        var config = new ActivityConfig();

        // Act
        config.Name = name;

        // Assert
        config.Name.Should().Be(name);
    }
}
