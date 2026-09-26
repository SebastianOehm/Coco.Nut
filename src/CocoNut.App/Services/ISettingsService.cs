using CocoNut.Core.Settings;

namespace CocoNut.App.Services;

/// <summary>
/// Holds the single, live <see cref="AppSettings"/> instance the whole app shares, and notifies interested
/// parties (view models, the loggers, the tray icon, …) when it changes. Backed by an <see cref="ISettingsStore"/>
/// for persistence (<see cref="JsonSettingsStore"/> in the real app).
/// </summary>
public interface ISettingsService
{
    /// <summary>The current settings. Never <see langword="null"/>; replaced wholesale by <see cref="Save"/>.</summary>
    AppSettings Current { get; }

    /// <summary>Persists <paramref name="settings"/>, makes it <see cref="Current"/>, and raises <see cref="SettingsChanged"/>.</summary>
    void Save(AppSettings settings);

    /// <summary>Raised after <see cref="Save"/> has updated <see cref="Current"/>, with the new settings.</summary>
    event EventHandler<AppSettings>? SettingsChanged;
}

/// <summary>Default <see cref="ISettingsService"/>, backed by an <see cref="ISettingsStore"/>.</summary>
public sealed class SettingsService : ISettingsService
{
    private readonly ISettingsStore _store;

    /// <param name="store">Persists <see cref="Save"/>d settings and supplies the initial value.</param>
    /// <param name="initial">
    /// The initial value of <see cref="Current"/>. Defaults to <c><paramref name="store"/>.Load()</c>; callers that
    /// already loaded the settings (to build other services from them before the DI container exists) pass that
    /// same instance here instead of loading the file twice.
    /// </param>
    public SettingsService(ISettingsStore store, AppSettings? initial = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        Current = initial ?? _store.Load();
    }

    /// <inheritdoc />
    public AppSettings Current { get; private set; }

    /// <inheritdoc />
    public event EventHandler<AppSettings>? SettingsChanged;

    /// <inheritdoc />
    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _store.Save(settings);
        Current = settings;
        SettingsChanged?.Invoke(this, settings);
    }
}
