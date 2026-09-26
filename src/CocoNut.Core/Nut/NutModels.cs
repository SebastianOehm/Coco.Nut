namespace CocoNut.Core.Nut;

/// <summary>An entry of <c>LIST UPS</c>.</summary>
public sealed record NutUpsEntry(string Name, string Description);

/// <summary>A UPS variable, optionally with its description from <c>GET DESC</c>.</summary>
public sealed record NutVariable(string Name, string Value, string? Description = null);
