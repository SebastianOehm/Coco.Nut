using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace CocoNut.Core.Settings;

/// <summary>
/// Secret protector for non-Windows platforms (no DPAPI available): a random 256-bit key is generated once and
/// stored in <c>secret.key</c> inside the data directory, restricted to the owning user (Unix mode 0600 where the
/// platform supports it). Secrets are sealed with AES-256-GCM using a fresh random nonce per call.
/// </summary>
/// <remarks>
/// Wire format of a protected value (before base64): <c>nonce (12 bytes) || tag (16 bytes) || ciphertext</c>.
/// </remarks>
public sealed class AesKeyFileSecretProtector : ISecretProtector
{
    /// <summary>File name of the key material inside the data directory.</summary>
    public const string KeyFileName = "secret.key";

    private const int KeySizeBytes = 32; // 256-bit
    private const int NonceSizeBytes = 12; // 96-bit, the recommended AES-GCM nonce size.
    private const int TagSizeBytes = 16; // 128-bit authentication tag.

    private readonly byte[] _key;
    private readonly ILogger<AesKeyFileSecretProtector> _logger;

    /// <summary>Full path of the key file this instance loaded or created.</summary>
    public string KeyFilePath { get; }

    /// <param name="dataDirectory">Directory the key file lives in (typically <see cref="AppPaths.DataDirectory"/>).</param>
    /// <param name="logger">Optional logger; a no-op logger is used when omitted.</param>
    public AesKeyFileSecretProtector(string dataDirectory, ILogger<AesKeyFileSecretProtector>? logger = null)
    {
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<AesKeyFileSecretProtector>.Instance;
        KeyFilePath = Path.Combine(dataDirectory, KeyFileName);
        _key = LoadOrCreateKey(KeyFilePath, _logger);
    }

    private static byte[] LoadOrCreateKey(string keyFilePath, ILogger logger)
    {
        if (File.Exists(keyFilePath))
        {
            var existing = File.ReadAllBytes(keyFilePath);
            if (existing.Length == KeySizeBytes)
            {
                return existing;
            }

            logger.LogWarning(
                "Secret key file at {KeyFilePath} had an unexpected length; regenerating it. Previously " +
                "protected secrets will no longer decrypt.", keyFilePath);
        }

        var key = RandomNumberGenerator.GetBytes(KeySizeBytes);
        Directory.CreateDirectory(Path.GetDirectoryName(keyFilePath)!);

        // Create the file with owner-only permissions from the start, so the key is never readable by
        // others, not even briefly. UnixCreateMode is not supported on Windows (where DPAPI is used instead).
        var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        using (var stream = new FileStream(keyFilePath, options))
        {
            stream.Write(key);
        }

        // FileMode.Create keeps the permissions of an existing (wrong-length) file; enforce them.
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(keyFilePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        return key;
    }

    /// <inheritdoc />
    public string Protect(string plainText)
    {
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
        var cipherText = new byte[plainBytes.Length];
        var tag = new byte[TagSizeBytes];

        using var aesGcm = new AesGcm(_key, TagSizeBytes);
        aesGcm.Encrypt(nonce, plainBytes, cipherText, tag);

        var combined = new byte[NonceSizeBytes + TagSizeBytes + cipherText.Length];
        nonce.CopyTo(combined, 0);
        tag.CopyTo(combined, NonceSizeBytes);
        cipherText.CopyTo(combined, NonceSizeBytes + TagSizeBytes);

        return Convert.ToBase64String(combined);
    }

    /// <inheritdoc />
    public string? Unprotect(string protectedText)
    {
        try
        {
            var combined = Convert.FromBase64String(protectedText);
            if (combined.Length < NonceSizeBytes + TagSizeBytes)
            {
                _logger.LogWarning("Stored secret is too short to be valid; treating it as absent.");
                return null;
            }

            var nonce = combined.AsSpan(0, NonceSizeBytes);
            var tag = combined.AsSpan(NonceSizeBytes, TagSizeBytes);
            var cipherText = combined.AsSpan(NonceSizeBytes + TagSizeBytes);
            var plainBytes = new byte[cipherText.Length];

            using var aesGcm = new AesGcm(_key, TagSizeBytes);
            aesGcm.Decrypt(nonce, cipherText, tag, plainBytes);

            return Encoding.UTF8.GetString(plainBytes);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or ArgumentException)
        {
            _logger.LogWarning(ex, "Failed to unprotect a secret using the AES key file; treating it as absent.");
            return null;
        }
    }
}
