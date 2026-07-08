using Scriban;
using SpotifyHonorific.Activities;
using System.Linq;

namespace SpotifyHonorific.Utils;

public static class TemplateHelper
{
    /// <summary>
    /// Extracts error messages from a Scriban template and joins them with semicolons.
    /// </summary>
    public static string GetTemplateErrors(Template template)
        => string.Join("; ", template.Messages.Select(m => m.Message));

    /// <summary>
    /// True if the rendered title can change over time: rainbow mode animates the
    /// color, and "Context" is the only time-varying render variable (exact member
    /// name; Scriban's renamer exposes property names verbatim). False positives
    /// only cause unnecessary re-renders, which is the safe direction.
    /// </summary>
    public static bool IsTimeDependent(ActivityConfig config)
        => config.RainbowMode || config.TitleTemplate.Contains("Context");
}
