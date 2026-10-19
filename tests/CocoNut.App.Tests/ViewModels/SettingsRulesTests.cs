using System.Globalization;
using CocoNut.App.Services;
using CocoNut.App.Tests.Fakes;
using CocoNut.App.ViewModels;
using CocoNut.Core.Abstractions;
using CocoNut.Core.Actions;
using CocoNut.Core.Logging;
using CocoNut.Core.Settings;
using CocoNut.Core.Shutdown;
using CocoNut.Core.Updates;
using CocoNut.Core.Ups;
using CocoNut.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CocoNut.App.Tests.ViewModels;

/// <summary>Tests of the Rules tab of <see cref="SettingsViewModel"/>: editing the user rules of the working copy.</summary>
public sealed class SettingsRulesTests : IDisposable
{
    private static readonly UpdateChecker SharedUpdateChecker = new(new HttpClient());

    private readonly string _logDirectory = Path.Combine(Path.GetTempPath(), "coconut-rules-tests-" + Guid.NewGuid().ToString("N"));

    public SettingsRulesTests() => Strings.Culture = CultureInfo.GetCultureInfo("en");

    public void Dispose()
    {
        if (Directory.Exists(_logDirectory))
        {
            Directory.Delete(_logDirectory, recursive: true);
        }
    }

    private sealed record Fixture(SettingsViewModel ViewModel, InMemorySettingsStore Store, SettingsService Settings, FakePowerActions PowerActions);

    private Fixture Build(AppSettings? settings = null, Action<FakePowerActions>? configurePower = null)
    {
        var store = new InMemorySettingsStore { Settings = settings ?? new AppSettings() };
        var service = new SettingsService(store, store.Settings);
        var power = new FakePowerActions();
        configurePower?.Invoke(power);
        var vm = new SettingsViewModel(
            service,
            new FakeUpsMonitorEvents(),
            power,
            new FakeAutoStartService(),
            new FakeShellLauncher(),
            new FileLoggerProvider(_logDirectory),
            SharedUpdateChecker,
            new FakeWindowNavigator(),
            new FakeNotificationService(),
            new FakeHistoryStore(),
            NullLogger<SettingsViewModel>.Instance);
        vm.ConfirmAsync = (_, _) => Task.FromResult(true);
        return new Fixture(vm, store, service, power);
    }

    private static ActionRule Rule(string name, UpsStatus flags = UpsStatus.OB, params RuleAction[] actions) => new()
    {
        Name = name,
        Trigger = new RuleTrigger { Kind = TriggerKind.FlagsBecameActive, Flags = flags },
        Actions = actions.Length == 0 ? [RuleAction.Notify("hello {ups}")] : [.. actions],
    };

    private static AppSettings WithRules(params ActionRule[] rules)
    {
        var settings = new AppSettings();
        settings.Actions.Rules.AddRange(rules);
        return settings;
    }

    [Fact]
    public void Existing_rules_are_listed_in_order_and_the_first_is_selected()
    {
        var f = Build(WithRules(Rule("A"), Rule("B")));

        Assert.Equal(["A", "B"], f.ViewModel.Rules.Select(r => r.Name));
        Assert.Same(f.ViewModel.Rules[0], f.ViewModel.SelectedRule);
        Assert.True(f.ViewModel.HasRules);
        Assert.True(f.ViewModel.HasSelectedRule);
    }

    [Fact]
    public void Without_rules_nothing_is_selected_and_selection_commands_are_disabled()
    {
        var f = Build();

        Assert.False(f.ViewModel.HasRules);
        Assert.Null(f.ViewModel.SelectedRule);
        Assert.False(f.ViewModel.DuplicateRuleCommand.CanExecute(null));
        Assert.False(f.ViewModel.DeleteRuleCommand.CanExecute(null));
        Assert.False(f.ViewModel.MoveRuleUpCommand.CanExecute(null));
        Assert.False(f.ViewModel.MoveRuleDownCommand.CanExecute(null));
    }

    [Fact]
    public void Adding_a_rule_creates_a_valid_custom_rule_with_a_notify_action_and_selects_it()
    {
        var f = Build(WithRules(Rule("A")));

        f.ViewModel.AddRuleCommand.Execute(null);

        var added = f.ViewModel.SelectedRule!;
        Assert.Equal(2, f.ViewModel.Rules.Count);
        Assert.Same(added, f.ViewModel.Rules[1]);
        Assert.Equal(Strings.Prefs_Rules_NewName, added.Name);
        Assert.NotEqual(Guid.Empty, added.Rule.Id);
        Assert.NotEqual(f.ViewModel.Rules[0].Rule.Id, added.Rule.Id);
        Assert.Equal(ShutdownReason.CustomRule, added.Rule.ShutdownReason);
        Assert.Null(added.Rule.UpsName);
        Assert.True(added.Rule.Enabled);
        Assert.Equal(TriggerKind.FlagsBecameActive, added.Rule.Trigger.Kind);
        Assert.Equal(UpsStatus.OB, added.Rule.Trigger.Flags);
        var action = Assert.Single(added.Rule.Actions);
        Assert.Equal(ActionKind.Notify, action.Kind);
        Assert.Equal("{ups}: {status}", action.Message);
        Assert.False(f.ViewModel.HasErrors);
        Assert.False(added.HasProblems);
    }

    [Fact]
    public void Duplicating_a_rule_inserts_an_independent_copy_with_a_new_id_after_it()
    {
        var original = Rule("Low battery", UpsStatus.LB, RuleAction.RunCommand("/bin/true", ["a", "b"], 30), RuleAction.Webhook("https://example.invalid/hook"));
        original.DelaySeconds = 15;
        var f = Build(WithRules(original, Rule("Other")));

        f.ViewModel.DuplicateRuleCommand.Execute(null);

        Assert.Equal(["Low battery", "Low battery (copy)", "Other"], f.ViewModel.Rules.Select(r => r.Name));
        var copy = f.ViewModel.SelectedRule!;
        Assert.Same(f.ViewModel.Rules[1], copy);
        Assert.NotEqual(f.ViewModel.Rules[0].Rule.Id, copy.Rule.Id);
        Assert.Equal(15, copy.Rule.DelaySeconds);
        Assert.Equal(UpsStatus.LB, copy.Rule.Trigger.Flags);
        Assert.Equal(2, copy.Rule.Actions.Count);
        Assert.Equal(["a", "b"], copy.Rule.Actions[0].Arguments);

        // Independent: editing the copy leaves the original alone.
        copy.Actions[0].ArgumentsText = "changed";
        copy.Actions[1].Url = "https://example.invalid/other";
        Assert.Equal(["a", "b"], f.ViewModel.Rules[0].Rule.Actions[0].Arguments);
        Assert.Equal("https://example.invalid/hook", f.ViewModel.Rules[0].Rule.Actions[1].Url);
    }

    [Fact]
    public async Task Deleting_a_rule_asks_first_and_keeps_the_rule_when_declined()
    {
        var f = Build(WithRules(Rule("A"), Rule("B")));
        string? askedTitle = null;
        string? askedMessage = null;
        f.ViewModel.ConfirmAsync = (title, message) =>
        {
            askedTitle = title;
            askedMessage = message;
            return Task.FromResult(false);
        };

        await f.ViewModel.DeleteRuleCommand.ExecuteAsync(null);

        Assert.Equal(Strings.Prefs_Rules_Delete_ConfirmTitle, askedTitle);
        Assert.Contains("A", askedMessage, StringComparison.Ordinal);
        Assert.Equal(2, f.ViewModel.Rules.Count);
    }

    [Fact]
    public async Task Deleting_a_confirmed_rule_removes_it_and_selects_its_neighbour()
    {
        var f = Build(WithRules(Rule("A"), Rule("B"), Rule("C")));
        f.ViewModel.SelectedRule = f.ViewModel.Rules[1];

        await f.ViewModel.DeleteRuleCommand.ExecuteAsync(null);

        Assert.Equal(["A", "C"], f.ViewModel.Rules.Select(r => r.Name));
        Assert.Equal("C", f.ViewModel.SelectedRule!.Name);

        await f.ViewModel.DeleteRuleCommand.ExecuteAsync(null);
        await f.ViewModel.DeleteRuleCommand.ExecuteAsync(null);

        Assert.Empty(f.ViewModel.Rules);
        Assert.Null(f.ViewModel.SelectedRule);
        Assert.False(f.ViewModel.HasErrors);
    }

    [Fact]
    public async Task Moving_rules_changes_their_priority_and_is_saved_in_that_order()
    {
        var f = Build(WithRules(Rule("A"), Rule("B"), Rule("C")));
        f.ViewModel.SelectedRule = f.ViewModel.Rules[0];
        Assert.False(f.ViewModel.MoveRuleUpCommand.CanExecute(null));
        Assert.True(f.ViewModel.MoveRuleDownCommand.CanExecute(null));

        f.ViewModel.MoveRuleDownCommand.Execute(null);
        f.ViewModel.MoveRuleDownCommand.Execute(null);

        Assert.Equal(["B", "C", "A"], f.ViewModel.Rules.Select(r => r.Name));
        Assert.Equal("A", f.ViewModel.SelectedRule!.Name);
        Assert.True(f.ViewModel.MoveRuleUpCommand.CanExecute(null));
        Assert.False(f.ViewModel.MoveRuleDownCommand.CanExecute(null));

        f.ViewModel.MoveRuleUpCommand.Execute(null);
        Assert.Equal(["B", "A", "C"], f.ViewModel.Rules.Select(r => r.Name));

        await f.ViewModel.SaveCommand.ExecuteAsync(null);
        Assert.Equal(["B", "A", "C"], f.Store.Settings.Actions.Rules.Select(r => r.Name));
    }

    [Fact]
    public void Switching_the_trigger_kind_shows_only_the_relevant_fields_and_revalidates()
    {
        var f = Build();
        f.ViewModel.AddRuleCommand.Execute(null);
        var rule = f.ViewModel.SelectedRule!;
        Assert.True(rule.IsFlagsTrigger);
        Assert.False(rule.IsLevelTrigger);
        Assert.False(rule.IsValueTrigger);
        Assert.False(rule.IsDurationTrigger);

        // Clearing the only flag is invalid for a flag trigger ...
        rule.Flags.Single(flag => flag.IsChecked).IsChecked = false;
        Assert.True(f.ViewModel.HasErrors);
        Assert.False(f.ViewModel.SaveCommand.CanExecute(null));

        // ... but flags do not matter for a value trigger, so switching clears the problem.
        rule.SelectedTriggerKind = rule.Options.TriggerKinds.First(o => o.Value == TriggerKind.ValueAtOrBelow);
        Assert.True(rule.IsValueTrigger);
        Assert.True(rule.IsLevelTrigger);
        Assert.False(rule.IsFlagsTrigger);
        Assert.False(f.ViewModel.HasErrors);
        Assert.Equal(TriggerKind.ValueAtOrBelow, rule.Rule.Trigger.Kind);

        rule.SelectedMetric = rule.Options.Metrics.First(o => o.Value == RuleMetric.BatteryCharge);
        rule.Threshold = 25.5m;
        rule.OnBatteryOnly = true;
        Assert.Equal(RuleMetric.BatteryCharge, rule.Rule.Trigger.Metric);
        Assert.Equal(25.5, rule.Rule.Trigger.Threshold);
        Assert.True(rule.Rule.Trigger.OnBatteryOnly);
        Assert.Contains("25.5", rule.Summary, StringComparison.Ordinal);

        rule.SelectedTriggerKind = rule.Options.TriggerKinds.First(o => o.Value == TriggerKind.OnBatteryFor);
        Assert.True(rule.IsDurationTrigger);
        Assert.True(rule.IsLevelTrigger);
        Assert.False(rule.IsValueTrigger);
        Assert.Equal(60, rule.Rule.Trigger.DurationSeconds);

        rule.SelectedTriggerKind = rule.Options.TriggerKinds.First(o => o.Value == TriggerKind.FlagsBecameInactive);
        Assert.False(rule.IsLevelTrigger);
        Assert.True(rule.IsFlagsTrigger);
        Assert.True(f.ViewModel.HasErrors);
    }

    [Fact]
    public void Flag_checkboxes_write_the_combined_flags_to_the_trigger()
    {
        var f = Build();
        f.ViewModel.AddRuleCommand.Execute(null);
        var rule = f.ViewModel.SelectedRule!;

        rule.Flags.Single(flag => flag.Flag == UpsStatus.LB).IsChecked = true;
        rule.Flags.Single(flag => flag.Flag == UpsStatus.FSD).IsChecked = true;

        Assert.Equal(UpsStatus.OB | UpsStatus.LB | UpsStatus.FSD, rule.Rule.Trigger.Flags);
        Assert.Contains("OB + LB + FSD", rule.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Flag_choices_cover_the_flags_the_issue_names()
    {
        UpsStatus[] expected =
        [
            UpsStatus.OL, UpsStatus.OB, UpsStatus.LB, UpsStatus.HB, UpsStatus.RB, UpsStatus.CHRG, UpsStatus.DISCHRG,
            UpsStatus.BYPASS, UpsStatus.CAL, UpsStatus.OFF, UpsStatus.OVER, UpsStatus.TRIM, UpsStatus.BOOST, UpsStatus.FSD,
        ];
        Assert.All(expected, flag => Assert.Contains(flag, RuleOptions.SelectableFlags));
    }

    [Fact]
    public void Validation_problems_are_shown_on_the_rule_and_block_save_until_fixed()
    {
        var f = Build(WithRules(Rule("A"), Rule("B")));
        var second = f.ViewModel.Rules[1];

        second.Name = "  ";

        Assert.True(f.ViewModel.HasErrors);
        Assert.False(f.ViewModel.SaveCommand.CanExecute(null));
        Assert.False(f.ViewModel.ApplyCommand.CanExecute(null));
        Assert.True(second.HasProblems);
        Assert.Contains(Strings.Validation_RuleNameRequired, second.Problems);
        Assert.False(f.ViewModel.Rules[0].HasProblems);

        second.Name = "B fixed";

        Assert.False(f.ViewModel.HasErrors);
        Assert.True(f.ViewModel.SaveCommand.CanExecute(null));
        Assert.Empty(second.Problems);
    }

    [Fact]
    public void A_rule_without_actions_is_invalid()
    {
        var f = Build(WithRules(Rule("A")));
        var rule = f.ViewModel.Rules[0];

        rule.Actions[0].RemoveCommand.Execute(null);

        Assert.Empty(rule.Actions);
        Assert.Empty(rule.Rule.Actions);
        Assert.Contains(Strings.Validation_RuleActionsRequired, rule.Problems);
        Assert.False(f.ViewModel.SaveCommand.CanExecute(null));

        rule.AddActionCommand.Execute(null);

        Assert.Single(rule.Actions);
        Assert.False(f.ViewModel.HasErrors);
    }

    [Fact]
    public void Action_kinds_show_their_own_fields_and_are_validated_per_kind()
    {
        var f = Build(WithRules(Rule("A")));
        var rule = f.ViewModel.Rules[0];
        var action = rule.Actions[0];
        Assert.True(action.IsMessage);
        Assert.False(action.IsLog);

        ActionKindOption Kind(ActionKind kind) => action.Options.ActionKinds.First(o => o.Value == kind);

        action.SelectedKind = Kind(ActionKind.Log);
        Assert.True(action.IsMessage);
        Assert.True(action.IsLog);
        action.SelectedLogLevel = action.Options.LogLevels.First(o => o.Value == LogLevel.Warning);
        Assert.Equal(LogLevel.Warning, action.Model.LogLevel);
        Assert.Equal(ActionKind.Log, action.Model.Kind);

        action.SelectedKind = Kind(ActionKind.RunCommand);
        Assert.True(action.IsCommand);
        Assert.False(action.IsMessage);
        Assert.True(f.ViewModel.HasErrors, "a command is required");
        Assert.Contains(Strings.Validation_RuleCommandRequired, rule.Problems);

        action.Command = "/usr/bin/notify";
        action.ArgumentsText = "--title\r\n\r\n{ups}\n";
        action.TimeoutSeconds = 5;
        Assert.False(f.ViewModel.HasErrors);
        Assert.Equal(["--title", "{ups}"], action.Model.Arguments);
        Assert.Equal(5, action.Model.TimeoutSeconds);

        action.SelectedKind = Kind(ActionKind.Webhook);
        Assert.True(action.IsWebhook);
        Assert.True(f.ViewModel.HasErrors, "a valid URL is required");
        action.Url = "https://example.invalid/hook";
        Assert.False(f.ViewModel.HasErrors);

        action.SelectedKind = Kind(ActionKind.StopComputer);
        Assert.True(action.IsStop);
        Assert.True(rule.StopsComputer);
    }

    [Fact]
    public void Switching_to_a_message_action_without_a_message_supplies_the_default()
    {
        var f = Build(WithRules(Rule("A", UpsStatus.OB, RuleAction.Webhook("https://example.invalid/hook"))));
        var action = f.ViewModel.Rules[0].Actions[0];

        action.SelectedKind = action.Options.ActionKinds.First(o => o.Value == ActionKind.Notify);

        Assert.Equal("{ups}: {status}", action.Message);
        Assert.False(f.ViewModel.HasErrors);
    }

    [Fact]
    public void Stop_actions_are_limited_to_what_the_platform_supports()
    {
        var f = Build(configurePower: power => power.Supported.Remove(StopAction.Hibernate));
        f.ViewModel.AddRuleCommand.Execute(null);

        var options = f.ViewModel.SelectedRule!.Options.StopActions;

        Assert.DoesNotContain(options, o => o.Value == StopAction.Hibernate);
        Assert.Contains(options, o => o.Value == StopAction.Shutdown);
    }

    [Fact]
    public void A_stop_action_raises_the_stop_warning_and_removing_it_clears_it()
    {
        var f = Build(WithRules(Rule("A", UpsStatus.LB, RuleAction.StopComputer(StopAction.Suspend), RuleAction.Notify("x"))));
        var rule = f.ViewModel.Rules[0];
        Assert.True(rule.StopsComputer);
        Assert.Equal(StopAction.Suspend, rule.Actions[0].SelectedStopAction.Value);
        var changed = new List<string?>();
        rule.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        rule.Actions[0].RemoveCommand.Execute(null);

        Assert.False(rule.StopsComputer);
        Assert.Contains(nameof(RuleViewModel.StopsComputer), changed);
    }

    [Fact]
    public void Summary_describes_trigger_and_actions()
    {
        var f = Build(WithRules(Rule("A", UpsStatus.OB | UpsStatus.LB, RuleAction.Notify("x"), RuleAction.StopComputer(StopAction.Shutdown))));

        var summary = f.ViewModel.Rules[0].Summary;

        Assert.Contains("OB + LB", summary, StringComparison.Ordinal);
        Assert.Contains(Strings.Prefs_Rules_Action_Notify, summary, StringComparison.Ordinal);
        Assert.Contains(Strings.StopAction_Shutdown, summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Placeholder_hint_lists_every_placeholder()
    {
        var f = Build();

        foreach (var name in ActionContext.PlaceholderNames)
        {
            Assert.Contains("{" + name + "}", f.ViewModel.RuleOptions.PlaceholderHint, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Save_persists_every_rule_field()
    {
        var f = Build();
        f.ViewModel.AddRuleCommand.Execute(null);
        var rule = f.ViewModel.SelectedRule!;
        rule.Name = "Shut down at 20 percent";
        rule.Enabled = false;
        rule.SelectedTriggerKind = rule.Options.TriggerKinds.First(o => o.Value == TriggerKind.ValueAtOrBelow);
        rule.SelectedMetric = rule.Options.Metrics.First(o => o.Value == RuleMetric.BatteryCharge);
        rule.Threshold = 20;
        rule.OnBatteryOnly = true;
        rule.DelaySeconds = 30;
        rule.Actions[0].Message = "Low: {charge}";
        rule.AddActionCommand.Execute(null);
        rule.Actions[1].SelectedKind = rule.Options.ActionKinds.First(o => o.Value == ActionKind.RunCommand);
        rule.Actions[1].Command = "/usr/bin/logger";
        rule.Actions[1].ArgumentsText = "one\ntwo";
        rule.Actions[1].TimeoutSeconds = 12;
        rule.AddActionCommand.Execute(null);
        rule.Actions[2].SelectedKind = rule.Options.ActionKinds.First(o => o.Value == ActionKind.StopComputer);
        rule.Actions[2].SelectedStopAction = rule.Options.StopActions.First(o => o.Value == StopAction.Suspend);
        var id = rule.Rule.Id;

        await f.ViewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(1, f.Store.SaveCount);
        var saved = Assert.Single(f.Store.Settings.Actions.Rules);
        Assert.Equal(id, saved.Id);
        Assert.Equal("Shut down at 20 percent", saved.Name);
        Assert.False(saved.Enabled);
        Assert.Null(saved.UpsName);
        Assert.Equal(ShutdownReason.CustomRule, saved.ShutdownReason);
        Assert.Equal(30, saved.DelaySeconds);
        Assert.Equal(TriggerKind.ValueAtOrBelow, saved.Trigger.Kind);
        Assert.Equal(RuleMetric.BatteryCharge, saved.Trigger.Metric);
        Assert.Equal(20, saved.Trigger.Threshold);
        Assert.True(saved.Trigger.OnBatteryOnly);
        Assert.Equal(3, saved.Actions.Count);
        Assert.Equal((ActionKind.Notify, "Low: {charge}"), (saved.Actions[0].Kind, saved.Actions[0].Message));
        Assert.Equal(ActionKind.RunCommand, saved.Actions[1].Kind);
        Assert.Equal("/usr/bin/logger", saved.Actions[1].Command);
        Assert.Equal(["one", "two"], saved.Actions[1].Arguments);
        Assert.Equal(12, saved.Actions[1].TimeoutSeconds);
        Assert.Equal((ActionKind.StopComputer, StopAction.Suspend), (saved.Actions[2].Kind, saved.Actions[2].StopAction));

        // The live settings carry the rules too, and a new view model on them shows the same rule again.
        Assert.Single(f.Settings.Current.Actions.Rules);
        var reopened = Build(f.Store.Settings);
        Assert.Equal("Shut down at 20 percent", reopened.ViewModel.Rules.Single().Name);
    }

    [Fact]
    public void Save_is_refused_while_a_rule_is_invalid_and_nothing_is_written()
    {
        var f = Build(WithRules(Rule("A")));
        f.ViewModel.Rules[0].Name = string.Empty;

        Assert.False(f.ViewModel.SaveCommand.CanExecute(null));
        Assert.Equal(0, f.Store.SaveCount);
    }

    [Fact]
    public void Cancel_discards_rule_edits()
    {
        var f = Build(WithRules(Rule("A")));
        var closed = false;
        f.ViewModel.CloseRequested += (_, _) => closed = true;

        f.ViewModel.Rules[0].Name = "Edited";
        f.ViewModel.AddRuleCommand.Execute(null);
        f.ViewModel.CancelCommand.Execute(null);

        Assert.True(closed);
        Assert.Equal(0, f.Store.SaveCount);
        var live = Assert.Single(f.Settings.Current.Actions.Rules);
        Assert.Equal("A", live.Name);
    }

    [Fact]
    public void Editing_rules_does_not_mutate_the_live_settings_until_saved()
    {
        var f = Build(WithRules(Rule("A")));

        f.ViewModel.Rules[0].Actions[0].Message = "changed";
        f.ViewModel.Rules[0].Enabled = false;

        var live = f.Settings.Current.Actions.Rules[0];
        Assert.Equal("hello {ups}", live.Actions[0].Message);
        Assert.True(live.Enabled);
    }
}
