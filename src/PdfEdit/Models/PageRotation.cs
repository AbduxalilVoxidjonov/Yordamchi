namespace PdfEdit.Models;

/// <summary>
/// Clockwise rotation applied on top of a page's intrinsic /Rotate value.
/// Values match the degrees expected by the PDF page dictionary.
/// </summary>
public enum PageRotation
{
    None = 0,
    Rotate90 = 90,
    Rotate180 = 180,
    Rotate270 = 270
}

public static class PageRotationExtensions
{
    /// <summary>Adds <paramref name="degrees"/> (any multiple of 90, may be negative) and normalizes to 0..270.</summary>
    public static PageRotation Add(this PageRotation rotation, int degrees)
    {
        var value = ((int)rotation + degrees) % 360;
        if (value < 0)
            value += 360;
        return (PageRotation)value;
    }

    public static PageRotation RotateClockwise(this PageRotation rotation) => rotation.Add(90);

    public static PageRotation RotateCounterClockwise(this PageRotation rotation) => rotation.Add(-90);

    /// <summary>True when the rotation swaps the page's width and height.</summary>
    public static bool IsQuarterTurn(this PageRotation rotation)
        => rotation is PageRotation.Rotate90 or PageRotation.Rotate270;
}
