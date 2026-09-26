using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CocoNut.App.ViewModels;
using CocoNut.Localization;

namespace CocoNut.App.Views;

/// <summary>
/// UPS variables window (WinNUT's <c>List_Var_Gui.vb</c>). Code-behind wires
/// <see cref="UpsVariablesViewModel.SetClipboardTextAsync"/>/<see cref="UpsVariablesViewModel.SaveTextToFileAsync"/>
/// to this window's <see cref="TopLevel.Clipboard"/>/<see cref="TopLevel.StorageProvider"/> (the view model cannot
/// reference either directly and stay unit testable) and disposes the view model when closed; every other
/// behaviour lives in <see cref="UpsVariablesViewModel"/>.
/// </summary>
public partial class UpsVariablesWindow : Window
{
    public UpsVariablesWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Closed += OnClosed;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not UpsVariablesViewModel viewModel)
        {
            return;
        }

        viewModel.SetClipboardTextAsync = async text =>
        {
            if (Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(text).ConfigureAwait(true);
            }
        };

        viewModel.SaveTextToFileAsync = async text =>
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = Strings.Vars_SaveFile_Title,
                SuggestedFileName = "ups-variables",
                DefaultExtension = "txt",
                FileTypeChoices = [new FilePickerFileType("Text") { Patterns = ["*.txt"] }],
            }).ConfigureAwait(true);

            if (file is null)
            {
                return null;
            }

            await using var stream = await file.OpenWriteAsync().ConfigureAwait(true);
            await using var writer = new StreamWriter(stream, System.Text.Encoding.UTF8);
            await writer.WriteAsync(text).ConfigureAwait(true);
            return file.TryGetLocalPath() ?? file.Name;
        };
    }

    private void OnClosed(object? sender, EventArgs e) => (DataContext as UpsVariablesViewModel)?.Dispose();

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
