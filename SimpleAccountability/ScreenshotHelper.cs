using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace SimpleAccountability
{
    public static class ScreenshotHelper
    {
        public static byte[] CaptureScreenToBytes()
        {
            var screen = Screen.PrimaryScreen
                         ?? throw new InvalidOperationException("No screen detected.");
            var bounds = screen.Bounds;

            using var original = new Bitmap(bounds.Width, bounds.Height);
            using (var g = Graphics.FromImage(original))
                g.CopyFromScreen(Point.Empty, Point.Empty, bounds.Size);

            // Resize to 50% of both dimensions
            int newW = bounds.Width / 2;
            int newH = bounds.Height / 2;
            using var resized = new Bitmap(original, new Size(newW, newH));

            using var ms = new MemoryStream();
            resized.Save(ms, ImageFormat.Jpeg);  // JPEG to shrink size
            return ms.ToArray();
        }
    }
}
