namespace PdfEdit.Models;

/// <summary>Progress payload reported by long running <c>IPdfService</c> operations.</summary>
/// <param name="Completed">Number of finished units (pages, files, images).</param>
/// <param name="Total">Total number of units; <c>0</c> when unknown.</param>
/// <param name="Message">Optional status line for the UI, e.g. the current file name.</param>
public readonly record struct PdfProgress(int Completed, int Total, string? Message = null)
{
    /// <summary>0..100; returns 0 when <see cref="Total"/> is unknown.</summary>
    public double Percentage => Total <= 0 ? 0d : Math.Clamp(Completed * 100d / Total, 0d, 100d);

    public bool IsIndeterminate => Total <= 0;

    public override string ToString()
        => Total > 0 ? $"{Completed}/{Total} — {Message}" : Message ?? string.Empty;
}
