using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace Scr;

/// <summary>A capture that was closed, with everything needed to reopen it as it was.</summary>
public sealed class ClosedCapture
{
    public required FrameStyle Style;
    public required EditState Edits;
    public string? Name;
    public DateTime Taken;
    public int Width, Height;
    public BitmapSource? Thumbnail; // small, for the list
    internal BitmapSource? Picture; // until it's been compressed
    internal byte[]? Png;
}

/// <summary>
/// The last 10 closed captures (closed with ×, Esc, Copy or by closing the window), so one can be reopened.
/// Each picture is compressed to PNG in the background, so Scr stays light while it waits in the tray.
/// Kept only while Scr runs.
/// </summary>
public static class ClosedCaptures
{
    const int Max = 10;
    static readonly List<ClosedCapture> list = new();
    static readonly object gate = new();

    public static event Action? Changed;
    public static int Count { get { lock (gate) return list.Count; } }

    public static void Add(BitmapSource picture, FrameStyle style, EditState edits, string? name, DateTime taken)
    {
        if (!picture.IsFrozen) return;
        var c = new ClosedCapture
        {
            Style = style.Clone(), Edits = edits, Name = name, Taken = taken, Picture = picture,
            Width = picture.PixelWidth, Height = picture.PixelHeight, Thumbnail = Thumb(picture),
        };
        lock (gate)
        {
            list.Add(c);
            while (list.Count > Max) list.RemoveAt(0);
        }
        Changed?.Invoke();
        Task.Run(() =>
        {
            try
            {
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(picture));
                using var ms = new MemoryStream();
                enc.Save(ms);
                lock (gate) { c.Png = ms.ToArray(); c.Picture = null; }
            }
            catch { /* keep the picture as it is */ }
        });
    }

    static BitmapSource? Thumb(BitmapSource picture)
    {
        try
        {
            double k = Math.Min(1.0, Math.Min(72.0 / picture.PixelWidth, 44.0 / picture.PixelHeight));
            var t = new TransformedBitmap(picture, new System.Windows.Media.ScaleTransform(k, k));
            var copy = new WriteableBitmap(t);
            copy.Freeze();
            return copy;
        }
        catch { return null; }
    }

    /// <summary>The closed captures, newest first.</summary>
    public static List<ClosedCapture> Newest()
    {
        lock (gate)
        {
            var copy = new List<ClosedCapture>(list);
            copy.Reverse();
            return copy;
        }
    }

    /// <summary>Takes a particular capture off the list (when it's reopened). False if it's no longer there.</summary>
    public static bool Take(ClosedCapture c)
    {
        bool removed;
        lock (gate) removed = list.Remove(c);
        if (removed) Changed?.Invoke();
        return removed;
    }

    /// <summary>The most recently closed capture, taken off the list. Null if there's none.</summary>
    public static ClosedCapture? Pop()
    {
        ClosedCapture? c;
        lock (gate)
        {
            if (list.Count == 0) return null;
            c = list[^1];
            list.RemoveAt(list.Count - 1);
        }
        Changed?.Invoke();
        return c;
    }

    public static BitmapSource PictureOf(ClosedCapture c)
    {
        lock (gate)
        {
            if (c.Picture != null) return c.Picture;
            using var ms = new MemoryStream(c.Png!);
            var dec = new PngBitmapDecoder(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var bmp = dec.Frames[0];
            bmp.Freeze();
            return bmp;
        }
    }
}
