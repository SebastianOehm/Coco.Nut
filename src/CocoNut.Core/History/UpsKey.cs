using CocoNut.Core.Settings;

namespace CocoNut.Core.History;

/// <summary>
/// Identity of a UPS in the history database: the NUT server (host and port) plus the UPS name on it. Every sample
/// and event is keyed by it, so monitoring several servers/UPSes later needs no data migration. The host compares
/// case-insensitively (host names are), the UPS name case-sensitively (as NUT does).
/// </summary>
public sealed record UpsKey(string Host, int Port, string UpsName)
{
    /// <summary>Builds the key of the UPS that <paramref name="connection"/> points at.</summary>
    public static UpsKey From(ConnectionSettings connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        return new UpsKey(connection.Host, connection.Port, connection.UpsName);
    }

    /// <inheritdoc />
    public bool Equals(UpsKey? other) =>
        other is not null
        && Port == other.Port
        && string.Equals(Host, other.Host, StringComparison.OrdinalIgnoreCase)
        && string.Equals(UpsName, other.UpsName, StringComparison.Ordinal);

    /// <inheritdoc />
    public override int GetHashCode() =>
        HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(Host), Port, StringComparer.Ordinal.GetHashCode(UpsName));

    /// <inheritdoc />
    public override string ToString() => $"{UpsName}@{Host}:{Port}";
}
