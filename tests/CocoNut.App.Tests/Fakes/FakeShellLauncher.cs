using CocoNut.App.Services;

namespace CocoNut.App.Tests.Fakes;

/// <summary>Records <see cref="IShellLauncher"/> calls instead of launching a real process.</summary>
public sealed class FakeShellLauncher : IShellLauncher
{
    public List<string> OpenedUrls { get; } = [];

    public List<string> OpenedFiles { get; } = [];

    public void OpenUrl(string url) => OpenedUrls.Add(url);

    public void OpenFile(string path) => OpenedFiles.Add(path);
}
