using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DegrandeScreenShot.App;

namespace DegrandeScreenShot.Tests;

[Collection("Editor windows")]
public class EditorRenderingTests
{
    [Fact]
    public void MovingALowerLayerPreservesTheHighlightAndExportsTheSamePixelsAsAFreshRender()
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                VerifyLayerEdit();
            }
            catch (Exception exception)
            {
                failure = ExceptionDispatchInfo.Capture(exception);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "Editor rendering timed out.");
        failure?.Throw();
    }

    private static void VerifyLayerEdit()
    {
        const int width = 180;
        const int height = 120;
        var pixels = new byte[width * height * 4];
        new Random(42).NextBytes(pixels);
        for (int i = 3; i < pixels.Length; i += 4)
        {
            pixels[i] = 255;
        }
        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        image.Freeze();

        var frame = new RectangleAnnotation { Bounds = new Rect(5, 10, 90, 76) };
        frame.SetBackgroundOpacity(0.8);
        var highlight = HighlightAnnotation.CreateRegion(new Point(48, 18));
        highlight.UpdateFromAnchor(new Point(48, 18), new Point(126, 90), false);

        var edited = new EditorWindow(new InMemoryEditorPreferences(), image);
        var fresh = new EditorWindow(new InMemoryEditorPreferences(), image);
        try
        {
            AddLayers(edited, frame, highlight);
            var selection = typeof(EditorWindow).GetField("_selectedAnnotation", PrivateInstance)!;
            selection.SetValue(edited, frame);
            Invoke(edited, "RefreshCanvas");
            var originalHighlight = Assert.Single(edited.AnnotationCanvas.Children.OfType<Image>());
            var before = ExportPixels(edited);

            selection.SetValue(edited, frame);
            frame.Move(new Vector(37, 23));
            Invoke(edited, "RefreshCanvas");
            Assert.Same(originalHighlight, Assert.Single(edited.AnnotationCanvas.Children.OfType<Image>()));
            var after = ExportPixels(edited);

            AddLayers(fresh, frame.Clone(), highlight.Clone());
            Assert.False(before.SequenceEqual(after));
            Assert.Equal(ExportPixels(fresh), after);
        }
        finally
        {
            edited.Close();
            fresh.Close();
        }
    }

    private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

    private static void AddLayers(EditorWindow window, params AnnotationBase[] layers)
    {
        var annotations = (List<AnnotationBase>)typeof(EditorWindow).GetField("_annotations", PrivateInstance)!.GetValue(window)!;
        annotations.AddRange(layers);
    }

    private static byte[] ExportPixels(EditorWindow window)
    {
        Invoke(window, "ClearSelectionBeforeExport");
        var surface = window.ArtworkSurface;
        surface.Measure(new Size(surface.Width, surface.Height));
        surface.Arrange(new Rect(0, 0, surface.Width, surface.Height));
        var result = (BitmapSource)Invoke(window, "RenderComposite")!;
        var stride = result.PixelWidth * 4;
        var pixels = new byte[stride * result.PixelHeight];
        result.CopyPixels(pixels, stride, 0);
        return pixels;
    }

    private static object? Invoke(EditorWindow window, string method) =>
        typeof(EditorWindow).GetMethod(method, PrivateInstance)!.Invoke(window, null);
}
