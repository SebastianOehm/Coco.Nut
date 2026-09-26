using System.Globalization;
using System.Reflection;
using CocoNut.App.Services;
using CocoNut.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CocoNut.App.ViewModels;

/// <summary>
/// View model of the About window (WinNUT's <c>About_Gui.vb</c>): app name/version/description, the copyright
/// line and a short acknowledgements list read from the entry assembly's attributes, and the project page link.
/// </summary>
public sealed partial class AboutViewModel : ObservableObject
{
    /// <summary>Project page opened by <see cref="OpenProjectPage"/> and shown as the link text.</summary>
    public const string ProjectPageUrl = "https://github.com/SebastianOehm/Coco.Nut";

    public AboutViewModel(IShellLauncher shellLauncher, Assembly? entryAssembly = null)
    {
        _shellLauncher = shellLauncher ?? throw new ArgumentNullException(nameof(shellLauncher));

        var assembly = entryAssembly ?? Assembly.GetEntryAssembly();
        var informationalVersion = assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var version = string.IsNullOrWhiteSpace(informationalVersion)
            ? assembly?.GetName().Version?.ToString() ?? "0.1.0"
            : informationalVersion;

        VersionText = string.Format(CultureInfo.CurrentCulture, Strings.About_Version, version);
        CopyrightText = assembly?.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? string.Empty;
    }

    private readonly IShellLauncher _shellLauncher;

    /// <summary>Localized "Version {0}" text, {0} from the assembly's informational (or, as a fallback, file) version.</summary>
    public string VersionText { get; }

    /// <summary>The <c>AssemblyCopyrightAttribute</c> text (from <c>Directory.Build.props</c>'s <c>&lt;Copyright&gt;</c>).</summary>
    public string CopyrightText { get; }

    /// <summary>Short, plain list of third-party projects Coco.Nut is built on or ported from (proper nouns, not translated).</summary>
    public string AcknowledgementsText { get; } =
        "Avalonia UI (MIT)" + Environment.NewLine +
        "CommunityToolkit.Mvvm (MIT)" + Environment.NewLine +
        "Microsoft.Extensions.* (MIT)" + Environment.NewLine +
        "AGauge by Code-Artist (MIT) - inspiration for the gauge control";

    /// <summary>Where Coco.Nut comes from: WinNUT-Client / nutdotnet by gbakeman (localized).</summary>
    public string OriginText { get; } = Strings.About_Origin;

    /// <summary>How AI was used to build Coco.Nut (localized).</summary>
    public string AiUsageText { get; } = Strings.About_AiUsage;

    [RelayCommand]
    private void OpenProjectPage() => _shellLauncher.OpenUrl(ProjectPageUrl);
}
