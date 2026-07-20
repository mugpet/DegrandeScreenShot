using System.IO;
using System.Text.RegularExpressions;
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

        var path = CreateCapturePath(capturedAt ?? DateTime.Now);
        SavePng(image, path, createNew: true);
        return path;
    }

    public string CreateCapturePath(DateTime capturedAt)
    {
        Directory.CreateDirectory(RootDirectory);
        var baseName = $"{FileNamePrefix} {capturedAt:yyyy-MM-dd HH-mm-ss}";
        return CreateAvailablePath(RootDirectory, baseName);
    }

    public string CreateEditedPath(string sourcePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        Directory.CreateDirectory(RootDirectory);

        var sourceName = Path.GetFileNameWithoutExtension(sourcePath);
        var baseName = EditedSuffixRegex.Replace(sourceName, string.Empty);
        var firstCandidate = Path.Combine(RootDirectory, $"{baseName} - Edited.png");
        if (!File.Exists(firstCandidate))
        {
            return firstCandidate;
        }

        for (var version = 2; ; version++)
        {
            var candidate = Path.Combine(RootDirectory, $"{baseName} - Edited ({version}).png");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    public IReadOnlyList<ScreenshotLibraryItem> GetScreenshots()
    {
        if (!Directory.Exists(RootDirectory))
        {
            return [];
        }

        try
        {
            return Directory
                .EnumerateFiles(RootDirectory, "*.png", SearchOption.TopDirectoryOnly)
                .Select(path => new FileInfo(path))
                .Where(file => file.Exists)
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

        var effectiveDecodePixelWidth = GetEffectiveDecodePixelWidth(path, decodePixelWidth);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        if (effectiveDecodePixelWidth > 0)
        {
            image.DecodePixelWidth = effectiveDecodePixelWidth;
        }

        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            image.StreamSource = stream;
            image.EndInit();
        }

        image.Freeze();
        return image;
    }

    private static int GetEffectiveDecodePixelWidth(string path, int requestedWidth)
    {
        if (requestedWidth <= 0)
        {
            return 0;
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
        return decoder.Frames[0].PixelWidth > requestedWidth ? requestedWidth : 0;
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

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = new FileStream(
            path,
            createNew ? FileMode.CreateNew : FileMode.Create,
            FileAccess.Write,
            FileShare.Read);
        encoder.Save(stream);
    }

    private static string CreateAvailablePath(string directory, string baseName)
    {
        var firstCandidate = Path.Combine(directory, $"{baseName}.png");
        if (!File.Exists(firstCandidate))
        {
            return firstCandidate;
        }

        for (var suffix = 2; ; suffix++)
        {
            var candidate = Path.Combine(directory, $"{baseName} ({suffix}).png");
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
