using CocoNut.App.Services;
using CocoNut.Core.Updates;

namespace CocoNut.App.Tests.Fakes;

/// <summary>Records navigation requests instead of opening real windows.</summary>
public sealed class FakeWindowNavigator : IWindowNavigator
{
    public int ShowSettingsCount { get; private set; }

    public int ShowUpsVariablesCount { get; private set; }

    public int ShowAboutCount { get; private set; }

    public int ShowMainWindowCount { get; private set; }

    public UpdateCheckResult? LastUpdateResult { get; private set; }

    public Task ShowSettingsAsync()
    {
        ShowSettingsCount++;
        return Task.CompletedTask;
    }

    public Task ShowUpsVariablesAsync()
    {
        ShowUpsVariablesCount++;
        return Task.CompletedTask;
    }

    public Task ShowAboutAsync()
    {
        ShowAboutCount++;
        return Task.CompletedTask;
    }

    public Task ShowUpdateAvailableAsync(UpdateCheckResult result)
    {
        LastUpdateResult = result;
        return Task.CompletedTask;
    }

    public void ShowMainWindow() => ShowMainWindowCount++;
}
