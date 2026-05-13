using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
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
        var screens = Screen.AllScreens;
        var left = screens.Min(screen => screen.Bounds.Left);
        var top = screens.Min(screen => screen.Bounds.Top);
        var right = screens.Max(screen => screen.Bounds.Right);
        var bottom = screens.Max(screen => screen.Bounds.Bottom);

        using var bitmap = new System.Drawing.Bitmap(right - left, bottom - top, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(left, top, 0, 0, bitmap.Size, CopyPixelOperation.SourceCopy);
        }

        return new CaptureFrame(left, top, ConvertToBitmapSource(bitmap));
    }

    public BitmapSource CaptureVisibleWindow(IntPtr windowHandle)
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
            SleepWithCancellation((int)FocusDelay.TotalMilliseconds);

            var windowBounds = GetWindowBounds(windowHandle);
            EnsureWindowIsVisibleOnDesktop(windowBounds);
            using var bitmap = CaptureScreenRectangle(windowBounds);
            return ConvertToBitmapSource(bitmap);
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

    public BitmapSource CaptureVisibleWindowRegion(IntPtr windowHandle, Rectangle captureBounds)
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
            SleepWithCancellation((int)FocusDelay.TotalMilliseconds);

            using var bitmap = CaptureScreenRectangle(captureBounds);
            return ConvertToBitmapSource(bitmap);
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

    public BitmapSource CaptureScrollingWindow(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException("No window was selected.");
        }

        RestoreAndActivateWindow(windowHandle);
        var windowBounds = GetWindowBounds(windowHandle);
        return CaptureScrollingWindow(windowHandle, windowBounds);
    }

    public BitmapSource CaptureScrollingWindow(IntPtr windowHandle, Rectangle captureBounds)
    {
        if (windowHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException("No window was selected.");
        }

        var zOrderState = CaptureWindowZOrderState(windowHandle);
        RestoreAndActivateWindow(windowHandle);
        var windowBounds = GetWindowBounds(windowHandle);
        EnsureWindowIsVisibleOnDesktop(windowBounds);
        var scrollCaptureBounds = Rectangle.Intersect(windowBounds, captureBounds);
        if (scrollCaptureBounds.Width <= 0 || scrollCaptureBounds.Height <= 0)
        {
            throw new InvalidOperationException("The selected scroll area is not visible inside the selected window.");
        }

        SetWindowTopMost(windowHandle, true);
        var originalCursor = GetCursorPosition();

        try
        {
            ThrowIfEscapePressed();
            RestoreAndActivateWindow(windowHandle);
            PositionCursorOnScrollBarEdge(scrollCaptureBounds);
            ScrollToTop(scrollCaptureBounds);
            using var topFrame = CaptureScreenRectangleAtScrollBarEdge(scrollCaptureBounds);

            try
            {
                using var secondFrame = CaptureNextScrolledFrame(windowHandle, scrollCaptureBounds, topFrame);
                if (BitmapsEqual(topFrame, secondFrame))
                {
                    throw new InvalidOperationException("The selected window did not scroll. Try selecting a scrollable browser content area.");
                }

                var viewport = DetermineScrollableViewport(topFrame, secondFrame);
                using var stitchedBitmap = StitchScrollingFrames(windowHandle, scrollCaptureBounds, viewport, topFrame, secondFrame);
                return ConvertToBitmapSource(stitchedBitmap);
            }
            catch (OperationCanceledException)
            {
                return ConvertToBitmapSource(topFrame);
            }
        }
        finally
        {
            RestoreCursor(originalCursor);
            RestoreWindowZOrder(windowHandle, zOrderState);
        }
    }

    private static BitmapSource ConvertToBitmapSource(Bitmap bitmap)
    {
        var handle = bitmap.GetHbitmap();

        try
        {
            var bitmapSource = Imaging.CreateBitmapSourceFromHBitmap(handle, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            bitmapSource.Freeze();
            return NormalizeDpi(bitmapSource);
        }
        finally
        {
            DeleteObject(handle);
        }
    }

    private static BitmapSource NormalizeDpi(BitmapSource bitmapSource)
    {
        const double targetDpi = 96d;
        if (Math.Abs(bitmapSource.DpiX - targetDpi) < 0.001 && Math.Abs(bitmapSource.DpiY - targetDpi) < 0.001)
        {
            return bitmapSource;
        }

        var pixelFormat = bitmapSource.Format;
        var stride = ((bitmapSource.PixelWidth * pixelFormat.BitsPerPixel) + 7) / 8;
        var pixels = new byte[stride * bitmapSource.PixelHeight];
        bitmapSource.CopyPixels(pixels, stride, 0);

        var normalized = BitmapSource.Create(
            bitmapSource.PixelWidth,
            bitmapSource.PixelHeight,
            targetDpi,
            targetDpi,
            pixelFormat,
            bitmapSource.Palette,
            pixels,
            stride);
        normalized.Freeze();
        return normalized;
    }

    private static Bitmap StitchScrollingFrames(IntPtr windowHandle, Rectangle windowBounds, Rectangle viewport, Bitmap topFrame, Bitmap secondFrame)
    {
        using var firstViewport = CopyRegion(topFrame, viewport);
        var stitched = CreateInitialStitchedBitmap(topFrame, viewport);
        var previousProfiles = ComputeRowProfiles(firstViewport, GetRowAnalysisBounds(firstViewport.Width, firstViewport.Height));
        Bitmap currentFullFrame = CopyBitmap(secondFrame);
        var lastAppendStartRow = EstimatePageDownAppendStartRow(firstViewport.Height);
        var diagnosticTrace = new System.Text.StringBuilder();
        diagnosticTrace.AppendLine($"viewport={viewport} analysisBounds={GetRowAnalysisBounds(firstViewport.Width, firstViewport.Height)}");

        try
        {
            for (var frameIndex = 0; frameIndex < MaximumScrollFrames; frameIndex++)
            {
                using var currentViewport = CopyRegion(currentFullFrame, viewport);
                var currentProfiles = ComputeRowProfiles(currentViewport, GetRowAnalysisBounds(currentViewport.Width, currentViewport.Height));
                if (RowProfilesIdentical(previousProfiles, currentProfiles))
                {
                    diagnosticTrace.AppendLine($"frame {frameIndex}: identical profiles, breaking");
                    break;
                }

                var shift = FindBestVerticalShift(previousProfiles, currentProfiles, out var bestSad, out var baselineSad);
                diagnosticTrace.AppendLine($"frame {frameIndex}: shift={shift} bestSad={bestSad:F1} baselineSad={baselineSad:F1}");

                int appendStartRow;
                if (shift > 0)
                {
                    appendStartRow = currentViewport.Height - shift;
                }
                else
                {
                    if (lastAppendStartRow <= 0 || lastAppendStartRow >= currentViewport.Height)
                    {
                        DumpScrollDiagnostics(topFrame, secondFrame, viewport, previousProfiles, currentProfiles, diagnosticTrace.ToString());
                        throw new InvalidOperationException("Could not match consecutive scroll captures. Try selecting a browser window with visible page content.");
                    }
                    appendStartRow = lastAppendStartRow;
                }

                stitched = AppendBitmap(stitched, currentViewport, appendStartRow);
                lastAppendStartRow = appendStartRow;
                previousProfiles = currentProfiles;
                if (IsEscapePressed())
                {
                    return stitched;
                }

                using var nextFullFrame = CaptureNextScrolledFrame(windowHandle, windowBounds, currentFullFrame);
                if (!ScrollPositionChanged(currentFullFrame, nextFullFrame))
                {
                    diagnosticTrace.AppendLine($"frame {frameIndex}: no further scroll, breaking");
                    break;
                }

                currentFullFrame.Dispose();
                currentFullFrame = CopyBitmap(nextFullFrame);
            }

            DumpScrollDiagnostics(topFrame, secondFrame, viewport, previousProfiles, previousProfiles, diagnosticTrace.ToString());
            return stitched;
        }
        catch (OperationCanceledException)
        {
            return stitched;
        }
        finally
        {
            currentFullFrame.Dispose();
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
        var data = bitmap.LockBits(analysisBounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
        try
        {
            for (var row = 0; row < analysisBounds.Height; row++)
            {
                hashes[row] = ComputePerceptualRowSignature(data, analysisBounds.Width, row);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return hashes;
    }

    private static ulong ComputePerceptualRowSignature(BitmapData data, int width, int row)
    {
        Span<int> bucketTotals = stackalloc int[RowSignatureBuckets];
        Span<int> bucketCounts = stackalloc int[RowSignatureBuckets];
        var rowPointer = data.Scan0 + (row * data.Stride);
        for (var x = 0; x < width; x++)
        {
            var pixelOffset = x * 4;
            var blue = Marshal.ReadByte(rowPointer, pixelOffset);
            var green = Marshal.ReadByte(rowPointer, pixelOffset + 1);
            var red = Marshal.ReadByte(rowPointer, pixelOffset + 2);
            var luminance = ((red * 77) + (green * 150) + (blue * 29)) >> 8;
            var bucket = Math.Min(RowSignatureBuckets - 1, x * RowSignatureBuckets / width);
            bucketTotals[bucket] += luminance;
            bucketCounts[bucket]++;
        }

        ulong signature = 0;
        for (var bucket = 0; bucket < RowSignatureBuckets; bucket++)
        {
            var average = bucketCounts[bucket] == 0 ? 255 : bucketTotals[bucket] / bucketCounts[bucket];
            var quantized = (ulong)Math.Clamp(average / 16, 0, 15);
            signature |= quantized << (bucket * 4);
        }

        return signature;
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
        var data = bitmap.LockBits(analysisBounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
        try
        {
            for (var row = 0; row < analysisBounds.Height; row++)
            {
                profiles[row] = ComputeRowProfile(data, analysisBounds.Width, row);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return profiles;
    }

    private static byte[] ComputeRowProfile(BitmapData data, int width, int row)
    {
        var buckets = RowProfileBuckets;
        Span<int> bucketTotals = stackalloc int[buckets];
        Span<int> bucketCounts = stackalloc int[buckets];
        var rowPointer = data.Scan0 + (row * data.Stride);
        for (var x = 0; x < width; x++)
        {
            var pixelOffset = x * 4;
            var blue = Marshal.ReadByte(rowPointer, pixelOffset);
            var green = Marshal.ReadByte(rowPointer, pixelOffset + 1);
            var red = Marshal.ReadByte(rowPointer, pixelOffset + 2);
            var luminance = ((red * 77) + (green * 150) + (blue * 29)) >> 8;
            var bucket = Math.Min(buckets - 1, x * buckets / width);
            bucketTotals[bucket] += luminance;
            bucketCounts[bucket]++;
        }

        var profile = new byte[buckets];
        for (var bucket = 0; bucket < buckets; bucket++)
        {
            var average = bucketCounts[bucket] == 0 ? 255 : bucketTotals[bucket] / bucketCounts[bucket];
            profile[bucket] = (byte)Math.Clamp(average, 0, 255);
        }

        return profile;
    }

    private static int RowProfileSad(byte[] left, byte[] right)
    {
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
    private static int FindBestVerticalShift(byte[][] previousProfiles, byte[][] currentProfiles, out double bestSad, out double baselineSad)
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
            var sad = AverageProfileSad(previousProfiles, currentProfiles, shift, overlap);
            if (sad < bestSad)
            {
                bestSad = sad;
                bestShift = shift;
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

    private static double AverageProfileSad(byte[][] previousProfiles, byte[][] currentProfiles, int shift, int overlap)
    {
        if (overlap <= 0)
        {
            return double.MaxValue;
        }

        long total = 0;
        for (var i = 0; i < overlap; i++)
        {
            total += RowProfileSad(previousProfiles[shift + i], currentProfiles[i]);
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
        var bitmap = new System.Drawing.Bitmap(bounds.Width, bounds.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
        return bitmap;
    }

    private static Bitmap CopyBitmap(Bitmap source)
    {
        return CopyRegion(source, new Rectangle(0, 0, source.Width, source.Height));
    }

    private static Bitmap CopyRegion(Bitmap source, Rectangle region)
    {
        var bitmap = new System.Drawing.Bitmap(region.Width, region.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.DrawImage(source, new Rectangle(0, 0, region.Width, region.Height), region, GraphicsUnit.Pixel);
        return bitmap;
    }

    private static Bitmap CreateInitialStitchedBitmap(Bitmap topFrame, Rectangle viewport)
    {
        if (viewport.Top <= 0)
        {
            return CopyRegion(topFrame, viewport);
        }

        var bitmap = new System.Drawing.Bitmap(topFrame.Width, viewport.Top + viewport.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.DrawImage(
            topFrame,
            new Rectangle(0, 0, topFrame.Width, viewport.Top),
            new Rectangle(0, 0, topFrame.Width, viewport.Top),
            GraphicsUnit.Pixel);
        graphics.DrawImage(
            topFrame,
            new Rectangle(0, viewport.Top, viewport.Width, viewport.Height),
            viewport,
            GraphicsUnit.Pixel);
        return bitmap;
    }

    private static Bitmap AppendBitmap(Bitmap existing, Bitmap incoming, int appendStartRow)
    {
        var appendHeight = incoming.Height - appendStartRow;
        if (appendHeight <= 0)
        {
            return existing;
        }

        var combined = new System.Drawing.Bitmap(existing.Width, existing.Height + appendHeight, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using (var graphics = Graphics.FromImage(combined))
        {
            graphics.DrawImage(existing, new Rectangle(0, 0, existing.Width, existing.Height));
            graphics.DrawImage(
                incoming,
                new Rectangle(0, existing.Height, incoming.Width, appendHeight),
                new Rectangle(0, appendStartRow, incoming.Width, appendHeight),
                GraphicsUnit.Pixel);
        }

        existing.Dispose();
        return combined;
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
        var screens = Screen.AllScreens;
        var desktopLeft = screens.Min(screen => screen.Bounds.Left);
        var desktopTop = screens.Min(screen => screen.Bounds.Top);
        var desktopRight = screens.Max(screen => screen.Bounds.Right);
        var desktopBottom = screens.Max(screen => screen.Bounds.Bottom);
        return Rectangle.FromLTRB(desktopLeft, desktopTop, desktopRight, desktopBottom);
    }

    private static void RestoreAndActivateWindow(IntPtr windowHandle)
    {
        ShowWindow(windowHandle, ShowWindowRestore);
        SetForegroundWindow(windowHandle);
        SleepWithCancellation((int)FocusDelay.TotalMilliseconds);
    }

    private static void PositionCursorOnScrollBarEdge(Rectangle bounds)
    {
        var scrollbarOffset = Math.Min(Math.Max(CursorScrollBarEdgePadding, ScrollIndicatorWidth / 2), Math.Max(1, bounds.Width / 3));
        var focusX = Math.Clamp(bounds.Right - scrollbarOffset, bounds.Left, bounds.Right - 1);
        var focusY = Math.Clamp(bounds.Top + (bounds.Height * FocusPointVerticalPercent / 100), bounds.Top, bounds.Bottom - 1);
        SetCursorPos(focusX, focusY);
        SleepWithCancellation((int)FocusDelay.TotalMilliseconds);
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

    private static void ScrollToTop(Rectangle windowBounds)
    {
        ThrowIfEscapePressed();
        SendModifiedKey(VirtualKeyControl, VirtualKeyHome);
        using var _ = CaptureScreenRectangleAfterScroll(windowBounds);
        for (var attempt = 0; attempt < MaximumScrollToTopAttempts; attempt++)
        {
            ThrowIfEscapePressed();
            using var before = CaptureScreenRectangleAtScrollBarEdge(windowBounds);
            ScrollByWheel(windowBounds, WheelScrollToTopNotches);
            using var after = CaptureScreenRectangleAfterScroll(windowBounds);
            if (BitmapsEqual(before, after))
            {
                return;
            }
        }
    }

    private static Bitmap CaptureScreenRectangleAfterScroll(Rectangle bounds)
    {
        SleepWithCancellation(ScrollSettleInitialMs);
        Bitmap prev = CaptureScreenRectangleAtScrollBarEdge(bounds);
        for (var i = 0; i < ScrollSettleMaxPolls; i++)
        {
            SleepWithCancellation(ScrollSettlePollMs);
            Bitmap current = CaptureScreenRectangleAtScrollBarEdge(bounds);
            if (BitmapsEqual(prev, current))
            {
                prev.Dispose();
                return current;
            }

            prev.Dispose();
            prev = current;
        }

        return prev;
    }

    private static Bitmap CaptureScreenRectangleAtScrollBarEdge(Rectangle bounds)
    {
        PositionCursorOnScrollBarEdge(bounds);
        return CaptureScreenRectangle(bounds);
    }

    private static Bitmap CaptureNextScrolledFrame(IntPtr windowHandle, Rectangle bounds, Bitmap previousFrame)
    {
        ThrowIfEscapePressed();
        RestoreAndActivateWindow(windowHandle);
        SendKey(VirtualKeyPageDown);
        var nextFrame = CaptureScreenRectangleAfterScroll(bounds);
        if (ScrollPositionChanged(previousFrame, nextFrame))
        {
            return nextFrame;
        }

        nextFrame.Dispose();
        RestoreAndActivateWindow(windowHandle);
        ScrollByWheel(bounds, -WheelScrollDownNotches);
        return CaptureScreenRectangleAfterScroll(bounds);
    }

    private static void ScrollByWheel(Rectangle bounds, int notchCount)
    {
        ThrowIfEscapePressed();
        PositionCursorOnScrollBarEdge(bounds);
        var delta = unchecked((uint)(notchCount * WheelDelta));
        mouse_event(MouseEventWheel, 0, 0, delta, UIntPtr.Zero);
        PositionCursorOnScrollBarEdge(bounds);
    }

    private static void SendModifiedKey(byte modifierKey, byte key)
    {
        ThrowIfEscapePressed();
        keybd_event(modifierKey, 0, 0, UIntPtr.Zero);
        SendKey(key);
        keybd_event(modifierKey, 0, KeyEventKeyUp, UIntPtr.Zero);
    }

    private static void SendKey(byte key)
    {
        ThrowIfEscapePressed();
        keybd_event(key, 0, 0, UIntPtr.Zero);
        keybd_event(key, 0, KeyEventKeyUp, UIntPtr.Zero);
    }

    private static void SleepWithCancellation(int milliseconds)
    {
        const int pollMilliseconds = 20;
        var remaining = milliseconds;
        while (remaining > 0)
        {
            ThrowIfEscapePressed();
            var sleepFor = Math.Min(pollMilliseconds, remaining);
            Thread.Sleep(sleepFor);
            remaining -= sleepFor;
        }

        ThrowIfEscapePressed();
    }

    private static void ThrowIfEscapePressed()
    {
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
    private static extern bool ClipCursor(ref NativeRect lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClipCursor(IntPtr lpRect);

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
}

public sealed record CaptureFrame(int VirtualLeft, int VirtualTop, BitmapSource Bitmap)
{
    public int Width => Bitmap.PixelWidth;

    public int Height => Bitmap.PixelHeight;
}

public enum PostCaptureAction
{
    Copy,
    Edit,
}

public sealed record CaptureResult(PostCaptureAction Action, BitmapSource Image);