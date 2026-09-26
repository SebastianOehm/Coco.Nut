namespace CocoNut.Core.Updates;

/// <summary>Outcome of <see cref="UpdateChecker.CheckAsync"/>. Network/parse failures are reported here rather
/// than thrown, so a failed check never crashes a background "check for updates on start" call.</summary>
public sealed record UpdateCheckResult
{
    /// <summary><see langword="true"/> when <see cref="LatestVersion"/> is newer than the version that was checked.</summary>
    public bool IsUpdateAvailable { get; init; }

    /// <summary>Version parsed from the matching release's tag, or <see langword="null"/> when none was found.</summary>
    public Version? LatestVersion { get; init; }

    /// <summary>The release's display name (GitHub <c>name</c> field).</summary>
    public string? ReleaseName { get; init; }

    /// <summary>Web page for the release, to open in a browser (no self-install).</summary>
    public string? ReleaseUrl { get; init; }

    /// <summary>When the release was published.</summary>
    public DateTimeOffset? PublishedAt { get; init; }

    /// <summary>The release's notes (GitHub <c>body</c> field, Markdown).</summary>
    public string? ReleaseNotes { get; init; }

    /// <summary>Set when the check could not complete (network error, unexpected HTTP status, malformed response).</summary>
    public string? Error { get; init; }

    /// <summary>No matching release was found (or the check failed): never an update, per <see cref="IsUpdateAvailable"/>.</summary>
    public static UpdateCheckResult NoUpdate() => new();

    /// <summary>A release was found; <see cref="IsUpdateAvailable"/> reflects whether it is newer.</summary>
    public static UpdateCheckResult FromRelease(bool isNewer, Version version, GitHubRelease release) => new()
    {
        IsUpdateAvailable = isNewer,
        LatestVersion = version,
        ReleaseName = release.Name,
        ReleaseUrl = release.HtmlUrl,
        PublishedAt = release.PublishedAt,
        ReleaseNotes = release.Body,
    };

    /// <summary>The check did not complete; <paramref name="error"/> is a short, non-sensitive description.</summary>
    public static UpdateCheckResult Failed(string error) => new() { Error = error };
}
