using CocoNut.Core;
using CocoNut.Core.Tests.TestSupport;

namespace CocoNut.Core.Tests.Settings;

/// <summary>
/// Exercises the <see cref="CocoNut.Core.AppPaths.DataDirectoryEnvironmentVariable"/> override. Not run in
/// parallel with anything else that touches the same process-wide environment variable.
/// </summary>
public sealed class AppPathsTests : IDisposable
{
    private readonly string? _originalValue =
        Environment.GetEnvironmentVariable(AppPaths.DataDirectoryEnvironmentVariable);

    public void Dispose() =>
        Environment.SetEnvironmentVariable(AppPaths.DataDirectoryEnvironmentVariable, _originalValue);

    [Fact]
    public void DataDirectory_WithEnvironmentOverride_UsesAndCreatesIt()
    {
        using var temp = new TempDirectory();
        var overridePath = Path.Combine(temp.Path, "override-data-dir");
        Environment.SetEnvironmentVariable(AppPaths.DataDirectoryEnvironmentVariable, overridePath);

        var result = AppPaths.DataDirectory;

        Assert.Equal(overridePath, result);
        Assert.True(Directory.Exists(overridePath));
    }

    [Fact]
    public void SettingsFile_IsInsideDataDirectory()
    {
        using var temp = new TempDirectory();
        Environment.SetEnvironmentVariable(AppPaths.DataDirectoryEnvironmentVariable, temp.Path);

        Assert.Equal(Path.Combine(temp.Path, "settings.json"), AppPaths.SettingsFile);
    }

    [Fact]
    public void LogDirectory_IsInsideDataDirectory_AndCreated()
    {
        using var temp = new TempDirectory();
        Environment.SetEnvironmentVariable(AppPaths.DataDirectoryEnvironmentVariable, temp.Path);

        var logDirectory = AppPaths.LogDirectory;

        Assert.Equal(Path.Combine(temp.Path, "logs"), logDirectory);
        Assert.True(Directory.Exists(logDirectory));
    }
}
