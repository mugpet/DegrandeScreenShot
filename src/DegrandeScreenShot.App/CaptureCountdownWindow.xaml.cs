using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace DegrandeScreenShot.App;

public partial class CaptureCountdownWindow : Window
{
    private const int ExtendedStyleIndex = -20;
    private const int NoActivateStyle = 0x08000000;
    private const int ToolWindowStyle = 0x00000080;
    private const int TransparentStyle = 0x00000020;
    private readonly System.Drawing.Rectangle _captureBounds;

    public CaptureCountdownWindow(System.Drawing.Rectangle captureBounds)
    {
        InitializeComponent();
        _captureBounds = captureBounds;
        Loaded += CaptureCountdownWindow_Loaded;
    }

    public async Task RunAsync(int seconds)
    {
        Show();

        for (var remaining = seconds; remaining > 0; remaining--)
        {
            SecondsText.Text = remaining.ToString(System.Globalization.CultureInfo.CurrentCulture);
            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        Close();
        await Task.Delay(120);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var handle = new WindowInteropHelper(this).Handle;
        var extendedStyle = GetWindowLong(handle, ExtendedStyleIndex);
        SetWindowLong(
            handle,
            ExtendedStyleIndex,
            extendedStyle | NoActivateStyle | ToolWindowStyle | TransparentStyle);
    }

    private void CaptureCountdownWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var logicalLeft = _captureBounds.Left / dpi.DpiScaleX;
        var logicalTop = _captureBounds.Top / dpi.DpiScaleY;
        var logicalWidth = _captureBounds.Width / dpi.DpiScaleX;

        Left = logicalLeft + Math.Max(12, (logicalWidth - ActualWidth) / 2);
        Top = logicalTop + 18;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr handle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(IntPtr handle, int index, int value);
}
