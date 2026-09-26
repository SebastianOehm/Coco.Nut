using CocoNut.App.Services;

namespace CocoNut.App.Tests.Fakes;

/// <summary>Runs every posted action synchronously and immediately, so tests do not need a pumped Avalonia dispatcher.</summary>
public sealed class ImmediateUiDispatcher : IUiDispatcher
{
    public void Post(Action action) => action();
}
