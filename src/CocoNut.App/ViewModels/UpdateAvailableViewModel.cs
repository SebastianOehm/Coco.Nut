using System.Globalization;
using CocoNut.App.Services;
using CocoNut.Core.Updates;
using CocoNut.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CocoNut.App.ViewModels;

/// <summary>
/// View model of the update-available window (WinNUT's <c>Forms/UpdateAvailableForm.vb</c>): the new/current
/// version headline, release name/date, plain-text release notes, and a button that opens the release page in a
/// browser (Coco.Nut never self-installs, unlike WinNUT's old download-and-run flow).
/// </summary>
public sealed partial class UpdateAvailableViewModel : ObservableObject
{
    private readonly IShellLauncher _shellLauncher;
    private readonly string? _releaseUrl;

    public UpdateAvailableViewModel(UpdateCheckResult result, Version currentVersion, IShellLauncher shellLauncher)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(currentVersion);
        _shellLauncher = shellLauncher ?? throw new ArgumentNullException(nameof(shellLauncher));

        MessageText = string.Format(CultureInfo.CurrentCulture, Strings.Update_Message, result.LatestVersion, currentVersion);
        ReleaseName = result.ReleaseName ?? string.Empty;
        HasReleaseName = !string.IsNullOrWhiteSpace(ReleaseName);
        PublishedDateText = result.PublishedAt is { } published ? published.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) : string.Empty;
        HasPublishedDate = result.PublishedAt is not null;
        ReleaseNotes = result.ReleaseNotes ?? string.Empty;
        _releaseUrl = result.ReleaseUrl;
        CanOpenReleasePage = !string.IsNullOrWhiteSpace(_releaseUrl);
    }

    public string MessageText { get; }

    public string ReleaseName { get; }

    public bool HasReleaseName { get; }

    public string PublishedDateText { get; }

    public bool HasPublishedDate { get; }

    public string ReleaseNotes { get; }

    public bool CanOpenReleasePage { get; }

    [RelayCommand(CanExecute = nameof(CanOpenReleasePage))]
    private void OpenReleasePage()
    {
        if (_releaseUrl is not null)
        {
            _shellLauncher.OpenUrl(_releaseUrl);
        }
    }
}
