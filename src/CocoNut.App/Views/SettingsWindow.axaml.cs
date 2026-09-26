using Avalonia.Controls;
using CocoNut.App.ViewModels;

namespace CocoNut.App.Views;

/// <summary>
/// Settings window (WinNUT's <c>Pref_Gui.vb</c>). The only code-behind behaviour is wiring
/// <see cref="SettingsViewModel.ConfirmAsync"/> to a real <see cref="ConfirmDialogWindow"/> owned by this window
/// (the view model cannot reference a window itself and stay unit testable) and closing this window when the
/// view model asks to; every other behaviour lives in <see cref="SettingsViewModel"/>.
/// </summary>
public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not SettingsViewModel viewModel)
        {
            return;
        }

        viewModel.ConfirmAsync = (title, message) => ConfirmDialogWindow.ShowAsync(this, title, message);
        viewModel.CloseRequested += (_, _) => Close();
    }
}
