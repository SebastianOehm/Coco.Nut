namespace CocoNut.Core.Ups;

/// <summary>Static product information of a UPS, read once after login.</summary>
public sealed record UpsInfo(string Manufacturer, string Model, string Serial, string Firmware);
