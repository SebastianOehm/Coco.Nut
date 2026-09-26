namespace CocoNut.Core.Settings;

/// <summary>Loads and saves <see cref="AppSettings"/>.</summary>
public interface ISettingsStore
{
    /// <summary>Full path of the settings file.</summary>
    string FilePath { get; }

    /// <summary>Returns defaults when the file does not exist. A corrupt file is backed up and defaults are returned.</summary>
    AppSettings Load();

    void Save(AppSettings settings);
}
