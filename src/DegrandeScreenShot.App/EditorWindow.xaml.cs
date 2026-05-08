using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;
using Brush = System.Windows.Media.Brush;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using ShapePath = System.Windows.Shapes.Path;
using FormsScreen = System.Windows.Forms.Screen;

namespace DegrandeScreenShot.App;

public partial class EditorWindow : Window
{
    internal static readonly Color DefaultAccentColor = Color.FromRgb(18, 91, 80);
    internal static readonly Color DefaultArrowColor = Color.FromRgb(242, 162, 58);
    internal static readonly Color DefaultTextColor = Color.FromRgb(108, 75, 22);
    private const string WindowsThemeRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const int DwmUseImmersiveDarkModeAttribute = 20;
    private const int DwmBorderColorAttribute = 34;
    private const int DwmCaptionColorAttribute = 35;
    private const int DwmTextColorAttribute = 36;
    private const double SelectionActionIslandSpacing = 18;
    private const double SelectionActionIslandViewportPadding = 18;
    private const double MinZoomLevel = 0.35;
    private const double MaxZoomLevel = 4.0;
    private const double ZoomStepFactor = 1.15;

    private readonly List<AnnotationBase> _annotations = [];
    private readonly Stack<EditorDocumentState> _undoHistory = [];
    private readonly Stack<EditorDocumentState> _redoHistory = [];
    private static int _nextEditorNumber = 1;
    private BitmapSource _workingImage;
    private Rect? _appliedCropRect;
    private Rect? _lastCropSelection;
    private EditorDocumentState? _cropPreviewReturnState;
    private EditorDocumentState? _currentHistoryState;
    private string _currentHistorySignature = string.Empty;
    private AnnotationBase? _selectedAnnotation;
    private AnnotationBase? _hoveredAnnotation;
    private AnnotationBase? _contextMenuAnnotation;
    private Rect? _cropSelection;
    private Rect? _cropDragOrigin;
    private EditorTool? _pendingDrawTool;
    private EditorTool _currentTool = EditorTool.Select;
    private TextAnnotation? _editingTextAnnotation;
    private Point? _dragStart;
    private DragOperation _dragOperation = DragOperation.None;
    private CropHandle _activeCropHandle = CropHandle.None;
    private string? _activeHandle;
    private string? _savedPath;
    private bool _isUpdatingInlineTextEditor;
    private bool _isUpdatingTextBackgroundOpacity;
    private bool _isUpdatingTextBackgroundStrength;
    private bool _hasFittedInitialWindowSize;
    private double _zoomLevel = 1.0;
    private Point? _panStartViewportPoint;
    private double _panStartHorizontalOffset;
    private double _panStartVerticalOffset;
    private ThemePreference _themePreference = ThemePreference.System;

    public EditorWindow(BitmapSource baseImage)
    {
        InitializeComponent();
        _workingImage = baseImage;
        Loaded += EditorWindow_Loaded;
        SourceInitialized += EditorWindow_SourceInitialized;
        Closed += EditorWindow_Closed;
        SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
        Title = $"Edit Capture {_nextEditorNumber++}";
        ApplyWorkingImage(_workingImage);
        InitializeHistory();
        ApplyTheme();
        SelectToolButton.IsChecked = true;
        RefreshCanvas();
    }

    private void EditorWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_hasFittedInitialWindowSize)
        {
            return;
        }

        _hasFittedInitialWindowSize = true;
        Dispatcher.BeginInvoke(new Action(FitWindowToArtwork), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void EditorWindow_SourceInitialized(object? sender, EventArgs e)
    {
        ApplyNativeTitleBarTheme();
    }

    private void EditorWindow_Closed(object? sender, EventArgs e)
    {
        SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
    }

    private void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (_themePreference != ThemePreference.System)
        {
            return;
        }

        Dispatcher.InvokeAsync(ApplyTheme);
    }

    private void ThemeModeButton_Click(object sender, RoutedEventArgs e)
    {
        _themePreference = _themePreference switch
        {
            ThemePreference.System => ThemePreference.Dark,
            ThemePreference.Dark => ThemePreference.Light,
            ThemePreference.Light => ThemePreference.System,
            _ => ThemePreference.System,
        };

        ApplyTheme();
    }

    private void ApplyTheme()
    {
        var isDarkTheme = ResolveThemeMode() == ThemePreference.Dark;
        var palette = isDarkTheme ? ThemePalette.Dark : ThemePalette.Light;

        SetBrushColor("ShellBackgroundBrush", palette.ShellBackground);
        SetBrushColor("EditorWorkspaceBrush", palette.EditorWorkspace);
        SetBrushColor("EditorCanvasBrush", palette.EditorCanvas);
        SetBrushColor("IslandBackgroundBrush", palette.IslandBackground);
        SetBrushColor("IslandBorderBrush", palette.IslandBorder);
        SetBrushColor("IslandButtonBrush", palette.IslandButton);
        SetBrushColor("IslandButtonHoverBrush", palette.IslandButtonHover);
        SetBrushColor("IslandButtonPressedBrush", palette.IslandButtonPressed);
        SetBrushColor("IslandButtonActiveBrush", palette.IslandButtonActive);
        SetBrushColor("IslandButtonActiveBorderBrush", palette.IslandButtonActiveBorder);
        SetBrushColor("IslandTextBrush", palette.IslandText);
        SetBrushColor("IslandMutedTextBrush", palette.IslandMutedText);
        SetBrushColor("PrimaryButtonBrush", palette.PrimaryButton);
        SetBrushColor("PrimaryButtonBorderBrush", palette.PrimaryButtonBorder);
        SetBrushColor("PrimaryButtonHoverBrush", palette.PrimaryButtonHover);
        SetBrushColor("PrimaryButtonPressedBrush", palette.PrimaryButtonPressed);
        SetBrushColor("SecondaryButtonBrush", palette.SecondaryButton);
        SetBrushColor("SecondaryButtonBorderBrush", palette.SecondaryButtonBorder);
        SetBrushColor("SecondaryButtonHoverBrush", palette.SecondaryButtonHover);
        SetBrushColor("SecondaryButtonPressedBrush", palette.SecondaryButtonPressed);
        SetBrushColor("FloatingCardBrush", palette.FloatingCard);
        SetBrushColor("FloatingCardBorderBrush", palette.FloatingCardBorder);
        SetBrushColor("ShellScrimBrush", palette.ShellScrim);
        SetBrushColor("ShellTopHighlightBrush", palette.ShellTopHighlight);
        SetBrushColor("InlineTextInputBackgroundBrush", palette.InlineTextInputBackground);
        SetBrushColor("InlineTextInputBorderBrush", palette.InlineTextInputBorder);
        SetBrushColor("InlineTextInputForegroundBrush", palette.InlineTextInputForeground);
        SetBrushColor("PopupPanelBackgroundBrush", palette.PopupPanelBackground);
        SetBrushColor("PopupPanelBorderBrush", palette.PopupPanelBorder);
        SetBrushColor("DeleteButtonBackgroundBrush", palette.DeleteButtonBackground);
        SetBrushColor("DeleteButtonBorderBrush", palette.DeleteButtonBorder);
        SetBrushColor("DeleteButtonForegroundBrush", palette.DeleteButtonForeground);
        SetBrushColor("IslandDividerBrush", palette.IslandDivider);
        SetGradientBrushColors("ShellGlowBrush", palette.GlowStart, palette.GlowMiddle, palette.GlowEnd);
        UpdateThemeModeButton();
        ApplyNativeTitleBarTheme();
    }

    private ThemePreference ResolveThemeMode()
    {
        if (_themePreference != ThemePreference.System)
        {
            return _themePreference;
        }

        return IsSystemLightTheme() ? ThemePreference.Light : ThemePreference.Dark;
    }

    private static bool IsSystemLightTheme()
    {
        using var personalizeKey = Registry.CurrentUser.OpenSubKey(WindowsThemeRegistryPath);
        var themeValue = personalizeKey?.GetValue("AppsUseLightTheme");
        return themeValue is not int intValue || intValue != 0;
    }

    private void UpdateThemeModeButton()
    {
        ThemeModeSystemIcon.Visibility = _themePreference == ThemePreference.System ? Visibility.Visible : Visibility.Collapsed;
        ThemeModeDarkIcon.Visibility = _themePreference == ThemePreference.Dark ? Visibility.Visible : Visibility.Collapsed;
        ThemeModeLightIcon.Visibility = _themePreference == ThemePreference.Light ? Visibility.Visible : Visibility.Collapsed;

        var resolvedLabel = ResolveThemeMode() == ThemePreference.Dark ? "dark" : "light";
        ThemeModeButton.ToolTip = _themePreference switch
        {
            ThemePreference.Dark => "Theme: Dark. Click to switch to Light.",
            ThemePreference.Light => "Theme: Light. Click to switch to System.",
            _ => $"Theme: System ({resolvedLabel}). Click to switch to Dark.",
        };
    }

    private void SetBrushColor(string resourceKey, Color color)
    {
        Resources[resourceKey] = new SolidColorBrush(color);
    }

    private void SetGradientBrushColors(string resourceKey, Color startColor, Color middleColor, Color endColor)
    {
        Resources[resourceKey] = new LinearGradientBrush(
            new GradientStopCollection
            {
                new(startColor, 0),
                new(middleColor, 0.4),
                new(endColor, 0.9),
            },
            new Point(0, 0),
            new Point(1, 1));
    }

    private void ApplyNativeTitleBarTheme()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var palette = ResolveThemeMode() == ThemePreference.Dark ? ThemePalette.Dark : ThemePalette.Light;
        var useDarkMode = ResolveThemeMode() == ThemePreference.Dark ? 1 : 0;
        _ = DwmSetWindowAttribute(handle, DwmUseImmersiveDarkModeAttribute, ref useDarkMode, sizeof(int));

        var captionColor = ToColorRef(palette.EditorWorkspace);
        var textColor = ToColorRef(palette.IslandText);
        var borderColor = ToColorRef(palette.IslandBorder);
        _ = DwmSetWindowAttribute(handle, DwmCaptionColorAttribute, ref captionColor, sizeof(uint));
        _ = DwmSetWindowAttribute(handle, DwmTextColorAttribute, ref textColor, sizeof(uint));
        _ = DwmSetWindowAttribute(handle, DwmBorderColorAttribute, ref borderColor, sizeof(uint));
    }

    private static uint ToColorRef(Color color)
    {
        return (uint)(color.R | (color.G << 8) | (color.B << 16));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int attributeValue, int attributeSize);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref uint attributeValue, int attributeSize);

    private void ToolButton_Checked(object sender, RoutedEventArgs e)
    {
        _contextMenuAnnotation = null;

        foreach (var button in new[] { SelectToolButton, CropToolButton, ArrowToolButton, RectangleToolButton, EllipseToolButton, TextToolButton })
        {
            if (!ReferenceEquals(button, sender))
            {
                button.IsChecked = false;
            }
        }

        if (sender is ToggleButton toggleButton && Enum.TryParse<EditorTool>(toggleButton.Tag?.ToString(), out var tool))
        {
            if (tool != EditorTool.Text)
            {
                CommitInlineTextEditing();
            }

            if (tool == EditorTool.Crop)
            {
                if (!_isCropPreviewActive() && _appliedCropRect is { } appliedCropRect)
                {
                    _cropPreviewReturnState = CaptureCurrentDocumentState(_cropSelection);
                    _appliedCropRect = null;
                    _cropSelection = _lastCropSelection ?? appliedCropRect;
                    UpdateArtworkViewport();
                }
            }
            else if (_isCropPreviewActive())
            {
                RestoreCropPreviewReturnState();
            }

            _currentTool = tool;
            if (tool != EditorTool.Crop)
            {
                _cropSelection = null;
            }

            RefreshCanvas();
        }
    }

    private void AnnotationCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _contextMenuAnnotation = null;

        Focus();
        var point = ToDocumentPoint(e.GetPosition(AnnotationCanvas));

        if (e.ClickCount == 2 && TryBeginTextEditOnDoubleClick(point))
        {
            _dragStart = null;
            _dragOperation = DragOperation.None;
            _activeHandle = null;
            RefreshCanvas();
            return;
        }

        if (e.ClickCount == 2 && TryToggleSelectedAnchorStyle(point))
        {
            _dragStart = null;
            _dragOperation = DragOperation.None;
            _activeHandle = null;
            CommitHistoryState();
            RefreshCanvas();
            return;
        }

        if (_currentTool != EditorTool.Text)
        {
            CommitInlineTextEditing();
        }

        if (_currentTool == EditorTool.Crop)
        {
            _dragStart = point;
            AnnotationCanvas.CaptureMouse();

            if (TryBeginCropInteraction(point))
            {
                RefreshCanvas();
                return;
            }

            _cropSelection = new Rect(point, point);
            _cropDragOrigin = _cropSelection;
            _activeCropHandle = CropHandle.BottomRight;
            _dragOperation = DragOperation.CropCreate;
            RefreshCanvas();
            return;
        }

        _dragStart = point;
        AnnotationCanvas.CaptureMouse();

        if (TryBeginSelectedHandleInteraction(point))
        {
            RefreshCanvas();
            return;
        }

        if (TryBeginExistingAnnotationInteraction(point))
        {
            RefreshCanvas();
            return;
        }

        if (_currentTool == EditorTool.Select)
        {
            BeginViewportPan(e.GetPosition(ArtworkScrollViewer));
            RefreshCanvas();
            return;
        }

        if (_currentTool == EditorTool.Text)
        {
            var text = new TextAnnotation { Location = point, Text = string.Empty };
            text.SetColor(DefaultTextColor);
            _annotations.Add(text);
            _selectedAnnotation = text;
            _hoveredAnnotation = text;
            _dragOperation = DragOperation.None;
            BeginInlineTextEditing(text, selectAll: false);
            return;
        }

        if (IsDeferredDrawTool(_currentTool))
        {
            _dragStart = point;
            _pendingDrawTool = _currentTool;
            _dragOperation = DragOperation.None;
            AnnotationCanvas.CaptureMouse();
            RefreshCanvas();
            return;
        }

        if (_currentTool == EditorTool.Rectangle)
        {
            var annotation = new RectangleAnnotation { Bounds = new Rect(point, point) };
            annotation.SetColor(DefaultAccentColor);
            _annotations.Add(annotation);
            _selectedAnnotation = annotation;
            _dragOperation = DragOperation.Draw;
            RefreshCanvas();
            return;
        }

        if (_currentTool == EditorTool.Ellipse)
        {
            var annotation = new EllipseAnnotation { Bounds = new Rect(point, point) };
            annotation.SetColor(DefaultAccentColor);
            _annotations.Add(annotation);
            _selectedAnnotation = annotation;
            _dragOperation = DragOperation.Draw;
            RefreshCanvas();
            return;
        }

        if (_currentTool == EditorTool.Arrow)
        {
            var annotation = ArrowAnnotation.Create(point);
            annotation.SetColor(DefaultArrowColor);
            _annotations.Add(annotation);
            _selectedAnnotation = annotation;
            _dragOperation = DragOperation.Draw;
            RefreshCanvas();
            return;
        }

        SelectAnnotationAt(point);
        if (_selectedAnnotation is null)
        {
            RefreshCanvas();
            return;
        }

        _activeHandle = _selectedAnnotation.HitHandle(point);
        _dragOperation = _activeHandle is null ? DragOperation.Move : DragOperation.Handle;
        RefreshCanvas();
    }

    private bool TryBeginSelectedHandleInteraction(Point point)
    {
        if (_selectedAnnotation is null)
        {
            return false;
        }

        var handle = _selectedAnnotation.HitHandle(point);
        if (handle is null)
        {
            return false;
        }

        _activeHandle = handle;
        _dragOperation = DragOperation.Handle;
        _hoveredAnnotation = _selectedAnnotation;
        return true;
    }

    private bool TryBeginExistingAnnotationInteraction(Point point)
    {
        var annotation = _annotations.LastOrDefault(candidate => candidate.HitTest(point));
        if (annotation is null)
        {
            return false;
        }

        _selectedAnnotation = annotation;
        _hoveredAnnotation = annotation;
        _activeHandle = annotation.HitHandle(point);

        CommitInlineTextEditing();
        _dragOperation = _activeHandle is null ? DragOperation.Move : DragOperation.Handle;
        return true;
    }

    private bool TryToggleSelectedAnchorStyle(Point point)
    {
        if (_selectedAnnotation is not ArrowAnnotation arrow)
        {
            return false;
        }

        var handle = arrow.HitHandle(point);
        if (handle is not "Control1" and not "Control2")
        {
            return false;
        }

        arrow.ToggleCornerStyle(handle);
        _hoveredAnnotation = arrow;
        return true;
    }

    private bool TryBeginTextEditOnDoubleClick(Point point)
    {
        if (_annotations.LastOrDefault(candidate => candidate.HitTest(point)) is not TextAnnotation textAnnotation)
        {
            return false;
        }

        _selectedAnnotation = textAnnotation;
        _hoveredAnnotation = textAnnotation;
        BeginInlineTextEditing(textAnnotation, selectAll: true);
        return true;
    }

    private void AnnotationCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_pendingDrawTool is { } pendingDrawTool)
        {
            if (_dragStart is null || e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            var drawPoint = ToDocumentPoint(e.GetPosition(AnnotationCanvas));
            if ((drawPoint - _dragStart.Value).Length < 4)
            {
                return;
            }

            BeginDeferredDraw(pendingDrawTool, _dragStart.Value);
            _pendingDrawTool = null;
            if (_selectedAnnotation is not null)
            {
                _selectedAnnotation.UpdateFromAnchor(_dragStart.Value, drawPoint, ShouldConstrainAspectRatio());
            }

            RefreshCanvas();
            return;
        }

        if (_dragOperation is DragOperation.CropCreate or DragOperation.CropMove or DragOperation.CropResize)
        {
            if (_dragStart is null || e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            var cropPoint = ToDocumentPoint(e.GetPosition(AnnotationCanvas));
            switch (_dragOperation)
            {
                case DragOperation.CropCreate:
                    _cropSelection = ClampCropRect(AnnotationBase.CreateRectFromPoints(_dragStart.Value, cropPoint, false));
                    break;
                case DragOperation.CropMove when _cropSelection is { } selection && _cropDragOrigin is { } origin:
                    var delta = cropPoint - _dragStart.Value;
                    _cropSelection = ClampCropRect(new Rect(origin.TopLeft + delta, origin.Size));
                    break;
                case DragOperation.CropResize when _cropDragOrigin is { } resizeOrigin:
                    _cropSelection = ClampCropRect(ResizeCropRect(resizeOrigin, cropPoint, _activeCropHandle));
                    break;
            }

            RefreshCanvas();
            return;
        }

        if (_dragOperation == DragOperation.Pan)
        {
            if (_panStartViewportPoint is null || e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            var currentViewportPoint = e.GetPosition(ArtworkScrollViewer);
            var delta = currentViewportPoint - _panStartViewportPoint.Value;
            ArtworkScrollViewer.ScrollToHorizontalOffset(Math.Clamp(_panStartHorizontalOffset - delta.X, 0, ArtworkScrollViewer.ScrollableWidth));
            ArtworkScrollViewer.ScrollToVerticalOffset(Math.Clamp(_panStartVerticalOffset - delta.Y, 0, ArtworkScrollViewer.ScrollableHeight));
            UpdateSelectionActionIsland();
            return;
        }

        if (_dragStart is null || e.LeftButton != MouseButtonState.Pressed || _selectedAnnotation is null)
        {
            UpdateHoveredAnnotation(ToDocumentPoint(e.GetPosition(AnnotationCanvas)));
            return;
        }

        var point = ToDocumentPoint(e.GetPosition(AnnotationCanvas));
        switch (_dragOperation)
        {
            case DragOperation.Draw:
                _selectedAnnotation.UpdateFromAnchor(_dragStart.Value, point, ShouldConstrainAspectRatio());
                break;
            case DragOperation.Move:
                _selectedAnnotation.Move(point - _dragStart.Value);
                _dragStart = point;
                break;
            case DragOperation.Handle when _activeHandle is not null:
                _selectedAnnotation.MoveHandle(_activeHandle, point, ShouldConstrainAspectRatio());
                break;
        }

        RefreshCanvas();
    }

    private void AnnotationCanvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (SelectToolButton.IsChecked != true)
        {
            ActivateSelectTool();
        }

        CommitInlineTextEditing();

        var viewportPoint = e.GetPosition(AnnotationCanvas);
        var point = ToDocumentPoint(viewportPoint);
        var annotation = _annotations.LastOrDefault(candidate => candidate.HitTest(point));
        if (annotation is null)
        {
            _selectedAnnotation = null;
            _hoveredAnnotation = null;
            _contextMenuAnnotation = null;
            RefreshCanvas();
            return;
        }

        if (AnnotationCanvas.IsMouseCaptured)
        {
            AnnotationCanvas.ReleaseMouseCapture();
        }

        _selectedAnnotation = annotation;
        _hoveredAnnotation = annotation;
        _contextMenuAnnotation = annotation;
        _dragStart = null;
        _dragOperation = DragOperation.None;
        _activeHandle = annotation.HitHandle(point);
        UpdateTextAnnotationControls(annotation as TextAnnotation);
        RefreshCanvas();
        e.Handled = true;
    }

    private void AnnotationCanvas_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_hoveredAnnotation is null)
        {
            return;
        }

        _hoveredAnnotation = null;
        RefreshCanvas();
    }

    private void AnnotationCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_pendingDrawTool is not null)
        {
            var point = ToDocumentPoint(e.GetPosition(AnnotationCanvas));
            _pendingDrawTool = null;
            _dragStart = null;
            _dragOperation = DragOperation.None;
            _activeCropHandle = CropHandle.None;
            _cropDragOrigin = null;
            _activeHandle = null;
            AnnotationCanvas.ReleaseMouseCapture();
            ActivateSelectTool();
            SelectAnnotationAt(point);
            RefreshCanvas();
            return;
        }

        _dragStart = null;
        var shouldCommitHistory = _dragOperation is DragOperation.Draw or DragOperation.Move or DragOperation.Handle or DragOperation.CropCreate or DragOperation.CropMove or DragOperation.CropResize;
        _dragOperation = DragOperation.None;
        _activeCropHandle = CropHandle.None;
        _cropDragOrigin = null;
        _activeHandle = null;
        _panStartViewportPoint = null;
        AnnotationCanvas.ReleaseMouseCapture();
        if (shouldCommitHistory)
        {
            CommitHistoryState();
        }

        RefreshCanvas();
    }

    private void ApplyCrop_Click(object sender, RoutedEventArgs e)
    {
        if (_cropSelection is not { } selection || selection.Width < 1 || selection.Height < 1)
        {
            return;
        }

        var cropRect = NormalizeCropRect(selection);

        _appliedCropRect = cropRect;
        _lastCropSelection = cropRect;
        UpdateArtworkViewport();
        _selectedAnnotation = null;
        _hoveredAnnotation = null;
        _contextMenuAnnotation = null;
        _cropPreviewReturnState = null;
        _cropSelection = null;
        SelectToolButton.IsChecked = true;
        CommitHistoryState();
        RefreshCanvas();
    }

    private void CancelCrop_Click(object sender, RoutedEventArgs e)
    {
        if (_isCropPreviewActive())
        {
            RestoreCropPreviewReturnState();
        }

        _cropSelection = null;
        SelectToolButton.IsChecked = true;
        RefreshCanvas();
    }

    private void UndoCrop_Click(object sender, RoutedEventArgs e)
    {
        UndoHistory();
    }

    private void ColorOption_Click(object sender, RoutedEventArgs e)
    {
        if (_contextMenuAnnotation is null)
        {
            return;
        }

        if (sender is Button button && button.Tag is string colorValue && ColorConverter.ConvertFromString(colorValue) is Color color)
        {
            _contextMenuAnnotation.SetColor(color);
            CommitHistoryState();
            RefreshCanvas();
        }
    }

    private void TextSizeOption_Click(object sender, RoutedEventArgs e)
    {
        if (_contextMenuAnnotation is not TextAnnotation textAnnotation)
        {
            return;
        }

        if (sender is not Button button || button.Tag is not string sizeValue || !double.TryParse(sizeValue, out var fontSize))
        {
            return;
        }

        textAnnotation.SetFontSize(fontSize);
        if (ReferenceEquals(_editingTextAnnotation, textAnnotation))
        {
            InlineTextEditor.FontSize = fontSize;
        }

        CommitHistoryState();
        RefreshCanvas();
    }

    private void TextBackgroundOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingTextBackgroundOpacity || _contextMenuAnnotation is not TextAnnotation textAnnotation)
        {
            return;
        }

        var opacity = TextBackgroundOpacitySlider.Value / 100d;
        textAnnotation.SetBackgroundOpacity(opacity);
        TextBackgroundOpacityValueText.Text = $"{(int)Math.Round(TextBackgroundOpacitySlider.Value)}%";
        CommitHistoryState();
        RefreshCanvas();
    }

    private void TextBackgroundStrengthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingTextBackgroundStrength || _contextMenuAnnotation is not TextAnnotation textAnnotation)
        {
            return;
        }

        var strength = TextBackgroundStrengthSlider.Value / 100d;
        textAnnotation.SetBackgroundColorStrength(strength);
        TextBackgroundStrengthValueText.Text = $"{(int)Math.Round(TextBackgroundStrengthSlider.Value)}%";
        CommitHistoryState();
        RefreshCanvas();
    }

    private void DeleteAnnotation_Click(object sender, RoutedEventArgs e)
    {
        if (_contextMenuAnnotation is null)
        {
            return;
        }

        RemoveAnnotation(_contextMenuAnnotation);
        CommitHistoryState();
        RefreshCanvas();
    }

    private void EditorWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (InlineTextEditor.IsKeyboardFocusWithin)
        {
            return;
        }

        var modifiers = Keyboard.Modifiers;
        if (modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.Z)
        {
            if (modifiers.HasFlag(ModifierKeys.Shift))
            {
                RedoHistory();
            }
            else
            {
                UndoHistory();
            }

            e.Handled = true;
            return;
        }

        if (modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.Y)
        {
            RedoHistory();
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Delete || _selectedAnnotation is null || _editingTextAnnotation is not null)
        {
            return;
        }

        RemoveAnnotation(_selectedAnnotation);
        CommitHistoryState();
        RefreshCanvas();
        e.Handled = true;
    }

    private void UndoHistory_Click(object sender, RoutedEventArgs e)
    {
        UndoHistory();
    }

    private void RedoHistory_Click(object sender, RoutedEventArgs e)
    {
        RedoHistory();
    }

    private void InlineTextEditor_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdatingInlineTextEditor || _editingTextAnnotation is null)
        {
            return;
        }

        _editingTextAnnotation.Text = InlineTextEditor.Text;
        RefreshCanvas();
    }

    private void InlineTextEditor_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        CommitInlineTextEditing();
        RefreshCanvas();
    }

    private void InlineTextEditor_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CommitInlineTextEditing();
            RefreshCanvas();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            CommitInlineTextEditing();
            RefreshCanvas();
            e.Handled = true;
        }
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        var bitmap = RenderComposite();
        System.Windows.Clipboard.SetImage(bitmap);
        ShowPreview(bitmap);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_savedPath))
        {
            _savedPath = CreateDefaultSavePath();
        }

        var bitmap = SaveToPath(_savedPath);
        ShowPreview(bitmap);
    }

    private void SaveAs_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "PNG Image|*.png",
            AddExtension = true,
            DefaultExt = ".png",
            InitialDirectory = GetPicturesFolder(),
            FileName = System.IO.Path.GetFileName(CreateDefaultSavePath()),
        };

        if (dialog.ShowDialog(this) == true)
        {
            _savedPath = dialog.FileName;
            var bitmap = SaveToPath(_savedPath);
            ShowPreview(bitmap);
        }
    }

    private BitmapSource SaveToPath(string path)
    {
        var directory = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var bitmap = RenderComposite();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
        return bitmap;
    }

    private void ShowPreview(BitmapSource bitmap)
    {
        if (Owner is MainWindow mainWindow)
        {
            mainWindow.ShowCapturePreview(bitmap);
        }
    }

    private void UpdateTextAnnotationControls(TextAnnotation? textAnnotation)
    {
        var visibility = textAnnotation is null ? Visibility.Collapsed : Visibility.Visible;
        TextSizePanel.Visibility = visibility;
        TextBackgroundOpacityPanel.Visibility = visibility;
        TextBackgroundStrengthPanel.Visibility = visibility;

        if (textAnnotation is null)
        {
            return;
        }

        _isUpdatingTextBackgroundOpacity = true;
        var opacityPercent = Math.Round(textAnnotation.BackgroundOpacity * 100);
        TextBackgroundOpacitySlider.Value = opacityPercent;
        TextBackgroundOpacityValueText.Text = $"{(int)opacityPercent}%";
        _isUpdatingTextBackgroundOpacity = false;

        _isUpdatingTextBackgroundStrength = true;
        var strengthPercent = Math.Round(textAnnotation.BackgroundColorStrength * 100);
        TextBackgroundStrengthSlider.Value = strengthPercent;
        TextBackgroundStrengthValueText.Text = $"{(int)strengthPercent}%";
        _isUpdatingTextBackgroundStrength = false;
    }

    private static string GetPicturesFolder()
    {
        return Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
    }

    private static string CreateDefaultSavePath()
    {
        var fileName = $"Degrande Capture {DateTime.Now:yyyy-MM-dd HH-mm-ss}.png";
        return System.IO.Path.Combine(GetPicturesFolder(), fileName);
    }

    private void SelectAnnotationAt(Point point)
    {
        _selectedAnnotation = _annotations.LastOrDefault(annotation => annotation.HitTest(point));
        _hoveredAnnotation = _selectedAnnotation;
        _contextMenuAnnotation = _selectedAnnotation;
        UpdateTextAnnotationControls(_selectedAnnotation as TextAnnotation);

        CommitInlineTextEditing();
    }

    private void ActivateSelectTool()
    {
        SelectToolButton.IsChecked = true;
    }

    private static bool IsDeferredDrawTool(EditorTool tool)
    {
        return tool is EditorTool.Rectangle or EditorTool.Ellipse or EditorTool.Arrow;
    }

    private void BeginDeferredDraw(EditorTool tool, Point point)
    {
        switch (tool)
        {
            case EditorTool.Rectangle:
            {
                var annotation = new RectangleAnnotation { Bounds = new Rect(point, point) };
                annotation.SetColor(DefaultAccentColor);
                _annotations.Add(annotation);
                _selectedAnnotation = annotation;
                _dragOperation = DragOperation.Draw;
                break;
            }
            case EditorTool.Ellipse:
            {
                var annotation = new EllipseAnnotation { Bounds = new Rect(point, point) };
                annotation.SetColor(DefaultAccentColor);
                _annotations.Add(annotation);
                _selectedAnnotation = annotation;
                _dragOperation = DragOperation.Draw;
                break;
            }
            case EditorTool.Arrow:
            {
                var annotation = ArrowAnnotation.Create(point);
                annotation.SetColor(DefaultArrowColor);
                _annotations.Add(annotation);
                _selectedAnnotation = annotation;
                _dragOperation = DragOperation.Draw;
                break;
            }
        }
    }

    private void UpdateHoveredAnnotation(Point point)
    {
        var hovered = _annotations.LastOrDefault(annotation => annotation.HitTest(point));
        if (ReferenceEquals(hovered, _hoveredAnnotation))
        {
            return;
        }

        _hoveredAnnotation = hovered;
        RefreshCanvas();
    }

    private RenderTargetBitmap RenderComposite()
    {
        CommitInlineTextEditing();
        RefreshCanvas();
        AnnotationCanvas.UpdateLayout();
        ArtworkSurface.UpdateLayout();

        var renderWidth = _appliedCropRect is { } cropRect
            ? Math.Max(1, (int)Math.Round(NormalizeCropRect(cropRect).Width))
            : _workingImage.PixelWidth;
        var renderHeight = _appliedCropRect is { } activeCropRect
            ? Math.Max(1, (int)Math.Round(NormalizeCropRect(activeCropRect).Height))
            : _workingImage.PixelHeight;

        var renderTarget = new RenderTargetBitmap(
            renderWidth,
            renderHeight,
            96,
            96,
            PixelFormats.Pbgra32);
        renderTarget.Render(ArtworkSurface);
        renderTarget.Freeze();
        return renderTarget;
    }

    private void RefreshCanvas()
    {
        AnnotationCanvas.Children.Clear();
        foreach (var annotation in _annotations)
        {
            if (ReferenceEquals(annotation, _editingTextAnnotation))
            {
                continue;
            }

            annotation.Render(AnnotationCanvas, annotation == _selectedAnnotation, annotation == _hoveredAnnotation);
        }

        if (_cropSelection is { } cropSelection)
        {
            RenderCropOverlay(cropSelection);
        }

        var isCropEditingVisible = _currentTool == EditorTool.Crop || _cropSelection is not null;
        CropActionsPanel.Visibility = isCropEditingVisible ? Visibility.Visible : Visibility.Collapsed;
        UndoCropPanel.Visibility = Visibility.Collapsed;
        EditorFooterPanel.Visibility = Visibility.Visible;
        ApplyCropButton.IsEnabled = _cropSelection is { Width: > 0, Height: > 0 };
        CancelCropButton.IsEnabled = _currentTool == EditorTool.Crop || _cropSelection is not null;
        UndoCropButton.IsEnabled = _undoHistory.Count > 0;
        UndoCropShortcutButton.IsEnabled = _undoHistory.Count > 0;
        UndoHistoryButton.IsEnabled = _undoHistory.Count > 0;
        RedoHistoryButton.IsEnabled = _redoHistory.Count > 0;
        UpdateInlineTextEditorState();
        UpdateSelectionActionIsland();
    }

    private bool ShouldConstrainAspectRatio()
    {
        return Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && (_selectedAnnotation is RectangleAnnotation or EllipseAnnotation);
    }

    private void ApplyWorkingImage(BitmapSource bitmapSource)
    {
        _workingImage = bitmapSource;
        UpdateArtworkViewport();
    }

    private void BeginInlineTextEditing(TextAnnotation textAnnotation, bool selectAll)
    {
        _editingTextAnnotation = textAnnotation;
        RefreshCanvas();

        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!ReferenceEquals(_editingTextAnnotation, textAnnotation))
            {
                return;
            }

            InlineTextPopup.IsOpen = true;
            InlineTextEditor.Focus();
            if (selectAll)
            {
                InlineTextEditor.SelectAll();
            }
            else
            {
                InlineTextEditor.CaretIndex = InlineTextEditor.Text.Length;
            }
        }), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void CommitInlineTextEditing()
    {
        if (_editingTextAnnotation is null)
        {
            InlineTextPopup.IsOpen = false;
            return;
        }

        _editingTextAnnotation.Text = InlineTextEditor.Text;
        var editedAnnotation = _editingTextAnnotation;
        _editingTextAnnotation = null;
        InlineTextPopup.IsOpen = false;

        if (string.IsNullOrWhiteSpace(editedAnnotation.Text))
        {
            _annotations.Remove(editedAnnotation);
            if (ReferenceEquals(_selectedAnnotation, editedAnnotation))
            {
                _selectedAnnotation = null;
            }

            if (ReferenceEquals(_hoveredAnnotation, editedAnnotation))
            {
                _hoveredAnnotation = null;
            }
        }

        CommitHistoryState();
    }

    private void RemoveAnnotation(AnnotationBase annotation)
    {
        if (ReferenceEquals(_editingTextAnnotation, annotation))
        {
            _editingTextAnnotation = null;
            InlineTextPopup.IsOpen = false;
        }

        _annotations.Remove(annotation);

        if (ReferenceEquals(_selectedAnnotation, annotation))
        {
            _selectedAnnotation = null;
        }

        if (ReferenceEquals(_hoveredAnnotation, annotation))
        {
            _hoveredAnnotation = null;
        }

        if (ReferenceEquals(_contextMenuAnnotation, annotation))
        {
            _contextMenuAnnotation = null;
        }
    }

    private void UpdateSelectionActionIsland()
    {
        if (_currentTool == EditorTool.Crop || _editingTextAnnotation is not null || _selectedAnnotation is null)
        {
            SelectionActionIsland.Visibility = Visibility.Collapsed;
            return;
        }

        _contextMenuAnnotation = _selectedAnnotation;
        UpdateTextAnnotationControls(_selectedAnnotation as TextAnnotation);

        SelectionActionIsland.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var islandWidth = SelectionActionIsland.ActualWidth > 0 ? SelectionActionIsland.ActualWidth : SelectionActionIsland.DesiredSize.Width;
        var islandHeight = SelectionActionIsland.ActualHeight > 0 ? SelectionActionIsland.ActualHeight : SelectionActionIsland.DesiredSize.Height;
        var bounds = GetAnnotationViewportBounds(_selectedAnnotation);
        var viewportWidth = ArtworkScrollViewer.ViewportWidth > 0 ? ArtworkScrollViewer.ViewportWidth : ArtworkScrollViewer.ActualWidth;
        var viewportHeight = ArtworkScrollViewer.ViewportHeight > 0 ? ArtworkScrollViewer.ViewportHeight : ArtworkScrollViewer.ActualHeight;
        var viewportOrigin = ArtworkScrollViewer.TranslatePoint(new Point(0, 0), EditorRootGrid);

        if (viewportWidth <= 0 || viewportHeight <= 0)
        {
            SelectionActionIsland.Visibility = Visibility.Collapsed;
            return;
        }

        var targetX = bounds.Left + ((bounds.Width - islandWidth) / 2);
        var targetY = bounds.Bottom + SelectionActionIslandSpacing;
        var maxY = viewportOrigin.Y + viewportHeight - islandHeight - SelectionActionIslandViewportPadding - (EditorFooterPanel.ActualHeight > 0 ? EditorFooterPanel.ActualHeight : 0);
        if (targetY > maxY)
        {
            targetY = bounds.Top - islandHeight - SelectionActionIslandSpacing;
        }

        var minX = viewportOrigin.X + SelectionActionIslandViewportPadding;
        var maxX = viewportOrigin.X + viewportWidth - islandWidth - SelectionActionIslandViewportPadding;
        var minY = viewportOrigin.Y + SelectionActionIslandViewportPadding;
        var boundedMaxY = viewportOrigin.Y + viewportHeight - islandHeight - SelectionActionIslandViewportPadding;
        targetX = Math.Clamp(targetX, minX, Math.Max(minX, maxX));
        targetY = Math.Clamp(targetY, minY, Math.Max(minY, boundedMaxY));

        SelectionActionIslandTransform.X = targetX;
        SelectionActionIslandTransform.Y = targetY;
        SelectionActionIsland.Visibility = Visibility.Visible;
    }

    private Rect GetAnnotationViewportBounds(AnnotationBase annotation)
    {
        return annotation switch
        {
            RectangleAnnotation rectangle => ToOverlayRect(new Rect(rectangle.Bounds.TopLeft, rectangle.Bounds.BottomRight)),
            EllipseAnnotation ellipse => ToOverlayRect(new Rect(ellipse.Bounds.TopLeft, ellipse.Bounds.BottomRight)),
            TextAnnotation text => ToOverlayRect(new Rect(text.Location, text.GetBounds())),
            ArrowAnnotation arrow => ToOverlayRect(GetArrowBounds(arrow)),
            _ => new Rect(SelectionActionIslandViewportPadding, SelectionActionIslandViewportPadding, 1, 1),
        };
    }

    private Rect ToOverlayRect(Rect documentRect)
    {
        var topLeft = ToOverlayPoint(documentRect.TopLeft);
        var bottomRight = ToOverlayPoint(documentRect.BottomRight);
        return new Rect(topLeft, bottomRight);
    }

    private Point ToOverlayPoint(Point documentPoint)
    {
        return AnnotationCanvas.TranslatePoint(ToViewportPoint(documentPoint), EditorRootGrid);
    }

    private static Rect GetArrowBounds(ArrowAnnotation arrow)
    {
        var minX = Math.Min(Math.Min(arrow.Start.X, arrow.Control1.X), Math.Min(arrow.Control2.X, arrow.End.X));
        var minY = Math.Min(Math.Min(arrow.Start.Y, arrow.Control1.Y), Math.Min(arrow.Control2.Y, arrow.End.Y));
        var maxX = Math.Max(Math.Max(arrow.Start.X, arrow.Control1.X), Math.Max(arrow.Control2.X, arrow.End.X));
        var maxY = Math.Max(Math.Max(arrow.Start.Y, arrow.Control1.Y), Math.Max(arrow.Control2.Y, arrow.End.Y));
        return new Rect(new Point(minX - 12, minY - 12), new Point(maxX + 12, maxY + 12));
    }

    private void UpdateInlineTextEditorState()
    {
        if (_editingTextAnnotation is null)
        {
            InlineTextPopup.IsOpen = false;
            return;
        }

        var viewportPoint = ToViewportPoint(_editingTextAnnotation.Location);
        InlineTextPopup.HorizontalOffset = viewportPoint.X;
        InlineTextPopup.VerticalOffset = viewportPoint.Y;

        if (InlineTextEditor.Text != _editingTextAnnotation.Text)
        {
            _isUpdatingInlineTextEditor = true;
            InlineTextEditor.Text = _editingTextAnnotation.Text;
            _isUpdatingInlineTextEditor = false;
        }

        if (!InlineTextEditor.FontSize.Equals(_editingTextAnnotation.FontSize))
        {
            InlineTextEditor.FontSize = _editingTextAnnotation.FontSize;
        }

        InlineTextEditorBorder.Background = AnnotationBase.MakeBrush(_editingTextAnnotation.GetBackgroundTint(isHovered: true));
        InlineTextEditorBorder.BorderBrush = AnnotationBase.MakeBrush(_editingTextAnnotation.GetFrameColor(isHovered: true));
        InlineTextEditor.Foreground = AnnotationBase.MakeBrush(_editingTextAnnotation.GetForegroundColor());

        InlineTextPopup.IsOpen = true;
        InlineTextEditorBorder.UpdateLayout();
        var backdropWidth = InlineTextEditorBorder.ActualWidth > 0 ? InlineTextEditorBorder.ActualWidth : Math.Max(InlineTextEditor.MinWidth, InlineTextEditorBorder.DesiredSize.Width);
        var backdropHeight = InlineTextEditorBorder.ActualHeight > 0 ? InlineTextEditorBorder.ActualHeight : Math.Max(InlineTextEditor.MinHeight, InlineTextEditorBorder.DesiredSize.Height);
        InlineTextBackdrop.Fill = _editingTextAnnotation.CreateBackdropBrush(this, backdropWidth, backdropHeight);
    }

    private void UpdateArtworkViewport()
    {
        if (_appliedCropRect is { } cropRect)
        {
            var normalized = NormalizeCropRect(cropRect);
            var viewportImage = CreateViewportImage(normalized);
            BaseImage.Source = viewportImage;
            BaseImage.Width = normalized.Width;
            BaseImage.Height = normalized.Height;
            AnnotationCanvas.Width = normalized.Width;
            AnnotationCanvas.Height = normalized.Height;
            ArtworkZoomHost.Width = normalized.Width;
            ArtworkZoomHost.Height = normalized.Height;
            ArtworkSurface.Width = normalized.Width;
            ArtworkSurface.Height = normalized.Height;
            BaseImage.RenderTransform = Transform.Identity;
            AnnotationCanvas.RenderTransform = new TranslateTransform(-normalized.X, -normalized.Y);
            UpdateImageDimensions(normalized.Width, normalized.Height);
            ApplyZoomTransform();
            QueueCenterArtworkInViewport();
            return;
        }

        BaseImage.Source = _workingImage;
        BaseImage.Width = _workingImage.PixelWidth;
        BaseImage.Height = _workingImage.PixelHeight;
        AnnotationCanvas.Width = _workingImage.PixelWidth;
        AnnotationCanvas.Height = _workingImage.PixelHeight;
        ArtworkZoomHost.Width = _workingImage.PixelWidth;
        ArtworkZoomHost.Height = _workingImage.PixelHeight;
        ArtworkSurface.Width = _workingImage.PixelWidth;
        ArtworkSurface.Height = _workingImage.PixelHeight;
        BaseImage.RenderTransform = Transform.Identity;
        AnnotationCanvas.RenderTransform = Transform.Identity;
        UpdateImageDimensions(_workingImage.PixelWidth, _workingImage.PixelHeight);
        ApplyZoomTransform();
        QueueCenterArtworkInViewport();
    }

    private void UpdateImageDimensions(double width, double height)
    {
        var roundedWidth = Math.Max(1, (int)Math.Round(width));
        var roundedHeight = Math.Max(1, (int)Math.Round(height));
        ImageDimensionsText.Text = $"{roundedWidth} x {roundedHeight}";
        ToolTipService.SetToolTip(ImageDimensionsText, $"Current artwork dimensions: {roundedWidth} x {roundedHeight} pixels");
    }

    private void FitWindowToArtwork()
    {
        if (ArtworkSurface.Width <= 0 || ArtworkSurface.Height <= 0)
        {
            return;
        }

        UpdateLayout();

        GrowWindowToFitArtwork();
        UpdateLayout();
        GrowWindowToFitArtwork();
        CenterWindowOnCurrentScreen();
        UpdateLayout();
        CenterArtworkInViewport();
    }

    private void QueueCenterArtworkInViewport()
    {
        if (!IsLoaded)
        {
            return;
        }

        Dispatcher.BeginInvoke(new Action(CenterArtworkInViewport), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void CenterArtworkInViewport()
    {
        ArtworkScrollViewer.UpdateLayout();
        ArtworkScrollViewer.ScrollToHorizontalOffset(Math.Max(0, ArtworkScrollViewer.ScrollableWidth / 2));
        ArtworkScrollViewer.ScrollToVerticalOffset(Math.Max(0, ArtworkScrollViewer.ScrollableHeight / 2));
        UpdateSelectionActionIsland();
        UpdateInlineTextEditorState();
    }

    private void ApplyZoomTransform()
    {
        ArtworkZoomTransform.ScaleX = _zoomLevel;
        ArtworkZoomTransform.ScaleY = _zoomLevel;
    }

    private void GrowWindowToFitArtwork()
    {
        var growWidth = ArtworkScrollViewer.ScrollableWidth;
        var growHeight = ArtworkScrollViewer.ScrollableHeight;
        if (growWidth <= 0 && growHeight <= 0)
        {
            return;
        }

        Width = Math.Max(MinWidth, Width + Math.Max(0, growWidth));
        Height = Math.Max(MinHeight, Height + Math.Max(0, growHeight));
    }

    private void CenterWindowOnCurrentScreen()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var screen = handle != IntPtr.Zero ? FormsScreen.FromHandle(handle) : FormsScreen.PrimaryScreen;
        if (screen is null)
        {
            return;
        }

        var workArea = screen.WorkingArea;
        Left = workArea.Left + Math.Max(0, (workArea.Width - Width) / 2);
        Top = workArea.Top + Math.Max(0, (workArea.Height - Height) / 2);
    }

    private BitmapSource CreateViewportImage(Rect cropRect)
    {
        var croppedBitmap = new CroppedBitmap(
            _workingImage,
            new Int32Rect(
                (int)Math.Round(cropRect.X),
                (int)Math.Round(cropRect.Y),
                Math.Max(1, (int)Math.Round(cropRect.Width)),
                Math.Max(1, (int)Math.Round(cropRect.Height))));
        croppedBitmap.Freeze();
        return croppedBitmap;
    }

    private Point ToDocumentPoint(Point viewportPoint)
    {
        if (_appliedCropRect is not { } cropRect)
        {
            return viewportPoint;
        }

        var normalized = NormalizeCropRect(cropRect);
        return viewportPoint + new Vector(normalized.X, normalized.Y);
    }

    internal Point ToViewportPoint(Point documentPoint)
    {
        if (_appliedCropRect is not { } cropRect)
        {
            return documentPoint;
        }

        var normalized = NormalizeCropRect(cropRect);
        return documentPoint - new Vector(normalized.X, normalized.Y);
    }

    private void BeginViewportPan(Point viewportPoint)
    {
        _dragOperation = DragOperation.Pan;
        _panStartViewportPoint = viewportPoint;
        _panStartHorizontalOffset = ArtworkScrollViewer.HorizontalOffset;
        _panStartVerticalOffset = ArtworkScrollViewer.VerticalOffset;
        _dragStart = null;
    }

    private void ArtworkScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (InlineTextEditor.IsKeyboardFocusWithin)
        {
            return;
        }

        var targetZoom = e.Delta > 0 ? _zoomLevel * ZoomStepFactor : _zoomLevel / ZoomStepFactor;
        targetZoom = Math.Clamp(targetZoom, MinZoomLevel, MaxZoomLevel);
        if (Math.Abs(targetZoom - _zoomLevel) < 0.001)
        {
            e.Handled = true;
            return;
        }

        var mouseInViewer = e.GetPosition(ArtworkScrollViewer);
        var anchorDocumentPoint = ToDocumentPoint(e.GetPosition(AnnotationCanvas));
        _zoomLevel = targetZoom;
        ApplyZoomTransform();
        UpdateLayout();

        var anchorAfterZoom = AnnotationCanvas.TranslatePoint(ToViewportPoint(anchorDocumentPoint), ArtworkScrollViewer);
        ArtworkScrollViewer.ScrollToHorizontalOffset(Math.Clamp(ArtworkScrollViewer.HorizontalOffset + (anchorAfterZoom.X - mouseInViewer.X), 0, ArtworkScrollViewer.ScrollableWidth));
        ArtworkScrollViewer.ScrollToVerticalOffset(Math.Clamp(ArtworkScrollViewer.VerticalOffset + (anchorAfterZoom.Y - mouseInViewer.Y), 0, ArtworkScrollViewer.ScrollableHeight));
        UpdateSelectionActionIsland();
        UpdateInlineTextEditorState();
        e.Handled = true;
    }

    private void ArtworkScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (Math.Abs(e.HorizontalChange) < 0.01 && Math.Abs(e.VerticalChange) < 0.01 && Math.Abs(e.ExtentWidthChange) < 0.01 && Math.Abs(e.ExtentHeightChange) < 0.01)
        {
            return;
        }

        UpdateSelectionActionIsland();
        UpdateInlineTextEditorState();
    }

    private void RenderCropOverlay(Rect cropSelection)
    {
        var cropRect = NormalizeCropRect(cropSelection);
        var width = AnnotationCanvas.Width;
        var height = AnnotationCanvas.Height;

        AddCropShade(0, 0, width, cropRect.Top);
        AddCropShade(0, cropRect.Top, cropRect.Left, cropRect.Height);
        AddCropShade(cropRect.Right, cropRect.Top, width - cropRect.Right, cropRect.Height);
        AddCropShade(0, cropRect.Bottom, width, height - cropRect.Bottom);

        var fill = new Rectangle
        {
            Width = cropRect.Width,
            Height = cropRect.Height,
            Fill = AnnotationBase.MakeBrush(Color.FromArgb(28, 18, 91, 80)),
            Stroke = AnnotationBase.MakeBrush(Color.FromRgb(18, 91, 80)),
            StrokeThickness = 2,
            StrokeDashArray = [8, 6],
            RadiusX = 12,
            RadiusY = 12,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(fill, cropRect.Left);
        Canvas.SetTop(fill, cropRect.Top);
        AnnotationCanvas.Children.Add(fill);

        foreach (var handlePoint in GetCropHandlePoints(cropRect).Values)
        {
            var handle = new Ellipse
            {
                Width = 12,
                Height = 12,
                Fill = AnnotationBase.MakeBrush(Color.FromRgb(18, 91, 80)),
                Stroke = Brushes.White,
                StrokeThickness = 2,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(handle, handlePoint.X - 6);
            Canvas.SetTop(handle, handlePoint.Y - 6);
            AnnotationCanvas.Children.Add(handle);
        }
    }

    private void AddCropShade(double left, double top, double width, double height)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var shade = new Rectangle
        {
            Width = width,
            Height = height,
            Fill = AnnotationBase.MakeBrush(Color.FromArgb(84, 17, 24, 27)),
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(shade, left);
        Canvas.SetTop(shade, top);
        AnnotationCanvas.Children.Add(shade);
    }

    private static Rect NormalizeCropRect(Rect rect)
    {
        return new Rect(rect.TopLeft, rect.BottomRight);
    }

    private void InitializeHistory()
    {
        _undoHistory.Clear();
        _redoHistory.Clear();
        _currentHistoryState = CaptureCurrentDocumentState();
        _currentHistorySignature = CreateHistorySignature(_currentHistoryState);
    }

    private EditorDocumentState CaptureCurrentDocumentState(Rect? cropRect = null)
    {
        return new EditorDocumentState(
            _workingImage,
            _annotations.Select(annotation => annotation.Clone()).ToList(),
            _appliedCropRect,
            cropRect ?? _lastCropSelection);
    }

    private void CommitHistoryState()
    {
        var state = CaptureCurrentDocumentState();
        var signature = CreateHistorySignature(state);
        if (signature == _currentHistorySignature)
        {
            return;
        }

        if (_currentHistoryState is not null)
        {
            _undoHistory.Push(_currentHistoryState);
        }

        _currentHistoryState = state;
        _currentHistorySignature = signature;
        _redoHistory.Clear();
    }

    private void UndoHistory()
    {
        CommitInlineTextEditing();
        if (_undoHistory.Count == 0 || _currentHistoryState is null)
        {
            RefreshCanvas();
            return;
        }

        _redoHistory.Push(_currentHistoryState);
        var state = _undoHistory.Pop();
        RestoreDocumentState(state);
        _currentHistoryState = state;
        _currentHistorySignature = CreateHistorySignature(state);
        RefreshCanvas();
    }

    private void RedoHistory()
    {
        CommitInlineTextEditing();
        if (_redoHistory.Count == 0 || _currentHistoryState is null)
        {
            RefreshCanvas();
            return;
        }

        _undoHistory.Push(_currentHistoryState);
        var state = _redoHistory.Pop();
        RestoreDocumentState(state);
        _currentHistoryState = state;
        _currentHistorySignature = CreateHistorySignature(state);
        RefreshCanvas();
    }

    private void RestoreDocumentState(EditorDocumentState state)
    {
        _annotations.Clear();
        _annotations.AddRange(state.Annotations.Select(annotation => annotation.Clone()));
        _appliedCropRect = state.AppliedCropRect;
        _lastCropSelection = state.LastCropSelection;
        ApplyWorkingImage(state.Image);
        ResetTransientEditorState();
    }

    private void ResetTransientEditorState()
    {
        _editingTextAnnotation = null;
        _selectedAnnotation = null;
        _hoveredAnnotation = null;
        _contextMenuAnnotation = null;
        _cropSelection = null;
        _cropDragOrigin = null;
        _activeCropHandle = CropHandle.None;
        _dragStart = null;
        _dragOperation = DragOperation.None;
        _activeHandle = null;
        _pendingDrawTool = null;
        _cropPreviewReturnState = null;
        InlineTextPopup.IsOpen = false;
        AnnotationCanvas.ReleaseMouseCapture();
    }

    private void RestoreCropPreviewReturnState()
    {
        if (_cropPreviewReturnState is null)
        {
            return;
        }

        RestoreDocumentState(_cropPreviewReturnState);
        _cropPreviewReturnState = null;
    }

    private bool _isCropPreviewActive()
    {
        return _cropPreviewReturnState is not null;
    }

    private bool TryBeginCropInteraction(Point point)
    {
        if (_cropSelection is not { } selection)
        {
            return false;
        }

        var normalized = NormalizeCropRect(selection);
        var handle = HitCropHandle(normalized, point);
        if (handle == CropHandle.None)
        {
            return false;
        }

        _cropSelection = normalized;
        _cropDragOrigin = normalized;
        _activeCropHandle = handle;
        _dragOperation = handle == CropHandle.Move ? DragOperation.CropMove : DragOperation.CropResize;
        return true;
    }

    private static CropHandle HitCropHandle(Rect rect, Point point)
    {
        const double handleRadius = 10;
        var handles = GetCropHandlePoints(rect);
        foreach (var handle in handles)
        {
            if ((handle.Value - point).Length <= handleRadius)
            {
                return handle.Key;
            }
        }

        if (rect.Contains(point))
        {
            return CropHandle.Move;
        }

        return CropHandle.None;
    }

    private static Dictionary<CropHandle, Point> GetCropHandlePoints(Rect rect)
    {
        return new Dictionary<CropHandle, Point>
        {
            [CropHandle.TopLeft] = rect.TopLeft,
            [CropHandle.Top] = new Point(rect.Left + (rect.Width / 2), rect.Top),
            [CropHandle.TopRight] = new Point(rect.Right, rect.Top),
            [CropHandle.Right] = new Point(rect.Right, rect.Top + (rect.Height / 2)),
            [CropHandle.BottomRight] = rect.BottomRight,
            [CropHandle.Bottom] = new Point(rect.Left + (rect.Width / 2), rect.Bottom),
            [CropHandle.BottomLeft] = new Point(rect.Left, rect.Bottom),
            [CropHandle.Left] = new Point(rect.Left, rect.Top + (rect.Height / 2)),
        };
    }

    private Rect ResizeCropRect(Rect origin, Point point, CropHandle handle)
    {
        var left = origin.Left;
        var top = origin.Top;
        var right = origin.Right;
        var bottom = origin.Bottom;

        switch (handle)
        {
            case CropHandle.Left:
            case CropHandle.TopLeft:
            case CropHandle.BottomLeft:
                left = point.X;
                break;
            case CropHandle.Right:
            case CropHandle.TopRight:
            case CropHandle.BottomRight:
                right = point.X;
                break;
        }

        switch (handle)
        {
            case CropHandle.Top:
            case CropHandle.TopLeft:
            case CropHandle.TopRight:
                top = point.Y;
                break;
            case CropHandle.Bottom:
            case CropHandle.BottomLeft:
            case CropHandle.BottomRight:
                bottom = point.Y;
                break;
        }

        return new Rect(new Point(left, top), new Point(right, bottom));
    }

    private Rect ClampCropRect(Rect rect)
    {
        var normalized = NormalizeCropRect(rect);
        var left = Math.Clamp(normalized.Left, 0, _workingImage.PixelWidth - 1);
        var top = Math.Clamp(normalized.Top, 0, _workingImage.PixelHeight - 1);
        var right = Math.Clamp(normalized.Right, left + 1, _workingImage.PixelWidth);
        var bottom = Math.Clamp(normalized.Bottom, top + 1, _workingImage.PixelHeight);
        return new Rect(new Point(left, top), new Point(right, bottom));
    }

    private enum EditorTool
    {
        Select,
        Crop,
        Arrow,
        Rectangle,
        Ellipse,
        Text,
    }

    private enum DragOperation
    {
        None,
        Draw,
        CropCreate,
        CropMove,
        CropResize,
        Move,
        Pan,
        Handle,
    }

    private enum CropHandle
    {
        None,
        Move,
        Left,
        Top,
        Right,
        Bottom,
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight,
    }

    private string CreateHistorySignature(EditorDocumentState state)
    {
        var annotationSignature = string.Join("|", state.Annotations.Select(CreateAnnotationSignature));
        return string.Join(";",
            RectToSignature(state.AppliedCropRect),
            RectToSignature(state.LastCropSelection),
            annotationSignature);
    }

    private static string CreateAnnotationSignature(AnnotationBase annotation)
    {
        return annotation switch
        {
            RectangleAnnotation rectangle => $"rect:{RectToSignature(rectangle.Bounds)}:{ColorToSignature(rectangle.StrokeColor)}",
            EllipseAnnotation ellipse => $"ellipse:{RectToSignature(ellipse.Bounds)}:{ColorToSignature(ellipse.StrokeColor)}",
            TextAnnotation text => $"text:{PointToSignature(text.Location)}:{FormatDouble(text.FontSize)}:{FormatDouble(text.BackgroundOpacity)}:{FormatDouble(text.BackgroundColorStrength)}:{ColorToSignature(text.TextColor)}:{Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(text.Text ?? string.Empty))}",
            ArrowAnnotation arrow => $"arrow:{PointToSignature(arrow.Start)}:{PointToSignature(arrow.Control1)}:{PointToSignature(arrow.Control2)}:{PointToSignature(arrow.End)}:{ColorToSignature(arrow.StrokeColor)}:{arrow.Control1IsSharp}:{arrow.Control2IsSharp}",
            _ => annotation.GetType().FullName ?? annotation.GetType().Name,
        };
    }

    private static string RectToSignature(Rect? rect)
    {
        return rect is { } value
            ? $"{FormatDouble(value.X)},{FormatDouble(value.Y)},{FormatDouble(value.Width)},{FormatDouble(value.Height)}"
            : "null";
    }

    private static string PointToSignature(Point point)
    {
        return $"{FormatDouble(point.X)},{FormatDouble(point.Y)}";
    }

    private static string ColorToSignature(Color color)
    {
        return $"{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    private static string FormatDouble(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private enum ThemePreference
    {
        System,
        Dark,
        Light,
    }

    private sealed record ThemePalette(
        Color ShellBackground,
        Color EditorWorkspace,
        Color EditorCanvas,
        Color IslandBackground,
        Color IslandBorder,
        Color IslandButton,
        Color IslandButtonHover,
        Color IslandButtonPressed,
        Color IslandButtonActive,
        Color IslandButtonActiveBorder,
        Color IslandText,
        Color IslandMutedText,
        Color PrimaryButton,
        Color PrimaryButtonBorder,
        Color PrimaryButtonHover,
        Color PrimaryButtonPressed,
        Color SecondaryButton,
        Color SecondaryButtonBorder,
        Color SecondaryButtonHover,
        Color SecondaryButtonPressed,
        Color FloatingCard,
        Color FloatingCardBorder,
        Color ShellScrim,
        Color ShellTopHighlight,
        Color InlineTextInputBackground,
        Color InlineTextInputBorder,
        Color InlineTextInputForeground,
        Color PopupPanelBackground,
        Color PopupPanelBorder,
        Color DeleteButtonBackground,
        Color DeleteButtonBorder,
        Color DeleteButtonForeground,
        Color IslandDivider,
        Color GlowStart,
        Color GlowMiddle,
        Color GlowEnd)
    {
        internal static readonly ThemePalette Dark = new(
            ShellBackground: Color.FromRgb(0x11, 0x14, 0x18),
            EditorWorkspace: Color.FromRgb(0x16, 0x1A, 0x20),
            EditorCanvas: Color.FromRgb(0x2A, 0x30, 0x39),
            IslandBackground: Color.FromArgb(0xD9, 0x1A, 0x1E, 0x25),
            IslandBorder: Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF),
            IslandButton: Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF),
            IslandButtonHover: Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF),
            IslandButtonPressed: Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF),
            IslandButtonActive: Color.FromRgb(0x1A, 0xA9, 0xD8),
            IslandButtonActiveBorder: Color.FromArgb(0x7D, 0xCC, 0xF3, 0xFF),
            IslandText: Color.FromRgb(0xF4, 0xF7, 0xFB),
            IslandMutedText: Color.FromArgb(0xA9, 0xC2, 0xCE, 0xDA),
            PrimaryButton: Color.FromRgb(0x1A, 0xA9, 0xD8),
            PrimaryButtonBorder: Color.FromArgb(0x7D, 0xCC, 0xF3, 0xFF),
            PrimaryButtonHover: Color.FromRgb(0x34, 0xBC, 0xE6),
            PrimaryButtonPressed: Color.FromRgb(0x14, 0x98, 0xC6),
            SecondaryButton: Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF),
            SecondaryButtonBorder: Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF),
            SecondaryButtonHover: Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF),
            SecondaryButtonPressed: Color.FromArgb(0x2B, 0xFF, 0xFF, 0xFF),
            FloatingCard: Color.FromArgb(0xD9, 0x16, 0x1A, 0x20),
            FloatingCardBorder: Color.FromArgb(0x2B, 0xFF, 0xFF, 0xFF),
            ShellScrim: Color.FromArgb(0x09, 0x00, 0x00, 0x00),
            ShellTopHighlight: Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF),
            InlineTextInputBackground: Color.FromArgb(0xD9, 0x1A, 0x1E, 0x25),
            InlineTextInputBorder: Color.FromArgb(0x66, 0xCC, 0xF3, 0xFF),
            InlineTextInputForeground: Color.FromRgb(0xF4, 0xF7, 0xFB),
            PopupPanelBackground: Color.FromArgb(0xF0, 0x18, 0x1C, 0x22),
            PopupPanelBorder: Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF),
            DeleteButtonBackground: Color.FromArgb(0x3A, 0xD9, 0x48, 0x41),
            DeleteButtonBorder: Color.FromArgb(0x80, 0xFF, 0xB0, 0xAA),
            DeleteButtonForeground: Colors.White,
            IslandDivider: Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF),
            GlowStart: Color.FromArgb(0x44, 0xFF, 0xB8, 0x6C),
            GlowMiddle: Color.FromArgb(0x14, 0xFF, 0x7A, 0x00),
            GlowEnd: Color.FromArgb(0x00, 0x00, 0x00, 0x00));

        internal static readonly ThemePalette Light = new(
            ShellBackground: Color.FromRgb(0xF4, 0xF7, 0xFB),
            EditorWorkspace: Color.FromRgb(0xEC, 0xF1, 0xF6),
            EditorCanvas: Color.FromRgb(0xD8, 0xE0, 0xE8),
            IslandBackground: Color.FromArgb(0xEC, 0xFF, 0xFF, 0xFF),
            IslandBorder: Color.FromArgb(0x33, 0x95, 0xA7, 0xB9),
            IslandButton: Color.FromArgb(0xE0, 0xF9, 0xFB, 0xFD),
            IslandButtonHover: Color.FromArgb(0xF2, 0xF2, 0xF8, 0xFC),
            IslandButtonPressed: Color.FromArgb(0xFF, 0xE6, 0xEE, 0xF4),
            IslandButtonActive: Color.FromRgb(0x0C, 0x8E, 0xC5),
            IslandButtonActiveBorder: Color.FromArgb(0x6E, 0x84, 0xCA, 0xEC),
            IslandText: Color.FromRgb(0x1D, 0x26, 0x31),
            IslandMutedText: Color.FromRgb(0x6C, 0x79, 0x87),
            PrimaryButton: Color.FromRgb(0x0C, 0x8E, 0xC5),
            PrimaryButtonBorder: Color.FromArgb(0x6E, 0x84, 0xCA, 0xEC),
            PrimaryButtonHover: Color.FromRgb(0x1A, 0x9F, 0xD5),
            PrimaryButtonPressed: Color.FromRgb(0x0A, 0x7D, 0xB1),
            SecondaryButton: Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF),
            SecondaryButtonBorder: Color.FromArgb(0xC7, 0xD6, 0xE2, 0xEC),
            SecondaryButtonHover: Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF),
            SecondaryButtonPressed: Color.FromArgb(0xFF, 0xEA, 0xF1, 0xF7),
            FloatingCard: Color.FromArgb(0xF4, 0xFF, 0xFF, 0xFF),
            FloatingCardBorder: Color.FromArgb(0xCC, 0xD5, 0xE0, 0xEA),
            ShellScrim: Color.FromArgb(0x00, 0x00, 0x00, 0x00),
            ShellTopHighlight: Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF),
            InlineTextInputBackground: Color.FromArgb(0xF4, 0xFF, 0xFF, 0xFF),
            InlineTextInputBorder: Color.FromArgb(0x99, 0x9D, 0xC0, 0xD8),
            InlineTextInputForeground: Color.FromRgb(0x1D, 0x26, 0x31),
            PopupPanelBackground: Color.FromArgb(0xFA, 0xFF, 0xFF, 0xFF),
            PopupPanelBorder: Color.FromArgb(0xCC, 0xD5, 0xE0, 0xEA),
            DeleteButtonBackground: Color.FromArgb(0x1F, 0xD9, 0x48, 0x41),
            DeleteButtonBorder: Color.FromArgb(0x6A, 0xE0, 0x89, 0x84),
            DeleteButtonForeground: Color.FromRgb(0x9C, 0x2E, 0x2C),
            IslandDivider: Color.FromArgb(0xCC, 0xD5, 0xE0, 0xEA),
            GlowStart: Color.FromArgb(0x26, 0x69, 0xC2, 0xFF),
            GlowMiddle: Color.FromArgb(0x18, 0x8F, 0xD4, 0xBE),
            GlowEnd: Color.FromArgb(0x00, 0x00, 0x00, 0x00));
    }

    private sealed record EditorDocumentState(BitmapSource Image, List<AnnotationBase> Annotations, Rect? AppliedCropRect, Rect? LastCropSelection);
}

internal abstract class AnnotationBase
{
    protected static readonly Brush HandleBrush = new SolidColorBrush(Color.FromRgb(242, 162, 58));
    private static readonly DoubleCollection MarchingDashArray = [4, 4];

    static AnnotationBase()
    {
        if (HandleBrush.CanFreeze)
        {
            HandleBrush.Freeze();
        }
    }

    public abstract void Render(Canvas canvas, bool isSelected, bool isHovered);

    public abstract bool HitTest(Point point);

    public abstract string? HitHandle(Point point);

    public abstract void Move(Vector delta);

    public abstract void MoveHandle(string handle, Point point, bool constrainToSquare);

    public abstract void UpdateFromAnchor(Point anchor, Point current, bool constrainToSquare);

    public abstract void SetColor(Color color);

    public abstract AnnotationBase Clone();

    protected static void AddHandle(Canvas canvas, Point point)
    {
        var ellipse = new Ellipse
        {
            Width = 14,
            Height = 14,
            Fill = HandleBrush,
            Stroke = Brushes.White,
            StrokeThickness = 2,
        };

		Canvas.SetLeft(ellipse, point.X - 7);
		Canvas.SetTop(ellipse, point.Y - 7);
        canvas.Children.Add(ellipse);
    }

    protected static double Distance(Point a, Point b)
    {
        return (a - b).Length;
    }

    internal static Rect CreateRectFromPoints(Point anchor, Point current, bool constrainToSquare)
    {
        var deltaX = current.X - anchor.X;
        var deltaY = current.Y - anchor.Y;

        if (constrainToSquare)
        {
            var size = Math.Max(Math.Abs(deltaX), Math.Abs(deltaY));
            deltaX = Math.Sign(deltaX == 0 ? 1 : deltaX) * size;
            deltaY = Math.Sign(deltaY == 0 ? 1 : deltaY) * size;
        }

        var left = deltaX >= 0 ? anchor.X : anchor.X + deltaX;
        var top = deltaY >= 0 ? anchor.Y : anchor.Y + deltaY;
        return new Rect(new Point(left, top), new Size(Math.Abs(deltaX), Math.Abs(deltaY)));
    }

    internal static SolidColorBrush MakeBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        if (brush.CanFreeze)
        {
            brush.Freeze();
        }

        return brush;
    }

    protected static Color Darken(Color color, double amount)
    {
        var scale = 1 - amount;
        return Color.FromRgb(
            (byte)Math.Clamp(color.R * scale, 0, 255),
            (byte)Math.Clamp(color.G * scale, 0, 255),
            (byte)Math.Clamp(color.B * scale, 0, 255));
    }

    protected static Color WithAlpha(Color color, byte alpha)
    {
        return Color.FromArgb(alpha, color.R, color.G, color.B);
    }

    protected static void AddMarchingAntsRectangle(Canvas canvas, Rect rect, double radiusX, double radiusY)
    {
        var dark = new Rectangle
        {
            Width = rect.Width,
            Height = rect.Height,
            RadiusX = radiusX,
            RadiusY = radiusY,
            Stroke = Brushes.Black,
            StrokeThickness = 1,
            StrokeDashArray = MarchingDashArray,
            Fill = Brushes.Transparent,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(dark, rect.Left);
        Canvas.SetTop(dark, rect.Top);
        canvas.Children.Add(dark);

        var light = new Rectangle
        {
            Width = rect.Width,
            Height = rect.Height,
            RadiusX = radiusX,
            RadiusY = radiusY,
            Stroke = Brushes.White,
            StrokeThickness = 1,
            StrokeDashArray = MarchingDashArray,
            StrokeDashOffset = 4,
            Fill = Brushes.Transparent,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(light, rect.Left);
        Canvas.SetTop(light, rect.Top);
        canvas.Children.Add(light);
    }

    protected static void AddMarchingAntsEllipse(Canvas canvas, Rect rect)
    {
        var dark = new Ellipse
        {
            Width = rect.Width,
            Height = rect.Height,
            Stroke = Brushes.Black,
            StrokeThickness = 1,
            StrokeDashArray = MarchingDashArray,
            Fill = Brushes.Transparent,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(dark, rect.Left);
        Canvas.SetTop(dark, rect.Top);
        canvas.Children.Add(dark);

        var light = new Ellipse
        {
            Width = rect.Width,
            Height = rect.Height,
            Stroke = Brushes.White,
            StrokeThickness = 1,
            StrokeDashArray = MarchingDashArray,
            StrokeDashOffset = 4,
            Fill = Brushes.Transparent,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(light, rect.Left);
        Canvas.SetTop(light, rect.Top);
        canvas.Children.Add(light);
    }

    protected static void AddMarchingAntsPath(Canvas canvas, Geometry geometry)
    {
        var dark = new ShapePath
        {
            Data = geometry,
            Stroke = Brushes.Black,
            StrokeThickness = 1,
            StrokeDashArray = MarchingDashArray,
            Fill = Brushes.Transparent,
            IsHitTestVisible = false,
        };
        canvas.Children.Add(dark);

        var light = new ShapePath
        {
            Data = geometry,
            Stroke = Brushes.White,
            StrokeThickness = 1,
            StrokeDashArray = MarchingDashArray,
            StrokeDashOffset = 4,
            Fill = Brushes.Transparent,
            IsHitTestVisible = false,
        };
        canvas.Children.Add(light);
    }
}

internal sealed class RectangleAnnotation : AnnotationBase
{
    public Color StrokeColor { get; private set; } = EditorWindow.DefaultAccentColor;

    public Rect Bounds { get; set; }

    public override void Render(Canvas canvas, bool isSelected, bool isHovered)
    {
        var normalized = Normalize(Bounds);
        var strokeBrush = MakeBrush(isHovered ? Darken(StrokeColor, 0.24) : StrokeColor);
        var fillBrush = MakeBrush(WithAlpha(StrokeColor, (byte)(isHovered ? 52 : 28)));
        var rectangle = new Rectangle
        {
            Width = normalized.Width,
            Height = normalized.Height,
            RadiusX = 14,
            RadiusY = 14,
            Stroke = strokeBrush,
            Fill = fillBrush,
            StrokeThickness = isSelected ? 5 : 4,
        };
        Canvas.SetLeft(rectangle, normalized.Left);
        Canvas.SetTop(rectangle, normalized.Top);
        canvas.Children.Add(rectangle);

        if (isSelected)
        {
            AddMarchingAntsRectangle(canvas, normalized, 14, 14);
        }

        if (isSelected)
        {
            AddHandle(canvas, normalized.TopLeft);
            AddHandle(canvas, normalized.BottomRight);
        }
    }

    public override bool HitTest(Point point)
    {
        return Normalize(Bounds).Contains(point);
    }

    public override string? HitHandle(Point point)
    {
        var normalized = Normalize(Bounds);
        if (Distance(normalized.TopLeft, point) <= 10)
        {
            return "TopLeft";
        }

        if (Distance(normalized.BottomRight, point) <= 10)
        {
            return "BottomRight";
        }

        return null;
    }

    public override void Move(Vector delta)
    {
        Bounds = new Rect(Bounds.TopLeft + delta, Bounds.BottomRight + delta);
    }

    public override void MoveHandle(string handle, Point point, bool constrainToSquare)
    {
        var normalized = Normalize(Bounds);
        var oppositeCorner = handle switch
        {
            "TopLeft" => normalized.BottomRight,
            "BottomRight" => normalized.TopLeft,
            _ => normalized.BottomRight,
        };

        Bounds = handle switch
        {
            "TopLeft" => CreateRectFromPoints(oppositeCorner, point, constrainToSquare),
            "BottomRight" => CreateRectFromPoints(oppositeCorner, point, constrainToSquare),
            _ => Bounds,
        };
    }

    public override void UpdateFromAnchor(Point anchor, Point current, bool constrainToSquare)
    {
        Bounds = CreateRectFromPoints(anchor, current, constrainToSquare);
    }

    public override void SetColor(Color color)
    {
        StrokeColor = color;
    }

    public override AnnotationBase Clone()
    {
        var clone = new RectangleAnnotation
        {
            Bounds = Bounds,
        };
        clone.SetColor(StrokeColor);
        return clone;
    }

    private static Rect Normalize(Rect rect)
    {
        return new Rect(rect.TopLeft, rect.BottomRight);
    }
}

internal sealed class EllipseAnnotation : AnnotationBase
{
    public Color StrokeColor { get; private set; } = EditorWindow.DefaultAccentColor;

    public Rect Bounds { get; set; }

    public override void Render(Canvas canvas, bool isSelected, bool isHovered)
    {
        var normalized = Normalize(Bounds);
        var strokeBrush = MakeBrush(isHovered ? Darken(StrokeColor, 0.24) : StrokeColor);
        var ellipse = new Ellipse
        {
            Width = normalized.Width,
            Height = normalized.Height,
            Stroke = strokeBrush,
            Fill = MakeBrush(WithAlpha(StrokeColor, (byte)(isHovered ? 26 : 10))),
            StrokeThickness = isSelected ? 5 : 4,
        };
        Canvas.SetLeft(ellipse, normalized.Left);
        Canvas.SetTop(ellipse, normalized.Top);
        canvas.Children.Add(ellipse);

        if (isSelected)
        {
            AddMarchingAntsEllipse(canvas, normalized);
        }

        if (isSelected)
        {
            AddHandle(canvas, normalized.TopLeft);
            AddHandle(canvas, normalized.BottomRight);
        }
    }

    public override bool HitTest(Point point)
    {
        return Normalize(Bounds).Contains(point);
    }

    public override string? HitHandle(Point point)
    {
        var normalized = Normalize(Bounds);
        if (Distance(normalized.TopLeft, point) <= 10)
        {
            return "TopLeft";
        }

        if (Distance(normalized.BottomRight, point) <= 10)
        {
            return "BottomRight";
        }

        return null;
    }

    public override void Move(Vector delta)
    {
        Bounds = new Rect(Bounds.TopLeft + delta, Bounds.BottomRight + delta);
    }

    public override void MoveHandle(string handle, Point point, bool constrainToSquare)
    {
        var normalized = Normalize(Bounds);
        var oppositeCorner = handle switch
        {
            "TopLeft" => normalized.BottomRight,
            "BottomRight" => normalized.TopLeft,
            _ => normalized.BottomRight,
        };

        Bounds = handle switch
        {
            "TopLeft" => CreateRectFromPoints(oppositeCorner, point, constrainToSquare),
            "BottomRight" => CreateRectFromPoints(oppositeCorner, point, constrainToSquare),
            _ => Bounds,
        };
    }

    public override void UpdateFromAnchor(Point anchor, Point current, bool constrainToSquare)
    {
        Bounds = CreateRectFromPoints(anchor, current, constrainToSquare);
    }

    public override void SetColor(Color color)
    {
        StrokeColor = color;
    }

    public override AnnotationBase Clone()
    {
        var clone = new EllipseAnnotation
        {
            Bounds = Bounds,
        };
        clone.SetColor(StrokeColor);
        return clone;
    }

    private static Rect Normalize(Rect rect)
    {
        return new Rect(rect.TopLeft, rect.BottomRight);
    }
}

internal sealed class TextAnnotation : AnnotationBase
{
    private const double MaxTextWidth = 340;
    private const double DefaultBackgroundOpacity = 0.80;
    private const double DefaultBackgroundColorStrength = 0.30;

    public Color TextColor { get; private set; } = EditorWindow.DefaultTextColor;

    public double FontSize { get; private set; } = 26;

    public double BackgroundOpacity { get; private set; } = DefaultBackgroundOpacity;

    public double BackgroundColorStrength { get; private set; } = DefaultBackgroundColorStrength;

    public Point Location { get; set; }

    public string Text { get; set; } = "Text";

    public override void Render(Canvas canvas, bool isSelected, bool isHovered)
    {
        var editorWindow = Window.GetWindow(canvas) as EditorWindow;
        var surface = CreateTextSurface(editorWindow, isHovered);
        var bounds = MeasureBounds();
        Canvas.SetLeft(surface, Location.X);
        Canvas.SetTop(surface, Location.Y);
        canvas.Children.Add(surface);

        if (isSelected)
        {
            AddMarchingAntsRectangle(canvas, new Rect(Location, bounds), 10, 10);
        }

        if (isSelected)
        {
            AddHandle(canvas, Location);
        }
    }

    public override bool HitTest(Point point)
    {
        return new Rect(Location, MeasureBounds()).Contains(point);
    }

    public override string? HitHandle(Point point)
    {
        return Distance(Location, point) <= 10 ? "Location" : null;
    }

    public override void Move(Vector delta)
    {
        Location += delta;
    }

    public override void MoveHandle(string handle, Point point, bool constrainToSquare)
    {
        Location = point;
    }

    public override void UpdateFromAnchor(Point anchor, Point current, bool constrainToSquare)
    {
        Location = current;
    }

    public override void SetColor(Color color)
    {
        TextColor = color;
    }

    public void SetFontSize(double fontSize)
    {
        FontSize = fontSize;
    }

    public void SetBackgroundOpacity(double opacity)
    {
        BackgroundOpacity = Math.Clamp(opacity, 0, 1);
    }

    public void SetBackgroundColorStrength(double strength)
    {
        BackgroundColorStrength = Math.Clamp(strength, 0, 1);
    }

    internal Size GetBounds()
    {
        return MeasureBounds();
    }

    public override AnnotationBase Clone()
    {
        var clone = new TextAnnotation
        {
            Location = Location,
            Text = Text,
        };
        clone.SetColor(TextColor);
        clone.SetFontSize(FontSize);
        clone.SetBackgroundOpacity(BackgroundOpacity);
        clone.SetBackgroundColorStrength(BackgroundColorStrength);
        return clone;
    }

    private FrameworkElement CreateTextSurface(EditorWindow? editorWindow, bool isHovered)
    {
        var borderColor = isHovered ? Darken(TextColor, 0.18) : TextColor;
        var contentBorder = CreateContentBorder(isHovered, borderColor);
        contentBorder.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var bounds = contentBorder.DesiredSize;
        var grid = new Grid
        {
            Width = bounds.Width,
            Height = bounds.Height,
        };

        var blurLayer = new Rectangle
        {
            RadiusX = 10,
            RadiusY = 10,
            Effect = new BlurEffect { Radius = 14, RenderingBias = RenderingBias.Quality },
            Opacity = 1,
            Fill = CreateBackdropBrush(editorWindow, grid.Width, grid.Height),
        };

        grid.Children.Add(blurLayer);
        grid.Children.Add(contentBorder);
        grid.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return grid;
    }

    internal Brush CreateBackdropBrush(EditorWindow? editorWindow, double width, double height)
    {
        if (editorWindow is null || !editorWindow.IsLoaded)
        {
            return MakeBrush(Color.FromRgb(226, 231, 228));
        }

        var viewportLocation = editorWindow.ToViewportPoint(Location);
        var viewbox = new Rect(viewportLocation.X, viewportLocation.Y, Math.Max(1, width), Math.Max(1, height));

        return new VisualBrush(editorWindow.BaseImage)
        {
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewbox = viewbox,
            Stretch = Stretch.Fill,
            AlignmentX = AlignmentX.Left,
            AlignmentY = AlignmentY.Top,
        };
    }

    private Size MeasureBounds()
    {
        var contentBorder = CreateContentBorder(isHovered: false, borderColor: Darken(TextColor, 0.18));
        contentBorder.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return contentBorder.DesiredSize;
    }

    private Border CreateContentBorder(bool isHovered, Color borderColor)
    {
        var backgroundColor = GetBackgroundTint(isHovered);
        var foregroundColor = GetForegroundColor();
        var frameColor = GetFrameColor(isHovered);
        var textEffectColor = GetTextEffectColor(foregroundColor);

        return new Border
        {
            Background = MakeBrush(backgroundColor),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 8, 12, 8),
            BorderThickness = new Thickness(isHovered ? 2 : 1),
            BorderBrush = MakeBrush(frameColor),
            Child = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(Text) ? " " : Text,
                FontSize = FontSize,
                Foreground = MakeBrush(foregroundColor),
                Effect = new DropShadowEffect
                {
                    BlurRadius = 2,
                    ShadowDepth = 0,
                    Color = textEffectColor,
                    Opacity = 1,
                },
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = MaxTextWidth,
            },
        };
    }

    internal Color GetBackgroundTint(bool isHovered)
    {
        var tint = BlendWithWhite(TextColor, 1 - BackgroundColorStrength);
        var hoveredOpacity = Math.Min(1, BackgroundOpacity + 0.08);
        var alpha = (byte)Math.Round((isHovered ? hoveredOpacity : BackgroundOpacity) * 255);
        return Color.FromArgb(alpha, tint.R, tint.G, tint.B);
    }

    internal Color GetForegroundColor()
    {
        var background = BlendOntoWhite(GetBackgroundTint(isHovered: false));
        var candidates = new[]
        {
            Color.FromRgb(0, 0, 0),
            Color.FromRgb(255, 255, 255),
        };

        return GetHighestContrastColor(background, candidates);
    }

    private static Color GetTextEffectColor(Color foregroundColor)
    {
        var luminance = GetRelativeLuminance(foregroundColor);
        return luminance < 0.5
            ? Color.FromArgb(210, 255, 255, 255)
            : Color.FromArgb(210, 0, 0, 0);
    }

    internal Color GetFrameColor(bool isHovered)
    {
        return isHovered ? Darken(TextColor, 0.12) : WithAlpha(Darken(TextColor, 0.38), 128);
    }

    private static Color BlendWithWhite(Color color, double whiteAmount)
    {
        var baseAmount = 1d - whiteAmount;
        return Color.FromRgb(
            (byte)Math.Clamp((color.R * baseAmount) + (255 * whiteAmount), 0, 255),
            (byte)Math.Clamp((color.G * baseAmount) + (255 * whiteAmount), 0, 255),
            (byte)Math.Clamp((color.B * baseAmount) + (255 * whiteAmount), 0, 255));
    }

    private static Color BlendOntoWhite(Color color)
    {
        var alpha = color.A / 255d;
        return Color.FromRgb(
            (byte)Math.Clamp((color.R * alpha) + (255 * (1 - alpha)), 0, 255),
            (byte)Math.Clamp((color.G * alpha) + (255 * (1 - alpha)), 0, 255),
            (byte)Math.Clamp((color.B * alpha) + (255 * (1 - alpha)), 0, 255));
    }

    private static double GetContrastRatio(Color foreground, Color background)
    {
        var foregroundLuminance = GetRelativeLuminance(foreground);
        var backgroundLuminance = GetRelativeLuminance(background);
        var lighter = Math.Max(foregroundLuminance, backgroundLuminance);
        var darker = Math.Min(foregroundLuminance, backgroundLuminance);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static Color GetHighestContrastColor(Color background, IEnumerable<Color> candidates)
    {
        var bestColor = background;
        var bestContrast = double.MinValue;

        foreach (var candidate in candidates)
        {
            var contrast = GetContrastRatio(candidate, background);
            if (contrast <= bestContrast)
            {
                continue;
            }

            bestContrast = contrast;
            bestColor = candidate;
        }

        return bestColor;
    }

    private static double GetRelativeLuminance(Color color)
    {
        static double Channel(double value)
        {
            value /= 255d;
            return value <= 0.03928 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        var red = Channel(color.R);
        var green = Channel(color.G);
        var blue = Channel(color.B);
        return (0.2126 * red) + (0.7152 * green) + (0.0722 * blue);
    }
}

internal sealed class ArrowAnnotation : AnnotationBase
{
    private static readonly Pen ArrowHitTestPen = CreateArrowHitTestPen();

    public Point Start { get; set; }

    public Point Control1 { get; set; }

    public Point Control2 { get; set; }

    public Point End { get; set; }

    public Color StrokeColor { get; private set; } = EditorWindow.DefaultArrowColor;

    public bool Control1IsSharp { get; private set; }

    public bool Control2IsSharp { get; private set; }

    public static ArrowAnnotation Create(Point point)
    {
        return new ArrowAnnotation
        {
            Start = point,
            Control1 = new Point(point.X + 26, point.Y),
            Control2 = new Point(point.X + 54, point.Y),
            End = new Point(point.X + 80, point.Y),
        };
    }

    public override void Render(Canvas canvas, bool isSelected, bool isHovered)
    {
        var geometry = CreatePathGeometry(trimmedForArrowHead: true);
        var strokeColor = MakeBrush(isHovered ? Darken(StrokeColor, 0.18) : StrokeColor);

        var path = new ShapePath
        {
            Data = geometry,
            Stroke = strokeColor,
            StrokeThickness = isSelected ? 7 : 6,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
        };
        canvas.Children.Add(path);

        if (isSelected)
        {
            AddMarchingAntsPath(canvas, geometry);
        }

        var arrowHead = CreateArrowHead(strokeColor);
        canvas.Children.Add(arrowHead);

        if (isSelected)
        {
            AddHandle(canvas, Start);
            AddHandle(canvas, Control1);
            AddHandle(canvas, Control2);
            AddHandle(canvas, End);
        }
    }

    public override bool HitTest(Point point)
    {
        return CreatePathGeometry().StrokeContains(ArrowHitTestPen, point);
    }

    public override string? HitHandle(Point point)
    {
        if (Distance(Start, point) <= 10)
        {
            return "Start";
        }

		if (Distance(Control1, point) <= 14)
        {
            return "Control1";
        }

		if (Distance(Control2, point) <= 14)
        {
            return "Control2";
        }

        if (Distance(End, point) <= 10)
        {
            return "End";
        }

        return null;
    }

    public override void Move(Vector delta)
    {
        Start += delta;
        Control1 += delta;
        Control2 += delta;
        End += delta;
    }

    public override void MoveHandle(string handle, Point point, bool constrainToSquare)
    {
        switch (handle)
        {
            case "Start":
                Start = point;
                break;
            case "Control1":
                Control1 = point;
                break;
            case "Control2":
                Control2 = point;
                break;
            case "End":
                End = point;
                break;
        }
    }

    public override void UpdateFromAnchor(Point anchor, Point current, bool constrainToSquare)
    {
        Start = anchor;
        End = current;
        var delta = current - anchor;
        Control1 = new Point(anchor.X + (delta.X / 3), anchor.Y + (delta.Y / 3));
        Control2 = new Point(anchor.X + ((delta.X * 2) / 3), anchor.Y + ((delta.Y * 2) / 3));
        Control1IsSharp = false;
        Control2IsSharp = false;
    }

    public override void SetColor(Color color)
    {
        StrokeColor = color;
    }

    public override AnnotationBase Clone()
    {
        var clone = new ArrowAnnotation
        {
            Start = Start,
            Control1 = Control1,
            Control2 = Control2,
            End = End,
        };
        clone.SetColor(StrokeColor);
        if (Control1IsSharp)
        {
            clone.ToggleCornerStyle("Control1");
        }

        if (Control2IsSharp)
        {
            clone.ToggleCornerStyle("Control2");
        }

        return clone;
    }

    public void ToggleCornerStyle(string handle)
    {
        switch (handle)
        {
            case "Control1":
                Control1IsSharp = !Control1IsSharp;
                break;
            case "Control2":
                Control2IsSharp = !Control2IsSharp;
                break;
        }
    }

    private Polygon CreateArrowHead(Brush fillBrush)
    {
        var direction = GetArrowHeadDirection();
        if (direction.Length < 0.001)
        {
            direction = End - Start;
        }

        direction.Normalize();
        var normal = new Vector(-direction.Y, direction.X);
        var tip = End;
        var basePoint = End - (direction * 20);
        var polygon = new Polygon
        {
            Fill = fillBrush,
            Points =
            {
                tip,
                basePoint + (normal * 8),
                basePoint - (normal * 8),
            },
        };

        return polygon;
    }

    private PathGeometry CreatePathGeometry(bool trimmedForArrowHead = false)
    {
        var points = new[] { Start, Control1, Control2, trimmedForArrowHead ? GetArrowShaftEnd() : End };
        var figure = new PathFigure
        {
            StartPoint = Start,
            IsFilled = false,
            IsClosed = false,
        };

        foreach (var segment in CreatePathSegments(points))
        {
            figure.Segments.Add(segment);
        }

        return new PathGeometry([figure]);
    }

    private IEnumerable<PathSegment> CreatePathSegments(Point[] points)
    {
        for (var index = 1; index < points.Length; index++)
        {
            var start = points[index - 1];
            var end = points[index];
            var hasOutgoingControl = HasSmoothCorner(index - 1);
            var hasIncomingControl = HasSmoothCorner(index);

            if (!hasOutgoingControl && !hasIncomingControl)
            {
                yield return new LineSegment(end, true);
                continue;
            }

            if (hasOutgoingControl && hasIncomingControl)
            {
                yield return new BezierSegment(GetOutgoingControlPoint(index - 1, points), GetIncomingControlPoint(index, points), end, true);
                continue;
            }

            var control = hasOutgoingControl ? GetOutgoingControlPoint(index - 1, points) : GetIncomingControlPoint(index, points);
            yield return new QuadraticBezierSegment(control, end, true);
        }
    }

    private bool HasSmoothCorner(int pointIndex)
    {
        return pointIndex switch
        {
            1 => !Control1IsSharp,
            2 => !Control2IsSharp,
            _ => false,
        };
    }

    private Point GetOutgoingControlPoint(int pointIndex, Point[] points)
    {
        var tangent = GetTangent(pointIndex, points);
        var handleLength = GetHandleLength(pointIndex, points);
        return points[pointIndex] + (tangent * handleLength);
    }

    private Point GetIncomingControlPoint(int pointIndex, Point[] points)
    {
        var tangent = GetTangent(pointIndex, points);
        var handleLength = GetHandleLength(pointIndex, points);
        return points[pointIndex] - (tangent * handleLength);
    }

    private Vector GetTangent(int pointIndex, Point[] points)
    {
        var incoming = points[pointIndex] - points[pointIndex - 1];
        var outgoing = points[pointIndex + 1] - points[pointIndex];

        if (incoming.Length < 0.001 && outgoing.Length < 0.001)
        {
            return new Vector(1, 0);
        }

        if (incoming.Length >= 0.001)
        {
            incoming.Normalize();
        }

        if (outgoing.Length >= 0.001)
        {
            outgoing.Normalize();
        }

        var tangent = incoming + outgoing;
        if (tangent.Length < 0.001)
        {
            tangent = outgoing.Length >= 0.001 ? outgoing : incoming;
        }

        tangent.Normalize();
        return tangent;
    }

    private double GetHandleLength(int pointIndex, Point[] points)
    {
        var incomingLength = (points[pointIndex] - points[pointIndex - 1]).Length;
        var outgoingLength = (points[pointIndex + 1] - points[pointIndex]).Length;
        return Math.Min(incomingLength, outgoingLength) * 0.3;
    }

    private Vector GetArrowHeadDirection()
    {
        var flattened = CreatePathGeometry().GetFlattenedPathGeometry();
        Point? previous = null;
        Point? last = null;

        foreach (var figure in flattened.Figures)
        {
            previous = figure.StartPoint;
            last = figure.StartPoint;

            foreach (var segment in figure.Segments.OfType<PolyLineSegment>())
            {
                foreach (var point in segment.Points)
                {
                    previous = last;
                    last = point;
                }
            }
        }

        return previous is { } before && last is { } end ? end - before : End - Control2;
    }

    private Point GetArrowShaftEnd()
    {
        var direction = GetArrowHeadDirection();
        if (direction.Length < 0.001)
        {
            direction = End - Start;
        }

        if (direction.Length < 0.001)
        {
            return End;
        }

        direction.Normalize();
        return End - (direction * 18);
    }

    private static Pen CreateArrowHitTestPen()
    {
        var pen = new Pen(Brushes.Black, 16);
        if (pen.CanFreeze)
        {
            pen.Freeze();
        }

        return pen;
    }
}