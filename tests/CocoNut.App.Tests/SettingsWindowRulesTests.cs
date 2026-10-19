using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using CocoNut.App.Services;
using CocoNut.App.Tests.Fakes;
using CocoNut.App.ViewModels;
using CocoNut.App.Views;
using CocoNut.Core.Abstractions;
using CocoNut.Core.Actions;
using CocoNut.Core.Logging;
using CocoNut.Core.Settings;
using CocoNut.Core.Updates;
using CocoNut.Core.Ups;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CocoNut.App.Tests;

/// <summary>Headless smoke tests of the Settings window with its Rules tab.</summary>
public class SettingsWindowRulesTests
{
    /// <summary>The index of the Rules tab: Connection, Calibration, Logging, Power, Rules, ...</summary>
    internal const int RulesTabIndex = 4;

    /// <summary>One rule per trigger kind and one action of every kind between them, plus an invalid one.</summary>
    internal static AppSettings SettingsWithSampleRules()
    {
        var settings = new AppSettings();
        settings.Actions.Rules.AddRange(
        [
            new ActionRule
            {
                Name = "Tell me when the power fails",
                Trigger = new RuleTrigger { Kind = TriggerKind.FlagsBecameActive, Flags = UpsStatus.OB },
                Actions = [RuleAction.Notify("{ups}: {status}"), RuleAction.Log("{rule} fired", LogLevel.Warning)],
            },
            new ActionRule
            {
                Name = "Both on battery and low",
                Trigger = new RuleTrigger { Kind = TriggerKind.FlagsActive, Flags = UpsStatus.OB | UpsStatus.LB },
                DelaySeconds = 10,
                Actions = [RuleAction.StopComputer(StopAction.Shutdown)],
            },
            new ActionRule
            {
                Name = "Mains is back",
                Trigger = new RuleTrigger { Kind = TriggerKind.FlagsBecameInactive, Flags = UpsStatus.OB },
                Actions = [RuleAction.Webhook("https://example.invalid/hook")],
            },
            new ActionRule
            {
                Name = "Battery below 30 percent",
                Trigger = new RuleTrigger { Kind = TriggerKind.ValueAtOrBelow, Metric = RuleMetric.BatteryCharge, Threshold = 30, OnBatteryOnly = true },
                Actions = [RuleAction.RunCommand("/usr/bin/logger", ["battery", "{charge}"], 20)],
            },
            new ActionRule
            {
                Name = "Load above 90 percent",
                Trigger = new RuleTrigger { Kind = TriggerKind.ValueAtOrAbove, Metric = RuleMetric.LoadPercent, Threshold = 90 },
                Actions = [RuleAction.Notify("Load is {load} %")],
            },
            new ActionRule
            {
                Name = "On battery for five minutes",
                Trigger = new RuleTrigger { Kind = TriggerKind.OnBatteryFor, DurationSeconds = 300 },
                Actions = [RuleAction.StopComputer(StopAction.Hibernate)],
            },
            new ActionRule
            {
                Name = "Lost the NUT server",
                Trigger = new RuleTrigger { Kind = TriggerKind.ConnectionLost, DurationSeconds = 120 },
                Actions = [RuleAction.Notify("{ups} is unreachable")],
            },
            new ActionRule
            {
                Name = string.Empty,
                Trigger = new RuleTrigger { Kind = TriggerKind.FlagsActive, Flags = UpsStatus.None },
                Actions = [],
            },
        ]);
        return settings;
    }

    internal static SettingsViewModel CreateViewModel(AppSettings settings)
    {
        var store = new InMemorySettingsStore { Settings = settings };
        var service = new SettingsService(store, store.Settings);
        return new SettingsViewModel(
            service,
            new FakeUpsMonitorEvents(),
            new FakePowerActions(),
            new FakeAutoStartService(),
            new FakeShellLauncher(),
            new FileLoggerProvider(Path.Combine(Path.GetTempPath(), "coconut-rules-window-logs-" + Guid.NewGuid().ToString("N"))),
            new UpdateChecker(new HttpClient()),
            new FakeWindowNavigator(),
            new FakeNotificationService(),
            new FakeHistoryStore(),
            NullLogger<SettingsViewModel>.Instance);
    }

    [AvaloniaFact]
    public void Settings_window_with_rules_of_every_kind_renders_every_tab_and_every_rule_editor()
    {
        var vm = CreateViewModel(SettingsWithSampleRules());
        var window = new SettingsWindow { DataContext = vm };
        window.Show();
        var tabs = window.GetVisualDescendants().OfType<TabControl>().First();
        Assert.Equal(8, tabs.ItemCount);

        for (var tab = 0; tab < tabs.ItemCount; tab++)
        {
            tabs.SelectedIndex = tab;
            Assert.NotNull(window.CaptureRenderedFrame());
        }

        // Select each rule in turn so every trigger and action editor is built and rendered once.
        tabs.SelectedIndex = RulesTabIndex;
        foreach (var rule in vm.Rules)
        {
            vm.SelectedRule = rule;
            Assert.NotNull(window.CaptureRenderedFrame());
        }

        Assert.Equal(8, vm.Rules.Count);
        Assert.True(vm.Rules[^1].HasProblems);
        Assert.True(vm.HasErrors);
    }

    [AvaloniaFact]
    public void Settings_window_without_rules_renders_the_rules_tab()
    {
        var vm = CreateViewModel(new AppSettings());
        var window = new SettingsWindow { DataContext = vm };
        window.Show();
        window.GetVisualDescendants().OfType<TabControl>().First().SelectedIndex = RulesTabIndex;

        Assert.NotNull(window.CaptureRenderedFrame());

        vm.AddRuleCommand.Execute(null);
        Assert.NotNull(window.CaptureRenderedFrame());
    }
}
