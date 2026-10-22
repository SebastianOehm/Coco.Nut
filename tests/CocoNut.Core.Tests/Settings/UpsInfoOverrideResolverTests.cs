using CocoNut.Core.History;
using CocoNut.Core.Settings;
using CocoNut.Core.Ups;

namespace CocoNut.Core.Tests.Settings;

public class UpsInfoOverrideResolverTests
{
    private static readonly UpsKey Key = new("Nut.Example", 3493, "ups");

    // ---- UpsInfo.IsReported -------------------------------------------------------------------------------------

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("Unknown", false)]
    [InlineData("unknown", false)]
    [InlineData("  UNKNOWN ", false)]
    [InlineData("APC", true)]
    [InlineData("Unknown Brand", true)]
    public void IsReported_OnlyTrueForARealValue(string? value, bool expected) =>
        Assert.Equal(expected, UpsInfo.IsReported(value));

    [Fact]
    public void UnknownValue_IsWhatBuildInfoReportsForAMissingVariable() =>
        Assert.Equal(UpsInfo.UnknownValue, UpsCalculations.BuildInfo(new Dictionary<string, string>()).Manufacturer);

    // ---- Resolve ------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("APC", "Mine")]
    [InlineData("APC", null)]
    [InlineData("  APC ", "Mine")]
    public void Resolve_AReportedValueWinsAndIsNeverMarkedAsOverridden(string reported, string? overrideValue)
    {
        var (text, isOverridden) = UpsInfoOverrideResolver.Resolve(reported, overrideValue);

        Assert.Equal("APC", text);
        Assert.False(isOverridden);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Unknown")]
    [InlineData("unknown")]
    public void Resolve_WhenNotReported_TheOverrideIsUsedAndMarked(string? reported)
    {
        var (text, isOverridden) = UpsInfoOverrideResolver.Resolve(reported, " Mine ");

        Assert.Equal("Mine", text);
        Assert.True(isOverridden);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("Unknown", "")]
    [InlineData("", "   ")]
    public void Resolve_WhenNeitherIsAvailable_ThereIsNothingToShow(string? reported, string? overrideValue)
    {
        var (text, isOverridden) = UpsInfoOverrideResolver.Resolve(reported, overrideValue);

        Assert.Null(text);
        Assert.False(isOverridden);
    }

    // ---- Find / SetOverride -------------------------------------------------------------------------------------

    [Fact]
    public void SetOverride_CreatesTheEntryTrimsTheValueAndKeepsTheOtherFieldEmpty()
    {
        var settings = new AppSettings();

        UpsInfoOverrideResolver.SetOverride(settings, Key, UpsInfoField.Manufacturer, "  Acme Corp ");

        var entry = Assert.Single(settings.InfoOverrides.Entries);
        Assert.Equal("Nut.Example", entry.Host);
        Assert.Equal(3493, entry.Port);
        Assert.Equal("ups", entry.UpsName);
        Assert.Equal("Acme Corp", entry.Manufacturer);
        Assert.Null(entry.Model);
    }

    [Fact]
    public void SetOverride_BothFieldsLandInTheSameEntry()
    {
        var settings = new AppSettings();

        UpsInfoOverrideResolver.SetOverride(settings, Key, UpsInfoField.Manufacturer, "Acme");
        UpsInfoOverrideResolver.SetOverride(settings, Key, UpsInfoField.Model, "X-1000");

        var entry = Assert.Single(settings.InfoOverrides.Entries);
        Assert.Equal("Acme", entry.Manufacturer);
        Assert.Equal("X-1000", entry.Model);
        Assert.Equal("Acme", UpsInfoOverrideResolver.GetOverride(settings, Key, UpsInfoField.Manufacturer));
        Assert.Equal("X-1000", UpsInfoOverrideResolver.GetOverride(settings, Key, UpsInfoField.Model));
    }

    [Fact]
    public void Find_UsesUpsKeySemantics_HostIgnoresCaseUpsNameAndPortDoNot()
    {
        var settings = new AppSettings();
        UpsInfoOverrideResolver.SetOverride(settings, Key, UpsInfoField.Model, "X-1000");

        Assert.NotNull(UpsInfoOverrideResolver.Find(settings, new UpsKey("NUT.EXAMPLE", 3493, "ups")));
        Assert.Null(UpsInfoOverrideResolver.Find(settings, new UpsKey("nut.example", 3493, "UPS")));
        Assert.Null(UpsInfoOverrideResolver.Find(settings, new UpsKey("nut.example", 3494, "ups")));
        Assert.Null(UpsInfoOverrideResolver.Find(settings, new UpsKey("other", 3493, "ups")));
        Assert.Null(UpsInfoOverrideResolver.GetOverride(settings, new UpsKey("other", 3493, "ups"), UpsInfoField.Model));
    }

    [Fact]
    public void SetOverride_IsKeptPerUps()
    {
        var settings = new AppSettings();
        var other = new UpsKey("other", 3493, "ups");

        UpsInfoOverrideResolver.SetOverride(settings, Key, UpsInfoField.Model, "A");
        UpsInfoOverrideResolver.SetOverride(settings, other, UpsInfoField.Model, "B");

        Assert.Equal(2, settings.InfoOverrides.Entries.Count);
        Assert.Equal("A", UpsInfoOverrideResolver.GetOverride(settings, Key, UpsInfoField.Model));
        Assert.Equal("B", UpsInfoOverrideResolver.GetOverride(settings, other, UpsInfoField.Model));
    }

    [Fact]
    public void SetOverride_ReplacesAnExistingValueAndMatchesTheHostCaseInsensitively()
    {
        var settings = new AppSettings();
        UpsInfoOverrideResolver.SetOverride(settings, Key, UpsInfoField.Model, "Old");

        UpsInfoOverrideResolver.SetOverride(settings, new UpsKey("NUT.EXAMPLE", 3493, "ups"), UpsInfoField.Model, "New");

        var entry = Assert.Single(settings.InfoOverrides.Entries);
        Assert.Equal("New", entry.Model);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SetOverride_EmptyInputClearsOnlyThatField(string? empty)
    {
        var settings = new AppSettings();
        UpsInfoOverrideResolver.SetOverride(settings, Key, UpsInfoField.Manufacturer, "Acme");
        UpsInfoOverrideResolver.SetOverride(settings, Key, UpsInfoField.Model, "X-1000");

        UpsInfoOverrideResolver.SetOverride(settings, Key, UpsInfoField.Manufacturer, empty);

        var entry = Assert.Single(settings.InfoOverrides.Entries);
        Assert.Null(entry.Manufacturer);
        Assert.Equal("X-1000", entry.Model);
    }

    [Fact]
    public void SetOverride_ClearingBothFieldsRemovesTheEntry()
    {
        var settings = new AppSettings();
        UpsInfoOverrideResolver.SetOverride(settings, Key, UpsInfoField.Manufacturer, "Acme");
        UpsInfoOverrideResolver.SetOverride(settings, Key, UpsInfoField.Model, "X-1000");

        UpsInfoOverrideResolver.SetOverride(settings, Key, UpsInfoField.Manufacturer, "");
        UpsInfoOverrideResolver.SetOverride(settings, Key, UpsInfoField.Model, null);

        Assert.Empty(settings.InfoOverrides.Entries);
        Assert.Null(UpsInfoOverrideResolver.Find(settings, Key));
    }

    [Fact]
    public void SetOverride_ClearingWithoutAnEntryCreatesNothing()
    {
        var settings = new AppSettings();

        UpsInfoOverrideResolver.SetOverride(settings, Key, UpsInfoField.Model, "  ");

        Assert.Empty(settings.InfoOverrides.Entries);
    }

    [Fact]
    public void Clone_CopiesTheOverridesDeeply()
    {
        var original = new AppSettings();
        UpsInfoOverrideResolver.SetOverride(original, Key, UpsInfoField.Model, "X-1000");

        var copy = original.Clone();
        UpsInfoOverrideResolver.SetOverride(copy, Key, UpsInfoField.Model, "Changed");

        Assert.Equal("X-1000", UpsInfoOverrideResolver.GetOverride(original, Key, UpsInfoField.Model));
        Assert.Equal("Changed", UpsInfoOverrideResolver.GetOverride(copy, Key, UpsInfoField.Model));
    }
}
