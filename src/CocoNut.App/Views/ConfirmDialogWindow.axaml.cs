using Avalonia.Controls;

namespace CocoNut.App.Views;

/// <summary>
/// A small generic Yes/No confirmation dialog (e.g. Settings &gt; Logging's "Delete log files" button), used where
/// WinNUT acted without confirming. Plain window plumbing only: <see cref="ShowAsync"/> sets the already-localized
/// title/message text and resolves once a button is clicked.
/// </summary>
public partial class ConfirmDialogWindow : Window
{
    public ConfirmDialogWindow() => InitializeComponent();

    /// <summary>Shows the dialog modally over <paramref name="owner"/> and returns whether "Yes" was clicked.</summary>
    /// <param name="owner">Owning window, used to center the dialog and block input to it while shown.</param>
    /// <param name="title">Already-localized window title.</param>
    /// <param name="message">Already-localized confirmation message.</param>
    public static async Task<bool> ShowAsync(Window owner, string title, string message)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var window = new ConfirmDialogWindow { Title = title };
        window.MessageTextBlock.Text = message;

        var result = false;
        window.YesButton.Click += (_, _) =>
        {
            result = true;
            window.Close();
        };
        window.NoButton.Click += (_, _) => window.Close();

        await window.ShowDialog(owner).ConfigureAwait(true);
        return result;
    }
}
