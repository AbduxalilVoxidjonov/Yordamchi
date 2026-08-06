using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SkiaSharp;

namespace PdfEdit.Helpers;

/// <summary>
/// Bridges SkiaSharp (used by pdfium rasterization and by the image importer) and WPF imaging.
/// <para>
/// Everything here is meant to run on a background thread, which is why the only
/// <see cref="BitmapSource"/> it produces is a frozen one.
/// </para>
/// </summary>
public static class SkiaImageHelper
{
    private static readonly SKSamplingOptions HighQualitySampling =
        new(SKFilterMode.Linear, SKMipmapMode.Linear);

    /// <summary>
    /// Converts an <see cref="SKBitmap"/> into a frozen <see cref="BitmapImage"/>.
    /// </summary>
    /// <remarks>
    /// The bitmap is encoded to PNG in memory and decoded with <see cref="BitmapCacheOption.OnLoad"/>
    /// so the stream can be released immediately, and the result is frozen because it is created on
    /// a worker thread but consumed by the UI thread.
    /// </remarks>
    /// <param name="bitmap">Source bitmap; ownership stays with the caller.</param>
    public static BitmapImage ToFrozenBitmapImage(SKBitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new NotSupportedException("The rendered bitmap could not be encoded.");
        using var stream = new MemoryStream(data.ToArray(), writable: false);

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    /// <summary>
    /// Decodes an image file and applies its EXIF orientation, so the returned bitmap is already
    /// the right way up.
    /// </summary>
    /// <exception cref="NotSupportedException">The file could not be decoded by Skia or WIC.</exception>
    public static SKBitmap DecodeOriented(string path)
    {
        var origin = ReadOrigin(path);
        var bitmap = DecodeWithSkia(path) ?? DecodeWithWic(path)
            ?? throw new NotSupportedException($"'{Path.GetFileName(path)}' is not a supported image.");
        return ApplyOrigin(bitmap, origin);
    }

    /// <summary>EXIF/codec orientation of an image file, or <see cref="SKEncodedOrigin.TopLeft"/>.</summary>
    public static SKEncodedOrigin ReadOrigin(string path)
    {
        try
        {
            using var codec = SKCodec.Create(path);
            return codec?.EncodedOrigin ?? SKEncodedOrigin.TopLeft;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return SKEncodedOrigin.TopLeft;
        }
    }

    /// <summary>Pixel size of an image file without fully decoding it, or <c>null</c> when unknown.</summary>
    public static (int Width, int Height)? ReadPixelSize(string path)
    {
        try
        {
            using var codec = SKCodec.Create(path);
            return codec is null ? null : (codec.Info.Width, codec.Info.Height);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }

    /// <summary>
    /// Shrinks <paramref name="bitmap"/> so its longest edge is at most <paramref name="maxEdgePixels"/>.
    /// </summary>
    /// <remarks>
    /// Consuming helper: returns <paramref name="bitmap"/> untouched when no work is needed,
    /// otherwise disposes it and returns the resized copy. Assign the result back to the variable.
    /// </remarks>
    public static SKBitmap LimitMaxEdge(SKBitmap bitmap, int maxEdgePixels)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        if (maxEdgePixels <= 0)
            return bitmap;

        var longest = Math.Max(bitmap.Width, bitmap.Height);
        if (longest <= maxEdgePixels)
            return bitmap;

        var scale = maxEdgePixels / (double)longest;
        return Resize(bitmap, Scale(bitmap.Width, scale), Scale(bitmap.Height, scale));
    }

    /// <summary>
    /// Shrinks <paramref name="bitmap"/> to <paramref name="targetWidth"/> keeping the aspect ratio.
    /// Never upscales. Same consuming semantics as <see cref="LimitMaxEdge"/>.
    /// </summary>
    public static SKBitmap ScaleToWidth(SKBitmap bitmap, int targetWidth)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        if (targetWidth <= 0 || bitmap.Width <= targetWidth)
            return bitmap;

        var scale = targetWidth / (double)bitmap.Width;
        return Resize(bitmap, targetWidth, Scale(bitmap.Height, scale));
    }

    /// <summary>True when at least one pixel is not fully opaque.</summary>
    public static bool HasTransparency(SKBitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        if (bitmap.AlphaType == SKAlphaType.Opaque)
            return false;
        if (bitmap.ColorType is not (SKColorType.Bgra8888 or SKColorType.Rgba8888))
            return true; // Unknown layout: assume alpha rather than flatten it away.

        var pixels = bitmap.GetPixelSpan();
        var rowBytes = bitmap.RowBytes;
        var usefulBytes = bitmap.Width * 4;
        for (var y = 0; y < bitmap.Height; y++)
        {
            var row = pixels.Slice(y * rowBytes, usefulBytes);
            for (var i = 3; i < row.Length; i += 4)
            {
                if (row[i] != byte.MaxValue)
                    return true;
            }
        }

        return false;
    }

    private static SKBitmap Resize(SKBitmap bitmap, int width, int height)
    {
        var info = new SKImageInfo(width, height, bitmap.ColorType, bitmap.AlphaType);
        var resized = bitmap.Resize(info, HighQualitySampling);
        if (resized is null)
            return bitmap;

        bitmap.Dispose();
        return resized;
    }

    private static int Scale(int value, double factor) => Math.Max(1, (int)Math.Round(value * factor));

    private static SKBitmap? DecodeWithSkia(string path)
    {
        try
        {
            return SKBitmap.Decode(path);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }

    /// <summary>
    /// Windows Imaging Component fallback for formats Skia cannot decode, most importantly TIFF.
    /// </summary>
    private static SKBitmap? DecodeWithWic(string path)
    {
        try
        {
            using var file = File.OpenRead(path);
            var decoder = BitmapDecoder.Create(file, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0)
                return null;

            var converted = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0d);
            converted.Freeze();

            var bitmap = new SKBitmap(new SKImageInfo(converted.PixelWidth, converted.PixelHeight, SKColorType.Bgra8888, SKAlphaType.Unpremul));
            converted.CopyPixels(Int32Rect.Empty, bitmap.GetPixels(), bitmap.ByteCount, bitmap.RowBytes);
            return bitmap;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }

    /// <summary>
    /// Bakes an EXIF orientation into the pixels. Consuming helper, see <see cref="LimitMaxEdge"/>.
    /// </summary>
    private static SKBitmap ApplyOrigin(SKBitmap source, SKEncodedOrigin origin)
    {
        if (origin == SKEncodedOrigin.TopLeft)
            return source;

        var swapsAxes = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;

        var width = source.Width;
        var height = source.Height;
        var info = new SKImageInfo(
            swapsAxes ? height : width,
            swapsAxes ? width : height,
            SKImageInfo.PlatformColorType,
            source.AlphaType == SKAlphaType.Opaque ? SKAlphaType.Opaque : SKAlphaType.Premul);

        var target = new SKBitmap(info);
        try
        {
            using var canvas = new SKCanvas(target);
            using var image = SKImage.FromBitmap(source);
            canvas.SetMatrix(OriginMatrix(origin, width, height));
            canvas.DrawImage(image, 0f, 0f, HighQualitySampling);
        }
        catch
        {
            target.Dispose();
            throw;
        }

        source.Dispose();
        return target;
    }

    /// <summary>Skia's canonical EXIF-origin matrices; <paramref name="w"/>/<paramref name="h"/> are the source size.</summary>
    private static SKMatrix OriginMatrix(SKEncodedOrigin origin, int w, int h) => origin switch
    {
        SKEncodedOrigin.TopRight => new SKMatrix(-1f, 0f, w, 0f, 1f, 0f, 0f, 0f, 1f),
        SKEncodedOrigin.BottomRight => new SKMatrix(-1f, 0f, w, 0f, -1f, h, 0f, 0f, 1f),
        SKEncodedOrigin.BottomLeft => new SKMatrix(1f, 0f, 0f, 0f, -1f, h, 0f, 0f, 1f),
        SKEncodedOrigin.LeftTop => new SKMatrix(0f, 1f, 0f, 1f, 0f, 0f, 0f, 0f, 1f),
        SKEncodedOrigin.RightTop => new SKMatrix(0f, -1f, h, 1f, 0f, 0f, 0f, 0f, 1f),
        SKEncodedOrigin.RightBottom => new SKMatrix(0f, -1f, h, -1f, 0f, w, 0f, 0f, 1f),
        SKEncodedOrigin.LeftBottom => new SKMatrix(0f, 1f, 0f, -1f, 0f, w, 0f, 0f, 1f),
        _ => SKMatrix.Identity
    };
}
