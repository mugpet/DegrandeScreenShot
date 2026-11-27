using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DegrandeScreenShot.App.Services;

public static class ClipboardHelper
{
    private static readonly SemaphoreSlim ClipboardQueue = new(1, 1);
    private static readonly Lazy<Task<Dispatcher>> ClipboardDispatcher = new(StartClipboardThread);

    internal static ArraySegment<byte> EncodePng(BitmapSource bitmap)
    {
        using var stream = new MemoryStream();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(stream);
        return new ArraySegment<byte>(stream.GetBuffer(), 0, checked((int)stream.Length));
    }

    public static void SetImage(BitmapSource bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        Publish(bitmap, TryEncodePng(bitmap));
    }

    public static Task SetImageAsync(BitmapSource bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        var snapshot = bitmap.IsFrozen ? bitmap : (BitmapSource)bitmap.GetAsFrozen();
        return SetImageAsync(snapshot, Task.Run(() => EncodePng(snapshot)));
    }

    internal static async Task SetImageAsync(BitmapSource snapshot, Task<ArraySegment<byte>> encodedPng)
    {
        // Preserve the order of copy requests even if a smaller, later image encodes first.
        await ClipboardQueue.WaitAsync().ConfigureAwait(false);
        try
        {
            ArraySegment<byte> png = default;
            try
            {
                png = await encodedPng.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Clipboard PNG unavailable: {ex.Message}");
            }

            var dispatcher = await ClipboardDispatcher.Value.ConfigureAwait(false);
            await dispatcher.InvokeAsync(() => Publish(snapshot, png)).Task.ConfigureAwait(false);
        }
        finally
        {
            ClipboardQueue.Release();
        }
    }

    internal static async Task<BitmapSource> ReadImageAsync(Func<BitmapSource> read)
    {
        await ClipboardQueue.WaitAsync().ConfigureAwait(false);
        try
        {
            var dispatcher = await ClipboardDispatcher.Value.ConfigureAwait(false);
            return await dispatcher.InvokeAsync(read).Task.ConfigureAwait(false);
        }
        finally
        {
            ClipboardQueue.Release();
        }
    }

    private static Task<Dispatcher> StartClipboardThread()
    {
        // OLE requires STA and can wait for another app. Keep its message pump
        // independent of the UI for both clipboard reads and persistent writes.
        var ready = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            ready.SetResult(Dispatcher.CurrentDispatcher);
            Dispatcher.Run();
        }) { IsBackground = true, Name = "Screenshot clipboard" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return ready.Task;
    }

    private static ArraySegment<byte> TryEncodePng(BitmapSource bitmap)
    {
        try
        {
            return EncodePng(bitmap);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Clipboard PNG unavailable: {ex.Message}");
            return default;
        }
    }

    private static void Publish(BitmapSource bitmap, ArraySegment<byte> png)
    {
        Clipboard.SetDataObject(CreateImageData(bitmap, png), copy: true);
    }

    internal static DataObject CreateImageData(BitmapSource bitmap, ArraySegment<byte> png)
    {
        var data = new DataObject();
        data.SetImage(bitmap);

        if (png.Array is not null)
        {
            data.SetData("PNG", new MemoryStream(png.Array, png.Offset, png.Count, writable: false), autoConvert: false);
        }

        return data;
    }
}
