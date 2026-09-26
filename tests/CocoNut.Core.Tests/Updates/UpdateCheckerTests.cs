using System.Net;
using CocoNut.Core.Settings;
using CocoNut.Core.Updates;

namespace CocoNut.Core.Tests.Updates;

public class UpdateCheckerTests
{
    private const string ReleasesJson = """
        [
          {
            "tag_name": "v9.9.9",
            "name": "Unreleased draft",
            "html_url": "https://example.invalid/releases/draft",
            "prerelease": false,
            "draft": true,
            "published_at": "2026-01-01T00:00:00Z",
            "body": "should never be picked"
          },
          {
            "tag_name": "v1.5.0-beta",
            "name": "1.5.0 beta",
            "html_url": "https://example.invalid/releases/v1.5.0-beta",
            "prerelease": true,
            "draft": false,
            "published_at": "2025-12-01T00:00:00Z",
            "body": "beta notes"
          },
          {
            "tag_name": "v1.4.0",
            "name": "1.4.0",
            "html_url": "https://example.invalid/releases/v1.4.0",
            "prerelease": false,
            "draft": false,
            "published_at": "2025-11-01T00:00:00Z",
            "body": "stable notes"
          },
          {
            "tag_name": "v1.0.0",
            "name": "1.0.0",
            "html_url": "https://example.invalid/releases/v1.0.0",
            "prerelease": false,
            "draft": false,
            "published_at": "2025-01-01T00:00:00Z",
            "body": "old stable notes"
          }
        ]
        """;

    private static UpdateChecker CreateChecker(FakeHttpMessageHandler handler, string owner = "nutdotnet", string repo = "Coco.Nut") =>
        new(new HttpClient(handler), owner, repo);

    [Fact]
    public async Task CheckAsync_StableChannel_SkipsDraftsAndPrereleases_PicksNewestStable()
    {
        var handler = FakeHttpMessageHandler.WithJson(HttpStatusCode.OK, ReleasesJson);
        var checker = CreateChecker(handler);

        var result = await checker.CheckAsync(new Version(1, 3, 0), UpdateChannel.Stable);

        Assert.True(result.IsUpdateAvailable);
        Assert.Equal(new Version(1, 4, 0), result.LatestVersion);
        Assert.Equal("https://example.invalid/releases/v1.4.0", result.ReleaseUrl);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task CheckAsync_PreReleaseChannel_AcceptsTheNewestNonDraftEvenIfPrerelease()
    {
        var handler = FakeHttpMessageHandler.WithJson(HttpStatusCode.OK, ReleasesJson);
        var checker = CreateChecker(handler);

        var result = await checker.CheckAsync(new Version(1, 3, 0), UpdateChannel.PreRelease);

        Assert.True(result.IsUpdateAvailable);
        Assert.Equal(new Version(1, 5, 0), result.LatestVersion);
        Assert.Equal("beta notes", result.ReleaseNotes);
    }

    [Fact]
    public async Task CheckAsync_WhenCurrentVersionIsUpToDate_ReportsNoUpdateAvailable()
    {
        var handler = FakeHttpMessageHandler.WithJson(HttpStatusCode.OK, ReleasesJson);
        var checker = CreateChecker(handler);

        var result = await checker.CheckAsync(new Version(1, 4, 0), UpdateChannel.Stable);

        Assert.False(result.IsUpdateAvailable);
        Assert.Equal(new Version(1, 4, 0), result.LatestVersion);
    }

    [Fact]
    public async Task CheckAsync_WhenCurrentVersionIsNewer_ReportsNoUpdateAvailable()
    {
        var handler = FakeHttpMessageHandler.WithJson(HttpStatusCode.OK, ReleasesJson);
        var checker = CreateChecker(handler);

        var result = await checker.CheckAsync(new Version(9, 0, 0), UpdateChannel.Stable);

        Assert.False(result.IsUpdateAvailable);
    }

    [Fact]
    public async Task CheckAsync_DraftOnlyMatch_ReportsNoUpdate()
    {
        const string draftOnlyJson = """
            [ { "tag_name": "v9.9.9", "prerelease": false, "draft": true } ]
            """;
        var handler = FakeHttpMessageHandler.WithJson(HttpStatusCode.OK, draftOnlyJson);
        var checker = CreateChecker(handler);

        var result = await checker.CheckAsync(new Version(1, 0, 0), UpdateChannel.PreRelease);

        Assert.False(result.IsUpdateAvailable);
        Assert.Null(result.LatestVersion);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task CheckAsync_EmptyReleaseList_ReportsNoUpdate()
    {
        var handler = FakeHttpMessageHandler.WithJson(HttpStatusCode.OK, "[]");
        var checker = CreateChecker(handler);

        var result = await checker.CheckAsync(new Version(1, 0, 0), UpdateChannel.Stable);

        Assert.False(result.IsUpdateAvailable);
    }

    [Fact]
    public async Task CheckAsync_HttpError_ReturnsFailureResultInsteadOfThrowing()
    {
        var handler = FakeHttpMessageHandler.WithJson(HttpStatusCode.InternalServerError, "");
        var checker = CreateChecker(handler);

        var result = await checker.CheckAsync(new Version(1, 0, 0), UpdateChannel.Stable);

        Assert.False(result.IsUpdateAvailable);
        Assert.NotNull(result.Error);
        Assert.Contains("500", result.Error);
    }

    [Fact]
    public async Task CheckAsync_NetworkFailure_ReturnsFailureResultInsteadOfThrowing()
    {
        var handler = FakeHttpMessageHandler.Throwing(new HttpRequestException("connection refused"));
        var checker = CreateChecker(handler);

        var result = await checker.CheckAsync(new Version(1, 0, 0), UpdateChannel.Stable);

        Assert.False(result.IsUpdateAvailable);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task CheckAsync_MalformedJson_ReturnsFailureResultInsteadOfThrowing()
    {
        var handler = FakeHttpMessageHandler.WithJson(HttpStatusCode.OK, "{ not a release array");
        var checker = CreateChecker(handler);

        var result = await checker.CheckAsync(new Version(1, 0, 0), UpdateChannel.Stable);

        Assert.False(result.IsUpdateAvailable);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task CheckAsync_WhenCancelled_PropagatesCancellation()
    {
        var handler = FakeHttpMessageHandler.WithJson(HttpStatusCode.OK, ReleasesJson);
        var checker = CreateChecker(handler);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => checker.CheckAsync(new Version(1, 0, 0), UpdateChannel.Stable, cts.Token));
    }

    [Fact]
    public async Task CheckAsync_SetsUserAgentHeader()
    {
        var handler = FakeHttpMessageHandler.WithJson(HttpStatusCode.OK, "[]");
        var checker = CreateChecker(handler);

        await checker.CheckAsync(new Version(1, 0, 0), UpdateChannel.Stable);

        Assert.NotNull(handler.LastRequest);
        Assert.NotEmpty(handler.LastRequest!.Headers.UserAgent);
    }

    [Fact]
    public async Task CheckAsync_RequestsTheConfiguredOwnerAndRepo()
    {
        var handler = FakeHttpMessageHandler.WithJson(HttpStatusCode.OK, "[]");
        var checker = CreateChecker(handler, owner: "some-owner", repo: "some-repo");

        await checker.CheckAsync(new Version(1, 0, 0), UpdateChannel.Stable);

        Assert.Contains("some-owner/some-repo", handler.LastRequest!.RequestUri!.ToString());
    }

    [Theory]
    [InlineData("v1.2.3", 1, 2, 3)]
    [InlineData("1.2.3", 1, 2, 3)]
    [InlineData("v2.3.9492", 2, 3, 9492)]
    [InlineData("V1.0.0", 1, 0, 0)]
    [InlineData("v2.3.0-beta", 2, 3, 0)]
    [InlineData("v2.3.0-beta.1", 2, 3, 0)]
    [InlineData("v2.3.0+build.5", 2, 3, 0)]
    public void TryParseVersionTag_ParsesSupportedFormats(string tag, int major, int minor, int build)
    {
        var version = UpdateChecker.TryParseVersionTag(tag);

        Assert.NotNull(version);
        Assert.Equal(major, version!.Major);
        Assert.Equal(minor, version.Minor);
        Assert.Equal(build, version.Build);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-version")]
    [InlineData("v")]
    public void TryParseVersionTag_WithUnparsableTag_ReturnsNull(string tag)
    {
        Assert.Null(UpdateChecker.TryParseVersionTag(tag));
    }

    [Fact]
    public void TryParseVersionTag_WithSingleComponent_TreatsItAsMajorMinor()
    {
        var version = UpdateChecker.TryParseVersionTag("v5");

        Assert.Equal(new Version(5, 0), version);
    }

    [Fact]
    public void IsCheckDue_WithNoLastCheck_IsTrue()
    {
        Assert.True(UpdateChecker.IsCheckDue(lastCheck: null, intervalDays: 7, now: DateTimeOffset.UtcNow));
    }

    [Fact]
    public void IsCheckDue_BeforeIntervalElapsed_IsFalse()
    {
        var now = new DateTimeOffset(2026, 1, 10, 0, 0, 0, TimeSpan.Zero);
        var lastCheck = new DateTimeOffset(2026, 1, 8, 0, 0, 0, TimeSpan.Zero);

        Assert.False(UpdateChecker.IsCheckDue(lastCheck, intervalDays: 7, now));
    }

    [Fact]
    public void IsCheckDue_AfterIntervalElapsed_IsTrue()
    {
        var now = new DateTimeOffset(2026, 1, 16, 0, 0, 0, TimeSpan.Zero);
        var lastCheck = new DateTimeOffset(2026, 1, 8, 0, 0, 0, TimeSpan.Zero);

        Assert.True(UpdateChecker.IsCheckDue(lastCheck, intervalDays: 7, now));
    }

    [Fact]
    public void IsCheckDue_WithNonPositiveInterval_IsAlwaysTrue()
    {
        Assert.True(UpdateChecker.IsCheckDue(DateTimeOffset.UtcNow, intervalDays: 0, DateTimeOffset.UtcNow));
    }
}
