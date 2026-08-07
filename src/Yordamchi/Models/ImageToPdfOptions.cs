namespace Yordamchi.Models;

/// <summary>How the PDF page is sized around an imported image.</summary>
public enum PdfPageSizeMode
{
    /// <summary>Page is exactly as large as the image (at <see cref="ImageToPdfOptions.ImageDpi"/>).</summary>
    FitToImage,
    /// <summary>A4 (595 x 842 pt); image is scaled to fit inside the margins.</summary>
    A4,
    /// <summary>US Letter (612 x 792 pt); image is scaled to fit inside the margins.</summary>
    Letter
}

/// <summary>Settings for <c>IPdfService.ConvertImagesToPdfAsync</c>.</summary>
public sealed class ImageToPdfOptions
{
    public PdfPageSizeMode PageSizeMode { get; set; } = PdfPageSizeMode.FitToImage;

    /// <summary>Margin in points applied on all four sides for the fixed page sizes.</summary>
    public double MarginPoints { get; set; } = 28d; // ~10 mm

    /// <summary>Rotate fixed-size pages to landscape when the image is wider than it is tall.</summary>
    public bool AutoOrientation { get; set; } = true;

    /// <summary>Assumed image resolution when the file carries no DPI metadata (FitToImage mode).</summary>
    public double ImageDpi { get; set; } = 96d;

    /// <summary>
    /// Images wider or taller than this are downscaled before being embedded.
    /// Keeps a folder of 40 MP phone photos from producing a 400 MB PDF. Set to 0 to disable.
    /// </summary>
    public int MaxImageEdgePixels { get; set; } = 3508; // A4 @ 300 dpi

    /// <summary>JPEG quality (1..100) used when an image has to be re-encoded.</summary>
    public int JpegQuality { get; set; } = 88;

    public static ImageToPdfOptions Default => new();
}
