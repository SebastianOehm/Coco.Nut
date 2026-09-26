using CocoNut.App.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace CocoNut.App.Tests.Services;

/// <summary>
/// Exercises the single-instance hand-off protocol with two in-process endpoints on a unique pipe name (no real
/// second OS process involved) - the second instance's "show" request must reach the first instance's callback.
/// </summary>
public sealed class SingleInstanceIpcTests
{
    private static string UniquePipeName() => $"CocoNutTest_{Guid.NewGuid():N}";

    [Fact]
    public async Task A_show_request_reaches_the_listening_server()
    {
        var pipeName = UniquePipeName();
        var showRequested = new TaskCompletionSource();

        await using var server = SingleInstanceIpc.StartServer(pipeName, () => showRequested.TrySetResult(), NullLogger.Instance);

        var delivered = await SingleInstanceIpc.TryRequestShowAsync(pipeName, TimeSpan.FromSeconds(5));

        Assert.True(delivered);
        var completed = await Task.WhenAny(showRequested.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(showRequested.Task, completed);
    }

    [Fact]
    public async Task Multiple_show_requests_each_invoke_the_callback()
    {
        var pipeName = UniquePipeName();
        var count = 0;
        var allReceived = new TaskCompletionSource();

        await using var server = SingleInstanceIpc.StartServer(pipeName, () =>
        {
            if (Interlocked.Increment(ref count) >= 3)
            {
                allReceived.TrySetResult();
            }
        }, NullLogger.Instance);

        for (var i = 0; i < 3; i++)
        {
            var delivered = await SingleInstanceIpc.TryRequestShowAsync(pipeName, TimeSpan.FromSeconds(5));
            Assert.True(delivered);
        }

        var completed = await Task.WhenAny(allReceived.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(allReceived.Task, completed);
        Assert.Equal(3, count);
    }

    [Fact]
    public async Task Requesting_show_with_nothing_listening_fails_gracefully_and_quickly()
    {
        var pipeName = UniquePipeName();

        var delivered = await SingleInstanceIpc.TryRequestShowAsync(pipeName, TimeSpan.FromMilliseconds(300));

        Assert.False(delivered);
    }

    [Fact]
    public async Task Disposing_the_server_stops_it_from_answering_further_requests()
    {
        var pipeName = UniquePipeName();
        var server = SingleInstanceIpc.StartServer(pipeName, () => { }, NullLogger.Instance);
        await server.DisposeAsync();

        var delivered = await SingleInstanceIpc.TryRequestShowAsync(pipeName, TimeSpan.FromMilliseconds(500));

        Assert.False(delivered);
    }

    [Fact]
    public void Pipe_name_is_scoped_to_the_current_user()
    {
        var name = SingleInstanceIpc.GetPipeName();

        Assert.Contains(Environment.UserName, name, StringComparison.Ordinal);
    }
}
