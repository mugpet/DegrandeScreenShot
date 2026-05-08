using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using DegrandeScreenShot.App.Services;
using DegrandeScreenShot.Core;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;

namespace DegrandeScreenShot.App;

public partial class CaptureOverlayWindow : Window
{
    private readonly CaptureFrame _captureFrame;
    private readonly CaptureLaunchMode _launchMode;
    private readonly SelectionSession _selectionSession = new();
    private BitmapSource? _capturedSelection;

    public CaptureOverlayWindow(CaptureFrame captureFrame, CaptureLaunchMode launchMode = CaptureLaunchMode.ChooseAction)
    {
        InitializeComponent();
        _captureFrame = captureFrame;
        _launchMode = launchMode;
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
        KeyDown += CaptureOverlayWindow_KeyDown;
        MouseLeftButtonDown += CaptureOverlayWindow_MouseLeftButtonDown;
        MouseMove += CaptureOverlayWindow_MouseMove;
        MouseLeftButtonUp += CaptureOverlayWindow_MouseLeftButtonUp;
    }

    public CaptureResult? CaptureResult { get; private set; }

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
        Canvas.SetLeft(HintPanel, 28);
        Canvas.SetTop(HintPanel, 28);
        ActionPanel.Visibility = _launchMode == CaptureLaunchMode.ChooseAction ? Visibility.Collapsed : Visibility.Collapsed;
        UpdateShadeRects(null);
    }

    private void CaptureOverlayWindow_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Focus();
        ActionPanel.Visibility = Visibility.Collapsed;
        _capturedSelection = null;

        var point = ToPixelPoint(e.GetPosition(OverlayCanvas));
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
        if (_selectionSession.Interaction == SelectionInteraction.None)
        {
            return;
        }

        var point = ToPixelPoint(e.GetPosition(OverlayCanvas));
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
    }

    private void CaptureOverlayWindow_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_selectionSession.Interaction == SelectionInteraction.None)
        {
            return;
        }

        ReleaseMouseCapture();
        _selectionSession.Complete();
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

    private void CaptureOverlayWindow_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
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
        DialogResult = true;
        Close();
    }

    private void CaptureCurrentSelection()
    {
        if (_selectionSession.Selection is not { } selection || !selection.HasArea)
        {
            _capturedSelection = null;
            return;
        }

        _capturedSelection = Crop(selection);
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
        if (_selectionSession.Selection is not { } selection || !selection.HasArea)
        {
            ActionPanel.Visibility = Visibility.Collapsed;
            return;
        }

        ActionPanel.Visibility = Visibility.Visible;
        var panelLeft = Math.Min(selection.Right - ActionPanel.Width, _captureFrame.Width - ActionPanel.Width - 24);
        panelLeft = Math.Max(24, panelLeft);

        var desiredTop = selection.Bottom + 16;
        var panelTop = desiredTop + 120 < _captureFrame.Height ? desiredTop : Math.Max(24, selection.Top - 104);

        Canvas.SetLeft(ActionPanel, panelLeft);
        Canvas.SetTop(ActionPanel, panelTop);
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
            SelectionFill.Visibility = Visibility.Collapsed;
            SelectionBorder.Visibility = Visibility.Collapsed;
            SelectionInfoPanel.Visibility = Visibility.Collapsed;
            return;
        }

        var rect = selection.Value;
        SetRect(TopShade, 0, 0, width, rect.Top);
        SetRect(LeftShade, 0, rect.Top, rect.Left, rect.Height);
        SetRect(RightShade, rect.Right, rect.Top, width - rect.Right, rect.Height);
        SetRect(BottomShade, 0, rect.Bottom, width, height - rect.Bottom);
        SetRect(SelectionFill, rect.Left, rect.Top, rect.Width, rect.Height);
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
        SelectionFill.Visibility = Visibility.Visible;
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
}

public enum CaptureLaunchMode
{
    ChooseAction,
    CopyToClipboard,
    OpenEditor,
}