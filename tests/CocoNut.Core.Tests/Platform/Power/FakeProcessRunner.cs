using CocoNut.Platform.Processes;

namespace CocoNut.Core.Tests.Platform.Power;

/// <summary>Records the last invocation and returns a preconfigured result, so no real process ever starts.</summary>
internal sealed class FakeProcessRunner : IProcessRunner
{
    private readonly ProcessResult _result;

    public FakeProcessRunner(ProcessResult result) => _result = result;

    public int CallCount { get; private set; }

    public string? FileName { get; private set; }

    public IReadOnlyList<string>? Arguments { get; private set; }

    public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken = default)
    {
        CallCount++;
        FileName = fileName;
        Arguments = arguments;
        return Task.FromResult(_result);
    }
}
