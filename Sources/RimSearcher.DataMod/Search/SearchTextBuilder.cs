using System.Collections.Generic;
using System.Text;

namespace RimSearcher.DataMod.Search;

internal static class SearchTextBuilder
{
    public static string Build(
        string? defName,
        string? label,
        string? description,
        IReadOnlyList<string> fieldTexts)
    {
        var builder = new StringBuilder();
        AppendIfPresent(builder, defName);
        AppendIfPresent(builder, label);
        AppendIfPresent(builder, description);

        foreach (var text in fieldTexts)
            builder.Append(text).Append(' ');

        return CjkBigramExpander.Expand(builder.ToString().Trim());
    }

    private static void AppendIfPresent(StringBuilder builder, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            builder.Append(value).Append(' ');
    }
}
