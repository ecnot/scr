using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Scr;

public enum RedactKind { Blur, Pixelate }

/// <summary>A hidden part of a capture, in the capture's own pixels.</summary>
public readonly record struct Redaction(Int32Rect Area, RedactKind Kind);

public enum MarkKind { Box, Marker, Spotlight }

/// <summary>All the edits on one capture at one moment: what's hidden, what's marked, and the crop.</summary>
public sealed record EditState(Redaction[] Redactions, Mark[] Marks, Int32Rect? Crop);

/// <summary>A highlight box, a marker stroke or a spotlight, in the capture's own pixels.</summary>
public readonly record struct Mark(MarkKind Kind, Int32Rect Area, Color Color);

/// <summary>Hiding parts of a screenshot, and finding a screenshot's main colours for the automatic backdrop.</summary>
public static class Redact
{
    /// <summary>A copy of the capture with every redaction baked into the pixels (so nothing can be recovered from Copy or Save).</summary>
    public static BitmapSource Apply(BitmapSource capture, IReadOnlyList<Redaction> list)
    {
        // Premultiplied alpha, so pictures with see-through parts keep them.
        var conv = new FormatConvertedBitmap(capture, PixelFormats.Pbgra32, null, 0);
        int w = conv.PixelWidth, h = conv.PixelHeight, stride = w * 4;
        var px = new byte[stride * h];
        conv.CopyPixels(px, stride, 0);
        int big = Math.Max(w, h);

        foreach (var r in list)
        {
            var a = ScreenCapture.Intersect(r.Area, new Int32Rect(0, 0, w, h));
            if (a.Width < 1 || a.Height < 1) continue;
            if (r.Kind == RedactKind.Pixelate) Pixelate(px, stride, a, Math.Max(8, (int)Math.Round(big / 100.0)));
            else BlurArea(px, stride, a, Math.Max(9, big / 85.0));
        }

        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, px, stride);
        bmp.Freeze();
        return bmp;
    }

    static void Pixelate(byte[] px, int stride, Int32Rect a, int block)
    {
        for (int by = a.Y; by < a.Y + a.Height; by += block)
        for (int bx = a.X; bx < a.X + a.Width; bx += block)
        {
            int x1 = Math.Min(bx + block, a.X + a.Width), y1 = Math.Min(by + block, a.Y + a.Height);
            long b = 0, g = 0, rr = 0, al = 0, n = 0;
            for (int y = by; y < y1; y++)
            for (int x = bx; x < x1; x++)
            {
                int i = y * stride + x * 4;
                b += px[i]; g += px[i + 1]; rr += px[i + 2]; al += px[i + 3]; n++;
            }
            if (n == 0) continue;
            byte cb = (byte)(b / n), cg = (byte)(g / n), cr = (byte)(rr / n), ca = (byte)(al / n);
            for (int y = by; y < y1; y++)
            for (int x = bx; x < x1; x++)
            {
                int i = y * stride + x * 4;
                px[i] = cb; px[i + 1] = cg; px[i + 2] = cr; px[i + 3] = ca;
            }
        }
    }

    static void BlurArea(byte[] px, int stride, Int32Rect a, double sigma)
    {
        int w = a.Width, h = a.Height;
        // Never blur less than the area allows; a tiny area just becomes its average colour.
        sigma = Math.Min(sigma, Math.Max(w, h) / 2.0);
        for (int c = 0; c < 4; c++)
        {
            var ch = new float[w * h];
            double sum = 0;
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float v = px[(a.Y + y) * stride + (a.X + x) * 4 + c];
                ch[y * w + x] = v;
                sum += v;
            }
            // Twice, so text is gone and not just soft.
            Pixels.Blur(ch, w, h, sigma);
            Pixels.Blur(ch, w, h, sigma);
            float avg = (float)(sum / ch.Length);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float v = ch[y * w + x] * 0.75f + avg * 0.25f;
                px[(a.Y + y) * stride + (a.X + x) * 4 + c] = (byte)Math.Clamp(v + 0.5f, 0, 255);
            }
        }
    }

    /// <summary>
    /// Three gradient colours that suit a screenshot: its most colourful hues (or a soft tint of its overall colour if it's mostly grey),
    /// made pale for light screenshots and deeper for dark ones.
    /// </summary>
    public static Color[] AutoColors(BitmapSource capture)
    {
        double s = Math.Min(1.0, 64.0 / Math.Max(capture.PixelWidth, capture.PixelHeight));
        BitmapSource small = s < 1 ? new TransformedBitmap(capture, new ScaleTransform(s, s)) : capture;
        var conv = new FormatConvertedBitmap(small, PixelFormats.Bgr32, null, 0);
        int w = conv.PixelWidth, h = conv.PixelHeight;
        var px = new byte[w * h * 4];
        conv.CopyPixels(px, w * 4, 0);

        var hueWeight = new double[36];
        var hueSat = new double[36];
        double lumSum = 0, rSum = 0, gSum = 0, bSum = 0;
        int n = w * h;
        for (int i = 0; i < n; i++)
        {
            var c = Color.FromRgb(px[i * 4 + 2], px[i * 4 + 1], px[i * 4]);
            var (hh, ss, ll) = ThemeColors.ToHsl(c);
            lumSum += ll; rSum += c.R; gSum += c.G; bSum += c.B;
            // Colourful mid-tones count most; greys, black and white hardly at all.
            double wgt = ss * Math.Max(0, 1 - Math.Abs(ll - 0.5) * 1.6);
            int bin = (int)(hh * 36) % 36;
            hueWeight[bin] += wgt;
            hueSat[bin] += wgt * ss;
        }
        bool dark = lumSum / n < 0.4;
        double total = hueWeight.Sum();

        // Smooth the hue histogram a little and pick up to three peaks at least 40° apart.
        var smooth = new double[36];
        for (int i = 0; i < 36; i++) smooth[i] = hueWeight[(i + 35) % 36] * 0.5 + hueWeight[i] + hueWeight[(i + 1) % 36] * 0.5;
        var picks = new List<(double Hue, double Sat)>();
        if (total > n * 0.004)
        {
            foreach (int i in Enumerable.Range(0, 36).OrderByDescending(i => smooth[i]))
            {
                if (smooth[i] < smooth.Max() * 0.12 || picks.Count == 3) break;
                double hue = (i + 0.5) / 36;
                if (picks.Any(p => HueDistance(p.Hue, hue) < 40.0 / 360)) continue;
                double sat = hueWeight[i] > 0 ? hueSat[i] / hueWeight[i] : 0.5;
                picks.Add((hue, sat));
            }
        }
        if (picks.Count == 0)
        {
            // A grey screenshot: a quiet tint of its overall colour, leaning to lavender if there's none.
            var avg = Color.FromRgb((byte)(rSum / n), (byte)(gSum / n), (byte)(bSum / n));
            var (ah, asat, _) = ThemeColors.ToHsl(avg);
            double hue = asat < 0.04 ? 0.66 : ah;
            picks.Add((hue, 0.3));
        }
        // Fill up to three colours with neighbours of what we found.
        while (picks.Count < 3)
        {
            var baseHue = picks[0].Hue;
            double step = picks.Count == 1 ? 0.07 : -0.07;
            picks.Add(((baseHue + step + 1) % 1, picks[0].Sat));
        }

        var result = new Color[3];
        for (int i = 0; i < 3; i++)
        {
            double sat = dark ? Math.Clamp(picks[i].Sat, 0.45, 0.75) : Math.Clamp(picks[i].Sat, 0.35, 0.7);
            double light = dark ? 0.26 + 0.08 * i : 0.86 - 0.04 * i;
            result[i] = ThemeColors.FromHsl(picks[i].Hue, sat, light);
        }
        return result;
    }

    static double HueDistance(double a, double b)
    {
        double d = Math.Abs(a - b);
        return Math.Min(d, 1 - d);
    }
}
