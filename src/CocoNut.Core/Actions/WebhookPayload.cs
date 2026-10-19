using System.Text.Json.Serialization;

namespace CocoNut.Core.Actions;

/// <summary>
/// The JSON body POSTed by <see cref="ActionKind.Webhook"/> (camelCase property names). Values the UPS does not
/// report, or all of them when there is no reading, are <see langword="null"/>.
/// </summary>
/// <param name="Rule">Name of the firing rule.</param>
/// <param name="Ups">Name of the UPS.</param>
/// <param name="Status">Raw <c>ups.status</c>.</param>
/// <param name="Timestamp">When the rule fired (ISO 8601).</param>
/// <param name="BatteryCharge">Percent.</param>
/// <param name="BatteryRuntime">Seconds.</param>
/// <param name="Load">Percent.</param>
/// <param name="InputVoltage">Volts.</param>
/// <param name="OutputVoltage">Volts.</param>
/// <param name="OutputPower">Watts.</param>
/// <param name="BatteryVoltage">Volts.</param>
public sealed record WebhookPayload(
    string Rule,
    string Ups,
    string? Status,
    DateTimeOffset Timestamp,
    double? BatteryCharge,
    double? BatteryRuntime,
    double? Load,
    double? InputVoltage,
    double? OutputVoltage,
    double? OutputPower,
    double? BatteryVoltage)
{
    /// <summary>Builds the payload for <paramref name="context"/>.</summary>
    public static WebhookPayload From(ActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var reading = context.Reading;
        return new WebhookPayload(
            context.Rule.Name,
            context.UpsName,
            reading?.RawStatus,
            context.Timestamp,
            reading?.BatteryCharge,
            reading?.BatteryRuntime?.TotalSeconds,
            reading?.LoadPercent,
            reading?.InputVoltage,
            reading?.OutputVoltage,
            reading?.OutputPowerWatts,
            reading?.BatteryVoltage);
    }
}

/// <summary>Source-generated serialization (trimming/AOT friendly) of <see cref="WebhookPayload"/>.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(WebhookPayload))]
internal partial class WebhookJsonContext : JsonSerializerContext;
