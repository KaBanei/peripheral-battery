using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
class ScreenCap {
  static void Main(string[] a) {
    string outPath = a.Length > 0 ? a[0] : "screen.png";
    Rectangle b = SystemInformation.VirtualScreen;
    using (var full = new Bitmap(b.Width, b.Height)) {
      using (var g = Graphics.FromImage(full)) g.CopyFromScreen(b.Left, b.Top, 0, 0, full.Size);
      if (a.Length >= 5) {
        var re = new Rectangle(int.Parse(a[1]), int.Parse(a[2]), int.Parse(a[3]), int.Parse(a[4]));
        using (var crop = new Bitmap(re.Width, re.Height)) {
          using (var g2 = Graphics.FromImage(crop)) g2.DrawImage(full, 0, 0, re, GraphicsUnit.Pixel);
          crop.Save(outPath, ImageFormat.Png);
        }
      } else if (a.Length >= 2) {
        int h = int.Parse(a[1]);
        var re = new Rectangle(0, full.Height - h, full.Width, h);
        using (var crop = new Bitmap(re.Width, re.Height)) {
          using (var g2 = Graphics.FromImage(crop)) g2.DrawImage(full, 0, 0, re, GraphicsUnit.Pixel);
          crop.Save(outPath, ImageFormat.Png);
        }
      } else full.Save(outPath, ImageFormat.Png);
    }
    Console.WriteLine("saved " + outPath);
  }
}
