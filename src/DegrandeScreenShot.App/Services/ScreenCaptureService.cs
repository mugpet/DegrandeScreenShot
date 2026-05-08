using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace DegrandeScreenShot.App.Services;

public sealed class ScreenCaptureService
{
    public CaptureFrame CaptureVirtualDesktop()
    {
        var screens = Screen.AllScreens;
        var left = screens.Min(screen => screen.Bounds.Left);
        var top = screens.Min(screen => screen.Bounds.Top);
        var right = screens.Max(screen => screen.Bounds.Right);
        var bottom = screens.Max(screen => screen.Bounds.Bottom);

        using var bitmap = new Bitmap(right - left, bottom - top, PixelFormat.Format32bppPArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(left, top, 0, 0, bitmap.Size, CopyPixelOperation.SourceCopy);
        }

        return new CaptureFrame(left, top, ConvertToBitmapSource(bitmap));
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

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);
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