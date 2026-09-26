using System.Net.Http.Json;
using System.Text.Json;
using CocoNut.Core.Settings;

namespace CocoNut.Core.Updates;

/// <summary>
/// Checks GitHub releases for a newer version, replacing WinNUT's <c>Updater.UpdateUtil</c> (which used Octokit).
/// Only checks and reports; it never downloads or installs anything.
/// </summary>
public sealed class UpdateChecker
{
    /// <summary>Default repository owner, used when the caller does not name one.</summary>
    public const string DefaultOwner = "nutdotnet";

    /// <summary>Default repository name, used when the caller does not name one.</summary>
    public const string DefaultRepo = "Coco.Nut";

    private readonly HttpClient _httpClient;
    private readonly string _owner;
    private readonly string _repo;

    /// <param name="httpClient">Client used for the request. Owned by the caller; not disposed here.</param>
    /// <param name="owner">GitHub repository owner.</param>
    /// <param name="repo">GitHub repository name.</param>
    public UpdateChecker(HttpClient httpClient, string owner = DefaultOwner, string repo = DefaultRepo)
    {
        _httpClient = httpClient;
        _owner = owner;
        _repo = repo;
    }

    /// <summary>
    /// Fetches the repository's releases and returns the newest one that matches <paramref name="channel"/>
    /// (draft releases are always skipped; a <see cref="UpdateChannel.Stable"/> check additionally skips
    /// pre-releases), in the order GitHub returns them (newest first). Network and parsing failures are
    /// returned as a <see cref="UpdateCheckResult.Error"/>, not thrown; a cancellation requested through
    /// <paramref name="cancellationToken"/> propagates as <see cref="OperationCanceledException"/>.
    /// </summary>
    public async Task<UpdateCheckResult> CheckAsync(
        Version currentVersion, UpdateChannel channel, CancellationToken cancellationToken = default)
    {
        List<GitHubRelease>? releases;
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get, $"https://api.github.com/repos/{_owner}/{_repo}/releases");
            request.Headers.UserAgent.ParseAdd($"{DefaultRepo}-UpdateChecker");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return UpdateCheckResult.Failed(
                    $"GitHub returned {(int)response.StatusCode} {response.ReasonPhrase} for {_owner}/{_repo}.");
            }

            releases = await response.Content
                .ReadFromJsonAsync(GitHubReleaseJsonContext.Default.ListGitHubRelease, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            return UpdateCheckResult.Failed($"Network error while checking for updates: {ex.Message}");
        }
        catch (JsonException ex)
        {
            return UpdateCheckResult.Failed($"Could not parse GitHub's response: {ex.Message}");
        }

        if (releases is null)
        {
            return UpdateCheckResult.NoUpdate();
        }

        foreach (var release in releases)
        {
            if (release.Draft || (channel == UpdateChannel.Stable && release.Prerelease))
            {
                continue;
            }

            var version = TryParseVersionTag(release.TagName);
            if (version is null)
            {
                continue;
            }

            var isNewer = version > currentVersion;
            return UpdateCheckResult.FromRelease(isNewer, version, release);
        }

        return UpdateCheckResult.NoUpdate();
    }

    /// <summary>
    /// Parses a release tag such as <c>v1.2.3</c>, <c>1.2.3</c> or <c>v2.3.9492</c> into a <see cref="Version"/>,
    /// tolerating a leading <c>v</c>/<c>V</c> and a trailing semver-style suffix (<c>-beta</c>, <c>+build.5</c>).
    /// Returns <see langword="null"/> for tags that still don't parse as a dotted numeric version.
    /// </summary>
    internal static Version? TryParseVersionTag(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return null;
        }

        var text = tag.Trim();
        if (text.Length > 0 && (text[0] == 'v' || text[0] == 'V'))
        {
            text = text[1..];
        }

        var suffixIndex = text.IndexOfAny(['-', '+']);
        if (suffixIndex >= 0)
        {
            text = text[..suffixIndex];
        }

        if (text.Length == 0)
        {
            return null;
        }

        // System.Version requires at least a major.minor pair.
        if (!text.Contains('.'))
        {
            text += ".0";
        }

        return Version.TryParse(text, out var version) ? version : null;
    }

    /// <summary>
    /// Whether an automatic check is due, given the last successful check time and the configured interval
    /// (<see cref="UpdateSettings.AutoCheckIntervalDays"/>). <see langword="true"/> when there is no
    /// <paramref name="lastCheck"/> yet.
    /// </summary>
    public static bool IsCheckDue(DateTimeOffset? lastCheck, int intervalDays, DateTimeOffset now)
    {
        if (lastCheck is null)
        {
            return true;
        }

        if (intervalDays <= 0)
        {
            return true;
        }

        return now - lastCheck.Value >= TimeSpan.FromDays(intervalDays);
    }
}
