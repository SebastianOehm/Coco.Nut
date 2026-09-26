using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace CocoNut.Core.Settings;

/// <summary>
/// Windows secret protector using DPAPI (<see cref="ProtectedData"/>) with <see cref="DataProtectionScope.CurrentUser"/>
/// and no extra entropy - the same scheme WinNUT's <c>SerializedProtectedString</c> used, so values line up with
/// what a user importing an old <c>user.config</c> would expect.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiSecretProtector : ISecretProtector
{
    private readonly ILogger<DpapiSecretProtector> _logger;

    public DpapiSecretProtector(ILogger<DpapiSecretProtector>? logger = null)
    {
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<DpapiSecretProtector>.Instance;
    }

    /// <inheritdoc />
    public string Protect(string plainText)
    {
        var bytes = Encoding.Unicode.GetBytes(plainText);
        var protectedBytes = ProtectedData.Protect(bytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(protectedBytes);
    }

    /// <inheritdoc />
    public string? Unprotect(string protectedText)
    {
        try
        {
            var protectedBytes = Convert.FromBase64String(protectedText);
            var bytes = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
            return Encoding.Unicode.GetString(bytes);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            _logger.LogWarning(ex, "Failed to unprotect a secret using DPAPI; treating it as absent.");
            return null;
        }
    }
}
