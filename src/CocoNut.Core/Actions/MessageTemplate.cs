using System.Globalization;
using System.Text.RegularExpressions;

namespace CocoNut.Core.Actions;

/// <summary>
/// Simple <c>{placeholder}</c> substitution for the messages of <see cref="ActionKind.Notify"/> and
/// <see cref="ActionKind.Log"/>. The placeholders are listed in <see cref="ActionContext.PlaceholderNames"/>; the
/// names are case sensitive, an unknown one (a typo, or braces that are not a placeholder) is left as it is, and a
/// known one whose value is unknown becomes the empty string.
/// </summary>
public static partial class MessageTemplate
{
    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex PlaceholderPattern();

    /// <summary>Replaces the placeholders in <paramref name="template"/>; numbers are formatted with <paramref name="culture"/>.</summary>
    public static string Expand(string template, ActionContext context, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(culture);

        if (string.IsNullOrEmpty(template))
        {
            return string.Empty;
        }

        var values = context.GetPlaceholders(culture);
        return PlaceholderPattern().Replace(template, match => values.TryGetValue(match.Groups[1].Value, out var value) ? value : match.Value);
    }
}
