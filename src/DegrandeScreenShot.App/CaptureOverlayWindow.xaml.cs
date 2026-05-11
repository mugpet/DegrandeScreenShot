using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using DegrandeScreenShot.App.Services;
using DegrandeScreenShot.Core;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using Forms = System.Windows.Forms;

namespace DegrandeScreenShot.App;

public partial class CaptureOverlayWindow : Window
{
    private const int HybridClickDragThreshold = 4;
    private const int HintPanelTopMargin = 28;
    private const int HintPanelMousePadding = 48;
    private const int MinimumSelectableWindowSize = 8;
    private const int MinimumSelectableRegionWidth = 48;
    private const int MinimumSelectableRegionHeight = 24;
    private const int MinimumSelectableRegionArea = 4_000;
    private readonly CaptureFrame _captureFrame;
    private readonly CaptureLaunchMode _launchMode;
    private readonly CaptureSelectionMode _selectionMode;
    private readonly ScreenCaptureService _screenCaptureService = new();
    private readonly SelectionSession _selectionSession = new();
    private BitmapSource? _capturedSelection;
    private PixelRect? _capturedSelectionBounds;
    private CaptureTargetSelection? _hoveredTarget;
    private CaptureTargetSelection? _pressedTarget;

    public CaptureOverlayWindow(
        CaptureFrame captureFrame,
        CaptureLaunchMode launchMode = CaptureLaunchMode.ChooseAction,
        CaptureSelectionMode selectionMode = CaptureSelectionMode.WindowOrRegion)
    {
        InitializeComponent();
        _captureFrame = captureFrame;
        _launchMode = launchMode;
        _selectionMode = selectionMode;
        Left = captureFrame.VirtualLeft;
        Top = captureFrame.VirtualTop;
        Width = captureFrame.Width;
        Height = captureFrame.Height;
        FrozenDesktopImage.Source = captureFrame.Bitmap;
        FrozenDesktopImage.Width = captureFrame.Width;
        FrozenDesktopImage.Height = captureFrame.Height;
        Canvas.SetLeft(FrozenDesktopImage, 0);
        Canvas.SetTop(FrozenDesktopImage, 0);

        Loaded += CaptureOverlayWindow_Loaded;
        PreviewKeyDown += CaptureOverlayWindow_PreviewKeyDown;
        KeyDown += CaptureOverlayWindow_KeyDown;
        MouseLeftButtonDown += CaptureOverlayWindow_MouseLeftButtonDown;
        MouseMove += CaptureOverlayWindow_MouseMove;
        MouseLeftButtonUp += CaptureOverlayWindow_MouseLeftButtonUp;
    }

    public CaptureResult? CaptureResult { get; private set; }

    public WindowSelection? SelectedWindow { get; private set; }

    public CaptureTargetSelection? SelectedScrollTarget { get; private set; }

    private void CaptureOverlayWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Left = _captureFrame.VirtualLeft;
        Top = _captureFrame.VirtualTop;
        Width = _captureFrame.Width;
        Height = _captureFrame.Height;
        OverlayCanvas.Width = _captureFrame.Width;
        OverlayCanvas.Height = _captureFrame.Height;
        Focus();
        Keyboard.Focus(this);
        ConfigureHintText();
        PositionHintPanel(ToPixelPoint(Mouse.GetPosition(OverlayCanvas)));
        ActionPanel.Visibility = _launchMode == CaptureLaunchMode.ChooseAction ? Visibility.Collapsed : Visibility.Collapsed;
        UpdateShadeRects(null);

        if (UsesWindowPicking)
        {
            UpdateWindowSelection(ToPixelPoint(Mouse.GetPosition(OverlayCanvas)));
        }
    }

    private void CaptureOverlayWindow_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Focus();
        ActionPanel.Visibility = Visibility.Collapsed;
        _capturedSelection = null;
        _capturedSelectionBounds = null;

        if (_selectionMode == CaptureSelectionMode.Window)
        {
            UpdateWindowSelection(ToPixelPoint(e.GetPosition(OverlayCanvas)));
            if (_hoveredTarget is not { Window: { } hoveredWindow })
            {
                return;
            }

            SelectedWindow = hoveredWindow;
            DialogResult = true;
            Close();
            e.Handled = true;
            return;
        }

        var point = ToPixelPoint(e.GetPosition(OverlayCanvas));
        if (_selectionMode is CaptureSelectionMode.WindowOrRegion or CaptureSelectionMode.ScrollTarget)
        {
            UpdateWindowSelection(point);
            _pressedTarget = _hoveredTarget;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && _selectionSession.Selection is { } selection && selection.Contains(point))
        {
            _selectionSession.BeginMove(point);
        }
        else
        {
            _selectionSession.BeginSelection(point);
        }

        CaptureMouse();
        UpdateShadeRects(_selectionSession.Selection);
        e.Handled = true;
    }

    private void CaptureOverlayWindow_MouseMove(object sender, MouseEventArgs e)
    {
        var pointer = ToPixelPoint(e.GetPosition(OverlayCanvas));
        if (_selectionMode == CaptureSelectionMode.Window)
        {
            UpdateWindowSelection(pointer);
            return;
        }

        if (_selectionSession.Interaction == SelectionInteraction.None)
        {
            if (_selectionMode is CaptureSelectionMode.WindowOrRegion or CaptureSelectionMode.ScrollTarget)
            {
                UpdateWindowSelection(pointer);
            }
            else
            {
                PositionHintPanel(pointer);
            }

            return;
        }

        var point = pointer;
        _selectionSession.TryToggleMoveWhileDragging(Keyboard.Modifiers.HasFlag(ModifierKeys.Control), point);

        if (_selectionSession.Interaction == SelectionInteraction.Selecting)
        {
            _selectionSession.UpdateSelection(point, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
        }
        else if (_selectionSession.Interaction == SelectionInteraction.Moving)
        {
            _selectionSession.UpdateMove(point);
        }

        UpdateShadeRects(_selectionSession.Selection);
        PositionHintPanel(pointer);
    }

    private void CaptureOverlayWindow_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_selectionMode == CaptureSelectionMode.Window)
        {
            return;
        }

        if (_selectionSession.Interaction == SelectionInteraction.None)
        {
            return;
        }

        ReleaseMouseCapture();
        _selectionSession.Complete();

        if (_selectionMode == CaptureSelectionMode.ScrollTarget)
        {
            SelectScrollTarget(e);
            return;
        }

        if (_selectionMode == CaptureSelectionMode.WindowOrRegion && IsHybridClickSelection())
        {
            var point = ToPixelPoint(e.GetPosition(OverlayCanvas));
            var targetSelection = _pressedTarget ?? GetTargetSelectionAt(point);
            _pressedTarget = null;
            _selectionSession.Clear();
            if (targetSelection is not null)
            {
                CaptureTarget(targetSelection);
                return;
            }
        }

        _pressedTarget = null;
        CaptureCurrentSelection();
        UpdateShadeRects(_selectionSession.Selection);

        if (_capturedSelection is null)
        {
            return;
        }

        if (_launchMode == CaptureLaunchMode.CopyToClipboard)
        {
            Finish(PostCaptureAction.Copy);
            return;
        }

        if (_launchMode == CaptureLaunchMode.OpenEditor)
        {
            Finish(PostCaptureAction.Edit);
            return;
        }

        PositionActionPanel();
    }

    private void CaptureOverlayWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CancelCapture();
            e.Handled = true;
        }
    }

    private void CaptureOverlayWindow_KeyDown(object sender, KeyEventArgs e)
    {
        if (_selectionMode == CaptureSelectionMode.Window)
        {
            return;
        }

        var moved = e.Key switch
        {
            Key.Left => _selectionSession.Nudge(-1, 0),
            Key.Right => _selectionSession.Nudge(1, 0),
            Key.Up => _selectionSession.Nudge(0, -1),
            Key.Down => _selectionSession.Nudge(0, 1),
            _ => false,
        };

        if (moved)
        {
            CaptureCurrentSelection();
            UpdateShadeRects(_selectionSession.Selection);
            PositionActionPanel();
            e.Handled = true;
        }
    }

    private void CancelCapture()
    {
        if (Mouse.Captured == this)
        {
            ReleaseMouseCapture();
        }

        _selectionSession.Clear();
        _capturedSelection = null;
        _capturedSelectionBounds = null;
        SelectedWindow = null;
        SelectedScrollTarget = null;
        _hoveredTarget = null;
        _pressedTarget = null;
        Close();
    }

    private bool IsHybridClickSelection()
    {
        if (_selectionSession.Selection is null)
        {
            return true;
        }

        return _selectionSession.Selection.Value.Width <= HybridClickDragThreshold
            && _selectionSession.Selection.Value.Height <= HybridClickDragThreshold;
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        Finish(PostCaptureAction.Copy);
    }

    private void EditButton_Click(object sender, RoutedEventArgs e)
    {
        Finish(PostCaptureAction.Edit);
    }

    private void Finish(PostCaptureAction action)
    {
        if (_capturedSelection is null)
        {
            return;
        }

        if (action == PostCaptureAction.Copy)
        {
            System.Windows.Clipboard.SetImage(_capturedSelection);
        }

        var preferredEditorSize = _selectionSession.Selection is { } selection && selection.HasArea
            ? new Size(selection.Width, selection.Height)
            : (Size?)null;
        CaptureResult = new CaptureResult(action, _capturedSelection);
        if (IsVisible)
        {
            DialogResult = true;
        }

        Close();
    }

    private void CaptureCurrentSelection()
    {
        if (_selectionSession.Selection is not { } selection || !selection.HasArea)
        {
            _capturedSelection = null;
            _capturedSelectionBounds = null;
            return;
        }

        _capturedSelection = Crop(selection);
        _capturedSelectionBounds = selection;
    }

    private void CaptureTarget(CaptureTargetSelection targetSelection)
    {
        Hide();
        _capturedSelection = targetSelection.ElementBounds is { } elementBounds
            ? _screenCaptureService.CaptureVisibleWindowRegion(targetSelection.Window.Handle, ToScreenRectangle(elementBounds))
            : _screenCaptureService.CaptureVisibleWindow(targetSelection.Window.Handle);
        _capturedSelectionBounds = targetSelection.Bounds;

        if (_launchMode == CaptureLaunchMode.CopyToClipboard)
        {
            Finish(PostCaptureAction.Copy);
            return;
        }

        if (_launchMode == CaptureLaunchMode.OpenEditor)
        {
            Finish(PostCaptureAction.Edit);
            return;
        }

        Show();
        Activate();
        Focus();
        Keyboard.Focus(this);
        UpdateShadeRects(targetSelection.Bounds);
        PositionActionPanel();
    }

    private void SelectScrollTarget(MouseButtonEventArgs e)
    {
        CaptureTargetSelection? targetSelection;
        if (IsHybridClickSelection())
        {
            var point = ToPixelPoint(e.GetPosition(OverlayCanvas));
            targetSelection = _pressedTarget ?? GetTargetSelectionAt(point);
            _selectionSession.Clear();
        }
        else
        {
            targetSelection = CreateScrollTargetFromSelection(_selectionSession.Selection);
        }

        _pressedTarget = null;
        if (targetSelection is null)
        {
            _selectionSession.Clear();
            UpdateShadeRects(null);
            return;
        }

        SelectedScrollTarget = targetSelection;
        DialogResult = true;
        Close();
    }

    private CaptureTargetSelection? CreateScrollTargetFromSelection(PixelRect? selection)
    {
        if (selection is not { HasArea: true } selectedBounds)
        {
            return null;
        }

        var centerPoint = new PixelPoint(
            selectedBounds.Left + (selectedBounds.Width / 2),
            selectedBounds.Top + (selectedBounds.Height / 2));
        var windowSelection = GetWindowSelectionAt(centerPoint) ?? _pressedTarget?.Window;
        if (windowSelection is null)
        {
            return null;
        }

        var clippedBounds = ClipToWindow(selectedBounds, windowSelection.Bounds);
        if (clippedBounds is not { HasArea: true })
        {
            return null;
        }

        return new CaptureTargetSelection(windowSelection, clippedBounds.Value, clippedBounds.Value);
    }

    private BitmapSource Crop(PixelRect selection)
    {
        var croppedBitmap = new CroppedBitmap(
            _captureFrame.Bitmap,
            new Int32Rect(selection.X, selection.Y, selection.Width, selection.Height));
        croppedBitmap.Freeze();
        return croppedBitmap;
    }

    private void PositionActionPanel()
    {
        if (_selectionMode == CaptureSelectionMode.Window)
        {
            ActionPanel.Visibility = Visibility.Collapsed;
            return;
        }

        var selection = _capturedSelectionBounds ?? _selectionSession.Selection;
        if (selection is not { HasArea: true })
        {
            ActionPanel.Visibility = Visibility.Collapsed;
            return;
        }

        ActionPanel.Visibility = Visibility.Visible;
        var panelLeft = Math.Min(selection.Value.Right - ActionPanel.Width, _captureFrame.Width - ActionPanel.Width - 24);
        panelLeft = Math.Max(24, panelLeft);

        var desiredTop = selection.Value.Bottom + 16;
        var panelTop = desiredTop + 120 < _captureFrame.Height ? desiredTop : Math.Max(24, selection.Value.Top - 104);

        Canvas.SetLeft(ActionPanel, panelLeft);
        Canvas.SetTop(ActionPanel, panelTop);
    }

    private void ConfigureHintText()
    {
        if (_selectionMode == CaptureSelectionMode.Window)
        {
            HintTitleText.Text = "Select a window";
            HintDescriptionText.Text = "Move over a visible window and click to capture it. Esc cancels.";
            return;
        }

        if (_selectionMode == CaptureSelectionMode.WindowOrRegion)
        {
            HintTitleText.Text = "Select a window or region";
            HintDescriptionText.Text = "Click a visible window or pane, or drag to select a custom region. Hold Shift for 1:1, Ctrl to move the frame, Esc cancels.";
            return;
        }

        if (_selectionMode == CaptureSelectionMode.ScrollTarget)
        {
            HintTitleText.Text = "Select scroll area";
            HintDescriptionText.Text = "Click a visible window or pane, or drag around the scrollable content area to stitch only that area. Esc cancels.";
            return;
        }

        HintTitleText.Text = "Select a region";
        HintDescriptionText.Text = "Drag to select. Hold Shift for 1:1, Ctrl to move the frame, arrows to nudge 1 px, Esc to cancel.";
    }

    private bool UsesWindowPicking => _selectionMode is CaptureSelectionMode.Window or CaptureSelectionMode.WindowOrRegion or CaptureSelectionMode.ScrollTarget;

    private void UpdateWindowSelection(PixelPoint overlayPoint)
    {
        _hoveredTarget = GetTargetSelectionAt(overlayPoint);
        UpdateShadeRects(_hoveredTarget?.Bounds);
        PositionHintPanel(overlayPoint);
    }

    private void PositionHintPanel(PixelPoint pointer)
    {
        HintPanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var panelWidth = HintPanel.ActualWidth > 0 ? HintPanel.ActualWidth : HintPanel.DesiredSize.Width;
        var panelHeight = HintPanel.ActualHeight > 0 ? HintPanel.ActualHeight : HintPanel.DesiredSize.Height;
        var monitorBounds = GetMonitorBounds(pointer);
        var panelLeft = Clamp(monitorBounds.Left + ((monitorBounds.Width - panelWidth) / 2), monitorBounds.Left, monitorBounds.Right - panelWidth);
        var panelTop = Clamp(monitorBounds.Top + HintPanelTopMargin, monitorBounds.Top, monitorBounds.Bottom - panelHeight);
        var topPanelBounds = new Rect(panelLeft, panelTop, panelWidth, panelHeight);
        topPanelBounds.Inflate(HintPanelMousePadding, HintPanelMousePadding);

        if (topPanelBounds.Contains(new Point(pointer.X, pointer.Y)))
        {
            panelTop = Clamp(monitorBounds.Top + ((monitorBounds.Height - panelHeight) / 2), monitorBounds.Top, monitorBounds.Bottom - panelHeight);
        }

        Canvas.SetLeft(HintPanel, panelLeft);
        Canvas.SetTop(HintPanel, panelTop);
    }

    private Rect GetMonitorBounds(PixelPoint pointer)
    {
        var screenPoint = new System.Drawing.Point(pointer.X + _captureFrame.VirtualLeft, pointer.Y + _captureFrame.VirtualTop);
        var screenBounds = Forms.Screen.FromPoint(screenPoint).Bounds;
        var left = screenBounds.Left - _captureFrame.VirtualLeft;
        var top = screenBounds.Top - _captureFrame.VirtualTop;
        var right = Math.Min(_captureFrame.Width, left + screenBounds.Width);
        var bottom = Math.Min(_captureFrame.Height, top + screenBounds.Height);

        left = Math.Max(0, left);
        top = Math.Max(0, top);
        if (right <= left || bottom <= top)
        {
            return new Rect(0, 0, _captureFrame.Width, _captureFrame.Height);
        }

        return new Rect(left, top, right - left, bottom - top);
    }

    private static double Clamp(double value, double min, double max)
    {
        return max < min ? min : Math.Clamp(value, min, max);
    }

    private CaptureTargetSelection? GetTargetSelectionAt(PixelPoint overlayPoint)
    {
        var windowSelection = GetWindowSelectionAt(overlayPoint);
        if (windowSelection is null)
        {
            return null;
        }

        if (_selectionMode == CaptureSelectionMode.Window)
        {
            return new CaptureTargetSelection(windowSelection, windowSelection.Bounds, null);
        }

        var screenPoint = new NativePoint(overlayPoint.X + _captureFrame.VirtualLeft, overlayPoint.Y + _captureFrame.VirtualTop);
        var regionBounds = TryGetLogicalRegionBounds(windowSelection.Handle, screenPoint, windowSelection.Bounds);
        return regionBounds is { HasArea: true }
            ? new CaptureTargetSelection(windowSelection, regionBounds.Value, regionBounds.Value)
            : new CaptureTargetSelection(windowSelection, windowSelection.Bounds, null);
    }

    private WindowSelection? GetWindowSelectionAt(PixelPoint overlayPoint)
    {
        var screenPoint = new NativePoint(overlayPoint.X + _captureFrame.VirtualLeft, overlayPoint.Y + _captureFrame.VirtualTop);
        var overlayHandle = new WindowInteropHelper(this).Handle;

        for (var handle = GetTopWindow(IntPtr.Zero); handle != IntPtr.Zero; handle = GetWindow(handle, GwHwndNext))
        {
            if (handle == overlayHandle
                || !IsWindowVisible(handle)
                || IsIconic(handle)
                || IsWindowCloaked(handle)
                || IsWindowFullyTransparent(handle)
                || !TryGetVisibleWindowRect(handle, out var rect))
            {
                continue;
            }

            if (screenPoint.X < rect.Left || screenPoint.X >= rect.Right || screenPoint.Y < rect.Top || screenPoint.Y >= rect.Bottom)
            {
                continue;
            }

            var bounds = ToVisibleOverlayBounds(rect);
            if (bounds is not { HasArea: true }
                || bounds.Value.Width < MinimumSelectableWindowSize
                || bounds.Value.Height < MinimumSelectableWindowSize)
            {
                continue;
            }

            return new WindowSelection(handle, bounds.Value);
        }

        return null;
    }

    private PixelRect? TryGetLogicalRegionBounds(IntPtr windowHandle, NativePoint screenPoint, PixelRect windowBounds)
    {
        try
        {
            var rootElement = AutomationElement.FromHandle(windowHandle);
            if (rootElement is null)
            {
                return null;
            }

            var walker = TreeWalker.ControlViewWalker;
            LogicalRegionCandidate? bestCandidate = null;
            FindLogicalRegionBounds(rootElement, screenPoint, windowBounds, walker, ref bestCandidate, depth: 0);
            return bestCandidate?.Bounds;
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (COMException)
        {
            return null;
        }
    }

    private void FindLogicalRegionBounds(
        AutomationElement element,
        NativePoint screenPoint,
        PixelRect windowBounds,
        TreeWalker walker,
        ref LogicalRegionCandidate? bestCandidate,
        int depth)
    {
        const int maxDepth = 9;
        if (depth > maxDepth)
        {
            return;
        }

        for (var child = walker.GetFirstChild(element); child is not null; child = walker.GetNextSibling(child))
        {
            AutomationElement.AutomationElementInformation current;
            PixelRect? childBounds;
            try
            {
                current = child.Current;
                if (current.IsOffscreen)
                {
                    continue;
                }

                childBounds = ToOverlayRect(current.BoundingRectangle);
            }
            catch (ElementNotAvailableException)
            {
                continue;
            }
            catch (InvalidOperationException)
            {
                continue;
            }
            catch (COMException)
            {
                continue;
            }

            if (childBounds is not { HasArea: true } bounds || !bounds.Contains(ToOverlayPoint(screenPoint)))
            {
                continue;
            }

            var clippedBounds = ClipToWindow(bounds, windowBounds);
            if (clippedBounds is not { HasArea: true })
            {
                continue;
            }

            var priority = GetLogicalRegionPriority(current.ControlType);
            if (priority > 0 && IsSelectableRegionBounds(clippedBounds.Value, windowBounds))
            {
                var area = (long)clippedBounds.Value.Width * clippedBounds.Value.Height;
                var candidate = new LogicalRegionCandidate(clippedBounds.Value, priority, area, depth);
                if (IsBetterLogicalRegion(candidate, bestCandidate))
                {
                    bestCandidate = candidate;
                }
            }

            FindLogicalRegionBounds(child, screenPoint, windowBounds, walker, ref bestCandidate, depth + 1);
        }
    }

    private static bool IsSelectableRegionBounds(PixelRect bounds, PixelRect windowBounds)
    {
        if (bounds.Width < MinimumSelectableRegionWidth
            || bounds.Height < MinimumSelectableRegionHeight
            || (long)bounds.Width * bounds.Height < MinimumSelectableRegionArea)
        {
            return false;
        }

        return bounds.Width < windowBounds.Width - 8 || bounds.Height < windowBounds.Height - 8;
    }

    private static bool IsBetterLogicalRegion(LogicalRegionCandidate candidate, LogicalRegionCandidate? currentBest)
    {
        if (currentBest is null)
        {
            return true;
        }

        if (candidate.Priority != currentBest.Priority)
        {
            return candidate.Priority > currentBest.Priority;
        }

        if (candidate.Area != currentBest.Area)
        {
            return candidate.Area < currentBest.Area;
        }

        return candidate.Depth > currentBest.Depth;
    }

    private static int GetLogicalRegionPriority(ControlType controlType)
    {
        if (controlType == ControlType.List
            || controlType == ControlType.Tree
            || controlType == ControlType.DataGrid
            || controlType == ControlType.Table
            || controlType == ControlType.Document)
        {
            return 700;
        }

        if (controlType == ControlType.Pane
            || controlType == ControlType.Group
            || controlType == ControlType.Custom)
        {
            return 550;
        }

        if (controlType == ControlType.ToolBar
            || controlType == ControlType.MenuBar
            || controlType == ControlType.Tab)
        {
            return 425;
        }

        return 0;
    }

    private PixelRect? ToOverlayRect(Rect rect)
    {
        if (rect.IsEmpty || double.IsInfinity(rect.Width) || double.IsInfinity(rect.Height) || rect.Width <= 0 || rect.Height <= 0)
        {
            return null;
        }

        var left = (int)Math.Floor(rect.Left) - _captureFrame.VirtualLeft;
        var top = (int)Math.Floor(rect.Top) - _captureFrame.VirtualTop;
        var right = (int)Math.Ceiling(rect.Right) - _captureFrame.VirtualLeft;
        var bottom = (int)Math.Ceiling(rect.Bottom) - _captureFrame.VirtualTop;
        return new PixelRect(left, top, right - left, bottom - top);
    }

    private PixelRect? ClipToWindow(PixelRect bounds, PixelRect windowBounds)
    {
        var left = Math.Max(bounds.Left, windowBounds.Left);
        var top = Math.Max(bounds.Top, windowBounds.Top);
        var right = Math.Min(bounds.Right, windowBounds.Right);
        var bottom = Math.Min(bounds.Bottom, windowBounds.Bottom);
        if (right <= left || bottom <= top)
        {
            return null;
        }

        return new PixelRect(left, top, right - left, bottom - top);
    }

    private PixelPoint ToOverlayPoint(NativePoint screenPoint)
    {
        return new PixelPoint(screenPoint.X - _captureFrame.VirtualLeft, screenPoint.Y - _captureFrame.VirtualTop);
    }

    private System.Drawing.Rectangle ToScreenRectangle(PixelRect overlayRect)
    {
        return new System.Drawing.Rectangle(
            overlayRect.X + _captureFrame.VirtualLeft,
            overlayRect.Y + _captureFrame.VirtualTop,
            overlayRect.Width,
            overlayRect.Height);
    }

    private PixelRect? ToVisibleOverlayBounds(NativeRect rect)
    {
        var left = Math.Max(0, rect.Left - _captureFrame.VirtualLeft);
        var top = Math.Max(0, rect.Top - _captureFrame.VirtualTop);
        var right = Math.Min(_captureFrame.Width, rect.Right - _captureFrame.VirtualLeft);
        var bottom = Math.Min(_captureFrame.Height, rect.Bottom - _captureFrame.VirtualTop);
        if (right <= left || bottom <= top)
        {
            return null;
        }

        return new PixelRect(left, top, right - left, bottom - top);
    }

    private static bool TryGetVisibleWindowRect(IntPtr handle, out NativeRect rect)
    {
        if (DwmGetWindowAttribute(handle, DwmWindowAttributeExtendedFrameBounds, out rect, Marshal.SizeOf<NativeRect>()) == 0
            && rect.Right > rect.Left
            && rect.Bottom > rect.Top)
        {
            return true;
        }

        return GetWindowRect(handle, out rect);
    }

    private static bool IsWindowCloaked(IntPtr handle)
    {
        return DwmGetWindowAttribute(handle, DwmWindowAttributeCloaked, out int cloaked, Marshal.SizeOf<int>()) == 0
            && cloaked != 0;
    }

    private static bool IsWindowFullyTransparent(IntPtr handle)
    {
        var extendedStyle = GetWindowLong(handle, GwlExStyle);
        if ((extendedStyle & WsExLayered) == 0)
        {
            return false;
        }

        return GetLayeredWindowAttributes(handle, out _, out var alpha, out var flags)
            && (flags & LwaAlpha) != 0
            && alpha == 0;
    }

    private void UpdateShadeRects(PixelRect? selection)
    {
        var width = _captureFrame.Width;
        var height = _captureFrame.Height;
        if (selection is null || !selection.Value.HasArea)
        {
            SetRect(TopShade, 0, 0, width, height);
            SetRect(LeftShade, 0, 0, 0, 0);
            SetRect(RightShade, 0, 0, 0, 0);
            SetRect(BottomShade, 0, 0, 0, 0);
            SelectionBorder.Visibility = Visibility.Collapsed;
            SelectionInfoPanel.Visibility = Visibility.Collapsed;
            return;
        }

        var rect = selection.Value;
        SetRect(TopShade, 0, 0, width, rect.Top);
        SetRect(LeftShade, 0, rect.Top, rect.Left, rect.Height);
        SetRect(RightShade, rect.Right, rect.Top, width - rect.Right, rect.Height);
        SetRect(BottomShade, 0, rect.Bottom, width, height - rect.Bottom);
        SetRect(SelectionBorder, rect.Left, rect.Top, rect.Width, rect.Height);
        SelectionInfoText.Text = $"{rect.Width} x {rect.Height}";
        SelectionInfoPanel.Visibility = Visibility.Visible;
        SelectionInfoPanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var labelWidth = SelectionInfoPanel.DesiredSize.Width;
        var labelHeight = SelectionInfoPanel.DesiredSize.Height;
        var labelLeft = Math.Max(24, Math.Min(rect.Left, width - labelWidth - 24));
        var labelTop = rect.Top >= labelHeight + 24
            ? rect.Top - labelHeight - 10
            : Math.Min(height - labelHeight - 24, rect.Bottom + 10);
        Canvas.SetLeft(SelectionInfoPanel, labelLeft);
        Canvas.SetTop(SelectionInfoPanel, Math.Max(24, labelTop));
        SelectionBorder.Visibility = Visibility.Visible;
    }

    private static void SetRect(FrameworkElement element, double left, double top, double width, double height)
    {
        Canvas.SetLeft(element, Math.Max(0, left));
        Canvas.SetTop(element, Math.Max(0, top));
        element.Width = Math.Max(0, width);
        element.Height = Math.Max(0, height);
    }

    private static PixelPoint ToPixelPoint(Point point)
    {
        return new PixelPoint((int)Math.Round(point.X), (int)Math.Round(point.Y));
    }

    private const uint GwHwndNext = 2;
    private const int GwlExStyle = -20;
    private const int WsExLayered = 0x00080000;
    private const uint LwaAlpha = 0x00000002;
    private const int DwmWindowAttributeExtendedFrameBounds = 9;
    private const int DwmWindowAttributeCloaked = 14;

    [DllImport("user32.dll")]
    private static extern IntPtr GetTopWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLayeredWindowAttributes(IntPtr hwnd, out uint colorKey, out byte alpha, out uint flags);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out NativeRect pvAttribute, int cbAttribute);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativePoint(int x, int y)
    {
        public int X { get; } = x;

        public int Y { get; } = y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private sealed record LogicalRegionCandidate(PixelRect Bounds, int Priority, long Area, int Depth);
}

public enum CaptureLaunchMode
{
    ChooseAction,
    CopyToClipboard,
    OpenEditor,
}

public enum CaptureSelectionMode
{
    Region,
    WindowOrRegion,
    Window,
    ScrollTarget,
}

public sealed record WindowSelection(IntPtr Handle, PixelRect Bounds);

public sealed record CaptureTargetSelection(WindowSelection Window, PixelRect Bounds, PixelRect? ElementBounds);