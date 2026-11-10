using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Scr;

/// <summary>Pictures from files, drag and drop, or the clipboard, ready to frame like a capture.</summary>
public static class Import
{
    public const string Filter = "Pictures|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif;*.tif;*.tiff;*.heic;*.jxr|All files|*.*";

    /// <summary>Opens a picture file, turned the right way up (phone photos often store their rotation separately).</summary>
    public static BitmapSource LoadFile(string path)
    {
        BitmapFrame frame;
        using (var fs = File.OpenRead(path))
            frame = BitmapFrame.Create(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);

        int orientation = 1;
        try
        {
            if (frame.Metadata is BitmapMetadata md && md.GetQuery("System.Photo.Orientation") is ushort o) orientation = o;
        }
        catch { /* no metadata, or a format without it */ }

        double angle = orientation switch { 3 or 4 => 180, 6 or 5 => 90, 8 or 7 => 270, _ => 0 };
        BitmapSource bmp = frame;
        if (angle != 0) bmp = new TransformedBitmap(frame, new RotateTransform(angle));
        return Normalize(bmp);
    }

    /// <summary>The pictures in a drop or on the clipboard: picture files first, then a copied picture.</summary>
    public static List<(BitmapSource Picture, string? Name)> Read(IDataObject? data)
    {
        var list = new List<(BitmapSource, string?)>();
        if (data == null) return list;
        try
        {
            if (data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] files)
            {
                foreach (var f in files)
                {
                    try { list.Add((LoadFile(f), Path.GetFileNameWithoutExtension(f))); }
                    catch { /* not a picture */ }
                }
                if (list.Count > 0) return list;
            }
            // PNG keeps see-through parts; apps like browsers and Office put it on the clipboard.
            if (data.GetDataPresent("PNG") && data.GetData("PNG") is MemoryStream png)
            {
                png.Position = 0;
                var dec = new PngBitmapDecoder(png, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                list.Add((Normalize(dec.Frames[0]), null));
                return list;
            }
            if (data.GetDataPresent(DataFormats.Bitmap) && data.GetData(DataFormats.Bitmap) is BitmapSource bmp)
            {
                list.Add((Normalize(bmp, opaque: true), null));
                return list;
            }
        }
        catch { /* the clipboard was busy or held something odd */ }
        return list;
    }

    public static bool HasPicture(IDataObject? data) => data != null &&
        (data.GetDataPresent(DataFormats.FileDrop) || data.GetDataPresent("PNG") || data.GetDataPresent(DataFormats.Bitmap));

    public static List<(BitmapSource Picture, string? Name)> FromClipboard()
    {
        try { return Read(Clipboard.GetDataObject()); }
        catch { return new(); }
    }

    /// <summary>
    /// A plain 96 dpi copy with premultiplied alpha, so every picture behaves like a capture.
    /// Clipboard bitmaps from older apps have no real alpha, so those are made opaque.
    /// </summary>
    static BitmapSource Normalize(BitmapSource src, bool opaque = false)
    {
        BitmapSource conv = new FormatConvertedBitmap(src, opaque ? PixelFormats.Bgr32 : PixelFormats.Pbgra32, null, 0);
        if (opaque) conv = new FormatConvertedBitmap(conv, PixelFormats.Pbgra32, null, 0);
        int w = conv.PixelWidth, h = conv.PixelHeight, stride = w * 4;
        var px = new byte[stride * h];
        conv.CopyPixels(px, stride, 0);
        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, px, stride);
        bmp.Freeze();
        return bmp;
    }
}
