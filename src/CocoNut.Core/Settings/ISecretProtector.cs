namespace CocoNut.Core.Settings;

/// <summary>
/// Protects small secrets (NUT username/password) at rest, the way WinNUT used DPAPI for
/// <c>NUT_Username</c>/<c>NUT_Password</c>. Implementations never throw: a failure to unprotect is reported
/// as <see langword="null"/> and logged, never surfaced as an exception, and secret values are never logged.
/// </summary>
public interface ISecretProtector
{
    /// <summary>Encrypts <paramref name="plainText"/> and returns an opaque, base64-safe string to persist.</summary>
    string Protect(string plainText);

    /// <summary>
    /// Decrypts a value previously returned by <see cref="Protect"/>. Returns <see langword="null"/> and logs a
    /// warning when <paramref name="protectedText"/> is corrupt, tampered with, or was protected by another user
    /// or machine.
    /// </summary>
    string? Unprotect(string protectedText);
}
