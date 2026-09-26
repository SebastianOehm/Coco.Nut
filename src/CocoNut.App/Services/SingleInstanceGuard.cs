namespace CocoNut.App.Services;

/// <summary>
/// A per-user named <see cref="Mutex"/> that lets the app detect a second instance of itself, the way WinNUT's
/// <c>ApplicationEvents.vb</c> used a <c>SingleInstance</c> application. On a platform/configuration where named
/// mutexes are not available, this fails open (<see cref="IsFirstInstance"/> is <see langword="true"/>) rather
/// than refusing to start the app.
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    private readonly Mutex? _mutex;
    private readonly bool _owns;

    /// <param name="name">
    /// Distinguishes this app's mutex from any other; defaults to a name scoped to the current user so different
    /// user sessions on the same machine (or, on Windows, different users under Terminal Services) do not see
    /// each other's instance.
    /// </param>
    public SingleInstanceGuard(string? name = null)
    {
        string mutexName = name ?? $"CocoNut_SingleInstance_{Environment.UserName}";

        try
        {
            _mutex = new Mutex(initiallyOwned: true, mutexName, out bool createdNew);
            IsFirstInstance = createdNew;
            _owns = createdNew;
        }
        catch (PlatformNotSupportedException)
        {
            // Named synchronization primitives are unavailable in this environment; do not block startup over it.
            IsFirstInstance = true;
        }
        catch (UnauthorizedAccessException)
        {
            // A mutex of this name already exists, owned by another user/session that we cannot open.
            IsFirstInstance = true;
        }
    }

    /// <summary><see langword="true"/> when no other instance currently holds the mutex (or it could not be checked).</summary>
    public bool IsFirstInstance { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_owns)
        {
            try
            {
                _mutex!.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Not owned (already released, or never acquired) - nothing to release.
            }
        }

        _mutex?.Dispose();
    }
}
