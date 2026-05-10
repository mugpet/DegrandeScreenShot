using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using DegrandeScreenShot.App.Services;
using Microsoft.Win32;
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
    private const string WindowsThemeRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string AppSettingsRegistryPath = @"Software\DegrandeScreenShot";
    private const string WindowsRunRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupPreferenceValueName = "StartWithWindows";
    private const string StartupRunValueName = "DegrandeScreenShot";
    private readonly List<GlobalHotKeyManager> _hotKeyManagers;
    private readonly ScreenCaptureService _screenCaptureService = new();
    private readonly System.Drawing.Icon _trayAppIcon;
    private readonly bool _ownsTrayAppIcon;
    private readonly FormsNotifyIcon _trayIcon;
    private bool _isExplicitExit;
    private SnapshotPreviewWindow? _previewWindow;
    private bool _startHiddenInTray;
    private bool _isUpdatingStartupToggle;
    private bool _hotKeysRegistered;

    public MainWindow(bool startHiddenInTray = false)
    {
        InitializeComponent();
        _startHiddenInTray = startHiddenInTray;
        ApplyLauncherTheme();
        EnsureStartupPreferenceInitialized();
        UpdateStartupToggle();
        _hotKeyManagers =
        [
            new GlobalHotKeyManager(this, ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt, Key.D4, BeginCaptureTypeSelectorFromHotKey),
            new GlobalHotKeyManager(this, ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt, Key.D5, BeginPromptCaptureFromHotKey),
            new GlobalHotKeyManager(this, ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt, Key.D6, BeginClipboardCaptureFromHotKey),
            new GlobalHotKeyManager(this, ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt, Key.D7, BeginEditorCaptureFromHotKey),
            new GlobalHotKeyManager(this, ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt, Key.D8, BeginClipboardEditorFromHotKey),
            new GlobalHotKeyManager(this, ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt, Key.D9, BeginScrollingWindowCaptureFromHotKey),
        ];

        if (_startHiddenInTray)
        {
            Opacity = 0;
            ShowInTaskbar = false;
        }

        (_trayAppIcon, _ownsTrayAppIcon) = LoadTrayAppIcon();
        _trayIcon = CreateTrayIcon();
        SourceInitialized += MainWindow_SourceInitialized;
        Loaded += MainWindow_Loaded;
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        StateChanged += MainWindow_StateChanged;
        Closing += MainWindow_Closing;
        Closed += MainWindow_Closed;
        SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        RegisterGlobalHotKeys();
    }

    internal void InitializeHiddenTrayMode()
    {
        _startHiddenInTray = false;
        ShowInTaskbar = false;
        RegisterGlobalHotKeys();
    }

    private void RegisterGlobalHotKeys()
    {
        if (_hotKeysRegistered)
        {
            return;
        }

        _hotKeysRegistered = true;
        try
        {
            foreach (var hotKeyManager in _hotKeyManagers)
            {
                hotKeyManager.Register();
            }
        }
        catch
        {
            _hotKeysRegistered = false;
            throw;
        }
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _trayIcon.Visible = false;
        _trayIcon.Dispose();

        if (_ownsTrayAppIcon)
        {
            _trayAppIcon.Dispose();
        }

        foreach (var hotKeyManager in _hotKeyManagers)
        {
            hotKeyManager.Dispose();
        }

        SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
    }

    private void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        Dispatcher.InvokeAsync(ApplyLauncherTheme);
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

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        HideToTray();
        e.Handled = true;
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

    private void ApplyLauncherTheme()
    {
        var palette = IsSystemLightTheme() ? LauncherPalette.Light : LauncherPalette.Dark;
        SetBrushColor("LauncherBackgroundBrush", palette.Background);
        SetBrushColor("LauncherPanelBrush", palette.Panel);
        SetBrushColor("LauncherPanelBorderBrush", palette.PanelBorder);
        SetBrushColor("LauncherHeroBrush", palette.Hero);
        SetBrushColor("LauncherHeroMutedBrush", palette.HeroMuted);
        SetBrushColor("LauncherHeroTextBrush", palette.HeroText);
        SetBrushColor("LauncherCardBrush", palette.Card);
        SetBrushColor("LauncherSubtleCardBrush", palette.SubtleCard);
        SetBrushColor("LauncherTextBrush", palette.Text);
        SetBrushColor("LauncherMutedTextBrush", palette.MutedText);
        SetBrushColor("LauncherShortcutBrush", palette.Shortcut);
        SetBrushColor("LauncherShortcutTextBrush", palette.ShortcutText);
        SetBrushColor("LauncherShortcutWarmBrush", palette.ShortcutWarm);
        SetBrushColor("LauncherShortcutWarmTextBrush", palette.ShortcutWarmText);
        SetBrushColor("LauncherShortcutPurpleBrush", palette.ShortcutPurple);
        SetBrushColor("LauncherShortcutPurpleTextBrush", palette.ShortcutPurpleText);
        SetBrushColor("LauncherCloseButtonBrush", palette.CloseButton);
        SetBrushColor("LauncherCloseButtonBorderBrush", palette.CloseButtonBorder);
    }

    private void SetBrushColor(string resourceKey, Color color)
    {
        Resources[resourceKey] = new SolidColorBrush(color);
    }

    private static bool IsSystemLightTheme()
    {
        using var personalizeKey = Registry.CurrentUser.OpenSubKey(WindowsThemeRegistryPath);
        var themeValue = personalizeKey?.GetValue("AppsUseLightTheme");
        return themeValue is not int intValue || intValue != 0;
    }

    private static void EnsureStartupPreferenceInitialized()
    {
        using var settingsKey = Registry.CurrentUser.CreateSubKey(AppSettingsRegistryPath);
        if (settingsKey?.GetValue(StartupPreferenceValueName) is null)
        {
            settingsKey?.SetValue(StartupPreferenceValueName, 1, RegistryValueKind.DWord);
            SetStartWithWindowsEnabled(true);
            return;
        }

        if (IsStartWithWindowsPreferred())
        {
            SetStartWithWindowsEnabled(true);
        }
    }

    private static bool IsStartWithWindowsPreferred()
    {
        using var settingsKey = Registry.CurrentUser.OpenSubKey(AppSettingsRegistryPath);
        var preferenceValue = settingsKey?.GetValue(StartupPreferenceValueName);
        return preferenceValue switch
        {
            int intValue => intValue != 0,
            string stringValue => string.Equals(stringValue, "true", StringComparison.OrdinalIgnoreCase) || stringValue == "1",
            _ => true,
        };
    }

    private void UpdateStartupToggle()
    {
        _isUpdatingStartupToggle = true;
        StartupToggleButton.IsChecked = IsStartWithWindowsPreferred();
        StartupToggleButton.ToolTip = StartupToggleButton.IsChecked == true
            ? "Start with Windows is on"
            : "Start with Windows is off";
        _isUpdatingStartupToggle = false;
    }

    private void StartupToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingStartupToggle)
        {
            return;
        }

        var isEnabled = StartupToggleButton.IsChecked == true;
        SaveStartupPreference(isEnabled);
        SetStartWithWindowsEnabled(isEnabled);
        UpdateStartupToggle();
    }

    private static void SaveStartupPreference(bool isEnabled)
    {
        using var settingsKey = Registry.CurrentUser.CreateSubKey(AppSettingsRegistryPath);
        settingsKey?.SetValue(StartupPreferenceValueName, isEnabled ? 1 : 0, RegistryValueKind.DWord);
    }

    private static void SetStartWithWindowsEnabled(bool isEnabled)
    {
        using var runKey = Registry.CurrentUser.CreateSubKey(WindowsRunRegistryPath);
        if (runKey is null)
        {
            return;
        }

        if (isEnabled)
        {
            runKey.SetValue(StartupRunValueName, QuoteCommandPath(GetExecutablePath()), RegistryValueKind.String);
            return;
        }

        runKey.DeleteValue(StartupRunValueName, throwOnMissingValue: false);
    }

    private static string GetExecutablePath()
    {
        return Environment.ProcessPath ?? System.Reflection.Assembly.GetEntryAssembly()?.Location ?? string.Empty;
    }

    private static string QuoteCommandPath(string path)
    {
        return $"\"{path}\"";
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

    private void BeginPromptCaptureFromHotKey()
    {
        Dispatcher.Invoke(() => StartCapture(CaptureLaunchMode.ChooseAction));
    }

    private void BeginCaptureTypeSelectorFromHotKey()
    {
        Dispatcher.Invoke(ShowCaptureTypeSelector);
    }

    private void BeginClipboardCaptureFromHotKey()
    {
        Dispatcher.Invoke(() => StartCapture(CaptureLaunchMode.CopyToClipboard));
    }

    private void BeginEditorCaptureFromHotKey()
    {
        Dispatcher.Invoke(() => StartCapture(CaptureLaunchMode.OpenEditor));
    }

    private void BeginClipboardEditorFromHotKey()
    {
        Dispatcher.Invoke(OpenClipboardInEditor);
    }

    private void BeginScrollingWindowCaptureFromHotKey()
    {
        Dispatcher.Invoke(StartScrollingWindowCapture);
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

            if (overlay.CaptureResult is null)
            {
                return;
            }

            ShowCapturePreview(overlay.CaptureResult.Image);

            if (overlay.CaptureResult.Action == PostCaptureAction.Edit)
            {
                OpenEditor(overlay.CaptureResult.Image);
            }
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(this, exception.Message, "Capture failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void StartScrollingWindowCapture()
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
            var overlay = new CaptureOverlayWindow(captureFrame, CaptureLaunchMode.OpenEditor, CaptureSelectionMode.ScrollTarget);
            overlay.ContentRendered += (_, _) => RestoreCursor();
            var result = overlay.ShowDialog();
            RestoreCursor();

            if (result != true || overlay.SelectedScrollTarget is not { } scrollTarget)
            {
                return;
            }

            var image = _screenCaptureService.CaptureScrollingWindow(scrollTarget.Window.Handle, ToScreenRectangle(captureFrame, scrollTarget.Bounds));
            ShowCapturePreview(image);
            OpenEditor(image);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(this, exception.Message, "Scrolling capture failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenClipboardInEditor()
    {
        try
        {
            OpenEditor(GetClipboardEditorImage());
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(this, exception.Message, "Clipboard import failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenEditor(BitmapSource image)
    {
        var editor = new EditorWindow(image);
        editor.Show();
        editor.Activate();
    }

    private static System.Drawing.Rectangle ToScreenRectangle(CaptureFrame captureFrame, DegrandeScreenShot.Core.PixelRect overlayRect)
    {
        return new System.Drawing.Rectangle(
            overlayRect.X + captureFrame.VirtualLeft,
            overlayRect.Y + captureFrame.VirtualTop,
            overlayRect.Width,
            overlayRect.Height);
    }

    private void ShowCaptureTypeSelector()
    {
        var selector = new CaptureTypeSelectorWindow(GetCursorPosition());
        var result = selector.ShowDialog();
        if (result != true || selector.SelectedAction is not { } action)
        {
            return;
        }

        switch (action)
        {
            case CaptureTypeSelection.ChooseAction:
                StartCapture(CaptureLaunchMode.ChooseAction);
                break;
            case CaptureTypeSelection.CopyRegion:
                StartCapture(CaptureLaunchMode.CopyToClipboard);
                break;
            case CaptureTypeSelection.OpenEditor:
                StartCapture(CaptureLaunchMode.OpenEditor);
                break;
            case CaptureTypeSelection.ClipboardEditor:
                OpenClipboardInEditor();
                break;
            case CaptureTypeSelection.ScrollingWindow:
                StartScrollingWindowCapture();
                break;
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

    private static (System.Drawing.Icon Icon, bool OwnsIcon) LoadTrayAppIcon()
    {
        var iconResource = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/AppIcon.ico", UriKind.Absolute));
        if (iconResource?.Stream is not null)
        {
            using var iconStream = iconResource.Stream;
            return (new System.Drawing.Icon(iconStream), true);
        }

        return (System.Drawing.SystemIcons.Application, false);
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
            Icon = _trayAppIcon,
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
        OpenEditor(demoImage);
    }

    private static BitmapSource GetClipboardEditorImage()
    {
        if (System.Windows.Clipboard.ContainsImage())
        {
            var clipboardImage = System.Windows.Clipboard.GetImage();
            if (clipboardImage is not null)
            {
                if (clipboardImage.CanFreeze && !clipboardImage.IsFrozen)
                {
                    clipboardImage.Freeze();
                }

                return clipboardImage;
            }
        }

        var (title, content) = ReadClipboardSummary();
        return CreateClipboardSummaryBitmap(title, content);
    }

    private static (string Title, string Content) ReadClipboardSummary()
    {
        var dataObject = System.Windows.Clipboard.GetDataObject();
        if (dataObject is null)
        {
            return ("Clipboard", "Clipboard is empty.");
        }

        if (System.Windows.Clipboard.ContainsText())
        {
            var text = System.Windows.Clipboard.GetText();
            return ("Clipboard text", NormalizeClipboardPreviewText(text));
        }

        if (dataObject.GetDataPresent(DataFormats.FileDrop) && dataObject.GetData(DataFormats.FileDrop) is string[] filePaths && filePaths.Length > 0)
        {
            return ("Clipboard files", NormalizeClipboardPreviewText(string.Join(Environment.NewLine, filePaths)));
        }

        if (dataObject.GetDataPresent(DataFormats.Html) && dataObject.GetData(DataFormats.Html) is string html && !string.IsNullOrWhiteSpace(html))
        {
            return ("Clipboard HTML", NormalizeClipboardPreviewText(html));
        }

        if (dataObject.GetDataPresent(DataFormats.Rtf) && dataObject.GetData(DataFormats.Rtf) is string rtf && !string.IsNullOrWhiteSpace(rtf))
        {
            return ("Clipboard RTF", NormalizeClipboardPreviewText(rtf));
        }

        foreach (var format in dataObject.GetFormats())
        {
            if (!TryConvertClipboardFormatToText(dataObject, format, out var text))
            {
                continue;
            }

            return ($"Clipboard {format}", NormalizeClipboardPreviewText(text));
        }

        var availableFormats = dataObject.GetFormats();
        if (availableFormats.Length == 0)
        {
            return ("Clipboard", "Clipboard is empty.");
        }

        return (
            "Clipboard contents",
            NormalizeClipboardPreviewText(
                "This clipboard data could not be converted directly to text. Available formats:" + Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, availableFormats)));
    }

    private static bool TryConvertClipboardFormatToText(System.Windows.IDataObject dataObject, string format, out string text)
    {
        text = string.Empty;

        if (string.Equals(format, DataFormats.Bitmap, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        object? value;
        try
        {
            value = dataObject.GetData(format);
        }
        catch
        {
            return false;
        }

        switch (value)
        {
            case null:
                return false;
            case string stringValue when !string.IsNullOrWhiteSpace(stringValue):
                text = stringValue;
                return true;
            case string[] stringArray when stringArray.Length > 0:
                text = string.Join(Environment.NewLine, stringArray);
                return true;
            default:
                var valueText = value.ToString();
                if (string.IsNullOrWhiteSpace(valueText) || string.Equals(valueText, value.GetType().FullName, StringComparison.Ordinal))
                {
                    return false;
                }

                text = valueText;
                return true;
        }
    }

    private static string NormalizeClipboardPreviewText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "Clipboard contents are empty.";
        }

        return text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Replace("\0", string.Empty, StringComparison.Ordinal);
    }

    private static BitmapSource CreateClipboardSummaryBitmap(string title, string content)
    {
        const int maxImageBytes = 10 * 1024 * 1024;
        const string truncationNotice = "\n\n[truncated to keep the generated image under 10 MB]";

        if (TryRenderClipboardSummaryBitmap(title, content, out var fullBitmap, out var fullBitmapSize) && fullBitmapSize <= maxImageBytes)
        {
            return fullBitmap;
        }

        var low = 0;
        var high = content.Length;
        BitmapSource? bestBitmap = null;

        while (low <= high)
        {
            var mid = low + ((high - low) / 2);
            var candidateContent = content[..mid].TrimEnd() + truncationNotice;

            if (TryRenderClipboardSummaryBitmap(title, candidateContent, out var candidateBitmap, out var candidateSize) && candidateSize <= maxImageBytes)
            {
                bestBitmap = candidateBitmap;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        if (bestBitmap is not null)
        {
            return bestBitmap;
        }

        TryRenderClipboardSummaryBitmap(title, truncationNotice.TrimStart(), out var fallbackBitmap, out _);
        return fallbackBitmap;
    }

    private static bool TryRenderClipboardSummaryBitmap(
        string title,
        string candidateContent,
        out BitmapSource bitmapSource,
        out long pngByteCount)
    {
        const int imageWidth = 1100;
        const int outerPadding = 48;
        const int panelPadding = 28;
        const int titleGap = 18;
        const int minimumImageHeight = 420;
        const int maximumImageHeight = 30000;

        bitmapSource = null!;
        pngByteCount = long.MaxValue;

        var bodyWidth = imageWidth - (outerPadding * 2) - (panelPadding * 2);
        using var titleFont = new System.Drawing.Font("Segoe UI", 24, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Pixel);
        using var bodyFont = new System.Drawing.Font("Consolas", 18, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Pixel);
        using var measureBitmap = new System.Drawing.Bitmap(1, 1);
        using var measureGraphics = System.Drawing.Graphics.FromImage(measureBitmap);
        using var stringFormat = new System.Drawing.StringFormat
        {
            Trimming = System.Drawing.StringTrimming.Word,
        };

        var titleLayout = new System.Drawing.SizeF(bodyWidth, 200);
        var bodyLayout = new System.Drawing.SizeF(bodyWidth, maximumImageHeight);
        var titleSize = measureGraphics.MeasureString(title, titleFont, titleLayout, stringFormat);
        var bodySize = measureGraphics.MeasureString(candidateContent, bodyFont, bodyLayout, stringFormat);
        var panelHeight = (panelPadding * 2) + (int)Math.Ceiling(titleSize.Height) + titleGap + (int)Math.Ceiling(bodySize.Height);
        var imageHeight = Math.Max(minimumImageHeight, panelHeight + (outerPadding * 2));

        if (imageHeight > maximumImageHeight)
        {
            return false;
        }

        using var bitmap = new System.Drawing.Bitmap(imageWidth, imageHeight, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using var graphics = System.Drawing.Graphics.FromImage(bitmap);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        graphics.Clear(System.Drawing.Color.FromArgb(244, 247, 251));

        var panelRect = new System.Drawing.Rectangle(outerPadding, outerPadding, imageWidth - (outerPadding * 2), imageHeight - (outerPadding * 2));
        using var panelPath = CreateRoundedRectanglePath(panelRect, 26);
        using var panelBrush = new System.Drawing.SolidBrush(System.Drawing.Color.White);
        using var panelBorderPen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(213, 224, 234), 2f);
        graphics.FillPath(panelBrush, panelPath);
        graphics.DrawPath(panelBorderPen, panelPath);

        var accentRect = new System.Drawing.Rectangle(outerPadding + panelPadding, outerPadding + panelPadding, 160, 12);
        using var accentPath = CreateRoundedRectanglePath(accentRect, 6);
        using var accentBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(12, 142, 197));
        graphics.FillPath(accentBrush, accentPath);

        using var titleBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(29, 38, 49));
        using var bodyBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(86, 101, 116));
        var titleRect = new System.Drawing.RectangleF(outerPadding + panelPadding, outerPadding + panelPadding + 28, bodyWidth, titleSize.Height + 4);
        var bodyRect = new System.Drawing.RectangleF(titleRect.Left, titleRect.Bottom + titleGap, bodyWidth, imageHeight - titleRect.Bottom - titleGap - outerPadding - panelPadding);
        graphics.DrawString(title, titleFont, titleBrush, titleRect, stringFormat);
        graphics.DrawString(candidateContent, bodyFont, bodyBrush, bodyRect, stringFormat);

        using var pngStream = new MemoryStream();
        bitmap.Save(pngStream, System.Drawing.Imaging.ImageFormat.Png);
        pngByteCount = pngStream.Length;

        var handle = bitmap.GetHbitmap();
        try
        {
            bitmapSource = Imaging.CreateBitmapSourceFromHBitmap(handle, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            bitmapSource.Freeze();
            return true;
        }
        finally
        {
            DeleteObject(handle);
        }
    }

    private static System.Drawing.Drawing2D.GraphicsPath CreateRoundedRectanglePath(System.Drawing.Rectangle bounds, int radius)
    {
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        var diameter = radius * 2;

        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static BitmapSource CreateDemoBitmap()
    {
        var visual = new DrawingVisual();
        using var context = visual.RenderOpen();
        context.DrawRectangle(new SolidColorBrush(Color.FromRgb(244, 247, 251)), null, new Rect(0, 0, 960, 540));
        context.DrawRectangle(new SolidColorBrush(Colors.White), new Pen(new SolidColorBrush(Color.FromRgb(213, 224, 234)), 2), new Rect(48, 56, 864, 428));
        context.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(12, 142, 197)), null, new Rect(104, 118, 264, 160), 20, 20);
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

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);

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

    private sealed record LauncherPalette(
        Color Background,
        Color Panel,
        Color PanelBorder,
        Color Hero,
        Color HeroMuted,
        Color HeroText,
        Color Card,
        Color SubtleCard,
        Color Text,
        Color MutedText,
        Color Shortcut,
        Color ShortcutText,
        Color ShortcutWarm,
        Color ShortcutWarmText,
        Color ShortcutPurple,
        Color ShortcutPurpleText,
        Color CloseButton,
        Color CloseButtonBorder)
    {
        internal static readonly LauncherPalette Light = new(
            Background: Color.FromRgb(0xF4, 0xF7, 0xFB),
            Panel: Colors.White,
            PanelBorder: Color.FromRgb(0xD5, 0xE0, 0xEA),
            Hero: Color.FromRgb(0x1D, 0x26, 0x31),
            HeroMuted: Color.FromRgb(0xAF, 0xC0, 0xCE),
            HeroText: Color.FromRgb(0xF4, 0xF7, 0xFB),
            Card: Colors.White,
            SubtleCard: Color.FromRgb(0xF0, 0xF5, 0xF9),
            Text: Color.FromRgb(0x1D, 0x26, 0x31),
            MutedText: Color.FromRgb(0x6C, 0x79, 0x87),
            Shortcut: Color.FromRgb(0xEA, 0xF4, 0xFA),
            ShortcutText: Color.FromRgb(0x0B, 0x6F, 0x9A),
            ShortcutWarm: Color.FromRgb(0xFD, 0xF3, 0xE7),
            ShortcutWarmText: Color.FromRgb(0xA8, 0x5B, 0x1A),
            ShortcutPurple: Color.FromRgb(0xEF, 0xEA, 0xF8),
            ShortcutPurpleText: Color.FromRgb(0x5E, 0x4D, 0x91),
            CloseButton: Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF),
            CloseButtonBorder: Color.FromArgb(0x2F, 0xFF, 0xFF, 0xFF));

        internal static readonly LauncherPalette Dark = new(
            Background: Color.FromRgb(0x1B, 0x20, 0x28),
            Panel: Color.FromRgb(0x20, 0x26, 0x30),
            PanelBorder: Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF),
            Hero: Color.FromRgb(0x11, 0x18, 0x21),
            HeroMuted: Color.FromRgb(0x9F, 0xB2, 0xC3),
            HeroText: Color.FromRgb(0xF4, 0xF7, 0xFB),
            Card: Color.FromRgb(0x26, 0x2D, 0x37),
            SubtleCard: Color.FromRgb(0x24, 0x2B, 0x35),
            Text: Color.FromRgb(0xF4, 0xF7, 0xFB),
            MutedText: Color.FromRgb(0xA9, 0xB7, 0xC6),
            Shortcut: Color.FromArgb(0x28, 0x1A, 0xA9, 0xD8),
            ShortcutText: Color.FromRgb(0x9F, 0xDD, 0xF4),
            ShortcutWarm: Color.FromArgb(0x28, 0xF0, 0x8C, 0x00),
            ShortcutWarmText: Color.FromRgb(0xF7, 0xC2, 0x7B),
            ShortcutPurple: Color.FromArgb(0x2E, 0x7C, 0x3A, 0xED),
            ShortcutPurpleText: Color.FromRgb(0xC8, 0xB9, 0xFF),
            CloseButton: Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF),
            CloseButtonBorder: Color.FromArgb(0x32, 0xFF, 0xFF, 0xFF));
    }
}