using DegrandeScreenShot.Core;
using DegrandeScreenShot.App;
using DegrandeScreenShot.App.Services;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DegrandeScreenShot.Tests;

public class SelectionSessionTests
{
    [Fact]
    public void CreatesSquareSelectionWhenShiftConstraintIsApplied()
    {
        var session = new SelectionSession();

        session.BeginSelection(new PixelPoint(10, 10));
        session.UpdateSelection(new PixelPoint(30, 20), true);
        session.Complete();

        Assert.Equal(new PixelRect(10, 10, 20, 20), session.Selection);
    }

    [Fact]
    public void MovesSelectionByArrowNudge()
    {
        var session = new SelectionSession();

        session.BeginSelection(new PixelPoint(25, 40));
        session.UpdateSelection(new PixelPoint(75, 80), false);
        session.Complete();
        session.Nudge(1, -1);

        Assert.Equal(new PixelRect(26, 39, 50, 40), session.Selection);
    }

    [Fact]
    public void MovesExistingSelectionWhenDragStartsInsideSelection()
    {
        var session = new SelectionSession();

        session.BeginSelection(new PixelPoint(10, 10));
        session.UpdateSelection(new PixelPoint(50, 40), false);
        session.Complete();

        var started = session.BeginMove(new PixelPoint(20, 20));
        session.UpdateMove(new PixelPoint(26, 28));
        session.Complete();

        Assert.True(started);
        Assert.Equal(new PixelRect(16, 18, 40, 30), session.Selection);
    }

    [Fact]
    public void SwitchesFromResizingToMovingAndBackDuringSameDrag()
    {
        var session = new SelectionSession();

        session.BeginSelection(new PixelPoint(10, 10));
        session.UpdateSelection(new PixelPoint(50, 40), false);

        var moveStarted = session.TryToggleMoveWhileDragging(true, new PixelPoint(20, 20));
        session.UpdateMove(new PixelPoint(25, 25));
        var resizeStarted = session.TryToggleMoveWhileDragging(false, new PixelPoint(60, 55));
        session.UpdateSelection(new PixelPoint(60, 55), false);
        session.Complete();

        Assert.True(moveStarted);
        Assert.True(resizeStarted);
        Assert.Equal(new PixelRect(15, 15, 45, 40), session.Selection);
    }

    [Fact]
    public void StartsMoveImmediatelyEvenWhenPointerHasDraggedPastSelectionBounds()
    {
        var session = new SelectionSession();

        session.BeginSelection(new PixelPoint(60, 60));
        session.UpdateSelection(new PixelPoint(20, 20), false);

        var moveStarted = session.TryToggleMoveWhileDragging(true, new PixelPoint(10, 10));
        session.UpdateMove(new PixelPoint(0, 0));
        session.Complete();

        Assert.True(moveStarted);
        Assert.Equal(new PixelRect(10, 10, 40, 40), session.Selection);
    }
}

public class ScreenshotLibraryServiceTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(
        Path.GetTempPath(),
        "DegrandeScreenShot.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void UsesConventionalTimestampedCaptureNamesAndAvoidsCollisions()
    {
        var service = new ScreenshotLibraryService(_testDirectory);
        var capturedAt = new DateTime(2026, 7, 20, 14, 5, 9);

        var firstPath = service.CreateCapturePath(capturedAt);
        File.WriteAllBytes(firstPath, []);
        var secondPath = service.CreateCapturePath(capturedAt);

        Assert.Equal("Screenshot 2026-07-20 14-05-09.png", Path.GetFileName(firstPath));
        Assert.Equal("Screenshot 2026-07-20 14-05-09 (2).png", Path.GetFileName(secondPath));
    }

    [Fact]
    public void CreatesDistinctEditedVersionsWithoutOverwritingTheSource()
    {
        var service = new ScreenshotLibraryService(_testDirectory);
        var sourcePath = Path.Combine(_testDirectory, "Screenshot 2026-07-20 14-05-09.png");

        var firstEditedPath = service.CreateEditedPath(sourcePath);
        File.WriteAllBytes(firstEditedPath, []);
        var secondEditedPath = service.CreateEditedPath(firstEditedPath);

        Assert.Equal("Screenshot 2026-07-20 14-05-09 - Edited.png", Path.GetFileName(firstEditedPath));
        Assert.Equal("Screenshot 2026-07-20 14-05-09 - Edited (2).png", Path.GetFileName(secondEditedPath));
        Assert.NotEqual(sourcePath, firstEditedPath);
    }

    [Fact]
    public void SavedPngCanBeLoadedAsAGalleryThumbnail()
    {
        var service = new ScreenshotLibraryService(_testDirectory);
        var pixels = new byte[]
        {
            0, 0, 255, 255,
            0, 255, 0, 255,
            255, 0, 0, 255,
            255, 255, 255, 255,
        };
        var image = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, pixels, stride: 8);

        var path = service.SaveCapture(image, new DateTime(2026, 7, 20, 15, 10, 0));
        var thumbnail = ScreenshotLibraryService.LoadImage(path, decodePixelWidth: 480);

        Assert.Equal(2, thumbnail.PixelWidth);
        Assert.Equal(2, thumbnail.PixelHeight);
        Assert.True(thumbnail.IsFrozen);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }
}

public class CapturedImageTests
{
    [Fact]
    public void RendersCursorAsASeparateOptionalLayer()
    {
        var basePixels = Enumerable.Repeat((byte)255, 4 * 4 * 4).ToArray();
        var cursorPixels = new byte[]
        {
            0, 0, 255, 255,
            0, 0, 255, 255,
            0, 0, 255, 255,
            0, 0, 255, 255,
        };
        var baseImage = BitmapSource.Create(4, 4, 96, 96, PixelFormats.Bgra32, null, basePixels, stride: 16);
        var cursorImage = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, cursorPixels, stride: 8);
        var capture = new CapturedImage(baseImage, new CapturedCursor(cursorImage, 1, 1));

        var withCursor = capture.Render(showCursor: true);
        var withoutCursor = capture.Render(showCursor: false);
        var renderedPixel = new byte[4];
        withCursor.CopyPixels(new System.Windows.Int32Rect(1, 1, 1, 1), renderedPixel, stride: 4, offset: 0);

        Assert.Equal(new byte[] { 0, 0, 255, 255 }, renderedPixel);
        Assert.Same(baseImage, withoutCursor);
    }
}

public class ImageAnnotationTests
{
    [Fact]
    public void CornerResizePreservesTheImportedImageAspectRatio()
    {
        var image = CreateImage(width: 400, height: 200);
        var annotation = new ImageAnnotation(
            image,
            new Rect(10, 20, 200, 100),
            "test image");

        annotation.MoveHandle("BottomRight", new Point(310, 100), constrainToSquare: false);

        Assert.Equal(300, annotation.Bounds.Width, precision: 3);
        Assert.Equal(150, annotation.Bounds.Height, precision: 3);
        Assert.Equal(2, annotation.Bounds.Width / annotation.Bounds.Height, precision: 3);
    }

    [Fact]
    public void CloneAndResetRetainTheImportedImageAndDefaultSize()
    {
        var image = CreateImage(width: 400, height: 200);
        var annotation = new ImageAnnotation(
            image,
            new Rect(10, 20, 200, 100),
            "test image");
        annotation.MoveHandle("BottomRight", new Point(310, 170), constrainToSquare: false);

        var clone = Assert.IsType<ImageAnnotation>(annotation.Clone());
        clone.ResetScale();

        Assert.Same(image, clone.Image);
        Assert.Equal(annotation.ImportId, clone.ImportId);
        Assert.Equal(200, clone.Bounds.Width, precision: 3);
        Assert.Equal(100, clone.Bounds.Height, precision: 3);
    }

    [Fact]
    public void ResizeScalePreservesAspectRatioAndUpdatesDimensions()
    {
        var annotation = new ImageAnnotation(
            CreateImage(width: 400, height: 200),
            new Rect(10, 20, 200, 100),
            "test image");

        annotation.SetScale(1.5);

        Assert.Equal(1.5, annotation.Scale, precision: 3);
        Assert.Equal(300, annotation.Bounds.Width, precision: 3);
        Assert.Equal(150, annotation.Bounds.Height, precision: 3);
        Assert.Equal(2, annotation.Bounds.Width / annotation.Bounds.Height, precision: 3);
    }

    [Fact]
    public void LockedDimensionEntryUpdatesTheOtherDimension()
    {
        var annotation = new ImageAnnotation(
            CreateImage(width: 400, height: 200),
            new Rect(10, 20, 200, 100),
            "test image");

        annotation.SetWidth(320);

        Assert.True(annotation.IsAspectRatioLocked);
        Assert.Equal(320, annotation.Bounds.Width, precision: 3);
        Assert.Equal(160, annotation.Bounds.Height, precision: 3);

        annotation.SetHeight(90);

        Assert.Equal(180, annotation.Bounds.Width, precision: 3);
        Assert.Equal(90, annotation.Bounds.Height, precision: 3);
    }

    [Fact]
    public void UnlockedDimensionEntryAllowsIndependentWidthAndHeight()
    {
        var annotation = new ImageAnnotation(
            CreateImage(width: 400, height: 200),
            new Rect(10, 20, 200, 100),
            "test image");

        annotation.SetAspectRatioLocked(false);
        annotation.SetWidth(300);
        annotation.SetHeight(240);

        Assert.False(annotation.IsAspectRatioLocked);
        Assert.Equal(300, annotation.Bounds.Width, precision: 3);
        Assert.Equal(240, annotation.Bounds.Height, precision: 3);

        var clone = Assert.IsType<ImageAnnotation>(annotation.Clone());
        clone.SetHeight(180);
        Assert.False(clone.IsAspectRatioLocked);
        Assert.Equal(300, clone.Bounds.Width, precision: 3);
        Assert.Equal(180, clone.Bounds.Height, precision: 3);

        annotation.SetAspectRatioLocked(true);
        annotation.SetWidth(450);

        Assert.True(annotation.IsAspectRatioLocked);
        Assert.Equal(450, annotation.Bounds.Width, precision: 3);
        Assert.Equal(360, annotation.Bounds.Height, precision: 3);
    }

    [Fact]
    public void UnlockedCornerResizeCanChangeAspectRatio()
    {
        var annotation = new ImageAnnotation(
            CreateImage(width: 400, height: 200),
            new Rect(10, 20, 200, 100),
            "test image");
        annotation.SetAspectRatioLocked(false);

        annotation.MoveHandle("BottomRight", new Point(310, 260), constrainToSquare: false);

        Assert.Equal(300, annotation.Bounds.Width, precision: 3);
        Assert.Equal(240, annotation.Bounds.Height, precision: 3);
        Assert.Equal(1.25, annotation.Bounds.Width / annotation.Bounds.Height, precision: 3);
    }

    [Fact]
    public void DocumentResizeScalesImportedImageGeometryAndLockedRatio()
    {
        var annotation = new ImageAnnotation(
            CreateImage(width: 400, height: 200),
            new Rect(10, 20, 200, 100),
            "test image");

        annotation.ScaleDocument(scaleX: 0.5, scaleY: 2);

        Assert.Equal(5, annotation.Bounds.X, precision: 3);
        Assert.Equal(40, annotation.Bounds.Y, precision: 3);
        Assert.Equal(100, annotation.Bounds.Width, precision: 3);
        Assert.Equal(200, annotation.Bounds.Height, precision: 3);

        annotation.SetWidth(150);

        Assert.Equal(150, annotation.Bounds.Width, precision: 3);
        Assert.Equal(300, annotation.Bounds.Height, precision: 3);
    }

    [Fact]
    public void BitmapResizeCreatesExactFrozenPixelDimensions()
    {
        var resized = EditorWindow.ResizeBitmapSource(
            CreateImage(width: 400, height: 200),
            targetWidth: 160,
            targetHeight: 90);

        Assert.Equal(160, resized.PixelWidth);
        Assert.Equal(90, resized.PixelHeight);
        Assert.True(resized.IsFrozen);
    }

    private static BitmapSource CreateImage(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        var image = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            width * 4);
        image.Freeze();
        return image;
    }
}

public class AnnotationRoundnessTests
{
    [Fact]
    public void FrameRoundnessIsClampedAndPreservedByClone()
    {
        var annotation = new RectangleAnnotation
        {
            Bounds = new Rect(10, 20, 200, 100),
        };

        annotation.SetCornerRadius(140);
        var clone = Assert.IsType<RectangleAnnotation>(annotation.Clone());

        Assert.Equal(100, annotation.CornerRadius);
        Assert.Equal(annotation.CornerRadius, clone.CornerRadius);
    }

    [Fact]
    public void TextBoxRoundnessIsClampedAndPreservedByClone()
    {
        var annotation = new TextAnnotation
        {
            Location = new Point(10, 20),
            Text = "Rounded text",
        };

        annotation.SetCornerRadius(-12);
        Assert.Equal(0, annotation.CornerRadius);

        annotation.SetCornerRadius(36);
        var clone = Assert.IsType<TextAnnotation>(annotation.Clone());

        Assert.Equal(36, clone.CornerRadius);
    }

    [Fact]
    public void RoundnessDefaultsPreserveTheExistingFrameAndTextAppearance()
    {
        Assert.Equal(EditorWindow.DefaultFrameCornerRadius, new RectangleAnnotation().CornerRadius);
        Assert.Equal(EditorWindow.DefaultTextCornerRadius, new TextAnnotation().CornerRadius);
        Assert.Equal(EditorWindow.DefaultFrameCornerRadius, EditorPreferences.Default.FrameCornerRadius);
        Assert.Equal(EditorWindow.DefaultTextCornerRadius, EditorPreferences.Default.TextCornerRadius);
    }
}

public class ImageDropDataTests
{
    [Fact]
    public void BrowserDropPrefersTheImageSourceOverTheContainingPage()
    {
        var data = new DataObject();
        data.SetData(DataFormats.UnicodeText, "https://photos.example.test/photo/123");
        data.SetData(
            DataFormats.Html,
            """
            <html><body>
            <a href="https://photos.example.test/photo/123">
                <img src="https://images.example.test/photo.jpg?width=1200&amp;height=800">
            </a>
            </body></html>
            """);

        var imageUrls = EditorWindow.GetDroppedImageUrls(data);

        Assert.Equal(
            [
                "https://images.example.test/photo.jpg?width=1200&height=800",
                "https://photos.example.test/photo/123",
            ],
            imageUrls);
    }

    [Fact]
    public void BrowserVirtualFileContentsAreDecodedBeforeAPrivateImageUrlIsNeeded()
    {
        var pixels = new byte[2 * 2 * 4];
        var source = BitmapSource.Create(
            2,
            2,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            2 * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var encodedImage = new MemoryStream();
        encoder.Save(encodedImage);
        encodedImage.Position = 0;

        var data = new DataObject();
        data.SetData("FileContents", new MemoryStream(encodedImage.ToArray(), writable: false), autoConvert: false);

        var wasDecoded = ImageImportService.TryLoadDroppedImageData(data, out var image);

        Assert.True(wasDecoded);
        Assert.Equal(2, image.PixelWidth);
        Assert.Equal(2, image.PixelHeight);
    }
}

public class ScrollCaptureStitcherTests
{
    [Fact]
    public void FindsDynamicTopAndBottomByIgnoringStaticChrome()
    {
        ulong[] beforeRows = [1, 1, 10, 11, 12, 13, 14, 90, 90];
        ulong[] afterRows = [1, 1, 11, 12, 13, 14, 15, 90, 90];

        var dynamicTop = ScrollCaptureStitcher.FindDynamicTop(beforeRows, afterRows);
        var dynamicBottom = ScrollCaptureStitcher.FindDynamicBottomExclusive(beforeRows, afterRows);

        Assert.Equal(2, dynamicTop);
        Assert.Equal(7, dynamicBottom);
    }

    [Fact]
    public void FindsLargestTailToHeadOverlap()
    {
        ulong[] existingRows = [10, 11, 12, 13, 14, 15];
        ulong[] incomingRows = [13, 14, 15, 16, 17];

        var overlap = ScrollCaptureStitcher.FindVerticalOverlap(existingRows, incomingRows, minOverlapRows: 2);

        Assert.Equal(3, overlap);
    }

    [Fact]
    public void ReturnsZeroWhenNoSufficientOverlapExists()
    {
        ulong[] existingRows = [10, 11, 12, 13];
        ulong[] incomingRows = [98, 99, 100, 101];

        var overlap = ScrollCaptureStitcher.FindVerticalOverlap(existingRows, incomingRows, minOverlapRows: 2);

        Assert.Equal(0, overlap);
    }

    [Fact]
    public void AllowsSmallRowDifferencesWithinOverlap()
    {
        ulong[] existingRows = [10, 11, 12, 13, 14, 15, 16, 17, 18, 19];
        ulong[] incomingRows = [12, 999, 14, 15, 16, 17, 18, 19, 20, 21];

        var overlap = ScrollCaptureStitcher.FindVerticalOverlap(existingRows, incomingRows, minOverlapRows: 4);

        Assert.Equal(8, overlap);
    }

    [Fact]
    public void FindsOverlapAfterStickyRowsAtIncomingTop()
    {
        ulong[] existingRows = [100, 101, 102, 103, 104, 105, 106, 107];
        ulong[] incomingRows = [900, 901, 104, 105, 106, 107, 108, 109];

        var match = ScrollCaptureStitcher.FindBestVerticalOverlap(existingRows, incomingRows, minOverlapRows: 2, maxIncomingOffsetRows: 3);

        Assert.Equal(4, match.OverlapRows);
        Assert.Equal(2, match.IncomingOffsetRows);
        Assert.Equal(6, match.AppendStartRow);
        Assert.Equal(1.0, match.MatchRatio);
    }

    [Fact]
    public void AllowsBrowserSeamWithSmallRowDifferences()
    {
        var existingRows = Enumerable.Range(0, 180).Select(index => (ulong)(1000 + index)).ToArray();
        var incomingRows = new ulong[180];

        Array.Copy(existingRows, 68, incomingRows, 0, 112);
        for (var index = 0; index < 112; index += 6)
        {
            incomingRows[index] = (ulong)(9000 + index);
        }

        var overlap = ScrollCaptureStitcher.FindVerticalOverlap(existingRows, incomingRows, minOverlapRows: 49);

        Assert.Equal(112, overlap);
    }

    [Fact]
    public void AllowsLargeBrowserSeamWithRepeatedVisualDifferences()
    {
        var existingRows = Enumerable.Range(0, 1300).Select(index => (ulong)(10000 + index)).ToArray();
        var incomingRows = new ulong[1300];

        Array.Copy(existingRows, 299, incomingRows, 0, 1001);
        for (var index = 0; index < 1001; index += 5)
        {
            incomingRows[index] = (ulong)(50000 + index);
        }

        var overlap = ScrollCaptureStitcher.FindVerticalOverlap(existingRows, incomingRows, minOverlapRows: 61);

        Assert.Equal(1001, overlap);
    }

    [Fact]
    public void AllowsVeryLargeBrowserSeamWithHeavyRerendering()
    {
        var existingRows = Enumerable.Range(0, 1300).Select(index => (ulong)(20000 + index)).ToArray();
        var incomingRows = new ulong[1300];

        Array.Copy(existingRows, 299, incomingRows, 0, 1001);
        for (var index = 0; index < 462; index++)
        {
            incomingRows[index] = (ulong)(70000 + index);
        }

        var overlap = ScrollCaptureStitcher.FindVerticalOverlap(existingRows, incomingRows, minOverlapRows: 61);

        Assert.Equal(1001, overlap);
    }

    [Fact]
    public void FindsOverlapForListScrollsThatKeepAboutOneThirdOfTheViewport()
    {
        var existingRows = Enumerable.Range(0, 900).Select(index => (ulong)(30000 + index)).ToArray();
        var incomingRows = new ulong[900];

        Array.Copy(existingRows, 594, incomingRows, 0, 306);
        for (var index = 306; index < incomingRows.Length; index++)
        {
            incomingRows[index] = (ulong)(60000 + index);
        }

        var overlap = ScrollCaptureStitcher.FindVerticalOverlap(existingRows, incomingRows, minOverlapRows: 45);

        Assert.Equal(306, overlap);
    }
}

public class UpdateServiceTests
{
    [Theory]
    [InlineData("v0.2.17", "0.2.16.0", true)]
    [InlineData("v0.2.17", "0.2.17.0", false)]
    [InlineData("0.2.18", "0.2.17.5", true)]
    [InlineData("v0.2.16", "0.2.17.0", false)]
    [InlineData("invalid", "0.2.16.0", false)]
    [InlineData("", "0.2.16.0", false)]
    [InlineData("  v0.2.19  ", "0.2.18.0", true)]
    public void IsUpdateAvailableCorrectlyComparesVersions(string tag, string currentStr, bool expected)
    {
        var current = Version.Parse(currentStr);
        var actual = UpdateService.IsUpdateAvailable(tag, current, out var parsedVersion);
        
        Assert.Equal(expected, actual);
        if (actual)
        {
            Assert.NotNull(parsedVersion);
        }
    }

    [Fact]
    public void FindInstallerAssetPrioritizesSetupExe()
    {
        var release = new GitHubRelease(
            TagName: "v0.2.17",
            Name: "v0.2.17",
            Body: "Notes",
            Assets: new List<GitHubAsset>
            {
                new("DegrandeScreenShot-Setup-0.2.17.exe", "https://example.com/setup"),
                new("DegrandeScreenShot-Portable-0.2.17.zip", "https://example.com/zip"),
                new("some-other-file.exe", "https://example.com/other")
            }
        );

        var asset = UpdateService.FindInstallerAsset(release);
        Assert.NotNull(asset);
        Assert.Equal("DegrandeScreenShot-Setup-0.2.17.exe", asset.Name);
    }

    [Fact]
    public void FindInstallerAssetFallsBackToAnyExe()
    {
        var release = new GitHubRelease(
            TagName: "v0.2.17",
            Name: "v0.2.17",
            Body: "Notes",
            Assets: new List<GitHubAsset>
            {
                new("DegrandeScreenShot-Portable-0.2.17.zip", "https://example.com/zip"),
                new("degrande.exe", "https://example.com/exe")
            }
        );

        var asset = UpdateService.FindInstallerAsset(release);
        Assert.NotNull(asset);
        Assert.Equal("degrande.exe", asset.Name);
    }

    [Fact]
    public void FindInstallerAssetFallsBackToFirstAsset()
    {
        var release = new GitHubRelease(
            TagName: "v0.2.17",
            Name: "v0.2.17",
            Body: "Notes",
            Assets: new List<GitHubAsset>
            {
                new("DegrandeScreenShot-Portable-0.2.17.zip", "https://example.com/zip")
            }
        );

        var asset = UpdateService.FindInstallerAsset(release);
        Assert.NotNull(asset);
        Assert.Equal("DegrandeScreenShot-Portable-0.2.17.zip", asset.Name);
    }
}
