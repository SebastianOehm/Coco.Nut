namespace CocoNut.Core;

/// <summary>
/// Resolves the per-user data directory and the well-known files/folders inside it (settings, logs, secret key).
/// </summary>
/// <remarks>
/// The directory defaults to <c>%APPDATA%/Coco.Nut</c> on Windows and the platform equivalent of
/// <see cref="Environment.SpecialFolder.ApplicationData"/> elsewhere (e.g. <c>~/.config/Coco.Nut</c> on Linux).
/// It can be overridden with the <c>COCONUT_DATA_DIR</c> environment variable, which is useful for portable
/// installs and for tests that must not touch the real user profile.
/// </remarks>
public static class AppPaths
{
    /// <summary>Name of the environment variable that overrides <see cref="DataDirectory"/>.</summary>
    public const string DataDirectoryEnvironmentVariable = "COCONUT_DATA_DIR";

    private const string AppFolderName = "Coco.Nut";
    private const string SettingsFileName = "settings.json";
    private const string LogFolderName = "logs";

    /// <summary>
    /// The per-user data directory, created on demand. Honours <see cref="DataDirectoryEnvironmentVariable"/>
    /// when set to a non-empty value.
    /// </summary>
    public static string DataDirectory
    {
        get
        {
            var overridePath = Environment.GetEnvironmentVariable(DataDirectoryEnvironmentVariable);
            var path = string.IsNullOrWhiteSpace(overridePath)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppFolderName)
                : overridePath;

            Directory.CreateDirectory(path);
            return path;
        }
    }

    /// <summary>Full path of the JSON settings file, inside <see cref="DataDirectory"/>.</summary>
    public static string SettingsFile => Path.Combine(DataDirectory, SettingsFileName);

    /// <summary>Full path of the log directory, created on demand inside <see cref="DataDirectory"/>.</summary>
    public static string LogDirectory
    {
        get
        {
            var path = Path.Combine(DataDirectory, LogFolderName);
            Directory.CreateDirectory(path);
            return path;
        }
    }
}
