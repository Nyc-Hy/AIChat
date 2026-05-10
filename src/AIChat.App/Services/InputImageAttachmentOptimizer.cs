using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AIChat.Application.Artifacts;

namespace AIChat.App.Services;

public sealed record InputImageAttachment(
    string FileName,
    string MimeType,
    string SourcePath,
    long SizeBytes,
    bool WasOptimized,
    byte[] OptimizedBytes,
    string OptimizedExtension,
    int PixelWidth,
    int PixelHeight,
    long OriginalSizeBytes,
    string OriginalFileName);

public static class InputImageAttachmentOptimizer
{
    private const int MaxLongEdgePixels = 1800;
    private const int JpegQuality = 82;

    public static InputImageAttachment Prepare(FileInfo fileInfo, string mimeType)
    {
        var original = new InputImageAttachment(
            fileInfo.Name,
            mimeType,
            fileInfo.FullName,
            fileInfo.Length,
            WasOptimized: false,
            OptimizedBytes: [],
            OptimizedExtension: "",
            PixelWidth: 0,
            PixelHeight: 0,
            fileInfo.Length,
            fileInfo.Name);

        if (!IsOptimizableImage(fileInfo.Extension, mimeType))
        {
            return original;
        }

        try
        {
            var bitmap = LoadBitmap(fileInfo.FullName);
            var originalWidth = bitmap.PixelWidth;
            var originalHeight = bitmap.PixelHeight;
            if (originalWidth <= 0 || originalHeight <= 0)
            {
                return original;
            }

            var shouldResize = Math.Max(originalWidth, originalHeight) > MaxLongEdgePixels;
            var shouldCompress = fileInfo.Length > InputArtifactVisionPolicy.MaxImageBytes;
            if (!shouldResize && !shouldCompress)
            {
                return original with { PixelWidth = originalWidth, PixelHeight = originalHeight };
            }

            var scale = shouldResize
                ? MaxLongEdgePixels / (double)Math.Max(originalWidth, originalHeight)
                : 1.0;
            var targetWidth = Math.Max(1, (int)Math.Round(originalWidth * scale));
            var targetHeight = Math.Max(1, (int)Math.Round(originalHeight * scale));
            var resized = ResizeToWhiteBackground(bitmap, targetWidth, targetHeight);
            var bytes = EncodeJpeg(resized);
            if (bytes.Length == 0 || bytes.Length >= fileInfo.Length)
            {
                return original with { PixelWidth = originalWidth, PixelHeight = originalHeight };
            }

            var optimizedName = Path.GetFileNameWithoutExtension(fileInfo.Name) + ".jpg";
            return new InputImageAttachment(
                optimizedName,
                "image/jpeg",
                fileInfo.FullName,
                bytes.Length,
                WasOptimized: true,
                OptimizedBytes: bytes,
                OptimizedExtension: ".jpg",
                targetWidth,
                targetHeight,
                fileInfo.Length,
                fileInfo.Name);
        }
        catch
        {
            return original;
        }
    }

    private static bool IsOptimizableImage(string extension, string mimeType)
    {
        var ext = extension.TrimStart('.').ToLowerInvariant();
        return mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ||
               ext is "png" or "jpg" or "jpeg" or "bmp" or "gif";
    }

    private static BitmapSource LoadBitmap(string path)
    {
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        frame.Freeze();
        return frame;
    }

    private static BitmapSource ResizeToWhiteBackground(BitmapSource source, int width, int height)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
            context.DrawImage(source, new Rect(0, 0, width, height));
        }

        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();
        return target;
    }

    private static byte[] EncodeJpeg(BitmapSource source)
    {
        var encoder = new JpegBitmapEncoder { QualityLevel = JpegQuality };
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}
