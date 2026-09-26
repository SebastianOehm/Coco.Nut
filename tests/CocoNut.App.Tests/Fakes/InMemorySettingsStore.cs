using CocoNut.Core.Settings;

namespace CocoNut.App.Tests.Fakes;

/// <summary>A trivial <see cref="ISettingsStore"/> that keeps settings in memory instead of on disk.</summary>
public sealed class InMemorySettingsStore : ISettingsStore
{
    public AppSettings Settings { get; set; } = new();

    public string FilePath => "in-memory";

    public int SaveCount { get; private set; }

    public AppSettings Load() => Settings;

    public void Save(AppSettings settings)
    {
        Settings = settings;
        SaveCount++;
    }
}
