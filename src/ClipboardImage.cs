using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Scr;

public static class ClipboardImage
{
    /// <summary>
    /// Puts the picture on the clipboard twice: as PNG (keeps see-through corners, used by browsers, Discord, Office)
    /// and as a normal bitmap on white for older apps like Paint.
    /// </summary>
    public static void Set(BitmapSource bmp)
    {
        var data = new DataObject();
        var png = new MemoryStream();
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        enc.Save(png);
        png.Position = 0;
        data.SetData("PNG", png, false);
        data.SetImage(OnWhite(bmp));

        for (int i = 0; ; i++)
        {
            try
            {
                Clipboard.SetDataObject(data, true);
                return;
            }
            catch (COMException) when (i < 6)
            {
                Thread.Sleep(40); // another app has the clipboard open for a moment
            }
        }
    }

    static BitmapSource OnWhite(BitmapSource bmp)
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            var r = new Rect(0, 0, bmp.PixelWidth, bmp.PixelHeight);
            dc.DrawRectangle(Brushes.White, null, r);
            dc.DrawImage(bmp, r);
        }
        var rtb = new RenderTargetBitmap(bmp.PixelWidth, bmp.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        var opaque = new FormatConvertedBitmap(rtb, PixelFormats.Bgr32, null, 0);
        opaque.Freeze();
        return opaque;
    }
}
