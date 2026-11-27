using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DegrandeScreenShot.App;

namespace DegrandeScreenShot.Tests;

// The WPF BAML reader shares a process-wide schema cache across these STA windows.
[Collection("Editor windows")]
public sealed class EditorWorkspaceLayoutTests
{
    [Theory]
    [InlineData(956, 700, false)]
    [InlineData(956, 700, true)]
    [InlineData(650, 540, false)]
    [InlineData(650, 540, true)]
    public void SelectionAndToolChangesLeaveTheImageInPlace(int width, int height, bool zoomAndPan)
    {
        WithEditor(width, height, window =>
        {
            Assert.True(window.DocumentOverviewPanel.IsVisible);
            Assert.Equal("Document", window.InspectorHeading.Text);
            const BindingFlags access = BindingFlags.Instance | BindingFlags.NonPublic;
            var annotations = (List<AnnotationBase>)typeof(EditorWindow).GetField("_annotations", access)!.GetValue(window)!;
            var arrow = ArrowAnnotation.Create(new Point(640, 470));
            arrow.UpdateFromAnchor(new Point(640, 470), new Point(740, 510), false);
            annotations.Add(arrow);
            annotations.Add(new RectangleAnnotation { Bounds = new Rect(755, 475, 85, 60) });
            var arrowPoint = new Point(690, 490);
            var framePoint = new Point(775, 500);
            var blankPoint = new Point(720, 550);
            var viewer = window.ArtworkScrollViewer;
            Click(window.ZoomFitButton);
            Layout(window);
            if (zoomAndPan)
            {
                for (int i = 0; i < 20 && (viewer.ScrollableWidth < 100 || viewer.ScrollableHeight < 100); i++)
                {
                    ZoomWheel(window, 120);
                }
                viewer.ScrollToHorizontalOffset(viewer.ScrollableWidth * 0.42);
                viewer.ScrollToVerticalOffset(viewer.ScrollableHeight * 0.58);
                Layout(window);
                Assert.True(viewer.HorizontalOffset > 0 && viewer.VerticalOffset > 0);
            }

            var openView = View();
            foreach (var (point, title) in new[] { (arrowPoint, "Arrow"), (arrowPoint, "Arrow"), (framePoint, "Frame"), (blankPoint, "Document") })
            {
                SelectAt(point);
                Assert.Equal(openView, View());
                Assert.True(window.InspectorDock.IsVisible);
                Assert.Equal(title, window.InspectorHeading.Text);
            }
            Assert.True(window.DocumentOverviewPanel.IsVisible);

            window.ArrowToolButton.IsChecked = true;
            Layout(window);
            Assert.True(window.ArrowOptionsPanel.IsVisible);
            Assert.Equal(openView, View());
            window.SelectToolButton.IsChecked = true;
            Layout(window);
            Assert.Equal(openView, View());

            Click(window.InspectorBackButton);
            Layout(window);
            Assert.False(window.InspectorDock.IsVisible);
            var hiddenView = View();
            SelectAt(arrowPoint);
            Assert.False(window.InspectorDock.IsVisible);
            Assert.Equal(hiddenView, View());
            SelectAt(blankPoint);
            Assert.Equal(hiddenView, View());
            window.HighlightToolButton.IsChecked = true;
            Click(window.HighlightToolButton);
            Layout(window);
            Assert.False(window.InspectorDock.IsVisible);
            Assert.Equal(hiddenView, View());

            foreach (var (command, page) in new[] { (window.ResizeImageButton, window.ResizePanel), (window.AddImageFromUrlButton, window.ImageUrlPanel) })
            {
                Click(command);
                Layout(window);
                Assert.True(page.IsVisible);
                Click(window.InspectorBackButton);
                Layout(window);
                Assert.False(window.InspectorDock.IsVisible);
                Assert.Equal(hiddenView.Viewport, BoundsInWindow(window, viewer));
            }
            window.SelectToolButton.IsChecked = true;
            Layout(window);
            Assert.False(window.InspectorDock.IsVisible);
            window.InspectorToggle.IsChecked = true;
            Click(window.InspectorToggle);
            Layout(window);
            Assert.True(window.InspectorDock.IsVisible);
            Assert.Equal("Document", window.InspectorHeading.Text);

            (Rect Viewport, double ScaleX, double ScaleY, Point LandmarkOnScreen, double HorizontalOffset, double VerticalOffset) View() =>
                (BoundsInWindow(window, viewer), window.ArtworkZoomTransform.ScaleX, window.ArtworkZoomTransform.ScaleY,
                    window.BaseImage.PointToScreen(arrowPoint), viewer.HorizontalOffset, viewer.VerticalOffset);

            void SelectAt(Point point)
            {
                typeof(EditorWindow).GetMethod("SelectAnnotationAt", access)!.Invoke(window, [point]);
                typeof(EditorWindow).GetMethod("RefreshCanvas", access)!.Invoke(window, null);
                Layout(window);
            }
        }, imageWidth: 1500, imageHeight: 1000);
    }

    private static void Click(ButtonBase button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));

    private static void ZoomWheel(EditorWindow window, int delta)
    {
        var wheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta) { RoutedEvent = Mouse.PreviewMouseWheelEvent };
        window.ArtworkScrollViewer.RaiseEvent(wheel);
        Assert.True(wheel.Handled);
        Layout(window);
    }

    [Fact]
    public void WheelZoomStartsAtTheFitScaleOfAVeryTallScreenshot()
    {
        WithEditor(650, 540, window =>
        {
            void Fit()
            {
                window.ZoomFitButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, window.ZoomFitButton));
                Layout(window);
            }
            Fit();
            var fit = window.ArtworkZoomTransform.ScaleY;
            Assert.InRange(fit * VisualTreeHelper.GetDpi(window).DpiScaleY, 0.0001, 0.0499);
            ZoomWheel(window, -120);
            Assert.True(window.ArtworkZoomTransform.ScaleY <= fit);

            Fit();
            ZoomWheel(window, 120);
            Assert.Equal(1.15, window.ArtworkZoomTransform.ScaleY / fit, precision: 5);
        }, imageWidth: 16, imageHeight: 96_000);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EscapeDiscardsDimensionInputBeforeClosingResizeOptions(bool resizeImportedImage)
    {
        WithEditor(956, 650, window =>
        {
            var bitmap = Assert.IsAssignableFrom<BitmapSource>(window.BaseImage.Source);
            ImageAnnotation? imported = null;
            if (resizeImportedImage)
            {
                const BindingFlags access = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(EditorWindow).GetMethod("AddImportedImage", access)!.Invoke(window, [bitmap, new Point(160, 100), "test"]);
                imported = Assert.IsType<ImageAnnotation>(typeof(EditorWindow).GetField("_selectedAnnotation", access)!.GetValue(window));
            }
            var originalBounds = imported?.Bounds;
            window.ResizeImageButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, window.ResizeImageButton));
            Layout(window);
            var field = resizeImportedImage ? window.ImageHeightTextBox : window.ImageWidthTextBox;
            var originalValue = field.Text;
            Assert.True(field.Focus());
            field.Text = "777";

            PressEscape();
            Assert.True(window.ResizePanel.IsVisible);
            Assert.Equal(originalValue, field.Text);
            Assert.Same(window.InspectorBackButton, Keyboard.FocusedElement);

            PressEscape();
            Assert.False(window.ResizePanel.IsVisible);
            Assert.Equal(originalBounds, imported?.Bounds);
            var result = Assert.IsAssignableFrom<BitmapSource>(window.BaseImage.Source);
            Assert.Equal(bitmap.PixelWidth, result.PixelWidth);
            Assert.Equal(bitmap.PixelHeight, result.PixelHeight);

            void PressEscape()
            {
                var source = Assert.IsAssignableFrom<UIElement>(Keyboard.FocusedElement);
                var key = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), Environment.TickCount, Key.Escape)
                {
                    RoutedEvent = Keyboard.PreviewKeyDownEvent,
                };
                source.RaiseEvent(key);
                Assert.True(key.Handled);
                Layout(window);
            }
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CanvasClickCommitsWidthOnceToTheOriginalResizeTarget(bool importedTarget, bool pressEnterFirst)
    {
        WithEditor(956, 650, window =>
        {
            const BindingFlags access = BindingFlags.Instance | BindingFlags.NonPublic;
            var editor = typeof(EditorWindow);
            var selected = editor.GetField("_selectedAnnotation", access)!;
            var baseImage = Assert.IsAssignableFrom<BitmapSource>(window.BaseImage.Source);
            ImageAnnotation? imported = null;
            if (importedTarget)
            {
                var smallImage = new CroppedBitmap(baseImage, new Int32Rect(0, 0, 60, 40));
                editor.GetMethod("AddImportedImage", access)!.Invoke(window, [smallImage, new Point(160, 100), "test"]);
                editor.GetMethod("CommitHistoryState", access)!.Invoke(window, null);
                editor.GetMethod("RefreshCanvas", access)!.Invoke(window, null);
                imported = Assert.IsType<ImageAnnotation>(selected.GetValue(window));
            }

            Click(window.ResizeImageButton);
            Layout(window);
            var history = Assert.IsAssignableFrom<System.Collections.ICollection>(editor.GetField("_undoHistory", access)!.GetValue(window));
            int before = history.Count;
            var field = window.ImageWidthTextBox;
            Assert.Equal(importedTarget ? "60" : "320", field.Text);
            Assert.True(field.Focus());
            field.Text = "99";
            if (pressEnterFirst)
            {
                field.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), Environment.TickCount, Key.Enter)
                {
                    RoutedEvent = Keyboard.PreviewKeyDownEvent,
                });
                Assert.Equal(before + 1, history.Count);
            }

            // The offscreen window places the pointer over blank canvas. Route the normal
            // bubbling events so the ScrollViewer also receives the mouse focus handoff.
            foreach (var routedEvent in new[] { Mouse.MouseDownEvent, Mouse.MouseUpEvent })
            {
                window.AnnotationCanvas.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                {
                    RoutedEvent = routedEvent,
                });
            }
            Layout(window);

            Assert.Null(selected.GetValue(window));
            Assert.False(field.IsKeyboardFocused);
            Assert.Equal(before + 1, history.Count);
            var result = Assert.IsAssignableFrom<BitmapSource>(window.BaseImage.Source);
            Assert.Equal(importedTarget ? 320 : 99, result.PixelWidth);
            if (imported is not null)
            {
                Assert.Equal(new Size(99, 66), imported.Bounds.Size);
                Assert.Same(baseImage, result);
            }
            Click(window.UndoHistoryButton);
            Assert.Equal(before, history.Count);
            Assert.Equal(320, Assert.IsAssignableFrom<BitmapSource>(window.BaseImage.Source).PixelWidth);
            if (importedTarget)
            {
                var annotations = (List<AnnotationBase>)editor.GetField("_annotations", access)!.GetValue(window)!;
                Assert.Equal(new Size(60, 40), Assert.IsType<ImageAnnotation>(Assert.Single(annotations)).Bounds.Size);
            }
        });
    }

    [Theory]
    [InlineData(956, 900, true)]
    [InlineData(650, 540, false)]
    [InlineData(620, 420, false)]
    public void ArrowSettingsStayOutsideTheCanvasAndCanBeReachedAtBothDockPositions(int width, int height, bool dockOnRight)
    {
        WithEditor(width, height, window =>
        {
            window.ArrowToolButton.IsChecked = true;
            Layout(window);
            AssertDockedWorkspace(window, dockOnRight);
            window.ZoomFitButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, window.ZoomFitButton));
            AssertArtworkFits(window);
            ReachControl(window, window.ArrowBodySlider);

            // Selecting an existing annotation exercises the inspector populated from a drawing.
            var start = new Point(35, 60);
            var end = new Point(235, 140);
            var arrow = ArrowAnnotation.Create(start);
            arrow.UpdateFromAnchor(start, end, false);
            const BindingFlags access = BindingFlags.Instance | BindingFlags.NonPublic;
            var annotations = (List<AnnotationBase>)typeof(EditorWindow).GetField("_annotations", access)!.GetValue(window)!;
            annotations.Add(arrow);
            window.SelectToolButton.IsChecked = true;
            typeof(EditorWindow).GetMethod("SelectAnnotationAt", access)!.Invoke(window, [new Point(135, 100)]);
            typeof(EditorWindow).GetMethod("RefreshCanvas", access)!.Invoke(window, null);
            Layout(window);
            AssertDockedWorkspace(window, dockOnRight);

            var geometry = window.ArrowGeometryExpander;
            Assert.True(geometry.IsVisible);
            geometry.IsExpanded = true;
            Layout(window);
            ReachControl(window, window.ArrowTailSlider);
            ReachControl(window, window.ArrowTailRoundnessSlider);
            ReachControl(window, window.ArrowBorderSlider);
            AssertDockedWorkspace(window, dockOnRight);

            window.Width = dockOnRight ? 650 : 956;
            Layout(window);
            AssertDockedWorkspace(window, !dockOnRight);
            AssertArtworkFits(window);
            Assert.True(geometry.IsExpanded);
            ReachControl(window, window.ArrowBorderSlider);
        });
    }

    private static void AssertArtworkFits(EditorWindow window)
    {
        Layout(window);
        var viewer = window.ArtworkScrollViewer;
        Assert.Equal(0, viewer.ScrollableWidth);
        Assert.Equal(0, viewer.ScrollableHeight);
        Assert.NotEqual(Visibility.Visible, viewer.ComputedHorizontalScrollBarVisibility);
        Assert.NotEqual(Visibility.Visible, viewer.ComputedVerticalScrollBarVisibility);
    }

    [Theory]
    [InlineData(956, 900, true)]
    [InlineData(650, 540, false)]
    public void ResizeUrlPresetsAndHighlightModesOpenInsideTheInspector(int width, int height, bool dockOnRight)
    {
        WithEditor(width, height, window =>
        {
            OpenInInspector(window.ResizeImageButton, window.ResizePanel);
            ReachControl(window, window.ImageWidthTextBox);
            OpenInInspector(window.AddImageFromUrlButton, window.ImageUrlPanel);
            ReachControl(window, window.ImageUrlTextBox);
            window.ArrowToolButton.IsChecked = true;
            OpenInInspector(window.ArrowPresetButton, window.ArrowPresetsPanel);
            window.HighlightToolButton.IsChecked = true;
            OpenInInspector(window.HighlightToolButton, window.HighlightModesPanel);

            void OpenInInspector(ButtonBase command, FrameworkElement panel)
            {
                Layout(window);
                ReachControl(window, command);
                command.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, command));
                Layout(window);
                Assert.True(panel.IsVisible, $"{panel.Name} should open from {command.Name}.");
                Assert.True(window.InspectorDock.IsAncestorOf(panel));
                AssertDockedWorkspace(window, dockOnRight);
            }
        });
    }

    private static void AssertDockedWorkspace(EditorWindow window, bool dockOnRight)
    {
        var viewport = BoundsInWindow(window, window.ArtworkScrollViewer);
        var dock = BoundsInWindow(window, window.InspectorDock);
        Assert.True(viewport.Width > 0 && viewport.Height > 0 && dock.Width > 0 && dock.Height > 0);
        Assert.True(dockOnRight ? dock.Left >= viewport.Right - 1 : dock.Top >= viewport.Bottom - 1,
            $"The inspector at {dock} overlaps the artwork viewport at {viewport}.");
        foreach (var chrome in new[] { window.FileActionsIsland, window.ZoomActionsIsland, window.ToolbarIsland })
        {
            Assert.True(chrome.IsVisible, $"{chrome.Name} is missing.");
            var intersection = Rect.Intersect(viewport, BoundsInWindow(window, chrome));
            Assert.True(intersection.IsEmpty || intersection.Width <= 1 || intersection.Height <= 1,
                $"{chrome.Name} overlaps the artwork viewport by {intersection}.");
        }
    }

    private static void ReachControl(EditorWindow window, Control control)
    {
        Assert.True(control.IsEnabled && control.IsVisible, $"{control.Name} is unavailable.");
        control.BringIntoView();
        Layout(window);
        var bounds = BoundsInWindow(window, control);
        Assert.True(bounds.Width > 0 && bounds.Height > 0);
        var middle = new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
        var root = (FrameworkElement)window.Content;
        var hit = root.InputHitTest(middle) as DependencyObject;
        Assert.True(hit is not null && (ReferenceEquals(hit, control) || control.IsAncestorOf(hit)),
            $"{control.Name} at {middle} is clipped or covered.");
    }

    private static Rect BoundsInWindow(EditorWindow window, FrameworkElement element) =>
        element.TransformToAncestor((FrameworkElement)window.Content).TransformBounds(new Rect(element.RenderSize));

    private static void Layout(EditorWindow window)
    {
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
    }

    private static void WithEditor(int width, int height, Action<EditorWindow> verify, int imageWidth = 320, int imageHeight = 200)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            EditorWindow? window = null;
            try
            {
                var image = BitmapSource.Create(imageWidth, imageHeight, 96, 96, PixelFormats.Bgra32, null,
                    new byte[imageWidth * imageHeight * 4], imageWidth * 4);
                image.Freeze();
                window = new EditorWindow(new InMemoryEditorPreferences(), image)
                {
                    Opacity = 0,
                    ShowActivated = false,
                    ShowInTaskbar = false,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = -30000,
                    Top = -30000,
                };
                window.Show();
                Layout(window);
                window.Left = window.Top = -30000;
                window.MaxWidth = window.MaxHeight = double.PositiveInfinity;
                window.Width = width;
                window.Height = height;
                Layout(window);
                Assert.InRange(window.ActualWidth, width - 1, width + 1);
                verify(window);
            }
            catch (Exception exception)
            {
                failure = ExceptionDispatchInfo.Capture(exception);
            }
            finally
            {
                window?.Close();
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Editor layout timed out.");
        failure?.Throw();
    }
}
