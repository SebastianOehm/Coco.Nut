namespace CocoNut.Platform.Processes;

/// <summary>Outcome of running an external process to completion.</summary>
internal readonly record struct ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Succeeded => ExitCode == 0;
}
