using System.Globalization;
using CocoNut.App.Tests.Fakes;
using CocoNut.App.ViewModels;
using CocoNut.Core.Monitoring;
using CocoNut.Core.Nut;
using CocoNut.Localization;
using Microsoft.Extensions.Logging.Abstractions;

namespace CocoNut.App.Tests.ViewModels;

public sealed class UpsVariablesViewModelTests
{
    public UpsVariablesViewModelTests() => Strings.Culture = CultureInfo.GetCultureInfo("en");

    private static (UpsVariablesViewModel ViewModel, FakeUpsMonitorEvents Monitor, FakeNotificationService Notifications) Build(
        bool connected = true, IReadOnlyList<NutVariable>? variables = null)
    {
        var monitor = new FakeUpsMonitorEvents { Variables = variables ?? SampleVariables };
        if (connected)
        {
            monitor.RaiseStateChanged(MonitorState.Disconnected, MonitorState.Connected);
        }

        var notifications = new FakeNotificationService();
        var vm = new UpsVariablesViewModel(monitor, notifications, new ImmediateUiDispatcher(), NullLogger<UpsVariablesViewModel>.Instance);
        return (vm, monitor, notifications);
    }

    private static readonly IReadOnlyList<NutVariable> SampleVariables =
    [
        new NutVariable("battery.charge", "80", "Battery charge (percent)"),
        new NutVariable("battery.voltage", "13.2", "Battery voltage (V)"),
        new NutVariable("ups.status", "OL", "UPS status"),
        new NutVariable("ups.mfr", "Acme", "UPS manufacturer"),
    ];

    [Fact]
    public void Not_connected_shows_the_not_connected_placeholder()
    {
        var (vm, _, _) = Build(connected: false);

        Assert.False(vm.IsConnected);
        Assert.False(vm.ShowContent);
    }

    [Fact]
    public async Task Connecting_loads_variables_and_builds_the_grouped_tree()
    {
        var (vm, _, _) = Build();
        await WaitUntil(() => vm.RootNodes.Count > 0);

        Assert.True(vm.ShowContent);
        Assert.Null(vm.ErrorMessage);

        var batteryGroup = Assert.Single(vm.RootNodes, n => n.Segment == "battery");
        Assert.False(batteryGroup.IsLeaf);
        Assert.Equal(2, batteryGroup.Children.Count);
        Assert.Contains(batteryGroup.Children, c => c.Segment == "charge" && c.IsLeaf && c.Value == "80");

        var upsGroup = Assert.Single(vm.RootNodes, n => n.Segment == "ups");
        Assert.Equal(2, upsGroup.Children.Count);
    }

    [Fact]
    public async Task Selecting_a_leaf_populates_the_detail_panel()
    {
        var (vm, _, _) = Build();
        await WaitUntil(() => vm.RootNodes.Count > 0);

        var batteryGroup = vm.RootNodes.Single(n => n.Segment == "battery");
        var chargeLeaf = batteryGroup.Children.Single(c => c.Segment == "charge");

        vm.SelectedNode = chargeLeaf;

        Assert.Equal("battery.charge", vm.DetailName);
        Assert.Equal("80", vm.DetailValue);
        Assert.Equal("Battery charge (percent)", vm.DetailDescription);
    }

    [Fact]
    public async Task Filtering_by_name_keeps_only_matching_leaves_and_their_groups()
    {
        var (vm, _, _) = Build();
        await WaitUntil(() => vm.RootNodes.Count > 0);

        vm.FilterText = "charge";

        var batteryGroup = Assert.Single(vm.RootNodes);
        Assert.Equal("battery", batteryGroup.Segment);
        var leaf = Assert.Single(batteryGroup.Children);
        Assert.Equal("charge", leaf.Segment);
    }

    [Fact]
    public async Task Filtering_by_value_or_description_also_matches()
    {
        var (vm, _, _) = Build();
        await WaitUntil(() => vm.RootNodes.Count > 0);

        vm.FilterText = "Acme";
        Assert.Single(vm.RootNodes); // "ups" group containing only "mfr"
        Assert.Single(vm.RootNodes[0].Children);

        vm.FilterText = "manufacturer";
        Assert.Single(vm.RootNodes[0].Children);
    }

    [Fact]
    public async Task Clearing_the_filter_restores_the_full_tree()
    {
        var (vm, _, _) = Build();
        await WaitUntil(() => vm.RootNodes.Count > 0);
        var fullRootCount = vm.RootNodes.Count;

        vm.FilterText = "charge";
        Assert.NotEqual(fullRootCount, vm.RootNodes.Count);

        vm.FilterText = string.Empty;
        Assert.Equal(fullRootCount, vm.RootNodes.Count);
    }

    [Fact]
    public async Task Copy_to_clipboard_uses_the_name_equals_value_format_for_every_variable_ignoring_the_filter()
    {
        var (vm, _, notifications) = Build();
        await WaitUntil(() => vm.RootNodes.Count > 0);
        vm.FilterText = "charge"; // Copy must still include everything, not just the filtered subset.

        string? copied = null;
        vm.SetClipboardTextAsync = text =>
        {
            copied = text;
            return Task.CompletedTask;
        };

        await vm.CopyToClipboardCommand.ExecuteAsync(null);

        Assert.NotNull(copied);
        Assert.Contains("battery.charge = 80", copied);
        Assert.Contains("ups.mfr = Acme", copied);
        Assert.Contains(notifications.Notifications, n => n.Message == Strings.Vars_Clipboard_Success);
    }

    [Fact]
    public async Task Copy_to_clipboard_notifies_on_failure()
    {
        var (vm, _, notifications) = Build();
        await WaitUntil(() => vm.RootNodes.Count > 0);

        vm.SetClipboardTextAsync = _ => throw new InvalidOperationException("clipboard unavailable");

        await vm.CopyToClipboardCommand.ExecuteAsync(null);

        Assert.Contains(notifications.Notifications, n => n.Title == Strings.Vars_Clipboard_Error_Title);
    }

    [Fact]
    public async Task Save_to_file_reports_success_with_the_chosen_path()
    {
        var (vm, _, notifications) = Build();
        await WaitUntil(() => vm.RootNodes.Count > 0);

        vm.SaveTextToFileAsync = _ => Task.FromResult<string?>("/tmp/ups-variables.txt");

        await vm.SaveToFileCommand.ExecuteAsync(null);

        Assert.Contains(notifications.Notifications, n => n.Message.Contains("/tmp/ups-variables.txt", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Save_to_file_does_nothing_when_the_user_cancels()
    {
        var (vm, _, notifications) = Build();
        await WaitUntil(() => vm.RootNodes.Count > 0);

        vm.SaveTextToFileAsync = _ => Task.FromResult<string?>(null);

        await vm.SaveToFileCommand.ExecuteAsync(null);

        Assert.Empty(notifications.Notifications);
    }

    [Fact]
    public async Task A_load_failure_shows_the_localized_error_message_and_disables_actions()
    {
        var monitor = new FakeUpsMonitorEvents { ThrowOnGetVariables = new InvalidOperationException("boom") };
        monitor.RaiseStateChanged(MonitorState.Disconnected, MonitorState.Connected);
        var vm = new UpsVariablesViewModel(monitor, new FakeNotificationService(), new ImmediateUiDispatcher(), NullLogger<UpsVariablesViewModel>.Instance);

        await WaitUntil(() => vm.ErrorMessage is not null);

        Assert.False(vm.ShowContent);
        Assert.False(vm.CopyToClipboardCommand.CanExecute(null));
        Assert.False(vm.SaveToFileCommand.CanExecute(null));
        Assert.Contains("boom", vm.ErrorMessage);
    }

    [Fact]
    public async Task Disconnecting_clears_the_tree_and_actions_are_disabled_again()
    {
        var (vm, monitor, _) = Build();
        await WaitUntil(() => vm.RootNodes.Count > 0);

        monitor.RaiseStateChanged(MonitorState.Connected, MonitorState.Disconnected);

        Assert.False(vm.IsConnected);
        Assert.Empty(vm.RootNodes);
        Assert.False(vm.CopyToClipboardCommand.CanExecute(null));
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition());
    }
}
