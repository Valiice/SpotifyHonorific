using Scriban;
using System.Linq;

namespace SpotifyHonorific.Utils;

public static class TemplateHelper
{
    /// <summary>
    /// Extracts error messages from a Scriban template and joins them with semicolons.
    /// </summary>
    public static string GetTemplateErrors(Template template)
        => string.Join("; ", template.Messages.Select(m => m.Message));
}
