namespace CocoNut.Platform.Power;

/// <summary>Default <see cref="IExecutableLocator"/>: looks for the executable in every directory on PATH.</summary>
internal sealed class PathExecutableLocator : IExecutableLocator
{
    public bool ExistsOnPath(string executableName)
    {
        var pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathVariable))
        {
            return false;
        }

        foreach (var directory in pathVariable.Split(Path.PathSeparator))
        {
            if (directory.Length == 0)
            {
                continue;
            }

            if (File.Exists(Path.Combine(directory, executableName)))
            {
                return true;
            }
        }

        return false;
    }
}
