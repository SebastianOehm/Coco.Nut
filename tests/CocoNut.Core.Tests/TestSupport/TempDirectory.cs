namespace CocoNut.Core.Tests.TestSupport;

/// <summary>A unique, empty directory under the OS temp folder that deletes itself when disposed.</summary>
public sealed class TempDirectory : IDisposable
{
    public string Path { get; }

    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "CocoNutTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // Best effort; a handle left open by a background writer shouldn't fail the test run.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
