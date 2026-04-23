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
