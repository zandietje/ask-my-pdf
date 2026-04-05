namespace AskMyPdf.Core.Services;

/// <summary>
/// Narrows broad page text to the exact span supporting the answer.
/// Returns null if no clear supporting text is found — caller should skip this citation.
/// </summary>
public interface ICitationFocuser
{
    Task<string?> FocusCitationAsync(
        string pageText, string question, string fullAnswer,
        CancellationToken ct = default);
}
