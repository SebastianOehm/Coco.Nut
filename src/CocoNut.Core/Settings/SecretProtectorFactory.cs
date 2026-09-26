using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CocoNut.Core.Settings;

/// <summary>Creates the <see cref="ISecretProtector"/> appropriate for the current platform.</summary>
public static class SecretProtectorFactory
{
    /// <summary>
    /// Returns <see cref="DpapiSecretProtector"/> on Windows and <see cref="AesKeyFileSecretProtector"/>
    /// (keyed from a file under <paramref name="dataDirectory"/>) everywhere else.
    /// </summary>
    /// <param name="dataDirectory">Data directory used by <see cref="AesKeyFileSecretProtector"/> for its key file.</param>
    /// <param name="loggerFactory">Optional logger factory; a no-op logger is used when omitted.</param>
    public static ISecretProtector Create(string dataDirectory, ILoggerFactory? loggerFactory = null)
    {
        loggerFactory ??= NullLoggerFactory.Instance;

        if (OperatingSystem.IsWindows())
        {
            return new DpapiSecretProtector(loggerFactory.CreateLogger<DpapiSecretProtector>());
        }

        return new AesKeyFileSecretProtector(dataDirectory, loggerFactory.CreateLogger<AesKeyFileSecretProtector>());
    }
}
