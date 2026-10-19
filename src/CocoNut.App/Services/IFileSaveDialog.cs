using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace CocoNut.App.Services;

/// <summary>A file the user picked in a save dialog, opened for writing. Disposing it closes <see cref="Stream"/>.</summary>
public sealed record SaveTarget(Stream Stream, string Path) : IAsyncDisposable
{
    /// <inheritdoc />
    public ValueTask DisposeAsync() => Stream.DisposeAsync();
}

/// <summary>Asks the user where to save a CSV file; behind an interface so view models stay testable without a window.</summary>
public interface IFileSaveDialog
{
    /// <summary>
    /// Shows a save-file dialog and opens the chosen file for writing (truncating it). Returns <see langword="null"/>
    /// if the user cancelled.
    /// </summary>
    /// <param name="title">Already-localized dialog title.</param>
    /// <param name="suggestedFileName">File name offered to the user, including the <c>.csv</c> extension.</param>
    /// <param name="fileTypeName">Already-localized name of the CSV file type shown in the dialog's filter.</param>
    Task<SaveTarget?> PickCsvAsync(string title, string suggestedFileName, string fileTypeName);
}

/// <summary>
/// Default <see cref="IFileSaveDialog"/>, backed by the <see cref="IStorageProvider"/> of the window that is active
/// when the dialog is requested (or the main window): view models are created before their window exists, so they
/// cannot be handed a <see cref="TopLevel"/> up front.
/// </summary>
public sealed class StorageProviderFileSaveDialog : IFileSaveDialog
{
    /// <inheritdoc />
    public async Task<SaveTarget?> PickCsvAsync(string title, string suggestedFileName, string fileTypeName)
    {
        var owner = FindOwner() ?? throw new InvalidOperationException("There is no window to show the save dialog on.");

        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedFileName,
            DefaultExtension = "csv",
            FileTypeChoices = [new FilePickerFileType(fileTypeName) { Patterns = ["*.csv"], MimeTypes = ["text/csv"] }],
        }).ConfigureAwait(true);

        if (file is null)
        {
            return null;
        }

        var stream = await file.OpenWriteAsync().ConfigureAwait(true);
        if (stream.CanSeek)
        {
            stream.SetLength(0);
        }

        return new SaveTarget(stream, file.TryGetLocalPath() ?? file.Name);
    }

    private static Window? FindOwner()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return null;
        }

        return desktop.Windows.FirstOrDefault(window => window.IsActive) ?? desktop.MainWindow ?? (desktop.Windows.Count > 0 ? desktop.Windows[0] : null);
    }
}
