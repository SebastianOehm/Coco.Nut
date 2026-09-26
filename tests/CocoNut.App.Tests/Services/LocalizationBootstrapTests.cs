using System.Globalization;
using CocoNut.App.Services;
using CocoNut.Localization;

namespace CocoNut.App.Tests.Services;

public class LocalizationBootstrapTests
{
    [Fact]
    public void Apply_with_a_culture_name_sets_Strings_Culture_and_the_thread_cultures()
    {
        LocalizationBootstrap.Apply("de-DE");

        Assert.Equal("de-DE", Strings.Culture!.Name);
        Assert.Equal("de-DE", CultureInfo.CurrentUICulture.Name);
        Assert.Equal("de-DE", CultureInfo.DefaultThreadCurrentCulture!.Name);
        Assert.Equal("de-DE", CultureInfo.DefaultThreadCurrentUICulture!.Name);
        Assert.Equal("Abbrechen", Strings.Common_Cancel);
    }

    [Fact]
    public void Apply_with_null_follows_the_operating_system_culture()
    {
        LocalizationBootstrap.Apply(null);

        Assert.Equal(CultureInfo.InstalledUICulture, Strings.Culture);
    }

    [Fact]
    public void Apply_with_an_empty_or_whitespace_name_also_follows_the_operating_system_culture()
    {
        LocalizationBootstrap.Apply("   ");

        Assert.Equal(CultureInfo.InstalledUICulture, Strings.Culture);
    }
}
