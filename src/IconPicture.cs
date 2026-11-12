using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Scr;

/// <summary>Turns a capture into a small square icon (its middle, scaled down) for a Windows notification.</summary>
public static class IconPicture
{
    public static IntPtr From(BitmapSource src, int size)
    {
        try
        {
            int side = Math.Min(src.PixelWidth, src.PixelHeight);
            var square = new CroppedBitmap(src, new Int32Rect((src.PixelWidth - side) / 2, (src.PixelHeight - side) / 2, side, side));
            double k = (double)size / side;
            var scaled = new TransformedBitmap(square, new ScaleTransform(k, k));
            var conv = new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);
            int w = conv.PixelWidth, h = conv.PixelHeight, stride = w * 4;
            var px = new byte[stride * h];
            conv.CopyPixels(px, stride, 0);
            for (int i = 3; i < px.Length; i += 4) px[i] = 255; // screenshots are opaque

            IntPtr color = CreateBitmap(w, h, 1, 32, px);
            IntPtr mask = CreateBitmap(w, h, 1, 1, new byte[((w + 15) / 16) * 2 * h]);
            var info = new ICONINFO { fIcon = true, hbmColor = color, hbmMask = mask };
            IntPtr icon = CreateIconIndirect(ref info);
            DeleteObject(color);
            DeleteObject(mask);
            return icon;
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct ICONINFO { public bool fIcon; public int xHotspot, yHotspot; public IntPtr hbmMask, hbmColor; }

    [DllImport("gdi32.dll")] static extern IntPtr CreateBitmap(int w, int h, uint planes, uint bpp, byte[] bits);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr o);
    [DllImport("user32.dll")] static extern IntPtr CreateIconIndirect(ref ICONINFO info);
}
