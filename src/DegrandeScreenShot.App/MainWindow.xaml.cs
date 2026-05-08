using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Runtime.InteropServices;
using DegrandeScreenShot.App.Services;
using FormsContextMenuStrip = System.Windows.Forms.ContextMenuStrip;
using FormsMouseButtons = System.Windows.Forms.MouseButtons;
using FormsNotifyIcon = System.Windows.Forms.NotifyIcon;
using FormsScreen = System.Windows.Forms.Screen;
using FormsToolStripMenuItem = System.Windows.Forms.ToolStripMenuItem;

namespace DegrandeScreenShot.App;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly List<GlobalHotKeyManager> _hotKeyManagers;
    private readonly ScreenCaptureService _screenCaptureService = new();
    private readonly FormsNotifyIcon _trayIcon;
    private bool _isExplicitExit;
    private SnapshotPreviewWindow? _previewWindow;
    private bool _startHiddenInTray;

    public MainWindow(bool startHiddenInTray = false)
    {
        InitializeComponent();
        _startHiddenInTray = startHiddenInTray;
        _hotKeyManagers =
        [
            new GlobalHotKeyManager(this, ModifierKeys.Control | ModifierKeys.Shift, Key.D4, BeginCaptureFromHotKey),
            new GlobalHotKeyManager(this, ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt, Key.D5, BeginPromptCaptureFromHotKey),
            new GlobalHotKeyManager(this, ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt, Key.D6, BeginClipboardCaptureFromHotKey),
            new GlobalHotKeyManager(this, ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt, Key.D7, BeginEditorCaptureFromHotKey),
        ];

        if (_startHiddenInTray)
        {
            Opacity = 0;
            ShowInTaskbar = false;
        }

        _trayIcon = CreateTrayIcon();
        SourceInitialized += MainWindow_SourceInitialized;
        Loaded += MainWindow_Loaded;
        StateChanged += MainWindow_StateChanged;
        Closing += MainWindow_Closing;
        Closed += MainWindow_Closed;
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        foreach (var hotKeyManager in _hotKeyManagers)
        {
            hotKeyManager.Register();
        }
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _trayIcon.Visible = false;
        _trayIcon.Dispose();

        foreach (var hotKeyManager in _hotKeyManagers)
        {
            hotKeyManager.Dispose();
        }
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        PositionLikeTaskbarPopup();

        if (_startHiddenInTray)
        {
            HideToTray();
            Opacity = 1;
            _startHiddenInTray = false;
        }
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            HideToTray();
        }
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_isExplicitExit)
        {
            return;
        }

        e.Cancel = true;
        HideToTray();
    }

    private void StartCapture_Click(object sender, RoutedEventArgs e)
    {
        StartCapture();
    }

    private void DragSurface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void CloseWindow_Click(object sender, RoutedEventArgs e)
    {
        HideToTray();
    }

    private void BeginCaptureFromHotKey()
    {
        Dispatcher.Invoke(StartCapture);
    }

    private void BeginPromptCaptureFromHotKey()
    {
        Dispatcher.Invoke(() => StartCapture(CaptureLaunchMode.ChooseAction));
    }

    private void BeginClipboardCaptureFromHotKey()
    {
        Dispatcher.Invoke(() => StartCapture(CaptureLaunchMode.CopyToClipboard));
    }

    private void BeginEditorCaptureFromHotKey()
    {
        Dispatcher.Invoke(() => StartCapture(CaptureLaunchMode.OpenEditor));
    }

    private void StartCapture()
    {
        StartCapture(CaptureLaunchMode.ChooseAction);
    }

    private void StartCapture(CaptureLaunchMode launchMode)
    {
        try
        {
            var originalCursor = GetCursorPosition();
            var cursorRestored = false;

            void RestoreCursor()
            {
                if (cursorRestored)
                {
                    return;
                }

                SetCursorPosition(originalCursor);
                cursorRestored = true;
            }

            MoveCursorToPrimaryScreen();
            var captureFrame = _screenCaptureService.CaptureVirtualDesktop();
            var overlay = new CaptureOverlayWindow(captureFrame, launchMode);
            overlay.ContentRendered += (_, _) => RestoreCursor();
            var result = overlay.ShowDialog();
            RestoreCursor();

            if (result != true || overlay.CaptureResult is null)
            {
                return;
            }

            ShowCapturePreview(overlay.CaptureResult.Image);

            if (overlay.CaptureResult.Action == PostCaptureAction.Edit)
            {
                var editor = new EditorWindow(overlay.CaptureResult.Image);
                editor.Show();
                editor.Activate();
            }
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(this, exception.Message, "Capture failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ShowLauncher()
    {
        if (!IsVisible)
        {
            Show();
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        PositionLikeTaskbarPopup();
        Activate();
        Opacity = 1;
        ShowInTaskbar = true;
    }

    private void HideToTray()
    {
        Hide();
        ShowInTaskbar = false;
        WindowState = WindowState.Normal;
    }

    internal void ShowCapturePreview(BitmapSource image)
    {
        _previewWindow?.Close();

        var cursor = GetCursorPosition();
        var activeScreen = FormsScreen.FromPoint(new System.Drawing.Point((int)cursor.X, (int)cursor.Y));
        var workArea = activeScreen.WorkingArea;

        var preview = new SnapshotPreviewWindow(image);
        preview.Position(new Rect(workArea.Left, workArea.Top, workArea.Width, workArea.Height));
        preview.Closed += (_, _) =>
        {
            if (ReferenceEquals(_previewWindow, preview))
            {
                _previewWindow = null;
            }
        };

        _previewWindow = preview;
        preview.Show();
    }

    private static void MoveCursorToPrimaryScreen()
    {
        var primaryScreen = FormsScreen.PrimaryScreen;
        if (primaryScreen is null)
        {
            return;
        }

        var primaryBounds = primaryScreen.Bounds;
        var centerX = primaryBounds.Left + (primaryBounds.Width / 2);
        var centerY = primaryBounds.Top + (primaryBounds.Height / 2);
        SetCursorPos(centerX, centerY);
    }

    private static void SetCursorPosition(Point point)
    {
        SetCursorPos((int)Math.Round(point.X), (int)Math.Round(point.Y));
    }

    private FormsNotifyIcon CreateTrayIcon()
    {
        var trayMenu = new FormsContextMenuStrip();
        var showItem = new FormsToolStripMenuItem("Show Launcher");
        showItem.Click += (_, _) => Dispatcher.Invoke(ShowLauncher);
        var captureItem = new FormsToolStripMenuItem("Capture Region");
        captureItem.Click += (_, _) => Dispatcher.Invoke(StartCapture);
        var exitItem = new FormsToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => Dispatcher.Invoke(ExitFromTray);
        trayMenu.Items.Add(showItem);
        trayMenu.Items.Add(captureItem);
        trayMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        trayMenu.Items.Add(exitItem);

        var trayIcon = new FormsNotifyIcon
        {
            Text = "Degrande ScreenShot",
            Visible = true,
            Icon = System.Drawing.SystemIcons.Application,
            ContextMenuStrip = trayMenu,
        };

        trayIcon.MouseClick += (_, e) =>
        {
            if (e.Button == FormsMouseButtons.Left)
            {
                Dispatcher.Invoke(() =>
                {
                    if (IsVisible)
                    {
                        HideToTray();
                    }
                    else
                    {
                        ShowLauncher();
                    }
                });
            }
        };

        return trayIcon;
    }

    private void ExitFromTray()
    {
        _isExplicitExit = true;
        _trayIcon.Visible = false;
        Close();
    }

    private void OpenEditorDemo_Click(object sender, RoutedEventArgs e)
    {
        var demoImage = CreateDemoBitmap();
        var editor = new EditorWindow(demoImage);

        editor.Show();
        editor.Activate();
    }

    private static BitmapSource CreateDemoBitmap()
    {
        var visual = new DrawingVisual();
        using var context = visual.RenderOpen();
        context.DrawRectangle(new SolidColorBrush(Color.FromRgb(238, 241, 236)), null, new Rect(0, 0, 960, 540));
        context.DrawRectangle(new SolidColorBrush(Color.FromRgb(252, 253, 251)), new Pen(new SolidColorBrush(Color.FromRgb(212, 217, 212)), 2), new Rect(48, 56, 864, 428));
        context.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(18, 91, 80)), null, new Rect(104, 118, 264, 160), 20, 20);
        context.DrawGeometry(new SolidColorBrush(Color.FromRgb(242, 162, 58)), null, Geometry.Parse("M 0 28 Q 78 -20 180 20 L 180 0 L 228 38 L 184 74 L 184 52 Q 90 18 0 60 Z"));
        var formattedText = new FormattedText(
            "Capture demo",
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI Variable Display"),
            36,
            Brushes.White,
            1.25);
        context.DrawText(formattedText, new Point(128, 162));

        var bitmap = new RenderTargetBitmap(960, 540, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private void PositionLikeTaskbarPopup()
    {
        var cursor = GetCursorPosition();
        var activeScreen = FormsScreen.FromPoint(new System.Drawing.Point((int)cursor.X, (int)cursor.Y));
        var screenBounds = activeScreen.Bounds;
        var workArea = activeScreen.WorkingArea;
        var popupWidth = ActualWidth > 0 ? ActualWidth : Width;
        var popupHeight = ActualHeight > 0 ? ActualHeight : Height;
        var taskbarEdge = GetTaskbarEdge(screenBounds, workArea);

        switch (taskbarEdge)
        {
            case TaskbarEdge.Left:
            {
                Left = workArea.Left + 4;
                var targetTop = IsCursorNearTaskbar(cursor, taskbarEdge, workArea)
                    ? cursor.Y - (popupHeight / 2)
                    : workArea.Top + ((workArea.Height - popupHeight) / 2);
                Top = Math.Clamp(targetTop, workArea.Top + 8, workArea.Bottom - popupHeight - 8);
                break;
            }
            case TaskbarEdge.Top:
            {
                var targetLeft = IsCursorNearTaskbar(cursor, taskbarEdge, workArea)
                    ? cursor.X - (popupWidth / 2)
                    : workArea.Left + ((workArea.Width - popupWidth) / 2);
                Left = Math.Clamp(targetLeft, workArea.Left + 8, workArea.Right - popupWidth - 8);
                Top = workArea.Top + 4;
                break;
            }
            case TaskbarEdge.Right:
            {
                Left = workArea.Right - popupWidth - 4;
                var targetTop = IsCursorNearTaskbar(cursor, taskbarEdge, workArea)
                    ? cursor.Y - (popupHeight / 2)
                    : workArea.Top + ((workArea.Height - popupHeight) / 2);
                Top = Math.Clamp(targetTop, workArea.Top + 8, workArea.Bottom - popupHeight - 8);
                break;
            }
            default:
            {
                var targetLeft = IsCursorNearTaskbar(cursor, taskbarEdge, workArea)
                    ? cursor.X - (popupWidth / 2)
                    : workArea.Left + ((workArea.Width - popupWidth) / 2);
                Left = Math.Clamp(targetLeft, workArea.Left + 8, workArea.Right - popupWidth - 8);
                Top = workArea.Bottom - popupHeight - 4;
                break;
            }
        }
    }

    private static TaskbarEdge GetTaskbarEdge(System.Drawing.Rectangle screenBounds, System.Drawing.Rectangle workArea)
    {
        if (workArea.Left > screenBounds.Left)
        {
            return TaskbarEdge.Left;
        }

        if (workArea.Top > screenBounds.Top)
        {
            return TaskbarEdge.Top;
        }

        if (workArea.Right < screenBounds.Right)
        {
            return TaskbarEdge.Right;
        }

        return TaskbarEdge.Bottom;
    }

    private static bool IsCursorNearTaskbar(Point cursor, TaskbarEdge taskbarEdge, System.Drawing.Rectangle workArea)
    {
        const double threshold = 96;
        return taskbarEdge switch
        {
            TaskbarEdge.Left => Math.Abs(cursor.X - workArea.Left) <= threshold,
            TaskbarEdge.Top => Math.Abs(cursor.Y - workArea.Top) <= threshold,
            TaskbarEdge.Right => Math.Abs(cursor.X - workArea.Right) <= threshold,
            _ => Math.Abs(cursor.Y - workArea.Bottom) <= threshold,
        };
    }

    private static Point GetCursorPosition()
    {
        GetCursorPos(out var point);
        return new Point(point.X, point.Y);
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint lpPoint);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    private enum TaskbarEdge
    {
        Left,
        Top,
        Right,
        Bottom,
    }
}