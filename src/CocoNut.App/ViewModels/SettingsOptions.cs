using CocoNut.Core.Abstractions;
using CocoNut.Core.Settings;
using Microsoft.Extensions.Logging;

namespace CocoNut.App.ViewModels;

/// <summary>One selectable entry of the Settings window's log-level combo box (Settings &gt; Logging).</summary>
public sealed record LogLevelOption(LogLevel Value, string Text)
{
    /// <inheritdoc />
    public override string ToString() => Text;
}

/// <summary>One selectable entry of the Settings window's stop-action combo box (Settings &gt; Power).</summary>
public sealed record StopActionOption(StopAction Value, string Text)
{
    /// <inheritdoc />
    public override string ToString() => Text;
}

/// <summary>One selectable entry of the Settings window's update-channel combo box (Settings &gt; Update).</summary>
public sealed record UpdateChannelOption(UpdateChannel Value, string Text)
{
    /// <inheritdoc />
    public override string ToString() => Text;
}

/// <summary>
/// One selectable entry of the Settings window's update-check-interval combo box (Settings &gt; Update).
/// <see cref="Days"/> is the value stored in <see cref="UpdateSettings.AutoCheckIntervalDays"/> (Daily/Weekly/Monthly = 1/7/30).
/// </summary>
public sealed record UpdateIntervalOption(int Days, string Text)
{
    /// <inheritdoc />
    public override string ToString() => Text;
}

/// <summary>
/// One selectable entry of the Settings window's language combo box (Settings &gt; Misc). <see cref="CultureCode"/>
/// is the value stored in <see cref="GeneralSettings.Language"/> (<see langword="null"/> = follow the OS).
/// </summary>
public sealed record LanguageOption(string? CultureCode, string Text)
{
    /// <inheritdoc />
    public override string ToString() => Text;
}

/// <summary>One selectable entry of the Settings window's theme combo box (Settings &gt; Misc).</summary>
public sealed record ThemeOption(AppTheme Value, string Text)
{
    /// <inheritdoc />
    public override string ToString() => Text;
}
