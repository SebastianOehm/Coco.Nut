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

    private static UpdateChecker CreateChecker(FakeHttpMessageHandler handler, string owner = "SebastianOehm", string repo = "Coco.Nut") =>
        new(new HttpClient(handler), owner, repo);

    [Fact]
    public async Task CheckAsync_StableChannel_SkipsDraftsAndPrereleases_PicksNewestStable()
    {
        var handler = FakeHttpMessageHandler.WithJson(HttpStatusCode.OK, ReleasesJson);
        var checker = CreateChecker(handler);

        var result = await checker.CheckAsync(ReleaseVersion.TryParse("1.3.0")!, UpdateChannel.Stable);

        Assert.True(result.IsUpdateAvailable);
        Assert.Equal(ReleaseVersion.TryParse("1.4.0")!, result.LatestVersion);
        Assert.Equal("https://example.invalid/releases/v1.4.0", result.ReleaseUrl);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task CheckAsync_PreReleaseChannel_AcceptsTheNewestNonDraftEvenIfPrerelease()
    {
        var handler = FakeHttpMessageHandler.WithJson(HttpStatusCode.OK, ReleasesJson);
        var checker = CreateChecker(handler);

        var result = await checker.CheckAsync(ReleaseVersion.TryParse("1.3.0")!, UpdateChannel.PreRelease);

        Assert.True(result.IsUpdateAvailable);
        Assert.Equal(ReleaseVersion.TryParse("1.5.0-beta")!, result.LatestVersion);
        Assert.Equal("beta notes", result.ReleaseNotes);
    }

    [Fact]
    public async Task CheckAsync_WhenCurrentVersionIsUpToDate_ReportsNoUpdateAvailable()
    {
        var handler = FakeHttpMessageHandler.WithJson(HttpStatusCode.OK, ReleasesJson);
        var checker = CreateChecker(handler);

        var result = await checker.CheckAsync(ReleaseVersion.TryParse("1.4.0")!, UpdateChannel.Stable);

        Assert.False(result.IsUpdateAvailable);
        Assert.Equal(ReleaseVersion.TryParse("1.4.0")!, result.LatestVersion);
    }

    [Fact]
    public async Task CheckAsync_WhenCurrentVersionIsNewer_ReportsNoUpdateAvailable()
    {
        var handler = FakeHttpMessageHandler.WithJson(HttpStatusCode.OK, ReleasesJson);
        var checker = CreateChecker(handler);

        var result = await checker.CheckAsync(ReleaseVersion.TryParse("9.0.0")!, UpdateChannel.Stable);

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

        var result = await checker.CheckAsync(ReleaseVersion.TryParse("1.0.0")!, UpdateChannel.PreRelease);

        Assert.False(result.IsUpdateAvailable);
        Assert.Null(result.LatestVersion);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task CheckAsync_EmptyReleaseList_ReportsNoUpdate()
    {
        var handler = FakeHttpMessageHandler.WithJson(HttpStatusCode.OK, "[]");
        var checker = CreateChecker(handler);

        var result = await checker.CheckAsync(ReleaseVersion.TryParse("1.0.0")!, UpdateChannel.Stable);

        Assert.False(result.IsUpdateAvailable);
    }

    [Fact]
    public async Task CheckAsync_HttpError_ReturnsFailureResultInsteadOfThrowing()
    {
        var handler = FakeHttpMessageHandler.WithJson(HttpStatusCode.InternalServerError, "");
        var checker = CreateChecker(handler);

        var result = await checker.CheckAsync(ReleaseVersion.TryParse("1.0.0")!, UpdateChannel.Stable);

        Assert.False(result.IsUpdateAvailable);
        Assert.NotNull(result.Error);
        Assert.Contains("500", result.Error);
    }

    [Fact]
    public async Task CheckAsync_NetworkFailure_ReturnsFailureResultInsteadOfThrowing()
    {
        var handler = FakeHttpMessageHandler.Throwing(new HttpRequestException("connection refused"));
        var checker = CreateChecker(handler);

        var result = await checker.CheckAsync(ReleaseVersion.TryParse("1.0.0")!, UpdateChannel.Stable);

        Assert.False(result.IsUpdateAvailable);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task CheckAsync_MalformedJson_ReturnsFailureResultInsteadOfThrowing()
    {
        var handler = FakeHttpMessageHandler.WithJson(HttpStatusCode.OK, "{ not a release array");
        var checker = CreateChecker(handler);

        var result = await checker.CheckAsync(ReleaseVersion.TryParse("1.0.0")!, UpdateChannel.Stable);

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
            () => checker.CheckAsync(ReleaseVersion.TryParse("1.0.0")!, UpdateChannel.Stable, cts.Token));
    }

    [Fact]
    public async Task CheckAsync_SetsUserAgentHeader()
    {
        var handler = FakeHttpMessageHandler.WithJson(HttpStatusCode.OK, "[]");
        var checker = CreateChecker(handler);

        await checker.CheckAsync(ReleaseVersion.TryParse("1.0.0")!, UpdateChannel.Stable);

        Assert.NotNull(handler.LastRequest);
        Assert.NotEmpty(handler.LastRequest!.Headers.UserAgent);
    }

    [Fact]
    public async Task CheckAsync_RequestsTheConfiguredOwnerAndRepo()
    {
        var handler = FakeHttpMessageHandler.WithJson(HttpStatusCode.OK, "[]");
        var checker = CreateChecker(handler, owner: "some-owner", repo: "some-repo");

        await checker.CheckAsync(ReleaseVersion.TryParse("1.0.0")!, UpdateChannel.Stable);

        Assert.Contains("some-owner/some-repo", handler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task CheckAsync_WithDefaultChecker_RequestsTheSebastianOehmReleasesEndpoint()
    {
        var handler = FakeHttpMessageHandler.WithJson(HttpStatusCode.OK, ReleasesJson);
        var checker = new UpdateChecker(new HttpClient(handler));

        await checker.CheckAsync(ReleaseVersion.TryParse("1.0.0")!, UpdateChannel.Stable);

        Assert.Equal("https://api.github.com/repos/SebastianOehm/Coco.Nut/releases", handler.LastRequest!.RequestUri!.ToString());
    }

    private const string Pre13Json = """
        { "tag_name": "v0.1.0-pre.13", "name": "pre 13", "html_url": "https://example.invalid/pre13", "prerelease": true, "draft": false }
        """;

    private const string Pre12Json = """
        { "tag_name": "v0.1.0-pre.12", "name": "pre 12", "html_url": "https://example.invalid/pre12", "prerelease": true, "draft": false }
        """;

    [Fact]
    public async Task CheckAsync_WithNewerPreMainBuild_ReportsUpdateWithFullVersionText()
    {
        var checker = CreateChecker(FakeHttpMessageHandler.WithJson(HttpStatusCode.OK, $"[{Pre13Json},{Pre12Json}]"));

        var result = await checker.CheckAsync(ReleaseVersion.TryParse("0.1.0-pre.12+abc123")!, UpdateChannel.PreRelease);

        Assert.True(result.IsUpdateAvailable);
        Assert.Equal("0.1.0-pre.13", result.LatestVersion!.ToString());
    }

    [Fact]
    public async Task CheckAsync_WithCurrentPreMainBuild_ReportsNoUpdate()
    {
        var checker = CreateChecker(FakeHttpMessageHandler.WithJson(HttpStatusCode.OK, $"[{Pre12Json}]"));

        var result = await checker.CheckAsync(ReleaseVersion.TryParse("0.1.0-pre.12")!, UpdateChannel.PreRelease);

        Assert.False(result.IsUpdateAvailable);
        Assert.Equal("0.1.0-pre.12", result.LatestVersion!.ToString());
    }

    [Theory]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("v2.3.9492", "2.3.9492")]
    [InlineData("V1.0.0", "1.0.0")]
    [InlineData("v0.1.0-pre.12", "0.1.0-pre.12")]
    [InlineData("v1.5.0-beta", "1.5.0-beta")]
    [InlineData("v2.3.0-beta.1", "2.3.0-beta.1")]
    [InlineData("v2.3.0+build.5", "2.3.0")]
    [InlineData("v5", "5.0.0")]
    public void TryParseVersionTag_ParsesSupportedFormats(string tag, string expected)
    {
        var version = UpdateChecker.TryParseVersionTag(tag);

        Assert.NotNull(version);
        Assert.Equal(expected, version!.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-version")]
    [InlineData("v")]
    [InlineData("v1.0.0-")]
    public void TryParseVersionTag_WithUnparsableTag_ReturnsNull(string tag)
    {
        Assert.Null(UpdateChecker.TryParseVersionTag(tag));
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
