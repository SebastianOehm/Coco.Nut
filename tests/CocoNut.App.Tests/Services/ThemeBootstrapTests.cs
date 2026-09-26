using Avalonia.Styling;
using CocoNut.App.Services;
using CocoNut.Core.Settings;

namespace CocoNut.App.Tests.Services;

public class ThemeBootstrapTests
{
    [Theory]
    [InlineData(AppTheme.System, "Default")]
    [InlineData(AppTheme.Light, "Light")]
    [InlineData(AppTheme.Dark, "Dark")]
    public void Maps_setting_to_theme_variant(AppTheme theme, string expected)
    {
        ThemeVariant variant = ThemeBootstrap.ToThemeVariant(theme);

        Assert.Equal(expected, variant.Key.ToString());
    }
}
