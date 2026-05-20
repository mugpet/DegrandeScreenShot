using System;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace DegrandeScreenShot.App.Services;

public static class ClipboardHelper
{
    public static void SetImage(BitmapSource bitmap)
    {
        if (bitmap == null)
        {
            throw new ArgumentNullException(nameof(bitmap));
        }

        var dataObject = new DataObject();
        dataObject.SetImage(bitmap);

        try
        {
            // Do not use a using block for MemoryStream here so that the stream 
            // is guaranteed to stay alive for the Clipboard operation, letting the GC 
            // clean it up naturally.
            var stream = new MemoryStream();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            encoder.Save(stream);
            dataObject.SetData("PNG", stream, autoConvert: false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to write PNG format to clipboard: {ex.Message}");
        }

        Clipboard.SetDataObject(dataObject, copy: true);
    }
}
