using System.Reflection;

namespace CocoNut.Core.Updates;

/// <summary>
/// A release version with SemVer-style ordering: a numeric core (<see cref="System.Version"/>) plus an optional
/// pre-release label such as <c>pre.13</c> or <c>beta</c>. <c>+build</c> metadata is ignored. Needed because
/// every <c>preMain</c> build shares the core <c>0.1.0</c> and differs only in its <c>-pre.N</c> suffix.
/// </summary>
public sealed class ReleaseVersion : IComparable<ReleaseVersion>, IEquatable<ReleaseVersion>
{
    /// <summary>The version used when nothing about the running build can be determined.</summary>
    public static ReleaseVersion Fallback { get; } = new(new Version(0, 1, 0));

    /// <param name="core">Numeric part. Missing build/revision components count as 0.</param>
    /// <param name="preRelease">Dot-separated pre-release label without the leading <c>-</c>, or <see langword="null"/> for a release.</param>
    public ReleaseVersion(Version core, string? preRelease = null)
    {
        ArgumentNullException.ThrowIfNull(core);
        Core = new Version(core.Major, core.Minor, Math.Max(core.Build, 0), Math.Max(core.Revision, 0));
        PreRelease = string.IsNullOrEmpty(preRelease) ? null : preRelease;
    }

    /// <summary>Numeric part of the version, e.g. <c>0.1.0</c>.</summary>
    public Version Core { get; }

    /// <summary>Pre-release label (e.g. <c>pre.13</c>), or <see langword="null"/> for a release.</summary>
    public string? PreRelease { get; }

    /// <summary>
    /// The running application's version: the assembly's informational version (which carries the <c>-pre.N</c>
    /// suffix and a <c>+commit</c> that is dropped), else its assembly version, else <see cref="Fallback"/>.
    /// </summary>
    public static ReleaseVersion FromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (informational is not null && TryParse(informational) is { } parsed)
        {
            return parsed;
        }

        return assembly.GetName().Version is { } version ? new ReleaseVersion(version) : Fallback;
    }

    /// <summary>
    /// Parses text such as <c>v0.1.0-pre.12</c>, <c>1.2.3</c>, <c>V2.3.9492</c> or <c>1.0.0-beta+sha.abc</c>,
    /// tolerating a leading <c>v</c>/<c>V</c> and a missing minor component (<c>v5</c> is 5.0.0). Returns
    /// <see langword="null"/> when it is not a dotted numeric version with an optional valid pre-release label.
    /// </summary>
    public static ReleaseVersion? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var span = text.Trim().AsSpan();
        if (span.Length > 0 && (span[0] == 'v' || span[0] == 'V'))
        {
            span = span[1..];
        }

        var plus = span.IndexOf('+');
        if (plus >= 0)
        {
            span = span[..plus];
        }

        string? preRelease = null;
        var dash = span.IndexOf('-');
        if (dash >= 0)
        {
            preRelease = span[(dash + 1)..].ToString();
            span = span[..dash];
            if (!IsValidLabel(preRelease))
            {
                return null;
            }
        }

        var core = span.ToString();
        if (core.Length == 0)
        {
            return null;
        }

        // System.Version requires at least a major.minor pair.
        if (!core.Contains('.'))
        {
            core += ".0";
        }

        return Version.TryParse(core, out var version) ? new ReleaseVersion(version, preRelease) : null;
    }

    private static bool IsValidLabel(string label)
    {
        foreach (var identifier in label.Split('.'))
        {
            if (identifier.Length == 0 || !identifier.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc />
    public int CompareTo(ReleaseVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        var byCore = Core.CompareTo(other.Core);
        if (byCore != 0)
        {
            return byCore;
        }

        // A release outranks any pre-release of the same core.
        if (PreRelease is null || other.PreRelease is null)
        {
            return (PreRelease is null ? 1 : 0) - (other.PreRelease is null ? 1 : 0);
        }

        return ComparePreRelease(PreRelease, other.PreRelease);
    }

    /// <summary>SemVer 2.0 precedence: numeric identifiers numerically, numeric below alphanumeric, shorter prefix first.</summary>
    private static int ComparePreRelease(string left, string right)
    {
        var a = left.Split('.');
        var b = right.Split('.');
        for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            var aNumeric = a[i].All(char.IsAsciiDigit);
            var bNumeric = b[i].All(char.IsAsciiDigit);
            int result;
            if (aNumeric && bNumeric)
            {
                // Compare by length after trimming leading zeros, then lexically: exact for arbitrarily long numbers.
                var x = a[i].TrimStart('0');
                var y = b[i].TrimStart('0');
                result = x.Length != y.Length ? x.Length.CompareTo(y.Length) : string.CompareOrdinal(x, y);
            }
            else if (aNumeric != bNumeric)
            {
                result = aNumeric ? -1 : 1;
            }
            else
            {
                result = string.CompareOrdinal(a[i], b[i]);
            }

            if (result != 0)
            {
                return Math.Sign(result);
            }
        }

        return a.Length.CompareTo(b.Length);
    }

    /// <inheritdoc />
    public bool Equals(ReleaseVersion? other) => other is not null && CompareTo(other) == 0;

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as ReleaseVersion);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Core, PreRelease is null);

    /// <summary>E.g. <c>0.1.0</c>, <c>0.1.0-pre.13</c>; a fourth component is shown only when non-zero.</summary>
    public override string ToString()
    {
        var core = Core.Revision > 0 ? Core.ToString(4) : Core.ToString(3);
        return PreRelease is null ? core : $"{core}-{PreRelease}";
    }

    public static bool operator >(ReleaseVersion? left, ReleaseVersion? right) => Compare(left, right) > 0;

    public static bool operator <(ReleaseVersion? left, ReleaseVersion? right) => Compare(left, right) < 0;

    public static bool operator >=(ReleaseVersion? left, ReleaseVersion? right) => Compare(left, right) >= 0;

    public static bool operator <=(ReleaseVersion? left, ReleaseVersion? right) => Compare(left, right) <= 0;

    public static bool operator ==(ReleaseVersion? left, ReleaseVersion? right) => Compare(left, right) == 0;

    public static bool operator !=(ReleaseVersion? left, ReleaseVersion? right) => Compare(left, right) != 0;

    private static int Compare(ReleaseVersion? left, ReleaseVersion? right) =>
        left is null ? (right is null ? 0 : -1) : left.CompareTo(right);
}
