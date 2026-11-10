using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Scr;

/// <summary>Small pixel jobs: soft shadows, blurred backdrops and the colour along a capture's edge.</summary>
public static class Pixels
{
    /// <summary>A soft shadow under a rounded card, drawn small and blurred (it's scaled up when drawn, which is invisible on something this soft).</summary>
    public static BitmapSource ShadowMask(int w, int h, Rect card, double radius,
        double offsetY, double sigma, double opacity,
        double offsetY2, double sigma2, double opacity2, out double scale)
    {
        // Small enough to stay quick while dragging a slider (the hairline drawn on top keeps the edge crisp).
        double k = Math.Min(1.0, 420.0 / Math.Max(w, h));
        int mw = Math.Max(1, (int)Math.Ceiling(w * k)), mh = Math.Max(1, (int)Math.Ceiling(h * k));

        var a = RoundRect(mw, mh, card.X * k, (card.Y + offsetY) * k, card.Width * k, card.Height * k, radius * k);
        var b = RoundRect(mw, mh, card.X * k, (card.Y + offsetY2) * k, card.Width * k, card.Height * k, radius * k);
        Blur(a, mw, mh, sigma * k);
        Blur(b, mw, mh, sigma2 * k);

        var px = new byte[mw * mh * 4];
        for (int i = 0; i < a.Length; i++)
        {
            double v = Math.Min(1, a[i] * opacity + b[i] * opacity2);
            byte al = (byte)(v * 255 + 0.5);
            // Premultiplied, a hint of blue-violet so it isn't a dead grey.
            px[i * 4] = (byte)(al * 40 / 255);
            px[i * 4 + 1] = (byte)(al * 16 / 255);
            px[i * 4 + 2] = (byte)(al * 18 / 255);
            px[i * 4 + 3] = al;
        }
        var bmp = BitmapSource.Create(mw, mh, 96, 96, PixelFormats.Pbgra32, null, px, mw * 4);
        bmp.Freeze();
        scale = k;
        return bmp;
    }

    static float[] RoundRect(int w, int h, double x, double y, double rw, double rh, double r)
    {
        var d = new float[w * h];
        double hw = rw / 2, hh = rh / 2, cx = x + hw, cy = y + hh;
        r = Math.Min(r, Math.Min(hw, hh));
        for (int py = 0; py < h; py++)
        {
            double qy = Math.Abs(py + 0.5 - cy) - hh + r;
            for (int px = 0; px < w; px++)
            {
                double qx = Math.Abs(px + 0.5 - cx) - hw + r;
                double ox = Math.Max(qx, 0), oy = Math.Max(qy, 0);
                double dist = Math.Sqrt(ox * ox + oy * oy) + Math.Min(Math.Max(qx, qy), 0) - r;
                d[py * w + px] = (float)Math.Clamp(0.5 - dist, 0, 1);
            }
        }
        return d;
    }

    /// <summary>Gaussian-like blur from three box blurs.</summary>
    public static void Blur(float[] d, int w, int h, double sigma)
    {
        if (sigma < 0.4) return;
        var tmp = new float[d.Length];
        foreach (int size in BoxesForGauss(sigma, 3))
        {
            int r = (size - 1) / 2;
            if (r < 1) continue;
            BoxH(d, tmp, w, h, r);
            BoxV(tmp, d, w, h, r);
        }
    }

    static int[] BoxesForGauss(double sigma, int n)
    {
        double wIdeal = Math.Sqrt(12 * sigma * sigma / n + 1);
        int wl = (int)Math.Floor(wIdeal);
        if (wl % 2 == 0) wl--;
        int wu = wl + 2;
        double mIdeal = (12 * sigma * sigma - n * wl * wl - 4 * n * wl - 3 * n) / (-4.0 * wl - 4);
        int m = (int)Math.Round(mIdeal);
        var sizes = new int[n];
        for (int i = 0; i < n; i++) sizes[i] = i < m ? wl : wu;
        return sizes;
    }

    static void BoxH(float[] src, float[] dst, int w, int h, int r)
    {
        float inv = 1f / (r + r + 1);
        for (int y = 0; y < h; y++)
        {
            int row = y * w;
            float acc = 0;
            for (int j = -r; j <= r; j++) acc += src[row + Math.Clamp(j, 0, w - 1)];
            for (int x = 0; x < w; x++)
            {
                dst[row + x] = acc * inv;
                acc += src[row + Math.Min(x + r + 1, w - 1)] - src[row + Math.Max(x - r, 0)];
            }
        }
    }

    static void BoxV(float[] src, float[] dst, int w, int h, int r)
    {
        float inv = 1f / (r + r + 1);
        for (int x = 0; x < w; x++)
        {
            float acc = 0;
            for (int j = -r; j <= r; j++) acc += src[Math.Clamp(j, 0, h - 1) * w + x];
            for (int y = 0; y < h; y++)
            {
                dst[y * w + x] = acc * inv;
                acc += src[Math.Min(y + r + 1, h - 1) * w + x] - src[Math.Max(y - r, 0) * w + x];
            }
        }
    }

    /// <summary>A small, heavily blurred copy of a picture, for backdrops.</summary>
    public static BitmapSource Blur(BitmapSource src, int maxDim, double sigma)
    {
        double s = Math.Min(1.0, (double)maxDim / Math.Max(src.PixelWidth, src.PixelHeight));
        BitmapSource small = s < 1 ? new TransformedBitmap(src, new ScaleTransform(s, s)) : src;
        var conv = new FormatConvertedBitmap(small, PixelFormats.Pbgra32, null, 0);
        int w = conv.PixelWidth, h = conv.PixelHeight;
        var px = new byte[w * h * 4];
        conv.CopyPixels(px, w * 4, 0);

        var ch = new float[4][];
        for (int c = 0; c < 4; c++)
        {
            ch[c] = new float[w * h];
            for (int i = 0; i < w * h; i++) ch[c][i] = px[i * 4 + c];
            Blur(ch[c], w, h, sigma);
        }
        for (int i = 0; i < w * h; i++)
            for (int c = 0; c < 4; c++)
                px[i * 4 + c] = (byte)Math.Clamp(ch[c][i] + 0.5f, 0, 255);

        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, px, w * 4);
        bmp.Freeze();
        return bmp;
    }

    public static BitmapSource? LoadPicture(string path, bool blur)
    {
        try
        {
            var img = new BitmapImage();
            using (var fs = File.OpenRead(path))
            {
                img.BeginInit();
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.StreamSource = fs;
                img.EndInit();
            }
            img.Freeze();
            if (blur) return Blur(img, 320, 8);
            BitmapSource result = img;
            double s = Math.Min(1.0, 2560.0 / Math.Max(img.PixelWidth, img.PixelHeight));
            if (s < 1)
            {
                var t = new TransformedBitmap(img, new ScaleTransform(s, s));
                t.Freeze();
                result = t;
            }
            return result;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>The average colour along the outer edge, used to fill the inset so it blends with the capture.</summary>
    public static Color EdgeColor(BitmapSource src)
    {
        var conv = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
        int w = conv.PixelWidth, h = conv.PixelHeight;
        long b = 0, g = 0, r = 0, n = 0;
        void Add(Int32Rect rect)
        {
            if (rect.Width <= 0 || rect.Height <= 0) return;
            var px = new byte[rect.Width * rect.Height * 4];
            conv.CopyPixels(rect, px, rect.Width * 4, 0);
            for (int i = 0; i < px.Length; i += 4) { b += px[i]; g += px[i + 1]; r += px[i + 2]; n++; }
        }
        Add(new Int32Rect(0, 0, w, 1));
        Add(new Int32Rect(0, h - 1, w, 1));
        Add(new Int32Rect(0, 0, 1, h));
        Add(new Int32Rect(w - 1, 0, 1, h));
        if (n == 0) return Colors.White;
        return Color.FromRgb((byte)(r / n), (byte)(g / n), (byte)(b / n));
    }
}
