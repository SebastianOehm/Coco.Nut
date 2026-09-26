namespace CocoNut.Platform.Power;

/// <summary>Seam for checking whether a command is available, so tests don't depend on the real PATH.</summary>
internal interface IExecutableLocator
{
    bool ExistsOnPath(string executableName);
}
