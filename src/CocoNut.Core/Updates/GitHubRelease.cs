using System.Text.Json.Serialization;

namespace CocoNut.Core.Updates;

/// <summary>The fields used from a GitHub REST API "list releases" response entry.</summary>
public sealed class GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; set; }

    [JsonPropertyName("prerelease")]
    public bool Prerelease { get; set; }

    [JsonPropertyName("draft")]
    public bool Draft { get; set; }

    [JsonPropertyName("published_at")]
    public DateTimeOffset? PublishedAt { get; set; }

    [JsonPropertyName("body")]
    public string? Body { get; set; }
}

/// <summary>Source-generated (de)serialization context for the GitHub releases API response.</summary>
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(List<GitHubRelease>))]
internal partial class GitHubReleaseJsonContext : JsonSerializerContext;
