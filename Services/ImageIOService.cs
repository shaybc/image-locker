using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImageLocker.Models;

namespace ImageLocker.Services;

/// <summary>
/// Handles PNG loading and saving.
/// This keeps image file access separate from the embedding and extraction logic.
/// </summary>
public class ImageIOService
{

    /// <summary>
    /// Loads a PNG from disk and converts it into the in-memory BGRA32 structure used by the rest of the project.
    /// </summary>
    /// <param name="path">Full path of the PNG file to load.</param>
    /// <returns>A <see cref="PngImageData"/> object containing the loaded image.</returns>
    public PngImageData LoadPng(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path is required.", nameof(path));
        if (!File.Exists(path)) throw new FileNotFoundException("File not found.", path);

        var bitmap = new BitmapImage();
        using (var stream = File.OpenRead(path))
        {
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
        }

        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);

        return new PngImageData(converted.PixelWidth, converted.PixelHeight, pixels);
    }

    /// <summary>
    /// Saves an in-memory BGRA32 image as a PNG file on disk.
    /// </summary>
    /// <param name="image">Image data to save.</param>
    /// <param name="path">Full output path of the PNG file.</param>

    public void SavePng(PngImageData image, string path)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path is required.", nameof(path));

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var bitmap = BitmapSource.Create(
            image.Width,
            image.Height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            image.Pixels,
            image.Stride);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var output = File.Create(path);
        encoder.Save(output);
    }
}
