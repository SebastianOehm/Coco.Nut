using System.Collections.ObjectModel;

namespace CocoNut.App.ViewModels;

/// <summary>
/// One node of the UPS variables window's tree (WinNUT's <c>List_Var_Gui</c> flat <c>TreeNode</c> hierarchy built
/// from each variable's dotted name, e.g. <c>battery.charge</c> becomes a "battery" group containing a "charge"
/// leaf). A leaf (<see cref="IsLeaf"/>) carries the variable's full name/value/description; a group node only has
/// <see cref="Segment"/> and <see cref="Children"/>. Rebuilt from scratch on every reload/filter change, so it
/// needs no change notification of its own.
/// </summary>
public sealed class UpsVariableNode(string segment, string? fullName = null, string? value = null, string? description = null)
{
    /// <summary>This node's own path segment (e.g. "battery" or "charge"), shown as the tree item's text.</summary>
    public string Segment { get; } = segment;

    /// <summary>The variable's full dotted name (e.g. "battery.charge"); <see langword="null"/> for a group node.</summary>
    public string? FullName { get; } = fullName;

    /// <summary>The variable's current value; <see langword="null"/> for a group node.</summary>
    public string? Value { get; } = value;

    /// <summary>The variable's <c>GET DESC</c> description, if any; <see langword="null"/> for a group node.</summary>
    public string? Description { get; } = description;

    /// <summary><see langword="true"/> for an actual variable, as opposed to a dotted-prefix group node.</summary>
    public bool IsLeaf => FullName is not null;

    /// <summary>Child nodes, in the order they were added. Empty for a leaf.</summary>
    public ObservableCollection<UpsVariableNode> Children { get; } = [];
}
