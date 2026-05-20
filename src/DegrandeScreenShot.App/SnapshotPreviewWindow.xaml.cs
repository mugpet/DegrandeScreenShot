using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DegrandeScreenShot.App;

public partial class SnapshotPreviewWindow : Window
{
    private const double MaxPreviewWidth = 320;
    private const double MaxPreviewHeight = 240;
    private readonly DispatcherTimer _closeTimer;

    public SnapshotPreviewWindow(BitmapSource image)
    {
        InitializeComponent();

        PreviewImage.Source = image;
        DimensionsText.Text = $"{image.PixelWidth} x {image.PixelHeight}";
        SizeToImage(image);

        _closeTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2.8),
        };
        _closeTimer.Tick += CloseTimer_Tick;

        Loaded += SnapshotPreviewWindow_Loaded;
        Closed += SnapshotPreviewWindow_Closed;
    }

    public void Position(Rect workAreaPhysical)
    {
        // Ensure the Win32 window handle is created so we get the accurate DPI scale for the correct monitor
        var handle = new WindowInteropHelper(this).EnsureHandle();

        var dpi = VisualTreeHelper.GetDpi(this);
        var scaleX = dpi.DpiScaleX;
        var scaleY = dpi.DpiScaleY;

        double logicalRight = workAreaPhysical.Right / scaleX;
        double logicalBottom = workAreaPhysical.Bottom / scaleY;

        Left = logicalRight - Width - 18;
        Top = logicalBottom - Height - 18;
    }

    private void SnapshotPreviewWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _closeTimer.Start();
    }

    private void SnapshotPreviewWindow_Closed(object? sender, EventArgs e)
    {
        _closeTimer.Stop();
        _closeTimer.Tick -= CloseTimer_Tick;
        Loaded -= SnapshotPreviewWindow_Loaded;
        Closed -= SnapshotPreviewWindow_Closed;
    }

    private void CloseTimer_Tick(object? sender, EventArgs e)
    {
        Close();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        if (PresentationSource.FromVisual(this) is HwndSource source)
        {
            const int extendedStyleIndex = -20;
            const int noActivateStyle = 0x08000000;
            const int toolWindowStyle = 0x00000080;

            var extendedStyle = GetWindowLong(source.Handle, extendedStyleIndex);
            SetWindowLong(source.Handle, extendedStyleIndex, extendedStyle | noActivateStyle | toolWindowStyle);
        }
    }

    private void SizeToImage(BitmapSource image)
    {
        var scale = Math.Min(MaxPreviewWidth / image.PixelWidth, MaxPreviewHeight / image.PixelHeight);
        scale = Math.Min(scale, 1d);

        var imageWidth = Math.Max(96, image.PixelWidth * scale);
        var imageHeight = Math.Max(72, image.PixelHeight * scale);

        Width = imageWidth + 20;
        Height = imageHeight + 68;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong(IntPtr handle, int index);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern int SetWindowLong(IntPtr handle, int index, int value);
}