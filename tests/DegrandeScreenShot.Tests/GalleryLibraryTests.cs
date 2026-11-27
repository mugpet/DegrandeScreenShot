using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DegrandeScreenShot.App.Services;

namespace DegrandeScreenShot.Tests;

public sealed class GalleryLibraryTests : IDisposable
{
    private readonly ScreenshotLibraryService _library = new(Path.Combine(
        Path.GetTempPath(), "DegrandeScreenShot.Tests", Guid.NewGuid().ToString("N")));

    [Fact]
    public async Task ConcurrentCapturesAppearOnlyWhenCompleteAndKeepTheirOwnPng()
    {
        var capturedAt = new DateTime(2026, 7, 20, 14, 5, 9);
        using var writesStarted = new CountdownEvent(2);
        using var finishWriting = new ManualResetEventSlim();
        var captures = new[] { EncodePixel(0x20), EncodePixel(0xC0) };
        var saves = captures.Select(png => Task.Factory.StartNew(() =>
        {
            using var source = new PausedPngStream(png, writesStarted, finishWriting);
            source.Position = source.Length;
            return _library.SaveCapture(source, capturedAt);
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();

        bool started;
        IReadOnlyList<ScreenshotLibraryItem> visibleDuringWrite;
        try
        {
            started = writesStarted.Wait(TimeSpan.FromSeconds(10));
            visibleDuringWrite = _library.GetScreenshots();
        }
        finally
        {
            finishWriting.Set();
        }

        var paths = await Task.WhenAll(saves);
        Assert.True(started);
        Assert.Empty(visibleDuringWrite);
        Assert.Equal(new[] { "Screenshot 2026-07-20 14-05-09 (2).png", "Screenshot 2026-07-20 14-05-09.png" },
            paths.Select(Path.GetFileName).Order(StringComparer.Ordinal));
        for (var index = 0; index < captures.Length; index++)
        {
            Assert.Equal(captures[index], File.ReadAllBytes(paths[index]));
        }

        Assert.Equal(2, Directory.GetFiles(_library.RootDirectory).Length);
    }

    [Fact]
    public void TallScreenshotThumbnailKeepsTheVisibleCenterAndBoundsDecodedSize()
    {
        const int width = 600, height = 1800;
        var pixels = new uint[width * height];
        Array.Fill(pixels, 0xFF0000FFu);
        Array.Fill(pixels, 0xFFFF0000u, width * (height / 2 - 200), width * 400);
        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        var path = _library.SaveCapture(image);

        var thumbnail = ScreenshotLibraryService.LoadGalleryThumbnail(path);

        Assert.True(thumbnail.IsFrozen);
        Assert.Equal(480, thumbnail.PixelWidth);
        Assert.InRange(thumbnail.PixelHeight, 311, 313);
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, PixelAt(thumbnail, 0, 0));
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, PixelAt(thumbnail, thumbnail.PixelWidth - 1, thumbnail.PixelHeight - 1));

        var original = ScreenshotLibraryService.LoadImage(path);
        Assert.Equal(width, original.PixelWidth);
        Assert.Equal(height, original.PixelHeight);
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, PixelAt(original, 0, 0));
    }

    private static byte[] EncodePixel(byte color)
    {
        var pixel = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { color, 0, 0, 255 }, 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(pixel));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static byte[] PixelAt(BitmapSource image, int x, int y)
    {
        var pixel = new byte[4];
        var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        converted.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return pixel;
    }

    private sealed class PausedPngStream(byte[] png, CountdownEvent started, ManualResetEventSlim finish) : MemoryStream(png)
    {
        public override void CopyTo(Stream destination, int bufferSize)
        {
            destination.WriteByte(checked((byte)ReadByte()));
            destination.Flush();
            started.Signal();
            if (!finish.Wait(TimeSpan.FromSeconds(15)))
            {
                throw new TimeoutException("The gallery inspection did not finish.");
            }

            base.CopyTo(destination, bufferSize);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_library.RootDirectory))
        {
            Directory.Delete(_library.RootDirectory, recursive: true);
        }
    }
}
