using CocoNut.Platform.Power;

namespace CocoNut.Core.Tests.Platform.Power;

internal sealed class FakeExecutableLocator : IExecutableLocator
{
    public bool Exists { get; set; } = true;

    public string? LastRequested { get; private set; }

    public bool ExistsOnPath(string executableName)
    {
        LastRequested = executableName;
        return Exists;
    }
}
