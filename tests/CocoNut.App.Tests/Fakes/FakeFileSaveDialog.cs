using CocoNut.App.Services;

namespace CocoNut.App.Tests.Fakes;

/// <summary>Hands the view model a <see cref="MemoryStream"/> instead of showing a save dialog; cancels when <see cref="Cancel"/> is set.</summary>
public sealed class FakeFileSaveDialog : IFileSaveDialog
{
    public bool Cancel { get; set; }

    public string? LastTitle { get; private set; }

    public string? LastSuggestedFileName { get; private set; }

    public int PickCount { get; private set; }

    /// <summary>The stream of the last pick (its buffer stays readable after the view model closed it).</summary>
    public MemoryStream? Stream { get; private set; }

    /// <summary>What was written to <see cref="Stream"/>, decoded as UTF-8.</summary>
    public string Text => System.Text.Encoding.UTF8.GetString(Stream!.ToArray());

    public Task<SaveTarget?> PickCsvAsync(string title, string suggestedFileName, string fileTypeName)
    {
        PickCount++;
        LastTitle = title;
        LastSuggestedFileName = suggestedFileName;
        if (Cancel)
        {
            return Task.FromResult<SaveTarget?>(null);
        }

        Stream = new MemoryStream();
        return Task.FromResult<SaveTarget?>(new SaveTarget(Stream, "/fake/" + suggestedFileName));
    }
}
