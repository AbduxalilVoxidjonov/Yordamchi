using System.IO;
using System.Windows.Media.Imaging;

namespace PdfEdit.Models;

/// <summary>
/// A single page of a source document together with its rendered thumbnail.
/// <para>
/// This is a pure data carrier: it never mutates and it never talks to a service.
/// The editable state (rotation applied by the user, selection, ordering) lives in
/// <c>PdfEdit.ViewModels.PageItemViewModel</c>, which wraps this model.
/// </para>
/// </summary>
public sealed class PageModel
{
    /// <summary>Absolute path of the PDF the page was read from.</summary>
    public required string SourceFilePath { get; init; }

    /// <summary>Zero-based page index inside <see cref="SourceFilePath"/>.</summary>
    public required int SourcePageIndex { get; init; }

    /// <summary>
    /// Rendered preview. Always a frozen <see cref="BitmapImage"/> so it can be created on a
    /// background thread and bound directly from the UI thread without marshalling.
    /// </summary>
    public required BitmapSource Thumbnail { get; init; }

    /// <summary>Page width in PDF points (1/72 inch), already accounting for the page's own /Rotate.</summary>
    public double WidthPoints { get; init; }

    /// <summary>Page height in PDF points (1/72 inch), already accounting for the page's own /Rotate.</summary>
    public double HeightPoints { get; init; }

    /// <summary>
    /// Rotation already baked into the rendered thumbnail. The rasterizer honours the page's own
    /// <c>/Rotate</c>, so this is <see cref="PageRotation.None"/> for freshly loaded pages and only
    /// matters if a caller ever renders a pre-rotated preview.
    /// </summary>
    public PageRotation Rotation { get; init; }

    /// <summary>Human friendly one-based page label, e.g. <c>12</c>.</summary>
    public int DisplayPageNumber => SourcePageIndex + 1;

    public string SourceFileName => Path.GetFileName(SourceFilePath);

    public bool IsLandscape => WidthPoints > HeightPoints;

    /// <summary>Aspect ratio (width / height); falls back to A4 portrait when the size is unknown.</summary>
    public double AspectRatio => HeightPoints > 0 ? WidthPoints / HeightPoints : 595d / 842d;

    public override string ToString() => $"{SourceFileName} #{DisplayPageNumber}";
}
