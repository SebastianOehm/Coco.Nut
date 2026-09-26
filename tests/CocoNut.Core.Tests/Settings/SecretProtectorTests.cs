using CocoNut.Core.Settings;
using CocoNut.Core.Tests.TestSupport;

namespace CocoNut.Core.Tests.Settings;

public class AesKeyFileSecretProtectorTests
{
    [Fact]
    public void Protect_ThenUnprotect_RoundTripsThePlainText()
    {
        using var temp = new TempDirectory();
        var protector = new AesKeyFileSecretProtector(temp.Path);

        var protectedText = protector.Protect("s3cr3t-p@ssw0rd");

        Assert.Equal("s3cr3t-p@ssw0rd", protector.Unprotect(protectedText));
    }

    [Fact]
    public void Protect_DoesNotContainThePlainText()
    {
        using var temp = new TempDirectory();
        var protector = new AesKeyFileSecretProtector(temp.Path);

        var protectedText = protector.Protect("s3cr3t-p@ssw0rd");

        Assert.DoesNotContain("s3cr3t-p@ssw0rd", protectedText, StringComparison.Ordinal);
    }

    [Fact]
    public void Unprotect_WithTamperedCiphertext_ReturnsNull()
    {
        using var temp = new TempDirectory();
        var protector = new AesKeyFileSecretProtector(temp.Path);
        var protectedText = protector.Protect("s3cr3t-p@ssw0rd");

        var tamperedBytes = Convert.FromBase64String(protectedText);
        tamperedBytes[^1] ^= 0xFF; // Flip bits in the last byte of the ciphertext.
        var tampered = Convert.ToBase64String(tamperedBytes);

        Assert.Null(protector.Unprotect(tampered));
    }

    [Fact]
    public void Unprotect_WithGarbageInput_ReturnsNullAndDoesNotThrow()
    {
        using var temp = new TempDirectory();
        var protector = new AesKeyFileSecretProtector(temp.Path);

        Assert.Null(protector.Unprotect("not-valid-base64!!"));
        Assert.Null(protector.Unprotect(""));
        Assert.Null(protector.Unprotect(Convert.ToBase64String([1, 2, 3])));
    }

    [Fact]
    public void SecondInstance_OverSameDirectory_ReusesTheSameKey()
    {
        using var temp = new TempDirectory();
        var first = new AesKeyFileSecretProtector(temp.Path);
        var protectedText = first.Protect("shared-key-value");

        var second = new AesKeyFileSecretProtector(temp.Path);

        Assert.Equal("shared-key-value", second.Unprotect(protectedText));
    }

    [Fact]
    public void Constructor_CreatesKeyFile()
    {
        using var temp = new TempDirectory();
        var protector = new AesKeyFileSecretProtector(temp.Path);

        Assert.True(File.Exists(protector.KeyFilePath));
        Assert.Equal(Path.Combine(temp.Path, AesKeyFileSecretProtector.KeyFileName), protector.KeyFilePath);
    }

    [Fact]
    public void Constructor_OnUnix_RestrictsKeyFileToOwner()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // File.GetUnixFileMode throws on Windows; the guarantee only applies on Unix-like platforms.
        }

        using var temp = new TempDirectory();
        var protector = new AesKeyFileSecretProtector(temp.Path);

        var mode = File.GetUnixFileMode(protector.KeyFilePath);

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, mode);
    }
}

public class DpapiSecretProtectorTests
{
    [Fact]
    public void Protect_ThenUnprotect_RoundTripsThePlainText_OnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return; // DPAPI is only available on Windows; nothing to assert elsewhere.
        }

        var protector = new DpapiSecretProtector();
        var protectedText = protector.Protect("s3cr3t-p@ssw0rd");

        Assert.Equal("s3cr3t-p@ssw0rd", protector.Unprotect(protectedText));
    }
}

public class SecretProtectorFactoryTests
{
    [Fact]
    public void Create_ReturnsThePlatformAppropriateProtector()
    {
        using var temp = new TempDirectory();
        var protector = SecretProtectorFactory.Create(temp.Path);

        if (OperatingSystem.IsWindows())
        {
            Assert.IsType<DpapiSecretProtector>(protector);
        }
        else
        {
            Assert.IsType<AesKeyFileSecretProtector>(protector);
        }
    }
}
