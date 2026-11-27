using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media.Imaging;

namespace DegrandeScreenShot.App.Services;

public sealed class ScreenshotLibraryService
{
    public const string LibraryFolderName = "Degrande Screenshots";
    private const string FileNamePrefix = "Screenshot";
    private static readonly Regex EditedSuffixRegex = new(
        @" - Edited(?: \(\d+\))?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public ScreenshotLibraryService(string? rootDirectory = null)
    {
        RootDirectory = rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            LibraryFolderName);
    }

    public string RootDirectory { get; }

    public string SaveCapture(BitmapSource image, DateTime? capturedAt = null)
    {
        ArgumentNullException.ThrowIfNull(image);

        return SaveNewCapture(CaptureName(capturedAt ?? DateTime.Now), stream => WritePng(image, stream));
    }

    public string SaveCapture(Stream png, DateTime capturedAt)
    {
        ArgumentNullException.ThrowIfNull(png);
        if (png.CanSeek)
        {
            png.Position = 0;
        }

        return SaveNewCapture(CaptureName(capturedAt), png.CopyTo);
    }

    public string SaveEditedCapture(BitmapSource image, string sourcePath)
    {
        ArgumentNullException.ThrowIfNull(image);
        return SaveNewCapture(EditedName(sourcePath), stream => WritePng(image, stream));
    }

    private string SaveNewCapture(string baseName, Action<Stream> write)
    {
        Directory.CreateDirectory(RootDirectory);
        var temporaryPath = Path.Combine(RootDirectory, $"{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                write(stream);
            }

            // Publish only complete PNGs. Two captures can finish within the same second.
            for (var version = 1; ; version++)
            {
                var path = VersionPath(baseName, version);
                try
                {
                    File.Move(temporaryPath, path);
                    return path;
                }
                catch (IOException exception) when ((exception.HResult & 0xFFFF) is 80 or 183)
                {
                    // ERROR_FILE_EXISTS / ERROR_ALREADY_EXISTS
                }
            }
        }
        catch
        {
            File.Delete(temporaryPath);
            throw;
        }
    }

    public string CreateCapturePath(DateTime capturedAt) => CreateAvailablePath(CaptureName(capturedAt));

    public string CreateEditedPath(string sourcePath) => CreateAvailablePath(EditedName(sourcePath));

    private static string CaptureName(DateTime capturedAt) => $"{FileNamePrefix} {capturedAt:yyyy-MM-dd HH-mm-ss}";

    private static string EditedName(string sourcePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        var sourceName = Path.GetFileNameWithoutExtension(sourcePath);
        return EditedSuffixRegex.Replace(sourceName, string.Empty) + " - Edited";
    }

    public IReadOnlyList<ScreenshotLibraryItem> GetScreenshots()
    {
        try
        {
            return new DirectoryInfo(RootDirectory)
                .EnumerateFiles("*.png", SearchOption.TopDirectoryOnly)
                .Select(file => new ScreenshotLibraryItem(
                    file.FullName,
                    file.Name,
                    file.CreationTime,
                    file.LastWriteTime,
                    file.Length))
                .OrderByDescending(item => item.CreatedAt)
                .ThenByDescending(item => item.FileName, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }
        catch (DirectoryNotFoundException)
        {
            return [];
        }
    }

    public static BitmapSource LoadImage(string path, int decodePixelWidth = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using var stream = OpenImageStream(path);
        if (decodePixelWidth > 0)
        {
            var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
            decodePixelWidth = Math.Min(frame.PixelWidth, decodePixelWidth);
            stream.Position = 0;
        }

        return DecodeImage(stream, decodePixelWidth, Int32Rect.Empty);
    }

    internal static BitmapSource LoadGalleryThumbnail(string path)
    {
        // Crop to the visible card before scaling. Tall scrolling captures then cost
        // the same thumbnail memory as ordinary screenshots and keep their detail.
        const double aspectRatio = 240d / 156;
        using var stream = OpenImageStream(path);
        var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
        var width = Math.Max(1, (int)Math.Min(frame.PixelWidth, Math.Round(frame.PixelHeight * aspectRatio)));
        var height = Math.Max(1, (int)Math.Min(frame.PixelHeight, Math.Round(frame.PixelWidth / aspectRatio)));
        var crop = new Int32Rect((frame.PixelWidth - width) / 2, (frame.PixelHeight - height) / 2, width, height);
        stream.Position = 0;
        return DecodeImage(stream, Math.Min(width, 480), crop);
    }

    private static FileStream OpenImageStream(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    private static BitmapSource DecodeImage(Stream stream, int decodePixelWidth, Int32Rect crop)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.SourceRect = crop;
        if (decodePixelWidth > 0)
        {
            image.DecodePixelWidth = decodePixelWidth;
        }

        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    public static void SavePng(BitmapSource image, string path, bool createNew = false)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var stream = new FileStream(
            path,
            createNew ? FileMode.CreateNew : FileMode.Create,
            FileAccess.Write,
            FileShare.Read);
        WritePng(image, stream);
    }

    private static void WritePng(BitmapSource image, Stream stream)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        encoder.Save(stream);
    }

    private string VersionPath(string baseName, int version) =>
        Path.Combine(RootDirectory, version == 1 ? $"{baseName}.png" : $"{baseName} ({version}).png");

    private string CreateAvailablePath(string baseName)
    {
        Directory.CreateDirectory(RootDirectory);
        for (var version = 1; ; version++)
        {
            var candidate = VersionPath(baseName, version);
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }
    }
}

public sealed record ScreenshotLibraryItem(
    string Path,
    string FileName,
    DateTime CreatedAt,
    DateTime ModifiedAt,
    long FileSize);
