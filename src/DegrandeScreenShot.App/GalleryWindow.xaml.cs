using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DegrandeScreenShot.App.Services;
using Microsoft.Win32;

namespace DegrandeScreenShot.App;

public partial class GalleryWindow : Window
{
    private const string WindowsThemeRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const int DwmUseImmersiveDarkModeAttribute = 20;
    private const int DwmBorderColorAttribute = 34;
    private const int DwmCaptionColorAttribute = 35;
    private const int DwmTextColorAttribute = 36;
    private const uint DwmColorDefault = 0xFFFFFFFF;
    private readonly ScreenshotLibraryService _screenshotLibrary;
    private readonly EditorPreferencesStore _preferencesStore = new();
    private FileSystemWatcher? _watcher;
    private readonly DispatcherTimer _refreshTimer;
    private readonly SemaphoreSlim _thumbnailWorkers = new(2);
    private readonly Dictionary<Image, CancellationTokenSource> _thumbnailRequests = [];
    private readonly HashSet<Image> _unavailableThumbnails = [];
    private readonly Dictionary<ScreenshotLibraryItem, BitmapSource> _thumbnailCache = [];
    private readonly Queue<ScreenshotLibraryItem> _thumbnailOrder = [];
    private readonly HashSet<string> _openingPaths = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<ScreenshotLibraryItem>? _screenshots;
    private GalleryThemePreference? _themeMode;
    private DateTime _groupDate;
    private int _columns = 1;
    private bool _refreshRunning;
    private bool _refreshPending;
    private int _refreshPosted;
    private bool _closed;
    private string _libraryMessage = "Loading screenshots…";
    private Point? _screenshotDragStart;
    private GalleryScreenshotViewModel? _screenshotDragCandidate;

    public GalleryWindow(ScreenshotLibraryService screenshotLibrary)
    {
        InitializeComponent();
        _screenshotLibrary = screenshotLibrary;
        ApplyTheme();

        LibraryPathText.Text = _screenshotLibrary.RootDirectory;
        LibraryPathText.ToolTip = _screenshotLibrary.RootDirectory;

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(300),
        };
        _refreshTimer.Tick += RefreshTimer_Tick;

        Loaded += GalleryWindow_Loaded;
        Activated += GalleryWindow_Activated;
        SourceInitialized += GalleryWindow_SourceInitialized;
        Closed += GalleryWindow_Closed;
        SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
        EditorPreferencesStore.PreferencesChanged += EditorPreferencesStore_PreferencesChanged;
    }

    private void GalleryWindow_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshGallery();
    }

    private void GalleryWindow_Activated(object? sender, EventArgs e)
    {
        if (IsLoaded && !_refreshRunning)
        {
            ApplyTheme();
            RefreshGallery();
        }
    }

    private void GalleryWindow_SourceInitialized(object? sender, EventArgs e)
    {
        ApplyNativeTitleBarTheme();
    }

    private void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        Dispatcher.InvokeAsync(ApplyTheme);
    }

    private void EditorPreferencesStore_PreferencesChanged(object? sender, EventArgs e)
    {
        Dispatcher.InvokeAsync(ApplyTheme);
    }

    private void ApplyTheme()
    {
        var mode = ResolveThemeMode();
        if (_themeMode == mode)
        {
            return;
        }

        _themeMode = mode;
        var isDark = mode == GalleryThemePreference.Dark;
        var palette = isDark ? GalleryPalette.Dark : GalleryPalette.Light;

        SetBrushColor("GalleryBackgroundBrush", palette.Background);
        SetBrushColor("GalleryHeaderBrush", palette.Header);
        SetBrushColor("GalleryCardBrush", palette.Card);
        SetBrushColor("GalleryBorderBrush", palette.Border);
        SetBrushColor("GalleryTextBrush", palette.Text);
        SetBrushColor("GalleryMutedTextBrush", palette.MutedText);
        SetBrushColor("GalleryAccentBrush", palette.Accent);
        SetBrushColor("GalleryAccentBorderBrush", palette.AccentBorder);
        SetBrushColor("GalleryAccentSubtleBrush", palette.AccentSubtle);
        SetBrushColor("GalleryActionHoverBrush", palette.ActionHover);
        SetBrushColor("GalleryThumbnailBackdropBrush", palette.ThumbnailBackdrop);
        ApplyNativeTitleBarTheme();
    }

    private GalleryThemePreference ResolveThemeMode()
    {
        var preference = _preferencesStore.Load().ThemePreference;
        if (Enum.TryParse<GalleryThemePreference>(preference, ignoreCase: true, out var parsed)
            && parsed != GalleryThemePreference.System)
        {
            return parsed;
        }

        return IsSystemLightTheme() ? GalleryThemePreference.Light : GalleryThemePreference.Dark;
    }

    private static bool IsSystemLightTheme()
    {
        using var personalizeKey = Registry.CurrentUser.OpenSubKey(WindowsThemeRegistryPath);
        var themeValue = personalizeKey?.GetValue("AppsUseLightTheme");
        return themeValue is not int intValue || intValue != 0;
    }

    private void SetBrushColor(string resourceKey, Color color)
    {
        Resources[resourceKey] = new SolidColorBrush(color);
    }

    private void ApplyNativeTitleBarTheme()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var isDark = _themeMode == GalleryThemePreference.Dark;
        var palette = isDark ? GalleryPalette.Dark : GalleryPalette.Light;
        var useDarkMode = isDark ? 1 : 0;
        _ = DwmSetWindowAttribute(handle, DwmUseImmersiveDarkModeAttribute, ref useDarkMode, sizeof(int));

        var captionColor = ToColorRef(palette.Header);
        var textColor = ToColorRef(palette.Text);
        var borderColor = DwmColorDefault;
        _ = DwmSetWindowAttribute(handle, DwmCaptionColorAttribute, ref captionColor, sizeof(uint));
        _ = DwmSetWindowAttribute(handle, DwmTextColorAttribute, ref textColor, sizeof(uint));
        _ = DwmSetWindowAttribute(handle, DwmBorderColorAttribute, ref borderColor, sizeof(uint));
    }

    private static uint ToColorRef(Color color)
    {
        return (uint)(color.R | (color.G << 8) | (color.B << 16));
    }

    private FileSystemWatcher WatchLibrary()
    {
        Directory.CreateDirectory(_screenshotLibrary.RootDirectory);
        var watcher = new FileSystemWatcher(_screenshotLibrary.RootDirectory, "*.png")
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.LastWrite | NotifyFilters.Size,
        };
        watcher.Created += Library_Changed;
        watcher.Changed += Library_Changed;
        watcher.Deleted += Library_Changed;
        watcher.Renamed += Library_Changed;
        watcher.Error += Library_WatchError;
        try
        {
            watcher.EnableRaisingEvents = true;
            return watcher;
        }
        catch
        {
            watcher.Dispose();
            throw;
        }
    }

    private async void RefreshGallery()
    {
        if (_closed)
        {
            return;
        }

        if (_refreshRunning)
        {
            _refreshPending = true;
            return;
        }

        _refreshRunning = true;
        try
        {
            if (_watcher is null)
            {
                var watcher = await Task.Run(WatchLibrary);
                if (_closed)
                {
                    watcher.Dispose();
                    return;
                }

                _watcher = watcher;
            }

            var items = await Task.Run(_screenshotLibrary.GetScreenshots);
            if (!_closed && (_screenshots is null || !items.SequenceEqual(_screenshots) || _groupDate != DateTime.Today))
            {
                _screenshots = items;
                RebuildRows();
            }
            else if (!_closed)
            {
                foreach (var image in _unavailableThumbnails.ToArray())
                {
                    LoadThumbnail(image);
                }
            }

            _libraryMessage = string.Empty;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Win32Exception or ArgumentException)
        {
            _libraryMessage = "Couldn't read the library. Click Refresh to retry.";
        }
        finally
        {
            _refreshRunning = false;
            if (!_closed)
            {
                UpdateStatus();
                if (_refreshPending)
                {
                    _refreshPending = false;
                    ScheduleRefresh();
                }
            }
        }
    }

    private void Gallery_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.ViewportWidthChange == 0 || e.ViewportWidth <= 0)
        {
            return;
        }

        // Each 240-DIP card has a 16-DIP trailing margin.
        var columns = Math.Max(1, (int)(e.ViewportWidth / 256));
        if (columns != _columns)
        {
            _columns = columns;
            RebuildRows();
        }
    }

    private void RebuildRows()
    {
        if (_screenshots is null)
        {
            return;
        }

        _groupDate = DateTime.Today;
        var rows = new List<GalleryRow>();
        foreach (var day in _screenshots.GroupBy(item => item.CreatedAt.Date))
        {
            string? header = FormatDayHeader(day.Key, _groupDate);
            var count = day.Count();
            foreach (var row in day.Select(item => new GalleryScreenshotViewModel(item)).Chunk(_columns))
            {
                rows.Add(new GalleryRow(header, count, row));
                header = null;
            }
        }

        GalleryRows.ItemsSource = rows;
        EmptyState.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Thumbnail_Loaded(object sender, RoutedEventArgs e) => LoadThumbnail((Image)sender);

    private void Thumbnail_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        var image = (Image)sender;
        if (image.IsLoaded)
        {
            LoadThumbnail(image);
        }
    }

    private void Thumbnail_Unloaded(object sender, RoutedEventArgs e) => ResetThumbnail((Image)sender);

    private void ResetThumbnail(Image image)
    {
        if (_thumbnailRequests.Remove(image, out var pending))
        {
            pending.Cancel();
        }

        _unavailableThumbnails.Remove(image);
        image.Tag = null;
        image.Source = null;
    }

    private async void LoadThumbnail(Image image)
    {
        ResetThumbnail(image);
        if (_closed || image.DataContext is not GalleryScreenshotViewModel screenshot)
        {
            return;
        }

        var item = screenshot.Item;
        if (_thumbnailCache.TryGetValue(item, out var cached))
        {
            image.Source = cached;
            return;
        }

        using var request = new CancellationTokenSource();
        _thumbnailRequests[image] = request;
        try
        {
            await _thumbnailWorkers.WaitAsync(request.Token);
            try
            {
                var thumbnail = await Task.Run(() => ScreenshotLibraryService.LoadGalleryThumbnail(item.Path), request.Token);
                if (!request.IsCancellationRequested && !_closed)
                {
                    image.Source = thumbnail;
                    if (_thumbnailCache.TryAdd(item, thumbnail))
                    {
                        _thumbnailOrder.Enqueue(item);
                        if (_thumbnailOrder.Count > 64)
                        {
                            _thumbnailCache.Remove(_thumbnailOrder.Dequeue());
                        }
                    }
                }
            }
            finally
            {
                _thumbnailWorkers.Release();
            }
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        {
            // The row was recycled or the gallery was closed.
        }
        catch (Exception exception) when (IsImageReadError(exception))
        {
            // An external editor may still be writing the PNG. Keep the card usable.
            if (!request.IsCancellationRequested && !_closed)
            {
                image.Tag = "Unavailable";
                _unavailableThumbnails.Add(image);
            }
        }
        finally
        {
            if (!request.IsCancellationRequested)
            {
                _thumbnailRequests.Remove(image);
            }
        }
    }

    private static bool IsImageReadError(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or FormatException or COMException;

    private void UpdateStatus() =>
        StatusText.Text = _openingPaths.Count > 0 ? "Opening screenshot…" : _libraryMessage;

    private static string FormatDayHeader(DateTime day, DateTime today)
    {
        if (day == today)
        {
            return "Today";
        }

        if (day == today.AddDays(-1))
        {
            return "Yesterday";
        }

        return day.ToString("dddd, d MMMM yyyy", CultureInfo.CurrentCulture);
    }

    private async void Screenshot_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: GalleryScreenshotViewModel screenshot }
            || !_openingPaths.Add(screenshot.Path))
        {
            return;
        }

        UpdateStatus();
        try
        {
            var image = await Task.Run(() => ScreenshotLibraryService.LoadImage(screenshot.Path));
            if (_closed)
            {
                return;
            }

            var editor = new EditorWindow(image, screenshot.Path);
            editor.Show();
            editor.Activate();
        }
        catch (Exception exception) when (IsImageReadError(exception))
        {
            if (!_closed)
            {
                MessageBox.Show(
                    this,
                    $"The screenshot could not be opened.\n\n{exception.Message}",
                    "Open screenshot",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        finally
        {
            _openingPaths.Remove(screenshot.Path);
            UpdateStatus();
        }
    }

    private void Screenshot_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.StylusDevice is not null)
        {
            _screenshotDragStart = null;
            _screenshotDragCandidate = null;
            return;
        }

        _screenshotDragStart = e.GetPosition(this);
        _screenshotDragCandidate = (sender as FrameworkElement)?.Tag as GalleryScreenshotViewModel;
    }

    private void Screenshot_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.StylusDevice is not null
            || e.LeftButton != MouseButtonState.Pressed
            || _screenshotDragStart is not { } dragStart
            || _screenshotDragCandidate is not { } screenshot)
        {
            return;
        }

        var current = e.GetPosition(this);
        if (Math.Abs(current.X - dragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(current.Y - dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _screenshotDragStart = null;
        _screenshotDragCandidate = null;
        var dragData = new DataObject(DataFormats.FileDrop, new[] { screenshot.Path });
        _ = DragDrop.DoDragDrop((DependencyObject)sender, dragData, DragDropEffects.Copy);
        e.Handled = true;
    }

    private async void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await Task.Run(() =>
            {
                Directory.CreateDirectory(_screenshotLibrary.RootDirectory);
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = _screenshotLibrary.RootDirectory,
                    UseShellExecute = true,
                });
            });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Win32Exception)
        {
            if (!_closed)
            {
                MessageBox.Show(this, exception.Message, "Open screenshot folder", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        RefreshGallery();
    }

    private void Library_Changed(object sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref _refreshPosted, 1) == 0)
        {
            Dispatcher.InvokeAsync(() =>
            {
                Interlocked.Exchange(ref _refreshPosted, 0);
                ScheduleRefresh();
            }, DispatcherPriority.Background);
        }
    }

    private void Library_WatchError(object sender, ErrorEventArgs e)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (_watcher == sender)
            {
                _watcher.Dispose();
                _watcher = null;
            }

            ScheduleRefresh();
        }, DispatcherPriority.Background);
    }

    private void ScheduleRefresh()
    {
        if (!_closed)
        {
            _refreshTimer.Stop();
            _refreshTimer.Start();
        }
    }

    private void RefreshTimer_Tick(object? sender, EventArgs e)
    {
        _refreshTimer.Stop();
        RefreshGallery();
    }

    private void GalleryWindow_Closed(object? sender, EventArgs e)
    {
        _closed = true;
        _refreshTimer.Stop();
        _refreshTimer.Tick -= RefreshTimer_Tick;

        _watcher?.Dispose();
        foreach (var request in _thumbnailRequests.Values.ToArray())
        {
            request.Cancel();
        }

        _thumbnailRequests.Clear();
        _unavailableThumbnails.Clear();
        _thumbnailCache.Clear();
        _thumbnailOrder.Clear();

        Loaded -= GalleryWindow_Loaded;
        Activated -= GalleryWindow_Activated;
        SourceInitialized -= GalleryWindow_SourceInitialized;
        Closed -= GalleryWindow_Closed;
        SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
        EditorPreferencesStore.PreferencesChanged -= EditorPreferencesStore_PreferencesChanged;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int attributeValue, int attributeSize);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref uint attributeValue, int attributeSize);

    private enum GalleryThemePreference
    {
        System,
        Dark,
        Light,
    }

    private sealed record GalleryPalette(
        Color Background,
        Color Header,
        Color Card,
        Color Border,
        Color Text,
        Color MutedText,
        Color Accent,
        Color AccentBorder,
        Color AccentSubtle,
        Color ActionHover,
        Color ThumbnailBackdrop)
    {
        internal static readonly GalleryPalette Light = new(
            Background: Color.FromRgb(0xF4, 0xF7, 0xFB),
            Header: Colors.White,
            Card: Colors.White,
            Border: Color.FromRgb(0xD5, 0xE0, 0xEA),
            Text: Color.FromRgb(0x1D, 0x26, 0x31),
            MutedText: Color.FromRgb(0x6C, 0x79, 0x87),
            Accent: Color.FromRgb(0x0A, 0x6F, 0x9A),
            AccentBorder: Color.FromRgb(0x0C, 0x8E, 0xC5),
            AccentSubtle: Color.FromRgb(0xEA, 0xF4, 0xFA),
            ActionHover: Color.FromRgb(0xEA, 0xF4, 0xFA),
            ThumbnailBackdrop: Color.FromRgb(0xE3, 0xE9, 0xEF));

        internal static readonly GalleryPalette Dark = new(
            Background: Color.FromRgb(0x1B, 0x20, 0x28),
            Header: Color.FromRgb(0x20, 0x26, 0x30),
            Card: Color.FromRgb(0x26, 0x2D, 0x37),
            Border: Color.FromRgb(0x3D, 0x47, 0x54),
            Text: Color.FromRgb(0xF4, 0xF7, 0xFB),
            MutedText: Color.FromRgb(0xA9, 0xB7, 0xC6),
            Accent: Color.FromRgb(0x9F, 0xDD, 0xF4),
            AccentBorder: Color.FromRgb(0x1A, 0xA9, 0xD8),
            AccentSubtle: Color.FromRgb(0x24, 0x3B, 0x48),
            ActionHover: Color.FromRgb(0x2A, 0x3A, 0x46),
            ThumbnailBackdrop: Color.FromRgb(0x11, 0x18, 0x21));
    }
}

internal sealed record GalleryRow(string? Header, int Count, IReadOnlyList<GalleryScreenshotViewModel> Screenshots)
{
    public Visibility HeaderVisibility => Header is null ? Visibility.Collapsed : Visibility.Visible;
}

internal sealed record GalleryScreenshotViewModel(ScreenshotLibraryItem Item)
{
    public string Path => Item.Path;
    public string DisplayName => System.IO.Path.GetFileNameWithoutExtension(Item.FileName);
    public string TimeText => Item.CreatedAt.ToString("t", CultureInfo.CurrentCulture);
}
