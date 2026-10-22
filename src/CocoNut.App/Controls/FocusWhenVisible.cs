using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace CocoNut.App.Controls;

/// <summary>
/// Attached behavior: when a control with <c>FocusWhenVisible.IsEnabled="True"</c> has its <c>IsVisible</c> switched on, the
/// text box it is or contains takes the keyboard focus and selects its text. For an inline editor that is shown on demand.
/// </summary>
public static class FocusWhenVisible
{
    /// <summary>Turns the behavior on for a control.</summary>
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("IsEnabled", typeof(FocusWhenVisible));

    static FocusWhenVisible() => IsEnabledProperty.Changed.AddClassHandler<Control>(OnIsEnabledChanged);

    public static bool GetIsEnabled(Control control) => control.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(Control control, bool value) => control.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        control.PropertyChanged -= OnControlPropertyChanged;
        if (e.NewValue is true)
        {
            control.PropertyChanged += OnControlPropertyChanged;
        }
    }

    private static void OnControlPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != Visual.IsVisibleProperty || e.NewValue is not true || sender is not Control control)
        {
            return;
        }

        // After the layout pass that makes the control visible; focusing a control that is not yet laid out does nothing.
        Dispatcher.UIThread.Post(
            () =>
            {
                var textBox = control as TextBox ?? control.GetVisualDescendants().OfType<TextBox>().FirstOrDefault();
                if (textBox is not null && textBox.Focus())
                {
                    textBox.SelectAll();
                }
            },
            DispatcherPriority.Input);
    }
}
