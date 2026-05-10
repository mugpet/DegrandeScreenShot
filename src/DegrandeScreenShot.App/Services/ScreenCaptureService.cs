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
    private const int HorizontalAnalysisBandPercent = 30;
    private const int MinimumAnalysisWidth = 96;
    private const int MaximumIncomingOffsetRows = 160;
    private const int ScrollIndicatorWidth = 24;
    private const int ScrollIndicatorChangedRowsThreshold = 4;
    private const int EstimatedPageDownOverlapPercent = 12;
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
        var previousWindowAbove = GetWindow(windowHandle, GwHwndPrevious);

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
            RestoreWindowZOrder(windowHandle, previousWindowAbove);
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
        var previousWindowAbove = GetWindow(windowHandle, GwHwndPrevious);

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
            RestoreWindowZOrder(windowHandle, previousWindowAbove);
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
        EnsureWindowIsVisibleOnDesktop(windowBounds);
        SetWindowTopMost(windowHandle, true);
        var originalCursor = GetCursorPosition();

        try
        {
            ThrowIfEscapePressed();
            RestoreAndActivateWindow(windowHandle);
            PinCursorOverWindowContent(windowBounds);
            ScrollToTop(windowBounds);
            using var topFrame = CaptureScreenRectangle(windowBounds);

            try
            {
                using var secondFrame = CaptureNextScrolledFrame(windowHandle, windowBounds, topFrame);
                if (BitmapsEqual(topFrame, secondFrame))
                {
                    throw new InvalidOperationException("The selected window did not scroll. Try selecting a scrollable browser content area.");
                }

                var viewport = DetermineScrollableViewport(topFrame, secondFrame);
                using var stitchedBitmap = StitchScrollingFrames(windowHandle, windowBounds, viewport, topFrame, secondFrame);
                return ConvertToBitmapSource(stitchedBitmap);
            }
            catch (OperationCanceledException)
            {
                return ConvertToBitmapSource(topFrame);
            }
        }
        finally
        {
            ReleasePinnedCursor(originalCursor);
            SetWindowTopMost(windowHandle, false);
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
        var previousRows = ComputeRowHashes(firstViewport, GetRowAnalysisBounds(firstViewport.Width, firstViewport.Height));
        Bitmap currentFullFrame = CopyBitmap(secondFrame);
        var lastAppendStartRow = EstimatePageDownAppendStartRow(firstViewport.Height);

        try
        {
            for (var frameIndex = 0; frameIndex < MaximumScrollFrames; frameIndex++)
            {
                using var currentViewport = CopyRegion(currentFullFrame, viewport);
                var currentRows = ComputeRowHashes(currentViewport, GetRowAnalysisBounds(currentViewport.Width, currentViewport.Height));
                if (previousRows.SequenceEqual(currentRows))
                {
                    break;
                }

                var minimumOverlapRows = Math.Max(MinimumOverlapRows, Math.Max(1, currentRows.Length / 20));
                var maxIncomingOffset = Math.Min(MaximumIncomingOffsetRows, Math.Max(0, currentRows.Length / 5));
                var overlap = ScrollCaptureStitcher.FindBestVerticalOverlap(previousRows, currentRows, minimumOverlapRows, maxIncomingOffset);
                if (overlap.OverlapRows == 0)
                {
                    if (lastAppendStartRow <= 0 || lastAppendStartRow >= currentViewport.Height)
                    {
                        DumpScrollDiagnostics(topFrame, secondFrame, viewport, previousRows, currentRows, minimumOverlapRows, maxIncomingOffset);
                        throw new InvalidOperationException("Could not match consecutive scroll captures. Try selecting a browser window with visible page content.");
                    }

                    stitched = AppendBitmap(stitched, currentViewport, lastAppendStartRow);
                    previousRows = currentRows;
                    if (IsEscapePressed())
                    {
                        return stitched;
                    }

                    using var fallbackNextFullFrame = CaptureNextScrolledFrame(windowHandle, windowBounds, currentFullFrame);
                    if (!ScrollPositionChanged(currentFullFrame, fallbackNextFullFrame))
                    {
                        break;
                    }

                    currentFullFrame.Dispose();
                    currentFullFrame = CopyBitmap(fallbackNextFullFrame);
                    continue;
                }

                stitched = AppendBitmap(stitched, currentViewport, overlap.AppendStartRow);
                lastAppendStartRow = overlap.AppendStartRow;
                previousRows = currentRows;
                if (IsEscapePressed())
                {
                    return stitched;
                }

                using var nextFullFrame = CaptureNextScrolledFrame(windowHandle, windowBounds, currentFullFrame);
                if (!ScrollPositionChanged(currentFullFrame, nextFullFrame))
                {
                    break;
                }

                currentFullFrame.Dispose();
                currentFullFrame = CopyBitmap(nextFullFrame);
            }

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
        var rowBytes = analysisBounds.Width * 4;
        var hashes = new ulong[analysisBounds.Height];
        var buffer = new byte[rowBytes];
        var data = bitmap.LockBits(analysisBounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
        try
        {
            for (var row = 0; row < analysisBounds.Height; row++)
            {
                Marshal.Copy(data.Scan0 + (row * data.Stride), buffer, 0, rowBytes);
                hashes[row] = ComputeHash(buffer);
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

    private static ulong ComputeHash(byte[] buffer)
    {
        const ulong offsetBasis = 14695981039346656037;
        const ulong prime = 1099511628211;
        var hash = offsetBasis;
        foreach (var value in buffer)
        {
            hash ^= value;
            hash *= prime;
        }

        return hash;
    }

    private static void DumpScrollDiagnostics(Bitmap topFrame, Bitmap secondFrame, Rectangle viewport, ulong[] previousRows, ulong[] currentRows, int minimumOverlapRows, int maxIncomingOffset)
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

            // Brute-force best match ratio at any overlap size (ignoring minimumOverlapRows)
            var bestRatio = 0.0;
            var bestOverlap = 0;
            for (var overlap = 1; overlap <= Math.Min(previousRows.Length, currentRows.Length); overlap++)
            {
                var startIndex = previousRows.Length - overlap;
                var matches = 0;
                for (var offset = 0; offset < overlap; offset++)
                {
                    if (previousRows[startIndex + offset] == currentRows[offset])
                        matches++;
                }
                var ratio = (double)matches / overlap;
                if (ratio > bestRatio) { bestRatio = ratio; bestOverlap = overlap; }
            }

            var log = $"""
                dss_scroll_diagnostics
                scrollMethod=Ctrl+Home/PageDown/WheelFallback
                viewport={viewport}
                topFrame={topFrame.Width}x{topFrame.Height}
                secondFrame={secondFrame.Width}x{secondFrame.Height}
                vpWidth={vp0.Width} vpHeight={vp0.Height}
                analysisBounds={GetRowAnalysisBounds(vp0.Width, vp0.Height)}
                previousRows.Length={previousRows.Length}
                currentRows.Length={currentRows.Length}
                minimumOverlapRows={minimumOverlapRows}
                maxIncomingOffset={maxIncomingOffset}
                RequiredMatchRatio=0.82 small / 0.75 large / 0.50 very large
                bestBruteForceOverlap={bestOverlap} ratio={bestRatio:P1}
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
        return ComputeRowHashes(first, analysisBounds).SequenceEqual(ComputeRowHashes(second, analysisBounds));
    }

    private static bool ScrollPositionChanged(Bitmap before, Bitmap after)
    {
        if (before.Width != after.Width || before.Height != after.Height)
        {
            return true;
        }

        var indicatorBounds = GetScrollIndicatorBounds(before.Width, before.Height);
        var beforeRows = ComputeRowHashes(before, indicatorBounds);
        var afterRows = ComputeRowHashes(after, indicatorBounds);
        var changedRows = 0;
        for (var index = 0; index < beforeRows.Length; index++)
        {
            if (beforeRows[index] == afterRows[index])
            {
                continue;
            }

            changedRows++;
            if (changedRows >= ScrollIndicatorChangedRowsThreshold)
            {
                return true;
            }
        }

        return false;
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

        var screens = Screen.AllScreens;
        var desktopLeft = screens.Min(screen => screen.Bounds.Left);
        var desktopTop = screens.Min(screen => screen.Bounds.Top);
        var desktopRight = screens.Max(screen => screen.Bounds.Right);
        var desktopBottom = screens.Max(screen => screen.Bounds.Bottom);
        var desktopBounds = Rectangle.FromLTRB(desktopLeft, desktopTop, desktopRight, desktopBottom);
        if (!desktopBounds.Contains(bounds))
        {
            throw new InvalidOperationException("The selected window must be fully visible on screen for scrolling capture.");
        }
    }

    private static void RestoreAndActivateWindow(IntPtr windowHandle)
    {
        ShowWindow(windowHandle, ShowWindowRestore);
        SetForegroundWindow(windowHandle);
        SleepWithCancellation((int)FocusDelay.TotalMilliseconds);
    }

    private static void PositionCursorOverWindowContent(Rectangle bounds)
    {
        var focusX = bounds.Left + (bounds.Width / 2);
        var focusY = bounds.Top + (bounds.Height * FocusPointVerticalPercent / 100);
        SetCursorPos(focusX, focusY);
        SleepWithCancellation((int)FocusDelay.TotalMilliseconds);
    }

    private static void PinCursorOverWindowContent(Rectangle bounds)
    {
        var focusX = bounds.Left + (bounds.Width / 2);
        var focusY = bounds.Top + (bounds.Height * FocusPointVerticalPercent / 100);
        SetCursorPos(focusX, focusY);
        var clipRect = new NativeRect
        {
            Left = focusX,
            Top = focusY,
            Right = focusX + 1,
            Bottom = focusY + 1,
        };
        ClipCursor(ref clipRect);
        SleepWithCancellation((int)FocusDelay.TotalMilliseconds);
    }

    private static NativePoint GetCursorPosition()
    {
        return GetCursorPos(out var point) ? point : new NativePoint();
    }

    private static void ReleasePinnedCursor(NativePoint originalCursor)
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
            using var before = CaptureScreenRectangle(windowBounds);
            ScrollByWheel(WheelScrollToTopNotches);
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
        Bitmap prev = CaptureScreenRectangle(bounds);
        for (var i = 0; i < ScrollSettleMaxPolls; i++)
        {
            SleepWithCancellation(ScrollSettlePollMs);
            Bitmap current = CaptureScreenRectangle(bounds);
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

    private static void ScrollDownOnePage()
    {
        ScrollByWheel(-WheelScrollDownNotches);
    }

    private static Bitmap CaptureNextScrolledFrame(IntPtr windowHandle, Rectangle bounds, Bitmap previousFrame)
    {
        ThrowIfEscapePressed();
        RestoreAndActivateWindow(windowHandle);
        PositionCursorOverWindowContent(bounds);
        SendKey(VirtualKeyPageDown);
        var nextFrame = CaptureScreenRectangleAfterScroll(bounds);
        if (ScrollPositionChanged(previousFrame, nextFrame))
        {
            return nextFrame;
        }

        nextFrame.Dispose();
        RestoreAndActivateWindow(windowHandle);
        PositionCursorOverWindowContent(bounds);
        ScrollDownOnePage();
        return CaptureScreenRectangleAfterScroll(bounds);
    }

    private static void ScrollByWheel(int notchCount)
    {
        ThrowIfEscapePressed();
        var delta = unchecked((uint)(notchCount * WheelDelta));
        mouse_event(MouseEventWheel, 0, 0, delta, UIntPtr.Zero);
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

    private static void SetWindowTopMost(IntPtr windowHandle, bool isTopMost)
    {
        var insertAfter = isTopMost ? HwndTopMost : HwndNoTopMost;
        SetWindowPos(windowHandle, insertAfter, 0, 0, 0, 0, SetWindowPosNoMove | SetWindowPosNoSize | SetWindowPosNoActivate);
    }

    private static void RestoreWindowZOrder(IntPtr windowHandle, IntPtr previousWindowAbove)
    {
        if (previousWindowAbove != IntPtr.Zero && IsWindow(previousWindowAbove))
        {
            SetWindowPos(windowHandle, previousWindowAbove, 0, 0, 0, 0, SetWindowPosNoMove | SetWindowPosNoSize | SetWindowPosNoActivate);
            return;
        }

        SetWindowPos(windowHandle, HwndTop, 0, 0, 0, 0, SetWindowPosNoMove | SetWindowPosNoSize | SetWindowPosNoActivate);
    }

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);

    private const int ShowWindowRestore = 9;
    private const byte VirtualKeyPageDown = 0x22;
    private const byte VirtualKeyEscape = 0x1B;
    private const byte VirtualKeyHome = 0x24;
    private const byte VirtualKeyControl = 0x11;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint MouseEventWheel = 0x0800;
    private const uint GwHwndPrevious = 3;
    private static readonly IntPtr HwndTop = new(0);
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