using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using DegrandeScreenShot.Core;

namespace DegrandeScreenShot.App.Services;

public sealed class ScreenCaptureService
{
    private const int MaximumScrollFrames = 400;
    private const int MinimumOverlapRows = 12;
    private const int FocusPointVerticalPercent = 65;
    private const int WheelDelta = 120;
    private const int WheelScrollDownNotches = 2;
    private const int WheelScrollToTopNotches = 8;
    private const int MaximumScrollToTopAttempts = 80;
    private const int CursorScrollBarEdgePadding = 6;
    private const int HorizontalAnalysisBandPercent = 30;
    private const int MinimumAnalysisWidth = 96;
    private const int RowSignatureBuckets = 16;
    private const int RowProfileBuckets = 64;
    private const int RowProfileSimilarityThreshold = 512;
    private const double VerticalShiftAcceptanceRatio = 0.6;
    private const double MaximumAcceptableShiftSad = 1500;
    private const double RequiredProfileSmallOverlapRatio = 0.65;
    private const double RequiredProfileLargeOverlapRatio = 0.55;
    private const double RequiredProfileVeryLargeOverlapRatio = 0.40;
    private const int ProfileLargeOverlapRowThreshold = 200;
    private const int ProfileVeryLargeOverlapRowThreshold = 800;
    private const int MaximumIncomingOffsetRows = 160;
    private const int ScrollIndicatorWidth = 24;
    private const int ScrollIndicatorChangedRowsThreshold = 4;
    private const int MinimumContentScrollOverlapPercent = 8;
    private const int EstimatedPageDownOverlapPercent = 58;
    private const int MinimumEstimatedPageDownOverlapRows = 60;
    private static readonly TimeSpan FocusDelay = TimeSpan.FromMilliseconds(150);
    private const int ScrollSettleInitialMs = 120;
    private const int ScrollSettlePollMs = 60;
    private const int ScrollSettleMaxPolls = 18;

    public CaptureFrame CaptureVirtualDesktop()
    {
        var desktopBounds = GetVirtualDesktopBounds();
        using var bitmap = CaptureScreenRectangle(desktopBounds);

        return new CaptureFrame(
            desktopBounds.Left,
            desktopBounds.Top,
            ConvertToBitmapSource(bitmap),
            CaptureCursor(desktopBounds));
    }

    public CapturedImage CaptureVisibleWindow(IntPtr windowHandle, CancellationToken cancellationToken = default)
    {
        if (windowHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException("No window was selected.");
        }

        var previousForegroundWindow = GetForegroundWindow();
        var zOrderState = CaptureWindowZOrderState(windowHandle);

        try
        {
            ShowWindow(windowHandle, ShowWindowRestore);
            SetWindowPos(windowHandle, HwndTopMost, 0, 0, 0, 0, SetWindowPosNoMove | SetWindowPosNoSize);
            SetForegroundWindow(windowHandle);
            SleepWithCancellation((int)FocusDelay.TotalMilliseconds, cancellationToken);

            var windowBounds = GetWindowBounds(windowHandle);
            EnsureWindowIsVisibleOnDesktop(windowBounds);
            using var bitmap = CaptureScreenRectangle(windowBounds);
            return new CapturedImage(ConvertToBitmapSource(bitmap), CaptureCursor(windowBounds));
        }
        finally
        {
            RestoreWindowZOrder(windowHandle, zOrderState);
            if (previousForegroundWindow != IntPtr.Zero && previousForegroundWindow != windowHandle)
            {
                SetForegroundWindow(previousForegroundWindow);
            }
        }
    }

    public CapturedImage CaptureVisibleWindowRegion(IntPtr windowHandle, Rectangle captureBounds, CancellationToken cancellationToken = default)
    {
        if (windowHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException("No window was selected.");
        }

        if (captureBounds.Width <= 0 || captureBounds.Height <= 0)
        {
            throw new InvalidOperationException("The selected element is not visible.");
        }

        var previousForegroundWindow = GetForegroundWindow();
        var zOrderState = CaptureWindowZOrderState(windowHandle);

        try
        {
            ShowWindow(windowHandle, ShowWindowRestore);
            SetWindowPos(windowHandle, HwndTopMost, 0, 0, 0, 0, SetWindowPosNoMove | SetWindowPosNoSize);
            SetForegroundWindow(windowHandle);
            SleepWithCancellation((int)FocusDelay.TotalMilliseconds, cancellationToken);

            using var bitmap = CaptureScreenRectangle(captureBounds);
            return new CapturedImage(ConvertToBitmapSource(bitmap), CaptureCursor(captureBounds));
        }
        finally
        {
            RestoreWindowZOrder(windowHandle, zOrderState);
            if (previousForegroundWindow != IntPtr.Zero && previousForegroundWindow != windowHandle)
            {
                SetForegroundWindow(previousForegroundWindow);
            }
        }
    }

    public CapturedImage CaptureScreenRegion(Rectangle bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bounds), "The capture area must have a positive size.");
        }

        using var bitmap = CaptureScreenRectangle(bounds);
        return new CapturedImage(ConvertToBitmapSource(bitmap), CaptureCursor(bounds));
    }

    public BitmapSource CaptureScrollingWindow(IntPtr windowHandle)
    {
        return CaptureScrollingWindowCore(windowHandle, null, CancellationToken.None);
    }

    public BitmapSource CaptureScrollingWindow(IntPtr windowHandle, Rectangle captureBounds, CancellationToken cancellationToken = default)
    {
        return CaptureScrollingWindowCore(windowHandle, captureBounds, cancellationToken);
    }

    private static BitmapSource CaptureScrollingWindowCore(IntPtr windowHandle, Rectangle? captureBounds, CancellationToken cancellationToken)
    {
        if (windowHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException("No window was selected.");
        }

        ThrowIfCaptureCanceled(cancellationToken);
        var zOrderState = CaptureWindowZOrderState(windowHandle);
        NativePoint? originalCursor = null;
        try
        {
            RestoreAndActivateWindow(windowHandle, cancellationToken);
            var windowBounds = GetWindowBounds(windowHandle);
            EnsureWindowIsVisibleOnDesktop(windowBounds);
            var scrollCaptureBounds = Rectangle.Intersect(windowBounds, captureBounds ?? windowBounds);
            if (scrollCaptureBounds.Width <= 0 || scrollCaptureBounds.Height <= 0)
            {
                throw new InvalidOperationException("The selected scroll area is not visible inside the selected window.");
            }

            SetWindowTopMost(windowHandle, true);
            originalCursor = GetCursorPosition();
            PositionCursorOnScrollBarEdge(scrollCaptureBounds, cancellationToken);
            ScrollToTop(scrollCaptureBounds, cancellationToken);
            using var topFrame = CaptureScreenRectangleAtScrollBarEdge(scrollCaptureBounds, cancellationToken);

            try
            {
                using var secondFrame = CaptureNextScrolledFrame(windowHandle, scrollCaptureBounds, topFrame, cancellationToken);
                if (BitmapsEqual(topFrame, secondFrame))
                {
                    throw new InvalidOperationException("The selected window did not scroll. Try selecting a scrollable browser content area.");
                }

                var viewport = DetermineScrollableViewport(topFrame, secondFrame);
                return StitchScrollingFrames(windowHandle, scrollCaptureBounds, viewport, topFrame, secondFrame, cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return ConvertToBitmapSource(topFrame);
            }
        }
        finally
        {
            if (originalCursor is { } cursor)
            {
                RestoreCursor(cursor);
            }
            RestoreWindowZOrder(windowHandle, zOrderState);
        }
    }

    private static BitmapSource ConvertToBitmapSource(Bitmap bitmap)
    {
        // The composited desktop is opaque. Copy its native BGR pixels directly at
        // capture resolution, without creating another full-size GDI bitmap handle.
        var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
        try
        {
            var image = BitmapSource.Create(bitmap.Width, bitmap.Height, 96, 96,
                System.Windows.Media.PixelFormats.Bgr32, null, data.Scan0, checked(data.Stride * bitmap.Height), data.Stride);
            image.Freeze();
            return image;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static CapturedCursor? CaptureCursor(Rectangle captureBounds)
    {
        var cursorInfo = new CursorInfo
        {
            Size = Marshal.SizeOf<CursorInfo>(),
        };

        if (!GetCursorInfo(ref cursorInfo)
            || (cursorInfo.Flags & CursorShowing) == 0
            || cursorInfo.Handle == IntPtr.Zero)
        {
            return null;
        }

        var cursorHandle = CopyIcon(cursorInfo.Handle);
        if (cursorHandle == IntPtr.Zero)
        {
            return null;
        }

        IconInfo iconInfo = default;
        try
        {
            if (!GetIconInfo(cursorHandle, out iconInfo))
            {
                return null;
            }

            var image = Imaging.CreateBitmapSourceFromHIcon(
                cursorHandle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            image.Freeze();

            var left = cursorInfo.Position.X - (int)iconInfo.HotspotX - captureBounds.Left;
            var top = cursorInfo.Position.Y - (int)iconInfo.HotspotY - captureBounds.Top;
            var cursorBounds = new Rectangle(left, top, image.PixelWidth, image.PixelHeight);
            if (!cursorBounds.IntersectsWith(new Rectangle(0, 0, captureBounds.Width, captureBounds.Height)))
            {
                return null;
            }

            return new CapturedCursor(image, left, top);
        }
        finally
        {
            if (iconInfo.ColorBitmap != IntPtr.Zero)
            {
                DeleteObject(iconInfo.ColorBitmap);
            }

            if (iconInfo.MaskBitmap != IntPtr.Zero)
            {
                DeleteObject(iconInfo.MaskBitmap);
            }

            DestroyIcon(cursorHandle);
        }
    }

    private static BitmapSource StitchScrollingFrames(IntPtr windowHandle, Rectangle windowBounds, Rectangle viewport, Bitmap topFrame, Bitmap secondFrame, CancellationToken cancellationToken)
    {
        var analysisBounds = GetRowAnalysisBounds(viewport.Width, viewport.Height);
        analysisBounds.Offset(viewport.Location);
        var previousProfiles = ComputeRowProfiles(topFrame, analysisBounds);
        var currentFullFrame = secondFrame;
        var strips = new List<Bitmap>();
        var lastAppendStartRow = EstimatePageDownAppendStartRow(viewport.Height);
        var diagnosticTrace = new System.Text.StringBuilder();
        diagnosticTrace.AppendLine($"viewport={viewport} analysisBounds={analysisBounds}");

        try
        {
            strips.Add(CopyRegion(topFrame, new Rectangle(0, 0, topFrame.Width, viewport.Bottom)));
            try
            {
                for (var frameIndex = 0; frameIndex < MaximumScrollFrames; frameIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var currentProfiles = ComputeRowProfiles(currentFullFrame, analysisBounds);
                    if (RowProfilesIdentical(previousProfiles, currentProfiles))
                    {
                        diagnosticTrace.AppendLine($"frame {frameIndex}: identical profiles, breaking");
                        break;
                    }

                    var shift = FindBestVerticalShift(previousProfiles, currentProfiles, out var bestSad, out var baselineSad);
                    diagnosticTrace.AppendLine($"frame {frameIndex}: shift={shift} bestSad={bestSad:F1} baselineSad={baselineSad:F1}");
                    var appendStartRow = shift > 0 ? viewport.Height - shift : lastAppendStartRow;
                    if (appendStartRow <= 0 || appendStartRow >= viewport.Height)
                    {
                        DumpScrollDiagnostics(topFrame, secondFrame, viewport, previousProfiles, currentProfiles, diagnosticTrace.ToString());
                        throw new InvalidOperationException("Could not match consecutive scroll captures. Try selecting a browser window with visible page content.");
                    }

                    var appendBounds = new Rectangle(viewport.Left, viewport.Top + appendStartRow, viewport.Width, viewport.Height - appendStartRow);
                    strips.Add(CopyRegion(currentFullFrame, appendBounds));
                    lastAppendStartRow = appendStartRow;
                    previousProfiles = currentProfiles;
                    ThrowIfCaptureCanceled(cancellationToken);

                    var nextFrame = CaptureNextScrolledFrame(windowHandle, windowBounds, currentFullFrame, cancellationToken);
                    var previousFrame = currentFullFrame;
                    currentFullFrame = nextFrame;
                    try
                    {
                        if (!ScrollPositionChanged(previousFrame, nextFrame))
                        {
                            diagnosticTrace.AppendLine($"frame {frameIndex}: no further scroll, breaking");
                            break;
                        }
                    }
                    finally
                    {
                        if (!ReferenceEquals(previousFrame, secondFrame))
                        {
                            previousFrame.Dispose();
                        }
                    }
                }

                DumpScrollDiagnostics(topFrame, secondFrame, viewport, previousProfiles, previousProfiles, diagnosticTrace.ToString());
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Escape keeps the part that has already been captured.
            }

            cancellationToken.ThrowIfCancellationRequested();
            return CombineStrips(strips);
        }
        finally
        {
            if (!ReferenceEquals(currentFullFrame, secondFrame))
            {
                currentFullFrame.Dispose();
            }
            foreach (var strip in strips)
            {
                strip.Dispose();
            }
        }
    }

    private static Rectangle DetermineScrollableViewport(Bitmap topFrame, Bitmap secondFrame)
    {
        var analysisBounds = GetRowAnalysisBounds(topFrame.Width, topFrame.Height);
        var topRows = ComputeRowHashes(topFrame, analysisBounds);
        var secondRows = ComputeRowHashes(secondFrame, analysisBounds);
        var dynamicTop = ScrollCaptureStitcher.FindDynamicTop(topRows, secondRows);
        var dynamicBottom = ScrollCaptureStitcher.FindDynamicBottomExclusive(topRows, secondRows);
        var dynamicHeight = dynamicBottom - dynamicTop;
        if (dynamicHeight < Math.Max(64, topFrame.Height / 5))
        {
            return new Rectangle(0, 0, topFrame.Width, topFrame.Height);
        }

        return new Rectangle(0, dynamicTop, topFrame.Width, dynamicHeight);
    }

    private static ulong[] ComputeRowHashes(Bitmap bitmap, Rectangle analysisBounds)
    {
        var hashes = new ulong[analysisBounds.Height];
        var rowPixels = new byte[analysisBounds.Width * 4];
        var bucketEnds = GetBucketEnds(analysisBounds.Width, RowSignatureBuckets);
        Span<byte> profile = stackalloc byte[RowSignatureBuckets];
        var data = bitmap.LockBits(analysisBounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
        try
        {
            for (var row = 0; row < analysisBounds.Height; row++)
            {
                ReadRowProfile(data, row, rowPixels, bucketEnds, profile);
                ulong signature = 0;
                for (var bucket = 0; bucket < profile.Length; bucket++)
                {
                    signature |= (ulong)(profile[bucket] / 16) << (bucket * 4);
                }
                hashes[row] = signature;
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return hashes;
    }

    private static Rectangle GetRowAnalysisBounds(int width, int height)
    {
        var analysisWidth = Math.Max(MinimumAnalysisWidth, width * HorizontalAnalysisBandPercent / 100);
        if (analysisWidth >= width)
        {
            return new Rectangle(0, 0, width, height);
        }

        var analysisLeft = (width - analysisWidth) / 2;
        return new Rectangle(analysisLeft, 0, analysisWidth, height);
    }

    private static byte[][] ComputeRowProfiles(Bitmap bitmap, Rectangle analysisBounds)
    {
        var profiles = new byte[analysisBounds.Height][];
        var rowPixels = new byte[analysisBounds.Width * 4];
        var bucketEnds = GetBucketEnds(analysisBounds.Width, RowProfileBuckets);
        var data = bitmap.LockBits(analysisBounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
        try
        {
            for (var row = 0; row < analysisBounds.Height; row++)
            {
                profiles[row] = new byte[RowProfileBuckets];
                ReadRowProfile(data, row, rowPixels, bucketEnds, profiles[row]);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return profiles;
    }

    private static int[] GetBucketEnds(int width, int buckets)
    {
        var ends = new int[buckets];
        for (var index = 0; index < buckets; index++)
        {
            ends[index] = ((index + 1) * width + buckets - 1) / buckets;
        }
        return ends;
    }

    private static void ReadRowProfile(BitmapData data, int row, byte[] pixels, int[] bucketEnds, Span<byte> profile)
    {
        Marshal.Copy(data.Scan0 + row * data.Stride, pixels, 0, pixels.Length);
        var start = 0;
        for (var bucket = 0; bucket < bucketEnds.Length; bucket++)
        {
            var end = bucketEnds[bucket];
            var luminance = 0;
            for (var offset = start * 4; offset < end * 4; offset += 4)
            {
                luminance += (pixels[offset + 2] * 77 + pixels[offset + 1] * 150 + pixels[offset] * 29) >> 8;
            }

            profile[bucket] = end == start ? (byte)255 : (byte)(luminance / (end - start));
            start = end;
        }
    }

    private static int RowProfileSad(byte[] left, byte[] right)
    {
        if (Sse2.IsSupported && left.Length == RowProfileBuckets && right.Length == RowProfileBuckets)
        {
            var total = Vector128<ushort>.Zero;
            for (var offset = 0; offset < RowProfileBuckets; offset += Vector128<byte>.Count)
            {
                var a = Vector128.LoadUnsafe(ref left[offset]);
                var b = Vector128.LoadUnsafe(ref right[offset]);
                total = Sse2.Add(total, Sse2.SumAbsoluteDifferences(a, b));
            }

            return total.GetElement(0) + total.GetElement(4);
        }

        var sum = 0;
        for (var i = 0; i < left.Length; i++)
        {
            sum += Math.Abs(left[i] - right[i]);
        }
        return sum;
    }

    private static bool RowProfilesIdentical(byte[][] left, byte[][] right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }
        for (var i = 0; i < left.Length; i++)
        {
            for (var b = 0; b < left[i].Length; b++)
            {
                if (left[i][b] != right[i][b])
                {
                    return false;
                }
            }
        }
        return true;
    }

    private static VerticalOverlapMatch FindBestVerticalOverlapBySimilarity(byte[][] previousProfiles, byte[][] currentProfiles, int minOverlapRows, int maxIncomingOffsetRows)
    {
        var best = VerticalOverlapMatch.None;
        var maxOffset = Math.Min(maxIncomingOffsetRows, Math.Max(0, currentProfiles.Length - minOverlapRows));
        for (var incomingOffset = 0; incomingOffset <= maxOffset; incomingOffset++)
        {
            var available = currentProfiles.Length - incomingOffset;
            var maxOverlap = Math.Min(previousProfiles.Length, available);
            for (var overlap = maxOverlap; overlap >= minOverlapRows; overlap--)
            {
                var startIndex = previousProfiles.Length - overlap;
                var similar = 0;
                for (var offset = 0; offset < overlap; offset++)
                {
                    if (RowProfileSad(previousProfiles[startIndex + offset], currentProfiles[incomingOffset + offset]) <= RowProfileSimilarityThreshold)
                    {
                        similar++;
                    }
                }

                var requiredRatio = overlap switch
                {
                    >= ProfileVeryLargeOverlapRowThreshold => RequiredProfileVeryLargeOverlapRatio,
                    >= ProfileLargeOverlapRowThreshold => RequiredProfileLargeOverlapRatio,
                    _ => RequiredProfileSmallOverlapRatio,
                };
                if (similar >= Math.Ceiling(overlap * requiredRatio))
                {
                    if (overlap > best.OverlapRows)
                    {
                        best = new VerticalOverlapMatch(overlap, incomingOffset, (double)similar / overlap);
                    }
                    break;
                }
            }
        }

        return best;
    }

    /// <summary>
    /// Estimates the vertical pixel shift between two consecutive viewports by minimizing
    /// average sum-of-absolute-differences across all aligned row profiles. Returns 0 when
    /// the best shift is not significantly better than the no-shift baseline.
    /// </summary>
    internal static int FindBestVerticalShift(byte[][] previousProfiles, byte[][] currentProfiles, out double bestSad, out double baselineSad)
    {
        var height = Math.Min(previousProfiles.Length, currentProfiles.Length);
        baselineSad = AverageProfileSad(previousProfiles, currentProfiles, 0, height);
        bestSad = baselineSad;

        var minShift = Math.Max(MinimumOverlapRows, height / 50);
        var maxShift = height - MinimumOverlapRows;
        if (maxShift <= minShift)
        {
            return 0;
        }

        var bestShift = 0;
        for (var shift = minShift; shift <= maxShift; shift++)
        {
            var overlap = height - shift;
            var sad = AverageProfileSad(previousProfiles, currentProfiles, shift, overlap, bestSad);
            if (sad < bestSad)
            {
                bestSad = sad;
                bestShift = shift;
                if (bestSad == 0)
                {
                    break;
                }
            }
        }

        // Require the best alignment to be meaningfully better than the unshifted baseline,
        // otherwise the page likely did not scroll or the analysis band is mostly background.
        if (bestShift == 0 || bestSad > baselineSad * VerticalShiftAcceptanceRatio || bestSad > MaximumAcceptableShiftSad)
        {
            return 0;
        }

        return bestShift;
    }

    private static double AverageProfileSad(byte[][] previousProfiles, byte[][] currentProfiles, int shift, int overlap, double upperBound = double.PositiveInfinity)
    {
        if (overlap <= 0)
        {
            return double.MaxValue;
        }

        var maximumTotal = Math.Ceiling(upperBound * overlap);
        long total = 0;
        for (var i = 0; i < overlap; i++)
        {
            total += RowProfileSad(previousProfiles[shift + i], currentProfiles[i]);
            // SADs are nonnegative. An unfinished candidate already at the best
            // total cannot improve the alignment, even if every remaining row matches.
            if (total >= maximumTotal)
            {
                return upperBound;
            }
        }
        return (double)total / overlap;
    }

    private static void DumpScrollDiagnostics(Bitmap topFrame, Bitmap secondFrame, Rectangle viewport, byte[][] previousProfiles, byte[][] currentProfiles, string trace)
    {
        try
        {
            var dir = Path.GetTempPath();
            topFrame.Save(Path.Combine(dir, "dss_frame0.png"), ImageFormat.Png);
            secondFrame.Save(Path.Combine(dir, "dss_frame1.png"), ImageFormat.Png);
            using var vp0 = CopyRegion(topFrame, viewport);
            using var vp1 = CopyRegion(secondFrame, viewport);
            vp0.Save(Path.Combine(dir, "dss_vp0.png"), ImageFormat.Png);
            vp1.Save(Path.Combine(dir, "dss_vp1.png"), ImageFormat.Png);

            var height = Math.Min(previousProfiles.Length, currentProfiles.Length);
            var baselineSad = AverageProfileSad(previousProfiles, currentProfiles, 0, height);
            var minShift = Math.Max(MinimumOverlapRows, height / 50);
            var maxShift = height - MinimumOverlapRows;
            var bestSad = baselineSad;
            var bestShift = 0;
            var samples = new System.Text.StringBuilder();
            for (var shift = minShift; shift <= maxShift; shift++)
            {
                var overlap = height - shift;
                var sad = AverageProfileSad(previousProfiles, currentProfiles, shift, overlap);
                if (sad < bestSad)
                {
                    bestSad = sad;
                    bestShift = shift;
                }
                if (shift % 50 == 0)
                {
                    samples.AppendLine($"  shift={shift} sad={sad:F1}");
                }
            }

            var log = $"""
                dss_scroll_diagnostics
                viewport={viewport}
                topFrame={topFrame.Width}x{topFrame.Height}
                secondFrame={secondFrame.Width}x{secondFrame.Height}
                vpWidth={vp0.Width} vpHeight={vp0.Height}
                analysisBounds={GetRowAnalysisBounds(vp0.Width, vp0.Height)}
                profilesLen={previousProfiles.Length}
                profileBuckets={RowProfileBuckets}
                baselineSad(noShift)={baselineSad:F1}
                bestShift={bestShift} bestSad={bestSad:F1} ratio={(baselineSad > 0 ? bestSad / baselineSad : 0):F3}
                acceptanceRatio={VerticalShiftAcceptanceRatio} maxAcceptableSad={MaximumAcceptableShiftSad}
                ---trace---
                {trace}
                ---samples---
                {samples}
                """;
            File.WriteAllText(Path.Combine(dir, "dss_log.txt"), log);
        }
        catch
        {
            // diagnostics must never crash the app
        }
    }

    private static Bitmap CaptureScreenRectangle(Rectangle bounds)
    {
        var bitmap = new System.Drawing.Bitmap(bounds.Width, bounds.Height, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
        return bitmap;
    }

    private static Bitmap CopyRegion(Bitmap source, Rectangle region)
    {
        return source.Clone(region, PixelFormat.Format32bppRgb);
    }

    internal static BitmapSource CombineStrips(IReadOnlyList<Bitmap> strips)
    {
        var result = new WriteableBitmap(strips[0].Width, strips.Sum(strip => strip.Height),
            96, 96, System.Windows.Media.PixelFormats.Bgr32, null);
        var top = 0;
        foreach (var strip in strips)
        {
            var data = strip.LockBits(new Rectangle(0, 0, strip.Width, strip.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
            try
            {
                // Write into the final image in pixel coordinates. GDI+ drawing
                // can clip strips whose destination is past row 32,767.
                result.WritePixels(new Int32Rect(0, top, strip.Width, strip.Height), data.Scan0,
                    checked(data.Stride * strip.Height), data.Stride);
            }
            finally
            {
                strip.UnlockBits(data);
            }
            top += strip.Height;
        }
        result.Freeze();
        return result;
    }

    private static bool BitmapsEqual(Bitmap first, Bitmap second)
    {
        if (first.Width != second.Width || first.Height != second.Height)
        {
            return false;
        }

        var analysisBounds = GetRowAnalysisBounds(first.Width, first.Height);
        var firstProfiles = ComputeRowProfiles(first, analysisBounds);
        var secondProfiles = ComputeRowProfiles(second, analysisBounds);
        return CountSimilarRows(firstProfiles, secondProfiles) >= firstProfiles.Length - 2;
    }

    private static bool ScrollPositionChanged(Bitmap before, Bitmap after)
    {
        if (before.Width != after.Width || before.Height != after.Height)
        {
            return true;
        }

        var analysisBounds = GetRowAnalysisBounds(before.Width, before.Height);
        var beforeProfiles = ComputeRowProfiles(before, analysisBounds);
        var afterProfiles = ComputeRowProfiles(after, analysisBounds);
        var similarRows = CountSimilarRows(beforeProfiles, afterProfiles);
        var threshold = Math.Max(2, beforeProfiles.Length / 50);
        return (beforeProfiles.Length - similarRows) > threshold;
    }

    private static int CountSimilarRows(byte[][] left, byte[][] right)
    {
        var count = 0;
        var rowCount = Math.Min(left.Length, right.Length);
        for (var i = 0; i < rowCount; i++)
        {
            if (RowProfileSad(left[i], right[i]) <= RowProfileSimilarityThreshold)
            {
                count++;
            }
        }
        return count;
    }

    private static Rectangle GetScrollIndicatorBounds(int width, int height)
    {
        var indicatorWidth = Math.Min(ScrollIndicatorWidth, width);
        return new Rectangle(width - indicatorWidth, 0, indicatorWidth, height);
    }

    private static int EstimatePageDownAppendStartRow(int viewportHeight)
    {
        var estimatedOverlap = Math.Max(MinimumEstimatedPageDownOverlapRows, viewportHeight * EstimatedPageDownOverlapPercent / 100);
        return Math.Clamp(estimatedOverlap, MinimumOverlapRows, Math.Max(MinimumOverlapRows, viewportHeight - 1));
    }

    private static int NormalizeAppendStartRow(int appendStartRow, double matchRatio, int viewportHeight)
    {
        var estimatedPageDownAppendStartRow = EstimatePageDownAppendStartRow(viewportHeight);
        if (appendStartRow < estimatedPageDownAppendStartRow && matchRatio < 0.95)
        {
            return estimatedPageDownAppendStartRow;
        }

        return appendStartRow;
    }

    private static Rectangle GetWindowBounds(IntPtr windowHandle)
    {
        if (DwmGetWindowAttribute(windowHandle, DwmWindowAttributeExtendedFrameBounds, out var extendedFrameRect, Marshal.SizeOf<NativeRect>()) == 0)
        {
            var extendedFrameBounds = Rectangle.FromLTRB(
                extendedFrameRect.Left,
                extendedFrameRect.Top,
                extendedFrameRect.Right,
                extendedFrameRect.Bottom);
            if (extendedFrameBounds.Width > 0 && extendedFrameBounds.Height > 0)
            {
                return extendedFrameBounds;
            }
        }

        if (!GetWindowRect(windowHandle, out var rect))
        {
            throw new InvalidOperationException("Could not read the selected window bounds.");
        }

        return Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
    }

    private static void EnsureWindowIsVisibleOnDesktop(Rectangle bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            throw new InvalidOperationException("The selected window is not visible.");
        }

        var desktopBounds = GetVirtualDesktopBounds();
        if (!desktopBounds.Contains(bounds))
        {
            throw new InvalidOperationException("The selected window must be fully visible on screen for scrolling capture.");
        }
    }

    private static Rectangle GetVirtualDesktopBounds()
    {
        return new Rectangle(
            GetSystemMetrics(SystemMetricVirtualScreenLeft),
            GetSystemMetrics(SystemMetricVirtualScreenTop),
            GetSystemMetrics(SystemMetricVirtualScreenWidth),
            GetSystemMetrics(SystemMetricVirtualScreenHeight));
    }

    private static void RestoreAndActivateWindow(IntPtr windowHandle, CancellationToken cancellationToken)
    {
        if (GetForegroundWindow() != windowHandle)
        {
            ShowWindow(windowHandle, ShowWindowRestore);
            SetForegroundWindow(windowHandle);
            SleepWithCancellation((int)FocusDelay.TotalMilliseconds, cancellationToken);
        }
    }

    private static void PositionCursorOnScrollBarEdge(Rectangle bounds, CancellationToken cancellationToken)
    {
        var scrollbarOffset = Math.Min(Math.Max(CursorScrollBarEdgePadding, ScrollIndicatorWidth / 2), Math.Max(1, bounds.Width / 3));
        var focusX = Math.Clamp(bounds.Right - scrollbarOffset, bounds.Left, bounds.Right - 1);
        var focusY = Math.Clamp(bounds.Top + (bounds.Height * FocusPointVerticalPercent / 100), bounds.Top, bounds.Bottom - 1);
        if (!GetCursorPos(out var cursor) || cursor.X != focusX || cursor.Y != focusY)
        {
            SetCursorPos(focusX, focusY);
            SleepWithCancellation((int)FocusDelay.TotalMilliseconds, cancellationToken);
        }
    }

    private static NativePoint GetCursorPosition()
    {
        return GetCursorPos(out var point) ? point : new NativePoint();
    }

    private static void RestoreCursor(NativePoint originalCursor)
    {
        ClipCursor(IntPtr.Zero);
        SetCursorPos(originalCursor.X, originalCursor.Y);
    }

    private static void ScrollToTop(Rectangle windowBounds, CancellationToken cancellationToken)
    {
        ThrowIfCaptureCanceled(cancellationToken);
        SendModifiedKey(VirtualKeyControl, VirtualKeyHome);
        using var _ = CaptureScreenRectangleAfterScroll(windowBounds, cancellationToken);
        for (var attempt = 0; attempt < MaximumScrollToTopAttempts; attempt++)
        {
            ThrowIfCaptureCanceled(cancellationToken);
            using var before = CaptureScreenRectangleAtScrollBarEdge(windowBounds, cancellationToken);
            ScrollByWheel(windowBounds, WheelScrollToTopNotches, cancellationToken);
            using var after = CaptureScreenRectangleAfterScroll(windowBounds, cancellationToken);
            if (BitmapsEqual(before, after))
            {
                return;
            }
        }
    }

    private static Bitmap CaptureScreenRectangleAfterScroll(Rectangle bounds, CancellationToken cancellationToken)
    {
        SleepWithCancellation(ScrollSettleInitialMs, cancellationToken);
        var previous = CaptureScreenRectangleAtScrollBarEdge(bounds, cancellationToken);
        try
        {
            for (var poll = 0; poll < ScrollSettleMaxPolls; poll++)
            {
                SleepWithCancellation(ScrollSettlePollMs, cancellationToken);
                var current = CaptureScreenRectangleAtScrollBarEdge(bounds, cancellationToken);
                bool stable;
                try
                {
                    stable = BitmapsEqual(previous, current);
                }
                finally
                {
                    previous.Dispose();
                    previous = current;
                }
                if (stable)
                {
                    break;
                }
            }
            return previous;
        }
        catch
        {
            previous.Dispose();
            throw;
        }
    }

    private static Bitmap CaptureScreenRectangleAtScrollBarEdge(Rectangle bounds, CancellationToken cancellationToken)
    {
        PositionCursorOnScrollBarEdge(bounds, cancellationToken);
        return CaptureScreenRectangle(bounds);
    }

    private static Bitmap CaptureNextScrolledFrame(IntPtr windowHandle, Rectangle bounds, Bitmap previousFrame, CancellationToken cancellationToken)
    {
        ThrowIfCaptureCanceled(cancellationToken);
        RestoreAndActivateWindow(windowHandle, cancellationToken);
        SendKey(VirtualKeyPageDown);
        var nextFrame = CaptureScreenRectangleAfterScroll(bounds, cancellationToken);
        if (ScrollPositionChanged(previousFrame, nextFrame))
        {
            return nextFrame;
        }

        nextFrame.Dispose();
        RestoreAndActivateWindow(windowHandle, cancellationToken);
        ScrollByWheel(bounds, -WheelScrollDownNotches, cancellationToken);
        return CaptureScreenRectangleAfterScroll(bounds, cancellationToken);
    }

    private static void ScrollByWheel(Rectangle bounds, int notchCount, CancellationToken cancellationToken)
    {
        ThrowIfCaptureCanceled(cancellationToken);
        PositionCursorOnScrollBarEdge(bounds, cancellationToken);
        var delta = unchecked((uint)(notchCount * WheelDelta));
        mouse_event(MouseEventWheel, 0, 0, delta, UIntPtr.Zero);
        PositionCursorOnScrollBarEdge(bounds, cancellationToken);
    }

    private static void SendModifiedKey(byte modifierKey, byte key)
    {
        ThrowIfCaptureCanceled();
        keybd_event(modifierKey, 0, 0, UIntPtr.Zero);
        try
        {
            SendKey(key);
        }
        finally
        {
            keybd_event(modifierKey, 0, KeyEventKeyUp, UIntPtr.Zero);
        }
    }

    private static void SendKey(byte key)
    {
        ThrowIfCaptureCanceled();
        keybd_event(key, 0, 0, UIntPtr.Zero);
        keybd_event(key, 0, KeyEventKeyUp, UIntPtr.Zero);
    }

    private static void SleepWithCancellation(int milliseconds, CancellationToken cancellationToken = default)
    {
        const int pollMilliseconds = 20;
        var remaining = milliseconds;
        while (remaining > 0)
        {
            ThrowIfCaptureCanceled(cancellationToken);
            var sleepFor = Math.Min(pollMilliseconds, remaining);
            Thread.Sleep(sleepFor);
            remaining -= sleepFor;
        }

        ThrowIfCaptureCanceled(cancellationToken);
    }

    private static void ThrowIfCaptureCanceled(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (IsEscapePressed())
        {
            throw new OperationCanceledException("Capture canceled.");
        }
    }

    private static bool IsEscapePressed()
    {
        var keyState = GetAsyncKeyState(VirtualKeyEscape);
        return (keyState & KeyPressedMask) != 0 || (keyState & KeyPressedSinceLastCheckMask) != 0;
    }

    private static WindowZOrderState CaptureWindowZOrderState(IntPtr windowHandle)
    {
        return new WindowZOrderState(IsWindowTopMost(windowHandle), GetWindow(windowHandle, GwHwndPrevious));
    }

    private static void SetWindowTopMost(IntPtr windowHandle, bool isTopMost)
    {
        var insertAfter = isTopMost ? HwndTopMost : HwndNoTopMost;
        SetWindowPos(windowHandle, insertAfter, 0, 0, 0, 0, SetWindowPosNoMove | SetWindowPosNoSize | SetWindowPosNoActivate);
    }

    private static void RestoreWindowZOrder(IntPtr windowHandle, WindowZOrderState zOrderState)
    {
        var insertAfter = zOrderState.PreviousWindowAbove != IntPtr.Zero
            && IsWindow(zOrderState.PreviousWindowAbove)
            && IsWindowTopMost(zOrderState.PreviousWindowAbove) == zOrderState.WasTopMost
            ? zOrderState.PreviousWindowAbove
            : zOrderState.WasTopMost
                ? HwndTopMost
                : HwndNoTopMost;

        SetWindowPos(windowHandle, insertAfter, 0, 0, 0, 0, SetWindowPosNoMove | SetWindowPosNoSize | SetWindowPosNoActivate);
    }

    private static bool IsWindowTopMost(IntPtr windowHandle)
    {
        return (GetWindowExStyle(windowHandle) & WindowExStyleTopMost) != 0;
    }

    private static nint GetWindowExStyle(IntPtr windowHandle)
    {
        return IntPtr.Size == 8
            ? GetWindowLongPtr(windowHandle, GwlExStyle)
            : GetWindowLong(windowHandle, GwlExStyle);
    }

    private readonly record struct WindowZOrderState(bool WasTopMost, IntPtr PreviousWindowAbove);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);

    private const int ShowWindowRestore = 9;
    private const int GwlExStyle = -20;
    private const byte VirtualKeyPageDown = 0x22;
    private const byte VirtualKeyEscape = 0x1B;
    private const byte VirtualKeyHome = 0x24;
    private const byte VirtualKeyControl = 0x11;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint MouseEventWheel = 0x0800;
    private const uint GwHwndPrevious = 3;
    private const nint WindowExStyleTopMost = 0x00000008;
    private static readonly IntPtr HwndTopMost = new(-1);
    private static readonly IntPtr HwndNoTopMost = new(-2);
    private const uint SetWindowPosNoSize = 0x0001;
    private const uint SetWindowPosNoMove = 0x0002;
    private const uint SetWindowPosNoActivate = 0x0010;
    private const int DwmWindowAttributeExtendedFrameBounds = 9;
    private const short KeyPressedMask = unchecked((short)0x8000);
    private const short KeyPressedSinceLastCheckMask = 0x0001;
    private const int SystemMetricVirtualScreenLeft = 76;
    private const int SystemMetricVirtualScreenTop = 77;
    private const int SystemMetricVirtualScreenWidth = 78;
    private const int SystemMetricVirtualScreenHeight = 79;
    private const int CursorShowing = 0x00000001;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect lpRect);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out NativeRect pvAttribute, int cbAttribute);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint lpPoint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorInfo(ref CursorInfo cursorInfo);

    [DllImport("user32.dll")]
    private static extern IntPtr CopyIcon(IntPtr iconHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetIconInfo(IntPtr iconHandle, out IconInfo iconInfo);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr iconHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClipCursor(ref NativeRect lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClipCursor(IntPtr lpRect);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(byte vKey);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, UIntPtr dwExtraInfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorInfo
    {
        public int Size;
        public int Flags;
        public IntPtr Handle;
        public NativePoint Position;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo
    {
        [MarshalAs(UnmanagedType.Bool)]
        public bool IsIcon;
        public uint HotspotX;
        public uint HotspotY;
        public IntPtr MaskBitmap;
        public IntPtr ColorBitmap;
    }
}

public sealed record CapturedCursor(BitmapSource Image, double Left, double Top)
{
    public CapturedCursor Offset(double horizontal, double vertical)
    {
        return this with { Left = Left + horizontal, Top = Top + vertical };
    }
}

public sealed record CapturedImage(BitmapSource Image, CapturedCursor? Cursor = null)
{
    public BitmapSource Render(bool showCursor = true)
    {
        if (!showCursor || Cursor is null)
        {
            return Image;
        }

        var visual = new System.Windows.Media.DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawImage(Image, new Rect(0, 0, Image.Width, Image.Height));
            context.DrawImage(
                Cursor.Image,
                new Rect(Cursor.Left, Cursor.Top, Cursor.Image.Width, Cursor.Image.Height));
        }

        var rendered = new RenderTargetBitmap(
            Image.PixelWidth,
            Image.PixelHeight,
            Image.DpiX,
            Image.DpiY,
            System.Windows.Media.PixelFormats.Pbgra32);
        rendered.Render(visual);
        rendered.Freeze();
        return rendered;
    }
}

public sealed record CaptureFrame(int VirtualLeft, int VirtualTop, BitmapSource Bitmap, CapturedCursor? Cursor = null)
{
    public int Width => Bitmap.PixelWidth;

    public int Height => Bitmap.PixelHeight;
}

public enum PostCaptureAction
{
    Copy,
    Edit,
}

public sealed record CaptureResult(PostCaptureAction Action, BitmapSource Image, CapturedCursor? Cursor = null)
{
    public BitmapSource Render(bool showCursor = true)
    {
        return new CapturedImage(Image, Cursor).Render(showCursor);
    }
}
