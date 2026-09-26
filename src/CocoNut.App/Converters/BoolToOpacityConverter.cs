using System.Globalization;
using Avalonia.Data.Converters;

namespace CocoNut.App.Converters;

/// <summary>
/// Converts a status-flag bool to full opacity when active or a dimmed opacity when not, for the main window's
/// four colored status badges (WinNUT's <c>Main_Flag_*</c> labels, which changed background color the same way).
/// </summary>
public sealed class BoolToOpacityConverter : IValueConverter
{
    /// <summary>Shared instance, referenced from XAML via <c>{x:Static conv:BoolToOpacityConverter.Instance}</c>.</summary>
    public static readonly BoolToOpacityConverter Instance = new();

    private const double ActiveOpacity = 1.0;
    private const double InactiveOpacity = 0.35;

    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? ActiveOpacity : InactiveOpacity;

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
