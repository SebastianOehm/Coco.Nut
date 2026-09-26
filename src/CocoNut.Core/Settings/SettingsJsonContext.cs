using System.Text.Json.Serialization;

namespace CocoNut.Core.Settings;

/// <summary>
/// Source-generated serialization context for <see cref="SettingsFileModel"/>. Enums (<see cref="UpdateChannel"/>,
/// <see cref="Abstractions.StopAction"/>, <see cref="Microsoft.Extensions.Logging.LogLevel"/>) are written as
/// strings so the file stays readable and stable across enum-member reordering.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    UseStringEnumConverter = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SettingsFileModel))]
[JsonSerializable(typeof(AppSettings))]
internal partial class SettingsJsonContext : JsonSerializerContext;
