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
        // This is a report, not a gate: a culture legitimately covers less than 100% of the
        // neutral keys (missing entries fall back to English at runtime), so no minimum
        // percentage is enforced here. See tools/TranslationImport for the same report
        // computed straight from the WinNUT checkout.
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
