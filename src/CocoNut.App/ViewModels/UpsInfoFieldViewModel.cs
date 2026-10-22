using CocoNut.Core.Settings;
using CocoNut.Core.Ups;
using CocoNut.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CocoNut.App.ViewModels;

/// <summary>
/// One line of the main window's UPS info block that the user can fill in by hand (manufacturer or model): the text to
/// show, whether it is a manual entry, and the inline editor. <see cref="MainWindowViewModel"/> feeds it through
/// <see cref="Update"/> and <see cref="Reset"/> and persists what the editor saves.
/// </summary>
public sealed partial class UpsInfoFieldViewModel : ObservableObject
{
    /// <summary>Maximum length of a manual entry, applied to the editor's text box.</summary>
    public const int MaxLength = 100;

    private readonly Action<UpsInfoField, string?> _saveOverride;
    private string? _overrideValue;

    /// <param name="field">Which value this line shows.</param>
    /// <param name="saveOverride">Persists the text the user saved (empty clears the manual entry).</param>
    public UpsInfoFieldViewModel(UpsInfoField field, Action<UpsInfoField, string?> saveOverride)
    {
        Field = field;
        _saveOverride = saveOverride ?? throw new ArgumentNullException(nameof(saveOverride));
    }

    /// <summary>Which value this line shows.</summary>
    public UpsInfoField Field { get; }

    /// <summary>The reported value, the manual entry, or <see cref="Strings.Common_Unavailable"/>.</summary>
    [ObservableProperty]
    private string _text = Strings.Common_Unavailable;

    /// <summary>Whether <see cref="Text"/> is a manual entry because the UPS does not report the value.</summary>
    [ObservableProperty]
    private bool _isOverridden;

    /// <summary>Whether the user may enter a value: connected, product info available and the UPS does not report it.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BeginEditCommand))]
    private bool _canOverride;

    /// <summary>Whether the inline editor is open instead of <see cref="Text"/>.</summary>
    [ObservableProperty]
    private bool _isEditing;

    /// <summary>The editor's text box content.</summary>
    [ObservableProperty]
    private string _editText = "";

    /// <summary>
    /// Shows what the UPS reported or the manual entry, and whether an entry is possible. An open editor stays open
    /// (the main window updates this every polling interval) unless entering a value is no longer possible.
    /// </summary>
    /// <param name="reported">The value from the UPS, or <see langword="null"/>/<c>Unknown</c> when it reports none.</param>
    /// <param name="overrideValue">The stored manual entry for this UPS, if any.</param>
    /// <param name="canOverride">Whether an entry is possible at all right now (connected, info available).</param>
    public void Update(string? reported, string? overrideValue, bool canOverride)
    {
        var (text, isOverridden) = UpsInfoOverrideResolver.Resolve(reported, overrideValue);
        _overrideValue = overrideValue;
        Text = text ?? Strings.Common_Unavailable;
        IsOverridden = isOverridden;
        CanOverride = canOverride && !UpsInfo.IsReported(reported);
    }

    /// <summary>The "no data" state of a disconnected main window: unavailable, no indicator, editor closed.</summary>
    public void Reset()
    {
        _overrideValue = null;
        Text = Strings.Common_Unavailable;
        IsOverridden = false;
        CanOverride = false;
    }

    partial void OnCanOverrideChanged(bool value)
    {
        if (!value)
        {
            IsEditing = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanOverride))]
    private void BeginEdit()
    {
        EditText = _overrideValue ?? "";
        IsEditing = true;
    }

    [RelayCommand]
    private void SaveEdit()
    {
        if (!IsEditing)
        {
            return;
        }

        IsEditing = false;
        _saveOverride(Field, EditText);
    }

    [RelayCommand]
    private void CancelEdit() => IsEditing = false;
}
