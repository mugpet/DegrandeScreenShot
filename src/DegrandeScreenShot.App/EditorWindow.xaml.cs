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
using DegrandeScreenShot.App.Services;
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
    private static readonly Color LegacyDefaultArrowColor = Color.FromRgb(242, 162, 58);
    internal static readonly Color DefaultArrowColor = Color.FromRgb(236, 72, 153);
    internal static readonly Color DefaultTextColor = Color.FromRgb(108, 75, 22);
    internal static readonly Color DefaultObscureColor = Color.FromRgb(217, 72, 65);
    internal static readonly Color DefaultHighlightColor = Color.FromRgb(245, 217, 10);
    internal const double DefaultTextFontSize = 26;
    internal const double DefaultTextBackgroundOpacity = 0.80;
    internal const double DefaultTextBackgroundStrength = 0.30;
    internal const double DefaultObscureColorStrength = 0.15;
    internal const double DefaultObscureBlurLevel = 0.00;
    internal const double DefaultObscurePixelationLevel = 0.20;
    internal const double DefaultHighlightStrength = 0.55;
    internal const double DefaultShapeShadowStrength = 0.00;
    internal const double DefaultShapeBorderWidth = 4.00;
    internal const double DefaultTextShadowStrength = 0.00;
    internal const double DefaultTextBorderWidth = 1.00;
    internal const double DefaultObscureShadowStrength = 0.00;
    internal const double DefaultObscureBorderWidth = 0.00;
    internal const double DefaultHighlightShadowStrength = 0.00;
    internal const double DefaultHighlightBorderWidth = 0.00;
    internal const double DefaultArrowTailScale = 1.00;
    internal const double DefaultArrowBodyScale = 0.20;
    internal const double DefaultArrowFrontScale = 0.30;
    internal const double DefaultArrowHeadScale = 0.40;
    internal const double DefaultArrowTailHeadScale = 1.00;
    internal const double DefaultArrowTailRoundness = 0.45;
    internal const double DefaultArrowHeadRoundness = 0.00;
    internal const double DefaultArrowShadowStrength = 0.00;
    internal const double DefaultArrowBorderWidth = 2.00;
    private const string WindowsThemeRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const int DwmUseImmersiveDarkModeAttribute = 20;
    private const int DwmBorderColorAttribute = 34;
    private const int DwmCaptionColorAttribute = 35;
    private const int DwmTextColorAttribute = 36;
    private const uint DwmColorDefault = 0xFFFFFFFF;
    private const double SelectionActionIslandSpacing = 18;
    private const double SelectionActionIslandViewportPadding = 18;
    private const double MinZoomLevel = 0.05;
    private const double MaxZoomLevel = 4.0;
    private const double ZoomStepFactor = 1.15;
    private const int MaxArrowShapePresets = 12;

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
    private bool _isUpdatingTextFontSize;
    private bool _isUpdatingTextBackgroundOpacity;
    private bool _isUpdatingTextBackgroundStrength;
    private bool _isUpdatingObscureBlurLevel;
    private bool _isUpdatingObscurePixelationLevel;
    private bool _isUpdatingObscureColorStrength;
    private bool _isUpdatingHighlightStrength;
    private bool _isUpdatingObjectShadowStrength;
    private bool _isUpdatingObjectBorderWidth;
    private bool _hasFittedInitialWindowSize;
    private SnapshotPreviewWindow? _previewWindow;
    private double _zoomLevel = 1.0;
    private Point? _panStartViewportPoint;
    private double _panStartHorizontalOffset;
    private double _panStartVerticalOffset;
    private ThemePreference _themePreference = ThemePreference.System;
    private Color _lastShapeColor = DefaultAccentColor;
    private Color _lastArrowColor = DefaultArrowColor;
    private ArrowStyle _lastArrowStyle = ArrowStyle.BrushStroke;
    private double _lastArrowTailScale = DefaultArrowTailScale;
    private double _lastArrowBodyScale = DefaultArrowBodyScale;
    private double _lastArrowFrontScale = DefaultArrowFrontScale;
    private double _lastArrowHeadScale = DefaultArrowHeadScale;
    private double _lastArrowTailHeadScale = DefaultArrowTailHeadScale;
    private double _lastArrowTailRoundness = DefaultArrowTailRoundness;
    private double _lastArrowHeadRoundness = DefaultArrowHeadRoundness;
    private double _lastArrowShadowStrength = DefaultArrowShadowStrength;
    private double _lastArrowBorderWidth = DefaultArrowBorderWidth;
    private bool _lastArrowHasStartHead;
    private bool _lastArrowHasEndHead = true;
    private List<(double U, double V)> _lastArrowRelativeBendPoints = new();
    private readonly List<ArrowShapePresetPreference> _arrowShapePresets = [];
    private string? _selectedArrowPresetId;
    private Color _lastHighlightColor = DefaultHighlightColor;
    private double _lastHighlightStrength = DefaultHighlightStrength;
    private HighlightMode _lastHighlightMode = HighlightMode.Region;
    private Color _lastTextColor = DefaultTextColor;
    private double _lastTextFontSize = DefaultTextFontSize;
    private double _lastTextBackgroundOpacity = DefaultTextBackgroundOpacity;
    private double _lastTextBackgroundStrength = DefaultTextBackgroundStrength;
    private System.Windows.TextAlignment _lastTextAlignment = System.Windows.TextAlignment.Left;
    private bool _lastTextIsBold = false;
    private bool _isUpdatingTextAlignment;
    private Color _lastObscureColor = DefaultObscureColor;
    private ObscureMode _lastObscureMode = ObscureMode.Blur;
    private double _lastObscureColorStrength = DefaultObscureColorStrength;
    private double _lastObscureBlurLevel = DefaultObscureBlurLevel;
    private double _lastObscurePixelationLevel = DefaultObscurePixelationLevel;
    private readonly EditorPreferencesStore _preferencesStore = new();

    public EditorWindow(BitmapSource baseImage)
    {
        InitializeComponent();
        ApplyEditorPreferences(_preferencesStore.Load());
        _workingImage = baseImage;
        Loaded += EditorWindow_Loaded;
        SourceInitialized += EditorWindow_SourceInitialized;
        Closed += EditorWindow_Closed;
        PreviewMouseDown += EditorWindow_PreviewMouseDown;
        SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
        Title = $"Edit Capture {_nextEditorNumber++}";
        ApplyWorkingImage(_workingImage);
        InitializeHistory();
        ApplyTheme();
        SelectToolButton.IsChecked = true;
        UpdateArrowPresetButtonLabel();
        UpdateHighlightModeButtonLabel();
        RefreshCanvas();
    }

    private void EditorWindow_Loaded(object sender, RoutedEventArgs e)
    {
        AttachSelectionActionIslandToFooter();

        if (_hasFittedInitialWindowSize)
        {
            return;
        }

        _hasFittedInitialWindowSize = true;
        Dispatcher.BeginInvoke(new Action(FitWindowToArtwork), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void AttachSelectionActionIslandToFooter()
    {
        if (ReferenceEquals(SelectionActionIsland.Parent, EditorFooterStack))
        {
            return;
        }

        if (SelectionActionIsland.Parent is Panel currentParent)
        {
            currentParent.Children.Remove(SelectionActionIsland);
        }

        SelectionActionIsland.Margin = new Thickness(0, 0, 0, 14);
        SelectionActionIsland.HorizontalAlignment = HorizontalAlignment.Center;
        SelectionActionIsland.VerticalAlignment = VerticalAlignment.Center;
        SelectionActionIslandTransform.X = 0;
        SelectionActionIslandTransform.Y = 0;

        var toolbarIndex = EditorFooterStack.Children.IndexOf(ToolbarIsland);
        EditorFooterStack.Children.Insert(
            toolbarIndex >= 0 ? toolbarIndex : EditorFooterStack.Children.Count,
            SelectionActionIsland);
    }

    private void EditorWindow_SourceInitialized(object? sender, EventArgs e)
    {
        ApplyNativeTitleBarTheme();
    }

    private void EditorWindow_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsActive)
        {
            BringEditorWindowToFront();
        }
    }

    private void BringEditorWindowToFront()
    {
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero)
        {
            _ = SetWindowPos(handle, HwndTop, 0, 0, 0, 0, SetWindowPosNoMove | SetWindowPosNoSize | SetWindowPosShowWindow);
            _ = SetForegroundWindow(handle);
        }

        Activate();
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
        PersistEditorPreferences();
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
        SetBrushColor("ScrollBarTrackBrush", palette.ScrollBarTrack);
        SetBrushColor("ScrollBarThumbBrush", palette.ScrollBarThumb);
        SetBrushColor("ScrollBarThumbHoverBrush", palette.ScrollBarThumbHover);
        SetBrushColor("ScrollBarThumbPressedBrush", palette.ScrollBarThumbPressed);
        SetBrushColor("EditorSliderTrackBrush", palette.SliderTrack);
        SetBrushColor("EditorSliderFillBrush", palette.SliderFill);
        SetBrushColor("EditorSliderFillHoverBrush", palette.SliderFillHover);
        SetBrushColor("EditorSliderThumbBrush", palette.SliderThumb);
        SetBrushColor("EditorSliderThumbBorderBrush", palette.SliderThumbBorder);
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
        var borderColor = DwmColorDefault;
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

    private static readonly IntPtr HwndTop = new(0);
    private const uint SetWindowPosNoSize = 0x0001;
    private const uint SetWindowPosNoMove = 0x0002;
    private const uint SetWindowPosShowWindow = 0x0040;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private void ToolButton_Checked(object sender, RoutedEventArgs e)
    {
        _contextMenuAnnotation = null;

        foreach (var button in new[] { SelectToolButton, CropToolButton, ArrowToolButton, RectangleToolButton, EllipseToolButton, ObscureToolButton, HighlightToolButton, TextToolButton })
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

            UpdateSelectionAnnotationControls(_selectedAnnotation);
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
            if (_dragOperation == DragOperation.Move
                && _selectedAnnotation is not null
                && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                var duplicate = _selectedAnnotation.Clone();
                _annotations.Add(duplicate);
                _selectedAnnotation = duplicate;
                _hoveredAnnotation = duplicate;
            }

            RefreshCanvas();
            return;
        }

        if (_currentTool == EditorTool.Select)
        {
            _selectedAnnotation = null;
            _hoveredAnnotation = null;
            _contextMenuAnnotation = null;
            UpdateSelectionAnnotationControls(null);
            BeginViewportPan(e.GetPosition(ArtworkScrollViewer));
            RefreshCanvas();
            return;
        }

        if (_currentTool == EditorTool.Text)
        {
            var text = new TextAnnotation { Location = point, Text = string.Empty };
            ApplyLastUsedTextStyle(text);
            _annotations.Add(text);
            _selectedAnnotation = text;
            _hoveredAnnotation = text;
            _dragOperation = DragOperation.None;
            BeginInlineTextEditing(text, selectAll: false);
            return;
        }

        if (_currentTool == EditorTool.Highlight && _lastHighlightMode == HighlightMode.Freehand)
        {
            var annotation = HighlightAnnotation.CreateFreehand(point);
            ApplyLastUsedHighlightStyle(annotation);
            _annotations.Add(annotation);
            _selectedAnnotation = annotation;
            _dragOperation = DragOperation.Draw;
            RefreshCanvas();
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
            annotation.SetColor(_lastShapeColor);
            _annotations.Add(annotation);
            _selectedAnnotation = annotation;
            _dragOperation = DragOperation.Draw;
            RefreshCanvas();
            return;
        }

        if (_currentTool == EditorTool.Ellipse)
        {
            var annotation = new EllipseAnnotation { Bounds = new Rect(point, point) };
            annotation.SetColor(_lastShapeColor);
            _annotations.Add(annotation);
            _selectedAnnotation = annotation;
            _dragOperation = DragOperation.Draw;
            RefreshCanvas();
            return;
        }

        if (_currentTool == EditorTool.Arrow)
        {
            var annotation = ArrowAnnotation.Create(point);
            annotation.SetColor(_lastArrowColor);
            ApplyLastUsedArrowStyle(annotation);
            _annotations.Add(annotation);
            _selectedAnnotation = annotation;
            _dragOperation = DragOperation.Draw;
            RefreshCanvas();
            return;
        }

        if (_currentTool == EditorTool.Obscure)
        {
            var annotation = new ObscureAnnotation { Bounds = new Rect(point, point) };
            ApplyLastUsedObscureStyle(annotation);
            _annotations.Add(annotation);
            _selectedAnnotation = annotation;
            _dragOperation = DragOperation.Draw;
            RefreshCanvas();
            return;
        }

        if (_currentTool == EditorTool.Highlight)
        {
            var annotation = _lastHighlightMode == HighlightMode.Line
                ? HighlightAnnotation.CreateLine(point)
                : HighlightAnnotation.CreateRegion(point);
            ApplyLastUsedHighlightStyle(annotation);
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
        var annotation = GetAnnotationsInHitTestOrder().FirstOrDefault(candidate => candidate.HitTest(point));
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

        // Don't add a bend point if the user double-clicked on an existing handle.
        if (arrow.HitHandle(point) is not null)
        {
            return false;
        }

        if (!arrow.HitTest(point))
        {
            return false;
        }

        arrow.AddBendPointAt(point);
        CaptureArrowDefaults(arrow);
        _hoveredAnnotation = arrow;
        return true;
    }

    private bool TryBeginTextEditOnDoubleClick(Point point)
    {
        if (GetAnnotationsInHitTestOrder().FirstOrDefault(candidate => candidate.HitTest(point)) is not TextAnnotation textAnnotation)
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
                ClampSelectedObscureAnnotationToCanvas(preserveSize: false);
                if (_selectedAnnotation is ArrowAnnotation deferredArrow)
                {
                    ApplyLastUsedArrowBendPoints(deferredArrow);
                }
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
                ClampSelectedObscureAnnotationToCanvas(preserveSize: false);
                if (_selectedAnnotation is ArrowAnnotation drawingArrow)
                {
                    ApplyLastUsedArrowBendPoints(drawingArrow);
                }
                break;
            case DragOperation.Move:
                _selectedAnnotation.Move(point - _dragStart.Value);
                ClampSelectedObscureAnnotationToCanvas(preserveSize: true);
                _dragStart = point;
                break;
            case DragOperation.Handle when _activeHandle is not null:
                _selectedAnnotation.MoveHandle(_activeHandle, point, ShouldConstrainAspectRatio());
                ClampSelectedObscureAnnotationToCanvas(preserveSize: false);
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

        // If right-clicking a bend handle on the selected arrow, remove that bend point.
        if (_selectedAnnotation is ArrowAnnotation selectedArrow)
        {
            var handle = selectedArrow.HitHandle(point);
            if (handle is { } h && h.StartsWith("Bend:", StringComparison.Ordinal)
                && int.TryParse(h.AsSpan(5), out var bendIndex))
            {
                selectedArrow.RemoveBendPointAt(bendIndex);
                CaptureArrowDefaults(selectedArrow);
                if (AnnotationCanvas.IsMouseCaptured)
                {
                    AnnotationCanvas.ReleaseMouseCapture();
                }
                _dragStart = null;
                _dragOperation = DragOperation.None;
                _activeHandle = null;
                CommitHistoryState();
                RefreshCanvas();
                e.Handled = true;
                return;
            }
        }

        var annotation = GetAnnotationsInHitTestOrder().FirstOrDefault(candidate => candidate.HitTest(point));
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
        UpdateSelectionAnnotationControls(annotation);
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
        var capturedOperation = _dragOperation;
        _dragOperation = DragOperation.None;
        _activeCropHandle = CropHandle.None;
        _cropDragOrigin = null;
        _activeHandle = null;
        _panStartViewportPoint = null;
        AnnotationCanvas.ReleaseMouseCapture();
        if (capturedOperation is DragOperation.Draw or DragOperation.Move or DragOperation.Handle
            && _selectedAnnotation is ArrowAnnotation finishedArrow)
        {
            CaptureArrowDefaults(finishedArrow);
        }

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
        if (sender is not Button button || button.Tag is not string colorValue || ColorConverter.ConvertFromString(colorValue) is not Color color)
        {
            return;
        }

        if (_contextMenuAnnotation is null)
        {
            if (_currentTool == EditorTool.Arrow)
            {
                _lastArrowColor = color;
                PersistEditorPreferences();
                RefreshCanvas();
            }
            else if (_currentTool == EditorTool.Highlight)
            {
                _lastHighlightColor = color;
                RefreshCanvas();
            }
            else if (_currentTool == EditorTool.Obscure)
            {
                _lastObscureColor = color;
                PersistEditorPreferences();
                RefreshCanvas();
            }

            return;
        }

        _contextMenuAnnotation.SetColor(color);
        switch (_contextMenuAnnotation)
        {
            case RectangleAnnotation:
            case EllipseAnnotation:
                _lastShapeColor = color;
                break;
            case ArrowAnnotation:
                _lastArrowColor = color;
                break;
            case HighlightAnnotation:
                _lastHighlightColor = color;
                break;
            case TextAnnotation:
                _lastTextColor = color;
                break;
            case ObscureAnnotation:
                _lastObscureColor = color;
                break;
        }

        PersistEditorPreferences();
        CommitHistoryState();
        RefreshCanvas();
    }

    private void HighlightStrengthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingHighlightStrength || _contextMenuAnnotation is not HighlightAnnotation highlightAnnotation)
        {
            return;
        }

        var strength = HighlightStrengthSlider.Value / 100d;
        highlightAnnotation.SetColorStrength(strength);
        _lastHighlightStrength = highlightAnnotation.ColorStrength;
        HighlightStrengthValueText.Text = $"{(int)Math.Round(HighlightStrengthSlider.Value)}%";
        PersistEditorPreferences();
        CommitHistoryState();
        RefreshCanvas();
    }

    private void TextFontSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TextFontSizeValueText is null)
        {
            return;
        }

        var fontSize = Math.Round(e.NewValue);
        TextFontSizeValueText.Text = fontSize.ToString("0", CultureInfo.InvariantCulture);
        if (_isUpdatingTextFontSize || _contextMenuAnnotation is not TextAnnotation textAnnotation)
        {
            return;
        }

        textAnnotation.SetFontSize(fontSize);
        _lastTextFontSize = fontSize;
        if (ReferenceEquals(_editingTextAnnotation, textAnnotation))
        {
            InlineTextEditor.FontSize = fontSize;
        }

        PersistEditorPreferences();
        CommitHistoryState();
        RefreshCanvas();
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
        _lastTextFontSize = fontSize;
        if (ReferenceEquals(_editingTextAnnotation, textAnnotation))
        {
            InlineTextEditor.FontSize = fontSize;
        }

        PersistEditorPreferences();
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
        _lastTextBackgroundOpacity = textAnnotation.BackgroundOpacity;
        TextBackgroundOpacityValueText.Text = $"{(int)Math.Round(TextBackgroundOpacitySlider.Value)}%";
        PersistEditorPreferences();
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
        _lastTextBackgroundStrength = textAnnotation.BackgroundColorStrength;
        TextBackgroundStrengthValueText.Text = $"{(int)Math.Round(TextBackgroundStrengthSlider.Value)}%";
        PersistEditorPreferences();
        CommitHistoryState();
        RefreshCanvas();
    }

    private void ObscureBlurLevelSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingObscureBlurLevel || _contextMenuAnnotation is not ObscureAnnotation obscureAnnotation)
        {
            return;
        }

        var blurLevel = ObscureBlurLevelSlider.Value / 100d;
        obscureAnnotation.SetBlurLevel(blurLevel);
        _lastObscureBlurLevel = obscureAnnotation.BlurLevel;
        ObscureBlurLevelValueText.Text = $"{(int)Math.Round(ObscureBlurLevelSlider.Value)}%";
        UpdateObscureAnnotationControls(obscureAnnotation);
        PersistEditorPreferences();
        CommitHistoryState();
        RefreshCanvas();
    }

    private void ObscurePixelationLevelSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingObscurePixelationLevel || _contextMenuAnnotation is not ObscureAnnotation obscureAnnotation)
        {
            return;
        }

        var pixelationLevel = ObscurePixelationLevelSlider.Value / 100d;
        obscureAnnotation.SetPixelationLevel(pixelationLevel);
        _lastObscurePixelationLevel = obscureAnnotation.PixelationLevel;
        ObscurePixelationLevelValueText.Text = $"{(int)Math.Round(ObscurePixelationLevelSlider.Value)}%";
        UpdateObscureAnnotationControls(obscureAnnotation);
        PersistEditorPreferences();
        CommitHistoryState();
        RefreshCanvas();
    }

    private void ObscureColorStrengthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingObscureColorStrength || _contextMenuAnnotation is not ObscureAnnotation obscureAnnotation)
        {
            return;
        }

        var colorStrength = ObscureColorStrengthSlider.Value / 100d;
        obscureAnnotation.SetColorStrength(colorStrength);
        _lastObscureColorStrength = obscureAnnotation.ColorStrength;
        ObscureColorStrengthValueText.Text = $"{(int)Math.Round(ObscureColorStrengthSlider.Value)}%";
        PersistEditorPreferences();
        CommitHistoryState();
        RefreshCanvas();
    }

    private void ObjectShadowSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ObjectShadowValueLabel is null)
        {
            return;
        }

        ObjectShadowValueLabel.Text = $"{Math.Round(e.NewValue):0}%";
        if (_isUpdatingObjectShadowStrength || _contextMenuAnnotation is not IBorderShadowAnnotation styleAnnotation || _contextMenuAnnotation is ArrowAnnotation)
        {
            return;
        }

        styleAnnotation.SetShadowStrength(Math.Clamp(e.NewValue / 100d, 0, 1));
        CommitHistoryState();
        RefreshCanvas();
    }

    private void ObjectBorderSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ObjectBorderValueLabel is null)
        {
            return;
        }

        ObjectBorderValueLabel.Text = e.NewValue.ToString("0.0", CultureInfo.InvariantCulture);
        if (_isUpdatingObjectBorderWidth || _contextMenuAnnotation is not IBorderShadowAnnotation styleAnnotation || _contextMenuAnnotation is ArrowAnnotation)
        {
            return;
        }

        styleAnnotation.SetBorderWidth(Math.Clamp(e.NewValue, 0, 12));
        CommitHistoryState();
        RefreshCanvas();
    }

    private void ResetAnnotation_Click(object sender, RoutedEventArgs e)
    {
        if (_contextMenuAnnotation is null)
        {
            return;
        }

        ResetAnnotationToDefaults(_contextMenuAnnotation);
        UpdateSelectionAnnotationControls(_contextMenuAnnotation);
        PersistEditorPreferences();
        CommitHistoryState();
        RefreshCanvas();
        UpdateSelectionActionIsland();
    }

    private void ResetAnnotationToDefaults(AnnotationBase annotation)
    {
        switch (annotation)
        {
            case RectangleAnnotation rectangle:
                rectangle.SetColor(DefaultAccentColor);
                rectangle.SetShadowStrength(DefaultShapeShadowStrength);
                rectangle.SetBorderWidth(DefaultShapeBorderWidth);
                _lastShapeColor = DefaultAccentColor;
                break;
            case EllipseAnnotation ellipse:
                ellipse.SetColor(DefaultAccentColor);
                ellipse.SetShadowStrength(DefaultShapeShadowStrength);
                ellipse.SetBorderWidth(DefaultShapeBorderWidth);
                _lastShapeColor = DefaultAccentColor;
                break;
            case TextAnnotation text:
                text.SetColor(DefaultTextColor);
                text.SetFontSize(DefaultTextFontSize);
                text.SetBackgroundOpacity(DefaultTextBackgroundOpacity);
                text.SetBackgroundColorStrength(DefaultTextBackgroundStrength);
                text.SetShadowStrength(DefaultTextShadowStrength);
                text.SetBorderWidth(DefaultTextBorderWidth);
                text.SetTextAlignment(System.Windows.TextAlignment.Left);
                text.SetBold(false);
                _lastTextColor = DefaultTextColor;
                _lastTextFontSize = DefaultTextFontSize;
                _lastTextBackgroundOpacity = DefaultTextBackgroundOpacity;
                _lastTextBackgroundStrength = DefaultTextBackgroundStrength;
                _lastTextAlignment = System.Windows.TextAlignment.Left;
                _lastTextIsBold = false;
                if (ReferenceEquals(_editingTextAnnotation, text))
                {
                    InlineTextEditor.FontSize = DefaultTextFontSize;
                    InlineTextEditor.TextAlignment = System.Windows.TextAlignment.Left;
                    InlineTextEditor.FontWeight = FontWeights.Normal;
                }
                break;
            case ObscureAnnotation obscure:
                obscure.SetColor(DefaultObscureColor);
                obscure.SetBlurLevel(DefaultObscureBlurLevel);
                obscure.SetPixelationLevel(DefaultObscurePixelationLevel);
                obscure.SetColorStrength(DefaultObscureColorStrength);
                obscure.SetShadowStrength(DefaultObscureShadowStrength);
                obscure.SetBorderWidth(DefaultObscureBorderWidth);
                _lastObscureColor = DefaultObscureColor;
                _lastObscureBlurLevel = DefaultObscureBlurLevel;
                _lastObscurePixelationLevel = DefaultObscurePixelationLevel;
                _lastObscureColorStrength = DefaultObscureColorStrength;
                break;
            case HighlightAnnotation highlight:
                highlight.SetColor(DefaultHighlightColor);
                highlight.SetColorStrength(DefaultHighlightStrength);
                highlight.SetShadowStrength(DefaultHighlightShadowStrength);
                highlight.SetBorderWidth(DefaultHighlightBorderWidth);
                _lastHighlightColor = DefaultHighlightColor;
                _lastHighlightStrength = DefaultHighlightStrength;
                break;
            case ArrowAnnotation arrow:
                arrow.SetColor(DefaultArrowColor);
                arrow.SetStyle(ArrowStyle.BrushStroke);
                arrow.SetTailScale(DefaultArrowTailScale);
                arrow.SetBodyScale(DefaultArrowBodyScale);
                arrow.SetFrontScale(DefaultArrowFrontScale);
                arrow.SetHeadScale(DefaultArrowHeadScale);
                arrow.SetTailHeadScale(DefaultArrowTailHeadScale);
                arrow.SetTailRoundness(DefaultArrowTailRoundness);
                arrow.SetHeadRoundness(DefaultArrowHeadRoundness);
                arrow.SetShadowStrength(DefaultArrowShadowStrength);
                arrow.SetBorderWidth(DefaultArrowBorderWidth);
                arrow.SetStartHeadEnabled(false);
                arrow.SetEndHeadEnabled(true);
                arrow.BendPoints.Clear();
                _lastArrowColor = DefaultArrowColor;
                _lastArrowStyle = ArrowStyle.BrushStroke;
                _lastArrowTailScale = DefaultArrowTailScale;
                _lastArrowBodyScale = DefaultArrowBodyScale;
                _lastArrowFrontScale = DefaultArrowFrontScale;
                _lastArrowHeadScale = DefaultArrowHeadScale;
                _lastArrowTailHeadScale = DefaultArrowTailHeadScale;
                _lastArrowTailRoundness = DefaultArrowTailRoundness;
                _lastArrowHeadRoundness = DefaultArrowHeadRoundness;
                _lastArrowShadowStrength = DefaultArrowShadowStrength;
                _lastArrowBorderWidth = DefaultArrowBorderWidth;
                _lastArrowHasStartHead = false;
                _lastArrowHasEndHead = true;
                ClearSelectedArrowPreset();
                UpdateArrowPresetButtonLabel();
                break;
        }
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

    private void LayerTop_Click(object sender, RoutedEventArgs e)
    {
        MoveSelectedAnnotationLayer(LayerMove.Top);
    }

    private void LayerUp_Click(object sender, RoutedEventArgs e)
    {
        MoveSelectedAnnotationLayer(LayerMove.Up);
    }

    private void LayerDown_Click(object sender, RoutedEventArgs e)
    {
        MoveSelectedAnnotationLayer(LayerMove.Down);
    }

    private void LayerBottom_Click(object sender, RoutedEventArgs e)
    {
        MoveSelectedAnnotationLayer(LayerMove.Bottom);
    }

    private void MoveSelectedAnnotationLayer(LayerMove move)
    {
        if (_selectedAnnotation is null)
        {
            return;
        }

        var currentIndex = _annotations.IndexOf(_selectedAnnotation);
        if (currentIndex < 0)
        {
            return;
        }

        var targetIndex = move switch
        {
            LayerMove.Top => _annotations.Count - 1,
            LayerMove.Up => Math.Min(_annotations.Count - 1, currentIndex + 1),
            LayerMove.Down => Math.Max(0, currentIndex - 1),
            LayerMove.Bottom => 0,
            _ => currentIndex,
        };

        if (targetIndex == currentIndex)
        {
            UpdateLayerActionIsland();
            return;
        }

        _annotations.RemoveAt(currentIndex);
        _annotations.Insert(targetIndex, _selectedAnnotation);
        _contextMenuAnnotation = _selectedAnnotation;
        _hoveredAnnotation = _selectedAnnotation;
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

        if (modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.D0)
        {
            SetZoomLevel(1.0);
            CenterArtworkInViewport();
            e.Handled = true;
            return;
        }

        if (modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.D1)
        {
            FitArtworkToView();
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

    private void ZoomActualSize_Click(object sender, RoutedEventArgs e)
    {
        SetZoomLevel(1.0);
        CenterArtworkInViewport();
    }

    private void ZoomFitToView_Click(object sender, RoutedEventArgs e)
    {
        FitArtworkToView();
    }

    private void FitArtworkToView()
    {
        var artworkWidth = ArtworkSurface.Width;
        var artworkHeight = ArtworkSurface.Height;
        if (artworkWidth <= 0 || artworkHeight <= 0)
        {
            return;
        }

        ArtworkScrollViewer.UpdateLayout();
        var viewportWidth = ArtworkScrollViewer.ViewportWidth > 0 ? ArtworkScrollViewer.ViewportWidth : ArtworkScrollViewer.ActualWidth;
        var viewportHeight = ArtworkScrollViewer.ViewportHeight > 0 ? ArtworkScrollViewer.ViewportHeight : ArtworkScrollViewer.ActualHeight;
        if (viewportWidth <= 0 || viewportHeight <= 0)
        {
            return;
        }

        var horizontalPadding = 64d;
        var verticalPadding = 176d;
        var availableWidth = Math.Max(1, viewportWidth - horizontalPadding);
        var availableHeight = Math.Max(1, viewportHeight - verticalPadding);
        var fitZoom = Math.Min(availableWidth / artworkWidth, availableHeight / artworkHeight);
        SetZoomLevel(fitZoom);
        CenterArtworkInViewport();
    }

    private void SetZoomLevel(double zoomLevel)
    {
        _zoomLevel = Math.Clamp(zoomLevel, MinZoomLevel, MaxZoomLevel);
        ApplyZoomTransform();
        UpdateLayout();
        UpdateSelectionActionIsland();
        UpdateInlineTextEditorState();
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
            return;
        }

        _previewWindow?.Close();

        var handle = new WindowInteropHelper(this).Handle;
        var screen = handle != IntPtr.Zero ? FormsScreen.FromHandle(handle) : FormsScreen.PrimaryScreen;
        var workArea = screen?.WorkingArea ?? System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;

        var preview = new SnapshotPreviewWindow(bitmap);
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

    private void UpdateSelectionAnnotationControls(AnnotationBase? annotation)
    {
        UpdateArrowAnnotationControls(annotation as ArrowAnnotation);
        UpdateObjectStyleControls(annotation);
        UpdateTextAnnotationControls(annotation as TextAnnotation);
        UpdateObscureAnnotationControls(annotation as ObscureAnnotation);
        UpdateHighlightAnnotationControls(annotation as HighlightAnnotation);
    }

    private bool _suppressArrowSliderEvents;

    private void HighlightToolButton_Click(object sender, RoutedEventArgs e)
    {
        if (HighlightToolButton.IsChecked != true)
        {
            HighlightToolButton.IsChecked = true;
        }

        HighlightModePopup.IsOpen = true;
    }

    private void HighlightModeOption_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string modeValue }
            || !Enum.TryParse<HighlightMode>(modeValue, ignoreCase: true, out var mode))
        {
            return;
        }

        SetHighlightMode(mode);
        HighlightModePopup.IsOpen = false;
    }

    private void SetHighlightMode(HighlightMode mode)
    {
        _lastHighlightMode = mode;
        UpdateHighlightModeButtonLabel();
        if (HighlightToolButton.IsChecked != true)
        {
            HighlightToolButton.IsChecked = true;
        }
    }

    private void UpdateHighlightModeButtonLabel()
    {
        HighlightAreaToolIcon.Visibility = _lastHighlightMode == HighlightMode.Region ? Visibility.Visible : Visibility.Collapsed;
        HighlightLineToolIcon.Visibility = _lastHighlightMode == HighlightMode.Line ? Visibility.Visible : Visibility.Collapsed;
        HighlightFreehandToolIcon.Visibility = _lastHighlightMode == HighlightMode.Freehand ? Visibility.Visible : Visibility.Collapsed;

        HighlightToolButton.ToolTip = _lastHighlightMode switch
        {
            HighlightMode.Line => "Line highlight",
            HighlightMode.Freehand => "Freehand highlight",
            _ => "Area highlight",
        };
    }

    private void UpdateArrowAnnotationControls(ArrowAnnotation? arrowAnnotation)
    {
        var shouldShowArrowControls = arrowAnnotation is not null;
        ArrowOptionsPanel.Visibility = shouldShowArrowControls ? Visibility.Visible : Visibility.Collapsed;
        ArrowPresetButton.Visibility = Visibility.Visible;
        if (!shouldShowArrowControls)
        {
            ArrowPresetPopup.IsOpen = false;
            UpdateArrowPresetButtonLabel();
            return;
        }

        var tailScale = arrowAnnotation?.TailScale ?? _lastArrowTailScale;
        var bodyScale = arrowAnnotation?.BodyScale ?? _lastArrowBodyScale;
        var frontScale = arrowAnnotation?.FrontScale ?? _lastArrowFrontScale;
        var headScale = arrowAnnotation?.HeadScale ?? _lastArrowHeadScale;
        var tailHeadScale = arrowAnnotation?.TailHeadScale ?? _lastArrowTailHeadScale;
        var tailRoundness = arrowAnnotation?.TailRoundness ?? _lastArrowTailRoundness;
        var headRoundness = arrowAnnotation?.HeadRoundness ?? _lastArrowHeadRoundness;
        var shadowStrength = arrowAnnotation?.ShadowStrength ?? _lastArrowShadowStrength;
        var borderWidth = arrowAnnotation?.BorderWidth ?? _lastArrowBorderWidth;
        var hasEndHead = arrowAnnotation?.HasEndHead ?? _lastArrowHasEndHead;
        var hasStartHead = arrowAnnotation?.HasStartHead ?? _lastArrowHasStartHead;

        _suppressArrowSliderEvents = true;
        try
        {
            ArrowTailSlider.Value = tailScale;
            ArrowBodySlider.Value = bodyScale;
            ArrowFrontSlider.Value = frontScale;
            ArrowHeadSlider.Value = headScale;
            ArrowTailHeadSlider.Value = tailHeadScale;
            ArrowTailRoundnessSlider.Value = tailRoundness * 100;
            ArrowHeadRoundnessSlider.Value = headRoundness * 100;
            ArrowShadowSlider.Value = shadowStrength * 100;
            ArrowBorderSlider.Value = borderWidth;
            ArrowHeadToggleButton.IsChecked = hasEndHead;
            ArrowTailToggleButton.IsChecked = hasStartHead;
        }
        finally
        {
            _suppressArrowSliderEvents = false;
        }
        ArrowTailValueLabel.Text = tailScale.ToString("0.00", CultureInfo.InvariantCulture);
        ArrowBodyValueLabel.Text = bodyScale.ToString("0.00", CultureInfo.InvariantCulture);
        ArrowFrontValueLabel.Text = frontScale.ToString("0.00", CultureInfo.InvariantCulture);
        ArrowHeadValueLabel.Text = headScale.ToString("0.00", CultureInfo.InvariantCulture);
        ArrowTailHeadValueLabel.Text = tailHeadScale.ToString("0.00", CultureInfo.InvariantCulture);
        ArrowTailRoundnessValueLabel.Text = $"{Math.Round(tailRoundness * 100):0}%";
        ArrowHeadRoundnessValueLabel.Text = $"{Math.Round(headRoundness * 100):0}%";
        ArrowShadowValueLabel.Text = $"{Math.Round(shadowStrength * 100):0}%";
        ArrowBorderValueLabel.Text = borderWidth.ToString("0.0", CultureInfo.InvariantCulture);
        UpdateArrowPresetButtonLabel();

        if (ArrowPresetPopup.IsOpen)
        {
            RebuildArrowPresetButtons();
        }
    }

    private void ArrowTailSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ArrowTailValueLabel is null) return;
        ArrowTailValueLabel.Text = e.NewValue.ToString("0.00", CultureInfo.InvariantCulture);
        if (_suppressArrowSliderEvents) return;
        _lastArrowTailScale = e.NewValue;
        ClearSelectedArrowPreset();
        UpdateArrowPresetButtonLabel();
        if (_selectedAnnotation is ArrowAnnotation arrow)
        {
            arrow.SetTailScale(e.NewValue);
            RefreshCanvas();
        }
    }

    private void ArrowBodySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ArrowBodyValueLabel is null) return;
        ArrowBodyValueLabel.Text = e.NewValue.ToString("0.00", CultureInfo.InvariantCulture);
        if (_suppressArrowSliderEvents) return;
        _lastArrowBodyScale = e.NewValue;
        ClearSelectedArrowPreset();
        UpdateArrowPresetButtonLabel();
        if (_selectedAnnotation is ArrowAnnotation arrow)
        {
            arrow.SetBodyScale(e.NewValue);
            RefreshCanvas();
        }
    }

    private void ArrowFrontSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ArrowFrontValueLabel is null) return;
        ArrowFrontValueLabel.Text = e.NewValue.ToString("0.00", CultureInfo.InvariantCulture);
        if (_suppressArrowSliderEvents) return;
        _lastArrowFrontScale = e.NewValue;
        ClearSelectedArrowPreset();
        UpdateArrowPresetButtonLabel();
        if (_selectedAnnotation is ArrowAnnotation arrow)
        {
            arrow.SetFrontScale(e.NewValue);
            RefreshCanvas();
        }
    }

    private void ArrowHeadSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ArrowHeadValueLabel is null) return;
        ArrowHeadValueLabel.Text = e.NewValue.ToString("0.00", CultureInfo.InvariantCulture);
        if (_suppressArrowSliderEvents) return;
        _lastArrowHeadScale = e.NewValue;
        ClearSelectedArrowPreset();
        UpdateArrowPresetButtonLabel();
        if (_selectedAnnotation is ArrowAnnotation arrow)
        {
            arrow.SetHeadScale(e.NewValue);
            RefreshCanvas();
        }
    }

    private void ArrowTailHeadSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ArrowTailHeadValueLabel is null) return;
        ArrowTailHeadValueLabel.Text = e.NewValue.ToString("0.00", CultureInfo.InvariantCulture);
        if (_suppressArrowSliderEvents) return;
        _lastArrowTailHeadScale = e.NewValue;
        ClearSelectedArrowPreset();
        UpdateArrowPresetButtonLabel();
        if (_selectedAnnotation is ArrowAnnotation arrow)
        {
            arrow.SetTailHeadScale(e.NewValue);
            RefreshCanvas();
        }
    }

    private void ArrowTailRoundnessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ArrowTailRoundnessValueLabel is null) return;
        ArrowTailRoundnessValueLabel.Text = $"{Math.Round(e.NewValue):0}%";
        if (_suppressArrowSliderEvents) return;
        _lastArrowTailRoundness = Math.Clamp(e.NewValue / 100d, 0, 1);
        ClearSelectedArrowPreset();
        UpdateArrowPresetButtonLabel();
        if (_selectedAnnotation is ArrowAnnotation arrow)
        {
            arrow.SetTailRoundness(_lastArrowTailRoundness);
            RefreshCanvas();
        }
    }

    private void ArrowHeadRoundnessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ArrowHeadRoundnessValueLabel is null) return;
        ArrowHeadRoundnessValueLabel.Text = $"{Math.Round(e.NewValue):0}%";
        if (_suppressArrowSliderEvents) return;
        _lastArrowHeadRoundness = Math.Clamp(e.NewValue / 100d, 0, 1);
        ClearSelectedArrowPreset();
        UpdateArrowPresetButtonLabel();
        if (_selectedAnnotation is ArrowAnnotation arrow)
        {
            arrow.SetHeadRoundness(_lastArrowHeadRoundness);
            RefreshCanvas();
        }
    }

    private void ArrowShadowSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ArrowShadowValueLabel is null) return;
        ArrowShadowValueLabel.Text = $"{Math.Round(e.NewValue):0}%";
        if (_suppressArrowSliderEvents) return;
        _lastArrowShadowStrength = Math.Clamp(e.NewValue / 100d, 0, 1);
        ClearSelectedArrowPreset();
        UpdateArrowPresetButtonLabel();
        if (_selectedAnnotation is ArrowAnnotation arrow)
        {
            arrow.SetShadowStrength(_lastArrowShadowStrength);
            RefreshCanvas();
        }
    }

    private void ArrowBorderSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ArrowBorderValueLabel is null) return;
        ArrowBorderValueLabel.Text = e.NewValue.ToString("0.0", CultureInfo.InvariantCulture);
        if (_suppressArrowSliderEvents) return;
        _lastArrowBorderWidth = Math.Clamp(e.NewValue, 0, 12);
        ClearSelectedArrowPreset();
        UpdateArrowPresetButtonLabel();
        if (_selectedAnnotation is ArrowAnnotation arrow)
        {
            arrow.SetBorderWidth(_lastArrowBorderWidth);
            RefreshCanvas();
        }
    }

    private void ArrowHeadToggleButton_Checked(object sender, RoutedEventArgs e)
    {
        if (_suppressArrowSliderEvents)
        {
            return;
        }

        _lastArrowHasEndHead = ArrowHeadToggleButton.IsChecked == true;
        ClearSelectedArrowPreset();
        UpdateArrowPresetButtonLabel();
        if (_selectedAnnotation is ArrowAnnotation arrow)
        {
            arrow.SetEndHeadEnabled(_lastArrowHasEndHead);
            RefreshCanvas();
        }
    }

    private void ArrowTailToggleButton_Checked(object sender, RoutedEventArgs e)
    {
        if (_suppressArrowSliderEvents)
        {
            return;
        }

        _lastArrowHasStartHead = ArrowTailToggleButton.IsChecked == true;
        ClearSelectedArrowPreset();
        UpdateArrowPresetButtonLabel();
        if (_selectedAnnotation is ArrowAnnotation arrow)
        {
            arrow.SetStartHeadEnabled(_lastArrowHasStartHead);
            RefreshCanvas();
        }
    }

    private void ArrowPresetButton_Click(object sender, RoutedEventArgs e)
    {
        RebuildArrowPresetButtons();
        ArrowPresetPopup.IsOpen = !ArrowPresetPopup.IsOpen;
    }

    private void TextAlignLeft_Checked(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingTextAlignment)
        {
            return;
        }

        _isUpdatingTextAlignment = true;
        TextAlignLeftButton.IsChecked = true;
        TextAlignCenterButton.IsChecked = false;
        TextAlignRightButton.IsChecked = false;
        _isUpdatingTextAlignment = false;

        _lastTextAlignment = System.Windows.TextAlignment.Left;
        if (_selectedAnnotation is TextAnnotation text)
        {
            text.SetTextAlignment(System.Windows.TextAlignment.Left);
            if (ReferenceEquals(_editingTextAnnotation, text))
            {
                InlineTextEditor.TextAlignment = System.Windows.TextAlignment.Left;
            }

            RefreshCanvas();
        }
    }

    private void TextAlignCenter_Checked(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingTextAlignment)
        {
            return;
        }

        _isUpdatingTextAlignment = true;
        TextAlignLeftButton.IsChecked = false;
        TextAlignCenterButton.IsChecked = true;
        TextAlignRightButton.IsChecked = false;
        _isUpdatingTextAlignment = false;

        _lastTextAlignment = System.Windows.TextAlignment.Center;
        if (_selectedAnnotation is TextAnnotation text)
        {
            text.SetTextAlignment(System.Windows.TextAlignment.Center);
            if (ReferenceEquals(_editingTextAnnotation, text))
            {
                InlineTextEditor.TextAlignment = System.Windows.TextAlignment.Center;
            }

            RefreshCanvas();
        }
    }

    private void TextAlignRight_Checked(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingTextAlignment)
        {
            return;
        }

        _isUpdatingTextAlignment = true;
        TextAlignLeftButton.IsChecked = false;
        TextAlignCenterButton.IsChecked = false;
        TextAlignRightButton.IsChecked = true;
        _isUpdatingTextAlignment = false;

        _lastTextAlignment = System.Windows.TextAlignment.Right;
        if (_selectedAnnotation is TextAnnotation text)
        {
            text.SetTextAlignment(System.Windows.TextAlignment.Right);
            if (ReferenceEquals(_editingTextAnnotation, text))
            {
                InlineTextEditor.TextAlignment = System.Windows.TextAlignment.Right;
            }

            RefreshCanvas();
        }
    }

    private void TextBoldButton_Checked(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingTextAlignment)
        {
            return;
        }

        _lastTextIsBold = TextBoldButton.IsChecked == true;
        if (_selectedAnnotation is TextAnnotation text)
        {
            text.SetBold(_lastTextIsBold);
            if (ReferenceEquals(_editingTextAnnotation, text))
            {
                InlineTextEditor.FontWeight = _lastTextIsBold ? FontWeights.Bold : FontWeights.Normal;
            }

            RefreshCanvas();
        }
    }



    private void SaveArrowPresetButton_Click(object sender, RoutedEventArgs e)
    {
        var preset = CaptureCurrentArrowPreset();
        var existingPresetIndex = _arrowShapePresets.FindIndex(candidate => candidate.Id == preset.Id);
        if (existingPresetIndex >= 0)
        {
            _arrowShapePresets[existingPresetIndex] = preset;
        }
        else
        {
            if (_arrowShapePresets.Count >= MaxArrowShapePresets)
            {
                _arrowShapePresets.RemoveAt(0);
            }

            _arrowShapePresets.Add(preset);
        }

        _selectedArrowPresetId = preset.Id;
        PersistEditorPreferences();
        UpdateArrowPresetButtonLabel();
        RebuildArrowPresetButtons();
    }

    private void ArrowPresetOption_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string presetId })
        {
            return;
        }

        var preset = _arrowShapePresets.FirstOrDefault(candidate => string.Equals(candidate.Id, presetId, StringComparison.Ordinal));
        if (preset is null)
        {
            return;
        }

        ApplyArrowPreset(preset);
        ArrowPresetPopup.IsOpen = false;
    }

    private void RebuildArrowPresetButtons()
    {
        ArrowPresetListPanel.Children.Clear();
        ArrowPresetEmptyStateText.Visibility = _arrowShapePresets.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        for (var index = 0; index < _arrowShapePresets.Count; index++)
        {
            var preset = _arrowShapePresets[index];
            var isSelected = string.Equals(_selectedArrowPresetId, preset.Id, StringComparison.Ordinal);
            ArrowPresetListPanel.Children.Add(CreateArrowPresetButton(preset, isSelected));
        }
    }

    private FrameworkElement CreateArrowPresetButton(ArrowShapePresetPreference preset, bool isSelected)
    {
        var previewArrow = CreateArrowPresetPreview(preset);
        previewArrow.SetColor(_lastArrowColor);

        var canvas = new Canvas
        {
            Width = 108,
            Height = 66,
            Background = Brushes.Transparent,
            IsHitTestVisible = false,
        };
        previewArrow.Render(canvas, isSelected: false, isHovered: false);

        var previewHost = new Border
        {
            Width = 112,
            Height = 70,
            Background = Brushes.Transparent,
            Padding = new Thickness(2),
            Child = new Viewbox
            {
                Stretch = Stretch.Uniform,
                Child = canvas,
            },
        };

        var button = new Button
        {
            Width = 112,
            Height = 70,
            Padding = new Thickness(0),
            Tag = preset.Id,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            ToolTip = "Apply preset",
            Content = previewHost,
        };

        button.Click += ArrowPresetOption_Click;

        var deleteButton = new Button
        {
            Width = 22,
            Height = 22,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 2, 0),
            Tag = preset.Id,
            ToolTip = "Delete preset",
            Content = new TextBlock
            {
                FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize = 10,
                Text = "\uE74D",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        if (TryFindResource("EditorDeleteIconButtonStyle") is Style deleteButtonStyle)
        {
            deleteButton.Style = deleteButtonStyle;
        }

        if (deleteButton.Content is TextBlock deleteIcon)
        {
            deleteIcon.SetResourceReference(TextBlock.ForegroundProperty, "DeleteButtonForegroundBrush");
        }

        deleteButton.Click += DeleteArrowPresetButton_Click;

        return new Grid
        {
            Width = 112,
            Height = 78,
            Margin = new Thickness(0, 0, 8, 8),
            Children =
            {
                button,
                deleteButton,
            },
        };
    }

    private void DeleteArrowPresetButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string presetId })
        {
            return;
        }

        var removed = _arrowShapePresets.RemoveAll(candidate => string.Equals(candidate.Id, presetId, StringComparison.Ordinal)) > 0;
        if (!removed)
        {
            return;
        }

        if (string.Equals(_selectedArrowPresetId, presetId, StringComparison.Ordinal))
        {
            ClearSelectedArrowPreset();
        }

        PersistEditorPreferences();
        UpdateArrowPresetButtonLabel();
        RebuildArrowPresetButtons();
        e.Handled = true;
    }

    private void UpdateArrowPresetButtonLabel()
    {
        if (ArrowPresetButtonLabel is null)
        {
            return;
        }

        var selectedPresetIndex = _arrowShapePresets.FindIndex(candidate => string.Equals(candidate.Id, _selectedArrowPresetId, StringComparison.Ordinal));
        ArrowPresetButtonLabel.Text = selectedPresetIndex >= 0
            ? $"Preset {selectedPresetIndex + 1}"
            : "Presets";
    }

    private void ClearSelectedArrowPreset()
    {
        _selectedArrowPresetId = null;
        _lastArrowRelativeBendPoints.Clear();
    }

    private ArrowShapePresetPreference CaptureCurrentArrowPreset()
    {
        if (_selectedAnnotation is ArrowAnnotation arrow)
        {
            var presetId = _selectedArrowPresetId ?? Guid.NewGuid().ToString("N");
            return new ArrowShapePresetPreference(
                Id: presetId,
                TailScale: arrow.TailScale,
                BodyScale: arrow.BodyScale,
                FrontScale: arrow.FrontScale,
                HeadScale: arrow.HeadScale,
                ShadowStrength: arrow.ShadowStrength,
                BorderWidth: arrow.BorderWidth,
                HasStartHead: arrow.HasStartHead,
                HasEndHead: arrow.HasEndHead,
                TailHeadScale: arrow.TailHeadScale,
                TailRoundness: arrow.TailRoundness,
                HeadRoundness: arrow.HeadRoundness,
                BendPoints: CreateRelativeArrowPresetPoints(arrow));
        }

        return new ArrowShapePresetPreference(
            Id: _selectedArrowPresetId ?? Guid.NewGuid().ToString("N"),
            TailScale: _lastArrowTailScale,
            BodyScale: _lastArrowBodyScale,
            FrontScale: _lastArrowFrontScale,
            HeadScale: _lastArrowHeadScale,
            ShadowStrength: _lastArrowShadowStrength,
            BorderWidth: _lastArrowBorderWidth,
            HasStartHead: _lastArrowHasStartHead,
            HasEndHead: _lastArrowHasEndHead,
            TailHeadScale: _lastArrowTailHeadScale,
                TailRoundness: _lastArrowTailRoundness,
                HeadRoundness: _lastArrowHeadRoundness,
            BendPoints: _lastArrowRelativeBendPoints.Select(point => new ArrowPresetPointPreference(point.U, point.V)).ToList());
    }

    private void ApplyArrowPreset(ArrowShapePresetPreference preset)
    {
        _selectedArrowPresetId = preset.Id;
        _lastArrowTailScale = Math.Clamp(preset.TailScale, 0.15, 3.0);
        _lastArrowBodyScale = Math.Clamp(preset.BodyScale, 0.15, 3.0);
        _lastArrowFrontScale = Math.Clamp(preset.FrontScale, 0.15, 3.0);
        _lastArrowHeadScale = Math.Clamp(preset.HeadScale, 0.2, 1.6);
        _lastArrowTailHeadScale = Math.Clamp(preset.TailHeadScale ?? 1.0, 0.2, 1.6);
        _lastArrowTailRoundness = Math.Clamp(preset.TailRoundness ?? DefaultArrowTailRoundness, 0, 1);
        _lastArrowHeadRoundness = Math.Clamp(preset.HeadRoundness ?? DefaultArrowHeadRoundness, 0, 1);
        _lastArrowShadowStrength = Math.Clamp(preset.ShadowStrength, 0, 1);
        _lastArrowBorderWidth = Math.Clamp(preset.BorderWidth, 0, 12);
        _lastArrowHasStartHead = preset.HasStartHead == true;
        _lastArrowHasEndHead = preset.HasEndHead ?? true;
        _lastArrowRelativeBendPoints = (preset.BendPoints ?? [])
            .Select(point => (point.U, point.V))
            .ToList();

        if (ArrowToolButton.IsChecked != true)
        {
            ArrowToolButton.IsChecked = true;
        }

        if (_selectedAnnotation is ArrowAnnotation selectedArrow)
        {
            ApplyLastUsedArrowStyle(selectedArrow);
            UpdateSelectionAnnotationControls(selectedArrow);
        }
        else
        {
            UpdateSelectionAnnotationControls(null);
        }

        PersistEditorPreferences();
        RefreshCanvas();
    }

    private static ArrowAnnotation CreateArrowPresetPreview(ArrowShapePresetPreference preset)
    {
        var arrow = ArrowAnnotation.Create(new Point(4, 33));
        arrow.End = new Point(104, 33);
        arrow.SetTailScale(preset.TailScale);
        arrow.SetBodyScale(preset.BodyScale);
        arrow.SetFrontScale(preset.FrontScale);
        arrow.SetHeadScale(preset.HeadScale);
        arrow.SetTailHeadScale(preset.TailHeadScale ?? 1.0);
        arrow.SetTailRoundness(preset.TailRoundness ?? DefaultArrowTailRoundness);
        arrow.SetHeadRoundness(preset.HeadRoundness ?? DefaultArrowHeadRoundness);
        arrow.SetShadowStrength(preset.ShadowStrength);
        arrow.SetBorderWidth(preset.BorderWidth);
        arrow.SetStartHeadEnabled(preset.HasStartHead == true);
        arrow.SetEndHeadEnabled(preset.HasEndHead ?? true);

        var bendPoints = preset.BendPoints ?? [];
        var length = arrow.End.X - arrow.Start.X;
        foreach (var bendPoint in bendPoints)
        {
            arrow.BendPoints.Add(new Point(
                arrow.Start.X + (bendPoint.U * length),
                arrow.Start.Y + (bendPoint.V * length)));
        }

        return arrow;
    }

    private static List<ArrowPresetPointPreference> CreateRelativeArrowPresetPoints(ArrowAnnotation arrow)
    {
        var relativePoints = new List<ArrowPresetPointPreference>();
        var delta = arrow.End - arrow.Start;
        var length = delta.Length;
        if (length < 0.001 || arrow.BendPoints.Count == 0)
        {
            return relativePoints;
        }

        var dir = new Vector(delta.X / length, delta.Y / length);
        var normal = new Vector(-dir.Y, dir.X);
        foreach (var bend in arrow.BendPoints)
        {
            var rel = bend - arrow.Start;
            var u = ((rel.X * dir.X) + (rel.Y * dir.Y)) / length;
            var v = ((rel.X * normal.X) + (rel.Y * normal.Y)) / length;
            relativePoints.Add(new ArrowPresetPointPreference(u, v));
        }

        return relativePoints;
    }

    private Brush CloneBrushResource(string resourceKey, Brush fallback)
    {
        if (TryFindResource(resourceKey) is Brush brush)
        {
            return brush.CloneCurrentValue();
        }

        return fallback.CloneCurrentValue();
    }

    private void UpdateTextAnnotationControls(TextAnnotation? textAnnotation)
    {
        var visibility = textAnnotation is null ? Visibility.Collapsed : Visibility.Visible;
        TextSizePanel.Visibility = visibility;
        TextBackgroundOpacityPanel.Visibility = visibility;
        TextBackgroundStrengthPanel.Visibility = visibility;
        TextAlignmentPanel.Visibility = visibility;

        if (textAnnotation is null)
        {
            return;
        }

        _isUpdatingTextFontSize = true;
        TextFontSizeSlider.Value = textAnnotation.FontSize;
        TextFontSizeValueText.Text = textAnnotation.FontSize.ToString("0", CultureInfo.InvariantCulture);
        _isUpdatingTextFontSize = false;

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

        _isUpdatingTextAlignment = true;
        TextAlignLeftButton.IsChecked = textAnnotation.TextAlignment == System.Windows.TextAlignment.Left;
        TextAlignCenterButton.IsChecked = textAnnotation.TextAlignment == System.Windows.TextAlignment.Center;
        TextAlignRightButton.IsChecked = textAnnotation.TextAlignment == System.Windows.TextAlignment.Right;
        TextBoldButton.IsChecked = textAnnotation.IsBold;
        _isUpdatingTextAlignment = false;
    }

    private void UpdateObjectStyleControls(AnnotationBase? annotation)
    {
        var styleAnnotation = annotation is ArrowAnnotation ? null : annotation as IBorderShadowAnnotation;
        ObjectStylePanel.Visibility = styleAnnotation is null ? Visibility.Collapsed : Visibility.Visible;
        if (styleAnnotation is null)
        {
            return;
        }

        _isUpdatingObjectShadowStrength = true;
        var shadowPercent = Math.Round(styleAnnotation.ShadowStrength * 100);
        ObjectShadowSlider.Value = shadowPercent;
        ObjectShadowValueLabel.Text = $"{(int)shadowPercent}%";
        _isUpdatingObjectShadowStrength = false;

        _isUpdatingObjectBorderWidth = true;
        ObjectBorderSlider.Value = styleAnnotation.BorderWidth;
        ObjectBorderValueLabel.Text = styleAnnotation.BorderWidth.ToString("0.0", CultureInfo.InvariantCulture);
        _isUpdatingObjectBorderWidth = false;
    }

    private void UpdateHighlightAnnotationControls(HighlightAnnotation? highlightAnnotation)
    {
        var visibility = highlightAnnotation is null ? Visibility.Collapsed : Visibility.Visible;
        HighlightStrengthPanel.Visibility = visibility;

        if (highlightAnnotation is null)
        {
            return;
        }

        _isUpdatingHighlightStrength = true;
        var strengthPercent = Math.Round(highlightAnnotation.ColorStrength * 100);
        HighlightStrengthSlider.Value = strengthPercent;
        HighlightStrengthValueText.Text = $"{(int)strengthPercent}%";
        _isUpdatingHighlightStrength = false;
    }

    private void UpdateObscureAnnotationControls(ObscureAnnotation? obscureAnnotation)
    {
        var visibility = obscureAnnotation is null ? Visibility.Collapsed : Visibility.Visible;
        ObscureBlurLevelPanel.Visibility = visibility;
        ObscurePixelationLevelPanel.Visibility = visibility;
        ObscureColorStrengthPanel.Visibility = visibility;

        if (obscureAnnotation is null)
        {
            return;
        }

        _isUpdatingObscureBlurLevel = true;
        var blurPercent = Math.Round(obscureAnnotation.BlurLevel * 100);
        ObscureBlurLevelSlider.Value = blurPercent;
        ObscureBlurLevelValueText.Text = $"{(int)blurPercent}%";
        _isUpdatingObscureBlurLevel = false;

        _isUpdatingObscurePixelationLevel = true;
        var pixelationPercent = Math.Round(obscureAnnotation.PixelationLevel * 100);
        ObscurePixelationLevelSlider.Value = pixelationPercent;
        ObscurePixelationLevelValueText.Text = $"{(int)pixelationPercent}%";
        _isUpdatingObscurePixelationLevel = false;

        _isUpdatingObscureColorStrength = true;
        var colorStrengthPercent = Math.Round(obscureAnnotation.ColorStrength * 100);
        ObscureColorStrengthSlider.Value = colorStrengthPercent;
        ObscureColorStrengthValueText.Text = $"{(int)colorStrengthPercent}%";
        _isUpdatingObscureColorStrength = false;
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
        _selectedAnnotation = GetAnnotationsInHitTestOrder().FirstOrDefault(annotation => annotation.HitTest(point));
        _hoveredAnnotation = _selectedAnnotation;
        _contextMenuAnnotation = _selectedAnnotation;
        UpdateSelectionAnnotationControls(_selectedAnnotation);

        CommitInlineTextEditing();
    }

    private void ActivateSelectTool()
    {
        SelectToolButton.IsChecked = true;
    }

    private bool IsDeferredDrawTool(EditorTool tool)
    {
        return tool is EditorTool.Rectangle or EditorTool.Ellipse or EditorTool.Arrow or EditorTool.Obscure
            || (tool == EditorTool.Highlight && _lastHighlightMode != HighlightMode.Freehand);
    }

    private void BeginDeferredDraw(EditorTool tool, Point point)
    {
        switch (tool)
        {
            case EditorTool.Rectangle:
            {
                var annotation = new RectangleAnnotation { Bounds = new Rect(point, point) };
                annotation.SetColor(_lastShapeColor);
                _annotations.Add(annotation);
                _selectedAnnotation = annotation;
                _dragOperation = DragOperation.Draw;
                break;
            }
            case EditorTool.Ellipse:
            {
                var annotation = new EllipseAnnotation { Bounds = new Rect(point, point) };
                annotation.SetColor(_lastShapeColor);
                _annotations.Add(annotation);
                _selectedAnnotation = annotation;
                _dragOperation = DragOperation.Draw;
                break;
            }
            case EditorTool.Arrow:
            {
                var annotation = ArrowAnnotation.Create(point);
                annotation.SetColor(_lastArrowColor);
                ApplyLastUsedArrowStyle(annotation);
                _annotations.Add(annotation);
                _selectedAnnotation = annotation;
                _dragOperation = DragOperation.Draw;
                break;
            }
            case EditorTool.Obscure:
            {
                var annotation = new ObscureAnnotation { Bounds = new Rect(point, point) };
                ApplyLastUsedObscureStyle(annotation);
                _annotations.Add(annotation);
                _selectedAnnotation = annotation;
                _dragOperation = DragOperation.Draw;
                break;
            }
            case EditorTool.Highlight:
            {
                var annotation = _lastHighlightMode == HighlightMode.Line
                    ? HighlightAnnotation.CreateLine(point)
                    : HighlightAnnotation.CreateRegion(point);
                ApplyLastUsedHighlightStyle(annotation);
                _annotations.Add(annotation);
                _selectedAnnotation = annotation;
                _dragOperation = DragOperation.Draw;
                break;
            }
        }
    }

    private void ApplyLastUsedTextStyle(TextAnnotation textAnnotation)
    {
        textAnnotation.SetColor(_lastTextColor);
        textAnnotation.SetFontSize(_lastTextFontSize);
        textAnnotation.SetBackgroundOpacity(_lastTextBackgroundOpacity);
        textAnnotation.SetBackgroundColorStrength(_lastTextBackgroundStrength);
        textAnnotation.SetTextAlignment(_lastTextAlignment);
        textAnnotation.SetBold(_lastTextIsBold);
    }

    private void ApplyLastUsedArrowStyle(ArrowAnnotation arrowAnnotation)
    {
        arrowAnnotation.SetStyle(ArrowStyle.BrushStroke);
        arrowAnnotation.SetTailScale(_lastArrowTailScale);
        arrowAnnotation.SetBodyScale(_lastArrowBodyScale);
        arrowAnnotation.SetFrontScale(_lastArrowFrontScale);
        arrowAnnotation.SetHeadScale(_lastArrowHeadScale);
        arrowAnnotation.SetTailHeadScale(_lastArrowTailHeadScale);
        arrowAnnotation.SetTailRoundness(_lastArrowTailRoundness);
        arrowAnnotation.SetHeadRoundness(_lastArrowHeadRoundness);
        arrowAnnotation.SetShadowStrength(_lastArrowShadowStrength);
        arrowAnnotation.SetBorderWidth(_lastArrowBorderWidth);
        arrowAnnotation.SetStartHeadEnabled(_lastArrowHasStartHead);
        arrowAnnotation.SetEndHeadEnabled(_lastArrowHasEndHead);
        ApplyLastUsedArrowBendPoints(arrowAnnotation);
    }

    private void ApplyLastUsedHighlightStyle(HighlightAnnotation highlightAnnotation)
    {
        highlightAnnotation.SetColor(_lastHighlightColor);
        highlightAnnotation.SetColorStrength(_lastHighlightStrength);
    }

    private void ApplyLastUsedArrowBendPoints(ArrowAnnotation arrowAnnotation)
    {
        arrowAnnotation.BendPoints.Clear();
        if (_selectedArrowPresetId is null || _lastArrowRelativeBendPoints.Count == 0)
        {
            return;
        }

        var delta = arrowAnnotation.End - arrowAnnotation.Start;
        var length = delta.Length;
        if (length < 0.001)
        {
            return;
        }

        var dir = new Vector(delta.X / length, delta.Y / length);
        var normal = new Vector(-dir.Y, dir.X);
        foreach (var (u, v) in _lastArrowRelativeBendPoints)
        {
            var p = arrowAnnotation.Start + (dir * (u * length)) + (normal * (v * length));
            arrowAnnotation.BendPoints.Add(p);
        }
    }

    private void CaptureArrowDefaults(ArrowAnnotation arrow, bool preserveSelectedPreset = false)
    {
        _lastArrowColor = arrow.StrokeColor;
        _lastArrowStyle = arrow.Style;
        _lastArrowTailScale = arrow.TailScale;
        _lastArrowBodyScale = arrow.BodyScale;
        _lastArrowFrontScale = arrow.FrontScale;
        _lastArrowHeadScale = arrow.HeadScale;
        _lastArrowTailHeadScale = arrow.TailHeadScale;
        _lastArrowTailRoundness = arrow.TailRoundness;
        _lastArrowHeadRoundness = arrow.HeadRoundness;
        _lastArrowShadowStrength = arrow.ShadowStrength;
        _lastArrowBorderWidth = arrow.BorderWidth;
        _lastArrowHasStartHead = arrow.HasStartHead;
        _lastArrowHasEndHead = arrow.HasEndHead;

        if (!preserveSelectedPreset)
        {
            ClearSelectedArrowPreset();
            UpdateArrowPresetButtonLabel();
        }

    }

    private void ApplyLastUsedObscureStyle(ObscureAnnotation obscureAnnotation)
    {
        obscureAnnotation.SetColor(_lastObscureColor);
        obscureAnnotation.SetBlurLevel(_lastObscureBlurLevel);
        obscureAnnotation.SetPixelationLevel(_lastObscurePixelationLevel);
        obscureAnnotation.SetColorStrength(_lastObscureColorStrength);
    }

    private void UpdateHoveredAnnotation(Point point)
    {
        var hovered = GetAnnotationsInHitTestOrder().FirstOrDefault(annotation => annotation.HitTest(point));
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
        foreach (var annotation in GetAnnotationsInRenderOrder())
        {
            if (ReferenceEquals(annotation, _editingTextAnnotation))
            {
                continue;
            }

            if (annotation is HighlightAnnotation highlightAnnotation)
            {
                highlightAnnotation.Render(AnnotationCanvas, annotation == _selectedAnnotation, annotation == _hoveredAnnotation, _workingImage);
            }
            else
            {
                annotation.Render(AnnotationCanvas, annotation == _selectedAnnotation, annotation == _hoveredAnnotation);
            }
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

    private IEnumerable<AnnotationBase> GetAnnotationsInRenderOrder()
    {
        return _annotations;
    }

    private IEnumerable<AnnotationBase> GetAnnotationsInHitTestOrder()
    {
        return _annotations.AsEnumerable().Reverse();
    }

    private bool ShouldConstrainAspectRatio()
    {
        return Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)
            && (_selectedAnnotation is RectangleAnnotation or EllipseAnnotation or ObscureAnnotation
                or HighlightAnnotation { Mode: HighlightMode.Region });
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
            LayerActionsIsland.Visibility = Visibility.Collapsed;
            DeleteActionPanel.Opacity = 1;
            DeleteActionPanel.IsHitTestVisible = true;
            UpdateSelectionAnnotationControls(null);
            return;
        }

        _contextMenuAnnotation = _selectedAnnotation;
        DeleteActionPanel.Visibility = Visibility.Visible;
        DeleteActionPanel.Opacity = 1;
        DeleteActionPanel.IsHitTestVisible = true;
        UpdateSelectionAnnotationControls(_selectedAnnotation);
        UpdateLayerActionIsland();

        if (ReferenceEquals(SelectionActionIsland.Parent, EditorFooterStack))
        {
            SelectionActionIslandTransform.X = 0;
            SelectionActionIslandTransform.Y = 0;
            SelectionActionIsland.Visibility = Visibility.Visible;
            return;
        }

        SelectionActionIsland.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        ToolbarIsland.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var islandWidth = SelectionActionIsland.DesiredSize.Width;
        var islandHeight = SelectionActionIsland.DesiredSize.Height;
        var toolbarWidth = ToolbarIsland.DesiredSize.Width;
        var toolbarHeight = ToolbarIsland.DesiredSize.Height;
        var viewportWidth = EditorRootGrid.ActualWidth;
        var viewportHeight = EditorRootGrid.ActualHeight;
        var toolbarOrigin = ToolbarIsland.TranslatePoint(new Point(0, 0), EditorRootGrid);

        if (viewportWidth <= 0 || viewportHeight <= 0 || toolbarWidth <= 0 || toolbarHeight <= 0)
        {
            SelectionActionIsland.Visibility = Visibility.Collapsed;
            return;
        }

        var targetX = toolbarOrigin.X + ((toolbarWidth - islandWidth) / 2);

        var targetY = toolbarOrigin.Y - islandHeight - SelectionActionIslandSpacing;
        var minX = SelectionActionIslandViewportPadding;
        var maxX = viewportWidth - islandWidth - SelectionActionIslandViewportPadding;
        var minY = SelectionActionIslandViewportPadding;
        targetX = Math.Clamp(targetX, minX, Math.Max(minX, maxX));
        targetY = Math.Max(minY, targetY);

        SelectionActionIslandTransform.X = targetX;
        SelectionActionIslandTransform.Y = targetY;
        SelectionActionIsland.Visibility = Visibility.Visible;
    }

    private void UpdateLayerActionIsland()
    {
        if (_currentTool == EditorTool.Crop || _editingTextAnnotation is not null || _selectedAnnotation is null)
        {
            LayerActionsIsland.Visibility = Visibility.Collapsed;
            return;
        }

        var selectedIndex = _annotations.IndexOf(_selectedAnnotation);
        var hasSelectionInStack = selectedIndex >= 0;
        var canMoveDown = hasSelectionInStack && selectedIndex > 0;
        var canMoveUp = hasSelectionInStack && selectedIndex < _annotations.Count - 1;

        LayerActionsIsland.Visibility = Visibility.Visible;
        LayerTopButton.IsEnabled = canMoveUp;
        LayerUpButton.IsEnabled = canMoveUp;
        LayerDownButton.IsEnabled = canMoveDown;
        LayerBottomButton.IsEnabled = canMoveDown;
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
        var minX = Math.Min(arrow.Start.X, arrow.End.X);
        var minY = Math.Min(arrow.Start.Y, arrow.End.Y);
        var maxX = Math.Max(arrow.Start.X, arrow.End.X);
        var maxY = Math.Max(arrow.Start.Y, arrow.End.Y);
        foreach (var bend in arrow.BendPoints)
        {
            if (bend.X < minX) minX = bend.X;
            if (bend.Y < minY) minY = bend.Y;
            if (bend.X > maxX) maxX = bend.X;
            if (bend.Y > maxY) maxY = bend.Y;
        }
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

        if (!InlineTextEditor.Width.Equals(_editingTextAnnotation.BoxWidth))
        {
            InlineTextEditor.Width = _editingTextAnnotation.BoxWidth;
        }

        if (InlineTextEditor.TextAlignment != _editingTextAnnotation.TextAlignment)
        {
            InlineTextEditor.TextAlignment = _editingTextAnnotation.TextAlignment;
        }

        var expectedFontWeight = _editingTextAnnotation.IsBold ? FontWeights.Bold : FontWeights.Normal;
        if (InlineTextEditor.FontWeight != expectedFontWeight)
        {
            InlineTextEditor.FontWeight = expectedFontWeight;
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
        var fileSizeLabel = GetDisplayedImageFileSizeLabel();
        ImageDimensionsText.Text = $"{roundedWidth} x {roundedHeight} | {fileSizeLabel}";
        ToolTipService.SetToolTip(ImageDimensionsText, $"Current artwork dimensions: {roundedWidth} x {roundedHeight} pixels. Estimated PNG size: {fileSizeLabel}.");
    }

    private string GetDisplayedImageFileSizeLabel()
    {
        if (BaseImage.Source is not BitmapSource sourceBitmap)
        {
            return "0 B";
        }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(sourceBitmap));

        using var stream = new MemoryStream();
        encoder.Save(stream);
        return FormatFileSize(stream.Length);
    }

    private static string FormatFileSize(long byteCount)
    {
        string[] suffixes = ["B", "KB", "MB", "GB"];
        double size = byteCount;
        var suffixIndex = 0;

        while (size >= 1024 && suffixIndex < suffixes.Length - 1)
        {
            size /= 1024;
            suffixIndex++;
        }

        var decimals = suffixIndex == 0 ? 0 : 1;
        return $"{size.ToString($"F{decimals}", CultureInfo.InvariantCulture)} {suffixes[suffixIndex]}";
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

    internal BitmapSource? CreatePixelatedObscureSource(Rect documentRect, double pixelationLevel)
    {
        if (BaseImage.Source is not BitmapSource sourceBitmap)
        {
            return null;
        }

        var viewportTopLeft = ToViewportPoint(documentRect.TopLeft);
        var left = (int)Math.Clamp(Math.Round(viewportTopLeft.X), 0, Math.Max(0, sourceBitmap.PixelWidth - 1));
        var top = (int)Math.Clamp(Math.Round(viewportTopLeft.Y), 0, Math.Max(0, sourceBitmap.PixelHeight - 1));
        var width = (int)Math.Clamp(Math.Round(documentRect.Width), 1, sourceBitmap.PixelWidth - left);
        var height = (int)Math.Clamp(Math.Round(documentRect.Height), 1, sourceBitmap.PixelHeight - top);
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        var cropped = new CroppedBitmap(sourceBitmap, new Int32Rect(left, top, width, height));
        var blockFactor = Math.Max(1, (int)Math.Round(1 + (pixelationLevel * 22)));
        var reducedWidth = Math.Max(1, cropped.PixelWidth / blockFactor);
        var reducedHeight = Math.Max(1, cropped.PixelHeight / blockFactor);
        var scaleX = reducedWidth / (double)cropped.PixelWidth;
        var scaleY = reducedHeight / (double)cropped.PixelHeight;
        var reduced = new TransformedBitmap(cropped, new ScaleTransform(scaleX, scaleY));
        reduced.Freeze();
        return reduced;
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

    private void ClampSelectedObscureAnnotationToCanvas(bool preserveSize)
    {
        if (_selectedAnnotation is ObscureAnnotation obscureAnnotation)
        {
            ClampObscureAnnotationToCanvas(obscureAnnotation, preserveSize);
        }
    }

    private void ClampObscureAnnotationToCanvas(ObscureAnnotation obscureAnnotation, bool preserveSize)
    {
        var canvasBounds = new Rect(0, 0, _workingImage.PixelWidth, _workingImage.PixelHeight);
        var normalized = new Rect(obscureAnnotation.Bounds.TopLeft, obscureAnnotation.Bounds.BottomRight);

        if (preserveSize)
        {
            var width = Math.Min(normalized.Width, canvasBounds.Width);
            var height = Math.Min(normalized.Height, canvasBounds.Height);
            var left = Math.Clamp(normalized.Left, canvasBounds.Left, canvasBounds.Right - width);
            var top = Math.Clamp(normalized.Top, canvasBounds.Top, canvasBounds.Bottom - height);
            obscureAnnotation.Bounds = new Rect(left, top, width, height);
            return;
        }

        var clipped = Rect.Intersect(normalized, canvasBounds);
        if (!clipped.IsEmpty)
        {
            obscureAnnotation.Bounds = clipped;
        }
    }

    private enum EditorTool
    {
        Select,
        Crop,
        Arrow,
        Rectangle,
        Ellipse,
        Obscure,
        Highlight,
        Text,
    }

    private enum LayerMove
    {
        Top,
        Up,
        Down,
        Bottom,
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
            RectangleAnnotation rectangle => $"rect:{RectToSignature(rectangle.Bounds)}:{ColorToSignature(rectangle.StrokeColor)}:{FormatDouble(rectangle.ShadowStrength)}:{FormatDouble(rectangle.BorderWidth)}",
            EllipseAnnotation ellipse => $"ellipse:{RectToSignature(ellipse.Bounds)}:{ColorToSignature(ellipse.StrokeColor)}:{FormatDouble(ellipse.ShadowStrength)}:{FormatDouble(ellipse.BorderWidth)}",
            TextAnnotation text => $"text:{PointToSignature(text.Location)}:{FormatDouble(text.BoxWidth)}:{FormatDouble(text.FontSize)}:{FormatDouble(text.BackgroundOpacity)}:{FormatDouble(text.BackgroundColorStrength)}:{FormatDouble(text.ShadowStrength)}:{FormatDouble(text.BorderWidth)}:{ColorToSignature(text.TextColor)}:{Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(text.Text ?? string.Empty))}:{text.TextAlignment}:{text.IsBold}",
            ObscureAnnotation obscure => $"obscure:{RectToSignature(obscure.Bounds)}:{FormatDouble(obscure.BlurLevel)}:{FormatDouble(obscure.PixelationLevel)}:{FormatDouble(obscure.ColorStrength)}:{FormatDouble(obscure.ShadowStrength)}:{FormatDouble(obscure.BorderWidth)}:{ColorToSignature(obscure.OverlayColor)}",
            ArrowAnnotation arrow => $"arrow:{PointToSignature(arrow.Start)}:{PointToSignature(arrow.End)}:{string.Join(",", arrow.BendPoints.Select(PointToSignature))}:{FormatDouble(arrow.ShaftThickness)}:{FormatDouble(arrow.HeadLength)}:{FormatDouble(arrow.HeadWidth)}:{FormatDouble(arrow.OuterEdgeWidth)}:{FormatDouble(arrow.InnerEdgeWidth)}:{FormatDouble(arrow.TailSweep)}:{FormatDouble(arrow.HeadSkew)}:{FormatDouble(arrow.TailScale)}:{FormatDouble(arrow.BodyScale)}:{FormatDouble(arrow.FrontScale)}:{FormatDouble(arrow.HeadScale)}:{FormatDouble(arrow.TailHeadScale)}:{FormatDouble(arrow.TailRoundness)}:{FormatDouble(arrow.HeadRoundness)}:{FormatDouble(arrow.ShadowStrength)}:{FormatDouble(arrow.BorderWidth)}:{arrow.HasStartHead}:{arrow.HasEndHead}:{ColorToSignature(arrow.StrokeColor)}:{arrow.Style}",
            HighlightAnnotation highlight => $"highlight:{highlight.Mode}:{RectToSignature(highlight.Bounds)}:{string.Join(",", highlight.Points.Select(PointToSignature))}:{ColorToSignature(highlight.HighlightColor)}:{FormatDouble(highlight.ColorStrength)}:{FormatDouble(highlight.ShadowStrength)}:{FormatDouble(highlight.BorderWidth)}",
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

    private void ApplyEditorPreferences(EditorPreferences preferences)
    {
        if (Enum.TryParse<ThemePreference>(preferences.ThemePreference, ignoreCase: true, out var themePreference))
        {
            _themePreference = themePreference;
        }

        _lastShapeColor = ParseColorOrDefault(preferences.ShapeColor, DefaultAccentColor);
        _lastArrowColor = ParseColorOrDefault(preferences.ArrowColor, DefaultArrowColor);
        if (_lastArrowColor == LegacyDefaultArrowColor)
        {
            _lastArrowColor = DefaultArrowColor;
        }
        _lastArrowStyle = ArrowStyle.BrushStroke;
        _arrowShapePresets.Clear();
        if (preferences.ArrowShapePresets is { Count: > 0 })
        {
            foreach (var preset in preferences.ArrowShapePresets)
            {
                _arrowShapePresets.Add(NormalizeArrowPreset(preset));
            }
        }

        _selectedArrowPresetId = _arrowShapePresets.Any(candidate => string.Equals(candidate.Id, preferences.SelectedArrowPresetId, StringComparison.Ordinal))
            ? preferences.SelectedArrowPresetId
            : null;
        _lastArrowTailScale = Math.Clamp(preferences.ArrowTailScale ?? DefaultArrowTailScale, 0.15, 3.0);
        _lastArrowBodyScale = Math.Clamp(preferences.ArrowBodyScale ?? DefaultArrowBodyScale, 0.15, 3.0);
        _lastArrowFrontScale = Math.Clamp(preferences.ArrowFrontScale ?? DefaultArrowFrontScale, 0.15, 3.0);
        _lastArrowHeadScale = Math.Clamp(preferences.ArrowHeadScale ?? DefaultArrowHeadScale, 0.2, 1.6);
        _lastArrowTailRoundness = Math.Clamp(preferences.ArrowTailRoundness ?? DefaultArrowTailRoundness, 0, 1);
        _lastArrowHeadRoundness = Math.Clamp(preferences.ArrowHeadRoundness ?? DefaultArrowHeadRoundness, 0, 1);
        _lastArrowShadowStrength = Math.Clamp(preferences.ArrowShadowStrength ?? DefaultArrowShadowStrength, 0, 1);
        _lastArrowBorderWidth = Math.Clamp(preferences.ArrowBorderWidth ?? DefaultArrowBorderWidth, 0, 12);
        _lastArrowHasStartHead = preferences.ArrowHasStartHead == true;
        _lastArrowHasEndHead = preferences.ArrowHasEndHead ?? true;
        _lastArrowTailHeadScale = Math.Clamp(preferences.ArrowTailHeadScale ?? DefaultArrowTailHeadScale, 0.2, 1.6);
        if (_selectedArrowPresetId is { } selectedPresetId)
        {
            var selectedPreset = _arrowShapePresets.First(candidate => string.Equals(candidate.Id, selectedPresetId, StringComparison.Ordinal));
            _lastArrowTailScale = selectedPreset.TailScale;
            _lastArrowBodyScale = selectedPreset.BodyScale;
            _lastArrowFrontScale = selectedPreset.FrontScale;
            _lastArrowHeadScale = selectedPreset.HeadScale;
            _lastArrowTailHeadScale = Math.Clamp(selectedPreset.TailHeadScale ?? 1.0, 0.2, 1.6);
            _lastArrowTailRoundness = Math.Clamp(selectedPreset.TailRoundness ?? DefaultArrowTailRoundness, 0, 1);
            _lastArrowHeadRoundness = Math.Clamp(selectedPreset.HeadRoundness ?? DefaultArrowHeadRoundness, 0, 1);
            _lastArrowShadowStrength = selectedPreset.ShadowStrength;
            _lastArrowBorderWidth = selectedPreset.BorderWidth;
            _lastArrowHasStartHead = selectedPreset.HasStartHead == true;
            _lastArrowHasEndHead = selectedPreset.HasEndHead ?? true;
            _lastArrowRelativeBendPoints = (selectedPreset.BendPoints ?? [])
                .Select(point => (point.U, point.V))
                .ToList();
        }

        _lastTextColor = ParseColorOrDefault(preferences.TextColor, DefaultTextColor);
        _lastTextFontSize = preferences.TextFontSize > 0 ? preferences.TextFontSize : DefaultTextFontSize;
        _lastTextBackgroundOpacity = Math.Clamp(preferences.TextBackgroundOpacity, 0, 1);
        _lastTextBackgroundStrength = Math.Clamp(preferences.TextBackgroundStrength, 0, 1);
        _lastHighlightStrength = Math.Clamp(preferences.HighlightStrength ?? DefaultHighlightStrength, 0, 1);
        var hasObscureColorStrengthPreference = preferences.ObscureColorStrength.HasValue;
        var savedObscureColor = ParseColorOrDefault(preferences.ObscureColor, DefaultObscureColor);
        _lastObscureColor = !hasObscureColorStrengthPreference && savedObscureColor == Color.FromRgb(17, 24, 39)
            ? DefaultObscureColor
            : savedObscureColor;
        if (Enum.TryParse<ObscureMode>(preferences.ObscureMode, ignoreCase: true, out var obscureMode))
        {
            _lastObscureMode = obscureMode;
        }

        _lastObscureColorStrength = preferences.ObscureColorStrength is { } savedObscureColorStrength
            && Math.Abs(savedObscureColorStrength - 0.35) >= 0.001
            ? Math.Clamp(savedObscureColorStrength, 0, 1)
            : DefaultObscureColorStrength;
        _lastObscureBlurLevel = !hasObscureColorStrengthPreference && Math.Abs(preferences.ObscureBlurLevel - 0.72) < 0.001
            ? DefaultObscureBlurLevel
            : Math.Clamp(preferences.ObscureBlurLevel, 0, 1);
        var savedLightenLevel = Math.Clamp(preferences.ObscurePixelationLevel, 0, 1);
        var looksLikeLegacyPixelateDefault = string.Equals(preferences.ObscureMode, "Blur", StringComparison.OrdinalIgnoreCase)
            && Math.Abs(savedLightenLevel - 0.60) < 0.001;
        var looksLikeOldPixelateDefault = !hasObscureColorStrengthPreference && Math.Abs(savedLightenLevel) < 0.001;
        _lastObscurePixelationLevel = looksLikeLegacyPixelateDefault || looksLikeOldPixelateDefault
            ? DefaultObscurePixelationLevel
            : savedLightenLevel;
    }

    private void PersistEditorPreferences()
    {
        _preferencesStore.Save(new EditorPreferences(
            ThemePreference: _themePreference.ToString(),
            ShapeColor: ToPreferenceColor(_lastShapeColor),
            ArrowColor: ToPreferenceColor(_lastArrowColor),
            ArrowStyle: _lastArrowStyle.ToString(),
            ArrowShapePresets: _arrowShapePresets.Select(NormalizeArrowPreset).ToList(),
            SelectedArrowPresetId: _selectedArrowPresetId,
            ArrowTailScale: _lastArrowTailScale,
            ArrowBodyScale: _lastArrowBodyScale,
            ArrowFrontScale: _lastArrowFrontScale,
            ArrowHeadScale: _lastArrowHeadScale,
            ArrowTailRoundness: _lastArrowTailRoundness,
            ArrowHeadRoundness: _lastArrowHeadRoundness,
            ArrowShadowStrength: _lastArrowShadowStrength,
            ArrowBorderWidth: _lastArrowBorderWidth,
            ArrowHasStartHead: _lastArrowHasStartHead,
            ArrowHasEndHead: _lastArrowHasEndHead,
            ArrowTailHeadScale: _lastArrowTailHeadScale,
            TextColor: ToPreferenceColor(_lastTextColor),
            TextFontSize: _lastTextFontSize,
            TextBackgroundOpacity: _lastTextBackgroundOpacity,
            TextBackgroundStrength: _lastTextBackgroundStrength,
            HighlightStrength: _lastHighlightStrength,
            ObscureColor: ToPreferenceColor(_lastObscureColor),
                ObscureMode: _lastObscureMode.ToString(),
                ObscureColorStrength: _lastObscureColorStrength,
                ObscureBlurLevel: _lastObscureBlurLevel,
                ObscurePixelationLevel: _lastObscurePixelationLevel));
    }

    private static Color ParseColorOrDefault(string? colorValue, Color fallback)
    {
        return !string.IsNullOrWhiteSpace(colorValue) && ColorConverter.ConvertFromString(colorValue) is Color color
            ? color
            : fallback;
    }

    private static string ToPreferenceColor(Color color)
    {
        return $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    private static ArrowShapePresetPreference NormalizeArrowPreset(ArrowShapePresetPreference preset)
    {
        var presetId = string.IsNullOrWhiteSpace(preset.Id) ? Guid.NewGuid().ToString("N") : preset.Id;
        return new ArrowShapePresetPreference(
            Id: presetId,
            TailScale: Math.Clamp(preset.TailScale, 0.15, 3.0),
            BodyScale: Math.Clamp(preset.BodyScale, 0.15, 3.0),
            FrontScale: Math.Clamp(preset.FrontScale, 0.15, 3.0),
            HeadScale: Math.Clamp(preset.HeadScale, 0.2, 1.6),
            ShadowStrength: Math.Clamp(preset.ShadowStrength, 0, 1),
            BorderWidth: Math.Clamp(preset.BorderWidth, 0, 12),
            HasStartHead: preset.HasStartHead == true,
            HasEndHead: preset.HasEndHead ?? true,
            TailHeadScale: Math.Clamp(preset.TailHeadScale ?? 1.0, 0.2, 1.6),
            TailRoundness: Math.Clamp(preset.TailRoundness ?? DefaultArrowTailRoundness, 0, 1),
            HeadRoundness: Math.Clamp(preset.HeadRoundness ?? DefaultArrowHeadRoundness, 0, 1),
            BendPoints: (preset.BendPoints ?? [])
                .Select(point => new ArrowPresetPointPreference(point.U, point.V))
                .ToList());
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
        Color ScrollBarTrack,
        Color ScrollBarThumb,
        Color ScrollBarThumbHover,
        Color ScrollBarThumbPressed,
        Color SliderTrack,
        Color SliderFill,
        Color SliderFillHover,
        Color SliderThumb,
        Color SliderThumbBorder,
        Color GlowStart,
        Color GlowMiddle,
        Color GlowEnd)
    {
        internal static readonly ThemePalette Dark = new(
            ShellBackground: Color.FromRgb(0x1B, 0x20, 0x28),
            EditorWorkspace: Color.FromRgb(0x20, 0x26, 0x30),
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
            ScrollBarTrack: Color.FromArgb(0x2B, 0x25, 0x2C, 0x36),
            ScrollBarThumb: Color.FromArgb(0x76, 0x86, 0x93, 0xA2),
            ScrollBarThumbHover: Color.FromArgb(0x9A, 0xA1, 0xAD, 0xBA),
            ScrollBarThumbPressed: Color.FromArgb(0xC0, 0xB7, 0xC3, 0xCF),
            SliderTrack: Color.FromRgb(0x30, 0x33, 0x3A),
            SliderFill: Color.FromRgb(0x1A, 0xA9, 0xD8),
            SliderFillHover: Color.FromRgb(0x34, 0xBC, 0xE6),
            SliderThumb: Colors.White,
            SliderThumbBorder: Color.FromArgb(0x26, 0x00, 0x00, 0x00),
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
            DeleteButtonBackground: Color.FromRgb(0xFE, 0xEA, 0xEA),
            DeleteButtonBorder: Color.FromRgb(0xF2, 0xB8, 0xB5),
            DeleteButtonForeground: Color.FromRgb(0xB4, 0x23, 0x2E),
            IslandDivider: Color.FromArgb(0xCC, 0xD5, 0xE0, 0xEA),
            ScrollBarTrack: Color.FromArgb(0x18, 0x95, 0xA7, 0xB9),
            ScrollBarThumb: Color.FromArgb(0x82, 0x7C, 0x8A, 0x99),
            ScrollBarThumbHover: Color.FromArgb(0xA8, 0x62, 0x72, 0x84),
            ScrollBarThumbPressed: Color.FromArgb(0xD0, 0x4B, 0x5C, 0x70),
            SliderTrack: Color.FromRgb(0xD8, 0xE1, 0xEA),
            SliderFill: Color.FromRgb(0x16, 0xA7, 0xD6),
            SliderFillHover: Color.FromRgb(0x0C, 0x8E, 0xC5),
            SliderThumb: Colors.White,
            SliderThumbBorder: Color.FromArgb(0x3A, 0x6F, 0x7D, 0x8B),
            GlowStart: Color.FromArgb(0x26, 0x69, 0xC2, 0xFF),
            GlowMiddle: Color.FromArgb(0x18, 0x8F, 0xD4, 0xBE),
            GlowEnd: Color.FromArgb(0x00, 0x00, 0x00, 0x00));
    }

    private sealed record EditorDocumentState(BitmapSource Image, List<AnnotationBase> Annotations, Rect? AppliedCropRect, Rect? LastCropSelection);
}

internal enum ObscureMode
{
    Blur,
    Pixelate,
}

internal enum ArrowStyle
{
    Classic,
    Handdrawn,
    BrushStroke,
}

internal enum HighlightMode
{
    Region,
    Line,
    Freehand,
}

internal interface IBorderShadowAnnotation
{
    double ShadowStrength { get; }

    double BorderWidth { get; }

    void SetShadowStrength(double value);

    void SetBorderWidth(double value);
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

    protected static DropShadowEffect CreateStrongShadowEffect(double strength)
    {
        strength = Math.Clamp(strength, 0, 1);
        return new DropShadowEffect
        {
            BlurRadius = 12 + (strength * 48),
            ShadowDepth = 3 + (strength * 16),
            Direction = 315,
            Color = Colors.Black,
            Opacity = 0.42 + (strength * 0.55),
            RenderingBias = RenderingBias.Quality,
        };
    }

    protected static void ApplyStrongShadow(FrameworkElement element, double strength)
    {
        if (strength <= 0.001)
        {
            element.Effect = null;
            return;
        }

        element.Effect = CreateStrongShadowEffect(strength);
    }

    protected static void AddGeometryShadow(Canvas canvas, Geometry geometry, double strength)
    {
        if (strength <= 0.001)
        {
            return;
        }

        canvas.Children.Add(new ShapePath
        {
            Data = geometry,
            Fill = MakeBrush(WithAlpha(Colors.Black, 44)),
            IsHitTestVisible = false,
            Effect = CreateStrongShadowEffect(strength),
        });
    }

    protected static Color GetAutoBorderColor(Color color)
    {
        var brightness = ((0.2126 * color.R) + (0.7152 * color.G) + (0.0722 * color.B)) / 255d;
        return brightness < 0.42
            ? WithAlpha(Colors.White, 214)
            : WithAlpha(Colors.Black, 185);
    }
}

internal sealed class RectangleAnnotation : AnnotationBase, IBorderShadowAnnotation
{
    public Color StrokeColor { get; private set; } = EditorWindow.DefaultAccentColor;

    public double ShadowStrength { get; private set; } = EditorWindow.DefaultShapeShadowStrength;

    public double BorderWidth { get; private set; } = EditorWindow.DefaultShapeBorderWidth;

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
            Stroke = BorderWidth > 0.001 ? strokeBrush : Brushes.Transparent,
            Fill = fillBrush,
            StrokeThickness = BorderWidth,
        };
        ApplyStrongShadow(rectangle, ShadowStrength);
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

    public void SetShadowStrength(double value)
    {
        ShadowStrength = Math.Clamp(value, 0, 1);
    }

    public void SetBorderWidth(double value)
    {
        BorderWidth = Math.Clamp(value, 0, 12);
    }

    public override AnnotationBase Clone()
    {
        var clone = new RectangleAnnotation
        {
            Bounds = Bounds,
        };
        clone.SetColor(StrokeColor);
        clone.SetShadowStrength(ShadowStrength);
        clone.SetBorderWidth(BorderWidth);
        return clone;
    }

    private static Rect Normalize(Rect rect)
    {
        return new Rect(rect.TopLeft, rect.BottomRight);
    }
}

internal sealed class EllipseAnnotation : AnnotationBase, IBorderShadowAnnotation
{
    public Color StrokeColor { get; private set; } = EditorWindow.DefaultAccentColor;

    public double ShadowStrength { get; private set; } = EditorWindow.DefaultShapeShadowStrength;

    public double BorderWidth { get; private set; } = EditorWindow.DefaultShapeBorderWidth;

    public Rect Bounds { get; set; }

    public override void Render(Canvas canvas, bool isSelected, bool isHovered)
    {
        var normalized = Normalize(Bounds);
        var strokeBrush = MakeBrush(isHovered ? Darken(StrokeColor, 0.24) : StrokeColor);
        var ellipse = new Ellipse
        {
            Width = normalized.Width,
            Height = normalized.Height,
            Stroke = BorderWidth > 0.001 ? strokeBrush : Brushes.Transparent,
            Fill = MakeBrush(WithAlpha(StrokeColor, (byte)(isHovered ? 26 : 10))),
            StrokeThickness = BorderWidth,
        };
        ApplyStrongShadow(ellipse, ShadowStrength);
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

    public void SetShadowStrength(double value)
    {
        ShadowStrength = Math.Clamp(value, 0, 1);
    }

    public void SetBorderWidth(double value)
    {
        BorderWidth = Math.Clamp(value, 0, 12);
    }

    public override AnnotationBase Clone()
    {
        var clone = new EllipseAnnotation
        {
            Bounds = Bounds,
        };
        clone.SetColor(StrokeColor);
        clone.SetShadowStrength(ShadowStrength);
        clone.SetBorderWidth(BorderWidth);
        return clone;
    }

    private static Rect Normalize(Rect rect)
    {
        return new Rect(rect.TopLeft, rect.BottomRight);
    }
}

internal sealed class TextAnnotation : AnnotationBase, IBorderShadowAnnotation
{
    private const double DefaultBoxWidth = 340;
    private const double MinBoxWidth = 64;
    private const double HorizontalChromeWidth = 26;

    public Color TextColor { get; private set; } = EditorWindow.DefaultTextColor;

    public double FontSize { get; private set; } = EditorWindow.DefaultTextFontSize;

    public double BackgroundOpacity { get; private set; } = EditorWindow.DefaultTextBackgroundOpacity;

    public double BackgroundColorStrength { get; private set; } = EditorWindow.DefaultTextBackgroundStrength;

    public double ShadowStrength { get; private set; } = EditorWindow.DefaultTextShadowStrength;

    public double BorderWidth { get; private set; } = EditorWindow.DefaultTextBorderWidth;

    public double BoxWidth { get; private set; } = DefaultBoxWidth;

    public Point Location { get; set; }

    public string Text { get; set; } = "Text";

    public System.Windows.TextAlignment TextAlignment { get; private set; } = System.Windows.TextAlignment.Left;

    public bool IsBold { get; private set; } = false;

    public void SetTextAlignment(System.Windows.TextAlignment alignment)
    {
        TextAlignment = alignment;
    }

    public void SetBold(bool isBold)
    {
        IsBold = isBold;
    }

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
            AddHandle(canvas, new Point(Location.X + bounds.Width, Location.Y + bounds.Height));
        }
    }

    public override bool HitTest(Point point)
    {
        return new Rect(Location, MeasureBounds()).Contains(point);
    }

    public override string? HitHandle(Point point)
    {
        var bounds = MeasureBounds();
        var resizePoint = new Point(Location.X + bounds.Width, Location.Y + bounds.Height);
        if (Distance(resizePoint, point) <= 10)
        {
            return "Resize";
        }

        return Distance(Location, point) <= 10 ? "Location" : null;
    }

    public override void Move(Vector delta)
    {
        Location += delta;
    }

    public override void MoveHandle(string handle, Point point, bool constrainToSquare)
    {
        if (handle == "Resize")
        {
            SetBoxWidth(point.X - Location.X - HorizontalChromeWidth);
            return;
        }

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

    public void SetBoxWidth(double width)
    {
        BoxWidth = Math.Max(MinBoxWidth, width);
    }

    public void SetBackgroundOpacity(double opacity)
    {
        BackgroundOpacity = Math.Clamp(opacity, 0, 1);
    }

    public void SetBackgroundColorStrength(double strength)
    {
        BackgroundColorStrength = Math.Clamp(strength, 0, 1);
    }

    public void SetShadowStrength(double value)
    {
        ShadowStrength = Math.Clamp(value, 0, 1);
    }

    public void SetBorderWidth(double value)
    {
        BorderWidth = Math.Clamp(value, 0, 12);
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
        clone.SetBoxWidth(BoxWidth);
        clone.SetFontSize(FontSize);
        clone.SetBackgroundOpacity(BackgroundOpacity);
        clone.SetBackgroundColorStrength(BackgroundColorStrength);
        clone.SetShadowStrength(ShadowStrength);
        clone.SetBorderWidth(BorderWidth);
        clone.SetTextAlignment(TextAlignment);
        clone.SetBold(IsBold);
        return clone;
    }

    private FrameworkElement CreateTextSurface(EditorWindow? editorWindow, bool isHovered)
    {
        var borderColor = isHovered ? Darken(TextColor, 0.18) : TextColor;
        var contentBorder = CreateContentBorder(isHovered, borderColor);
        ApplyStrongShadow(contentBorder, ShadowStrength);
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
            BorderThickness = new Thickness(BorderWidth),
            BorderBrush = BorderWidth > 0.001 ? MakeBrush(frameColor) : Brushes.Transparent,
            Child = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(Text) ? " " : Text,
                FontSize = FontSize,
                FontWeight = IsBold ? FontWeights.Bold : FontWeights.Normal,
                TextAlignment = TextAlignment,
                Foreground = MakeBrush(foregroundColor),
                Effect = new DropShadowEffect
                {
                    BlurRadius = 2,
                    ShadowDepth = 0,
                    Color = textEffectColor,
                    Opacity = 1,
                },
                TextWrapping = TextWrapping.Wrap,
                Width = BoxWidth,
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

internal sealed class ObscureAnnotation : AnnotationBase, IBorderShadowAnnotation
{
    private const double CornerRadius = 3;
    private const double BaseBlurRadius = 2;
    private const double MaxAdditionalBlurRadius = 26;

    public Color OverlayColor { get; private set; } = EditorWindow.DefaultObscureColor;

    public double BlurLevel { get; private set; } = EditorWindow.DefaultObscureBlurLevel;

    public double PixelationLevel { get; private set; } = EditorWindow.DefaultObscurePixelationLevel;

    public double ColorStrength { get; private set; } = EditorWindow.DefaultObscureColorStrength;

    public double ShadowStrength { get; private set; } = EditorWindow.DefaultObscureShadowStrength;

    public double BorderWidth { get; private set; } = EditorWindow.DefaultObscureBorderWidth;

    public Rect Bounds { get; set; }

    public override void Render(Canvas canvas, bool isSelected, bool isHovered)
    {
        var normalized = Normalize(Bounds);
        if (normalized.Width <= 0 || normalized.Height <= 0)
        {
            return;
        }

        var editorWindow = Window.GetWindow(canvas) as EditorWindow;
        var surface = CreateSurface(editorWindow, normalized, isSelected, isHovered);
        Canvas.SetLeft(surface, normalized.Left);
        Canvas.SetTop(surface, normalized.Top);
        canvas.Children.Add(surface);

        if (isSelected)
        {
            AddMarchingAntsRectangle(canvas, normalized, CornerRadius, CornerRadius);
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
        OverlayColor = color;
    }

    public void SetBlurLevel(double blurLevel)
    {
        BlurLevel = Math.Clamp(blurLevel, 0, 1);
    }

    public void SetPixelationLevel(double pixelationLevel)
    {
        PixelationLevel = Math.Clamp(pixelationLevel, 0, 1);
    }

    public void SetColorStrength(double colorStrength)
    {
        ColorStrength = Math.Clamp(colorStrength, 0, 1);
    }

    public void SetShadowStrength(double value)
    {
        ShadowStrength = Math.Clamp(value, 0, 1);
    }

    public void SetBorderWidth(double value)
    {
        BorderWidth = Math.Clamp(value, 0, 12);
    }

    public override AnnotationBase Clone()
    {
        var clone = new ObscureAnnotation
        {
            Bounds = Bounds,
        };
        clone.SetColor(OverlayColor);
        clone.SetBlurLevel(BlurLevel);
        clone.SetPixelationLevel(PixelationLevel);
        clone.SetColorStrength(ColorStrength);
        clone.SetShadowStrength(ShadowStrength);
        clone.SetBorderWidth(BorderWidth);
        return clone;
    }

    private Grid CreateSurface(EditorWindow? editorWindow, Rect normalized, bool isSelected, bool isHovered)
    {
        var grid = new Grid
        {
            Width = normalized.Width,
            Height = normalized.Height,
            Clip = new RectangleGeometry(new Rect(0, 0, normalized.Width, normalized.Height), CornerRadius, CornerRadius),
            IsHitTestVisible = false,
        };
        ApplyStrongShadow(grid, ShadowStrength);

        if (editorWindow?.CreatePixelatedObscureSource(normalized, PixelationLevel) is BitmapSource obscuredSource)
        {
            var obscuredImage = new Image
            {
                Width = normalized.Width,
                Height = normalized.Height,
                Stretch = Stretch.Fill,
                Source = obscuredSource,
                IsHitTestVisible = false,
            };
            RenderOptions.SetBitmapScalingMode(
                obscuredImage,
                PixelationLevel > 0.01 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);

            if (BlurLevel > 0)
            {
                obscuredImage.Effect = new BlurEffect
                {
                    Radius = BaseBlurRadius + (BlurLevel * MaxAdditionalBlurRadius),
                    RenderingBias = RenderingBias.Quality,
                };
            }

            grid.Children.Add(obscuredImage);
        }
        else
        {
            var fallbackOverlay = new Rectangle
            {
                Width = normalized.Width,
                Height = normalized.Height,
                RadiusX = CornerRadius,
                RadiusY = CornerRadius,
                Fill = MakeBrush(GetTintColor(isHovered: false)),
                IsHitTestVisible = false,
            };
            grid.Children.Add(fallbackOverlay);
        }

        var tint = new Rectangle
        {
            Width = normalized.Width,
            Height = normalized.Height,
            RadiusX = CornerRadius,
            RadiusY = CornerRadius,
            Fill = MakeBrush(GetTintColor(isHovered)),
            IsHitTestVisible = false,
        };
        grid.Children.Add(tint);

        if (BorderWidth > 0.001)
        {
            grid.Children.Add(new Rectangle
            {
                Width = normalized.Width,
                Height = normalized.Height,
                RadiusX = CornerRadius,
                RadiusY = CornerRadius,
                Fill = Brushes.Transparent,
                Stroke = MakeBrush(GetAutoBorderColor(OverlayColor)),
                StrokeThickness = BorderWidth,
                IsHitTestVisible = false,
            });
        }

        return grid;
    }

    private Color GetTintColor(bool isHovered)
    {
        var alpha = (byte)Math.Clamp(Math.Round((ColorStrength * 150) + (isHovered ? 10 : 0)), 0, 255);
        return WithAlpha(OverlayColor, alpha);
    }

    private static Rect Normalize(Rect rect)
    {
        return new Rect(rect.TopLeft, rect.BottomRight);
    }
}

internal sealed class HighlightAnnotation : AnnotationBase, IBorderShadowAnnotation
{
    private const double RegionCornerRadius = 10;
    private const double LineThickness = 24;
    private const double FreehandThickness = 20;

    public HighlightMode Mode { get; private set; } = HighlightMode.Region;

    public Rect Bounds { get; set; }

    public List<Point> Points { get; } = [];

    public Color HighlightColor { get; private set; } = EditorWindow.DefaultHighlightColor;

    public double ColorStrength { get; private set; } = EditorWindow.DefaultHighlightStrength;

    public double ShadowStrength { get; private set; } = EditorWindow.DefaultHighlightShadowStrength;

    public double BorderWidth { get; private set; } = EditorWindow.DefaultHighlightBorderWidth;

    public static HighlightAnnotation CreateRegion(Point point)
    {
        return new HighlightAnnotation
        {
            Mode = HighlightMode.Region,
            Bounds = new Rect(point, point),
        };
    }

    public static HighlightAnnotation CreateLine(Point point)
    {
        var annotation = new HighlightAnnotation
        {
            Mode = HighlightMode.Line,
        };
        annotation.Points.Add(point);
        annotation.Points.Add(point);
        return annotation;
    }

    public static HighlightAnnotation CreateFreehand(Point point)
    {
        var annotation = new HighlightAnnotation
        {
            Mode = HighlightMode.Freehand,
        };
        annotation.Points.Add(point);
        return annotation;
    }

    public override void Render(Canvas canvas, bool isSelected, bool isHovered)
    {
        RenderFlat(canvas, isSelected, isHovered);
    }

    public void Render(Canvas canvas, bool isSelected, bool isHovered, BitmapSource background)
    {
        var geometry = CreateFillGeometry();
        if (geometry is null)
        {
            return;
        }

        var blendedHighlight = CreateBlendedHighlightImage(background, geometry, isHovered);
        if (blendedHighlight is null)
        {
            RenderFlat(canvas, isSelected, isHovered);
            return;
        }

        AddGeometryShadow(canvas, geometry, ShadowStrength);
        canvas.Children.Add(blendedHighlight);
        AddHighlightBorder(canvas, geometry);
        RenderSelectionAdorners(canvas, isSelected, geometry);
    }

    private void RenderFlat(Canvas canvas, bool isSelected, bool isHovered)
    {
        switch (Mode)
        {
            case HighlightMode.Region:
            {
                var rect = Normalize(Bounds);
                if (rect.Width < 0.5 || rect.Height < 0.5)
                {
                    return;
                }

                var fill = new Rectangle
                {
                    Width = rect.Width,
                    Height = rect.Height,
                    RadiusX = RegionCornerRadius,
                    RadiusY = RegionCornerRadius,
                    Fill = MakeBrush(GetFillColor(isHovered)),
                    IsHitTestVisible = false,
                };
                ApplyStrongShadow(fill, ShadowStrength);
                Canvas.SetLeft(fill, rect.Left);
                Canvas.SetTop(fill, rect.Top);
                canvas.Children.Add(fill);

                if (BorderWidth > 0.001)
                {
                    var border = new Rectangle
                    {
                        Width = rect.Width,
                        Height = rect.Height,
                        RadiusX = RegionCornerRadius,
                        RadiusY = RegionCornerRadius,
                        Stroke = MakeBrush(GetAutoBorderColor(HighlightColor)),
                        StrokeThickness = BorderWidth,
                        Fill = Brushes.Transparent,
                        IsHitTestVisible = false,
                    };
                    Canvas.SetLeft(border, rect.Left);
                    Canvas.SetTop(border, rect.Top);
                    canvas.Children.Add(border);
                }

                if (isSelected)
                {
                    AddMarchingAntsRectangle(canvas, rect, RegionCornerRadius, RegionCornerRadius);
                    AddHandle(canvas, rect.TopLeft);
                    AddHandle(canvas, rect.BottomRight);
                }

                break;
            }
            case HighlightMode.Line:
            case HighlightMode.Freehand:
            {
                var geometry = CreateStrokeGeometry();
                if (geometry is null)
                {
                    return;
                }

                AddGeometryShadow(canvas, geometry, ShadowStrength);
                canvas.Children.Add(new ShapePath
                {
                    Data = geometry,
                    Fill = MakeBrush(GetFillColor(isHovered)),
                    IsHitTestVisible = false,
                });
                AddHighlightBorder(canvas, geometry);

                if (isSelected)
                {
                    AddMarchingAntsPath(canvas, geometry);
                    if (Mode == HighlightMode.Line && Points.Count >= 2)
                    {
                        AddHandle(canvas, Points[0]);
                        AddHandle(canvas, Points[^1]);
                    }
                }

                break;
            }
        }
    }

    private Geometry? CreateFillGeometry()
    {
        return Mode switch
        {
            HighlightMode.Region => CreateRegionGeometry(),
            HighlightMode.Line or HighlightMode.Freehand => CreateStrokeGeometry(),
            _ => null,
        };
    }

    private Geometry? CreateRegionGeometry()
    {
        var rect = Normalize(Bounds);
        if (rect.Width < 0.5 || rect.Height < 0.5)
        {
            return null;
        }

        return new RectangleGeometry(rect, RegionCornerRadius, RegionCornerRadius);
    }

    private void AddHighlightBorder(Canvas canvas, Geometry geometry)
    {
        if (BorderWidth <= 0.001)
        {
            return;
        }

        canvas.Children.Add(new ShapePath
        {
            Data = geometry,
            Stroke = MakeBrush(GetAutoBorderColor(HighlightColor)),
            StrokeThickness = BorderWidth,
            StrokeLineJoin = PenLineJoin.Round,
            Fill = Brushes.Transparent,
            IsHitTestVisible = false,
        });
    }

    private void RenderSelectionAdorners(Canvas canvas, bool isSelected, Geometry geometry)
    {
        if (!isSelected)
        {
            return;
        }

        if (Mode == HighlightMode.Region)
        {
            var rect = Normalize(Bounds);
            AddMarchingAntsRectangle(canvas, rect, RegionCornerRadius, RegionCornerRadius);
            AddHandle(canvas, rect.TopLeft);
            AddHandle(canvas, rect.BottomRight);
            return;
        }

        AddMarchingAntsPath(canvas, geometry);
        if (Mode == HighlightMode.Line && Points.Count >= 2)
        {
            AddHandle(canvas, Points[0]);
            AddHandle(canvas, Points[^1]);
        }
    }

    private Image? CreateBlendedHighlightImage(BitmapSource background, Geometry geometry, bool isHovered)
    {
        var imageBounds = new Rect(0, 0, background.PixelWidth, background.PixelHeight);
        var highlightBounds = Rect.Intersect(geometry.Bounds, imageBounds);
        if (highlightBounds.IsEmpty || highlightBounds.Width < 1 || highlightBounds.Height < 1)
        {
            return null;
        }

        var left = Math.Clamp((int)Math.Floor(highlightBounds.Left), 0, Math.Max(0, background.PixelWidth - 1));
        var top = Math.Clamp((int)Math.Floor(highlightBounds.Top), 0, Math.Max(0, background.PixelHeight - 1));
        var right = Math.Clamp((int)Math.Ceiling(highlightBounds.Right), left + 1, background.PixelWidth);
        var bottom = Math.Clamp((int)Math.Ceiling(highlightBounds.Bottom), top + 1, background.PixelHeight);
        var width = right - left;
        var height = bottom - top;
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        var sourcePixels = CopyBackgroundPixels(background, new Int32Rect(left, top, width, height));
        var maskPixels = CreateGeometryMaskPixels(geometry, left, top, width, height);
        var blendedPixels = new byte[sourcePixels.Length];
        var blendStrength = GetBlendStrength(isHovered);

        for (var i = 0; i < sourcePixels.Length; i += 4)
        {
            var maskAlpha = maskPixels[i + 3];
            if (maskAlpha == 0)
            {
                continue;
            }

            var baseBlue = sourcePixels[i];
            var baseGreen = sourcePixels[i + 1];
            var baseRed = sourcePixels[i + 2];
            var targetRed = BlendHighlightChannel(baseRed, HighlightColor.R, blendStrength);
            var targetGreen = BlendHighlightChannel(baseGreen, HighlightColor.G, blendStrength);
            var targetBlue = BlendHighlightChannel(baseBlue, HighlightColor.B, blendStrength);

            blendedPixels[i] = targetBlue;
            blendedPixels[i + 1] = targetGreen;
            blendedPixels[i + 2] = targetRed;
            blendedPixels[i + 3] = maskAlpha;
        }

        var stride = width * 4;
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, blendedPixels, stride);
        if (bitmap.CanFreeze)
        {
            bitmap.Freeze();
        }

        var image = new Image
        {
            Source = bitmap,
            Width = width,
            Height = height,
            Stretch = Stretch.None,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(image, left);
        Canvas.SetTop(image, top);
        return image;
    }

    private static byte[] CopyBackgroundPixels(BitmapSource background, Int32Rect sourceRect)
    {
        var converted = background.Format == PixelFormats.Bgra32
            ? background
            : new FormatConvertedBitmap(background, PixelFormats.Bgra32, null, 0);
        var stride = sourceRect.Width * 4;
        var pixels = new byte[stride * sourceRect.Height];
        converted.CopyPixels(sourceRect, pixels, stride, 0);
        return pixels;
    }

    private static byte[] CreateGeometryMaskPixels(Geometry geometry, int left, int top, int width, int height)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.PushTransform(new TranslateTransform(-left, -top));
            context.DrawGeometry(Brushes.White, null, geometry);
            context.Pop();
        }

        var mask = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        mask.Render(visual);
        var stride = width * 4;
        var pixels = new byte[stride * height];
        mask.CopyPixels(pixels, stride, 0);
        return pixels;
    }

    private double GetBlendStrength(bool isHovered)
    {
        var strength = ColorStrength;
        return Math.Clamp(strength + (isHovered ? 0.07 : 0), 0, 1);
    }

    private static byte BlendHighlightChannel(byte baseChannel, byte highlightChannel, double strength)
    {
        var multiply = baseChannel * highlightChannel / 255d;
        return (byte)Math.Clamp(Math.Round(baseChannel + ((multiply - baseChannel) * strength)), 0, 255);
    }

    public override bool HitTest(Point point)
    {
        return Mode switch
        {
            HighlightMode.Region => Normalize(Bounds).Contains(point),
            _ => CreateStrokeGeometry()?.FillContains(point) == true,
        };
    }

    public override string? HitHandle(Point point)
    {
        if (Mode == HighlightMode.Region)
        {
            var rect = Normalize(Bounds);
            if (Distance(rect.TopLeft, point) <= 14)
            {
                return "TopLeft";
            }

            if (Distance(rect.BottomRight, point) <= 14)
            {
                return "BottomRight";
            }

            return null;
        }

        if (Mode == HighlightMode.Line && Points.Count >= 2)
        {
            if (Distance(Points[0], point) <= 14)
            {
                return "Start";
            }

            if (Distance(Points[^1], point) <= 14)
            {
                return "End";
            }
        }

        return null;
    }

    public override void Move(Vector delta)
    {
        if (Mode == HighlightMode.Region)
        {
            Bounds = new Rect(Bounds.TopLeft + delta, Bounds.BottomRight + delta);
            return;
        }

        for (var i = 0; i < Points.Count; i++)
        {
            Points[i] = Points[i] + delta;
        }
    }

    public override void MoveHandle(string handle, Point point, bool constrainToSquare)
    {
        switch (Mode)
        {
            case HighlightMode.Region:
            {
                var rect = Normalize(Bounds);
                Bounds = handle switch
                {
                    "TopLeft" => CreateRectFromPoints(rect.BottomRight, point, constrainToSquare),
                    "BottomRight" => CreateRectFromPoints(rect.TopLeft, point, constrainToSquare),
                    _ => rect,
                };
                break;
            }
            case HighlightMode.Line when Points.Count >= 2:
                if (handle == "Start")
                {
                    Points[0] = point;
                }
                else if (handle == "End")
                {
                    Points[^1] = point;
                }
                break;
        }
    }

    public override void UpdateFromAnchor(Point anchor, Point current, bool constrainToSquare)
    {
        switch (Mode)
        {
            case HighlightMode.Region:
                Bounds = CreateRectFromPoints(anchor, current, constrainToSquare);
                break;
            case HighlightMode.Line:
                if (Points.Count == 0)
                {
                    Points.Add(anchor);
                }
                if (Points.Count == 1)
                {
                    Points.Add(current);
                }
                else
                {
                    Points[0] = anchor;
                    Points[^1] = current;
                }
                break;
            case HighlightMode.Freehand:
                if (Points.Count == 0)
                {
                    Points.Add(anchor);
                }
                if (Points.Count == 1)
                {
                    Points.Add(current);
                }
                else if (Distance(Points[^1], current) >= 1.5)
                {
                    Points.Add(current);
                }
                else
                {
                    Points[^1] = current;
                }
                break;
        }
    }

    public override void SetColor(Color color)
    {
        HighlightColor = color;
    }

    public void SetColorStrength(double strength)
    {
        ColorStrength = Math.Clamp(strength, 0, 1);
    }

    public void SetShadowStrength(double value)
    {
        ShadowStrength = Math.Clamp(value, 0, 1);
    }

    public void SetBorderWidth(double value)
    {
        BorderWidth = Math.Clamp(value, 0, 12);
    }

    public override AnnotationBase Clone()
    {
        var clone = new HighlightAnnotation
        {
            Mode = Mode,
            Bounds = Bounds,
            HighlightColor = HighlightColor,
            ColorStrength = ColorStrength,
        };
        clone.Points.AddRange(Points);
        clone.SetShadowStrength(ShadowStrength);
        clone.SetBorderWidth(BorderWidth);
        return clone;
    }

    private Geometry? CreateStrokeGeometry()
    {
        if (Points.Count < 2)
        {
            return null;
        }

        var figure = new PathFigure
        {
            StartPoint = Points[0],
            IsClosed = false,
            IsFilled = false,
        };

        for (var i = 1; i < Points.Count; i++)
        {
            figure.Segments.Add(new LineSegment(Points[i], true));
        }

        var geometry = new PathGeometry([figure]);
        return geometry.GetWidenedPathGeometry(CreateStrokePen(GetStrokeThickness()));
    }

    private Pen CreateStrokePen(double thickness)
    {
        return new Pen(Brushes.Black, thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
    }

    private double GetStrokeThickness()
    {
        return Mode == HighlightMode.Line ? LineThickness : FreehandThickness;
    }

    private Color GetFillColor(bool isHovered)
    {
        var maxAlpha = Mode == HighlightMode.Region ? 84 : 112;
        var alpha = (int)Math.Round(maxAlpha * ColorStrength);
        if (isHovered)
        {
            alpha += 8;
        }

        return WithAlpha(HighlightColor, (byte)Math.Clamp(alpha, 0, 255));
    }

    private static Rect Normalize(Rect rect)
    {
        return new Rect(rect.TopLeft, rect.BottomRight);
    }
}

internal sealed class ArrowAnnotation : AnnotationBase, IBorderShadowAnnotation
{
    private const double DefaultShaftThickness = 8;
    private const double DefaultHeadLength = 28;
    private const double DefaultHeadWidth = 22;
    private const double MinShaftThickness = 4;
    private const double MaxShaftThickness = 40;
    private const double MinHeadLength = 12;
    private const double MaxHeadLength = 128;
    private const double MinHeadWidth = 8;
    private const double MaxHeadWidth = 96;
    private const double DefaultOuterEdgeWidth = 32;
    private const double DefaultInnerEdgeWidth = 7;
    private const double DefaultTailSweep = 42;
    private const double DefaultHeadSkew = 0;
    private const double MinEdgeWidth = 2;
    private const double MaxEdgeWidth = 150;
    private const double MinTailSweep = -150;
    private const double MaxTailSweep = 180;
    private const double MinHeadSkew = -110;
    private const double MaxHeadSkew = 110;

    public Point Start { get; set; }

    public Point End { get; set; }

    public List<Point> BendPoints { get; } = new();

    public Color StrokeColor { get; private set; } = EditorWindow.DefaultArrowColor;

    public ArrowStyle Style { get; private set; } = ArrowStyle.BrushStroke;

    public double ShaftThickness { get; private set; } = DefaultShaftThickness;

    public double HeadLength { get; private set; } = DefaultHeadLength;

    public double HeadWidth { get; private set; } = DefaultHeadWidth;

    public double OuterEdgeWidth { get; private set; } = DefaultOuterEdgeWidth;

    public double InnerEdgeWidth { get; private set; } = DefaultInnerEdgeWidth;

    public double TailSweep { get; private set; } = DefaultTailSweep;

    public double HeadSkew { get; private set; } = DefaultHeadSkew;

    public double TailScale { get; private set; } = 1.0;

    public double BodyScale { get; private set; } = 1.0;

    public double FrontScale { get; private set; } = 1.0;

    public double HeadScale { get; private set; } = 1.0;

    public double TailHeadScale { get; private set; } = 1.0;

    public double TailRoundness { get; private set; } = EditorWindow.DefaultArrowTailRoundness;

    public double HeadRoundness { get; private set; } = EditorWindow.DefaultArrowHeadRoundness;

    public double ShadowStrength { get; private set; }

    public double BorderWidth { get; private set; }

    public bool HasStartHead { get; private set; }

    public bool HasEndHead { get; private set; } = true;

    public void SetTailScale(double value) => TailScale = Math.Clamp(value, 0.15, 3.0);

    public void SetBodyScale(double value) => BodyScale = Math.Clamp(value, 0.15, 3.0);

    public void SetFrontScale(double value) => FrontScale = Math.Clamp(value, 0.15, 3.0);

    public void SetHeadScale(double value) => HeadScale = Math.Clamp(value, 0.2, 1.6);

    public void SetTailHeadScale(double value) => TailHeadScale = Math.Clamp(value, 0.2, 1.6);

    public void SetTailRoundness(double value) => TailRoundness = Math.Clamp(value, 0, 1);

    public void SetHeadRoundness(double value) => HeadRoundness = Math.Clamp(value, 0, 1);

    public void SetShadowStrength(double value) => ShadowStrength = Math.Clamp(value, 0, 1);

    public void SetBorderWidth(double value) => BorderWidth = Math.Clamp(value, 0, 12);

    public void SetStartHeadEnabled(bool value) => HasStartHead = value;

    public void SetEndHeadEnabled(bool value) => HasEndHead = value;

    public static ArrowAnnotation Create(Point point)
    {
        return new ArrowAnnotation
        {
            Start = point,
            End = new Point(point.X + 80, point.Y),
        };
    }

    public void AddBendPointAt(Point clickPoint)
    {
        var anchors = BuildAnchors(End);
        var bestIndex = 0;
        var bestDist = double.MaxValue;
        for (var i = 0; i < anchors.Count - 1; i++)
        {
            var d = DistanceToSegment(anchors[i], anchors[i + 1], clickPoint);
            if (d < bestDist)
            {
                bestDist = d;
                bestIndex = i;
            }
        }
        BendPoints.Insert(bestIndex, clickPoint);
    }

    public void RemoveBendPointAt(int index)
    {
        if (index >= 0 && index < BendPoints.Count)
        {
            BendPoints.RemoveAt(index);
        }
    }

    private List<Point> BuildAnchors(Point endPoint)
    {
        var anchors = new List<Point>(BendPoints.Count + 2) { Start };
        anchors.AddRange(BendPoints);
        anchors.Add(endPoint);
        return anchors;
    }

    private static double DistanceToSegment(Point a, Point b, Point p)
    {
        var abx = b.X - a.X;
        var aby = b.Y - a.Y;
        var len2 = (abx * abx) + (aby * aby);
        if (len2 < 0.0001)
        {
            return (p - a).Length;
        }
        var t = Math.Clamp((((p.X - a.X) * abx) + ((p.Y - a.Y) * aby)) / len2, 0, 1);
        var proj = new Point(a.X + (abx * t), a.Y + (aby * t));
        return (p - proj).Length;
    }

    public override void Render(Canvas canvas, bool isSelected, bool isHovered)
    {
        var strokeColor = StrokeColor;
        var selectionGeometry = Style == ArrowStyle.BrushStroke || HasStartHead
            ? CreateExplicitArrowGeometry()
            : CreatePathGeometry(trimmedForArrowHead: false).GetWidenedPathGeometry(new Pen(Brushes.Black, GetResolvedShaftThickness() + 4));
        AddArrowShadow(canvas, selectionGeometry, ShadowStrength);
        switch (Style)
        {
            case ArrowStyle.Classic:
                RenderClassic(canvas, CreatePathGeometry(trimmedForArrowHead: HasEndHead), strokeColor, isSelected);
                break;
            case ArrowStyle.Handdrawn:
                RenderHanddrawn(canvas, CreatePathGeometry(trimmedForArrowHead: HasEndHead), strokeColor, isSelected);
                break;
            default:
                RenderBrush(canvas, strokeColor, isSelected);
                break;
        }

        var interactionGeometry = CreateArrowInteractionGeometry(selectionGeometry, BorderWidth);
        if (isSelected)
        {
            AddArrowMarchingAntsOutline(canvas, interactionGeometry);
        }
        else if (isHovered)
        {
            AddArrowHoverOutline(canvas, interactionGeometry);
        }

        if (isSelected)
        {
            AddHandle(canvas, Start);
            for (var i = 0; i < BendPoints.Count; i++)
            {
                AddHandle(canvas, BendPoints[i]);
            }
            AddHandle(canvas, End);
        }
    }

    private static Geometry CreateArrowInteractionGeometry(Geometry geometry, double borderWidth)
    {
        if (geometry.Bounds.Width <= 0 || geometry.Bounds.Height <= 0)
        {
            return geometry;
        }

        var outerStroke = geometry.GetWidenedPathGeometry(new Pen(Brushes.Black, Math.Max(2, (borderWidth * 2) + 2))
        {
            LineJoin = PenLineJoin.Round,
        });

        return Geometry.Combine(geometry, outerStroke, GeometryCombineMode.Union, null);
    }

    private static void AddArrowHoverOutline(Canvas canvas, Geometry geometry)
    {
        var outline = new ShapePath
        {
            Data = geometry,
            Stroke = new SolidColorBrush(Color.FromRgb(26, 169, 216)),
            StrokeThickness = 2,
            StrokeLineJoin = PenLineJoin.Round,
            Fill = Brushes.Transparent,
            IsHitTestVisible = false,
        };
        canvas.Children.Add(outline);
    }

    private static void AddArrowMarchingAntsOutline(Canvas canvas, Geometry geometry)
    {
        var dark = new ShapePath
        {
            Data = geometry,
            Stroke = Brushes.Black,
            StrokeThickness = 2,
            StrokeDashArray = new DoubleCollection { 2, 2 },
            StrokeLineJoin = PenLineJoin.Round,
            Fill = Brushes.Transparent,
            IsHitTestVisible = false,
        };
        canvas.Children.Add(dark);

        var light = new ShapePath
        {
            Data = geometry,
            Stroke = Brushes.White,
            StrokeThickness = 2,
            StrokeDashArray = new DoubleCollection { 2, 2 },
            StrokeDashOffset = 2,
            StrokeLineJoin = PenLineJoin.Round,
            Fill = Brushes.Transparent,
            IsHitTestVisible = false,
        };
        canvas.Children.Add(light);
    }

    private static void AddArrowShadow(Canvas canvas, Geometry geometry, double strength)
    {
        if (strength <= 0.001)
        {
            return;
        }

        var resolvedStrength = Math.Pow(Math.Clamp(strength, 0, 1), 0.65);
        canvas.Children.Add(new ShapePath
        {
            Data = geometry,
            Fill = MakeBrush(WithAlpha(Colors.Black, (byte)Math.Round(120 + (resolvedStrength * 110)))),
            IsHitTestVisible = false,
            Effect = new DropShadowEffect
            {
                BlurRadius = 10 + (resolvedStrength * 58),
                ShadowDepth = 5 + (resolvedStrength * 26),
                Direction = 315,
                Color = Colors.Black,
                Opacity = 0.62 + (resolvedStrength * 0.38),
                RenderingBias = RenderingBias.Quality,
            },
        });
    }

    private static void AddBezierHandleLine(Canvas canvas, Point from, Point to)
    {
        var line = new System.Windows.Shapes.Line
        {
            X1 = from.X,
            Y1 = from.Y,
            X2 = to.X,
            Y2 = to.Y,
            Stroke = Brushes.DodgerBlue,
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 4, 3 },
            IsHitTestVisible = false,
        };
        canvas.Children.Add(line);
    }

    public override bool HitTest(Point point)
    {
        var hitPen = new Pen(Brushes.Black, Math.Max(16, GetResolvedShaftThickness() + 12));
        var explicitGeometry = CreateExplicitArrowGeometry();
        return explicitGeometry.FillContains(point)
            || explicitGeometry.StrokeContains(new Pen(Brushes.Black, 14), point)
            || CreatePathGeometry(trimmedForArrowHead: true).StrokeContains(hitPen, point);
    }

    public override string? HitHandle(Point point)
    {
        if (Distance(Start, point) <= 14)
        {
            return "Start";
        }

        for (var i = 0; i < BendPoints.Count; i++)
        {
            if (Distance(BendPoints[i], point) <= 14)
            {
                return "Bend:" + i;
            }
        }

        if (Distance(End, point) <= 14)
        {
            return "End";
        }

        return null;
    }

    public override void Move(Vector delta)
    {
        Start += delta;
        End += delta;
        for (var i = 0; i < BendPoints.Count; i++)
        {
            BendPoints[i] = BendPoints[i] + delta;
        }
    }

    public override void MoveHandle(string handle, Point point, bool constrainToSquare)
    {
        switch (handle)
        {
            case "Start":
                Start = point;
                break;
            case "End":
                End = point;
                break;
            default:
                if (handle.StartsWith("Bend:", StringComparison.Ordinal)
                    && int.TryParse(handle.AsSpan(5), out var idx)
                    && idx >= 0 && idx < BendPoints.Count)
                {
                    BendPoints[idx] = point;
                }
                break;
        }
    }

    public override void UpdateFromAnchor(Point anchor, Point current, bool constrainToSquare)
    {
        Start = anchor;
        End = current;
        BendPoints.Clear();
        var delta = current - anchor;

        var length = Math.Max(1, delta.Length);
        ShaftThickness = Math.Clamp(length * 0.11, MinShaftThickness, 24);
        HeadLength = Math.Clamp(length * 0.32, 22, 68);
        HeadWidth = Math.Clamp(length * 0.22, 16, 54);
        OuterEdgeWidth = Math.Clamp(length * 0.34, 18, 86);
        InnerEdgeWidth = Math.Clamp(length * 0.08, 3, 22);
        TailSweep = Math.Clamp(length * 0.36, MinTailSweep, MaxTailSweep);
        HeadSkew = 0;
    }

    public override void SetColor(Color color)
    {
        StrokeColor = color;
    }

    public void SetStyle(ArrowStyle arrowStyle)
    {
        Style = arrowStyle;
    }

    public override AnnotationBase Clone()
    {
        var clone = new ArrowAnnotation
        {
            Start = Start,
            End = End,
            ShaftThickness = ShaftThickness,
            HeadLength = HeadLength,
            HeadWidth = HeadWidth,
            OuterEdgeWidth = OuterEdgeWidth,
            InnerEdgeWidth = InnerEdgeWidth,
            TailSweep = TailSweep,
            HeadSkew = HeadSkew,
            TailScale = TailScale,
            BodyScale = BodyScale,
            FrontScale = FrontScale,
            HeadScale = HeadScale,
            TailHeadScale = TailHeadScale,
            TailRoundness = TailRoundness,
            HeadRoundness = HeadRoundness,
            ShadowStrength = ShadowStrength,
            BorderWidth = BorderWidth,
            HasStartHead = HasStartHead,
            HasEndHead = HasEndHead,
        };
        clone.BendPoints.AddRange(BendPoints);
        clone.SetColor(StrokeColor);
        clone.SetStyle(Style);
        return clone;
    }

    private void RenderClassic(Canvas canvas, PathGeometry geometry, Color strokeColor, bool isSelected)
    {
        var shaftThickness = GetResolvedShaftThickness();
        var headLength = GetResolvedHeadLength();
        var headWidth = GetResolvedHeadWidth();
        var strokeBrush = MakeBrush(strokeColor);
        canvas.Children.Add(CreateShaftPath(geometry, strokeBrush, isSelected ? shaftThickness + 1 : shaftThickness));
        if (HasEndHead)
        {
            canvas.Children.Add(CreateFilledArrowHead(strokeBrush, headLength, headWidth));
        }
        if (HasStartHead)
        {
            canvas.Children.Add(CreateFilledArrowHead(strokeBrush, GetResolvedTailHeadLength(), GetResolvedTailHeadWidth(), atStart: true));
        }
    }

    private void RenderHanddrawn(Canvas canvas, PathGeometry geometry, Color strokeColor, bool isSelected)
    {
        var points = GetSampledCenterlinePoints(trimmedForArrowHead: HasEndHead, sampleCount: 28);
        if (points.Count < 2)
        {
            RenderClassic(canvas, geometry, strokeColor, isSelected);
            return;
        }

        var shaftThickness = GetResolvedShaftThickness();
        var baseThickness = Math.Max(2.8, (shaftThickness * 0.72) + (isSelected ? 0.9 : 0));
        canvas.Children.Add(CreateSketchStroke(points, strokeColor, baseThickness, jitterSeed: 0.65, offsetScale: 4.8 + (shaftThickness * 0.4)));
        canvas.Children.Add(CreateSketchStroke(points, WithAlpha(strokeColor, 205), Math.Max(1.2, baseThickness - 1.6), jitterSeed: 1.95, offsetScale: 7.2 + (shaftThickness * 0.45)));
        canvas.Children.Add(CreateSketchStroke(points, WithAlpha(Darken(strokeColor, 0.08), 155), Math.Max(1, shaftThickness * 0.2), jitterSeed: 3.2, offsetScale: 9.8 + (shaftThickness * 0.5), dashArray: [1.2, 3.1]));
        canvas.Children.Add(CreateHanddrawnTail(strokeColor, offset: -(shaftThickness * 0.9), thickness: Math.Max(1.4, shaftThickness * 0.3), length: 18 + (shaftThickness * 1.8)));
        canvas.Children.Add(CreateHanddrawnTail(strokeColor, offset: shaftThickness * 0.52, thickness: Math.Max(1.1, shaftThickness * 0.2), length: 14 + (shaftThickness * 1.1)));
        if (HasEndHead)
        {
            canvas.Children.Add(CreateHanddrawnHead(strokeColor, Math.Max(2.2, baseThickness * 0.72), GetResolvedHeadLength() * 1.04, GetResolvedHeadWidth() * 1.08));
        }
        if (HasStartHead)
        {
            canvas.Children.Add(CreateHanddrawnHead(strokeColor, Math.Max(2.2, baseThickness * 0.72), GetResolvedTailHeadLength() * 1.04, GetResolvedTailHeadWidth() * 1.08, atStart: true));
        }
    }

    private void RenderBrush(Canvas canvas, Color strokeColor, bool isSelected)
    {
        var explicitGeometry = CreateExplicitArrowGeometry();
        if (BorderWidth > 0.001)
        {
            canvas.Children.Add(new ShapePath
            {
                Data = explicitGeometry,
                Fill = Brushes.Transparent,
                Stroke = MakeBrush(GetBorderColor(strokeColor)),
                StrokeThickness = BorderWidth * 2,
                StrokeLineJoin = PenLineJoin.Round,
                IsHitTestVisible = false,
            });
        }

        canvas.Children.Add(new ShapePath
        {
            Data = explicitGeometry,
            Fill = MakeBrush(strokeColor),
            IsHitTestVisible = false,
        });
    }

    private void RenderBrushShaft(Canvas canvas, Color strokeColor, bool isSelected)
    {
        var shaftGeometry = CreatePathGeometry(trimmedForArrowHead: false);
        var thickness = isSelected ? GetResolvedShaftThickness() + 1 : GetResolvedShaftThickness();
        if (BorderWidth > 0.001)
        {
            canvas.Children.Add(CreateShaftPath(shaftGeometry, MakeBrush(GetBorderColor(strokeColor)), thickness + (BorderWidth * 2)));
        }

        canvas.Children.Add(CreateShaftPath(shaftGeometry, MakeBrush(strokeColor), thickness));
    }

    private void AddClippedOffsetStroke(Canvas canvas, PathGeometry arrowGeometry, IReadOnlyList<Point> points, double offset, Color strokeColor, double thickness)
    {
        var stroke = CreateOffsetStroke(points, offset, MakeBrush(strokeColor), thickness);
        stroke.Clip = arrowGeometry.Clone();
        canvas.Children.Add(stroke);
    }

    private void AddClippedSketchStroke(Canvas canvas, PathGeometry arrowGeometry, IReadOnlyList<Point> points, Color strokeColor, double thickness, double jitterSeed, double offsetScale, DoubleCollection? dashArray = null)
    {
        var stroke = CreateSketchStroke(points, strokeColor, thickness, jitterSeed, offsetScale, dashArray);
        stroke.Clip = arrowGeometry.Clone();
        canvas.Children.Add(stroke);
    }

    private static Color GetBorderColor(Color strokeColor)
    {
        var brightness = ((0.2126 * strokeColor.R) + (0.7152 * strokeColor.G) + (0.0722 * strokeColor.B)) / 255d;
        return brightness < 0.42
            ? WithAlpha(Colors.White, 214)
            : WithAlpha(Colors.Black, 170);
    }

    private PathGeometry CreateExplicitArrowGeometry()
    {
        // Classic block arrow: shaft (parallel sides) + triangular arrowhead with notched base.
        // Outline traversal:
        //   tailLeft -> ... centerline left edge ... -> shaftLeftBase
        //        -> headLeftBarb -> tip -> headRightBarb -> shaftRightBase
        //        -> ... centerline right edge (reversed) ... -> tailRight -> close
        //
        // Sizing is proportional to total length so the head is always visible relative to the shaft.

        var totalLength = Math.Max(1.0, GetApproximateLength());

        // Resolve dimensions, but enforce sane proportions vs. arrow length so the head reads as an arrow.
        var maxShaft = Math.Max(6, totalLength * 0.18);
        var shaftThickness = Math.Clamp(GetResolvedShaftThickness(), 6, maxShaft);
        var minHead = shaftThickness * 2.2 * HeadScale;
        var maxHead = Math.Max(minHead, totalLength * 0.45);
        var headLength = Math.Clamp(GetResolvedHeadLength() * HeadScale, minHead, maxHead);
        var minHeadWidth = shaftThickness * 2.4 * HeadScale;
        var maxHeadWidth = Math.Max(minHeadWidth, totalLength * 0.45);
        var headWidth = Math.Clamp(GetResolvedHeadWidth() * HeadScale, minHeadWidth, maxHeadWidth);
        // Make sure the head is always at least as wide as the shaft + visible barbs.
        headWidth = Math.Max(headWidth, (shaftThickness + 14) * HeadScale);
        var tailHeadLength = GetResolvedTailHeadLength();
        var tailHeadWidth = GetResolvedTailHeadWidth();

        // Sample the centerline and trim it so the shaft stops where the arrowhead begins.
        var fullCenterline = GetSampledCenterlinePoints(trimmedForArrowHead: false, sampleCount: 64);
        if (fullCenterline.Count < 2)
        {
            fullCenterline = new List<Point> { Start, End };
        }

        var trimmedCenterline = HasStartHead
            ? TrimPolylineFromStart(fullCenterline, tailHeadLength)
            : fullCenterline;
        if (HasEndHead)
        {
            trimmedCenterline = TrimPolylineFromEnd(trimmedCenterline, headLength);
        }
        if (trimmedCenterline.Count < 2)
        {
            trimmedCenterline = new List<Point> { Start, End };
        }

        // Final tangent at the tip.
        var endDir = trimmedCenterline[^1] - (trimmedCenterline.Count >= 2 ? trimmedCenterline[^2] : trimmedCenterline[0]);
        if (endDir.Length < 0.001) endDir = End - Start;
        if (endDir.Length < 0.001) endDir = new Vector(1, 0);
        endDir.Normalize();
        var endNormal = new Vector(-endDir.Y, endDir.X);

        var shaftBaseCenter = trimmedCenterline[^1];
        var tip = End;
        var startDir = trimmedCenterline.Count >= 2 ? trimmedCenterline[1] - trimmedCenterline[0] : End - Start;
        if (startDir.Length < 0.001) startDir = End - Start;
        if (startDir.Length < 0.001) startDir = new Vector(1, 0);
        startDir.Normalize();
        var startHeadDir = -startDir;
        var startNormal = new Vector(-startHeadDir.Y, startHeadDir.X);
        var tailBaseCenter = trimmedCenterline[0];
        var tailTip = Start;
        var tailLeftBarb = tailBaseCenter + (startNormal * (tailHeadWidth / 2.0));
        var tailRightBarb = tailBaseCenter - (startNormal * (tailHeadWidth / 2.0));

        // Build left/right edges of the shaft along the centerline.
        // Width varies along the shaft using TailScale (t=0), BodyScale (t=0.5), and 1.0 at the head base (t=1)
        // via a quadratic Bezier blend so the transitions are smooth.
        var leftEdge = new List<Point>(trimmedCenterline.Count);
        var rightEdge = new List<Point>(trimmedCenterline.Count);
        var halfShaftBase = shaftThickness / 2.0;
        var count = trimmedCenterline.Count;
        for (var i = 0; i < count; i++)
        {
            var t = count > 1 ? i / (double)(count - 1) : 0;
            var scale = ShaftScaleAt(t, TailScale, BodyScale, FrontScale);
            var halfShaft = halfShaftBase * scale;
            var n = GetPolylineNormal(trimmedCenterline, i);
            leftEdge.Add(trimmedCenterline[i] + (n * halfShaft));
            rightEdge.Add(trimmedCenterline[i] - (n * halfShaft));
        }

        var shaftLeftBase = shaftBaseCenter + (endNormal * halfShaftBase * FrontScale);
        var shaftRightBase = shaftBaseCenter - (endNormal * halfShaftBase * FrontScale);
        var headLeftBarb = shaftBaseCenter + (endNormal * (headWidth / 2.0));
        var headRightBarb = shaftBaseCenter - (endNormal * (headWidth / 2.0));

        var figure = new PathFigure
        {
            StartPoint = HasStartHead ? tailLeftBarb : leftEdge[0],
            IsClosed = true,
            IsFilled = true,
            Segments = new PathSegmentCollection(),
        };

        for (var i = HasStartHead ? 0 : 1; i < leftEdge.Count; i++)
        {
            figure.Segments.Add(new LineSegment(leftEdge[i], true));
        }

        if (HasEndHead)
        {
            // Replace the last left point with the precise shaftLeftBase to avoid micro-misalignment with the head.
            figure.Segments.Add(new LineSegment(shaftLeftBase, true));
            figure.Segments.Add(new LineSegment(headLeftBarb, true));
            AddRoundedHeadTip(figure, headLeftBarb, tip, headRightBarb, headLength, HeadRoundness);
            figure.Segments.Add(new LineSegment(shaftRightBase, true));
        }
        else
        {
            AddRoundedEndCap(figure, leftEdge[^1], rightEdge[^1], endDir, halfShaftBase * FrontScale, HeadRoundness);
        }

        for (var i = rightEdge.Count - 1; i >= 0; i--)
        {
            figure.Segments.Add(new LineSegment(rightEdge[i], true));
        }

        if (HasStartHead)
        {
            figure.Segments.Add(new LineSegment(tailRightBarb, true));
            AddRoundedHeadTip(figure, tailRightBarb, tailTip, tailLeftBarb, tailHeadLength, TailRoundness);
        }
        else
        {
            AddRoundedEndCap(figure, rightEdge[0], leftEdge[0], -startDir, halfShaftBase * TailScale, TailRoundness);
        }

        return new PathGeometry([figure]) { FillRule = FillRule.Nonzero };
    }

    private static void AddRoundedEndCap(PathFigure figure, Point from, Point to, Vector outwardDirection, double halfShaft, double roundness)
    {
        roundness = Math.Clamp(roundness, 0, 1);
        if (roundness <= 0.001 || outwardDirection.Length < 0.001 || halfShaft <= 0.001)
        {
            figure.Segments.Add(new LineSegment(to, true));
            return;
        }

        outwardDirection.Normalize();
        var capDepth = Math.Clamp(halfShaft * roundness * 1.65, 0, halfShaft * 1.65);
        var controlOffset = outwardDirection * capDepth;
        figure.Segments.Add(new BezierSegment(
            from + controlOffset,
            to + controlOffset,
            to,
            true));
    }

    private static void AddRoundedHeadTip(PathFigure figure, Point leftBarb, Point tip, Point rightBarb, double headLength, double roundness)
    {
        roundness = Math.Clamp(roundness, 0, 1);
        if (roundness <= 0.001 || headLength <= 0.001)
        {
            figure.Segments.Add(new LineSegment(tip, true));
            figure.Segments.Add(new LineSegment(rightBarb, true));
            return;
        }

        var edgeT = Math.Clamp(roundness * 0.48, 0, 0.48);
        var leftShoulder = Lerp(tip, leftBarb, edgeT);
        var rightShoulder = Lerp(tip, rightBarb, edgeT);
        figure.Segments.Add(new LineSegment(leftShoulder, true));
        figure.Segments.Add(new BezierSegment(tip, tip, rightShoulder, true));
        figure.Segments.Add(new LineSegment(rightBarb, true));
    }

    private static Point Lerp(Point from, Point to, double amount)
    {
        return new Point(
            from.X + ((to.X - from.X) * amount),
            from.Y + ((to.Y - from.Y) * amount));
    }

    private static double ShaftScaleAt(double t, double tail, double body, double front)
    {
        // Quadratic Bezier scalar blend: tail (t=0) -> body (t=0.5) -> front (t=1, head base).
        var u = 1 - t;
        return (u * u * tail) + (2 * u * t * body) + (t * t * front);
    }

    private static List<Point> TrimPolylineFromEnd(IReadOnlyList<Point> points, double trimLength)
    {
        if (points.Count < 2 || trimLength <= 0)
        {
            return points.ToList();
        }

        // Compute total length and walk back from the end until trimLength is consumed.
        double remaining = trimLength;
        var lastIndex = points.Count - 1;
        var endPoint = points[lastIndex];

        for (var i = lastIndex; i > 0; i--)
        {
            var seg = points[i] - points[i - 1];
            var segLen = seg.Length;
            if (segLen >= remaining)
            {
                seg.Normalize();
                var cut = points[i] - (seg * remaining);
                var trimmed = new List<Point>(i + 1);
                for (var k = 0; k < i; k++)
                {
                    trimmed.Add(points[k]);
                }
                trimmed.Add(cut);
                return trimmed;
            }
            remaining -= segLen;
        }

        // Trim length exceeds total polyline; return a single short segment near the start.
        var dir = points[1] - points[0];
        if (dir.Length < 0.001) dir = new Vector(1, 0);
        dir.Normalize();
        return new List<Point> { points[0], points[0] + (dir * 1.0) };
    }

    private static List<Point> TrimPolylineFromStart(IReadOnlyList<Point> points, double trimLength)
    {
        if (points.Count < 2 || trimLength <= 0)
        {
            return points.ToList();
        }

        double remaining = trimLength;
        for (var i = 0; i < points.Count - 1; i++)
        {
            var seg = points[i + 1] - points[i];
            var segLen = seg.Length;
            if (segLen >= remaining)
            {
                seg.Normalize();
                var cut = points[i] + (seg * remaining);
                var trimmed = new List<Point>(points.Count - i) { cut };
                for (var k = i + 1; k < points.Count; k++)
                {
                    trimmed.Add(points[k]);
                }
                return trimmed;
            }
            remaining -= segLen;
        }

        var dir = points[^2] - points[^1];
        if (dir.Length < 0.001) dir = new Vector(-1, 0);
        dir.Normalize();
        return new List<Point> { points[^1] + (dir * 1.0), points[^1] };
    }

    private ShapePath CreateShaftPath(PathGeometry geometry, Brush strokeBrush, double thickness)
    {
        return new ShapePath
        {
            Data = geometry,
            Stroke = strokeBrush,
            StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
        };
    }

    private Polygon CreateFilledArrowHead(Brush fillBrush, double headLength, double headWidth, bool atStart = false)
    {
        var frame = GetArrowHeadFrame(headLength, headWidth, atStart);
        return new Polygon
        {
            Fill = fillBrush,
            Points =
            {
                frame.Tip,
                frame.LeftBase,
                frame.RightBase,
            },
            IsHitTestVisible = false,
        };
    }

    private Geometry CreateArrowHeadGeometry(double headLength, double headWidth, bool atStart = false)
    {
        var frame = GetArrowHeadFrame(headLength, headWidth, atStart);
        var figure = new PathFigure
        {
            StartPoint = frame.Tip,
            IsClosed = true,
            IsFilled = true,
            Segments =
            {
                new LineSegment(frame.LeftBase, true),
                new LineSegment(frame.RightBase, true),
            },
        };
        return new PathGeometry([figure]);
    }

    private ShapePath CreateOffsetStroke(IReadOnlyList<Point> points, double offset, Brush strokeBrush, double thickness)
    {
        return new ShapePath
        {
            Data = CreateOffsetPathGeometry(points, offset),
            Stroke = strokeBrush,
            StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            IsHitTestVisible = false,
        };
    }

    private ShapePath CreateSketchStroke(IReadOnlyList<Point> points, Color strokeColor, double thickness, double jitterSeed, double offsetScale, DoubleCollection? dashArray = null)
    {
        var path = new ShapePath
        {
            Data = CreateSketchedPathGeometry(points, jitterSeed, offsetScale),
            Stroke = MakeBrush(strokeColor),
            StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            IsHitTestVisible = false,
        };

        if (dashArray is not null)
        {
            path.StrokeDashArray = dashArray;
        }

        return path;
    }

    private Line CreateBrushTail(Color strokeColor, double offset, double thickness, double length)
    {
        var direction = GetStartDirection();
        var normal = new Vector(-direction.Y, direction.X);
        var tailStart = Start - (direction * length) + (normal * (offset + GetResolvedTailSweep()));
        var tailEnd = Start + (direction * (6 + (ShaftThickness * 0.3))) + (normal * (offset * 0.45));
        return new Line
        {
            X1 = tailStart.X,
            Y1 = tailStart.Y,
            X2 = tailEnd.X,
            Y2 = tailEnd.Y,
            Stroke = MakeBrush(WithAlpha(strokeColor, 150)),
            StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            IsHitTestVisible = false,
        };
    }

    private Line CreateHanddrawnTail(Color strokeColor, double offset, double thickness, double length)
    {
        var direction = GetStartDirection();
        var normal = new Vector(-direction.Y, direction.X);
        var tailStart = Start - (direction * length) + (normal * offset);
        var tailEnd = Start + (normal * (offset * 0.28));
        return new Line
        {
            X1 = tailStart.X,
            Y1 = tailStart.Y,
            X2 = tailEnd.X,
            Y2 = tailEnd.Y,
            Stroke = MakeBrush(WithAlpha(strokeColor, 180)),
            StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            IsHitTestVisible = false,
        };
    }

    private ShapePath CreateHanddrawnHead(Color strokeColor, double thickness, double headLength, double headWidth, bool atStart = false)
    {
        var frame = GetArrowHeadFrame(headLength, headWidth, atStart);
        var leftMid = frame.Tip - (frame.Direction * (headLength * 0.42)) + (frame.Normal * (headWidth * 0.16));
        var rightMid = frame.Tip - (frame.Direction * (headLength * 0.34)) - (frame.Normal * (headWidth * 0.08));

        var leftFigure = new PathFigure
        {
            StartPoint = frame.LeftBase,
            IsClosed = false,
            IsFilled = false,
            Segments = { new LineSegment(frame.Tip, true), new LineSegment(leftMid, true) },
        };
        var rightFigure = new PathFigure
        {
            StartPoint = frame.RightBase + (frame.Direction * (headLength * 0.18)),
            IsClosed = false,
            IsFilled = false,
            Segments = { new LineSegment(frame.Tip, true), new LineSegment(rightMid, true) },
        };

        return new ShapePath
        {
            Data = new PathGeometry([leftFigure, rightFigure]),
            Stroke = MakeBrush(strokeColor),
            StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            IsHitTestVisible = false,
        };
    }

    private Polygon CreateBrushArrowHead(Color strokeColor, double headLength, double headWidth, bool atStart = false)
    {
        var frame = GetArrowHeadFrame(headLength, headWidth, atStart);
        var upperShoulder = frame.LeftBase + (frame.Direction * (headLength * 0.02));
        var innerNotch = frame.Tip - (frame.Direction * (headLength * 0.42)) + (frame.Normal * (headWidth * 0.04));
        var lowerShoulder = frame.RightBase + (frame.Direction * (headLength * 0.28));
        var belly = frame.BaseCenter - (frame.Normal * (headWidth * 0.34));
        return new Polygon
        {
            Fill = MakeBrush(strokeColor),
            Points =
            {
                frame.Tip,
                upperShoulder,
                innerNotch,
                lowerShoulder,
                belly,
            },
            IsHitTestVisible = false,
        };
    }

    private Geometry CreateBrushArrowHeadGeometry(double headLength, double headWidth, bool atStart = false)
    {
        var frame = GetArrowHeadFrame(headLength, headWidth, atStart);
        var upperShoulder = frame.LeftBase + (frame.Direction * (headLength * 0.02));
        var innerNotch = frame.Tip - (frame.Direction * (headLength * 0.42)) + (frame.Normal * (headWidth * 0.04));
        var lowerShoulder = frame.RightBase + (frame.Direction * (headLength * 0.28));
        var belly = frame.BaseCenter - (frame.Normal * (headWidth * 0.34));

        var figure = new PathFigure
        {
            StartPoint = frame.Tip,
            IsClosed = true,
            IsFilled = true,
            Segments =
            {
                new LineSegment(upperShoulder, true),
                new LineSegment(innerNotch, true),
                new LineSegment(lowerShoulder, true),
                new LineSegment(belly, true),
            },
        };

        return new PathGeometry([figure]);
    }

    private Ellipse CreateBrushStartCap(Color strokeColor, double shaftThickness)
    {
        var normal = GetPolylineNormal(GetFlattenedPoints(CreatePathGeometry()), 0);
        var width = Math.Max(12, shaftThickness * 1.85);
        var height = Math.Max(8, shaftThickness * 1.1);
        var ellipse = new Ellipse
        {
            Width = width,
            Height = height,
            Fill = MakeBrush(WithAlpha(strokeColor, 210)),
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(ellipse, Start.X - (width / 2) + (normal.X * 1.4));
        Canvas.SetTop(ellipse, Start.Y - (height / 2) + (normal.Y * 1.4));
        return ellipse;
    }

    private Geometry CreateBrushStartCapGeometry(double shaftThickness)
    {
        var normal = GetPolylineNormal(GetFlattenedPoints(CreatePathGeometry()), 0);
        var width = Math.Max(12, shaftThickness * 1.85);
        var height = Math.Max(8, shaftThickness * 1.1);
        var center = new Point(Start.X + (normal.X * 1.4), Start.Y + (normal.Y * 1.4));
        return new EllipseGeometry(center, width / 2, height / 2);
    }

    private PathGeometry CreateRibbonGeometry(IReadOnlyList<Point> points, double shaftThickness, bool useAsymmetricEdges = false)
    {
        var leftEdge = new List<Point>(points.Count);
        var rightEdge = new List<Point>(points.Count);
        var outerWidth = useAsymmetricEdges ? GetResolvedOuterEdgeWidth() : shaftThickness * 0.72;
        var innerWidth = useAsymmetricEdges ? GetResolvedInnerEdgeWidth() : shaftThickness * 0.58;

        for (var index = 0; index < points.Count; index++)
        {
            var normal = GetPolylineNormal(points, index);
            var edgeWidths = GetBrushEdgeWidths(index, points.Count, shaftThickness, outerWidth, innerWidth);
            leftEdge.Add(points[index] + (normal * edgeWidths.Outer));
            rightEdge.Add(points[index] - (normal * edgeWidths.Inner));
        }

        var figure = new PathFigure
        {
            StartPoint = leftEdge[0],
            IsClosed = true,
            IsFilled = true,
        };

        for (var index = 1; index < leftEdge.Count; index++)
        {
            figure.Segments.Add(new LineSegment(leftEdge[index], true));
        }

        for (var index = rightEdge.Count - 1; index >= 0; index--)
        {
            figure.Segments.Add(new LineSegment(rightEdge[index], true));
        }

        return new PathGeometry([figure]);
    }

    private List<Point> GetSampledCenterlinePoints(bool trimmedForArrowHead, int sampleCount)
    {
        var endPoint = trimmedForArrowHead ? GetArrowShaftEnd() : End;
        var anchors = BuildAnchors(endPoint);

        if (anchors.Count == 2)
        {
            var count = Math.Max(2, sampleCount);
            var line = new List<Point>(count);
            for (var i = 0; i < count; i++)
            {
                var t = i / (double)(count - 1);
                line.Add(new Point(
                    anchors[0].X + ((anchors[1].X - anchors[0].X) * t),
                    anchors[0].Y + ((anchors[1].Y - anchors[0].Y) * t)));
            }
            return line;
        }

        // Catmull-Rom spline through anchors (tension 0.5).
        var segments = anchors.Count - 1;
        var perSegment = Math.Max(8, sampleCount / segments);
        var points = new List<Point>((perSegment * segments) + 1) { anchors[0] };
        for (var s = 0; s < segments; s++)
        {
            var p0 = s == 0 ? anchors[0] : anchors[s - 1];
            var p1 = anchors[s];
            var p2 = anchors[s + 1];
            var p3 = s + 2 >= anchors.Count ? anchors[^1] : anchors[s + 2];
            for (var i = 1; i <= perSegment; i++)
            {
                var t = i / (double)perSegment;
                var t2 = t * t;
                var t3 = t2 * t;
                var x = 0.5 * ((2 * p1.X)
                    + ((-p0.X + p2.X) * t)
                    + ((((2 * p0.X) - (5 * p1.X) + (4 * p2.X)) - p3.X) * t2)
                    + (((-p0.X + (3 * p1.X)) - (3 * p2.X) + p3.X) * t3));
                var y = 0.5 * ((2 * p1.Y)
                    + ((-p0.Y + p2.Y) * t)
                    + ((((2 * p0.Y) - (5 * p1.Y) + (4 * p2.Y)) - p3.Y) * t2)
                    + (((-p0.Y + (3 * p1.Y)) - (3 * p2.Y) + p3.Y) * t3));
                points.Add(new Point(x, y));
            }
        }
        return points;
    }

    private List<Point> GetSampledCenterlinePointsLegacy(bool trimmedForArrowHead, int sampleCount)
    {
        var flattenedPoints = GetFlattenedPoints(CreatePathGeometry(trimmedForArrowHead));
        if (flattenedPoints.Count <= 2 || sampleCount <= 2)
        {
            return flattenedPoints;
        }

        var cumulativeLengths = new double[flattenedPoints.Count];
        for (var index = 1; index < flattenedPoints.Count; index++)
        {
            cumulativeLengths[index] = cumulativeLengths[index - 1] + (flattenedPoints[index] - flattenedPoints[index - 1]).Length;
        }

        var totalLength = cumulativeLengths[^1];
        if (totalLength < 0.001)
        {
            return flattenedPoints;
        }

        var sampledPoints = new List<Point>(sampleCount);
        for (var index = 0; index < sampleCount; index++)
        {
            var targetLength = totalLength * (index / (double)(sampleCount - 1));
            sampledPoints.Add(InterpolatePointAtLength(flattenedPoints, cumulativeLengths, targetLength));
        }

        return sampledPoints;
    }

    private PathGeometry CreateSketchedPathGeometry(IReadOnlyList<Point> points, double jitterSeed, double offsetScale)
    {
        var figure = new PathFigure
        {
            StartPoint = JitterPoint(points, 0, jitterSeed, offsetScale),
            IsClosed = false,
            IsFilled = false,
        };

        for (var index = 1; index < points.Count; index++)
        {
            figure.Segments.Add(new LineSegment(JitterPoint(points, index, jitterSeed, offsetScale), true));
        }

        return new PathGeometry([figure]);
    }

    private PathGeometry CreateOffsetPathGeometry(IReadOnlyList<Point> points, double offset)
    {
        var figure = new PathFigure
        {
            StartPoint = OffsetPoint(points, 0, offset),
            IsClosed = false,
            IsFilled = false,
        };

        for (var index = 1; index < points.Count; index++)
        {
            figure.Segments.Add(new LineSegment(OffsetPoint(points, index, offset), true));
        }

        return new PathGeometry([figure]);
    }

    private List<Point> GetFlattenedPoints(PathGeometry geometry)
    {
        var flattened = geometry.GetFlattenedPathGeometry();
        var points = new List<Point>();

        foreach (var figure in flattened.Figures)
        {
            AddFlattenedPoint(points, figure.StartPoint);
            foreach (var segment in figure.Segments.OfType<PolyLineSegment>())
            {
                foreach (var point in segment.Points)
                {
                    AddFlattenedPoint(points, point);
                }
            }
        }

        return points;
    }

    private static void AddFlattenedPoint(ICollection<Point> points, Point point)
    {
        if (points.Count == 0)
        {
            points.Add(point);
            return;
        }

        var lastPoint = points.Last();
        if ((lastPoint - point).Length >= 0.75)
        {
            points.Add(point);
        }
    }

    private Point OffsetPoint(IReadOnlyList<Point> points, int index, double offset)
    {
        return points[index] + (GetPolylineNormal(points, index) * offset);
    }

    private static Point InterpolatePointAtLength(IReadOnlyList<Point> points, IReadOnlyList<double> cumulativeLengths, double targetLength)
    {
        for (var index = 1; index < points.Count; index++)
        {
            if (targetLength > cumulativeLengths[index])
            {
                continue;
            }

            var segmentLength = cumulativeLengths[index] - cumulativeLengths[index - 1];
            if (segmentLength < 0.001)
            {
                return points[index];
            }

            var progress = (targetLength - cumulativeLengths[index - 1]) / segmentLength;
            return points[index - 1] + ((points[index] - points[index - 1]) * progress);
        }

        return points[^1];
    }

    private Point JitterPoint(IReadOnlyList<Point> points, int index, double jitterSeed, double offsetScale)
    {
        var progress = points.Count <= 1 ? 0 : index / (double)(points.Count - 1);
        var normal = GetPolylineNormal(points, index);
        var tangent = index == 0
            ? points[Math.Min(1, points.Count - 1)] - points[0]
            : points[index] - points[index - 1];

        if (tangent.Length < 0.001)
        {
            tangent = new Vector(1, 0);
        }

        tangent.Normalize();
        var normalWobble = Math.Sin((progress * 8.4) + jitterSeed) * offsetScale;
        var tangentWobble = Math.Cos((progress * 5.7) + (jitterSeed * 1.7)) * (offsetScale * 0.42);
        return points[index] + (normal * normalWobble) + (tangent * tangentWobble);
    }

    private Vector GetPolylineNormal(IReadOnlyList<Point> points, int index)
    {
        Vector tangent;
        if (points.Count < 2)
        {
            tangent = End - Start;
        }
        else if (index == 0)
        {
            tangent = points[1] - points[0];
        }
        else if (index == points.Count - 1)
        {
            tangent = points[index] - points[index - 1];
        }
        else
        {
            tangent = points[index + 1] - points[index - 1];
        }

        if (tangent.Length < 0.001)
        {
            tangent = new Vector(1, 0);
        }

        tangent.Normalize();
        return new Vector(-tangent.Y, tangent.X);
    }

    private static (double Outer, double Inner) GetBrushEdgeWidths(int index, int count, double shaftThickness, double outerWidth, double innerWidth)
    {
        var progress = count <= 1 ? 1 : index / (double)(count - 1);
        var tailTaper = 0.08 + (0.92 * Math.Pow(progress, 0.34));
        var bodySwell = 0.82 + (0.55 * Math.Sin(progress * Math.PI * 0.9));
        var headTaper = progress > 0.84 ? 1 - (((progress - 0.84) / 0.16) * 0.18) : 1;
        var outer = Math.Max(shaftThickness * 0.34, outerWidth * tailTaper * bodySwell * headTaper);
        var inner = Math.Max(shaftThickness * 0.08, innerWidth * (0.42 + (0.58 * tailTaper)) * (0.9 + (0.26 * Math.Sin(progress * Math.PI))) * headTaper);
        return (outer, inner);
    }

    private PathGeometry CreatePathGeometry(bool trimmedForArrowHead = false)
    {
        var points = GetSampledCenterlinePoints(trimmedForArrowHead, 64);
        var figure = new PathFigure
        {
            StartPoint = points[0],
            IsFilled = false,
            IsClosed = false,
        };

        for (var i = 1; i < points.Count; i++)
        {
            figure.Segments.Add(new LineSegment(points[i], true));
        }

        return new PathGeometry([figure]);
    }

    private IEnumerable<PathSegment> CreatePathSegments(Point[] points)
    {
        for (var index = 1; index < points.Length; index++)
        {
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
        return false;
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

        return previous is { } before && last is { } end ? end - before : End - Start;
    }

    private Point GetArrowShaftEnd()
    {
        if (!HasEndHead)
        {
            return End;
        }

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
        return End - (direction * Math.Max(10, GetResolvedHeadLength() * 0.74));
    }

    private Vector GetStartDirection()
    {
        var firstAnchor = BendPoints.Count > 0 ? BendPoints[0] : End;
        var direction = firstAnchor - Start;
        if (direction.Length < 0.001)
        {
            direction = End - Start;
        }

        if (direction.Length < 0.001)
        {
            direction = new Vector(1, 0);
        }

        direction.Normalize();
        return direction;
    }

    private double GetApproximateLength()
    {
        var points = GetFlattenedPoints(CreatePathGeometry());
        if (points.Count < 2)
        {
            return (End - Start).Length;
        }

        double length = 0;
        for (var index = 1; index < points.Count; index++)
        {
            length += (points[index] - points[index - 1]).Length;
        }

        return length;
    }

    private double GetResolvedShaftThickness()
    {
        var maxThickness = Math.Max(MinShaftThickness, Math.Min(MaxShaftThickness, GetApproximateLength() * 0.26));
        return Math.Clamp(ShaftThickness, MinShaftThickness, maxThickness);
    }

    private double GetResolvedHeadLength()
    {
        var maxLength = Math.Max(MinHeadLength, Math.Min(MaxHeadLength, GetApproximateLength() * 0.48));
        return Math.Clamp(HeadLength, MinHeadLength, maxLength);
    }

    private double GetResolvedHeadWidth()
    {
        var maxWidth = Math.Max(MinHeadWidth, Math.Min(MaxHeadWidth, GetResolvedHeadLength() * 1.85));
        return Math.Clamp(HeadWidth, MinHeadWidth, maxWidth);
    }

    private double GetResolvedTailHeadLength()
    {
        return Math.Clamp(GetResolvedHeadLength() * TailHeadScale, MinHeadLength, MaxHeadLength);
    }

    private double GetResolvedTailHeadWidth()
    {
        return Math.Clamp(GetResolvedHeadWidth() * TailHeadScale, MinHeadWidth, MaxHeadWidth);
    }

    private double GetResolvedOuterEdgeWidth()
    {
        var maxWidth = Math.Max(MinEdgeWidth, Math.Min(MaxEdgeWidth, GetApproximateLength() * 0.95));
        return Math.Clamp(OuterEdgeWidth, MinEdgeWidth, maxWidth);
    }

    private double GetResolvedInnerEdgeWidth()
    {
        var maxWidth = Math.Max(MinEdgeWidth, Math.Min(MaxEdgeWidth, GetApproximateLength() * 0.7));
        return Math.Clamp(InnerEdgeWidth, MinEdgeWidth, maxWidth);
    }

    private double GetResolvedTailSweep()
    {
        var limit = Math.Max(12, Math.Min(MaxTailSweep, GetApproximateLength() * 0.9));
        return Math.Clamp(TailSweep, -limit, limit);
    }

    private double GetResolvedHeadSkew()
    {
        var limit = Math.Max(8, Math.Min(MaxHeadSkew, GetResolvedHeadWidth() * 1.6));
        return Math.Clamp(HeadSkew, -limit, limit);
    }

    private (Point Tip, Point BaseCenter, Point LeftBase, Point RightBase, Vector Direction, Vector Normal) GetArrowHeadFrame(double headLength, double headWidth, bool atStart = false)
    {
        var direction = atStart ? -GetStartDirection() : GetArrowHeadDirection();
        if (direction.Length < 0.001)
        {
            direction = atStart ? Start - End : End - Start;
        }

        if (direction.Length < 0.001)
        {
            direction = new Vector(1, 0);
        }

        direction.Normalize();
        var normal = new Vector(-direction.Y, direction.X);
        var tip = atStart ? Start : End;
        var skew = atStart ? -GetResolvedHeadSkew() : GetResolvedHeadSkew();
        var baseCenter = tip - (direction * headLength) + (normal * skew);
        return (
            Tip: tip,
            BaseCenter: baseCenter,
            LeftBase: baseCenter + (normal * headWidth),
            RightBase: baseCenter - (normal * headWidth),
            Direction: direction,
            Normal: normal);
    }

    private Point GetThicknessHandlePoint()
    {
        GetThicknessHandleFrame(out var center, out var normal);
        return center + (normal * (GetResolvedShaftThickness() * 0.5));
    }

    private void GetThicknessHandleFrame(out Point center, out Vector normal)
    {
        var points = GetSampledCenterlinePoints(trimmedForArrowHead: false, sampleCount: 32);
        if (points.Count < 2)
        {
            center = new Point((Start.X + End.X) / 2, (Start.Y + End.Y) / 2);
            normal = GetFallbackNormal();
            return;
        }

        var index = Math.Clamp((int)Math.Round((points.Count - 1) * 0.42), 1, points.Count - 2);
        center = points[index];
        normal = GetPolylineNormal(points, index);
    }

    private Point GetHeadLengthHandlePoint()
    {
        var basis = GetArrowHeadBasis(GetResolvedHeadLength());
        return basis.BaseCenter - (basis.Normal * (GetResolvedHeadWidth() + 14));
    }

    private Point GetHeadWidthHandlePoint()
    {
        var frame = GetArrowHeadFrame(GetResolvedHeadLength(), GetResolvedHeadWidth());
        return frame.LeftBase;
    }

    private Point GetOuterEdgeHandlePoint()
    {
        GetEdgeHandleFrame(progress: 0.46, out var center, out var normal);
        return center + (normal * GetResolvedOuterEdgeWidth());
    }

    private Point GetInnerEdgeHandlePoint()
    {
        GetEdgeHandleFrame(progress: 0.50, out var center, out var normal);
        return center - (normal * GetResolvedInnerEdgeWidth());
    }

    private Point GetTailSweepHandlePoint()
    {
        var direction = GetStartDirection();
        var normal = new Vector(-direction.Y, direction.X);
        return Start - (direction * (24 + (GetResolvedShaftThickness() * 2.5))) + (normal * GetResolvedTailSweep());
    }

    private Point GetHeadSkewHandlePoint()
    {
        var frame = GetArrowHeadFrame(GetResolvedHeadLength(), GetResolvedHeadWidth());
        return frame.BaseCenter;
    }

    private void GetEdgeHandleFrame(double progress, out Point center, out Vector normal)
    {
        var points = GetSampledCenterlinePoints(trimmedForArrowHead: true, sampleCount: 36);
        if (points.Count < 2)
        {
            center = new Point((Start.X + End.X) / 2, (Start.Y + End.Y) / 2);
            normal = GetFallbackNormal();
            return;
        }

        var index = Math.Clamp((int)Math.Round((points.Count - 1) * progress), 1, points.Count - 2);
        center = points[index];
        normal = GetPolylineNormal(points, index);
    }

    private (Point BaseCenter, Vector Direction, Vector Normal) GetArrowHeadBasis(double headLength)
    {
        var direction = GetArrowHeadDirection();
        if (direction.Length < 0.001)
        {
            direction = End - Start;
        }

        if (direction.Length < 0.001)
        {
            direction = new Vector(1, 0);
        }

        direction.Normalize();
        var normal = new Vector(-direction.Y, direction.X);
        return (End - (direction * headLength), direction, normal);
    }

    private Vector GetFallbackNormal()
    {
        var direction = End - Start;
        if (direction.Length < 0.001)
        {
            direction = new Vector(1, 0);
        }

        direction.Normalize();
        return new Vector(-direction.Y, direction.X);
    }

    private void SetShaftThicknessFromHandle(Point point)
    {
        GetThicknessHandleFrame(out var center, out var normal);
        var offset = Math.Abs(Vector.Multiply(point - center, normal));
        ShaftThickness = Math.Clamp(offset * 2, MinShaftThickness, MaxShaftThickness);
    }

    private void SetHeadLengthFromHandle(Point point)
    {
        var frame = GetArrowHeadFrame(GetResolvedHeadLength(), GetResolvedHeadWidth());
        var projectedLength = Vector.Multiply(End - point, frame.Direction);
        HeadLength = Math.Clamp(projectedLength, MinHeadLength, MaxHeadLength);
    }

    private void SetHeadWidthFromHandle(Point point)
    {
        var frame = GetArrowHeadFrame(GetResolvedHeadLength(), GetResolvedHeadWidth());
        var projectedWidth = Math.Abs(Vector.Multiply(point - frame.BaseCenter, frame.Normal));
        HeadWidth = Math.Clamp(projectedWidth, MinHeadWidth, MaxHeadWidth);
    }

    private void SetOuterEdgeFromHandle(Point point)
    {
        GetEdgeHandleFrame(progress: 0.46, out var center, out var normal);
        var projectedWidth = Vector.Multiply(point - center, normal) * 1.35;
        OuterEdgeWidth = Math.Clamp(projectedWidth, MinEdgeWidth, MaxEdgeWidth);
    }

    private void SetInnerEdgeFromHandle(Point point)
    {
        GetEdgeHandleFrame(progress: 0.50, out var center, out var normal);
        var projectedWidth = Vector.Multiply(center - point, normal) * 1.35;
        InnerEdgeWidth = Math.Clamp(projectedWidth, MinEdgeWidth, MaxEdgeWidth);
    }

    private void SetTailSweepFromHandle(Point point)
    {
        var direction = GetStartDirection();
        var normal = new Vector(-direction.Y, direction.X);
        var basePoint = Start - (direction * (24 + (GetResolvedShaftThickness() * 2.5)));
        var projectedSweep = Vector.Multiply(point - basePoint, normal) * 1.45;
        TailSweep = Math.Clamp(projectedSweep, MinTailSweep, MaxTailSweep);
    }

    private void SetHeadSkewFromHandle(Point point)
    {
        var basis = GetArrowHeadBasis(GetResolvedHeadLength());
        var projectedSkew = Vector.Multiply(point - basis.BaseCenter, basis.Normal) * 1.4;
        HeadSkew = Math.Clamp(projectedSkew, MinHeadSkew, MaxHeadSkew);
    }
}