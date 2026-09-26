using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using CocoNut.Localization;
using Xunit.Abstractions;

namespace CocoNut.Core.Tests.Localization;

/// <summary>
/// Validates the resx files behind <see cref="Strings"/>: <c>Strings.resx</c> (neutral, English)
/// and one <c>Strings.&lt;culture&gt;.resx</c> per supported culture. These are plain XML files,
/// not code, so they are checked here rather than by the compiler.
/// </summary>
public sealed class StringsResxTests
{
    private static readonly string[] Cultures = ["de-DE", "fr-FR", "ru-RU", "uk-UA", "zh-CN", "zh-TW"];

    private static readonly Regex PlaceholderPattern = new(@"\{\d+\}", RegexOptions.Compiled);

    private readonly ITestOutputHelper _output;

    public StringsResxTests(ITestOutputHelper output) => _output = output;

    public static IEnumerable<object[]> CultureNames() => Cultures.Select(culture => new object[] { culture });

    [Fact]
    public void NeutralResx_EveryKeyHasAComment()
    {
        var comments = LoadComments(NeutralResxPath);

        var missing = comments.Where(pair => string.IsNullOrWhiteSpace(pair.Value)).Select(pair => pair.Key);

        Assert.Empty(missing);
    }

    [Fact]
    public void NeutralResx_HasNoEmptyValues()
    {
        var neutral = LoadValues(NeutralResxPath);

        var empty = neutral.Where(pair => string.IsNullOrWhiteSpace(pair.Value)).Select(pair => pair.Key);

        Assert.Empty(empty);
    }

    [Theory]
    [MemberData(nameof(CultureNames))]
    public void CultureResx_OnlyContainsKeysKnownToTheNeutralResx(string culture)
    {
        var neutral = LoadValues(NeutralResxPath);
        var translated = LoadValues(CultureResxPath(culture));

        var unknownKeys = translated.Keys.Where(key => !neutral.ContainsKey(key));

        Assert.Empty(unknownKeys);
    }

    [Theory]
    [MemberData(nameof(CultureNames))]
    public void CultureResx_HasNoEmptyValues(string culture)
    {
        var translated = LoadValues(CultureResxPath(culture));

        var empty = translated.Where(pair => string.IsNullOrWhiteSpace(pair.Value)).Select(pair => pair.Key);

        Assert.Empty(empty);
    }

    [Theory]
    [MemberData(nameof(CultureNames))]
    public void CultureResx_PlaceholdersMatchTheNeutralString(string culture)
    {
        var neutral = LoadValues(NeutralResxPath);
        var translated = LoadValues(CultureResxPath(culture));

        var mismatches = new List<string>();
        foreach (var (key, value) in translated)
        {
            if (value is null || !neutral.TryGetValue(key, out var neutralValue) || neutralValue is null)
            {
                continue;
            }

            if (!Placeholders(neutralValue).SetEquals(Placeholders(value)))
            {
                mismatches.Add(key);
            }
        }

        Assert.Empty(mismatches);
    }

    [Fact]
    public void Strings_AppName_Resolves() => Assert.Equal("Coco.Nut", Strings.AppName);

    [Fact]
    public void ResourceManager_GermanSatelliteAssembly_ReturnsGermanText()
    {
        var german = Strings.ResourceManager.GetString("Common_Cancel", new CultureInfo("de-DE"));

        Assert.Equal("Abbrechen", german);
    }

    [Fact]
    public void TranslationCoverage_IsReportedPerCulture()
    {
        // This is a report, not a gate (100% is not required - missing entries legitimately
        // fall back to English at runtime); see CultureResx_MeetsMinimumCoverageFloor below for
        // the regression gate, and tools/TranslationImport for the same report computed straight
        // from the WinNUT checkout.
        var neutral = LoadValues(NeutralResxPath);
        var total = neutral.Count;
        Assert.NotEqual(0, total);

        foreach (var culture in Cultures)
        {
            var translated = LoadValues(CultureResxPath(culture));
            var coverage = 100.0 * translated.Count / total;
            _output.WriteLine(
                $"{culture}: {translated.Count}/{total} keys translated ({coverage:F1}%)");
        }
    }

    /// <summary>
    /// Regression guard: a WP-B follow-up (commit 51b5f35) rewrote new_strings.csv's generator
    /// input wholesale instead of editing it, and silently dropped 15 keys' worth of translations
    /// (90 strings) from every culture in the process - a plain per-culture diff against the
    /// previous commit was the only thing that caught it. This floor makes a drop like that fail
    /// the build instead. 90% is set well below every culture's actual coverage (91.4% for
    /// zh-TW, the lowest, up to 95.7% for de-DE, as of the fix - see
    /// tools/TranslationImport/README.md's "Regression guard" section) so it only trips on a
    /// real, sizeable loss, not on the ordinary trickle of not-yet-translated new keys.
    /// </summary>
    [Theory]
    [MemberData(nameof(CultureNames))]
    public void CultureResx_MeetsMinimumCoverageFloor(string culture)
    {
        const double MinimumCoveragePercent = 90.0;

        var neutral = LoadValues(NeutralResxPath);
        var translated = LoadValues(CultureResxPath(culture));
        var coverage = 100.0 * translated.Count / neutral.Count;

        Assert.True(
            coverage >= MinimumCoveragePercent,
            $"{culture} translates only {translated.Count}/{neutral.Count} keys ({coverage:F1}%), "
                + $"below the {MinimumCoveragePercent:F0}% regression floor. If this drop is "
                + "deliberate (e.g. a batch of brand-new keys was just added and not yet "
                + "translated), lower the floor here and explain why, and update "
                + "tools/TranslationImport/README.md's \"Regression guard\" section to match; "
                + "otherwise restore the missing translations via new_strings.csv, not by "
                + "hand-editing the generated Strings.<culture>.resx files.");
    }

    private static string NeutralResxPath => Path.Combine(LocalizationDirectory, "Strings.resx");

    private static string CultureResxPath(string culture) =>
        Path.Combine(LocalizationDirectory, $"Strings.{culture}.resx");

    private static string LocalizationDirectory => FindLocalizationDirectory();

    private static string FindLocalizationDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CocoNut.sln")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new DirectoryNotFoundException(
                $"Could not find CocoNut.sln above '{AppContext.BaseDirectory}'.");
        }

        return Path.Combine(directory.FullName, "src", "CocoNut.Localization");
    }

    private static Dictionary<string, string?> LoadValues(string path)
    {
        var root = XDocument.Load(path).Root ?? throw new InvalidDataException($"{path} has no root element.");
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var data in root.Elements("data"))
        {
            var name = data.Attribute("name")?.Value
                ?? throw new InvalidDataException($"{path} has a <data> element without a name.");
            result[name] = data.Element("value")?.Value;
        }

        return result;
    }

    private static Dictionary<string, string?> LoadComments(string path)
    {
        var root = XDocument.Load(path).Root ?? throw new InvalidDataException($"{path} has no root element.");
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var data in root.Elements("data"))
        {
            var name = data.Attribute("name")?.Value
                ?? throw new InvalidDataException($"{path} has a <data> element without a name.");
            result[name] = data.Element("comment")?.Value;
        }

        return result;
    }

    private static HashSet<string> Placeholders(string text) =>
        [.. PlaceholderPattern.Matches(text).Select(match => match.Value)];
}
