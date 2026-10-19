using System.Reflection;
using CocoNut.Core.Updates;

namespace CocoNut.Core.Tests.Updates;

public class ReleaseVersionTests
{
    [Theory]
    [InlineData("0.1.0-pre.12", "0.1.0-pre.13")]
    [InlineData("0.1.0-pre.13", "0.1.0")]
    [InlineData("0.1.0-pre.9", "0.1.0-pre.10")]
    [InlineData("0.1.0", "0.1.1-pre.1")]
    [InlineData("1.0.0-alpha", "1.0.0-alpha.1")]
    [InlineData("1.0.0-alpha.1", "1.0.0-alpha.beta")]
    [InlineData("1.0.0-alpha.beta", "1.0.0-beta")]
    [InlineData("1.0.0-beta.2", "1.0.0-beta.11")]
    [InlineData("1.0.0-rc.1", "1.0.0")]
    [InlineData("1.2", "1.2.1")]
    public void Ordering_FollowsSemVer(string lower, string higher)
    {
        var a = ReleaseVersion.TryParse(lower)!;
        var b = ReleaseVersion.TryParse(higher)!;

        Assert.True(a < b);
        Assert.True(b > a);
        Assert.True(a.CompareTo(b) < 0);
        Assert.False(b <= a);
    }

    [Fact]
    public void Equality_IgnoresBuildMetadataAndLeadingV()
    {
        var a = ReleaseVersion.TryParse("v0.1.0-pre.12+abcdef")!;
        var b = ReleaseVersion.TryParse("0.1.0-pre.12")!;

        Assert.True(a == b);
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.Equal("0.1.0-pre.12", a.ToString());
    }

    [Fact]
    public void ToString_ShowsFourthComponentOnlyWhenNonZero()
    {
        Assert.Equal("1.2.3", new ReleaseVersion(new Version(1, 2, 3, 0)).ToString());
        Assert.Equal("1.2.3.4", new ReleaseVersion(new Version(1, 2, 3, 4)).ToString());
        Assert.Equal("1.2.0", new ReleaseVersion(new Version(1, 2)).ToString());
    }

    [Fact]
    public void FromAssembly_UsesInformationalVersionWithoutBuildMetadata()
    {
        var assembly = new FakeAssembly("0.1.0-pre.12+0123456789abcdef", new Version(0, 1, 0, 0));

        Assert.Equal("0.1.0-pre.12", ReleaseVersion.FromAssembly(assembly).ToString());
    }

    [Fact]
    public void FromAssembly_WithoutInformationalVersion_FallsBackToAssemblyVersion()
    {
        var assembly = new FakeAssembly(null, new Version(2, 4, 6, 0));

        Assert.Equal("2.4.6", ReleaseVersion.FromAssembly(assembly).ToString());
    }

    [Fact]
    public void FromAssembly_WithUnparsableInformationalVersion_FallsBackToAssemblyVersion()
    {
        var assembly = new FakeAssembly("garbage", new Version(2, 4, 6, 0));

        Assert.Equal("2.4.6", ReleaseVersion.FromAssembly(assembly).ToString());
    }

    [Fact]
    public void FromAssembly_ForTheTestAssembly_ReturnsSomeVersion()
    {
        Assert.NotNull(ReleaseVersion.FromAssembly(typeof(ReleaseVersionTests).Assembly));
    }

    private sealed class FakeAssembly(string? informational, Version version) : Assembly
    {
        public override AssemblyName GetName() => new("Fake") { Version = version };

        public override IList<CustomAttributeData> GetCustomAttributesData() => [];

        public override object[] GetCustomAttributes(Type attributeType, bool inherit) =>
            informational is not null && attributeType == typeof(AssemblyInformationalVersionAttribute)
                ? new Attribute[] { new AssemblyInformationalVersionAttribute(informational) }
                : Array.Empty<Attribute>();

        public override bool IsDefined(Type attributeType, bool inherit) => false;
    }
}
