using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DegrandeScreenShot.App.Services;
using Microsoft.Win32;
using FormsScreen = System.Windows.Forms.Screen;

namespace DegrandeScreenShot.App;

public partial class CaptureTypeSelectorWindow : Window
{
    private const string WindowsThemeRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private readonly Point _anchorScreenPoint;
    private readonly EditorPreferencesStore _preferencesStore = new();
    private bool _closingBySelection;
    private bool _isClosing;

    public CaptureTypeSelectorWindow(Point anchorScreenPoint)
    {
        InitializeComponent();
        _anchorScreenPoint = anchorScreenPoint;
        ApplyTheme();

        Loaded += CaptureTypeSelectorWindow_Loaded;
        Deactivated += CaptureTypeSelectorWindow_Deactivated;
        Closed += CaptureTypeSelectorWindow_Closed;
        PreviewKeyDown += CaptureTypeSelectorWindow_PreviewKeyDown;
        SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
    }

    internal CaptureTypeSelection? SelectedAction { get; private set; }

    private void CaptureTypeSelectorWindow_Loaded(object sender, RoutedEventArgs e)
    {
        PositionOnCurrentMonitor();
        Activate();
        Focus();
        Keyboard.Focus(this);
    }

    private void CaptureTypeSelectorWindow_Deactivated(object? sender, EventArgs e)
    {
        if (!_closingBySelection)
        {
            CloseSelector();
        }
    }

    private void CaptureTypeSelectorWindow_Closed(object? sender, EventArgs e)
    {
        SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
    }

    private void CaptureTypeSelectorWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                CloseSelector();
                e.Handled = true;
                break;
            case Key.D6:
            case Key.NumPad6:
                SelectAction(CaptureTypeSelection.CopyRegion);
                e.Handled = true;
                break;
            case Key.D7:
            case Key.NumPad7:
                SelectAction(CaptureTypeSelection.OpenEditor);
                e.Handled = true;
                break;
            case Key.D8:
            case Key.NumPad8:
                SelectAction(CaptureTypeSelection.ClipboardEditor);
                e.Handled = true;
                break;
            case Key.D9:
            case Key.NumPad9:
                SelectAction(CaptureTypeSelection.ScrollingWindow);
                e.Handled = true;
                break;
        }
    }

    private void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        Dispatcher.InvokeAsync(ApplyTheme);
    }

    private void ActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag }
            || !Enum.TryParse<CaptureTypeSelection>(tag, ignoreCase: true, out var action))
        {
            return;
        }

        SelectAction(action);
    }

    private void SelectAction(CaptureTypeSelection action)
    {
        if (_isClosing)
        {
            return;
        }

        _closingBySelection = true;
        _isClosing = true;
        SelectedAction = action;
        DialogResult = true;
    }

    private void CloseSelector()
    {
        if (_isClosing)
        {
            return;
        }

        _isClosing = true;
        Close();
    }

    private void PositionOnCurrentMonitor()
    {
        var screen = FormsScreen.FromPoint(new System.Drawing.Point((int)Math.Round(_anchorScreenPoint.X), (int)Math.Round(_anchorScreenPoint.Y)));
        var workArea = screen.WorkingArea;
        var popupWidth = ActualWidth > 0 ? ActualWidth : Width;
        var popupHeight = ActualHeight > 0 ? ActualHeight : Height;

        Left = Math.Clamp(workArea.Left + ((workArea.Width - popupWidth) / 2), workArea.Left + 8, workArea.Right - popupWidth - 8);
        Top = Math.Clamp(workArea.Bottom - popupHeight - 18, workArea.Top + 8, workArea.Bottom - popupHeight - 8);
    }

    private void ApplyTheme()
    {
        var palette = ResolveThemeMode() == SelectorThemeMode.Dark ? SelectorPalette.Dark : SelectorPalette.Light;
        SetBrushColor("IslandBackgroundBrush", palette.IslandBackground);
        SetBrushColor("IslandBorderBrush", palette.IslandBorder);
        SetBrushColor("IslandButtonBrush", palette.IslandButton);
        SetBrushColor("IslandButtonHoverBrush", palette.IslandButtonHover);
        SetBrushColor("IslandButtonPressedBrush", palette.IslandButtonPressed);
        SetBrushColor("IslandButtonActiveBrush", palette.IslandButtonActive);
        SetBrushColor("IslandButtonActiveBorderBrush", palette.IslandButtonActiveBorder);
        SetBrushColor("IslandTextBrush", palette.IslandText);
        SetBrushColor("IslandMutedTextBrush", palette.IslandMutedText);
        SetBrushColor("IslandDividerBrush", palette.IslandDivider);
    }

    private void SetBrushColor(string resourceKey, Color color)
    {
        Resources[resourceKey] = new SolidColorBrush(color);
    }

    private SelectorThemeMode ResolveThemeMode()
    {
        var preference = _preferencesStore.Load().ThemePreference;
        if (Enum.TryParse<SelectorThemePreference>(preference, ignoreCase: true, out var parsedPreference)
            && parsedPreference != SelectorThemePreference.System)
        {
            return parsedPreference == SelectorThemePreference.Dark ? SelectorThemeMode.Dark : SelectorThemeMode.Light;
        }

        return IsSystemLightTheme() ? SelectorThemeMode.Light : SelectorThemeMode.Dark;
    }

    private static bool IsSystemLightTheme()
    {
        using var personalizeKey = Registry.CurrentUser.OpenSubKey(WindowsThemeRegistryPath);
        var themeValue = personalizeKey?.GetValue("AppsUseLightTheme");
        return themeValue is not int intValue || intValue != 0;
    }

    private enum SelectorThemePreference
    {
        System,
        Dark,
        Light,
    }

    private enum SelectorThemeMode
    {
        Dark,
        Light,
    }

    private sealed record SelectorPalette(
        Color IslandBackground,
        Color IslandBorder,
        Color IslandButton,
        Color IslandButtonHover,
        Color IslandButtonPressed,
        Color IslandButtonActive,
        Color IslandButtonActiveBorder,
        Color IslandText,
        Color IslandMutedText,
        Color IslandDivider)
    {
        internal static readonly SelectorPalette Dark = new(
            IslandBackground: Color.FromArgb(0xD9, 0x1A, 0x1E, 0x25),
            IslandBorder: Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF),
            IslandButton: Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF),
            IslandButtonHover: Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF),
            IslandButtonPressed: Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF),
            IslandButtonActive: Color.FromRgb(0x1A, 0xA9, 0xD8),
            IslandButtonActiveBorder: Color.FromArgb(0x7D, 0xCC, 0xF3, 0xFF),
            IslandText: Color.FromRgb(0xF4, 0xF7, 0xFB),
            IslandMutedText: Color.FromArgb(0xA9, 0xC2, 0xCE, 0xDA),
            IslandDivider: Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF));

        internal static readonly SelectorPalette Light = new(
            IslandBackground: Color.FromArgb(0xEC, 0xFF, 0xFF, 0xFF),
            IslandBorder: Color.FromArgb(0x33, 0x95, 0xA7, 0xB9),
            IslandButton: Color.FromArgb(0xE0, 0xF9, 0xFB, 0xFD),
            IslandButtonHover: Color.FromArgb(0xF2, 0xF2, 0xF8, 0xFC),
            IslandButtonPressed: Color.FromArgb(0xFF, 0xE6, 0xEE, 0xF4),
            IslandButtonActive: Color.FromRgb(0x0C, 0x8E, 0xC5),
            IslandButtonActiveBorder: Color.FromArgb(0x6E, 0x84, 0xCA, 0xEC),
            IslandText: Color.FromRgb(0x1D, 0x26, 0x31),
            IslandMutedText: Color.FromRgb(0x6C, 0x79, 0x87),
            IslandDivider: Color.FromArgb(0xCC, 0xD5, 0xE0, 0xEA));
    }
}

internal enum CaptureTypeSelection
{
    CopyRegion,
    OpenEditor,
    ClipboardEditor,
    ScrollingWindow,
}