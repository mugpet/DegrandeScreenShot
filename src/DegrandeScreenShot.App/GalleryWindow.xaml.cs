using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
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
    private readonly FileSystemWatcher _watcher;
    private readonly DispatcherTimer _refreshTimer;

    public GalleryWindow(ScreenshotLibraryService screenshotLibrary)
    {
        InitializeComponent();
        _screenshotLibrary = screenshotLibrary;
        ApplyTheme();

        Directory.CreateDirectory(_screenshotLibrary.RootDirectory);
        LibraryPathText.Text = _screenshotLibrary.RootDirectory;
        LibraryPathText.ToolTip = _screenshotLibrary.RootDirectory;

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(300),
        };
        _refreshTimer.Tick += RefreshTimer_Tick;

        _watcher = new FileSystemWatcher(_screenshotLibrary.RootDirectory, "*.png")
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };
        _watcher.Created += Library_Changed;
        _watcher.Changed += Library_Changed;
        _watcher.Deleted += Library_Changed;
        _watcher.Renamed += Library_Changed;

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
        ApplyTheme();
        RefreshGallery();
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
        var isDark = ResolveThemeMode() == GalleryThemePreference.Dark;
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

        var isDark = ResolveThemeMode() == GalleryThemePreference.Dark;
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

    private void RefreshGallery()
    {
        var today = DateTime.Today;
        var groups = _screenshotLibrary
            .GetScreenshots()
            .GroupBy(item => item.CreatedAt.Date)
            .OrderByDescending(group => group.Key)
            .Select(group => new GalleryDayGroup(
                FormatDayHeader(group.Key, today),
                group.Select(CreateViewModel).Where(item => item is not null).Cast<GalleryScreenshotViewModel>().ToArray()))
            .Where(group => group.Count > 0)
            .ToArray();

        DayGroupsItemsControl.ItemsSource = groups;
        var isEmpty = groups.Length == 0;
        EmptyState.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
        GalleryScroll.Visibility = isEmpty ? Visibility.Collapsed : Visibility.Visible;
    }

    private static GalleryScreenshotViewModel? CreateViewModel(ScreenshotLibraryItem item)
    {
        try
        {
            var thumbnail = ScreenshotLibraryService.LoadImage(item.Path, decodePixelWidth: 480);
            return new GalleryScreenshotViewModel(
                item.Path,
                Path.GetFileNameWithoutExtension(item.FileName),
                item.CreatedAt.ToString("t", CultureInfo.CurrentCulture),
                thumbnail);
        }
        catch (IOException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

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

    private void Screenshot_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: GalleryScreenshotViewModel screenshot })
        {
            return;
        }

        try
        {
            var image = ScreenshotLibraryService.LoadImage(screenshot.Path);
            var editor = new EditorWindow(image, screenshot.Path);
            editor.Show();
            editor.Activate();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            MessageBox.Show(
                this,
                $"The screenshot could not be opened.\n\n{exception.Message}",
                "Open screenshot",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(_screenshotLibrary.RootDirectory);
        Process.Start(new ProcessStartInfo
        {
            FileName = _screenshotLibrary.RootDirectory,
            UseShellExecute = true,
        });
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        RefreshGallery();
    }

    private void Library_Changed(object sender, FileSystemEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(ScheduleRefresh));
    }

    private void ScheduleRefresh()
    {
        _refreshTimer.Stop();
        _refreshTimer.Start();
    }

    private void RefreshTimer_Tick(object? sender, EventArgs e)
    {
        _refreshTimer.Stop();
        RefreshGallery();
    }

    private void GalleryWindow_Closed(object? sender, EventArgs e)
    {
        _refreshTimer.Stop();
        _refreshTimer.Tick -= RefreshTimer_Tick;

        _watcher.EnableRaisingEvents = false;
        _watcher.Created -= Library_Changed;
        _watcher.Changed -= Library_Changed;
        _watcher.Deleted -= Library_Changed;
        _watcher.Renamed -= Library_Changed;
        _watcher.Dispose();

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

public sealed record GalleryDayGroup(string Header, IReadOnlyList<GalleryScreenshotViewModel> Screenshots)
{
    public int Count => Screenshots.Count;
}

public sealed record GalleryScreenshotViewModel(
    string Path,
    string DisplayName,
    string TimeText,
    BitmapSource Thumbnail);
