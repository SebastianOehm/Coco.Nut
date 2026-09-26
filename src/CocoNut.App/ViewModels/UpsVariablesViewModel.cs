using System.Collections.ObjectModel;
using System.Globalization;
using CocoNut.App.Services;
using CocoNut.Core.Abstractions;
using CocoNut.Core.Monitoring;
using CocoNut.Core.Nut;
using CocoNut.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace CocoNut.App.ViewModels;

/// <summary>
/// View model of the UPS variables window (WinNUT's <c>List_Var_Gui.vb</c>): loads every UPS variable
/// asynchronously, groups it into a tree by its dotted name, supports filtering by name/value/description, and
/// copies/saves the full (unfiltered) list as plain text. Disabled - with an explanatory message - whenever the
/// UPS monitor is not connected.
/// </summary>
public sealed partial class UpsVariablesViewModel : ObservableObject, IDisposable
{
    private readonly IUpsMonitorEvents _monitor;
    private readonly INotificationService _notifications;
    private readonly IUiDispatcher _dispatcher;
    private readonly ILogger<UpsVariablesViewModel> _logger;

    private IReadOnlyList<NutVariable> _allVariables = [];

    public UpsVariablesViewModel(
        IUpsMonitorEvents monitor, INotificationService notifications, IUiDispatcher dispatcher, ILogger<UpsVariablesViewModel> logger)
    {
        _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _monitor.StateChanged += OnMonitorStateChanged;
        IsConnected = _monitor.State == MonitorState.Connected;

        if (IsConnected)
        {
            _ = ReloadAsync();
        }
    }

    /// <summary>Writes <c>string</c> to the system clipboard; wired by the view (needs a <c>TopLevel</c>).</summary>
    public Func<string, Task> SetClipboardTextAsync { get; set; } = _ => Task.CompletedTask;

    /// <summary>
    /// Shows a save-file dialog for the given content and writes it as UTF-8 text; wired by the view (needs a
    /// <c>TopLevel</c>'s <c>StorageProvider</c>). Returns the chosen path, or <see langword="null"/> if the user
    /// cancelled.
    /// </summary>
    public Func<string, Task<string?>> SaveTextToFileAsync { get; set; } = _ => Task.FromResult<string?>(null);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CopyToClipboardCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveToFileCommand))]
    [NotifyPropertyChangedFor(nameof(ShowContent))]
    private bool _isConnected;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CopyToClipboardCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveToFileCommand))]
    [NotifyPropertyChangedFor(nameof(ShowContent))]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CopyToClipboardCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveToFileCommand))]
    [NotifyPropertyChangedFor(nameof(ShowContent))]
    private string? _errorMessage;

    /// <summary>Whether the tree/detail panel should be shown, as opposed to the "not connected"/error placeholder.</summary>
    public bool ShowContent => IsConnected && !IsLoading && ErrorMessage is null;

    [ObservableProperty]
    private string _filterText = string.Empty;

    partial void OnFilterTextChanged(string value) => RebuildTree();

    /// <summary>Root nodes of the variable tree, rebuilt on every reload or filter change.</summary>
    public ObservableCollection<UpsVariableNode> RootNodes { get; } = [];

    [ObservableProperty]
    private UpsVariableNode? _selectedNode;

    partial void OnSelectedNodeChanged(UpsVariableNode? value)
    {
        DetailName = value?.FullName ?? string.Empty;
        DetailValue = value?.Value ?? string.Empty;
        DetailDescription = value?.Description ?? string.Empty;
    }

    [ObservableProperty]
    private string _detailName = string.Empty;

    [ObservableProperty]
    private string _detailValue = string.Empty;

    [ObservableProperty]
    private string _detailDescription = string.Empty;

    private bool CanInteract() => IsConnected && !IsLoading && ErrorMessage is null && _allVariables.Count > 0;

    private void OnMonitorStateChanged(object? sender, MonitorStateChangedEventArgs e) => _dispatcher.Post(() =>
    {
        var connected = e.NewState == MonitorState.Connected;
        IsConnected = connected;

        if (connected)
        {
            if (_allVariables.Count == 0)
            {
                _ = ReloadAsync();
            }
        }
        else
        {
            _allVariables = [];
            RootNodes.Clear();
            SelectedNode = null;
            CopyToClipboardCommand.NotifyCanExecuteChanged();
            SaveToFileCommand.NotifyCanExecuteChanged();
        }
    });

    [RelayCommand]
    private async Task ReloadAsync()
    {
        if (!IsConnected)
        {
            return;
        }

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            _allVariables = await _monitor.GetAllVariablesAsync(includeDescriptions: true).ConfigureAwait(true);
            RebuildTree();
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or TimeoutException or NutException)
        {
            _logger.LogWarning(ex, "Failed to load UPS variables.");
            _allVariables = [];
            RootNodes.Clear();
            ErrorMessage = string.Format(CultureInfo.CurrentCulture, Strings.Vars_LoadError_Text, ex.Message);
        }
        finally
        {
            IsLoading = false;
            CopyToClipboardCommand.NotifyCanExecuteChanged();
            SaveToFileCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanInteract))]
    private async Task CopyToClipboardAsync()
    {
        try
        {
            await SetClipboardTextAsync(BuildExportText()).ConfigureAwait(true);
            _notifications.Notify(Strings.Notify_Title_Info, Strings.Vars_Clipboard_Success, NotificationKind.Info);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to copy UPS variables to the clipboard.");
            _notifications.Notify(
                Strings.Vars_Clipboard_Error_Title,
                string.Format(CultureInfo.CurrentCulture, Strings.Vars_Clipboard_Error, ex.Message),
                NotificationKind.Error);
        }
    }

    [RelayCommand(CanExecute = nameof(CanInteract))]
    private async Task SaveToFileAsync()
    {
        try
        {
            var path = await SaveTextToFileAsync(BuildExportText()).ConfigureAwait(true);
            if (path is null)
            {
                return; // The user cancelled the save dialog.
            }

            _notifications.Notify(
                Strings.Notify_Title_Info,
                string.Format(CultureInfo.CurrentCulture, Strings.Vars_SaveFile_Success, path),
                NotificationKind.Info);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Failed to save UPS variables to a file.");
            _notifications.Notify(
                Strings.Notify_Title_Error,
                string.Format(CultureInfo.CurrentCulture, Strings.Vars_SaveFile_Error, ex.Message),
                NotificationKind.Error);
        }
    }

    /// <summary>Serializes every loaded variable (regardless of the current filter) as <c>name = value</c> lines.</summary>
    private string BuildExportText() =>
        string.Join(Environment.NewLine, _allVariables.Select(v => $"{v.Name} = {v.Value}"));

    private static bool MatchesFilter(NutVariable variable, string filter) =>
        filter.Length == 0
        || variable.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || variable.Value.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || (variable.Description?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false);

    private void RebuildTree()
    {
        RootNodes.Clear();

        var filter = FilterText.Trim();
        var matched = _allVariables.Where(v => MatchesFilter(v, filter)).OrderBy(v => v.Name, StringComparer.Ordinal);

        var byPath = new Dictionary<string, UpsVariableNode>(StringComparer.Ordinal);

        foreach (var variable in matched)
        {
            var segments = variable.Name.Split('.');
            var path = string.Empty;
            UpsVariableNode? parent = null;

            for (var i = 0; i < segments.Length; i++)
            {
                path = i == 0 ? segments[0] : $"{path}.{segments[i]}";
                var isLeaf = i == segments.Length - 1;

                if (!byPath.TryGetValue(path, out var node))
                {
                    node = isLeaf
                        ? new UpsVariableNode(segments[i], variable.Name, variable.Value, variable.Description)
                        : new UpsVariableNode(segments[i]);
                    byPath[path] = node;

                    if (parent is null)
                    {
                        RootNodes.Add(node);
                    }
                    else
                    {
                        parent.Children.Add(node);
                    }
                }

                parent = node;
            }
        }
    }

    /// <inheritdoc />
    public void Dispose() => _monitor.StateChanged -= OnMonitorStateChanged;
}
