using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Windows;
using System.Windows.Media.Imaging;

namespace DegrandeScreenShot.App.Services;

internal static class ImageImportService
{
    private const long MaximumDownloadBytes = 50L * 1024 * 1024;
    private const string FileContentsFormat = "FileContents";
    private static readonly string[] EncodedImageFormats =
    [
        FileContentsFormat,
        "PNG",
        "JFIF",
        "image/png",
        "image/jpeg",
        "image/webp",
        "image/avif",
    ];
    private static readonly HttpClient Client = CreateHttpClient();

    internal static BitmapSource LoadFile(string path)
    {
        return ScreenshotLibraryService.LoadImage(path);
    }

    internal static bool HasDroppedImageData(System.Windows.IDataObject data)
    {
        return EncodedImageFormats.Any(format => data.GetDataPresent(format, autoConvert: false))
            || data.GetDataPresent("FileGroupDescriptorW", autoConvert: false);
    }

    internal static bool TryLoadDroppedImageData(System.Windows.IDataObject data, out BitmapSource image)
    {
        foreach (var format in EncodedImageFormats)
        {
            if (!data.GetDataPresent(format, autoConvert: false))
            {
                continue;
            }

            try
            {
                if (TryDecodeDroppedValue(data.GetData(format, autoConvert: false), out image))
                {
                    return true;
                }
            }
            catch (Exception exception) when (exception is IOException
                or NotSupportedException
                or ArgumentException
                or COMException)
            {
                // Try the indexed virtual-file representation next.
            }
        }

        return TryLoadChromiumVirtualFile(data, out image);
    }

    internal static async Task<BitmapSource> LoadUrlAsync(string url, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException("Enter a valid http or https image URL.", nameof(url));
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/png"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/jpeg"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/gif"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/bmp"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/tiff"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*", 0.1));
        using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength is > MaximumDownloadBytes)
        {
            throw new InvalidDataException("The image is larger than the 50 MB download limit.");
        }

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var imageStream = new MemoryStream();
        var buffer = new byte[81920];
        long totalBytes = 0;

        while (true)
        {
            var bytesRead = await responseStream.ReadAsync(buffer, cancellationToken);
            if (bytesRead == 0)
            {
                break;
            }

            totalBytes += bytesRead;
            if (totalBytes > MaximumDownloadBytes)
            {
                throw new InvalidDataException("The image is larger than the 50 MB download limit.");
            }

            await imageStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
        }

        imageStream.Position = 0;
        return DecodeImage(imageStream);
    }

    private static bool TryDecodeDroppedValue(object? value, out BitmapSource image)
    {
        switch (value)
        {
            case byte[] bytes when bytes.LongLength is > 0 and <= MaximumDownloadBytes:
                using (var byteStream = new MemoryStream(bytes, writable: false))
                {
                    image = DecodeImage(byteStream);
                    return true;
                }
            case Stream stream:
                using (var copy = CopyDroppedStream(stream))
                {
                    image = DecodeImage(copy);
                    return true;
                }
            default:
                image = null!;
                return false;
        }
    }

    private static MemoryStream CopyDroppedStream(Stream source)
    {
        if (source.CanSeek)
        {
            source.Position = 0;
        }

        var copy = new MemoryStream();
        var buffer = new byte[81920];
        long totalBytes = 0;

        while (true)
        {
            var bytesRead = source.Read(buffer, 0, buffer.Length);
            if (bytesRead == 0)
            {
                break;
            }

            totalBytes += bytesRead;
            if (totalBytes > MaximumDownloadBytes)
            {
                copy.Dispose();
                throw new InvalidDataException("The dropped image is larger than the 50 MB limit.");
            }

            copy.Write(buffer, 0, bytesRead);
        }

        copy.Position = 0;
        return copy;
    }

    private static bool TryLoadChromiumVirtualFile(System.Windows.IDataObject data, out BitmapSource image)
    {
        image = null!;
        if (data is not System.Runtime.InteropServices.ComTypes.IDataObject comData)
        {
            return false;
        }

        var format = new FORMATETC
        {
            cfFormat = (short)DataFormats.GetDataFormat(FileContentsFormat).Id,
            dwAspect = DVASPECT.DVASPECT_CONTENT,
            lindex = 0,
            ptd = IntPtr.Zero,
            tymed = TYMED.TYMED_HGLOBAL,
        };

        try
        {
            if (comData.QueryGetData(ref format) != 0)
            {
                return false;
            }

            comData.GetData(ref format, out var medium);
            try
            {
                if (medium.tymed != TYMED.TYMED_HGLOBAL || medium.unionmember == IntPtr.Zero)
                {
                    return false;
                }

                var byteCount = GlobalSize(medium.unionmember).ToUInt64();
                if (byteCount == 0 || byteCount > MaximumDownloadBytes || byteCount > int.MaxValue)
                {
                    return false;
                }

                var source = GlobalLock(medium.unionmember);
                if (source == IntPtr.Zero)
                {
                    return false;
                }

                try
                {
                    var bytes = new byte[(int)byteCount];
                    Marshal.Copy(source, bytes, 0, bytes.Length);
                    using var stream = new MemoryStream(bytes, writable: false);
                    image = DecodeImage(stream);
                    return true;
                }
                finally
                {
                    _ = GlobalUnlock(medium.unionmember);
                }
            }
            finally
            {
                ReleaseStgMedium(ref medium);
            }
        }
        catch (Exception exception) when (exception is COMException
            or IOException
            or NotSupportedException
            or ArgumentException)
        {
            return false;
        }
    }

    private static BitmapSource DecodeImage(Stream stream)
    {
        var decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);

        if (decoder.Frames.Count == 0)
        {
            throw new NotSupportedException("The downloaded file does not contain a supported image.");
        }

        var image = decoder.Frames[0];
        if (image.CanFreeze)
        {
            image.Freeze();
        }

        return image;
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DegrandeScreenShot/1.0");
        return client;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern UIntPtr GlobalSize(IntPtr memory);

    [DllImport("ole32.dll")]
    private static extern void ReleaseStgMedium(ref STGMEDIUM medium);
}
